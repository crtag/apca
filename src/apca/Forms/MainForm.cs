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
        private BufferedWaveProvider? outputBuffer;
        private BufferedWaveProvider? micBuffer;
        private MMDeviceEnumerator? deviceEnumerator;
        private ComboBox outputDeviceCombo = null!;
        private ComboBox inputDeviceCombo = null!;
        private string? outputFilePath;
        private bool isRecording;
        private System.Threading.Timer? mixerTimer;
        private Label outputLevelLabel = null!;
        private Label micLevelLabel = null!;

        public bool IsRecording => isRecording;

        private int GetAudioLevel(byte[] buffer)
        {
            int sum = 0;
            for (int i = 0; i < buffer.Length; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                sum += Math.Abs(sample);
            }
            return sum / (buffer.Length / 2);
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
            this.Height = 250;

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
            
            // Initialize output capture first to get its format
            outputCapture = new WasapiLoopbackCapture(outputDevice.Device);
            var deviceFormat = outputCapture.WaveFormat;
            
            // Create a compatible format for both devices
            var waveFormat = new WaveFormat(deviceFormat.SampleRate, 16, 1);
            
            // Reinitialize output capture with the desired format
            outputCapture.Dispose();
            outputCapture = new WasapiLoopbackCapture(outputDevice.Device);
            outputCapture.WaveFormat = waveFormat;

            micCapture = new WaveInEvent
            {
                DeviceNumber = inputDevice.DeviceNumber,
                WaveFormat = waveFormat
            };

            // Create buffers with appropriate formats
            outputBuffer = new BufferedWaveProvider(waveFormat) 
            {
                BufferDuration = TimeSpan.FromSeconds(0.2),  // Reduced buffer duration
                DiscardOnBufferOverflow = true
            };

            micBuffer = new BufferedWaveProvider(waveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(0.2),
                DiscardOnBufferOverflow = true
            };

            outputCapture.DataAvailable += OutputCapture_DataAvailable;
            micCapture.DataAvailable += MicCapture_DataAvailable;

            // Stop existing timer if any
            mixerTimer?.Dispose();
            mixerTimer = new System.Threading.Timer(MixAndWriteAudio, null, 0, 5); // Even more frequent updates
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
            
            // Create stereo wave format for output
            var stereoFormat = new WaveFormat(44100, 16, 2);
            writer = new WaveFileWriter(outputFilePath, stereoFormat);

            outputCapture?.StartRecording();
            micCapture?.StartRecording();
        }

        private void StopRecording()
        {
            outputCapture?.StopRecording();
            micCapture?.StopRecording();

            // Dispose and clear the timer
            mixerTimer?.Dispose();
            mixerTimer = null;

            // Clear the buffers
            outputBuffer?.ClearBuffer();
            micBuffer?.ClearBuffer();

            writer?.Dispose();
            writer = null;

            if (outputFilePath != null)
            {
                MessageBox.Show($"Recording saved to:\n{outputFilePath}");
            }
        }

        private void OutputCapture_DataAvailable(object? sender, WaveInEventArgs e)
        {
            if (isRecording)
            {
                outputBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
                int level = GetAudioLevel(e.Buffer);
                this.BeginInvoke(() => outputLevelLabel.Text = $"Output Level: {level}");
            }
        }

        private void MicCapture_DataAvailable(object? sender, WaveInEventArgs e)
        {
            if (isRecording)
            {
                LogMessage($"Mic incoming data: {e.BytesRecorded} bytes");
                int preBufLevel = GetAudioLevel(e.Buffer);
                LogMessage($"Pre-buffer mic level: {preBufLevel}");
                
                micBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
                LogMessage($"Mic buffer contains: {micBuffer?.BufferedBytes ?? 0} bytes");
                
                int level = GetAudioLevel(e.Buffer);
                this.BeginInvoke(() => micLevelLabel.Text = $"Mic Level: {level}");
            }
        }

        private void MixAndWriteAudio(object? state)
        {
            if (!isRecording || writer == null || outputBuffer == null || micBuffer == null) return;

            try
            {
                LogMessage($"Mix: Output buffer: {outputBuffer.BufferedBytes}, Mic buffer: {micBuffer.BufferedBytes}");
                
                int bytesToRead = Math.Min(outputBuffer.BufferedBytes, micBuffer.BufferedBytes);
                bytesToRead -= bytesToRead % 2;

                if (bytesToRead == 0) 
                {
                    LogMessage("No bytes to read from buffers");
                    return;
                }

                LogMessage($"Will read {bytesToRead} bytes");

                byte[] outputData = new byte[bytesToRead];
                byte[] micData = new byte[bytesToRead];

                int outputRead = outputBuffer.Read(outputData, 0, bytesToRead);
                int micRead = micBuffer.Read(micData, 0, bytesToRead);

                LogMessage($"Actually read - Output: {outputRead}, Mic: {micRead}");

                if (outputRead > 0 || micRead > 0)
                {
                    int micLevel = GetAudioLevel(micData);
                    int outputLevel = GetAudioLevel(outputData);
                    LogMessage($"Levels - Output: {outputLevel}, Mic: {micLevel}");
                    System.Diagnostics.Debug.WriteLine($"Mix: Levels - Output: {outputLevel}, Mic: {micLevel}");

                    byte[] stereoData = new byte[outputRead * 2];
                    for (int i = 0; i < outputRead; i += 2)
                    {
                        short outputSample = BitConverter.ToInt16(outputData, i);
                        short micSample = BitConverter.ToInt16(micData, i);

                        // Adjust scaling factors
                        const float outputScale = 0.7f;  // Increased from 0.5
                        const float micScale = 1.5f;     // Reduced from 2.0

                        // Apply scaling with floating-point arithmetic
                        float scaledOutput = outputSample * outputScale;
                        float scaledMic = micSample * micScale;

                        // Clamp values
                        short finalOutput = (short)Math.Clamp(scaledOutput, short.MinValue, short.MaxValue);
                        short finalMic = (short)Math.Clamp(scaledMic, short.MinValue, short.MaxValue);

                        // Write to stereo output (maintaining original byte order)
                        var outputBytes = BitConverter.GetBytes(finalOutput);
                        var micBytes = BitConverter.GetBytes(finalMic);

                        stereoData[i * 2] = outputBytes[0];
                        stereoData[i * 2 + 1] = outputBytes[1];
                        stereoData[i * 2 + 2] = micBytes[0];
                        stereoData[i * 2 + 3] = micBytes[1];
                    }

                    writer.Write(stereoData, 0, stereoData.Length);
                    LogMessage($"Wrote {Math.Max(outputRead, micRead) * 2} bytes to output file");
                }
            }
            catch (Exception ex)
            {
                // Log or handle the error appropriately
                System.Diagnostics.Debug.WriteLine($"Error in MixAndWriteAudio: {ex.Message}");
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
            mixerTimer?.Dispose();
            base.OnFormClosing(e);
        }
    }
}