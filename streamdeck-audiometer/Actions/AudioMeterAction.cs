﻿using AudioMeter.Wrappers;
using BarRaider.SdTools;
using BarRaider.SdTools.Payloads;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
    [PluginActionId("com.barraider.audiometer.audiometer")]

    //---------------------------------------------------
    //          BarRaider's Hall Of Fame
    // Subscriber: SenseiHitokiri
    //---------------------------------------------------
    public class AudioMeterAction : KeyAndEncoderBase
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
                    AudioDevice = String.Empty,
                    AudioDevices = null,
                    MidLevel = MID_LEVEL_DEFAULT.ToString(),
                    PeakLevel = PEAK_LEVEL_DEFAULT.ToString(),
                    LowColor = LOW_COLOR_DEFAULT,
                    MidColor = MID_COLOR_DEFAULT,
                    PeakColor = PEAK_COLOR_DEFAULT,
                    BackgroundColor = BACKGROUND_COLOR_DEFAULT,
                    VisualStyle = MeterVisualStyle.ThreeColorGradient,
                    ShowLevelAsText = false,
                    MaxThreshold = MAX_THRESHOLD_DEFAULT.ToString()
                };
                return instance;
            }

            [JsonProperty(PropertyName = "audioDevices")]
            public List<AudioDevice> AudioDevices { get; set; }

            [JsonProperty(PropertyName = "audioDevice")]
            public string AudioDevice { get; set; }

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

        }

        #region Private Members
        private const int MID_LEVEL_DEFAULT = 75;
        private const int PEAK_LEVEL_DEFAULT = 85;
        private const int MAX_THRESHOLD_DEFAULT = 100;
        private const string LOW_COLOR_DEFAULT = "#00FF00";
        private const string MID_COLOR_DEFAULT = "#FFFF00";
        private const string PEAK_COLOR_DEFAULT = "#FF0000";
        private const string BACKGROUND_COLOR_DEFAULT = "#000000";
        private const string MUTE_ICON_PATH = @"images\muteIcon.png";

        private readonly PluginSettings settings;
        private readonly System.Timers.Timer tmrGetAudioLevel = new System.Timers.Timer();
        private MMDevice mmDevice = null;
        private int midLevel;
        private int peakLevel;
        private int maxThreshold;
        private bool isActionOnDial = false;

        #endregion
        public AudioMeterAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
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
            tmrGetAudioLevel.Interval = 200;
            tmrGetAudioLevel.Elapsed += TmrGetAudioLevel_Elapsed;

            _ = InitializeSettings();
        }

        public override void Dispose()
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, $"Destructor called");
            tmrGetAudioLevel.Stop();
        }

        public async override void KeyPressed(KeyPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "Key Pressed");

            if (mmDevice == null)
            {
                SetMMDeviceFromDeviceName(settings.AudioDevice);
            }

            if (mmDevice == null)
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, "Key Pressed but mmDevice is null");
                return;
            }

            // Toggle mute
            mmDevice.AudioEndpointVolume.Mute = !mmDevice.AudioEndpointVolume.Mute;
            await Connection.SetTitleAsync((String)null);
        }

        public override void KeyReleased(KeyPayload payload) { }

        public override void OnTick() { }

        public override async void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            string deviceName = settings.AudioDevice;
            bool showLevelAsText = settings.ShowLevelAsText;
            Tools.AutoPopulateSettings(settings, payload.Settings);
            if (deviceName != settings.AudioDevice)
            {
                mmDevice = null;
            }

            if (showLevelAsText != settings.ShowLevelAsText)
            {
                await Connection.SetTitleAsync((String)null);
            }
            await InitializeSettings();
            await SaveSettings();
        }

        public override void DialRotate(DialRotatePayload payload) { }

        public override void DialDown(DialPayload payload) 
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "Key Pressed");

            if (mmDevice == null)
            {
                SetMMDeviceFromDeviceName(settings.AudioDevice);
            }

            if (mmDevice == null)
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, "Key Pressed but mmDevice is null");
                return;
            }

            // Toggle mute
            mmDevice.AudioEndpointVolume.Mute = !mmDevice.AudioEndpointVolume.Mute;
            _ = ClearDialImage();
        }

        public override void DialUp(DialPayload payload) { }

        public override void TouchPress(TouchpadPressPayload payload) { }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }

        #region Private Methods

        private async Task InitializeSettings()
        {
            tmrGetAudioLevel.Stop();
            if (!String.IsNullOrEmpty(settings.AudioDevice))
            {
                tmrGetAudioLevel.Start();
            }

            if (!Int32.TryParse(settings.MidLevel, out midLevel))
            {
                settings.MidLevel = MID_LEVEL_DEFAULT.ToString();
                await SaveSettings();
            }

            if (!Int32.TryParse(settings.PeakLevel, out peakLevel))
            {
                settings.PeakLevel = PEAK_LEVEL_DEFAULT.ToString();
                await SaveSettings();
            }

            if (!Int32.TryParse(settings.MaxThreshold, out maxThreshold))
            {
                settings.MaxThreshold = MAX_THRESHOLD_DEFAULT.ToString();
                await SaveSettings();
            }

            if (midLevel > 100)
            {
                settings.MidLevel = MID_LEVEL_DEFAULT.ToString();
                await SaveSettings();
            }
            if (peakLevel > 100)
            {
                settings.PeakLevel = PEAK_LEVEL_DEFAULT.ToString();
                await SaveSettings();
            }
            if (maxThreshold < 0 || maxThreshold > 100)
            {
                settings.MaxThreshold = MAX_THRESHOLD_DEFAULT.ToString();
                await SaveSettings();
            }

            if (isActionOnDial)
            {
                await ClearDialImage();
            }

            await PropagatePlaybackDevices();
        }

        private Task SaveSettings()
        {
            return Connection.SetSettingsAsync(JObject.FromObject(settings));
        }

        private Task PropagatePlaybackDevices()
        {
            return Task.Run(async () =>
            {
                settings.AudioDevices = new List<AudioDevice>();

                try
                {
                    MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
                    var devices = enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active).ToList().OrderBy(d => d.FriendlyName);
                    foreach (var device in devices)
                    {
                        settings.AudioDevices.Add(new AudioDevice() { ProductName = device.FriendlyName });
                    }

                    await SaveSettings();
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error propagating playback devices {ex}");
                }
            });
        }

        private async void TmrGetAudioLevel_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (mmDevice == null)
            {
                SetMMDeviceFromDeviceName(settings.AudioDevice);
            }

            if (mmDevice != null)
            {
                if (mmDevice.AudioEndpointVolume.Mute)
                {
                    await DisplayMute();
                }
                else
                {
                    int volumeLevel = (int)(mmDevice.AudioMeterInformation.MasterPeakValue * 100);
                    await DisplayMeter(volumeLevel);
                }
            }
            else
            {
                tmrGetAudioLevel.Stop();
            }
        }

        private void SetMMDeviceFromDeviceName(string deviceName)
        {
            if (String.IsNullOrEmpty(deviceName))
            {
                mmDevice = null;
                return;
            }

            MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
            mmDevice = enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active).ToList().Where(d => d.FriendlyName == deviceName).FirstOrDefault();
        }

        private async Task DisplayMute()
        {
            using (Bitmap img = Tools.GenerateGenericKeyImage(out Graphics graphics))
            {
                int height = img.Height;
                int width = img.Width;
                var backgroundColor = ColorTranslator.FromHtml(settings.BackgroundColor);

                // Background
                var bgBrush = new SolidBrush(backgroundColor);
                graphics.FillRectangle(bgBrush, 0, 0, width, height);

                // Mute icon
                Image icon = Image.FromFile(MUTE_ICON_PATH);
                if (isActionOnDial)
                {
                    graphics.DrawImage(icon, (width - icon.Width) / 2, 0, icon.Width, icon.Height* 2);
                }
                else
                {
                    float iconWidth = (width - icon.Width) / 2;
                    float iconHeight = (height - icon.Height) / 2;
                    PointF iconStart = new PointF(iconWidth, iconHeight);
                    graphics.DrawImage(icon, iconStart);
                }
                icon.Dispose();


                if (isActionOnDial)
                {
                    Dictionary<string, string> dkv = new Dictionary<string, string>();
                    dkv["canvas"] = img.ToBase64(true);
                    dkv["title"] = settings.AudioDevice;
                    await Connection.SetFeedbackAsync(dkv);

                }
                else
                {
                    await Connection.SetImageAsync(img);
                }

                graphics.Dispose();
            }
        }


        private async Task DisplayMeter(int level)
        {
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
                    dkv["title"] = settings.AudioDevice;
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
            dkv["title"] = settings.AudioDevice;
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
        #endregion
    }
}