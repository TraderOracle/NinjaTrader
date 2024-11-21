#region Using declarations
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Reflection;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using BltTriggerLines.Common;
#endregion

namespace BltTriggerLines.Common
{
    public enum MAType { EMA, HMA, SMA, TMA, VMA, WMA, DEMA, TEMA, VWMA, ZLEMA, LinReg }
    public enum ColorStyle { Transparent, RegionColors, CustomColors, PlotColors }
}

namespace NinjaTrader.NinjaScript.Indicators
{
    [Gui.CategoryOrder("Options", 1000001)]
    [Gui.CategoryOrder("Custom Colors", 1000002)]
    public class BltTriggerLines : Indicator
    {
        #region Globals

        private MAType                  triggerMAType                   = MAType.LinReg;
        private int                     triggerPeriod                   = 80;
        private MAType                  averageMAType                   = MAType.EMA;
        private int                     averagePeriod                   = 20;

        private Brush                   triggerRisingColor              = Brushes.Lime;
        private Brush                   triggerFallingColor             = Brushes.Yellow;
        private Brush                   averageRisingColor              = Brushes.Cyan;
        private Brush                   averageFallingColor             = Brushes.Red;

        private bool                    drawArrows                      = false;
        private int                     arrowOffset                     = 30;
        private Brush                   arrowUpColor                    = Brushes.Blue;
        private Brush                   arrowDownColor                  = Brushes.Red;
        private bool                    ArrowDrawn                      = false;

        private bool                    colorRegion                     = false;
        private ColorStyle              colorLines                      = ColorStyle.RegionColors;
        private int                     regionOpacity                   = 30;
        private Brush                   regionUpColor                   = Brushes.Cyan;
        private Brush                   regionDownColor                 = Brushes.Red;
        private int                     StartIndex                      = 1;
        private int                     PriorIndex                      = 0;

        private bool                    soundAlert                      = false;
        private string                  soundFile                       = "Alert4.wav";
        private bool                    SoundPlayed                     = false;

        private Series<bool>            upTrend                         = null;
        private int                     MinBarsNeeded                   = 1;
        private double                  ArrowTickOffset                 = 0;

        #endregion

        /* --------------------------------------------------------------------------------------------------- */

        private void Initialize()
        {
            AddPlot(new Stroke(Brushes.Green, 2), PlotStyle.Line, "Trigger");
            AddPlot(new Stroke(Brushes.Red, 2), PlotStyle.Line, "Average");

            IsOverlay = true;
            ArePlotsConfigurable = true;
            PaintPriceMarkers = false;
            Calculate = Calculate.OnEachTick;

            upTrend = new Series<bool>(this);
        }

        /* --------------------------------------------------------------------------------------------------- */

        protected override void OnStateChange()
        {
            switch (State)
            {
                case State.SetDefaults:
                    Name = "BltTriggerLines";
                    Description = "Trigger Lines";
                    Initialize();
                    break;

                case State.DataLoaded:
                    OnStartUp();
                    break;
             }
        }

        /* --------------------------------------------------------------------------------------------------- */

        public override string DisplayName
        {
            get { return string.Format("{0}({1},{2},{3},{4})", this.Name,
                    TriggerMAType, TriggerPeriod, AverageMAType, AveragePeriod); }
        }

        /* --------------------------------------------------------------------------------------------------- */

        private void OnStartUp()
        {
            MinBarsNeeded = Math.Max(MinBarsNeeded, TriggerPeriod+AveragePeriod);

            ArrowTickOffset = ArrowOffset * TickSize;
        }

        /* --------------------------------------------------------------------------------------------------- */

        protected override void OnBarUpdate()
        {
            if ((Calculate == Calculate.OnBarClose) || IsFirstTickOfBar)
            {
                ArrowDrawn = false;
                SoundPlayed = false;
            }

            /* ------- */

            switch (TriggerMAType)
            {
                case MAType.EMA: Trigger[0] = EMA(Input, TriggerPeriod)[0]; break;
                case MAType.HMA: Trigger[0] = HMA(Input, TriggerPeriod)[0]; break;
                case MAType.SMA: Trigger[0] = SMA(Input, TriggerPeriod)[0]; break;
                case MAType.TMA: Trigger[0] = TMA(Input, TriggerPeriod)[0]; break;
                case MAType.WMA: Trigger[0] = WMA(Input, TriggerPeriod)[0]; break;
                case MAType.DEMA: Trigger[0] = DEMA(Input, TriggerPeriod)[0]; break;
                case MAType.TEMA: Trigger[0] = TEMA(Input, TriggerPeriod)[0]; break;
                case MAType.VWMA: Trigger[0] = VWMA(Input, TriggerPeriod)[0]; break;
                case MAType.ZLEMA: Trigger[0] = ZLEMA(Input, TriggerPeriod)[0]; break;
                case MAType.LinReg: Trigger[0] = LinReg(Input, TriggerPeriod)[0]; break;
            }

            switch (AverageMAType)
            {
                case MAType.EMA: Average[0] = EMA(Trigger, AveragePeriod)[0]; break;
                case MAType.HMA: Average[0] = HMA(Trigger, AveragePeriod)[0]; break;
                case MAType.SMA: Average[0] = SMA(Trigger, AveragePeriod)[0]; break;
                case MAType.TMA: Average[0] = TMA(Trigger, AveragePeriod)[0]; break;
                case MAType.WMA: Average[0] = WMA(Trigger, AveragePeriod)[0]; break;
                case MAType.DEMA: Average[0] = DEMA(Trigger, AveragePeriod)[0]; break;
                case MAType.TEMA: Average[0] = TEMA(Trigger, AveragePeriod)[0]; break;
                case MAType.VWMA: Average[0] = VWMA(Trigger, AveragePeriod)[0]; break;
                case MAType.ZLEMA: Average[0] = ZLEMA(Trigger, AveragePeriod)[0]; break;
                case MAType.LinReg: Average[0] = LinReg(Trigger, AveragePeriod)[0]; break;
            }

            /* ------- */

            if (CurrentBar < MinBarsNeeded)
                return;

            /* ------- */

            if (ColorLines == ColorStyle.Transparent)
            {
                PlotBrushes[0][-Displacement] = Brushes.Transparent;
                PlotBrushes[1][-Displacement] = Brushes.Transparent;
            }

            else if (ColorLines == ColorStyle.CustomColors)
            {
                Brush TriggerColor = IsRising(Trigger) ? TriggerRisingColor : TriggerFallingColor;
                Brush AverageColor = IsRising(Average) ? AverageRisingColor : AverageFallingColor;

                if (IsBrushEqual(TriggerColor, Brushes.Transparent)) TriggerColor = Plots[0].Brush;
                if (IsBrushEqual(AverageColor, Brushes.Transparent)) AverageColor = Plots[1].Brush;

                PlotBrushes[0][-Displacement] = TriggerColor;
                PlotBrushes[1][-Displacement] = AverageColor;
            }

            /* ------- */

            if (CrossAbove(Trigger, Average, 1))
            {
                if (DrawArrows)
                {
                    Draw.ArrowUp(this, "Arrow"+CurrentBar, false, 0, Average[0]-ArrowTickOffset, ArrowUpColor);
                    ArrowDrawn = true;
                }
                if (SoundAlert && !SoundPlayed)
                {
                    PlaySound(NinjaTrader.Core.Globals.InstallDir+"\\sounds\\"+SoundFile);
                    SoundPlayed = true;
                }
            }
            else if (CrossBelow(Trigger, Average, 1))
            {
                if (DrawArrows)
                {
                    Draw.ArrowDown(this, "Arrow"+CurrentBar, false, 0, Average[0]+ArrowTickOffset, ArrowDownColor);
                    ArrowDrawn = true;
                }
                if (SoundAlert && !SoundPlayed)
                {
                    PlaySound(NinjaTrader.Core.Globals.InstallDir+"\\sounds\\"+SoundFile);
                    SoundPlayed = true;
                }
            }
            else
            {
                if (ArrowDrawn)
                {
                    RemoveDrawObject("Arrow"+CurrentBar);
                    ArrowDrawn = false;
                }
            }

            /* ------- */

            if (Trigger[0] > Average[0])
            {
                if (ColorLines == ColorStyle.RegionColors)
                {
                    PlotBrushes[0][-Displacement] = UpTrend[1] ? RegionUpColor : RegionDownColor;
                    PlotBrushes[1][-Displacement] = UpTrend[1] ? RegionUpColor : RegionDownColor;
                }
                if (ColorRegion && RegionOpacity != 0)
                {
                    if (IsFirstTickOfBar)
                        PriorIndex = StartIndex;
                    int CountBars = CurrentBar - PriorIndex + 1 - Displacement;
                    if (UpTrend[1])
                    {
                        if (StartIndex == CurrentBar)
                            RemoveDrawObject("Region"+CurrentBar);
                        if (CountBars <= CurrentBar)
                            Draw.Region(this, "Region"+PriorIndex, CountBars, -Displacement, Trigger, Average, null, RegionUpColor, RegionOpacity);
                        StartIndex = PriorIndex;
                    }
                    else
                    {
                        if (CountBars <= CurrentBar && StartIndex == PriorIndex)
                            Draw.Region(this, "Region"+PriorIndex, CountBars, 1-Displacement, Trigger, Average, null, RegionDownColor, RegionOpacity);
                        Draw.Region(this, "Region"+CurrentBar, 1-Displacement, -Displacement, Trigger, Average, null, RegionUpColor, RegionOpacity);
                        StartIndex = CurrentBar;
                    }
                }
                UpTrend[0] = true;
            }
            else if (Trigger[0] < Average[0])
            {
                if (ColorLines == ColorStyle.RegionColors)
                {
                    PlotBrushes[0][-Displacement] = UpTrend[1] ? RegionUpColor : RegionDownColor;
                    PlotBrushes[1][-Displacement] = UpTrend[1] ? RegionUpColor : RegionDownColor;
                }
                if (ColorRegion && RegionOpacity != 0)
                {
                    if (IsFirstTickOfBar)
                        PriorIndex = StartIndex;
                    int CountBars = CurrentBar - PriorIndex + 1 - Displacement;
                    if (!UpTrend[1])
                    {
                        if (StartIndex == CurrentBar)
                            RemoveDrawObject("Region"+CurrentBar);
                        if (CountBars <= CurrentBar)
                            Draw.Region(this, "Region"+PriorIndex, CurrentBar-PriorIndex+1-Displacement, -Displacement, Trigger, Average, null, RegionDownColor, RegionOpacity);
                        StartIndex = PriorIndex;
                    }
                    else
                    {
                        if (CountBars <= CurrentBar && StartIndex == PriorIndex)
                            Draw.Region(this, "Region"+PriorIndex, CurrentBar-PriorIndex+1-Displacement, 1-Displacement, Trigger, Average, null, RegionUpColor, RegionOpacity);
                        Draw.Region(this, "Region"+CurrentBar, 1-Displacement, -Displacement, Trigger, Average, null, RegionDownColor, RegionOpacity);
                        StartIndex = CurrentBar;
                    }
                }
                UpTrend[0] = false;
            }
            else
            {
                if (ColorLines == ColorStyle.RegionColors)
                {
                    PlotBrushes[0][-Displacement] = UpTrend[1] ? RegionUpColor : RegionDownColor;
                    PlotBrushes[1][-Displacement] = UpTrend[1] ? RegionUpColor : RegionDownColor;
                }
                if (ColorRegion && RegionOpacity != 0)
                {
                    if (IsFirstTickOfBar)
                        PriorIndex = StartIndex;
                    int CountBars = CurrentBar - PriorIndex + 1 - Displacement;
                    if (StartIndex == CurrentBar)
                        RemoveDrawObject("Region"+CurrentBar);
                    if (CountBars <= CurrentBar)
                        Draw.Region(this, "Region"+PriorIndex, CountBars, -Displacement, Trigger, Average, null, UpTrend[1] ? RegionUpColor : RegionDownColor, RegionOpacity);
                    StartIndex = PriorIndex;
                }
                UpTrend[0] = UpTrend[1];
            }
        }

        /* --------------------------------------------------------------------------------------------------- */

        public static bool IsBrushEqual(Brush aBrush1, Brush aBrush2)
        {
            if (aBrush1.GetType() != aBrush2.GetType())
                return false;
            else if (aBrush1 is SolidColorBrush)
            {
                return (aBrush1 as SolidColorBrush).Color == (aBrush2 as SolidColorBrush).Color &&
                       (aBrush1 as SolidColorBrush).Opacity == (aBrush2 as SolidColorBrush).Opacity;
            }
            else
                throw new NotImplementedException("Not implemented: "+aBrush1.GetType().ToString());
        }

        /* --------------------------------------------------------------------------------------------------- */

        #region Property Grid

        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> Trigger
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> Average
        {
            get { return Values[1]; }
        }

        [Browsable(false)]
        [XmlIgnore()]
        public Series<bool> UpTrend
        {
            get { return upTrend; }
        }

        /* --------------------------------------------------------------------------------------------------- */

        [NinjaScriptProperty]
        [Display(Name = "TriggerMAType", GroupName = "Parameters", Order = 1)]
        public MAType TriggerMAType
        {
            get { return triggerMAType; }
            set { triggerMAType = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "TriggerPeriod", GroupName = "Parameters", Order = 2)]
        public int TriggerPeriod
        {
            get { return triggerPeriod; }
            set { triggerPeriod = Math.Max(1, value); }
        }

        /* ------- */

        [NinjaScriptProperty]
        [Display(Name = "AverageMAType", GroupName = "Parameters", Order = 3)]
        public MAType AverageMAType
        {
            get { return averageMAType; }
            set { averageMAType = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "AveragePeriod", GroupName = "Parameters", Order = 4)]
        public int AveragePeriod
        {
            get { return averagePeriod; }
            set { averagePeriod = Math.Max(1, value); }
        }

        /* --------------------------------------------------------------------------------------------------- */

        [NinjaScriptProperty]
        [Display(Name = "ColorLines", GroupName = "Options", Order = 1)]
        public ColorStyle ColorLines
        {
            get { return colorLines; }
            set { colorLines = value; }
        }

        /* ------- */

        [NinjaScriptProperty]
        [Display(Name = "DrawArrows", GroupName = "Options", Order = 2)]
        public bool DrawArrows
        {
            get { return drawArrows; }
            set { drawArrows = value; }
        }

        /* ------- */

        [NinjaScriptProperty]
        [Display(Name = "ArrowOffset", GroupName = "Options", Order = 3)]
        public int ArrowOffset
        {
            get { return arrowOffset; }
            set { arrowOffset = Math.Max(1, value); }
        }

        /* ------- */

        [XmlIgnore()]
        [NinjaScriptProperty]
        [Display(Name = "ArrowUpColor", GroupName = "Options", Order = 4)]
        public Brush ArrowUpColor
        {
            get { return arrowUpColor; }
            set { arrowUpColor = value; }
        }

        [Browsable(false)]
        public string ArrowUpColorSerialize
        {
            get { return Serialize.BrushToString(arrowUpColor); }
            set { arrowUpColor = Serialize.StringToBrush(value); }
        }

        /* ------- */

        [XmlIgnore()]
        [NinjaScriptProperty]
        [Display(Name = "ArrowDownColor", GroupName = "Options", Order = 5)]
        public Brush ArrowDownColor
        {
            get { return arrowDownColor; }
            set { arrowDownColor = value; }
        }

        [Browsable(false)]
        public string ArrowDownColorSerialize
        {
            get { return Serialize.BrushToString(arrowDownColor); }
            set { arrowDownColor = Serialize.StringToBrush(value); }
        }

        /* ------- */

        [NinjaScriptProperty]
        [Display(Name = "ColorRegion", GroupName = "Options", Order = 6)]
        public bool ColorRegion
        {
            get { return colorRegion; }
            set { colorRegion = value; }
        }

        /* ------- */

        [NinjaScriptProperty]
        [Display(Name = "RegionOpacity", GroupName = "Options", Order = 7)]
        public int RegionOpacity
        {
            get { return regionOpacity; }
            set { regionOpacity = Math.Min(100, Math.Max(0, value)); }
        }

        /* ------- */

        [XmlIgnore()]
        [NinjaScriptProperty]
        [Display(Name = "RegionUpColor", GroupName = "Options", Order = 8)]
        public Brush RegionUpColor
        {
            get { return regionUpColor; }
            set { regionUpColor = value; }
        }

        [Browsable(false)]
        public string RegionUpColorSerialize
        {
            get { return Serialize.BrushToString(regionUpColor); }
            set { regionUpColor = Serialize.StringToBrush(value); }
        }

        /* ------- */

        [XmlIgnore()]
        [NinjaScriptProperty]
        [Display(Name = "RegionDownColor", GroupName = "Options", Order = 9)]
        public Brush RegionDownColor
        {
            get { return regionDownColor; }
            set { regionDownColor = value; }
        }

        [Browsable(false)]
        public string RegionDownColorSerialize
        {
            get { return Serialize.BrushToString(regionDownColor); }
            set { regionDownColor = Serialize.StringToBrush(value); }
        }

        /* ------- */

        [NinjaScriptProperty]
        [Display(Name = "SoundAlert", GroupName = "Options", Order = 10)]
        public bool SoundAlert
        {
            get { return soundAlert; }
            set { soundAlert = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "SoundFile", Description = "Sound filename for Crossover alert - it must exist in your Sounds folder in order to be played", GroupName = "Options", Order = 11)]
        public string SoundFile
        {
            get { return soundFile; }
            set { soundFile = value; }
        }

        /* --------------------------------------------------------------------------------------------------- */

        [XmlIgnore()]
        [NinjaScriptProperty]
        [Display(Name = "TriggerRisingColor", GroupName = "Custom Colors", Order = 1)]
        public Brush TriggerRisingColor
        {
            get { return triggerRisingColor; }
            set { triggerRisingColor = value; }
        }

        [Browsable(false)]
        public string TriggerRisingColorSerialize
        {
            get { return Serialize.BrushToString(triggerRisingColor); }
            set { triggerRisingColor = Serialize.StringToBrush(value); }
        }

        /* ------- */

        [XmlIgnore()]
        [NinjaScriptProperty]
        [Display(Name = "TriggerFallingColor", GroupName = "Custom Colors", Order = 2)]
        public Brush TriggerFallingColor
        {
            get { return triggerFallingColor; }
            set { triggerFallingColor = value; }
        }

        [Browsable(false)]
        public string TriggerFallingColorSerialize
        {
            get { return Serialize.BrushToString(triggerFallingColor); }
            set { triggerFallingColor = Serialize.StringToBrush(value); }
        }

        /* ------- */

        [XmlIgnore()]
        [NinjaScriptProperty]
        [Display(Name = "AverageRisingColor", GroupName = "Custom Colors", Order = 3)]
        public Brush AverageRisingColor
        {
            get { return averageRisingColor; }
            set { averageRisingColor = value; }
        }

        [Browsable(false)]
        public string AverageRisingColorSerialize
        {
            get { return Serialize.BrushToString(averageRisingColor); }
            set { averageRisingColor = Serialize.StringToBrush(value); }
        }

        /* ------- */

        [XmlIgnore()]
        [NinjaScriptProperty]
        [Display(Name = "AverageFallingColor", GroupName = "Custom Colors", Order = 4)]
        public Brush AverageFallingColor
        {
            get { return averageFallingColor; }
            set { averageFallingColor = value; }
        }

        [Browsable(false)]
        public string AverageColorSerialize
        {
            get { return Serialize.BrushToString(averageFallingColor); }
            set { averageFallingColor = Serialize.StringToBrush(value); }
        }

        #endregion

    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BltTriggerLines[] cacheBltTriggerLines;
		public BltTriggerLines BltTriggerLines(MAType triggerMAType, int triggerPeriod, MAType averageMAType, int averagePeriod, ColorStyle colorLines, bool drawArrows, int arrowOffset, Brush arrowUpColor, Brush arrowDownColor, bool colorRegion, int regionOpacity, Brush regionUpColor, Brush regionDownColor, bool soundAlert, string soundFile, Brush triggerRisingColor, Brush triggerFallingColor, Brush averageRisingColor, Brush averageFallingColor)
		{
			return BltTriggerLines(Input, triggerMAType, triggerPeriod, averageMAType, averagePeriod, colorLines, drawArrows, arrowOffset, arrowUpColor, arrowDownColor, colorRegion, regionOpacity, regionUpColor, regionDownColor, soundAlert, soundFile, triggerRisingColor, triggerFallingColor, averageRisingColor, averageFallingColor);
		}

		public BltTriggerLines BltTriggerLines(ISeries<double> input, MAType triggerMAType, int triggerPeriod, MAType averageMAType, int averagePeriod, ColorStyle colorLines, bool drawArrows, int arrowOffset, Brush arrowUpColor, Brush arrowDownColor, bool colorRegion, int regionOpacity, Brush regionUpColor, Brush regionDownColor, bool soundAlert, string soundFile, Brush triggerRisingColor, Brush triggerFallingColor, Brush averageRisingColor, Brush averageFallingColor)
		{
			if (cacheBltTriggerLines != null)
				for (int idx = 0; idx < cacheBltTriggerLines.Length; idx++)
					if (cacheBltTriggerLines[idx] != null && cacheBltTriggerLines[idx].TriggerMAType == triggerMAType && cacheBltTriggerLines[idx].TriggerPeriod == triggerPeriod && cacheBltTriggerLines[idx].AverageMAType == averageMAType && cacheBltTriggerLines[idx].AveragePeriod == averagePeriod && cacheBltTriggerLines[idx].ColorLines == colorLines && cacheBltTriggerLines[idx].DrawArrows == drawArrows && cacheBltTriggerLines[idx].ArrowOffset == arrowOffset && cacheBltTriggerLines[idx].ArrowUpColor == arrowUpColor && cacheBltTriggerLines[idx].ArrowDownColor == arrowDownColor && cacheBltTriggerLines[idx].ColorRegion == colorRegion && cacheBltTriggerLines[idx].RegionOpacity == regionOpacity && cacheBltTriggerLines[idx].RegionUpColor == regionUpColor && cacheBltTriggerLines[idx].RegionDownColor == regionDownColor && cacheBltTriggerLines[idx].SoundAlert == soundAlert && cacheBltTriggerLines[idx].SoundFile == soundFile && cacheBltTriggerLines[idx].TriggerRisingColor == triggerRisingColor && cacheBltTriggerLines[idx].TriggerFallingColor == triggerFallingColor && cacheBltTriggerLines[idx].AverageRisingColor == averageRisingColor && cacheBltTriggerLines[idx].AverageFallingColor == averageFallingColor && cacheBltTriggerLines[idx].EqualsInput(input))
						return cacheBltTriggerLines[idx];
			return CacheIndicator<BltTriggerLines>(new BltTriggerLines(){ TriggerMAType = triggerMAType, TriggerPeriod = triggerPeriod, AverageMAType = averageMAType, AveragePeriod = averagePeriod, ColorLines = colorLines, DrawArrows = drawArrows, ArrowOffset = arrowOffset, ArrowUpColor = arrowUpColor, ArrowDownColor = arrowDownColor, ColorRegion = colorRegion, RegionOpacity = regionOpacity, RegionUpColor = regionUpColor, RegionDownColor = regionDownColor, SoundAlert = soundAlert, SoundFile = soundFile, TriggerRisingColor = triggerRisingColor, TriggerFallingColor = triggerFallingColor, AverageRisingColor = averageRisingColor, AverageFallingColor = averageFallingColor }, input, ref cacheBltTriggerLines);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BltTriggerLines BltTriggerLines(MAType triggerMAType, int triggerPeriod, MAType averageMAType, int averagePeriod, ColorStyle colorLines, bool drawArrows, int arrowOffset, Brush arrowUpColor, Brush arrowDownColor, bool colorRegion, int regionOpacity, Brush regionUpColor, Brush regionDownColor, bool soundAlert, string soundFile, Brush triggerRisingColor, Brush triggerFallingColor, Brush averageRisingColor, Brush averageFallingColor)
		{
			return indicator.BltTriggerLines(Input, triggerMAType, triggerPeriod, averageMAType, averagePeriod, colorLines, drawArrows, arrowOffset, arrowUpColor, arrowDownColor, colorRegion, regionOpacity, regionUpColor, regionDownColor, soundAlert, soundFile, triggerRisingColor, triggerFallingColor, averageRisingColor, averageFallingColor);
		}

		public Indicators.BltTriggerLines BltTriggerLines(ISeries<double> input , MAType triggerMAType, int triggerPeriod, MAType averageMAType, int averagePeriod, ColorStyle colorLines, bool drawArrows, int arrowOffset, Brush arrowUpColor, Brush arrowDownColor, bool colorRegion, int regionOpacity, Brush regionUpColor, Brush regionDownColor, bool soundAlert, string soundFile, Brush triggerRisingColor, Brush triggerFallingColor, Brush averageRisingColor, Brush averageFallingColor)
		{
			return indicator.BltTriggerLines(input, triggerMAType, triggerPeriod, averageMAType, averagePeriod, colorLines, drawArrows, arrowOffset, arrowUpColor, arrowDownColor, colorRegion, regionOpacity, regionUpColor, regionDownColor, soundAlert, soundFile, triggerRisingColor, triggerFallingColor, averageRisingColor, averageFallingColor);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BltTriggerLines BltTriggerLines(MAType triggerMAType, int triggerPeriod, MAType averageMAType, int averagePeriod, ColorStyle colorLines, bool drawArrows, int arrowOffset, Brush arrowUpColor, Brush arrowDownColor, bool colorRegion, int regionOpacity, Brush regionUpColor, Brush regionDownColor, bool soundAlert, string soundFile, Brush triggerRisingColor, Brush triggerFallingColor, Brush averageRisingColor, Brush averageFallingColor)
		{
			return indicator.BltTriggerLines(Input, triggerMAType, triggerPeriod, averageMAType, averagePeriod, colorLines, drawArrows, arrowOffset, arrowUpColor, arrowDownColor, colorRegion, regionOpacity, regionUpColor, regionDownColor, soundAlert, soundFile, triggerRisingColor, triggerFallingColor, averageRisingColor, averageFallingColor);
		}

		public Indicators.BltTriggerLines BltTriggerLines(ISeries<double> input , MAType triggerMAType, int triggerPeriod, MAType averageMAType, int averagePeriod, ColorStyle colorLines, bool drawArrows, int arrowOffset, Brush arrowUpColor, Brush arrowDownColor, bool colorRegion, int regionOpacity, Brush regionUpColor, Brush regionDownColor, bool soundAlert, string soundFile, Brush triggerRisingColor, Brush triggerFallingColor, Brush averageRisingColor, Brush averageFallingColor)
		{
			return indicator.BltTriggerLines(input, triggerMAType, triggerPeriod, averageMAType, averagePeriod, colorLines, drawArrows, arrowOffset, arrowUpColor, arrowDownColor, colorRegion, regionOpacity, regionUpColor, regionDownColor, soundAlert, soundFile, triggerRisingColor, triggerFallingColor, averageRisingColor, averageFallingColor);
		}
	}
}

#endregion
