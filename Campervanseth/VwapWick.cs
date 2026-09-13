#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class VWAP : Indicator
	{
		private double cumVolume = 0;
		private double cumTypicalVolume = 0;
		
		// Variables to track completed bars for intra-bar per-tick calculations
		private double prevCumVolume = 0;
		private double prevCumTypicalVolume = 0;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description							= @"Simple Volume Weighted Average Price supporting Per-Tick Calculation";
				Name								= "VWAP";
				Calculate							= Calculate.OnPriceChange; // Supports real-time updates without corruption
				IsOverlay							= true;
				DisplayInDataBox					= true;
				DrawOnPricePanel					= true;
				DrawHorizontalGridLines				= true;
				DrawVerticalGridLines				= true;
				PaintPriceMarkers					= true;
				ScaleJustification					= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive			= true;

				// Wick Settings Defaults
				EnableWickSignals					= true;
				BullishSymbol						= "W";
				BearishSymbol						= "W";
				SymbolOffsetTicks					= 4;

				AddPlot(new Stroke(Brushes.Gold, 2), PlotStyle.Line, "VWAP");
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 0)
				return;

			// Store completed bar totals when a new bar starts
			if (IsFirstTickOfBar)
			{
				if (Bars.IsFirstBarOfSession)
				{
					prevCumVolume = 0;
					prevCumTypicalVolume = 0;
				}
				else
				{
					prevCumVolume = cumVolume;
					prevCumTypicalVolume = cumTypicalVolume;
				}
			}

			double hl3 = (High[0] + Low[0] + Close[0]) / 3.0;

			// Add current bar's volume and price-volume to previous session totals
			cumVolume = prevCumVolume + Volume[0];
			cumTypicalVolume = prevCumTypicalVolume + (Volume[0] * hl3);

			double vwapVal = cumVolume > 0 ? cumTypicalVolume / cumVolume : hl3;
			VWAPPlot[0] = vwapVal;

			// Plot wick signals
			if (EnableWickSignals)
			{
				bool isBullishWick = (Low[0] <= vwapVal) && (Close[0] > vwapVal);
				bool isBearishWick = (High[0] >= vwapVal) && (Close[0] < vwapVal);

				if (isBullishWick)
				{
					Draw.Text(this, "BullWick" + CurrentBar, BullishSymbol, 0, Low[0] - (SymbolOffsetTicks * TickSize), Brushes.Green);
				}

				if (isBearishWick)
				{
					Draw.Text(this, "BearWick" + CurrentBar, BearishSymbol, 0, High[0] + (SymbolOffsetTicks * TickSize), Brushes.Red);
				}
			}
		}

		#region Properties

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> VWAPPlot
		{
			get { return Values[0]; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Enable Wick Signals", GroupName = "Wick Settings", Order = 1)]
		public bool EnableWickSignals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Bullish Symbol/Letter", GroupName = "Wick Settings", Order = 2)]
		public string BullishSymbol { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Bearish Symbol/Letter", GroupName = "Wick Settings", Order = 3)]
		public string BearishSymbol { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Symbol Offset (Ticks)", GroupName = "Wick Settings", Order = 4)]
		public int SymbolOffsetTicks { get; set; }

		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private VWAP[] cacheVWAP;
		public VWAP VWAP(bool enableWickSignals, string bullishSymbol, string bearishSymbol, int symbolOffsetTicks)
		{
			return VWAP(Input, enableWickSignals, bullishSymbol, bearishSymbol, symbolOffsetTicks);
		}

		public VWAP VWAP(ISeries<double> input, bool enableWickSignals, string bullishSymbol, string bearishSymbol, int symbolOffsetTicks)
		{
			if (cacheVWAP != null)
				for (int idx = 0; idx < cacheVWAP.Length; idx++)
					if (cacheVWAP[idx] != null && cacheVWAP[idx].EnableWickSignals == enableWickSignals && cacheVWAP[idx].BullishSymbol == bullishSymbol && cacheVWAP[idx].BearishSymbol == bearishSymbol && cacheVWAP[idx].SymbolOffsetTicks == symbolOffsetTicks && cacheVWAP[idx].EqualsInput(input))
						return cacheVWAP[idx];
			return CacheIndicator<VWAP>(new VWAP() { EnableWickSignals = enableWickSignals, BullishSymbol = bullishSymbol, BearishSymbol = bearishSymbol, SymbolOffsetTicks = symbolOffsetTicks }, input, ref cacheVWAP);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.VWAP VWAP(bool enableWickSignals, string bullishSymbol, string bearishSymbol, int symbolOffsetTicks)
		{
			return indicator.VWAP(Input, enableWickSignals, bullishSymbol, bearishSymbol, symbolOffsetTicks);
		}

		public Indicators.VWAP VWAP(ISeries<double> input, bool enableWickSignals, string bullishSymbol, string bearishSymbol, int symbolOffsetTicks)
		{
			return indicator.VWAP(input, enableWickSignals, bearishSymbol, symbolOffsetTicks);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.VWAP VWAP(bool enableWickSignals, string bullishSymbol, string bearishSymbol, int symbolOffsetTicks)
		{
			return indicator.VWAP(Input, enableWickSignals, bullishSymbol, bearishSymbol, symbolOffsetTicks);
		}

		public Indicators.VWAP VWAP(ISeries<double> input, bool enableWickSignals, string bullishSymbol, string bearishSymbol, int symbolOffsetTicks)
		{
			return indicator.VWAP(input, enableWickSignals, bearishSymbol, symbolOffsetTicks);
		}
	}
}

#endregion