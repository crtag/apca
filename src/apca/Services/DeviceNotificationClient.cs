using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using apca.Forms;

namespace apca.Services
{
    public class DeviceNotificationClient : IMMNotificationClient
    {
        private readonly MainForm _form;

        public DeviceNotificationClient(MainForm form)
        {
            _form = form;
        }

        public void OnDefaultDeviceChanged(DataFlow dataFlow, Role role, string defaultDeviceId)
        {
            _form.BeginInvoke(new Action(() => {
                if (!_form.isRecording)
                {
                    _form.PopulateDeviceLists();
                }
            }));
        }

        public void OnDeviceAdded(string deviceId)
        {
            _form.BeginInvoke(new Action(() => {
                if (!_form.isRecording)
                {
                    _form.PopulateDeviceLists();
                }
            }));
        }

        public void OnDeviceRemoved(string deviceId)
        {
            _form.BeginInvoke(new Action(() => {
                if (!_form.isRecording)
                {
                    _form.PopulateDeviceLists();
                }
            }));
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            _form.BeginInvoke(new Action(() => {
                if (!_form.isRecording)
                {
                    _form.PopulateDeviceLists();
                }
            }));
        }

        public void OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
            // Usually don't need to handle this one, but required by interface
        }
    }
}