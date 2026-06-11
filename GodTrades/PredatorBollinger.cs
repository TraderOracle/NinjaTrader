#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class PredatorBollinger : Indicator
	{
		private SMA sma;
		private StdDev stdDev;

		private bool currentLowerTouched;
		private bool currentUpperTouched;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name					= "PredatorBollinger";
				Description				= "Bollinger Bands with real-time data box signals for band touches and middle band close location.";
				Calculate				= Calculate.OnEachTick;
				IsOverlay				= true;
				DisplayInDataBox		= true;
				ShowTransparentPlotsInDataBox= true;
				DrawOnPricePanel		= true;
				PaintPriceMarkers		= true;
				IsAutoScale				= false;
				IsSuspendedWhileInactive = true;

				Period					= 20;
				NumStdDev				= 2.0;

				AddPlot(Brushes.DodgerBlue, "LowerBand");
				AddPlot(Brushes.Goldenrod, "MiddleBand");
				AddPlot(Brushes.DodgerBlue, "UpperBand");

				// Transparent plots show in Data Box without cluttering the chart.
				AddPlot(Brushes.Transparent, "LowerTouchSignal");
				AddPlot(Brushes.Transparent, "UpperTouchSignal");
				AddPlot(Brushes.Transparent, "MiddleCloseSignal");
			}
			else if (State == State.Configure)
			{
				Plots[0].Width = 2;
				Plots[1].Width = 2;
				Plots[2].Width = 2;
			}
			else if (State == State.DataLoaded)
			{
				sma		= SMA(Input, Period);
				stdDev	= StdDev(Input, Period);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < Period - 1)
			{
				LowerBand[0]			= double.NaN;
				MiddleBand[0]			= double.NaN;
				UpperBand[0]			= double.NaN;

				LowerTouchSignal[0]		= 0.0;
				UpperTouchSignal[0]		= 0.0;
				MiddleCloseSignal[0]	= 0.0;
				return;
			}

			if (IsFirstTickOfBar)
			{
				currentLowerTouched = false;
				currentUpperTouched = false;
			}

			double middle	= sma[0];
			double dev		= NumStdDev * stdDev[0];
			double upper	= middle + dev;
			double lower	= middle - dev;

			LowerBand[0]	= lower;
			MiddleBand[0]	= middle;
			UpperBand[0]	= upper;

			// Real-time touch detection.
			// Once touched during the current bar, the signal remains active for that bar.
			if (Low[0] <= lower)
				currentLowerTouched = true;

			if (High[0] >= upper)
				currentUpperTouched = true;

			LowerTouchSignal[0] = currentLowerTouched ? 1.0 : 0.0;
			UpperTouchSignal[0] = currentUpperTouched ? -1.0 : 0.0;

			// Real-time middle-band location.
			if (Close[0] < middle)
				MiddleCloseSignal[0] = 1.0;
			else if (Close[0] > middle)
				MiddleCloseSignal[0] = -1.0;
			else
				MiddleCloseSignal[0] = 0.0;
		}

		#region Properties

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Period", Order = 1, GroupName = "Parameters")]
		public int Period
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "Standard Deviations", Order = 2, GroupName = "Parameters")]
		public double NumStdDev
		{ get; set; }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> LowerBand
		{
			get { return Values[0]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> MiddleBand
		{
			get { return Values[1]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> UpperBand
		{
			get { return Values[2]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> LowerTouchSignal
		{
			get { return Values[3]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> UpperTouchSignal
		{
			get { return Values[4]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> MiddleCloseSignal
		{
			get { return Values[5]; }
		}

		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private PredatorBollinger[] cachePredatorBollinger;
		public PredatorBollinger PredatorBollinger(int period, double numStdDev)
		{
			return PredatorBollinger(Input, period, numStdDev);
		}

		public PredatorBollinger PredatorBollinger(ISeries<double> input, int period, double numStdDev)
		{
			if (cachePredatorBollinger != null)
				for (int idx = 0; idx < cachePredatorBollinger.Length; idx++)
					if (cachePredatorBollinger[idx] != null && cachePredatorBollinger[idx].Period == period && cachePredatorBollinger[idx].NumStdDev == numStdDev && cachePredatorBollinger[idx].EqualsInput(input))
						return cachePredatorBollinger[idx];
			return CacheIndicator<PredatorBollinger>(new PredatorBollinger(){ Period = period, NumStdDev = numStdDev }, input, ref cachePredatorBollinger);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.PredatorBollinger PredatorBollinger(int period, double numStdDev)
		{
			return indicator.PredatorBollinger(Input, period, numStdDev);
		}

		public Indicators.PredatorBollinger PredatorBollinger(ISeries<double> input , int period, double numStdDev)
		{
			return indicator.PredatorBollinger(input, period, numStdDev);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.PredatorBollinger PredatorBollinger(int period, double numStdDev)
		{
			return indicator.PredatorBollinger(Input, period, numStdDev);
		}

		public Indicators.PredatorBollinger PredatorBollinger(ISeries<double> input , int period, double numStdDev)
		{
			return indicator.PredatorBollinger(input, period, numStdDev);
		}
	}
}

#endregion
