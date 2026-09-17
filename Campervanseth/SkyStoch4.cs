#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// This namespace holds indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.Sky
{
    /// <summary>
    /// SkyStoch4.
    /// Behavior-preserving hardened version of SkyStoch3.
    /// </summary>
    public class SkyStoch4 : Indicator
    {
        #region Constants
        private const string SystemVersion = "v1";
        private const string SystemName = "SkyStoch4";
        private const string FullSystemName = SystemName + " - " + SystemVersion;

        // If true, arrow logic compares the prior bar to the prior band instead of the current band.
        // Left false by default to preserve your existing signal behavior.
        private const bool UseStrictSignalBands = false;

        // Default values
        private const bool DEFAULT_ENABLE_LONG_ALERTS = true;
        private const bool DEFAULT_ENABLE_SHORT_ALERTS = false;
        private const bool DEFAULT_USE_EXPONENTIAL_MA = true;
        private const int DEFAULT_MA_PERIOD = 86;
        private const int DEFAULT_ATR_PERIOD = 600;
        private const double DEFAULT_OFFSET_MULTIPLIER1 = 4.0;
        private const double DEFAULT_OFFSET_MULTIPLIER2 = 6.0;
        private const bool DEFAULT_SKY_BAR_COLORING = true;
        private const bool DEFAULT_ENABLE_SYMBOL = false;
        private const int DEFAULT_SYMBOL_DISTANCE = 11;
        private const int DEFAULT_OPACITY = 20;
        private const int DEFAULT_STOCH_K_PERIOD = 3;
        private const int DEFAULT_STOCH_D_PERIOD = 15;
        private const int DEFAULT_STOCH_SMOOTH = 3;
        private const double DEFAULT_STOCHASTIC_OVERBOUGHT = 90.0;
        private const double DEFAULT_STOCHASTIC_OVERSOLD = 10.0;
        private const string DEFAULT_LONG_ALERT_SOUND = "Alert1.wav";
        private const string DEFAULT_SHORT_ALERT_SOUND = "Alert2.wav";
        #endregion

        #region Private Fields
        private EMA emaValue;
        private SMA smaValue;
        private ATR atrValue;
        private Stochastics stochastics;

        private string drawLong = "▲";
        private string drawShort = "▼";
        private string longAlertSound = DEFAULT_LONG_ALERT_SOUND;
        private string shortAlertSound = DEFAULT_SHORT_ALERT_SOUND;

        private int minimumBars;
        private BarZone previousBarZone = BarZone.Mid;

        private enum BarZone
        {
            Mid,
            Up,
            Down
        }
        #endregion

        #region Public Class
        public override string DisplayName => FullSystemName;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = SystemName;
                Description = FullSystemName;
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                IsSuspendedWhileInactive = true;
                IsAutoScale = false;
                PaintPriceMarkers = false;

                // Set default values using constants
                EnableLongAlerts = DEFAULT_ENABLE_LONG_ALERTS;
                EnableShortAlerts = DEFAULT_ENABLE_SHORT_ALERTS;
                LongAlertSound = DEFAULT_LONG_ALERT_SOUND;
                ShortAlertSound = DEFAULT_SHORT_ALERT_SOUND;
                UseExponentialMovingAverage = DEFAULT_USE_EXPONENTIAL_MA;
                MAPeriod = DEFAULT_MA_PERIOD;
                ATRPeriod = DEFAULT_ATR_PERIOD;
                OffsetMultiplier1 = DEFAULT_OFFSET_MULTIPLIER1;
                OffsetMultiplier2 = DEFAULT_OFFSET_MULTIPLIER2;

                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "KeltnerMidline");
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "KeltnerUpperLevel1");
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "KeltnerLowerLevel1");
                AddPlot(new Stroke(Brushes.Transparent, 2), PlotStyle.Line, "KeltnerUpperLevel2");
                AddPlot(new Stroke(Brushes.Transparent, 2), PlotStyle.Line, "KeltnerLowerLevel2");

                SkyBarColoring = DEFAULT_SKY_BAR_COLORING;
                UpBarColor = Brushes.Lime;
                DownBarColor = Brushes.Red;
                MidBarColor = Brushes.DimGray;

                EnableSymbol = DEFAULT_ENABLE_SYMBOL;
                SymbolLColor = Brushes.Lime;
                SymbolSColor = Brushes.Red;
                SymbolDistance = DEFAULT_SYMBOL_DISTANCE;

                RegionDownBrush = Brushes.Red;
                RegionUpBrush = Brushes.DodgerBlue;
                Opacity = DEFAULT_OPACITY;

                StochKPeriod = DEFAULT_STOCH_K_PERIOD;
                StochDPeriod = DEFAULT_STOCH_D_PERIOD;
                StochSmooth = DEFAULT_STOCH_SMOOTH;
                StochasticOverbought = DEFAULT_STOCHASTIC_OVERBOUGHT;
                StochasticOversold = DEFAULT_STOCHASTIC_OVERSOLD;
            }
            else if (State == State.DataLoaded)
            {
                this.SetZOrder(-1);

                if (UseExponentialMovingAverage)
                {
                    emaValue = EMA(Close, MAPeriod);
                }
                else
                {
                    smaValue = SMA(Close, MAPeriod);
                }

                atrValue = ATR(ATRPeriod);

                // Preserved your existing parameter order intentionally.
                stochastics = Stochastics(StochSmooth, StochKPeriod, StochDPeriod);

                minimumBars = GetMinimumBars();
                previousBarZone = BarZone.Mid;

                FreezeAllBrushes();
            }
            else if (State == State.Terminated)
            {
                try
                {
                    emaValue = null;
                    smaValue = null;
                    atrValue = null;
                    stochastics = null;
                }
                catch { }
            }
        }

        protected override void OnBarUpdate()
        {
            // Keep the indicator focused on the primary series.
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < minimumBars)
                return;

            if (atrValue == null)
                return;

            double middle = UseExponentialMovingAverage
                ? (emaValue != null ? emaValue[0] : double.NaN)
                : (smaValue != null ? smaValue[0] : double.NaN);

            double atr = atrValue[0];

            if (double.IsNaN(middle) || double.IsNaN(atr))
                return;

            double offset1 = atr * OffsetMultiplier1;
            double offset2 = atr * OffsetMultiplier2;

            double upperLevel1 = middle + offset1;
            double lowerLevel1 = middle - offset1;
            double upperLevel2 = middle + offset2;
            double lowerLevel2 = middle - offset2;

            Midline[0] = middle;
            UpperLevel1[0] = upperLevel1;
            LowerLevel1[0] = lowerLevel1;
            UpperLevel2[0] = upperLevel2;
            LowerLevel2[0] = lowerLevel2;

            int areaOpacity = Math.Max(0, Math.Min(100, Opacity));
            Brush upperRegionBrush = GetBrush(RegionDownBrush, Brushes.Red);
            Brush lowerRegionBrush = GetBrush(RegionUpBrush, Brushes.DodgerBlue);

            Draw.Region(this, "SkyStoch4Upper", CurrentBar, 0, UpperLevel2, UpperLevel1, null, upperRegionBrush, areaOpacity, 0);
            Draw.Region(this, "SkyStoch4Lower", CurrentBar, 0, LowerLevel2, LowerLevel1, null, lowerRegionBrush, areaOpacity, 0);

            if (SkyBarColoring)
            {
                if (stochastics != null)
                {
                    double stochD = stochastics.D[0];
                    double stochK = stochastics.K[0];

                    if (!double.IsNaN(stochD) && !double.IsNaN(stochK))
                    {
                        UpdateBarColor(stochD, stochK, middle, upperLevel1, lowerLevel1);
                    }
                }
            }

            if (EnableSymbol && CurrentBar >= 1)
            {
                double lowerForPrior = UseStrictSignalBands ? LowerLevel1[1] : LowerLevel1[0];
                double upperForPrior = UseStrictSignalBands ? UpperLevel1[1] : UpperLevel1[0];

                // Long Symbol
                if (!double.IsNaN(lowerForPrior) && !double.IsNaN(LowerLevel1[0]))
                {
                    if ((Low[1] < lowerForPrior) && (Low[0] > LowerLevel1[0]))
                    {
                        Draw.Text(
                            this,
                            SystemName + "Long " + Convert.ToString(CurrentBars[0]),
                            DrawLong,
                            0,
                            Low[0] + (-SymbolDistance * TickSize),
                            GetBrush(SymbolLColor, Brushes.Lime));
                    }
                }

                // Short Symbol
                if (!double.IsNaN(upperForPrior) && !double.IsNaN(UpperLevel1[0]))
                {
                    if ((High[1] > upperForPrior) && (High[0] < UpperLevel1[0]))
                    {
                        Draw.Text(
                            this,
                            SystemName + "Short " + Convert.ToString(CurrentBars[0]),
                            DrawShort,
                            0,
                            High[0] + (SymbolDistance * TickSize),
                            GetBrush(SymbolSColor, Brushes.Red));
                    }
                }
            }
        }

        #region Helpers
        private int GetMinimumBars()
        {
            int required = Math.Max(MAPeriod, ATRPeriod);

            if (SkyBarColoring || EnableLongAlerts || EnableShortAlerts)
            {
                int stochRequired = Math.Max(StochKPeriod, Math.Max(StochDPeriod, StochSmooth)) + 2;
                required = Math.Max(required, stochRequired);
            }

            return Math.Max(1, required);
        }

        private void UpdateBarColor(double stochD, double stochK, double middle, double upperLevel1, double lowerLevel1)
        {
            BarZone currentZone = BarZone.Mid;

            // Preserve original priority: upper band check first.
            if (High[0] > upperLevel1)
            {
                currentZone = BarZone.Down;
            }
            else if (Low[0] < lowerLevel1)
            {
                currentZone = BarZone.Up;
            }

            switch (currentZone)
            {
                case BarZone.Down:
                    BarBrush = GetBrush(DownBarColor, Brushes.Red);
                    CandleOutlineBrush = BarBrush;

                    if (EnableShortAlerts &&
                        stochD >= StochasticOverbought &&
                        stochK >= StochasticOverbought &&
                        previousBarZone != BarZone.Down)
                    {
                        PlayAlertSound(ShortAlertSound);
                    }
                    break;

                case BarZone.Up:
                    BarBrush = GetBrush(UpBarColor, Brushes.Lime);
                    CandleOutlineBrush = BarBrush;

                    if (EnableLongAlerts &&
                        stochD <= StochasticOversold &&
                        stochK <= StochasticOversold &&
                        previousBarZone != BarZone.Up)
                    {
                        PlayAlertSound(LongAlertSound);
                    }
                    break;

                default:
                    BarBrush = GetBrush(MidBarColor, Brushes.DimGray);
                    CandleOutlineBrush = BarBrush;
                    break;
            }

            previousBarZone = currentZone;
        }

        private void PlayAlertSound(string soundFile)
        {
            if (string.IsNullOrWhiteSpace(soundFile))
                return;

            try
            {
                string path = ResolveSoundPath(soundFile);

                if (!string.IsNullOrWhiteSpace(path))
                    PlaySound(path);
                else
                    PlaySound(soundFile);
            }
            catch
            {
                // Intentionally silent. Alert sound failure should not break the indicator.
            }
        }

        private string ResolveSoundPath(string soundFile)
        {
            try
            {
                if (Path.IsPathRooted(soundFile))
                {
                    return File.Exists(soundFile) ? soundFile : string.Empty;
                }

                List<string> candidates = new List<string>();

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrWhiteSpace(baseDir))
                {
                    candidates.Add(Path.Combine(baseDir, "sounds", soundFile));
                }

                string myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrWhiteSpace(myDocs))
                {
                    candidates.Add(Path.Combine(myDocs, "NinjaTrader 8", "sounds", soundFile));
                }

                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrWhiteSpace(programFiles))
                {
                    candidates.Add(Path.Combine(programFiles, "NinjaTrader 8", "sounds", soundFile));
                }

                string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (!string.IsNullOrWhiteSpace(programFilesX86))
                {
                    candidates.Add(Path.Combine(programFilesX86, "NinjaTrader 8", "sounds", soundFile));
                }

                return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private Brush GetBrush(Brush brush, Brush fallback)
        {
            return brush ?? fallback;
        }

        private void FreezeAllBrushes()
        {
            FreezeBrush(UpBarColor);
            FreezeBrush(DownBarColor);
            FreezeBrush(MidBarColor);
            FreezeBrush(RegionDownBrush);
            FreezeBrush(RegionUpBrush);
            FreezeBrush(SymbolLColor);
            FreezeBrush(SymbolSColor);
        }

        private void FreezeBrush(Brush brush)
        {
            try
            {
                if (brush != null && brush.CanFreeze && !brush.IsFrozen)
                {
                    brush.Freeze();
                }
            }
            catch
            {
                // Intentionally ignored. Freezing is only a performance/safety enhancement.
            }
        }
        #endregion

        #region Properties

        #region Core Parameters
        [NinjaScriptProperty]
        [Display(Name = "UseExponentialMovingAverage", GroupName = "1) Core Parameters", Order = 0)]
        public bool UseExponentialMovingAverage
        { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "MAPeriod", GroupName = "1) Core Parameters", Order = 1)]
        public int MAPeriod
        { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "ATRPeriod", GroupName = "1) Core Parameters", Order = 2)]
        public int ATRPeriod
        { get; set; }

        [Range(0.01, int.MaxValue), NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "OffsetMultiplier1", GroupName = "1) Core Parameters", Order = 3)]
        public double OffsetMultiplier1
        { get; set; }

        [Range(0.01, int.MaxValue), NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "OffsetMultiplier2", GroupName = "1) Core Parameters", Order = 4)]
        public double OffsetMultiplier2
        { get; set; }
        #endregion

        #region Stochastics
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stoch K Period", GroupName = "2) Stochastics", Order = 0)]
        public int StochKPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stoch D Period", GroupName = "2) Stochastics", Order = 1)]
        public int StochDPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stoch Smooth", GroupName = "2) Stochastics", Order = 2)]
        public int StochSmooth { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Overbought Level", GroupName = "2) Stochastics", Order = 3)]
        public double StochasticOverbought { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Oversold Level", GroupName = "2) Stochastics", Order = 4)]
        public double StochasticOversold { get; set; }
        #endregion

        #region Visual Settings
        [NinjaScriptProperty]
        [Display(Name = "Enable Coloring", Order = 0, GroupName = "3) Visual Settings")]
        public bool SkyBarColoring { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Up Bar Color", Description = "Color for up bars", Order = 1, GroupName = "3) Visual Settings")]
        [DefaultValue(typeof(Brush), "Lime")]
        public Brush UpBarColor { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Down Bar Color", Description = "Color for down bars", Order = 2, GroupName = "3) Visual Settings")]
        [DefaultValue(typeof(Brush), "Red")]
        public Brush DownBarColor { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Mid Bar Color", Description = "Color for Mid bars", Order = 3, GroupName = "3) Visual Settings")]
        [DefaultValue(typeof(Brush), "DimGray")]
        public Brush MidBarColor { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Upper Band Region", Description = "Brush used for Upper Band Region Color", Order = 4, GroupName = "3) Visual Settings")]
        public Brush RegionDownBrush { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Lower Band Region", Description = "Brush used for Lower Band Region Color", Order = 5, GroupName = "3) Visual Settings")]
        public Brush RegionUpBrush { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Opacity", Order = 6, GroupName = "3) Visual Settings")]
        public int Opacity { get; set; }
        #endregion

        #region Signal Settings
        [NinjaScriptProperty]
        [Display(Name = "Enable Signal", Order = 0, GroupName = "4) Signal Settings")]
        public bool EnableSymbol { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "・ Long Signal", Order = 1, GroupName = "4) Signal Settings")]
        public string DrawLong
        {
            get { return drawLong; }
            set { drawLong = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "・ Short Signal", Order = 2, GroupName = "4) Signal Settings")]
        public string DrawShort
        {
            get { return drawShort; }
            set { drawShort = value; }
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "・ Signal Distance", Order = 3, GroupName = "4) Signal Settings")]
        public int SymbolDistance { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(ResourceType = typeof(Custom.Resource), Name = "・ Signal ▲ Color", GroupName = "4) Signal Settings", Order = 4)]
        [DefaultValue(typeof(Brush), "Lime")]
        public Brush SymbolLColor { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(ResourceType = typeof(Custom.Resource), Name = "・ Signal ▼ Color", GroupName = "4) Signal Settings", Order = 5)]
        [DefaultValue(typeof(Brush), "Red")]
        public Brush SymbolSColor { get; set; }
        #endregion

        #region Alerts
        [NinjaScriptProperty]
        [Display(Name = "Enable Long (Green) Alerts", Order = 0, GroupName = "5) Alerts")]
        public bool EnableLongAlerts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Short (Red) Alerts", Order = 1, GroupName = "5) Alerts")]
        public bool EnableShortAlerts { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.FilePathPicker", Filter = "WAV Files (*.wav)|*.wav")]
        [Display(Name = "Long Alert Sound", Description = "Sound file for long alerts", Order = 2, GroupName = "5) Alerts")]
        public string LongAlertSound
        {
            get { return longAlertSound; }
            set { longAlertSound = value; }
        }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.FilePathPicker", Filter = "WAV Files (*.wav)|*.wav")]
        [Display(Name = "Short Alert Sound", Description = "Sound file for short alerts", Order = 3, GroupName = "5) Alerts")]
        public string ShortAlertSound
        {
            get { return shortAlertSound; }
            set { shortAlertSound = value; }
        }
        #endregion

        #region Series
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> Midline
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> UpperLevel1
        {
            get { return Values[1]; }
        }

        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> LowerLevel1
        {
            get { return Values[2]; }
        }

        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> UpperLevel2
        {
            get { return Values[3]; }
        }

        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> LowerLevel2
        {
            get { return Values[4]; }
        }
        #endregion

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.
namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private Sky.SkyStoch4[] cacheSkyStoch4;

        public Sky.SkyStoch4 SkyStoch4(bool useExponentialMovingAverage, int mAPeriod, int aTRPeriod, double offsetMultiplier1, double offsetMultiplier2, int stochKPeriod, int stochDPeriod, int stochSmooth, double stochasticOverbought, double stochasticOversold, bool skyBarColoring, Brush upBarColor, Brush downBarColor, Brush midBarColor, Brush regionDownBrush, Brush regionUpBrush, int opacity, bool enableSymbol, string drawLong, string drawShort, int symbolDistance, Brush symbolLColor, Brush symbolSColor, bool enableLongAlerts, bool enableShortAlerts, string longAlertSound, string shortAlertSound)
        {
            return SkyStoch4(Input, useExponentialMovingAverage, mAPeriod, aTRPeriod, offsetMultiplier1, offsetMultiplier2, stochKPeriod, stochDPeriod, stochSmooth, stochasticOverbought, stochasticOversold, skyBarColoring, upBarColor, downBarColor, midBarColor, regionDownBrush, regionUpBrush, opacity, enableSymbol, drawLong, drawShort, symbolDistance, symbolLColor, symbolSColor, enableLongAlerts, enableShortAlerts, longAlertSound, shortAlertSound);
        }

        public Sky.SkyStoch4 SkyStoch4(ISeries<double> input, bool useExponentialMovingAverage, int mAPeriod, int aTRPeriod, double offsetMultiplier1, double offsetMultiplier2, int stochKPeriod, int stochDPeriod, int stochSmooth, double stochasticOverbought, double stochasticOversold, bool skyBarColoring, Brush upBarColor, Brush downBarColor, Brush midBarColor, Brush regionDownBrush, Brush regionUpBrush, int opacity, bool enableSymbol, string drawLong, string drawShort, int symbolDistance, Brush symbolLColor, Brush symbolSColor, bool enableLongAlerts, bool enableShortAlerts, string longAlertSound, string shortAlertSound)
        {
            if (cacheSkyStoch4 != null)
                for (int idx = 0; idx < cacheSkyStoch4.Length; idx++)
                    if (cacheSkyStoch4[idx] != null && cacheSkyStoch4[idx].UseExponentialMovingAverage == useExponentialMovingAverage && cacheSkyStoch4[idx].MAPeriod == mAPeriod && cacheSkyStoch4[idx].ATRPeriod == aTRPeriod && cacheSkyStoch4[idx].OffsetMultiplier1 == offsetMultiplier1 && cacheSkyStoch4[idx].OffsetMultiplier2 == offsetMultiplier2 && cacheSkyStoch4[idx].StochKPeriod == stochKPeriod && cacheSkyStoch4[idx].StochDPeriod == stochDPeriod && cacheSkyStoch4[idx].StochSmooth == stochSmooth && cacheSkyStoch4[idx].StochasticOverbought == stochasticOverbought && cacheSkyStoch4[idx].StochasticOversold == stochasticOversold && cacheSkyStoch4[idx].SkyBarColoring == skyBarColoring && cacheSkyStoch4[idx].UpBarColor == upBarColor && cacheSkyStoch4[idx].DownBarColor == downBarColor && cacheSkyStoch4[idx].MidBarColor == midBarColor && cacheSkyStoch4[idx].RegionDownBrush == regionDownBrush && cacheSkyStoch4[idx].RegionUpBrush == regionUpBrush && cacheSkyStoch4[idx].Opacity == opacity && cacheSkyStoch4[idx].EnableSymbol == enableSymbol && cacheSkyStoch4[idx].DrawLong == drawLong && cacheSkyStoch4[idx].DrawShort == drawShort && cacheSkyStoch4[idx].SymbolDistance == symbolDistance && cacheSkyStoch4[idx].SymbolLColor == symbolLColor && cacheSkyStoch4[idx].SymbolSColor == symbolSColor && cacheSkyStoch4[idx].EnableLongAlerts == enableLongAlerts && cacheSkyStoch4[idx].EnableShortAlerts == enableShortAlerts && cacheSkyStoch4[idx].LongAlertSound == longAlertSound && cacheSkyStoch4[idx].ShortAlertSound == shortAlertSound && cacheSkyStoch4[idx].EqualsInput(input))
                        return cacheSkyStoch4[idx];

            return CacheIndicator<Sky.SkyStoch4>(new Sky.SkyStoch4()
            {
                UseExponentialMovingAverage = useExponentialMovingAverage,
                MAPeriod = mAPeriod,
                ATRPeriod = aTRPeriod,
                OffsetMultiplier1 = offsetMultiplier1,
                OffsetMultiplier2 = offsetMultiplier2,
                StochKPeriod = stochKPeriod,
                StochDPeriod = stochDPeriod,
                StochSmooth = stochSmooth,
                StochasticOverbought = stochasticOverbought,
                StochasticOversold = stochasticOversold,
                SkyBarColoring = skyBarColoring,
                UpBarColor = upBarColor,
                DownBarColor = downBarColor,
                MidBarColor = midBarColor,
                RegionDownBrush = regionDownBrush,
                RegionUpBrush = regionUpBrush,
                Opacity = opacity,
                EnableSymbol = enableSymbol,
                DrawLong = drawLong,
                DrawShort = drawShort,
                SymbolDistance = symbolDistance,
                SymbolLColor = symbolLColor,
                SymbolSColor = symbolSColor,
                EnableLongAlerts = enableLongAlerts,
                EnableShortAlerts = enableShortAlerts,
                LongAlertSound = longAlertSound,
                ShortAlertSound = shortAlertSound
            }, input, ref cacheSkyStoch4);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.Sky.SkyStoch4 SkyStoch4(bool useExponentialMovingAverage, int mAPeriod, int aTRPeriod, double offsetMultiplier1, double offsetMultiplier2, int stochKPeriod, int stochDPeriod, int stochSmooth, double stochasticOverbought, double stochasticOversold, bool skyBarColoring, Brush upBarColor, Brush downBarColor, Brush midBarColor, Brush regionDownBrush, Brush regionUpBrush, int opacity, bool enableSymbol, string drawLong, string drawShort, int symbolDistance, Brush symbolLColor, Brush symbolSColor, bool enableLongAlerts, bool enableShortAlerts, string longAlertSound, string shortAlertSound)
        {
            return indicator.SkyStoch4(Input, useExponentialMovingAverage, mAPeriod, aTRPeriod, offsetMultiplier1, offsetMultiplier2, stochKPeriod, stochDPeriod, stochSmooth, stochasticOverbought, stochasticOversold, skyBarColoring, upBarColor, downBarColor, midBarColor, regionDownBrush, regionUpBrush, opacity, enableSymbol, drawLong, drawShort, symbolDistance, symbolLColor, symbolSColor, enableLongAlerts, enableShortAlerts, longAlertSound, shortAlertSound);
        }

        public Indicators.Sky.SkyStoch4 SkyStoch4(ISeries<double> input, bool useExponentialMovingAverage, int mAPeriod, int aTRPeriod, double offsetMultiplier1, double offsetMultiplier2, int stochKPeriod, int stochDPeriod, int stochSmooth, double stochasticOverbought, double stochasticOversold, bool skyBarColoring, Brush upBarColor, Brush downBarColor, Brush midBarColor, Brush regionDownBrush, Brush regionUpBrush, int opacity, bool enableSymbol, string drawLong, string drawShort, int symbolDistance, Brush symbolLColor, Brush symbolSColor, bool enableLongAlerts, bool enableShortAlerts, string longAlertSound, string shortAlertSound)
        {
            return indicator.SkyStoch4(input, useExponentialMovingAverage, mAPeriod, aTRPeriod, offsetMultiplier1, offsetMultiplier2, stochKPeriod, stochDPeriod, stochSmooth, stochasticOverbought, stochasticOversold, skyBarColoring, upBarColor, downBarColor, midBarColor, regionDownBrush, regionUpBrush, opacity, enableSymbol, drawLong, drawShort, symbolDistance, symbolLColor, symbolSColor, enableLongAlerts, enableShortAlerts, longAlertSound, shortAlertSound);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.Sky.SkyStoch4 SkyStoch4(bool useExponentialMovingAverage, int mAPeriod, int aTRPeriod, double offsetMultiplier1, double offsetMultiplier2, int stochKPeriod, int stochDPeriod, int stochSmooth, double stochasticOverbought, double stochasticOversold, bool skyBarColoring, Brush upBarColor, Brush downBarColor, Brush midBarColor, Brush regionDownBrush, Brush regionUpBrush, int opacity, bool enableSymbol, string drawLong, string drawShort, int symbolDistance, Brush symbolLColor, Brush symbolSColor, bool enableLongAlerts, bool enableShortAlerts, string longAlertSound, string shortAlertSound)
        {
            return indicator.SkyStoch4(Input, useExponentialMovingAverage, mAPeriod, aTRPeriod, offsetMultiplier1, offsetMultiplier2, stochKPeriod, stochDPeriod, stochSmooth, stochasticOverbought, stochasticOversold, skyBarColoring, upBarColor, downBarColor, midBarColor, regionDownBrush, regionUpBrush, opacity, enableSymbol, drawLong, drawShort, symbolDistance, symbolLColor, symbolSColor, enableLongAlerts, enableShortAlerts, longAlertSound, shortAlertSound);
        }

        public Indicators.Sky.SkyStoch4 SkyStoch4(ISeries<double> input, bool useExponentialMovingAverage, int mAPeriod, int aTRPeriod, double offsetMultiplier1, double offsetMultiplier2, int stochKPeriod, int stochDPeriod, int stochSmooth, double stochasticOverbought, double stochasticOversold, bool skyBarColoring, Brush upBarColor, Brush downBarColor, Brush midBarColor, Brush regionDownBrush, Brush regionUpBrush, int opacity, bool enableSymbol, string drawLong, string drawShort, int symbolDistance, Brush symbolLColor, Brush symbolSColor, bool enableLongAlerts, bool enableShortAlerts, string longAlertSound, string shortAlertSound)
        {
            return indicator.SkyStoch4(input, useExponentialMovingAverage, mAPeriod, aTRPeriod, offsetMultiplier1, offsetMultiplier2, stochKPeriod, stochDPeriod, stochSmooth, stochasticOverbought, stochasticOversold, skyBarColoring, upBarColor, downBarColor, midBarColor, regionDownBrush, regionUpBrush, opacity, enableSymbol, drawLong, drawShort, symbolDistance, symbolLColor, symbolSColor, enableLongAlerts, enableShortAlerts, longAlertSound, shortAlertSound);
        }
    }
}
#endregion