using NAudio.CoreAudioApi;
using AudioCaptureApp.Forms;

namespace AudioCaptureApp.Services;

public class DeviceNotificationClient : MMNotificationClient
{
    private readonly MainForm _form;

    public DeviceNotificationClient(MainForm form)
    {
        _form = form;
    }

    public override void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        _form.BeginInvoke(new Action(() => {
            if (!_form.IsRecording)
            {
                _form.PopulateDeviceLists();
            }
        }));
    }

    public override void OnDeviceAdded(string pwstrDeviceId)
    {
        _form.BeginInvoke(new Action(() => {
            if (!_form.IsRecording)
            {
                _form.PopulateDeviceLists();
            }
        }));
    }

    public override void OnDeviceRemoved(string deviceId)
    {
        _form.BeginInvoke(new Action(() => {
            if (!_form.IsRecording)
            {
                _form.PopulateDeviceLists();
            }
        }));
    }

    public override void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        _form.BeginInvoke(new Action(() => {
            if (!_form.IsRecording)
            {
                _form.PopulateDeviceLists();
            }
        }));
    }
}