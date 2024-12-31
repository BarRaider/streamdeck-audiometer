using AudioMeter.Wrappers;
using BarRaider.SdTools;
using RtMidi.Core;
using RtMidi.Core.Devices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AudioMeter.Backend
{

    public class MidiConnectionManager
    {
        #region Private Members

        private static MidiConnectionManager instance = null;
        private static readonly object objLock = new object();

        private static readonly object connectLock = new object();
        private Dictionary<string, IMidiInputDevice> dictMidiInputDevices = new Dictionary<string, IMidiInputDevice>();
        private List<AudioDevice> inputDevices;


        #endregion

        #region Constructors

        public static MidiConnectionManager Instance
        {
            get
            {
                if (instance != null)
                {
                    return instance;
                }

                lock (objLock)
                {
                    if (instance == null)
                    {
                        instance = new MidiConnectionManager();
                    }
                    return instance;
                }
            }
        }

        private MidiConnectionManager()
        {
        }

        #endregion

        #region Public Methods

        internal event EventHandler<MidiDeviceValueUpdate> OnDeviceValueChange;

        internal List<AudioDevice> GetInputDevices(bool forceRefresh)
        {
            if (inputDevices != null && inputDevices.Count > 0 && !forceRefresh)
            {

                Logger.Instance.LogMessage(TracingLevel.WARN, $"{this.GetType()} GetInputDevices cache found {inputDevices.Count} devices");
                return inputDevices;
            }
            inputDevices = new List<AudioDevice>();

            try
            {
                var devices = MidiDeviceManager.Default.InputDevices.OrderBy(d => d.Name).ToList();
                foreach (var device in devices)
                {
                    inputDevices.Add(new AudioDevice() { ProductName = device.Name });
                }

                if (devices.Count == 0)
                {
                    var outputDevices = MidiDeviceManager.Default.OutputDevices.ToList();
                    Logger.Instance.LogMessage(TracingLevel.WARN, $"{this.GetType()} GetInputDevices failed to find any input devices. There are {outputDevices.Count} output devices");
                    if (outputDevices.Count > 0)
                    {
                        Logger.Instance.LogMessage(TracingLevel.WARN, $"{this.GetType()} GetInputDevices { String.Join(", ", outputDevices.Select(d => d.Name).ToArray())}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error propagating input devices {ex}");
            }
            Logger.Instance.LogMessage(TracingLevel.WARN, $"{this.GetType()} GetInputDevices discovered {inputDevices.Count} devices");
            return inputDevices;
        }

        internal void ConnectInputDevice(string deviceName)
        {
            deviceName = deviceName.ToLowerInvariant();
            lock (connectLock)
            {
                if (dictMidiInputDevices.ContainsKey(deviceName) && dictMidiInputDevices[deviceName] != null && dictMidiInputDevices[deviceName].IsOpen)
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"{this.GetType()} ConnectMidiDevice: Already connected to {deviceName}");
                    return;
                }

                try
                {
                    IMidiInputDevice midiDevice = null;
                    if (dictMidiInputDevices.ContainsKey(deviceName) && dictMidiInputDevices[deviceName] != null)
                    {
                        DisconnectMidiDevice(deviceName);
                    }

                    Logger.Instance.LogMessage(TracingLevel.INFO, $"{this.GetType()} ConnectMidiDevice: Connecting to {deviceName}");

                    var device = MidiDeviceManager.Default.InputDevices.Where(d => d.Name.ToLowerInvariant().Contains(deviceName)).FirstOrDefault();
                    if (device == null)
                    {
                        Logger.Instance.LogMessage(TracingLevel.WARN, $"{this.GetType()} ConnectMidiDevice: Device not found: {deviceName}!");
                        return;
                    }
                    midiDevice = device.CreateDevice();
                    midiDevice.Open();
                    midiDevice.ControlChange += MidiDevice_ControlChange;
                    dictMidiInputDevices[deviceName] = midiDevice;
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, $"{this.GetType()} ConnectMidiDevice Exception {ex}");
                }
            }
        }

        private void DisconnectMidiDevice(string deviceName)
        {
            deviceName = deviceName.ToLowerInvariant();

            if (!dictMidiInputDevices.ContainsKey(deviceName) || dictMidiInputDevices[deviceName] == null)
            {
                return;
            }

            try
            {

                Logger.Instance.LogMessage(TracingLevel.INFO, $"{this.GetType()} DisconnectMidiDevice: Disconnecting {deviceName}");
                var midiDevice = dictMidiInputDevices[deviceName];
                dictMidiInputDevices.Remove(deviceName);
                if (midiDevice.IsOpen)
                {
                    midiDevice.Close();
                }
                midiDevice.ControlChange -= MidiDevice_ControlChange;
                midiDevice.Dispose();
                midiDevice = null;
                
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"{this.GetType()} DisconnectMidiDevice Exception {ex}");
            }
        }

        private void MidiDevice_ControlChange(IMidiInputDevice sender, in RtMidi.Core.Messages.ControlChangeMessage msg)
        {
            OnDeviceValueChange?.Invoke(this, new MidiDeviceValueUpdate(sender.Name, ((int)msg.Channel + 1), msg.Control, msg.Value));
        }

        #endregion
    }

}
