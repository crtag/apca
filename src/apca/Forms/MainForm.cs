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

    public class MultiplexingSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider[] sources;
        private readonly int outputChannelCount;
        private readonly int[] mappings;
        private float[] sourceBuffer;

        public WaveFormat WaveFormat { get; }

        public MultiplexingSampleProvider(ISampleProvider[] sources, int outputChannels)
        {
            this.sources = sources;
            this.outputChannelCount = outputChannels;
            
            // Create default mappings (will be configured later)
            this.mappings = new int[outputChannels];
            for (int i = 0; i < mappings.Length; i++)
                mappings[i] = -1; // -1 means no input mapped
                
            // Create a buffer for reading from sources
            int maxSourceChannels = 0;
            foreach (var source in sources)
                maxSourceChannels = Math.Max(maxSourceChannels, source.WaveFormat.Channels);
                
            sourceBuffer = new float[maxSourceChannels * 1024]; // 1024 samples buffer
            
            // Create output format (always 32-bit float)
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, outputChannels);
        }

        public void MapInputChannelToOutput(int inputProviderIndex, int inputChannel, int outputChannel)
        {
            if (outputChannel < 0 || outputChannel >= outputChannelCount)
                throw new ArgumentException("Invalid output channel");
                
            if (inputProviderIndex < 0 || inputProviderIndex >= sources.Length)
                throw new ArgumentException("Invalid input provider index");
                
            if (inputChannel < 0 || inputChannel >= sources[inputProviderIndex].WaveFormat.Channels)
                throw new ArgumentException("Invalid input channel");
                
            mappings[outputChannel] = (inputProviderIndex << 16) | inputChannel;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            // Clear the output buffer first
            Array.Clear(buffer, offset, count);
            
            int sampleFrames = count / outputChannelCount;
            int maxFramesRead = 0;
            
            // Read from each source
            for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                var source = sources[sourceIndex];
                int sourceSamplesRequired = sampleFrames * source.WaveFormat.Channels;
                
                if (sourceBuffer.Length < sourceSamplesRequired)
                    Array.Resize(ref sourceBuffer, sourceSamplesRequired);
                    
                int samplesRead = source.Read(sourceBuffer, 0, sourceSamplesRequired);
                int framesRead = samplesRead / source.WaveFormat.Channels;
                maxFramesRead = Math.Max(maxFramesRead, framesRead);
                
                // Copy samples to output according to mapping
                for (int frame = 0; frame < framesRead; frame++)
                {
                    for (int outputChannel = 0; outputChannel < outputChannelCount; outputChannel++)
                    {
                        if ((mappings[outputChannel] >> 16) == sourceIndex)
                        {
                            int inputChannel = mappings[outputChannel] & 0xFFFF;
                            if (inputChannel < source.WaveFormat.Channels)
                            {
                                int inputIndex = (frame * source.WaveFormat.Channels) + inputChannel;
                                int outputIndex = offset + (frame * outputChannelCount) + outputChannel;
                                buffer[outputIndex] += sourceBuffer[inputIndex];
                            }
                        }
                    }
                }
            }
            
            return maxFramesRead * outputChannelCount;
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
        private Label outputLevelLabel = null!;
        private Label micLevelLabel = null!;
        private MultiplexingSampleProvider? multiplexer;
        private readonly object writerLock = new object();
        private System.Threading.Timer? processingTimer;

        public bool isRecording { get; private set; }

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

            // Cleanup existing instances
            StopAndDisposeDevices();
            
            try
            {
                const int sampleRate = 44100;
                const int bufferMs = 50; // 50ms buffer for both devices
                
                // Initialize output capture with error checking
                var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
                outputCapture = new WasapiLoopbackCapture(outputDevice.Device);
                outputCapture.WaveFormat = waveFormat;
                
                // Test the output capture initialization
                outputCapture.StartRecording();
                System.Threading.Thread.Sleep(100);
                if (!outputCapture.CaptureState.Equals(NAudio.CoreAudioApi.CaptureState.Capturing))
                {
                    throw new InvalidOperationException("Output capture failed to start properly");
                }
                outputCapture.StopRecording();
                
                LogMessage($"Successfully initialized output capture for device: {outputDevice.Device.FriendlyName}");
                
                // Initialize input capture with same buffer size
                micCapture = new WaveInEvent
                {
                    DeviceNumber = inputDevice.DeviceNumber,
                    WaveFormat = waveFormat,
                    BufferMilliseconds = bufferMs
                };

                // Create sample providers
                var outputProvider = new SampleProvider(outputCapture);
                var micProvider = new SampleProvider(micCapture);
                
                // Create multiplexer with both sources
                multiplexer = new MultiplexingSampleProvider(
                    new ISampleProvider[] { outputProvider, micProvider }, 
                    2); // Stereo output
                    
                multiplexer.MapInputChannelToOutput(0, 0, 0); // Output -> Left
                multiplexer.MapInputChannelToOutput(1, 0, 1); // Mic -> Right
                
                outputFilePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    $"dual_channel_audio_{DateTime.Now:yyyyMMdd_HHmmss}.wav"
                );
                writer = new WaveFileWriter(outputFilePath, multiplexer.WaveFormat);

                // Use a shorter timer interval for more frequent processing
                processingTimer?.Dispose();
                processingTimer = new System.Threading.Timer(ProcessAudio, null, 0, bufferMs); // Match buffer size

                outputCapture.DataAvailable += (s, e) => UpdateLevel(GetAudioLevel(e.Buffer), true);
                micCapture.DataAvailable += (s, e) => UpdateLevel(GetAudioLevel(e.Buffer), false);
            }
            catch (Exception ex)
            {
                LogMessage($"Error in InitializeAudioDevices: {ex.Message}");
                MessageBox.Show($"Error initializing audio devices: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                StopAndDisposeDevices();
                throw;
            }
        }

        private void StopAndDisposeDevices()
        {
            processingTimer?.Dispose();
            processingTimer = null;

            outputCapture?.StopRecording();
            outputCapture?.Dispose();
            outputCapture = null;

            micCapture?.StopRecording();
            micCapture?.Dispose();
            micCapture = null;

            lock (writerLock)
            {
                writer?.Dispose();
                writer = null;
            }

            multiplexer = null;
        }

        private class SampleProvider : ISampleProvider
        {
            private readonly IWaveIn waveIn;
            private readonly Queue<float> sampleQueue = new Queue<float>();
            private readonly object lockObj = new object();
            private readonly int maxQueueSize = 44100; // 1 second worth of samples

            public WaveFormat WaveFormat { get; }

            public SampleProvider(IWaveIn waveIn)
            {
                this.waveIn = waveIn;
                this.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
                waveIn.DataAvailable += WaveIn_DataAvailable;
            }

            private void WaveIn_DataAvailable(object? sender, WaveInEventArgs e)
            {
                lock (lockObj)
                {
                    // Convert bytes to float samples
                    for (int i = 0; i < e.BytesRecorded; i += 4) // 4 bytes per float
                    {
                        float sample = BitConverter.ToSingle(e.Buffer, i);
                        
                        // Only add if we haven't exceeded our max queue size
                        if (sampleQueue.Count < maxQueueSize)
                        {
                            sampleQueue.Enqueue(sample);
                        }
                    }
                }
            }

            public int Read(float[] buffer, int offset, int count)
            {
                int samplesRead = 0;

                lock (lockObj)
                {
                    while (samplesRead < count && sampleQueue.Count > 0)
                    {
                        buffer[offset + samplesRead] = sampleQueue.Dequeue();
                        samplesRead++;
                    }

                    // If we couldn't read enough samples, fill the rest with silence
                    while (samplesRead < count)
                    {
                        buffer[offset + samplesRead] = 0f;
                        samplesRead++;
                    }
                }

                return samplesRead;
            }
        }

        private void ProcessAudio(object? state)
        {
            if (!isRecording || writer == null || multiplexer == null)
                return;

            try
            {
                const int samplesPerProcess = 4410; // Process 100ms worth of audio at 44.1kHz
                var buffer = new float[samplesPerProcess];
                
                int samplesRead = multiplexer.Read(buffer, 0, buffer.Length);
                
                if (samplesRead > 0)
                {
                    lock (writerLock)
                    {
                        writer.WriteSamples(buffer, 0, samplesRead);
                        writer.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error in audio processing: {ex.Message}");
            }
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
                outputCapture?.StartRecording();
                micCapture?.StartRecording();
                
                button.Text = "Stop Recording";  // Changed to show Stop
                isRecording = true;              // Set to true when starting
                outputDeviceCombo.Enabled = false; // Disable device selection during recording
                inputDeviceCombo.Enabled = false;  // Disable device selection during recording
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

        private void StopRecording()
        {
            processingTimer?.Dispose();
            processingTimer = null;

            outputCapture?.StopRecording();
            micCapture?.StopRecording();

            lock (writerLock)
            {
                writer?.Dispose();
                writer = null;
            }

            if (outputFilePath != null)
            {
                MessageBox.Show($"Recording saved to:\n{outputFilePath}");
            }
        }

        private int GetAudioLevel(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0)
                return 0;

            long sum = 0;
            for (int i = 0; i < buffer.Length; i += 4) // 4 bytes per float
            {
                float sample = BitConverter.ToSingle(buffer, i);
                sum += (int)(Math.Abs(sample) * short.MaxValue);
            }
            return (int)(sum / (buffer.Length / 4));
        }

        private void UpdateLevel(int level, bool isOutput)
        {
            BeginInvoke(() =>
            {
                if (isOutput)
                    outputLevelLabel.Text = $"Output Level: {level}";
                else
                    micLevelLabel.Text = $"Mic Level: {level}";
            });
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

            processingTimer?.Dispose();
            outputCapture?.Dispose();
            micCapture?.Dispose();
            writer?.Dispose();
            deviceEnumerator?.Dispose();
            base.OnFormClosing(e);
        }
    }
}