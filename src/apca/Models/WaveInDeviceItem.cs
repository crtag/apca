namespace AudioCaptureApp.Models;

public class WaveInDeviceItem
{
    public int DeviceNumber { get; private set; }
    public string DeviceName { get; private set; }

    public WaveInDeviceItem(int number, string name)
    {
        DeviceNumber = number;
        DeviceName = name;
    }

    public override string ToString()
    {
        return DeviceName;
    }
}