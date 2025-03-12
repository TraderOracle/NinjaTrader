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

namespace NinjaTrader.NinjaScript.Indicators
{
    public class FibonacciRetrace : Indicator
    {
        private int period1 = 21;
        private int period2 = 50;
        private double retFac = 0.62;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Period 1", Order = 1, GroupName = "Parameters")]
        public int Period1 { get; set; } = 21;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Period 2", Order = 2, GroupName = "Parameters")]
        public int Period2 { get; set; } = 50;

        [NinjaScriptProperty]
        [Range(0.1, 1)]
        [Display(Name = "Retrace Factor", Order = 3, GroupName = "Parameters")]
        public double RetFac { get; set; } = 0.62;

        [NinjaScriptProperty]
        [Display(Name = "Moving Average Method", Order = 4, GroupName = "Parameters")]
        public string MaMethod { get; set; } = "WMA";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Fibonacci Retrace Indicator based on Moving Average.";
                Name = "FibonacciRetrace";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                AddPlot(Brushes.Blue, "Moving Average");
                AddPlot(Brushes.Green, "Upper Retracement");
                AddPlot(Brushes.Red, "Lower Retracement");
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Period2)
                return;

            double maValue = 0;
            switch (MaMethod.ToUpper())
            {
                case "WMA":
                    maValue = WMA(Close, Period1)[0];
                    break;
                case "EMA":
                    maValue = EMA(Close, Period1)[0];
                    break;
                case "SMA":
                    maValue = SMA(Close, Period1)[0];
                    break;
                default:
                    maValue = WMA(Close, Period1)[0]; // Default to WMA
                    break;
            }

            double highestHigh = MAX(High, Period2)[0];
            double lowestLow = MIN(Low, Period2)[0];
            double retrace = (highestHigh - lowestLow) * RetFac;
            
            Values[0][0] = maValue;
            Values[1][0] = highestHigh - retrace;
            Values[2][0] = lowestLow + retrace;
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private FibonacciRetrace[] cacheFibonacciRetrace;
		public FibonacciRetrace FibonacciRetrace(int period1, int period2, double retFac, string maMethod)
		{
			return FibonacciRetrace(Input, period1, period2, retFac, maMethod);
		}

		public FibonacciRetrace FibonacciRetrace(ISeries<double> input, int period1, int period2, double retFac, string maMethod)
		{
			if (cacheFibonacciRetrace != null)
				for (int idx = 0; idx < cacheFibonacciRetrace.Length; idx++)
					if (cacheFibonacciRetrace[idx] != null && cacheFibonacciRetrace[idx].Period1 == period1 && cacheFibonacciRetrace[idx].Period2 == period2 && cacheFibonacciRetrace[idx].RetFac == retFac && cacheFibonacciRetrace[idx].MaMethod == maMethod && cacheFibonacciRetrace[idx].EqualsInput(input))
						return cacheFibonacciRetrace[idx];
			return CacheIndicator<FibonacciRetrace>(new FibonacciRetrace(){ Period1 = period1, Period2 = period2, RetFac = retFac, MaMethod = maMethod }, input, ref cacheFibonacciRetrace);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.FibonacciRetrace FibonacciRetrace(int period1, int period2, double retFac, string maMethod)
		{
			return indicator.FibonacciRetrace(Input, period1, period2, retFac, maMethod);
		}

		public Indicators.FibonacciRetrace FibonacciRetrace(ISeries<double> input , int period1, int period2, double retFac, string maMethod)
		{
			return indicator.FibonacciRetrace(input, period1, period2, retFac, maMethod);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.FibonacciRetrace FibonacciRetrace(int period1, int period2, double retFac, string maMethod)
		{
			return indicator.FibonacciRetrace(Input, period1, period2, retFac, maMethod);
		}

		public Indicators.FibonacciRetrace FibonacciRetrace(ISeries<double> input , int period1, int period2, double retFac, string maMethod)
		{
			return indicator.FibonacciRetrace(input, period1, period2, retFac, maMethod);
		}
	}
}

#endregion
