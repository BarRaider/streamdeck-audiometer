using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AudioMeter.Wrappers
{
    internal class MidiDeviceValueUpdate
    {
        public string DeviceName { get; private set; }
        public int Channel { get; private set; }
        public int CCNumber { get; private set; }

        public int Value { get; private set; }

        public MidiDeviceValueUpdate(string deviceName, int channel, int ccNumber, int value)
        {
            DeviceName = deviceName;
            Channel = channel;
            CCNumber = ccNumber;
            Value = value;
        }

    }
}
