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
using System.Runtime.InteropServices;
using System.Drawing;
using Color = System.Drawing.Color;
using MColor = System.Windows.Media.Color;
using MColors = System.Windows.Media.Colors;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{
	public class Ratchet : Indicator
	{
        private int iJunk = 0;

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
            int pbar = CurrentBar;
            double _tick = TickSize;
            double bb_top = 0;
            double bb_bottom = 0;

            ADX x = ADX(10);
            KAMA kama = KAMA(2, 9, 109);
            FisherTransform ft = FisherTransform(10);
            T3 t3 = T3(10, 1, 1);
            ParabolicSAR sar = ParabolicSAR(0.2, 0.2, 2);
            RSI rsi = RSI(14, 0);
            HMA hma = HMA(14);

            var red = Close[0] < Open[0];
            var green = Close[0] > Open[0];
            var c0G = Open[0] < Close[0];
            var c0R = Open[0] > Close[0];
            var c1G = Open[1] < Close[1];
            var c1R = Open[1] > Close[1];
            var c2G = Open[2] < Close[2];
            var c2R = Open[2] > Close[2];
            var c3G = Open[3] < Close[3];
            var c3R = Open[3] > Close[3];
            var c4G = Open[4] < Close[4];
            var c4R = Open[4] > Close[4];

            var c0Body = Math.Abs(Close[0] - Open[0]);
            var c1Body = Math.Abs(Close[1] - Open[1]);
            var c2Body = Math.Abs(Close[2] - Open[2]);
            var c3Body = Math.Abs(Close[3] - Open[3]);
            var c4Body = Math.Abs(Close[4] - Open[4]);

            var upWickLarger = c0R && Math.Abs(High[0] - Open[0]) > Math.Abs(Low[0] - Close[0]);
            var downWickLarger = c0G && Math.Abs(Low[0] - Open[0]) > Math.Abs(Close[0] - High[0]);

            var ThreeOutUp = c2R && c1G && c0G && Open[1] < Close[2] && Open[2] < Close[1] && Math.Abs(Open[1] - Close[1]) > Math.Abs(Open[2] - Close[2]) && Close[0] > Low[1];

            var ThreeOutDown = c2G && c1R && c0R && Open[1] > Close[2] && Open[2] > Close[1] && Math.Abs(Open[1] - Close[1]) > Math.Abs(Open[2] - Close[2]) && Close[0] < Low[1];

            var eqHigh = c0R && c1R && c2G && c3G && (High[1] > bb_top || High[2] > bb_top) && Close[0] < Close[1] && (Open[1] == Close[2] || Open[1] == Close[2] + _tick || Open[1] + _tick == Close[2]);

            var eqLow = c0G && c1G && c2R && c3R && (Low[1] < bb_bottom || Low[2] < bb_bottom) && Close[0] > Close[1] && (Open[1] == Close[2] || Open[1] == Close[2] + _tick || Open[1] + _tick == Close[2]);

            // if (bVolumeImbalances)
            {
                // var highPen = new Pen(new SolidBrush(Color.RebeccaPurple)) { Width = 2 };
                if (green && c1G && Open[0] > Close[1])
                {
                    // HorizontalLinesTillTouch.Add(new LineTillTouch(pbar, Open[0], highPen));
                    // _negWhite[pbar] = candle.Low - (_tick * 2);
                }
                if (red && c1R && Open[0] < Close[1])
                {
                    // HorizontalLinesTillTouch.Add(new LineTillTouch(pbar, Open[0], highPen));
                    // _posWhite[pbar] = candle.High + (_tick * 2);
                }
            }

            //if (bAdvanced)
            {
                if (c4Body > c3Body && c3Body > c2Body && c2Body > c1Body && c1Body > c0Body)
                    if ((Close[0] > Close[1] && Close[1] > Close[2] && Close[2] > Close[3]) ||
                    (Close[0] < Close[1] && Close[1] < Close[2] && Close[2] < Close[3]))
                        iJunk = 0;
                        DrawText(pbar, "Stairs", Color.Yellow, Color.Transparent);
            }

            //if (bShowRevPattern)
            {
/*
                if (c0R && High[0] > bb_top && Open[0] < bb_top && Open[0] > Close[1] && upWickLarger)
                    DrawText(pbar, "Wick", Color.Yellow, Color.Transparent, false, true);
                if (c0G && Lo[0]w < bb_bottom && Open[0] > bb_bottom && Open[0] > Close[1] && downWickLarger)
                    DrawText(pbar, "Wick", Color.Yellow, Color.Transparent, false, true);

                if (c0G && c1R && c2R && VolSec(p1C) > VolSec(p2C) && VolSec(p2C) > VolSec(p3C) && candle.Delta < 0)
                    DrawText(pbar, "Vol\nRev", Color.Yellow, Color.Transparent, false, true);
                if (c0R && c1G && c2G && VolSec(p1C) > VolSec(p2C) && VolSec(p2C) > VolSec(p3C) && candle.Delta > 0)
                    DrawText(pbar, "Vol\nRev", Color.Lime, Color.Transparent, false, true);

                if (ThreeOutUp)
                    DrawText(pbar, "3oU", Color.Yellow, Color.Transparent);
                if (ThreeOutDown && bShowRevPattern)
                    DrawText(pbar, "3oD", Color.Yellow, Color.Transparent);
*/
            }

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

//        private decimal VolSec(Candle c) { return c.Volume / Convert.ToDecimal((c.LastTime - c.Time).TotalSeconds); }

        protected void DrawText(int bBar, String strX, Color cI, Color cB, bool bOverride = false, bool bSwap = false)
        {

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
