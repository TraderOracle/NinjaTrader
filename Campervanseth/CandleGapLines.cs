// ============================================================================
//  CandleGapLines.cs
//
//  NinjaTrader 8 (NinjaScript) indicator.                     Version 1.6.0-NT
//
//  Port of the Sierra Chart ACSIL study "Candle Gap Lines" v1.5.0.
//
//  1.6.0-NT  Separate bullish / bearish line colors. Direction display can be
//            pinned to the top right or bottom right of the panel, arrow inline
//            to the left of the text. Top right is the default.
//
//  Draws a horizontal line extending to the right for every candle gap that
//  occurs between two same-colored candles (two greens in a row where the
//  second candle's body opens above the first candle's close, and the mirror
//  case for reds). Lines are tracked until price comes back and touches them.
//
//  Behaviour
//  ---------
//  - Every line lives in an internal List<GapLineRecord>. Nothing is drawn as a
//    NinjaTrader DrawingTool; the whole registry is painted in OnRender with
//    SharpDX. That removes the entire draw / delete / re-extend dance the
//    ACSIL version needed, and there is no chart-object churn on scroll.
//  - When a later candle touches a line's price level, the line is terminated
//    exactly at that bar and a "line filled" alert is fired.
//  - A gap that is filled within the first few bars is dropped and never
//    alerts, so only lines that actually held are kept.
//  - An on-screen arrow and text show the direction, distance and proximity
//    weighted odds of the nearest unfilled line.
//
//  Differences from the ACSIL original (deliberate)
//  ------------------------------------------------
//  - No LineNumber bookkeeping, no DeleteACSChartDrawing, no HighestLineNumber,
//    no "NewBarFormed" re-draw gate. SharpDX repaints from state every frame.
//  - "Extend active lines past last bar" is a render-time offset in bars.
//  - Alerts fire only in State.Realtime, which is the NT equivalent of the
//    IsFullRecalculation / DownloadingHistoricalData guards.
//  - Diagnostics are printed once at the historical/realtime transition.
//
//  Install: Tools >> Import >> NinjaScript Add-On, or drop in
//           Documents\NinjaTrader 8\bin\Custom\Indicators and compile (F5).
// ============================================================================

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using SharpDX;
using SharpDX.Direct2D1;
#endregion

// ---------------------------------------------------------------------------
// Global scope enums
// ---------------------------------------------------------------------------
public enum CandleGapSource
{
    [Display(Name = "Wicks - High/Low")]    Wicks,
    [Display(Name = "Bodies - Open/Close")] Bodies
}

public enum CandleGapLevelMode
{
    [Display(Name = "Far edge - full fill")]   FarEdge,
    [Display(Name = "Near edge - first touch")] NearEdge,
    [Display(Name = "Both edges")]              BothEdges
}

public enum CandleGapDisplayPosition
{
    [Display(Name = "Follow price - bars right / ticks above")] FollowPrice,
    [Display(Name = "Fixed - top right of panel")]              TopRight,
    [Display(Name = "Fixed - bottom right of panel")]           BottomRight
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class CandleGapLines : Indicator
    {
        private const string CglVersion = "1.6.0-NT";

        // -------------------------------------------------------------------
        // Internal line registry
        // -------------------------------------------------------------------
        public class GapLineRecord
        {
            public int      Id;
            public int      BeginIndex;        // bar the gap completed on
            public int      EndIndex;          // current right edge
            public int      LastCheckedIndex;  // last bar checked for a touch
            public int      FillIndex;         // bar that filled it (-1 while active)
            public bool     IsUpGap;
            public bool     IsActive;
            public bool     IsConfirmed;       // survived the window, alert issued
            public bool     Discarded;         // filled too fast, drop silently
            public int      GapSizeTicks;
            public double   Level;
            public double   GapLow;
            public double   GapHigh;
            public DateTime CreateTime;
        }

        private readonly List<GapLineRecord> lines = new List<GapLineRecord>();

        private int  lastDetectionIndex = -1;
        private int  nextLineId         = 1;
        private bool diagnosticsPrinted;

        // Live display state, computed in OnBarUpdate, consumed in OnRender.
        private int    nextDirection;
        private int    nextDistanceTicks;
        private double nextLevel;
        private double upProbability;

        // Diagnostic counters
        private int barsProcessed, rawGapsFound, rejectedByColor, rejectedBySize;
        private int rejectedByCross, gapsQueued, linesConfirmed, linesDiscarded, largestGapTicks;

        #region OnStateChange
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                        = "CandleGapLines";
                Description                 = "v" + CglVersion + " - Right-extending lines from candle gaps between "
                                            + "same-colored candles. Tracks every line internally, terminates a line at "
                                            + "the bar that touches it, drops lines filled within the first few bars, "
                                            + "and alerts on gap creation and gap fill. Shows an arrow and text for the "
                                            + "direction, distance and proximity weighted odds of the nearest unfilled line.";
                Calculate                   = Calculate.OnEachTick;
                IsOverlay                   = true;
                DrawOnPricePanel            = true;
                DisplayInDataBox            = false;
                PaintPriceMarkers           = false;
                IsSuspendedWhileInactive    = true;
                BarsRequiredToPlot          = 3;

                BarsBetween                 = 0;
                MinGapTicks                 = 1;
                RequireSameColor            = true;
                LevelMode                   = CandleGapLevelMode.NearEdge;
                GapSource                   = CandleGapSource.Bodies;
                RequireCleanGap             = false;

                BullishLineBrush            = Brushes.DeepSkyBlue;
                BearishLineBrush            = Brushes.Orange;
                LineWidth                   = 1;
                ExtendBars                  = 0;
                KeepFilledLine              = true;
                MaxLines                    = 500;

                EnableAlerts                = true;
                NewGapSound                 = "Alert1.wav";
                FillSound                   = "Alert2.wav";
                LogEvents                   = false;
                Diagnostics                 = true;
                MinBarsForFill              = 4;
                MinBarsToDraw               = 1;

                ShowDisplay                 = true;
                DisplayPosition             = CandleGapDisplayPosition.TopRight;
                DisplayMarginX              = 12;
                DisplayMarginY              = 12;
                DisplayOffsetX              = 4;
                DisplayOffsetY              = 40;
                ArrowTextGap                = 10;
                ArrowFontSize               = 24;
                TextFontSize                = 12;
                UpBrush                     = Brushes.Lime;
                DownBrush                   = Brushes.Red;

                AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "ActiveLineCount");
                AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "NewGapFlag");
                AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "FilledFlag");
                AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "NextDirection");
                AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "NextDistanceTicks");
                AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "UpProbability");
            }
            else if (State == State.Configure)
            {
                lines.Clear();
                lastDetectionIndex = -1;
                nextLineId         = 1;
                diagnosticsPrinted = false;
            }
            else if (State == State.Historical)
            {
                // Nothing here on purpose; the registry rebuilds bar by bar.
            }
            else if (State == State.Realtime)
            {
                if (Diagnostics && !diagnosticsPrinted)
                {
                    diagnosticsPrinted = true;
                    Print(string.Format(
                        "Candle Gap Lines v{0} [{1}]: scanned {2} bars | raw gaps {3} | rejected: color {4}, size {5}, crossed {6} | queued {7} | confirmed {8} | discarded as quick fill {9} | largest gap {10} ticks",
                        CglVersion, Instrument != null ? Instrument.MasterInstrument.Name : "?",
                        barsProcessed, rawGapsFound, rejectedByColor, rejectedBySize,
                        rejectedByCross, gapsQueued, linesConfirmed, linesDiscarded, largestGapTicks));
                }
            }
        }
        #endregion

        #region OnBarUpdate
        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(3, BarsBetween + 2))
                return;

            // Current-bar event flags start clean every pass.
            Values[1][0] = 0;
            Values[2][0] = 0;

            double epsilon = TickSize * 0.5;

            // The forming bar can still extend its high/low, so gap detection
            // only ever looks at closed bars.
            int lastClosedBar = (State == State.Historical || Calculate == Calculate.OnBarClose)
                                ? CurrentBar
                                : CurrentBar - 1;

            // ---------------------------------------------------------------
            // 1. Gap detection
            // ---------------------------------------------------------------
            int firstDetectIndex = Math.Max(lastDetectionIndex + 1, BarsBetween + 1);

            for (int barIndex = firstDetectIndex; barIndex <= lastClosedBar; barIndex++)
            {
                lastDetectionIndex = barIndex;
                barsProcessed++;

                int prevIndex = barIndex - BarsBetween - 1;
                if (prevIndex < 0)
                    continue;

                double currentHigh, currentLow, prevHigh, prevLow;

                if (GapSource == CandleGapSource.Bodies)
                {
                    double curOpen  = Bars.GetOpen(barIndex);
                    double curClose = Bars.GetClose(barIndex);
                    double prvOpen  = Bars.GetOpen(prevIndex);
                    double prvClose = Bars.GetClose(prevIndex);

                    currentHigh = Math.Max(curOpen, curClose);
                    currentLow  = Math.Min(curOpen, curClose);
                    prevHigh    = Math.Max(prvOpen, prvClose);
                    prevLow     = Math.Min(prvOpen, prvClose);
                }
                else
                {
                    currentHigh = Bars.GetHigh(barIndex);
                    currentLow  = Bars.GetLow(barIndex);
                    prevHigh    = Bars.GetHigh(prevIndex);
                    prevLow     = Bars.GetLow(prevIndex);
                }

                bool   isUpGap;
                double gapLow, gapHigh;

                // With "Bodies" selected these two tests are exactly the volume
                // imbalance test: currentLow is open[i] on a green candle and
                // prevHigh is close[prev] on a green candle, so the first line
                // reads open[i] > close[prev]. The mirror applies to reds.
                if (currentLow > prevHigh + epsilon)          // gap up
                {
                    isUpGap = true;
                    gapLow  = prevHigh;
                    gapHigh = currentLow;
                }
                else if (currentHigh < prevLow - epsilon)     // gap down
                {
                    isUpGap = false;
                    gapLow  = currentHigh;
                    gapHigh = prevLow;
                }
                else
                {
                    continue;
                }

                rawGapsFound++;

                int gapSizeTicks = (int)Math.Round((gapHigh - gapLow) / TickSize);
                if (gapSizeTicks > largestGapTicks)
                    largestGapTicks = gapSizeTicks;

                // --- color requirement -------------------------------------
                // Stricter than "both the same": a green pair may only produce
                // an up gap and a red pair only a down gap.
                if (RequireSameColor)
                {
                    int colorCurrent  = CandleColor(barIndex);
                    int colorPrevious = CandleColor(prevIndex);

                    bool colorMatchesDirection =
                           (isUpGap  && colorCurrent ==  1 && colorPrevious ==  1)
                        || (!isUpGap && colorCurrent == -1 && colorPrevious == -1);

                    if (!colorMatchesDirection)
                    {
                        rejectedByColor++;
                        continue;
                    }
                }

                // --- minimum gap size --------------------------------------
                if (gapSizeTicks < MinGapTicks)
                {
                    rejectedBySize++;
                    continue;
                }

                // --- optional: bars in between may not cross the gap -------
                // Off by default. In a normal 3-candle imbalance the middle
                // candle is the one that ran through this range, so requiring
                // it to stay clear would reject every single imbalance.
                if (RequireCleanGap)
                {
                    bool gapIsClean = true;

                    for (int innerIndex = prevIndex + 1; innerIndex < barIndex; innerIndex++)
                    {
                        if (isUpGap)
                        {
                            if (Bars.GetLow(innerIndex) < gapHigh - epsilon) { gapIsClean = false; break; }
                        }
                        else
                        {
                            if (Bars.GetHigh(innerIndex) > gapLow + epsilon) { gapIsClean = false; break; }
                        }
                    }

                    if (!gapIsClean)
                    {
                        rejectedByCross++;
                        continue;
                    }
                }

                // --- build the line level(s) -------------------------------
                double farEdge  = isUpGap ? gapLow  : gapHigh;   // full fill level
                double nearEdge = isUpGap ? gapHigh : gapLow;    // first touch level

                double[] levels;
                if (LevelMode == CandleGapLevelMode.FarEdge)
                    levels = new[] { farEdge };
                else if (LevelMode == CandleGapLevelMode.NearEdge)
                    levels = new[] { nearEdge };
                else
                    levels = new[] { nearEdge, farEdge };

                foreach (double level in levels)
                {
                    lines.Add(new GapLineRecord
                    {
                        Id               = nextLineId++,
                        BeginIndex       = barIndex,
                        EndIndex         = CurrentBar + ExtendBars,
                        LastCheckedIndex = barIndex,   // never test the origin bar
                        FillIndex        = -1,
                        IsUpGap          = isUpGap,
                        IsActive         = true,
                        IsConfirmed      = false,
                        Discarded        = false,
                        GapSizeTicks     = gapSizeTicks,
                        Level            = level,
                        GapLow           = gapLow,
                        GapHigh          = gapHigh,
                        CreateTime       = Bars.GetTime(barIndex)
                    });

                    gapsQueued++;
                }
            }

            // ---------------------------------------------------------------
            // 2. Unconfirmed lines. If touched within MinBarsToDraw bars the
            //    line is dropped and no alert is issued. If it survives, the
            //    gap alert fires and it becomes a normal tracked line.
            // ---------------------------------------------------------------
            for (int i = 0; i < lines.Count; i++)
            {
                GapLineRecord line = lines[i];

                if (line.IsConfirmed || line.Discarded || !line.IsActive)
                    continue;

                int confirmAtIndex = line.BeginIndex + MinBarsToDraw;
                int scanEndIndex   = Math.Min(CurrentBar, confirmAtIndex);

                bool touchedEarly = false;

                for (int barIndex = line.BeginIndex + 1; barIndex <= scanEndIndex; barIndex++)
                {
                    if (Bars.GetHigh(barIndex) >= line.Level - epsilon &&
                        Bars.GetLow(barIndex)  <= line.Level + epsilon)
                    {
                        touchedEarly = true;
                        break;
                    }
                }

                if (touchedEarly)
                {
                    line.Discarded = true;
                    line.IsActive  = false;
                    linesDiscarded++;
                    continue;
                }

                // Every bar in the survival window must be closed before we commit.
                if (lastClosedBar < confirmAtIndex)
                {
                    line.EndIndex = CurrentBar + ExtendBars;
                    continue;
                }

                line.IsConfirmed      = true;
                line.LastCheckedIndex = confirmAtIndex;
                line.EndIndex         = CurrentBar + ExtendBars;
                linesConfirmed++;

                SetFlagAt(1, line.BeginIndex, line.IsUpGap ? 1 : -1);

                if (AlertsAllowed())
                {
                    string message = string.Format("Candle gap {0}: {1} ticks, line @ {2}  [{3}]",
                        line.IsUpGap ? "UP" : "DOWN",
                        line.GapSizeTicks,
                        FormatPrice(line.Level),
                        line.CreateTime.ToString("yyyy-MM-dd HH:mm:ss"));

                    Alert("CGL_NEW_" + line.Id, Priority.Medium, message,
                          ResolveSound(NewGapSound), 10, Brushes.Transparent, UpDownBrush(line.IsUpGap));

                    if (LogEvents)
                        Print(message);
                }
            }

            // Drop the discarded records.
            lines.RemoveAll(l => l.Discarded);

            // ---------------------------------------------------------------
            // 3. Touch / fill detection for every confirmed, active line.
            //    The last bar is always re-tested, because it can still extend.
            // ---------------------------------------------------------------
            for (int i = 0; i < lines.Count; i++)
            {
                GapLineRecord line = lines[i];

                if (!line.IsActive || !line.IsConfirmed)
                    continue;

                for (int barIndex = line.LastCheckedIndex + 1; barIndex <= CurrentBar; barIndex++)
                {
                    bool touched = Bars.GetHigh(barIndex) >= line.Level - epsilon
                                && Bars.GetLow(barIndex)  <= line.Level + epsilon;

                    if (!touched)
                        continue;

                    // ---- terminate the line exactly at this bar ----
                    line.IsActive  = false;
                    line.FillIndex = barIndex;
                    line.EndIndex  = barIndex;

                    SetFlagAt(2, barIndex, line.IsUpGap ? 1 : -1);

                    // Bars strictly between the gap bar and the filling bar.
                    // A fill on the very next bar gives 0.
                    int  barsInBetween    = barIndex - line.BeginIndex - 1;
                    bool oldEnoughToAlert = barsInBetween >= MinBarsForFill;

                    if (AlertsAllowed() && oldEnoughToAlert)
                    {
                        string message = string.Format(
                            "Gap line FILLED @ {0}  ({1} gap created {2}, {3} bars in between)",
                            FormatPrice(line.Level),
                            line.IsUpGap ? "up" : "down",
                            line.CreateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                            barsInBetween);

                        Alert("CGL_FILL_" + line.Id, Priority.High, message,
                              ResolveSound(FillSound), 10, Brushes.Transparent, UpDownBrush(line.IsUpGap));

                        if (LogEvents)
                            Print(message);
                    }
                    else if (LogEvents && AlertsAllowed())
                    {
                        Print(string.Format("Gap line filled @ {0} after only {1} bars - alert suppressed",
                                            FormatPrice(line.Level), barsInBetween));
                    }

                    break;
                }

                if (line.IsActive)
                {
                    // Re-check the last bar next time round, it is not final yet.
                    line.LastCheckedIndex = Math.Max(CurrentBar - 1, line.BeginIndex);
                    line.EndIndex         = CurrentBar + ExtendBars;
                }
            }

            // ---------------------------------------------------------------
            // 4. Prune old, already terminated lines.
            // ---------------------------------------------------------------
            if (lines.Count > MaxLines)
            {
                int removeCount = lines.Count - MaxLines;

                for (int i = 0; i < lines.Count && removeCount > 0; )
                {
                    if (!lines[i].IsActive)
                    {
                        lines.RemoveAt(i);
                        removeCount--;
                    }
                    else
                    {
                        i++;
                    }
                }
            }

            // ---------------------------------------------------------------
            // 5. Nearest unfilled line: direction, distance, proximity odds.
            //
            //    A straight inverse-distance split between the closest unfilled
            //    line above and the closest below. Line above 10 ticks away and
            //    the one below 30 gives up 75%. Proximity weighting, not a
            //    measured hit rate.
            // ---------------------------------------------------------------
            double currentPrice = Close[0];

            bool   hasAbove = false, hasBelow = false;
            double nearestAbove = 0.0, nearestBelow = 0.0;

            for (int i = 0; i < lines.Count; i++)
            {
                GapLineRecord line = lines[i];

                if (!line.IsActive || !line.IsConfirmed)
                    continue;

                if (line.Level > currentPrice)
                {
                    if (!hasAbove || line.Level < nearestAbove) { nearestAbove = line.Level; hasAbove = true; }
                }
                else if (line.Level < currentPrice)
                {
                    if (!hasBelow || line.Level > nearestBelow) { nearestBelow = line.Level; hasBelow = true; }
                }
            }

            upProbability     = 0.0;
            nextDirection     = 0;
            nextDistanceTicks = 0;
            nextLevel         = 0.0;

            if (hasAbove || hasBelow)
            {
                double distAbove = hasAbove ? (nearestAbove - currentPrice) : 0.0;
                double distBelow = hasBelow ? (currentPrice - nearestBelow) : 0.0;

                if (hasAbove && hasBelow)
                {
                    double totalDist = distAbove + distBelow;
                    upProbability = totalDist > 0.0 ? distBelow / totalDist : 0.5;
                }
                else
                {
                    upProbability = hasAbove ? 1.0 : 0.0;
                }

                bool goingUp = upProbability >= 0.5;

                nextDirection     = goingUp ? 1 : -1;
                nextLevel         = goingUp ? nearestAbove : nearestBelow;
                nextDistanceTicks = (int)Math.Round((goingUp ? distAbove : distBelow) / TickSize);
            }

            // ---------------------------------------------------------------
            // 6. Publish state for spreadsheets, strategies and other studies.
            // ---------------------------------------------------------------
            int activeCount = 0;
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].IsActive) activeCount++;

            Values[0][0] = activeCount;
            Values[3][0] = nextDirection;
            Values[4][0] = nextDistanceTicks;
            Values[5][0] = upProbability * 100.0;
        }
        #endregion

        #region Helpers
        // 1 = up candle, -1 = down candle, 0 = doji
        private int CandleColor(int index)
        {
            double openPrice  = Bars.GetOpen(index);
            double closePrice = Bars.GetClose(index);

            if (closePrice > openPrice) return 1;
            if (closePrice < openPrice) return -1;
            return 0;
        }

        private void SetFlagAt(int plotIndex, int absoluteBarIndex, double value)
        {
            int barsAgo = CurrentBar - absoluteBarIndex;
            if (barsAgo >= 0 && barsAgo <= CurrentBar)
                Values[plotIndex][barsAgo] = value;
        }

        private bool AlertsAllowed()
        {
            return EnableAlerts && State == State.Realtime;
        }

        private string ResolveSound(string soundFile)
        {
            if (string.IsNullOrWhiteSpace(soundFile))
                return string.Empty;

            if (soundFile.Contains(@"\") || soundFile.Contains("/"))
                return soundFile;

            return NinjaTrader.Core.Globals.InstallDir + @"\sounds\" + soundFile;
        }

        private System.Windows.Media.Brush UpDownBrush(bool isUp)
        {
            return isUp ? UpBrush : DownBrush;
        }

        private string FormatPrice(double price)
        {
            return Instrument != null
                 ? Instrument.MasterInstrument.FormatPrice(price)
                 : price.ToString("0.#####");
        }
        #endregion

        #region OnRender
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartBars == null || RenderTarget == null || CurrentBar < 1)
                return;

            AntialiasMode priorMode = RenderTarget.AntialiasMode;
            RenderTarget.AntialiasMode = AntialiasMode.Aliased;

            try
            {
                RenderGapLines(chartControl, chartScale);
                RenderNearestDisplay(chartControl, chartScale);
            }
            finally
            {
                RenderTarget.AntialiasMode = priorMode;
            }
        }

        private void RenderGapLines(ChartControl chartControl, ChartScale chartScale)
        {
            if (lines.Count == 0)
                return;

            float panelLeft  = ChartPanel.X;
            float panelRight = ChartPanel.X + ChartPanel.W;

            using (SharpDX.Direct2D1.Brush dxBull = BullishLineBrush.ToDxBrush(RenderTarget))
            using (SharpDX.Direct2D1.Brush dxBear = BearishLineBrush.ToDxBrush(RenderTarget))
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    GapLineRecord line = lines[i];

                    if (!line.IsConfirmed)
                        continue;

                    if (!line.IsActive && !KeepFilledLine)
                        continue;

                    int endIndex = line.IsActive
                                 ? Math.Min(CurrentBar, ChartBars.ToIndex) + ExtendBars
                                 : line.FillIndex;

                    if (endIndex < ChartBars.FromIndex || line.BeginIndex > ChartBars.ToIndex + ExtendBars)
                        continue;

                    float x1 = GetX(chartControl, line.BeginIndex);
                    float x2 = GetX(chartControl, endIndex);

                    if (x2 < panelLeft || x1 > panelRight)
                        continue;

                    x1 = Math.Max(x1, panelLeft);
                    x2 = Math.Min(x2, panelRight);

                    float y = chartScale.GetYByValue(line.Level);
                    if (y < ChartPanel.Y || y > ChartPanel.Y + ChartPanel.H)
                        continue;

                    RenderTarget.DrawLine(new Vector2(x1, y), new Vector2(x2, y),
                                          line.IsUpGap ? dxBull : dxBear, LineWidth);
                }
            }
        }

        private void RenderNearestDisplay(ChartControl chartControl, ChartScale chartScale)
        {
            if (!ShowDisplay || nextDirection == 0)
                return;

            bool goingUp = nextDirection == 1;
            System.Windows.Media.Brush wpfBrush = goingUp ? UpBrush : DownBrush;

            string arrowGlyph = goingUp ? "\u25B2" : "\u25BC";
            string displayText = string.Format("{0}  {1} ticks to {2}   |  up {3}%  dn {4}%",
                goingUp ? "UP" : "DOWN",
                nextDistanceTicks,
                FormatPrice(nextLevel),
                (int)Math.Round(upProbability * 100.0),
                100 - (int)Math.Round(upProbability * 100.0));

            using (SharpDX.Direct2D1.Brush dxBrush = wpfBrush.ToDxBrush(RenderTarget))
            {
                if (DisplayPosition == CandleGapDisplayPosition.FollowPrice)
                {
                    // Anchored to the chart data: N bars right of the last bar,
                    // N ticks above the last price, exactly as the ACSIL version.
                    double arrowValue = Close[0] + DisplayOffsetY * TickSize;
                    double textValue  = arrowValue - ArrowTextGap * TickSize;

                    float x = GetX(chartControl, Math.Min(CurrentBar, ChartBars.ToIndex) + DisplayOffsetX);
                    if (x < ChartPanel.X || x > ChartPanel.X + ChartPanel.W)
                        return;

                    DrawText(arrowGlyph, x, chartScale.GetYByValue(arrowValue), ArrowFontSize, dxBrush, false);
                    DrawText(displayText, x, chartScale.GetYByValue(textValue), TextFontSize, dxBrush, false);
                }
                else
                {
                    // Pinned to a panel corner: one right-aligned row, arrow
                    // immediately left of the text, unaffected by scroll/scale.
                    const float inlineGap = 6f;

                    float right = ChartPanel.X + ChartPanel.W - DisplayMarginX;

                    SharpDX.Size2F arrowSize = MeasureText(arrowGlyph, ArrowFontSize);
                    SharpDX.Size2F textSize  = MeasureText(displayText, TextFontSize);

                    float blockHeight = Math.Max(arrowSize.Height, textSize.Height);

                    float centerY = DisplayPosition == CandleGapDisplayPosition.TopRight
                                  ? ChartPanel.Y + DisplayMarginY + blockHeight * 0.5f
                                  : ChartPanel.Y + ChartPanel.H - DisplayMarginY - blockHeight * 0.5f;

                    // Both are right aligned, so the arrow's right edge is the
                    // text's left edge less the gap.
                    DrawText(displayText, right, centerY, TextFontSize, dxBrush, true);
                    DrawText(arrowGlyph, right - textSize.Width - inlineGap, centerY, ArrowFontSize, dxBrush, true);
                }
            }
        }

        private void DrawText(string text, float x, float y, int fontSize,
                              SharpDX.Direct2D1.Brush dxBrush, bool rightAligned)
        {
            using (SharpDX.DirectWrite.TextFormat textFormat = new SharpDX.DirectWrite.TextFormat(
                       NinjaTrader.Core.Globals.DirectWriteFactory, "Arial",
                       SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, fontSize))
            using (SharpDX.DirectWrite.TextLayout textLayout = new SharpDX.DirectWrite.TextLayout(
                       NinjaTrader.Core.Globals.DirectWriteFactory, text, textFormat, 900, fontSize * 2))
            {
                float drawX = rightAligned ? x - textLayout.Metrics.Width : x;
                RenderTarget.DrawTextLayout(new Vector2(drawX, y - textLayout.Metrics.Height * 0.5f),
                                            textLayout, dxBrush);
            }
        }

        private SharpDX.Size2F MeasureText(string text, int fontSize)
        {
            using (SharpDX.DirectWrite.TextFormat textFormat = new SharpDX.DirectWrite.TextFormat(
                       NinjaTrader.Core.Globals.DirectWriteFactory, "Arial",
                       SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, fontSize))
            using (SharpDX.DirectWrite.TextLayout textLayout = new SharpDX.DirectWrite.TextLayout(
                       NinjaTrader.Core.Globals.DirectWriteFactory, text, textFormat, 900, fontSize * 2))
            {
                return new SharpDX.Size2F(textLayout.Metrics.Width, textLayout.Metrics.Height);
            }
        }

        // Handles bar indices to the right of the last plotted bar, which
        // GetXByBarIndex does not extrapolate for.
        private float GetX(ChartControl chartControl, int absoluteBarIndex)
        {
            int lastIndex = ChartBars.ToIndex;

            if (absoluteBarIndex <= lastIndex)
                return chartControl.GetXByBarIndex(ChartBars, absoluteBarIndex);

            float lastX = chartControl.GetXByBarIndex(ChartBars, lastIndex);
            return lastX + (absoluteBarIndex - lastIndex) * (float)chartControl.Properties.BarDistance;
        }
        #endregion

        #region Properties - Detection
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Category("1. Detection")]
        [DisplayName("Bars between the two gap candles (0 = adjacent)")]
        public int BarsBetween { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100000)]
        [Category("1. Detection")]
        [DisplayName("Minimum gap size (ticks)")]
        public int MinGapTicks { get; set; }

        [NinjaScriptProperty]
        [Category("1. Detection")]
        [DisplayName("Require both candles same color")]
        public bool RequireSameColor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Line level", GroupName = "1. Detection", Order = 4)]
        public CandleGapLevelMode LevelMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Gap measured between", GroupName = "1. Detection", Order = 5)]
        public CandleGapSource GapSource { get; set; }

        [NinjaScriptProperty]
        [Category("1. Detection")]
        [DisplayName("Bars in between may not cross the gap")]
        public bool RequireCleanGap { get; set; }
        #endregion

        #region Properties - Lines
        [XmlIgnore]
        [Category("2. Lines")]
        [DisplayName("Bullish line color (gap up)")]
        public System.Windows.Media.Brush BullishLineBrush { get; set; }

        [Browsable(false)]
        public string BullishLineBrushSerialize
        {
            get { return Serialize.BrushToString(BullishLineBrush); }
            set { BullishLineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Category("2. Lines")]
        [DisplayName("Bearish line color (gap down)")]
        public System.Windows.Media.Brush BearishLineBrush { get; set; }

        [Browsable(false)]
        public string BearishLineBrushSerialize
        {
            get { return Serialize.BrushToString(BearishLineBrush); }
            set { BearishLineBrush = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Category("2. Lines")]
        [DisplayName("Line width")]
        public int LineWidth { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Category("2. Lines")]
        [DisplayName("Extend active lines past last bar (bars)")]
        public int ExtendBars { get; set; }

        [NinjaScriptProperty]
        [Category("2. Lines")]
        [DisplayName("Keep terminated line visible")]
        public bool KeepFilledLine { get; set; }

        [NinjaScriptProperty]
        [Range(10, 20000)]
        [Category("2. Lines")]
        [DisplayName("Maximum lines to track")]
        public int MaxLines { get; set; }
        #endregion

        #region Properties - Alerts
        [NinjaScriptProperty]
        [Category("3. Alerts")]
        [DisplayName("Enable alerts")]
        public bool EnableAlerts { get; set; }

        [NinjaScriptProperty]
        [Category("3. Alerts")]
        [DisplayName("Alert sound - new gap")]
        public string NewGapSound { get; set; }

        [NinjaScriptProperty]
        [Category("3. Alerts")]
        [DisplayName("Alert sound - line filled")]
        public string FillSound { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Category("3. Alerts")]
        [DisplayName("Minimum bars between gap and fill to alert")]
        public int MinBarsForFill { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Category("3. Alerts")]
        [DisplayName("Drop line if filled within this many bars")]
        public int MinBarsToDraw { get; set; }

        [NinjaScriptProperty]
        [Category("3. Alerts")]
        [DisplayName("Write events to output window")]
        public bool LogEvents { get; set; }

        [NinjaScriptProperty]
        [Category("3. Alerts")]
        [DisplayName("Diagnostics to output window")]
        public bool Diagnostics { get; set; }
        #endregion

        #region Properties - Display
        [NinjaScriptProperty]
        [Category("4. Display")]
        [DisplayName("Show nearest line direction display")]
        public bool ShowDisplay { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Display position", GroupName = "4. Display", Order = 2)]
        public CandleGapDisplayPosition DisplayPosition { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Category("4. Display")]
        [DisplayName("Fixed position - margin from right edge (pixels)")]
        public int DisplayMarginX { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Category("4. Display")]
        [DisplayName("Fixed position - margin from top/bottom edge (pixels)")]
        public int DisplayMarginY { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Category("4. Display")]
        [DisplayName("Follow price - bars right of last bar")]
        public int DisplayOffsetX { get; set; }

        [NinjaScriptProperty]
        [Range(-100000, 100000)]
        [Category("4. Display")]
        [DisplayName("Follow price - ticks above last price")]
        public int DisplayOffsetY { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Category("4. Display")]
        [DisplayName("Gap between arrow and text (ticks)")]
        public int ArrowTextGap { get; set; }

        [NinjaScriptProperty]
        [Range(4, 200)]
        [Category("4. Display")]
        [DisplayName("Arrow font size")]
        public int ArrowFontSize { get; set; }

        [NinjaScriptProperty]
        [Range(4, 200)]
        [Category("4. Display")]
        [DisplayName("Text font size")]
        public int TextFontSize { get; set; }

        [XmlIgnore]
        [Category("4. Display")]
        [DisplayName("Display color - up")]
        public System.Windows.Media.Brush UpBrush { get; set; }

        [Browsable(false)]
        public string UpBrushSerialize
        {
            get { return Serialize.BrushToString(UpBrush); }
            set { UpBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Category("4. Display")]
        [DisplayName("Display color - down")]
        public System.Windows.Media.Brush DownBrush { get; set; }

        [Browsable(false)]
        public string DownBrushSerialize
        {
            get { return Serialize.BrushToString(DownBrush); }
            set { DownBrush = Serialize.StringToBrush(value); }
        }
        #endregion

        #region Exposed series
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> ActiveLineCount { get { return Values[0]; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> NewGapFlag { get { return Values[1]; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> FilledFlag { get { return Values[2]; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> NextDirection { get { return Values[3]; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> NextDistanceTicks { get { return Values[4]; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> UpProbability { get { return Values[5]; } }
        #endregion
    }
}
