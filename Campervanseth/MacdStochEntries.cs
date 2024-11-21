#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using BltTriggerLines.Common;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators.SK
{
    public enum TheMAType { EMA, HMA, SMA, TMA, VMA, WMA, DEMA, TEMA, VWMA, ZLEMA, LinReg }

    public class MACDStoch : Indicator
    {
        #region Variables
        private int fast = 5;
        private int slow = 20;
        private int smooth2 = 30;
        private Series<double> fastEma;
        private Series<double> slowEma;
        private int upDown = 0;

        private int periodD = 3;        // SlowDperiod
        private int periodK = 5;        // Kperiod
        private int smooth = 2;         // SlowKperiod
        private Series<double> den;
        private Series<double> nom;
        private Series<double> fastK;
        private int trendBars = 100;    // HMA Period
        private MAType selectedMAType = MAType.SMA;
       // private Series<double> igreen;
       // private Series<double> ired;

        private Brush backgroundShortBrush = Brushes.Red;
        private Brush backgroundLongBrush = Brushes.Green;
        private int opacity = 25;
        private bool enableBackgroundColor = true;
        private int trend = 0;
        private static readonly int FALLING = -1;
        private static readonly int RISING = 1;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "MACD combined with Stochastic oscillator";
                Name = "MACD+Stoch";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;

                AddPlot(Brushes.Black, "Macd");
                AddPlot(Brushes.Blue, "Avg");
                AddPlot(Brushes.Transparent, "Diff");
                AddPlot(Brushes.Black, "Mom");

                AddPlot(new Stroke(Brushes.Transparent, 4), PlotStyle.Dot, "igreen");
                AddPlot(new Stroke(Brushes.Transparent, 4), PlotStyle.Dot, "ired");

                AddLine(Brushes.Red, 45, "Lower");
                AddLine(Brushes.Red, 55, "Upper");
                AddLine(Brushes.White, 80, "Over");
                AddLine(Brushes.White, 20, "Under");

                GreenBgOpacity = 40;
                RedBgOpacity = 40;
                GreenBgColor = Colors.LightGreen;
                RedBgColor = Colors.Pink;
            }
            else if (State == State.DataLoaded)
            {
                fastEma = new Series<double>(this);
                slowEma = new Series<double>(this);
                den = new Series<double>(this);
                nom = new Series<double>(this);
                fastK = new Series<double>(this);
               
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;

            CalculateMacd();
            CalculateStochastic();

            if (enableBackgroundColor)
                SetBackgroundColor();

            if (CurrentBar >= trendBars)
            {
                DetermineTrend();
                CheckValidLongShort();
				StratSignals();
            }
        }

        private void CalculateMacd()
        {
            double fastMultiplier = 2.0 / (1 + fast);
            double slowMultiplier = 2.0 / (1 + slow);
            double smoothMultiplier = 2.0 / (1 + smooth2);

            fastEma[0] = fastMultiplier * Input[0] + (1 - fastMultiplier) * fastEma[1];
            slowEma[0] = slowMultiplier * Input[0] + (1 - slowMultiplier) * slowEma[1];

            double macd = fastEma[0] - slowEma[0];
            double macdAvg = smoothMultiplier * macd + (1 - smoothMultiplier) * Values[1][1];

            Values[0][0] = macd;
            Values[1][0] = macdAvg;
            Values[2][0] = macd - macdAvg;
            Values[3][0] = macd;  // Momentum (Mom) plot

            SetMacdPlotColors(macd, macdAvg);
        }

        private void SetMacdPlotColors(double macd, double macdAvg)
        {
            if (macd > macdAvg)
                PlotBrushes[0][0] = Brushes.Green;
            else
                PlotBrushes[0][0] = Brushes.Red;

            if (IsRising(Values[1]))
                PlotBrushes[1][0] = Brushes.Green;
            else
                PlotBrushes[1][0] = Brushes.Red;
        }

        private void CalculateStochastic()
        {
            if (CurrentBar < Math.Max(periodK, smooth)) return;

            nom[0] = Close[0] - MIN(Low, periodK)[0];
            den[0] = MAX(High, periodK)[0] - MIN(Low, periodK)[0];
            fastK[0] = den[0] != 0 ? Math.Min(100, Math.Max(0, 100 * nom[0] / den[0])) : 50;

            Values[4][0] = SMA(fastK, smooth)[0];  // K line
            Values[5][0] = SMA(Values[4], periodD)[0];  // D line
        }

        private void SetBackgroundColor()
        {
            double macd = Values[0][0];
            double macdAvg = Values[1][0];

            if (macd > 0 && macdAvg > 0)
                BackBrush = new SolidColorBrush(Color.FromArgb((byte)(GreenBgOpacity * 2.55), GreenBgColor.R, GreenBgColor.G, GreenBgColor.B) );
			
            else if (macd < 0 && macdAvg < 0)
                BackBrush = new SolidColorBrush(Color.FromArgb((byte)(RedBgOpacity * 2.55), RedBgColor.R, RedBgColor.G, RedBgColor.B));
			
            else
                BackBrush = null;
        }

        private void DetermineTrend()
        {
            switch (selectedMAType)
            {
                case MAType.EMA:
                    trend = IsRising(EMA(trendBars)) ? RISING : FALLING;
                    break;
                case MAType.HMA:
                    trend = IsRising(HMA(trendBars)) ? RISING : FALLING;
                    break;
                case MAType.SMA:
                    trend = IsRising(SMA(trendBars)) ? RISING : FALLING;
                    break;
                case MAType.TMA:
                    trend = IsRising(TMA(trendBars)) ? RISING : FALLING;
                    break;
                case MAType.WMA:
                    trend = IsRising(WMA(trendBars)) ? RISING : FALLING;
                    break;
                case MAType.DEMA:
                    trend = IsRising(DEMA(trendBars)) ? RISING : FALLING;
                    break;
                case MAType.TEMA:
                    trend = IsRising(TEMA(trendBars)) ? RISING : FALLING;
                    break;
                case MAType.VWMA:
                    trend = IsRising(VWMA(trendBars)) ? RISING : FALLING;
                    break;
                case MAType.ZLEMA:
                    trend = IsRising(ZLEMA(trendBars)) ? RISING : FALLING;
                    break;
                case MAType.LinReg:
                    trend = IsRising(LinReg(trendBars)) ? RISING : FALLING;
                    break;
            }
        }

        private void CheckValidLongShort()
        {
			double macd = Values[0][0];
            double macdAvg = Values[1][0];
			
            if (trend == FALLING && Values[4][0] <= Values[4][1] && Values[4][1] >= 80 && macd < 0 && macdAvg < 0)
                Draw.ArrowDown(this, "validShort_" + CurrentBar, false, 0, High[0] + 2 * TickSize, Brushes.Pink);
	
            if (trend == FALLING && Values[5][0] <= Values[5][1] && Values[5][1] >= 80 && macd < 0 && macdAvg < 0 )
                Draw.ArrowDown(this, "validShort_" + CurrentBar, false, 0, High[0] + 2 * TickSize, Brushes.Red);
	
			

            if (trend == RISING && Values[4][0] >= Values[4][1] && Values[4][1] <= 20 && macd > 0 && macdAvg > 0)
                Draw.ArrowUp(this, "validLong_" + CurrentBar, false, 0, Low[0] - 2 * TickSize, Brushes.LightGreen);
			
			if (trend == RISING && Values[5][0] >= Values[5][1] && Values[5][1] <= 20 && macd > 0 && macdAvg > 0)
                Draw.ArrowUp(this, "validLong_" + CurrentBar, false, 0, Low[0] - 2 * TickSize, Brushes.DarkGreen);
			   
        }
		
		        private void StratSignals() //Strategy Builder signals
        {
			double macd = Values[0][0];
            double macdAvg = Values[1][0];
			
            if (trend == FALLING && Values[4][0] <= Values[4][1] && Values[4][1] >= 80 && macd < 0 && macdAvg < 0)
               
		     	ired[0] = -1;
			

            if (trend == RISING && Values[4][0] >= Values[4][1] && Values[4][1] <= 20 && macd > 0 && macdAvg > 0)
              
			    igreen[0] = 1;
        }

        #region Properties

        [Range(1, int.MaxValue)]
        [Display(Name = "Fast EMA Period", GroupName = "Parameters", Order = 0)]
        public int Fast
        {
            get => fast;
            set => fast = Math.Max(1, value);
        }

        [Range(1, int.MaxValue)]
        [Display(Name = "Slow EMA Period", GroupName = "Parameters", Order = 1)]
        public int Slow
        {
            get => slow;
            set => slow = Math.Max(1, value);
        }

        [Range(1, int.MaxValue)]
        [Display(Name = "Smooth Period", GroupName = "Parameters", Order = 2)]
        public int Smooth2
        {
            get => smooth2;
            set => smooth2 = Math.Max(1, value);
        }
		
		     [Range(1, int.MaxValue)]
        [Display(Name = "PeriodD", GroupName = "Parameters", Order = 3)]
        public int PeriodD
        {
            get => periodD;
            set => periodD = Math.Max(1, value);
        }

        [Range(1, int.MaxValue)]
        [Display(Name = "PeriodK", GroupName = "Parameters", Order = 4)]
        public int PeriodK
        {
            get => periodK;
            set => periodK = Math.Max(1, value);
        }

        [Range(1, int.MaxValue)]
        [Display(Name = "Stoch Smooth Period", GroupName = "Parameters", Order = 5)]
        public int Smooth
        {
            get => smooth;
            set => smooth = Math.Max(1, value);
        }

        [Range(0, 100)]
        [Display(Name = "Green Background Opacity", GroupName = "Background", Order = 6)]
        public int GreenBgOpacity { get; set; }

        [Range(0, 100)]
        [Display(Name = "Red Background Opacity", GroupName = "Background", Order = 7)]
        public int RedBgOpacity { get; set; }

        [Display(Name = "Green Background Color", GroupName = "Background", Order = 8)]
        public Color GreenBgColor { get; set; }

        [Display(Name = "Red Background Color", GroupName = "Background", Order = 9)]
        public Color RedBgColor { get; set; }
		
		        [NinjaScriptProperty]
        [Display(Name = "Moving Average Type", Order = 10, GroupName = "Trend Parameters")]
        public MAType SelectedMAType
        {
            get { return selectedMAType; }
            set { selectedMAType = value; }
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Trend Bars", Order = 11, GroupName = "Trend Parameters")]
        public int TrendBars
        {
            get { return trendBars; }
            set { trendBars = Math.Max(1, value); }
        }
		
		    [Browsable(false)]
        [XmlIgnore]
        public Series<double> igreen
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> ired
        {
            get { return Values[1]; }
        }


        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private SK.MACDStoch[] cacheMACDStoch;
		public SK.MACDStoch MACDStoch(MAType selectedMAType, int trendBars)
		{
			return MACDStoch(Input, selectedMAType, trendBars);
		}

		public SK.MACDStoch MACDStoch(ISeries<double> input, MAType selectedMAType, int trendBars)
		{
			if (cacheMACDStoch != null)
				for (int idx = 0; idx < cacheMACDStoch.Length; idx++)
					if (cacheMACDStoch[idx] != null && cacheMACDStoch[idx].SelectedMAType == selectedMAType && cacheMACDStoch[idx].TrendBars == trendBars && cacheMACDStoch[idx].EqualsInput(input))
						return cacheMACDStoch[idx];
			return CacheIndicator<SK.MACDStoch>(new SK.MACDStoch(){ SelectedMAType = selectedMAType, TrendBars = trendBars }, input, ref cacheMACDStoch);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.SK.MACDStoch MACDStoch(MAType selectedMAType, int trendBars)
		{
			return indicator.MACDStoch(Input, selectedMAType, trendBars);
		}

		public Indicators.SK.MACDStoch MACDStoch(ISeries<double> input , MAType selectedMAType, int trendBars)
		{
			return indicator.MACDStoch(input, selectedMAType, trendBars);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.SK.MACDStoch MACDStoch(MAType selectedMAType, int trendBars)
		{
			return indicator.MACDStoch(Input, selectedMAType, trendBars);
		}

		public Indicators.SK.MACDStoch MACDStoch(ISeries<double> input , MAType selectedMAType, int trendBars)
		{
			return indicator.MACDStoch(input, selectedMAType, trendBars);
		}
	}
}

#endregion
