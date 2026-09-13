#region Namespaces
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class EMAWickSignals : Indicator
	{
		private EMA ema;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description							= "Plots an EMA and labels bars that wick off it on the upside or downside.";
				Name								= "EMAWickSignals";
				Calculate							= Calculate.OnBarClose;
				IsOverlay							= true;
				DisplayInDataBox					= true;
				DrawOnPricePanel					= true;

				// Default Parameters
				EmaPeriod							= 20;
				BullishSymbol						= "W";
				BearishSymbol						= "W";
				SymbolOffsetTicks					= 4;

				AddPlot(Brushes.DodgerBlue, "EMA Line");
			}
			else if (State == State.DataLoaded)
			{
				ema = EMA(EmaPeriod);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < EmaPeriod)
				return;

			double emaVal = ema[0];
			Value[0] = emaVal;

			// Bullish Wick (Downside Wick): Low touches/dips below EMA, but Close stays above EMA
			bool isBullishWick = (Low[0] <= emaVal) && (Close[0] > emaVal);

			// Bearish Wick (Upside Wick): High touches/surpasses EMA, but Close stays below EMA
			bool isBearishWick = (High[0] >= emaVal) && (Close[0] < emaVal);

			if (isBullishWick)
			{
				Draw.Text(this, "BullWick" + CurrentBar, BullishSymbol, 0, Low[0] - (SymbolOffsetTicks * TickSize), Brushes.Green);
			}

			if (isBearishWick)
			{
				Draw.Text(this, "BearWick" + CurrentBar, BearishSymbol, 0, High[0] + (SymbolOffsetTicks * TickSize), Brushes.Red);
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA Period", GroupName = "Parameters", Order = 1)]
		public int EmaPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Bullish Symbol/Letter", GroupName = "Parameters", Order = 2)]
		public string BullishSymbol { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Bearish Symbol/Letter", GroupName = "Parameters", Order = 3)]
		public string BearishSymbol { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Symbol Offset (Ticks)", GroupName = "Parameters", Order = 4)]
		public int SymbolOffsetTicks { get; set; }
		#endregion
	}
}