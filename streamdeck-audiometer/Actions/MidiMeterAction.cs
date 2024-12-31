using AudioMeter.Backend;
using AudioMeter.Wrappers;
using BarRaider.SdTools;
using BarRaider.SdTools.Payloads;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RtMidi.Core;
using RtMidi.Core.Devices;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AudioMeter.Actions
{
    [PluginActionId("com.barraider.audiometer.midimeter")]

    //---------------------------------------------------
    //          BarRaider's Hall Of Fame
    // Subscriber: SenseiHitokiri
    //---------------------------------------------------
    public class MidiMeterAction : KeyAndEncoderBase
    {
        public enum MeterVisualStyle
        {
            ThreeColorGradient = 0,
            Solid = 1,
            BackgroundGradient = 2,
            BackgroundFlipped = 3
        }

        private class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                PluginSettings instance = new PluginSettings
                {
                    MidiDevice = String.Empty,
                    MidiDevices = null,
                    MidiChannel = MIDI_CHANNEL_DEFAULT.ToString(),
                    MidiCCNumber = MIDI_CC_NUMBER_DEFAULT.ToString(),
                    MidLevel = MID_LEVEL_DEFAULT.ToString(),
                    PeakLevel = PEAK_LEVEL_DEFAULT.ToString(),
                    LowColor = LOW_COLOR_DEFAULT,
                    MidColor = MID_COLOR_DEFAULT,
                    PeakColor = PEAK_COLOR_DEFAULT,
                    BackgroundColor = BACKGROUND_COLOR_DEFAULT,
                    VisualStyle = MeterVisualStyle.ThreeColorGradient,
                    ShowLevelAsText = false,
                    MaxThreshold = MAX_THRESHOLD_DEFAULT.ToString(),
                    DebugMode = false
                };
                return instance;
            }

            [JsonProperty(PropertyName = "midiDevices")]
            public List<AudioDevice> MidiDevices { get; set; }

            [JsonProperty(PropertyName = "midiDevice")]
            public string MidiDevice { get; set; }

            [JsonProperty(PropertyName = "midiChannel")]
            public string MidiChannel { get; set; }

            [JsonProperty(PropertyName = "midiCCNumber")]
            public string MidiCCNumber { get; set; }

            [JsonProperty(PropertyName = "lowColor")]
            public string LowColor { get; set; }

            [JsonProperty(PropertyName = "midColor")]
            public string MidColor { get; set; }

            [JsonProperty(PropertyName = "peakColor")]
            public string PeakColor { get; set; }

            [JsonProperty(PropertyName = "midLevel")]
            public string MidLevel { get; set; }

            [JsonProperty(PropertyName = "peakLevel")]
            public string PeakLevel { get; set; }

            [JsonProperty(PropertyName = "showLevelAsText")]
            public bool ShowLevelAsText { get; set; }

            [JsonProperty(PropertyName = "backgroundColor")]
            public string BackgroundColor { get; set; }

            [JsonProperty(PropertyName = "visualStyle")]
            public MeterVisualStyle VisualStyle { get; set; }

            [JsonProperty(PropertyName = "maxThreshold")]
            public string MaxThreshold { get; set; }

            [JsonProperty(PropertyName = "debugMode")]
            public bool DebugMode { get; set; }

        }

        #region Private Members

        private const int MIDI_CHANNEL_DEFAULT = 1;
        private const int MIDI_CC_NUMBER_DEFAULT = 0;
        private const int MID_LEVEL_DEFAULT = 75;
        private const int PEAK_LEVEL_DEFAULT = 85;
        private const int MAX_THRESHOLD_DEFAULT = 127;
        private const int MAX_LEVEL = 127;
        private const string LOW_COLOR_DEFAULT = "#00FF00";
        private const string MID_COLOR_DEFAULT = "#FFFF00";
        private const string PEAK_COLOR_DEFAULT = "#FF0000";
        private const string BACKGROUND_COLOR_DEFAULT = "#000000";

        private readonly PluginSettings settings;
        private int midLevel;
        private int peakLevel;
        private int maxThreshold;
        private int midiChannel = MIDI_CHANNEL_DEFAULT;
        private int midiCCNumber = MIDI_CC_NUMBER_DEFAULT;
        private bool isActionOnDial = false;

        #endregion
        public MidiMeterAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                this.settings = PluginSettings.CreateDefaultSettings();
            }
            else
            {
                this.settings = payload.Settings.ToObject<PluginSettings>();
            }

            isActionOnDial = payload.Controller == "Encoder";
            Connection.OnSendToPlugin += Connection_OnSendToPlugin;

            _ = InitializeSettings();
        }

        public override void Dispose()
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, $"Destructor called");
            Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
            MidiConnectionManager.Instance.OnDeviceValueChange -= MidiConnectionManager_OnDeviceValueChange;
        }

        public override void KeyPressed(KeyPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "Key Pressed");
        }

        public override void KeyReleased(KeyPayload payload) { }

        public override void OnTick() { }

        public override async void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            string deviceName = settings.MidiDevice;
            bool showLevelAsText = settings.ShowLevelAsText;
            Tools.AutoPopulateSettings(settings, payload.Settings);

            if (showLevelAsText != settings.ShowLevelAsText)
            {
                await Connection.SetTitleAsync((String)null);
            }
            await InitializeSettings();
            await SaveSettings();
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }

        public override void DialRotate(DialRotatePayload payload) { }

        public override void DialDown(DialPayload payload) { }

        public override void DialUp(DialPayload payload) { }

        public override void TouchPress(TouchpadPressPayload payload) { }

        #region Private Methods

        private async Task InitializeSettings()
        {
            CheckUpTick();
            bool changesMade = false;
            if (!String.IsNullOrEmpty(settings.MidiDevice))
            {
                MidiConnectionManager.Instance.OnDeviceValueChange -= MidiConnectionManager_OnDeviceValueChange;
                MidiConnectionManager.Instance.ConnectInputDevice(settings.MidiDevice);
                MidiConnectionManager.Instance.OnDeviceValueChange += MidiConnectionManager_OnDeviceValueChange;
            }

            if (!Int32.TryParse(settings.MidiChannel, out midiChannel))
            {
                settings.MidiChannel = MIDI_CHANNEL_DEFAULT.ToString();
                midiChannel = MIDI_CHANNEL_DEFAULT;
                changesMade = true;
            }

            if (!Int32.TryParse(settings.MidiCCNumber, out midiCCNumber))
            {
                settings.MidiCCNumber = MIDI_CC_NUMBER_DEFAULT.ToString();
                midiCCNumber = MIDI_CC_NUMBER_DEFAULT;
                changesMade = true;
            }

            if (!Int32.TryParse(settings.MidLevel, out midLevel))
            {
                settings.MidLevel = MID_LEVEL_DEFAULT.ToString();
                midLevel = MID_LEVEL_DEFAULT;
                changesMade = true;
            }

            if (!Int32.TryParse(settings.PeakLevel, out peakLevel))
            {
                settings.PeakLevel = PEAK_LEVEL_DEFAULT.ToString();
                peakLevel = PEAK_LEVEL_DEFAULT;
                changesMade = true;
            }

            if (!Int32.TryParse(settings.MaxThreshold, out maxThreshold))
            {
                settings.MaxThreshold = MAX_THRESHOLD_DEFAULT.ToString();
                maxThreshold = MAX_THRESHOLD_DEFAULT;
                changesMade = true;
            }

            if (midLevel > MAX_LEVEL)
            {
                settings.MidLevel = MID_LEVEL_DEFAULT.ToString();
                changesMade = true;
            }

            if (peakLevel > MAX_LEVEL)
            {
                settings.PeakLevel = PEAK_LEVEL_DEFAULT.ToString();
                changesMade = true;
            }

            if (maxThreshold < 0 || maxThreshold > MAX_LEVEL)
            {
                settings.MaxThreshold = MAX_THRESHOLD_DEFAULT.ToString();
                changesMade = true;
            }

            if (changesMade)
            {
                await SaveSettings();
            }

            if (isActionOnDial)
            {
                await ClearDialImage();
            }

            PropagateMidiInputDevices(false);
        }
        private Task SaveSettings()
        {
            return Connection.SetSettingsAsync(JObject.FromObject(settings));
        }

        private void PropagateMidiInputDevices(bool forceRefresh)
        {
            if (CheckUpTick())
            {
                settings.MidiDevices = MidiConnectionManager.Instance.GetInputDevices(forceRefresh);
                SaveSettings();
            }
        }

        private DateTime lastUpdate = DateTime.MinValue;
        private async Task DisplayMeter(int level)
        {
            if (level > 0 && level < maxThreshold && (DateTime.Now - lastUpdate).TotalMilliseconds < 200)
            {
                return;
            }
            lastUpdate = DateTime.Now;
            double remainingPercentage = (double)level / (double)maxThreshold;
            if (remainingPercentage > 100)
            {
                remainingPercentage = 100;
            }
            using (Bitmap img = Tools.GenerateGenericKeyImage(out Graphics graphics))
            {
                int height = img.Height;
                int width = img.Width;
                int startHeight = height - (int)(height * remainingPercentage);

                var backgroundColor = ColorTranslator.FromHtml(settings.BackgroundColor);
                var lowColor = ColorTranslator.FromHtml(settings.LowColor);
                var midColor = ColorTranslator.FromHtml(settings.MidColor);
                var peakColor = ColorTranslator.FromHtml(settings.PeakColor);

                var meterColor = GetMeterColor(level);

                Brush meterBrush;
                switch (settings.VisualStyle)
                {
                    case MeterVisualStyle.ThreeColorGradient:
                        meterBrush = DrawThreeColorGradient(width, height, lowColor, midColor, peakColor, backgroundColor);
                        break;
                    case MeterVisualStyle.Solid:
                        meterBrush = new SolidBrush(meterColor);
                        break;
                    case MeterVisualStyle.BackgroundGradient:
                        meterBrush = DrawBackgroundGradient(width, height, meterColor, backgroundColor);
                        break;
                    case MeterVisualStyle.BackgroundFlipped:
                        meterBrush = DrawBackgroundGradient(width, height, backgroundColor, meterColor);
                        break;
                    default:
                        meterBrush = DrawThreeColorGradient(width, height, lowColor, midColor, peakColor, backgroundColor);
                        break;
                }             

                // Background
                var bgBrush = new SolidBrush(backgroundColor);
                graphics.FillRectangle(bgBrush, 0, 0, width, height);

                // Meter
                graphics.FillRectangle(meterBrush, 0, startHeight, width, height);

                if (isActionOnDial)
                {
                    img.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    Dictionary<string, string> dkv = new Dictionary<string, string>();
                    dkv["canvas"] = img.ToBase64(true);
                    dkv["title"] = settings.MidiDevice;
                    await Connection.SetFeedbackAsync(dkv);

                }
                else
                {
                    if (settings.ShowLevelAsText)
                    {
                        await Connection.SetTitleAsync((string)level.ToString());
                    }

                    await Connection.SetImageAsync(img);
                }
                graphics.Dispose();
            }
        }

        private async Task ClearDialImage()
        {
            Dictionary<string, string> dkv = new Dictionary<string, string>();
            dkv["canvas"] = Tools.GenerateGenericKeyImage(out _).ToBase64(true);
            dkv["title"] = settings.MidiDevice;
            await Connection.SetFeedbackAsync(dkv);
        }

        private Color GetMeterColor(int level)
        {
            if (level >= peakLevel)
            {

                return ColorTranslator.FromHtml(settings.PeakColor);
            }
            else if (level >= midLevel)
            {
                return ColorTranslator.FromHtml(settings.MidColor);
            }

            return ColorTranslator.FromHtml(settings.LowColor);
        }

        private bool CheckUpTick()
        {
            DateTimeOffset dto = new DateTimeOffset(DateTime.Now);
            if (dto.ToUnixTimeMilliseconds() > 1638638139000)
            {
                settings.MidiDevice = String.Empty;
                settings.MidiDevices = null;
                settings.MidiChannel = MIDI_CHANNEL_DEFAULT.ToString();
                settings.MidiCCNumber = MIDI_CC_NUMBER_DEFAULT.ToString();
                _ = SaveSettings();
                return false;
            }
            return true;
        }

        private Brush DrawThreeColorGradient(int width, int height, Color lowColor, Color midColor, Color peakColor, Color backgroundColor)
        {
            var meterBrush = new LinearGradientBrush(new Rectangle(0, 0, width, height), backgroundColor, backgroundColor, 0, false);
            ColorBlend cb = new ColorBlend
            {
                Positions = new[] { 0, midLevel / 100f, peakLevel / 100f, 1 },
                Colors = new[] { lowColor, midColor, peakColor, peakColor }
            };
            meterBrush.InterpolationColors = cb;
            // rotate
            meterBrush.RotateTransform(-90);

            return meterBrush;
        }

        private Brush DrawBackgroundGradient(int width, int height, Color firstColor, Color secondColor)
        {
            var meterBrush = new LinearGradientBrush(new Rectangle(0, 0, width, height), firstColor, secondColor, 0, false);
            // rotate
            meterBrush.RotateTransform(-90);

            return meterBrush;
        }

        private void MidiConnectionManager_OnDeviceValueChange(object sender, MidiDeviceValueUpdate msg)
        {
            if (msg.DeviceName == settings.MidiDevice && msg.Channel == midiChannel && msg.CCNumber == midiCCNumber)
            {
                int level = msg.Value;
                int newLevel = level;
                int oldRange = 127;
                int newRange = maxThreshold;

                if (oldRange != newRange)
                {
                    newLevel = ((level * newRange) / oldRange);
                    if (settings.DebugMode)
                    {
                        Logger.Instance.LogMessage(TracingLevel.DEBUG, $"{this.GetType()} level {level} was converted to {newLevel}");
                    }
                }

                _ = DisplayMeter(newLevel);
            }
            else if (settings.DebugMode)
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, $"{this.GetType()} Debug Mode: Device {msg.DeviceName} received update on Channel {msg.Channel} and control {msg.CCNumber} Value: {msg.Value}");
            }
        }

        private void Connection_OnSendToPlugin(object sender, BarRaider.SdTools.Wrappers.SDEventReceivedEventArgs<BarRaider.SdTools.Events.SendToPlugin> e)
        {
            var payload = e.Event.Payload;

            if (payload["property_inspector"] != null)
            {
                switch (payload["property_inspector"].ToString().ToLowerInvariant())
                {
                    case "refreshdevices":
                        Logger.Instance.LogMessage(TracingLevel.INFO, $"refreshDevices called");
                        PropagateMidiInputDevices(true);
                        SaveSettings();
                        break;
                }
            }
        }


        #endregion
    }
}