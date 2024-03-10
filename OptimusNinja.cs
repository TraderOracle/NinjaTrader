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
using System.IO;
using System.Net;
using System.Xml.Linq;
using System.Runtime.Remoting.Contexts;
using System.Windows.Media.TextFormatting;
using System.Windows.Markup;
using Infragistics.Windows.DataPresenter;
using NinjaTrader.NinjaScript.MarketAnalyzerColumns;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;
using NinjaTrader.Adapter;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;
using System.Windows.Forms;

#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    [Gui.CategoryOrder("Buy/Sell Filters", 1)]
    [Gui.CategoryOrder("Advanced", 2)]
    [Gui.CategoryOrder("Colors", 3)]

    public class Optimus : Indicator
    {
        private string sVersion = "1.22";

        #region VARIABLES

        public struct lines
        {
            public string tag;
            public double loc;
        }

        List<lines> ll = new List<lines>();

        private bool sqRelaxUp = false;

        private SMA EMAS, EMAF;
        private Series<double> MACD1, sqzData, SqueezeDef, AO, atrSeries, bottomSeries, topSeries;

        private bool bBigArrowUp = false;
        private bool bDefibCalculated = false;
        private string PrevEvil = string.Empty;

        private double barHighValue, barLowValue, bottomValue, topValue;
        private int ATRMultiplier = 2;
        private int ATRPeriod = 11;

        private string sndBuy = NinjaTrader.Core.Globals.InstallDir + @"\sounds\Buy.wav";
        private string sndSell = NinjaTrader.Core.Globals.InstallDir + @"\sounds\Sell.wav";
        private string sndImb = NinjaTrader.Core.Globals.InstallDir + @"\sounds\Imbalance.wav";

        #endregion

        #region STATE CHANGES

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Displays Bar Count";
                Name = "OptimusNinja";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                //Disable this property if your indicator requires custom values that cumulate with each new market data event. 
                //See Help Guide for additional information.
                IsSuspendedWhileInactive = true;

                VI_Brush = Brushes.MediumPurple;
                Green_Brush = Brushes.Lime;
                Red_Brush = Brushes.Red;

                iMinADX = 0;
                iTextSize = 9;
                iDotSize = 12;
                iTickOffset = 7;
                sEmailAddress = "username@gmail.com";

                bLindaCandle = false;
                bWaddahCandle = false;
                iWaddahIntense = 160;
                iWaddahBuffer = 80;
                iLindaIntense = 40;

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

                bPlaySounds = false;
                bSendEmail = false;
                bShowEvilTimes = false;
                myVersion = "(c) 2024 by TraderOracle, version " + sVersion;
            }
            else if (State == State.Configure)
            {
                MACD1 = new Series<double>(this);
                sqzData = new Series<double>(this);
                SqueezeDef = new Series<double>(this);
                AO = new Series<double>(this);
            }
            else if (State == State.DataLoaded)
            {
                EMAF = SMA(3);
                EMAS = SMA(10);
                topSeries = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
                bottomSeries = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
                atrSeries = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
            }
        }

        #endregion

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

			if (CurrentBar > 25)
            	TotalBars();
        }

        private void TotalBars()
        {
            var tag = "Total Bars " + CurrentBar;
            double y = 0;

            //if (ToTime(Time[0]) < ToTime(9, 00, 00) && (Bars.IsLastBarOfSession))
            //    Defibillator();

            #region INDICATOR CALCULATIONS

            // SUPER TREND
            if (IsFirstTickOfBar)
            {
                barHighValue = double.MinValue;
                barLowValue = double.MaxValue;
            }

            barHighValue = High[0];
            barLowValue = Low[0];

            if (CurrentBar == 0)
                atrSeries[0] = High[0] - Low[0];
            else
            {
                double close1 = Input is PriceSeries ? Close[1] : Input[1];
                double trueRange = Math.Max(Math.Abs(barLowValue - close1), Math.Max(barHighValue - barLowValue, Math.Abs(barHighValue - close1)));
                atrSeries[0] = ((Math.Min(CurrentBar + 1, ATRPeriod) - 1) * atrSeries[1] + trueRange) / Math.Min(CurrentBar + 1, ATRPeriod);
            }

            topValue = ((barHighValue + barLowValue) / 2) + (ATRMultiplier * atrSeries[0]);
            bottomValue = ((barHighValue + barLowValue) / 2) - (ATRMultiplier * atrSeries[0]);
            topSeries[0] = (topValue < Default[1] || Input[1] > Default[1]) ? topValue : Default[1];
            bottomSeries[0] = (bottomValue > Default[1] || Input[1] < Default[1]) ? bottomValue : Default[1];
            Default[0] = (Default[1] == topSeries[1]) ? ((Input[0] <= topSeries[0]) ? topSeries[0] : bottomSeries[0]) : ((Default[1] == bottomSeries[1]) ? ((Input[0] >= bottomSeries[0]) ? bottomSeries[0] : topSeries[0]) : topSeries[0]);

            // Awesome Oscillator
            bool bAOGreen = false;
            var ao = SMA(Median, 5)[0] - SMA(Median, 34)[0];
            if (AO[0] > AO[1])
                bAOGreen = true;

            // SQUEEZE 
            double bbt = Bollinger(2, 20).Upper[0];
            double bbb = Bollinger(2, 20).Lower[0];
            double kct = KeltnerChannel(2, 20).Upper[0];
            double kcb = KeltnerChannel(2, 20).Lower[0];

            bool sqzOn = (bbb > kcb) && (bbt < kct);
            bool sqzOff = (bbb < kcb) && (bbt > kct);
            bool noSqz = (sqzOn == false) && (sqzOff == false);

            double h = High[HighestBar(High, 20)];
            double l = Low[LowestBar(Low, 20)];

            double avg = (h + l) / 2;
            avg = (avg + (kct + kcb) / 2) / 2;

            sqzData[0] = Close[0] - avg;
            SqueezeDef[0] = LinReg(sqzData, 20)[0];

            bool sqeezeUp = false;
            if (SqueezeDef[0] > 0)
            {
                sqeezeUp = true;
                if (SqueezeDef[0] < SqueezeDef[1] && !sqRelaxUp && bShowSqueeze)
                {
                    DrawText("✦", Brushes.Yellow, false, true);
                    sqRelaxUp = true;
                }
            }
            else
            {
                if (SqueezeDef[0] > SqueezeDef[1] && sqRelaxUp && bShowSqueeze)
                {
                    DrawText("✦", Brushes.Yellow, false, true);
                    sqRelaxUp = false;
                }
            }

            // Linda MACD
            double lindaMD = 0;
            MACD1[0] = EMAF[0] - EMAS[0];
            lindaMD = MACD1[0] - SMA(MACD1, 16)[0];
            bool macdUp = lindaMD > 0;

            //Print("Linda = " + lindaMD.ToString());
            //DrawText(lindaMD.ToString(), Brushes.White);

            var filteredLinda = Math.Min(Math.Abs(lindaMD) * iLindaIntense, 255);
            var cCoLorLinda = lindaMD > 0 ? Color.FromArgb(255, 0, (byte)filteredLinda, 0) : Color.FromArgb(255, (byte)filteredLinda, 0, 0);

            double Trend1, Trend2, Explo1, Explo2, Dead;
            Trend1 = (MACD(20, 40, 9)[0] - MACD(20, 40, 9)[1]) * iWaddahIntense;
            Trend2 = (MACD(20, 40, 9)[2] - MACD(20, 40, 9)[3]) * iWaddahIntense;
            Explo1 = Bollinger(2, 20).Upper[0] - Bollinger(2, 20).Lower[0];
            Explo2 = Bollinger(2, 20).Upper[1] - Bollinger(2, 20).Lower[1];
            Dead = TickSize * 30;
            bool wadaUp = Trend1 >= 0 ? true : false;

            var waddah = Math.Min(Math.Abs(Trend1) + iWaddahBuffer, 255);
            var cCoLorWaddah = Trend1 >= 0 ? Color.FromArgb(255, 0, (byte)waddah, 0) : Color.FromArgb(255, (byte)waddah, 0, 0);

            //Supertrend st = Supertrend(2, 11);
            //bool superUp = st.Value[0] < Low[0] ? true : false;
            bool superUp = Default[0] < Low[0] ? true : false;

            FisherTransform ft = FisherTransform(10);
            bool fisherUp = ft.Value[0] > ft.Value[1] ? true : false;

            //WaddahAttarExplosion wae = WaddahAttarExplosion(150, 30, 15, 1, false, 1, false, false, false, false);

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
                    if (bPlaySounds)
                        Alert("Alert", Priority.Medium, "Volume Imbalance BUY", sndImb, 10, Brushes.Black, Brushes.BlanchedAlmond);
                    if (bSendEmail)
                        SendMail(sEmailAddress, "Volume Imbalance BUY", "Volume Imbalance BUY " + Instrument + " " + Close[0].ToString());
                    //Draw.Line(this, tag, true, DateTime.Today.AddDays(-0.4), Open[0], DateTime.Now, Open[0], Brushes.MediumPurple, DashStyleHelper.Dash, 1);
                }
                if (red && c1R && Open[0] < Close[1])
                {
                    //DrawText("▼", Brushes.Orange, false, true);
                    Draw.Line(this, CurrentBar.ToString(), 1, Open[0], -600, Open[0], VI_Brush);
                    lines li = new lines() { loc = Open[0], tag = CurrentBar.ToString() };
                    ll.Add(li);
                    if (bPlaySounds)
                        Alert("Alert", Priority.Medium, "Volume Imbalance SELL", sndImb, 10, Brushes.Black, Brushes.BlanchedAlmond);
                    if (bSendEmail)
                        SendMail(sEmailAddress, "Volume Imbalance SELL", "Volume Imbalance SELL " + Instrument + " " + Close[0].ToString());
                }
            }

            #endregion

            #region DISPLAY BUY / SELL

            // ========================    UP CONDITIONS    ===========================

            if ((!macdUp && bUseMACD) || (!psarUp && bUsePSAR) || (!fisherUp && bUseFisher) || (!t3Up && bUseT3) || (!wadaUp && bUseWaddah) || (!superUp && bUseSuperTrend) || (!sqeezeUp && bUseSqueeze) || x.Value[0] < iMinADX || (bUseHMA && !hullUp) || (bUseAO && !bAOGreen))
                bShowUp = false;

            if (green && bShowUp && bShowRegularBuySell)
            {
                DrawText("▴", Green_Brush, false, true);
                if (bPlaySounds)
                    Alert("Alert", Priority.Medium, "Standard BUY", sndBuy, 10, Brushes.Black, Brushes.BlanchedAlmond);
                if (bSendEmail)
                    SendMail(sEmailAddress, "Standard BUY", "Standard BUY " + Instrument + " " + Close[0].ToString());
            }

            // ========================    DOWN CONDITIONS    =========================

            if ((macdUp && bUseMACD) || (psarUp && bUsePSAR) || (fisherUp && bUseFisher) || (t3Up && bUseT3) || (wadaUp && bUseWaddah) || (superUp && bUseSuperTrend) || (sqeezeUp && bUseSqueeze) || x.Value[0] < iMinADX || (bUseHMA && hullUp) || (bUseAO && bAOGreen))
                bShowDown = false;

            if (red && bShowDown && bShowRegularBuySell)
            {
                DrawText("▾", Red_Brush, false, true);
                if (bPlaySounds)
                    Alert("Alert", Priority.Medium, "Standard SELL", sndSell, 10, Brushes.Black, Brushes.BlanchedAlmond);
                if (bSendEmail)
                    SendMail(sEmailAddress, "Standard SELL", "Standard SELL " + Instrument + " " + Close[0].ToString());
            }

            if (bShowAdvanced)
            {
                if (c4Body > c3Body && c3Body > c2Body && c2Body > c1Body && c1Body > c0Body)
                    if ((Close[0] > Close[1] && Close[1] > Close[2] && Close[2] > Close[3]) ||
                    (Close[0] < Close[1] && Close[1] < Close[2] && Close[2] < Close[3]))
                        DrawText("Stairs", Brushes.Yellow);

                if (eqHigh)
                {
                    DrawText("Eq Hi", Brushes.Yellow, false, true);
                }

                if (eqLow)
                {
                    DrawText("Eq Low", Brushes.Yellow, false, true);
                }
            }

            if (bShowRevPattern)
            {
                if (c0R && High[0] > bb_top && Open[0] < bb_top && Open[0] > Close[1] && upWickLarger)
                    DrawText("Wick", Brushes.Yellow, false, true);
                if (c0G && Low[0] < bb_bottom && Open[0] > bb_bottom && Open[0] > Close[1] && downWickLarger)
                    DrawText("Wick", Brushes.Yellow, false, true);

                if (ThreeOutUp)
                    DrawText("3oU", Brushes.Yellow);
                if (ThreeOutDown)
                    DrawText("3oD", Brushes.Yellow);
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

            if (Trend1 > Explo1 && psarUp && !bBigArrowUp && bShowMACDPSARArrow)
            {
                Draw.ArrowUp(this, "A" + CurrentBar, true, 0, Low[0] - TickSize * 7, Green_Brush);
                bBigArrowUp = true;
                if (bPlaySounds)
                    PlaySound(Core.Globals.InstallDir + @"\sounds\Buy.wav");
                if (bSendEmail)
                    SendMail(sEmailAddress, "MACD/PSAR BUY", "MACD/PSAR BUY " + Instrument + " " + Close[0].ToString());
            }

            if (Trend1 < 0 && Math.Abs(Trend1) > Explo1 && !psarUp && bBigArrowUp && bShowMACDPSARArrow)
            {
                Draw.ArrowDown(this, "A" + CurrentBar, true, 0, High[0] + TickSize * 7, Red_Brush);
                bBigArrowUp = false;
                if (bPlaySounds)
                    PlaySound(Core.Globals.InstallDir + @"\sounds\Sell.wav");
                if (bSendEmail)
                    SendMail(sEmailAddress, "MACD/PSAR SELL", "MACD/PSAR SELL " + Instrument + " " + Close[0].ToString());
            }

            #endregion

            #region OTHER STUFF

            if (bWaddahCandle)
                BarBrush = new SolidColorBrush(cCoLorWaddah);

            if (bLindaCandle)
                BarBrush = new SolidColorBrush(cCoLorLinda);

            if (bShowEvilTimes)
            {
                string evil = EvilTimes(Bars.GetTime(0));
                if (evil != "" && evil != PrevEvil)
                {
                    DrawText(evil, Red_Brush, false, true);
                    //Draw.TextFixed(this, "ET" + CurrentBar, evil, TextPosition.BottomRight);
                    PrevEvil = evil;
                }
            }

            #endregion

        }

        #region MISC FUNCTIONS

        protected void Defibillator()
        {
            bDefibCalculated = true;
            double _highest = High[HighestBar(High, Bars.BarsSinceNewTradingDay)];
            double _lowest = Low[LowestBar(High, Bars.BarsSinceNewTradingDay)];

            var fibrange = _highest - _lowest;
            double r = Math.Abs(fibrange);
            var A05 = _lowest + 0.5 * r;
            var A113 = _lowest + 1.13 * r;
            var A150 = _lowest + 1.5 * r;
            var A1618 = _lowest + 1.618 * r;
            var A200 = _lowest + 2.00 * r;
            var A213 = _lowest + 2.13 * r;
            var A250 = _lowest + 2.5 * r;
            var A2618 = _lowest + 2.618 * r;

            var nA113 = _highest - 1.13 * r;
            var nA150 = _highest - 1.5 * r;
            var nA1618 = _highest - 1.618 * r;
            var nA200 = _highest - 2.00 * r;
            var nA213 = _highest - 2.13 * r;
            var nA250 = _highest - 2.5 * r;
            var nA2618 = _highest - 2.618 * r;

            Print(Bars.BarsSinceNewTradingDay);
            Print(CurrentBar);

            int ixBar = Bars.BarsSinceNewTradingDay;

            Draw.Line(this, "lix1" + CurrentBar, ixBar, A05, 0, A05, Brushes.White);
            Draw.Line(this, "lix2" + CurrentBar, ixBar, A113, 0, A113, Brushes.White);
            Draw.Line(this, "lix3" + CurrentBar, ixBar, A150, 0, A150, Brushes.White);
            Draw.Line(this, "lix4" + CurrentBar, ixBar, A1618, 0, A1618, Brushes.White);
            Draw.Line(this, "lix5" + CurrentBar, ixBar, A200, 0, A200, Brushes.White);
            Draw.Line(this, "lix6" + CurrentBar, ixBar, A213, 0, A213, Brushes.White);
            Draw.Line(this, "lix7" + CurrentBar, ixBar, A250, 0, A250, Brushes.White);
            Draw.Line(this, "lix8" + CurrentBar, ixBar, A2618, 0, A2618, Brushes.White);
            Draw.Line(this, "lix9" + CurrentBar, ixBar, nA113, 0, nA113, Brushes.White);
            Draw.Line(this, "lix10" + CurrentBar, ixBar, nA150, 0, nA150, Brushes.White);
            Draw.Line(this, "lix11" + CurrentBar, ixBar, nA1618, 0, nA1618, Brushes.White);
            Draw.Line(this, "lix12" + CurrentBar, ixBar, nA200, 0, nA200, Brushes.White);
            Draw.Line(this, "lix13" + CurrentBar, ixBar, nA213, 0, nA213, Brushes.White);
            Draw.Line(this, "lix14" + CurrentBar, ixBar, nA250, 0, nA250, Brushes.White);
            Draw.Line(this, "lix15" + CurrentBar, ixBar, nA2618, 0, nA2618, Brushes.White);
        }

        protected void DrawText(String strX, Brush br, bool bOverride = false, bool bSwap = false)
        {
            Brush brFinal;
            double loc = 0;
            int bar = CurrentBar;
            int zero = 0;
            int iOffsetMe = 0;
            SolidColorBrush backGround = Brushes.Transparent;

            SimpleFont sf = new SimpleFont();
            sf.Bold = false;
            sf.Size = iTextSize;

            if (strX.Contains("Eq"))
            {
                bar = CurrentBar - 1;
                zero = 1;
            }

            if (Close[zero] > Open[zero] || bOverride)
                loc = High[zero] + (TickSize * iTickOffset);
            else
                loc = Low[zero] - (TickSize * iTickOffset);

            if (Close[zero] > Open[zero] && bSwap)
                loc = Low[zero] - (TickSize * iTickOffset);
            else if (Close[zero] < Open[zero] && bSwap)
                loc = High[zero] + (TickSize * iTickOffset);

            brFinal = loc == High[zero] + (TickSize * iTickOffset) ? Red_Brush :Green_Brush;
            if (strX.Contains("▾") || strX.Contains("▴") || strX.Contains("✦"))
            {
                brFinal = br;
                sf.Size = iDotSize;
                backGround = Brushes.Transparent;
            }
            if (strX.Contains("Eq") || strX.Contains("3o"))
                backGround = brFinal == Red_Brush ? Brushes.Red : Brushes.Lime;
            if (strX.Contains("TR"))
                backGround = Brushes.PowderBlue;
            if (strX.Contains("CST"))
            {
                br = Brushes.White;
                iOffsetMe = 150;
            }

            //Draw.Text(this, "D" + bar, strX, zero, loc, brFinal);
            Draw.Text(this, "D" + bar, true, strX, zero, loc, iOffsetMe, br, sf, 
                TextAlignment.Center, Brushes.Transparent, backGround, 40);
        }

        private String EvilTimes(DateTime time)
        {
            int curr = ToTime(Time[0]);
            int pivotStart = ToTime(9, 00, 00);
            int pivotEnd = ToTime(10, 00, 00);
            int euroMove = ToTime(10, 30, 00);
            int inverse1 = ToTime(11, 00, 00);
            int inverse2 = ToTime(12, 00, 00);

            int auctions = ToTime(13, 30, 00);
            int inject = ToTime(14, 30, 00);
            int rug = ToTime(14, 45, 00);
            int finale = ToTime(15, 00, 00);

            if (curr >= pivotStart && curr <= pivotEnd)
                return "Market Pivot\n(9 - 10am CST)";

            if (curr >= pivotEnd && curr <= euroMove)
                return "Euro Move\n(10 - 10:30am CST)";

            if (curr >= euroMove && curr <= inverse1)
                return "Inverse\n(10:30 - 11am CST)";

            if (curr >= inverse1 && curr <= inverse2)
                return "Inverse\n(11am - 12pm CST)";

            if (curr >= inverse2 && curr <= auctions)
                return "Bond Auctions\n(12pm - 1:30pm CST)";

            if (curr >= auctions && curr <= rug)
                return "Capital Injection\n(1:30pm - 2:45pm CST)";

            if (curr >= rug && curr <= finale)
                return "Rug Pull\n(2:45pm - 3pm CST)";

            return "";
        }

        #endregion

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

        [NinjaScriptProperty]
        [Display(Name = "Show FighterOfEvilTimes", GroupName = "Advanced", Order = 8)]
        public bool bShowEvilTimes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Play Alert Sounds", GroupName = "Advanced", Order = 9)]
        public bool bPlaySounds { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Send Email Alerts", GroupName = "Advanced", Order = 10)]
        public bool bSendEmail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Email Address", GroupName = "Advanced", Order = 11)]
        public string sEmailAddress { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert Text Size", GroupName = "Advanced", Order = 12)]
        public int iTextSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dots and Arrow Size", GroupName = "Advanced", Order = 13)]
        public int iDotSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Indicator tick offset", GroupName = "Advanced", Order = 14)]
        public int iTickOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Indicator Version", GroupName = "Advanced", Order = 15)]
        public string myVersion { get; set; }

        //////////////////////////////////////////////////////////////////////////////

        [NinjaScriptProperty]
        [Display(Name = "Show Waddah Candles", GroupName = "Colored Candles", Order = 1)]
        public bool bWaddahCandle { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Waddah Intensity", GroupName = "Colored Candles", Order = 2)]
        public int iWaddahIntense { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Waddah Buffer", GroupName = "Colored Candles", Order = 3)]
        public int iWaddahBuffer { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Linda MACD Candles", GroupName = "Colored Candles", Order = 4)]
        public bool bLindaCandle { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Linda MACD Intensity", GroupName = "Colored Candles", Order = 5)]
        public int iLindaIntense { get; set; }

        [XmlIgnore]
        [Display(Name = "Volume Imbalance Color", GroupName = "Colors", Order = 1)]
        public Brush VI_Brush
        { get; set; }

        [XmlIgnore]
        [Display(Name = "Green Global Color", GroupName = "Colors", Order = 2)]
        public Brush Green_Brush
        { get; set; }

        [XmlIgnore]
        [Display(Name = "Red Global Color", GroupName = "Colors", Order = 3)]
        public Brush Red_Brush
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
		private Optimus[] cacheOptimus;
		public Optimus Optimus(bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowRegularBuySell, bool bShowMACDPSARArrow, bool bVolumeImbalances, bool bShowTramp, bool bShowSqueeze, bool bShowRevPattern, bool bShowAdvanced, bool bShowEvilTimes, bool bPlaySounds, bool bSendEmail, string sEmailAddress, int iTextSize, int iDotSize, int iTickOffset, string myVersion, bool bWaddahCandle, int iWaddahIntense, int iWaddahBuffer, bool bLindaCandle, int iLindaIntense)
		{
			return Optimus(Input, bUseWaddah, bUseAO, bUsePSAR, bUseSqueeze, bUseMACD, bUseHMA, bUseSuperTrend, bUseT3, bUseFisher, iMinADX, bShowRegularBuySell, bShowMACDPSARArrow, bVolumeImbalances, bShowTramp, bShowSqueeze, bShowRevPattern, bShowAdvanced, bShowEvilTimes, bPlaySounds, bSendEmail, sEmailAddress, iTextSize, iDotSize, iTickOffset, myVersion, bWaddahCandle, iWaddahIntense, iWaddahBuffer, bLindaCandle, iLindaIntense);
		}

		public Optimus Optimus(ISeries<double> input, bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowRegularBuySell, bool bShowMACDPSARArrow, bool bVolumeImbalances, bool bShowTramp, bool bShowSqueeze, bool bShowRevPattern, bool bShowAdvanced, bool bShowEvilTimes, bool bPlaySounds, bool bSendEmail, string sEmailAddress, int iTextSize, int iDotSize, int iTickOffset, string myVersion, bool bWaddahCandle, int iWaddahIntense, int iWaddahBuffer, bool bLindaCandle, int iLindaIntense)
		{
			if (cacheOptimus != null)
				for (int idx = 0; idx < cacheOptimus.Length; idx++)
					if (cacheOptimus[idx] != null && cacheOptimus[idx].bUseWaddah == bUseWaddah && cacheOptimus[idx].bUseAO == bUseAO && cacheOptimus[idx].bUsePSAR == bUsePSAR && cacheOptimus[idx].bUseSqueeze == bUseSqueeze && cacheOptimus[idx].bUseMACD == bUseMACD && cacheOptimus[idx].bUseHMA == bUseHMA && cacheOptimus[idx].bUseSuperTrend == bUseSuperTrend && cacheOptimus[idx].bUseT3 == bUseT3 && cacheOptimus[idx].bUseFisher == bUseFisher && cacheOptimus[idx].iMinADX == iMinADX && cacheOptimus[idx].bShowRegularBuySell == bShowRegularBuySell && cacheOptimus[idx].bShowMACDPSARArrow == bShowMACDPSARArrow && cacheOptimus[idx].bVolumeImbalances == bVolumeImbalances && cacheOptimus[idx].bShowTramp == bShowTramp && cacheOptimus[idx].bShowSqueeze == bShowSqueeze && cacheOptimus[idx].bShowRevPattern == bShowRevPattern && cacheOptimus[idx].bShowAdvanced == bShowAdvanced && cacheOptimus[idx].bShowEvilTimes == bShowEvilTimes && cacheOptimus[idx].bPlaySounds == bPlaySounds && cacheOptimus[idx].bSendEmail == bSendEmail && cacheOptimus[idx].sEmailAddress == sEmailAddress && cacheOptimus[idx].iTextSize == iTextSize && cacheOptimus[idx].iDotSize == iDotSize && cacheOptimus[idx].iTickOffset == iTickOffset && cacheOptimus[idx].myVersion == myVersion && cacheOptimus[idx].bWaddahCandle == bWaddahCandle && cacheOptimus[idx].iWaddahIntense == iWaddahIntense && cacheOptimus[idx].iWaddahBuffer == iWaddahBuffer && cacheOptimus[idx].bLindaCandle == bLindaCandle && cacheOptimus[idx].iLindaIntense == iLindaIntense && cacheOptimus[idx].EqualsInput(input))
						return cacheOptimus[idx];
			return CacheIndicator<Optimus>(new Optimus(){ bUseWaddah = bUseWaddah, bUseAO = bUseAO, bUsePSAR = bUsePSAR, bUseSqueeze = bUseSqueeze, bUseMACD = bUseMACD, bUseHMA = bUseHMA, bUseSuperTrend = bUseSuperTrend, bUseT3 = bUseT3, bUseFisher = bUseFisher, iMinADX = iMinADX, bShowRegularBuySell = bShowRegularBuySell, bShowMACDPSARArrow = bShowMACDPSARArrow, bVolumeImbalances = bVolumeImbalances, bShowTramp = bShowTramp, bShowSqueeze = bShowSqueeze, bShowRevPattern = bShowRevPattern, bShowAdvanced = bShowAdvanced, bShowEvilTimes = bShowEvilTimes, bPlaySounds = bPlaySounds, bSendEmail = bSendEmail, sEmailAddress = sEmailAddress, iTextSize = iTextSize, iDotSize = iDotSize, iTickOffset = iTickOffset, myVersion = myVersion, bWaddahCandle = bWaddahCandle, iWaddahIntense = iWaddahIntense, iWaddahBuffer = iWaddahBuffer, bLindaCandle = bLindaCandle, iLindaIntense = iLindaIntense }, input, ref cacheOptimus);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Optimus Optimus(bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowRegularBuySell, bool bShowMACDPSARArrow, bool bVolumeImbalances, bool bShowTramp, bool bShowSqueeze, bool bShowRevPattern, bool bShowAdvanced, bool bShowEvilTimes, bool bPlaySounds, bool bSendEmail, string sEmailAddress, int iTextSize, int iDotSize, int iTickOffset, string myVersion, bool bWaddahCandle, int iWaddahIntense, int iWaddahBuffer, bool bLindaCandle, int iLindaIntense)
		{
			return indicator.Optimus(Input, bUseWaddah, bUseAO, bUsePSAR, bUseSqueeze, bUseMACD, bUseHMA, bUseSuperTrend, bUseT3, bUseFisher, iMinADX, bShowRegularBuySell, bShowMACDPSARArrow, bVolumeImbalances, bShowTramp, bShowSqueeze, bShowRevPattern, bShowAdvanced, bShowEvilTimes, bPlaySounds, bSendEmail, sEmailAddress, iTextSize, iDotSize, iTickOffset, myVersion, bWaddahCandle, iWaddahIntense, iWaddahBuffer, bLindaCandle, iLindaIntense);
		}

		public Indicators.Optimus Optimus(ISeries<double> input , bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowRegularBuySell, bool bShowMACDPSARArrow, bool bVolumeImbalances, bool bShowTramp, bool bShowSqueeze, bool bShowRevPattern, bool bShowAdvanced, bool bShowEvilTimes, bool bPlaySounds, bool bSendEmail, string sEmailAddress, int iTextSize, int iDotSize, int iTickOffset, string myVersion, bool bWaddahCandle, int iWaddahIntense, int iWaddahBuffer, bool bLindaCandle, int iLindaIntense)
		{
			return indicator.Optimus(input, bUseWaddah, bUseAO, bUsePSAR, bUseSqueeze, bUseMACD, bUseHMA, bUseSuperTrend, bUseT3, bUseFisher, iMinADX, bShowRegularBuySell, bShowMACDPSARArrow, bVolumeImbalances, bShowTramp, bShowSqueeze, bShowRevPattern, bShowAdvanced, bShowEvilTimes, bPlaySounds, bSendEmail, sEmailAddress, iTextSize, iDotSize, iTickOffset, myVersion, bWaddahCandle, iWaddahIntense, iWaddahBuffer, bLindaCandle, iLindaIntense);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Optimus Optimus(bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowRegularBuySell, bool bShowMACDPSARArrow, bool bVolumeImbalances, bool bShowTramp, bool bShowSqueeze, bool bShowRevPattern, bool bShowAdvanced, bool bShowEvilTimes, bool bPlaySounds, bool bSendEmail, string sEmailAddress, int iTextSize, int iDotSize, int iTickOffset, string myVersion, bool bWaddahCandle, int iWaddahIntense, int iWaddahBuffer, bool bLindaCandle, int iLindaIntense)
		{
			return indicator.Optimus(Input, bUseWaddah, bUseAO, bUsePSAR, bUseSqueeze, bUseMACD, bUseHMA, bUseSuperTrend, bUseT3, bUseFisher, iMinADX, bShowRegularBuySell, bShowMACDPSARArrow, bVolumeImbalances, bShowTramp, bShowSqueeze, bShowRevPattern, bShowAdvanced, bShowEvilTimes, bPlaySounds, bSendEmail, sEmailAddress, iTextSize, iDotSize, iTickOffset, myVersion, bWaddahCandle, iWaddahIntense, iWaddahBuffer, bLindaCandle, iLindaIntense);
		}

		public Indicators.Optimus Optimus(ISeries<double> input , bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowRegularBuySell, bool bShowMACDPSARArrow, bool bVolumeImbalances, bool bShowTramp, bool bShowSqueeze, bool bShowRevPattern, bool bShowAdvanced, bool bShowEvilTimes, bool bPlaySounds, bool bSendEmail, string sEmailAddress, int iTextSize, int iDotSize, int iTickOffset, string myVersion, bool bWaddahCandle, int iWaddahIntense, int iWaddahBuffer, bool bLindaCandle, int iLindaIntense)
		{
			return indicator.Optimus(input, bUseWaddah, bUseAO, bUsePSAR, bUseSqueeze, bUseMACD, bUseHMA, bUseSuperTrend, bUseT3, bUseFisher, iMinADX, bShowRegularBuySell, bShowMACDPSARArrow, bVolumeImbalances, bShowTramp, bShowSqueeze, bShowRevPattern, bShowAdvanced, bShowEvilTimes, bPlaySounds, bSendEmail, sEmailAddress, iTextSize, iDotSize, iTickOffset, myVersion, bWaddahCandle, iWaddahIntense, iWaddahBuffer, bLindaCandle, iLindaIntense);
		}
	}
}

#endregion
