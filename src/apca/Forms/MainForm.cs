using NAudio.Wave;
using NAudio.CoreAudioApi;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace apca.Forms
{
    public class DeviceItem
    {
        public MMDevice Device { get; }
        public DeviceItem(MMDevice device)
        {
            Device = device;
        }
        public override string ToString()
        {
            return Device.FriendlyName;
        }
    }

    public class WaveInDeviceItem
    {
        public int DeviceNumber { get; }
        public string DeviceName { get; }

        public WaveInDeviceItem(int deviceNumber, string deviceName)
        {
            DeviceNumber = deviceNumber;
            DeviceName = deviceName;
        }

        public override string ToString()
        {
            return DeviceName;
        }
    }

    public class DeviceNotificationClient : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
    {
        private readonly MainForm mainForm;

        public DeviceNotificationClient(MainForm form)
        {
            mainForm = form;
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            mainForm.BeginInvoke(new Action(mainForm.PopulateDeviceLists));
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
            mainForm.BeginInvoke(new Action(mainForm.PopulateDeviceLists));
        }

        public void OnDeviceRemoved(string deviceId)
        {
            mainForm.BeginInvoke(new Action(mainForm.PopulateDeviceLists));
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            mainForm.BeginInvoke(new Action(mainForm.PopulateDeviceLists));
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
            // Not needed for this implementation
        }
    }

    public partial class MainForm : Form
    {
        private WasapiLoopbackCapture? outputCapture;
        private WaveInEvent? micCapture;
        private WaveFileWriter? writer;
        private MMDeviceEnumerator? deviceEnumerator;
        private ComboBox outputDeviceCombo = null!;
        private ComboBox inputDeviceCombo = null!;
        private string? outputFilePath;
        private bool isRecording;
        private Label outputLevelLabel = null!;
        private Label micLevelLabel = null!;
        private readonly object writerLock = new object();

        public bool IsRecording => isRecording;

        private int GetAudioLevel(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0)
                return 0; // Return 0 for null or empty buffer

            if (buffer.Length % 2 != 0)
                return 0; // Return 0 for odd-length buffer

            long sum = 0;
            for (int i = 0; i < buffer.Length; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                sum += Math.Abs(sample);
            }
            return (int)(sum / (buffer.Length / 2));
        }

        public MainForm()
        {
            InitializeComponent();
            InitializeDeviceEnumerator();
            PopulateDeviceLists();
        }

        private void InitializeComponent()
        {
            this.Text = "Dual Channel Audio Capture";
            this.Width = 400;
            this.Height = 350;

            var outputLabel = new Label
            {
                Text = "Output Device:",
                Location = new Point(20, 20),
                Width = 100
            };

            outputDeviceCombo = new ComboBox
            {
                Location = new Point(20, 40),
                Width = 350,
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            var inputLabel = new Label
            {
                Text = "Input Device:",
                Location = new Point(20, 80),
                Width = 100
            };

            inputDeviceCombo = new ComboBox
            {
                Location = new Point(20, 100),
                Width = 350,
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            var startButton = new Button
            {
                Text = "Start Recording",
                Location = new Point(140, 150),
                Width = 120
            };
            startButton.Click += StartButton_Click;

            outputLevelLabel = new Label
            {
                Text = "Output Level: 0",
                Location = new Point(20, 180),
                Width = 200
            };

            micLevelLabel = new Label
            {
                Text = "Mic Level: 0",
                Location = new Point(20, 200),
                Width = 200
            };

            Controls.AddRange(new Control[] { 
                outputLabel, outputDeviceCombo, 
                inputLabel, inputDeviceCombo,
                startButton, outputLevelLabel, micLevelLabel 
            });
        }

        private void InitializeDeviceEnumerator()
        {
            deviceEnumerator = new MMDeviceEnumerator();
            deviceEnumerator.RegisterEndpointNotificationCallback(new DeviceNotificationClient(this));
        }

        public void PopulateDeviceLists()
        {
            if (deviceEnumerator == null) return;

            // Populate output devices
            outputDeviceCombo.Items.Clear();
            var outputDevices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in outputDevices)
            {
                outputDeviceCombo.Items.Add(new DeviceItem(device));
            }
            
            // Select default output device
            var defaultOutput = outputDevices.FirstOrDefault(d => d.ID == deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID);
            if (defaultOutput != null)
            {
                outputDeviceCombo.SelectedItem = outputDeviceCombo.Items.Cast<DeviceItem>()
                    .FirstOrDefault(item => item.Device.ID == defaultOutput.ID);
            }

            // Populate input devices
            inputDeviceCombo.Items.Clear();
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var capabilities = WaveIn.GetCapabilities(i);
                inputDeviceCombo.Items.Add(new WaveInDeviceItem(i, capabilities.ProductName));
            }

            // Select default input device
            if (inputDeviceCombo.Items.Count > 0)
            {
                inputDeviceCombo.SelectedIndex = 0;
            }
        }

        private void InitializeAudioDevices()
        {
            if (outputDeviceCombo.SelectedItem is not DeviceItem outputDevice ||
                inputDeviceCombo.SelectedItem is not WaveInDeviceItem inputDevice)
                return;

            outputCapture?.Dispose();
            micCapture?.Dispose();
            
            // Initialize output capture
            outputCapture = new WasapiLoopbackCapture(outputDevice.Device);
            var deviceFormat = outputCapture.WaveFormat;
            
            // Create a compatible format for both devices
            var waveFormat = new WaveFormat(44100, 16, 1);
            
            // Reinitialize output capture with the desired format
            outputCapture.Dispose();
            outputCapture = new WasapiLoopbackCapture(outputDevice.Device);
            outputCapture.WaveFormat = waveFormat;

            micCapture = new WaveInEvent
            {
                DeviceNumber = inputDevice.DeviceNumber,
                WaveFormat = waveFormat,
                BufferMilliseconds = 20  // Smaller buffer for more frequent updates
            };

            outputCapture.DataAvailable += OutputCapture_DataAvailable;
            micCapture.DataAvailable += MicCapture_DataAvailable;
        }

        private void StartButton_Click(object? sender, EventArgs e)
        {
            if (sender is not Button button) return;

            if (!isRecording)
            {
                if (outputDeviceCombo.SelectedItem == null || inputDeviceCombo.SelectedItem == null)
                {
                    MessageBox.Show("Please select both input and output devices.");
                    return;
                }

                InitializeAudioDevices();
                StartRecording();
                button.Text = "Stop Recording";
                isRecording = true;
                
                outputDeviceCombo.Enabled = false;
                inputDeviceCombo.Enabled = false;
            }
            else
            {
                StopRecording();
                button.Text = "Start Recording";
                isRecording = false;
                
                outputDeviceCombo.Enabled = true;
                inputDeviceCombo.Enabled = true;
            }
        }

        private void StartRecording()
        {
            outputFilePath = $"dual_channel_audio_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
            var stereoFormat = new WaveFormat(44100, 16, 2); // Always use stereo output
            writer = new WaveFileWriter(outputFilePath, stereoFormat);

            outputCapture?.StartRecording();
            micCapture?.StartRecording();
        }

        private void StopRecording()
        {
            outputCapture?.StopRecording();
            micCapture?.StopRecording();

            writer?.Dispose();
            writer = null;

            if (outputFilePath != null)
            {
                MessageBox.Show($"Recording saved to:\n{outputFilePath}");
            }
        }

        private void OutputCapture_DataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!isRecording) return;

            try
            {
                WriteAudioData(e.Buffer, e.BytesRecorded, true);
                int level = GetAudioLevel(e.Buffer);
                this.BeginInvoke(() => outputLevelLabel.Text = $"Output Level: {level}");
            }
            catch (Exception ex)
            {
                LogMessage($"Error in OutputCapture_DataAvailable: {ex.Message}");
            }
        }

        private void MicCapture_DataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!isRecording) return;

            try
            {
                WriteAudioData(e.Buffer, e.BytesRecorded, false);
                int level = GetAudioLevel(e.Buffer);
                this.BeginInvoke(() => micLevelLabel.Text = $"Mic Level: {level}");
            }
            catch (Exception ex)
            {
                LogMessage($"Error in MicCapture_DataAvailable: {ex.Message}");
            }
        }

        private void WriteAudioData(byte[] buffer, int bytesRecorded, bool isOutput)
        {
            if (writer == null || bytesRecorded == 0) return;

            try
            {
                // Create stereo buffer (twice the size since we're converting mono to stereo)
                byte[] stereoData = new byte[bytesRecorded * 2];

                // Process each sample (2 bytes per sample since we're using 16-bit audio)
                for (int i = 0; i < bytesRecorded; i += 2)
                {
                    if (i + 1 >= bytesRecorded) break; // Skip incomplete sample at the end if any

                    // Get the sample from the incoming buffer
                    short sample = BitConverter.ToInt16(buffer, i);

                    // Scale the sample
                    float scaledSample = sample * (isOutput ? 0.7f : 50.0f);
                    short finalSample = (short)Math.Clamp(scaledSample, short.MinValue, short.MaxValue);

                    // Convert back to bytes
                    byte[] sampleBytes = BitConverter.GetBytes(finalSample);

                    // Write to the appropriate stereo channel
                    int stereoIndex = i * 2;
                    if (isOutput)
                    {
                        // Output device goes to left channel
                        stereoData[stereoIndex] = sampleBytes[0];
                        stereoData[stereoIndex + 1] = sampleBytes[1];
                        stereoData[stereoIndex + 2] = 0; // Right channel silent
                        stereoData[stereoIndex + 3] = 0;
                    }
                    else
                    {
                        // Mic goes to right channel
                        stereoData[stereoIndex] = 0; // Left channel silent
                        stereoData[stereoIndex + 1] = 0;
                        stereoData[stereoIndex + 2] = sampleBytes[0];
                        stereoData[stereoIndex + 3] = sampleBytes[1];
                    }
                }

                // Write the stereo data
                lock (writerLock)
                {
                    writer.Write(stereoData, 0, stereoData.Length);
                    writer.Flush();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error in WriteAudioData: {ex.Message}");
            }
        }

        private void LogMessage(string message)
        {
            try
            {
                string logPath = Path.Combine(
                    Path.GetDirectoryName(outputFilePath ?? "logs") ?? "logs", 
                    "audio_capture.log"
                );
                
                string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: {message}";
                File.AppendAllText(logPath, logMessage + Environment.NewLine);
            }
            catch
            {
                // Silently fail if we can't write to the log
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (isRecording)
            {
                StopRecording();
            }

            outputCapture?.Dispose();
            micCapture?.Dispose();
            writer?.Dispose();
            deviceEnumerator?.Dispose();
            base.OnFormClosing(e);
        }
    }
}