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
namespace NinjaTrader.NinjaScript.Indicators.Dimension
{
    public class DeltaPlusv3 : Indicator
    {
        private OrderFlowCumulativeDelta cumulativeDelta;
        private Series<double> valueSeries;
        private Series<double> jmaSeries;
        private Series<double> e0;
        private Series<double> e1;
        private Series<double> e2;
        private Series<double> jm;

        private int BigTradesThreshold = 25000;
        private Brush IntensePositiveBrush = Brushes.PaleGreen;
        private Brush IntenseNegativeBrush = Brushes.LightCoral;
        private Brush PositiveBrush = Brushes.Green;
        private Brush NegativeBrush = Brushes.DarkRed;

        private double barTotal = 0;
        private double barDelta = 0;
        private double barMaxDelta = 0;
        private double barMinDelta = 0;
        private TimeSpan barOpenTime;

        private double phaseratio;
        private double beta;
        private double alpha;

        [NinjaScriptProperty]
        [Display(Name = "Plot Divergences", Order = 1, GroupName = "Parameters")]
        public bool PlotDivergences { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Volume Absorption Background", Order = 2, GroupName = "Parameters")]
        public bool EnableVolumeAbsorptionBackground { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Plots the absolute difference between current and previous bar's cumulative delta. Colors the bar green if positive, red if negative.";
                Name = "DeltaPlusv3";
                Calculate = Calculate.OnEachTick;
                IsOverlay = false;
                AddPlot(new Stroke(PositiveBrush, 8), PlotStyle.Bar, "PositiveDelta");
                AddPlot(new Stroke(NegativeBrush, 8), PlotStyle.Bar, "NegativeDelta");

                PlotDivergences = true;
                EnableVolumeAbsorptionBackground = true;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(Data.BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                cumulativeDelta = OrderFlowCumulativeDelta(CumulativeDeltaType.BidAsk, CumulativeDeltaPeriod.Session, 0);
                valueSeries = new Series<double>(this);
                jmaSeries = new Series<double>(this);
                e0 = new Series<double>(this);
                e1 = new Series<double>(this);
                e2 = new Series<double>(this);
                jm = new Series<double>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || CurrentBar < 3 || cumulativeDelta == null || cumulativeDelta.Count < 4)
                return;

            calculateVolume();

            if (IsFirstTickOfBar)
            {
                barTotal = 0;
                barDelta = 0;
                barMaxDelta = 1;
                barMinDelta = -1;
                barOpenTime = Time[0].TimeOfDay;
            }

            double currentDelta = cumulativeDelta.DeltaClose[0];
            double previousDelta = cumulativeDelta.DeltaClose[1];
            double prevprevDelta = cumulativeDelta.DeltaClose[2];
            double deltaDifference = currentDelta - previousDelta;
            double prevdeltaDifference = previousDelta - prevprevDelta;

            // Calculate delta intensity
            double candleSeconds = Time[0].TimeOfDay.TotalSeconds - barOpenTime.TotalSeconds;
            if (candleSeconds == 0)
                candleSeconds = 1;

            var volPerSecond = barTotal / candleSeconds;
            double deltaPer = barDelta > 0 ? (barDelta / barMaxDelta) : (barDelta / barMinDelta);
            var deltaIntense = Math.Abs((barDelta * deltaPer) * volPerSecond);

            // Plot the absolute difference
            if (deltaDifference >= 0)
            {
                Values[0][0] = Math.Abs(deltaDifference);
                Values[1][0] = 0; // Reset negative plot

                if (deltaIntense > BigTradesThreshold)
                    PlotBrushes[0][0] = IntensePositiveBrush;
                else
                    PlotBrushes[0][0] = PositiveBrush;
            }
            else
            {
                Values[1][0] = Math.Abs(deltaDifference);
                Values[0][0] = 0; // Reset positive plot

                if (deltaIntense > BigTradesThreshold)
                    PlotBrushes[1][0] = IntenseNegativeBrush;
                else
                    PlotBrushes[1][0] = NegativeBrush;
            }

            // Plot divergences on the previous bar
            if (PlotDivergences && IsFirstTickOfBar)
            {
                bool priceUp = Close[0] > Open[0];
                bool deltaDown = deltaDifference < 0;
                bool priceDown = Close[0] < Open[0];
                bool deltaUp = deltaDifference > 0;

                if (priceUp && deltaDown)
                {
                    Draw.TriangleDown(this, "PriceUpDeltaDown" + (CurrentBar - 2), true, 0, High[2] + TickSize, Brushes.Red);
                }
                else if (priceDown && deltaUp)
                {
                    Draw.TriangleUp(this, "PriceDownDeltaUp" + (CurrentBar - 2), true, 0, Low[2] - TickSize, Brushes.Green);
                }
            }

            // Calculate Volume Absorption values
            double vaCurrentDelta = cumulativeDelta.DeltaClose[0];
            double vaPreviousDelta = cumulativeDelta.DeltaClose[1];
            double vaDeltaDifference = vaCurrentDelta - vaPreviousDelta;

            double openCloseDiff = (Close[0] - Open[0]);
            double value = openCloseDiff != 0 ? vaDeltaDifference / openCloseDiff : 0;

            valueSeries[0] = Math.Abs(value);

            // Calculate JMA
            CalculateJMA(valueSeries, 3, 1, 60);

            double jmaValue = jmaSeries[0];

            // Apply background color based on VolumeAbsorption values
            if (EnableVolumeAbsorptionBackground)
            {
                if (value > 0 && valueSeries[0] > jmaValue)
                {
                    BackBrushes[0] = Brushes.LightCoral;
                }
                else if (value < 0 && valueSeries[0] > jmaValue)
                {
                    BackBrushes[0] = Brushes.DarkSeaGreen;
                }
                else
                {
                    BackBrushes[0] = Brushes.Transparent;
                }
            }
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

        private void CalculateJMA(ISeries<double> input, int jurikPhase, int jurikPower, int len)
        {
            if (CurrentBar < len)
                return;

            if (jurikPhase < -100)
                phaseratio = 0.5;
            else if (jurikPhase > 100)
                phaseratio = 2.5;
            else
                phaseratio = (jurikPhase / 100) + 0.5;

            beta = 0.45 * (len - 1) / (0.45 * (len - 1) + 2);
            alpha = Math.Pow(beta, jurikPower);

            e0[0] = (1 - alpha) * input[0] + alpha * (e0[1]);
            e1[0] = (input[0] - e0[0]) * (1 - beta) + beta * (e1[1]);
            e2[0] = (e0[0] + phaseratio * e1[0] - jm[1]) * Math.Pow(1 - alpha, 2) + Math.Pow(alpha, 2) * e2[1];
            jm[0] = e2[0] + jm[1];
            jmaSeries[0] = jm[0];
        }

        #region Properties

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PositiveDelta
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> NegativeDelta
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
		private Dimension.DeltaPlusv3[] cacheDeltaPlusv3;
		public Dimension.DeltaPlusv3 DeltaPlusv3(bool plotDivergences, bool enableVolumeAbsorptionBackground)
		{
			return DeltaPlusv3(Input, plotDivergences, enableVolumeAbsorptionBackground);
		}

		public Dimension.DeltaPlusv3 DeltaPlusv3(ISeries<double> input, bool plotDivergences, bool enableVolumeAbsorptionBackground)
		{
			if (cacheDeltaPlusv3 != null)
				for (int idx = 0; idx < cacheDeltaPlusv3.Length; idx++)
					if (cacheDeltaPlusv3[idx] != null && cacheDeltaPlusv3[idx].PlotDivergences == plotDivergences && cacheDeltaPlusv3[idx].EnableVolumeAbsorptionBackground == enableVolumeAbsorptionBackground && cacheDeltaPlusv3[idx].EqualsInput(input))
						return cacheDeltaPlusv3[idx];
			return CacheIndicator<Dimension.DeltaPlusv3>(new Dimension.DeltaPlusv3(){ PlotDivergences = plotDivergences, EnableVolumeAbsorptionBackground = enableVolumeAbsorptionBackground }, input, ref cacheDeltaPlusv3);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Dimension.DeltaPlusv3 DeltaPlusv3(bool plotDivergences, bool enableVolumeAbsorptionBackground)
		{
			return indicator.DeltaPlusv3(Input, plotDivergences, enableVolumeAbsorptionBackground);
		}

		public Indicators.Dimension.DeltaPlusv3 DeltaPlusv3(ISeries<double> input , bool plotDivergences, bool enableVolumeAbsorptionBackground)
		{
			return indicator.DeltaPlusv3(input, plotDivergences, enableVolumeAbsorptionBackground);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Dimension.DeltaPlusv3 DeltaPlusv3(bool plotDivergences, bool enableVolumeAbsorptionBackground)
		{
			return indicator.DeltaPlusv3(Input, plotDivergences, enableVolumeAbsorptionBackground);
		}

		public Indicators.Dimension.DeltaPlusv3 DeltaPlusv3(ISeries<double> input , bool plotDivergences, bool enableVolumeAbsorptionBackground)
		{
			return indicator.DeltaPlusv3(input, plotDivergences, enableVolumeAbsorptionBackground);
		}
	}
}

#endregion
