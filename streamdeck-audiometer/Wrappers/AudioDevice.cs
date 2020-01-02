using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AudioMeter.Wrappers
{
    internal class AudioDevice
    {
        [JsonProperty(PropertyName = "name")]
        public string ProductName {get; set;}
    }
}
