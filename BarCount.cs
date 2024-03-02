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
using NinjaTrader.NinjaScript.SuperDomColumns;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{
    [Gui.CategoryOrder("Buy/Sell Filters", 1)]
    [Gui.CategoryOrder("Advanced", 2)]

    public class BarCount : Indicator
    {
        public struct lines
        {
            public string tag;
            public double loc;
        }

        List<lines> ll = new List<lines>();

        private SMA EMAS;
        private SMA EMAF;
        private Series<double> MACD1;
        private Series<double> LMACD;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Displays Bar Count";
                Name = "BarCount";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                //Disable this property if your indicator requires custom values that cumulate with each new market data event. 
                //See Help Guide for additional information.
                IsSuspendedWhileInactive = true;

                VI_Brush = Brushes.MediumPurple;
                Green_Brush = Brushes.Lime;
                Red_Brush = Brushes.Red;

                iMinADX = 0;

                bShowTramp = true;          // SHOW
                bShowMACDPSARArrow = true;
                bShowRegularBuySell = true;
                bVolumeImbalances = true;
                bShowSqueeze = false;
                bShowRevPattern = true;

                bUseFisher = true;          // USE
                bUseWaddah = true;
                bUseT3 = true;
                bUsePSAR = true;
                bUseSuperTrend = true;
                bUseSqueeze = false;
                bUseMACD = false;
                bUseAO = false;
                bUseHMA = false;
            }
            else if (State == State.Configure)
            {
                MACD1 = new Series<double>(this);
                LMACD = new Series<double>(this);
            }
            else if (State == State.DataLoaded)
            {
                EMAF = SMA(3);
                EMAS = SMA(10);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

			if (CurrentBar > 10)
            	TotalBars();

        }

        private void TotalBars()
        {
            var tag = "Total Bars " + CurrentBar;
            double y = 0;

            #region INDICATOR CALCULATIONS

            // Linda MACD
            double lindaMD = 0;
            MACD1[0] = EMAF[0] - EMAS[0];
            lindaMD = MACD1[0] - SMA(MACD1, 16)[0];
            bool macdUp = lindaMD > 0;

            Print("Linda = " + lindaMD.ToString());
            //DrawText(lindaMD.ToString(), Brushes.White);

            double Trend1, Trend2, Explo1, Explo2, Dead;
            Trend1 = (MACD(20, 40, 9)[0] - MACD(20, 40, 9)[1]) * 150;
            Trend2 = (MACD(20, 40, 9)[2] - MACD(20, 40, 9)[3]) * 150;
            Explo1 = Bollinger(2, 20).Upper[0] - Bollinger(2, 20).Lower[0];
            Explo2 = Bollinger(2, 20).Upper[1] - Bollinger(2, 20).Lower[1];
            Dead = TickSize * 30;

            Supertrend st = Supertrend(2, 11);
            bool superUp = st.Value[0] < Low[0] ? true : false;

            FisherTransform ft = FisherTransform(10);
            bool fisherUp = ft.Value[0] > ft.Value[1] ? true : false;

            //WaddahAttarExplosion wae = WaddahAttarExplosion(150, 30, 15, 1, false, 1, false, false, false, false);
            bool wadaUp = Trend1 > 0 ? true : false;

            ParabolicSAR sar = ParabolicSAR(0.02, 0.2, 0.02);
            bool psarUp = sar.Value[0] < Low[0] ? true : false;

            Bollinger bb = Bollinger(2, 20);
            double bb_top = bb.Values[0][0];
            double bb_bottom = bb.Values[2][0];

            HMA hma = HMA(14);
            bool hullUp = hma.Value[0] > hma.Value[1];

            T3 t3 = T3(10, 2, 0.7);
            bool t3Up = Close[0] > t3.Value[0];

            ADX x = ADX(10);
            KAMA kama = KAMA(2, 9, 109);

            RSI rsi = RSI(14, 1);

            bool sqeezeUp = false;

            #endregion

            #region CANDLE CALCULATIONS

            bool bShowDown = true;
            bool bShowUp = true;

            var red = Close[0] < Open[0];
            var green = Close[0] > Open[0];
            if (green)
                y = High[0] + 1 * TickSize;
            else
                y = Low[0] + 1 * TickSize;

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

            var eqHigh = c0R && c1R && c2G && c3G && (High[1] > bb_top || High[2] > bb_top) && Close[0] < Close[1] && (Open[1] == Close[2] || Open[1] == Close[2] + TickSize || Open[1] + TickSize == Close[2]);

            var eqLow = c0G && c1G && c2R && c3R && (Low[1] < bb_bottom || Low[2] < bb_bottom) && Close[0] > Close[1] && (Open[1] == Close[2] || Open[1] == Close[2] + TickSize || Open[1] + TickSize == Close[2]);

            #endregion

            #region VOLUME IMBALANCE

            if (bVolumeImbalances)
            {
                int ix = 0;
                foreach (lines li in ll)
                {
                    if (High[0] > li.loc && Low[0] < li.loc)
                    {
                        //Print(li.tag);
                        int barsAgo = (CurrentBar - Convert.ToInt16(li.tag));
                        //Print(barsAgo);
                        Draw.Line(this, li.tag, 0, li.loc, barsAgo, li.loc, VI_Brush);
                        ll.RemoveAt(ix);
                        break;
                    }
                    ix++;
                }

                if (green && c1G && Open[0] > Close[1])
                {
                    //DrawText("▲", Brushes.Lime, false, true);
                    Draw.Line(this, CurrentBar.ToString(), 1, Open[0], -600, Open[0], VI_Brush);
                    lines li = new lines() { loc = Open[0], tag = CurrentBar.ToString() };
                    ll.Add(li);
                    //Draw.Line(this, tag, true, DateTime.Today.AddDays(-0.4), Open[0], DateTime.Now, Open[0], Brushes.MediumPurple, DashStyleHelper.Dash, 1);
                }
                if (red && c1R && Open[0] < Close[1])
                {
                    //DrawText("▼", Brushes.Orange, false, true);
                    Draw.Line(this, CurrentBar.ToString(), 1, Open[0], -600, Open[0], VI_Brush);
                    lines li = new lines() { loc = Open[0], tag = CurrentBar.ToString() };
                    ll.Add(li);
                }
            }

            #endregion

            // ========================    UP CONDITIONS    ===========================

            if ((!macdUp && bUseMACD) || (!psarUp && bUsePSAR) || (!fisherUp && bUseFisher) || (!t3Up && bUseT3) || (!wadaUp && bUseWaddah) || (!superUp && bUseSuperTrend) || (!sqeezeUp && bUseSqueeze) || x.Value[0] < iMinADX || (bUseHMA && !hullUp))
                bShowUp = false;

            if (green && bShowUp && bShowRegularBuySell)
                DrawText("▲", Green_Brush, false, true);

            // ========================    DOWN CONDITIONS    =========================

            if ((macdUp && bUseMACD) || (psarUp && bUsePSAR) || (fisherUp && bUseFisher) || (t3Up && bUseT3) || (wadaUp && bUseWaddah) || (superUp && bUseSuperTrend) || (sqeezeUp && bUseSqueeze) || x.Value[0] < iMinADX || (bUseHMA && hullUp))
                bShowDown = false;

            if (red && bShowDown && bShowRegularBuySell)
                DrawText("▼", Red_Brush, false, true);

            if (bShowAdvanced)
            {
                if (c4Body > c3Body && c3Body > c2Body && c2Body > c1Body && c1Body > c0Body)
                    if ((Close[0] > Close[1] && Close[1] > Close[2] && Close[2] > Close[3]) ||
                    (Close[0] < Close[1] && Close[1] < Close[2] && Close[2] < Close[3]))
                        DrawText("Stairs", Brushes.Yellow);
                if (eqHigh)
                    DrawText("Eq\nHigh", Brushes.Yellow, false, true);
                if (eqLow)
                    DrawText("Eq\nLow", Brushes.Yellow, false, true);
            }

            if (bShowRevPattern)
            {
                if (c0R && High[0] > bb_top && Open[0] < bb_top && Open[0] > Close[1] && upWickLarger)
                    DrawText("Wick", Brushes.Yellow, false, true);
                if (c0G && Low[0] < bb_bottom && Open[0] > bb_bottom && Open[0] > Close[1] && downWickLarger)
                    DrawText("Wick", Brushes.Yellow, false, true);

                if (ThreeOutUp)
                    DrawText("30U", Brushes.Yellow);
                if (ThreeOutDown)
                    DrawText("30D", Brushes.Yellow);
            }

            if (bShowTramp)
            {
                if (c0R && c1R && Close[0] < Close[1] && (rsi.Value[0] >= 70 || rsi.Value[1] >= 70 || rsi.Value[2] >= 70) &&
                    c2G && High[2] >= (bb_top - (TickSize * 30)))
                    DrawText("TR", Brushes.Yellow, false, true);
                if (c0G && c1G && Close[0] > Close[1] && (rsi.Value[0] < 25 || rsi.Value[1] < 25 || rsi.Value[2] < 25) &&
                    c2R && Low[2] <= (bb_bottom + (TickSize * 30)))
                    DrawText("TR", Brushes.Yellow, false, true);
            }

            //Print(bb.Values[0][0] + " " + bb.Values[1][0] + " " + bb.Values[2][0]);

            //if (Close[0] > hma.Value[0] && false)
                //Draw.Text(this, "Total Bars " + CurrentBar, "Dot", 0, High[0] + 1 * TickSize, TBBrush);
            //Draw.ArrowUp(this, CurrentBar.ToString(), true, DateTime.Now, Low[0] - 1 * TickSize, TBBrush);

        }

        protected void DrawText(String strX, Brush br, bool bOverride = false, bool bSwap = false)
        {
            Brush brFinal;
            double loc = 0;
            int bar = CurrentBar;
            int zero = 0;

            if (strX.Contains("Eq"))
            {
                bar = CurrentBar-1;
                zero = 1;
            }

            if (Close[zero] > Open[zero] || bOverride)
                loc = High[zero] + (TickSize * 7);
            else
                loc = Low[zero] - (TickSize * 7);

            if (Close[zero] > Open[zero] && bSwap)
                loc = Low[zero] - (TickSize * 7);
            else if (Close[zero] < Open[zero] && bSwap)
                loc = High[zero] + (TickSize * 7);

            brFinal = loc == High[zero] + (TickSize * 7) ? Red_Brush :Green_Brush;
            if (strX.Contains("▼") || strX.Contains("▲"))
                brFinal = br;

            Draw.Text(this, "D" + bar, strX, zero, loc, brFinal);
        }

        #region Parameters

        [NinjaScriptProperty]
        [Display(Name = "Waddah Explosion", GroupName = "Buy/Sell Filters", Order = 1)]
        public bool bUseWaddah { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Awesome Oscillator", GroupName = "Buy/Sell Filters", Order = 2)]
        public bool bUseAO { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Parabolic SAR", GroupName = "Buy/Sell Filters", Order = 3)]
        public bool bUsePSAR { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Squeeze Momentum", GroupName = "Buy/Sell Filters", Order = 4)]
        public bool bUseSqueeze { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Linda MACD", GroupName = "Buy/Sell Filters", Order = 5)]
        public bool bUseMACD { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hull Moving Avg", GroupName = "Buy/Sell Filters", Order = 6)]
        public bool bUseHMA { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SuperTrend", GroupName = "Buy/Sell Filters", Order = 7)]
        public bool bUseSuperTrend { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "T3", GroupName = "Buy/Sell Filters", Order = 8)]
        public bool bUseT3 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Fisher Transform", GroupName = "Buy/Sell Filters", Order = 9)]
        public bool bUseFisher { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Minimum ADX", GroupName = "Buy/Sell Filters", Order = 10)]
        public int iMinADX { get; set; }
        

        [NinjaScriptProperty]
        [Display(Name = "Show Regular Buy/Sell Arrow", GroupName = "Advanced", Order = 1)]
        public bool bShowRegularBuySell { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show MACD/PSAR Big Arrow", GroupName = "Advanced", Order = 2)]
        public bool bShowMACDPSARArrow { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Volume Imbalances", GroupName = "Advanced", Order = 3)]
        public bool bVolumeImbalances { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Trampoline", GroupName = "Advanced", Order = 4)]
        public bool bShowTramp { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Squeeze Relaxer", GroupName = "Advanced", Order = 5)]
        public bool bShowSqueeze { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Reversal Patterns", GroupName = "Advanced", Order = 6)]
        public bool bShowRevPattern { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Advanced Signals", GroupName = "Advanced", Order = 7)]
        public bool bShowAdvanced { get; set; }

        [XmlIgnore]
        [Display(Name = "Volume Imbalance Color", GroupName = "Colors", Order = 1)]
        public Brush VI_Brush
        { get; set; }

        [XmlIgnore]
        [Display(Name = "Green Brush Color", GroupName = "Colors", Order = 2)]
        public Brush Green_Brush
        { get; set; }

        [XmlIgnore]
        [Display(Name = "Red Brush Color", GroupName = "Colors", Order = 3)]
        public Brush Red_Brush
        { get; set; }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BarCount[] cacheBarCount;
		public BarCount BarCount(bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowRegularBuySell, bool bShowMACDPSARArrow, bool bVolumeImbalances, bool bShowTramp, bool bShowSqueeze, bool bShowRevPattern, bool bShowAdvanced)
		{
			return BarCount(Input, bUseWaddah, bUseAO, bUsePSAR, bUseSqueeze, bUseMACD, bUseHMA, bUseSuperTrend, bUseT3, bUseFisher, iMinADX, bShowRegularBuySell, bShowMACDPSARArrow, bVolumeImbalances, bShowTramp, bShowSqueeze, bShowRevPattern, bShowAdvanced);
		}

		public BarCount BarCount(ISeries<double> input, bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowRegularBuySell, bool bShowMACDPSARArrow, bool bVolumeImbalances, bool bShowTramp, bool bShowSqueeze, bool bShowRevPattern, bool bShowAdvanced)
		{
			if (cacheBarCount != null)
				for (int idx = 0; idx < cacheBarCount.Length; idx++)
					if (cacheBarCount[idx] != null && cacheBarCount[idx].bUseWaddah == bUseWaddah && cacheBarCount[idx].bUseAO == bUseAO && cacheBarCount[idx].bUsePSAR == bUsePSAR && cacheBarCount[idx].bUseSqueeze == bUseSqueeze && cacheBarCount[idx].bUseMACD == bUseMACD && cacheBarCount[idx].bUseHMA == bUseHMA && cacheBarCount[idx].bUseSuperTrend == bUseSuperTrend && cacheBarCount[idx].bUseT3 == bUseT3 && cacheBarCount[idx].bUseFisher == bUseFisher && cacheBarCount[idx].iMinADX == iMinADX && cacheBarCount[idx].bShowRegularBuySell == bShowRegularBuySell && cacheBarCount[idx].bShowMACDPSARArrow == bShowMACDPSARArrow && cacheBarCount[idx].bVolumeImbalances == bVolumeImbalances && cacheBarCount[idx].bShowTramp == bShowTramp && cacheBarCount[idx].bShowSqueeze == bShowSqueeze && cacheBarCount[idx].bShowRevPattern == bShowRevPattern && cacheBarCount[idx].bShowAdvanced == bShowAdvanced && cacheBarCount[idx].EqualsInput(input))
						return cacheBarCount[idx];
			return CacheIndicator<BarCount>(new BarCount(){ bUseWaddah = bUseWaddah, bUseAO = bUseAO, bUsePSAR = bUsePSAR, bUseSqueeze = bUseSqueeze, bUseMACD = bUseMACD, bUseHMA = bUseHMA, bUseSuperTrend = bUseSuperTrend, bUseT3 = bUseT3, bUseFisher = bUseFisher, iMinADX = iMinADX, bShowRegularBuySell = bShowRegularBuySell, bShowMACDPSARArrow = bShowMACDPSARArrow, bVolumeImbalances = bVolumeImbalances, bShowTramp = bShowTramp, bShowSqueeze = bShowSqueeze, bShowRevPattern = bShowRevPattern, bShowAdvanced = bShowAdvanced }, input, ref cacheBarCount);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BarCount BarCount(bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowRegularBuySell, bool bShowMACDPSARArrow, bool bVolumeImbalances, bool bShowTramp, bool bShowSqueeze, bool bShowRevPattern, bool bShowAdvanced)
		{
			return indicator.BarCount(Input, bUseWaddah, bUseAO, bUsePSAR, bUseSqueeze, bUseMACD, bUseHMA, bUseSuperTrend, bUseT3, bUseFisher, iMinADX, bShowRegularBuySell, bShowMACDPSARArrow, bVolumeImbalances, bShowTramp, bShowSqueeze, bShowRevPattern, bShowAdvanced);
		}

		public Indicators.BarCount BarCount(ISeries<double> input , bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowRegularBuySell, bool bShowMACDPSARArrow, bool bVolumeImbalances, bool bShowTramp, bool bShowSqueeze, bool bShowRevPattern, bool bShowAdvanced)
		{
			return indicator.BarCount(input, bUseWaddah, bUseAO, bUsePSAR, bUseSqueeze, bUseMACD, bUseHMA, bUseSuperTrend, bUseT3, bUseFisher, iMinADX, bShowRegularBuySell, bShowMACDPSARArrow, bVolumeImbalances, bShowTramp, bShowSqueeze, bShowRevPattern, bShowAdvanced);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BarCount BarCount(bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowRegularBuySell, bool bShowMACDPSARArrow, bool bVolumeImbalances, bool bShowTramp, bool bShowSqueeze, bool bShowRevPattern, bool bShowAdvanced)
		{
			return indicator.BarCount(Input, bUseWaddah, bUseAO, bUsePSAR, bUseSqueeze, bUseMACD, bUseHMA, bUseSuperTrend, bUseT3, bUseFisher, iMinADX, bShowRegularBuySell, bShowMACDPSARArrow, bVolumeImbalances, bShowTramp, bShowSqueeze, bShowRevPattern, bShowAdvanced);
		}

		public Indicators.BarCount BarCount(ISeries<double> input , bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowRegularBuySell, bool bShowMACDPSARArrow, bool bVolumeImbalances, bool bShowTramp, bool bShowSqueeze, bool bShowRevPattern, bool bShowAdvanced)
		{
			return indicator.BarCount(input, bUseWaddah, bUseAO, bUsePSAR, bUseSqueeze, bUseMACD, bUseHMA, bUseSuperTrend, bUseT3, bUseFisher, iMinADX, bShowRegularBuySell, bShowMACDPSARArrow, bVolumeImbalances, bShowTramp, bShowSqueeze, bShowRevPattern, bShowAdvanced);
		}
	}
}

#endregion
