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
#endregion

/// 11/11/2024
/// I wish I could remember where I got this code originally, it appears to be a port of jdehorty's tradingview indicator. 
/// I have made modifications so that it matches the style outlined in the Trader Oracle Method. 

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators.Dimension
{
	public class NadarayaWatsonEnvelopeNR : Indicator
	{
		private Series<double> yhat_close;
		private Series<double> yhat_high;
		private Series<double> yhat_low;
		private Series<double> yhat;
		private Series<double> atr;

		private Brush upColor = Brushes.Red;
        private Brush downColor = Brushes.Green;
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"Nadaraya-Watson Envelope with adjustable lines.";
				Name = "NadarayaWatsonEnvelopeNR";
				Calculate = Calculate.OnEachTick;
				IsOverlay = true;
				DisplayInDataBox = true;
				DrawOnPricePanel = true;
				PaintPriceMarkers = true;
				IsSuspendedWhileInactive = true;

				H = 8;
				Alpha = 8;
				X_0 = 25;
				ATR_Length = 60;
				Near_Factor = 1.5;
				Far_Factor = 8.0;

				// Removed UpperFar and LowerFar lines from plotting
				AddPlot(new Stroke(Brushes.Red, 1), PlotStyle.Line, "UpperAvg");
				AddPlot(new Stroke(Brushes.Red, 1), PlotStyle.Line, "UpperNear");
				AddPlot(new Stroke(Brushes.Green, 1), PlotStyle.Line, "LowerAvg");
				AddPlot(new Stroke(Brushes.Green, 1), PlotStyle.Line, "LowerNear");
				AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "NWEstimate");
			}
			else if (State == State.Configure)
			{
				yhat_close = new Series<double>(this, MaximumBarsLookBack.Infinite);
				yhat_high = new Series<double>(this, MaximumBarsLookBack.Infinite);
				yhat_low = new Series<double>(this, MaximumBarsLookBack.Infinite);
				yhat = new Series<double>(this, MaximumBarsLookBack.Infinite);
				atr = new Series<double>(this, MaximumBarsLookBack.Infinite);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < X_0) return;

			// Envelope Calculations
			yhat_close[0] = kernel_regression(Close, H, Alpha, X_0);
			yhat_high[0] = kernel_regression(High, H, Alpha, X_0);
			yhat_low[0] = kernel_regression(Low, H, Alpha, X_0);
			yhat[0] = yhat_close[0];
			double ktr = kernel_atr(ATR_Length, yhat_high, yhat_low, yhat_close);
			double[] bounds = getBounds(ktr, Near_Factor, Far_Factor, yhat_close);
			
			UpperNear[0] = bounds[0];
			UpperAvg[0] = bounds[2];
			LowerNear[0] = bounds[3];
			LowerAvg[0] = bounds[5];
			NWEstimate[0] = yhat_close[0];
			
			//if (yhat[0] > yhat[1]) PlotBrushes[4][0] = downColor;
			//else PlotBrushes[4][0] = upColor;

			// Draw Regions
			Draw.Region(this, "UpNearAvg", 0, CurrentBar, UpperNear, UpperAvg, Brushes.Red, Brushes.Red, 30, 0);
			Draw.Region(this, "LowNearAvg", 0, CurrentBar, LowerNear, LowerAvg, Brushes.Green, Brushes.Green, 30, 0);
		}
		
		private double[] getBounds(double _atr, double _nearFactor, double _farFactor, Series<double> _yhat)
		{
			double _upper_far = _yhat[0] + _farFactor * _atr;
			double _upper_near = _yhat[0] + _nearFactor * _atr;
			double _lower_near = _yhat[0] - _nearFactor * _atr;
			double _lower_far = _yhat[0] - _farFactor * _atr;
			double _upper_avg = (_upper_far + _upper_near) / 2;
			double _lower_avg = (_lower_far + _lower_near) / 2;
			return new double[] {_upper_near, _upper_far, _upper_avg, _lower_near, _lower_far, _lower_avg};
		}

		private double kernel_atr(int length, ISeries<double> _high, ISeries<double> _low, ISeries<double> _close)
		{
			double trueRange = Math.Max(Math.Max(_high[0] - _low[0], Math.Abs(_high[0] - _close[1])), Math.Abs(_low[0] - _close[1]));
			double smoothingFactor = 2.0 / (length + 1);
			atr[0] = (1 - smoothingFactor) * atr[1] + smoothingFactor * trueRange;
			return atr[0];
		}

		private double kernel_regression(ISeries<double> _src, double _h, double _r, int startAtBar)
		{
			double cumulativeWeight = 0.0;
			double weightedSum = 0.0;

			for (int i = 0; i < startAtBar; i++)
			{
				double weight = Math.Pow(1 + (Math.Pow(i, 2) / (Math.Pow(_h, 2) * 2 * _r)), -_r);
				cumulativeWeight += weight;
				weightedSum += _src[i] * weight;
			}
			return cumulativeWeight != 0 ? weightedSum / cumulativeWeight : 0.0;
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name = "Lookback Window (H)", GroupName = "Parameters", Order = 1)]
		public double H { get; set; }

		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name = "Relative Weighting (Alpha)", GroupName = "Parameters", Order = 2)]
		public double Alpha { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Start Regression at Bar (X_0)", GroupName = "Parameters", Order = 3)]
		public int X_0 { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "ATR Length", GroupName = "Parameters", Order = 4)]
		public int ATR_Length { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Near Factor", GroupName = "Parameters", Order = 5)]
		public double Near_Factor { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Far Factor", GroupName = "Parameters", Order = 6)]
		public double Far_Factor { get; set; }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> UpperAvg => Values[0];

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> UpperNear => Values[1];

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> LowerAvg => Values[2];

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> LowerNear => Values[3];

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> NWEstimate => Values[4];
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Dimension.NadarayaWatsonEnvelopeNR[] cacheNadarayaWatsonEnvelopeNR;
		public Dimension.NadarayaWatsonEnvelopeNR NadarayaWatsonEnvelopeNR(double h, double alpha, int x_0, int aTR_Length, double near_Factor, double far_Factor)
		{
			return NadarayaWatsonEnvelopeNR(Input, h, alpha, x_0, aTR_Length, near_Factor, far_Factor);
		}

		public Dimension.NadarayaWatsonEnvelopeNR NadarayaWatsonEnvelopeNR(ISeries<double> input, double h, double alpha, int x_0, int aTR_Length, double near_Factor, double far_Factor)
		{
			if (cacheNadarayaWatsonEnvelopeNR != null)
				for (int idx = 0; idx < cacheNadarayaWatsonEnvelopeNR.Length; idx++)
					if (cacheNadarayaWatsonEnvelopeNR[idx] != null && cacheNadarayaWatsonEnvelopeNR[idx].H == h && cacheNadarayaWatsonEnvelopeNR[idx].Alpha == alpha && cacheNadarayaWatsonEnvelopeNR[idx].X_0 == x_0 && cacheNadarayaWatsonEnvelopeNR[idx].ATR_Length == aTR_Length && cacheNadarayaWatsonEnvelopeNR[idx].Near_Factor == near_Factor && cacheNadarayaWatsonEnvelopeNR[idx].Far_Factor == far_Factor && cacheNadarayaWatsonEnvelopeNR[idx].EqualsInput(input))
						return cacheNadarayaWatsonEnvelopeNR[idx];
			return CacheIndicator<Dimension.NadarayaWatsonEnvelopeNR>(new Dimension.NadarayaWatsonEnvelopeNR(){ H = h, Alpha = alpha, X_0 = x_0, ATR_Length = aTR_Length, Near_Factor = near_Factor, Far_Factor = far_Factor }, input, ref cacheNadarayaWatsonEnvelopeNR);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Dimension.NadarayaWatsonEnvelopeNR NadarayaWatsonEnvelopeNR(double h, double alpha, int x_0, int aTR_Length, double near_Factor, double far_Factor)
		{
			return indicator.NadarayaWatsonEnvelopeNR(Input, h, alpha, x_0, aTR_Length, near_Factor, far_Factor);
		}

		public Indicators.Dimension.NadarayaWatsonEnvelopeNR NadarayaWatsonEnvelopeNR(ISeries<double> input , double h, double alpha, int x_0, int aTR_Length, double near_Factor, double far_Factor)
		{
			return indicator.NadarayaWatsonEnvelopeNR(input, h, alpha, x_0, aTR_Length, near_Factor, far_Factor);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Dimension.NadarayaWatsonEnvelopeNR NadarayaWatsonEnvelopeNR(double h, double alpha, int x_0, int aTR_Length, double near_Factor, double far_Factor)
		{
			return indicator.NadarayaWatsonEnvelopeNR(Input, h, alpha, x_0, aTR_Length, near_Factor, far_Factor);
		}

		public Indicators.Dimension.NadarayaWatsonEnvelopeNR NadarayaWatsonEnvelopeNR(ISeries<double> input , double h, double alpha, int x_0, int aTR_Length, double near_Factor, double far_Factor)
		{
			return indicator.NadarayaWatsonEnvelopeNR(input, h, alpha, x_0, aTR_Length, near_Factor, far_Factor);
		}
	}
}

#endregion
