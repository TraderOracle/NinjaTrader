// ZScoreChannel.cs
//
// Rolling Z-Score Channel — NinjaTrader 8 NinjaScript port of the Pine v6
// "Adaptive Rolling Z-Score Channel" indicator (zchann.txt), math-verified
// against the ACSIL Sierra Chart port (ZScoreChannel.cpp). Computes a
// rolling mean/stdev of the source, converts price to a z-score, derives
// band levels either as fixed z-values or adaptively from a weighted
// multi-period blend of rolling percentiles of the z-score, optionally
// smooths the z-score and/or band levels with one of four filters (LinReg,
// Hull, Super Smoother, Two-Pole Gaussian), inverts back to price space,
// and plots bands + basis with region fills, gradient coloring, opt-in bar
// coloring, re-entry markers, and alerts.
//
// Fixes carried over from the ACSIL port's Pine analysis:
//  1. The minimum-band-Z floor is re-applied AFTER smoothing the band
//     levels (Pine only floors before smoothing, which lets an overshoot
//     collapse or invert the channel).
//  2. Plotting/state accumulation for the band pipeline is suppressed
//     until the longest required window has real data (the NT8 analogue
//     of sc.DataStartIndex), so the chart never draws the degenerate
//     warmup region.

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public enum ZChannelBandMode
    {
        FixedZScore,
        Adaptive
    }

    public enum ZChannelSmoothingType
    {
        LinReg,
        HullMA,
        SuperSmoother,
        TwoPoleGaussian
    }

    public class RollingZScoreChannel : Indicator
    {
        // ============================================================
        // CONSTANTS
        // ============================================================

        // Matches the input limits on the three blend-period properties
        // (10..2000), so the reusable sort buffer can always hold one full
        // blend window.
        private const int MaxBlendPeriod = 2000;

        // ============================================================
        // SERIES STATE (mirrors the ACSIL extra Subgraph.Arrays[] slots)
        // ============================================================

        private Series<double> srcSeries;     // copy of Input with unlimited lookback (RollingWindow can
                                              // exceed the 256-bar default of a hosted input series)
        private Series<double> zRawSeries;    // raw (pre-smoothing) z-score input to the z smoothers
        private Series<double> zScoreSeries;  // final selected z-score (public readout ZScore)
        private Series<double> zUpFlrSeries;  // floored upper-Z input to the band smoothers
        private Series<double> zDnFlrSeries;  // floored lower-Z input to the band smoothers

        private Series<double> zHullDiff;     // Hull diff state (z)
        private Series<double> upHullDiff;    // Hull diff state (upper Z)
        private Series<double> dnHullDiff;    // Hull diff state (lower Z)

        private Series<double> zRecursive;    // SuperSmoother/Gaussian output state (z)
        private Series<double> upRecursive;   // SuperSmoother/Gaussian output state (upper Z)
        private Series<double> dnRecursive;   // SuperSmoother/Gaussian output state (lower Z)

        private Series<double> finalZUp;      // final upper Z after clamp/smooth/re-clamp (public readout UpperZ)
        private Series<double> finalZDn;      // final lower Z after clamp/smooth/re-clamp (public readout LowerZ)

        private double[] sortBuffer;          // reusable percentile sort buffer, sized MaxBlendPeriod
        private Dictionary<int, Brush> brushCache; // packed-RGB -> frozen SolidColorBrush cache

        // ============================================================
        // PUBLIC READOUTS (not plots — an overlay plot would wreck price
        // autoscale — exposed as data-window-style Series<double> instead)
        // ============================================================

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> ZScore
        {
            get { return zScoreSeries; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> UpperZ
        {
            get { return finalZUp; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> LowerZ
        {
            get { return finalZDn; }
        }

        // ============================================================
        // STATE LIFECYCLE
        // ============================================================

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                      = "RollingZScoreChannel";
                Description               = "Adaptive rolling z-score channel ported from Pine v6. Computes a "
                                           + "rolling mean/stdev z-score of the source, derives band levels "
                                           + "from a fixed z or a weighted multi-period percentile blend, "
                                           + "optionally smooths z-score and/or bands, and inverts to price.";
                Calculate                 = Calculate.OnBarClose;
                IsOverlay                 = true;
                DrawOnPricePanel          = true;
                IsSuspendedWhileInactive  = true;

                AddPlot(new Stroke(Brushes.Crimson, 2), PlotStyle.Line, "Upper Band");
                AddPlot(new Stroke(Brushes.MediumSeaGreen, 2), PlotStyle.Line, "Lower Band");
                AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "Basis");
                AddPlot(new Stroke(Brushes.Crimson, DashStyleHelper.Dot, 1), PlotStyle.Line, "Inner Upper");
                AddPlot(new Stroke(Brushes.MediumSeaGreen, DashStyleHelper.Dot, 1), PlotStyle.Line, "Inner Lower");

                // ---- Functional defaults ----
                RollingWindow         = 80;
                BandMode              = ZChannelBandMode.Adaptive;
                FixedUpperZ           = 2.0;
                FixedLowerZ           = -2.0;
                ForceSymmetricBands   = false;
                MinimumBandZ          = 0.5;
                InnerBandFraction     = 0.5;
                UpperPercentile       = 95.0;
                LowerPercentile       = 5.0;
                SmoothZScore          = false;
                SmoothBandLevels      = false;
                SmoothingType         = ZChannelSmoothingType.TwoPoleGaussian;
                SmoothingLength       = 5;
                BlendPeriodShort      = 50;
                BlendPeriodMedium     = 100;
                BlendPeriodLong       = 200;
                BlendWeightShort      = 1.0;
                BlendWeightMedium     = 1.0;
                BlendWeightLong       = 1.0;

                // ---- Visual-only defaults ----
                GradientBasisColoring = true;
                ColorBars             = false;
                ShowRegionFill        = true;
                RegionOpacity         = 10;
                ShowSignals           = true;
                EnableAlerts          = false;
                AlertRearmSeconds     = 10;
            }
            else if (State == State.DataLoaded)
            {
                // zScoreSeries feeds percentile windows up to MaxBlendPeriod bars
                // back, so it needs unlimited lookback. Everything else here is
                // only ever read a handful of bars back (smoothing length <= 50,
                // signals need at most barsAgo 2), so the default lookback is fine.
                srcSeries    = new Series<double>(this, MaximumBarsLookBack.Infinite);
                zRawSeries   = new Series<double>(this);
                zScoreSeries = new Series<double>(this, MaximumBarsLookBack.Infinite);
                zUpFlrSeries = new Series<double>(this);
                zDnFlrSeries = new Series<double>(this);

                zHullDiff    = new Series<double>(this);
                upHullDiff   = new Series<double>(this);
                dnHullDiff   = new Series<double>(this);

                zRecursive   = new Series<double>(this);
                upRecursive  = new Series<double>(this);
                dnRecursive  = new Series<double>(this);

                finalZUp     = new Series<double>(this);
                finalZDn     = new Series<double>(this);

                sortBuffer   = new double[MaxBlendPeriod];
                brushCache   = new Dictionary<int, Brush>();
            }
        }

        // ============================================================
        // MAIN PIPELINE
        // ============================================================

        protected override void OnBarUpdate()
        {
            // ---- warmup threshold (mirrors ACSIL sc.DataStartIndex derivation) ----
            bool isFixed = BandMode == ZChannelBandMode.FixedZScore;
            int longestBlend = 0;
            if (BlendWeightShort > 0.0)  longestBlend = Math.Max(longestBlend, BlendPeriodShort);
            if (BlendWeightMedium > 0.0) longestBlend = Math.Max(longestBlend, BlendPeriodMedium);
            if (BlendWeightLong > 0.0)   longestBlend = Math.Max(longestBlend, BlendPeriodLong);
            bool adaptiveInUse = !isFixed && longestBlend > 0;
            int startBar = adaptiveInUse ? (Math.Max(RollingWindow, longestBlend) - 1) : (RollingWindow - 1);

            // ============================================================
            // LOCATION AND DISPERSION + RAW Z-SCORE (always computed,
            // zero before RollingWindow-1 bars — matches ACSIL's
            // zero-seeded warmup)
            // ============================================================
            srcSeries[0] = Input[0];

            bool haveRollWin = CurrentBar >= RollingWindow - 1;
            double mean = 0.0, disp = 0.0;
            if (haveRollWin)
                RollingMeanStdev(srcSeries, RollingWindow, out mean, out disp);

            double zRawVal = (haveRollWin && disp > 1e-12) ? (Input[0] - mean) / disp : 0.0;
            zRawSeries[0] = zRawVal;

            // ============================================================
            // SMOOTHED Z-SCORE (only the selected smoother is computed)
            // ============================================================
            double smZ;
            switch (SmoothingType)
            {
                case ZChannelSmoothingType.LinReg:
                    smZ = LinRegEndpoint(zRawSeries, CurrentBar, SmoothingLength);
                    break;
                case ZChannelSmoothingType.HullMA:
                    smZ = HullMa(zRawSeries, zHullDiff, CurrentBar, SmoothingLength);
                    break;
                case ZChannelSmoothingType.SuperSmoother:
                    smZ = SuperSmootherStep(zRawSeries, zRecursive, CurrentBar, SmoothingLength);
                    break;
                default: // TwoPoleGaussian
                    smZ = TwoPoleGaussianStep(zRawSeries, zRecursive, CurrentBar, SmoothingLength);
                    break;
            }

            double zScoreVal = SmoothZScore ? smZ : zRawVal;
            zScoreSeries[0] = zScoreVal;

            // ============================================================
            // PERCENTILE BAND LEVELS IN Z UNITS (adaptive multi-period
            // blend — skipped in Fixed mode and for zero-weight periods)
            // ============================================================
            double wSum = 0.0, upWSum = 0.0, dnWSum = 0.0;
            if (adaptiveInUse)
            {
                double upP, dnP;
                if (BlendWeightShort > 0.0 && PercentileBlendPair(zScoreSeries, CurrentBar, BlendPeriodShort, UpperPercentile, LowerPercentile, sortBuffer, out upP, out dnP))
                {
                    upWSum += upP * BlendWeightShort;
                    dnWSum += dnP * BlendWeightShort;
                    wSum   += BlendWeightShort;
                }
                if (BlendWeightMedium > 0.0 && PercentileBlendPair(zScoreSeries, CurrentBar, BlendPeriodMedium, UpperPercentile, LowerPercentile, sortBuffer, out upP, out dnP))
                {
                    upWSum += upP * BlendWeightMedium;
                    dnWSum += dnP * BlendWeightMedium;
                    wSum   += BlendWeightMedium;
                }
                if (BlendWeightLong > 0.0 && PercentileBlendPair(zScoreSeries, CurrentBar, BlendPeriodLong, UpperPercentile, LowerPercentile, sortBuffer, out upP, out dnP))
                {
                    upWSum += upP * BlendWeightLong;
                    dnWSum += dnP * BlendWeightLong;
                    wSum   += BlendWeightLong;
                }
            }

            bool adaptiveAvailable = wSum > 0.0;
            double zAdaptUp = adaptiveAvailable ? upWSum / wSum : 0.0;
            double zAdaptDn = adaptiveAvailable ? dnWSum / wSum : 0.0;

            double zUpSel = isFixed ? FixedUpperZ : (adaptiveAvailable ? zAdaptUp : FixedUpperZ);
            double zDnSel = isFixed ? FixedLowerZ : (adaptiveAvailable ? zAdaptDn : FixedLowerZ);

            double zUpFlrVal = Math.Max(zUpSel, MinimumBandZ);
            double zDnFlrVal = Math.Min(zDnSel, -MinimumBandZ);

            zUpFlrSeries[0] = zUpFlrVal;
            zDnFlrSeries[0] = zDnFlrVal;

            // ============================================================
            // SMOOTHED BAND LEVELS (re-clamped after smoothing — fix #1)
            // ============================================================
            double smU, smD;
            switch (SmoothingType)
            {
                case ZChannelSmoothingType.LinReg:
                    smU = LinRegEndpoint(zUpFlrSeries, CurrentBar, SmoothingLength);
                    smD = LinRegEndpoint(zDnFlrSeries, CurrentBar, SmoothingLength);
                    break;
                case ZChannelSmoothingType.HullMA:
                    smU = HullMa(zUpFlrSeries, upHullDiff, CurrentBar, SmoothingLength);
                    smD = HullMa(zDnFlrSeries, dnHullDiff, CurrentBar, SmoothingLength);
                    break;
                case ZChannelSmoothingType.SuperSmoother:
                    smU = SuperSmootherStep(zUpFlrSeries, upRecursive, CurrentBar, SmoothingLength);
                    smD = SuperSmootherStep(zDnFlrSeries, dnRecursive, CurrentBar, SmoothingLength);
                    break;
                default:
                    smU = TwoPoleGaussianStep(zUpFlrSeries, upRecursive, CurrentBar, SmoothingLength);
                    smD = TwoPoleGaussianStep(zDnFlrSeries, dnRecursive, CurrentBar, SmoothingLength);
                    break;
            }

            double zUp, zDn;
            if (SmoothBandLevels)
            {
                zUp = Math.Max(smU, MinimumBandZ);
                zDn = Math.Min(smD, -MinimumBandZ);
            }
            else
            {
                zUp = zUpFlrVal;
                zDn = zDnFlrVal;
            }

            if (ForceSymmetricBands)
            {
                double mag = Math.Max(Math.Abs(zUp), Math.Abs(zDn));
                zUp = mag;
                zDn = -mag;
            }

            finalZUp[0] = zUp;
            finalZDn[0] = zDn;

            // ============================================================
            // INVERSION BACK TO PRICE
            // ============================================================
            double basis   = mean;
            double upper   = mean + zUp * disp;
            double lower   = mean + zDn * disp;
            double innerUp = mean + zUp * InnerBandFraction * disp;
            double innerDn = mean + zDn * InnerBandFraction * disp;

            Values[0][0] = upper;
            Values[1][0] = lower;
            Values[2][0] = basis;
            Values[3][0] = innerUp;
            Values[4][0] = innerDn;

            // ---- suppress DRAWING before the warmup threshold (fix #2). The
            // full pipeline above still runs every bar so all smoother and
            // percentile state is warm at startBar, exactly like the ACSIL
            // port (which computes unconditionally and hides the region via
            // sc.DataStartIndex). ----
            if (CurrentBar < startBar)
            {
                for (int i = 0; i <= 4; i++)
                    Values[i].Reset();
                return;
            }

            // ============================================================
            // REGION FILLS (NT8-specific; ACSIL renders fills via native
            // TRANSPARENT_FILL_TOP/BOTTOM subgraphs instead)
            // ============================================================
            if (ShowRegionFill)
            {
                int fillStartBarsAgo = CurrentBar - startBar;
                Draw.Region(this, "zchUpperZone", fillStartBarsAgo, 0, Values[0], Values[2], null, Plots[0].Brush, RegionOpacity);
                Draw.Region(this, "zchLowerZone", fillStartBarsAgo, 0, Values[2], Values[1], null, Plots[1].Brush, RegionOpacity);
            }

            // ============================================================
            // COLORS (gradient basis coloring + opt-in bar coloring).
            // Base colors come from Plots[i].Brush — the user-configured
            // plot colors — never from PlotBrushes, which holds per-bar
            // overrides (reading it back would compound earlier gradient
            // writes). PlotBrushes is barsAgo-indexed like every series.
            // ============================================================
            Brush gradBrush = Plots[2].Brush;
            if (GradientBasisColoring)
            {
                byte bR, bG, bB;
                if (TryGetRgb(Plots[2].Brush, out bR, out bG, out bB))
                {
                    byte tR = bR, tG = bG, tB = bB;
                    if (zScoreVal >= 0.0)
                    {
                        byte uR, uG, uB;
                        if (TryGetRgb(Plots[0].Brush, out uR, out uG, out uB))
                        {
                            double t = zUp > 1e-12 ? zScoreVal / zUp : 0.0;
                            t = Math.Max(0.0, Math.Min(1.0, t));
                            tR = LerpByte(bR, uR, t);
                            tG = LerpByte(bG, uG, t);
                            tB = LerpByte(bB, uB, t);
                        }
                    }
                    else
                    {
                        byte lR, lG, lB;
                        if (TryGetRgb(Plots[1].Brush, out lR, out lG, out lB))
                        {
                            double t = zDn < -1e-12 ? zScoreVal / zDn : 0.0;
                            t = Math.Max(0.0, Math.Min(1.0, t));
                            tR = LerpByte(bR, lR, t);
                            tG = LerpByte(bG, lG, t);
                            tB = LerpByte(bB, lB, t);
                        }
                    }
                    gradBrush = GetCachedBrush(tR, tG, tB);
                    PlotBrushes[2][0] = gradBrush;
                }
            }

            if (ColorBars)
            {
                Brush barColor;
                if (Input[0] > upper)
                    barColor = Plots[0].Brush;
                else if (Input[0] < lower)
                    barColor = Plots[1].Brush;
                else
                    barColor = gradBrush;

                BarBrush            = barColor;
                CandleOutlineBrush  = barColor;
            }

            // ============================================================
            // SIGNALS AND ALERTS (Pine crossover/crossunder semantics;
            // closed-bar safe regardless of the user's Calculate mode)
            // ============================================================
            if (ShowSignals || EnableAlerts)
            {
                if (Calculate == Calculate.OnBarClose)
                    EvaluateCrossSignalsAndAlerts(0);
                else if (IsFirstTickOfBar)
                    EvaluateCrossSignalsAndAlerts(1);
            }
        }

        // ============================================================
        // SIGNALS / ALERTS HELPER
        // (Pine ta.crossover / ta.crossunder semantics: strictly beyond
        // NOW, and NOT strictly beyond on the previous bar. `offset` is
        // the barsAgo of "now": 0 for OnBarClose, 1 for the just-closed
        // bar under any other Calculate mode.)
        // ============================================================
        private void EvaluateCrossSignalsAndAlerts(int offset)
        {
            int prevOffset = offset + 1;
            if (!Values[0].IsValidDataPoint(prevOffset))
                return;

            double curSrc   = Input[offset];
            double curUpper = Values[0][offset];
            double curLower = Values[1][offset];
            double curBasis = Values[2][offset];
            double prevSrc   = Input[prevOffset];
            double prevUpper = Values[0][prevOffset];
            double prevLower = Values[1][prevOffset];
            double prevBasis = Values[2][prevOffset];

            bool reentryUp       = CrossesOver(curSrc, curLower, prevSrc, prevLower);
            bool reentryDn       = CrossesUnder(curSrc, curUpper, prevSrc, prevUpper);
            bool crossOverUpper  = CrossesOver(curSrc, curUpper, prevSrc, prevUpper);
            bool crossUnderLower = CrossesUnder(curSrc, curLower, prevSrc, prevLower);
            bool crossOverBasis  = CrossesOver(curSrc, curBasis, prevSrc, prevBasis);
            bool crossUnderBasis = CrossesUnder(curSrc, curBasis, prevSrc, prevBasis);

            if (ShowSignals)
            {
                if (reentryUp)
                    Draw.ArrowUp(this, "zchReUp" + CurrentBar, true, offset, Low[offset] - 2 * TickSize, Plots[1].Brush);
                if (reentryDn)
                    Draw.ArrowDown(this, "zchReDn" + CurrentBar, true, offset, High[offset] + 2 * TickSize, Plots[0].Brush);
            }

            if (EnableAlerts && State == State.Realtime)
            {
                if (crossOverUpper)
                    Alert("ZChannelBreakAboveUpper", Priority.Medium, "Break Above Upper", "", AlertRearmSeconds, Brushes.Black, Brushes.White);
                if (crossUnderLower)
                    Alert("ZChannelBreakBelowLower", Priority.Medium, "Break Below Lower", "", AlertRearmSeconds, Brushes.Black, Brushes.White);
                if (reentryDn)
                    Alert("ZChannelReEntryFromAbove", Priority.Medium, "Re-Entry From Above", "", AlertRearmSeconds, Brushes.Black, Brushes.White);
                if (reentryUp)
                    Alert("ZChannelReEntryFromBelow", Priority.Medium, "Re-Entry From Below", "", AlertRearmSeconds, Brushes.Black, Brushes.White);
                if (crossOverBasis)
                    Alert("ZChannelBasisCrossUp", Priority.Medium, "Basis Cross Up", "", AlertRearmSeconds, Brushes.Black, Brushes.White);
                if (crossUnderBasis)
                    Alert("ZChannelBasisCrossDown", Priority.Medium, "Basis Cross Down", "", AlertRearmSeconds, Brushes.Black, Brushes.White);
            }
        }

        // ============================================================
        // COLOR HELPERS
        // ============================================================

        private static bool TryGetRgb(Brush brush, out byte r, out byte g, out byte b)
        {
            SolidColorBrush solid = brush as SolidColorBrush;
            if (solid == null)
            {
                r = 0; g = 0; b = 0;
                return false;
            }
            r = solid.Color.R;
            g = solid.Color.G;
            b = solid.Color.B;
            return true;
        }

        private static byte LerpByte(byte a, byte b, double t)
        {
            int v = (int)Math.Round(a + (b - a) * t, MidpointRounding.AwayFromZero);
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return (byte)v;
        }

        private Brush GetCachedBrush(byte r, byte g, byte b)
        {
            int key = (r << 16) | (g << 8) | b;
            Brush brush;
            if (!brushCache.TryGetValue(key, out brush))
            {
                brush = new SolidColorBrush(Color.FromRgb(r, g, b));
                brush.Freeze();
                brushCache[key] = brush;
            }
            return brush;
        }

        // ============================================================
        // PURE MATH HELPERS
        // (Formula-for-formula mirrors of ZScoreChannel.cpp / harness.cpp
        // — keep in sync if either changes.)
        // ============================================================

        // Rolling sample mean + stdev (n-1 divisor, i.e. ta.stdev(..., false)).
        private static void RollingMeanStdev(ISeries<double> series, int n, out double outMean, out double outStdev)
        {
            double sum = 0.0;
            for (int k = 0; k < n; k++)
                sum += series[k];
            double mean = sum / n;

            double sqSum = 0.0;
            for (int k = 0; k < n; k++)
            {
                double d = series[k] - mean;
                sqSum += d * d;
            }
            double variance = (n > 1) ? sqSum / (n - 1) : 0.0;

            outMean = mean;
            outStdev = Math.Sqrt(variance);
        }

        // ta.percentile_linear_interpolation equivalent. Sorts the trailing
        // `period` values ending at the current bar ONCE into the reusable
        // buffer, then reads BOTH the upper and lower percentile off that
        // single sorted buffer. Returns false if the window isn't fully
        // available yet.
        private static bool PercentileBlendPair(Series<double> series, int currentBar, int period,
                                                  double pctUpper, double pctLower, double[] buf,
                                                  out double outUpper, out double outLower)
        {
            outUpper = 0.0;
            outLower = 0.0;
            if (period <= 0 || period > MaxBlendPeriod || currentBar < period - 1)
                return false;

            for (int k = 0; k < period; k++)
                buf[k] = series[k];

            Array.Sort(buf, 0, period);

            outUpper = InterpPercentile(buf, period, pctUpper);
            outLower = InterpPercentile(buf, period, pctLower);
            return true;
        }

        private static double InterpPercentile(double[] sortedBuf, int period, double pct)
        {
            double r = (pct / 100.0) * (period - 1);
            int lo = (int)Math.Floor(r);
            int hi = (int)Math.Ceiling(r);
            if (lo < 0) lo = 0;
            if (hi > period - 1) hi = period - 1;
            double frac = r - lo;
            return sortedBuf[lo] + (sortedBuf[hi] - sortedBuf[lo]) * frac;
        }

        // Linearly weighted moving average (weights n..1, most recent
        // weighted highest) over the trailing `length` bars. Gracefully
        // degrades to the available history near the start of the chart.
        private static double Wma(ISeries<double> series, int currentBar, int length)
        {
            int n = Math.Min(length, currentBar + 1);
            if (n <= 0)
                return 0.0;

            double weightedSum = 0.0;
            double weightSum = 0.0;
            for (int k = 0; k < n; k++)
            {
                double w = n - k;
                weightedSum += series[k] * w;
                weightSum += w;
            }
            return weightedSum / weightSum;
        }

        // Least-squares linear regression endpoint — equivalent to
        // ta.linreg(src, length, 0).
        private static double LinRegEndpoint(ISeries<double> series, int currentBar, int length)
        {
            int n = Math.Min(length, currentBar + 1);
            if (n <= 1)
                return (n == 1) ? series[0] : 0.0;

            double sumX = 0.0, sumY = 0.0, sumXY = 0.0, sumXX = 0.0;
            for (int k = 0; k < n; k++)
            {
                double x = n - 1 - k; // most recent bar -> x = n-1
                double y = series[k];
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumXX += x * x;
            }
            double denom = n * sumXX - sumX * sumX;
            if (Math.Abs(denom) < 1e-12)
                return sumY / n;

            double slope = (n * sumXY - sumX * sumY) / denom;
            double intercept = (sumY - slope * sumX) / n;
            return intercept + slope * (n - 1);
        }

        // Hull MA: diff = 2*WMA(src, halfLen) - WMA(src, length);
        // out = WMA(diff, sqrtLen). `diffSeries` persists the diff series
        // per-bar so the outer WMA can look back over it on later bars.
        private static double HullMa(Series<double> srcSeries, Series<double> diffSeries, int currentBar, int length)
        {
            int halfLen = Math.Max(1, length / 2);
            int sqrtLen = Math.Max(1, (int)Math.Round(Math.Sqrt((double)length), MidpointRounding.AwayFromZero));

            double wmaHalf = Wma(srcSeries, currentBar, halfLen);
            double wmaFull = Wma(srcSeries, currentBar, length);
            diffSeries[0] = 2.0 * wmaHalf - wmaFull;

            return Wma(diffSeries, currentBar, sqrtLen);
        }

        // Ehlers Super Smoother (2-pole). Coefficients are recomputed from
        // `length` every call (cheap). `outSeries` holds the recursive
        // output series; bars before the start of the chart are treated as
        // 0, matching Pine's nz().
        private static double SuperSmootherStep(Series<double> xSeries, Series<double> outSeries, int currentBar, int length)
        {
            double a1 = Math.Exp(-Math.Sqrt(2.0) * Math.PI / length);
            double b1 = 2.0 * a1 * Math.Cos(Math.Sqrt(2.0) * Math.PI / length);
            double c2 = b1;
            double c3 = -a1 * a1;
            double c1 = 1.0 - c2 - c3;

            double x = xSeries[0];
            double xPrev    = (currentBar >= 1) ? xSeries[1] : 0.0;
            double outPrev1 = (currentBar >= 1) ? outSeries[1] : 0.0;
            double outPrev2 = (currentBar >= 2) ? outSeries[2] : 0.0;

            double outVal = c1 * (x + xPrev) / 2.0 + c2 * outPrev1 + c3 * outPrev2;
            outSeries[0] = outVal;
            return outVal;
        }

        // Ehlers Two-Pole Gaussian filter. Same nz()-style guards as
        // SuperSmootherStep.
        private static double TwoPoleGaussianStep(Series<double> xSeries, Series<double> outSeries, int currentBar, int length)
        {
            double beta = (1.0 - Math.Cos(2.0 * Math.PI / length)) / (Math.Sqrt(2.0) - 1.0);
            double alpha = -beta + Math.Sqrt(beta * beta + 2.0 * beta);
            double a2 = alpha * alpha;
            double om = 1.0 - alpha;
            double om2 = om * om;

            double x = xSeries[0];
            double outPrev1 = (currentBar >= 1) ? outSeries[1] : 0.0;
            double outPrev2 = (currentBar >= 2) ? outSeries[2] : 0.0;

            double outVal = a2 * x + 2.0 * om * outPrev1 - om2 * outPrev2;
            outSeries[0] = outVal;
            return outVal;
        }

        // Pine ta.crossover / ta.crossunder semantics: strictly beyond NOW,
        // and NOT strictly beyond on the previous bar.
        private static bool CrossesOver(double curA, double curB, double prevA, double prevB)
        {
            return curA > curB && prevA <= prevB;
        }
        private static bool CrossesUnder(double curA, double curB, double prevA, double prevB)
        {
            return curA < curB && prevA >= prevB;
        }

        // ============================================================
        // PROPERTIES
        // ============================================================

        [NinjaScriptProperty]
        [Range(10, 500)]
        [Display(Name = "Rolling Window", GroupName = "Z-Score Settings", Order = 1)]
        public int RollingWindow
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Band Mode", GroupName = "Channel Settings", Order = 1)]
        public ZChannelBandMode BandMode
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 6.0)]
        [Display(Name = "Fixed Upper Z", GroupName = "Channel Settings", Order = 2)]
        public double FixedUpperZ
        { get; set; }

        [NinjaScriptProperty]
        [Range(-6.0, -0.1)]
        [Display(Name = "Fixed Lower Z", GroupName = "Channel Settings", Order = 3)]
        public double FixedLowerZ
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Force Symmetric Bands", GroupName = "Channel Settings", Order = 4)]
        public bool ForceSymmetricBands
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 3.0)]
        [Display(Name = "Minimum Band Z", GroupName = "Channel Settings", Order = 5)]
        public double MinimumBandZ
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 0.9)]
        [Display(Name = "Inner Band Fraction", GroupName = "Channel Settings", Order = 6)]
        public double InnerBandFraction
        { get; set; }

        [NinjaScriptProperty]
        [Range(50.0, 99.0)]
        [Display(Name = "Upper Percentile", GroupName = "Percentile Thresholds", Order = 1)]
        public double UpperPercentile
        { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 50.0)]
        [Display(Name = "Lower Percentile", GroupName = "Percentile Thresholds", Order = 2)]
        public double LowerPercentile
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Smooth Z-Score", GroupName = "Smoothing", Order = 1)]
        public bool SmoothZScore
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Smooth Band Levels", GroupName = "Smoothing", Order = 2)]
        public bool SmoothBandLevels
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Smoothing Type", GroupName = "Smoothing", Order = 3)]
        public ZChannelSmoothingType SmoothingType
        { get; set; }

        [NinjaScriptProperty]
        [Range(2, 50)]
        [Display(Name = "Smoothing Length", GroupName = "Smoothing", Order = 4)]
        public int SmoothingLength
        { get; set; }

        [NinjaScriptProperty]
        [Range(10, 2000)]
        [Display(Name = "Blend Period Short", GroupName = "Multi-Period Blend", Order = 1)]
        public int BlendPeriodShort
        { get; set; }

        [NinjaScriptProperty]
        [Range(10, 2000)]
        [Display(Name = "Blend Period Medium", GroupName = "Multi-Period Blend", Order = 2)]
        public int BlendPeriodMedium
        { get; set; }

        [NinjaScriptProperty]
        [Range(10, 2000)]
        [Display(Name = "Blend Period Long", GroupName = "Multi-Period Blend", Order = 3)]
        public int BlendPeriodLong
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Blend Weight Short", GroupName = "Multi-Period Blend", Order = 4)]
        public double BlendWeightShort
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Blend Weight Medium", GroupName = "Multi-Period Blend", Order = 5)]
        public double BlendWeightMedium
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Blend Weight Long", GroupName = "Multi-Period Blend", Order = 6)]
        public double BlendWeightLong
        { get; set; }

        // ---- Visual-only (not strategy-parameterizable) ----

        [Display(Name = "Gradient Basis Coloring", GroupName = "Theme & Coloring", Order = 1)]
        public bool GradientBasisColoring
        { get; set; }

        [Display(Name = "Color Bars", GroupName = "Theme & Coloring", Order = 2)]
        public bool ColorBars
        { get; set; }

        [Display(Name = "Show Region Fill", GroupName = "Theme & Coloring", Order = 3)]
        public bool ShowRegionFill
        { get; set; }

        [Range(0, 100)]
        [Display(Name = "Region Opacity", GroupName = "Theme & Coloring", Order = 4)]
        public int RegionOpacity
        { get; set; }

        [Display(Name = "Show Signals", GroupName = "Signals", Order = 1)]
        public bool ShowSignals
        { get; set; }

        [Display(Name = "Enable Alerts", GroupName = "Signals", Order = 2)]
        public bool EnableAlerts
        { get; set; }

        [Range(1, 3600)]
        [Display(Name = "Alert Rearm Seconds", GroupName = "Signals", Order = 3)]
        public int AlertRearmSeconds
        { get; set; }
    }
}
