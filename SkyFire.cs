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
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.MarketAnalyzerColumns;
using NinjaTrader.CQG.ProtoBuf;
using static NinjaTrader.NinjaScript.Indicators.Optimus;
using System.Net.Mail;
using System.Reflection.Emit;
using System.Diagnostics;
using System.Runtime.Remoting.Contexts;
using System.Runtime.InteropServices;
using System.Windows.Forms;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class SkyFire : Strategy
    {
        private string sVersion = "1.0";

        #region SHITLOAD OF VARIABLES

        private Stopwatch clock = new Stopwatch();
        private DateTime dtStart = DateTime.Now;
        private String sLastTrade = String.Empty;

        private SMA SMAS;
        private SMA SMAF;
        private Series<double> MACD1;
        private Series<double> LMACD;
        private Series<double> sqzData;
        private Series<double> SqueezeDef;
        private Series<double> AO;

        private bool countOnce;
        private int iOpenPositions;
        private double totalPnL;
        private double cumPnL;
        private double dailyPnL;
        private double percentageCalc;
        private double priceCalc;
        private double tickCalc;
        private double candleBarOffset;
        private bool currentBullRev;
        private bool currentBearRev;

        private double entryAreaLong;
        private double entryAreaShort;

        private double percentageCalcEntry;
        private double priceCalcEntry;
        private double tickCalcEntry;
        private double candleBarOffsetEntry;

        private double enterLong;
        private double enterShort;

        private double stopAreaLong;
        private double stopAreaShort;

        private double percentageCalcStop;
        private double priceCalcStop;
        private double tickCalcStop;
        private double candleBarOffsetStop;

        private double stopLong;
        private double stopShort;

        private double breakevenTriggerLong;
        private double breakevenTriggerShort;

        private bool myFreeBELong;
        private bool myFreeBEShort;

        private double breakevenLong;
        private double breakevenShort;

        private double trailAreaLong;
        private double trailAreaShort;

        private double percentageCalcTrail;
        private double priceCalcTrail;
        private double tickCalcTrail;
        private double candleBarOffsetTrail;

        private double trailLong;
        private double trailShort;

        private double trailTriggerLong;
        private double trailTriggerShort;

        private bool myFreeTrail;

        private bool trailTriggeredCandle;

        private bool myFreeTradeLong;
        private bool myFreeTradeShort;

        #endregion

        protected override void OnBarUpdate()
        {
            if (!clock.IsRunning)
                clock.Start();

            if (State != State.Realtime || CurrentBars[0] < 2)
                return;

            if (Bars.IsFirstBarOfSession)
            {
                iOpenPositions = 0;
                cumPnL = totalPnL;
                dailyPnL = totalPnL - cumPnL;

                Print("totalPnL First Bar " + totalPnL + " " + Time[0]);
                Print("cumPnL First Bar " + cumPnL + " " + Time[0]);
                Print("dailyPnL First Bar " + dailyPnL + " " + Time[0]);
            }

            if (Bars.BarsSinceNewTradingDay < 1)
                return;

            //if (IsFirstTickOfBar)
            {
                string xy = EnterNewTrade();

                if (xy.Contains("+1")
                    && (Position.MarketPosition != MarketPosition.Short)
                    && (dailyPnL > -DailyLossLimit)
                    && (dailyPnL < DailyProfitLimit)
                    )
                {
                    if (iOpenPositions >= iMaxContracts)
                    {
                        Print("Maximum contracts of " + iMaxContracts + " reached.  No trade occurred.");
                        return;
                    }

                    Print("ENTERING LONG POSITION : " + xy);
                    EnterLong(PositionSize, "MyEntryLong");
                    sLastTrade = xy + " at " + Position.AveragePrice;
                }

                if (xy.Contains("-1")
                    && (Position.MarketPosition != MarketPosition.Long)
                    && (dailyPnL > -DailyLossLimit)
                    && (dailyPnL < DailyProfitLimit)
                    )
                {
                    if (iOpenPositions >= iMaxContracts)
                    {
                        Print("Maximum contracts of " + iMaxContracts + " reached.  No trade occurred.");
                        return;
                    }

                    Print("ENTERING SHORT POSITION : " + xy);
                    EnterShort(PositionSize, "MyEntryShort");
                    sLastTrade = xy + " at " + Position.AveragePrice;
                }

            }

        }

        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            if (marketDataUpdate.MarketDataType == MarketDataType.Last)
            {

            }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                #region SHIT

                Description = @"Automated trading bot (c) 2024 TraderOracle";
                Name = "SkyFire";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 1;
                IsInstantiatedOnEachOptimizationIteration = true;

                DailyProfitLimit = 5000;
                DailyLossLimit = 5000;
                PositionSize = 1;
                iMaxContracts = 5;

                iMinADX = 0;
                bUseFisher = true;          // USE
                bUseWaddah = true;
                bUseT3 = true;
                bUsePSAR = true;
                bUseSuperTrend = true;
                bUseSqueeze = false;
                bUseMACD = false;
                bUseAO = false;
                bUseHMA = false;
                iWaddahIntense = 150;
                bExitHammer = false;
                bExitSqueeze = false;
                bExitKama9 = false;
                myVersion = "(c) 2024 by TraderOracle, version " + sVersion;

                #endregion
            }
            else if (State == State.Configure)
            {
                MACD1 = new Series<double>(this);
                LMACD = new Series<double>(this);
                sqzData = new Series<double>(this);
                SqueezeDef = new Series<double>(this);
                AO = new Series<double>(this);
            }
            else if (State == State.DataLoaded)
            {
                SMAF = SMA(3);
                SMAS = SMA(10);
            }
        }

        private string EnterNewTrade()
        {
            #region INDICATOR CALCULATIONS

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
                sqeezeUp = true;

            // Linda MACD
            MACD1[0] = SMAF[0] - SMAS[0];
            bool macdUp = MACD1[0] - SMA(MACD1, 16)[0] > 0;

            double Trend1, Trend2, Explo1, Explo2, Dead;
            Trend1 = (MACD(20, 40, 9)[0] - MACD(20, 40, 9)[1]) * iWaddahIntense;
            Trend2 = (MACD(20, 40, 9)[2] - MACD(20, 40, 9)[3]) * iWaddahIntense;
            Explo1 = Bollinger(2, 20).Upper[0] - Bollinger(2, 20).Lower[0];
            Explo2 = Bollinger(2, 20).Upper[1] - Bollinger(2, 20).Lower[1];
            Dead = TickSize * 30;
            bool wadaUp = Trend1 >= 0 ? true : false;

            Supertrend st = Supertrend(2, 11);
            bool superUp = st.Value[0] < Low[0] ? true : false;

            FisherTransform ft = FisherTransform(10);
            bool fisherUp = ft.Value[0] > ft.Value[1] ? true : false;

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
            KAMA kama9 = KAMA(2, 9, 109);
            RSI rsi = RSI(14, 1);

            #endregion

            #region CANDLE CALCULATIONS

            bool bShowDown = true;
            bool bShowUp = true;

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
            var eqHigh = c0R && c1R && c2G && c3G && (High[1] > bb_top || High[2] > bb_top) && Close[0] < Close[1] && (Open[1] == Close[2] || Open[1] == Close[2] + TickSize || Open[1] + TickSize == Close[2]);
            var eqLow = c0G && c1G && c2R && c3R && (Low[1] < bb_bottom || Low[2] < bb_bottom) && Close[0] > Close[1] && (Open[1] == Close[2] || Open[1] == Close[2] + TickSize || Open[1] + TickSize == Close[2]);

            #endregion


            #region EXIT POSITIONS

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (red && Close[0] < kama9.Value[0] && bExitKama9)
                {
                    ExitLong("MyEntryLong");
                    Print("Exit = Priced crossed KAMA9");
                    sLastTrade = "Exit = Priced crossed KAMA9";
                    iOpenPositions = 0;
                }
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                if (green && Close[0] > kama9.Value[0] && bExitKama9)
                {
                    ExitShort("MyEntryShort");
                    Print("Exit = Priced crossed KAMA9");
                    sLastTrade = "Exit = Priced crossed KAMA9";
                    iOpenPositions = 0;
                }
            }

            #endregion

            #region DISPLAY BUY / SELL

            // VOLUME IMBALANCE
            if (green && c1G && Open[0] > Close[1])
                return "+1 Volume Imbalance";

            if (red && c1R && Open[0] < Close[1])
                return "-1 Volume Imbalance";

            // ========================    UP CONDITIONS    ===========================

            if ((!macdUp && bUseMACD) || (!psarUp && bUsePSAR) || (!fisherUp && bUseFisher) || (!t3Up && bUseT3) || (!wadaUp && bUseWaddah) || (!superUp && bUseSuperTrend) || (!sqeezeUp && bUseSqueeze) || x.Value[0] < iMinADX || (bUseHMA && !hullUp) || (bUseAO && !bAOGreen))
                bShowUp = false;

            if (green && bShowUp)
                return "+1 Standard Buy";

            // ========================    DOWN CONDITIONS    =========================

            if ((macdUp && bUseMACD) || (psarUp && bUsePSAR) || (fisherUp && bUseFisher) || (t3Up && bUseT3) || (wadaUp && bUseWaddah) || (superUp && bUseSuperTrend) || (sqeezeUp && bUseSqueeze) || x.Value[0] < iMinADX || (bUseHMA && hullUp) || (bUseAO && bAOGreen))
                bShowDown = false;

            if (red && bShowDown)
                return "-1 Standard Sell";

            #endregion

            return "0000";
        }


        protected override void OnPositionUpdate(Cbi.Position position, double averagePrice, int quantity, Cbi.MarketPosition marketPosition)
        {
            SimpleFont sf = new SimpleFont();
            totalPnL = SystemPerformance.RealTimeTrades.TradesPerformance.Currency.CumProfit;

            var txt = $"SkyFire version " + sVersion;
            TimeSpan t = TimeSpan.FromMilliseconds(clock.ElapsedMilliseconds);
            txt += "\n" + $"ACTIVE since " + dtStart.ToString() + " (" + String.Format("{0:D2}:{1:D2}:{2:D2}", t.Hours, t.Minutes, t.Seconds) + ")";
            txt += "\n" + SystemPerformance.RealTimeTrades.TradesPerformance.TradesCount + " trades with profit factor " + SystemPerformance.RealTimeTrades.TradesPerformance.ProfitFactor.ToString("0.##") + " PNL = " + totalPnL;
            txt += "\nLast Trade: " + sLastTrade;

            Draw.TextFixed(this, "xdf", txt, TextPosition.BottomLeft, Brushes.White, sf, Brushes.Transparent, Brushes.Transparent, 100);

            if (Position.Quantity == PositionSize)
            {
                iOpenPositions++; //Adds +1 to your currentCount every time a position is filled
                Print("Current Positions " + iOpenPositions + " " + Time[1]);
            }
            
            if (Position.MarketPosition == MarketPosition.Flat && SystemPerformance.AllTrades.Count > 0)
            {
                dailyPnL = (totalPnL) - (cumPnL); ///Your daily limit is the difference between these

                if (dailyPnL <= -DailyLossLimit) //Print this when daily Pnl is under Loss Limit
                    Print("Daily Loss of " + DailyLossLimit + " has been hit. No More Entries! Daily PnL >> " + dailyPnL + " <<" + Time[0]);

                if (dailyPnL >= DailyProfitLimit) //Print this when daily Pnl is above Profit limit
                    Print("Daily Profit of " + DailyProfitLimit + " has been hit. No more Entries! Daily PnL >>" + dailyPnL + " <<" + Time[0]);
            }
        }

        #region Properties

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Position Size", GroupName = "General", Order = 0)]
        public int PositionSize
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Waddah Intensity", GroupName = "General", Order = 1)]
        public int iWaddahIntense { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Indicator Version", GroupName = "General", Order = 2)]
        public string myVersion { get; set; }

        // =======================================================================================

        [NinjaScriptProperty]
        [Display(Name = "Daily Profit Limit", GroupName = "Limits", Order = 0)]
        public double DailyProfitLimit
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Daily Loss Limit", GroupName = "Limits", Order = 1)]
        public double DailyLossLimit
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Contracts to Open", GroupName = "Limits", Order = 2)]
        public int iMaxContracts
        { get; set; }

        //===========================================================================================

        [NinjaScriptProperty]
        [Display(Name = "KAMA 9 cros", GroupName = "Exit Conditions", Order = 0)]
        public bool bExitKama9 { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Hammer candle", GroupName = "Exit Conditions", Order = 1)]
        public bool bExitHammer { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Squeeze relaxer", GroupName = "Exit Conditions", Order = 2)]
        public bool bExitSqueeze { get; set; }

        //===========================================================================================

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

        #endregion

    }
}
