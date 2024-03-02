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
	public class Ratchet : Indicator
	{
        // SuperTrend
        private double barHighValue, barLowValue, bottomValue, topValue;
        private Series<double> atrSeries, bottomSeries, topSeries, Default;

        // Linda MACD
        private EMA EMAS;
        private EMA EMAF;
        private double LindaMACD = 0;
        private Series<double> MACD1;
        private Series<double> LMACD;

        // Waddah Explosion
        private int Sensitive = 150;
        private int DeadZonePip = 30;
        private int ExplosionPower = 15;

        protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Indicator here.";
				Name										= "Ratchet";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= true;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= true;
            }
            else if (State == State.Configure)
            {
                MACD1 = new Series<double>(this);
                LMACD = new Series<double>(this);
            }
            else if (State == State.DataLoaded)
            {
                EMAF = EMA(3);
                EMAS = EMA(10);            
            }
        }

		protected override void OnBarUpdate()
		{
            #region WADDAH EXPLOSION

            if (CurrentBars[0] > 2)
            {
                double Trend1, Trend2, Explo1, Explo2, Dead;
                double pwrt, pwre;
                Trend1 = (MACD(20, 40, 9)[0] - MACD(20, 40, 9)[1]) * Sensitive;
                Trend2 = (MACD(20, 40, 9)[2] - MACD(20, 40, 9)[3]) * Sensitive;
                Explo1 = Bollinger(2, 20).Upper[0] - Bollinger(2, 20).Lower[0];
                Explo2 = Bollinger(2, 20).Upper[1] - Bollinger(2, 20).Lower[1];
                Dead = TickSize * DeadZonePip;
                if (Trend1 >= 0) Values[0][0] = Trend1;
                if (Trend1 < 0) Values[1][0] = (-1 * Trend1);
            }

            #endregion

            #region LINDA MACD

            MACD1[0] = EMAF[0] - EMAS[0];
            if (MACD1[0] - EMA(MACD1, 16)[0] > 0)
                LindaMACD = MACD1[0] - EMA(MACD1, 16)[0];
            if (MACD1[0] - EMA(MACD1, 16)[0] <= 0)
                LindaMACD = -1 * (MACD1[0] - EMA(MACD1, 16)[0]);

            #endregion

            #region SUPER TREND

            int ATRMultiplier = 2;
            int ATRPeriod = 10;

            if (IsFirstTickOfBar)
            {
                barHighValue = double.MinValue;
                barLowValue = double.MaxValue;
            }
            barHighValue = (Input is PriceSeries) ? High[0] : Math.Max(barHighValue, Input[0]);
            barLowValue = (Input is PriceSeries) ? Low[0] : Math.Min(barLowValue, Input[0]);
            if (CurrentBar == 0)
                atrSeries[0] = barHighValue - barLowValue;
            else
            {
                double close1 = Input is PriceSeries ? Close[1] : Input[1];
                double trueRange = Math.Max(Math.Abs(barLowValue - close1), Math.Max(barHighValue - barLowValue, Math.Abs(barHighValue - close1)));
                atrSeries[0] = ((Math.Min(CurrentBar + 1, ATRPeriod) - 1) * atrSeries[1] + trueRange) / Math.Min(CurrentBar + 1, ATRPeriod);
            }
            topValue = ((barHighValue + barLowValue) / 2) + (ATRMultiplier * atrSeries[0]);
            bottomValue = ((barHighValue + barLowValue) / 2) - (ATRMultiplier * atrSeries[0]);
            if (CurrentBar > 0)
			{
                topSeries[0] = (topValue < Default[1] || Input[1] > Default[1]) ? topValue : Default[1];
                bottomSeries[0] = (bottomValue > Default[1] || Input[1] < Default[1]) ? bottomValue : Default[1];
                Default[0] = (Default[1] == topSeries[1]) ? ((Input[0] <= topSeries[0]) ? topSeries[0] : bottomSeries[0]) : ((Default[1] == bottomSeries[1]) ? ((Input[0] >= bottomSeries[0]) ? bottomSeries[0] : topSeries[0]) : topSeries[0]);
            }

            #endregion



        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Ratchet[] cacheRatchet;
		public Ratchet Ratchet()
		{
			return Ratchet(Input);
		}

		public Ratchet Ratchet(ISeries<double> input)
		{
			if (cacheRatchet != null)
				for (int idx = 0; idx < cacheRatchet.Length; idx++)
					if (cacheRatchet[idx] != null &&  cacheRatchet[idx].EqualsInput(input))
						return cacheRatchet[idx];
			return CacheIndicator<Ratchet>(new Ratchet(), input, ref cacheRatchet);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Ratchet Ratchet()
		{
			return indicator.Ratchet(Input);
		}

		public Indicators.Ratchet Ratchet(ISeries<double> input )
		{
			return indicator.Ratchet(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Ratchet Ratchet()
		{
			return indicator.Ratchet(Input);
		}

		public Indicators.Ratchet Ratchet(ISeries<double> input )
		{
			return indicator.Ratchet(input);
		}
	}
}

#endregion
