#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
#endregion

//This code is subject to the terms of the Mozilla Public License 2.0 at https://mozilla.org/MPL/2.0/
//Created by RedTail Indicators - @_hawkeye_13
// RedTail Liquidity Terrain
// A reimagined volume profile. Volume is encoded as MASS, not bar length: high-volume nodes form an
// opaque "wall" pinned to the right that bulges inward where volume is thick and pinches to nothing
// where it is thin. Low-volume nodes (LVNs) are detected by local prominence and rendered as bright
// rails that project out of the wall gaps across the price action - the entries. High-volume nodes
// (HVNs) render as faint no-fly zones. The wall silhouette therefore reads as resistance-to-traversal:
// deep bulge = price will grind, pinch = price slips through.

namespace NinjaTrader.NinjaScript.Indicators.RedTail
{
    public enum RailRenderModeEnum
    {
        FullSpan,
        Projection,
        Both
    }

    public enum WallStyleEnum
    {
        Smooth,
        Stepped
    }

    // How the buy/sell lean (polarity) is sourced.
    //   Proxy - close-in-range OHLC heuristic; works on any data, but it's a lean, not delta.
    //   True  - real bid/ask delta: each trade classified buyer- or seller-initiated by the bid/ask
    //           rule (tick-rule fallback when a quote is missing). Falls back to Proxy per-bar when
    //           no tick/quote flow is available.
    //   Cvd   - CVD-Zones net delta: net delta is computed PER BAR over a rolling lookback, weak bars
    //           are discarded (below CvdThresholdPct of the window's max), and the survivors are
    //           stamped onto a COARSE zone grid (CvdZoneCount tall bins over the lookback range, +/-1
    //           spread, optional smoothing) - which is what makes it read as tradeable bands rather
    //           than per-row specks. Unlike Proxy/True - which measure the buy FRACTION at a price and
    //           sit near-neutral all session - this only lights up zones where price moved on
    //           decisively one-sided bars. Drives the wall tint, strip and rail dots; the per-row
    //           Delta Split has no CVD analogue and stays on Proxy/True.
    public enum DeltaSourceEnum
    {
        Proxy,
        True,
        Cvd
    }

    // What sources a rail. Confirmed Pivot Reversals (CPR) are swing pivots that landed in thin
    // volume - an actual reversal in a void. Volume Valleys are the predictive LVN troughs. Both
    // draws each; where a valley is confirmed by a pivot it graduates to CPR.
    public enum RailSourceEnum
    {
        ConfirmedPivots,
        VolumeValleys,
        Both
    }

    public enum FrvpHeightModeEnum
    {
        AtrMultiple,
        FixedTicks
    }

    public enum FrvpZoneStateEnum
    {
        Fresh,
        Tested,
        Mitigated
    }

    // Momentum-confluence gate (Waddah-Attar-Explosion read distilled to a directional state).
    //   Off       - feature disabled, zero cost.
    //   Highlight  - HTF proximity alerts always show; a momentum chip is added and the matching
    //                alert lines tint brighter when momentum is exploding (informational only).
    //   Filter     - HTF proximity alert lines are suppressed unless momentum is currently
    //                exploding, so the banner only fires on level + momentum confluence.
    public enum MomentumGateModeEnum
    {
        Off,
        Highlight,
        Filter
    }

    // Priority order for the right-gutter label layer. When several labels land within a text-height
    // of each other they collapse into one chip; the LOWEST rank present wins the visible slot and
    // tints the chip. The rest are reachable on hover. Order is deliberate, not alphabetical: a
    // confirmed pivot reversal outranks a POC outranks the live value area, and so on down to the
    // averaged merge line, which is the least informative thing on the chart.
    public enum LabelRankEnum
    {
        Extreme    = 0,   // prior-session high/low: the day's actual boundary, and a magnet on inside days
        Cpr        = 1,
        Poc        = 2,
        ValueArea  = 3,
        Lvn        = 4,
        Frvp       = 5,
        Htf        = 6,
        Session    = 7,
        Reference  = 8,
        Merged     = 9
    }

    public class RedTailLiquidityTerrain : Indicator
    {
        #region Internal types

        private class HvnBand
        {
            public double LowPrice;
            public double HighPrice;
            public double PeakVol;
            public double PocPrice;   // price at the node's peak-volume row (its POC)
        }

        private class TrackedLvn
        {
            public double Price;
            public double Depth;       // 0..1, fraction below the lower flanking wall (at strongest detection)
            public bool Strong;        // depth >= LvnStrongDepth
            public bool Tested;        // a bar has wicked into it (rejection) since birth
            public bool Filled;        // a bar has closed through it (gap consumed) -> retired/hidden
            public bool IsPivot;       // true = Confirmed Pivot Reversal (CPR); false = volume valley (LVN)
            public int Dir;            // +1 = from a swing HIGH (resistance); -1 = swing LOW (support); 0 = valley
            public int OriginBar;      // absolute bar index of the source pivot (for source anchoring); -1 if n/a
            public double Polarity;    // -1 (seller) .. +1 (buyer): OHLC-proxy lean of the gap's volume
            public int BornBar;
            public int LastTouchBar;
            // Reaction memory: how the level has performed when price returned to it.
            public bool Inside;        // price is currently touching (gate for counting distinct holds)
            public int Holds;          // distinct hold events (touched, then left without closing through)
            public double RejectMax;   // strongest push-off on a hold, in ATR units
            public double Score;        // 0..1 earned weight (holds + rejection strength)
            // CPR mitigation lifecycle (mirrors FrvpZone): a rail must clear its band before returns
            // count, and a close through it FLIPS its polarity rather than killing it. Retires on counts.
            public bool HasLeft;       // price has cleared the level's working side since birth/last flip
            public int Touches;        // distinct entries into the band (retirement counter)
            public int Flips;          // close-throughs that reversed polarity instead of retiring
        }

        private class FrvpZone
        {
            public long      Key;          // stable id = start-bar time ticks
            public DateTime  StartTime;
            public DateTime  EndTime;
            public int       StartBarIdx;  // absolute bar index of the first source bar
            public int       EndBarIdx;
            public double    POC;
            public double    VAH;
            public double    VAL;
            public double    SrcHigh;      // high/low of the source bars (the consolidation outline)
            public double    SrcLow;
            public int       Bars;
            public int       Dir;          // +1 broke up, -1 broke down
            public double    RefEdge;      // consolidation edge the breakout departs from
            public double    Height;       // VA height (departure normaliser)
            public double    MaxExcursion;
            public int       DepartBarsLeft;
            public double    Departure;
            public bool      Strong;
            public double    Volume;
            // mitigation lifecycle
            public FrvpZoneStateEnum State;   // Fresh -> Tested -> Mitigated
            public bool      HasLeft;         // price has cleared the band since forming
            public bool      Inside;          // price currently inside the band (for touch counting)
            public int       Touches;
            public int       Flips;
            public int       LastTouchBar;
            // Reaction memory: Touches already counts distinct holds; add rejection strength + score.
            public double    RejectMax;   // strongest push-off on a hold, in VA-height units
            public double    Score;        // 0..1 earned weight
        }

        // The 00:00 open in the reference zone (12 AM ET by default). Not a profile level - just the
        // price the day started at, which the ICT/intraday crowd uses as the day's directional pivot.
        private class MidnightOpen
        {
            public DateTime Day;    // calendar date in the reference zone
            public double   Price;  // Open of the first bar printed on or after 00:00
            public int      BarIdx; // absolute bar index of that bar (for optional anchoring)
        }

        private class SessionVA
        {
            public double Vah;
            public double Val;
            public double Poc;
            public double High;
            public double Low;
            public DateTime Day;
            // Ghost-profile silhouette: per-row volume across the VAL..VAH band, bottom-up.
            public double[] Bins;
            public double BinLow;     // price at the low edge of Bins[0]
            public double BinSize;    // price height per bin
            public double BinPeak;    // max bin volume (for depth normalisation)
        }

        private class HtfProfile
        {
            public double Poc;
            public double Vah;
            public double Val;
            public DateTime Period;    // period start (ref-zone date)
            // Bar-anchored silhouette data (retained only when the profile display is enabled):
            public double[] RowVol;    // volume per row, MinRow upward
            public int MinRow;
            public double RowSize;
            public double Peak;
            public DateTime Start;     // chart-time span of the period (for GetXByTime)
            public DateTime End;
        }

        // A trading-session window (Asia / London / NY) and its profile.
        private class RTSession
        {
            public int Type;              // 0 Asia, 1 London, 2 NY
            public DateTime TradingDay;
            public DateTime StartTime;
            public double Open;
            public double High = double.MinValue;
            public double Low = double.MaxValue;
            public double POC;
            public double VAH;
            public double VAL;
            public bool IsComplete;
            public bool HasData;
            public Dictionary<int, double> Bins = new Dictionary<int, double>();
        }

        // One buffered secondary-series bar (chart time), held until the HTF backfill has merged.
        private struct HtfBar
        {
            public DateTime T;
            public double H, L, V;
        }

        // A reference level queued for the cross-family merge pass.
        private class PendingLevel
        {
            public double Price;
            public Brush Wpf;
            public DashStyleHelper Style;
            public int Thickness;
            public int Opacity;
            public float X0;
            public float X1;
            public string Label;
            public LabelRankEnum Rank;
            public bool Dim;
        }

        // One right-gutter label, queued by whatever layer produced it and rendered only at the end
        // of the frame. Nothing draws text into the gutter directly any more - that is what let the
        // last-painting layer silently eat the ones underneath it.
        private class GutterLabel
        {
            public double Price;     // exact price, never averaged
            public float  Y;         // exact row
            public float  X;         // desired left edge (gutter column)
            public string Tag;       // "CPR", "pdVAH", ... - no price suffix
            public Brush  Wpf;
            public int    Opacity;
            public LabelRankEnum Rank;
            public bool   Dim;       // tested / mitigated / below-strength: never takes the visible slot
            public int    Pin;       // index in LabelPinTags, or -1. A pin outranks every Rank, and ignores Dim.
        }

        // A resolved cluster of gutter labels sharing a row. One is drawn; all are hoverable.
        private class LabelGroup
        {
            public readonly List<GutterLabel> Members = new List<GutterLabel>();
            public GutterLabel Primary;              // the one that wins the chip
            public float L, T, R, B;                 // hover rect (chip bounds, padded)
        }

        #endregion

        #region Variables

        // ---- Rolling scan window (LVN discovery only) ----
        private readonly List<double> scanHighs = new List<double>();
        private readonly List<double> scanLows = new List<double>();
        private readonly List<double> scanCloses = new List<double>();
        private readonly List<double> scanVols = new List<double>();
        private double scanLow, scanHigh, scanRowSize;
        private int scanRowCount;
        private double[] scanRowVol = new double[0];
        private double[] scanBuyVol = new double[0];   // buy portion per row (OHLC proxy)
        private double scanPocVol;

        // ---- Session-anchored profile (structure: wall, POC, value area, HVN) ----
        private readonly Dictionary<int, double> profBins = new Dictionary<int, double>(); // abs row index -> total volume
        private readonly Dictionary<int, double> profBuyBins = new Dictionary<int, double>(); // buy portion (OHLC proxy)
        private double[] profBuyRowVol = new double[0];
        private double profRowSize;
        private double profLow, profHigh;
        private int profRowCount;
        private double[] profRowVol = new double[0];

        // CVD-sourced polarity: per-wall-row net delta, normalized to -1..+1, aligned 1:1 with
        // profRowVol (relative row index). Rebuilt in Recompute from the rolling scan window when
        // DeltaSource is Cvd; empty otherwise. A row with no decisive delta reads 0 (neutral).
        private double[] cvdRowPol = new double[0];
        private float[] profGradScore = new float[0];   // Wall Gradient heat score per row (see ComputeGradientScores)
        private double profPocVol;
        private int profPocRow;
        private double curPoc, curVah, curVal;
        private double curSessHigh = double.MinValue;
        private double curSessLow = double.MaxValue;
        private DateTime curSessionDay = DateTime.MinValue;
        private int lastProfBar = -1;

        // ---- Tick-accurate session profile (exact volume-at-price from actual Last prints) ----
        // Ticks for the forming primary bar accumulate here and are folded into profBins on that
        // bar's close (CommitBarToProfile). Keyed by the SAME absolute row index as profBins.
        private readonly Dictionary<int, double> barTickVol = new Dictionary<int, double>();
        private double barTickSum;    // total tick volume seen for the forming bar
        private bool   barHadTicks;   // any Last prints captured for the forming bar
        // Min fraction of the bar's reported volume that must arrive as ticks to trust tick binning.
        // Below this (partial first live bar, post-suspension catch-up, no Tick Replay) we fall back
        // to the OHLC distribution for that one bar so no row is left under-filled.
        private const double TickCoverageMin = 0.5;

        // ---- True bid/ask delta (aggressor classification of trade prints) ----
        private readonly Dictionary<int, double> barBuyTickVol = new Dictionary<int, double>(); // classified buy vol, forming bar
        private double curBid = double.NaN, curAsk = double.NaN;   // latest quotes from the feed
        private int    lastAggressor;   // +1 buyer / -1 seller, carried across unchanged/mid prints

        // ---- Prior-session value-area snapshots ----
        private readonly List<SessionVA> priorVA = new List<SessionVA>();

        // ---- Higher-timeframe POC/VA (weekly / monthly), reusing the fine intraday binning ----
        private readonly Dictionary<int, double> weekBins = new Dictionary<int, double>();
        private readonly Dictionary<int, double> monthBins = new Dictionary<int, double>();
        private DateTime curWeekStart = DateTime.MinValue;
        private DateTime curMonthStart = DateTime.MinValue;
        private DateTime curWeekStartTime = DateTime.MinValue;    // chart-time of the developing week's first bar
        private DateTime curMonthStartTime = DateTime.MinValue;
        private double devWeekPoc = double.NaN, devWeekVah = double.NaN, devWeekVal = double.NaN;
        private double devMonthPoc = double.NaN, devMonthVah = double.NaN, devMonthVal = double.NaN;
        private readonly List<HtfProfile> priorWeeks = new List<HtfProfile>();   // most-recent first
        private readonly List<HtfProfile> priorMonths = new List<HtfProfile>();

        // ---- Timezone (session reset boundary) ----
        private TimeZoneInfo platformZone;
        private TimeZoneInfo refZone;
        private TimeSpan resetTod = new TimeSpan(18, 0, 0);

        // ---- Detection results (rebuilt into fresh lists on the data thread and PUBLISHED BY
        //      REFERENCE; OnRender only ever reads a published reference, never a mutating list) ----
        private volatile List<HvnBand> hvnBands = new List<HvnBand>();
        // HTF proximity alerts: [0]=weekly HVN, [1]=weekly LVN, [2]=monthly HVN, [3]=monthly LVN.
        private readonly bool[] alertActive = new bool[4];
        private readonly double[] alertPrice = new double[4];
        private readonly List<TrackedLvn> detected = new List<TrackedLvn>();  // current scan detection (transient)
        private readonly List<TrackedLvn> tracked = new List<TrackedLvn>();   // persistent LVN levels

        private bool hasProfile;
        private ChartControl renderCC;   // stashed during OnRender for bar-index -> X lookups
        // ---- Proximity reveal / hover state ----
        private double renderLastPrice;
        private double lastTradePrice = double.NaN;   // live last trade, captured in OnMarketData (decoupled from bar closes)
        private float renderWallRightX;
        private float renderBarDist;     // px per bar this frame (gradient extension length)
        private float mousePxX, mousePxY;
        private volatile bool mouseValid;

        // FRVP hover tooltip: hover regions published from render, hit-tested on mouse move.
        private struct FrvpHover { public float L, T, R, B; public long Key; }
        private volatile FrvpHover[] frvpHoverSnap = new FrvpHover[0];
        private volatile bool frvpHasHover;
        private long frvpHoveredKey;
        private float renderCanvasLeft, renderCanvasRight, renderPanelTop, renderPanelBottom;
        private bool mouseHooked;
        private SessionVA ghostSel;   // currently-selected prior session for the ghost (sticky w/ hysteresis)

        // --- FRVP consolidation zones (ported from RedTailAutoFRVP, render-gated like the other levels) ---
        private readonly List<FrvpZone> frvpZones = new List<FrvpZone>();
        private ATR frvpAtr;
        private bool   fbActive;
        private int    fbStartIdx, fbEndIdx, fbBars;
        private double fbHigh, fbLow, fbCloseHi, fbCloseLo;
        private DateTime fbStartTime;

        // --- Session levels (Asia/London/NY profiles, ported from RedTailSessionLevels) ---
        private readonly List<RTSession> sessList = new List<RTSession>();
        private RTSession sessActive;
        private bool sessTimesParsed;
        private TimeSpan sessAsiaStart, sessAsiaEnd, sessLonStart, sessLonEnd, sessNyStart, sessNyEnd;

        // Cross-family level merge collection (populated during the level layer, flushed at its end).
        private readonly List<PendingLevel> pendingLevels = new List<PendingLevel>();

        // Global gutter-label layer. Every subsystem queues here; one flush at the end of OnRender
        // clusters in PIXEL space (label collision is a pixel problem, not a tick problem) and draws.
        // This is also the only place in the indicator that knows what is drawn at a given row, so
        // the debug readout can enumerate a cluster instead of you squinting at a screenshot.
        private readonly List<GutterLabel> labelQueue  = new List<GutterLabel>();
        private readonly List<LabelGroup>  labelGroups = new List<LabelGroup>();
        private string[] pinTags = new string[0];   // parsed LabelPinTags, rebuilt only when the string changes
        private string   pinTagsSrc;

        // --- Momentum-confluence gate (WAE-style: MACD-histogram slope vs Bollinger-width explosion) ---
        private MACD momMacd;
        private Bollinger momBoll;
        private ATR momAtr;     // volatility unit for the scale-free (ATR-normalized) mode
        private int momState;   // +1 = long explosion, -1 = short explosion, 0 = none / below explosion line

        // --- HTF backfill (BarsRequest so weekly/monthly profiles are complete on short-lookback charts) ---
        // The chart's secondary series only loads the primary chart's date range, so on a 3-day tick
        // chart the "monthly" bins would cover 3 days. A BarsRequest pulls HtfBackfillDays of minute
        // history independently; chart-series bars buffer until it lands, then drain past the seam.
        private BarsRequest htfRequest;
        private readonly object htfSync = new object();                       // guards bins + rollover state
        private readonly List<HtfBar> htfPendingBars = new List<HtfBar>();    // chart bars buffered pre-merge
        private DateTime htfSeamTime = DateTime.MinValue;                     // last bar time the backfill covered
        private volatile bool htfReady;                                       // merged (or disabled) -> HTF may draw

        // --- HTF snapshots: built on secondary bar close / backfill completion, read-only for render ---
        private volatile HtfProfile devWeekSnap, devMonthSnap;
        private volatile HtfProfile[] priorWeeksSnap = new HtfProfile[0];
        private volatile HtfProfile[] priorMonthsSnap = new HtfProfile[0];
        private volatile List<HvnBand> weekHvnSnap = new List<HvnBand>();
        private volatile List<HvnBand> weekLvnSnap = new List<HvnBand>();
        private volatile List<HvnBand> monthHvnSnap = new List<HvnBand>();
        private volatile List<HvnBand> monthLvnSnap = new List<HvnBand>();

        // --- Render snapshots of data-thread lists (published once per bar; render never iterates
        //     the live lists, so RemoveAt/Add on the data thread can't tear a frame) ---
        private volatile TrackedLvn[] trackedSnap = new TrackedLvn[0];
        private volatile TrackedLvn[] detectedSnap = new TrackedLvn[0];
        private volatile FrvpZone[] frvpSnap = new FrvpZone[0];
        private volatile SessionVA[] priorVASnap = new SessionVA[0];

        // Midnight open (00:00 reference zone). Newest is last.
        private readonly List<MidnightOpen> midnightOpens = new List<MidnightOpen>();
        private volatile MidnightOpen[] midnightSnap = new MidnightOpen[0];
        private DateTime lastMidnightDay = DateTime.MinValue;
        private volatile RTSession[] sessSnap = new RTSession[0];

        // --- Out-of-value FRVP (FOV): 15-min FRVP zones (own BarsRequest, own snapshot) that sit
        //     OUTSIDE today's developing value area - potential reversal shelves back toward session POC.
        //     Detected off-thread on reload; the in/out-of-VA test is re-applied live at render. ---
        private BarsRequest fovRequest;
        private readonly object fovSync = new object();
        private volatile FrvpZone[] fovSnap = new FrvpZone[0];

        // Live parity: the FOV zones are rebuilt from a rolling window of NATIVE FrvpVaMinutes bars.
        // The BarsRequest seeds the history; a secondary series of the same period feeds it forward, and
        // every new native bar re-runs RebuildFovZones - the identical code path the seed used, so the
        // live zones cannot drift from the backfilled ones.
        private readonly List<DateTime> fovT = new List<DateTime>();
        private readonly List<double> fovH = new List<double>();
        private readonly List<double> fovL = new List<double>();
        private readonly List<double> fovC = new List<double>();
        private readonly List<double> fovV = new List<double>();
        private struct FovBar { public DateTime T; public double H, L, C, V; }
        private readonly List<FovBar> fovPending = new List<FovBar>();   // bars that arrived before the seed landed
        private DateTime fovSeamTime = DateTime.MinValue;                // last native bar time the window covers
        private bool fovSeeded;
        private int fovBip = -1;                                         // BarsInProgress of the native FrvpVaMinutes series

        // Out-of-value hover tooltip: band regions published from render, hit-tested on mouse move.
        private volatile FrvpHover[] fovHoverSnap = new FrvpHover[0];
        private volatile bool fovHasHover;
        private long fovHoveredKey;

        // ===== Order Blocks (out-of-value / HTF, same architecture as the out-of-value FRVP subsystem) =====
        // An OB is the last opposing candle before a swing was taken out (a BOS/CHoCH origin). Detected on
        // NATIVE ObVaMinutes bars via a BarsRequest (independent of chart range) and replayed forward, so a
        // higher-timeframe OB shows up on the entry chart. Lifecycle: active -> (close through) breaker ->
        // (close through the breaker) gone. The POC is the true volume POC of the single OB candle, profiled
        // from ticks and cached by candle key.
        private class ObZone
        {
            public long     Key;          // stable id = OB candle start-time ticks
            public DateTime StartTime;    // OB candle open time
            public DateTime EndTime;      // OB candle close time (candle span)
            public double   Top;          // drawn upper edge (candle high, or body edge in Wick-Only)
            public double   Bottom;       // drawn lower edge
            public double   CandleHigh;   // the OB candle's true high/low (POC request window + midpoint fallback)
            public double   CandleLow;
            public double   Mid;          // 50% equilibrium (mean threshold) - always available
            public double   Poc;          // tick-profiled volume POC of the candle (falls back to Mid until it lands)
            public bool     PocReady;     // true once the tick profile has resolved
            public bool     IsBull;       // true = bullish OB (demand); false = bearish (supply)
            public bool     Breaker;      // flipped once (broken through) - now acts as the opposite polarity
            public bool     Left;         // price has fully cleared the box since forming (gate for a return-tap)
            public bool     Mitigated;    // price returned and tapped the box (used) - removed, or faded if shown
            public sbyte    Side;         // committed side of price vs the box: +1 above, -1 below (drives flip counting)
            public int      Bars;         // OB candle span in native bars (usually 1)
            public double   Volume;       // OB candle volume
        }

        private BarsRequest obRequest;
        private readonly object obSync = new object();
        private volatile ObZone[] obSnap = new ObZone[0];

        // Rolling window of native ObVaMinutes bars (mirrors the FOV window; OB detection needs Open too).
        private readonly List<DateTime> obT = new List<DateTime>();
        private readonly List<double> obO = new List<double>();
        private readonly List<double> obH = new List<double>();
        private readonly List<double> obL = new List<double>();
        private readonly List<double> obC = new List<double>();
        private readonly List<double> obV = new List<double>();
        private struct ObBar { public DateTime T; public double O, H, L, C, V; }
        private readonly List<ObBar> obPending = new List<ObBar>();
        private DateTime obSeamTime = DateTime.MinValue;
        private bool obSeeded;
        private int obBip = -1;

        // Tick-profiled candle POC cache, keyed by OB candle start-time ticks. Filled asynchronously by
        // small per-candle tick BarsRequests; obPocPending guards against re-firing an in-flight window.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, double> obPocCache
            = new System.Collections.Concurrent.ConcurrentDictionary<long, double>();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, byte> obPocPending
            = new System.Collections.Concurrent.ConcurrentDictionary<long, byte>();
        private readonly List<BarsRequest> obPocRequests = new List<BarsRequest>();   // kept alive; disposed on reset

        // Out-of-value OB hover tooltip regions.
        private volatile FrvpHover[] obHoverSnap = new FrvpHover[0];
        private volatile bool obHasHover;
        private long obHoveredKey;
        private object obLastDumpedSnap;   // render-side debug: dump filter/draw counts once per rebuild

        // --- Cached device resources (one DX brush per WPF color, opacity set per use; four stroke
        //     styles; one text format; 21-bucket polarity palette). Rebuilt on render-target change. ---
        private readonly Dictionary<Brush, SharpDX.Direct2D1.SolidColorBrush> dxBrushCache = new Dictionary<Brush, SharpDX.Direct2D1.SolidColorBrush>();
        private readonly Dictionary<System.Windows.Media.Color, SharpDX.Direct2D1.SolidColorBrush> colorBrushCache = new Dictionary<System.Windows.Media.Color, SharpDX.Direct2D1.SolidColorBrush>();
        private readonly Dictionary<DashStyleHelper, SharpDX.Direct2D1.StrokeStyle> strokeCache = new Dictionary<DashStyleHelper, SharpDX.Direct2D1.StrokeStyle>();
        private readonly List<SharpDX.Direct2D1.Brush> transientBrushes = new List<SharpDX.Direct2D1.Brush>();  // non-solid fallbacks, per frame
        private SharpDX.DirectWrite.TextFormat cachedTf;
        private int cachedTfSize = -1;
        private SharpDX.DirectWrite.TextFormat cachedTfBold;   // banner rows
        private int cachedTfBoldSize = -1;
        private SharpDX.Direct2D1.SolidColorBrush[] polPalette;

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Reimagined volume profile: HVN no-fly walls, LVN entry rails, terrain silhouette.";
                Name = "RedTailLiquidityTerrain";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                IsAutoScale = false;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = true;

                // 01. Profile (structure = session-anchored; scan = rolling for LVNs)
                ProfileTicksPerRow = 4;
                ScanLookbackBars = 1500;
                ScanTicksPerRow = 2;
                MaxRows = 1500;
                ValueAreaPercent = 70;
                UseTickVolume = true;

                // 02. Session
                SessionTimeZoneId = "Eastern Standard Time";
                SessionResetTime = "18:00";
                ShowValueArea = true;
                VaColor = MakeBrush(120, 170, 210);
                VaStyle = DashStyleHelper.Dash;
                VaOpacity = 55;
                PriorSessionsToShow = 1;
                PriorVaColor = MakeBrush(120, 130, 150);
                PriorVaOpacity = 45;
                PriorDayPocStyle = DashStyleHelper.Dash;
                PriorDayVaStyle = DashStyleHelper.Dot;
                PriorDayHLStyle = DashStyleHelper.Solid;
                ShowPriorHL = true;
                LineProjectionBars = 30;
                MergeLevels = false;
                MergeDistanceTicks = 12;
                MergeColor = MakeBrush(150, 156, 168);
                MergeOpacity = 70;

                ShowLabelBackdrop    = true;
                LabelBackdropColor   = MakeBrush(20, 22, 28);   // near-black; match your chart background
                LabelBackdropOpacity = 85;
                TooltipBackColor     = MakeBrush(10, 11, 14);
                TooltipBackOpacity   = 88;
                GroupStackedLabels   = true;
                LabelClusterPadPx    = 2;
                ShowGroupDots        = true;
                LabelGroupTooltip    = true;
                ExpandAllGroups      = false;
                LabelPinTags         = "pdH, pdL";

                // 10. Ghost Profile (dim prior-day VA silhouette, shown only when price is inside it)
                ShowGhostProfile = false;
                ShowGhostSilhouette = true;
                ShowGhostLevels = true;
                GhostFaceRight = true;
                GhostColor = MakeBrush(150, 158, 170);
                GhostOpacity = 16;
                GhostLevelColor = MakeBrush(210, 180, 120);
                GhostLevelOpacity = 60;
                GhostWidthPx = 130;
                GhostPosition = 0.35;
                GhostHysteresisTicks = 6;
                GhostLookback = 20;

                // 11. FRVP Zones (consolidation detection ported from RedTailAutoFRVP)
                ShowFrvpZones = true;
                ShowFrvpOutOfVa = true;   // on by default; kicks off its own BarsRequest on reload
                FrvpVaMinutes = 5;
                FrvpVaAtrMult = 1.5;   // must track FrvpMaxHeightAtr - same gate, native-bar ATR
                FrvpVaLookbackDays = 20;
                FrvpVaWarmupDays = 3;
                FrvpVaUseChartSession = false;
                FrvpOutOfVaAcrossChart = true;   // full-width by default
                FrvpVaZoneColor = MakeBrush(255, 255, 255);   // white VA
                FrvpVaPocColor = MakeBrush(255, 0, 0);        // red POC
                FrvpVaEdgeStyle = DashStyleHelper.Dash;
                FrvpVaPocStyle = DashStyleHelper.Solid;
                FrvpVaShowFill = true;
                FrvpVaOpacity = 50;
                FrvpVaShowMitigated = false;
                FrvpVaShowWeak = false;   // only zones that produced a real move
                FrvpVaMaxFlips = 3;      // identical rules AND thresholds to the chart-timeframe zones
                FrvpVaMaxTouches = 5;    // (FrvpMaxFlips / FrvpRetireTouches). Set both 0 to never retire.
                FrvpVaOutsideMinPct = 50;   // half the zone's VA must be clear of the session VA to show
                FrvpVaTooltip = true;

                // 16. Order Blocks (out-of-value / HTF)
                ShowObOutOfVa = false;   // opt-in; fires its own BarsRequest on reload
                ObVaMinutes = 15;        // HTF origin by default; set to the chart TF for chart-timeframe OOV OBs
                ObMaxAtrMult = 3.0;      // matches the MS order-block height gate
                ObSwingLength = 10;      // matches the MS OB swing length
                ObWickOnly = false;
                ObVaLookbackDays = 20;
                ObVaWarmupDays = 3;
                ObAcrossChart = true;
                ObBullColor = MakeBrush(45, 212, 191);    // teal (demand)
                ObBearColor = MakeBrush(239, 83, 80);     // red (supply)
                ObBreakerColor = MakeBrush(150, 150, 150);// grey (flipped)
                ObPocColor = MakeBrush(255, 215, 0);      // gold POC
                ObEdgeStyle = DashStyleHelper.Solid;
                ObPocStyle = DashStyleHelper.Dash;
                ObShowFill = true;
                ObOpacity = 55;
                ObShowBreakers = true;
                ObShowMitigated = false;   // remove tapped OBs (declutter); on = keep them faded
                ObOutsideMinPct = 50;
                ObShowInValue = false;   // out-of-value reversal shelves only, by default
                ObInValueDim = true;     // when shown, in-value blocks are dimmed so the shelves stand out
                ObTooltip = true;
                ObVaUseChartSession = false;
                ObDebug = false;

                FrvpShowWeak = false;
                FrvpShowFill = true;
                FrvpSourceColor = MakeBrush(120, 130, 145);
                FrvpVaColor = MakeBrush(90, 140, 180);
                FrvpPocColor = MakeBrush(210, 160, 90);
                FrvpOpacity = 70;
                FrvpFillOpacity = 10;
                FrvpHeightMode = FrvpHeightModeEnum.AtrMultiple;
                FrvpMaxHeightTicks = 40;
                FrvpMaxHeightAtr = 1.5;
                FrvpAtrPeriod = 14;
                FrvpBreakoutBufferTicks = 4;
                FrvpMinBars = 8;
                FrvpMaxBars = 60;
                FrvpUseCloseBand = true;
                FrvpProfileRows = 50;
                FrvpValueAreaPct = 70;
                FrvpMinDeparture = 1.5;
                FrvpDepartureBars = 10;
                FrvpOverlapThreshold = 0;
                FrvpMaxZones = 12;
                FrvpEnableMitigation = true;
                FrvpFlipOnBreakthrough = true;
                FrvpMaxFlips = 3;         // remove after this many flips (broke clean through)
                FrvpRetireTouches = 5;    // remove after this many touches (tagged repeatedly)
                ShowFrvpTooltip = true;
                FrvpTestedOpacityPct = 65;
                FrvpShowMitigatedFootprint = true;
                FrvpMitigatedFootprintOpacity = 22;

                // Midnight open (12 AM ET) - off by default, no change to existing charts
                ShowMidnightOpen = false;
                MidnightPriorDays = 0;
                MidnightAnchorToOpen = false;
                MidnightOpenColor = MakeBrush(212, 175, 55);   // muted gold
                MidnightOpenStyle = DashStyleHelper.DashDot;
                MidnightOpenThickness = 1;
                MidnightOpenOpacity = 70;

                // 12. Session Levels (Asia/London/NY profiles) - all sessions OFF by default
                ShowAsia = false;
                ShowLondon = false;
                ShowNewYork = false;
                ShowSessPOC = true;
                ShowSessVAH = true;
                ShowSessVAL = true;
                ShowSessOpen = false;
                ShowSessHigh = false;
                ShowSessLow = false;
                SessPreviousDays = 0;
                SessValueAreaPct = 70;
                SessTicksPerRow = 4;
                SessLevelOpacity = 70;
                AsiaStartText = "18:00";
                AsiaEndText = "03:00";
                LondonStartText = "03:00";
                LondonEndText = "09:30";
                NewYorkStartText = "09:30";
                NewYorkEndText = "17:00";
                AsiaColor = MakeBrush(196, 184, 120);
                LondonColor = MakeBrush(96, 160, 200);
                NewYorkColor = MakeBrush(150, 120, 196);

                // 03. Detection
                RailSource = RailSourceEnum.Both;
                PivotStrength = 5;
                PivotVolumeFactor = 0.85;
                PivotVolumeWindow = 40;
                SmoothBins = 3;
                LvnFlankTicks = 80;
                WallMinFraction = 0.03;
                LvnValleyFactor = 0.65;
                LvnStrongDepth = 0.55;
                HvnFraction = 0.70;
                HvnLocalNodes = false;      // default off: classic global-gate HVN behavior
                HvnLocalProminence = 0.08;  // shelf must rise >= 8% of POC above its flanking valleys
                HvnLocalWindow = 10;        // +/- 10 rows to find the peak and its valleys
                HvnFloorFraction = 0.12;    // ignore rows under 12% of POC volume
                HvnVaEdges = false;         // default: classic threshold-crossing edges
                HvnNodeVaPct = 78;          // node VA width when VA edges are on
                FillBufferTicks = 2;
                MaxTrackedLevels = 200;
                ScoreReactions = true;
                ReactionOpacityBoost = 25;
                CprFlipOnBreakthrough = true;   // CPRs live the FRVP lifecycle: flip, don't die
                CprMaxFlips = 3;                // matches FrvpMaxFlips
                CprRetireTouches = 5;           // matches FrvpRetireTouches

                // 03. Wall
                ShowWall = true;
                WallStyle = WallStyleEnum.Smooth;
                WallMaxDepth = 180;
                WallColor = MakeBrush(138, 47, 30);
                WallVaColoring = true;
                WallInVaColor = MakeBrush(168, 100, 64);
                WallOutVaColor = MakeBrush(78, 82, 90);
                WallOpacity = 60;
                WallGradient = false;                       // off by default: existing charts unchanged
                GradientHvnColor = MakeBrush(239, 83, 80);  // dense volume -> red, opacity scales with density
                GradientLvnColor = MakeBrush(76, 175, 80);  // thin volume  -> green, opacity scales with thinness
                GradientMaxOpacity = 65;                    // opacity at the extremes (POC / thinnest row)
                GradientMinOpacity = 0;                     // underlay floor: 0 = neutral rows draw nothing (wall carries the silhouette)
                GradientSensitivity = 50;                   // 50 = linear ramp; higher = color reaches toward the middle sooner
                GradientCrossover = 50;                     // score percentile where red flips to green; raise -> fewer rows qualify as red
                GradientRankWeight = 60;                    // 60% percentile rank / 40% linear vs POC
                GradientExtendBars = 30;                    // heat shading projects this far left of the wall
                GradientExtendOpacity = 40;                 // extension alpha as % of the row's footprint alpha
                GradientAcrossChart = false;                // off by default: existing charts unchanged
                GradientAcrossOpacity = 50;                 // across band alpha, % of footprint alpha (independent of the extension)
                GradientObeyProximity = false;              // heat map is terrain: it ignores Proximity Reveal

                // 04. HVN Zones
                ShowHvnZones = true;
                HvnZoneAcrossChart = true;
                HvnColor = MakeBrush(239, 83, 80);
                HvnZoneOpacity = 12;

                // 05. POC
                ShowPOC = true;
                PocColor = MakeBrush(216, 90, 48);
                PocStyle = DashStyleHelper.Solid;
                PocThickness = 2;
                PocOpacity = 85;

                // 06. LVN Rails
                ShowRails = true;
                PersistLevels = true;
                ShowTested = true;
                RailRenderMode = RailRenderModeEnum.Both;
                RailProjectionBars = 30;
                ShowWeakRails = true;
                RailStrongStyle = DashStyleHelper.Solid;
                RailWeakStyle = DashStyleHelper.Dash;
                RailColor = MakeBrush(34, 211, 238);
                UseSideColoring = true;
                RailSupportColor = MakeBrush(45, 212, 191);
                RailResistanceColor = MakeBrush(248, 113, 113);
                RailThickness = 2;
                RailOpacity = 90;
                DimOpacity = 28;
                ShowChevron = true;
                ShowPivotSource = true;
                ShowDimExtension = true;
                CombineRails = true;
                RailCombineTicks = 30;
                ZoneOpacity = 18;

                // 07. Labels
                ShowLabels = true;
                ShowPrices = false;
                LabelFontSize = 11;
                ProximityReveal = true;
                RevealDistance = 50;
                HoverReveal = true;

                // 08. Polarity (buy/sell lean; true bid/ask delta when tick flow is available)
                WallPolarity = false;
                WallDeltaSplit = false;
                DeltaSplitBuysInner = false;
                DeltaSplitOutline = true;
                RailPolarity = true;
                DeltaSource = DeltaSourceEnum.True;
                PolarityDeadzone = 0.10;
                WallBuyColor = MakeBrush(45, 212, 191);
                WallSellColor = MakeBrush(239, 83, 80);
                PolarityNeutralColor = MakeBrush(80, 84, 92);
                ShowPolarityStrip = true;
                PolarityStripWidth = 7;
                CvdLookbackBars = 300;
                CvdThresholdPct = 35.0;
                CvdUseVwMidpoint = true;
                CvdZoneCount = 62;
                CvdSmoothing = 1;

                // 09. HTF POC (weekly / monthly POC + value area)
                HtfSourceMinutes = 30;
                ShowWeeklyPoc = true;
                ShowWeeklyVA = true;
                ShowMonthlyPoc = true;
                PriorWeeksToShow = 1;
                PriorMonthsToShow = 1;
                HtfBackfillDays = 65;   // covers the prior month + developing month; 0 = chart range only

                // 13. HTF Profiles (weekly/monthly profile silhouettes, bar-anchored) - default OFF
                ShowWeeklyProfile = false;
                ShowMonthlyProfile = false;
                WeeklyProfileColor = MakeBrush(96, 130, 200);
                MonthlyProfileColor = MakeBrush(150, 120, 196);
                WeeklyProfileOpacity = 22;
                MonthlyProfileOpacity = 22;
                HtfProfileWidthFrac = 0.9;
                HtfProfileView = false;
                HtfProfileLevels = true;
                HtfProfileLevelOpacity = 75;

                // 14. HTF Alerts (LTF entry-chart proximity banner) - all OFF by default
                ShowHtfAlerts = false;
                WarnWeeklyHvn = false;
                WarnWeeklyLvn = false;
                WarnMonthlyHvn = false;
                WarnMonthlyLvn = false;
                AlertDistance = 25;
                HtfLvnFraction = 0.20;
                AlertHvnColor = MakeBrush(232, 120, 64);
                AlertLvnColor = MakeBrush(96, 170, 220);

                // Weekly family
                WeeklyPocColor = MakeBrush(232, 184, 64);    // amber
                WeeklyPocStyle = DashStyleHelper.Solid;
                WeeklyVaStyle = DashStyleHelper.Dash;
                WeeklyThickness = 1;
                WeeklyOpacity = 75;
                // Prior-week family
                PriorWeekColor = MakeBrush(150, 150, 150);   // grey
                PriorWeekPocStyle = DashStyleHelper.Dash;
                PriorWeekVaStyle = DashStyleHelper.Dot;
                PriorWeekOpacity = 55;
                // Monthly family
                MonthlyPocColor = MakeBrush(196, 141, 233);  // violet
                MonthlyPocStyle = DashStyleHelper.Solid;
                MonthlyThickness = 1;
                MonthlyOpacity = 75;
                // Prior-month family
                PriorMonthColor = MakeBrush(150, 150, 150);  // grey
                PriorMonthStyle = DashStyleHelper.Dash;
                PriorMonthOpacity = 55;

                // 16. Momentum Gate (WAE confluence for HTF alerts) - OFF by default
                MomentumGate = MomentumGateModeEnum.Off;
                MomNormalizeByAtr = true;   // scale-free read (recommended for tick/range and cross-instrument)
                MomAtrPeriod = 14;
                MomAtrThreshold = 0.10;
                MomMacdFast = 20;
                MomMacdSlow = 40;
                MomMacdSignal = 9;
                MomBbPeriod = 20;
                MomBbStdDev = 2.0;
                MomSensitivity = 150;
                MomRequireAcceleration = true;
                MomLongColor = MakeBrush(45, 212, 191);   // teal (matches wall buy)
                MomShortColor = MakeBrush(239, 83, 80);   // red  (matches wall sell)
            }
            else if (State == State.Configure)
            {
                scanHighs.Clear();
                scanLows.Clear();
                scanCloses.Clear();
                scanVols.Clear();
                profBins.Clear();
                profBuyBins.Clear();
                weekBins.Clear();
                monthBins.Clear();
                priorWeeks.Clear();
                priorMonths.Clear();
                priorVA.Clear();
                hvnBands.Clear();
                detected.Clear();
                tracked.Clear();
                hasProfile = false;
                scanRowVol = new double[0];
                profRowVol = new double[0];
                profGradScore = new float[0];
                scanBuyVol = new double[0];
                profBuyRowVol = new double[0];
                cvdRowPol = new double[0];
                curSessionDay = DateTime.MinValue;
                curWeekStart = DateTime.MinValue;
                curMonthStart = DateTime.MinValue;
                devWeekPoc = devWeekVah = devWeekVal = double.NaN;
                devMonthPoc = devMonthVah = devMonthVal = double.NaN;
                curSessHigh = double.MinValue;
                curSessLow = double.MaxValue;
                lastProfBar = -1;
                barTickVol.Clear();
                barTickSum = 0;
                barHadTicks = false;
                barBuyTickVol.Clear();
                curBid = curAsk = double.NaN;
                lastAggressor = 0;
                htfPendingBars.Clear();
                htfSeamTime = DateTime.MinValue;
                htfReady = false;
                devWeekSnap = null; devMonthSnap = null;
                priorWeeksSnap = new HtfProfile[0]; priorMonthsSnap = new HtfProfile[0];
                weekHvnSnap = new List<HvnBand>(); weekLvnSnap = new List<HvnBand>();
                monthHvnSnap = new List<HvnBand>(); monthLvnSnap = new List<HvnBand>();
                trackedSnap = new TrackedLvn[0]; detectedSnap = new TrackedLvn[0];
                frvpSnap = new FrvpZone[0]; priorVASnap = new SessionVA[0]; sessSnap = new RTSession[0];
                midnightOpens.Clear(); midnightSnap = new MidnightOpen[0]; lastMidnightDay = DateTime.MinValue;
                lock (fovSync)
                {
                    fovT.Clear(); fovH.Clear(); fovL.Clear(); fovC.Clear(); fovV.Clear();
                    fovPending.Clear(); fovSeamTime = DateTime.MinValue; fovSeeded = false;
                    fovSnap = new FrvpZone[0];
                }
                lock (obSync)
                {
                    obT.Clear(); obO.Clear(); obH.Clear(); obL.Clear(); obC.Clear(); obV.Clear();
                    obPending.Clear(); obSeamTime = DateTime.MinValue; obSeeded = false;
                    obSnap = new ObZone[0];
                    obPocCache.Clear(); obPocPending.Clear();
                    foreach (var r in obPocRequests) { try { r?.Dispose(); } catch { } }
                    obPocRequests.Clear();
                }

                // Time-based secondary series for HTF (weekly/monthly) volume-at-price. Building these
                // from minute bars instead of the tick/range primary avoids volume-at-price distortion
                // and short-lookback coverage gaps. Same instrument as the chart. Added unconditionally
                // (cheap) so the HTF toggles take effect without a reload.
                AddDataSeries(BarsPeriodType.Minute, Math.Max(1, HtfSourceMinutes));

                // Native FrvpVaMinutes series that carries the out-of-value zones forward after the
                // BarsRequest seeds their history. Added unconditionally (cheap); if it happens to match
                // the HTF period above, NinjaTrader reuses that series and both handlers share the index -
                // which is why the BarsInProgress routing below resolves the index rather than assuming 2.
                AddDataSeries(BarsPeriodType.Minute, Math.Max(1, FrvpVaMinutes));

                // Native ObVaMinutes series that carries the out-of-value order blocks forward after the
                // BarsRequest seeds their history. Added unconditionally; collapses with a matching series
                // above, so the BarsInProgress routing resolves the index rather than assuming a slot.
                AddDataSeries(BarsPeriodType.Minute, Math.Max(1, ObVaMinutes));
            }
            else if (State == State.DataLoaded)
            {
                InitTimeZones();
                frvpAtr = ATR(FrvpAtrPeriod);   // primary-series ATR for the zone-height threshold

                // Resolve which secondary series carries the native FrvpVaMinutes bars. Never assume 2:
                // if FrvpVaMinutes == HtfSourceMinutes, NinjaTrader collapses the two AddDataSeries calls
                // into one and this lands on index 1 alongside the HTF accumulator.
                fovBip = -1;
                int fovMin = Math.Max(1, FrvpVaMinutes);
                for (int i = 1; i < BarsArray.Length; i++)
                {
                    if (BarsArray[i] == null || BarsArray[i].BarsPeriod == null) continue;
                    if (BarsArray[i].BarsPeriod.BarsPeriodType == BarsPeriodType.Minute
                        && BarsArray[i].BarsPeriod.Value == fovMin) { fovBip = i; break; }
                }

                // Momentum-gate indicators (primary series). Built once; cheap when the gate is off
                // since OnBarUpdate skips the read. Periods are tuned for time-based bars - see the
                // Momentum Gate group note for tick/range retuning.
                if (MomentumGate != MomentumGateModeEnum.Off)
                {
                    momMacd = MACD(Math.Max(1, MomMacdFast), Math.Max(2, MomMacdSlow), Math.Max(1, MomMacdSignal));
                    momBoll = Bollinger(MomBbStdDev, Math.Max(1, MomBbPeriod));
                    momAtr  = ATR(Math.Max(1, MomAtrPeriod));
                }

                // Resolve the native ObVaMinutes series index (collapses with the HTF/FOV series if equal).
                obBip = -1;
                int obMin = Math.Max(1, ObVaMinutes);
                for (int i = 1; i < BarsArray.Length; i++)
                {
                    if (BarsArray[i] == null || BarsArray[i].BarsPeriod == null) continue;
                    if (BarsArray[i].BarsPeriod.BarsPeriodType == BarsPeriodType.Minute
                        && BarsArray[i].BarsPeriod.Value == obMin) { obBip = i; break; }
                }

                StartHtfBackfill();
                StartFovBackfill();
                StartObBackfill();
            }
            else if (State == State.Realtime)
            {
                // Historical secondary bars past the seam were buffered, not rebuilt (that would be O(n^2)).
                // Fold them in once, now, so the zones are current the instant the chart goes live.
                PublishFovZones();
                PublishObZones();

                // Paint the instant the chart goes live, even if the historical load was slow.
                Recompute();
            }
            else if (State == State.Historical)
            {
                HookMouse();
            }
            else if (State == State.Terminated)
            {
                UnhookMouse();
                try { htfRequest?.Dispose(); } catch { }
                try { fovRequest?.Dispose(); } catch { }
                try { obRequest?.Dispose(); } catch { }
                lock (obSync) { foreach (var r in obPocRequests) { try { r?.Dispose(); } catch { } } obPocRequests.Clear(); }
                htfRequest = null;
                DisposeDeviceCache();
            }
        }

        // ---- Hover support: track the cursor so a label's row can extend its line on demand. ----
        private void HookMouse()
        {
            if (mouseHooked || ChartControl == null) return;
            try
            {
                ChartControl.Dispatcher.InvokeAsync(() =>
                {
                    ChartControl.MouseMove += OnChartMouseMove;
                    ChartControl.MouseLeave += OnChartMouseLeave;
                });
                mouseHooked = true;
            }
            catch { }
        }

        private void UnhookMouse()
        {
            if (!mouseHooked || ChartControl == null) return;
            try
            {
                ChartControl.Dispatcher.InvokeAsync(() =>
                {
                    ChartControl.MouseMove -= OnChartMouseMove;
                    ChartControl.MouseLeave -= OnChartMouseLeave;
                });
            }
            catch { }
            mouseHooked = false;
        }

        private void OnChartMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!HoverReveal && !ShowFrvpTooltip && !FrvpVaTooltip && !LabelGroupTooltip) { if (mouseValid) { mouseValid = false; } return; }
            try
            {
                var panel = ChartPanel;
                if (panel == null) return;
                var pt = e.GetPosition(panel);
                double sx = panel.ActualWidth > 0 ? panel.W / panel.ActualWidth : 1.0;   // DIP -> device px
                double sy = panel.ActualHeight > 0 ? panel.H / panel.ActualHeight : 1.0;
                mousePxX = (float)(pt.X * sx);
                mousePxY = (float)(pt.Y * sy);
                mouseValid = true;

                // Hit-test the FRVP hover regions (published from render) for the tooltip.
                if (ShowFrvpTooltip)
                {
                    var boxes = frvpHoverSnap;
                    bool hit = false;
                    for (int i = boxes.Length - 1; i >= 0; i--)
                    {
                        var hb = boxes[i];
                        if (mousePxX >= hb.L && mousePxX <= hb.R && mousePxY >= hb.T && mousePxY <= hb.B)
                        {
                            frvpHoveredKey = hb.Key; hit = true; break;
                        }
                    }
                    frvpHasHover = hit;
                }

                // Out-of-value bands hit-test independently: their regions are the bands themselves, so
                // they can overlap an FRVP source box. Render decides which card wins.
                if (FrvpVaTooltip)
                {
                    var fboxes = fovHoverSnap;
                    bool fhit = false;
                    for (int i = fboxes.Length - 1; i >= 0; i--)
                    {
                        var hb = fboxes[i];
                        if (mousePxX >= hb.L && mousePxX <= hb.R && mousePxY >= hb.T && mousePxY <= hb.B)
                        {
                            fovHoveredKey = hb.Key; fhit = true; break;
                        }
                    }
                    fovHasHover = fhit;
                }

                // Out-of-value OB bands hit-test independently, same as the FRVP bands.
                if (ObTooltip)
                {
                    var oboxes = obHoverSnap;
                    bool ohit = false;
                    for (int i = oboxes.Length - 1; i >= 0; i--)
                    {
                        var hb = oboxes[i];
                        if (mousePxX >= hb.L && mousePxX <= hb.R && mousePxY >= hb.T && mousePxY <= hb.B)
                        {
                            obHoveredKey = hb.Key; ohit = true; break;
                        }
                    }
                    obHasHover = ohit;
                }
                ForceRefresh();
            }
            catch { }
        }

        private void OnChartMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (mouseValid || frvpHasHover || fovHasHover || obHasHover)
            {
                mouseValid = false; frvpHasHover = false; fovHasHover = false; obHasHover = false; ForceRefresh();
            }
        }

        // A reference line draws only when price is near it, or (optionally) when its label is hovered.
        // Labels themselves always draw - this is the visibility gate for the LINE only.
        private bool LineVisible(double levelPrice, float y)
        {
            if (!ProximityReveal) return true;
            if (Math.Abs(renderLastPrice - levelPrice) <= RevealDistance) return true;   // RevealDistance is in price points
            // Hover: only when the cursor is actually in the label gutter and within a few px of the row.
            if (HoverReveal && mouseValid
                && mousePxX >= renderWallRightX && mousePxX <= renderWallRightX + 80f
                && Math.Abs(mousePxY - y) <= 4f)
                return true;
            return false;
        }


        private void InitTimeZones()
        {
            try { platformZone = Core.Globals.GeneralOptions.TimeZoneInfo; }
            catch { platformZone = TimeZoneInfo.Local; }
            if (platformZone == null) platformZone = TimeZoneInfo.Local;

            try { refZone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(SessionTimeZoneId) ? "Eastern Standard Time" : SessionTimeZoneId); }
            catch { refZone = platformZone; }

            resetTod = ParseTod(SessionResetTime, new TimeSpan(18, 0, 0));
        }

        private TimeSpan ParseTod(string hhmm, TimeSpan fallback)
        {
            if (!string.IsNullOrWhiteSpace(hhmm))
            {
                var parts = hhmm.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m)
                    && h >= 0 && h < 24 && m >= 0 && m < 60)
                    return new TimeSpan(h, m, 0);
            }
            return fallback;
        }

        private DateTime ToRefZone(DateTime barTime)
        {
            try
            {
                var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(barTime, DateTimeKind.Unspecified), platformZone);
                return TimeZoneInfo.ConvertTimeFromUtc(utc, refZone);
            }
            catch { return barTime; }
        }

        // CME-style trading day keyed off the configurable reset boundary (default 18:00 reference zone).
        private DateTime TradingDayFor(DateTime refTime)
        {
            return refTime.TimeOfDay >= resetTod ? refTime.Date.AddDays(1) : refTime.Date;
        }

        private static Brush MakeBrush(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        protected override void OnBarUpdate()
        {
            // Secondary series. Both handlers are index-resolved rather than hard-coded, because the HTF
            // series and the out-of-value series collapse into one when their periods match.
            if (BarsInProgress != 0)
            {
                if (BarsInProgress == 1) AccumulateHtfBar();
                if (BarsInProgress == fovBip) AccumulateFovBar();
                if (BarsInProgress == obBip) AccumulateObBar();
                return;
            }

            if (BarsInProgress != 0) return;
            if (CurrentBar < 1) return;

            // --- Rolling scan window (each bar arrives once on close) ---
            scanHighs.Add(High[0]);
            scanLows.Add(Low[0]);
            scanCloses.Add(Close[0]);
            scanVols.Add(Volume[0]);
            int cap = Math.Max(10, ScanLookbackBars);
            while (scanHighs.Count > cap)
            {
                scanHighs.RemoveAt(0);
                scanLows.RemoveAt(0);
                scanCloses.RemoveAt(0);
                scanVols.RemoveAt(0);
            }

            // --- Midnight open (00:00 in the reference zone; 12 AM ET by default) ---
            // Keyed off the CALENDAR date, not the 18:00 trading day - a rollover from 23:59 to 00:00
            // marks the first bar of the new date and its Open is the level. The reference zone honours
            // DST, so this tracks 12 AM Eastern year-round rather than a fixed UTC offset.
            if (ShowMidnightOpen)
            {
                DateTime refNow = ToRefZone(Time[0]);
                if (refNow.Date != lastMidnightDay)
                {
                    // Seed on the first bar we ever see: its date is almost never a true 00:00 rollover
                    // (the chart just starts mid-day), so record nothing and arm for the next crossing.
                    bool seeding = lastMidnightDay == DateTime.MinValue;
                    lastMidnightDay = refNow.Date;

                    if (!seeding)
                    {
                        midnightOpens.Add(new MidnightOpen { Day = refNow.Date, Price = Open[0], BarIdx = CurrentBar });
                        int keep = Math.Max(1, MidnightPriorDays + 1);
                        while (midnightOpens.Count > keep) midnightOpens.RemoveAt(0);
                        midnightSnap = midnightOpens.ToArray();   // publish for render
                    }
                }
            }

            // --- Session-anchored profile accumulation ---
            if (lastProfBar != CurrentBar)
            {
                DateTime day = TradingDayFor(ToRefZone(Time[0]));
                if (curSessionDay == DateTime.MinValue)
                {
                    curSessionDay = day;
                    curSessHigh = High[0];
                    curSessLow = Low[0];
                }
                else if (day != curSessionDay)
                {
                    SnapshotPriorSession();
                    profBins.Clear();
                    profBuyBins.Clear();
                    curSessionDay = day;
                    curSessHigh = High[0];
                    curSessLow = Low[0];
                }
                else
                {
                    if (High[0] > curSessHigh) curSessHigh = High[0];
                    if (Low[0] < curSessLow) curSessLow = Low[0];
                }

                CommitBarToProfile();
                lastProfBar = CurrentBar;
            }

            // FRVP consolidation-zone detection (always runs so the display toggle is instant).
            DetectFrvp();

            // Session-level profiles (Asia/London/NY). Gated since they default off; enabling rebuilds on reload.
            if (ShowAsia || ShowLondon || ShowNewYork)
                DetectSession();

            // Momentum-confluence state (drives HTF-alert gating in OnRender). Cheap; skips when off.
            ComputeMomentumState();

            bool nearEnd = CurrentBar >= Bars.Count - 1;
            if (nearEnd || State == State.Realtime)
                Recompute();
        }

        // Capture the live last-trade price independent of bar closes / suspension. This is the price
        // the proximity reveal compares against - the bar series' Close[0] can lag in OnRender.
        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (BarsInProgress != 0) return;

            // Track the live quote so trades can be classified by the bid/ask rule.
            if (e.MarketDataType == MarketDataType.Ask) { curAsk = e.Price; return; }
            if (e.MarketDataType == MarketDataType.Bid) { curBid = e.Price; return; }
            if (e.MarketDataType != MarketDataType.Last) return;

            double prevTrade = lastTradePrice;
            lastTradePrice = e.Price;

            // Exact volume-at-price: bin each trade print into its true row rather than smearing the
            // bar's volume across its range. Accumulated for the forming bar; folded in on bar close.
            // Fires for history too when Tick Replay is enabled, so a reload builds the whole session
            // tick-exact; without it only the realtime-forward portion is exact (rest falls back).
            if (!UseTickVolume || e.Volume <= 0) return;
            double rowSize = ProfileRowSize();
            if (rowSize <= 0) return;
            int row = (int)Math.Floor(e.Price / rowSize);
            barTickVol.TryGetValue(row, out double v);
            barTickVol[row] = v + e.Volume;
            barTickSum += e.Volume;
            barHadTicks = true;

            // True delta: classify the trade's aggressor. At/above the ask = buyer lifted the offer;
            // at/below the bid = seller hit the bid; between (wide/mid print, or no quote yet) fall
            // back to the tick rule (uptick=buy, downtick=sell, unchanged=carry the last aggressor).
            if (DeltaSource == DeltaSourceEnum.True)
            {
                // Prefer the bid/ask carried ON the Last event itself - no quote/trade ordering race.
                // Fall back to the last-seen quote events where the feed doesn't populate them.
                double ask = e.Ask > 0 ? e.Ask : curAsk;
                double bid = e.Bid > 0 ? e.Bid : curBid;
                int agg;
                if (!double.IsNaN(ask) && ask > 0 && e.Price >= ask) agg = 1;
                else if (!double.IsNaN(bid) && bid > 0 && e.Price <= bid) agg = -1;
                else if (!double.IsNaN(prevTrade) && e.Price > prevTrade) agg = 1;
                else if (!double.IsNaN(prevTrade) && e.Price < prevTrade) agg = -1;
                else agg = lastAggressor;   // unchanged / cold start -> carry
                if (agg != 0) lastAggressor = agg;

                if (agg > 0)
                {
                    barBuyTickVol.TryGetValue(row, out double bb);
                    barBuyTickVol[row] = bb + e.Volume;   // sells contribute 0 buy; total already binned
                }
            }
        }

        // Waddah-Attar-Explosion read, distilled to a single directional state for the HTF-alert gate.
        // Trend = slope of the MACD line (now vs 1 bar ago); Explosion = a volatility threshold the trend
        // must clear. Two ways to set that threshold:
        //   Fixed (Bollinger): trend is scaled by Sensitivity and compared to Bollinger band width. Both
        //     are price points, so the right Sensitivity differs by instrument/bar type.
        //   ATR-normalized: the raw trend slope is compared to ATR x Threshold. ATR adapts to each
        //     instrument's volatility, so one Threshold travels across MNQ/MES/MGC and tick/range without
        //     retuning. This is the recommended mode and the default.
        // A "long explosion" is a rising trend whose magnitude clears the explosion line (and, optionally,
        // is accelerating vs the prior slope); short is the mirror. Below the line = dead zone = 0.
        private void ComputeMomentumState()
        {
            if (MomentumGate == MomentumGateModeEnum.Off || momMacd == null)
            {
                momState = 0;
                return;
            }

            // Need enough history for the MACD slope (4 samples back) plus the bands/ATR window.
            int need = Math.Max(MomMacdSlow + MomMacdSignal, Math.Max(MomBbPeriod, MomAtrPeriod)) + 4;
            if (CurrentBar < need) { momState = 0; return; }

            try
            {
                double slopeNow = momMacd[0] - momMacd[1];    // recent MACD-line slope (price points)
                double slopePrev = momMacd[2] - momMacd[3];   // older slope (for acceleration)

                double trendNow, trendPrev, explosion;
                if (MomNormalizeByAtr)
                {
                    double atr = momAtr != null ? momAtr[0] : 0.0;
                    if (atr <= 0) { momState = 0; return; }
                    trendNow = slopeNow;                     // raw slope, no Sensitivity in scale-free mode
                    trendPrev = slopePrev;
                    explosion = atr * MomAtrThreshold;       // volatility-scaled explosion line
                }
                else
                {
                    if (momBoll == null) { momState = 0; return; }
                    trendNow = slopeNow * MomSensitivity;
                    trendPrev = slopePrev * MomSensitivity;
                    explosion = momBoll.Upper[0] - momBoll.Lower[0];
                }

                bool accelUp = !MomRequireAcceleration || trendNow > trendPrev;
                bool accelDn = !MomRequireAcceleration || Math.Abs(trendNow) > Math.Abs(trendPrev);

                if (trendNow > 0 && trendNow > explosion && accelUp)
                    momState = 1;
                else if (trendNow < 0 && Math.Abs(trendNow) > explosion && accelDn)
                    momState = -1;
                else
                    momState = 0;
            }
            catch { momState = 0; }
        }

        // ===== FRVP consolidation-zone detection (ported from RedTailAutoFRVP) =====
        // Grows a running box bar-by-bar; when price breaks out (or the box gets too tall/long), the
        // finished base is profiled into POC/VAH/VAL and stored as a zone.
        private void DetectFrvp()
        {
            if (CurrentBar < 2 || CurrentBar < FrvpAtrPeriod) return;

            UpdateFrvpDepartures();   // measure how far recent zones have departed (strength)
            UpdateFrvpMitigation();   // Fresh -> Tested -> Mitigated (POC close); flip revives, matches AutoFRVP
            RetireFrvpZones();        // hard-remove flip-churned / eroded / abandoned zones (AutoFRVP retirement)

            double th     = CurrentFrvpThreshold();
            double buffer = FrvpBreakoutBufferTicks * TickSize;

            if (!fbActive) { StartFrvpBox(); return; }

            double nH     = Math.Max(fbHigh, High[0]);
            double nL     = Math.Min(fbLow,  Low[0]);
            double cHi    = Math.Max(fbCloseHi, Close[0]);
            double cLo    = Math.Min(fbCloseLo, Close[0]);
            double sizeHi = FrvpUseCloseBand ? cHi : nH;
            double sizeLo = FrvpUseCloseBand ? cLo : nL;
            bool   tooTall = (sizeHi - sizeLo) > th;
            bool   brokeUp = Close[0] > fbHigh + buffer;
            bool   brokeDn = Close[0] < fbLow  - buffer;
            bool   tooLong = FrvpMaxBars > 0 && fbBars >= FrvpMaxBars;

            if (tooTall || brokeUp || brokeDn || tooLong)
            {
                int dir = brokeUp ? 1 : brokeDn ? -1 : (Close[0] >= (fbHigh + fbLow) * 0.5 ? 1 : -1);
                if (fbBars >= FrvpMinBars)
                    FinalizeFrvpZone(fbStartIdx, CurrentBar - 1, dir);
                StartFrvpBox();
                frvpSnap = frvpZones.ToArray();   // list changed (add/supersede) - publish for render
            }
            else
            {
                fbHigh = nH; fbLow = nL; fbCloseHi = cHi; fbCloseLo = cLo;
                fbEndIdx = CurrentBar; fbBars++;
            }
        }

        private void StartFrvpBox()
        {
            fbActive = true;
            fbStartIdx = CurrentBar; fbEndIdx = CurrentBar; fbBars = 1;
            fbHigh = High[0]; fbLow = Low[0]; fbCloseHi = Close[0]; fbCloseLo = Close[0];
            fbStartTime = Time[0];
        }

        private double CurrentFrvpThreshold()
        {
            if (FrvpHeightMode == FrvpHeightModeEnum.FixedTicks)
                return FrvpMaxHeightTicks * TickSize;
            double a = (frvpAtr != null && CurrentBar >= FrvpAtrPeriod) ? frvpAtr[0] : 0;
            if (a <= 0) a = 10 * TickSize;
            return a * FrvpMaxHeightAtr;
        }

        private void UpdateFrvpDepartures()
        {
            for (int i = 0; i < frvpZones.Count; i++)
            {
                FrvpZone z = frvpZones[i];
                if (z.DepartBarsLeft <= 0) continue;
                double exc = z.Dir > 0 ? (High[0] - z.RefEdge) : (z.RefEdge - Low[0]);
                if (exc > z.MaxExcursion) z.MaxExcursion = exc;
                z.DepartBarsLeft--;
                z.Departure = z.Height > 0 ? z.MaxExcursion / z.Height : 0;
                z.Strong = z.Departure >= FrvpMinDeparture;
            }
        }

        // Fresh -> Tested -> Mitigated. A zone must first clear its band (HasLeft) before returns count;
        // a touch back into the band makes it Tested; a close back through the POC mitigates it. Optional
        // flip reverses a zone's role when a close clears the far edge (broken supply becomes demand).
        private void UpdateFrvpMitigation()
        {
            if (!FrvpEnableMitigation) return;
            double buffer = FrvpBreakoutBufferTicks * TickSize;

            for (int i = 0; i < frvpZones.Count; i++)
            {
                FrvpZone z = frvpZones[i];

                // Flip: a close fully through the FAR edge reverses polarity (broken supply -> demand and
                // vice-versa) and RE-ARMS the zone as Tested - it can even revive a mitigated zone. It never
                // mitigates here; flip-churn is handled by retirement (RetireFrvpZones). This is why a single
                // touch + a single flip leaves a zone live, not mitigated.
                if (FrvpFlipOnBreakthrough && z.HasLeft)
                {
                    bool brokeThrough = z.Dir > 0 ? Close[0] < z.VAL - buffer : Close[0] > z.VAH + buffer;
                    if (brokeThrough)
                    {
                        z.Dir = -z.Dir;
                        z.State = FrvpZoneStateEnum.Tested;
                        z.HasLeft = false;
                        z.Inside = false;
                        z.Flips++;
                        continue;
                    }
                }

                if (z.State == FrvpZoneStateEnum.Mitigated) continue;

                if (!z.HasLeft)
                {
                    bool away = z.Dir > 0 ? Low[0] > z.VAH : High[0] < z.VAL;
                    if (away) z.HasLeft = true;
                    continue;
                }

                bool touched = High[0] >= z.VAL && Low[0] <= z.VAH;
                if (touched && !z.Inside) z.Touches++;
                if (touched) z.LastTouchBar = CurrentBar;
                if (!touched && z.Inside && ScoreReactions)
                {
                    // Left the band without a close-through -> a completed hold. Sample the push-off
                    // in the held direction, normalised by the zone's own VA height.
                    double rej = z.Dir > 0 ? (Close[0] - z.VAH) : (z.VAL - Close[0]);
                    if (rej > 0 && z.Height > 0)
                    {
                        double r = rej / z.Height;
                        if (r > z.RejectMax) z.RejectMax = r;
                    }
                    z.Score = ReactionScore(z.Touches, z.RejectMax / 2.0);   // full strength at 2 heights
                }
                z.Inside = touched;

                if (z.State == FrvpZoneStateEnum.Fresh && touched)
                    z.State = FrvpZoneStateEnum.Tested;

                // Pure count model: a zone is never "mitigated" by a close-through. It stays live - Fresh
                // becomes Tested on the first tag, and it flips (re-arming as Tested) on breaks - and is
                // REMOVED purely on touch/flip counts in RetireFrvpZones. Predictable and easy to read:
                // hover a zone to see its counts and how close it is to retiring.
            }
        }

        // Removes zones purely on interaction counts: too many flips (broke clean through) or too many
        // touches (tagged repeatedly). No close-through or distance logic - what you see is what retires.
        private void RetireFrvpZones()
        {
            if (!FrvpEnableMitigation) return;
            bool changed = false;
            for (int i = frvpZones.Count - 1; i >= 0; i--)
            {
                FrvpZone z = frvpZones[i];
                bool retire = (FrvpMaxFlips > 0 && z.Flips >= FrvpMaxFlips)
                           || (FrvpRetireTouches > 0 && z.Touches >= FrvpRetireTouches);
                if (retire) { frvpZones.RemoveAt(i); changed = true; }
            }
            if (changed) frvpSnap = frvpZones.ToArray();
        }

        // True when a zone is one trigger from retirement (for the hover tooltip warning), matching AutoFRVP.
        private string FrvpRetireWarning(FrvpZone z)
        {
            if (FrvpMaxFlips > 1 && z.Flips == FrvpMaxFlips - 1) return "retires on next flip";
            if (FrvpRetireTouches > 1 && z.Touches == FrvpRetireTouches - 1) return "retires on next touch";
            return null;
        }


        private void FinalizeFrvpZone(int startIdx, int endIdx, int dir)
        {
            if (endIdx < startIdx) return;

            double poc, vah, val, totalVol, srcHi, srcLo;
            if (!ComputeFrvpProfile(startIdx, endIdx, out poc, out vah, out val, out totalVol, out srcHi, out srcLo))
                return;

            long key = fbStartTime.Ticks;
            for (int i = 0; i < frvpZones.Count; i++)
                if (frvpZones[i].Key == key) return;   // de-dupe on real-time re-finalize

            // A new zone at the same level supersedes older overlapping ones.
            double newH = vah - val;
            for (int i = frvpZones.Count - 1; i >= 0; i--)
            {
                FrvpZone old = frvpZones[i];
                double ov = Math.Min(old.VAH, vah) - Math.Max(old.VAL, val);
                if (ov <= 0) continue;
                double minH = Math.Min(newH, old.VAH - old.VAL);
                if (minH <= 0) continue;
                if (ov / minH >= FrvpOverlapThreshold)
                    frvpZones.RemoveAt(i);
            }

            double refEdge = dir > 0 ? fbHigh : fbLow;
            double height  = Math.Max(TickSize, vah - val);
            double exc0    = dir > 0 ? (High[0] - refEdge) : (refEdge - Low[0]);
            if (exc0 < 0) exc0 = 0;

            FrvpZone z = new FrvpZone
            {
                Key = key,
                StartTime = fbStartTime,
                EndTime = Time[CurrentBar - endIdx],
                StartBarIdx = startIdx,
                EndBarIdx = endIdx,
                POC = poc, VAH = vah, VAL = val,
                SrcHigh = srcHi, SrcLow = srcLo,
                Bars = fbBars, Dir = dir,
                RefEdge = refEdge, Height = height,
                MaxExcursion = exc0,
                DepartBarsLeft = Math.Max(1, FrvpDepartureBars),
                Departure = exc0 / height,
                Volume = totalVol,
                State = FrvpZoneStateEnum.Fresh,
                LastTouchBar = endIdx
            };
            z.Strong = z.Departure >= FrvpMinDeparture;
            frvpZones.Add(z);

            int cap = Math.Max(4, FrvpMaxZones) + 4;
            while (frvpZones.Count > cap)
                frvpZones.RemoveAt(0);
        }

        private bool ComputeFrvpProfile(int startIdx, int endIdx, out double poc, out double vah, out double val, out double totalVol, out double srcHi, out double srcLo)
        {
            poc = vah = val = totalVol = 0; srcHi = srcLo = 0;

            double hi = double.MinValue, lo = double.MaxValue;
            for (int idx = startIdx; idx <= endIdx; idx++)
            {
                int ba = CurrentBar - idx;
                if (ba < 0 || ba > CurrentBar) continue;
                hi = Math.Max(hi, High[ba]);
                lo = Math.Min(lo, Low[ba]);
            }
            if (hi <= lo) return false;
            srcHi = hi; srcLo = lo;

            int rows = Math.Max(2, FrvpProfileRows);
            double interval = (hi - lo) / (rows - 1);
            if (interval <= 0) return false;

            double[] vol = new double[rows];
            for (int idx = startIdx; idx <= endIdx; idx++)
            {
                int ba = CurrentBar - idx;
                if (ba < 0 || ba > CurrentBar) continue;
                double bl = Low[ba], bh = High[ba], bv = Volume[ba];
                int mn = ClampInt((int)Math.Floor((bl - lo) / interval), 0, rows - 1);
                int mx = ClampInt((int)Math.Ceiling((bh - lo) / interval), 0, rows - 1);
                int touched = mx - mn + 1;
                if (touched <= 0) continue;
                double per = bv / touched;
                for (int j = mn; j <= mx; j++) vol[j] += per;
            }

            int pocI = 0; double maxV = 0, total = 0;
            for (int i = 0; i < rows; i++) { total += vol[i]; if (vol[i] > maxV) { maxV = vol[i]; pocI = i; } }
            if (maxV <= 0) return false;

            double target = total * FrvpValueAreaPct / 100.0;
            int up = pocI, dn = pocI; double sum = vol[pocI];
            while (sum < target)
            {
                double vUp = (up < rows - 1) ? vol[up + 1] : 0;
                double vDn = (dn > 0) ? vol[dn - 1] : 0;
                if (vUp == 0 && vDn == 0) break;
                if (vUp >= vDn) { sum += vUp; up++; } else { sum += vDn; dn--; }
            }

            // Same row convention as the session/HTF profiles: POC at the row CENTER, VAH at the
            // TOP edge of the highest VA row (previously both sat half a row / one edge low, biasing
            // FRVP levels vs everything they're graded against).
            poc = lo + (pocI + 0.5) * interval;
            vah = lo + (up + 1) * interval;
            val = lo + dn * interval;
            totalVol = total;
            return true;
        }

        private static int ClampInt(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        // 0 if price is inside the value area, else distance to the nearest edge (in price points).
        // 0 if price is inside the value area, else distance to the nearest edge (in price points).
        private static double FrvpZoneDistance(FrvpZone z, double price)
        {
            if (price >= z.VAL && price <= z.VAH) return 0;
            return price < z.VAL ? z.VAL - price : price - z.VAH;
        }

        // Generic LVN detector: interior low-volume voids (rows below HtfLvnFraction of peak, bounded
        // above and below by higher-volume rows - i.e. true gaps inside the profile, not the tails).
        private void BuildLvnBands(double[] rowVol, int count, double low, double rowSize, double peak, List<HvnBand> outList)
        {
            outList.Clear();
            if (rowVol == null || count <= 0 || peak <= 0 || rowSize <= 0) return;
            double gate = HtfLvnFraction * peak;

            int first = -1, last = -1;
            for (int r = 0; r < count; r++) { if (rowVol[r] > 0) { if (first < 0) first = r; last = r; } }
            if (first < 0 || last <= first) return;

            int i = first;
            while (i <= last)
            {
                if (rowVol[i] < gate)
                {
                    int start = i;
                    double trough = rowVol[i];
                    while (i <= last && rowVol[i] < gate) { if (rowVol[i] < trough) trough = rowVol[i]; i++; }
                    int end = i - 1;
                    if (start > first && end < last)   // interior void only
                        outList.Add(new HvnBand { LowPrice = low + start * rowSize, HighPrice = low + (end + 1) * rowSize, PeakVol = trough });
                }
                else i++;
            }
        }

        private static double NearestBandDistance(List<HvnBand> bands, double px, out double nearestEdge)
        {
            double best = double.MaxValue; nearestEdge = double.NaN;
            foreach (var b in bands)
            {
                double d = (px >= b.LowPrice && px <= b.HighPrice) ? 0 : (px < b.LowPrice ? b.LowPrice - px : px - b.HighPrice);
                if (d < best)
                {
                    best = d;
                    nearestEdge = px < b.LowPrice ? b.LowPrice : (px > b.HighPrice ? b.HighPrice : (b.LowPrice + b.HighPrice) * 0.5);
                }
            }
            return best;
        }

        // Sticky per-category proximity test: arm when price is outside the clear band, fire when it
        // enters the approach band, hold until it leaves past the clear band (no flicker).
        private void UpdateAlertCategory(int idx, bool enabled, List<HvnBand> bands, double px)
        {
            if (!enabled || bands == null) { alertActive[idx] = false; return; }
            double clear = AlertDistance * 1.35;
            double d = NearestBandDistance(bands, px, out double edge);
            if (alertActive[idx])
            {
                if (d > clear) alertActive[idx] = false;
                else alertPrice[idx] = double.IsNaN(edge) ? alertPrice[idx] : edge;
            }
            else if (d <= AlertDistance && !double.IsNaN(edge))
            {
                alertActive[idx] = true;
                alertPrice[idx] = edge;
            }
        }

        // The band lists are built once per secondary bar close in RefreshHtfSnapshots; only the
        // (cheap) sticky proximity test against live price runs per frame.
        private void UpdateHtfAlerts()
        {
            double px = renderLastPrice;
            UpdateAlertCategory(0, WarnWeeklyHvn, weekHvnSnap, px);
            UpdateAlertCategory(1, WarnWeeklyLvn, weekLvnSnap, px);
            UpdateAlertCategory(2, WarnMonthlyHvn, monthHvnSnap, px);
            UpdateAlertCategory(3, WarnMonthlyLvn, monthLvnSnap, px);
        }

        private void DrawAlertBanner(float canvasLeft, float panelRight)
        {
            bool momOn = MomentumGate != MomentumGateModeEnum.Off;
            bool momActive = momOn && momState != 0;

            // Filter mode gates the entire banner on a live momentum explosion: no explosion, no banner.
            if (MomentumGate == MomentumGateModeEnum.Filter && momState == 0) return;

            // Collect active warnings, sorted by imminence (nearest first), capped at 3.
            var lines = new List<KeyValuePair<double, KeyValuePair<string, Brush>>>();
            string[] tags = { "wHVN", "wLVN", "mHVN", "mLVN" };
            bool[] isHvn = { true, false, true, false };
            double px = renderLastPrice;
            for (int k = 0; k < 4; k++)
            {
                if (!alertActive[k]) continue;
                Brush c = isHvn[k] ? (AlertHvnColor ?? Brushes.OrangeRed) : (AlertLvnColor ?? Brushes.DeepSkyBlue);
                string txt = "Near " + tags[k] + " " + FormatP(alertPrice[k]);
                lines.Add(new KeyValuePair<double, KeyValuePair<string, Brush>>(Math.Abs(px - alertPrice[k]), new KeyValuePair<string, Brush>(txt, c)));
            }
            // Nothing to show if there are no level warnings and no momentum chip to draw.
            if (lines.Count == 0 && !momActive) return;
            lines.Sort((a, b) => a.Key.CompareTo(b.Key));
            int n = Math.Min(3, lines.Count);

            float centerX = (canvasLeft + panelRight) * 0.5f;
            float w = 240f, lh = Math.Max(16f, LabelFontSize + 8f);
            float y = 8f;

            // Momentum chip (Highlight or Filter, when exploding) sits above the level lines.
            if (momActive)
            {
                Brush mc = momState > 0 ? (MomLongColor ?? Brushes.Aqua) : (MomShortColor ?? Brushes.Red);
                string mtxt = momState > 0 ? "\u25B2 Momentum Long" : "\u25BC Momentum Short";
                DrawBannerLine(mtxt, mc, centerX, y, w, lh, 0.30f);
                y += lh;
            }

            // Brighten the level tint when momentum agrees, so confluence reads at a glance.
            float lineBg = momActive ? 0.26f : 0.16f;
            for (int i = 0; i < n; i++)
            {
                DrawBannerLine(lines[i].Value.Key, lines[i].Value.Value, centerX, y, w, lh, lineBg);
                y += lh;
            }
        }

        // One centered banner row (translucent fill + bold text). Shared by the level warnings and the
        // momentum chip so they render identically.
        private void DrawBannerLine(string text, Brush color, float centerX, float y, float w, float lh, float bgOpacity)
        {
            try
            {
                var rect = new SharpDX.RectangleF(centerX - w / 2f, y, w, lh - 2f);
                var dx = AcquireBrush(color, bgOpacity);   // opacity is honored at each draw call,
                RenderTarget.FillRectangle(rect, dx);      // so one cached brush serves fill + text
                var tf = GetBoldTextFormat();
                dx.Opacity = 0.95f;
                RenderTarget.DrawText(text, tf, rect, dx);
            }
            catch { }
        }

        private SharpDX.DirectWrite.TextFormat GetBoldTextFormat()
        {
            int size = Math.Max(8, LabelFontSize + 1);
            if (cachedTfBold == null || cachedTfBold.IsDisposed || cachedTfBoldSize != size)
            {
                cachedTfBold?.Dispose();
                cachedTfBold = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial",
                    SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, size);
                cachedTfBold.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
                cachedTfBold.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
                cachedTfBoldSize = size;
            }
            return cachedTfBold;
        }

        private string FormatP(double price)
        {
            try { return Instrument.MasterInstrument.FormatPrice(price); }
            catch { return price.ToString("0.##"); }
        }

        // ===== Session levels (Asia / London / NY profiles) =====
        private void ParseSessTimes()
        {
            sessAsiaStart = ParseTod(AsiaStartText, new TimeSpan(18, 0, 0));
            sessAsiaEnd   = ParseTod(AsiaEndText, new TimeSpan(3, 0, 0));
            sessLonStart  = ParseTod(LondonStartText, new TimeSpan(3, 0, 0));
            sessLonEnd    = ParseTod(LondonEndText, new TimeSpan(9, 30, 0));
            sessNyStart   = ParseTod(NewYorkStartText, new TimeSpan(9, 30, 0));
            sessNyEnd     = ParseTod(NewYorkEndText, new TimeSpan(17, 0, 0));
            sessTimesParsed = true;
        }

        private static bool InSessWindow(TimeSpan tod, TimeSpan start, TimeSpan end)
        {
            if (start <= end) return tod >= start && tod < end;   // half-open
            return tod >= start || tod < end;                     // wraps midnight (Asia)
        }

        private int SessionTypeFor(TimeSpan tod)
        {
            if (InSessWindow(tod, sessAsiaStart, sessAsiaEnd)) return 0;
            if (InSessWindow(tod, sessLonStart, sessLonEnd)) return 1;
            if (InSessWindow(tod, sessNyStart, sessNyEnd)) return 2;
            return -1;
        }

        private bool IsSessEnabled(int t)
        {
            return t == 0 ? ShowAsia : t == 1 ? ShowLondon : t == 2 ? ShowNewYork : false;
        }

        private Brush SessColorFor(int t)
        {
            return t == 0 ? (AsiaColor ?? Brushes.Khaki)
                 : t == 1 ? (LondonColor ?? Brushes.DeepSkyBlue)
                 : (NewYorkColor ?? Brushes.MediumPurple);
        }

        private static string SessTagFor(int t)
        {
            return t == 0 ? "ASIA" : t == 1 ? "LON" : t == 2 ? "NY" : "";
        }

        // Under Calculate.OnBarClose each bar closes once: commit it into its session, rolling sessions at
        // the window boundaries. Profiles recompute incrementally from the per-session bin map.
        private void DetectSession()
        {
            if (!sessTimesParsed) ParseSessTimes();

            int sessNow = SessionTypeFor(ToRefZone(Time[0]).TimeOfDay);
            int activeType = sessActive != null ? sessActive.Type : -1;

            if (sessNow != activeType)
            {
                if (sessActive != null) { ComputeSessProfile(sessActive); sessActive.IsComplete = true; }
                sessActive = null;

                if (sessNow >= 0)
                {
                    sessActive = new RTSession
                    {
                        Type = sessNow,
                        TradingDay = TradingDayFor(ToRefZone(Time[0])),
                        StartTime = Time[0],
                        Open = Open[0], High = High[0], Low = Low[0],
                        POC = Open[0], VAH = Open[0], VAL = Open[0],
                        HasData = true
                    };
                    sessList.Add(sessActive);
                    int cap = Math.Max(24, (Math.Max(0, SessPreviousDays) + 2) * 3 + 6);
                    while (sessList.Count > cap)
                    {
                        if (sessList[0] == sessActive) break;
                        sessList.RemoveAt(0);
                    }
                }
            }

            if (sessActive != null && sessNow == sessActive.Type)
            {
                CommitSessBar(sessActive, High[0], Low[0], Volume[0]);
                ComputeSessProfile(sessActive);
            }

            sessSnap = sessList.ToArray();   // publish for render
        }

        private void CommitSessBar(RTSession s, double h, double l, double vol)
        {
            double bs = TickSize * Math.Max(1, SessTicksPerRow);
            if (vol <= 0 || bs <= 0) return;

            int lowBin = (int)Math.Floor(l / bs);
            int highBin = (int)Math.Floor(h / bs);
            if (highBin < lowBin) { int t = lowBin; lowBin = highBin; highBin = t; }
            int n = Math.Max(1, highBin - lowBin + 1);
            double per = vol / n;
            for (int b = lowBin; b <= highBin; b++)
            {
                double cur;
                s.Bins[b] = s.Bins.TryGetValue(b, out cur) ? cur + per : per;
            }
            if (h > s.High) s.High = h;
            if (l < s.Low) s.Low = l;
            s.HasData = true;
        }

        private void ComputeSessProfile(RTSession s)
        {
            if (s == null || s.Bins.Count == 0) return;
            double bs = TickSize * Math.Max(1, SessTicksPerRow);

            double total = 0, maxV = double.MinValue;
            int pocBin = 0;
            foreach (var kv in s.Bins)
            {
                total += kv.Value;
                if (kv.Value > maxV) { maxV = kv.Value; pocBin = kv.Key; }
            }
            if (total <= 0) return;

            s.POC = pocBin * bs + bs / 2.0;

            var keys = s.Bins.Keys.OrderBy(k => k).ToList();
            int pocPos = keys.IndexOf(pocBin);
            if (pocPos < 0) { s.VAH = s.POC; s.VAL = s.POC; return; }

            double target = total * (SessValueAreaPct / 100.0);
            double acc = maxV;
            int up = pocPos, dn = pocPos;
            while (acc < target && (up < keys.Count - 1 || dn > 0))
            {
                double vUp = up < keys.Count - 1 ? s.Bins[keys[up + 1]] : -1;
                double vDn = dn > 0 ? s.Bins[keys[dn - 1]] : -1;
                if (vUp < 0 && vDn < 0) break;
                if (vDn < 0 || (vUp >= 0 && vUp >= vDn)) { acc += vUp; up++; }
                else { acc += vDn; dn--; }
            }
            s.VAH = keys[up] * bs + bs / 2.0;
            s.VAL = keys[dn] * bs + bs / 2.0;
        }

        private IEnumerable<RTSession> SessionsToDraw()
        {
            var withData = sessSnap.Where(s => s.HasData && IsSessEnabled(s.Type)).ToList();
            if (withData.Count == 0) return withData;
            var days = withData.Select(s => s.TradingDay).Distinct().OrderByDescending(d => d).ToList();
            int keep = Math.Min(days.Count, Math.Max(0, SessPreviousDays) + 1);
            var allowed = new HashSet<DateTime>(days.Take(keep));
            return withData.Where(s => allowed.Contains(s.TradingDay)).ToList();
        }

        // Session profile levels, drawn through the shared DrawHLine path so they get the same proximity
        // reveal + persistent labels as the rest. Coloured per session; line style is fixed per level type.
        private void DrawSessionLevels(ChartScale chartScale, float leftX, float wallRightX)
        {
            foreach (var s in SessionsToDraw())
            {
                Brush c = SessColorFor(s.Type);
                string pfx = SessTagFor(s.Type) + " ";

                if (ShowSessPOC)
                    DrawHLine(s.POC, c, DashStyleHelper.Solid, 1, SessLevelOpacity, leftX, wallRightX, chartScale.GetYByValue(s.POC), ShowLabels ? pfx + "POC" + PriceSuffix(s.POC) : null, true, LabelRankEnum.Session);
                if (ShowSessVAH)
                    DrawHLine(s.VAH, c, DashStyleHelper.Dash, 1, SessLevelOpacity, leftX, wallRightX, chartScale.GetYByValue(s.VAH), ShowLabels ? pfx + "VAH" + PriceSuffix(s.VAH) : null, false, LabelRankEnum.Session);
                if (ShowSessVAL)
                    DrawHLine(s.VAL, c, DashStyleHelper.Dash, 1, SessLevelOpacity, leftX, wallRightX, chartScale.GetYByValue(s.VAL), ShowLabels ? pfx + "VAL" + PriceSuffix(s.VAL) : null, false, LabelRankEnum.Session);
                if (ShowSessOpen)
                    DrawHLine(s.Open, c, DashStyleHelper.Dot, 1, SessLevelOpacity, leftX, wallRightX, chartScale.GetYByValue(s.Open), ShowLabels ? pfx + "Open" + PriceSuffix(s.Open) : null, false, LabelRankEnum.Session);
                if (ShowSessHigh && s.High > double.MinValue)
                    DrawHLine(s.High, c, DashStyleHelper.Solid, 1, SessLevelOpacity, leftX, wallRightX, chartScale.GetYByValue(s.High), ShowLabels ? pfx + "High" + PriceSuffix(s.High) : null, false, LabelRankEnum.Session);
                if (ShowSessLow && s.Low < double.MaxValue)
                    DrawHLine(s.Low, c, DashStyleHelper.Solid, 1, SessLevelOpacity, leftX, wallRightX, chartScale.GetYByValue(s.Low), ShowLabels ? pfx + "Low" + PriceSuffix(s.Low) : null, false, LabelRankEnum.Session);
            }
        }

        // The 12 AM ET open, drawn as a plain reference line. Today's is bright; prior days (if any)
        // are dimmed and date-stamped. Optionally anchored to the bar that opened the day, so the line
        // starts where the level was set rather than running the full canvas width.
        private void DrawMidnightOpen(ChartScale chartScale, float leftX, float wallRightX)
        {
            var snap = midnightSnap;
            if (snap.Length == 0) return;

            Brush c = MidnightOpenColor ?? Brushes.Goldenrod;
            int last = snap.Length - 1;

            for (int i = 0; i < snap.Length; i++)
            {
                MidnightOpen m = snap[i];
                if (m == null || m.Price <= 0) continue;

                bool today = i == last;
                int op = today ? MidnightOpenOpacity : Math.Max(15, MidnightOpenOpacity - 35);

                float x0 = leftX;
                if (MidnightAnchorToOpen && renderCC != null && m.BarIdx >= 0)
                {
                    try
                    {
                        float ox = renderCC.GetXByBarIndex(ChartBars, m.BarIdx);
                        if (ox > x0) x0 = ox;
                        if (x0 > wallRightX - 4f) x0 = wallRightX - 4f;
                    }
                    catch { x0 = leftX; }
                }

                string lbl = null;
                if (ShowLabels)
                    lbl = (today ? "MO" : "MO " + m.Day.ToString("MM/dd")) + PriceSuffix(m.Price);

                DrawHLine(m.Price, c, MidnightOpenStyle, MidnightOpenThickness, op,
                          x0, wallRightX, chartScale.GetYByValue(m.Price), lbl, false, LabelRankEnum.Session, !today);
            }
        }



        // Single source of truth for the structural-profile row height, so tick binning (OnMarketData)
        // and the OHLC fallback (AddBarToProfile) resolve to identical absolute row indices.
        private double ProfileRowSize()
        {
            double ts = TickSize > 0 ? TickSize : 1.0;
            return ts * Math.Max(1, ProfileTicksPerRow);
        }

        // Buy/sell lean of the session profile at a given price, -1 (seller) .. +1 (buyer). Reads the
        // same bins the wall tint uses, so a rail dot reflects real delta wherever ticks were classified
        // (proxy-based otherwise). Returns NaN when the level hasn't traded this session (no override).
        private double SessionPolarityAt(double price)
        {
            double rs = ProfileRowSize();
            if (rs <= 0) return double.NaN;

            // CVD source: read the normalized net delta at this price from the wall-aligned map so the
            // rail dot matches the wall/strip. No override (NaN) when the price is outside the current
            // profile's row range; 0 (neutral dot) when in range but no decisive delta landed here.
            if (DeltaSource == DeltaSourceEnum.Cvd)
            {
                if (cvdRowPol.Length != profRowCount || profRowSize <= 0) return double.NaN;
                int baseRow = (int)Math.Floor(profLow / profRowSize + 0.5);
                int r = (int)Math.Floor(price / profRowSize) - baseRow;
                if (r < 0 || r >= cvdRowPol.Length) return double.NaN;
                return cvdRowPol[r];
            }

            int row = (int)Math.Floor(price / rs);
            if (!profBins.TryGetValue(row, out double tot) || tot <= 0) return double.NaN;
            profBuyBins.TryGetValue(row, out double buy);
            return 2.0 * buy / tot - 1.0;
        }

        // Fold the just-closed primary bar into the session profile. Prefers exact volume-at-price
        // from the trade prints captured this bar; falls back to the OHLC uniform distribution when
        // tick coverage is thin so no row is left under-filled. The OHLC buy proxy is allocated onto
        // the tick-touched rows (buy <= total per row) so polarity stays consistent until real
        // bid/ask delta replaces it.
        private void CommitBarToProfile()
        {
            bool tickOk = UseTickVolume && barHadTicks && barTickSum >= Volume[0] * TickCoverageMin;

            if (tickOk)
            {
                profRowSize = ProfileRowSize();
                bool useDelta = DeltaSource == DeltaSourceEnum.True;
                double range = High[0] - Low[0];
                double buyFrac = range > 0 ? (Close[0] - Low[0]) / range : 0.5;
                foreach (var kv in barTickVol)
                {
                    profBins.TryGetValue(kv.Key, out double v);
                    profBins[kv.Key] = v + kv.Value;

                    // Buy portion: real classified delta when enabled (buy <= this row's total by
                    // construction), otherwise the OHLC proxy allocated onto the traded rows.
                    double buy;
                    if (useDelta) barBuyTickVol.TryGetValue(kv.Key, out buy);
                    else          buy = kv.Value * buyFrac;

                    profBuyBins.TryGetValue(kv.Key, out double b);
                    profBuyBins[kv.Key] = b + buy;
                }
            }
            else
            {
                AddBarToProfile(High[0], Low[0], Close[0], Volume[0]);
            }

            barTickVol.Clear();
            barBuyTickVol.Clear();
            barTickSum = 0;
            barHadTicks = false;
        }

        private void AddBarToProfile(double bh, double bl, double bc, double bv)
        {
            profRowSize = ProfileRowSize();
            double inv = 1.0 / profRowSize;

            int startRow = (int)Math.Floor(bl * inv);
            int endRow = (int)Math.Floor(bh * inv);
            double range = bh - bl;

            // OHLC buy/sell proxy: close near high => buyer-dominated, near low => seller-dominated.
            double buyFrac = range > 0 ? (bc - bl) / range : 0.5;
            double buyVol = bv * buyFrac;

            if (range <= 0)
            {
                profBins.TryGetValue(startRow, out double v0);
                profBins[startRow] = v0 + bv;
                profBuyBins.TryGetValue(startRow, out double b0);
                profBuyBins[startRow] = b0 + buyVol;
                return;
            }

            double invRange = 1.0 / range;
            for (int row = startRow; row <= endRow; row++)
            {
                double levelLow = row * profRowSize;
                double levelHigh = levelLow + profRowSize;
                double ovHigh = Math.Min(bh, levelHigh);
                double ovLow = Math.Max(bl, levelLow);
                double ov = ovHigh - ovLow;
                if (ov > 0)
                {
                    double w = ov * invRange;
                    profBins.TryGetValue(row, out double v);
                    profBins[row] = v + bv * w;
                    profBuyBins.TryGetValue(row, out double b);
                    profBuyBins[row] = b + buyVol * w;
                }
            }
        }

        // Chart-secondary-series entry point for HTF accumulation. While the backfill request is in
        // flight the bar is buffered (the request's bars must merge first, chronologically); once the
        // seam is established, bars at or before it are already covered and are skipped.
        private void AccumulateHtfBar()
        {
            if (CurrentBars[1] < 0) return;
            if (!HtfConsumersEnabled()) return;

            lock (htfSync)
            {
                if (!htfReady)
                {
                    htfPendingBars.Add(new HtfBar { T = Time[0], H = High[0], L = Low[0], V = Volume[0] });
                    return;
                }
                if (Time[0] <= htfSeamTime) return;   // covered by the backfill
                ProcessHtfBar(Time[0], High[0], Low[0], Volume[0]);
                RefreshHtfSnapshots();
            }
        }

        private bool HtfConsumersEnabled()
        {
            return ShowWeeklyPoc || ShowWeeklyVA || ShowMonthlyPoc || ShowWeeklyProfile || ShowMonthlyProfile
                   || WarnWeeklyHvn || WarnWeeklyLvn || WarnMonthlyHvn || WarnMonthlyLvn;
        }

        // Fold one time-based HTF bar (chart time) into the weekly/monthly bins, rolling periods at
        // their boundaries. Called from the chart's secondary series AND the backfill callback -
        // always under htfSync, always in chronological order.
        private void ProcessHtfBar(DateTime barTime, double h, double l, double v)
        {
            double ts = TickSize > 0 ? TickSize : 1.0;
            profRowSize = ts * Math.Max(1, ProfileTicksPerRow);

            DateTime day = TradingDayFor(ToRefZone(barTime));
            DateTime wkStart = WeekStartOf(day);
            DateTime moStart = MonthStartOf(day);

            if (curWeekStart == DateTime.MinValue) { curWeekStart = wkStart; curWeekStartTime = barTime; }
            else if (wkStart != curWeekStart)
            {
                SnapshotPriorHtf(weekBins, priorWeeks, curWeekStart, PriorWeeksToShow, ShowWeeklyProfile, curWeekStartTime, barTime);
                weekBins.Clear(); curWeekStart = wkStart; curWeekStartTime = barTime;
            }
            if (curMonthStart == DateTime.MinValue) { curMonthStart = moStart; curMonthStartTime = barTime; }
            else if (moStart != curMonthStart)
            {
                SnapshotPriorHtf(monthBins, priorMonths, curMonthStart, PriorMonthsToShow, ShowMonthlyProfile, curMonthStartTime, barTime);
                monthBins.Clear(); curMonthStart = moStart; curMonthStartTime = barTime;
            }

            if (ShowWeeklyPoc || ShowWeeklyVA || ShowWeeklyProfile || WarnWeeklyHvn || WarnWeeklyLvn) DistributeToBins(weekBins, h, l, v);
            if (ShowMonthlyPoc || ShowMonthlyProfile || WarnMonthlyHvn || WarnMonthlyLvn) DistributeToBins(monthBins, h, l, v);
        }

        // Request HtfBackfillDays of minute history via BarsRequest, independent of the chart's loaded
        // range (an AddDataSeries secondary always mirrors the primary's range, so a 3-day tick chart
        // would otherwise yield a 3-day "monthly" profile). Minute data at this depth is a few
        // thousand bars - cheap to fetch. Until it lands, chart-series bars buffer and the HTF layers
        // are gated (htfReady) so a partial profile never draws.
        private void StartHtfBackfill()
        {
            if (HtfBackfillDays <= 0 || !HtfConsumersEnabled())
            {
                lock (htfSync) { DrainHtfPending(); RefreshHtfSnapshots(); htfReady = true; }
                return;
            }

            try
            {
                htfRequest = new BarsRequest(Instrument, DateTime.Now.AddDays(-Math.Max(7, HtfBackfillDays)), DateTime.Now)
                {
                    BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = Math.Max(1, HtfSourceMinutes) },
                    TradingHours = Bars.TradingHours
                };
                htfRequest.Request((request, errorCode, errorMessage) =>
                {
                    try
                    {
                        if (State == State.Terminated) return;
                        lock (htfSync)
                        {
                            if (errorCode == ErrorCode.NoError && request != null && request.Bars != null)
                            {
                                var b = request.Bars;
                                for (int i = 0; i < b.Count; i++)
                                {
                                    DateTime t = b.GetTime(i);
                                    ProcessHtfBar(t, b.GetHigh(i), b.GetLow(i), b.GetVolume(i));
                                    if (t > htfSeamTime) htfSeamTime = t;
                                }
                            }
                            else
                            {
                                Print(Name + ": HTF backfill failed (" + errorCode + ") " + errorMessage
                                    + " - falling back to the chart-loaded range.");
                            }
                            DrainHtfPending();
                            RefreshHtfSnapshots();
                            htfReady = true;
                        }
                        ForceRefresh();
                    }
                    catch (Exception ex)
                    {
                        Print(Name + ": HTF backfill merge error: " + ex.Message);
                        lock (htfSync) { DrainHtfPending(); RefreshHtfSnapshots(); htfReady = true; }
                    }
                });
            }
            catch (Exception ex)
            {
                Print(Name + ": HTF backfill request error: " + ex.Message);
                lock (htfSync) { DrainHtfPending(); RefreshHtfSnapshots(); htfReady = true; }
            }
        }

        // Kick off the out-of-value FRVP request: FrvpVaLookbackDays of 1-minute history via its own
        // BarsRequest (independent of chart range), aggregated to FrvpVaMinutes for detection. Runs off
        // the render/primary thread; publishes fovSnap atomically. Opt-in; takes effect on reload.
        private void StartFovBackfill()
        {
            if (!ShowFrvpOutOfVa || FrvpVaLookbackDays <= 0) return;
            try
            {
                int days = Math.Max(1, FrvpVaLookbackDays);
                // Pad the request with extra history. The window's left edge is poisoned: the Wilder ATR
                // restarts there, and no box can begin before bar FrvpAtrPeriod. Without a pad, the oldest
                // few zones are detected against a cold ATR and get truncated start bars - which is exactly
                // the "edges don't quite line up" signature. The pad bars feed the ATR and the box detector
                // but never emit a zone (see fovEmitFrom below).
                int padDays = Math.Max(1, FrvpVaWarmupDays);
                fovRequest = new BarsRequest(Instrument, DateTime.Now.AddDays(-(days + padDays)), DateTime.Now)
                {
                    // Request NATIVE FrvpVaMinutes bars - the same bars AutoFRVP's chart is built from - so
                    // detection runs on identical OHLC (no synthetic aggregation, which misaligned the bars).
                    BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = Math.Max(1, FrvpVaMinutes) },
                    // Session template decides where each native bar starts and which bars exist at all.
                    // If the comparison chart runs a different template from the instrument default, every
                    // bar boundary shifts and the boxes land on different bars. Match whatever the chart
                    // you are diffing against uses.
                    TradingHours = (FrvpVaUseChartSession && Bars != null && Bars.TradingHours != null)
                                   ? Bars.TradingHours
                                   : Instrument.MasterInstrument.TradingHours
                };
                fovRequest.Request((request, errorCode, errorMessage) =>
                {
                    try
                    {
                        if (State == State.Terminated) return;
                        if (errorCode == ErrorCode.NoError && request != null && request.Bars != null)
                        {
                            SeedFovBars(request.Bars);
                            ForceRefresh();
                        }
                        else
                        {
                            Print(Name + ": out-of-value FRVP request failed (" + errorCode + ") " + errorMessage);
                        }
                    }
                    catch (Exception ex) { Print(Name + ": out-of-value FRVP build error: " + ex.Message); }
                });
            }
            catch (Exception ex) { Print(Name + ": out-of-value FRVP request error: " + ex.Message); }
        }

        // Load the backfilled native bars into the rolling window, fold in anything the live series
        // already delivered past the seam, and publish. Runs on the BarsRequest callback thread.
        private void SeedFovBars(Bars b)
        {
            lock (fovSync)
            {
                fovT.Clear(); fovH.Clear(); fovL.Clear(); fovC.Clear(); fovV.Clear();
                int n = b.Count;
                for (int i = 0; i < n; i++)
                {
                    fovT.Add(b.GetTime(i)); fovH.Add(b.GetHigh(i)); fovL.Add(b.GetLow(i));
                    fovC.Add(b.GetClose(i)); fovV.Add(b.GetVolume(i));
                }
                fovSeamTime = fovT.Count > 0 ? fovT[fovT.Count - 1] : DateTime.MinValue;
                fovSeeded = true;

                // The chart's own secondary series may already have pushed bars while the request was in
                // flight. Merge only those past the seam - the rest are duplicates of the backfill.
                for (int i = 0; i < fovPending.Count; i++)
                {
                    FovBar p = fovPending[i];
                    if (p.T <= fovSeamTime) continue;
                    fovT.Add(p.T); fovH.Add(p.H); fovL.Add(p.L); fovC.Add(p.C); fovV.Add(p.V);
                    fovSeamTime = p.T;
                }
                fovPending.Clear();

                TrimFovWindow();
                fovSnap = RebuildFovZones(true);
            }
        }

        // One native FrvpVaMinutes bar closed. Append it and re-run the FULL detection over the window -
        // the same call the seed makes - so a live zone ages exactly as a backfilled one does. This is
        // the parity guarantee: there is no incremental mitigation path that can diverge. The rebuild is
        // a few thousand bars every FrvpVaMinutes minutes, which is nothing.
        private void AccumulateFovBar()
        {
            if (!ShowFrvpOutOfVa) return;
            bool publish;
            lock (fovSync)
            {
                if (!fovSeeded)
                {
                    // Request still in flight. Buffer; SeedFovBars will merge whatever is past its seam.
                    // Bounded, in case the request errors out and never seeds - it is only ever drained
                    // against the seam anyway, so dropping the oldest costs nothing.
                    if (fovPending.Count >= 20000) fovPending.RemoveAt(0);
                    fovPending.Add(new FovBar { T = Time[0], H = High[0], L = Low[0], C = Close[0], V = Volume[0] });
                    return;
                }
                if (Time[0] <= fovSeamTime) return;   // already covered by the backfill

                fovT.Add(Time[0]); fovH.Add(High[0]); fovL.Add(Low[0]); fovC.Add(Close[0]); fovV.Add(Volume[0]);
                fovSeamTime = Time[0];
                TrimFovWindow();

                // Historical replay would rebuild once per bar - O(n^2). Buffer through the load; the
                // State.Realtime transition folds it all in with a single rebuild.
                publish = State == State.Realtime;
                if (publish) fovSnap = RebuildFovZones();
            }
            if (publish) ForceRefresh();
        }

        // Force a rebuild + publish from whatever the window currently holds.
        private void PublishFovZones()
        {
            if (!ShowFrvpOutOfVa) return;
            lock (fovSync) { if (fovSeeded) fovSnap = RebuildFovZones(); }
        }

        // Drop bars that have aged out of the lookback window PLUS its warm-up pad. Caller holds fovSync.
        private void TrimFovWindow()
        {
            if (fovT.Count == 0) return;
            DateTime cutoff = fovT[fovT.Count - 1]
                .AddDays(-(Math.Max(1, FrvpVaLookbackDays) + Math.Max(1, FrvpVaWarmupDays)));
            int drop = 0;
            while (drop < fovT.Count && fovT[drop] < cutoff) drop++;
            if (drop <= 0) return;
            fovT.RemoveRange(0, drop); fovH.RemoveRange(0, drop);
            fovL.RemoveRange(0, drop); fovC.RemoveRange(0, drop); fovV.RemoveRange(0, drop);
        }

        // ===== Order Block subsystem (mirrors the FOV backfill exactly; detector is OB-native) =====

        // BarsRequest for the OB history, aggregated to ObVaMinutes. Opt-in; takes effect on reload.
        private void StartObBackfill()
        {
            if (!ShowObOutOfVa || ObVaLookbackDays <= 0) return;
            try
            {
                int days = Math.Max(1, ObVaLookbackDays);
                int padDays = Math.Max(1, ObVaWarmupDays);
                obRequest = new BarsRequest(Instrument, DateTime.Now.AddDays(-(days + padDays)), DateTime.Now)
                {
                    BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = Math.Max(1, ObVaMinutes) },
                    TradingHours = (ObVaUseChartSession && Bars != null && Bars.TradingHours != null)
                                   ? Bars.TradingHours
                                   : Instrument.MasterInstrument.TradingHours
                };
                obRequest.Request((request, errorCode, errorMessage) =>
                {
                    try
                    {
                        if (State == State.Terminated) return;
                        if (errorCode == ErrorCode.NoError && request != null && request.Bars != null)
                        {
                            SeedObBars(request.Bars);
                            ForceRefresh();
                        }
                        else Print(Name + ": out-of-value OB request failed (" + errorCode + ") " + errorMessage);
                    }
                    catch (Exception ex) { Print(Name + ": out-of-value OB build error: " + ex.Message); }
                });
            }
            catch (Exception ex) { Print(Name + ": out-of-value OB request error: " + ex.Message); }
        }

        private void SeedObBars(Bars b)
        {
            lock (obSync)
            {
                obT.Clear(); obO.Clear(); obH.Clear(); obL.Clear(); obC.Clear(); obV.Clear();
                int n = b.Count;
                for (int i = 0; i < n; i++)
                {
                    obT.Add(b.GetTime(i)); obO.Add(b.GetOpen(i)); obH.Add(b.GetHigh(i));
                    obL.Add(b.GetLow(i)); obC.Add(b.GetClose(i)); obV.Add(b.GetVolume(i));
                }
                obSeamTime = obT.Count > 0 ? obT[obT.Count - 1] : DateTime.MinValue;
                obSeeded = true;

                for (int i = 0; i < obPending.Count; i++)
                {
                    ObBar p = obPending[i];
                    if (p.T <= obSeamTime) continue;
                    obT.Add(p.T); obO.Add(p.O); obH.Add(p.H); obL.Add(p.L); obC.Add(p.C); obV.Add(p.V);
                    obSeamTime = p.T;
                }
                obPending.Clear();

                TrimObWindow();
                obSnap = RebuildObZones(true);
            }
        }

        // One native ObVaMinutes bar closed. Append and re-run the full detection (same call the seed makes).
        private void AccumulateObBar()
        {
            if (!ShowObOutOfVa) return;
            bool publish;
            lock (obSync)
            {
                if (!obSeeded)
                {
                    if (obPending.Count >= 20000) obPending.RemoveAt(0);
                    obPending.Add(new ObBar { T = Times[obBip][0], O = Opens[obBip][0], H = Highs[obBip][0],
                                              L = Lows[obBip][0], C = Closes[obBip][0], V = Volumes[obBip][0] });
                    return;
                }
                if (Times[obBip][0] <= obSeamTime) return;

                obT.Add(Times[obBip][0]); obO.Add(Opens[obBip][0]); obH.Add(Highs[obBip][0]);
                obL.Add(Lows[obBip][0]); obC.Add(Closes[obBip][0]); obV.Add(Volumes[obBip][0]);
                obSeamTime = Times[obBip][0];
                TrimObWindow();

                publish = State == State.Realtime;
                if (publish) obSnap = RebuildObZones();
            }
            if (publish) ForceRefresh();
        }

        private void PublishObZones()
        {
            if (!ShowObOutOfVa) return;
            lock (obSync) { if (obSeeded) obSnap = RebuildObZones(); }
        }

        private void TrimObWindow()
        {
            if (obT.Count == 0) return;
            DateTime cutoff = obT[obT.Count - 1]
                .AddDays(-(Math.Max(1, ObVaLookbackDays) + Math.Max(1, ObVaWarmupDays)));
            int drop = 0;
            while (drop < obT.Count && obT[drop] < cutoff) drop++;
            if (drop <= 0) return;
            obT.RemoveRange(0, drop); obO.RemoveRange(0, drop); obH.RemoveRange(0, drop);
            obL.RemoveRange(0, drop); obC.RemoveRange(0, drop); obV.RemoveRange(0, drop);
        }

        // Replay the OB detector over the native window, drop pad-formed OBs, queue tick-POC profiling,
        // and publish. Caller holds obSync.
        private ObZone[] RebuildObZones(bool verbose = false)
        {
            int n = obT.Count;
            if (n < Math.Max(4, ObSwingLength * 2 + 2)) return new ObZone[0];

            var all = DetectObZones();

            DateTime emitFrom = obT[n - 1].AddDays(-Math.Max(1, ObVaLookbackDays));
            var kept = new List<ObZone>();
            foreach (var z in all)
            {
                if (z.StartTime < emitFrom) continue;                 // formed in the warm-up pad -> detect only
                if (obPocCache.TryGetValue(z.Key, out double poc)) { z.Poc = poc; z.PocReady = true; }
                else { z.Poc = z.Mid; RequestObPoc(z); }              // provisional midpoint until the tick profile lands
                kept.Add(z);
            }

            if (ObDebug && verbose)
            {
                int act = 0, brk = 0, mit = 0;
                foreach (var z in kept) { if (z.Mitigated) mit++; else if (z.Breaker) brk++; else act++; }
                Print("=== LT OB pipeline: " + all.Count + " survived detector / " + kept.Count + " in window ("
                    + act + " active, " + brk + " breaker, " + mit + " mitigated) ===");
                Print("OB|WINDOW|native=" + Math.Max(1, ObVaMinutes) + "min|bars=" + n
                    + "|from=" + obT[0].ToString("MM-dd HH:mm") + "|to=" + obT[n - 1].ToString("MM-dd HH:mm")
                    + "|emitFrom=" + emitFrom.ToString("MM-dd HH:mm"));
                Print("OB|EFFECTIVE|swingLen=" + ObSwingLength + "|atrMult=" + ObMaxAtrMult + "|wickOnly=" + ObWickOnly
                    + "|showBreakers=" + ObShowBreakers + "|showMitigated=" + ObShowMitigated
                    + "|outsideMinPct=" + ObOutsideMinPct + "|acrossChart=" + ObAcrossChart);
                foreach (var z in kept)
                    Print("OB|" + z.StartTime.ToString("MM-dd HH:mm")
                        + "|" + (z.IsBull ? "bull" : "bear") + (z.Breaker ? "|BRK" : "") + (z.Mitigated ? "|MIT" : "")
                        + "|box=" + z.Bottom.ToString("F2") + "-" + z.Top.ToString("F2")
                        + "|poc=" + z.Poc.ToString("F2") + (z.PocReady ? "" : "(prov)")
                        + "|side=" + z.Side + "|left=" + z.Left);
            }
            return kept.ToArray();
        }

        // The order-block detector: a forward replay of the swing-break logic ported from RedTailMarketStructure,
        // producing the surviving OBs with the active -> breaker -> gone lifecycle applied. Runs on the native
        // OB-timeframe bar arrays (obO/obH/obL/obC). Caller holds obSync.
        private List<ObZone> DetectObZones()
        {
            int n = obT.Count;
            int len = Math.Max(1, ObSwingLength);
            bool wick = ObWickOnly;
            double mult = Math.Max(0.1, ObMaxAtrMult);
            int p = Math.Max(1, FrvpAtrPeriod);

            // Running Wilder ATR over the native bars (matches the live _atrValue the chart detector would see).
            double atr = 0, trSum = 0;

            int swingType = -1;
            double topY = double.MinValue; int topX = -1; bool topCrossed = false;
            double botY = double.MaxValue; int botX = -1; bool botCrossed = false;

            var bull = new List<ObZone>();   // newest-first, like the MS lists
            var bear = new List<ObZone>();

            for (int i = 0; i < n; i++)
            {
                // --- running ATR ---
                double tr = (i == 0) ? (obH[i] - obL[i])
                          : Math.Max(obH[i] - obL[i], Math.Max(Math.Abs(obH[i] - obC[i - 1]), Math.Abs(obL[i] - obC[i - 1])));
                if (i < p) { trSum += tr; atr = trSum / (i + 1); }
                else atr = (atr * (p - 1) + tr) / p;

                double O = obO[i], H = obH[i], L = obL[i], C = obC[i];

                // --- FindOBSwings (needs len bars on each side) ---
                if (i >= len * 2)
                {
                    double u = double.MinValue, lo = double.MaxValue;
                    for (int k = 0; k < len; k++) { if (obH[i - k] > u) u = obH[i - k]; if (obL[i - k] < lo) lo = obL[i - k]; }
                    int prev = swingType;
                    if (obH[i - len] > u) swingType = 0; else if (obL[i - len] < lo) swingType = 1;
                    if (swingType == 0 && prev != 0) { topX = i - len; topY = obH[i - len]; topCrossed = false; }
                    if (swingType == 1 && prev != 1) { botX = i - len; botY = obL[i - len]; botCrossed = false; }
                }

                // --- bull OB lifecycle: two closes-through (either direction) = OB -> breaker -> gone.
                //     A return-tap from the committed side (price re-enters without closing through) mitigates it. ---
                for (int b = bull.Count - 1; b >= 0; b--)
                {
                    var ob = bull[b];
                    sbyte newSide = C > ob.Top ? (sbyte)1 : (C < ob.Bottom ? (sbyte)-1 : ob.Side);
                    if (newSide != ob.Side)                         // committed side reversed = a close-through
                    {
                        ob.Side = newSide; ob.Left = false;         // re-arm the return-tap for the new side
                        if (!ob.Breaker) ob.Breaker = true;         // first close-through -> breaker
                        else { bull.RemoveAt(b); }                  // second close-through -> gone
                        continue;
                    }
                    if (ob.Side > 0)   // price committed above: arm once clear above, mitigate on a return down
                    {
                        if (!ob.Left) { if (Math.Min(O, C) > ob.Top) ob.Left = true; }
                        else if ((wick ? L : Math.Min(O, C)) <= ob.Top) { if (!MitigateOb(bull, b, ob)) continue; }
                    }
                    else               // price committed below (broken): arm once clear below, mitigate on a return up
                    {
                        if (!ob.Left) { if (Math.Max(O, C) < ob.Bottom) ob.Left = true; }
                        else if ((wick ? H : Math.Max(O, C)) >= ob.Bottom) { if (!MitigateOb(bull, b, ob)) continue; }
                    }
                }

                // --- detect new bull OB ---
                if (topY != double.MinValue && C > topY && !topCrossed)
                {
                    topCrossed = true;
                    double bb = (i >= 1 ? obH[i - 1] : H), bt = (i >= 1 ? obL[i - 1] : L);
                    int lb = Math.Min(i - topX, i); if (lb < 2) lb = 2;
                    int ago = 1;
                    for (int k = 1; k < lb && k <= i; k++)
                        if (obL[i - k] < bb) { bb = obL[i - k]; bt = obH[i - k]; ago = k; }

                    int idx = i - ago;
                    double drawTop = bt, drawBottom = bb;
                    if (wick) drawTop = Math.Min(obO[idx], obC[idx]);
                    double hgt = Math.Abs(drawTop - drawBottom);
                    if (hgt > 0 && hgt <= atr * mult)
                    {
                        var z = MakeOb(idx, drawTop, drawBottom, true);
                        bull.Insert(0, z);
                        for (int b = bull.Count - 1; b > 0; b--)
                        {
                            var older = bull[b];   // any overlapping older bull box (incl. breakers) collapses into the fresh one
                            if (older.Top >= drawBottom && older.Bottom <= drawTop) bull.RemoveAt(b);
                        }
                        if (bull.Count > 30) bull.RemoveAt(bull.Count - 1);
                    }
                }

                // --- bear OB lifecycle (mirror of the bull path). ---
                for (int b = bear.Count - 1; b >= 0; b--)
                {
                    var ob = bear[b];
                    sbyte newSide = C > ob.Top ? (sbyte)1 : (C < ob.Bottom ? (sbyte)-1 : ob.Side);
                    if (newSide != ob.Side)
                    {
                        ob.Side = newSide; ob.Left = false;
                        if (!ob.Breaker) ob.Breaker = true;
                        else { bear.RemoveAt(b); }
                        continue;
                    }
                    if (ob.Side < 0)   // committed below: arm once clear below, mitigate on a return up
                    {
                        if (!ob.Left) { if (Math.Max(O, C) < ob.Bottom) ob.Left = true; }
                        else if ((wick ? H : Math.Max(O, C)) >= ob.Bottom) { if (!MitigateOb(bear, b, ob)) continue; }
                    }
                    else               // committed above (broken): arm once clear above, mitigate on a return down
                    {
                        if (!ob.Left) { if (Math.Min(O, C) > ob.Top) ob.Left = true; }
                        else if ((wick ? L : Math.Min(O, C)) <= ob.Top) { if (!MitigateOb(bear, b, ob)) continue; }
                    }
                }

                // --- detect new bear OB ---
                if (botY != double.MaxValue && C < botY && !botCrossed)
                {
                    botCrossed = true;
                    double bt = (i >= 1 ? obL[i - 1] : L), bb = (i >= 1 ? obH[i - 1] : H);
                    int lb = Math.Min(i - botX, i); if (lb < 2) lb = 2;
                    int ago = 1;
                    for (int k = 1; k < lb && k <= i; k++)
                        if (obH[i - k] > bt) { bt = obH[i - k]; bb = obL[i - k]; ago = k; }

                    int idx = i - ago;
                    double drawTop = bt, drawBottom = bb;
                    if (wick) drawBottom = Math.Max(obO[idx], obC[idx]);
                    double hgt = Math.Abs(drawTop - drawBottom);
                    if (hgt > 0 && hgt <= atr * mult)
                    {
                        var z = MakeOb(idx, drawTop, drawBottom, false);
                        bear.Insert(0, z);
                        for (int b = bear.Count - 1; b > 0; b--)
                        {
                            var older = bear[b];   // any overlapping older bear box (incl. breakers) collapses into the fresh one
                            if (older.Top >= drawBottom && older.Bottom <= drawTop) bear.RemoveAt(b);
                        }
                        if (bear.Count > 30) bear.RemoveAt(bear.Count - 1);
                    }
                }
            }

            var outList = new List<ObZone>(bull.Count + bear.Count);
            outList.AddRange(bull); outList.AddRange(bear);
            return outList;
        }

        // Build an ObZone from the OB candle at native index idx. Caller holds obSync.
        private ObZone MakeOb(int idx, double drawTop, double drawBottom, bool isBull)
        {
            double top = Math.Max(drawTop, drawBottom), bot = Math.Min(drawTop, drawBottom);
            return new ObZone
            {
                Key = obT[idx].Ticks,
                StartTime = obT[idx],
                EndTime = (idx + 1 < obT.Count) ? obT[idx + 1] : obT[idx].AddMinutes(Math.Max(1, ObVaMinutes)),
                Top = top, Bottom = bot,
                CandleHigh = obH[idx], CandleLow = obL[idx],
                Mid = (top + bot) / 2.0,
                Poc = (top + bot) / 2.0,   // provisional until the tick profile resolves
                IsBull = isBull, Breaker = false, Bars = 1, Volume = obV[idx],
                Side = (sbyte)(isBull ? 1 : -1)   // OB forms with price committed to the breakout side
            };
        }

        // Price returned and tapped an OB (used it). Off = remove (declutter); on = keep it faded.
        // Returns false when the OB was removed, so the caller skips the rest of this iteration.
        private bool MitigateOb(List<ObZone> list, int idx, ObZone ob)
        {
            if (!ObShowMitigated) { list.RemoveAt(idx); return false; }
            ob.Mitigated = true; return true;
        }

        // Fire a bounded tick BarsRequest over just this OB candle's window, build a volume-at-price
        // histogram, and cache the POC. Only fires once per unique candle. Callback runs off-thread.
        private void RequestObPoc(ObZone z)
        {
            if (obPocCache.ContainsKey(z.Key)) return;
            if (!obPocPending.TryAdd(z.Key, 0)) return;   // already in flight
            try
            {
                long key = z.Key;
                double lo = z.CandleLow, hi = z.CandleHigh;
                // Bar timestamps are close-times, so the candle spans (close - period, close]. Bracket it
                // with a minute of margin on each side and let the price-range filter in TickPoc discard
                // anything from an adjacent bar - the POC can only be a price the candle actually traded.
                int per = Math.Max(1, ObVaMinutes);
                DateTime from = z.StartTime.AddMinutes(-(per + 1));
                DateTime to = z.StartTime.AddMinutes(1);
                var req = new BarsRequest(Instrument, from, to)
                {
                    BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Tick, Value = 1 },
                    TradingHours = (ObVaUseChartSession && Bars != null && Bars.TradingHours != null)
                                   ? Bars.TradingHours : Instrument.MasterInstrument.TradingHours
                };
                lock (obSync) obPocRequests.Add(req);
                req.Request((request, errorCode, errorMessage) =>
                {
                    try
                    {
                        double poc = double.NaN;
                        if (errorCode == ErrorCode.NoError && request != null && request.Bars != null && request.Bars.Count > 0)
                            poc = TickPoc(request.Bars, lo, hi);
                        if (!double.IsNaN(poc))
                        {
                            obPocCache[key] = poc;
                            var snap = obSnap;                       // patch the live snapshot so it shows without waiting for a rebuild
                            for (int i = 0; i < snap.Length; i++)
                                if (snap[i].Key == key) { snap[i].Poc = poc; snap[i].PocReady = true; }
                            ForceRefresh();
                        }
                    }
                    catch { }
                    finally { obPocPending.TryRemove(key, out _); }
                });
            }
            catch { obPocPending.TryRemove(z.Key, out _); }
        }

        // Volume POC of a tick series, restricted to the candle's own [lo, hi] range so a stray tick from
        // an adjacent bar in the bracketed window can't drag the POC outside the candle. Returns NaN if the
        // window contained no in-range prints (the caller then keeps the midpoint).
        private double TickPoc(Bars b, double lo, double hi)
        {
            if (hi < lo) { double t = lo; lo = hi; hi = t; }
            var vol = new Dictionary<long, double>();
            double ts = TickSize <= 0 ? 0.01 : TickSize;
            int n = b.Count;
            double bestPrice = double.NaN, bestVol = -1;
            for (int i = 0; i < n; i++)
            {
                double price = b.GetClose(i);
                if (price < lo - ts || price > hi + ts) continue;   // outside the OB candle -> not its volume
                long bucket = (long)Math.Round(price / ts);
                double add = b.GetVolume(i);
                double cur = vol.TryGetValue(bucket, out double e) ? e + add : add;
                vol[bucket] = cur;
                if (cur > bestVol) { bestVol = cur; bestPrice = bucket * ts; }
            }
            return bestPrice;
        }

        // Detect FRVP consolidation zones on the NATIVE FrvpVaMinutes bars (identical to AutoFRVP's chart),
        // using AutoFRVP's exact box logic + ATR height gate. Each box is profiled and graded like AutoFRVP.
        // The in/out-of-value filter is applied later at render against the developing session VA.
        // Caller holds fovSync. Called from the request callback AND from every live native bar close.
        private FrvpZone[] RebuildFovZones(bool verbose = false)
        {
            int n = fovT.Count;
            int ap = Math.Max(1, FrvpAtrPeriod);
            if (n < Math.Max(4, ap + 2)) return new FrvpZone[0];

            var bt = fovT.ToArray(); var bh = fovH.ToArray(); var bl = fovL.ToArray();
            var bc = fovC.ToArray(); var bv = fovV.ToArray();

            // Wilder ATR on the native bars.
            var atr = new double[n];
            double prevAtr = 0, prevClose = bc[0];
            for (int i = 1; i < n; i++)
            {
                double tr = Math.Max(bh[i] - bl[i], Math.Max(Math.Abs(bh[i] - prevClose), Math.Abs(bl[i] - prevClose)));
                if (i <= ap) prevAtr = ((prevAtr * (i - 1)) + tr) / i;   // running mean while warming up
                else prevAtr = (prevAtr * (ap - 1) + tr) / ap;           // Wilder smoothing
                atr[i] = prevAtr; prevClose = bc[i];
            }

            // Box detector - AutoFRVP's exact logic (breakout tested off the accumulated box; box ends on
            // the previous bar; new box starts on the breakout bar). Detection begins once ATR has warmed.
            double buffer = FrvpBreakoutBufferTicks * TickSize;
            int minBars = Math.Max(1, FrvpMinBars);
            var boxes = new List<int[]>();
            int fbStart = -1; double fbHi = 0, fbLo = 0, fbCHi = 0, fbCLo = 0; int fbBars = 0;
            for (int i = ap; i < n; i++)
            {
                if (fbStart < 0)
                {
                    fbStart = i; fbHi = bh[i]; fbLo = bl[i]; fbCHi = bc[i]; fbCLo = bc[i]; fbBars = 1;
                    continue;
                }
                double nH = Math.Max(fbHi, bh[i]);
                double nL = Math.Min(fbLo, bl[i]);
                double cHi = Math.Max(fbCHi, bc[i]);
                double cLo = Math.Min(fbCLo, bc[i]);
                double a = atr[i] > 0 ? atr[i] : 10 * TickSize;
                // Box-height gate. This MUST mirror CurrentFrvpThreshold(), including the FixedTicks
                // branch - the old code always did `a * FrvpVaAtrMult` and so silently ignored
                // FrvpHeightMode entirely. On a FixedTicks setup the out-of-value detector was running a
                // completely different gate from the chart-timeframe one, which is why some zones lined
                // up and others existed only here. The ATR itself is native-bar ATR (correct: the chart's
                // frvpAtr is measured on the primary tick/range series and is not comparable).
                double th = FrvpHeightMode == FrvpHeightModeEnum.FixedTicks
                    ? FrvpMaxHeightTicks * TickSize
                    : a * Math.Max(0.1, FrvpVaAtrMult);
                double sizeHi = FrvpUseCloseBand ? cHi : nH;
                double sizeLo = FrvpUseCloseBand ? cLo : nL;
                bool tooTall = (sizeHi - sizeLo) > th;
                bool brokeUp = bc[i] > fbHi + buffer;
                bool brokeDn = bc[i] < fbLo - buffer;
                bool tooLong = FrvpMaxBars > 0 && fbBars >= FrvpMaxBars;
                if (tooTall || brokeUp || brokeDn || tooLong)
                {
                    int dir = brokeUp ? 1 : brokeDn ? -1 : (bc[i] >= (fbHi + fbLo) * 0.5 ? 1 : -1);
                    if (fbBars >= minBars) boxes.Add(new int[] { fbStart, i - 1, dir });
                    fbStart = i; fbHi = bh[i]; fbLo = bl[i]; fbCHi = bc[i]; fbCLo = bc[i]; fbBars = 1;
                }
                else { fbHi = nH; fbLo = nL; fbCHi = cHi; fbCLo = cLo; fbBars++; }
            }

            var outList = new List<FrvpZone>();
            // Zones whose box STARTS inside the warm-up pad were graded against a cold ATR - or clipped by
            // the detector's `i = ap` start - so they are diagnostic only. Detect them (they matter for the
            // box chain), never publish them.
            DateTime emitFrom = bt[n - 1].AddDays(-Math.Max(1, FrvpVaLookbackDays));

            for (int z = 0; z < boxes.Count; z++)
            {
                int s = boxes[z][0], e = boxes[z][1], dir = boxes[z][2];
                if (bt[s] < emitFrom) continue;
                double poc, vah, val, tot, sHi, sLo;
                if (!ComputeFovProfile(bh, bl, bv, s, e, out poc, out vah, out val, out tot, out sHi, out sLo)) continue;
                if (vah <= val) continue;
                var zone = new FrvpZone
                {
                    Key = bt[s].Ticks, StartTime = bt[s], EndTime = bt[e],
                    StartBarIdx = -1, EndBarIdx = -1,
                    POC = poc, VAH = vah, VAL = val, SrcHigh = sHi, SrcLow = sLo,
                    Bars = e - s + 1, Dir = dir, RefEdge = dir > 0 ? sHi : sLo,
                    Height = Math.Max(TickSize, vah - val), Volume = tot, State = FrvpZoneStateEnum.Fresh
                };
                if (ApplyFovMitigation(zone, bh, bl, bc, e + 1, n)) continue;   // retired -> drop
                outList.Add(zone);
            }

            // ReplaceOverlapping: keep the most recent of any heavily overlapping zones.
            bool[] drop = new bool[outList.Count];
            for (int i = 0; i < outList.Count; i++)
            {
                if (drop[i]) continue;
                for (int j = i + 1; j < outList.Count; j++)
                {
                    if (drop[j]) continue;
                    double ov = Math.Min(outList[i].VAH, outList[j].VAH) - Math.Max(outList[i].VAL, outList[j].VAL);
                    if (ov <= 0) continue;
                    double minH = Math.Min(outList[i].VAH - outList[i].VAL, outList[j].VAH - outList[j].VAL);
                    if (minH > 0 && ov / minH >= FrvpOverlapThreshold) { drop[i] = true; break; }   // i is older
                }
            }
            var kept = new List<FrvpZone>();
            for (int i = 0; i < outList.Count; i++) if (!drop[i]) kept.Add(outList[i]);

            // Parity with the chart-timeframe zones. FinalizeFrvpZone caps frvpZones at
            // Math.Max(4, FrvpMaxZones) + 4 - it deliberately keeps a few beyond the nominal max - so
            // mirror that EXACT cap. Capping at FrvpMaxZones alone left the FOV list four zones shorter
            // than the chart and dropped the oldest zones the comparison chart still displays.
            int fovCap = Math.Max(4, FrvpMaxZones) + 4;
            if (kept.Count > fovCap)
                kept.RemoveRange(0, kept.Count - fovCap);   // keep the most recent

            return kept.ToArray();
        }

        // FRVP profile over native bars [startIdx, endIdx] - distribute each bar's volume across its H/L
        // range, then expand the value area to FrvpValueAreaPct, same convention as ComputeFrvpProfile.
        private bool ComputeFovProfile(double[] bh, double[] bl, double[] bv, int startIdx, int endIdx,
                                       out double poc, out double vah, out double val, out double totalVol, out double srcHi, out double srcLo)
        {
            poc = vah = val = totalVol = 0; srcHi = srcLo = 0;
            double hi = double.MinValue, lo = double.MaxValue;
            for (int i = startIdx; i <= endIdx; i++)
            {
                if (bh[i] > hi) hi = bh[i];
                if (bl[i] < lo) lo = bl[i];
            }
            if (hi <= lo) return false;
            srcHi = hi; srcLo = lo;

            int rows = Math.Max(2, FrvpProfileRows);
            double interval = (hi - lo) / (rows - 1);
            if (interval <= 0) return false;

            double[] vol = new double[rows];
            for (int i = startIdx; i <= endIdx; i++)
            {
                int mn = ClampInt((int)Math.Floor((bl[i] - lo) / interval), 0, rows - 1);
                int mx = ClampInt((int)Math.Ceiling((bh[i] - lo) / interval), 0, rows - 1);
                int touched = mx - mn + 1;
                if (touched <= 0) continue;
                double per = bv[i] / touched;
                for (int j = mn; j <= mx; j++) vol[j] += per;
            }

            int pocI = 0; double maxV = 0, total = 0;
            for (int i = 0; i < rows; i++) { total += vol[i]; if (vol[i] > maxV) { maxV = vol[i]; pocI = i; } }
            if (maxV <= 0) return false;

            double target = total * FrvpValueAreaPct / 100.0;
            int up = pocI, dn = pocI; double sum = vol[pocI];
            while (sum < target)
            {
                double vUp = (up < rows - 1) ? vol[up + 1] : 0;
                double vDn = (dn > 0) ? vol[dn - 1] : 0;
                if (vUp == 0 && vDn == 0) break;
                if (vUp >= vDn) { sum += vUp; up++; } else { sum += vDn; dn--; }
            }

            poc = lo + (pocI + 0.5) * interval;
            vah = lo + (up + 1) * interval;
            val = lo + dn * interval;
            totalVol = total;
            return true;
        }

        // Grades an out-of-value zone by replaying the native bars [fromIdx, n) that came AFTER it formed,
        // through the EXACT rules UpdateFrvpMitigation + RetireFrvpZones apply to the chart-timeframe FRVP
        // zones. The two must not drift:
        //   - a close through the FAR edge only FLIPS polarity and re-arms the zone as Tested; it does not
        //     mitigate. Flip-churn is handled by retirement.
        //   - a zone must first clear its band (HasLeft) before returns count; a touch back in makes it
        //     Tested and increments Touches.
        //   - a zone is NEVER mitigated by a close-through. The pure count model: it stays live until it
        //     retires on flip or touch counts.
        // Returns true if the zone should be retired (dropped entirely).
        private bool ApplyFovMitigation(FrvpZone z, double[] bh, double[] bl, double[] bc, int fromIdx, int n)
        {
            int start = Math.Max(0, fromIdx);

            // Departure / Strong. The chart measures this by calling UpdateFrvpDepartures BEFORE
            // UpdateFrvpMitigation every bar, so departure uses the zone's CURRENT Dir and a flip can change
            // that Dir mid-window. RefEdge is frozen at creation and is NOT rewritten on flip. The net effect:
            // if a zone flips inside its departure window, the remaining bars measure excursion from the stale
            // (now wrong-side) edge and the value balloons. That is a chart quirk, but parity demands we
            // reproduce it - so departure is measured at the TOP of the same loop that applies the flips,
            // NOT in a separate up-front pass (which used the original Dir throughout and read low).
            // Window = exc0 bar + DepartureBars subsequent = DepartureBars + 1 bars, matching the chart's seed.
            int depBars = Math.Max(1, FrvpDepartureBars);
            int depLeft = depBars + 1;
            double maxExc = 0;
            double buffer = FrvpBreakoutBufferTicks * TickSize;

            for (int i = start; i < n; i++)
            {
                double H = bh[i], L = bl[i], C = bc[i];

                if (depLeft > 0)
                {
                    double exc = z.Dir > 0 ? (H - z.RefEdge) : (z.RefEdge - L);
                    if (exc > maxExc) maxExc = exc;
                    depLeft--;
                }

                if (!FrvpEnableMitigation)
                {
                    if (depLeft == 0) break;   // mitigation off -> nothing to do once departure is measured
                    continue;
                }

                if (FrvpFlipOnBreakthrough && z.HasLeft)
                {
                    bool brokeThrough = z.Dir > 0 ? C < z.VAL - buffer : C > z.VAH + buffer;
                    if (brokeThrough)
                    {
                        z.Dir = -z.Dir; z.State = FrvpZoneStateEnum.Tested; z.HasLeft = false; z.Inside = false; z.Flips++;
                        continue;
                    }
                }

                if (z.State == FrvpZoneStateEnum.Mitigated) continue;

                if (!z.HasLeft)
                {
                    bool away = z.Dir > 0 ? L > z.VAH : H < z.VAL;
                    if (away) z.HasLeft = true;
                    continue;
                }

                bool touched = H >= z.VAL && L <= z.VAH;
                if (touched && !z.Inside) z.Touches++;
                if (touched) z.LastTouchBar = i;

                // Left the band without a close-through -> a completed hold. Same push-off sample,
                // normalised by the zone's own VA height, as the live path.
                if (!touched && z.Inside && ScoreReactions)
                {
                    double rej = z.Dir > 0 ? (C - z.VAH) : (z.VAL - C);
                    if (rej > 0 && z.Height > 0)
                    {
                        double rr = rej / z.Height;
                        if (rr > z.RejectMax) z.RejectMax = rr;
                    }
                    z.Score = ReactionScore(z.Touches, z.RejectMax / 2.0);
                }
                z.Inside = touched;

                if (z.State == FrvpZoneStateEnum.Fresh && touched) z.State = FrvpZoneStateEnum.Tested;

                // NO close-through mitigation. The chart-timeframe zones removed it (pure count model);
                // the old AutoFRVP "close back through the POC -> Mitigated" rule lived here and was the
                // sole reason FOV zones aged differently from their chart-timeframe counterparts.
            }

            z.MaxExcursion = maxExc;
            z.Departure = z.Height > 0 ? maxExc / z.Height : 0;
            z.Strong = z.Departure >= FrvpMinDeparture;

            // Retirement (identical rule to RetireFrvpZones): flip-churn or retest-erosion -> hard-remove.
            if (FrvpVaMaxFlips   > 0 && z.Flips   >= FrvpVaMaxFlips)   return true;
            if (FrvpVaMaxTouches > 0 && z.Touches >= FrvpVaMaxTouches) return true;
            return false;
        }

        // Merge buffered chart-series bars the backfill didn't cover (list is chronological). Caller
        // holds htfSync.
        private void DrainHtfPending()
        {
            for (int i = 0; i < htfPendingBars.Count; i++)
                if (htfPendingBars[i].T > htfSeamTime)
                    ProcessHtfBar(htfPendingBars[i].T, htfPendingBars[i].H, htfPendingBars[i].L, htfPendingBars[i].V);
            htfPendingBars.Clear();
        }

        // Rebuild the render-facing HTF snapshots (developing weekly/monthly profile + POC/VA + the
        // four alert band lists) from the live bins. Runs on each secondary bar close and once when
        // the backfill merges - NEVER from OnRender, so render only reads published references and
        // the old per-frame CaptureHtfBins/array allocation is gone. Caller holds htfSync.
        private void RefreshHtfSnapshots()
        {
            bool wantWeek = ShowWeeklyPoc || ShowWeeklyVA || ShowWeeklyProfile || WarnWeeklyHvn || WarnWeeklyLvn;
            bool wantMonth = ShowMonthlyPoc || ShowMonthlyProfile || WarnMonthlyHvn || WarnMonthlyLvn;

            // Completed-period lists mutate on week/month rollovers; render iterates the snapshots.
            priorWeeksSnap = priorWeeks.ToArray();
            priorMonthsSnap = priorMonths.ToArray();

            if (wantWeek)
            {
                if (weekBins.Count > 0)
                {
                    var hp = new HtfProfile { Start = curWeekStartTime, End = DateTime.MinValue, Period = curWeekStart };
                    CaptureHtfBins(weekBins, hp);
                    if (ComputeHtfProfile(weekBins, out hp.Poc, out hp.Vah, out hp.Val))
                    { devWeekPoc = hp.Poc; devWeekVah = hp.Vah; devWeekVal = hp.Val; }
                    else
                    { devWeekPoc = devWeekVah = devWeekVal = double.NaN; }
                    devWeekSnap = hp;

                    if (hp.RowVol != null && hp.Peak > 0)
                    {
                        double low = hp.MinRow * hp.RowSize;
                        var hv = new List<HvnBand>(); BuildHvnBands(hp.RowVol, hp.RowVol.Length, low, hp.RowSize, hp.Peak, hv); weekHvnSnap = hv;
                        var lv = new List<HvnBand>(); BuildLvnBands(hp.RowVol, hp.RowVol.Length, low, hp.RowSize, hp.Peak, lv); weekLvnSnap = lv;
                    }
                    else { weekHvnSnap = new List<HvnBand>(); weekLvnSnap = new List<HvnBand>(); }
                }
                else
                {
                    devWeekSnap = null;
                    devWeekPoc = devWeekVah = devWeekVal = double.NaN;
                    weekHvnSnap = new List<HvnBand>(); weekLvnSnap = new List<HvnBand>();
                }
            }

            if (wantMonth)
            {
                if (monthBins.Count > 0)
                {
                    var hp = new HtfProfile { Start = curMonthStartTime, End = DateTime.MinValue, Period = curMonthStart };
                    CaptureHtfBins(monthBins, hp);
                    if (ComputeHtfProfile(monthBins, out hp.Poc, out hp.Vah, out hp.Val))
                    { devMonthPoc = hp.Poc; devMonthVah = hp.Vah; devMonthVal = hp.Val; }
                    else
                    { devMonthPoc = devMonthVah = devMonthVal = double.NaN; }
                    devMonthSnap = hp;

                    if (hp.RowVol != null && hp.Peak > 0)
                    {
                        double low = hp.MinRow * hp.RowSize;
                        var hv = new List<HvnBand>(); BuildHvnBands(hp.RowVol, hp.RowVol.Length, low, hp.RowSize, hp.Peak, hv); monthHvnSnap = hv;
                        var lv = new List<HvnBand>(); BuildLvnBands(hp.RowVol, hp.RowVol.Length, low, hp.RowSize, hp.Peak, lv); monthLvnSnap = lv;
                    }
                    else { monthHvnSnap = new List<HvnBand>(); monthLvnSnap = new List<HvnBand>(); }
                }
                else
                {
                    devMonthSnap = null;
                    devMonthPoc = devMonthVah = devMonthVal = double.NaN;
                    monthHvnSnap = new List<HvnBand>(); monthLvnSnap = new List<HvnBand>();
                }
            }
        }

        private void DistributeToBins(Dictionary<int, double> bins, double bh, double bl, double bv)
        {
            if (profRowSize <= 0) return;
            double inv = 1.0 / profRowSize;
            int startRow = (int)Math.Floor(bl * inv);
            int endRow = (int)Math.Floor(bh * inv);
            double range = bh - bl;

            if (range <= 0)
            {
                bins.TryGetValue(startRow, out double v0);
                bins[startRow] = v0 + bv;
                return;
            }
            double invRange = 1.0 / range;
            for (int row = startRow; row <= endRow; row++)
            {
                double levelLow = row * profRowSize;
                double ovHigh = Math.Min(bh, levelLow + profRowSize);
                double ovLow = Math.Max(bl, levelLow);
                double ov = ovHigh - ovLow;
                if (ov > 0)
                {
                    bins.TryGetValue(row, out double v);
                    bins[row] = v + bv * (ov * invRange);
                }
            }
        }

        private void SnapshotPriorSession()
        {
            if (!BuildProfileArrays()) return;        // computes curPoc/curVah/curVal from current bins

            var sva = new SessionVA { Vah = curVah, Val = curVal, Poc = curPoc, High = curSessHigh, Low = curSessLow, Day = curSessionDay };

            // Capture the VAL..VAH slice of the just-closed profile for the ghost silhouette.
            if (profRowCount > 0 && profRowSize > 0 && curVah > curVal)
            {
                int loRow = (int)Math.Floor((curVal - profLow) / profRowSize);
                int hiRow = (int)Math.Floor((curVah - profLow) / profRowSize);
                if (loRow < 0) loRow = 0;
                if (hiRow > profRowCount - 1) hiRow = profRowCount - 1;
                if (hiRow >= loRow)
                {
                    int n = hiRow - loRow + 1;
                    var bins = new double[n];
                    double peak = 0;
                    for (int r = 0; r < n; r++)
                    {
                        double v = profRowVol[loRow + r];
                        bins[r] = v;
                        if (v > peak) peak = v;
                    }
                    sva.Bins = bins;
                    sva.BinLow = profLow + loRow * profRowSize;
                    sva.BinSize = profRowSize;
                    sva.BinPeak = peak;
                }
            }

            priorVA.Add(sva);
            // Retain enough for whichever needs more: the prior-line references, or the ghost lookback
            // (which may reach back many sessions to find one whose value area contains price).
            int keep = Math.Max(Math.Max(0, PriorSessionsToShow) + 2, Math.Max(1, GhostLookback) + 1);
            while (priorVA.Count > keep)
                priorVA.RemoveAt(0);

            priorVASnap = priorVA.ToArray();   // publish for render (ghost + prior-session levels)
        }

        // Monday-anchored week start; for CME the Sunday-evening session already rolls to Monday's
        // trading day, so all trading days of a week map to the same Monday.
        private static DateTime WeekStartOf(DateTime tradingDay)
        {
            int off = ((int)tradingDay.DayOfWeek + 6) % 7;   // Mon->0 ... Sun->6
            return tradingDay.Date.AddDays(-off);
        }

        private static DateTime MonthStartOf(DateTime tradingDay)
        {
            return new DateTime(tradingDay.Year, tradingDay.Month, 1);
        }

        // POC + value area (VAH/VAL) for an HTF bin set. Builds a contiguous array over the populated
        // row span, finds the POC, then expands to ValueAreaPercent the same way the session profile does.
        private bool ComputeHtfProfile(Dictionary<int, double> bins, out double poc, out double vah, out double val)
        {
            poc = vah = val = double.NaN;
            if (bins.Count == 0 || profRowSize <= 0) return false;

            int minRow = int.MaxValue, maxRow = int.MinValue;
            foreach (var k in bins.Keys) { if (k < minRow) minRow = k; if (k > maxRow) maxRow = k; }
            int count = maxRow - minRow + 1;
            if (count < 1 || count > 200000) return false;

            var rv = new double[count];
            double total = 0, pocVol = 0; int pocRow = 0;
            for (int r = 0; r < count; r++)
            {
                bins.TryGetValue(minRow + r, out double v);
                rv[r] = v; total += v;
                if (v > pocVol) { pocVol = v; pocRow = r; }
            }
            if (pocVol <= 0) return false;

            double low = minRow * profRowSize;
            poc = low + (pocRow + 0.5) * profRowSize;

            double target = total * Math.Max(0.1, Math.Min(0.95, ValueAreaPercent / 100.0));
            int lo = pocRow, hi = pocRow; double acc = rv[pocRow];
            while (acc < target && (lo > 0 || hi < count - 1))
            {
                double below = lo > 0 ? rv[lo - 1] : -1;
                double above = hi < count - 1 ? rv[hi + 1] : -1;
                if (above >= below) { if (hi < count - 1) { hi++; acc += rv[hi]; } else if (lo > 0) { lo--; acc += rv[lo]; } else break; }
                else { if (lo > 0) { lo--; acc += rv[lo]; } else if (hi < count - 1) { hi++; acc += rv[hi]; } else break; }
            }
            val = low + lo * profRowSize;
            vah = low + (hi + 1) * profRowSize;
            return true;
        }

        // Freeze a completed period's POC + value area as a static reference (most-recent first).
        private void SnapshotPriorHtf(Dictionary<int, double> bins, List<HtfProfile> store, DateTime period, int show, bool captureBins, DateTime start, DateTime end)
        {
            if (!ComputeHtfProfile(bins, out double poc, out double vah, out double val)) return;
            var hp = new HtfProfile { Poc = poc, Vah = vah, Val = val, Period = period, Start = start, End = end };
            if (captureBins) CaptureHtfBins(bins, hp);
            store.Insert(0, hp);
            int keep = Math.Max(1, show) + 1;
            while (store.Count > keep) store.RemoveAt(store.Count - 1);
        }

        // Compact a bin dictionary into a contiguous MinRow..MaxRow array + peak, for silhouette rendering.
        private void CaptureHtfBins(Dictionary<int, double> bins, HtfProfile hp)
        {
            if (bins.Count == 0 || profRowSize <= 0) return;
            int minRow = int.MaxValue, maxRow = int.MinValue;
            foreach (var k in bins.Keys) { if (k < minRow) minRow = k; if (k > maxRow) maxRow = k; }
            int count = maxRow - minRow + 1;
            if (count < 1 || count > 200000) return;

            var rv = new double[count];
            double peak = 0;
            for (int r = 0; r < count; r++)
            {
                bins.TryGetValue(minRow + r, out double v);
                rv[r] = v;
                if (v > peak) peak = v;
            }
            hp.RowVol = rv; hp.MinRow = minRow; hp.RowSize = profRowSize; hp.Peak = peak;
        }

        // Bar-anchored weekly/monthly profile silhouettes: each period drawn over its own time span,
        // spine at the period start, volume growing right (NT weekly-profile style). The developing period
        // uses its live bins and extends to the live edge; prior periods use their captured bins.
        private void DrawHtfProfiles(ChartScale chartScale, float rightEdgeX)
        {
            if (renderCC == null) return;

            if (ShowWeeklyProfile)
            {
                var dev = devWeekSnap;   // built on secondary bar close - no per-frame bin capture
                if (dev != null)
                    DrawHtfSil(chartScale, dev, WeeklyProfileColor, WeeklyProfileOpacity, true, rightEdgeX, "w");
                var pw = priorWeeksSnap;
                int n = Math.Min(PriorWeeksToShow, pw.Length);
                for (int i = 0; i < n; i++)
                    DrawHtfSil(chartScale, pw[i], WeeklyProfileColor, WeeklyProfileOpacity, false, rightEdgeX, "w");
            }

            if (ShowMonthlyProfile)
            {
                var dev = devMonthSnap;
                if (dev != null)
                    DrawHtfSil(chartScale, dev, MonthlyProfileColor, MonthlyProfileOpacity, true, rightEdgeX, "m");
                var pm = priorMonthsSnap;
                int n = Math.Min(PriorMonthsToShow, pm.Length);
                for (int i = 0; i < n; i++)
                    DrawHtfSil(chartScale, pm[i], MonthlyProfileColor, MonthlyProfileOpacity, false, rightEdgeX, "m");
            }
        }

        private void DrawHtfSil(ChartScale chartScale, HtfProfile hp, Brush wpf, int opacity, bool developing, float rightEdgeX, string tag)
        {
            if (hp == null || hp.RowVol == null || hp.RowVol.Length == 0 || hp.Peak <= 0 || hp.RowSize <= 0) return;
            if (hp.Start == DateTime.MinValue) return;

            float xStart = renderCC.GetXByTime(hp.Start);
            float xEnd = developing ? rightEdgeX : (hp.End != DateTime.MinValue ? renderCC.GetXByTime(hp.End) : rightEdgeX);
            if (xEnd <= xStart) return;

            float fullW = (xEnd - xStart) * (float)HtfProfileWidthFrac;
            if (fullW < 4f) return;

            // Mirror the session "wall" LOOK (smooth terrain silhouette / stepped slabs), but anchored
            // on the LEFT at the period start (xStart) and bulging RIGHT proportional to per-row volume.
            // Keeps the HTF family's own colour, opacity, and per-period width basis (fullW).
            double invPeak = 1.0 / hp.Peak;
            int len = hp.RowVol.Length;

            try
            {
                var dx = AcquireBrush(wpf ?? Brushes.SteelBlue, Clamp01(opacity));

                if (WallStyle == WallStyleEnum.Stepped)
                {
                    for (int r = 0; r < len; r++)
                    {
                        double v = hp.RowVol[r];
                        if (v <= 0) continue;
                        float depth = (float)(v * invPeak * fullW);
                        if (depth < 1f) continue;
                        double rowLow = (hp.MinRow + r) * hp.RowSize;
                        float yTop = chartScale.GetYByValue(rowLow + hp.RowSize);
                        float yBot = chartScale.GetYByValue(rowLow);
                        float h = yBot - yTop;
                        if (h < 1f) h = 1f;
                        RenderTarget.FillRectangle(new SharpDX.RectangleF(xStart, yTop, depth, h), dx);   // spine left, bulges right
                    }
                }
                else
                {
                    // Smooth terrain silhouette: left spine at xStart, right boundary bulging out by depth
                    // (horizontal mirror of FillSmoothWall).
                    double topPrice = (hp.MinRow + len) * hp.RowSize;
                    double botPrice = hp.MinRow * hp.RowSize;
                    float yTop = chartScale.GetYByValue(topPrice);
                    float yBot = chartScale.GetYByValue(botPrice);

                    SharpDX.Direct2D1.PathGeometry geo = null;
                    SharpDX.Direct2D1.GeometrySink sink = null;
                    try
                    {
                        geo = new SharpDX.Direct2D1.PathGeometry(RenderTarget.Factory);
                        sink = geo.Open();
                        sink.BeginFigure(new SharpDX.Vector2(xStart, yTop), SharpDX.Direct2D1.FigureBegin.Filled);
                        sink.AddLine(new SharpDX.Vector2(xStart, yBot));
                        for (int r = 0; r < len; r++)
                        {
                            double v = hp.RowVol[r];
                            if (v < 0) v = 0;
                            float depth = (float)(v * invPeak * fullW);
                            float rx = xStart + depth;
                            float y = chartScale.GetYByValue((hp.MinRow + r + 0.5) * hp.RowSize);
                            sink.AddLine(new SharpDX.Vector2(rx, y));
                        }
                        sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
                        sink.Close();
                        RenderTarget.FillGeometry(geo, dx);
                    }
                    finally
                    {
                        sink?.Dispose();
                        geo?.Dispose();
                    }
                }
            }
            catch { }

            // Each period's VAH/VAL/POC, spanning that period (developing extends to the live edge),
            // labelled at the period's right edge so stacked periods don't overlap.
            if (HtfProfileLevels && hp.Poc > 0 && hp.Vah > hp.Val)
            {
                int op = HtfProfileLevelOpacity;
                DrawRailLine(wpf, DashStyleHelper.Dash, 1, op, xStart, xEnd, chartScale.GetYByValue(hp.Vah));
                DrawRailLine(wpf, DashStyleHelper.Dash, 1, op, xStart, xEnd, chartScale.GetYByValue(hp.Val));
                DrawRailLine(wpf, DashStyleHelper.Solid, 1, op, xStart, xEnd, chartScale.GetYByValue(hp.Poc));
                if (ShowLabels)
                {
                    // Exact rows now - the old -7f nudge was an ad-hoc de-collision hack that the
                    // label layer makes unnecessary (and that quietly lied about where the level was).
                    QueueGutterLabel(hp.Poc, chartScale.GetYByValue(hp.Poc), xEnd + 3f, tag + "POC", wpf, op, LabelRankEnum.Htf, false);
                    QueueGutterLabel(hp.Vah, chartScale.GetYByValue(hp.Vah), xEnd + 3f, tag + "VAH", wpf, op, LabelRankEnum.Htf, false);
                    QueueGutterLabel(hp.Val, chartScale.GetYByValue(hp.Val), xEnd + 3f, tag + "VAL", wpf, op, LabelRankEnum.Htf, false);
                }
            }
        }

        #region Profile + detection

        private void Recompute()
        {
            hasProfile = false;
            bool profileOk = BuildProfileArrays();   // session structure (wall/POC/VA/HVN)
            BuildScan();                              // rolling scan (LVN discovery)

            if (profileOk)
            {
                DetectHvn();
                ComputeGradientScores();          // Wall Gradient heat map rides the same rebuild
                RebuildCvdPolarityMap();           // CVD-sourced wall/strip polarity (no-op unless selected)
            }
            else
            {
                hvnBands = new List<HvnBand>();   // publish an empty list; render may hold the old one
                profGradScore = new float[0];
                cvdRowPol = new double[0];
            }

            DetectLvn();                              // region-based, on smoothed scan
            if (PersistLevels)
                UpdateTrackedLvns();

            // Weekly/monthly POC + VA are owned by RefreshHtfSnapshots (secondary bar close /
            // backfill merge, under htfSync) - Recompute no longer touches the HTF bins.

            // Publish render-facing snapshots of the level lists: OnRender iterates these arrays,
            // never the live lists the data thread mutates.
            trackedSnap = tracked.ToArray();
            detectedSnap = detected.ToArray();

            hasProfile = profileOk;
        }

        // Build contiguous arrays + POC + value area from the incremental session bins.
        // Built entirely into locals and PUBLISHED at the end (arrays first, count last) so a render
        // frame can never observe a fresh count against a stale/empty array - the old version reset
        // profRowVol to length 0 up front, which guaranteed a torn window every recompute.
        private bool BuildProfileArrays()
        {
            if (profBins.Count == 0 || profRowSize <= 0)
            {
                profRowCount = 0;   // count first on the failure path (render clamps to min anyway)
                profRowVol = new double[0];
                profBuyRowVol = new double[0];
                return false;
            }

            int minRow = int.MaxValue, maxRow = int.MinValue;
            foreach (var k in profBins.Keys)
            {
                if (k < minRow) minRow = k;
                if (k > maxRow) maxRow = k;
            }
            int count = maxRow - minRow + 1;
            int maxR = Math.Max(10, MaxRows);
            if (count > maxR) count = maxR;     // safety clamp on pathological ranges
            if (count < 1) { profRowCount = 0; return false; }

            var rv = new double[count];
            var brv = new double[count];
            double pocVol = 0;
            int pocRow = 0;
            for (int r = 0; r < count; r++)
            {
                profBins.TryGetValue(minRow + r, out double v);
                rv[r] = v;
                profBuyBins.TryGetValue(minRow + r, out double bvr);
                brv[r] = bvr;
                if (v > pocVol) { pocVol = v; pocRow = r; }
            }
            if (pocVol <= 0) { profRowCount = 0; return false; }

            profLow = minRow * profRowSize;
            profHigh = (minRow + count) * profRowSize;
            profPocVol = pocVol;
            profPocRow = pocRow;
            profRowVol = rv;          // arrays before count: a torn read sees old count + new arrays,
            profBuyRowVol = brv;      // or new count + new arrays - never new count + empty arrays
            profRowCount = count;

            curPoc = ProfRowCenter(profPocRow);
            ComputeValueArea();
            return true;
        }

        // Build the CVD-sourced per-row polarity that the wall tint / strip / rail dots read when
        // DeltaSource is Cvd. Faithful to CVDZonesPremium: net delta is computed PER BAR over the rolling
        // scan window, weak bars are dropped (below CvdThresholdPct of the window's max |net|), and each
        // survivor's net is stamped onto a COARSE zone grid (CvdZoneCount fixed-height bins over the
        // lookback high/low, +/-1 spread) rather than onto a single fine wall row. That coarse grid is
        // what makes it read as ZONES: on a tick/range chart the wall rows are only a couple ticks tall,
        // so one-row-per-bar stamping just scatters specks; piling many bars into a small number of tall
        // bins lets contiguous wall rows share a colour and a band emerges. An optional 1-2-1 smoothing
        // pass merges neighbours and kills lone hits. Each wall row then samples the zone its price falls
        // in. Rows with no decisive delta read 0 (neutral) - grey means "nothing decisive here", not "I
        // couldn't tell". No-op unless Cvd is selected. Lookback (CvdLookbackBars) reaches across the
        // session boundary, so prior-session decisive delta still tints in-range zones.
        private void RebuildCvdPolarityMap()
        {
            if (DeltaSource != DeltaSourceEnum.Cvd) { cvdRowPol = new double[0]; return; }

            int rows = profRowCount;
            if (rows <= 0 || profRowSize <= 0) { cvdRowPol = new double[0]; return; }

            int n = scanHighs.Count;
            if (n == 0) { cvdRowPol = new double[0]; return; }

            int lookback = Math.Min(Math.Max(10, CvdLookbackBars), n);
            int start = n - lookback;

            // Coarse zone grid over the lookback's high/low range (CVDZonesPremium uses 62 bins).
            double H = double.MinValue, L = double.MaxValue;
            for (int i = start; i < n; i++)
            {
                if (scanHighs[i] > H) H = scanHighs[i];
                if (scanLows[i]  < L) L = scanLows[i];
            }
            double range = H - L;
            if (range <= 0) { cvdRowPol = new double[0]; return; }

            int zones = Math.Max(4, CvdZoneCount);
            double zStep = range / zones;
            double invZ  = 1.0 / zStep;

            // Pass 1: window max |net| (the threshold reference).
            double maxAbsNet = 0.0;
            for (int i = start; i < n; i++)
            {
                double br = scanHighs[i] - scanLows[i];
                if (br <= 0) continue;
                double nb = scanVols[i] * ((scanCloses[i] - scanLows[i]) - (scanHighs[i] - scanCloses[i])) / br;
                double a = nb < 0 ? -nb : nb;
                if (a > maxAbsNet) maxAbsNet = a;
            }
            if (maxAbsNet <= 0) { cvdRowPol = new double[0]; return; }

            double threshAbs = maxAbsNet * (Math.Max(0.0, Math.Min(100.0, CvdThresholdPct)) / 100.0);

            // Pass 2: stamp surviving bars onto their zone and the two neighbours (distance-gated), the
            // same +/-1 spread CVDZonesPremium uses.
            var znet = new double[zones];
            for (int i = start; i < n; i++)
            {
                double h = scanHighs[i], l = scanLows[i], c = scanCloses[i], v = scanVols[i];
                double br = h - l;
                if (br <= 0) continue;
                double nbar = v * ((c - l) - (h - c)) / br;
                if ((nbar < 0 ? -nbar : nbar) < threshAbs) continue;

                double assignPrice = CvdUseVwMidpoint ? (h + l + c) / 3.0 : c;
                int center = (int)((assignPrice - L) * invZ);
                int z0 = Math.Max(center - 1, 0);
                int z1 = Math.Min(center + 1, zones - 1);
                for (int z = z0; z <= z1; z++)
                {
                    double mid = L + zStep * z + zStep * 0.5;
                    if (Math.Abs(assignPrice - mid) < zStep) znet[z] += nbar;
                }
            }

            // Optional smoothing: a 1-2-1 pass merges adjacent zones into blobbier bands.
            int smooth = Math.Max(0, Math.Min(5, CvdSmoothing));
            for (int s = 0; s < smooth; s++)
            {
                var tmp = new double[zones];
                for (int z = 0; z < zones; z++)
                {
                    double pl = z > 0         ? znet[z - 1] : znet[z];
                    double pc = znet[z];
                    double pr = z < zones - 1 ? znet[z + 1] : znet[z];
                    tmp[z] = (pl + 2.0 * pc + pr) * 0.25;
                }
                znet = tmp;
            }

            // Normalize by the strongest zone so the existing palette spans the full -1..+1.
            double maxAbsZone = 0.0;
            for (int z = 0; z < zones; z++) { double a = znet[z] < 0 ? -znet[z] : znet[z]; if (a > maxAbsZone) maxAbsZone = a; }
            if (maxAbsZone <= 0) { cvdRowPol = new double[0]; return; }

            double invMax = 1.0 / maxAbsZone;

            // Sample each fine wall row from the zone its center price falls in: contiguous rows in one
            // zone share a value, so the wall paints a band instead of a scatter.
            var pol = new double[rows];
            for (int r = 0; r < rows; r++)
            {
                double price = profLow + (r + 0.5) * profRowSize;
                int z = (int)((price - L) * invZ);
                if (z < 0 || z >= zones) { pol[r] = 0.0; continue; }
                double p = znet[z] * invMax;
                if (p > 1.0) p = 1.0; else if (p < -1.0) p = -1.0;
                pol[r] = p;
            }
            cvdRowPol = pol;
        }

        // Standard value-area expansion: grow out from the POC toward the heavier neighbor until the
        // requested fraction of total volume is enclosed.
        private void ComputeValueArea()
        {
            double total = 0;
            for (int r = 0; r < profRowCount; r++) total += profRowVol[r];

            double target = total * Math.Max(0.1, Math.Min(0.95, ValueAreaPercent / 100.0));
            int lo = profPocRow, hi = profPocRow;
            double acc = profRowVol[profPocRow];

            while (acc < target && (lo > 0 || hi < profRowCount - 1))
            {
                double below = lo > 0 ? profRowVol[lo - 1] : -1;
                double above = hi < profRowCount - 1 ? profRowVol[hi + 1] : -1;
                if (above >= below)
                {
                    if (hi < profRowCount - 1) { hi++; acc += profRowVol[hi]; }
                    else if (lo > 0) { lo--; acc += profRowVol[lo]; }
                    else break;
                }
                else
                {
                    if (lo > 0) { lo--; acc += profRowVol[lo]; }
                    else if (hi < profRowCount - 1) { hi++; acc += profRowVol[hi]; }
                    else break;
                }
            }
            curVal = profLow + lo * profRowSize;
            curVah = profLow + (hi + 1) * profRowSize;
        }

        private double ProfRowCenter(int row)
        {
            return profLow + (row + 0.5) * profRowSize;
        }

        private double ScanRowCenter(int row)
        {
            return scanLow + (row + 0.5) * scanRowSize;
        }

        // Build the rolling scan histogram (finer resolution than the structural profile).
        private void BuildScan()
        {
            scanRowVol = new double[0];
            scanPocVol = 0;
            if (scanHighs.Count == 0) return;

            scanHigh = double.MinValue;
            scanLow = double.MaxValue;
            for (int i = 0; i < scanHighs.Count; i++)
            {
                if (scanHighs[i] > scanHigh) scanHigh = scanHighs[i];
                if (scanLows[i] < scanLow) scanLow = scanLows[i];
            }
            if (scanHigh <= scanLow) return;

            double ts = TickSize > 0 ? TickSize : 1.0;
            scanRowSize = ts * Math.Max(1, ScanTicksPerRow);

            int rawRows = (int)Math.Ceiling((scanHigh - scanLow) / scanRowSize) + 1;
            int maxR = Math.Max(10, MaxRows);
            if (rawRows > maxR) { scanRowCount = maxR; scanRowSize = (scanHigh - scanLow) / maxR; }
            else scanRowCount = Math.Max(1, rawRows);
            if (scanRowSize <= 0) return;

            scanRowVol = new double[scanRowCount];
            scanBuyVol = new double[scanRowCount];
            double inv = 1.0 / scanRowSize;

            for (int i = 0; i < scanHighs.Count; i++)
            {
                double bh = scanHighs[i], bl = scanLows[i], bc = scanCloses[i], bv = scanVols[i];
                double range = bh - bl;
                double buyFrac = range > 0 ? (bc - bl) / range : 0.5;
                double bbuy = bv * buyFrac;
                if (range <= 0)
                {
                    int row = (int)((bl - scanLow) * inv);
                    if (row < 0) row = 0; if (row >= scanRowCount) row = scanRowCount - 1;
                    scanRowVol[row] += bv;
                    scanBuyVol[row] += bbuy;
                    continue;
                }
                double invRange = 1.0 / range;
                int startRow = Math.Max(0, (int)((bl - scanLow) * inv));
                int endRow = Math.Min(scanRowCount - 1, (int)((bh - scanLow) * inv));
                for (int row = startRow; row <= endRow; row++)
                {
                    double levelLow = scanLow + row * scanRowSize;
                    double ovHigh = Math.Min(bh, levelLow + scanRowSize);
                    double ovLow = Math.Max(bl, levelLow);
                    double ov = ovHigh - ovLow;
                    if (ov > 0)
                    {
                        double w = ov * invRange;
                        scanRowVol[row] += bv * w;
                        scanBuyVol[row] += bbuy * w;
                    }
                }
            }

            for (int i = 0; i < scanRowCount; i++)
                if (scanRowVol[i] > scanPocVol) scanPocVol = scanRowVol[i];
        }

        private void DetectHvn()
        {
            var list = new List<HvnBand>();
            BuildHvnBands(profRowVol, profRowCount, profLow, profRowSize, profPocVol, list);
            hvnBands = list;   // publish by reference - render never sees a half-built list
        }

        // Wall Gradient heat score. No detection, no thresholds, no bands - the failure mode that
        // painted the pdVAL..CPR plateau as one giant "HVN" simply cannot happen here, because there
        // is no yes/no decision to get wrong. Every traded row gets a score in [0..1]:
        //   1   = POC (densest)      -> HVN color at Gradient Max Opacity
        //   0   = thinnest traded row -> LVN color at Gradient Max Opacity
        //   0.5 = neutral middle      -> faint (Gradient Min Opacity floor)
        // The score blends two normalizations, weighted by GradientRankWeight:
        //   RANK   (weight w)  : the row's percentile among traded rows. Immune to a dominant POC
        //                        squashing everything else green - a secondary shelf still ranks high.
        //   LINEAR (weight 1-w): vol / pocVol. Preserves true magnitude so a flat mid-volume plateau
        //                        reads uniformly faint instead of getting an artificial rank spread
        //                        painted across it.
        // Zero-volume rows get -1 (sentinel: draw nothing). Published as a fresh array, same
        // arrays-before-count discipline as profRowVol - render clamps with Math.Min and never
        // observes a half-built score set.
        private void ComputeGradientScores()
        {
            var rv = profRowVol;
            int count = Math.Min(profRowCount, rv.Length);
            double pocVol = profPocVol;
            if (count <= 0 || pocVol <= 0) { profGradScore = new float[0]; return; }

            var score = new float[count];
            var idx = new List<int>(count);
            for (int r = 0; r < count; r++)
            {
                score[r] = -1f;                    // sentinel: untouched rows draw nothing
                if (rv[r] > 0) idx.Add(r);
            }
            if (idx.Count == 0) { profGradScore = score; return; }
            idx.Sort((a, b) => rv[a].CompareTo(rv[b]));

            double w = Math.Max(0, Math.Min(100, GradientRankWeight)) / 100.0;
            double denom = Math.Max(1, idx.Count - 1);
            for (int i = 0; i < idx.Count; i++)
            {
                int r = idx[i];
                double rank = idx.Count == 1 ? 1.0 : i / denom;
                double lin = rv[r] / pocVol; if (lin > 1) lin = 1;
                score[r] = (float)(w * rank + (1.0 - w) * lin);
            }
            profGradScore = score;
        }

        // HVN band detector.
        //   HvnLocalNodes OFF: contiguous rows at/above HvnFraction of the profile's GLOBAL peak (POC).
        //                      This is the original behavior, byte-for-byte, and stays the default.
        //   HvnLocalNodes ON : hybrid. A row qualifies if it clears the global gate OR it stands out
        //                      relative to its LOCAL neighborhood (>= HvnLocalProminence x the window
        //                      average), subject to an absolute floor (HvnFloorFraction of POC) so the
        //                      dead tail stays quiet. This surfaces secondary/tertiary shelves that a
        //                      global-only gate can never reach without fusing the whole top into a blob.
        // Either way, contiguous qualifying rows collapse into a single band.
        private void BuildHvnBands(double[] rowVol, int count, double low, double rowSize, double pocVol, List<HvnBand> outList)
        {
            outList.Clear();
            if (rowVol == null || count <= 0 || pocVol <= 0 || rowSize <= 0) return;
            double gate = HvnFraction * pocVol;

            var isHvn = new bool[count];

            if (!HvnLocalNodes)
            {
                // ---- Classic detection: global gate only ----
                for (int r = 0; r < count; r++)
                    if (rowVol[r] >= gate) isHvn[r] = true;
            }
            else
            {
                // ---- Local nodes: bumps above their own valley-to-valley baseline ----
                // A shelf on the flank of a dominant POC barely rises above the HIGHER of its two valleys
                // (the profile is only just coming off the POC on that side), so the old max-saddle test
                // rejected most of them and truncated the rest. Two decoupled measures fix that:
                //   KEEP: a bump counts if it rises >= riseGate above its DEEPER flanking valley - the rise
                //         it genuinely stands above - which catches flank shelves. The local-maximum
                //         requirement keeps plain slopes (which have no peak) out, so leniency is safe.
                //   WRAP: the band is placed against the straight line between the bump's two valleys, so it
                //         hugs the bulge itself and doesn't run off down the whole slope. Valley rows stay
                //         unflagged (excess -> 0 there) so neighboring bumps remain separate bands.
                double floor = Math.Max(0.0, HvnFloorFraction) * pocVol;
                double riseGate = Math.Max(0.0, HvnLocalProminence) * pocVol;   // rise as a fraction of POC
                double wrapEps = 0.005 * pocVol;                                 // "clear of the baseline" margin
                int w = Math.Max(1, HvnLocalWindow);

                // Light smoothing for DETECTION only (rendering still uses raw rows) so single-row jitter
                // doesn't spawn phantom peaks/valleys.
                int sr = Math.Max(1, w / 4);
                var sm = new double[count];
                for (int r = 0; r < count; r++)
                {
                    int a = r - sr; if (a < 0) a = 0;
                    int b = r + sr; if (b > count - 1) b = count - 1;
                    double s = 0; for (int k = a; k <= b; k++) s += rowVol[k];
                    sm[r] = s / (b - a + 1);
                }

                // Global-gate rows always belong (dominant wall, unchanged from classic).
                for (int r = 0; r < count; r++)
                    if (rowVol[r] >= gate) isHvn[r] = true;

                for (int r = 0; r < count; r++)
                {
                    if (sm[r] < floor || rowVol[r] >= gate) continue;

                    // Local maximum over the window (ties allowed)?
                    int a = r - w; if (a < 0) a = 0;
                    int b = r + w; if (b > count - 1) b = count - 1;
                    bool isMax = true;
                    for (int k = a; k <= b; k++) if (sm[k] > sm[r]) { isMax = false; break; }
                    if (!isMax) continue;

                    // Deepest trough on each side, stopping at higher ground or the array edge.
                    int Lv = r; double lvVol = sm[r];
                    for (int k = r - 1; k >= 0; k--)
                    {
                        if (sm[k] >= sm[r]) break;
                        if (sm[k] < lvVol) { lvVol = sm[k]; Lv = k; }
                    }
                    int Rv = r; double rvVol = sm[r];
                    for (int k = r + 1; k < count; k++)
                    {
                        if (sm[k] >= sm[r]) break;
                        if (sm[k] < rvVol) { rvVol = sm[k]; Rv = k; }
                    }
                    if (Rv - Lv < 2) continue;   // no room for a bump

                    // KEEP test: rise above the deeper of the two valleys.
                    if (sm[r] - Math.Min(lvVol, rvVol) < riseGate) continue;

                    // WRAP: flag rows that clear the straight valley-to-valley baseline. Excess -> 0 at the
                    // valleys, so the band tapers off exactly where the bulge rejoins the trend, and the
                    // valley rows stay unflagged -> a gap that keeps this bump separate from its neighbors.
                    double denom = Rv - Lv;
                    for (int k = Lv + 1; k <= Rv - 1; k++)
                    {
                        double baseLine = lvVol + (rvVol - lvVol) * ((k - Lv) / denom);
                        if (sm[k] >= floor && sm[k] - baseLine >= wrapEps) isHvn[k] = true;
                    }
                    isHvn[r] = true;   // always include the peak
                }
            }

            // Collapse contiguous flagged rows into bands, then place each band's edges per the edge mode.
            int j = 0;
            while (j < count)
            {
                if (isHvn[j])
                {
                    int start = j;
                    double peak = rowVol[j];
                    while (j < count && isHvn[j])
                    {
                        if (rowVol[j] > peak) peak = rowVol[j];
                        j++;
                    }
                    int end = j - 1;
                    EmitHvnBand(rowVol, start, end, low, rowSize, peak, outList);
                }
                else j++;
            }
        }

        // Turn a flagged row run [start..end] into a band. Threshold mode uses the raw run edges.
        // Value-area mode instead covers the central HvnNodeVaPct% of THIS node's own volume, expanded
        // outward from the node's peak using the same single-row, tie-to-upper convention as the session
        // and HTF value areas - so a node's zone reflects its own shape rather than an external cut line.
        private void EmitHvnBand(double[] rowVol, int start, int end, double low, double rowSize, double peak, List<HvnBand> outList)
        {
            int loRow = start, hiRow = end;

            // Peak (POC) row of this node - the highest-volume row in its territory.
            int pk = start; double pv = rowVol[start];
            for (int r = start; r <= end; r++)
                if (rowVol[r] > pv) { pv = rowVol[r]; pk = r; }

            if (HvnVaEdges && end > start)
            {
                double total = 0;
                for (int r = start; r <= end; r++) total += rowVol[r];
                if (total > 0)
                {
                    double target = total * (Math.Max(40, Math.Min(95, HvnNodeVaPct)) / 100.0);
                    int lo = pk, hi = pk; double acc = rowVol[pk];
                    while (acc < target && (lo > start || hi < end))
                    {
                        double below = lo > start ? rowVol[lo - 1] : -1;
                        double above = hi < end ? rowVol[hi + 1] : -1;
                        if (above >= below) { if (hi < end) { hi++; acc += rowVol[hi]; } else if (lo > start) { lo--; acc += rowVol[lo]; } else break; }
                        else { if (lo > start) { lo--; acc += rowVol[lo]; } else if (hi < end) { hi++; acc += rowVol[hi]; } else break; }
                    }
                    loRow = lo; hiRow = hi;
                }
            }

            outList.Add(new HvnBand
            {
                LowPrice = low + loRow * rowSize,
                HighPrice = low + (hiRow + 1) * rowSize,
                PeakVol = peak,
                PocPrice = low + (pk + 0.5) * rowSize   // center of the peak-volume row
            });
        }

        // LVN detection on the SMOOTHED scan histogram. For each row, find the nearest higher-volume
        // wall on each side within a distance-anchored window (skipping empty gaps); a row qualifies
        // if it is the local minimum between two such walls and sits below the valley gate. Contiguous
        // qualifying rows then collapse into ONE level at the deepest point - so a multi-bin gap is a
        // single clean rail, and light smoothing keeps single-bin noise from registering. This keeps
        // the original detector's completeness (tail valleys included) while removing the flicker.
        private void DetectLvn()
        {
            detected.Clear();
            if (scanRowCount < 3 || scanPocVol <= 0) return;
            if (RailSource != RailSourceEnum.ConfirmedPivots) DetectValleys();
            if (RailSource != RailSourceEnum.VolumeValleys) DetectPivots();
        }

        // Predictive LVN troughs: local minima in the smoothed scan flanked by higher walls.
        private void DetectValleys()
        {
            double[] sm = Smooth(scanRowVol, Math.Max(0, SmoothBins));
            double valleyFactor = Math.Max(0.05, Math.Min(0.95, LvnValleyFactor));
            double wallFloor = Math.Max(0.0, WallMinFraction) * scanPocVol;
            int window = Math.Max(2, (int)Math.Round(LvnFlankTicks / (double)Math.Max(1, ScanTicksPerRow)));

            var flag = new bool[scanRowCount];
            var dep = new double[scanRowCount];

            for (int i = 0; i < scanRowCount; i++)
            {
                double cur = sm[i];
                if (cur <= 0) continue;

                double leftPeak = 0; bool leftWall = false, leftMinOk = true;
                for (int j = i - 1; j >= i - window && j >= 0; j--)
                {
                    if (sm[j] <= 0) continue;
                    if (sm[j] < cur) { leftMinOk = false; break; }
                    if (sm[j] > cur) { leftWall = true; if (sm[j] > leftPeak) leftPeak = sm[j]; }
                }
                if (!leftMinOk || !leftWall) continue;

                double rightPeak = 0; bool rightWall = false, rightMinOk = true;
                for (int j = i + 1; j <= i + window && j < scanRowCount; j++)
                {
                    if (sm[j] <= 0) continue;
                    if (sm[j] < cur) { rightMinOk = false; break; }
                    if (sm[j] > cur) { rightWall = true; if (sm[j] > rightPeak) rightPeak = sm[j]; }
                }
                if (!rightMinOk || !rightWall) continue;

                double flank = Math.Min(leftPeak, rightPeak);
                if (flank <= 0 || flank < wallFloor) continue;
                if (cur > flank * valleyFactor) continue;

                flag[i] = true;
                dep[i] = 1.0 - (cur / flank);   // topographic prominence (0..1)
            }

            // Collapse contiguous qualifying rows into one level at the deepest point.
            int r = 0;
            while (r < scanRowCount)
            {
                if (!flag[r]) { r++; continue; }
                double maxDep = 0; int bestRow = r;
                double regBuy = 0, regTot = 0;
                while (r < scanRowCount && flag[r])
                {
                    if (dep[r] > maxDep) { maxDep = dep[r]; bestRow = r; }
                    regTot += scanRowVol[r];
                    if (r < scanBuyVol.Length) regBuy += scanBuyVol[r];
                    r++;
                }
                double pol = regTot > 0 ? (2.0 * regBuy / regTot - 1.0) : 0.0;
                detected.Add(new TrackedLvn
                {
                    Price = ScanRowCenter(bestRow),
                    Depth = maxDep,
                    Strong = maxDep >= LvnStrongDepth,
                    Polarity = pol
                });
            }
        }

        // Confirmed Pivot Reversals (CPR): swing pivots in the rolling window that landed in volume
        // thinner than their surroundings - i.e. price actually reversed, and did so in a void. The
        // pivot is the noise filter (only marks where price turned); the volume gate keeps only the
        // turns that happened in thin air. Snapshot each recompute; the tracked lifecycle dedupes.
        private void DetectPivots()
        {
            int n = scanHighs.Count;
            int s = Math.Max(1, PivotStrength);
            if (n < 2 * s + 1 || scanRowSize <= 0) return;

            double factor = Math.Max(0.1, Math.Min(1.0, PivotVolumeFactor));
            int farRows = Math.Max(2, (int)Math.Round(PivotVolumeWindow / (double)Math.Max(1, ScanTicksPerRow)));
            double minSep = scanRowSize;   // only collapse exact-row duplicates; render-merge clusters the rest

            for (int p = s; p < n - s; p++)
            {
                // Swing high: bar p's high is the max of its +/- s neighbourhood (strict vs at least one side).
                bool isHigh = true, gtSome = false;
                for (int j = p - s; j <= p + s && isHigh; j++)
                {
                    if (j == p) continue;
                    if (scanHighs[j] > scanHighs[p]) isHigh = false;
                    else if (scanHighs[j] < scanHighs[p]) gtSome = true;
                }
                if (isHigh && gtSome) TryAddPivot(scanHighs[p], +1, p, n, factor, farRows, minSep);

                // Swing low: bar p's low is the min of its neighbourhood.
                bool isLow = true, ltSome = false;
                for (int j = p - s; j <= p + s && isLow; j++)
                {
                    if (j == p) continue;
                    if (scanLows[j] < scanLows[p]) isLow = false;
                    else if (scanLows[j] > scanLows[p]) ltSome = true;
                }
                if (isLow && ltSome) TryAddPivot(scanLows[p], -1, p, n, factor, farRows, minSep);
            }
        }

        // Gate one pivot by local volume; if thin AND not already mitigated, stage it as a CPR.
        private void TryAddPivot(double price, int dir, int p, int n, double factor, int farRows, double minSep)
        {
            int row = (int)Math.Floor((price - scanLow) / scanRowSize);
            if (row < 0 || row >= scanRowCount) return;

            // Near band (the reversal zone) vs the broader local area. Thin reversal => near < factor*far.
            double nearSum = 0; int nearN = 0;
            for (int k = row - 1; k <= row + 1; k++)
                if (k >= 0 && k < scanRowCount) { nearSum += scanRowVol[k]; nearN++; }
            double farSum = 0; int farN = 0;
            for (int k = row - farRows; k <= row + farRows; k++)
                if (k >= 0 && k < scanRowCount) { farSum += scanRowVol[k]; farN++; }
            if (nearN == 0 || farN == 0) return;

            double nearAvg = nearSum / nearN;
            double farAvg = farSum / farN;
            if (farAvg <= 0 || nearAvg >= factor * farAvg) return;   // not in a void -> reject

            // Retroactive lifecycle: replay the bars AFTER the pivot through the SAME rules the live
            // path uses (FRVP semantics) rather than dropping the pivot on its first close-through.
            // A close through the level flips its polarity and re-arms it; the level is only "spent"
            // once it has burned its flip or touch budget. This is what kept CPRs from being born at
            // all after a single poke - a flipped level is still tradeable structure, just inverted.
            double buf = FillBufferTicks * (TickSize > 0 ? TickSize : 1.0);
            double bandTol = scanRowSize * 0.5;
            int rDir = dir, rFlips = 0, rTouches = 0;
            bool rLeft = false, rInside = false, rTested = false;

            for (int k = p + 1; k < n && k < scanCloses.Count; k++)
            {
                double kh = scanHighs[k], kl = scanLows[k], kc = scanCloses[k];

                // Flip on a CLOSE through the level. Unlike an FRVP band, a rail is a line - price is
                // never "inside" it at birth - so the flip is not gated on HasLeft. Wicks never count.
                bool broke = rDir > 0 ? kc >= price + buf : kc <= price - buf;
                if (broke)
                {
                    if (!CprFlipOnBreakthrough || CprMaxFlips == 0) return;   // legacy: spent on first close-through
                    rDir = -rDir;
                    rFlips++;
                    rLeft = false;
                    rInside = false;
                    rTested = true;
                    if (rFlips >= CprMaxFlips) return;                        // burned its flip budget
                    continue;
                }

                if (!rLeft)
                {
                    // Working side: a swing-high rail (+1) works while price is UNDER it, and vice-versa.
                    if (rDir > 0 ? kh < price - bandTol : kl > price + bandTol) rLeft = true;
                    continue;   // returns don't count until it has cleared
                }

                bool tch = kh >= price - bandTol && kl <= price + bandTol;
                if (tch && !rInside)
                {
                    rTouches++;
                    if (CprRetireTouches > 0 && rTouches >= CprRetireTouches) return;   // tagged out
                }
                if (tch) rTested = true;
                rInside = tch;
            }

            // De-dupe against pivots already staged this pass (near-equal price).
            foreach (var d in detected)
                if (d.IsPivot && Math.Abs(d.Price - price) <= minSep) return;

            double rowBuy = row < scanBuyVol.Length ? scanBuyVol[row] : 0.0;
            double rowTot = scanRowVol[row];
            double pol = rowTot > 0 ? (2.0 * rowBuy / rowTot - 1.0) : 0.0;
            double depth = 1.0 - (nearAvg / farAvg);   // how deep the void is (0..1)
            int originBar = CurrentBar - (n - 1 - p);   // absolute bar index of the source pivot

            detected.Add(new TrackedLvn
            {
                Price = price,
                Depth = depth,
                Strong = true,          // a confirmed reversal is a first-class level
                Polarity = pol,
                IsPivot = true,
                Dir = rDir,             // replayed polarity (flipped if price closed through it)
                OriginBar = originBar,
                Tested = rTested,
                HasLeft = rLeft,
                Inside = rInside,
                Touches = rTouches,
                Flips = rFlips
            });
        }

        private static double[] Smooth(double[] src, int radius)
        {
            int n = src.Length;
            if (radius <= 0 || n == 0) return (double[])src.Clone();
            var outv = new double[n];
            for (int i = 0; i < n; i++)
            {
                double sum = 0; int cnt = 0;
                int lo = Math.Max(0, i - radius), hi = Math.Min(n - 1, i + radius);
                for (int j = lo; j <= hi; j++) { sum += src[j]; cnt++; }
                outv[i] = cnt > 0 ? sum / cnt : 0;
            }
            return outv;
        }

        // Blend distinct holds with rejection strength into a 0..1 earned weight. Holds saturate
        // (each additional hold matters less); strength is a pre-normalised 0..1 push-off magnitude.
        // Weighted toward holds - a level that repeatedly repels price is the stronger tell.
        private double ReactionScore(int holds, double strength01)
        {
            if (holds <= 0) return 0.0;
            double hc = holds / (holds + 1.5);
            double sc = strength01 < 0 ? 0 : (strength01 > 1 ? 1 : strength01);
            double s = 0.6 * hc + 0.4 * sc;
            return s > 1 ? 1 : s;
        }

        // Track a level's contact with the just-closed bar for reaction memory. A touch marks it
        // Inside; leaving without a close-through completes a HOLD and samples the push-off (how far
        // the close ended from the level, in ATR). Fills are handled by the caller before this runs.
        private void MonitorRailTouch(TrackedLvn t, double lo, double hi, double tol)
        {
            bool touching = (lo - tol <= t.Price && t.Price <= hi + tol);
            if (touching)
            {
                t.Tested = true;
                t.LastTouchBar = CurrentBar;
                t.Inside = true;
            }
            else if (t.Inside)
            {
                if (ScoreReactions)
                {
                    double atr = (frvpAtr != null && CurrentBar >= FrvpAtrPeriod && frvpAtr[0] > 0)
                               ? frvpAtr[0] : 10.0 * (TickSize > 0 ? TickSize : 1.0);
                    double rej = Math.Abs(Close[0] - t.Price) / atr;   // push-off in ATR
                    t.Holds++;
                    if (rej > t.RejectMax) t.RejectMax = rej;
                    t.Score = ReactionScore(t.Holds, t.RejectMax / 1.5);   // full strength at 1.5 ATR
                }
                t.Inside = false;
            }
        }

        // Persistence: promote detected (window) LVNs into a list that survives beyond the rolling
        // window. A level stays NAKED until a bar wicks into it (TESTED), and retires/hides once a
        // bar closes through it (FILLED). Monitored against the just-closed bar each update.
        private void UpdateTrackedLvns()
        {
            double matchTol = scanRowSize > 0 ? scanRowSize : (TickSize > 0 ? TickSize : 1.0);
            double tol = matchTol * 0.5;

            // 1) Merge new detections. A detected LVN matching an existing tracked level (within a
            //    row) refreshes its depth; otherwise it is born naked.
            foreach (var d in detected)
            {
                TrackedLvn match = null;
                for (int k = 0; k < tracked.Count; k++)
                {
                    if (Math.Abs(tracked[k].Price - d.Price) <= matchTol)
                    {
                        match = tracked[k];
                        break;
                    }
                }
                if (match != null)
                {
                    match.Polarity = d.Polarity;   // always refresh lean from latest read
                    if (d.IsPivot) match.IsPivot = true;   // a valley confirmed by a pivot graduates to CPR
                    // Refresh the source only while the tracked level is still in its original polarity.
                    // Once it has flipped, its Dir is owned by the LIVE lifecycle - the detector's
                    // window replay would otherwise stomp it back and forth every recompute.
                    if (d.IsPivot && match.Flips == 0) { match.Dir = d.Dir; match.OriginBar = d.OriginBar; }
                    if (!match.Filled && d.Depth > match.Depth)
                    {
                        match.Depth = d.Depth;
                        match.Strong = d.Strong;
                    }
                }
                else
                {
                    tracked.Add(new TrackedLvn
                    {
                        Price = d.Price,
                        Depth = d.Depth,
                        Strong = d.Strong,
                        Polarity = d.Polarity,
                        IsPivot = d.IsPivot,
                        Dir = d.Dir,
                        OriginBar = d.IsPivot ? d.OriginBar : -1,
                        Tested = d.Tested,
                        Filled = false,
                        BornBar = CurrentBar,
                        LastTouchBar = -1,
                        HasLeft = d.HasLeft,   // replayed lifecycle (CPR only; valleys leave these zeroed)
                        Inside = d.Inside,
                        Touches = d.Touches,
                        Flips = d.Flips
                    });
                }
            }

            // 2) Monitor the just-closed bar: touch -> tested, close-through -> filled.
            double hi = High[0];
            double lo = Low[0];
            double c0 = Close[0];
            double c1 = CurrentBar > 0 ? Close[1] : c0;
            double buf = FillBufferTicks * (TickSize > 0 ? TickSize : 1.0);

            foreach (var t in tracked)
            {
                if (t.Filled) continue;

                if (t.IsPivot && t.Dir != 0)
                {
                    // CPR lifecycle, mirroring UpdateFrvpMitigation/RetireFrvpZones. A close on the
                    // opposite side of the reversal no longer kills the level - it FLIPS its polarity
                    // (broken resistance becomes support) and re-arms it as Tested, exactly like a
                    // broken supply zone becoming demand. The rail only retires on counts: too many
                    // flips (chopped clean through) or too many touches (tagged out). Wicks never count.
                    bool broke = (t.Dir > 0 && c0 >= t.Price + buf) || (t.Dir < 0 && c0 <= t.Price - buf);
                    if (broke)
                    {
                        if (!CprFlipOnBreakthrough || CprMaxFlips == 0) { t.Filled = true; continue; }
                        t.Dir = -t.Dir;
                        t.Flips++;
                        t.Tested = true;      // a flipped rail is proven structure, not virgin
                        t.HasLeft = false;    // must clear the new working side before returns count
                        t.Inside = false;
                        if (t.Flips >= CprMaxFlips) t.Filled = true;
                        continue;
                    }

                    if (!t.HasLeft)
                    {
                        // A swing-high rail (+1) works while price is under it; a swing-low (-1) above it.
                        bool away = t.Dir > 0 ? hi < t.Price - tol : lo > t.Price + tol;
                        if (away) t.HasLeft = true;
                        continue;
                    }

                    bool touched = hi >= t.Price - tol && lo <= t.Price + tol;
                    if (touched && !t.Inside) t.Touches++;
                    MonitorRailTouch(t, lo, hi, tol);   // owns Inside/Tested/Holds/Score
                    if (CprRetireTouches > 0 && t.Touches >= CprRetireTouches) t.Filled = true;
                    continue;
                }

                double a = c1 - t.Price;
                double b = c0 - t.Price;
                bool crossed = (a > 0 && b < 0) || (a < 0 && b > 0);
                if (crossed && Math.Abs(b) >= buf)
                {
                    t.Filled = true;
                    continue;
                }
                MonitorRailTouch(t, lo, hi, tol);
            }

            // 3) Purge filled tombstones the detector no longer supports (volume has filled the gap),
            //    keeping ones still detected so they suppress immediate re-add (anti-flicker).
            for (int k = tracked.Count - 1; k >= 0; k--)
            {
                if (!tracked[k].Filled) continue;
                bool stillDetected = false;
                foreach (var d in detected)
                {
                    if (Math.Abs(d.Price - tracked[k].Price) <= matchTol) { stillDetected = true; break; }
                }
                if (!stillDetected) tracked.RemoveAt(k);
            }

            // 4) Memory cap: drop the oldest levels (filled first, then by birth) if over budget.
            int cap = Math.Max(10, MaxTrackedLevels);
            if (tracked.Count > cap)
            {
                tracked.Sort((x, y) =>
                {
                    if (x.Filled != y.Filled) return x.Filled ? -1 : 1; // filled first (removed first)
                    return x.BornBar.CompareTo(y.BornBar);              // then oldest first
                });
                tracked.RemoveRange(0, tracked.Count - cap);
            }

            // 5) True delta: refresh each level's lean from the session profile at its own price row,
            //    so the rail polarity dots read real bid/ask delta (not the OHLC-scan proxy). Skipped
            //    in Proxy mode (dots keep their scan-derived lean); skipped per-level where the level
            //    hasn't traded this session (keeps its last-known lean rather than snapping to neutral).
            if (DeltaSource == DeltaSourceEnum.True)
            {
                foreach (var t in tracked)
                {
                    if (t.Filled) continue;
                    double p = SessionPolarityAt(t.Price);
                    if (!double.IsNaN(p)) t.Polarity = p;
                }
            }
        }

        #endregion

        #region Rendering

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (chartControl == null || chartScale == null || ChartBars == null || Bars == null) return;
            if (RenderTarget == null) return;
            if (!hasProfile || profPocVol <= 0 || profRowCount <= 0) return;

            try
            {
                renderCC = chartControl;
                labelQueue.Clear();   // defensive: an exception mid-frame must not leak labels forward
                if ((HoverReveal || LabelGroupTooltip) && !mouseHooked) HookMouse();
                int lastIdx = ChartBars.ToIndex;
                int fromIdx = ChartBars.FromIndex;
                if (lastIdx < fromIdx) return;

                float lastX = chartControl.GetXByBarIndex(ChartBars, lastIdx);
                float prevX = chartControl.GetXByBarIndex(ChartBars, Math.Max(fromIdx, lastIdx - 1));
                float barDist = Math.Abs(lastX - prevX);
                if (barDist < 0.5f) barDist = 5f;

                float canvasLeft = (float)chartControl.CanvasLeft;
                float panelRight = (float)chartControl.CanvasRight;

                // Pin the profile to the right edge of the price panel (screen-fixed, like a
                // standard volume profile) so it stays put when the chart is scrolled. Reserve a
                // gutter on the right for the level labels so they don't land on the price axis.
                float gutter = ShowLabels ? Math.Max(70f, LabelFontSize * 8f) : 4f;
                float wallRightX = panelRight - gutter;
                if (wallRightX <= canvasLeft) wallRightX = panelRight;
                renderWallRightX = wallRightX;
                renderBarDist = barDist;
                renderCanvasLeft = canvasLeft;
                renderCanvasRight = panelRight;
                var cpTip = ChartPanel;
                if (cpTip != null) { renderPanelTop = (float)cpTip.Y; renderPanelBottom = (float)(cpTip.Y + cpTip.H); }
                // Current price = live trade if flowing, else the LAST bar by ABSOLUTE index. The
                // relative Close[0] is unreliable in this multi-series render context (CurrentBar can
                // be parked on an old bar), so read Count-1 directly off the primary series.
                double absLastClose;
                var pbar = BarsArray != null && BarsArray.Length > 0 ? BarsArray[0] : null;
                if (pbar != null && pbar.Count > 0) absLastClose = pbar.GetClose(pbar.Count - 1);
                else absLastClose = Close[0];
                renderLastPrice = !double.IsNaN(lastTradePrice) ? lastTradePrice : absLastClose;

                // Left extent for horizontal reference lines (VA, POC, prior sessions, PDH/PDL, HVN).
                // 0 = full span to the canvas edge; otherwise N bars left of the wall.
                float lineLeftX = LineProjectionBars > 0 ? wallRightX - LineProjectionBars * barDist : canvasLeft;
                if (lineLeftX < canvasLeft) lineLeftX = canvasLeft;

                // HTF layers wait for the backfill (htfReady) so a partial weekly/monthly profile
                // never draws - a 3-day "monthly" POC is worse than none.
                bool htfOk = htfReady;

                // HTF Profile View: a 10,000-foot read. Hides the LTF wall, levels, ghost and session,
                // leaving only the weekly/monthly profile (with its own VAH/VAL/POC), HVN zones and FRVP.
                bool htfView = HtfProfileView && (ShowWeeklyProfile || ShowMonthlyProfile) && htfOk;

                // Faint HVN no-fly zones first (under everything). In HTF view they derive from the
                // weekly/monthly profile being shown; otherwise from the session profile.
                if (ShowHvnZones)
                {
                    if (htfView)
                    {
                        var snap = ShowWeeklyProfile ? devWeekSnap : devMonthSnap;
                        var bands = ShowWeeklyProfile ? weekHvnSnap : monthHvnSnap;
                        if (snap != null && snap.Peak > 0)
                            DrawHvnZones(chartScale, canvasLeft, wallRightX, bands, snap.Peak);
                    }
                    else
                    {
                        DrawHvnZones(chartScale, canvasLeft, wallRightX, hvnBands, profPocVol);
                    }
                }

                // Prior-day ghost profile silhouette (dim, viewport-anchored, only inside a prior VA).
                if (!htfView && ShowGhostProfile && ShowGhostSilhouette)
                    DrawGhostSilhouette(chartScale, canvasLeft, wallRightX);

                // Weekly / monthly profile silhouettes, bar-anchored over their own periods (HTF charts).
                if ((ShowWeeklyProfile || ShowMonthlyProfile) && htfOk)
                    DrawHtfProfiles(chartScale, wallRightX);

                // LVN rails (under the wall so they appear to exit the gaps).
                if (!htfView && ShowRails)
                    DrawRails(chartScale, canvasLeft, wallRightX, barDist);

                // The wall on top.
                if (!htfView && ShowWall)
                    DrawWall(chartScale, wallRightX);

                // Polarity ribbon at the wall's right edge.
                if (!htfView && ShowPolarityStrip)
                    DrawPolarityStrip(chartScale, wallRightX);

                // POC line.
                if (!htfView && ShowPOC)
                    DrawPoc(chartScale, lineLeftX, wallRightX);

                // Current-session value area + prior-session references.
                pendingLevels.Clear();   // start collecting mergeable reference levels for this frame
                if (!htfView && ShowValueArea)
                    DrawValueArea(chartScale, lineLeftX, wallRightX);
                if (!htfView && PriorSessionsToShow > 0)
                    DrawPriorSessions(chartScale, lineLeftX, wallRightX);

                // Ghost session's VAH/VAL/POC as reference levels (same treatment as the rest).
                if (!htfView && ShowGhostProfile && ShowGhostLevels)
                    DrawGhostLevels(chartScale, lineLeftX, wallRightX);

                // FRVP consolidation zones: source outline always; VA projection revealed near price.
                if (ShowFrvpZones)
                    DrawFrvpZones(chartScale, wallRightX);

                // Out-of-value FRVP: 15-min zones that sit outside today's VA (reversal shelves -> POC).
                if (ShowFrvpOutOfVa)
                    DrawFrvpOutOfVa(chartScale, canvasLeft, lineLeftX, wallRightX);

                // Out-of-value order blocks: HTF/out-of-value OBs that sling price back to the candle POC.
                if (ShowObOutOfVa)
                    DrawObOutOfVa(chartScale, canvasLeft, lineLeftX, wallRightX);

                // Session levels (Asia/London/NY).
                if (!htfView && (ShowAsia || ShowLondon || ShowNewYork))
                    DrawSessionLevels(chartScale, lineLeftX, wallRightX);

                // Midnight open (12 AM ET).
                if (!htfView && ShowMidnightOpen)
                    DrawMidnightOpen(chartScale, lineLeftX, wallRightX);

                // Higher-timeframe (weekly / monthly) POC + value area — obey the global line length.
                // Suppressed in HTF view, where each profile draws its own period-anchored VAH/VAL/POC.
                if (!htfView && (ShowWeeklyPoc || ShowWeeklyVA || ShowMonthlyPoc) && htfOk)
                    DrawHtfPocs(chartScale, lineLeftX, wallRightX);

                // Merge pass: collapse stacked mergeable reference LINES (POC/CPR/current VA are exempt).
                if (MergeLevels)
                    FlushMergedLevels(chartScale, wallRightX);

                // Label pass: every layer above has queued its gutter text instead of painting it.
                // Resolve collisions once, in pixel space, now that all geometry is down. This must be
                // the last thing before the banners - nothing may draw into the gutter after it.
                FlushGutterLabels();

                // HTF proximity alert banner (LTF entry charts). Realtime only, drawn on top.
                if (ShowHtfAlerts && State == State.Realtime && htfOk)
                {
                    UpdateHtfAlerts();
                    DrawAlertBanner(canvasLeft, panelRight);
                }
                else if (!ShowHtfAlerts)
                {
                    alertActive[0] = alertActive[1] = alertActive[2] = alertActive[3] = false;
                }

                // Backfill pending: a small notice instead of a partial HTF read.
                if (!htfOk && (ShowWeeklyPoc || ShowWeeklyVA || ShowMonthlyPoc
                               || ShowWeeklyProfile || ShowMonthlyProfile || ShowHtfAlerts))
                    DrawText("HTF backfill…", Brushes.Gray, 70, canvasLeft + 12f, 16f);
            }
            catch { /* never take down the chart */ }
            finally
            {
                // Dispose per-frame fallback brushes (non-solid user brushes only; the cache persists).
                for (int i = 0; i < transientBrushes.Count; i++) transientBrushes[i]?.Dispose();
                transientBrushes.Clear();
            }
        }

        private void DrawHtfPocs(ChartScale chartScale, float leftX, float wallRightX)
        {
            // ---- Developing (current) week ----
            if (ShowWeeklyPoc && !double.IsNaN(devWeekPoc))
                DrawHLine(devWeekPoc, WeeklyPocColor, WeeklyPocStyle, WeeklyThickness, WeeklyOpacity, leftX, wallRightX,
                    chartScale.GetYByValue(devWeekPoc), ShowLabels ? "wPOC" + PriceSuffix(devWeekPoc) : null, true, LabelRankEnum.Poc);
            if (ShowWeeklyVA && !double.IsNaN(devWeekVah) && devWeekVah > devWeekVal)
            {
                DrawHLine(devWeekVah, WeeklyPocColor, WeeklyVaStyle, 1, Math.Max(25, WeeklyOpacity - 15), leftX, wallRightX,
                    chartScale.GetYByValue(devWeekVah), ShowLabels ? "wVAH" + PriceSuffix(devWeekVah) : null, false, LabelRankEnum.Htf);
                DrawHLine(devWeekVal, WeeklyPocColor, WeeklyVaStyle, 1, Math.Max(25, WeeklyOpacity - 15), leftX, wallRightX,
                    chartScale.GetYByValue(devWeekVal), ShowLabels ? "wVAL" + PriceSuffix(devWeekVal) : null, false, LabelRankEnum.Htf);
            }

            // ---- Prior weeks (static) ----
            var pw = priorWeeksSnap;
            int showW = Math.Min(PriorWeeksToShow, pw.Length);
            for (int n = 0; n < showW; n++)
            {
                var w = pw[n];
                string pfx = n == 0 ? "pw" : "pw" + (n + 1);
                if (ShowWeeklyPoc)
                    DrawHLine(w.Poc, PriorWeekColor, PriorWeekPocStyle, 1, PriorWeekOpacity, leftX, wallRightX,
                        chartScale.GetYByValue(w.Poc), ShowLabels ? pfx + "POC" + PriceSuffix(w.Poc) : null, true, LabelRankEnum.Poc, true);
                if (ShowWeeklyVA && w.Vah > w.Val)
                {
                    DrawHLine(w.Vah, PriorWeekColor, PriorWeekVaStyle, 1, PriorWeekOpacity, leftX, wallRightX,
                        chartScale.GetYByValue(w.Vah), ShowLabels ? pfx + "VAH" + PriceSuffix(w.Vah) : null, false, LabelRankEnum.Htf, true);
                    DrawHLine(w.Val, PriorWeekColor, PriorWeekVaStyle, 1, PriorWeekOpacity, leftX, wallRightX,
                        chartScale.GetYByValue(w.Val), ShowLabels ? pfx + "VAL" + PriceSuffix(w.Val) : null, false, LabelRankEnum.Htf, true);
                }
            }

            // ---- Developing (current) month POC ----
            if (ShowMonthlyPoc && !double.IsNaN(devMonthPoc))
                DrawHLine(devMonthPoc, MonthlyPocColor, MonthlyPocStyle, MonthlyThickness, MonthlyOpacity, leftX, wallRightX,
                    chartScale.GetYByValue(devMonthPoc), ShowLabels ? "mPOC" + PriceSuffix(devMonthPoc) : null, true, LabelRankEnum.Poc);

            // ---- Prior months (static) ----
            var pm = priorMonthsSnap;
            int showM = Math.Min(PriorMonthsToShow, pm.Length);
            for (int n = 0; n < showM; n++)
            {
                var m = pm[n];
                string pfx = n == 0 ? "pm" : "pm" + (n + 1);
                if (ShowMonthlyPoc)
                    DrawHLine(m.Poc, PriorMonthColor, PriorMonthStyle, 1, PriorMonthOpacity, leftX, wallRightX,
                        chartScale.GetYByValue(m.Poc), ShowLabels ? pfx + "POC" + PriceSuffix(m.Poc) : null, true, LabelRankEnum.Poc, true);
            }
        }

        private void DrawValueArea(ChartScale chartScale, float leftX, float wallRightX)
        {
            if (curVah <= curVal) return;
            float yH = chartScale.GetYByValue(curVah);
            float yL = chartScale.GetYByValue(curVal);
            DrawHLine(curVah, VaColor, VaStyle, 1, VaOpacity, leftX, wallRightX, yH, ShowLabels ? "VAH" + PriceSuffix(curVah) : null, true, LabelRankEnum.ValueArea);
            DrawHLine(curVal, VaColor, VaStyle, 1, VaOpacity, leftX, wallRightX, yL, ShowLabels ? "VAL" + PriceSuffix(curVal) : null, true, LabelRankEnum.ValueArea);
        }

        private void DrawPriorSessions(ChartScale chartScale, float leftX, float wallRightX)
        {
            var snap = priorVASnap;
            int show = Math.Min(PriorSessionsToShow, snap.Length);
            for (int n = 0; n < show; n++)
            {
                var s = snap[snap.Length - 1 - n];
                int age = n + 1;   // 1 = prior day
                string pfx = age == 1 ? "pd" : "pd" + age;
                float yH = chartScale.GetYByValue(s.Vah);
                float yL = chartScale.GetYByValue(s.Val);
                float yP = chartScale.GetYByValue(s.Poc);
                DrawHLine(s.Vah, PriorVaColor, PriorDayVaStyle, 1, PriorVaOpacity, leftX, wallRightX, yH, ShowLabels ? pfx + "VAH" + PriceSuffix(s.Vah) : null);
                DrawHLine(s.Val, PriorVaColor, PriorDayVaStyle, 1, PriorVaOpacity, leftX, wallRightX, yL, ShowLabels ? pfx + "VAL" + PriceSuffix(s.Val) : null);
                DrawHLine(s.Poc, PriorVaColor, PriorDayPocStyle, 1, PriorVaOpacity, leftX, wallRightX, yP, ShowLabels ? pfx + "POC" + PriceSuffix(s.Poc) : null, true, LabelRankEnum.Poc);

                if (ShowPriorHL && s.High > s.Low)
                {
                    float yHi = chartScale.GetYByValue(s.High);
                    float yLo = chartScale.GetYByValue(s.Low);
                    // Only the immediately prior day's extremes are promoted. pd2H / pd3H are history,
                    // not targets, and shouldn't outrank a live CPR just because they share a family.
                    LabelRankEnum hlRank = age == 1 ? LabelRankEnum.Extreme : LabelRankEnum.Reference;
                    DrawHLine(s.High, PriorVaColor, PriorDayHLStyle, 1, PriorVaOpacity, leftX, wallRightX, yHi, ShowLabels ? pfx + "H" + PriceSuffix(s.High) : null, false, hlRank);
                    DrawHLine(s.Low, PriorVaColor, PriorDayHLStyle, 1, PriorVaOpacity, leftX, wallRightX, yLo, ShowLabels ? pfx + "L" + PriceSuffix(s.Low) : null, false, hlRank);
                }
            }
        }

        // exempt = never folded into an averaged merge LINE (POC variants, current VAH/VAL, etc.).
        // Note this no longer has anything to do with labels: since grouping never moves a line, an
        // exempt level can safely share a label chip with anything else on its row.
        private void DrawHLine(double price, Brush wpf, DashStyleHelper style, int thickness, int opacity,
                               float x0, float x1, float y, string label,
                               bool exempt = false, LabelRankEnum rank = LabelRankEnum.Reference, bool dim = false)
        {
            if (price <= 0) return;
            if (MergeLevels && !exempt)
            {
                pendingLevels.Add(new PendingLevel
                {
                    Price = price, Wpf = wpf, Style = style, Thickness = thickness,
                    Opacity = opacity, X0 = x0, X1 = x1, Label = label, Rank = rank, Dim = dim
                });
                return;
            }
            DrawHLineNow(price, wpf, style, thickness, opacity, x0, x1, y, label, rank, dim);
        }

        private void DrawHLineNow(double price, Brush wpf, DashStyleHelper style, int thickness, int opacity,
                                  float x0, float x1, float y, string label,
                                  LabelRankEnum rank = LabelRankEnum.Reference, bool dim = false)
        {
            if (price <= 0) return;
            if (LineVisible(price, y))
                DrawRailLine(wpf, style, thickness, opacity, x0, x1, y);
            if (label != null)
                QueueGutterLabel(price, y, x1 + 4f, StripPriceSuffix(label), wpf, Math.Max(60, opacity), rank, dim);
        }

        // Clusters the collected mergeable levels: any within Merge Distance of the cluster's lowest member
        // collapse to one line at the average price, with the member tags joined (capped at 3, then +N).
        private void FlushMergedLevels(ChartScale chartScale, float wallRightX)
        {
            if (pendingLevels.Count == 0) return;
            double thr = MergeDistanceTicks * (TickSize > 0 ? TickSize : 1.0);
            pendingLevels.Sort((a, b) => a.Price.CompareTo(b.Price));

            int i = 0;
            while (i < pendingLevels.Count)
            {
                int j = i + 1;
                while (j < pendingLevels.Count && pendingLevels[j].Price - pendingLevels[i].Price <= thr) j++;
                int count = j - i;

                if (count == 1)
                {
                    var p = pendingLevels[i];
                    DrawHLineNow(p.Price, p.Wpf, p.Style, p.Thickness, p.Opacity, p.X0, p.X1, chartScale.GetYByValue(p.Price), p.Label, p.Rank, p.Dim);
                }
                else
                {
                    double sum = 0;
                    var tags = new List<string>();
                    float x0 = pendingLevels[i].X0;
                    for (int k = i; k < j; k++)
                    {
                        sum += pendingLevels[k].Price;
                        string t = StripPriceSuffix(pendingLevels[k].Label);
                        if (!string.IsNullOrEmpty(t)) tags.Add(t);
                    }
                    double avg = sum / count;
                    float y = chartScale.GetYByValue(avg);

                    string label = null;
                    if (ShowLabels && tags.Count > 0)
                    {
                        string joined = tags.Count <= 3
                            ? string.Join(" / ", tags)
                            : string.Join(" / ", tags.GetRange(0, 3)) + " +" + (tags.Count - 3);
                        label = joined + PriceSuffix(avg);
                    }

                    // Averaged line, so it draws in the neutral merge colour - but it is the LEAST
                    // informative thing on the row, so it never wins a label chip over a real level.
                    Brush mc = MergeColor ?? Brushes.Gray;
                    DrawHLineNow(avg, mc, DashStyleHelper.Solid, 1, MergeOpacity, x0, wallRightX, y, label, LabelRankEnum.Merged, false);
                }
                i = j;
            }
            pendingLevels.Clear();
        }

        private static string StripPriceSuffix(string label)
        {
            if (string.IsNullOrEmpty(label)) return label;
            int sp = label.LastIndexOf(' ');
            if (sp > 0)
            {
                double d;
                if (double.TryParse(label.Substring(sp + 1), out d)) return label.Substring(0, sp);
            }
            return label;
        }

        private float LeftXForVol(double vol, float wallRightX)
        {
            double frac = vol / profPocVol;
            if (frac < 0) frac = 0;
            if (frac > 1) frac = 1;
            return wallRightX - (float)(frac * WallMaxDepth);
        }

        private void DrawWall(ChartScale chartScale, float wallRightX)
        {
            // Wall Gradient: a heat-map UNDERLAY, layered beneath the wall exactly like the HVN
            // zones are - per-row rectangles spanning the wall's footprint (plus the optional bar
            // extension). It does NOT replace the fill: the normal wall (flat / VA coloring /
            // Delta Split / polarity tint) draws on top, so the terrain silhouette stays fully
            // visible and the heat reads as terrain shading behind it.
            if (WallGradient && profRowCount > 0)
                DrawWallGradient(chartScale, wallRightX);

            // Delta Split takes top precedence among the fill modes: same silhouette, each row
            // partitioned into buy/sell segments sized by the classified split (magnitude AND
            // lean; the tint shows lean only).
            if (WallDeltaSplit && profRowCount > 0 && profBuyRowVol.Length == profRowCount)
            {
                DrawWallDeltaSplit(chartScale, wallRightX);
                return;
            }

            // VA-membership colouring next (the outer strip now carries buy/sell polarity).
            if (WallVaColoring && profRowCount > 0 && curVah > curVal)
            {
                DrawWallVa(chartScale, wallRightX);
                return;
            }

            if (WallPolarity && profRowCount > 0 && profBuyRowVol.Length == profRowCount)
            {
                DrawWallPolarity(chartScale, wallRightX);
                return;
            }

            Brush wpf = WallColor ?? Brushes.Sienna;
            try
            {
                var dx = AcquireBrush(wpf, Clamp01(WallOpacity));

                if (WallStyle == WallStyleEnum.Stepped)
                {
                    var rv = profRowVol;
                    int nRows = Math.Min(profRowCount, rv.Length);   // clamp: publish race can't overrun
                    for (int r = 0; r < nRows; r++)
                    {
                        if (rv[r] <= 0) continue;
                        float lx = LeftXForVol(rv[r], wallRightX);
                        float yTop = chartScale.GetYByValue(profLow + (r + 1) * profRowSize);
                        float yBot = chartScale.GetYByValue(profLow + r * profRowSize);
                        float h = yBot - yTop;
                        if (h < 1f) h = 1f;
                        RenderTarget.FillRectangle(new SharpDX.RectangleF(lx, yTop, wallRightX - lx, h), dx);
                    }
                }
                else
                {
                    FillSmoothWall(chartScale, wallRightX, dx);
                }
            }
            catch { }
        }

        // Builds and fills the smooth terrain silhouette with the given brush (reused for VA two-toning).
        private void FillSmoothWall(ChartScale chartScale, float wallRightX, SharpDX.Direct2D1.Brush dx)
        {
            SharpDX.Direct2D1.PathGeometry geo = null;
            try
            {
                geo = BuildWallGeometry(chartScale, wallRightX);
                RenderTarget.FillGeometry(geo, dx);
            }
            finally
            {
                geo?.Dispose();
            }
        }

        // The smooth terrain silhouette as a closed path (right edge sealed). Byte-for-byte the same
        // contour FillSmoothWall has always drawn; the gradient also uses it as a geometric clip so
        // its per-row heat strips live inside the exact same smooth outline. Caller disposes.
        private SharpDX.Direct2D1.PathGeometry BuildWallGeometry(ChartScale chartScale, float wallRightX)
        {
            float yTop = chartScale.GetYByValue(profHigh);
            float yBot = chartScale.GetYByValue(profLow);
            var geo = new SharpDX.Direct2D1.PathGeometry(RenderTarget.Factory);
            SharpDX.Direct2D1.GeometrySink sink = null;
            try
            {
                sink = geo.Open();
                sink.BeginFigure(new SharpDX.Vector2(wallRightX, yTop), SharpDX.Direct2D1.FigureBegin.Filled);
                sink.AddLine(new SharpDX.Vector2(wallRightX, yBot));
                var rv = profRowVol;
                int nRows = Math.Min(profRowCount, rv.Length);
                for (int r = 0; r < nRows; r++)
                {
                    float lx = LeftXForVol(rv[r], wallRightX);
                    float y = chartScale.GetYByValue(ProfRowCenter(r));
                    sink.AddLine(new SharpDX.Vector2(lx, y));
                }
                sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
                sink.Close();
            }
            finally
            {
                sink?.Dispose();
            }
            return geo;
        }

        // Wall Gradient: a continuous heat-map UNDERLAY behind the wall, driven by profGradScore
        // (see ComputeGradientScores - no detection, no thresholds, no bands). It renders exactly
        // where an at-the-wall HVN zone renders - full-width row rectangles spanning the wall's
        // footprint (wallRightX - WallMaxDepth .. wallRightX) - with the normal wall drawn on top.
        //   score -> distance from the median (d = |s - 0.5| * 2): 0 at neutral, 1 at the extremes.
        //   FOOTPRINT : alpha = MinOp + (MaxOp - MinOp) * d. Default MinOp = 0, so neutral rows
        //               vanish and only meaningful red/green terrain shades the footprint; raise
        //               MinOp for a faint full-band tint.
        //   EXTENSION : each row's color also projects out to GradientExtendBars left of the wall
        //               over the price action, alpha = MaxOp * d * ExtendOpacity (never floored).
        //               Extension length is deliberately a SEPARATE setting from Line Projection
        //               (bars): set levels longer (e.g. 40 vs 30) and a level nestled in an LVN
        //               sticks out past the green instead of drowning in it.
        //   ACROSS    : with GradientAcrossChart on, the color keeps running to the left edge of the
        //               canvas past the extension's stopping point, alpha = MaxOp * d * AcrossOpacity.
        //               Its slider is on the SAME scale as ExtendOpacity (both are a % of the row's
        //               footprint alpha) and the two do not compound - so 100 on either reaches full
        //               strength, and the across band can legitimately be set brighter than the
        //               extension if the terrain reads better that way.
        //   PROXIMITY : both bands ignore Proximity Reveal (09. Levels) by default. The heat map is
        //               terrain, not a level line - it describes where volume IS, not where price is,
        //               and blinking it in and out as price wanders reads as noise. Turn on
        //               GradientObeyProximity to restore the level-line contract (a row only shades
        //               while the live price is within Reveal Distance of it).
        // Unlike the first cut (which replaced the fill and clipped the heat to the silhouette,
        // rendering LVN rows as invisible slivers), full-width rows give HVNs and LVNs equal visual
        // weight - the thin green rows you most want to see are exactly the ones the silhouette
        // clip erased. Two cached DX brushes reused with per-row opacity - no per-row allocations.
        private void DrawWallGradient(ChartScale chartScale, float wallRightX)
        {
            var rv = profRowVol;
            var gs = profGradScore;
            int nRows = Math.Min(profRowCount, Math.Min(rv.Length, gs.Length));
            if (nRows <= 0) return;   // scores not published yet; the wall itself still draws

            float maxOp = Clamp01(GradientMaxOpacity);
            float minOp = Clamp01(GradientMinOpacity);
            if (minOp > maxOp) minOp = maxOp;
            float extFrac = Clamp01(GradientExtendOpacity);

            // Sensitivity -> gamma on the median-distance ramp. 50 = linear (gamma 1). Higher
            // sensitivity bends the curve so color builds quickly as rows leave the neutral middle
            // (more of the profile visibly shaded); lower sensitivity starves everything but the
            // true extremes. Mapped exponentially: 100 -> gamma 0.25, 0 -> gamma 4.
            double sens = GradientSensitivity; if (sens < 0) sens = 0; if (sens > 100) sens = 100;
            double gamma = Math.Pow(4.0, (50.0 - sens) / 50.0);

            // Crossover -> the score at which red flips to green (the HVN/LVN classification
            // boundary). 50 = the middle of the score range (original behavior). RAISE it and rows
            // must score higher to qualify as red - thin shelves that were being tagged HVN drop
            // into green. LOWER it and red reaches further down the profile. Each side's ramp is
            // renormalized to its own span, so full-opacity red still lands on the POC and
            // full-opacity green on the thinnest row wherever the boundary sits.
            float cross = GradientCrossover / 100f;
            if (cross < 0.05f) cross = 0.05f;
            if (cross > 0.95f) cross = 0.95f;

            try
            {
                var hvnDx = AcquireBrush(GradientHvnColor ?? Brushes.IndianRed, maxOp);
                var lvnDx = AcquireBrush(GradientLvnColor ?? Brushes.MediumSeaGreen, maxOp);

                float gLeft = wallRightX - WallMaxDepth;             // wall footprint left edge
                if (gLeft < renderCanvasLeft) gLeft = renderCanvasLeft;

                // Three bands, right to left. Each has its OWN alpha, expressed as a percentage of the
                // row's footprint alpha - they do not compound:
                //   gLeft   .. wallRightX  FOOTPRINT  (under the wall)         alpha = minOp + (maxOp-minOp)*d
                //   extLeft .. gLeft       EXTENSION  (over the price action)  alpha = maxOp * d * extFrac
                //   canvas  .. extLeft     ACROSS     (rest of the chart)      alpha = maxOp * d * acrossFrac
                // extLeftX is the extension's stopping point; with Extend (bars) = 0 it collapses onto
                // the footprint edge, so Across Chart alone shades the whole canvas at the across alpha.
                float extLeftX = gLeft;
                if (GradientExtendBars > 0 && renderBarDist > 0.5f)
                    extLeftX = wallRightX - GradientExtendBars * renderBarDist;
                if (extLeftX < renderCanvasLeft) extLeftX = renderCanvasLeft;
                if (extLeftX > gLeft) extLeftX = gLeft;              // never right of the footprint

                float acrossLeftX = GradientAcrossChart ? renderCanvasLeft : extLeftX;
                if (acrossLeftX > extLeftX) acrossLeftX = extLeftX;

                float acrossFrac = Clamp01(GradientAcrossOpacity);
                bool wantBright = extFrac > 0f && extLeftX < gLeft - 1f;
                bool wantAcross = acrossFrac > 0f && acrossLeftX < extLeftX - 1f;
                bool anyExt = wantBright || wantAcross;

                for (int r = 0; r < nRows; r++)
                {
                    float s = gs[r];
                    if (s < 0f) continue;                            // untouched row: draw nothing
                    bool isHvn = s >= cross;
                    float raw = isHvn ? (s - cross) / (1f - cross)   // each side renormalized to its own span:
                                      : (cross - s) / cross;         // 0 at the boundary, 1 at POC / thinnest row
                    float d = (float)Math.Pow(raw, gamma);           // bent by sensitivity
                    var dx = isHvn ? hvnDx : lvnDx;

                    float yTop = chartScale.GetYByValue(profLow + (r + 1) * profRowSize);
                    float yBot = chartScale.GetYByValue(profLow + r * profRowSize);
                    float h = yBot - yTop; if (h < 1f) h = 1f;

                    // Footprint band (under the wall).
                    float a = minOp + (maxOp - minOp) * d;
                    if (a >= 0.005f)
                    {
                        dx.Opacity = a;
                        RenderTarget.FillRectangle(new SharpDX.RectangleF(gLeft, yTop, wallRightX - gLeft, h), dx);
                    }

                    // Extension over the price action. The gradient is terrain, not a level line, so by
                    // default it ignores Proximity Reveal and stays painted wherever price is. Turn on
                    // Gradient Obeys Proximity to restore the level-line contract (a row only shades
                    // while the live price is within Reveal Distance of it).
                    if (!anyExt) continue;
                    if (GradientObeyProximity && ProximityReveal
                        && Math.Abs(renderLastPrice - ProfRowCenter(r)) > RevealDistance) continue;

                    if (wantBright)
                    {
                        float ea = maxOp * d * extFrac;              // never floored: neutral rows stay off the candles
                        if (ea >= 0.01f)
                        {
                            dx.Opacity = ea;
                            RenderTarget.FillRectangle(new SharpDX.RectangleF(extLeftX, yTop, gLeft - extLeftX, h), dx);
                        }
                    }

                    // Past the extension's stopping point. Its alpha is its OWN percentage of the row's
                    // footprint alpha - NOT a further knock-down of the extension - so the slider means
                    // the same thing on both bands and 100 reaches full strength on either. Compounding
                    // the two (across = extension * across) starved the band: at ExtendOpacity 40 the
                    // across band topped out at 40% of maxOp even with its own slider pinned at 100.
                    if (wantAcross)
                    {
                        float aa = maxOp * d * acrossFrac;
                        if (aa >= 0.01f)
                        {
                            dx.Opacity = aa;
                            RenderTarget.FillRectangle(new SharpDX.RectangleF(acrossLeftX, yTop, extLeftX - acrossLeftX, h), dx);
                        }
                    }
                }
            }
            catch { }
        }

        // Delta Split: the wall keeps its exact silhouette (row width = total volume) but each row is
        // partitioned into a buy segment and a sell segment sized by the classified split. A 5k x 5k
        // row reads as a long half/half bar; a 50 x 50 row reads as a stub - magnitude the tint can't
        // show. Uses the same profBuyRowVol data as the tint (true bid/ask delta when tick flow is
        // live, OHLC proxy otherwise), so its meaning tracks the Delta Source exactly. Renders
        // per-row by construction; in Smooth style the terrain silhouette is stroked on top so the
        // terrain read survives (DeltaSplitOutline).
        private void DrawWallDeltaSplit(ChartScale chartScale, float wallRightX)
        {
            var rv = profRowVol;
            var brv = profBuyRowVol;
            int nRows = Math.Min(profRowCount, Math.Min(rv.Length, brv.Length));
            if (nRows <= 0) return;

            try
            {
                float op = Clamp01(WallOpacity);
                var buyDx = AcquireBrush(WallBuyColor ?? Brushes.Teal, op);
                var sellDx = AcquireBrush(WallSellColor ?? Brushes.IndianRed, op);

                for (int r = 0; r < nRows; r++)
                {
                    double tot = rv[r];
                    if (tot <= 0) continue;
                    double buy = brv[r];
                    if (buy < 0) buy = 0; else if (buy > tot) buy = tot;

                    float lx = LeftXForVol(tot, wallRightX);   // full-row width: silhouette unchanged
                    float w = wallRightX - lx;
                    if (w <= 0f) continue;
                    float yTop = chartScale.GetYByValue(profLow + (r + 1) * profRowSize);
                    float yBot = chartScale.GetYByValue(profLow + r * profRowSize);
                    float h = yBot - yTop;
                    if (h < 1f) h = 1f;

                    float buyW = (float)(w * (buy / tot));
                    float innerW = DeltaSplitBuysInner ? buyW : w - buyW;
                    var innerDx = DeltaSplitBuysInner ? buyDx : sellDx;
                    var outerDx = DeltaSplitBuysInner ? sellDx : buyDx;

                    if (innerW > 0.4f)
                        RenderTarget.FillRectangle(new SharpDX.RectangleF(wallRightX - innerW, yTop, innerW, h), innerDx);
                    float outerW = w - innerW;
                    if (outerW > 0.4f)
                        RenderTarget.FillRectangle(new SharpDX.RectangleF(lx, yTop, outerW, h), outerDx);
                }

                if (WallStyle == WallStyleEnum.Smooth && DeltaSplitOutline)
                    OutlineSmoothWall(chartScale, wallRightX);
            }
            catch { }
        }

        // Strokes the smooth terrain face (no fill) - drawn over the delta split so the Smooth wall
        // read is preserved even though the split itself renders per-row.
        private void OutlineSmoothWall(ChartScale chartScale, float wallRightX)
        {
            SharpDX.Direct2D1.PathGeometry geo = null;
            SharpDX.Direct2D1.GeometrySink sink = null;
            try
            {
                var dx = AcquireBrush(WallColor ?? Brushes.Sienna, Clamp01(Math.Min(100, WallOpacity + 20)));
                geo = new SharpDX.Direct2D1.PathGeometry(RenderTarget.Factory);
                sink = geo.Open();
                sink.BeginFigure(new SharpDX.Vector2(wallRightX, chartScale.GetYByValue(profLow)),
                    SharpDX.Direct2D1.FigureBegin.Hollow);
                var rv = profRowVol;
                int nRows = Math.Min(profRowCount, rv.Length);
                for (int r = 0; r < nRows; r++)
                {
                    float lx = LeftXForVol(rv[r], wallRightX);
                    float y = chartScale.GetYByValue(ProfRowCenter(r));
                    sink.AddLine(new SharpDX.Vector2(lx, y));
                }
                sink.AddLine(new SharpDX.Vector2(wallRightX, chartScale.GetYByValue(profHigh)));
                sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Open);
                sink.Close();
                RenderTarget.DrawGeometry(geo, dx, 1.25f);
            }
            catch { }
            finally
            {
                sink?.Dispose();
                geo?.Dispose();
            }
        }

        // Colour the wall by value-area membership: rows inside [VAL,VAH] one colour, outside another.
        private void DrawWallVa(ChartScale chartScale, float wallRightX)
        {
            Brush inWpf = WallInVaColor ?? WallColor ?? Brushes.Sienna;
            Brush outWpf = WallOutVaColor ?? WallColor ?? Brushes.Gray;
            try
            {
                var dxIn = AcquireBrush(inWpf, Clamp01(WallOpacity));
                var dxOut = AcquireBrush(outWpf, Clamp01(WallOpacity));

                if (WallStyle == WallStyleEnum.Stepped)
                {
                    var rv = profRowVol;
                    int nRows = Math.Min(profRowCount, rv.Length);
                    for (int r = 0; r < nRows; r++)
                    {
                        if (rv[r] <= 0) continue;
                        float lx = LeftXForVol(rv[r], wallRightX);
                        float yTop = chartScale.GetYByValue(profLow + (r + 1) * profRowSize);
                        float yBot = chartScale.GetYByValue(profLow + r * profRowSize);
                        float h = yBot - yTop;
                        if (h < 1f) h = 1f;
                        double c = ProfRowCenter(r);
                        var dx = (c >= curVal && c <= curVah) ? dxIn : dxOut;
                        RenderTarget.FillRectangle(new SharpDX.RectangleF(lx, yTop, wallRightX - lx, h), dx);
                    }
                }
                else
                {
                    // Smooth: fill the whole silhouette out-of-VA, then clip to the VA band and refill in-VA.
                    FillSmoothWall(chartScale, wallRightX, dxOut);
                    float yVah = chartScale.GetYByValue(curVah);
                    float yVal = chartScale.GetYByValue(curVal);
                    float top = Math.Min(yVah, yVal), bot = Math.Max(yVah, yVal);
                    if (bot - top >= 1f)
                    {
                        RenderTarget.PushAxisAlignedClip(new SharpDX.RectangleF(0, top, wallRightX, bot - top), SharpDX.Direct2D1.AntialiasMode.Aliased);
                        FillSmoothWall(chartScale, wallRightX, dxIn);
                        RenderTarget.PopAxisAlignedClip();
                    }
                }
            }
            catch { }
        }

        // Polarity wall: per-row slabs tinted by the row's buy/sell lean (OHLC proxy). Rendered stepped
        // regardless of WallStyle, since per-row colour requires per-row fills. A small bucketed palette
        // is built once per frame so we are not allocating a brush per row.
        private void DrawWallPolarity(ChartScale chartScale, float wallRightX)
        {
            const int Buckets = 21;
            float op = Clamp01(WallOpacity);
            try
            {
                var palette = GetPolarityPalette();   // cached; opacity set per use
                var rv = profRowVol;
                var brv = profBuyRowVol;
                int nRows = Math.Min(profRowCount, Math.Min(rv.Length, brv.Length));

                bool cvd = DeltaSource == DeltaSourceEnum.Cvd;
                bool cvdOk = cvd && cvdRowPol.Length == profRowCount;

                for (int r = 0; r < nRows; r++)
                {
                    double tot = rv[r];
                    if (tot <= 0) continue;
                    double pol = cvd ? (cvdOk ? cvdRowPol[r] : 0.0)      // CVD net (neutral when no map)
                                     : 2.0 * brv[r] / tot - 1.0;         // buy-fraction proxy/true, -1..+1
                    int bucket = (int)Math.Round((pol + 1.0) * 0.5 * (Buckets - 1));
                    if (bucket < 0) bucket = 0; if (bucket > Buckets - 1) bucket = Buckets - 1;

                    float lx = LeftXForVol(tot, wallRightX);
                    float yTop = chartScale.GetYByValue(profLow + (r + 1) * profRowSize);
                    float yBot = chartScale.GetYByValue(profLow + r * profRowSize);
                    float h = yBot - yTop;
                    if (h < 1f) h = 1f;
                    var pb = palette[bucket];
                    pb.Opacity = op;
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(lx, yTop, wallRightX - lx, h), pb);
                }
            }
            catch { }
        }

        // Solid full-opacity polarity ribbon pinned to the wall's right edge. Unlike the in-wall tint
        // (which fights the wall's transparency and thin tails), the strip gives the colour real area
        // to read: grey where balanced, vivid buy/sell where the row leans.
        private void DrawPolarityStrip(ChartScale chartScale, float wallRightX)
        {
            var rv = profRowVol;
            var brv = profBuyRowVol;
            int nRows = Math.Min(profRowCount, Math.Min(rv.Length, brv.Length));
            if (nRows <= 0) return;

            const int Buckets = 21;
            float w = Math.Max(2, PolarityStripWidth);
            float x0 = wallRightX - w;
            try
            {
                var palette = GetPolarityPalette();
                bool cvd = DeltaSource == DeltaSourceEnum.Cvd;
                bool cvdOk = cvd && cvdRowPol.Length == profRowCount;

                for (int r = 0; r < nRows; r++)
                {
                    double tot = rv[r];
                    if (tot <= 0) continue;
                    double pol = cvd ? (cvdOk ? cvdRowPol[r] : 0.0)
                                     : 2.0 * brv[r] / tot - 1.0;
                    int bucket = (int)Math.Round((pol + 1.0) * 0.5 * (Buckets - 1));
                    if (bucket < 0) bucket = 0; if (bucket > Buckets - 1) bucket = Buckets - 1;

                    float yTop = chartScale.GetYByValue(profLow + (r + 1) * profRowSize);
                    float yBot = chartScale.GetYByValue(profLow + r * profRowSize);
                    float h = yBot - yTop;
                    if (h < 1f) h = 1f;
                    var pb = palette[bucket];
                    pb.Opacity = 1f;
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(x0, yTop, w, h), pb);
                }
            }
            catch { }
        }

        private static System.Windows.Media.Color BrushColor(Brush b, System.Windows.Media.Color fallback)
        {
            var scb = b as SolidColorBrush;
            return scb != null ? scb.Color : fallback;
        }

        private static System.Windows.Media.Color LerpColor(System.Windows.Media.Color a, System.Windows.Media.Color b, double t)
        {
            if (t < 0) t = 0; if (t > 1) t = 1;
            return System.Windows.Media.Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        // Neutral midpoint at balance, lerping toward buy/sell colours past the deadzone. The neutral
        // is a true grey by default so both directions read clearly; set PolarityNeutralColor to the
        // wall colour for a subtler warm-anchored version.
        private System.Windows.Media.Color ColorForPolarity(double p)
        {
            double dz = Math.Max(0.0, Math.Min(0.9, PolarityDeadzone));
            var neutral = BrushColor(PolarityNeutralColor, System.Windows.Media.Color.FromRgb(80, 84, 92));
            if (p > dz)
                return LerpColor(neutral, BrushColor(WallBuyColor, System.Windows.Media.Color.FromRgb(45, 212, 191)), (p - dz) / (1.0 - dz));
            if (p < -dz)
                return LerpColor(neutral, BrushColor(WallSellColor, System.Windows.Media.Color.FromRgb(239, 83, 80)), (-p - dz) / (1.0 - dz));
            return neutral;
        }

        // Discrete 3-state colour for rail polarity dots (clearer than a gradient at dot size).
        private System.Windows.Media.Color PolarityDotColor(double p)
        {
            double dz = Math.Max(0.0, Math.Min(0.9, PolarityDeadzone));
            if (p > dz) return BrushColor(WallBuyColor, System.Windows.Media.Color.FromRgb(45, 212, 191));
            if (p < -dz) return BrushColor(WallSellColor, System.Windows.Media.Color.FromRgb(239, 83, 80));
            return System.Windows.Media.Color.FromRgb(150, 150, 150);
        }

        private void DrawPolarityDot(double polarity, float cx, float cy)
        {
            try
            {
                var dot = AcquireColorBrush(PolarityDotColor(polarity));
                dot.Opacity = 1f;
                RenderTarget.FillEllipse(new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(cx, cy), 3f, 3f), dot);
            }
            catch { }
        }

        private static bool GhostHasBins(SessionVA s)
        {
            return s != null && s.Bins != null && s.Bins.Length > 0 && s.BinPeak > 0 && s.BinSize > 0 && s.Vah > s.Val;
        }

        // Pick which prior session to ghost: the most-recent retained session whose value area contains
        // the current price. Sticky - once chosen it holds until price leaves its VA by the hysteresis
        // buffer, so overlapping sessions don't flicker; then the most-recent container is re-selected.
        private SessionVA SelectGhostSession()
        {
            double px = renderLastPrice;
            double hyst = GhostHysteresisTicks * (TickSize > 0 ? TickSize : 1.0);

            var snap = priorVASnap;
            if (ghostSel != null && Array.IndexOf(snap, ghostSel) >= 0 && GhostHasBins(ghostSel)
                && px >= ghostSel.Val - hyst && px <= ghostSel.Vah + hyst)
                return ghostSel;

            ghostSel = null;
            for (int i = snap.Length - 1; i >= 0; i--)
            {
                var s = snap[i];
                if (!GhostHasBins(s)) continue;
                if (px >= s.Val && px <= s.Vah) { ghostSel = s; return s; }
            }
            return null;
        }

        // Dim silhouette of a prior session's value-area profile, floated left of price in screen space
        // (viewport-anchored, so it sits the same across timeframes/zoom). Shows the most-recent prior
        // session whose value area currently contains price - the retrace-into-structure context - and
        // nothing when price is outside every retained session's VA. Mirrors the wall's grammar: spine
        // at the right, volume extends left.
        private void DrawGhostSilhouette(ChartScale chartScale, float canvasLeft, float wallRightX)
        {
            var s = SelectGhostSession();
            if (s == null) return;

            float span = wallRightX - canvasLeft;
            float anchorX = wallRightX - (float)GhostPosition * span;        // spine / baseline
            float avail = GhostFaceRight ? (wallRightX - anchorX - 6f) : (anchorX - canvasLeft - 6f);
            float maxDepth = Math.Min(GhostWidthPx, avail);
            if (maxDepth < 8f) return;

            Brush wpf = GhostColor ?? Brushes.Gray;
            try
            {
                var dx = AcquireBrush(wpf, Clamp01(GhostOpacity));
                double invPeak = 1.0 / s.BinPeak;
                for (int r = 0; r < s.Bins.Length; r++)
                {
                    double v = s.Bins[r];
                    if (v <= 0) continue;
                    float depth = (float)(v * invPeak * maxDepth);
                    if (depth < 1f) continue;
                    float yTop = chartScale.GetYByValue(s.BinLow + (r + 1) * s.BinSize);
                    float yBot = chartScale.GetYByValue(s.BinLow + r * s.BinSize);
                    float h = yBot - yTop;
                    if (h < 1f) h = 1f;
                    float x = GhostFaceRight ? anchorX : anchorX - depth;
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(x, yTop, depth, h), dx);
                }
            }
            catch { }

            float xL = GhostFaceRight ? anchorX : anchorX - maxDepth;
            float xR = GhostFaceRight ? anchorX + maxDepth : anchorX;

            // POC line within the silhouette (top/bottom are understood as VAH/VAL).
            if (s.Poc > 0)
                DrawRailLine(wpf, DashStyleHelper.Solid, 1, Math.Min(70, GhostOpacity * 3 + 10), xL, xR, chartScale.GetYByValue(s.Poc));

            // Session date just above the silhouette (above VAH).
            if (ShowLabels)
                DrawText(s.Day.ToString("M/d"), wpf, Math.Min(80, GhostOpacity * 4 + 12), xL, chartScale.GetYByValue(s.Vah) - 14f);
        }

        // The ghost session's VAH/VAL/POC as full reference levels - same treatment (proximity reveal +
        // persistent labels) as every other level. Labelled with the session date so the source is clear.
        private void DrawGhostLevels(ChartScale chartScale, float leftX, float wallRightX)
        {
            var s = SelectGhostSession();
            if (s == null || s.Vah <= s.Val) return;

            Brush c = GhostLevelColor ?? Brushes.Goldenrod;
            string d = s.Day.ToString("M/d");
            DrawHLine(s.Vah, c, DashStyleHelper.Dash, 1, GhostLevelOpacity, leftX, wallRightX, chartScale.GetYByValue(s.Vah), ShowLabels ? d + " VAH" + PriceSuffix(s.Vah) : null, false, LabelRankEnum.Reference, true);
            DrawHLine(s.Poc, c, DashStyleHelper.Solid, 1, GhostLevelOpacity, leftX, wallRightX, chartScale.GetYByValue(s.Poc), ShowLabels ? d + " POC" + PriceSuffix(s.Poc) : null, true, LabelRankEnum.Poc, true);
            DrawHLine(s.Val, c, DashStyleHelper.Dash, 1, GhostLevelOpacity, leftX, wallRightX, chartScale.GetYByValue(s.Val), ShowLabels ? d + " VAL" + PriceSuffix(s.Val) : null, false, LabelRankEnum.Reference, true);
        }

        // Out-of-value order blocks. Detected off-thread over ObVaMinutes history; here we keep only those
        // whose box sits OUTSIDE the developing session VA and draw each as its candle edges + the tick-
        // profiled candle POC, tagged with the direction back toward the session POC. Active OBs use the
        // bull/bear color; breakers (flipped once) use the breaker color. Same live VA filter as the FRVP.
        private void DrawObOutOfVa(ChartScale chartScale, float canvasLeft, float lineLeftX, float wallRightX)
        {
            var zones = obSnap;
            if (zones.Length == 0 || curVah <= curVal || renderCC == null)
            {
                if (obHoverSnap.Length > 0) { obHoverSnap = new FrvpHover[0]; obHasHover = false; }
                return;
            }

            Brush bullWpf = ObBullColor ?? Brushes.SeaGreen;
            Brush bearWpf = ObBearColor ?? Brushes.IndianRed;
            Brush brkWpf  = ObBreakerColor ?? Brushes.Gray;
            Brush pocWpf  = ObPocColor ?? Brushes.Gold;
            int op = ObOpacity;
            try
            {
                var hoverList = ObTooltip ? new List<FrvpHover>() : null;
                ObZone hoverZone = null; double hoverOutside = 0;
                int nPassed = 0, nDrawn = 0;
                foreach (var z in zones)
                {
                    if (z.Breaker && !ObShowBreakers) continue;

                    // Out-of-value test against the DEVELOPING session VA, re-evaluated every frame.
                    double bh = z.Top - z.Bottom;
                    if (bh <= 0) continue;
                    double overlap = Math.Min(z.Top, curVah) - Math.Max(z.Bottom, curVal);
                    if (overlap < 0) overlap = 0;
                    double outsideFrac = 1.0 - (overlap / bh);
                    if (outsideFrac < 0) outsideFrac = 0;
                    bool outOfValue = outsideFrac * 100.0 >= ObOutsideMinPct;
                    if (!outOfValue && !ObShowInValue) continue;   // in-value block, and we're only showing shelves
                    nPassed++;

                    // Anchor the box at its SOURCE candle: left edge = the OB candle's time, clamped to the
                    // canvas if it formed off-screen to the left. The block extends right to the projection
                    // wall. It never draws back before the candle that created it.
                    float boxLeftX = Math.Max(canvasLeft, renderCC.GetXByTime(z.StartTime));
                    if (boxLeftX >= wallRightX - 1f) continue;   // formed at/after the wall -> nothing to draw

                    Brush edgeWpf = z.Breaker ? brkWpf : (z.IsBull ? bullWpf : bearWpf);
                    int zop = z.Breaker ? Math.Max(3, (int)Math.Round(op * 0.6)) : op;
                    if (z.Mitigated) zop = Math.Max(3, (int)Math.Round(zop * 0.4));         // tapped -> faint
                    if (!outOfValue && ObInValueDim) zop = Math.Max(3, (int)Math.Round(zop * 0.45));   // in-value -> dimmer

                    bool above = z.Poc >= curPoc;   // sits above the session POC -> price returns down to it

                    float yTop = chartScale.GetYByValue(z.Top);
                    float yBot = chartScale.GetYByValue(z.Bottom);
                    if (yTop > yBot) { float t = yTop; yTop = yBot; yBot = t; }
                    float yPoc = chartScale.GetYByValue(z.Poc);

                    bool drawBand = true;
                    if (!ObAcrossChart)
                    {
                        double px = z.Poc;
                        bool nearPrice = !ProximityReveal || Math.Abs(px - renderLastPrice) <= RevealDistance;
                        bool hovered = HoverReveal && mouseValid &&
                            ( (mousePxX >= boxLeftX - 2f && mousePxX <= wallRightX + 2f && mousePxY >= yTop - 2f && mousePxY <= yBot + 2f)
                              || (mousePxX >= wallRightX && mousePxX <= wallRightX + 90f && Math.Abs(mousePxY - yPoc) <= 8f) );
                        drawBand = nearPrice || hovered;
                    }

                    if (drawBand)
                    {
                        nDrawn++;
                        if (ObShowFill && yBot - yTop >= 1f)
                        {
                            var fillDx = AcquireBrush(edgeWpf, Clamp01(Math.Max(4, zop / 6)));
                            if (fillDx != null)
                                RenderTarget.FillRectangle(new SharpDX.RectangleF(boxLeftX, yTop, wallRightX - boxLeftX, yBot - yTop), fillDx);
                        }
                        DrawRailLine(edgeWpf, ObEdgeStyle, 1, zop, boxLeftX, wallRightX, yTop);
                        DrawRailLine(edgeWpf, ObEdgeStyle, 1, zop, boxLeftX, wallRightX, yBot);
                        DrawRailLine(pocWpf,  ObPocStyle,  1, zop, boxLeftX, wallRightX, yPoc);
                    }

                    if (ShowLabels)
                    {
                        string kind = z.Breaker ? "BRK" : "OB";
                        string flag = z.Mitigated ? "\u00b7m" : "";
                        string tag = kind + flag + (above ? " \u2193POC" : " \u2191POC");
                        QueueGutterLabel(z.Poc, yPoc, wallRightX + 4f, tag, pocWpf, zop, LabelRankEnum.Frvp, z.Breaker || z.Mitigated);
                    }

                    if (hoverList != null && drawBand)
                    {
                        // Bounded to the box itself (plus the label chip to the right). Never keyed off the
                        // POC line, so a POC anywhere can't inflate this into a full-height ghost strip.
                        hoverList.Add(new FrvpHover { L = boxLeftX - 2f, T = yTop - 2f, R = wallRightX + 92f, B = yBot + 2f, Key = z.Key });
                        if (obHasHover && z.Key == obHoveredKey) { hoverZone = z; hoverOutside = outsideFrac; }
                    }
                }

                if (ObTooltip)
                {
                    obHoverSnap = hoverList != null ? hoverList.ToArray() : new FrvpHover[0];
                    if (obHasHover && mouseValid && !frvpHasHover && !fovHasHover && hoverZone != null)
                        DrawObTooltip(mousePxX, mousePxY, hoverZone, hoverOutside);
                }

                if (ObDebug && !ReferenceEquals(obSnap, obLastDumpedSnap))
                {
                    obLastDumpedSnap = obSnap;
                    Print("OB|RENDER|inSnap=" + zones.Length + "|passedVAfilter=" + nPassed + "|drawn=" + nDrawn
                        + "|hoverRegions=" + (hoverList != null ? hoverList.Count : 0)
                        + "|acrossChart=" + ObAcrossChart + "|showLabels=" + ShowLabels
                        + "|curVA=" + curVal.ToString("F2") + "-" + curVah.ToString("F2") + "|curPOC=" + curPoc.ToString("F2"));
                }
            }
            catch { }
        }

        private void DrawObTooltip(float mx, float my, ObZone z, double outsideFrac)
        {
            try
            {
                string type = (z.IsBull ? "Bullish OB (demand)" : "Bearish OB (supply)")
                    + (z.Breaker ? " \u2013 breaker" : "") + (z.Mitigated ? " \u2013 mitigated" : "");
                string side = z.Poc >= curPoc ? "above VA" : "below VA";
                bool oov = outsideFrac * 100.0 >= ObOutsideMinPct;
                string vaLine = oov ? (outsideFrac * 100.0).ToString("F0") + "% outside VA (" + side + ")"
                                    : "inside VA (" + side + ")";
                TimeSpan age = Time[0] - z.EndTime;
                string ageStr = age.TotalDays >= 1 ? age.TotalDays.ToString("F1") + "d"
                    : (age.TotalHours >= 1 ? age.TotalHours.ToString("F1") + "h" : Math.Max(0, age.TotalMinutes).ToString("F0") + "m");

                var lines = new List<string>
                {
                    "Order Block  \u2013  " + type,
                    vaLine,
                    "POC " + z.Poc.ToString("F2") + (z.PocReady ? "" : " (profiling…)"),
                    "Box " + z.Bottom.ToString("F2") + " / " + z.Top.ToString("F2") + "    Mid " + z.Mid.ToString("F2"),
                    "Volume " + z.Volume.ToString("N0"),
                    "Formed " + z.StartTime.ToString("MM/dd HH:mm") + "    Age " + ageStr
                };
                DrawTooltipBox(mx, my, lines);
            }
            catch { }
        }

        // Out-of-value FRVP zones. Detected off-thread over 15-min history; here we keep only those whose
        // POC sits OUTSIDE the developing session VA (above VAH or below VAL) and draw each as a hollow
        // band + dashed POC, tagged with the direction back toward the session POC. The filter is applied
        // live, so a zone that falls out of value as the VA develops appears without a reload.
        private void DrawFrvpOutOfVa(ChartScale chartScale, float canvasLeft, float lineLeftX, float wallRightX)
        {
            var zones = fovSnap;
            if (zones.Length == 0 || curVah <= curVal)
            {
                // Nothing drawn this frame - drop the hover regions so a stale one can't fire a tooltip.
                if (fovHoverSnap.Length > 0) { fovHoverSnap = new FrvpHover[0]; fovHasHover = false; }
                return;
            }
            float leftX = FrvpOutOfVaAcrossChart ? canvasLeft : lineLeftX;   // across-screen vs projection width
            if (wallRightX - leftX <= 0) return;

            Brush vaWpf  = FrvpVaZoneColor ?? Brushes.SteelBlue;
            Brush pocWpf = FrvpVaPocColor ?? Brushes.Goldenrod;
            int op = FrvpVaOpacity;
            try
            {
                var fillDx = FrvpVaShowFill ? AcquireBrush(vaWpf, Clamp01(Math.Max(4, op / 6))) : null;
                var hoverList = FrvpVaTooltip ? new List<FrvpHover>() : null;
                FrvpZone hoverZone = null; double hoverOutside = 0;
                foreach (var z in zones)
                {
                    // Value-area filter, re-evaluated every frame against the DEVELOPING session VA - so a
                    // zone that was out of value this morning is dropped the moment the VA expands to
                    // swallow it. The old rule hid a zone only when it was FULLY inside (VAH <= curVah &&
                    // VAL >= curVal), which meant a zone with a single tick poking past VAL survived and
                    // read, visually, as an in-value zone. Now we measure how much of the zone's own VA
                    // height actually sits outside the session VA and require at least
                    // FrvpVaOutsideMinPct of it. Set that to 0 to restore the any-poke-counts behavior.
                    double zh = z.VAH - z.VAL;
                    if (zh <= 0) continue;
                    double overlap = Math.Min(z.VAH, curVah) - Math.Max(z.VAL, curVal);
                    if (overlap < 0) overlap = 0;
                    double outsideFrac = 1.0 - (overlap / zh);
                    if (outsideFrac <= 0) continue;                                  // fully inside value -> hidden
                    if (outsideFrac * 100.0 < FrvpVaOutsideMinPct) continue;         // not enough of it is out of value

                    // Display exactly like AutoFRVP: weak / tested / mitigated are DIMMED and the factors
                    // STACK (a weak+mitigated zone gets both), rather than being hidden. FrvpShowWeak and
                    // FrvpVaShowMitigated act as hide-overrides (off = hide, like AutoFRVP's Hide mode;
                    // on = show dimmed, like its Dim mode). Opacities match AutoFRVP: weak 40, tested
                    // FrvpTestedOpacityPct (65), mitigated 25.
                    bool weak      = !z.Strong;
                    bool mitigated = FrvpEnableMitigation && z.State == FrvpZoneStateEnum.Mitigated;
                    bool tested    = FrvpEnableMitigation && z.State == FrvpZoneStateEnum.Tested;
                    if (weak && !FrvpVaShowWeak) continue;               // hide weak (AutoFRVP Hide)
                    if (mitigated && !FrvpVaShowMitigated) continue;     // hide mitigated (AutoFRVP Hide)

                    double f = 1.0;
                    if (weak) f *= 0.40;                                 // WeakOpacityPct
                    if (tested) f *= FrvpTestedOpacityPct / 100.0;       // TestedOpacityPct
                    else if (mitigated) f *= 0.25;                       // MitigatedOpacityPct
                    int zop = Math.Max(3, (int)Math.Round(op * f));

                    bool above = z.POC >= curPoc;   // sits above the session POC -> reversal points back down

                    float yVah = chartScale.GetYByValue(z.VAH);
                    float yVal = chartScale.GetYByValue(z.VAL);
                    float yTop = Math.Min(yVah, yVal), yBot = Math.Max(yVah, yVal);

                    float yPoc = chartScale.GetYByValue(z.POC);

                    // When not drawn across the screen, the band lines obey the same reveal rules as the
                    // other levels - but the right-side LABEL is always drawn as a locator (these zones have
                    // no visible source box, so without it you'd never know one is there until price arrived).
                    // Hovering the band OR the label reveals the band, just like the standard levels.
                    bool drawBand = true;
                    if (!FrvpOutOfVaAcrossChart)
                    {
                        bool nearPrice = !ProximityReveal || FrvpZoneDistance(z, renderLastPrice) <= RevealDistance;
                        bool hovered   = HoverReveal && mouseValid &&
                            ( (mousePxX >= leftX - 2f && mousePxX <= wallRightX + 2f && mousePxY >= yTop - 2f && mousePxY <= yBot + 2f)   // over the band
                              || (mousePxX >= wallRightX && mousePxX <= wallRightX + 90f && Math.Abs(mousePxY - yPoc) <= 8f) );            // over the right-side label
                        drawBand = nearPrice || hovered;
                    }

                    // VA band structured like the standard FRVP zone: VAH/VAL edge lines, POC line, optional fill.
                    if (drawBand)
                    {
                        if (FrvpVaShowFill && fillDx != null && yBot - yTop >= 1f)
                        {
                            fillDx.Opacity = Clamp01(Math.Max(4, zop / 6));
                            RenderTarget.FillRectangle(new SharpDX.RectangleF(leftX, yTop, wallRightX - leftX, yBot - yTop), fillDx);
                        }

                        DrawRailLine(vaWpf,  FrvpVaEdgeStyle, 1, zop, leftX, wallRightX, yVah);
                        DrawRailLine(vaWpf,  FrvpVaEdgeStyle, 1, zop, leftX, wallRightX, yVal);
                        DrawRailLine(pocWpf, FrvpVaPocStyle,  1, zop, leftX, wallRightX, yPoc);
                    }

                    // Locator label at the right of the wall - always shown, like every other level.
                    if (ShowLabels)
                    {
                        string flag = mitigated ? "\u00b7m" : (tested ? "\u00b7t" : "");
                        string tag = "FRVP" + flag + (above ? " \u2193POC" : " \u2191POC");
                        QueueGutterLabel(z.POC, yPoc, wallRightX + 4f, tag, pocWpf, zop,
                                         LabelRankEnum.Frvp, tested || mitigated);
                    }

                    // Hover region = the whole band plus its right-side label. These zones have no source
                    // box on the chart, so the band itself is the only thing there is to point at.
                    if (hoverList != null)
                    {
                        float hTop = Math.Min(yTop, yPoc - 8f) - 2f;
                        float hBot = Math.Max(yBot, yPoc + 8f) + 2f;
                        hoverList.Add(new FrvpHover { L = leftX - 2f, T = hTop, R = wallRightX + 92f, B = hBot, Key = z.Key });
                        if (fovHasHover && z.Key == fovHoveredKey) { hoverZone = z; hoverOutside = outsideFrac; }
                    }
                }

                if (FrvpVaTooltip)
                {
                    fovHoverSnap = hoverList != null ? hoverList.ToArray() : new FrvpHover[0];
                    // The chart-timeframe zone's source box is the more specific target: if the cursor is
                    // over one of those, let its tooltip win rather than stacking two cards.
                    if (fovHasHover && mouseValid && !frvpHasHover && hoverZone != null)
                        DrawFovTooltip(mousePxX, mousePxY, hoverZone, hoverOutside);
                }
            }
            catch { }
        }

        // FRVP consolidation zones. The source-bar outline (the candles that formed the base) is always
        // drawn; the value-area projection (POC/VAH/VAL + optional fill) is gated by proximity like the
        // other levels and, when revealed, extends right to terminate exactly at the polarity strip.
        private void DrawFrvpZones(ChartScale chartScale, float wallRightX)
        {
            var zones = frvpSnap;   // data thread mutates frvpZones; render iterates the snapshot
            if (zones.Length == 0 || renderCC == null) return;

            float stripLeft = wallRightX - Math.Max(2, PolarityStripWidth);   // projection ends at the strip's left edge
            double px = renderLastPrice;

            Brush srcWpf = FrvpSourceColor ?? Brushes.SlateGray;
            Brush vaWpf  = FrvpVaColor ?? Brushes.SteelBlue;
            Brush pocWpf = FrvpPocColor ?? Brushes.Goldenrod;

            try
            {
                var srcDx = AcquireBrush(srcWpf, Clamp01(FrvpOpacity));
                var fillDx = FrvpShowFill ? AcquireBrush(vaWpf, Clamp01(FrvpFillOpacity)) : null;
                var hoverList = ShowFrvpTooltip ? new List<FrvpHover>() : null;

                foreach (var z in zones)
                {
                    if (!FrvpShowWeak && !z.Strong) continue;
                    if (z.SrcHigh <= z.SrcLow) continue;

                    bool mitigated = FrvpEnableMitigation && z.State == FrvpZoneStateEnum.Mitigated;
                    bool tested    = FrvpEnableMitigation && z.State == FrvpZoneStateEnum.Tested;

                    // A mitigated zone is dead structure: optionally leave a faint source footprint, no projection.
                    if (mitigated && !FrvpShowMitigatedFootprint) continue;

                    float xStart = renderCC.GetXByTime(z.StartTime);
                    float xEnd   = renderCC.GetXByTime(z.EndTime);
                    float ysHi   = chartScale.GetYByValue(z.SrcHigh);
                    float ysLo   = chartScale.GetYByValue(z.SrcLow);

                    // Source-bar outline - always visible (faint when mitigated).
                    if (xEnd > xStart && ysLo - ysHi >= 1f)
                    {
                        srcDx.Opacity = Clamp01(mitigated ? FrvpMitigatedFootprintOpacity : FrvpOpacity);
                        RenderTarget.DrawRectangle(new SharpDX.RectangleF(xStart, ysHi, xEnd - xStart, ysLo - ysHi), srcDx, 1.4f);

                        // Hover region = the source box (so the tooltip works on the dim mitigated box too).
                        if (hoverList != null)
                            hoverList.Add(new FrvpHover { L = xStart - 2f, T = ysHi - 2f, R = xEnd + 2f, B = ysLo + 2f, Key = z.Key });
                    }

                    if (mitigated) continue;   // no value-area projection for mitigated zones

                    // VA projection reveals when price is within RevealDistance of the band, OR (like the
                    // other levels) when the cursor hovers the zone's source-bar box.
                    bool nearPrice = !ProximityReveal || FrvpZoneDistance(z, px) <= RevealDistance;
                    bool hovered   = HoverReveal && mouseValid
                                     && mousePxX >= xStart - 2f && mousePxX <= xEnd + 2f
                                     && mousePxY >= Math.Min(ysHi, ysLo) - 2f && mousePxY <= Math.Max(ysHi, ysLo) + 2f;
                    if (!nearPrice && !hovered) continue;

                    float xL = xStart;
                    float xR = stripLeft;
                    if (xR <= xL) continue;

                    float yVah = chartScale.GetYByValue(z.VAH);
                    float yVal = chartScale.GetYByValue(z.VAL);
                    float yPoc = chartScale.GetYByValue(z.POC);
                    float yTop = Math.Min(yVah, yVal), yBot = Math.Max(yVah, yVal);

                    // Tested zones (price returned and the level held) read subtler than Fresh.
                    int projOp = tested ? Math.Max(5, FrvpOpacity * FrvpTestedOpacityPct / 100) : FrvpOpacity;
                    if (ScoreReactions && z.Score > 0)
                        projOp = Math.Min(100, projOp + (int)(z.Score * ReactionOpacityBoost));

                    if (FrvpShowFill && fillDx != null && yBot - yTop >= 1f)
                    {
                        fillDx.Opacity = Clamp01(tested ? FrvpFillOpacity * FrvpTestedOpacityPct / 100 : FrvpFillOpacity);
                        RenderTarget.FillRectangle(new SharpDX.RectangleF(xL, yTop, xR - xL, yBot - yTop), fillDx);
                    }

                    DrawRailLine(vaWpf,  DashStyleHelper.Dash,  1, projOp, xL, xR, yVah);
                    DrawRailLine(vaWpf,  DashStyleHelper.Dash,  1, projOp, xL, xR, yVal);
                    DrawRailLine(pocWpf, DashStyleHelper.Solid, 1, projOp, xL, xR, yPoc);

                    if (ShowLabels)
                    {
                        string ft = "FRVP" + (tested ? "·t" : "");
                        if (ScoreReactions && z.Touches > 0)
                            ft += " ×" + z.Touches;
                        QueueGutterLabel(z.POC, yPoc, wallRightX + 4f, ft, pocWpf, projOp, LabelRankEnum.Frvp, tested);
                    }
                }

                if (ShowFrvpTooltip)
                {
                    frvpHoverSnap = hoverList != null ? hoverList.ToArray() : new FrvpHover[0];
                    if (frvpHasHover && mouseValid)
                    {
                        FrvpZone hz = null;
                        for (int i = 0; i < zones.Length; i++)
                            if (zones[i].Key == frvpHoveredKey) { hz = zones[i]; break; }
                        if (hz != null) DrawFrvpTooltip(mousePxX, mousePxY, hz);
                    }
                }
            }
            catch { }
        }

        // Hover pop-up for an FRVP zone, mirroring AutoFRVP's tooltip: type/strength/state, POC + VA,
        // volume, departure, bars, age, touches, flips, and a retirement warning when one away.
        private void DrawFrvpTooltip(float mx, float my, FrvpZone z)
        {
            try
            {
                string type = (z.Dir > 0 ? "Demand" : "Supply") + (z.Flips > 0 ? " (flipped)" : "");
                string strength = z.Strong ? "Strong" : "Weak";
                string state = (z.State == FrvpZoneStateEnum.Fresh && z.Touches > 0) ? "Tested" : z.State.ToString();
                int age = Math.Max(0, CurrentBar - z.EndBarIdx);

                var lines = new List<string>
                {
                    type + "  \u2013  " + strength + "  \u2013  " + state,
                    "POC " + z.POC.ToString("F2") + "    VA " + z.VAL.ToString("F2") + " / " + z.VAH.ToString("F2"),
                    "Volume " + z.Volume.ToString("N0"),
                    "Departure " + z.Departure.ToString("F2") + "x    Bars " + z.Bars,
                    "Age " + age + " bars    Touches " + z.Touches + (z.Flips > 0 ? "    Flips " + z.Flips : "")
                };
                string warn = FrvpRetireWarning(z);
                if (warn != null) lines.Add("\u26A0 " + warn);

                DrawTooltipBox(mx, my, lines);
            }
            catch { }
        }

        // Hover pop-up for an out-of-value zone. Same lifecycle fields as the chart-timeframe tooltip
        // (they now run identical mitigation rules), plus what is unique to these: how far out of today's
        // value area the zone sits, and a wall-clock age, since FOV zones carry no chart bar index.
        private void DrawFovTooltip(float mx, float my, FrvpZone z, double outsideFrac)
        {
            try
            {
                string type = (z.Dir > 0 ? "Demand" : "Supply") + (z.Flips > 0 ? " (flipped)" : "");
                string strength = z.Strong ? "Strong" : "Weak";
                string state = (z.State == FrvpZoneStateEnum.Fresh && z.Touches > 0) ? "Tested" : z.State.ToString();
                string side = z.POC >= curPoc ? "above VA" : "below VA";

                TimeSpan age = Time[0] - z.EndTime;
                string ageStr = age.TotalDays >= 1
                    ? age.TotalDays.ToString("F1") + "d"
                    : (age.TotalHours >= 1 ? age.TotalHours.ToString("F1") + "h" : Math.Max(0, age.TotalMinutes).ToString("F0") + "m");

                var lines = new List<string>
                {
                    "Out-of-Value  \u2013  " + type + "  \u2013  " + strength,
                    state + "  \u2013  " + (outsideFrac * 100.0).ToString("F0") + "% outside VA (" + side + ")",
                    "POC " + z.POC.ToString("F2") + "    VA " + z.VAL.ToString("F2") + " / " + z.VAH.ToString("F2"),
                    "Volume " + z.Volume.ToString("N0"),
                    "Departure " + z.Departure.ToString("F2") + "x    Bars " + z.Bars,
                    "Formed " + z.StartTime.ToString("MM/dd HH:mm") + "    Age " + ageStr,
                    "Touches " + z.Touches + "    Flips " + z.Flips
                };
                string warn = FovRetireWarning(z);
                if (warn != null) lines.Add("\u26A0 " + warn);

                DrawTooltipBox(mx, my, lines);
            }
            catch { }
        }

        // One trigger from retirement, against the out-of-value thresholds.
        private string FovRetireWarning(FrvpZone z)
        {
            if (FrvpVaMaxFlips   > 1 && z.Flips   == FrvpVaMaxFlips   - 1) return "retires on next flip";
            if (FrvpVaMaxTouches > 1 && z.Touches == FrvpVaMaxTouches - 1) return "retires on next touch";
            return null;
        }

        // Shared pop-up chrome: rounded dark card, clamped inside the panel, cursor-relative.
        private void DrawTooltipBox(float mx, float my, List<string> lines)
        {
            try
            {
                float pad = 7f, lineH = Math.Max(14f, LabelFontSize + 3f);
                int maxChars = 0;
                for (int i = 0; i < lines.Count; i++) if (lines[i].Length > maxChars) maxChars = lines[i].Length;
                float w = Math.Max(150f, maxChars * 6.7f + pad * 2);
                float h = lines.Count * lineH + pad * 2;

                float x = mx + 16f, y = my + 12f;
                if (x + w > renderCanvasRight)  x = mx - w - 16f;
                if (y + h > renderPanelBottom)  y = renderPanelBottom - h - 2f;
                if (x < renderCanvasLeft)       x = renderCanvasLeft + 2f;
                if (y < renderPanelTop)         y = renderPanelTop + 2f;

                var bg = AcquireBrush(TooltipBackColor ?? Brushes.Black, Clamp01(TooltipBackOpacity));
                var fg = AcquireBrush(Brushes.Gainsboro, Clamp01(100));
                var rr = new SharpDX.Direct2D1.RoundedRectangle { Rect = new SharpDX.RectangleF(x, y, w, h), RadiusX = 4f, RadiusY = 4f };
                RenderTarget.FillRoundedRectangle(rr, bg);
                RenderTarget.DrawRoundedRectangle(rr, fg, 1f);

                var tf = GetTextFormat();
                RenderTarget.DrawText(string.Join("\n", lines), tf, new SharpDX.RectangleF(x + pad, y + pad, w - pad, h - pad), fg);
            }
            catch { }
        }

        private void DrawHvnZones(ChartScale chartScale, float leftX, float wallRightX, List<HvnBand> bands, double pocVol)
        {
            if (bands == null || bands.Count == 0 || pocVol <= 0) return;
            Brush wpf = HvnColor ?? Brushes.IndianRed;
            try
            {
                var dx = AcquireBrush(wpf, Clamp01(HvnZoneOpacity));   // per-band opacity set below
                float zoneLeft = HvnZoneAcrossChart ? leftX : LeftXForVol(profPocVol, wallRightX);
                if (wallRightX - zoneLeft <= 0) return;

                foreach (var b in bands)
                {
                    float yTop = chartScale.GetYByValue(b.HighPrice);
                    float yBot = chartScale.GetYByValue(b.LowPrice);
                    float h = yBot - yTop;
                    if (h < 1f) h = 1f;
                    // Opacity scales with how dominant this band is relative to the profile's peak.
                    double rel = b.PeakVol / pocVol;
                    if (HvnLocalNodes)
                    {
                        // Local-node mode surfaces shelves that sit well below the global POC. Their raw
                        // global-relative opacity would render them invisible, so remap [0..1] into
                        // [floor..1]: the POC still reads strongest, lesser shelves stay legible.
                        const double localFloor = 0.55;
                        double t = rel < 0 ? 0 : (rel > 1 ? 1 : rel);
                        rel = localFloor + (1.0 - localFloor) * t;
                    }
                    dx.Opacity = Clamp01(HvnZoneOpacity * rel);
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(zoneLeft, yTop, wallRightX - zoneLeft, h), dx);

                    // Node POC: a 1px dashed line at the peak-volume row, mirroring an FRVP POC. Its
                    // opacity tracks the node's dominance but is floored so it stays legible over the
                    // faint zone fill.
                    if (b.PocPrice > 0)
                    {
                        float yPoc = chartScale.GetYByValue(b.PocPrice);
                        int pocOp = (int)Math.Round(Math.Max(30.0, Math.Min(85.0, HvnZoneOpacity * rel * 3.0)));
                        DrawRailLine(wpf, DashStyleHelper.Dash, 1, pocOp, zoneLeft, wallRightX, yPoc);
                    }
                }
            }
            catch { }
        }

        private void DrawRails(ChartScale chartScale, float canvasLeft, float wallRightX, float barDist)
        {
            double price = renderLastPrice;   // live last trade (set in OnRender), not the bar close
            float projLeftX = wallRightX - Math.Max(1, RailProjectionBars) * barDist;
            if (projLeftX < canvasLeft) projLeftX = canvasLeft;

            TrackedLvn[] rails = PersistLevels ? trackedSnap : detectedSnap;

            // Resolve the renderable set (state-filtered).
            var vis = new List<TrackedLvn>();
            foreach (var node in rails)
            {
                if (node.Filled) continue;
                if (!node.Strong && !ShowWeakRails) continue;
                bool dim = PersistLevels ? node.Tested : !node.Strong;
                if (dim && PersistLevels && !ShowTested) continue;
                vis.Add(node);
            }
            if (vis.Count == 0) return;

            if (!CombineRails)
            {
                foreach (var node in vis)
                    DrawOneRail(node, chartScale, canvasLeft, wallRightX, projLeftX, price);
                return;
            }

            // Cluster by price; anchored at the lowest member so a zone never spans more than the
            // threshold. Singletons draw as a line, clusters of 2+ draw as a filled zone band.
            vis.Sort((a, b) => a.Price.CompareTo(b.Price));
            double thr = Math.Max(1, RailCombineTicks) * (TickSize > 0 ? TickSize : 1.0);

            int i = 0;
            while (i < vis.Count)
            {
                int j = i + 1;
                while (j < vis.Count && (vis[j].Price - vis[i].Price) <= thr) j++;

                if (j - i == 1)
                    DrawOneRail(vis[i], chartScale, canvasLeft, wallRightX, projLeftX, price);
                else
                    DrawRailZone(vis, i, j, chartScale, canvasLeft, wallRightX, projLeftX, price);

                i = j;
            }
        }

        private bool IsDim(TrackedLvn node)
        {
            return PersistLevels ? node.Tested : !node.Strong;
        }

        private Brush RailBrushFor(double levelPrice, double price)
        {
            Brush b = RailColor ?? Brushes.Cyan;
            if (UseSideColoring)
                b = price >= levelPrice ? (RailSupportColor ?? b) : (RailResistanceColor ?? b);
            return b;
        }

        private void DrawOneRail(TrackedLvn node, ChartScale chartScale, float canvasLeft, float wallRightX, float projLeftX, double price)
        {
            bool dim = IsDim(node);
            Brush brush = RailBrushFor(node.Price, price);
            DashStyleHelper style = dim ? RailWeakStyle : RailStrongStyle;
            int thick = node.Strong ? RailThickness : Math.Max(1, RailThickness - 1);
            int op = dim ? Math.Max(20, RailOpacity - 35) : RailOpacity;
            bool chevron = ShowChevron && !dim;
            if (ScoreReactions && node.Score > 0)
            {
                op = Math.Min(100, op + (int)(node.Score * ReactionOpacityBoost));
                if (node.Score >= 0.66) thick += 1;   // proven levels read heavier
            }

            float y = chartScale.GetYByValue(node.Price);
            bool show = LineVisible(node.Price, y);

            // CPR source anchoring: draw the rail from its originating pivot bar to the wall, so the
            // swing it came from (and how long it has held) is visible - rather than a fixed tongue.
            if (show && node.IsPivot && ShowPivotSource && node.OriginBar >= 0 && renderCC != null)
            {
                float ox = renderCC.GetXByBarIndex(ChartBars, node.OriginBar);
                if (ox < canvasLeft) ox = canvasLeft;
                if (ox > wallRightX - 4f) ox = wallRightX - 4f;
                DrawRailLine(brush, style, thick, op, ox, wallRightX, y);
                if (chevron) DrawChevron(brush, op, ox, y);   // chevron marks the source pivot
            }
            else if (show)
            {
                DrawRailAtY(brush, style, thick, op, chevron, canvasLeft, wallRightX, projLeftX, y);
            }

            if (ShowLabels)
                DrawRailLabel(node, dim, brush, op, wallRightX, y);
        }

        private void DrawRailZone(List<TrackedLvn> vis, int i, int j, ChartScale chartScale, float canvasLeft, float wallRightX, float projLeftX, double price)
        {
            double minP = vis[i].Price;
            double maxP = vis[j - 1].Price;
            double centerP = 0.5 * (minP + maxP);

            bool anyNaked = false, anyStrong = false;
            double maxScore = 0.0; int maxHolds = 0;
            for (int k = i; k < j; k++)
            {
                if (!IsDim(vis[k])) anyNaked = true;
                if (vis[k].Strong) anyStrong = true;
                if (vis[k].Score > maxScore) maxScore = vis[k].Score;
                if (vis[k].Holds > maxHolds) maxHolds = vis[k].Holds;
            }
            bool dim = !anyNaked;   // a zone is "naked" if any member is still naked

            Brush brush = RailBrushFor(centerP, price);
            DashStyleHelper style = dim ? RailWeakStyle : RailStrongStyle;
            int thick = anyStrong ? RailThickness : Math.Max(1, RailThickness - 1);
            int op = dim ? Math.Max(20, RailOpacity - 35) : RailOpacity;
            bool chevron = ShowChevron && !dim;
            if (ScoreReactions && maxScore > 0)
            {
                op = Math.Min(100, op + (int)(maxScore * ReactionOpacityBoost));
                if (maxScore >= 0.66) thick += 1;
            }

            float yTop = chartScale.GetYByValue(maxP);
            float yBot = chartScale.GetYByValue(minP);

            float yCenter = chartScale.GetYByValue(centerP);
            if (LineVisible(centerP, yCenter))
            {
                // Fill the band across the bright extent.
                float fillLeft = RailRenderMode == RailRenderModeEnum.FullSpan ? canvasLeft : projLeftX;
                DrawZoneFill(brush, ZoneOpacity, fillLeft, wallRightX, yTop, yBot);

                // Top + bottom borders (no chevron on the borders).
                DrawRailAtY(brush, style, thick, op, false, canvasLeft, wallRightX, projLeftX, yTop);
                DrawRailAtY(brush, style, thick, op, false, canvasLeft, wallRightX, projLeftX, yBot);

                if (chevron)
                {
                    float chevX = RailRenderMode == RailRenderModeEnum.FullSpan ? canvasLeft : projLeftX;
                    DrawChevron(brush, op, chevX, yCenter);
                }
            }

            if (ShowLabels)
            {
                bool anyPivot = false;
                for (int k = i; k < j; k++) if (vis[k].IsPivot) { anyPivot = true; break; }
                string baseTag = anyPivot ? "CPR" : "LVN";   // a confirmed reversal in the cluster wins
                string tag = dim ? baseTag + "·t" : baseTag;
                if (ScoreReactions && maxHolds > 0)
                    tag += " ×" + maxHolds;
                float labelX = wallRightX + 4f;
                if (RailPolarity)
                {
                    double polSum = 0; int polN = 0;
                    for (int k = i; k < j; k++) { polSum += vis[k].Polarity; polN++; }
                    DrawPolarityDot(polN > 0 ? polSum / polN : 0.0, wallRightX + 6f, yCenter);
                    labelX = wallRightX + 13f;
                }
                QueueGutterLabel(centerP, yCenter, labelX, tag + " zone", brush, op,
                                 anyPivot ? LabelRankEnum.Cpr : LabelRankEnum.Lvn, dim);
            }
        }

        // Draws a rail line at a given y, honoring the render mode (and the dim full-span layer in Both).
        private void DrawRailAtY(Brush brush, DashStyleHelper style, int thick, int op, bool chevron,
                                 float canvasLeft, float wallRightX, float projLeftX, float y)
        {
            if (RailRenderMode == RailRenderModeEnum.FullSpan)
            {
                DrawRailLine(brush, style, thick, op, canvasLeft, wallRightX, y);
                if (chevron) DrawChevron(brush, op, canvasLeft, y);
            }
            else if (RailRenderMode == RailRenderModeEnum.Projection)
            {
                DrawRailLine(brush, style, thick, op, projLeftX, wallRightX, y);
                if (chevron) DrawChevron(brush, op, projLeftX, y);
            }
            else // Both: dim full-span map + bright projection
            {
                if (ShowDimExtension)
                    DrawRailLine(brush, style, Math.Max(1, thick - 1), DimOpacity, canvasLeft, wallRightX, y);
                DrawRailLine(brush, style, thick, op, projLeftX, wallRightX, y);
                if (chevron) DrawChevron(brush, op, projLeftX, y);
            }
        }

        private void DrawZoneFill(Brush wpf, int opacity, float x0, float x1, float yTop, float yBot)
        {
            if (x1 <= x0) return;
            float h = yBot - yTop;
            if (h < 1f) h = 1f;
            try
            {
                var dx = AcquireBrush(wpf ?? Brushes.Cyan, Clamp01(opacity));
                RenderTarget.FillRectangle(new SharpDX.RectangleF(x0, yTop, x1 - x0, h), dx);
            }
            catch { }
        }

        private void DrawRailLine(Brush wpf, DashStyleHelper style, int thickness, int opacity, float x0, float x1, float y)
        {
            if (x1 <= x0) return;
            try
            {
                var dx = AcquireBrush(wpf ?? Brushes.Cyan, Clamp01(opacity));
                var stroke = GetStrokeStyle(style);   // cached - do NOT dispose per call
                var p0 = new SharpDX.Vector2(x0, y);
                var p1 = new SharpDX.Vector2(x1, y);
                if (stroke != null)
                    RenderTarget.DrawLine(p0, p1, dx, thickness, stroke);
                else
                    RenderTarget.DrawLine(p0, p1, dx, thickness);
            }
            catch { }
        }

        private void DrawChevron(Brush wpf, int opacity, float xLeft, float y)
        {
            SharpDX.Direct2D1.PathGeometry geo = null;
            SharpDX.Direct2D1.GeometrySink sink = null;
            try
            {
                var dx = AcquireBrush(wpf ?? Brushes.Cyan, Clamp01(opacity));
                float s = 6f;
                geo = new SharpDX.Direct2D1.PathGeometry(RenderTarget.Factory);
                sink = geo.Open();
                sink.BeginFigure(new SharpDX.Vector2(xLeft, y - s), SharpDX.Direct2D1.FigureBegin.Filled);
                sink.AddLine(new SharpDX.Vector2(xLeft, y + s));
                sink.AddLine(new SharpDX.Vector2(xLeft + s * 2f, y));
                sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
                sink.Close();
                RenderTarget.FillGeometry(geo, dx);
            }
            catch { }
            finally
            {
                sink?.Dispose();
                geo?.Dispose();
            }
        }

        private void DrawPoc(ChartScale chartScale, float leftX, float wallRightX)
        {
            double pocPrice = ProfRowCenter(profPocRow);
            float y = chartScale.GetYByValue(pocPrice);
            Brush wpf = PocColor ?? Brushes.OrangeRed;
            try
            {
                var dx = AcquireBrush(wpf, Clamp01(PocOpacity));
                var stroke = GetStrokeStyle(PocStyle);   // cached - do NOT dispose per call
                var p0 = new SharpDX.Vector2(leftX, y);
                var p1 = new SharpDX.Vector2(wallRightX, y);
                if (stroke != null)
                    RenderTarget.DrawLine(p0, p1, dx, PocThickness, stroke);
                else
                    RenderTarget.DrawLine(p0, p1, dx, PocThickness);

                if (ShowLabels)
                    QueueGutterLabel(pocPrice, y, wallRightX + 4f, "POC", wpf, PocOpacity, LabelRankEnum.Poc, false);
            }
            catch { }
        }

        private void DrawRailLabel(TrackedLvn node, bool dim, Brush wpf, int opacity, float wallRightX, float y)
        {
            string baseTag = node.IsPivot ? "CPR" : "LVN";
            string tag;
            if (PersistLevels)
                tag = node.Tested ? baseTag + "·t" : baseTag;
            else
                tag = node.Strong ? baseTag : baseTag.ToLowerInvariant();

            if (node.IsPivot && node.Flips > 0)
                tag += "\u21c4";   // flipped CPR: broken resistance now working as support (or vice-versa)

            if (ScoreReactions && node.Holds > 0)
                tag += " ×" + node.Holds;

            float labelX = wallRightX + 4f;
            if (RailPolarity)
            {
                DrawPolarityDot(node.Polarity, wallRightX + 6f, y);
                labelX = wallRightX + 13f;
            }
            // A tested / below-strength rail is deliberately de-emphasised, so it must not take the
            // visible slot away from a live level sharing its row - but it still shows in the card.
            QueueGutterLabel(node.Price, y, labelX, tag, wpf, opacity,
                             node.IsPivot ? LabelRankEnum.Cpr : LabelRankEnum.Lvn, dim);
        }

        private void DrawText(string text, Brush wpf, int opacity, float x, float y)
        {
            try
            {
                var tf = GetTextFormat();
                var tb = AcquireBrush(wpf ?? Brushes.Gray, Clamp01(Math.Max(60, opacity)));
                float h = Math.Max(12f, LabelFontSize + 4f);
                var rect = new SharpDX.RectangleF(x, y - h / 2f, 220f, h);
                RenderTarget.DrawText(text, tf, rect, tb);
            }
            catch { }
        }

        #region Gutter label layer

        // Row height used for both text layout and pixel-space collision. Two labels collide when
        // their rows are closer than this - which has nothing to do with how many TICKS apart their
        // prices are. That mismatch is why a tick-based merge threshold never behaved consistently
        // across zoom levels.
        private float LabelRowHeight { get { return Math.Max(12f, LabelFontSize + 4f); } }

        private float EstimateTextWidth(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            return s.Length * (LabelFontSize * 0.62f) + 6f;
        }

        private string FormatPriceSafe(double price)
        {
            try { return Instrument.MasterInstrument.FormatPrice(price); }
            catch { return price.ToString("0.##"); }
        }

        // Pin list: exact, case-insensitive tag matches that jump the whole rank order. Parsed lazily,
        // and only when the property string actually changes - this runs once per label per frame.
        private int ResolvePin(string tag)
        {
            string src = LabelPinTags ?? string.Empty;
            if (src != pinTagsSrc)
            {
                pinTagsSrc = src;
                var parts = src.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
                pinTags = parts;
            }
            for (int i = 0; i < pinTags.Length; i++)
                if (pinTags[i].Length > 0 && string.Equals(pinTags[i], tag, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        // Every "who wins the slot" question routes through here. Lower is better.
        //   pinned  -> beats everything, ordered by position in the pin list
        //   dim     -> loses to any non-dim (a tested rail must not silence a live level)
        //   rank    -> Extreme > Cpr > Poc > ... > Merged
        private static bool Beats(GutterLabel a, GutterLabel b)
        {
            if (b == null) return true;
            bool ap = a.Pin >= 0, bp = b.Pin >= 0;
            if (ap != bp) return ap;                       // a pin ignores Dim entirely
            if (ap && bp) return a.Pin < b.Pin;
            if (a.Dim != b.Dim) return !a.Dim;
            return a.Rank < b.Rank;
        }

        // The single entry point for anything that wants to write text into the right gutter.
        // Nothing is painted here; the frame's labels are resolved together at the end of OnRender.
        private void QueueGutterLabel(double price, float y, float x, string tag, Brush wpf, int opacity,
                                      LabelRankEnum rank, bool dim)
        {
            if (!ShowLabels || string.IsNullOrEmpty(tag)) return;
            labelQueue.Add(new GutterLabel
            {
                Price = price, Y = y, X = x, Tag = tag,
                Wpf = wpf ?? Brushes.Gray, Opacity = Math.Max(60, opacity),
                Rank = rank, Dim = dim, Pin = ResolvePin(tag)
            });
        }

        // Opaque backing so a label is legible over whatever geometry (or other label) is behind it.
        // Cheap and independent of grouping: even with one label per row, glyphs sitting directly on a
        // wall gradient or a zone fill were washing out.
        private void DrawLabelBackdrop(float x, float y, float w)
        {
            if (!ShowLabelBackdrop) return;
            try
            {
                // Explicit colour. This used to read ChartControl.Properties.ChartBackground, which is
                // not reliably the brush that paints the panel (a skin can override the template and
                // leave Properties on a default), and which took AcquireBrush's Clone()->ToDxBrush
                // fallback once per label per frame when it wasn't a SolidColorBrush.
                var dx = AcquireBrush(LabelBackdropColor ?? Brushes.Black, Clamp01(LabelBackdropOpacity));
                float h = LabelRowHeight;
                var rr = new SharpDX.Direct2D1.RoundedRectangle
                {
                    Rect = new SharpDX.RectangleF(x - 2f, y - h / 2f, w + 4f, h),
                    RadiusX = 2f, RadiusY = 2f
                };
                RenderTarget.FillRoundedRectangle(rr, dx);
            }
            catch { }
        }

        private void DrawLabelWithBackdrop(string text, Brush wpf, int opacity, float x, float y)
        {
            DrawLabelBackdrop(x, y, EstimateTextWidth(text));
            DrawText(text, wpf, opacity, x, y);
        }

        // A small filled square, one per group member, tinted by that member's family colour. Color is
        // doing the identification work in this indicator, so a collapsed chip has to keep it.
        // A pinned member gets a bright ring: that is how you see that a group holds a SECOND pin whose
        // tag lost the chip, without having to hover it.
        private void DrawMemberDot(Brush wpf, int opacity, float x, float y, bool pinned)
        {
            try
            {
                var dx = AcquireBrush(wpf ?? Brushes.Gray, Clamp01(Math.Max(70, opacity)));
                RenderTarget.FillRectangle(new SharpDX.RectangleF(x, y - 2.5f, 5f, 5f), dx);
                if (pinned)
                {
                    var ring = AcquireBrush(Brushes.White, Clamp01(90));
                    RenderTarget.DrawRectangle(new SharpDX.RectangleF(x - 1f, y - 3.5f, 7f, 7f), ring, 1f);
                }
            }
            catch { }
        }

        // A 3px stub at a member's TRUE row, drawn when a fan pushes its text off the exact price.
        private void DrawRowTick(Brush wpf, int opacity, float x, float y)
        {
            try
            {
                var dx = AcquireBrush(wpf ?? Brushes.Gray, Clamp01(Math.Max(70, opacity)));
                RenderTarget.FillRectangle(new SharpDX.RectangleF(x, y - 0.5f, 4f, 1f), dx);
            }
            catch { }
        }

        // Cluster the frame's labels in pixel space and draw. Lines are never touched - every level is
        // already on the chart at its exact price. This only decides who owns the text slot.
        private void FlushGutterLabels()
        {
            labelGroups.Clear();
            if (labelQueue.Count == 0) return;

            if (!GroupStackedLabels)
            {
                // Legacy behaviour, minus the occlusion: draw every label, still backed.
                for (int k = 0; k < labelQueue.Count; k++)
                {
                    var m = labelQueue[k];
                    DrawLabelWithBackdrop(m.Tag + PriceSuffix(m.Price), m.Wpf, m.Opacity, m.X, m.Y);
                }
                labelQueue.Clear();
                return;
            }

            float rowH = LabelRowHeight;
            float thr  = rowH + Math.Max(0, LabelClusterPadPx);

            // Sort by row, then by importance so an equal-row tie resolves deterministically.
            labelQueue.Sort((a, b) =>
            {
                int c = a.Y.CompareTo(b.Y);
                if (c != 0) return c;
                if (Beats(a, b)) return -1;
                if (Beats(b, a)) return 1;
                return 0;
            });

            // Single-linkage on Y; a large X gap splits a run so a mid-chart HTF profile label never
            // gets folded into the wall's gutter column.
            int i = 0;
            while (i < labelQueue.Count)
            {
                var g = new LabelGroup();
                g.Members.Add(labelQueue[i]);
                float anchorX = labelQueue[i].X;
                float lastY   = labelQueue[i].Y;

                int j = i + 1;
                while (j < labelQueue.Count
                       && labelQueue[j].Y - lastY < thr
                       && Math.Abs(labelQueue[j].X - anchorX) <= 40f)
                {
                    g.Members.Add(labelQueue[j]);
                    lastY = labelQueue[j].Y;
                    j++;
                }
                labelGroups.Add(g);
                i = j;
            }
            labelQueue.Clear();

            int stacked = 0;
            for (int k = 0; k < labelGroups.Count; k++)
            {
                var g = labelGroups[k];

                // The visible slot goes to whichever member Beats() all the others: a pin first, then
                // the highest-ranked non-dim level, then rank alone if every member is dimmed.
                GutterLabel primary = null;
                for (int m = 0; m < g.Members.Count; m++)
                    if (Beats(g.Members[m], primary)) primary = g.Members[m];

                g.Primary = primary;

                if (g.Members.Count == 1)
                {
                    DrawLabelWithBackdrop(primary.Tag + PriceSuffix(primary.Price), primary.Wpf, primary.Opacity, primary.X, primary.Y);
                    g.L = primary.X - 2f;
                    g.R = primary.X + EstimateTextWidth(primary.Tag + PriceSuffix(primary.Price)) + 2f;
                    g.T = primary.Y - rowH / 2f;
                    g.B = primary.Y + rowH / 2f;
                    continue;
                }

                stacked++;

                if (ExpandAllGroups)
                {
                    // Fan the members apart vertically so a screenshot or a replay review shows all of
                    // them. Each keeps its own colour, and a stub marks the row it actually belongs to.
                    var fan = new List<GutterLabel>(g.Members);
                    fan.Sort((a, b) => a.Price.CompareTo(b.Price));   // low price = low on screen
                    float cy = primary.Y;
                    int n = fan.Count;
                    for (int m = 0; m < n; m++)
                    {
                        // fan[0] is the lowest price -> largest Y. Draw bottom-up.
                        var mem = fan[m];
                        float fy = cy + ((n - 1) / 2f - m) * rowH;
                        DrawRowTick(mem.Wpf, mem.Opacity, mem.X - 6f, mem.Y);
                        DrawLabelWithBackdrop(mem.Tag + PriceSuffix(mem.Price), mem.Wpf, mem.Opacity, mem.X, fy);
                    }
                    float fh = n * rowH;
                    g.L = primary.X - 8f;
                    g.R = primary.X + 120f;
                    g.T = cy - fh / 2f;
                    g.B = cy + fh / 2f;
                    continue;
                }

                // Collapsed chip: dots for every member (in price order), then the winner's tag, a +N
                // count, and the winner's EXACT price. Nothing is averaged and nothing is hidden from
                // the hover card.
                float x = primary.X;
                float y = primary.Y;

                if (ShowGroupDots)
                {
                    var dots = new List<GutterLabel>(g.Members);
                    dots.Sort((a, b) => b.Price.CompareTo(a.Price));   // high price first, left to right
                    float dx0 = x + 1f;
                    for (int m = 0; m < dots.Count; m++)
                    {
                        DrawLabelBackdrop(dx0 - 1f, y, 7f);
                        DrawMemberDot(dots[m].Wpf, dots[m].Opacity, dx0, y, dots[m].Pin >= 0);
                        dx0 += 8f;
                    }
                    x = dx0 + 3f;
                }

                string chip = primary.Tag + " +" + (g.Members.Count - 1) + PriceSuffix(primary.Price);
                float w = EstimateTextWidth(chip);
                DrawLabelWithBackdrop(chip, primary.Wpf, primary.Opacity, x, y);

                g.L = primary.X - 3f;
                g.R = Math.Max(x + w + 3f, primary.X + 60f);
                g.T = y - rowH / 2f - 1f;
                g.B = y + rowH / 2f + 1f;
            }

            // Hover card. FRVP and out-of-value cards are painted earlier in the frame and take
            // precedence, so we never stack two pop-ups.
            if (LabelGroupTooltip && !ExpandAllGroups && mouseValid && !frvpHasHover && !fovHasHover)
            {
                for (int k = labelGroups.Count - 1; k >= 0; k--)
                {
                    var g = labelGroups[k];
                    if (g.Members.Count < 2) continue;
                    if (mousePxX >= g.L && mousePxX <= g.R && mousePxY >= g.T && mousePxY <= g.B)
                    {
                        DrawLabelGroupCard(mousePxX, mousePxY, g);
                        break;
                    }
                }
            }
        }

        // Pop-up listing every member of a collapsed group with its exact, un-averaged price. Same
        // chrome as the FRVP card, but each row keeps its family colour - the price suffix is exactly
        // what overlap was eating, so it is the first thing this has to give back.
        private void DrawLabelGroupCard(float mx, float my, LabelGroup g)
        {
            try
            {
                var rows = new List<GutterLabel>(g.Members);
                rows.Sort((a, b) => b.Price.CompareTo(a.Price));   // top of card = highest price

                int pins = 0;
                for (int i = 0; i < rows.Count; i++) if (rows[i].Pin >= 0) pins++;

                string header = g.Members.Count + " levels on this row" + (pins > 0 ? "   (" + pins + " pinned)" : "");

                float pad = 7f, lineH = Math.Max(14f, LabelFontSize + 3f);
                int maxChars = header.Length;
                var texts = new List<string>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    string t = (rows[i].Pin >= 0 ? "◆ " : "  ") + rows[i].Tag + "   " + FormatPriceSafe(rows[i].Price) + (rows[i].Dim ? "  ·dim" : "");
                    texts.Add(t);
                    if (t.Length + 2 > maxChars) maxChars = t.Length + 2;
                }

                float w = Math.Max(170f, maxChars * 6.7f + pad * 2f);
                float h = (rows.Count + 1) * lineH + pad * 2f;

                float x = mx + 16f, y = my + 12f;
                if (x + w > renderCanvasRight) x = mx - w - 16f;
                if (y + h > renderPanelBottom) y = renderPanelBottom - h - 2f;
                if (x < renderCanvasLeft)      x = renderCanvasLeft + 2f;
                if (y < renderPanelTop)        y = renderPanelTop + 2f;

                var bg = AcquireBrush(TooltipBackColor ?? Brushes.Black, Clamp01(TooltipBackOpacity));
                var fg = AcquireBrush(Brushes.Gainsboro, Clamp01(100));
                var rr = new SharpDX.Direct2D1.RoundedRectangle { Rect = new SharpDX.RectangleF(x, y, w, h), RadiusX = 4f, RadiusY = 4f };
                RenderTarget.FillRoundedRectangle(rr, bg);
                RenderTarget.DrawRoundedRectangle(rr, fg, 1f);

                var tf = GetTextFormat();
                RenderTarget.DrawText(header, tf, new SharpDX.RectangleF(x + pad, y + pad, w - pad, lineH), fg);

                for (int i = 0; i < rows.Count; i++)
                {
                    float ry = y + pad + (i + 1) * lineH;
                    var cb = AcquireBrush(rows[i].Wpf ?? Brushes.Gray, Clamp01(rows[i].Dim && rows[i].Pin < 0 ? 55 : 100));
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(x + pad, ry + lineH / 2f - 3f, 6f, 6f), cb);
                    RenderTarget.DrawText(texts[i], tf, new SharpDX.RectangleF(x + pad + 12f, ry, w - pad - 12f, lineH), cb);
                }
            }
            catch { }
        }

        #endregion

        private string PriceSuffix(double price)
        {
            if (!ShowPrices) return "";
            string fp;
            try { fp = Instrument.MasterInstrument.FormatPrice(price); }
            catch { fp = price.ToString("0.##"); }
            return " " + fp;
        }

        private static float Clamp01(double pct)
        {
            double v = pct / 100.0;
            if (v < 0) v = 0;
            if (v > 1) v = 1;
            return (float)v;
        }

        // Cached per style. Callers must NOT dispose the returned style - the cache owns it and it
        // is invalidated wholesale when the render target changes.
        private SharpDX.Direct2D1.StrokeStyle GetStrokeStyle(DashStyleHelper style)
        {
            if (style == DashStyleHelper.Solid) return null;

            SharpDX.Direct2D1.StrokeStyle s;
            if (strokeCache.TryGetValue(style, out s) && !s.IsDisposed) return s;

            var props = new SharpDX.Direct2D1.StrokeStyleProperties();
            // Direct2D renders Dot as zero-length dashes; without round caps they collapse to nothing.
            props.DashCap = SharpDX.Direct2D1.CapStyle.Round;
            props.StartCap = SharpDX.Direct2D1.CapStyle.Round;
            props.EndCap = SharpDX.Direct2D1.CapStyle.Round;
            if (style == DashStyleHelper.Dash) props.DashStyle = SharpDX.Direct2D1.DashStyle.Dash;
            else if (style == DashStyleHelper.Dot) props.DashStyle = SharpDX.Direct2D1.DashStyle.Dot;
            else if (style == DashStyleHelper.DashDot) props.DashStyle = SharpDX.Direct2D1.DashStyle.DashDot;
            else if (style == DashStyleHelper.DashDotDot) props.DashStyle = SharpDX.Direct2D1.DashStyle.DashDotDot;
            else return null;
            s = new SharpDX.Direct2D1.StrokeStyle(RenderTarget.Factory, props);
            strokeCache[style] = s;
            return s;
        }

        // ==== Cached device resources ====================================================
        // One DX brush per WPF SolidColorBrush - a DX brush's Opacity is mutable, so a single cached
        // brush serves every opacity variant (opacity is set immediately before every use). Non-solid
        // user brushes fall back to a per-frame transient, disposed in OnRender's finally. Everything
        // here is render-target-scoped and rebuilt lazily after OnRenderTargetChanged.

        private SharpDX.Direct2D1.Brush AcquireBrush(Brush wpf, float opacity)
        {
            if (wpf == null) wpf = Brushes.Gray;
            var scb = wpf as SolidColorBrush;
            if (scb == null)
            {
                var t = wpf.Clone().ToDxBrush(RenderTarget);   // rare: gradient/other user brush
                t.Opacity = opacity;
                transientBrushes.Add(t);
                return t;
            }
            SharpDX.Direct2D1.SolidColorBrush b;
            if (!dxBrushCache.TryGetValue(wpf, out b) || b.IsDisposed)
            {
                var c = scb.Color;
                b = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,
                    new SharpDX.Color4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f));
                dxBrushCache[wpf] = b;
            }
            b.Opacity = opacity;
            return b;
        }

        private SharpDX.Direct2D1.SolidColorBrush AcquireColorBrush(System.Windows.Media.Color c)
        {
            SharpDX.Direct2D1.SolidColorBrush b;
            if (!colorBrushCache.TryGetValue(c, out b) || b.IsDisposed)
            {
                b = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,
                    new SharpDX.Color4(c.R / 255f, c.G / 255f, c.B / 255f, 1f));
                colorBrushCache[c] = b;
            }
            return b;
        }

        private SharpDX.DirectWrite.TextFormat GetTextFormat()
        {
            int size = Math.Max(6, LabelFontSize);
            if (cachedTf == null || cachedTf.IsDisposed || cachedTfSize != size)
            {
                cachedTf?.Dispose();
                cachedTf = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", size);
                cachedTf.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
                cachedTf.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
                cachedTfSize = size;
            }
            return cachedTf;
        }

        // 21-bucket polarity palette shared by the wall tint and the strip (opacity set per use).
        private SharpDX.Direct2D1.SolidColorBrush[] GetPolarityPalette()
        {
            if (polPalette != null && polPalette[0] != null && !polPalette[0].IsDisposed)
                return polPalette;
            DisposePalette();
            polPalette = new SharpDX.Direct2D1.SolidColorBrush[21];
            for (int b = 0; b < 21; b++)
            {
                double p = (b / 20.0) * 2.0 - 1.0;   // -1..+1
                var c = ColorForPolarity(p);
                polPalette[b] = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,
                    new SharpDX.Color4(c.R / 255f, c.G / 255f, c.B / 255f, 1f));
            }
            return polPalette;
        }

        private void DisposePalette()
        {
            if (polPalette == null) return;
            for (int i = 0; i < polPalette.Length; i++) polPalette[i]?.Dispose();
            polPalette = null;
        }

        private void DisposeDeviceCache()
        {
            foreach (var kv in dxBrushCache) kv.Value?.Dispose();
            dxBrushCache.Clear();
            foreach (var kv in colorBrushCache) kv.Value?.Dispose();
            colorBrushCache.Clear();
            foreach (var kv in strokeCache) kv.Value?.Dispose();
            strokeCache.Clear();
            cachedTf?.Dispose(); cachedTf = null; cachedTfSize = -1;
            cachedTfBold?.Dispose(); cachedTfBold = null; cachedTfBoldSize = -1;
            DisposePalette();
            for (int i = 0; i < transientBrushes.Count; i++) transientBrushes[i]?.Dispose();
            transientBrushes.Clear();
        }

        public override void OnRenderTargetChanged()
        {
            // Device resources are render-target-scoped: drop them all and rebuild lazily.
            DisposeDeviceCache();
        }

        #endregion

        #region Properties

        // ===== 01. Profile =====
        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Profile Ticks Per Row", Description = "Row height (ticks) for the session structural profile: the wall, POC, value area, HVN.", Order = 1, GroupName = "01. Profile")]
        public int ProfileTicksPerRow { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Scan Ticks Per Row", Description = "Row height (ticks) for the rolling LVN scan. Finer than the profile for sharper level detection.", Order = 2, GroupName = "01. Profile")]
        public int ScanTicksPerRow { get; set; }

        [NinjaScriptProperty]
        [Range(50, 200000)]
        [Display(Name = "Scan Lookback (bars)", Description = "How many recent bars feed the rolling LVN scan. Does not affect the session profile.", Order = 3, GroupName = "01. Profile")]
        public int ScanLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Range(50, 6000)]
        [Display(Name = "Max Rows", Description = "Upper bound on row count for either histogram.", Order = 4, GroupName = "01. Profile")]
        public int MaxRows { get; set; }

        [NinjaScriptProperty]
        [Range(30, 95)]
        [Display(Name = "Value Area (%)", Description = "Percent of session volume enclosed by the value area (VAH/VAL).", Order = 5, GroupName = "01. Profile")]
        public int ValueAreaPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Tick Volume", Description = "Build the session profile (wall / POC / value area / HVN) from actual trade prints for exact volume-at-price, instead of spreading each bar's volume uniformly across its high-low range. Recommended on tick/range charts. Realtime is always exact once live; enable the chart's Tick Replay for exact historical bars too. Falls back to bar-OHLC distribution when no ticks are available.", Order = 6, GroupName = "01. Profile")]
        public bool UseTickVolume { get; set; }

        // ===== 02. Session =====
        [NinjaScriptProperty]
        [Browsable(false)]   // hidden: sessions always anchor to Eastern and auto-adjust from the platform time zone
        [Display(Name = "Session Time Zone Id", Description = "Windows time zone the reset time is expressed in. Default Eastern (US), incl. DST.", Order = 1, GroupName = "02. Session & Value Area")]
        public string SessionTimeZoneId { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]   // hidden: the structural session profile always rolls at 18:00 ET (Globex open)
        [Display(Name = "Session Reset (HH:mm)", Description = "Time of day (reference zone) the structural profile resets and the trading day rolls. 18:00 = CME Globex open.", Order = 2, GroupName = "02. Session & Value Area")]
        public string SessionResetTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Value Area", Order = 3, GroupName = "02. Session & Value Area")]
        public bool ShowValueArea { get; set; }

        [XmlIgnore]
        [Display(Name = "Value Area Color", Order = 4, GroupName = "02. Session & Value Area")]
        public Brush VaColor { get; set; }
        [Browsable(false)]
        public string VaColorSerialize
        {
            get { return Serialize.BrushToString(VaColor); }
            set { VaColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Value Area Style", Order = 5, GroupName = "02. Session & Value Area")]
        public DashStyleHelper VaStyle { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Value Area Opacity", Order = 6, GroupName = "02. Session & Value Area")]
        public int VaOpacity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Prior Sessions To Show", Description = "Number of completed prior sessions to draw as VAH/VAL/POC reference lines (0 = none).", Order = 7, GroupName = "02. Session & Value Area")]
        public int PriorSessionsToShow { get; set; }

        [XmlIgnore]
        [Display(Name = "Prior VA Color", Order = 8, GroupName = "02. Session & Value Area")]
        public Brush PriorVaColor { get; set; }
        [Browsable(false)]
        public string PriorVaColorSerialize
        {
            get { return Serialize.BrushToString(PriorVaColor); }
            set { PriorVaColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Prior VA Opacity", Order = 9, GroupName = "02. Session & Value Area")]
        public int PriorVaOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Prior Day POC Style", Order = 10, GroupName = "02. Session & Value Area")]
        public DashStyleHelper PriorDayPocStyle { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Prior Day VA Style", Order = 11, GroupName = "02. Session & Value Area")]
        public DashStyleHelper PriorDayVaStyle { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Prior Day H/L Style", Order = 12, GroupName = "02. Session & Value Area")]
        public DashStyleHelper PriorDayHLStyle { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Prior High/Low", Description = "Draw each shown prior session's high and low as PDH/PDL reference lines (pdH / pdL).", Order = 13, GroupName = "02. Session & Value Area")]
        public bool ShowPriorHL { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Line Length (bars)", Description = "Global length for ALL horizontal reference lines (session VA/POC, prior day, weekly, prior week, monthly). 0 = full chart width. (LVN rails have their own projection in group 07.)", Order = 7, GroupName = "09. Levels")]
        public int LineProjectionBars { get; set; }

        // ===== 03. Detection =====
        [Display(Name = "Rail Source", Description = "What sources the rails. Confirmed Pivots = swing reversals that landed in thin volume (CPR). Volume Valleys = predictive LVN troughs. Both = draw each; a valley confirmed by a pivot graduates to CPR.", Order = 1, GroupName = "04. Detection")]
        public RailSourceEnum RailSource { get; set; }

        [NinjaScriptProperty]
        [Range(2, 30)]
        [Display(Name = "Pivot Strength (bars)", Description = "Bars required on each side of a swing for a pivot to confirm. Higher = fewer, more significant reversals (and more lag before they appear).", Order = 2, GroupName = "04. Detection")]
        public int PivotStrength { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 1.0)]
        [Display(Name = "Pivot Volume Factor", Description = "How thin the reversal must be: the volume in the pivot's band must be below this fraction of the surrounding area's average. Lower = stricter (only true voids). 0.85 = at least 15% thinner than its surroundings.", Order = 3, GroupName = "04. Detection")]
        public double PivotVolumeFactor { get; set; }

        [NinjaScriptProperty]
        [Range(4, 400)]
        [Display(Name = "Pivot Volume Window (ticks)", Description = "Half-width (in ticks) of the surrounding area the pivot's local volume is compared against.", Order = 4, GroupName = "04. Detection")]
        public int PivotVolumeWindow { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Smoothing (bins)", Description = "Moving-average radius applied to the scan histogram before LVN detection. 0 = off. Removes one-bin noise so troughs are found in the shape.", Order = 5, GroupName = "04. Detection")]
        public int SmoothBins { get; set; }

        [NinjaScriptProperty]
        [Range(4, 2000)]
        [Display(Name = "LVN Flank Window (ticks)", Description = "How far (in ticks) to search either side of a trough for a higher-volume wall. Distance-anchored so it behaves the same across quiet and wide ranges.", Order = 6, GroupName = "04. Detection")]
        public int LvnFlankTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Wall Min Fraction", Description = "Gentle noise floor: a valley is ignored only if BOTH flanking walls hold less than this fraction of the scan POC. 0 = off (keep thin-tail valleys).", Order = 7, GroupName = "04. Detection")]
        public double WallMinFraction { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 0.95)]
        [Display(Name = "LVN Valley Factor", Description = "A valley qualifies only if it sits below this fraction of the lower flanking wall. Lower = stricter/deeper valleys.", Order = 8, GroupName = "04. Detection")]
        public double LvnValleyFactor { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "LVN Strong Depth", Description = "Valleys at or above this prominence render as strong/bright rails; shallower ones render as weak.", Order = 9, GroupName = "04. Detection")]
        public double LvnStrongDepth { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 1.0)]
        [Display(Name = "HVN Fraction", Description = "Profile rows at or above this fraction of the POC volume are treated as high-volume no-fly nodes.", Order = 10, GroupName = "04. Detection")]
        public double HvnFraction { get; set; }

        [Display(Name = "HVN Local Nodes", Description = "Also flag secondary/tertiary shelves that stand out relative to their LOCAL surroundings, not just rows near the global POC. Off = classic behavior (only rows >= HVN Fraction of the single biggest node). On = hybrid: global gate OR local prominence.", Order = 5, GroupName = "06. HVN Zones")]
        public bool HvnLocalNodes { get; set; }

        [Range(0.02, 0.5)]
        [Display(Name = "HVN Local Prominence", Description = "How far a bump must rise above its deeper flanking valley to count, as a fraction of POC volume. This is measured against the LOWER of the two valleys, so shelves sitting on the flank of a dominant POC still qualify. Lower = more/smaller shelves flagged (more sensitive); higher = only bold bumps. Only used when HVN Local Nodes is on.", Order = 6, GroupName = "06. HVN Zones")]
        public double HvnLocalProminence { get; set; }

        [Range(2, 40)]
        [Display(Name = "HVN Local Window (rows)", Description = "Search radius, in profile rows, for a shelf's peak and its flanking valleys. It should comfortably exceed your shelf height: too small and a wide shelf's flanks fall outside the window so nothing seeds. Larger = only broader, more separated humps register as distinct. Only used when HVN Local Nodes is on.", Order = 7, GroupName = "06. HVN Zones")]
        public int HvnLocalWindow { get; set; }

        [Range(0.0, 0.9)]
        [Display(Name = "HVN Local Floor", Description = "Absolute floor for local nodes, as a fraction of POC volume. Rows below this are never flagged, even if locally prominent - keeps the dead tail of the profile quiet. Only used when HVN Local Nodes is on.", Order = 8, GroupName = "06. HVN Zones")]
        public double HvnFloorFraction { get; set; }

        [Display(Name = "HVN Value-Area Edges", Description = "How each node's zone edges are placed. Off = threshold edges (the band spans wherever the node stays above the detection gate - quantized to profile rows). On = local value-area edges: each node's band covers the central HVN Node VA% of that node's OWN volume, expanded outward from its peak. VA edges hug the meat of each shelf and don't cut every node at the same absolute height.", Order = 9, GroupName = "06. HVN Zones")]
        public bool HvnVaEdges { get; set; }

        [Range(40, 95)]
        [Display(Name = "HVN Node VA %", Description = "Percent of a node's own volume its zone should contain when HVN Value-Area Edges is on. Higher = taller/wider bands; ~75-80 gives a slightly wider-than-classic no-fly wall. Ignored when VA Edges is off.", Order = 10, GroupName = "06. HVN Zones")]
        public int HvnNodeVaPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Fill Buffer (ticks)", Description = "How far beyond a level a bar must close (on the opposite side) to count it as filled and retire it.", Order = 11, GroupName = "04. Detection")]
        public int FillBufferTicks { get; set; }

        [NinjaScriptProperty]
        [Range(10, 2000)]
        [Display(Name = "Max Tracked Levels", Description = "Upper bound on persistent levels kept in memory; oldest are dropped beyond this.", Order = 12, GroupName = "04. Detection")]
        public int MaxTrackedLevels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Score Reactions", Description = "Earn weight for levels that price has returned to and respected. Each distinct hold (touched, then left without closing through) and its push-off strength build a 0..1 score that brightens the rail/zone and appends a hold-count tag (e.g. x3). Requires Persist Naked Levels for rails. Non-destructive - it only emphasises, never hides.", Order = 13, GroupName = "04. Detection")]
        public bool ScoreReactions { get; set; }

        [NinjaScriptProperty]
        [Range(0, 60)]
        [Display(Name = "Reaction Opacity Boost", Description = "Extra opacity (0-60) added to a fully-earned level, scaled by its score. 0 = tag only, no brightness change.", Order = 14, GroupName = "04. Detection")]
        public int ReactionOpacityBoost { get; set; }

        // ===== 03. Wall =====
        [NinjaScriptProperty]
        [Display(Name = "Show Wall", Order = 1, GroupName = "03. Wall")]
        public bool ShowWall { get; set; }

        [Display(Name = "Wall Style", Description = "Smooth = continuous terrain silhouette. Stepped = one block per row.", Order = 2, GroupName = "03. Wall")]
        public WallStyleEnum WallStyle { get; set; }

        [NinjaScriptProperty]
        [Range(10, 600)]
        [Display(Name = "Wall Max Depth (px)", Description = "How far the POC row protrudes left. Other rows scale by volume.", Order = 3, GroupName = "03. Wall")]
        public int WallMaxDepth { get; set; }

        [XmlIgnore]
        [Display(Name = "Wall Color", Order = 5, GroupName = "03. Wall")]
        public Brush WallColor { get; set; }
        [Browsable(false)]
        public string WallColorSerialize
        {
            get { return Serialize.BrushToString(WallColor); }
            set { WallColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Wall Opacity", Order = 6, GroupName = "03. Wall")]
        public int WallOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Color Wall by Value Area", Description = "Color each wall row by whether it sits inside the current session's value area (VAH-VAL) or outside it. Takes precedence over the polarity tint, which now lives on the outer strip.", Order = 7, GroupName = "03. Wall")]
        public bool WallVaColoring { get; set; }

        [XmlIgnore]
        [Display(Name = "Wall In-VA Color", Order = 8, GroupName = "03. Wall")]
        public Brush WallInVaColor { get; set; }
        [Browsable(false)]
        public string WallInVaColorSerialize
        {
            get { return Serialize.BrushToString(WallInVaColor); }
            set { WallInVaColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Wall Out-VA Color", Order = 9, GroupName = "03. Wall")]
        public Brush WallOutVaColor { get; set; }
        [Browsable(false)]
        public string WallOutVaColorSerialize
        {
            get { return Serialize.BrushToString(WallOutVaColor); }
            set { WallOutVaColor = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Wall Gradient (Heat Map)", Description = "Layer a continuous per-row heat map UNDERNEATH the wall, exactly where an at-the-wall HVN zone renders: dense volume (HVN) in the HVN color, thin volume (LVN) in the LVN color, opacity scaling with how extreme the row is. The wall itself (flat / VA coloring / Delta Split / polarity) still draws on top, so the terrain silhouette stays fully visible. No detection, no thresholds, no bands.", Order = 10, GroupName = "03. Wall")]
        public bool WallGradient { get; set; }

        [XmlIgnore]
        [Display(Name = "Gradient HVN Color", Description = "Color for dense (high-volume) rows. The denser the row, the more opaque.", Order = 11, GroupName = "03. Wall")]
        public Brush GradientHvnColor { get; set; }
        [Browsable(false)]
        public string GradientHvnColorSerialize
        {
            get { return Serialize.BrushToString(GradientHvnColor); }
            set { GradientHvnColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Gradient LVN Color", Description = "Color for thin (low-volume) rows. The thinner the row, the more opaque.", Order = 12, GroupName = "03. Wall")]
        public Brush GradientLvnColor { get; set; }
        [Browsable(false)]
        public string GradientLvnColorSerialize
        {
            get { return Serialize.BrushToString(GradientLvnColor); }
            set { GradientLvnColor = Serialize.StringToBrush(value); }
        }

        [Range(0, 100)]
        [Display(Name = "Gradient Max Opacity", Description = "Opacity at the extremes: the POC row and the thinnest traded row both render at this opacity (in their respective colors).", Order = 13, GroupName = "03. Wall")]
        public int GradientMaxOpacity { get; set; }

        [Range(0, 100)]
        [Display(Name = "Gradient Min Opacity", Description = "Underlay floor at the neutral middle (average-volume rows). Default 0 = neutral rows draw nothing and only meaningful red/green terrain shades the wall footprint. Raise for a faint full-band tint behind the whole wall.", Order = 14, GroupName = "03. Wall")]
        public int GradientMinOpacity { get; set; }

        [Range(0, 100)]
        [Display(Name = "Gradient Sensitivity", Description = "How aggressively rows pick up color as they move away from the neutral middle. 50 = linear ramp. Higher = color builds quickly, more of the profile visibly shaded (busier, catches subtle nodes). Lower = only the true extremes light up (cleaner, POC and the deepest LVNs only). Applies to both the wall footprint and the extension.", Order = 15, GroupName = "03. Wall")]
        public int GradientSensitivity { get; set; }

        [Range(0, 100)]
        [Display(Name = "Gradient Crossover (HVN/LVN Split)", Description = "The score at which red flips to green - the HVN/LVN classification boundary. 50 = the middle of the score range. RAISE it if thin rows are still being marked red: rows must score higher to qualify as HVN, so borderline shelves drop into green. LOWER it and red reaches further down the profile. Full-opacity red always lands on the POC and full-opacity green on the thinnest row, wherever the boundary sits.", Order = 16, GroupName = "03. Wall")]
        public int GradientCrossover { get; set; }

        [Range(0, 100)]
        [Display(Name = "Gradient Rank Weight (%)", Description = "How each row's heat is scored. 100 = pure percentile rank among traded rows (secondary shelves stay red even under a monster POC, but flat plateaus pick up an artificial spread). 0 = pure linear volume vs POC (true magnitude, but one dominant POC turns everything else green). Default 60 blends both.", Order = 17, GroupName = "03. Wall")]
        public int GradientRankWeight { get; set; }

        [Range(0, 500)]
        [Display(Name = "Gradient Extend (bars)", Description = "Project each row's heat color this many bars left of the wall over the price action. Deliberately separate from Line Projection (bars) - set levels longer so a level nestled in an LVN sticks out past the shading. 0 = wall only (or, with Gradient Across Chart on, the whole canvas at the reduced across alpha).", Order = 18, GroupName = "03. Wall")]
        public int GradientExtendBars { get; set; }

        [Range(0, 100)]
        [Display(Name = "Gradient Extend Opacity (%)", Description = "Extension alpha as a percentage of the row's footprint alpha. The extension is never floored: neutral rows never reach the candles, only meaningful red/green terrain does.", Order = 19, GroupName = "03. Wall")]
        public int GradientExtendOpacity { get; set; }

        [Display(Name = "Gradient Across Chart", Description = "Keep each row's heat color running to the left edge of the chart, past where Gradient Extend (bars) stops. Everything beyond that stopping point is drawn at Gradient Across Opacity, so the near-wall extension keeps its emphasis. With Extend (bars) = 0 the whole canvas shades at the reduced alpha.", Order = 20, GroupName = "03. Wall")]
        public bool GradientAcrossChart { get; set; }

        [Range(0, 100)]
        [Display(Name = "Gradient Across Opacity (%)", Description = "Alpha of the across-chart band, as a percentage of the row's footprint alpha - the same scale Gradient Extend Opacity uses. Independent of the extension: 100 = the across band is as strong as the wall footprint, 0 = it draws nothing.", Order = 21, GroupName = "03. Wall")]
        public int GradientAcrossOpacity { get; set; }

        [Display(Name = "Gradient Obeys Proximity", Description = "Make the gradient's extension and across-chart bands honor Proximity Reveal (09. Levels), so a row only shades while the live price is within Reveal Distance of it. Off by default: the heat map is terrain, not a level line, and stays painted wherever price is. Has no effect unless Proximity Reveal is on.", Order = 22, GroupName = "03. Wall")]
        public bool GradientObeyProximity { get; set; }

        // ===== 04. HVN Zones =====
        [NinjaScriptProperty]
        [Display(Name = "Show HVN Zones", Order = 1, GroupName = "06. HVN Zones")]
        public bool ShowHvnZones { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "HVN Zone Across Chart", Description = "Extend the faint no-fly shading across the price action (vs only at the wall).", Order = 2, GroupName = "06. HVN Zones")]
        public bool HvnZoneAcrossChart { get; set; }

        [XmlIgnore]
        [Display(Name = "HVN Color", Order = 3, GroupName = "06. HVN Zones")]
        public Brush HvnColor { get; set; }
        [Browsable(false)]
        public string HvnColorSerialize
        {
            get { return Serialize.BrushToString(HvnColor); }
            set { HvnColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "HVN Zone Opacity", Description = "Peak opacity of the no-fly shading (scaled down for lesser nodes).", Order = 4, GroupName = "06. HVN Zones")]
        public int HvnZoneOpacity { get; set; }

        // ===== 05. POC =====
        [NinjaScriptProperty]
        [Display(Name = "Show POC", Order = 1, GroupName = "05. POC")]
        public bool ShowPOC { get; set; }

        [XmlIgnore]
        [Display(Name = "POC Color", Order = 2, GroupName = "05. POC")]
        public Brush PocColor { get; set; }
        [Browsable(false)]
        public string PocColorSerialize
        {
            get { return Serialize.BrushToString(PocColor); }
            set { PocColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "POC Line Style", Order = 3, GroupName = "05. POC")]
        public DashStyleHelper PocStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "POC Thickness", Order = 4, GroupName = "05. POC")]
        public int PocThickness { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "POC Opacity", Order = 5, GroupName = "05. POC")]
        public int PocOpacity { get; set; }

        // ===== 06. LVN Rails =====
        [NinjaScriptProperty]
        [Display(Name = "Show Rails", Order = 1, GroupName = "07. LVN Rails")]
        public bool ShowRails { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Persist Naked Levels", Description = "Keep detected LVNs alive past the rolling window: naked until price returns, retired once a bar closes through them. Off = window-only (ephemeral) rails.", Order = 15, GroupName = "07. LVN Rails")]
        public bool PersistLevels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Tested Levels", Description = "Show levels that price has wicked into but not closed through (dimmed). Only applies when persistence is on.", Order = 16, GroupName = "07. LVN Rails")]
        public bool ShowTested { get; set; }

        [Display(Name = "Rail Render Mode", Description = "FullSpan = rail crosses the whole chart. Projection = fixed tongue out of the wall. Both = dim full-span map + bright projection.", Order = 2, GroupName = "07. LVN Rails")]
        public RailRenderModeEnum RailRenderMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Rail Projection (bars)", Description = "Tongue length in Projection / Both modes, measured left from the current bar.", Order = 3, GroupName = "07. LVN Rails")]
        public int RailProjectionBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Weak Rails", Description = "Also draw the shallower (lower-conviction) LVNs.", Order = 4, GroupName = "07. LVN Rails")]
        public bool ShowWeakRails { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Strong Rail Style", Order = 5, GroupName = "07. LVN Rails")]
        public DashStyleHelper RailStrongStyle { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Weak Rail Style", Order = 6, GroupName = "07. LVN Rails")]
        public DashStyleHelper RailWeakStyle { get; set; }

        [XmlIgnore]
        [Display(Name = "Rail Color", Description = "Used when side-coloring is off.", Order = 7, GroupName = "07. LVN Rails")]
        public Brush RailColor { get; set; }
        [Browsable(false)]
        public string RailColorSerialize
        {
            get { return Serialize.BrushToString(RailColor); }
            set { RailColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Side Coloring", Description = "Color a rail by which side price is on: support when price is above, resistance when below.", Order = 8, GroupName = "07. LVN Rails")]
        public bool UseSideColoring { get; set; }

        [XmlIgnore]
        [Display(Name = "Support Color (price above)", Order = 9, GroupName = "07. LVN Rails")]
        public Brush RailSupportColor { get; set; }
        [Browsable(false)]
        public string RailSupportColorSerialize
        {
            get { return Serialize.BrushToString(RailSupportColor); }
            set { RailSupportColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Resistance Color (price below)", Order = 10, GroupName = "07. LVN Rails")]
        public Brush RailResistanceColor { get; set; }
        [Browsable(false)]
        public string RailResistanceColorSerialize
        {
            get { return Serialize.BrushToString(RailResistanceColor); }
            set { RailResistanceColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Rail Thickness", Order = 11, GroupName = "07. LVN Rails")]
        public int RailThickness { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Rail Opacity", Order = 12, GroupName = "07. LVN Rails")]
        public int RailOpacity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Dim Opacity (Both mode)", Description = "Opacity of the faint full-span layer in Both mode.", Order = 13, GroupName = "07. LVN Rails")]
        public int DimOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Entry Chevron", Order = 14, GroupName = "07. LVN Rails")]
        public bool ShowChevron { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Anchor CPR to Source", Description = "Draw each Confirmed Pivot Reversal from its originating swing bar to the wall, so you can see the source and how long it has held. Off = use the standard rail projection.", Order = 21, GroupName = "07. LVN Rails")]
        public bool ShowPivotSource { get; set; }

        [Display(Name = "CPR Flip On Breakthrough", Description = "Give CPRs the same mitigation lifecycle as the FRVP zones: a close through the level flips its polarity (broken resistance becomes support) and re-arms it, instead of retiring it. Off = legacy behavior, the first close-through retires the level.", Order = 22, GroupName = "07. LVN Rails")]
        public bool CprFlipOnBreakthrough { get; set; }

        [Range(0, 20)]
        [Display(Name = "  CPR Max Flips", Description = "How many times a CPR may flip its polarity on a close-through before it is retired instead of re-armed. 0 = never flip (any clean close-through retires it immediately). Only applies when CPR Flip On Breakthrough is on.", Order = 23, GroupName = "07. LVN Rails")]
        public int CprMaxFlips { get; set; }

        [Range(0, 50)]
        [Display(Name = "  CPR Retire Touches", Description = "Retire a CPR after this many distinct touches (tagged repeatedly = liquidity consumed). 0 = never retire on touches. The rail must first clear its working side before returns are counted.", Order = 24, GroupName = "07. LVN Rails")]
        public int CprRetireTouches { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Extend Past Chevron", Description = "In Both mode, draw the faint full-span line that continues left past the chevron. Off = bright projection only.", Order = 20, GroupName = "07. LVN Rails")]
        public bool ShowDimExtension { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Combine Nearby Into Zones", Description = "Merge rails within the combine threshold into a single filled zone band with one label.", Order = 17, GroupName = "07. LVN Rails")]
        public bool CombineRails { get; set; }

        [NinjaScriptProperty]
        [Range(1, 400)]
        [Display(Name = "Zone Combine (ticks)", Description = "Rails within this many ticks of each other collapse into one zone. A zone never spans more than this.", Order = 18, GroupName = "07. LVN Rails")]
        public int RailCombineTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Zone Fill Opacity", Order = 19, GroupName = "07. LVN Rails")]
        public int ZoneOpacity { get; set; }

        // ===== 07. Labels =====
        [NinjaScriptProperty]
        [Display(Name = "Show Labels", Order = 1, GroupName = "09. Levels")]
        public bool ShowLabels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Prices", Order = 2, GroupName = "09. Levels")]
        public bool ShowPrices { get; set; }

        [NinjaScriptProperty]
        [Range(6, 24)]
        [Display(Name = "Label Font Size", Order = 3, GroupName = "09. Levels")]
        public int LabelFontSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Proximity Reveal", Description = "Hide a level's LINE unless price is within Reveal Distance of it; labels always stay. Keeps the chart clean while the full map remains readable in the gutter. Off = all lines always drawn.", Order = 4, GroupName = "09. Levels")]
        public bool ProximityReveal { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Reveal Distance (points)", Description = "How close (in price points) price must be to a level for its line to appear. ~50 for MNQ; lower for MGC/MES.", Order = 5, GroupName = "09. Levels")]
        public double RevealDistance { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hover Reveal", Description = "Hovering a label's row extends that level's line (back to its source for CPRs), even when price is far away. Disable if the hover hit-test misbehaves on your display.", Order = 6, GroupName = "09. Levels")]
        public bool HoverReveal { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Merge Levels (lines)", Description = "Collapse stacked reference LINES into one line at their average price. POC (all variants), the current session VAH/VAL, and CPR/LVN rails are exempt. This no longer has any effect on labels - label collision is handled by Group Stacked Labels, which never moves a line. Off by default: an averaged line sits at a price that does not exist.", Order = 8, GroupName = "09. Levels")]
        public bool MergeLevels { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Merge Distance (ticks)", Description = "Reference levels within this many ticks of each other merge into one.", Order = 9, GroupName = "09. Levels")]
        public int MergeDistanceTicks { get; set; }

        [XmlIgnore]
        [Display(Name = "Merge Color", Order = 10, GroupName = "09. Levels")]
        public Brush MergeColor { get; set; }
        [Browsable(false)]
        public string MergeColorSerialize
        {
            get { return Serialize.BrushToString(MergeColor); }
            set { MergeColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Merge Opacity", Order = 11, GroupName = "09. Levels")]
        public int MergeOpacity { get; set; }

        [NinjaScriptProperty]

        // ---- Label layer ----

        [Display(Name = "Label Backdrop", Description = "Draw an opaque chip behind every gutter label so its glyphs never blend into the wall gradient, a zone fill, or another label. Cheap, and independent of grouping.", Order = 13, GroupName = "09. Levels")]
        public bool ShowLabelBackdrop { get; set; }

        [XmlIgnore]
        [Display(Name = "  Backdrop Color", Description = "Fill colour of the label backdrop chip. Set this to your chart background for a label that reads as a hole punched through the profile; set it darker for a chip that floats above it.", Order = 14, GroupName = "09. Levels")]
        public Brush LabelBackdropColor { get; set; }
        [Browsable(false)]
        public string LabelBackdropColorSerialize
        {
            get { return Serialize.BrushToString(LabelBackdropColor); }
            set { LabelBackdropColor = Serialize.StringToBrush(value); }
        }

        [Range(0, 100)]
        [Display(Name = "  Backdrop Opacity", Description = "Opacity of the label backdrop chip. At 100 the text sits on a solid block; lower values let the profile bleed through.", Order = 15, GroupName = "09. Levels")]
        public int LabelBackdropOpacity { get; set; }

        [Display(Name = "Group Stacked Labels", Description = "When several labels land on the same row (a pixel-space test, not a tick one) collapse them into one chip: the highest-priority tag, a coloured dot per member, and a +N count. Every line still draws at its exact price. Hover the chip to see all members with their exact, un-averaged prices. Priority order: CPR > POC > value area > LVN > FRVP > HTF > session > reference > merged.", Order = 16, GroupName = "09. Levels")]
        public bool GroupStackedLabels { get; set; }

        [Range(0, 12)]
        [Display(Name = "  Cluster Padding (px)", Description = "Extra pixels beyond one text height before two labels are considered to share a row. 0 = collapse only on true overlap.", Order = 17, GroupName = "09. Levels")]
        public int LabelClusterPadPx { get; set; }

        [Display(Name = "  Member Dots", Description = "Show one small colour-coded square per group member on the chip, so the families in a cluster read at a glance without hovering.", Order = 18, GroupName = "09. Levels")]
        public bool ShowGroupDots { get; set; }

        [Display(Name = "  Group Hover Card", Description = "Hovering a collapsed chip pops up a card listing every level on that row - tag, exact price, and whether it is dimmed (tested / mitigated).", Order = 19, GroupName = "09. Levels")]
        public bool LabelGroupTooltip { get; set; }

        [Display(Name = "  Expand All Groups", Description = "Instead of collapsing, fan every member of a group apart vertically so all of them are readable at once. A small stub marks each label's true row. Use for screenshots, replay review, and anything you cannot hover.", Order = 20, GroupName = "09. Levels")]
        public bool ExpandAllGroups { get; set; }

        [Display(Name = "  Pinned Tags", Description = "Comma-separated label tags that always win the chip when grouped, regardless of family priority - and regardless of being tested or mitigated. Order matters: the first listed tag wins a group containing two pins. Pinned members get a white ring on their dot so you can see a second pin without hovering. Tags are matched exactly: pdH, pdL, pdPOC, wPOC, VAH, CPR, POC. Leave blank to use family priority alone.", Order = 21, GroupName = "09. Levels")]
        public string LabelPinTags { get; set; }

        [XmlIgnore]
        [Display(Name = "Hover Card Color", Description = "Background of every hover pop-up: the group card, the FRVP zone card, and the out-of-value card.", Order = 22, GroupName = "09. Levels")]
        public Brush TooltipBackColor { get; set; }
        [Browsable(false)]
        public string TooltipBackColorSerialize
        {
            get { return Serialize.BrushToString(TooltipBackColor); }
            set { TooltipBackColor = Serialize.StringToBrush(value); }
        }

        [Range(0, 100)]
        [Display(Name = "  Hover Card Opacity", Order = 23, GroupName = "09. Levels")]
        public int TooltipBackOpacity { get; set; }

        // ===== 10. Ghost Profile =====
        [NinjaScriptProperty]
        [Display(Name = "Show Ghost Profile", Description = "Master switch for the prior-session ghost (silhouette + its VAH/VAL/POC levels). Appears only while price is inside a prior session's value area.", Order = 1, GroupName = "13. Ghost Profile")]
        public bool ShowGhostProfile { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Silhouette", Description = "Draw the dim volume-profile shape. Turn off to keep only the VAH/VAL/POC levels.", Order = 2, GroupName = "13. Ghost Profile")]
        public bool ShowGhostSilhouette { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Ghost Levels", Description = "Draw the ghost session's VAH/VAL/POC as full reference lines (same proximity reveal + persistent labels as the other levels), labelled with the session date.", Order = 3, GroupName = "13. Ghost Profile")]
        public bool ShowGhostLevels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Silhouette Faces Right", Description = "Right = volume grows toward current price (POC bulge points at the bar). Left = grows away. For positioning.", Order = 4, GroupName = "13. Ghost Profile")]
        public bool GhostFaceRight { get; set; }

        [XmlIgnore]
        [Display(Name = "Silhouette Color", Order = 5, GroupName = "13. Ghost Profile")]
        public Brush GhostColor { get; set; }
        [Browsable(false)]
        public string GhostColorSerialize
        {
            get { return Serialize.BrushToString(GhostColor); }
            set { GhostColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(2, 60)]
        [Display(Name = "Silhouette Opacity", Description = "Very low keeps it a faint ghost.", Order = 6, GroupName = "13. Ghost Profile")]
        public int GhostOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "Ghost Level Color", Order = 7, GroupName = "13. Ghost Profile")]
        public Brush GhostLevelColor { get; set; }
        [Browsable(false)]
        public string GhostLevelColorSerialize
        {
            get { return Serialize.BrushToString(GhostLevelColor); }
            set { GhostLevelColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Ghost Level Opacity", Order = 8, GroupName = "13. Ghost Profile")]
        public int GhostLevelOpacity { get; set; }

        [NinjaScriptProperty]
        [Range(30, 600)]
        [Display(Name = "Silhouette Width (px)", Description = "Maximum horizontal depth of the silhouette in pixels.", Order = 9, GroupName = "13. Ghost Profile")]
        public int GhostWidthPx { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 0.9)]
        [Display(Name = "Silhouette Position", Description = "Where the silhouette's spine sits, as a fraction of chart width left of the wall. Viewport-anchored, so it stays put across timeframes/zoom.", Order = 10, GroupName = "13. Ghost Profile")]
        public double GhostPosition { get; set; }

        [NinjaScriptProperty]
        [Range(0, 40)]
        [Display(Name = "Ghost Edge Hysteresis (ticks)", Description = "Price must exit the value area by this many ticks to hide the ghost (and re-enter to show it), so it doesn't flicker as price wicks across VAH/VAL.", Order = 11, GroupName = "13. Ghost Profile")]
        public int GhostHysteresisTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 120)]
        [Display(Name = "Ghost Lookback (sessions)", Description = "How many prior sessions to retain and search. The ghost shows the most-recent retained session whose value area contains price - so a session from days/weeks ago can reappear when price trades back into its range.", Order = 12, GroupName = "13. Ghost Profile")]
        public int GhostLookback { get; set; }

        // ===== 11. FRVP Zones =====
        [NinjaScriptProperty]
        [Display(Name = "Show FRVP Zones", Description = "Auto-detect consolidation bases and profile each into a fixed-range value area. The source-bar outline is always shown; the VA projection reveals when price is within the reveal distance and ends at the strip.", Order = 1, GroupName = "15. FRVP Zones")]
        public bool ShowFrvpZones { get; set; }

        [Display(Name = "Show Out-of-Value FRVP", Description = "Detect FRVP zones on an intraday timeframe and show only those whose POC sits OUTSIDE today's developing value area (above VAH or below VAL) - potential reversal shelves back toward the session POC. Zones inside value are hidden; the test is re-checked live as the VA develops. Pulls its own history via BarsRequest - takes effect on reload.", Order = 40, GroupName = "15. FRVP Zones")]
        public bool ShowFrvpOutOfVa { get; set; }

        [Range(1, 60)]
        [Display(Name = "  Out-of-Value Timeframe (min)", Description = "The intraday bar size the out-of-value FRVP zones are detected on. 15 = 'what a 15-minute chart would show'.", Order = 41, GroupName = "15. FRVP Zones")]
        public int FrvpVaMinutes { get; set; }

        [Range(0.5, 6.0)]
        [Display(Name = "  Out-of-Value ATR Mult", Description = "The out-of-value counterpart to Max Height (ATR) - the same box-height gate, measured on the out-of-value timeframe's own ATR rather than the primary chart's. Set it equal to Max Height (ATR) for identical boxes. Ignored when Height Mode is FixedTicks, in which case Max Height (Ticks) applies to both.", Order = 42, GroupName = "15. FRVP Zones")]
        public double FrvpVaAtrMult { get; set; }

        [Range(1, 120)]
        [Display(Name = "  Out-of-Value Lookback (days)", Description = "How many days of history to scan for out-of-value FRVP zones (includes today). Takes effect on reload.", Order = 43, GroupName = "15. FRVP Zones")]
        public int FrvpVaLookbackDays { get; set; }

        [Display(Name = "  Out-of-Value Across Chart", Description = "On = draw the out-of-value zones full-width across the screen (default), so these reversal shelves are visible everywhere. Off = draw them only from the line-projection width in to the wall, like the other levels.", Order = 44, GroupName = "15. FRVP Zones")]
        public bool FrvpOutOfVaAcrossChart { get; set; }

        [XmlIgnore]
        [Display(Name = "  Out-of-Value VA Color", Description = "Color of the VAH/VAL band lines (and fill) for out-of-value zones. Defaults to the FRVP VA color so they resemble the standard zones.", Order = 45, GroupName = "15. FRVP Zones")]
        public Brush FrvpVaZoneColor { get; set; }
        [Browsable(false)]
        public string FrvpVaZoneColorSerialize
        {
            get { return Serialize.BrushToString(FrvpVaZoneColor); }
            set { FrvpVaZoneColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "  Out-of-Value POC Color", Description = "Color of the POC line and label for out-of-value zones. Defaults to the FRVP POC color.", Order = 46, GroupName = "15. FRVP Zones")]
        public Brush FrvpVaPocColor { get; set; }
        [Browsable(false)]
        public string FrvpVaPocColorSerialize
        {
            get { return Serialize.BrushToString(FrvpVaPocColor); }
            set { FrvpVaPocColor = Serialize.StringToBrush(value); }
        }

        [Display(Name = "  Out-of-Value Edge Style", Description = "Line style of the VAH/VAL band lines for out-of-value zones (default Dash, matching FRVP).", Order = 47, GroupName = "15. FRVP Zones")]
        public DashStyleHelper FrvpVaEdgeStyle { get; set; }

        [Display(Name = "  Out-of-Value POC Style", Description = "Line style of the POC line for out-of-value zones (default Solid, matching FRVP).", Order = 48, GroupName = "15. FRVP Zones")]
        public DashStyleHelper FrvpVaPocStyle { get; set; }

        [Display(Name = "  Out-of-Value VA Fill", Description = "Shade the band between VAH and VAL for out-of-value zones. Off by default since these span the full screen.", Order = 49, GroupName = "15. FRVP Zones")]
        public bool FrvpVaShowFill { get; set; }

        [Range(0, 100)]
        [Display(Name = "  Out-of-Value Opacity", Description = "Line opacity for out-of-value zones (0-100).", Order = 50, GroupName = "15. FRVP Zones")]
        public int FrvpVaOpacity { get; set; }

        [Display(Name = "  Out-of-Value Show Mitigated", Description = "Also show out-of-value zones that price has already run through (mitigated), drawn faintly and tagged '·m'. Off by default so only live reversal shelves show.", Order = 51, GroupName = "15. FRVP Zones")]
        public bool FrvpVaShowMitigated { get; set; }

        [Display(Name = "  Out-of-Value Show Weak", Description = "Show weak out-of-value zones (small push-off after forming), drawn dimmed - matches AutoFRVP's WeakZoneDisplay = Dim. Off = hide weak zones. Independent of the standard FRVP zones' weak setting.", Order = 55, GroupName = "15. FRVP Zones")]
        public bool FrvpVaShowWeak { get; set; }

        [Range(0, 20)]
        [Display(Name = "  Out-of-Value Max Flips", Description = "Retire (remove) an out-of-value zone once it has flipped polarity this many times. Same rule the chart-timeframe zones use (FRVP Max Flips). 0 = never retire on flips.", Order = 52, GroupName = "15. FRVP Zones")]
        public int FrvpVaMaxFlips { get; set; }

        [Range(0, 40)]
        [Display(Name = "  Out-of-Value Max Touches", Description = "Retire (remove) an out-of-value zone once price has returned and touched it this many separate times. Same rule the chart-timeframe zones use (FRVP Retire Touches). Note that LT replays the whole lookback at once rather than accumulating live, so over 20 days this prunes hard - set 0 to disable.", Order = 53, GroupName = "15. FRVP Zones")]
        public int FrvpVaMaxTouches { get; set; }

        [Range(0, 100)]
        [Display(Name = "  Out-of-Value Outside VA (%)", Description = "How much of an out-of-value zone's own value area must sit OUTSIDE today's developing session VA for it to stay on the chart. Re-checked every frame, so a zone is dropped as soon as the expanding session VA swallows it. 0 = any part outside keeps it visible (a zone with one tick poking past VAL survives and reads as an in-value zone). 100 = the zone must be entirely clear of the session VA.", Order = 54, GroupName = "15. FRVP Zones")]
        public int FrvpVaOutsideMinPct { get; set; }

        [Display(Name = "  Out-of-Value Hover Tooltip", Description = "Show a details pop-up (type, strength, state, % outside VA, POC/VA, volume, departure, age, touches, flips) when the cursor is over an out-of-value zone's band or its right-side label. These zones have no source box on the chart, so the band itself is the hover target.", Order = 55, GroupName = "15. FRVP Zones")]
        public bool FrvpVaTooltip { get; set; }

        [Range(1, 10)]
        [Display(Name = "  Out-of-Value Warmup (days)", Description = "Extra history pulled BEFORE the lookback window so the ATR is warm and the box detector is mid-chain when the visible window begins. Zones that form inside the pad are detected but never shown. Raise it if the oldest zones still disagree with your comparison chart.", Order = 56, GroupName = "15. FRVP Zones")]
        public int FrvpVaWarmupDays { get; set; }

        [Display(Name = "  Out-of-Value Use Chart Session", Description = "Build the out-of-value bars with the CHART's trading-hours template instead of the instrument default. The session template decides where each native bar starts and which bars exist, so if the chart you are comparing against uses a different template, every bar boundary shifts and the boxes land on different bars. Turn this on when diffing against a chart on a non-default session.", Order = 57, GroupName = "15. FRVP Zones")]
        public bool FrvpVaUseChartSession { get; set; }


        // ===== 16. Order Blocks (out-of-value / HTF) =====
        [Display(Name = "Show Order Blocks", Description = "Turn on the order-block section: reconstruct OBs from a higher (or matching) timeframe via a BarsRequest and draw them from their source candle. By default only out-of-value blocks (outside today's VA) show - enable 'OB Show In-Value' to also draw the in-value ones. An OB is the last opposing candle before a swing break (BOS/CHoCH), so it carries directional intent an FRVP shelf may not.", Order = 1, GroupName = "16. Order Blocks")]
        public bool ShowObOutOfVa { get; set; }

        [Range(1, 240)]
        [Display(Name = "OB Timeframe (min)", Description = "The intraday bar size the out-of-value order blocks are detected on. Set it to your chart's timeframe for chart-timeframe out-of-value OBs, or higher for HTF OBs.", Order = 2, GroupName = "16. Order Blocks")]
        public int ObVaMinutes { get; set; }

        [Range(0.1, 20.0)]
        [Display(Name = "OB ATR Mult", Description = "Maximum OB candle height as a multiple of the OB-timeframe ATR. Caps oversized candles from becoming order blocks. Lower = only tight, decisive origin candles qualify.", Order = 3, GroupName = "16. Order Blocks")]
        public double ObMaxAtrMult { get; set; }

        [Range(2, 50)]
        [Display(Name = "OB Swing Length", Description = "Bars on each side used to confirm the swing highs/lows whose break defines an order block. Larger = fewer, more significant OBs.", Order = 4, GroupName = "16. Order Blocks")]
        public int ObSwingLength { get; set; }

        [Display(Name = "OB Wick Only", Description = "Draw the OB from the candle's wick to its body edge (SMC 'wick-only' style) instead of the full candle high/low.", Order = 5, GroupName = "16. Order Blocks")]
        public bool ObWickOnly { get; set; }

        [Range(1, 90)]
        [Display(Name = "OB Lookback (days)", Description = "How many days of history to scan for out-of-value order blocks (includes today). Takes effect on reload.", Order = 6, GroupName = "16. Order Blocks")]
        public int ObVaLookbackDays { get; set; }

        [Range(1, 10)]
        [Display(Name = "OB Warmup (days)", Description = "Extra history pulled BEFORE the lookback so the ATR is warm and the swing detector is mid-chain when the visible window begins. OBs that form inside the pad are detected but never shown.", Order = 7, GroupName = "16. Order Blocks")]
        public int ObVaWarmupDays { get; set; }

        [Display(Name = "OB Always Show", Description = "On = every out-of-value order block is drawn (from its source candle to the right). Off = a block stays hidden until price comes within reveal distance of it, or you hover it. Either way the box now starts at the candle that created it and never extends back before it.", Order = 8, GroupName = "16. Order Blocks")]
        public bool ObAcrossChart { get; set; }

        [XmlIgnore]
        [Display(Name = "OB Bull Color", Description = "Color for bullish (demand) order block edges and fill.", Order = 9, GroupName = "16. Order Blocks")]
        public Brush ObBullColor { get; set; }
        [Browsable(false)]
        public string ObBullColorSerialize
        {
            get { return Serialize.BrushToString(ObBullColor); }
            set { ObBullColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "OB Bear Color", Description = "Color for bearish (supply) order block edges and fill.", Order = 10, GroupName = "16. Order Blocks")]
        public Brush ObBearColor { get; set; }
        [Browsable(false)]
        public string ObBearColorSerialize
        {
            get { return Serialize.BrushToString(ObBearColor); }
            set { ObBearColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "OB Breaker Color", Description = "Color for breaker blocks (an OB that has been broken once and now acts as the opposite polarity).", Order = 11, GroupName = "16. Order Blocks")]
        public Brush ObBreakerColor { get; set; }
        [Browsable(false)]
        public string ObBreakerColorSerialize
        {
            get { return Serialize.BrushToString(ObBreakerColor); }
            set { ObBreakerColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "OB POC Color", Description = "Color of the tick-profiled candle POC line and label.", Order = 12, GroupName = "16. Order Blocks")]
        public Brush ObPocColor { get; set; }
        [Browsable(false)]
        public string ObPocColorSerialize
        {
            get { return Serialize.BrushToString(ObPocColor); }
            set { ObPocColor = Serialize.StringToBrush(value); }
        }

        [Display(Name = "OB Edge Style", Description = "Line style of the order block's top/bottom edges.", Order = 13, GroupName = "16. Order Blocks")]
        public DashStyleHelper ObEdgeStyle { get; set; }

        [Display(Name = "OB POC Style", Description = "Line style of the candle POC line.", Order = 14, GroupName = "16. Order Blocks")]
        public DashStyleHelper ObPocStyle { get; set; }

        [Display(Name = "OB Fill", Description = "Shade the order block box between its edges.", Order = 15, GroupName = "16. Order Blocks")]
        public bool ObShowFill { get; set; }

        [Range(0, 100)]
        [Display(Name = "OB Opacity", Description = "Line opacity for order blocks (0-100).", Order = 16, GroupName = "16. Order Blocks")]
        public int ObOpacity { get; set; }

        [Display(Name = "OB Show Breakers", Description = "Show breaker blocks (an OB broken once, now acting as the opposite polarity), drawn in the breaker color. Off = only live order blocks show. A second break through the breaker removes it either way.", Order = 17, GroupName = "16. Order Blocks")]
        public bool ObShowBreakers { get; set; }

        [Display(Name = "OB Show Mitigated", Description = "What happens when price returns and taps an order block (uses it). Off (default) = remove it - a tapped block has done its job, keeping the chart clean. On = keep it, drawn faintly and tagged, so you can see where price has already reacted. Either way, an OB that is CLOSED THROUGH twice (OB -> breaker -> gone) is removed.", Order = 18, GroupName = "16. Order Blocks")]
        public bool ObShowMitigated { get; set; }

        [Range(0, 100)]
        [Display(Name = "OB Outside VA (%)", Description = "How much of an order block's box must sit OUTSIDE today's developing session VA to count as an out-of-value (reversal-shelf) block. Re-checked every frame. 0 = any part outside; 100 = the box must be entirely clear of the session VA.", Order = 19, GroupName = "16. Order Blocks")]
        public int ObOutsideMinPct { get; set; }

        [Display(Name = "OB Show In-Value", Description = "Also show order blocks that sit INSIDE today's value area, not just the out-of-value reversal shelves. Off (default) = out-of-value only. On = every block prints, including the in-value ones.", Order = 20, GroupName = "16. Order Blocks")]
        public bool ObShowInValue { get; set; }

        [Display(Name = "OB Dim In-Value", Description = "When in-value blocks are shown, draw them fainter so the out-of-value reversal shelves still stand out. Off = draw in-value blocks at full strength.", Order = 21, GroupName = "16. Order Blocks")]
        public bool ObInValueDim { get; set; }

        [Display(Name = "OB Hover Tooltip", Description = "Show a details pop-up (type, % outside VA, POC, box, volume, age) when the cursor is over an order block's band or its right-side label.", Order = 22, GroupName = "16. Order Blocks")]
        public bool ObTooltip { get; set; }

        [Display(Name = "OB Use Chart Session", Description = "Build the out-of-value OB bars with the CHART's trading-hours template instead of the instrument default. Turn on when the chart uses a non-default session.", Order = 23, GroupName = "16. Order Blocks")]
        public bool ObVaUseChartSession { get; set; }

        [Display(Name = "OB Debug", Description = "Print the order-block pipeline (detector survivors, in-window count, and render-side filter/draw/hover counts) to the NinjaScript Output window. For diagnosing over-counts. Leave off in normal use.", Order = 24, GroupName = "16. Order Blocks")]
        public bool ObDebug { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Show Weak Zones", Description = "Also show bases whose breakout did not depart far (weak). Off = only zones that produced a real move.", Order = 2, GroupName = "15. FRVP Zones")]
        public bool FrvpShowWeak { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show VA Fill", Description = "Shade the value-area band of the projection.", Order = 3, GroupName = "15. FRVP Zones")]
        public bool FrvpShowFill { get; set; }

        [XmlIgnore]
        [Display(Name = "Source Outline Color", Order = 4, GroupName = "15. FRVP Zones")]
        public Brush FrvpSourceColor { get; set; }
        [Browsable(false)]
        public string FrvpSourceColorSerialize
        {
            get { return Serialize.BrushToString(FrvpSourceColor); }
            set { FrvpSourceColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "VA Line Color", Order = 5, GroupName = "15. FRVP Zones")]
        public Brush FrvpVaColor { get; set; }
        [Browsable(false)]
        public string FrvpVaColorSerialize
        {
            get { return Serialize.BrushToString(FrvpVaColor); }
            set { FrvpVaColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "POC Line Color", Order = 6, GroupName = "15. FRVP Zones")]
        public Brush FrvpPocColor { get; set; }
        [Browsable(false)]
        public string FrvpPocColorSerialize
        {
            get { return Serialize.BrushToString(FrvpPocColor); }
            set { FrvpPocColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Line Opacity", Order = 7, GroupName = "15. FRVP Zones")]
        public int FrvpOpacity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 60)]
        [Display(Name = "Fill Opacity", Order = 8, GroupName = "15. FRVP Zones")]
        public int FrvpFillOpacity { get; set; }

        [Display(Name = "Height Mode", Description = "How the max base height is measured: ATR multiple (adaptive) or fixed ticks.", Order = 9, GroupName = "15. FRVP Zones")]
        public FrvpHeightModeEnum FrvpHeightMode { get; set; }

        [NinjaScriptProperty]
        [Range(4, 400)]
        [Display(Name = "Max Height (ticks)", Description = "Used when Height Mode = FixedTicks.", Order = 10, GroupName = "15. FRVP Zones")]
        public int FrvpMaxHeightTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Max Height (ATR x)", Description = "Used when Height Mode = AtrMultiple.", Order = 11, GroupName = "15. FRVP Zones")]
        public double FrvpMaxHeightAtr { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ATR Period", Order = 12, GroupName = "15. FRVP Zones")]
        public int FrvpAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Breakout Buffer (ticks)", Description = "How far beyond the base edge a close must travel to count as a breakout.", Order = 13, GroupName = "15. FRVP Zones")]
        public int FrvpBreakoutBufferTicks { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "Min Base Bars", Description = "Shortest consolidation that qualifies.", Order = 14, GroupName = "15. FRVP Zones")]
        public int FrvpMinBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Max Base Bars", Description = "Longest a base can run before it's force-closed (0 = no cap).", Order = 15, GroupName = "15. FRVP Zones")]
        public int FrvpMaxBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Close Band", Description = "Measure base height from closes (wick-tolerant) instead of highs/lows.", Order = 16, GroupName = "15. FRVP Zones")]
        public bool FrvpUseCloseBand { get; set; }

        [NinjaScriptProperty]
        [Range(4, 100)]
        [Display(Name = "Profile Rows", Order = 17, GroupName = "15. FRVP Zones")]
        public int FrvpProfileRows { get; set; }

        [NinjaScriptProperty]
        [Range(50, 90)]
        [Display(Name = "Value Area %", Order = 18, GroupName = "15. FRVP Zones")]
        public double FrvpValueAreaPct { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Min Departure (x height)", Description = "Breakout must travel at least this multiple of the VA height to count as Strong.", Order = 19, GroupName = "15. FRVP Zones")]
        public double FrvpMinDeparture { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Departure Window (bars)", Description = "Bars after the breakout over which departure is measured.", Order = 20, GroupName = "15. FRVP Zones")]
        public int FrvpDepartureBars { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Overlap Threshold", Description = "A new zone replaces older overlapping zones when their overlap (as a fraction of the smaller value area) meets this. 0 = any overlap replaces (matches AutoFRVP).", Order = 27, GroupName = "15. FRVP Zones")]
        public double FrvpOverlapThreshold { get; set; }

        // ===== 12. Session Levels =====
        [NinjaScriptProperty]
        [Display(Name = "Show Asia", Order = 1, GroupName = "14. Session Levels")]
        public bool ShowAsia { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show London", Order = 2, GroupName = "14. Session Levels")]
        public bool ShowLondon { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show New York", Order = 3, GroupName = "14. Session Levels")]
        public bool ShowNewYork { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Level: POC", Order = 4, GroupName = "14. Session Levels")]
        public bool ShowSessPOC { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Level: VAH", Order = 5, GroupName = "14. Session Levels")]
        public bool ShowSessVAH { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Level: VAL", Order = 6, GroupName = "14. Session Levels")]
        public bool ShowSessVAL { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Level: Open", Order = 7, GroupName = "14. Session Levels")]
        public bool ShowSessOpen { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Level: High", Order = 8, GroupName = "14. Session Levels")]
        public bool ShowSessHigh { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Level: Low", Order = 9, GroupName = "14. Session Levels")]
        public bool ShowSessLow { get; set; }

        [NinjaScriptProperty]
        [Range(0, 30)]
        [Display(Name = "Previous Days", Description = "0 = current trading day's sessions only. 1 = also the prior day, etc. (rolls at 18:00).", Order = 10, GroupName = "14. Session Levels")]
        public int SessPreviousDays { get; set; }

        [NinjaScriptProperty]
        [Range(50, 90)]
        [Display(Name = "Value Area %", Order = 11, GroupName = "14. Session Levels")]
        public double SessValueAreaPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Ticks Per Row", Description = "Price bin size (ticks) for the session POC/VA computation.", Order = 12, GroupName = "14. Session Levels")]
        public int SessTicksPerRow { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Level Opacity", Order = 13, GroupName = "14. Session Levels")]
        public int SessLevelOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Asia Start (HH:mm)", Order = 14, GroupName = "14. Session Levels")]
        public string AsiaStartText { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Asia End (HH:mm)", Order = 15, GroupName = "14. Session Levels")]
        public string AsiaEndText { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "London Start (HH:mm)", Order = 16, GroupName = "14. Session Levels")]
        public string LondonStartText { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "London End (HH:mm)", Order = 17, GroupName = "14. Session Levels")]
        public string LondonEndText { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York Start (HH:mm)", Order = 18, GroupName = "14. Session Levels")]
        public string NewYorkStartText { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York End (HH:mm)", Order = 19, GroupName = "14. Session Levels")]
        public string NewYorkEndText { get; set; }

        [XmlIgnore]
        [Display(Name = "Asia Color", Order = 20, GroupName = "14. Session Levels")]
        public Brush AsiaColor { get; set; }
        [Browsable(false)]
        public string AsiaColorSerialize
        {
            get { return Serialize.BrushToString(AsiaColor); }
            set { AsiaColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "London Color", Order = 21, GroupName = "14. Session Levels")]
        public Brush LondonColor { get; set; }
        [Browsable(false)]
        public string LondonColorSerialize
        {
            get { return Serialize.BrushToString(LondonColor); }
            set { LondonColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "New York Color", Order = 22, GroupName = "14. Session Levels")]
        public Brush NewYorkColor { get; set; }
        [Browsable(false)]
        public string NewYorkColorSerialize
        {
            get { return Serialize.BrushToString(NewYorkColor); }
            set { NewYorkColor = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Show Midnight Open", Description = "Mark the 00:00 open in the reference time zone (12 AM ET by default) - the price the calendar day started at. DST is honoured by the reference zone, so this is true midnight Eastern year-round.", Order = 30, GroupName = "14. Session Levels")]
        public bool ShowMidnightOpen { get; set; }

        [Range(0, 10)]
        [Display(Name = "  Midnight Prior Days", Description = "How many previous midnight opens to keep on the chart behind today's, drawn dimmed and date-stamped. 0 = today only.", Order = 31, GroupName = "14. Session Levels")]
        public int MidnightPriorDays { get; set; }

        [Display(Name = "  Midnight Anchor To Open", Description = "Start each midnight-open line at the bar that opened the day rather than running it across the full canvas. Off = standard full-length reference line.", Order = 32, GroupName = "14. Session Levels")]
        public bool MidnightAnchorToOpen { get; set; }

        [XmlIgnore]
        [Display(Name = "  Midnight Open Color", Order = 33, GroupName = "14. Session Levels")]
        public Brush MidnightOpenColor { get; set; }
        [Browsable(false)]
        public string MidnightOpenColorSerialize
        {
            get { return Serialize.BrushToString(MidnightOpenColor); }
            set { MidnightOpenColor = Serialize.StringToBrush(value); }
        }

        [Display(Name = "  Midnight Open Style", Order = 34, GroupName = "14. Session Levels")]
        public DashStyleHelper MidnightOpenStyle { get; set; }

        [Range(1, 5)]
        [Display(Name = "  Midnight Open Thickness", Order = 35, GroupName = "14. Session Levels")]
        public int MidnightOpenThickness { get; set; }

        [Range(5, 100)]
        [Display(Name = "  Midnight Open Opacity", Order = 36, GroupName = "14. Session Levels")]
        public int MidnightOpenOpacity { get; set; }

        // ===== 13. HTF Profiles =====
        [NinjaScriptProperty]
        [Display(Name = "Show Weekly Profile", Description = "Draw each week's volume profile as a bar-anchored silhouette over its own bars (multiple weeks across the chart). Built from the 30-min HTF series. Number of prior weeks = Prior Weeks (group 10).", Order = 1, GroupName = "11. HTF Profiles")]
        public bool ShowWeeklyProfile { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Monthly Profile", Description = "Draw each month's volume profile as a bar-anchored silhouette over its own bars. Number of prior months = Prior Months (group 10).", Order = 2, GroupName = "11. HTF Profiles")]
        public bool ShowMonthlyProfile { get; set; }

        [XmlIgnore]
        [Display(Name = "Weekly Profile Color", Order = 3, GroupName = "11. HTF Profiles")]
        public Brush WeeklyProfileColor { get; set; }
        [Browsable(false)]
        public string WeeklyProfileColorSerialize
        {
            get { return Serialize.BrushToString(WeeklyProfileColor); }
            set { WeeklyProfileColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Monthly Profile Color", Order = 4, GroupName = "11. HTF Profiles")]
        public Brush MonthlyProfileColor { get; set; }
        [Browsable(false)]
        public string MonthlyProfileColorSerialize
        {
            get { return Serialize.BrushToString(MonthlyProfileColor); }
            set { MonthlyProfileColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(2, 60)]
        [Display(Name = "Weekly Profile Opacity", Description = "Low keeps overlapping weeks readable.", Order = 5, GroupName = "11. HTF Profiles")]
        public int WeeklyProfileOpacity { get; set; }

        [NinjaScriptProperty]
        [Range(2, 60)]
        [Display(Name = "Monthly Profile Opacity", Order = 6, GroupName = "11. HTF Profiles")]
        public int MonthlyProfileOpacity { get; set; }

        [NinjaScriptProperty]
        [Range(0.2, 1.0)]
        [Display(Name = "Profile Width", Description = "Width of each period's silhouette as a fraction of its time span. 1.0 = the POC row spans the full period (NT style); lower keeps them narrower.", Order = 7, GroupName = "11. HTF Profiles")]
        public double HtfProfileWidthFrac { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "HTF Profile View", Description = "10,000-foot view: when a weekly or monthly profile is on, hide the LTF wall, levels, ghost and session - leaving only the HTF profile (with its VAH/VAL/POC), HVN zones and FRVP. Off = HTF profiles overlay the normal LTF view.", Order = 8, GroupName = "11. HTF Profiles")]
        public bool HtfProfileView { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Profile VAH/VAL/POC", Description = "Draw each drawn period's value area and POC, spanning that period (developing extends to the live edge).", Order = 9, GroupName = "11. HTF Profiles")]
        public bool HtfProfileLevels { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Profile Level Opacity", Order = 10, GroupName = "11. HTF Profiles")]
        public int HtfProfileLevelOpacity { get; set; }

        // ===== 14. HTF Alerts =====
        [NinjaScriptProperty]
        [Display(Name = "Show HTF Alerts", Description = "Persistent top-center banner warning when price is near a weekly/monthly HVN or LVN. For LTF entry charts - works without the HTF profile being drawn.", Order = 1, GroupName = "12. HTF Alerts")]
        public bool ShowHtfAlerts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Warn: Weekly HVN", Order = 2, GroupName = "12. HTF Alerts")]
        public bool WarnWeeklyHvn { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Warn: Weekly LVN", Order = 3, GroupName = "12. HTF Alerts")]
        public bool WarnWeeklyLvn { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Warn: Monthly HVN", Order = 4, GroupName = "12. HTF Alerts")]
        public bool WarnMonthlyHvn { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Warn: Monthly LVN", Order = 5, GroupName = "12. HTF Alerts")]
        public bool WarnMonthlyLvn { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "Alert Distance (points)", Description = "How close (in price points) price must be to a node to trigger the banner. Separate from the visual reveal distance - warn earlier than you draw.", Order = 6, GroupName = "12. HTF Alerts")]
        public double AlertDistance { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 0.6)]
        [Display(Name = "LVN Fraction", Description = "A row counts toward a low-volume void if its volume is below this fraction of the profile's peak.", Order = 7, GroupName = "12. HTF Alerts")]
        public double HtfLvnFraction { get; set; }

        [XmlIgnore]
        [Display(Name = "HVN Alert Color", Order = 8, GroupName = "12. HTF Alerts")]
        public Brush AlertHvnColor { get; set; }
        [Browsable(false)]
        public string AlertHvnColorSerialize
        {
            get { return Serialize.BrushToString(AlertHvnColor); }
            set { AlertHvnColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "LVN Alert Color", Order = 9, GroupName = "12. HTF Alerts")]
        public Brush AlertLvnColor { get; set; }
        [Browsable(false)]
        public string AlertLvnColorSerialize
        {
            get { return Serialize.BrushToString(AlertLvnColor); }
            set { AlertLvnColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(1, 60)]
        [Display(Name = "Max Zones", Description = "Cap on retained zones (oldest dropped first).", Order = 21, GroupName = "15. FRVP Zones")]
        public int FrvpMaxZones { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Mitigation", Description = "Track each zone Fresh -> Tested (price returned and it held) -> Mitigated (price closed back through the POC). Mitigated zones stop projecting.", Order = 22, GroupName = "15. FRVP Zones")]
        public bool FrvpEnableMitigation { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flip On Breakthrough", Description = "When a close clears a zone's far edge, reverse its role (broken supply becomes demand) and re-arm it as Tested.", Order = 23, GroupName = "15. FRVP Zones")]
        public bool FrvpFlipOnBreakthrough { get; set; }

        [NinjaScriptProperty]
        [Range(0, 5)]
        [Display(Name = "Max Flips", Description = "How many times a zone may flip its polarity on a break-through before it is retired (mitigated) instead of re-armed. 0 = never flip (any clean break-through mitigates immediately). Only applies when Flip On Breakthrough is on.", Order = 24, GroupName = "15. FRVP Zones")]
        public int FrvpMaxFlips { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Retire After Touches", Description = "Remove a zone once price has returned into it this many separate times. 0 = disabled.", Order = 25, GroupName = "15. FRVP Zones")]
        public int FrvpRetireTouches { get; set; }

        [Display(Name = "FRVP Hover Tooltip", Description = "Show a details pop-up (type, strength, state, POC/VA, volume, departure, touches, flips, age) when the cursor is over an FRVP zone's source box.", Order = 29, GroupName = "15. FRVP Zones")]
        public bool ShowFrvpTooltip { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Tested Opacity %", Description = "Projection opacity for Tested zones, relative to Fresh. Lower = subtler once a zone has been retested.", Order = 26, GroupName = "15. FRVP Zones")]
        public int FrvpTestedOpacityPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mitigated Footprint", Description = "Leave a faint source-bar outline where a mitigated zone was, as a historical footprint. Off = mitigated zones vanish entirely.", Order = 27, GroupName = "15. FRVP Zones")]
        public bool FrvpShowMitigatedFootprint { get; set; }

        [NinjaScriptProperty]
        [Range(5, 60)]
        [Display(Name = "Footprint Opacity", Order = 28, GroupName = "15. FRVP Zones")]
        public int FrvpMitigatedFootprintOpacity { get; set; }

        // ===== 08. Polarity =====
        [Display(Name = "Delta Source", Description = "How the buy/sell lean is measured. Proxy = close-in-range OHLC heuristic (works on any data, but it's a lean). True = real bid/ask delta: each trade is classified buyer- or seller-initiated by the bid/ask rule, with a tick-rule fallback when a quote is missing. True needs live tick flow; it falls back to Proxy per-bar when no ticks/quotes are available (historical bars without Tick Replay, or feeds that record only Last). Cvd = CVD-Zones net delta: net is computed per bar over a rolling lookback (CVD Lookback Bars), weak bars are dropped (CVD Threshold %), and survivors are aggregated onto a coarse zone grid (CVD Zone Count) so only zones where price moved on decisively one-sided bars light up - reads as tradeable bands, not per-row specks. Proxy/True sit near-neutral all session; Cvd is the sparse, high-contrast read. Drives the wall tint, strip and rail dots (the Delta Split has no CVD analogue and stays on Proxy/True).", Order = 0, GroupName = "08. Polarity")]
        public DeltaSourceEnum DeltaSource { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Wall Polarity Tint", Description = "Tint the wall by each row's buy/sell lean. Uses the Delta Source (true bid/ask delta when available, else the OHLC proxy). Off = single-colour wall.", Order = 1, GroupName = "08. Polarity")]
        public bool WallPolarity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Rail Polarity Dots", Description = "Show a small buy/sell/neutral dot by each LVN rail label. In True delta mode the dot reads the session delta at the level's price; in Proxy mode it reads the OHLC lean of the gap's volume.", Order = 2, GroupName = "08. Polarity")]
        public bool RailPolarity { get; set; }

        [Display(Name = "Wall Delta Split", Description = "Render the wall as per-row buy/sell segments: each row keeps its total-volume width (the silhouette is unchanged) but is partitioned into buy and sell segments sized by the classified split, using the Buy/Sell colors below. Shows magnitude AND lean where the tint shows lean only - a 5k x 5k row and a 50 x 50 row are both neutral to the tint but obviously different here. Uses the same Delta Source data as the tint. Takes precedence over the polarity tint and VA coloring.", Order = 10, GroupName = "08. Polarity")]
        public bool WallDeltaSplit { get; set; }

        [Display(Name = "Delta Split: Buys Inner", Description = "Segment order within each row. Off (default) = sells nearest the wall's right anchor, buys outboard. On = buys inner.", Order = 11, GroupName = "08. Polarity")]
        public bool DeltaSplitBuysInner { get; set; }

        [Display(Name = "Delta Split: Terrain Outline", Description = "When Wall Style is Smooth, stroke the terrain silhouette over the split rows so the smooth terrain read is preserved.", Order = 12, GroupName = "08. Polarity")]
        public bool DeltaSplitOutline { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Polarity Strip", Description = "Solid full-opacity buy/sell ribbon pinned to the wall's right edge - the clearest read of the lean.", Order = 7, GroupName = "08. Polarity")]
        public bool ShowPolarityStrip { get; set; }

        [NinjaScriptProperty]
        [Range(2, 30)]
        [Display(Name = "Polarity Strip Width (px)", Order = 8, GroupName = "08. Polarity")]
        public int PolarityStripWidth { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 0.9)]
        [Display(Name = "Polarity Deadzone", Description = "Lean within ±this stays neutral (no tint / grey dot). Higher = only strongly one-sided rows or gaps colour up.", Order = 3, GroupName = "08. Polarity")]
        public double PolarityDeadzone { get; set; }

        [Range(10, 1500)]
        [Display(Name = "CVD Lookback Bars", Description = "Cvd source only. How many bars back the per-bar net delta is scanned. This is the 'market memory' window: it reaches across the session boundary, so decisive delta from prior sessions still tints in-range rows. Capped by Scan Lookback Bars. ~300 = a session or so; larger = longer memory.", Order = 3, GroupName = "08. Polarity")]
        public int CvdLookbackBars { get; set; }

        [Range(0.0, 100.0)]
        [Display(Name = "CVD Threshold %", Description = "Cvd source only. A bar's net delta must exceed this % of the window's strongest bar to count - everything weaker is dropped. Higher = only the most decisive bars colour up (sparser, cleaner); lower = more rows light up. 0 = keep every bar.", Order = 3, GroupName = "08. Polarity")]
        public double CvdThresholdPct { get; set; }

        [Display(Name = "CVD Volume-Weighted Midpoint", Description = "Cvd source only. On = stamp each bar's net onto the row at its (H+L+C)/3 midpoint. Off = stamp at the close. On spreads the read a touch and is usually steadier.", Order = 3, GroupName = "08. Polarity")]
        public bool CvdUseVwMidpoint { get; set; }

        [Range(8, 200)]
        [Display(Name = "CVD Zone Count", Description = "Cvd source only. How many fixed-height zones the lookback range is split into (CVDZonesPremium uses 62). This is the zone-size control: FEWER zones = taller, chunkier, more tradeable bands; more zones = finer detail but noisier. If the wall reads as scattered specks, lower this.", Order = 3, GroupName = "08. Polarity")]
        public int CvdZoneCount { get; set; }

        [Range(0, 5)]
        [Display(Name = "CVD Smoothing", Description = "Cvd source only. Number of 1-2-1 smoothing passes over the zones. Each pass merges neighbours and kills lone specks, blobbing the read into cleaner bands. 0 = raw, 1-2 = typical, higher = very smooth.", Order = 3, GroupName = "08. Polarity")]
        public int CvdSmoothing { get; set; }

        [XmlIgnore]
        [Display(Name = "Buy Color", Order = 4, GroupName = "08. Polarity")]
        public Brush WallBuyColor { get; set; }
        [Browsable(false)]
        public string WallBuyColorSerialize
        {
            get { return Serialize.BrushToString(WallBuyColor); }
            set { WallBuyColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Sell Color", Order = 5, GroupName = "08. Polarity")]
        public Brush WallSellColor { get; set; }
        [Browsable(false)]
        public string WallSellColorSerialize
        {
            get { return Serialize.BrushToString(WallSellColor); }
            set { WallSellColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Neutral Color", Description = "Wall tint at balance. Grey reads as a clear delta view; set to your wall colour for a subtler warm-anchored tint.", Order = 6, GroupName = "08. Polarity")]
        public Brush PolarityNeutralColor { get; set; }
        [Browsable(false)]
        public string PolarityNeutralColorSerialize
        {
            get { return Serialize.BrushToString(PolarityNeutralColor); }
            set { PolarityNeutralColor = Serialize.StringToBrush(value); }
        }

        // ===== 09. HTF POC =====
        [NinjaScriptProperty]
        [Range(1, 1440)]
        [Display(Name = "HTF Source (minutes)", Description = "Minute bar size of the hidden time series the weekly/monthly profiles are built from. 15-30 matches a standard intraday profile; larger is lighter. Takes effect on reload.", Order = 0, GroupName = "10. HTF POC & VA")]
        public int HtfSourceMinutes { get; set; }

        [Range(0, 365)]
        [Display(Name = "HTF Backfill (days)", Description = "Minute history requested via BarsRequest, independent of the chart's loaded range, so the weekly/monthly profiles are complete even on a 3-day tick chart. 65 covers the prior month + developing month. 0 = off (HTF built from the chart-loaded range only, the old behavior). Takes effect on reload.", Order = 20, GroupName = "10. HTF POC & VA")]
        public int HtfBackfillDays { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Weekly POC", Description = "Developing weekly POC (solid) plus prior weeks' POC (pwPOC, static grey).", Order = 1, GroupName = "10. HTF POC & VA")]
        public bool ShowWeeklyPoc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Weekly Value Area", Description = "Weekly VAH/VAL for the developing week (wVAH/wVAL) and prior weeks (pwVAH/pwVAL).", Order = 2, GroupName = "10. HTF POC & VA")]
        public bool ShowWeeklyVA { get; set; }

        [NinjaScriptProperty]
        [Range(0, 12)]
        [Display(Name = "Prior Weeks To Show", Description = "How many completed weeks back to draw (1 = last week only).", Order = 3, GroupName = "10. HTF POC & VA")]
        public int PriorWeeksToShow { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Monthly POC", Description = "Developing monthly POC (solid) plus prior months' POC (pmPOC, static grey). Often far from price - a high-value level when reached.", Order = 4, GroupName = "10. HTF POC & VA")]
        public bool ShowMonthlyPoc { get; set; }

        [NinjaScriptProperty]
        [Range(0, 12)]
        [Display(Name = "Prior Months To Show", Description = "How many completed months back to draw (1 = last month only).", Order = 5, GroupName = "10. HTF POC & VA")]
        public int PriorMonthsToShow { get; set; }

        // ---- Weekly family ----
        [XmlIgnore]
        [Display(Name = "Weekly Color", Order = 6, GroupName = "10. HTF POC & VA")]
        public Brush WeeklyPocColor { get; set; }
        [Browsable(false)]
        public string WeeklyPocColorSerialize
        {
            get { return Serialize.BrushToString(WeeklyPocColor); }
            set { WeeklyPocColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Weekly POC Style", Order = 7, GroupName = "10. HTF POC & VA")]
        public DashStyleHelper WeeklyPocStyle { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Weekly VA Style", Order = 8, GroupName = "10. HTF POC & VA")]
        public DashStyleHelper WeeklyVaStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 6)]
        [Display(Name = "Weekly Thickness", Order = 9, GroupName = "10. HTF POC & VA")]
        public int WeeklyThickness { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Weekly Opacity", Order = 10, GroupName = "10. HTF POC & VA")]
        public int WeeklyOpacity { get; set; }

        // ---- Prior-week family ----
        [XmlIgnore]
        [Display(Name = "Prior Week Color", Order = 11, GroupName = "10. HTF POC & VA")]
        public Brush PriorWeekColor { get; set; }
        [Browsable(false)]
        public string PriorWeekColorSerialize
        {
            get { return Serialize.BrushToString(PriorWeekColor); }
            set { PriorWeekColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Prior Week POC Style", Order = 12, GroupName = "10. HTF POC & VA")]
        public DashStyleHelper PriorWeekPocStyle { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Prior Week VA Style", Order = 13, GroupName = "10. HTF POC & VA")]
        public DashStyleHelper PriorWeekVaStyle { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Prior Week Opacity", Order = 14, GroupName = "10. HTF POC & VA")]
        public int PriorWeekOpacity { get; set; }

        // ---- Monthly family ----
        [XmlIgnore]
        [Display(Name = "Monthly Color", Order = 15, GroupName = "10. HTF POC & VA")]
        public Brush MonthlyPocColor { get; set; }
        [Browsable(false)]
        public string MonthlyPocColorSerialize
        {
            get { return Serialize.BrushToString(MonthlyPocColor); }
            set { MonthlyPocColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Monthly POC Style", Order = 16, GroupName = "10. HTF POC & VA")]
        public DashStyleHelper MonthlyPocStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 6)]
        [Display(Name = "Monthly Thickness", Order = 17, GroupName = "10. HTF POC & VA")]
        public int MonthlyThickness { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Monthly Opacity", Order = 18, GroupName = "10. HTF POC & VA")]
        public int MonthlyOpacity { get; set; }

        // ---- Prior-month family ----
        [XmlIgnore]
        [Display(Name = "Prior Month Color", Order = 19, GroupName = "10. HTF POC & VA")]
        public Brush PriorMonthColor { get; set; }
        [Browsable(false)]
        public string PriorMonthColorSerialize
        {
            get { return Serialize.BrushToString(PriorMonthColor); }
            set { PriorMonthColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Prior Month POC Style", Order = 20, GroupName = "10. HTF POC & VA")]
        public DashStyleHelper PriorMonthStyle { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Prior Month Opacity", Order = 21, GroupName = "10. HTF POC & VA")]
        public int PriorMonthOpacity { get; set; }

        // ===== 16. Momentum Gate (Waddah-Attar-Explosion confluence for the HTF alert banner) =====
        // NOTE: this is a confluence layer on the "12. HTF Alerts" banner - it does nothing unless
        // "Show HTF Alerts" is on. It's an entry-timing read: a momentum expansion as price reaches a
        // weekly/monthly node is higher-conviction than the node alone. Useful on ANY time-based
        // entry chart (1m, 5m, 15m, hourly) - the defaults are tuned for time bars. On tick/range bars
        // the MACD/Bollinger periods behave differently and need retuning (raise the periods and/or
        // sensitivity until the chip fires on real expansions, not noise). These are NOT NinjaScript
        // params, so they stay UI/serialize-only and don't disturb the generated cache block.
        [Display(Name = "Momentum Gate", Description = "Off = disabled. Highlight = banner always shows, with a momentum chip + brighter tint when momentum is exploding. Filter = banner only fires when a momentum explosion is active (level + momentum confluence). Requires 'Show HTF Alerts' to be on.", Order = 1, GroupName = "17. Momentum Gate")]
        public MomentumGateModeEnum MomentumGate { get; set; }

        [Display(Name = "Normalize By ATR", Description = "On (recommended): compares the trend slope to ATR x Threshold, so one setting works across instruments and tick/range/time bars without retuning - uses ATR Period + ATR Threshold below and ignores Sensitivity/Bollinger. Off: legacy fixed mode using Sensitivity vs Bollinger width.", Order = 2, GroupName = "17. Momentum Gate")]
        public bool MomNormalizeByAtr { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", Description = "ATR lookback for the scale-free explosion threshold (ATR-normalized mode). Default 14.", Order = 3, GroupName = "17. Momentum Gate")]
        public int MomAtrPeriod { get; set; }

        [Range(0.001, 10.0)]
        [Display(Name = "ATR Threshold", Description = "How much MACD-slope expansion (in ATR units) counts as an explosion, in ATR-normalized mode. Higher = harder to trigger. Default 0.10 - this is the one knob to dial in Highlight mode; it travels across MNQ/MES/MGC and tick/range.", Order = 4, GroupName = "17. Momentum Gate")]
        public double MomAtrThreshold { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "MACD Fast", Description = "Fast EMA period for the momentum (MACD-slope) read. Default 20 suits time-based bars.", Order = 5, GroupName = "17. Momentum Gate")]
        public int MomMacdFast { get; set; }

        [Range(2, int.MaxValue)]
        [Display(Name = "MACD Slow", Description = "Slow EMA period. Default 40 suits time-based bars; raise for tick/range.", Order = 6, GroupName = "17. Momentum Gate")]
        public int MomMacdSlow { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "MACD Signal", Description = "Signal smoothing period. Default 9.", Order = 7, GroupName = "17. Momentum Gate")]
        public int MomMacdSignal { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Bollinger Period", Description = "Fixed-mode only (Normalize By ATR off): period for the Bollinger-width 'explosion' threshold. Default 20.", Order = 8, GroupName = "17. Momentum Gate")]
        public int MomBbPeriod { get; set; }

        [Range(0.1, 10.0)]
        [Display(Name = "Bollinger StdDev", Description = "Fixed-mode only: standard-deviation multiplier for the explosion threshold. Default 2.0. Higher = harder to trigger.", Order = 9, GroupName = "17. Momentum Gate")]
        public double MomBbStdDev { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Sensitivity", Description = "Fixed-mode only: scales the trend slope vs the Bollinger explosion line. Higher = more/earlier signals. Default 150 (time bars); retune per instrument/bar type. Ignored when Normalize By ATR is on.", Order = 10, GroupName = "17. Momentum Gate")]
        public int MomSensitivity { get; set; }

        [Display(Name = "Require Acceleration", Description = "When on, momentum must be expanding vs the prior bar (fresh explosions only). Off = any bar above the explosion line counts.", Order = 11, GroupName = "17. Momentum Gate")]
        public bool MomRequireAcceleration { get; set; }

        [XmlIgnore]
        [Display(Name = "Long Momentum Color", Order = 12, GroupName = "17. Momentum Gate")]
        public Brush MomLongColor { get; set; }
        [Browsable(false)]
        public string MomLongColorSerialize
        {
            get { return Serialize.BrushToString(MomLongColor); }
            set { MomLongColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Short Momentum Color", Order = 13, GroupName = "17. Momentum Gate")]
        public Brush MomShortColor { get; set; }
        [Browsable(false)]
        public string MomShortColorSerialize
        {
            get { return Serialize.BrushToString(MomShortColor); }
            set { MomShortColor = Serialize.StringToBrush(value); }
        }

        #endregion
    }
}
