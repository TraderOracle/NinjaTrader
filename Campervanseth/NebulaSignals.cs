#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// Ported from the "Nebula" Pine Script indicator (kaiso / TraderOracle).
// This port focuses ONLY on the logic that actually generates buy/sell
// signals. Purely cosmetic pieces from the original (cloud fill, candle
// coloring themes, HEMA line, 9/21 kernel cross, break/retest boxes,
// "Vodka Shot" add-on markers) were intentionally left out since they
// don't drive signal generation. See chat notes for what was included.

namespace NinjaTrader.NinjaScript.Indicators
{
    public class NebulaSignals : Indicator
    {
        #region Constants (signal weights, mirrors Pine consts)
        private const int VSqueeze = 4;
        private const int VTramp = 4;
        private const int VBands = 2;
        private const int VLuxRev = 3;
        private const int VEarlyRev = 2;
        private const int VDeadRev = 2;
        private const int VShark = 2;

        private const int UpTrend = 1;
        private const int DownTrend = 2;
        #endregion

        #region State series (need historical [] access across bars)
        // Tidal Wave engine
        private Series<int> waveState;
        private Series<bool> noOverlapGreen, noOverlapRed;
        private Series<bool> brightGreen, brightRed;
        private Series<bool> gapGreen, gapRed;

        // LuxAlgo Reversal Signals state machine
        private Series<int> bSCSeries, sSCSeries;

        // Squeeze Relaxer persistent state
        private int cGreenCt = 0;
        private int cRedCt = 0;
        private Series<double> sqValSeries;   // (close - avg2) raw source
        private Series<double> sqLinRegOut;   // linreg output ("val")
        private Series<bool> sqPos, sqNeg;

        // Native PVSRA-style vector candle detection
        private Series<double> volSpreadSeries;
        private Series<bool> vecGreen, vecRed;

        // Simplified market-structure break tracking (fractal via Swing)
        private double lastSwingHigh = double.NaN;
        private double lastSwingLow = double.NaN;
        private Series<bool> structBreakUp, structBreakDown;

        // Trampoline
        private Series<bool> weGoUpSeries, weGoDownSeries;

        // "Ultimate Buy/Sell" confirmation system
        private Series<double> rsiUSeries;
        private List<int> buyWatchHist = new List<int>();
        private List<int> sellWatchHist = new List<int>();
        private Series<bool> plotBuySeries, plotSellSeries;

        // Composite per-bar flags (need [1] = previous bar) for the
        // take-profit signal counter
        private Series<bool> deadRevSeries, luxRevSeries, trampSeries;
        private Series<bool> sqSeries, sharkSeries, bandsSeries, earlySeries;

        // Misc small helper series
        private Series<double> hlRangeSeries; // High-Low, for squeeze KC
        #endregion

        #region Inputs
        [NinjaScriptProperty]
        [Display(Name = "Show basic buy/sell markers", GroupName = "Visible Settings", Order = 0)]
        public bool ShowBasicSignals { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show strong buy/sell markers", GroupName = "Visible Settings", Order = 1)]
        public bool ShowStrongSignals { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show volume-imbalance plus signs", GroupName = "Visible Settings", Order = 2)]
        public bool ShowVolumeImbalance { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show take-profit markers", GroupName = "Visible Settings", Order = 3)]
        public bool ShowTakeProfitSignals { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Ultimate Buy/Sell triangles", GroupName = "Visible Settings", Order = 4)]
        public bool ShowUltimateSignals { get; set; }

        [NinjaScriptProperty]
        [Range(2, 50)]
        [Display(Name = "Signal count: Full profit threshold", GroupName = "Basic Settings", Order = 0)]
        public int SignalThresholdFull { get; set; }

        [NinjaScriptProperty]
        [Range(3, 50)]
        [Display(Name = "Signal count: Partial (all) profit threshold", GroupName = "Basic Settings", Order = 1)]
        public int SignalThresholdPartial { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Ignore dojis", GroupName = "Basic Settings", Order = 2)]
        public bool IgnoreDojis { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Doji body threshold (price units)", GroupName = "Basic Settings", Order = 3)]
        public double DojiBodyThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require watch signal for Ultimate Buy/Sell", GroupName = "Ultimate Buy/Sell", Order = 0)]
        public bool RequireWatchSignals { get; set; }

        [NinjaScriptProperty]
        [Range(5, 200)]
        [Display(Name = "Watch signal lookback (bars)", GroupName = "Ultimate Buy/Sell", Order = 1)]
        public int WatchSignalLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Apply Shark 25/75 RSI rule", GroupName = "Shark Settings", Order = 0)]
        public bool ApplyShark2575Rule { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX threshold (Squeeze)", GroupName = "Squeeze Settings", Order = 0)]
        public int AdxThresholdSqueeze { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Squeeze tolerance", GroupName = "Squeeze Settings", Order = 1)]
        public int SqueezeTolerance { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Bollinger width threshold (Trampoline)", GroupName = "Trampoline Settings", Order = 0)]
        public double BBThresholdTrampoline { get; set; }

        [NinjaScriptProperty]
        [Range(1, 99)]
        [Display(Name = "RSI lower threshold (Trampoline)", GroupName = "Trampoline Settings", Order = 1)]
        public int RsiThresholdTrampoline { get; set; }

        [NinjaScriptProperty]
        [Range(1, 99)]
        [Display(Name = "RSI upper threshold (Trampoline)", GroupName = "Trampoline Settings", Order = 2)]
        public int RsiUpperTrampoline { get; set; }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Ports the buy/sell-signal-generating logic from the 'Nebula' Pine Script indicator (Tidal Wave engine, Ultimate Buy/Sell system, and a weighted take-profit signal counter fed by Squeeze Relaxer, Trampoline, LuxAlgo Reversal, Dead Simple Reversal, Shark, wick-Bollinger bands, and a native PVSRA + market-structure early-reversal check).";
                Name = "NebulaSignals";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                PaintPriceMarkers = false;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                ShowBasicSignals = true;
                ShowStrongSignals = true;
                ShowVolumeImbalance = true;
                ShowTakeProfitSignals = true;
                ShowUltimateSignals = true;

                SignalThresholdFull = 5;
                SignalThresholdPartial = 7;

                IgnoreDojis = false;
                DojiBodyThreshold = 1.0;

                RequireWatchSignals = true;
                WatchSignalLookback = 35;

                ApplyShark2575Rule = false;

                AdxThresholdSqueeze = 21;
                SqueezeTolerance = 2;

                BBThresholdTrampoline = 0.0015;
                RsiThresholdTrampoline = 25;
                RsiUpperTrampoline = 72;
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.DataLoaded)
            {
                waveState = new Series<int>(this, MaximumBarsLookBack.Infinite);
                noOverlapGreen = new Series<bool>(this, MaximumBarsLookBack.Infinite);
                noOverlapRed = new Series<bool>(this, MaximumBarsLookBack.Infinite);
                brightGreen = new Series<bool>(this, MaximumBarsLookBack.Infinite);
                brightRed = new Series<bool>(this, MaximumBarsLookBack.Infinite);
                gapGreen = new Series<bool>(this, MaximumBarsLookBack.Infinite);
                gapRed = new Series<bool>(this, MaximumBarsLookBack.Infinite);

                bSCSeries = new Series<int>(this, MaximumBarsLookBack.Infinite);
                sSCSeries = new Series<int>(this, MaximumBarsLookBack.Infinite);

                sqValSeries = new Series<double>(this, MaximumBarsLookBack.Infinite);
                sqLinRegOut = new Series<double>(this, MaximumBarsLookBack.Infinite);
                sqPos = new Series<bool>(this, MaximumBarsLookBack.Infinite);
                sqNeg = new Series<bool>(this, MaximumBarsLookBack.Infinite);

                volSpreadSeries = new Series<double>(this, MaximumBarsLookBack.Infinite);
                vecGreen = new Series<bool>(this, MaximumBarsLookBack.Infinite);
                vecRed = new Series<bool>(this, MaximumBarsLookBack.Infinite);

                structBreakUp = new Series<bool>(this, MaximumBarsLookBack.Infinite);
                structBreakDown = new Series<bool>(this, MaximumBarsLookBack.Infinite);

                weGoUpSeries = new Series<bool>(this, MaximumBarsLookBack.Infinite);
                weGoDownSeries = new Series<bool>(this, MaximumBarsLookBack.Infinite);

                rsiUSeries = new Series<double>(this, MaximumBarsLookBack.Infinite);
                plotBuySeries = new Series<bool>(this, MaximumBarsLookBack.Infinite);
                plotSellSeries = new Series<bool>(this, MaximumBarsLookBack.Infinite);

                deadRevSeries = new Series<bool>(this, MaximumBarsLookBack.Infinite);
                luxRevSeries = new Series<bool>(this, MaximumBarsLookBack.Infinite);
                trampSeries = new Series<bool>(this, MaximumBarsLookBack.Infinite);
                sqSeries = new Series<bool>(this, MaximumBarsLookBack.Infinite);
                sharkSeries = new Series<bool>(this, MaximumBarsLookBack.Infinite);
                bandsSeries = new Series<bool>(this, MaximumBarsLookBack.Infinite);
                earlySeries = new Series<bool>(this, MaximumBarsLookBack.Infinite);

                hlRangeSeries = new Series<double>(this, MaximumBarsLookBack.Infinite);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 60)
            {
                // seed empty history so later [i] look-ups are safe
                waveState[0] = 0;
                noOverlapGreen[0] = false; noOverlapRed[0] = false;
                brightGreen[0] = false; brightRed[0] = false;
                gapGreen[0] = false; gapRed[0] = false;
                bSCSeries[0] = 0; sSCSeries[0] = 0;
                sqValSeries[0] = 0; sqLinRegOut[0] = 0; sqPos[0] = false; sqNeg[0] = false;
                volSpreadSeries[0] = (High[0] - Low[0]) * Volume[0];
                vecGreen[0] = false; vecRed[0] = false;
                structBreakUp[0] = false; structBreakDown[0] = false;
                weGoUpSeries[0] = false; weGoDownSeries[0] = false;
                rsiUSeries[0] = 50;
                plotBuySeries[0] = false; plotSellSeries[0] = false;
                deadRevSeries[0] = false; luxRevSeries[0] = false; trampSeries[0] = false;
                sqSeries[0] = false; sharkSeries[0] = false; bandsSeries[0] = false; earlySeries[0] = false;
                hlRangeSeries[0] = High[0] - Low[0];
                return;
            }

            bool redCandle = Close[0] < Open[0];
            bool greenCandle = Close[0] > Open[0];
            bool isDoji = Math.Abs(Close[0] - Open[0]) <= DojiBodyThreshold && IgnoreDojis;

            hlRangeSeries[0] = High[0] - Low[0];
            volSpreadSeries[0] = (High[0] - Low[0]) * Volume[0];

            #region Dead Simple Reversal
            bool c1 = Close[1] < Open[1] && greenCandle;
            bool c2 = Close[0] > Open[1];
            double low3 = MIN(Low, 3)[0];
            double low50_1 = MIN(Low, 50)[1];
            double low50_2 = MIN(Low, 50)[2];
            double low50_3 = MIN(Low, 50)[3];
            bool c3 = low3 < low50_1 || low3 < low50_2 || low3 < low50_3;
            bool buyDSR = c1 && c2 && c3;

            bool c4 = Close[1] > Open[1] && redCandle;
            bool c5 = Close[0] < Open[1];
            double high3 = MAX(High, 3)[0];
            double high50_1 = MAX(High, 50)[1];
            double high50_2 = MAX(High, 50)[2];
            double high50_3 = MAX(High, 50)[3];
            bool c6 = high3 > high50_1 || high3 > high50_2 || high3 > high50_3;
            bool sellDSR = c4 && c5 && c6;

            deadRevSeries[0] = buyDSR || sellDSR;
            #endregion

            #region LuxAlgo Reversal Signals (streak state machine)
            bool con = Close[0] < Close[4];
            int bSCPrev = bSCSeries[1];
            int sSCPrev = sSCSeries[1];
            int bSCCur, sSCCur;
            if (con)
            {
                bSCCur = bSCPrev == 9 ? 1 : bSCPrev + 1;
                sSCCur = 0;
            }
            else
            {
                sSCCur = sSCPrev == 9 ? 1 : sSCPrev + 1;
                bSCCur = 0;
            }
            bSCSeries[0] = bSCCur;
            sSCSeries[0] = sSCCur;

            bool pbS = (Low[0] <= Low[3] && Low[0] <= Low[2]) || (Low[1] <= Low[3] && Low[1] <= Low[2]);
            bool bC8 = bSCPrev == 8 && sSCCur == 1;
            bool showUppies = (bSCCur == 9 && !pbS) || (bSCCur == 9 && pbS) || bC8;

            bool psS = (High[0] >= High[3] && High[0] >= High[2]) || (High[1] >= High[3] && High[1] >= High[2]);
            bool sC8 = sSCPrev == 8 && bSCCur == 1;
            bool showDownies = (sSCCur == 9 && !psS) || (sSCCur == 9 && psS) || sC8;

            luxRevSeries[0] = showUppies || showDownies;
            #endregion

            #region Squeeze Relaxer
            double adxSq = ADX(14)[0];
            bool sigAbove19 = adxSq > AdxThresholdSqueeze;

            double basisSq = SMA(Close, 20)[0];
            double devSq = 2.0 * StdDev(Close, 20)[0];
            double upperBBsq = basisSq + devSq;
            double lowerBBsq = basisSq - devSq;
            double maSq = SMA(Close, 20)[0];
            double rangemaSq = SMA(hlRangeSeries, 20)[0];
            double upperKC = maSq + rangemaSq * 1.5;
            double lowerKC = maSq - rangemaSq * 1.5;
            bool sqzOn = (lowerBBsq > lowerKC) && (upperBBsq < upperKC);

            double avg1 = (MAX(High, 20)[0] + MIN(Low, 20)[0]) / 2.0;
            double avg2 = (avg1 + SMA(Close, 20)[0]) / 2.0;
            sqValSeries[0] = Close[0] - avg2;

            double val = LinRegEndpoint(sqValSeries, 20);
            sqLinRegOut[0] = val;
            double valPrev = sqLinRegOut[1];

            if (val < valPrev && val < 5 && !sqzOn) cRedCt++;
            if (val > valPrev && val > 5 && !sqzOn) cGreenCt++;

            bool posLocal = false, negLocal = false;
            if (val > valPrev && cRedCt > SqueezeTolerance && val < 5 && !sqPos[1] && sigAbove19)
            {
                cRedCt = 0;
                posLocal = true;
            }
            if (val < valPrev && cGreenCt > SqueezeTolerance && val > 5 && !sqNeg[1] && sigAbove19)
            {
                cGreenCt = 0;
                negLocal = true;
            }
            sqPos[0] = posLocal;
            sqNeg[0] = negLocal;
            sqSeries[0] = posLocal || negLocal;
            #endregion

            #region Trampoline
            double rsiTr = RSI(Close, 14, 1)[0];
            double basisTr = SMA(Close, 20)[0];
            double devTr = 2.0 * StdDev(Close, 20)[0];
            double upperTr = basisTr + devTr;
            double lowerTr = basisTr - devTr;
            double bbwTr = (upperTr - lowerTr) / basisTr;

            bool back1 = RedAt(1) && RsiAt(1) <= RsiThresholdTrampoline && Close[1] < LowerBBAt(1) && BbwAt(1) > BBThresholdTrampoline;
            bool back2 = RedAt(2) && RsiAt(2) <= RsiThresholdTrampoline && Close[2] < LowerBBAt(2) && BbwAt(2) > BBThresholdTrampoline;
            bool back3 = RedAt(3) && RsiAt(3) <= RsiThresholdTrampoline && Close[3] < LowerBBAt(3) && BbwAt(3) > BBThresholdTrampoline;
            bool back4 = RedAt(4) && RsiAt(4) <= RsiThresholdTrampoline && Close[4] < LowerBBAt(4) && BbwAt(4) > BBThresholdTrampoline;
            bool back5 = RedAt(5) && RsiAt(5) <= RsiThresholdTrampoline && Close[5] < LowerBBAt(5) && BbwAt(5) > BBThresholdTrampoline;

            bool for1 = GreenAt(1) && RsiAt(1) >= RsiUpperTrampoline && Close[1] > UpperBBAt(1) && BbwAt(1) > BBThresholdTrampoline;
            bool for2 = GreenAt(2) && RsiAt(2) >= RsiUpperTrampoline && Close[2] > UpperBBAt(2) && BbwAt(2) > BBThresholdTrampoline;
            bool for3 = GreenAt(3) && RsiAt(3) >= RsiUpperTrampoline && Close[3] > UpperBBAt(3) && BbwAt(3) > BBThresholdTrampoline;
            bool for4 = GreenAt(4) && RsiAt(4) >= RsiUpperTrampoline && Close[4] > UpperBBAt(4) && BbwAt(4) > BBThresholdTrampoline;
            bool for5 = GreenAt(5) && RsiAt(5) >= RsiUpperTrampoline && Close[5] > UpperBBAt(5) && BbwAt(5) > BBThresholdTrampoline;

            bool weGoUpNow = greenCandle && (back1 || back2 || back3 || back4 || back5) && (High[0] > High[1]);
            bool weGoDownNow = redCandle && (for1 || for2 || for3 || for4 || for5) && (Low[0] < Low[1]);
            weGoUpSeries[0] = weGoUpNow;
            weGoDownSeries[0] = weGoDownNow;

            bool upThrust = weGoUpNow && !weGoUpSeries[1] && !weGoUpSeries[2] && !weGoUpSeries[3] && !weGoUpSeries[4];
            bool downThrust = weGoDownNow && !weGoDownSeries[1] && !weGoDownSeries[2] && !weGoDownSeries[3] && !weGoDownSeries[4];

            trampSeries[0] = upThrust || downThrust;
            #endregion

            #region Shark
            bool sharkUp = false, sharkDown = false;
            if (CurrentBar >= 831)
            {
                double basisShark = SMA(rsiUSeries, 30)[0]; // seeded below via rsiUSeries update
                double devShark = 2.0 * StdDev(rsiUSeries, 30)[0];
                double upperShark = basisShark + devShark;
                double lowerShark = basisShark - devShark;
                double rsiSharkVal = rsiTr; // same 14-length RSI reused, matches original
                bool below25 = !ApplyShark2575Rule || rsiSharkVal < 26;
                bool above75 = !ApplyShark2575Rule || rsiSharkVal > 74;
                sharkUp = rsiSharkVal < lowerShark && below25;
                sharkDown = rsiSharkVal > upperShark && above75;
            }
            sharkSeries[0] = sharkUp || sharkDown;
            #endregion

            #region Native PVSRA vector-candle detection
            double avgVol10 = SMA(Volume, 10)[0];
            double highestVolSpread10 = MAX(volSpreadSeries, 10)[1];
            bool climax = Volume[0] >= avgVol10 * 2.0 || volSpreadSeries[0] >= highestVolSpread10;
            bool elevated = Volume[0] >= avgVol10 * 1.5;
            bool vecGreenNow = greenCandle && (climax || elevated);
            bool vecRedNow = redCandle && (climax || elevated);
            vecGreen[0] = vecGreenNow;
            vecRed[0] = vecRedNow;
            #endregion

            #region Simplified market-structure break + early reversal
            Swing sw = Swing(2);
            if (!double.IsNaN(sw.SwingHigh[0])) lastSwingHigh = sw.SwingHigh[0];
            if (!double.IsNaN(sw.SwingLow[0])) lastSwingLow = sw.SwingLow[0];

            bool crossUp = !double.IsNaN(lastSwingHigh) && Close[1] <= lastSwingHigh && Close[0] > lastSwingHigh;
            bool crossDown = !double.IsNaN(lastSwingLow) && Close[1] >= lastSwingLow && Close[0] < lastSwingLow;
            structBreakUp[0] = crossUp;
            structBreakDown[0] = crossDown;

            bool recentBreakUp = structBreakUp[0] || structBreakUp[1] || structBreakUp[2] || structBreakUp[3];
            bool recentBreakDown = structBreakDown[0] || structBreakDown[1] || structBreakDown[2] || structBreakDown[3];
            bool earlyRevUp = recentBreakUp && (vecGreen[0] || vecGreen[1] || vecGreen[2] || vecGreen[3]);
            bool earlyRevDown = recentBreakDown && (vecRed[0] || vecRed[1] || vecRed[2] || vecRed[3]);

            earlySeries[0] = earlyRevUp || earlyRevDown;
            #endregion

            #region Wick Bollinger Bands (John Wick)
            double wbasis = SMA(Close, 20)[0];
            double wdev = 2.5 * StdDev(Close, 20)[0];
            double wupper = wbasis + wdev;
            double wlower = wbasis - wdev;
            bool bBBUp = Low[0] <= wlower && Close[0] >= wlower && redCandle;
            bool bBBDown = High[0] >= wupper && Close[0] < wupper && greenCandle;
            bandsSeries[0] = bBBUp || bBBDown;
            #endregion

            #region Take-profit weighted signal counter (resets every bar, matches Pine)
            int tpCount = 0;
            if (deadRevSeries[0] || deadRevSeries[1]) tpCount += VDeadRev;
            if (luxRevSeries[0] || luxRevSeries[1]) tpCount += VLuxRev;
            if (trampSeries[0] || trampSeries[1]) tpCount += VTramp;
            if (sqSeries[0] || sqSeries[1]) tpCount += VSqueeze;
            if (sharkSeries[0] || sharkSeries[1]) tpCount += VShark;
            if (bandsSeries[0] || bandsSeries[1]) tpCount += VBands;
            if (earlySeries[0] || earlySeries[1]) tpCount += VEarlyRev;
            #endregion

            #region Tidal Wave engine (core trend/reversal state machine)
            bool noOverlapGreenNow = false, noOverlapRedNow = false;
            bool brightGreenNow = false, brightRedNow = false;
            bool gapGreenNow = false, gapRedNow = false;
            int waveStateLocal = waveState[1];

            if (greenCandle && !isDoji)
            {
                int lookback = Math.Min(200, CurrentBar);
                for (int i = 1; i <= lookback; i++)
                {
                    if (brightRed[i]) { break; }
                    else if (waveStateLocal == UpTrend && RedAt(i)) { break; }
                    else if (waveStateLocal == DownTrend && Open[0] >= Close[i] && GreenAt(i))
                    {
                        noOverlapGreenNow = true;
                        brightGreenNow = true;
                        waveStateLocal = UpTrend;
                        break;
                    }
                }
                if (Open[0] >= Close[1] && GreenAt(1))
                {
                    waveStateLocal = UpTrend;
                    brightGreenNow = true;
                    gapGreenNow = true;
                }
            }

            if (redCandle && !isDoji)
            {
                int lookback = Math.Min(200, CurrentBar);
                for (int i = 1; i <= lookback; i++)
                {
                    if (brightGreen[i]) { break; }
                    else if (waveStateLocal == DownTrend && GreenAt(i)) { break; }
                    else if (waveStateLocal == UpTrend && Open[0] <= Close[i] && RedAt(i))
                    {
                        noOverlapRedNow = true;
                        brightRedNow = true;
                        waveStateLocal = DownTrend;
                        break;
                    }
                }
                if (RedAt(1) && Open[0] < Close[1])
                {
                    waveStateLocal = DownTrend;
                    brightRedNow = true;
                    gapRedNow = true;
                }
            }

            waveState[0] = waveStateLocal;
            noOverlapGreen[0] = noOverlapGreenNow;
            noOverlapRed[0] = noOverlapRedNow;
            brightGreen[0] = brightGreenNow;
            brightRed[0] = brightRedNow;
            gapGreen[0] = gapGreenNow;
            gapRed[0] = gapRedNow;
            #endregion

            #region Ultimate Buy/Sell confirmation system
            rsiUSeries[0] = RSI(Close, 32, 1)[0];
            double rsiBasisVal = WMA(rsiUSeries, 32)[0];
            double rsiDev = StdDev(rsiUSeries, 32)[0];
            double upperRsi = rsiBasisVal + 2.0 * rsiDev;
            double lowerRsi = rsiBasisVal - 2.0 * rsiDev;
            double rsiMaVal = WMA(rsiUSeries, 24)[0];

            double priceBasisVal = SMA(Close, 20)[0];
            double priceInnerDev = 2.0 * StdDev(Close, 20)[0];
            double lowerPriceInner = priceBasisVal - priceInnerDev;
            double upperPriceInner = priceBasisVal + priceInnerDev;

            double atrVal = ATR(30)[0];
            double atrMaVal = WMA(Close, 10)[0];
            double upperAtrBand = atrMaVal + atrVal * 1.5;
            double lowerAtrBand = atrMaVal - atrVal * 1.5;

            MACD macdI = MACD(12, 26, 9);
            bool macdBuy = macdI.Avg[1] >= macdI.Default[1] && macdI.Avg[0] < macdI.Default[0];
            bool macdSell = macdI.Avg[1] <= macdI.Default[1] && macdI.Avg[0] > macdI.Default[0];

            bool priceCrossOverInner = Close[1] <= LowerPriceInnerAt(1) && Close[0] > lowerPriceInner;
            bool priceCrossUnderInner = Close[1] >= UpperPriceInnerAt(1) && Close[0] < upperPriceInner;

            bool rsiCrossOverLower = RsiUAt(1) <= LowerRsiAt(1) && rsiUSeries[0] > lowerRsi;
            bool rsiCrossUnderUpper = RsiUAt(1) >= UpperRsiAt(1) && rsiUSeries[0] < upperRsi;

            bool rsiCrossOverBasis = RsiUAt(1) <= RsiBasisAt(1) && rsiUSeries[0] > rsiBasisVal;
            bool rsiCrossUnderBasis = RsiUAt(1) >= RsiBasisAt(1) && rsiUSeries[0] < rsiBasisVal;

            bool rsiCrossOverMa = RsiUAt(1) <= RsiMaAt(1) && rsiUSeries[0] > rsiMaVal;
            bool rsiCrossUnderMa = RsiUAt(1) >= RsiMaAt(1) && rsiUSeries[0] < rsiMaVal;

            bool rsiCrossUnder75 = RsiUAt(1) >= 75 && rsiUSeries[0] < 75;
            bool rsiCrossOver25 = RsiUAt(1) <= 25 && rsiUSeries[0] > 25;

            bool highUnderAtrLower = High[1] >= LowerAtrBandAt(1) && High[0] < lowerAtrBand;
            bool lowOverAtrUpper = Low[1] <= UpperAtrBandAt(1) && Low[0] > upperAtrBand;

            bool buyWatch1 = priceCrossOverInner && !rsiCrossOverLower;
            bool buyWatch2 = rsiCrossOverLower && !priceCrossOverInner;
            bool buyWatch3 = priceCrossOverInner && rsiCrossOverLower;
            bool buyWatch4 = priceCrossOverInner;
            bool buyWatch5 = rsiCrossOverLower;
            bool buyWatch6 = rsiCrossOver25;
            bool buyWatch7 = highUnderAtrLower;

            bool sellWatch1 = priceCrossUnderInner && !rsiCrossUnderUpper;
            bool sellWatch2 = rsiCrossUnderUpper && !priceCrossUnderInner;
            bool sellWatch3 = priceCrossUnderInner && rsiCrossUnderUpper;
            bool sellWatch4 = priceCrossUnderInner;
            bool sellWatch5 = rsiCrossUnderUpper;
            bool sellWatch6 = rsiCrossUnder75;
            bool sellWatch7 = lowOverAtrUpper;

            bool buyWatched = buyWatch1 || buyWatch2 || buyWatch3 || buyWatch4 || buyWatch5 || buyWatch6 || buyWatch7;
            bool sellWatched = sellWatch1 || sellWatch2 || sellWatch3 || sellWatch4 || sellWatch5 || sellWatch6 || sellWatch7;

            buyWatchHist.Add(buyWatched ? 1 : 0);
            sellWatchHist.Add(sellWatched ? 1 : 0);
            while (buyWatchHist.Count > WatchSignalLookback) buyWatchHist.RemoveAt(0);
            while (sellWatchHist.Count > WatchSignalLookback) sellWatchHist.RemoveAt(0);

            bool buyWatchMet = SumList(buyWatchHist) >= 1;
            bool sellWatchMet = SumList(sellWatchHist) >= 1;

            bool combinedBuySignals = rsiCrossOverBasis || rsiCrossOver25 || rsiCrossOverMa;
            bool combinedSellSignals = rsiCrossUnderBasis || rsiCrossUnder75 || rsiCrossUnderMa;

            bool buySignals = RequireWatchSignals ? (buyWatchMet && combinedBuySignals) : combinedBuySignals;
            bool sellSignals = RequireWatchSignals ? (sellWatchMet && combinedSellSignals) : combinedSellSignals;

            bool plotBuyNow = false, plotSellNow = false;
            if (buySignals && !buyWatched)
            {
                plotBuyNow = true;
                buyWatchHist.Clear();
                sellWatchHist.Clear();
            }
            else if (sellSignals && !sellWatched)
            {
                plotSellNow = true;
                buyWatchHist.Clear();
                sellWatchHist.Clear();
            }
            plotBuySeries[0] = plotBuyNow;
            plotSellSeries[0] = plotSellNow;
            #endregion

            #region Combine into final Tidal Wave buy/sell + plots
            bool buyChar = noOverlapGreen[0] && waveState[1] == DownTrend;
            bool sellChar = noOverlapRed[0] && waveState[1] == UpTrend;

            bool bigBuy = plotBuySeries[0] || plotBuySeries[1] || plotBuySeries[2] || plotBuySeries[3];
            bool bigSell = plotSellSeries[0] || plotSellSeries[1] || plotSellSeries[2] || plotSellSeries[3];

            if (ShowBasicSignals && !bigBuy && buyChar)
                Draw.Text(this, "BuyBasic" + CurrentBar, "\u2460", 0, Low[0] - 2 * TickSize, Brushes.Lime);
            if (ShowBasicSignals && !bigSell && sellChar)
                Draw.Text(this, "SellBasic" + CurrentBar, "\u2460", 0, High[0] + 2 * TickSize, Brushes.Red);

            if (ShowStrongSignals && bigBuy && buyChar)
                Draw.Text(this, "BuyStrong" + CurrentBar, "\u2776", 0, Low[0] - 2 * TickSize, Brushes.Lime);
            if (ShowStrongSignals && bigSell && sellChar)
                Draw.Text(this, "SellStrong" + CurrentBar, "\u2776", 0, High[0] + 2 * TickSize, Brushes.Red);

            if (ShowVolumeImbalance && gapGreen[0] && waveState[1] == UpTrend)
                Draw.Text(this, "PlusBuy" + CurrentBar, "+", 0, Low[0] - 2 * TickSize, Brushes.Lime);
            if (ShowVolumeImbalance && gapRed[0] && waveState[1] == DownTrend)
                Draw.Text(this, "PlusSell" + CurrentBar, "+", 0, High[0] + 2 * TickSize, Brushes.Red);

            if (ShowUltimateSignals && plotBuyNow)
                Draw.TriangleUp(this, "UltBuy" + CurrentBar, false, 0, Low[0] - 4 * TickSize, Brushes.DodgerBlue);
            if (ShowUltimateSignals && plotSellNow)
                Draw.TriangleDown(this, "UltSell" + CurrentBar, false, 0, High[0] + 4 * TickSize, Brushes.Fuchsia);

            if (ShowTakeProfitSignals && tpCount >= SignalThresholdPartial && waveStateLocal == UpTrend)
                Draw.Text(this, "TPPartialUp" + CurrentBar, "\u2714", 0, High[0] + 6 * TickSize, Brushes.Lime);
            if (ShowTakeProfitSignals && tpCount >= SignalThresholdPartial && waveStateLocal == DownTrend)
                Draw.Text(this, "TPPartialDn" + CurrentBar, "\u2714", 0, Low[0] - 6 * TickSize, Brushes.Red);

            if (ShowTakeProfitSignals && tpCount >= SignalThresholdFull && waveStateLocal == UpTrend)
                Draw.Text(this, "TPFullUp" + CurrentBar, "\u2713", 0, High[0] + 8 * TickSize, Brushes.MediumPurple);
            if (ShowTakeProfitSignals && tpCount >= SignalThresholdFull && waveStateLocal == DownTrend)
                Draw.Text(this, "TPFullDn" + CurrentBar, "\u2713", 0, Low[0] - 8 * TickSize, Brushes.MediumPurple);
            #endregion

            #region Alerts (mirrors Pine alertcondition calls)
            if (plotBuyNow || plotSellNow)
                Alert("UltimateBuySell" + CurrentBar, Priority.Medium, "Ultimate Buy/Sell Signal", NinjaTrader.Core.Globals.InstallDir + "\\sounds\\Alert1.wav", 10, Brushes.Black, Brushes.Yellow);
            if (!bigBuy && buyChar)
                Alert("BuyBasic" + CurrentBar, Priority.Low, "Buy Signal Basic", NinjaTrader.Core.Globals.InstallDir + "\\sounds\\Alert1.wav", 10, Brushes.Black, Brushes.Lime);
            if (!bigSell && sellChar)
                Alert("SellBasic" + CurrentBar, Priority.Low, "Sell Signal Basic", NinjaTrader.Core.Globals.InstallDir + "\\sounds\\Alert1.wav", 10, Brushes.Black, Brushes.Red);
            if (bigBuy && buyChar)
                Alert("BuyStrong" + CurrentBar, Priority.High, "Buy Signal Super", NinjaTrader.Core.Globals.InstallDir + "\\sounds\\Alert2.wav", 10, Brushes.Black, Brushes.Lime);
            if (bigSell && sellChar)
                Alert("SellStrong" + CurrentBar, Priority.High, "Sell Signal Super", NinjaTrader.Core.Globals.InstallDir + "\\sounds\\Alert2.wav", 10, Brushes.Black, Brushes.Red);
            if (tpCount >= SignalThresholdPartial)
                Alert("TPPartial" + CurrentBar, Priority.Medium, "Take Partial Profit", NinjaTrader.Core.Globals.InstallDir + "\\sounds\\Alert1.wav", 10, Brushes.Black, Brushes.Lime);
            if (tpCount >= SignalThresholdFull)
                Alert("TPFull" + CurrentBar, Priority.Medium, "Take FULL Profit", NinjaTrader.Core.Globals.InstallDir + "\\sounds\\Alert2.wav", 10, Brushes.Black, Brushes.MediumPurple);
            #endregion
        }

        #region Helper methods
        private bool GreenAt(int i) { return Close[i] > Open[i]; }
        private bool RedAt(int i) { return Close[i] < Open[i]; }

        private double LowerBBAt(int i)
        {
            double b = SMA(Close, 20)[i];
            double d = 2.0 * StdDev(Close, 20)[i];
            return b - d;
        }
        private double UpperBBAt(int i)
        {
            double b = SMA(Close, 20)[i];
            double d = 2.0 * StdDev(Close, 20)[i];
            return b + d;
        }
        private double BbwAt(int i)
        {
            double b = SMA(Close, 20)[i];
            double d = 2.0 * StdDev(Close, 20)[i];
            return ((b + d) - (b - d)) / b;
        }
        private double RsiAt(int i) { return RSI(Close, 14, 1)[i]; }

        private double RsiUAt(int i) { return rsiUSeries[i]; }
        private double RsiBasisAt(int i) { return WMA(rsiUSeries, 32)[i]; }
        private double RsiMaAt(int i) { return WMA(rsiUSeries, 24)[i]; }
        private double LowerRsiAt(int i) { return WMA(rsiUSeries, 32)[i] - 2.0 * StdDev(rsiUSeries, 32)[i]; }
        private double UpperRsiAt(int i) { return WMA(rsiUSeries, 32)[i] + 2.0 * StdDev(rsiUSeries, 32)[i]; }

        private double LowerPriceInnerAt(int i) { return SMA(Close, 20)[i] - 2.0 * StdDev(Close, 20)[i]; }
        private double UpperPriceInnerAt(int i) { return SMA(Close, 20)[i] + 2.0 * StdDev(Close, 20)[i]; }

        private double LowerAtrBandAt(int i) { return WMA(Close, 10)[i] - ATR(30)[i] * 1.5; }
        private double UpperAtrBandAt(int i) { return WMA(Close, 10)[i] + ATR(30)[i] * 1.5; }

        private int SumList(List<int> list)
        {
            int s = 0;
            for (int i = 0; i < list.Count; i++) s += list[i];
            return s;
        }

        // Replicates Pine's ta.linreg(source, length, 0): endpoint value of a
        // least-squares regression line fit to the last `length` bars.
        private double LinRegEndpoint(Series<double> series, int length)
        {
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            for (int i = 0; i < length; i++)
            {
                double x = length - 1 - i; // oldest bar = 0, current bar = length-1
                double y = series[i];
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumX2 += x * x;
            }
            double n = length;
            double denom = (n * sumX2 - sumX * sumX);
            if (denom == 0) return series[0];
            double slope = (n * sumXY - sumX * sumY) / denom;
            double intercept = (sumY - slope * sumX) / n;
            return intercept + slope * (length - 1);
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private NebulaSignals[] cacheNebulaSignals;
		public NebulaSignals NebulaSignals(bool showBasicSignals, bool showStrongSignals, bool showVolumeImbalance, bool showTakeProfitSignals, bool showUltimateSignals, int signalThresholdFull, int signalThresholdPartial, bool ignoreDojis, double dojiBodyThreshold, bool requireWatchSignals, int watchSignalLookback, bool applyShark2575Rule, int adxThresholdSqueeze, int squeezeTolerance, double bBThresholdTrampoline, int rsiThresholdTrampoline, int rsiUpperTrampoline)
		{
			return NebulaSignals(Input, showBasicSignals, showStrongSignals, showVolumeImbalance, showTakeProfitSignals, showUltimateSignals, signalThresholdFull, signalThresholdPartial, ignoreDojis, dojiBodyThreshold, requireWatchSignals, watchSignalLookback, applyShark2575Rule, adxThresholdSqueeze, squeezeTolerance, bBThresholdTrampoline, rsiThresholdTrampoline, rsiUpperTrampoline);
		}

		public NebulaSignals NebulaSignals(ISeries<double> input, bool showBasicSignals, bool showStrongSignals, bool showVolumeImbalance, bool showTakeProfitSignals, bool showUltimateSignals, int signalThresholdFull, int signalThresholdPartial, bool ignoreDojis, double dojiBodyThreshold, bool requireWatchSignals, int watchSignalLookback, bool applyShark2575Rule, int adxThresholdSqueeze, int squeezeTolerance, double bBThresholdTrampoline, int rsiThresholdTrampoline, int rsiUpperTrampoline)
		{
			if (cacheNebulaSignals != null)
				for (int idx = 0; idx < cacheNebulaSignals.Length; idx++)
					if (cacheNebulaSignals[idx] != null && cacheNebulaSignals[idx].ShowBasicSignals == showBasicSignals && cacheNebulaSignals[idx].ShowStrongSignals == showStrongSignals && cacheNebulaSignals[idx].ShowVolumeImbalance == showVolumeImbalance && cacheNebulaSignals[idx].ShowTakeProfitSignals == showTakeProfitSignals && cacheNebulaSignals[idx].ShowUltimateSignals == showUltimateSignals && cacheNebulaSignals[idx].SignalThresholdFull == signalThresholdFull && cacheNebulaSignals[idx].SignalThresholdPartial == signalThresholdPartial && cacheNebulaSignals[idx].IgnoreDojis == ignoreDojis && cacheNebulaSignals[idx].DojiBodyThreshold == dojiBodyThreshold && cacheNebulaSignals[idx].RequireWatchSignals == requireWatchSignals && cacheNebulaSignals[idx].WatchSignalLookback == watchSignalLookback && cacheNebulaSignals[idx].ApplyShark2575Rule == applyShark2575Rule && cacheNebulaSignals[idx].AdxThresholdSqueeze == adxThresholdSqueeze && cacheNebulaSignals[idx].SqueezeTolerance == squeezeTolerance && cacheNebulaSignals[idx].BBThresholdTrampoline == bBThresholdTrampoline && cacheNebulaSignals[idx].RsiThresholdTrampoline == rsiThresholdTrampoline && cacheNebulaSignals[idx].RsiUpperTrampoline == rsiUpperTrampoline && cacheNebulaSignals[idx].EqualsInput(input))
						return cacheNebulaSignals[idx];
			return CacheIndicator<NebulaSignals>(new NebulaSignals(){ ShowBasicSignals = showBasicSignals, ShowStrongSignals = showStrongSignals, ShowVolumeImbalance = showVolumeImbalance, ShowTakeProfitSignals = showTakeProfitSignals, ShowUltimateSignals = showUltimateSignals, SignalThresholdFull = signalThresholdFull, SignalThresholdPartial = signalThresholdPartial, IgnoreDojis = ignoreDojis, DojiBodyThreshold = dojiBodyThreshold, RequireWatchSignals = requireWatchSignals, WatchSignalLookback = watchSignalLookback, ApplyShark2575Rule = applyShark2575Rule, AdxThresholdSqueeze = adxThresholdSqueeze, SqueezeTolerance = squeezeTolerance, BBThresholdTrampoline = bBThresholdTrampoline, RsiThresholdTrampoline = rsiThresholdTrampoline, RsiUpperTrampoline = rsiUpperTrampoline }, input, ref cacheNebulaSignals);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.NebulaSignals NebulaSignals(bool showBasicSignals, bool showStrongSignals, bool showVolumeImbalance, bool showTakeProfitSignals, bool showUltimateSignals, int signalThresholdFull, int signalThresholdPartial, bool ignoreDojis, double dojiBodyThreshold, bool requireWatchSignals, int watchSignalLookback, bool applyShark2575Rule, int adxThresholdSqueeze, int squeezeTolerance, double bBThresholdTrampoline, int rsiThresholdTrampoline, int rsiUpperTrampoline)
		{
			return indicator.NebulaSignals(Input, showBasicSignals, showStrongSignals, showVolumeImbalance, showTakeProfitSignals, showUltimateSignals, signalThresholdFull, signalThresholdPartial, ignoreDojis, dojiBodyThreshold, requireWatchSignals, watchSignalLookback, applyShark2575Rule, adxThresholdSqueeze, squeezeTolerance, bBThresholdTrampoline, rsiThresholdTrampoline, rsiUpperTrampoline);
		}

		public Indicators.NebulaSignals NebulaSignals(ISeries<double> input , bool showBasicSignals, bool showStrongSignals, bool showVolumeImbalance, bool showTakeProfitSignals, bool showUltimateSignals, int signalThresholdFull, int signalThresholdPartial, bool ignoreDojis, double dojiBodyThreshold, bool requireWatchSignals, int watchSignalLookback, bool applyShark2575Rule, int adxThresholdSqueeze, int squeezeTolerance, double bBThresholdTrampoline, int rsiThresholdTrampoline, int rsiUpperTrampoline)
		{
			return indicator.NebulaSignals(input, showBasicSignals, showStrongSignals, showVolumeImbalance, showTakeProfitSignals, showUltimateSignals, signalThresholdFull, signalThresholdPartial, ignoreDojis, dojiBodyThreshold, requireWatchSignals, watchSignalLookback, applyShark2575Rule, adxThresholdSqueeze, squeezeTolerance, bBThresholdTrampoline, rsiThresholdTrampoline, rsiUpperTrampoline);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.NebulaSignals NebulaSignals(bool showBasicSignals, bool showStrongSignals, bool showVolumeImbalance, bool showTakeProfitSignals, bool showUltimateSignals, int signalThresholdFull, int signalThresholdPartial, bool ignoreDojis, double dojiBodyThreshold, bool requireWatchSignals, int watchSignalLookback, bool applyShark2575Rule, int adxThresholdSqueeze, int squeezeTolerance, double bBThresholdTrampoline, int rsiThresholdTrampoline, int rsiUpperTrampoline)
		{
			return indicator.NebulaSignals(Input, showBasicSignals, showStrongSignals, showVolumeImbalance, showTakeProfitSignals, showUltimateSignals, signalThresholdFull, signalThresholdPartial, ignoreDojis, dojiBodyThreshold, requireWatchSignals, watchSignalLookback, applyShark2575Rule, adxThresholdSqueeze, squeezeTolerance, bBThresholdTrampoline, rsiThresholdTrampoline, rsiUpperTrampoline);
		}

		public Indicators.NebulaSignals NebulaSignals(ISeries<double> input , bool showBasicSignals, bool showStrongSignals, bool showVolumeImbalance, bool showTakeProfitSignals, bool showUltimateSignals, int signalThresholdFull, int signalThresholdPartial, bool ignoreDojis, double dojiBodyThreshold, bool requireWatchSignals, int watchSignalLookback, bool applyShark2575Rule, int adxThresholdSqueeze, int squeezeTolerance, double bBThresholdTrampoline, int rsiThresholdTrampoline, int rsiUpperTrampoline)
		{
			return indicator.NebulaSignals(input, showBasicSignals, showStrongSignals, showVolumeImbalance, showTakeProfitSignals, showUltimateSignals, signalThresholdFull, signalThresholdPartial, ignoreDojis, dojiBodyThreshold, requireWatchSignals, watchSignalLookback, applyShark2575Rule, adxThresholdSqueeze, squeezeTolerance, bBThresholdTrampoline, rsiThresholdTrampoline, rsiUpperTrampoline);
		}
	}
}

#endregion
