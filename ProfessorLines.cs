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

namespace NinjaTrader.NinjaScript.Indicators
{
	public class ProfessorLines : Indicator
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Indicator here.";
				Name										= "ProfessorLines";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= true;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive					= true;
			}
			else if (State == State.Configure)
			{
			}
		}

		private void DrawShit()
		{
			double H10 = 0, L10 = 50000, H2 = 0, L2 = 50000;
			int iLookbackDays = 3;

            if (DateTime.Now.DayOfWeek == DayOfWeek.Monday || DateTime.Now.DayOfWeek == DayOfWeek.Sunday)
                iLookbackDays = 4;

            for (int i = 1; i < 11; i++)
			{
                if (Bars.GetDayBar(i) != null)
				{
					Print("10 and " + i + " " + Bars.GetDayBar(i).High + " " + H10);
                    if (Bars.GetDayBar(i).High > H10)
                        H10 = Bars.GetDayBar(i).High;
                    if (Bars.GetDayBar(i).Low < L10)
                        L10 = Bars.GetDayBar(i).Low;
                }
			}
			
            for (int i = 1; i < iLookbackDays; i++)
			{
                if (Bars.GetDayBar(i) != null)
                {
					Print("2 - " + Bars.GetDayBar(i).High + " " + H2);
                    if (Bars.GetDayBar(i).High > H2)
						H2 = Bars.GetDayBar(i).High;
					if (Bars.GetDayBar(i).Low < L2)
						L2 = Bars.GetDayBar(i).Low;
                }
			}
			
            FibonacciRetracements fib2 = Draw.FibonacciRetracements(this, "tag1", true, 300, L2, 0, H2);
			//fib2.PriceLevels.Clear();
			//fib2.PriceLevels.Add(new PriceLevel(0.00, Brushes.Wheat){ IsVisible = true });
			//fib2.PriceLevels.Add(new PriceLevel(0.236, Brushes.Wheat){ IsVisible = true });
			//fib2.PriceLevels.Add(new PriceLevel(0.382, Brushes.Wheat){ IsVisible = true });
			//fib2.PriceLevels.Add(new PriceLevel(0.5, Brushes.Wheat){ IsVisible = true });
			//fib2.PriceLevels.Add(new PriceLevel(0.618, Brushes.Wheat));
			//fib2.PriceLevels.Add(new PriceLevel(0.786, Brushes.Wheat));
			//fib2.PriceLevels.Add(new PriceLevel(1.00, Brushes.Wheat));
			//fib2.IsExtendedLinesRight = true;
			//fib2.IsVisible = true;
    		
			
            FibonacciRetracements fib10 = Draw.FibonacciRetracements(this, "tag2", true, 400, L10, 0, H10);
			//fib10.PriceLevels.Clear();
			//fib10.PriceLevels.Add(new PriceLevel(0.00, Brushes.Wheat){ IsVisible = true });
			//fib10.PriceLevels.Add(new PriceLevel(0.236, Brushes.Wheat){ IsVisible = true });
			//fib10.PriceLevels.Add(new PriceLevel(0.382, Brushes.Wheat){ IsVisible = true });
			//fib10.PriceLevels.Add(new PriceLevel(0.5, Brushes.Wheat){ IsVisible = true });
			//fib10.PriceLevels.Add(new PriceLevel(0.618, Brushes.Wheat){ IsVisible = true });
			//fib10.PriceLevels.Add(new PriceLevel(0.786, Brushes.Wheat){ IsVisible = true });
			//fib10.PriceLevels.Add(new PriceLevel(1.00, Brushes.Wheat){ IsVisible = true });
			//fib10.IsExtendedLinesRight = true;
        	//fib10.IsVisible = true;
    		
		}
		protected override void OnBarUpdate()
		{
    	if (BarsInProgress != 0)
        return;

    	try
    		{
        		// Ensure bars data is available
        		if (Bars == null || Bars.Count == 0)
        		{
        	 	   Print("No bars data available.");
         	   		return;
       			}

        		// Call DrawShit to update the Fibonacci retracements
        		DrawShit();
    		}
    	catch (Exception ex)
    	{
        Print("Error in OnBarUpdate: " + ex.Message);
    }
}
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ProfessorLines[] cacheProfessorLines;
		public ProfessorLines ProfessorLines()
		{
			return ProfessorLines(Input);
		}

		public ProfessorLines ProfessorLines(ISeries<double> input)
		{
			if (cacheProfessorLines != null)
				for (int idx = 0; idx < cacheProfessorLines.Length; idx++)
					if (cacheProfessorLines[idx] != null &&  cacheProfessorLines[idx].EqualsInput(input))
						return cacheProfessorLines[idx];
			return CacheIndicator<ProfessorLines>(new ProfessorLines(), input, ref cacheProfessorLines);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ProfessorLines ProfessorLines()
		{
			return indicator.ProfessorLines(Input);
		}

		public Indicators.ProfessorLines ProfessorLines(ISeries<double> input )
		{
			return indicator.ProfessorLines(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ProfessorLines ProfessorLines()
		{
			return indicator.ProfessorLines(Input);
		}

		public Indicators.ProfessorLines ProfessorLines(ISeries<double> input )
		{
			return indicator.ProfessorLines(input);
		}
	}
}

#endregion
