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
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Mechanical strategy wrapper around the NebulaNoCloud indicator.
    ///
    /// Plot contract (read directly from the indicator source, NOT from its comments):
    ///
    ///   Signal        +2 strong buy   +1 basic buy   -1 basic sell   -2 strong sell
    ///   AddSignal     +1 gap continuation long   +2 "vodka" surge long   (negative mirrors short)
    ///   ProfitSignal  +1 FULL exit long   +2 PARTIAL exit long   -1 FULL exit short   -2 PARTIAL exit short
    ///
    /// NOTE on ProfitSignal: the indicator's AddPlot comment says "+1/-1 partial, +2/-2 full",
    /// but OnBarUpdate assigns 1 when tpCount >= MaxProfitThreshold (the FULL case) and 2 when
    /// tpCount >= ProfitThreshold (the PARTIAL case). This strategy follows the code.
    ///
    /// Sign convention: ProfitSignal is positive when the wave state is UpTrend, so a POSITIVE
    /// profit value is an instruction to reduce/close a LONG, not to open one.
    /// </summary>
    public class NebulaSignalStrategy : Strategy
    {
        private NebulaNoCloud nebula;
        private ATR atrStop;
        private EMA trendEma;

        private int longEntryCount;
        private int shortEntryCount;
        private int partialsTaken;
        private int entryBar = -1;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description  = @"Trades the NebulaNoCloud indicator's Signal / AddSignal / ProfitSignal plots.";
                Name         = "NebulaSignalStrategy";

                // The indicator is built for bar-close evaluation. Keep the host on bar close so the
                // hosted instance is not re-evaluated intrabar with unformed candles.
                Calculate                                   = Calculate.OnBarClose;
                EntriesPerDirection                         = 3;
                EntryHandling                               = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy                = true;
                ExitOnSessionCloseSeconds                   = 30;
                IsFillLimitOnTouch                          = false;
                MaximumBarsLookBack                         = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution                         = OrderFillResolution.Standard;
                Slippage                                    = 0;
                StartBehavior                               = StartBehavior.WaitUntilFlat;
                TimeInForce                                 = TimeInForce.Gtc;
                TraceOrders                                 = false;
                RealtimeErrorHandling                       = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling                          = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade                         = 220;   // indicator returns nothing before bar 210
                IsInstantiatedOnEachOptimizationIteration   = false;

                // --- Strategy behaviour defaults ---
                BaseQuantity          = 1;
                StrongSignalsOnly     = false;
                AllowPyramiding       = true;
                VodkaAddsOnly         = true;
                AddQuantity           = 1;
                MaxAddsPerTrade       = 2;
                AllowReversals        = true;
                UsePartialExits       = true;
                PartialExitPercent    = 50;
                MaxPartialsPerTrade   = 1;
                UseAtrStop            = true;
                AtrPeriod             = 14;
                AtrStopMultiple       = 2.0;
                UseAtrTarget          = false;
                AtrTargetMultiple     = 4.0;
                MinBarsBetweenEntries = 1;
                UseEmaFilter          = true;
                EmaFilterPeriod       = 14;
                FilterAppliesToAdds   = true;

                // --- Indicator pass-through defaults (mirror the indicator's own SetDefaults) ---
                IndProfitThreshold    = 5;
                IndMaxProfitThreshold = 7;
                IndWatchLookback      = 35;
                IndSqueezeAdxThreshold= 21;
                IndIgnoreDoji         = false;

                // Off by default: the hosted indicator is a silent calculation engine.
                // Candle coloring, HEMA, imbalance lines, reversal outlines and the dashboard are
                // all already disabled via the constructor arguments below. The signal glyphs,
                // however, are painted unconditionally by the indicator's RenderSignals() - there
                // is no property that turns them off - so the only way to render nothing is to
                // keep this false and skip AddChartIndicator entirely.
                ShowIndicatorOnChart  = false;
            }
            else if (State == State.Configure)
            {
                EntriesPerDirection = Math.Max(1, 1 + (AllowPyramiding ? MaxAddsPerTrade : 0));
            }
            else if (State == State.DataLoaded)
            {
                atrStop  = ATR(AtrPeriod);
                trendEma = EMA(EmaFilterPeriod);

                // All 47 indicator parameters, in declaration order. Cosmetic / drawing options are
                // forced off so the hosted instance does no rendering work during a backtest.
                nebula = NebulaNoCloud(
                    "Simple",                 // cloudType (vestigial - cloud drawing was removed)
                    "None",                   // candleColoring
                    "Standard",               // theme
                    false,                    // showHEMA
                    true,                     // showPlus      -> required for AddSignal +/-1
                    true,                     // showBigPlus   -> required for AddSignal +/-2
                    true,                     // enhanceStrongSignals (unused by the indicator)
                    true,                     // showProfit    -> required for ProfitSignal
                    false,                    // show921
                    IndIgnoreDoji,            // ignoreDoji
                    IndProfitThreshold,       // profitThreshold      -> partial
                    IndMaxProfitThreshold,    // maxProfitThreshold   -> full
                    1,                        // maxDojiTicks
                    false,                    // showVolumeImbalanceLines
                    50,                       // imbalanceLineBars
                    3,                        // imbalanceLineWidth (unused by the indicator)
                    false,                    // showReversalPattern
                    false,                    // showRetests
                    false,                    // showDashboard
                    "Top Right",              // dashboardPosition
                    13,                       // signalIconSize
                    false,                    // useQuadratic921
                    80, 80, 50,               // simple / low / high cloud opacity (vestigial)
                    14,                       // adxLength
                    14,                       // diLength
                    2,                        // fantailAdxLength
                    10.0,                     // fantailWeighting
                    6,                        // fantailMaLength
                    150,                      // waeSensitivity
                    20,                       // waeFastLength
                    40,                       // waeSlowLength
                    20,                       // waeChannelLength
                    2.0,                      // waeMultiplier
                    0.0015,                   // trampolineBbThreshold
                    25,                       // trampolineRsiLower
                    72,                       // trampolineRsiUpper
                    2,                        // squeezeTolerance
                    IndSqueezeAdxThreshold,   // squeezeAdxThreshold
                    IndWatchLookback,         // watchSignalLookback
                    20,                       // alphaLength
                    20,                       // gammaLength
                    21,                       // kernelLookback
                    8,                        // kernelRelativeWeight
                    15,                       // kernelStartBar
                    false                     // enableAlerts - off, otherwise every backtest bar pops a sound
                );

                if (ShowIndicatorOnChart)
                    AddChartIndicator(nebula);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade) return;
            if (BarsInProgress != 0) return;

            int signal = (int)Math.Round(nebula.Signal[0]);
            int add    = (int)Math.Round(nebula.AddSignal[0]);
            int profit = (int)Math.Round(nebula.ProfitSignal[0]);

            MarketPosition mp = Position.MarketPosition;

            // ---------------------------------------------------------------
            // 1. EXITS FIRST. Scaling out on the same bar an entry fires would
            //    otherwise fight the entry order.
            // ---------------------------------------------------------------
            if (profit != 0 && mp != MarketPosition.Flat)
            {
                bool full    = Math.Abs(profit) == 1;   // 1 = full  (tpCount >= MaxProfitThreshold)
                bool partial = Math.Abs(profit) == 2;   // 2 = partial

                // Positive profit signal == uptrend == reduce the LONG side.
                if (profit > 0 && mp == MarketPosition.Long)
                {
                    if (full)
                    {
                        ExitLong("FullTP", "");
                        return;
                    }
                    if (partial && UsePartialExits && partialsTaken < MaxPartialsPerTrade)
                    {
                        int qty = ScaleOutQuantity(Position.Quantity);
                        if (qty > 0) { ExitLong(qty, "PartialTP", ""); partialsTaken++; }
                    }
                }
                else if (profit < 0 && mp == MarketPosition.Short)
                {
                    if (full)
                    {
                        ExitShort("FullTP", "");
                        return;
                    }
                    if (partial && UsePartialExits && partialsTaken < MaxPartialsPerTrade)
                    {
                        int qty = ScaleOutQuantity(Position.Quantity);
                        if (qty > 0) { ExitShort(qty, "PartialTP", ""); partialsTaken++; }
                    }
                }
            }

            // ---------------------------------------------------------------
            // 2. ENTRIES AND REVERSALS
            // ---------------------------------------------------------------
            int minStrength = StrongSignalsOnly ? 2 : 1;

            if (signal != 0 && Math.Abs(signal) >= minStrength)
            {
                bool wantLong  = signal > 0;
                bool wantShort = signal < 0;
                mp = Position.MarketPosition;   // re-read, an exit may have been submitted above

                // EMA filter: longs need a close above the EMA, shorts a close below it.
                // A blocked signal still flattens an opposing position - we suppress new
                // exposure against the trend, we do not hold through an explicit opposite flip.
                bool allowed = wantLong ? LongAllowed() : ShortAllowed();
                if (!allowed)
                {
                    if (wantLong  && mp == MarketPosition.Short) ExitShort("FilteredExit", "");
                    if (wantShort && mp == MarketPosition.Long)  ExitLong("FilteredExit", "");
                    return;
                }

                if (wantLong)
                {
                    if (mp == MarketPosition.Short)
                    {
                        if (AllowReversals)
                        {
                            // A long entry while short closes the short and reverses in one step.
                            SetProtectiveOrders();
                            EnterLong(BaseQuantity, NextLongName());
                            entryBar = CurrentBar;
                        }
                        else
                        {
                            ExitShort("OppositeSignal", "");
                        }
                    }
                    else if (mp == MarketPosition.Flat)
                    {
                        SetProtectiveOrders();
                        EnterLong(BaseQuantity, NextLongName());
                        entryBar = CurrentBar;
                    }
                }
                else if (wantShort)
                {
                    if (mp == MarketPosition.Long)
                    {
                        if (AllowReversals)
                        {
                            SetProtectiveOrders();
                            EnterShort(BaseQuantity, NextShortName());
                            entryBar = CurrentBar;
                        }
                        else
                        {
                            ExitLong("OppositeSignal", "");
                        }
                    }
                    else if (mp == MarketPosition.Flat)
                    {
                        SetProtectiveOrders();
                        EnterShort(BaseQuantity, NextShortName());
                        entryBar = CurrentBar;
                    }
                }
                return;   // a bar that produced an entry does not also add
            }

            // ---------------------------------------------------------------
            // 3. PYRAMID ADDS - only in the direction of an open position
            // ---------------------------------------------------------------
            if (!AllowPyramiding || add == 0) return;
            mp = Position.MarketPosition;
            if (mp == MarketPosition.Flat) return;
            if (CurrentBar - entryBar < MinBarsBetweenEntries) return;

            // AddSignal +/-1 = gap continuation, +/-2 = vodka volume surge.
            int addStrength = Math.Abs(add);
            if (VodkaAddsOnly && addStrength != 2) return;

            if (add > 0 && mp == MarketPosition.Long && longEntryCount <= MaxAddsPerTrade)
            {
                if (FilterAppliesToAdds && !LongAllowed()) return;
                SetProtectiveOrders();
                EnterLong(AddQuantity, NextLongName());
                entryBar = CurrentBar;
            }
            else if (add < 0 && mp == MarketPosition.Short && shortEntryCount <= MaxAddsPerTrade)
            {
                if (FilterAppliesToAdds && !ShortAllowed()) return;
                SetProtectiveOrders();
                EnterShort(AddQuantity, NextShortName());
                entryBar = CurrentBar;
            }
        }

        /// <summary>Longs require the bar to close above the EMA.</summary>
        private bool LongAllowed()
        {
            return !UseEmaFilter || Close[0] > trendEma[0];
        }

        /// <summary>Shorts require the bar to close below the EMA.</summary>
        private bool ShortAllowed()
        {
            return !UseEmaFilter || Close[0] < trendEma[0];
        }

        /// <summary>
        /// ATR-derived stop and optional target, expressed in ticks so they can be recalculated
        /// each bar. Must be called BEFORE the entry order that they should attach to.
        /// </summary>
        private void SetProtectiveOrders()
        {
            double tick = TickSize <= 0 ? 1 : TickSize;

            if (UseAtrStop)
            {
                int stopTicks = (int)Math.Round(atrStop[0] * AtrStopMultiple / tick);
                if (stopTicks > 0) SetStopLoss(CalculationMode.Ticks, stopTicks);
            }

            if (UseAtrTarget)
            {
                int targetTicks = (int)Math.Round(atrStop[0] * AtrTargetMultiple / tick);
                if (targetTicks > 0) SetProfitTarget(CalculationMode.Ticks, targetTicks);
            }
        }

        private int ScaleOutQuantity(int currentQty)
        {
            if (currentQty <= 1) return 0;                  // nothing left to scale out of
            int qty = (int)Math.Round(currentQty * PartialExitPercent / 100.0);
            qty = Math.Max(1, Math.Min(qty, currentQty - 1)); // always leave at least one unit on
            return qty;
        }

        private string NextLongName()  { longEntryCount++;  return "L" + longEntryCount; }
        private string NextShortName() { shortEntryCount++; return "S" + shortEntryCount; }

        protected override void OnPositionUpdate(Position position, double averagePrice,
                                                 int quantity, MarketPosition marketPosition)
        {
            if (marketPosition == MarketPosition.Flat)
            {
                longEntryCount  = 0;
                shortEntryCount = 0;
                partialsTaken   = 0;
                entryBar        = -1;
            }
        }

        #region Properties

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Base Quantity", Order = 1, GroupName = "1. Position Sizing")]
        public int BaseQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Add Quantity", Order = 2, GroupName = "1. Position Sizing")]
        public int AddQuantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Strong Signals Only (ignore basic)", Order = 1, GroupName = "2. Entries")]
        public bool StrongSignalsOnly { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Reversals On Opposite Signal", Order = 2, GroupName = "2. Entries")]
        public bool AllowReversals { get; set; }

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Min Bars Between Entries", Order = 3, GroupName = "2. Entries")]
        public int MinBarsBetweenEntries { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Trend Filter", Order = 4, GroupName = "2. Entries")]
        public bool UseEmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "EMA Filter Period", Order = 5, GroupName = "2. Entries")]
        public int EmaFilterPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Filter Applies To Adds", Order = 6, GroupName = "2. Entries")]
        public bool FilterAppliesToAdds { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Pyramiding", Order = 1, GroupName = "3. Adds")]
        public bool AllowPyramiding { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Vodka Adds Only (skip gap adds)", Order = 2, GroupName = "3. Adds")]
        public bool VodkaAddsOnly { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Max Adds Per Trade", Order = 3, GroupName = "3. Adds")]
        public int MaxAddsPerTrade { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Partial Exits", Order = 1, GroupName = "4. Exits")]
        public bool UsePartialExits { get; set; }

        [NinjaScriptProperty]
        [Range(1, 99)]
        [Display(Name = "Partial Exit Percent", Order = 2, GroupName = "4. Exits")]
        public int PartialExitPercent { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Max Partials Per Trade", Order = 3, GroupName = "4. Exits")]
        public int MaxPartialsPerTrade { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ATR Stop", Order = 1, GroupName = "5. Risk")]
        public bool UseAtrStop { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "ATR Period", Order = 2, GroupName = "5. Risk")]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 20)]
        [Display(Name = "ATR Stop Multiple", Order = 3, GroupName = "5. Risk")]
        public double AtrStopMultiple { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ATR Target", Order = 4, GroupName = "5. Risk")]
        public bool UseAtrTarget { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 50)]
        [Display(Name = "ATR Target Multiple", Order = 5, GroupName = "5. Risk")]
        public double AtrTargetMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(2, 50)]
        [Display(Name = "Partial Profit Score", Order = 1, GroupName = "6. Indicator")]
        public int IndProfitThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(3, 50)]
        [Display(Name = "Full Profit Score", Order = 2, GroupName = "6. Indicator")]
        public int IndMaxProfitThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Watch Lookback", Order = 3, GroupName = "6. Indicator")]
        public int IndWatchLookback { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Squeeze ADX Threshold", Order = 4, GroupName = "6. Indicator")]
        public int IndSqueezeAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Ignore Doji", Order = 5, GroupName = "6. Indicator")]
        public bool IndIgnoreDoji { get; set; }

        /// <summary>
        /// Leave false for a clean chart. Enabling this attaches the hosted indicator and will
        /// paint its signal glyphs (circled numbers, +, checkmarks) - useful for confirming that
        /// the strategy's fills line up with the indicator's marks while debugging.
        /// Candles are never recolored either way.
        /// </summary>
        [NinjaScriptProperty]
        [Display(Name = "Show Signal Glyphs On Chart (debug)", Order = 6, GroupName = "6. Indicator")]
        public bool ShowIndicatorOnChart { get; set; }

        #endregion
    }
}
