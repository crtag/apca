using NAudio.Wave;
using NAudio.CoreAudioApi;
using AudioCaptureApp.Models;
using AudioCaptureApp.Services;

namespace AudioCaptureApp.Forms;

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

    public bool IsRecording => isRecording;

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

        Controls.AddRange(new Control[] { 
            outputLabel, outputDeviceCombo, 
            inputLabel, inputDeviceCombo,
            startButton 
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

        // Initialize input capture
        micCapture = new WaveInEvent { DeviceNumber = inputDevice.DeviceNumber };

        // Set same format for both captures
        var waveFormat = new WaveFormat(44100, 16, 1);
        micCapture.WaveFormat = waveFormat;

        outputBuffer = new BufferedWaveProvider(waveFormat);
        micBuffer = new BufferedWaveProvider(waveFormat);

        outputCapture.DataAvailable += OutputCapture_DataAvailable;
        micCapture.DataAvailable += MicCapture_DataAvailable;

        mixerTimer = new System.Threading.Timer(MixAndWriteAudio, null, 0, 20);
    }

    private void StartButton_Click(object sender, EventArgs e)
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
        }
    }

    private void MicCapture_DataAvailable(object? sender, WaveInEventArgs e)
    {
        if (isRecording)
        {
            micBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }
    }

    private void MixAndWriteAudio(object? state)
    {
        if (!isRecording || writer == null || outputBuffer == null || micBuffer == null) return;

        int bytesToRead = 4410; // Read ~100ms worth of audio at 44.1kHz
        byte[] outputData = new byte[bytesToRead];
        byte[] micData = new byte[bytesToRead];

        int outputRead = outputBuffer.Read(outputData, 0, bytesToRead);
        int micRead = micBuffer.Read(micData, 0, bytesToRead);

        // If we have data from both sources
        if (outputRead > 0 && micRead > 0)
        {
            // Convert to stereo: left channel = system audio, right channel = mic
            byte[] stereoData = new byte[outputRead * 2];
            for (int i = 0; i < outputRead; i += 2)
            {
                // Left channel (system audio)
                stereoData[i * 2] = outputData[i];
                stereoData[i * 2 + 1] = outputData[i + 1];
                
                // Right channel (mic)
                stereoData[i * 2 + 2] = micData[i];
                stereoData[i * 2 + 3] = micData[i + 1];
            }

            writer.Write(stereoData, 0, stereoData.Length);
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