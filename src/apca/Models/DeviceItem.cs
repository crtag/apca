using NAudio.CoreAudioApi;

namespace apca.Models;

public class DeviceItem
{
    public MMDevice Device { get; private set; }

    public DeviceItem(MMDevice device)
    {
        Device = device;
    }

    public override string ToString()
    {
        return Device.FriendlyName;
    }
}