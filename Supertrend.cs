//
// Copyright (C) 2023, NinjaTrader LLC <www.ninjatrader.com>
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component
// Coded by NinjaTrader_ChelseaB
//
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

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{
	public class Supertrend : Indicator
	{
		private double				barHighValue, barLowValue, bottomValue, topValue;
		private Series<double>		atrSeries, bottomSeries, topSeries;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description		= @"Supertrend indicator as detailed in the July 2023 Technical Analysis Stocks and Commodities article Stay On Track With by Barbara Star, PhD.";
				Name			= "Supertrend";
				Calculate		= Calculate.OnBarClose;
				IsOverlay		= true;

				ATRMultiplier	= 3;
				ATRPeriod		= 10;

				AddPlot(Brushes.Firebrick, "Supertrend");
			}
			else if (State == State.DataLoaded)
			{
				topSeries		= new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				bottomSeries	= new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				atrSeries		= new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);

				if (!(Input is PriceSeries) && (!Bars.IsTickReplay || Calculate == Calculate.OnBarClose))
					Log("Supertrend: Enable TickReplay with Calculate.OnPriceChange/.OnEachTick when using another indicator as the input series. (To collect the intra-bar high and low in historical)", LogLevel.Warning);
			}
		}

		protected override void OnBarUpdate()
		{
			if (IsFirstTickOfBar)
			{
				barHighValue	= double.MinValue;
				barLowValue		= double.MaxValue;
			}

			// calculate the bar high and low when another indicator is input series 
			barHighValue	= (Input is PriceSeries) ? High[0] : Math.Max(barHighValue, Input[0]);
			barLowValue		= (Input is PriceSeries) ? Low[0] : Math.Min(barLowValue, Input[0]);
			
			// calculate the ATR, but allowing for another indicator as an input series
			if (CurrentBar == 0)
				atrSeries[0]		= barHighValue - barLowValue;
			else
			{
				double close1		= Input is PriceSeries ? Close[1] : Input[1];
				double trueRange	= Math.Max(Math.Abs(barLowValue - close1), Math.Max(barHighValue - barLowValue, Math.Abs(barHighValue - close1)));
				atrSeries[0]		= ((Math.Min(CurrentBar + 1, ATRPeriod) - 1 ) * atrSeries[1] + trueRange) / Math.Min(CurrentBar + 1, ATRPeriod);
			}

			//	dis:= a2 * ATR(a1);
			//	bTop:= MP() + dis;
			//	bBot:= MP() - dis;
			topValue		= ((barHighValue + barLowValue) / 2) + (ATRMultiplier * atrSeries[0]);
			bottomValue		= ((barHighValue + barLowValue) / 2) - (ATRMultiplier * atrSeries[0]);

			if (CurrentBar < 1)
				return;

			//	Top:= If((bTop < PREV) OR(Ref(C, -1)) > PREV, bTop, PREV);
			//	Bot:= If((bBot > PREV) OR(Ref(C, -1)) < PREV, bBot, PREV);
			topSeries[0]	= (topValue < Default[1] || Input[1] > Default[1]) ? topValue : Default[1];
			bottomSeries[0]	= (bottomValue > Default[1] || Input[1] < Default[1]) ? bottomValue : Default[1];

			//	If(PREV = Ref(top, -1), If(C <= Top, Top, Bot), If(PREV = Ref(bot, -1), If(C >= Bot, Bot, Top), top));
			Default[0]		= (Default[1] == topSeries[1]) ? ((Input[0] <= topSeries[0]) ? topSeries[0] : bottomSeries[0]) : ((Default[1] == bottomSeries[1]) ? ((Input[0] >= bottomSeries[0]) ? bottomSeries[0] : topSeries[0]) : topSeries[0]);
		}

		#region Properties
		//		a2:= Input(�Multiplier for ATR�, 1, 10, 3) ;
		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name="ATRMultiplier", Description="Multiplier of the ATR distance", Order=1, GroupName="Parameters")]
		public int ATRMultiplier
		{ get; set; }
	
		//		a1:= Input(�Number of periods in ATR�, 2, 100, 7);
		[NinjaScriptProperty]
		[Range(2, 100)]
		[Display(Name="ATRPeriod", Description="Period supplied to ATR calculation", Order=2, GroupName="Parameters")]
		public int ATRPeriod
		{ get; set; }

		[XmlIgnore]
		[Browsable(false)]
		public Series<double> Default
		{
			get { return Values[0]; }
		}
		#endregion

	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Supertrend[] cacheSupertrend;
		public Supertrend Supertrend(int aTRMultiplier, int aTRPeriod)
		{
			return Supertrend(Input, aTRMultiplier, aTRPeriod);
		}

		public Supertrend Supertrend(ISeries<double> input, int aTRMultiplier, int aTRPeriod)
		{
			if (cacheSupertrend != null)
				for (int idx = 0; idx < cacheSupertrend.Length; idx++)
					if (cacheSupertrend[idx] != null && cacheSupertrend[idx].ATRMultiplier == aTRMultiplier && cacheSupertrend[idx].ATRPeriod == aTRPeriod && cacheSupertrend[idx].EqualsInput(input))
						return cacheSupertrend[idx];
			return CacheIndicator<Supertrend>(new Supertrend(){ ATRMultiplier = aTRMultiplier, ATRPeriod = aTRPeriod }, input, ref cacheSupertrend);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Supertrend Supertrend(int aTRMultiplier, int aTRPeriod)
		{
			return indicator.Supertrend(Input, aTRMultiplier, aTRPeriod);
		}

		public Indicators.Supertrend Supertrend(ISeries<double> input , int aTRMultiplier, int aTRPeriod)
		{
			return indicator.Supertrend(input, aTRMultiplier, aTRPeriod);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Supertrend Supertrend(int aTRMultiplier, int aTRPeriod)
		{
			return indicator.Supertrend(Input, aTRMultiplier, aTRPeriod);
		}

		public Indicators.Supertrend Supertrend(ISeries<double> input , int aTRMultiplier, int aTRPeriod)
		{
			return indicator.Supertrend(input, aTRMultiplier, aTRPeriod);
		}
	}
}

#endregion
