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
using System.Security.Cryptography;
using NinjaTrader.NinjaScript.Strategies;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{
	public class DeltaIntensity : Indicator
	{

		private int BigTradesThreshold = 25000;
		private Brush IntensePositiveBrush = Brushes.LimeGreen;
        private Brush IntenseNegativeBrush = Brushes.Red;
        private Brush PositiveBrush = Brushes.DarkGreen;
        private Brush NegativeBrush = Brushes.DarkRed;

        // Candle volume
        private double barTotal = 0;
		private double barDelta = 0;
		private double barMaxDelta = 0;
		private double barMinDelta = 0;
		private TimeSpan barOpenTime;

        protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"TraderOracle delta intensity, ported from ATAS";
				Name										= "DeltaIntensity";
				Calculate									= Calculate.OnEachTick;
				IsOverlay									= false;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= true;

                AddPlot(new Stroke(Brushes.DarkGreen, 4), PlotStyle.Bar, "Delta");
				Plots[0].AutoWidth = true;
            }
			else if (State == State.Configure)
			{
				AddDataSeries(BarsPeriodType.Tick, 1);
            }
			else if (State == State.DataLoaded)
			{

            }
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress == 1)
			{
                calculateVolume();
				return;
            }
			if (IsFirstTickOfBar)
			{
                barTotal = 0;
				barDelta = 0;
				barMaxDelta = 1;
				barMinDelta = -1;
				barOpenTime = Time[0].TimeOfDay;
			}

			if (CurrentBars[0] < 2 || CurrentBars[1] < 2) return;
			
			double candleSeconds = Time[0].TimeOfDay.TotalSeconds - barOpenTime.TotalSeconds;
            if (candleSeconds == 0)
                candleSeconds = 1;
 
            var volPerSecond = barTotal / candleSeconds;

			double deltaPer = barDelta > 0 ? (barDelta / barMaxDelta) : (barDelta / barMinDelta);

            var deltaIntense = Math.Abs((barDelta * deltaPer) * volPerSecond);

			double deltaShaved = barDelta * deltaPer;

			Value[0] = Math.Abs(deltaShaved);

			if (barDelta > 0)
			{
				if (deltaIntense > BigTradesThreshold)
					PlotBrushes[0][0] = IntensePositiveBrush;
				else
					PlotBrushes[0][0] = PositiveBrush;
			} else
			{
                if (deltaIntense > BigTradesThreshold)
                    PlotBrushes[0][0] = IntenseNegativeBrush;
                else
                    PlotBrushes[0][0] = NegativeBrush;
            }

   //         if (barDelta > 0 && isRedCandle())
			//	_DeltaDivergenceRed[0] = 500;
			//if (barDelta < 0 && isGreenCandle())
			//	_DeltaDivergenceGreen[0] = 500;
		}


        private void calculateVolume()
		{
            bool useCurrentBar = State == State.Historical;
            int whatBar = useCurrentBar ? CurrentBars[1] : Math.Min(CurrentBars[1] + 1, BarsArray[1].Count - 1);

            long v = BarsArray[1].GetVolume(whatBar);
			double price = BarsArray[1].GetClose(whatBar);
			double ask = BarsArray[1].GetAsk(whatBar);
			double bid = BarsArray[1].GetBid(whatBar);

            barTotal += v;
            if (price >= ask)
				barDelta += v;
			else if (price <= bid)
				barDelta -= v;
			barMaxDelta = Math.Max(barMaxDelta, barDelta);
			barMinDelta = Math.Min(barMinDelta, barDelta);
		}

        private bool isRedCandle()
		{
			return Close[0] < Open[0];
		}

		private bool isGreenCandle()
		{
			return Open[0] > Close[0];
		}
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private DeltaIntensity[] cacheDeltaIntensity;
		public DeltaIntensity DeltaIntensity()
		{
			return DeltaIntensity(Input);
		}

		public DeltaIntensity DeltaIntensity(ISeries<double> input)
		{
			if (cacheDeltaIntensity != null)
				for (int idx = 0; idx < cacheDeltaIntensity.Length; idx++)
					if (cacheDeltaIntensity[idx] != null &&  cacheDeltaIntensity[idx].EqualsInput(input))
						return cacheDeltaIntensity[idx];
			return CacheIndicator<DeltaIntensity>(new DeltaIntensity(), input, ref cacheDeltaIntensity);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.DeltaIntensity DeltaIntensity()
		{
			return indicator.DeltaIntensity(Input);
		}

		public Indicators.DeltaIntensity DeltaIntensity(ISeries<double> input )
		{
			return indicator.DeltaIntensity(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.DeltaIntensity DeltaIntensity()
		{
			return indicator.DeltaIntensity(Input);
		}

		public Indicators.DeltaIntensity DeltaIntensity(ISeries<double> input )
		{
			return indicator.DeltaIntensity(input);
		}
	}
}

#endregion
