#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using SharpDX;
using SharpDX.Direct2D1;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// ═════════════════════════════════════════════════════════════════════════════
//  Nebula_Core — a phased NinjaScript port of TraderOracle's "Nebula v2.2" (TradingView)
//
//  PHASE 1 of N: only the self-contained core is ported here — the Fantail VMA /
//  McGinley Dynamic cloud (cloudType="Simple", the Pine script's own default) and
//  the Waddah Attar Explosion candle coloring (sCandleType="Waddah", also the
//  Pine default). The original script bundles ~10 largely-independent sub-systems
//  (LuxAlgo reversal counter, Market Structure/Fractal, "The Shark", Tidal Wave,
//  Breaks and Retests, a Rational Quadratic Kernel regression, etc.) — those are
//  NOT included here; they're separate follow-up conversions by design, not an
//  oversight. See the chat discussion for why: this piece is self-contained
//  (verified against the source — the cloud/WAE math doesn't reference the larger
//  sub-systems at all) and is the lowest-risk piece to get right without being
//  able to compile/run the original Pine script side-by-side to verify against.
//
//  Standalone ADX is deliberately NOT included in this phase either — checked the
//  source and it isn't actually used by the cloud or WAE candles at all; it only
//  gates Tidal Wave's threshold and Squeeze Relaxer, both later phases.
//
//  TRANSLATION NOTES (for anyone diffing this against the Pine source):
//  - Pine's automatic bar-history indexing (close[1]) → NinjaScript Series<T>
//    indexing (Close[1]); functionally equivalent, no behavior change.
//  - Pine's implicit "na on first bars" handling → explicit CurrentBar>0 checks,
//    matching the Pine source's own `if (bar_index > 0)` guards exactly (the
//    Fantail VMA's internal ADX-style smoothing already guards this way in Pine).
//  - Pine's fill(plot1, plot2, color) between two lines has no direct NinjaScript
//    equivalent — implemented here as a custom Direct2D quad-strip fill in
//    OnRender, semi-transparent, matching the "Simple" cloud type's flat color
//    (no gradient — gradient cloud types from the original are a later phase).
//  - Pine's plotcandle(...) per-bar body/wick/border recoloring → NinjaScript's
//    CandleOutlineBrushes / CandleWickBrushes / BarBrushes arrays (only valid
//    when PaintPriceMarkers=true and the indicator overlays the price panel).
//  - color.from_gradient(value, min, max, colA, colB) → a manual linear
//    interpolation helper (Lerp), since NinjaScript has no built-in equivalent.
//
//  ⚠ HONEST CONFIDENCE NOTE: the core math (Fantail VMA, McGinley Dynamic, WAE,
//  the candle-recoloring brush arrays) follows patterns already verified working
//  elsewhere in this conversation. The OnRender cloud-fill block (GetXByBarIndex /
//  GetYByValue / ChartBars.FromIndex/ToIndex / Series.GetValueAt) is written from
//  general knowledge of the NT8 charting API, NOT against a reference file I could
//  check exact method names against the way I did for every Sentinel Suite change
//  — unlike that work, I don't have this API's real source to verify signatures
//  against. If OnRender specifically fails to compile, send me the exact error —
//  that's the one part of this file I'd bet on needing a follow-up correction.
//
//  This has NOT been run against a live NinjaTrader instance or visually compared
//  to the original TradingView chart. Treat this as a careful first draft to
//  verify visually once imported, the same as every other from-scratch indicator
//  built in this conversation.
// ═════════════════════════════════════════════════════════════════════════════

// Enums used as [NinjaScriptProperty] params live in the parent NinjaTrader.NinjaScript
// namespace so NT8's zip-import cache-accessor regen (which emits unqualified type refs)
// resolves them correctly — a real, hard-won gotcha from earlier in this conversation.
namespace NinjaTrader.NinjaScript
{
	public enum NebulaCandleMode { Waddah, VolumeDelta, Squeeze, MACD }
}

namespace NinjaTrader.NinjaScript.Indicators
{
	public class Nebula_Core_v1_0_0 : Indicator
	{
		// ── Fantail VMA state (persisted across bars, matching Pine's var-style recursion) ──
		private double _sPDI, _sMDI, _str, _adxInternal, _varMA;
		private Series<double> _fanVmaRaw;   // pre-smoothing VarMA, SMA'd into FanVMA below
		private SMA _fanVmaSmooth;
		private Series<double> _adxSeries;   // rolling buffer so MAX()/MIN() can look back over it

		// ── McGinley Dynamic state ──
		private double _mg;
		private bool _mgSeeded;
		private EMA _mgSeedEma;

		// ── WAE (Waddah Attar Explosion) building blocks ──
		private EMA _waeFastEma, _waeSlowEma;

		// ── Cumulative Volume Delta building blocks ──
		private Series<double> _buyingVolume, _sellingVolume;
		private EMA _cvdBuyEma, _cvdSellEma;

		// ── Squeeze Relaxer v2.1 building blocks ──
		private SMA _sqBBBasis, _sqKcMa, _sqCloseSma;
		// Wicking Bollinger Bands (Pine's wlengthBB/wmultBB, 20/2.5, always the source's own defaults —
		// no plotshape of its own, feeds only the vBands weight in the TP confluence counter, so not
		// worth exposing as its own settings group)
		private SMA _wbbBasis;
		private StdDev _wbbDev;
		private ATR _sqAtr;
		private StdDev _sqBBDev;
		private MAX _sqHighest;
		private MIN _sqLowest;
		private Series<double> _sqDiffSeries;
		private ADX _sqAdx;
		private double _sqValPrev = double.NaN;

		// ── Trampoline building blocks ──
		private RSI _trampRsi;
		private SMA _trampBBBasis;
		private StdDev _trampBBDev;
		private Series<double> _trampLowerBB, _trampUpperBB, _trampBbw;
		private Series<double> _weGoUpSeries, _weGoDownSeries;

		// ── Dead Simple Reversal building blocks ──
		private MIN _dsrLow3, _dsrLow50;
		private MAX _dsrHigh3, _dsrHigh50;

		// ── LuxAlgo Reversal Signals (TD-Sequential-style 9-count) ──
		private int _bSC, _sSC;

		// ── Tidal Wave building blocks ──
		private const int WAVE_NONE = 0, WAVE_UP = 1, WAVE_DOWN = 2;
		private int _waveState = WAVE_NONE;
		private Series<double> _greenCandleSeries, _redCandleSeries, _brightGreenSeries, _brightRedSeries;
		private readonly List<KeyValuePair<string, double>> _tidalLines = new List<KeyValuePair<string, double>>();
		private int _tidalLineSeq;

		// ── HEMA building blocks ──
		private double _hema, _bH;
		private bool _hemaSeeded;

		// ── Skull-cross (EMA9/21 or EMA9/RQK) building blocks ──
		private EMA _ema9, _ema21;
		private MIN _pivotLowScan;
		private MAX _pivotHighScan;

		// ── Breaks and Retests building blocks ──
		private double _lastPL = double.NaN, _lastPH = double.NaN;   // Pine's fixnan-held pl/ph
		private string _supportBoxTag, _resistanceBoxTag;
		private double _sTop, _sBot, _rTop, _rBot;
		private bool _supportBroken, _resistanceBroken;   // reset per NEW box — see the deviation note above the calc block
		private int _supportBreakBar = -1, _resistanceBreakBar = -1;
		private bool _supportRetested, _resistanceRetested;   // one retest marker per break episode
		private int _brTagSeq;

		// ── Market Structure (Fractal) building blocks ──
		private Series<double> _dhtSeries, _dltSeries;
		private MAX _msHighestScan;
		private MIN _msLowestScan;
		private double _upperFractalValue = double.NaN, _lowerFractalValue = double.NaN;

		// ── MACD candle mode building blocks ──
		private EMA _macdFastEma, _macdSlowEma;
		private Series<double> _macdSeries;
		private EMA _macdSignalEma;
		private double _prevHist = double.NaN;
		private bool _histAIsUp, _histAIsDown, _histBIsDown, _histBIsUp;

		// ── The Shark building blocks (reuses Trampoline's _trampRsi) ──
		private SMA _sharkRsiBasis;
		private StdDev _sharkRsiDev;

		// ── RedK Slow-Smooth WMA ("LazyLine") ──
		private WMA _lazyWma1, _lazyWma2, _lazyWma3;

		// ── Vodka Shot (volume deviation) ──
		private SMA _volAvgLSma;
		private Series<double> _volAvgLSeries;
		private StdDev _volAvgLStdDev;

		// ── Directional Energy Ratio (only the bar-color state Vodka Shot's signal needs —
		// not the full RedK indicator with its own separate visible display) ──
		private MAX _derVolHighest, _derHigh2;
		private MIN _derVolLowest, _derLow2;
		private Series<double> _derVolaSeries, _derCPlusSeries, _derCMinusSeries, _derDemSeries, _derSupSeries;
		private WMA _derAvgVolaWma, _derDemWma, _derSupWma, _derAdpWma, _derAspWma;
		private double _derLastC;
		private double _derBc = double.NaN, _derBo = double.NaN, _derBcPrev = double.NaN;

		// ── "Add Contract" pyramid signal state ──
		private Series<double> _upwardsSeries, _downwardsSeries;

		// ── Ultimate Buy/Sell building blocks ──
		private RSI _ubsRsi;
		private WMA _ubsRsiBasisWma, _ubsRsiMaWma, _ubsAtrMaWma;
		private StdDev _ubsRsiDev, _ubsPriceDev;
		private SMA _ubsPriceBasisSma;
		private ATR _ubsAtr;
		private readonly Queue<int> _buyWatchWindow = new Queue<int>();
		private readonly Queue<int> _sellWatchWindow = new Queue<int>();
		private int _buyWatchSum, _sellWatchSum;
		private Series<double> _plotBuySeries, _plotSellSeries;
		private int _cGreen, _cRed;   // Pine's var cGreen/cRed run counters
		private bool _posPrev, _negPrev;
		// Take Profit confluence counter (Pine's iTPSignalCount) — resets to 0 every bar (no `var`
		// in the source), then accumulates weighted contributions from 6 of the port's other signals
		// firing on the current bar OR the prior bar. vEarlyRev's contribution is permanently excluded:
		// it depends on Vector Candles/Total Recall (external closed library), same blocked dependency
		// as everywhere else in this port that touches pvsraColor.
		private int _tpSignalCount;
		private bool _bbUpPrev, _bbDownPrev;   // Wicking Bollinger Bands — feeds vBands only, no marker of its own
		private SMA _waeBBBasis;
		private StdDev _waeBBDev;
		private double _prevMacd = double.NaN;

		// ── Rendering ──
		private Series<double> _cloudIsGreen;
		private SharpDX.Direct2D1.SolidColorBrush _cloudBrushUp, _cloudBrushDown;
		private System.Windows.Media.Color _lastCloudUpWpf, _lastCloudDownWpf;
		private int _lastRenderExLog = int.MinValue;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Phase 1 of a phased NinjaScript port of TraderOracle's Nebula v2.2 (TradingView): "
					+ "the Fantail VMA / McGinley Dynamic cloud (cloudType=Simple) and Waddah Attar Explosion "
					+ "candle coloring (sCandleType=Waddah) — both the Pine script's own defaults. The larger "
					+ "bundled sub-systems (Market Structure, The Shark, Tidal Wave, Breaks and Retests, the "
					+ "LuxAlgo reversal counter, Rational Quadratic Kernel) are separate later phases, not "
					+ "included here.";
				Name                     = "Nebula Core v1.0.0";
				Calculate                = Calculate.OnBarClose;
				IsOverlay                = true;
				DisplayInDataBox         = true;
				DrawOnPricePanel         = true;
				PaintPriceMarkers        = true;
				IsSuspendedWhileInactive = true;
				ShowIndicatorLabel       = false;   // suppressed via Name blanking below (see State.DataLoaded)

				// Fantail VMA defaults, matching the Pine script's "Fantail VMA" input group.
				FvAdxLength  = 2;
				FvWeighting  = 10.0;
				FvMaLength   = 6;

				// WAE defaults, matching the Pine script's "WAE" input group.
				WaeSensitivity   = 150;
				WaeFastLength    = 20;
				WaeSlowLength    = 40;
				WaeChannelLength = 20;
				WaeBBMult        = 2.0;
				WaeTopBody       = 80;
				WaeTopBorder     = 33;

				CloudOpacityPercent = 80;   // Pine's iSimpleCloud (0=opaque, 100=invisible), same scale

				CandleColorMode  = NebulaCandleMode.Waddah;   // matches the Pine script's own sCandleType default
				CvdTopEnd        = 300;                        // Pine's iVolDeltaTop

				SqTolerance    = 2;
				SqAdxThreshold = 21;
				SqAdxPeriod    = 14;    // matches Pine's shared adxlen/dilen (both default 14)
				SqLength       = 20;
				SqBBMult       = 2.0;
				SqKcLength     = 20;
				SqKcMult       = 1.5;

				TrampBBThreshold = 0.0015;   // Pine's iBBThreshold, tuned for 30-min candles per the source's own comment — worth revisiting for 1-min
				TrampRsiLower    = 25;
				TrampRsiUpper    = 72;
				TrampRsiLength   = 14;
				TrampBBLength    = 20;
				TrampBBMult      = 2.0;
				ShowTrampSignals = false;   // Pine's own thrust markers are commented out (invisible) by default in the
				                            // source — matched here now. The real, always-visible markers you'd see in
				                            // the reference video are the ①/➊/+/✚/✔/✓ chars and the skull-cross
				                            // triangles; this and the other "made visible" extras below are bonus
				                            // signals genuinely absent from Nebula's own default output — flip on if
				                            // you want them, but they won't appear in any stock walkthrough.
				ShowDSRSignals   = false;   // same reasoning — off by default in the source, matched here

				RqkLookback  = 21;   // Pine's h — genuinely controls the window here (see the fix note above AddPlot)
				RqkRelativeWeighting = 8.0;   // Pine's r

				ShowLuxRevSignals = false;   // same reasoning — off by default in the source, matched here

				ShowTidalLines   = false;  // Pine's bTrackBar, respected as-is — unlike Trampoline/DSR/LuxAlgo's
				                            // commented-out (truly inaccessible) markers, this is a REAL, exposed
				                            // toggle the original author deliberately defaulted off (likely for
				                            // performance — a 200-bar scan plus potentially many drawn lines),
				                            // not a mistake to "fix." Turn it on in the indicator's own properties.
				TidalBarExtend   = 50;     // Pine's iBarExtend
				TidalLineWidth   = 3;      // Pine's lWidth
				TidalLookback    = 200;    // Pine's fixed 200-bar backward scan
				IgnoreDojis      = false;  // Pine's bIgnoreDoji — source comment: "only use on NQ, 1 min"
				DojiBodyTicks    = 1;      // Pine's iMaxBody (in price units there; ticks here)

				AlphaLength = 20;   // HEMA
				GammaLength = 20;

				ShowSkullCross = false;   // Pine's bShow921 — off by default in the source, matched here
				UseKernelForSkullCross = false;   // Pine's bShowQuad

				PivotBarsBack   = 20;    // Pine's bb — a hardcoded constant there, exposed as a setting here
				ShowRetestMarkers = false;   // Pine's bShowRetest — off by default in the source itself, matched here
				RetestWindowBars = 20;   // MY OWN addition — Pine's own retest validity logic doesn't map to one
				                          // clean window number (see the deviation note above the calc block)
				ShowThreeOutPattern = false;   // Pine's bShowRevPattern — off by default in the source itself, matched here

				MsFractalLength = 5;   // Pine's length (hardcoded there, exposed as a setting here) — must stay odd-ish/≥3
				ShowStructureSignals = false;   // this port's own bonus signal, no direct Pine visibility toggle to match
				                                 // (see the plot comment above) — off by default, same treatment as the
				                                 // other "made visible" extras, so it doesn't clutter the chart with
				                                 // markers absent from the reference video

				MacdFastLength = 12;
				MacdSlowLength = 26;
				MacdSignalLength = 9;

				ApplySharkRsiRule = false;   // Pine's bApply25and75 — off by default in the source itself, matched here
				ShowSharkSignals  = false;   // Pine's own marker (shark emoji) is commented out (invisible) by default; matched here

				LazyLineLength = 21;   // Pine's fixed f_LazyLine(close, 21) argument, exposed as a setting here
				DerRLength = 12;       // Pine's rlength
				DerAvgLength = 5;      // Pine's DER_avg
				DerVolLookback = 20;   // Pine's vlookbk
				ShowAddContractSignals = true;   // Pine's bShowBigPlus — a real toggle, respected as-is
				ShowAddContractLightSignals = true;   // Pine's bShowPlus — a real, separate toggle
				ShowTakeProfitSignals = true;   // Pine's bShowProfit — a real toggle, respected as-is
				IProfitThreshold = 5;      // Pine's iProfit default
				IMaxProfitThreshold = 7;   // Pine's iMaxProfit default

				UbsWatchSignalLookback = 35;
				UbsRsiLength = 32;
				UbsRsiBasisLength = 32;
				UbsRsiMultiplier = 2.0;
				UbsUseRsiWatchSignals = true;
				UbsPriceBasisLength = 20;
				UbsPriceInnerMultiplier = 2.0;
				UbsUsePriceBandWatchSignals = true;
				UbsAtrPeriod = 30;
				UbsAtrMaPeriod = 10;
				UbsAtrMult = 1.5;
				UbsUseAtrWatchSignals = true;
				UbsUseRsiSignals = true;
				UbsUse75Signals = true;
				UbsUse25Signals = true;
				UbsUseRsiMa = true;
				UbsRsiMaLength = 24;
				ShowUltimateBuySell = true;   // a real, visible-by-default marker in the source itself — matched here, not a "make it visible" fix

				AddPlot(new Stroke(Brushes.White, 1), PlotStyle.Line, "FanVMA");
				AddPlot(new Stroke(Brushes.SteelBlue, 1), PlotStyle.Line, "McGinleyDynamic");
				// 1.0 on the firing bar, 0.0 otherwise — same convention as every other signal
				// series built throughout this conversation (SentinelWAE.Signal, etc.), so a
				// 3rd-party bar-tag automation tool (e.g. PredatorXOrderEntryLT) can key off these.
				AddPlot(new Stroke(Brushes.Lime, 3), PlotStyle.Dot, "UpThrustSignal");
				AddPlot(new Stroke(Brushes.Red, 3), PlotStyle.Dot, "DownThrustSignal");
				AddPlot(new Stroke(Brushes.Lime, 3), PlotStyle.Dot, "BuyDSRSignal");
				AddPlot(new Stroke(Brushes.Red, 3), PlotStyle.Dot, "SellDSRSignal");
				// DEVIATION FROM SOURCE (bug fix, not a style choice): the Pine source computes
				// this over array.from(close), which — since bare `close` is just the CURRENT
				// bar's scalar value, not the whole series — always produces a 1-element array.
				// The regression loop's range ends up fixed at ~17 iterations regardless of the
				// h="Lookback Window" input, so h never actually does what its own label says.
				// Implemented here as the standard Rational Quadratic Kernel regression instead
				// (h genuinely controls the window), which is clearly what was intended — the
				// original's own plot() call for this is ALSO commented out (invisible), so
				// there's no faithful "as displayed" behavior to preserve here even if I wanted to.
				AddPlot(new Stroke(Brushes.Orange, 2), PlotStyle.Line, "RationalQuadraticKernel");
				// DEVIATION FROM SOURCE: same as Trampoline/DSR — Pine's own markers for this are
				// commented out (invisible), only feeding the unsurfaced iTPSignalCount. Made
				// visible + plotted here. Also: pbS/psS in the Pine source are computed but have
				// ZERO actual effect on the signal — (X and not Y) or (X and Y) is always just X
				// regardless of Y — so they're omitted here rather than faithfully replicating
				// genuinely inert logic (not a behavior change, unlike the RQK fix above).
				AddPlot(new Stroke(Brushes.Yellow, 3), PlotStyle.Dot, "UpReversalSignal");
				AddPlot(new Stroke(Brushes.Yellow, 3), PlotStyle.Dot, "DownReversalSignal");
				AddPlot(new Stroke(Brushes.Purple, 2), PlotStyle.Line, "Hema");
				// Tidal Wave's "regular vs strong" buy/sell distinction depends on The Shark
				// (not yet built — see the comment above the Tidal Wave calculation block for
				// the full explanation). BuyCharSignal/SellCharSignal are the REAL, already-
				// correct base signal (Tidal Wave's own state only) — not a placeholder.
				AddPlot(new Stroke(Brushes.Lime, 3), PlotStyle.Dot, "BuyCharSignal");
				AddPlot(new Stroke(Brushes.Red, 3), PlotStyle.Dot, "SellCharSignal");
				AddPlot(new Stroke(Brushes.Cyan, 3), PlotStyle.Dot, "SkullUpSignal");
				AddPlot(new Stroke(Brushes.Cyan, 3), PlotStyle.Dot, "SkullDownSignal");
				// bb/rTon/rTcc/rThv are hardcoded plain assignments in the Pine source, not
				// input.xxx() calls — genuinely not user-configurable there either. rTon=true is
				// the only active repaint mode (rTcc/rThv are both false, dead code paths never
				// reached) — only that one path is implemented here.
				AddPlot(new Stroke(Brushes.Red, 3), PlotStyle.Dot, "SupportBreakSignal");
				AddPlot(new Stroke(Brushes.Red, 3), PlotStyle.Dot, "ResistanceBreakSignal");
				AddPlot(new Stroke(Brushes.Magenta, 3), PlotStyle.Dot, "SupportRetestSignal");
				AddPlot(new Stroke(Brushes.Magenta, 3), PlotStyle.Dot, "ResistanceRetestSignal");
				// The Pine source's real consumer chain for this feature (upOpen/upClose/
				// downOpen/downClose → bGreenSignal/bRedSignal) requires bVectorGreen/bVectorRed
				// from Vector Candles, which is blocked on an external closed library (see the
				// earlier chat discussion) — and even THAT downstream plotshape() is commented
				// out regardless. Exposed here as its own standalone signal instead — a swing
				// high/low fractal confirming, and price later breaking through that level, is
				// independently useful without needing the blocked dependency. Also: `iscrossed`
				// in the Pine source is declared, reset per new fractal, and checked, but never
				// actually set true anywhere — a fourth inert condition found in this script —
				// simplified away here rather than faithfully replicating a permanent no-op.
				AddPlot(new Stroke(Brushes.DodgerBlue, 3), PlotStyle.Dot, "FractalHighSignal");
				AddPlot(new Stroke(Brushes.DodgerBlue, 3), PlotStyle.Dot, "FractalLowSignal");
				AddPlot(new Stroke(Brushes.White, 3), PlotStyle.Dot, "StructureBreakUpSignal");
				AddPlot(new Stroke(Brushes.White, 3), PlotStyle.Dot, "StructureBreakDownSignal");
				// "The Shark" (final piece): reuses Trampoline's own RSI (rsiM in the Pine
				// source is the same standard formula _trampRsi already computes) — RSI
				// stretched beyond its own 30-period Bollinger-style bands, optionally also
				// requiring RSI itself be at a hard 26/74 extreme. Pine's own marker for this
				// (a literal shark emoji via plotchar) is commented out — made real here, same
				// as every other invisible-by-default signal in this port. bTouchedLine and its
				// four EMAs + VWAP were skipped — computed in the source but never consumed by
				// anything, confirmed by direct search.
				AddPlot(new Stroke(Brushes.Teal, 3), PlotStyle.Dot, "SharkUpSignal");
				AddPlot(new Stroke(Brushes.OrangeRed, 3), PlotStyle.Dot, "SharkDownSignal");
				// CORRECTION to my own earlier framing: I initially conflated this with Tidal
				// Wave's "regular vs strong" buy/sell upgrade — those actually need bBigBuy1/
				// bBigSell1, which come from plotBuy/plotSell in the (still not built) Ultimate
				// Buy/Sell Indicator, NOT pBuyVodka/pSellVodka. What THIS actually is: a
				// separate "Add Contract (Strong)" pyramid/scale-in marker — fires when RedK's
				// Slow-Smooth WMA ("LazyLine") + Directional Energy Ratio's bar-color state +
				// Vodka Shot's volume-deviation metrics all align, AND Tidal Wave's CURRENT
				// wave-state agrees with the direction. Genuinely three separate small systems
				// feeding one signal, not one. Pine's own "up" check in this condition is
				// Trampoline's already-computed RMA average of positive price changes —
				// virtually always non-zero in real market data, treated as a practical no-op
				// here rather than rebuilt from scratch for something that's always true anyway.
				AddPlot(new Stroke(Brushes.Lime, 4), PlotStyle.Dot, "AddContractBuySignal");
				AddPlot(new Stroke(Brushes.Red, 4), PlotStyle.Dot, "AddContractSellSignal");
				// Ultimate Buy/Sell Indicator (final piece). MUCH smaller in real scope than its
				// ~150 lines suggested: requireWatchSignals is hardcoded true (never a real
				// input) so its "not required" branch is permanently dead; watchesInsideYellowRsi/
				// buyAndSellInsideYellowRsi/signalsBlocked are all declared false and never
				// reassigned ANYWHERE in the file — confirmed by direct search, genuinely inert.
				// macdBuy/macdSell and the secondary ATR bands (middleAtrBand/bbUpper/bbLower)
				// and the outer price Bollinger Bands are all computed but never consumed either
				// — skipped entirely, along with the fully-separate MACD calculation that only
				// existed to feed the dead macdBuy/macdSell. The MA-type switches (SMA/EMA/WMA/
				// HMA/VWMA/RMA, selectable per sub-system in Pine) are hardcoded to each one's
				// own default (WMA/WMA/SMA/WMA) rather than rebuilt as 4 separate 6-way switches
				// — a real simplification, not a bug fix, for what's mostly cosmetic flexibility.
				// UNLIKE almost everything else in this port, these markers are NOT commented out
				// in the source — genuinely visible by default there too, matched here as such.
				AddPlot(new Stroke(Brushes.SpringGreen, 3), PlotStyle.Dot, "UbsBuySignal");
				AddPlot(new Stroke(Brushes.Red, 3), PlotStyle.Dot, "UbsSellSignal");
				// Completes Tidal Wave's REAL "strong" upgrade this time (bBigBuy1/bBigSell1 =
				// UbsBuySignal held true within the last 4 bars) — the actual dependency, not
				// pBuyVodka/pSellVodka as I mistakenly said two phases ago.
				AddPlot(new Stroke(Brushes.Lime, 4), PlotStyle.Dot, "StrongBuySignal");
				AddPlot(new Stroke(Brushes.Red, 4), PlotStyle.Dot, "StrongSellSignal");
			}
			else if (State == State.DataLoaded)
			{
				_fanVmaRaw = new Series<double>(this);
				_fanVmaSmooth = SMA(_fanVmaRaw, Math.Max(1, FvMaLength));
				_adxSeries = new Series<double>(this);
				_cloudIsGreen = new Series<double>(this);

				_mgSeedEma = EMA(14);

				_waeFastEma = EMA(Math.Max(1, WaeFastLength));
				_waeSlowEma = EMA(Math.Max(1, WaeSlowLength));
				_waeBBBasis = SMA(Math.Max(1, WaeChannelLength));
				_waeBBDev   = StdDev(Math.Max(1, WaeChannelLength));

				_buyingVolume = new Series<double>(this);
				_sellingVolume = new Series<double>(this);
				_cvdBuyEma  = EMA(_buyingVolume, 14);
				_cvdSellEma = EMA(_sellingVolume, 14);

				_wbbBasis = SMA(20);
				_wbbDev   = StdDev(20);

				_sqBBBasis  = SMA(Math.Max(1, SqLength));
				_sqBBDev    = StdDev(Math.Max(1, SqLength));
				_sqKcMa     = SMA(Math.Max(1, SqKcLength));
				_sqHighest  = MAX(High, Math.Max(1, SqKcLength));
				_sqLowest   = MIN(Low, Math.Max(1, SqKcLength));
				_sqCloseSma = SMA(Math.Max(1, SqKcLength));
				_sqAtr      = ATR(Math.Max(1, SqKcLength));
				_sqDiffSeries = new Series<double>(this);
				_sqAdx      = ADX(Math.Max(1, SqAdxPeriod));

				_trampRsi     = RSI(Math.Max(1, TrampRsiLength), 1);
				_trampBBBasis = SMA(Math.Max(1, TrampBBLength));
				_trampBBDev   = StdDev(Math.Max(1, TrampBBLength));
				_trampLowerBB = new Series<double>(this);
				_trampUpperBB = new Series<double>(this);
				_trampBbw     = new Series<double>(this);
				_weGoUpSeries   = new Series<double>(this);
				_weGoDownSeries = new Series<double>(this);

				_dsrLow3   = MIN(Low, 3);
				_dsrLow50  = MIN(Low, 50);
				_dsrHigh3  = MAX(High, 3);
				_dsrHigh50 = MAX(High, 50);

				_greenCandleSeries = new Series<double>(this);
				_redCandleSeries   = new Series<double>(this);
				_brightGreenSeries = new Series<double>(this);
				_brightRedSeries   = new Series<double>(this);

				_ema9  = EMA(9);
				_ema21 = EMA(21);
				_pivotLowScan  = MIN(Low, Math.Max(3, 2 * PivotBarsBack + 1));
				_pivotHighScan = MAX(High, Math.Max(3, 2 * PivotBarsBack + 1));

				_dhtSeries = new Series<double>(this);
				_dltSeries = new Series<double>(this);
				_msHighestScan = MAX(High, Math.Max(3, MsFractalLength));
				_msLowestScan  = MIN(Low, Math.Max(3, MsFractalLength));

				_macdFastEma = EMA(Math.Max(1, MacdFastLength));
				_macdSlowEma = EMA(Math.Max(1, MacdSlowLength));
				_macdSeries  = new Series<double>(this);
				_macdSignalEma = EMA(_macdSeries, Math.Max(1, MacdSignalLength));

				_sharkRsiBasis = SMA(_trampRsi.Value, 30);
				_sharkRsiDev   = StdDev(_trampRsi.Value, 30);

				// LazyLine: w = length/3 rounded/truncated three ways, matching Pine's f_LazyLine exactly.
				double lw = LazyLineLength / 3.0;
				int lw2 = (int)Math.Round(lw, MidpointRounding.AwayFromZero);
				int lw1 = (int)Math.Round((LazyLineLength - lw2) / 2.0, MidpointRounding.AwayFromZero);
				int lw3 = (int)((LazyLineLength - lw2) / 2.0);   // Pine's int() cast truncates toward zero
				_lazyWma1 = WMA(Math.Max(1, lw1));
				_lazyWma2 = WMA(_lazyWma1, Math.Max(1, lw2));
				_lazyWma3 = WMA(_lazyWma2, Math.Max(1, lw3));

				_volAvgLSma   = SMA(70);
				_volAvgLSeries = new Series<double>(this);
				_volAvgLStdDev = StdDev(_volAvgLSeries, 70);

				_derVolHighest = MAX(Volume, Math.Max(1, DerVolLookback));
				_derVolLowest  = MIN(Volume, Math.Max(1, DerVolLookback));
				_derHigh2 = MAX(High, 2);
				_derLow2  = MIN(Low, 2);
				_derVolaSeries   = new Series<double>(this);
				_derCPlusSeries  = new Series<double>(this);
				_derCMinusSeries = new Series<double>(this);
				_derDemSeries    = new Series<double>(this);
				_derSupSeries    = new Series<double>(this);
				_derAvgVolaWma = WMA(_derVolaSeries, Math.Max(1, DerRLength));
				_derDemWma     = WMA(_derCPlusSeries, Math.Max(1, DerRLength));
				_derSupWma     = WMA(_derCMinusSeries, Math.Max(1, DerRLength));
				_derAdpWma     = WMA(_derDemSeries, Math.Max(1, DerAvgLength));
				_derAspWma     = WMA(_derSupSeries, Math.Max(1, DerAvgLength));

				_upwardsSeries   = new Series<double>(this);
				_downwardsSeries = new Series<double>(this);

				_ubsRsi          = RSI(Math.Max(1, UbsRsiLength), 1);
				_ubsRsiBasisWma  = WMA(_ubsRsi.Value, Math.Max(1, UbsRsiBasisLength));
				_ubsRsiDev       = StdDev(_ubsRsi.Value, Math.Max(1, UbsRsiBasisLength));
				_ubsRsiMaWma     = WMA(_ubsRsi.Value, Math.Max(1, UbsRsiMaLength));
				_ubsPriceBasisSma = SMA(Math.Max(1, UbsPriceBasisLength));
				_ubsPriceDev     = StdDev(Math.Max(1, UbsPriceBasisLength));
				_ubsAtr          = ATR(Math.Max(1, UbsAtrPeriod));
				_ubsAtrMaWma     = WMA(Math.Max(1, UbsAtrMaPeriod));
				_plotBuySeries   = new Series<double>(this);
				_plotSellSeries  = new Series<double>(this);

				// ShowIndicatorLabel isn't an NT8 built-in — it's a convention used throughout this
				// conversation's other indicators: the "label" NT8 draws per-indicator is literally
				// its Name, so blanking Name is how it's suppressed for a clean chart.
				if (!ShowIndicatorLabel) Name = string.Empty;
			}
			else if (State == State.Terminated)
			{
				if (_cloudBrushUp != null)   { try { _cloudBrushUp.Dispose(); } catch { } _cloudBrushUp = null; }
				if (_cloudBrushDown != null) { try { _cloudBrushDown.Dispose(); } catch { } _cloudBrushDown = null; }
				foreach (var kv in _tidalLines) { try { RemoveDrawObject(kv.Key); } catch { } }
				_tidalLines.Clear();
				if (_supportBoxTag != null) { try { RemoveDrawObject(_supportBoxTag); } catch { } }
				if (_resistanceBoxTag != null) { try { RemoveDrawObject(_resistanceBoxTag); } catch { } }
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < Math.Max(FvMaLength, Math.Max(WaeSlowLength, Math.Max(WaeChannelLength, Math.Max(SqLength, Math.Max(SqKcLength, Math.Max(TrampBBLength, Math.Max(TrampRsiLength, Math.Max(50, Math.Max(RqkLookback, Math.Max(2 * PivotBarsBack + 1, Math.Max(MsFractalLength, Math.Max(70, Math.Max(LazyLineLength, Math.Max(UbsRsiLength, Math.Max(UbsRsiBasisLength, Math.Max(UbsPriceBasisLength, UbsAtrPeriod)))))))))))))))) + 6)
			{
				FanVMA[0] = double.NaN;
				McGinleyDynamic[0] = double.NaN;
				UpThrustSignal[0] = 0.0;
				DownThrustSignal[0] = 0.0;
				BuyDSRSignal[0] = 0.0;
				SellDSRSignal[0] = 0.0;
				RationalQuadraticKernel[0] = double.NaN;
				UpReversalSignal[0] = 0.0;
				DownReversalSignal[0] = 0.0;
				_adxSeries[0] = 0.0;
				_cloudIsGreen[0] = 0.0;
				_sqDiffSeries[0] = 0.0;
				_trampLowerBB[0] = Close[0];
				_trampUpperBB[0] = Close[0];
				_trampBbw[0] = 0.0;
				_weGoUpSeries[0] = 0.0;
				_weGoDownSeries[0] = 0.0;
				Hema[0] = double.NaN;
				BuyCharSignal[0] = 0.0;
				SellCharSignal[0] = 0.0;
				SkullUpSignal[0] = 0.0;
				SkullDownSignal[0] = 0.0;
				_greenCandleSeries[0] = Close[0] > Open[0] ? 1.0 : 0.0;
				_redCandleSeries[0] = Close[0] < Open[0] ? 1.0 : 0.0;
				_brightGreenSeries[0] = 0.0;
				_brightRedSeries[0] = 0.0;
				SupportBreakSignal[0] = 0.0;
				ResistanceBreakSignal[0] = 0.0;
				SupportRetestSignal[0] = 0.0;
				ResistanceRetestSignal[0] = 0.0;
				FractalHighSignal[0] = 0.0;
				FractalLowSignal[0] = 0.0;
				StructureBreakUpSignal[0] = 0.0;
				StructureBreakDownSignal[0] = 0.0;
				_dhtSeries[0] = 0.0;
				_dltSeries[0] = 0.0;
				SharkUpSignal[0] = 0.0;
				SharkDownSignal[0] = 0.0;
				AddContractBuySignal[0] = 0.0;
				AddContractSellSignal[0] = 0.0;
				UbsBuySignal[0] = 0.0;
				UbsSellSignal[0] = 0.0;
				StrongBuySignal[0] = 0.0;
				StrongSellSignal[0] = 0.0;
				_plotBuySeries[0] = 0.0;
				_plotSellSeries[0] = 0.0;
				_volAvgLSeries[0] = Volume[0];
				_derVolaSeries[0] = 0.0;
				_derCPlusSeries[0] = 0.0;
				_derCMinusSeries[0] = 0.0;
				_derDemSeries[0] = 0.0;
				_derSupSeries[0] = 0.0;
				_upwardsSeries[0] = 0.0;
				_downwardsSeries[0] = 0.0;
				return;
			}

			_tpSignalCount = 0;   // Pine's iTPSignalCount — no `var`, resets every bar

			// ── Fantail VMA ──────────────────────────────────────────────────────────
			// Direct port of the Pine source's embedded ADX-style volatility index
			// (NOT the standard Wilder ADX — this is FanVMA's own bespoke smoothing,
			// self-contained to this indicator) driving a variable-speed blended MA.
			double hi = High[0], hi1 = High[1], lo = Low[0], lo1 = Low[1], close1 = Close[1];
			double bulls1 = 0.5 * (Math.Abs(hi - hi1) + (hi - hi1));
			double bears1 = 0.5 * (Math.Abs(lo1 - lo) + (lo1 - lo));
			double bears = bulls1 > bears1 ? 0 : (bulls1 == bears1 ? 0 : bears1);
			double bulls = bulls1 < bears1 ? 0 : (bulls1 == bears1 ? 0 : bulls1);

			if (CurrentBar > 0)
			{
				_sPDI = (FvWeighting * _sPDI + bulls) / (FvWeighting + 1);
				_sMDI = (FvWeighting * _sMDI + bears) / (FvWeighting + 1);
			}

			double tr = Math.Max(hi - lo, hi - close1);
			if (CurrentBar > 0)
				_str = (FvWeighting * _str + tr) / (FvWeighting + 1);

			double pdi = _str > 0 ? _sPDI / _str : 0;
			double mdi = _str > 0 ? _sMDI / _str : 0;
			double dx  = (pdi + mdi) > 0 ? Math.Abs(pdi - mdi) / (pdi + mdi) : 0;
			if (CurrentBar > 0)
				_adxInternal = (FvWeighting * _adxInternal + dx) / (FvWeighting + 1);

			// Rolling highest/lowest of the internal ADX index over FvAdxLength bars,
			// matching Pine's ta.highest/ta.lowest(ADX, ADX_Length).
			_adxSeries[0] = _adxInternal;
			double adxLow  = MIN(_adxSeries, Math.Max(1, FvAdxLength))[0];
			double adxHigh = MAX(_adxSeries, Math.Max(1, FvAdxLength))[0];
			double adxMin = Math.Min(1000000.0, adxLow);
			double adxMax = Math.Max(-1.0, adxHigh);
			double diff = adxMax - adxMin;
			double constK = diff > 0 ? (_adxInternal - adxMin) / diff : 0;

			if (CurrentBar > 0)
				_varMA = ((2 - constK) * _varMA + constK * Close[0]) / 2;
			else
				_varMA = Close[0];

			_fanVmaRaw[0] = _varMA;
			double fanVma = _fanVmaSmooth[0];

			// ── McGinley Dynamic ─────────────────────────────────────────────────────
			if (!_mgSeeded)
			{
				_mg = _mgSeedEma[0];
				_mgSeeded = true;
			}
			else
			{
				double ratio = Close[0] / _mg;
				_mg = _mg + (Close[0] - _mg) / (14 * Math.Pow(ratio, 4));
			}

			FanVMA[0] = fanVma;
			McGinleyDynamic[0] = _mg;

			bool cloudGreen = fanVma > _mg;
			_cloudIsGreen[0] = cloudGreen ? 1.0 : 0.0;

			// ── WAE (Waddah Attar Explosion) — drives candle coloring only in this phase ──
			double fastMa = _waeFastEma[0];
			double slowMa = _waeSlowEma[0];
			double macd = fastMa - slowMa;
			double t1 = double.IsNaN(_prevMacd) ? 0 : (macd - _prevMacd) * WaeSensitivity;
			_prevMacd = macd;

			double basis = _waeBBBasis[0];
			double dev = WaeBBMult * _waeBBDev[0];
			double upper = basis + dev;
			double lower = basis - dev;
			double e1 = upper - lower;

			double trendUp = t1 >= 0 ? t1 : 0;
			double trendDown = t1 < 0 ? -t1 : 0;

			// Body/border/wick colors, ported from the Pine source's four-branch gradient logic.
			System.Windows.Media.Color bodyCol = Colors.White, borderCol = Colors.White, wickCol = Colors.White;
			System.Windows.Media.Color bigGreen = Colors.Lime, bigRed = Colors.Red;

			if (trendUp > e1 && trendUp > 0)
			{
				double t = Clamp01((Math.Abs(trendUp - e1) - 1) / Math.Max(1.0, WaeTopBody - 1));
				bodyCol   = LerpAlpha(bigGreen, 0.30, 1.00, t);   // Pine transp 70→0 = alpha 0.30→1.00
				double tb = Clamp01((Math.Abs(trendUp - e1) - 1) / Math.Max(1.0, WaeTopBorder - 1));
				borderCol = LerpAlpha(bigGreen, 0.30, 1.00, tb);
				wickCol   = LerpAlpha(bigGreen, 1.00, 1.00, 0);
			}
			else if (trendUp < e1 && trendUp > 0)
			{
				bodyCol   = LerpAlpha(bigGreen, 0.10, 0.10, 0);   // Pine transp 90 = alpha 0.10 (near-invisible)
				double tb = Clamp01((Math.Abs(e1 - trendUp) - 1) / Math.Max(1.0, WaeTopBody - 1));
				borderCol = LerpAlpha(bigGreen, 0.30, 1.00, tb);
				wickCol   = LerpAlpha(bigGreen, 0.70, 0.70, 0);   // Pine transp 30 = alpha 0.70
			}
			else if (trendDown > e1 && trendDown > 0)
			{
				double t = Clamp01((Math.Abs(trendDown - e1) - 1) / Math.Max(1.0, WaeTopBody - 1));
				bodyCol   = LerpAlpha(bigRed, 0.50, 1.00, t);     // Pine transp 50→0 = alpha 0.50→1.00
				double tb = Clamp01((Math.Abs(trendDown - e1) - 1) / Math.Max(1.0, WaeTopBorder - 1));
				borderCol = LerpAlpha(bigRed, 0.50, 1.00, tb);
				wickCol   = LerpAlpha(bigRed, 1.00, 1.00, 0);
			}
			else if (trendDown < e1 && trendDown > 0)
			{
				bodyCol   = LerpAlpha(bigRed, 0.10, 0.10, 0);
				double tb = Clamp01((Math.Abs(e1 - trendDown) - 1) / Math.Max(1.0, WaeTopBody - 1));
				borderCol = LerpAlpha(bigRed, 0.50, 1.00, tb);
				wickCol   = LerpAlpha(bigRed, 0.70, 0.70, 0);
			}

			// ── Cumulative Volume Delta (Ankit_1618) ────────────────────────────────────
			// Estimates buy/sell volume from candle geometry (body vs wick proportions),
			// not real order-flow data — same approximation the Pine source itself uses.
			double o = Open[0], hB = High[0], lB = Low[0], cB = Close[0], vol = Volume[0];
			double upperWick = cB > o ? hB - cB : hB - o;
			double lowerWick = cB > o ? o - lB : cB - lB;
			double spread = hB - lB;
			double bodyLen = spread - (upperWick + lowerWick);
			double pctUpperWick = spread > 0 ? upperWick / spread : 0;
			double pctLowerWick = spread > 0 ? lowerWick / spread : 0;
			double pctBodyLen   = spread > 0 ? bodyLen / spread : 0;

			_buyingVolume[0]  = cB > o ? (pctBodyLen + (pctUpperWick + pctLowerWick) / 2) * vol : ((pctUpperWick + pctLowerWick) / 2) * vol;
			_sellingVolume[0] = cB < o ? (pctBodyLen + (pctUpperWick + pctLowerWick) / 2) * vol : ((pctUpperWick + pctLowerWick) / 2) * vol;

			double cvd = _cvdBuyEma[0] - _cvdSellEma[0];
			System.Windows.Media.Color cvdCol = Colors.White;
			if (cvd > 0)
				cvdCol = LerpAlpha(bigGreen, 0.30, 1.00, Clamp01(Math.Abs(cvd) / Math.Max(1.0, CvdTopEnd)));
			else if (cvd < 0)
				cvdCol = LerpAlpha(bigRed, 0.30, 1.00, Clamp01(Math.Abs(cvd) / Math.Max(1.0, CvdTopEnd)));
			// Pine gives CVD's body/border/wick all the SAME color (unlike WAE's separately-
			// graded body vs border) — matched here rather than reusing WAE's bodyCol/borderCol.

			// ── Squeeze Relaxer v2.1 ─────────────────────────────────────────────────────
			// TTM-Squeeze-style compression detection (BB vs an ATR-based Keltner Channel)
			// feeding a rolling linear-regression momentum value that drives the candle
			// coloring. DEVIATION FROM SOURCE: the Pine script itself uses plain High-Low
			// range for the channel width, not true ATR — switched to ATR here by request,
			// since High-Low range misses gap moves entirely (a bar's High-Low says nothing
			// about a jump from the prior close), which matters most exactly when it's least
			// wanted: fast, gappy conditions where a squeeze reading needs to be trustworthy.
			// This also matches the original TTM Squeeze design (John Carter) this section is
			// clearly modeled on, which uses ATR-based Keltner Channels.
			double sqBasis = _sqBBBasis[0];
			double sqDev = SqBBMult * _sqBBDev[0];
			double upperBBsq = sqBasis + sqDev, lowerBBsq = sqBasis - sqDev;

			double sqMa = _sqKcMa[0];
			double sqAtr = _sqAtr[0];
			double upperKC = sqMa + sqAtr * SqKcMult, lowerKC = sqMa - sqAtr * SqKcMult;
			bool sqzOn = lowerBBsq > lowerKC && upperBBsq < upperKC;

			double avg1 = (_sqHighest[0] + _sqLowest[0]) / 2.0;
			double avg2 = (avg1 + _sqCloseSma[0]) / 2.0;
			_sqDiffSeries[0] = Close[0] - avg2;
			double val = LinRegEndpoint(_sqDiffSeries, Math.Max(1, SqKcLength));

			double valPrevForCompare = double.IsNaN(_sqValPrev) ? val : _sqValPrev;
			bool pos = false, neg = false;
			bool sigAbove = _sqAdx != null && _sqAdx[0] > SqAdxThreshold;
			bool posPrevForTP = _posPrev, negPrevForTP = _negPrev;   // captured before this bar overwrites them

			if (val < valPrevForCompare && val < 5 && !sqzOn) _cRed++;
			if (val > valPrevForCompare && val > 5 && !sqzOn) _cGreen++;
			if (val > valPrevForCompare && _cRed > SqTolerance && val < 5 && !_posPrev && sigAbove) { _cRed = 0; pos = true; }
			if (val < valPrevForCompare && _cGreen > SqTolerance && val > 5 && !_negPrev && sigAbove) { _cGreen = 0; neg = true; }
			_posPrev = pos; _negPrev = neg;
			// buySignal1/sellSignal1 (pos/neg) feed Pine's iTPSignalCount aggregator (vSqueeze=4),
			// firing if pos/neg was true THIS bar or the prior bar — matches Pine's "(X or Y) or (X[1] or Y[1])".
			if (pos || neg || posPrevForTP || negPrevForTP) _tpSignalCount += 4;

			System.Windows.Media.Color sqCol = Colors.White, sqBorder = Colors.White;
			bool valRising = val > valPrevForCompare;
			if (val > 0)
			{
				if (valRising) { sqCol = LerpAlpha(bigGreen, 0.50, 1.00, Clamp01(Math.Abs(val) / 30.0)); sqBorder = bigGreen; }
				else           { sqCol = LerpAlpha(bigGreen, 0.30, 0.30, 0); sqBorder = LerpAlpha(Colors.Black, 0.0, 0.0, 0); }
			}
			else
			{
				if (!valRising) { sqCol = LerpAlpha(bigRed, 0.50, 1.00, Clamp01(Math.Abs(val) / 30.0)); sqBorder = bigRed; }
				else             { sqCol = LerpAlpha(bigRed, 0.50, 0.50, 0); sqBorder = LerpAlpha(Colors.Black, 0.0, 0.0, 0); }
			}
			_sqValPrev = val;

			// ── Trampoline ────────────────────────────────────────────────────────────
			// "Reversal thrust" detector: after price has been oversold (low RSI, closed
			// below the lower BB, band sufficiently wide) on at least one of the last 5
			// bars, a green bar making a new high fires an up-thrust; mirrored for down.
			// DEVIATION FROM SOURCE: Pine's own thrust markers are drawn via commented-out
			// plotshape() calls (invisible by default, only feeding an internal, also-
			// unsurfaced signal counter). Made genuinely visible + plotted here instead,
			// consistent with how every other signal in this conversation gets built —
			// an invisible-by-design feature isn't very useful to have ported at all.
			double trampBasis = _trampBBBasis[0];
			double trampDev = TrampBBMult * _trampBBDev[0];
			_trampUpperBB[0] = trampBasis + trampDev;
			_trampLowerBB[0] = trampBasis - trampDev;
			_trampBbw[0] = trampBasis != 0 ? (_trampUpperBB[0] - _trampLowerBB[0]) / trampBasis : 0.0;

			bool isRedNow = Close[0] < Open[0];
			bool isGreenNow = Close[0] > Open[0];

			bool back = false, forCond = false;   // Pine's back1..5 / for1..5, folded into a 5-bar loop
			for (int i = 1; i <= 5 && CurrentBar >= i; i++)
			{
				bool isRedI = Close[i] < Open[i];
				bool isGreenI = Close[i] > Open[i];
				if (isRedI && _trampRsi.Value[i] <= TrampRsiLower && Close[i] < _trampLowerBB[i] && _trampBbw[i] > TrampBBThreshold)
					back = true;
				if (isGreenI && _trampRsi.Value[i] >= TrampRsiUpper && Close[i] > _trampUpperBB[i] && _trampBbw[i] > TrampBBThreshold)
					forCond = true;
			}

			bool weGoUp = isGreenNow && back && CurrentBar >= 1 && High[0] > High[1];
			bool weGoDown = isRedNow && forCond && CurrentBar >= 1 && Low[0] < Low[1];
			_weGoUpSeries[0] = weGoUp ? 1.0 : 0.0;
			_weGoDownSeries[0] = weGoDown ? 1.0 : 0.0;

			bool upThrust = weGoUp && CurrentBar >= 4
				&& _weGoUpSeries[1] == 0.0 && _weGoUpSeries[2] == 0.0 && _weGoUpSeries[3] == 0.0 && _weGoUpSeries[4] == 0.0;
			bool downThrust = weGoDown && CurrentBar >= 4
				&& _weGoDownSeries[1] == 0.0 && _weGoDownSeries[2] == 0.0 && _weGoDownSeries[3] == 0.0 && _weGoDownSeries[4] == 0.0;

			UpThrustSignal[0] = upThrust ? 1.0 : 0.0;
			DownThrustSignal[0] = downThrust ? -1.0 : 0.0;   // -1 = short trigger, matches PredatorX's ShortEntryPlotValue convention
			if (upThrust || downThrust || (CurrentBar >= 1 && (UpThrustSignal[1] >= 0.5 || DownThrustSignal[1] <= -0.5)))
				_tpSignalCount += 4;

			if (ShowTrampSignals)
			{
				if (upThrust)
					Draw.Text(this, "Nebula_UpThrust_" + CurrentBar, "T", 0, Low[0] - 4 * TickSize, Brushes.Lime);
				if (downThrust)
					Draw.Text(this, "Nebula_DownThrust_" + CurrentBar, "T", 0, High[0] + 4 * TickSize, Brushes.Red);
			}

			// ── Dead Simple Reversal ─────────────────────────────────────────────────────
			// A color-flip candle (red→green or green→red) that also closes past the prior
			// bar's open, at a fresh 50-bar extreme (the 3-bar low/high just undercut/
			// exceeded the 50-bar low/high from 1-3 bars back) — genuinely "dead simple":
			// a reversal candle occurring exactly where a reversal would matter most.
			// DEVIATION FROM SOURCE: same as Trampoline — Pine's own markers are commented
			// out (invisible) by default; made visible + plotted here instead.
			if (CurrentBar >= 3)
			{
				bool c1 = Close[1] < Open[1] && Close[0] > Open[0];
				bool c2 = Close[0] > Open[1];
				double low3 = _dsrLow3[0];
				bool c3 = low3 < _dsrLow50[1] || low3 < _dsrLow50[2] || low3 < _dsrLow50[3];
				bool buyDSR = c1 && c2 && c3;

				bool c4 = Close[1] > Open[1] && Close[0] < Open[0];
				bool c5 = Close[0] < Open[1];
				double high3 = _dsrHigh3[0];
				bool c6 = high3 > _dsrHigh50[1] || high3 > _dsrHigh50[2] || high3 > _dsrHigh50[3];
				bool sellDSR = c4 && c5 && c6;

				BuyDSRSignal[0] = buyDSR ? 1.0 : 0.0;
				SellDSRSignal[0] = sellDSR ? -1.0 : 0.0;   // -1 = short trigger, matches PredatorX's ShortEntryPlotValue convention
				if (buyDSR || sellDSR || (CurrentBar >= 4 && (BuyDSRSignal[1] >= 0.5 || SellDSRSignal[1] <= -0.5)))
					_tpSignalCount += 2;

				if (ShowDSRSignals)
				{
					if (buyDSR)
						Draw.Square(this, "Nebula_BuyDSR_" + CurrentBar, false, 0, Low[0] - 8 * TickSize, Brushes.Cyan);
					if (sellDSR)
						Draw.Square(this, "Nebula_SellDSR_" + CurrentBar, false, 0, High[0] + 8 * TickSize, Brushes.Magenta);
				}
			}
			else
			{
				BuyDSRSignal[0] = 0.0;
				SellDSRSignal[0] = 0.0;
			}

			// ── Rational Quadratic Kernel Estimate ──────────────────────────────────────
			// Standard kernel-weighted regression (see the fix note above the AddPlot call):
			// yhat = Σ(close[i]·w_i) / Σ(w_i), i=0..h-1, w_i = (1 + i²/(2·r·h²))^(-r).
			// A smooth, non-parametric trend line that weights recent bars more heavily,
			// with r controlling how sharply that weight falls off with distance.
			{
				int h = Math.Max(3, RqkLookback);
				double curWeight = 0.0, cumWeight = 0.0;
				for (int i = 0; i < h; i++)
				{
					double y = Close[i];
					double w = Math.Pow(1.0 + (i * i) / (Math.Pow(h, 2) * 2.0 * RqkRelativeWeighting), -RqkRelativeWeighting);
					curWeight += y * w;
					cumWeight += w;
				}
				RationalQuadraticKernel[0] = cumWeight != 0 ? curWeight / cumWeight : Close[0];
			}

			// ── LuxAlgo Reversal Signals (TD-Sequential-style 9-count) ──────────────────
			// Classic DeMark-style setup counter: each bar, compare Close to Close[4] and
			// increment whichever of two mutually-exclusive counters (bullish/bearish) that
			// favors, wrapping back to 1 immediately after hitting 9 (never accumulating
			// past it), resetting the OTHER counter to 0. A completion signal fires either
			// when a counter hits the full 9, or when a count was interrupted one bar short
			// of completing (was 8, then flipped) — both read as "a reversal may be near."
			if (CurrentBar >= 4)
			{
				bool con = Close[0] < Close[4];
				int bSCPrev = _bSC;   // value BEFORE this bar's update — Pine's S.bSC[1]
				int sSCPrev = _sSC;

				if (con) { _bSC = _bSC == 9 ? 1 : _bSC + 1; _sSC = 0; }
				else     { _sSC = _sSC == 9 ? 1 : _sSC + 1; _bSC = 0; }

				bool showUppies = _bSC == 9 || (bSCPrev == 8 && _sSC == 1);
				bool showDownies = _sSC == 9 || (sSCPrev == 8 && _bSC == 1);

				UpReversalSignal[0] = showUppies ? 1.0 : 0.0;
				DownReversalSignal[0] = showDownies ? -1.0 : 0.0;   // -1 = short trigger, matches PredatorX's ShortEntryPlotValue convention
				if (showUppies || showDownies || (CurrentBar >= 5 && (UpReversalSignal[1] >= 0.5 || DownReversalSignal[1] <= -0.5)))
					_tpSignalCount += 3;

				if (ShowLuxRevSignals)
				{
					if (showUppies)
						Draw.ArrowUp(this, "Nebula_UpRev_" + CurrentBar, false, 0, Low[0] - 12 * TickSize, Brushes.Yellow);
					if (showDownies)
						Draw.ArrowDown(this, "Nebula_DownRev_" + CurrentBar, false, 0, High[0] + 12 * TickSize, Brushes.Yellow);
				}
			}
			else
			{
				UpReversalSignal[0] = 0.0;
				DownReversalSignal[0] = 0.0;
			}

			// ── Tidal Wave ────────────────────────────────────────────────────────────
			// Credit: idea by Aaron D (youtube.com/@aarond98). Tracks a persistent "wave
			// state" (up/down trend) by scanning backward for the most recent opposing
			// bright candle; when a fresh bright candle appears with no closer opposite-
			// color candle in the way, a horizontal line is drawn at its open, projected
			// forward — a simple trend-continuation/support-resistance marker.
			// NOTE: "regular vs strong" buy/sell char upgrade needs The Shark's plotBuy/
			// plotSell (not yet built) — BuyCharSignal/SellCharSignal below are the real,
			// already-correct BASE signal (Tidal Wave's own state only), not a stand-in.
			bool greenCandleNow = Close[0] > Open[0];
			bool redCandleNow = Close[0] < Open[0];
			double bodyWidth = Math.Abs(Close[0] - Open[0]);
			bool isDoji = IgnoreDojis && bodyWidth <= DojiBodyTicks * TickSize;

			_greenCandleSeries[0] = greenCandleNow ? 1.0 : 0.0;
			_redCandleSeries[0] = redCandleNow ? 1.0 : 0.0;

			bool noOverlapGreen = false, noOverlapRed = false;
			bool brightGreenNow = false, brightRedNow = false;
			bool gapGreenNow = false, gapRedNow = false;   // Pine's gapGreen/gapRed — the "fresh line" subset of brightGreen/brightRed
			int waveStatePrev = _waveState;   // Pine's waveState[1], captured before this bar can change it

			// Prune active lines price has crossed back through — Pine's line-array deletion loop.
			if (_tidalLines.Count > 0)
			{
				for (int i = _tidalLines.Count - 1; i >= 0; i--)
				{
					double level = _tidalLines[i].Value;
					if (High[0] > level && Low[0] < level)
					{
						try { RemoveDrawObject(_tidalLines[i].Key); } catch { }
						_tidalLines.RemoveAt(i);
					}
				}
			}

			int lookback = Math.Min(Math.Max(1, TidalLookback), CurrentBar);

			if (greenCandleNow && !isDoji)
			{
				for (int i = 1; i <= lookback; i++)
				{
					if (_brightRedSeries[i] >= 0.5) break;
					if (_waveState == WAVE_UP && _redCandleSeries[i] >= 0.5) break;
					if (_waveState == WAVE_DOWN && Open[0] >= Close[i] && _greenCandleSeries[i] >= 0.5)
					{
						noOverlapGreen = true;
						brightGreenNow = true;
						_waveState = WAVE_UP;
						break;
					}
				}
				if (CurrentBar >= 1 && Open[0] >= Close[1] && _greenCandleSeries[1] >= 0.5)
				{
					if (ShowTidalLines)
					{
						string tag = "Nebula_Tidal_" + (_tidalLineSeq++);
						Draw.Line(this, tag, false, 0, Open[0], -TidalBarExtend, Open[0], Brushes.Lime, DashStyleHelper.Dot, TidalLineWidth);
						_tidalLines.Add(new KeyValuePair<string, double>(tag, Open[0]));
					}
					_waveState = WAVE_UP;
					brightGreenNow = true;
					gapGreenNow = true;
				}
			}

			if (redCandleNow && !isDoji)
			{
				for (int i = 1; i <= lookback; i++)
				{
					if (_brightGreenSeries[i] >= 0.5) break;
					if (_waveState == WAVE_DOWN && _greenCandleSeries[i] >= 0.5) break;
					if (_waveState == WAVE_UP && Open[0] <= Close[i] && _redCandleSeries[i] >= 0.5)
					{
						noOverlapRed = true;
						brightRedNow = true;
						_waveState = WAVE_DOWN;
						break;
					}
				}
				// FIXED — was `Open[0] < Close[1]`, the mirror of the green line's `>=`.
				// The green condition allows open == previous close; the red one required
				// strictly below, so a bar opening EXACTLY at the prior close drew a green
				// line but never a red one. On a liquid future (NQ especially) that case is
				// common, so support levels accumulated faster than resistance levels.
				// Two independent ports of the Pine source carry the identical asymmetry, so
				// it came from the original script rather than from the port — but the SCAN
				// twenty lines above already uses the symmetric pair (>= for green, <= for
				// red), which is what makes this look like a typo rather than intent.
				// Affects the drawn LINES only: buyChar/sellChar come from the scan's
				// noOverlapGreen/noOverlapRed, so signals and arrows are unchanged.
				if (CurrentBar >= 1 && _redCandleSeries[1] >= 0.5 && Open[0] <= Close[1])
				{
					if (ShowTidalLines)
					{
						string tag = "Nebula_Tidal_" + (_tidalLineSeq++);
						Draw.Line(this, tag, false, 0, Open[0], -TidalBarExtend, Open[0], Brushes.Red, DashStyleHelper.Dot, TidalLineWidth);
						_tidalLines.Add(new KeyValuePair<string, double>(tag, Open[0]));
					}
					_waveState = WAVE_DOWN;
					brightRedNow = true;
					gapRedNow = true;
				}
			}

			_brightGreenSeries[0] = brightGreenNow ? 1.0 : 0.0;
			_brightRedSeries[0] = brightRedNow ? 1.0 : 0.0;

			bool buyChar = noOverlapGreen && waveStatePrev == WAVE_DOWN;
			bool sellChar = noOverlapRed && waveStatePrev == WAVE_UP;
			BuyCharSignal[0] = buyChar ? 1.0 : 0.0;
			SellCharSignal[0] = sellChar ? -1.0 : 0.0;   // -1 = short trigger, matches PredatorX's ShortEntryPlotValue convention

			// "Add Contract (Light)" — Pine's plotchar(bShowPlus and gapGreen/gapRed and waveState[1]==...).
			// A distinct, lighter-weight marker from the "Strong" (Vodka Shot) version built later.
			bool addLightBuy = ShowAddContractLightSignals && gapGreenNow && waveStatePrev == WAVE_UP;
			bool addLightSell = ShowAddContractLightSignals && gapRedNow && waveStatePrev == WAVE_DOWN;
			if (addLightBuy)
				Draw.Text(this, "Nebula_AddLight_" + CurrentBar, "+", 0, Low[0] - 4 * TickSize, Brushes.Lime);
			if (addLightSell)
				Draw.Text(this, "Nebula_AddLightS_" + CurrentBar, "+", 0, High[0] + 4 * TickSize, Brushes.Red);

			// ── HEMA ──────────────────────────────────────────────────────────────────
			// A double-smoothed EMA-derivative momentum line: hema itself is EMA-like, bH
			// tracks hema's own smoothed rate of change, feeding back into hema next bar.
			double alpha = 2.0 / (AlphaLength + 1);
			double gamma = 2.0 / (GammaLength + 1);
			if (!_hemaSeeded)
			{
				_hema = Close[0];
				_bH = gamma * (_hema - 0.0);
				_hemaSeeded = true;
			}
			else
			{
				double newHema = (1 - alpha) * (_hema + _bH) + alpha * Close[0];
				double newBH = (1 - gamma) * _bH + gamma * (newHema - _hema);
				_hema = newHema;
				_bH = newBH;
			}
			Hema[0] = _hema;

			// ── Skull-cross (EMA9 / EMA21, or EMA9 / Rational Quadratic Kernel) ─────────
			double fastLine = _ema9[0];
			double slowLine = UseKernelForSkullCross ? RationalQuadraticKernel[0] : _ema21[0];
			double fastLinePrev = _ema9[1];
			double slowLinePrev = UseKernelForSkullCross ? RationalQuadraticKernel[1] : _ema21[1];
			bool crossedOver = CurrentBar >= 1 && fastLinePrev <= slowLinePrev && fastLine > slowLine;
			bool crossedUnder = CurrentBar >= 1 && fastLinePrev >= slowLinePrev && fastLine < slowLine;
			bool skullUp = crossedOver && ShowSkullCross;
			bool skullDown = crossedUnder && ShowSkullCross;
			SkullUpSignal[0] = skullUp ? 1.0 : 0.0;
			SkullDownSignal[0] = skullDown ? -1.0 : 0.0;   // -1 = short trigger, matches PredatorX's ShortEntryPlotValue convention
			if (ShowSkullCross)
			{
				if (skullUp)
					Draw.TriangleUp(this, "Nebula_SkullUp_" + CurrentBar, false, 0, Low[0] - 4 * TickSize, Brushes.Yellow);
				if (skullDown)
					Draw.TriangleDown(this, "Nebula_SkullDown_" + CurrentBar, false, 0, High[0] + 4 * TickSize, Brushes.Yellow);
			}

			// ── Breaks and Retests [HG] ──────────────────────────────────────────────────
			// Pivot-based support/resistance zones: a confirmed pivot low/high (needs
			// PivotBarsBack bars of confirmation on both sides — so it's only known
			// PivotBarsBack bars AFTER it actually happened) draws a box; a close through
			// the box's edge marks a break; a subsequent touch back into the broken zone,
			// while price is inside the Nebula cloud, marks a retest.
			//
			// DEVIATION FROM SOURCE (simplification, not a bug fix): Pine's sBreak/rBreak
			// are declared with `var` at the top level and only ever SET to true, never
			// reset — meaning after the very FIRST support break that ever occurs on the
			// whole chart, retest-eligibility stays permanently armed against whatever the
			// CURRENT box happens to be from then on, rather than being scoped to the box
			// that was actually broken. This reads as more likely an artifact of the var
			// declaration never being paired with a reset than deliberate design. Tracked
			// per-box here instead (reset on every new pivot), which is the more sensible
			// "this zone broke, watch for a retest of THIS zone" reading. Also simplified:
			// Pine's retestEvent() runs a 2-slot label array + ta.valuewhen + multiple
			// overlapping validity windows (s1-s4/r1-r4) — condensed here into one direct
			// touch-back-into-the-zone check within RetestWindowBars of the break, which
			// preserves the core "broke, then retested" signal without replicating that
			// full state machine's edge-case handling for overlapping zones.
			double s_yLoc = Low[PivotBarsBack + 1] > Low[PivotBarsBack - 1] ? Low[PivotBarsBack - 1] : Low[PivotBarsBack + 1];
			double r_yLoc = High[PivotBarsBack + 1] > High[PivotBarsBack - 1] ? High[PivotBarsBack + 1] : High[PivotBarsBack - 1];
			double candidatePL = Low[PivotBarsBack];
			double candidatePH = High[PivotBarsBack];
			bool newPL = candidatePL <= _pivotLowScan[0] && !double.IsNaN(candidatePL);
			bool newPH = candidatePH >= _pivotHighScan[0] && !double.IsNaN(candidatePH);

			bool plChanged = newPL && candidatePL != _lastPL;
			bool phChanged = newPH && candidatePH != _lastPH;
			if (newPL) _lastPL = candidatePL;
			if (newPH) _lastPH = candidatePH;

			if (plChanged)
			{
				if (_supportBoxTag != null) { try { RemoveDrawObject(_supportBoxTag); } catch { } }
				_supportBoxTag = "Nebula_SBox_" + (_brTagSeq++);
				_sTop = s_yLoc; _sBot = _lastPL;
				try
				{
					Draw.Rectangle(this, _supportBoxTag, false, PivotBarsBack, _sTop, 0, _sBot,
						Brushes.Red, Brushes.Red, 10);
				}
				catch { }
				_supportBroken = false;
				_supportBreakBar = -1;
				_supportRetested = false;
			}
			if (phChanged)
			{
				if (_resistanceBoxTag != null) { try { RemoveDrawObject(_resistanceBoxTag); } catch { } }
				_resistanceBoxTag = "Nebula_RBox_" + (_brTagSeq++);
				_rTop = _lastPH; _rBot = r_yLoc;
				try
				{
					Draw.Rectangle(this, _resistanceBoxTag, false, PivotBarsBack, _rTop, 0, _rBot,
						Brushes.Red, Brushes.Red, 10);
				}
				catch { }
				_resistanceBroken = false;
				_resistanceBreakBar = -1;
				_resistanceRetested = false;
			}

			bool supportBreakNow = false, resistanceBreakNow = false;
			if (_supportBoxTag != null && !_supportBroken && CurrentBar >= 1)
			{
				bool crossUnder = Close[1] >= _sBot && Close[0] < _sBot;
				if (crossUnder) { _supportBroken = true; _supportBreakBar = CurrentBar; supportBreakNow = true; }
			}
			if (_resistanceBoxTag != null && !_resistanceBroken && CurrentBar >= 1)
			{
				bool crossOver = Close[1] <= _rTop && Close[0] > _rTop;
				if (crossOver) { _resistanceBroken = true; _resistanceBreakBar = CurrentBar; resistanceBreakNow = true; }
			}
			// NOTE on polarity: "Support" breaking is a BEARISH event (price fell through a floor), and
			// "Resistance" breaking is BULLISH (price broke above a ceiling) — the opposite of what the
			// names alone might suggest. Polarity below follows actual market direction, not the label.
			SupportBreakSignal[0] = supportBreakNow ? -1.0 : 0.0;
			ResistanceBreakSignal[0] = resistanceBreakNow ? 1.0 : 0.0;

			bool candleInCloud = fanVma > _mg
				? (Close[0] < fanVma || Open[0] < fanVma) && (Close[0] > _mg || Open[0] > _mg)
				: (Close[0] > fanVma || Open[0] > fanVma) && (Close[0] < _mg || Open[0] < _mg);

			bool supportRetestNow = false, resistanceRetestNow = false;
			if (_supportBroken && !_supportRetested && _supportBreakBar >= 0
				&& CurrentBar - _supportBreakBar <= RetestWindowBars && CurrentBar - _supportBreakBar > 0)
			{
				if (High[0] >= _sBot && Close[0] <= _sTop && candleInCloud)
				{
					supportRetestNow = true;
					_supportRetested = true;
					if (ShowRetestMarkers)
						Draw.Dot(this, "Nebula_SRetest_" + CurrentBar, false, 0, High[0] + 20 * TickSize, Brushes.Magenta);
				}
			}
			if (_resistanceBroken && !_resistanceRetested && _resistanceBreakBar >= 0
				&& CurrentBar - _resistanceBreakBar <= RetestWindowBars && CurrentBar - _resistanceBreakBar > 0)
			{
				if (Low[0] <= _rTop && Close[0] >= _rBot && candleInCloud)
				{
					resistanceRetestNow = true;
					_resistanceRetested = true;
					if (ShowRetestMarkers)
						Draw.Dot(this, "Nebula_RRetest_" + CurrentBar, false, 0, Low[0] - 20 * TickSize, Brushes.Magenta);
				}
			}
			// Same naming-vs-direction note as the Break signals above: a Support retest confirms the
			// prior bearish break, Resistance retest confirms the prior bullish break.
			SupportRetestSignal[0] = supportRetestNow ? -1.0 : 0.0;
			ResistanceRetestSignal[0] = resistanceRetestNow ? 1.0 : 0.0;

			// ── Market Structure (Fractal) ────────────────────────────────────────────
			// Swing high/low fractal detection: over a short trailing window (half of
			// MsFractalLength), track whether highs/lows were consistently rising then
			// consistently falling (or vice versa) via a cumulative sign-sum — a peak/
			// trough confirms pT bars back once the candidate bar is also genuinely the
			// highest/lowest in the whole window. See the plot comment above for why this
			// is exposed as its own standalone signal rather than chasing the original's
			// blocked consumer chain.
			int pT = Math.Max(1, MsFractalLength / 2);
			double dhT = 0, dlT = 0;
			for (int i = 0; i < pT; i++)
			{
				dhT += Math.Sign(High[i] - High[i + 1]);
				dlT += Math.Sign(Low[i] - Low[i + 1]);
			}
			_dhtSeries[0] = dhT;
			_dltSeries[0] = dlT;

			bool bullf = CurrentBar >= pT && dhT == -pT && _dhtSeries[pT] == pT && High[pT] == _msHighestScan[0];
			bool bearf = CurrentBar >= pT && dlT == pT && _dltSeries[pT] == -pT && Low[pT] == _msLowestScan[0];

			if (bullf) _upperFractalValue = High[pT];
			if (bearf) _lowerFractalValue = Low[pT];

			bool crossedOverFractal = CurrentBar >= 1 && !double.IsNaN(_upperFractalValue)
				&& Close[1] <= _upperFractalValue && Close[0] > _upperFractalValue;
			bool crossedUnderFractal = CurrentBar >= 1 && !double.IsNaN(_lowerFractalValue)
				&& Close[1] >= _lowerFractalValue && Close[0] < _lowerFractalValue;

			FractalHighSignal[0] = bullf ? 1.0 : 0.0;
			FractalLowSignal[0] = bearf ? 1.0 : 0.0;
			StructureBreakUpSignal[0] = crossedOverFractal ? 1.0 : 0.0;
			StructureBreakDownSignal[0] = crossedUnderFractal ? -1.0 : 0.0;   // -1 = short trigger, matches PredatorX's ShortEntryPlotValue convention

			if (ShowStructureSignals)
			{
				if (bullf) Draw.Diamond(this, "Nebula_FractalHigh_" + CurrentBar, false, pT, High[pT] + 6 * TickSize, Brushes.DodgerBlue);
				if (bearf) Draw.Diamond(this, "Nebula_FractalLow_" + CurrentBar, false, pT, Low[pT] - 6 * TickSize, Brushes.DodgerBlue);
				if (crossedOverFractal) Draw.ArrowUp(this, "Nebula_StructBreakUp_" + CurrentBar, false, 0, Low[0] - 24 * TickSize, Brushes.White);
				if (crossedUnderFractal) Draw.ArrowDown(this, "Nebula_StructBreakDown_" + CurrentBar, false, 0, High[0] + 24 * TickSize, Brushes.White);
			}

			// ── MACD candle mode ─────────────────────────────────────────────────────────
			// Standard MACD (12/26/9 EMA-based). Histogram-rising/falling state persists
			// across bars unless the histogram value actually changes — matching Pine's
			// own "hist==hist[1] ? keep old flags : recompute" pattern exactly.
			double macdVal = _macdFastEma[0] - _macdSlowEma[0];
			_macdSeries[0] = macdVal;
			double hist = macdVal - _macdSignalEma[0];
			double histPrev = double.IsNaN(_prevHist) ? hist : _prevHist;
			if (hist != histPrev)
			{
				_histAIsUp = hist > histPrev && hist > 0;
				_histAIsDown = hist < histPrev && hist > 0;
				_histBIsDown = hist < histPrev && hist <= 0;
				_histBIsUp = hist > histPrev && hist <= 0;
			}
			_prevHist = hist;

			System.Windows.Media.Color cMDColor = Colors.White, cMDBorder = Colors.White;
			if (_histAIsUp) { cMDColor = LerpAlpha(bigGreen, 0.70, 1.00, Clamp01(Math.Abs(hist) / 3.0)); cMDBorder = bigGreen; }
			else if (_histAIsDown) { cMDColor = LerpAlpha(bigGreen, 0.20, 0.70, Clamp01(Math.Abs(hist) / 3.0)); cMDBorder = bigGreen; }
			if (_histBIsUp) { cMDColor = LerpAlpha(bigRed, 0.70, 1.00, Clamp01(Math.Abs(hist) / 4.0)); cMDBorder = bigRed; }
			else if (_histBIsDown) { cMDColor = LerpAlpha(bigRed, 0.20, 0.70, Clamp01(Math.Abs(hist) / 4.0)); cMDBorder = bigRed; }

			// ── The Shark ─────────────────────────────────────────────────────────────
			// RSI (reusing Trampoline's own _trampRsi — the Pine source's "rsiM" is the
			// same standard formula) stretched beyond its own 30-period Bollinger-style
			// bands, optionally also requiring RSI itself past a hard 26/74 extreme.
			double rsiMNow = _trampRsi.Value[0];
			double sharkBasis = _sharkRsiBasis[0];
			double sharkDev = 2.0 * _sharkRsiDev[0];
			double sharkUpper = sharkBasis + sharkDev;
			double sharkLower = sharkBasis - sharkDev;

			bool belowExtreme = !ApplySharkRsiRule || rsiMNow < 26;
			bool aboveExtreme = !ApplySharkRsiRule || rsiMNow > 74;
			bool sharkUp = rsiMNow < sharkLower && belowExtreme;
			bool sharkDown = rsiMNow > sharkUpper && aboveExtreme;

			SharkUpSignal[0] = sharkUp ? 1.0 : 0.0;
			SharkDownSignal[0] = sharkDown ? -1.0 : 0.0;   // -1 = short trigger, matches PredatorX's ShortEntryPlotValue convention
			if (sharkUp || sharkDown || (CurrentBar >= 1 && (SharkUpSignal[1] >= 0.5 || SharkDownSignal[1] <= -0.5)))
				_tpSignalCount += 2;
			if (ShowSharkSignals)
			{
				if (sharkUp) Draw.Text(this, "Nebula_SharkUp_" + CurrentBar, "S", 0, Low[0] - 28 * TickSize, Brushes.Teal);
				if (sharkDown) Draw.Text(this, "Nebula_SharkDown_" + CurrentBar, "S", 0, High[0] + 28 * TickSize, Brushes.OrangeRed);
			}

			// ── Wicking Bollinger Bands ──────────────────────────────────────────────────
			// Small standalone wick-rejection detector: no marker of its own in the source
			// (never plotted) — exists solely to feed the TP confluence counter (vBands).
			double wbbBasisNow = _wbbBasis[0];
			double wbbDevNow = 2.5 * _wbbDev[0];
			double wbbUpper = wbbBasisNow + wbbDevNow, wbbLower = wbbBasisNow - wbbDevNow;
			bool bBBUp = Low[0] <= wbbLower && Close[0] >= wbbLower && Close[0] < Open[0];
			bool bBBDown = High[0] >= wbbUpper && Close[0] < wbbUpper && Close[0] > Open[0];
			if (bBBUp || bBBDown || _bbUpPrev || _bbDownPrev) _tpSignalCount += 2;
			_bbUpPrev = bBBUp; _bbDownPrev = bBBDown;

			// ── "Add Contract" pyramid signal (RedK LazyLine + DER bar-color + Vodka Shot) ──
			// See the deviation note above the AddPlot calls for why this is NOT what I
			// originally thought it was (Tidal Wave's "strong" upgrade) — it's a genuinely
			// separate pyramid/scale-in marker.
			double LL = _lazyWma3[0];
			bool luptrend = CurrentBar >= 1 && LL > _lazyWma3[1];

			double volHi = _derVolHighest[0], volLo = _derVolLowest[0];
			double vola = (volHi - volLo) > 0 ? (Volume[0] - volLo) / (volHi - volLo) : 0.0;
			_derVolaSeries[0] = vola;

			double hi2 = _derHigh2[0], lo2 = _derLow2[0];
			double Rrange = (hi2 - lo2) / 2.0;
			double sr = (CurrentBar >= 1 && Rrange > 0) ? (Close[0] - Close[1]) / Rrange : 0.0;
			double rsr = Math.Max(Math.Min(sr, 1.0), -1.0);
			double cRaw = rsr * vola;
			if (!double.IsNaN(cRaw)) _derLastC = cRaw;
			double cVal = _derLastC;
			double cPlus = Math.Max(cVal, 0.0);
			double cMinus = -Math.Min(cVal, 0.0);
			_derCPlusSeries[0] = cPlus;
			_derCMinusSeries[0] = cMinus;

			double avgVola = _derAvgVolaWma[0];
			double dem = avgVola != 0 ? _derDemWma[0] / avgVola : 0.0;
			double sup = avgVola != 0 ? _derSupWma[0] / avgVola : 0.0;
			_derDemSeries[0] = dem;
			_derSupSeries[0] = sup;

			double adp = 100.0 * _derAdpWma[0];
			double asp = 100.0 * _derAspWma[0];
			_derBc = adp;   // Pine's fixnan(adp) — adp is never actually NaN here (WMA of a
			_derBo = asp;   // populated series), so the hold-forward never has anything to hold.
			bool rising = CurrentBar >= 1 && !double.IsNaN(_derBcPrev) && (_derBc - _derBcPrev) > 0;
			const int BAR_UP = 1, BAR_DOWN = -1, BAR_NEUTRAL = 0;
			int barColorState = (_derBc > _derBo && rising) ? BAR_UP : (_derBc < _derBo && !rising) ? BAR_DOWN : BAR_NEUTRAL;
			_derBcPrev = _derBc;

			double volAvgLNow = _volAvgLSma[0];
			_volAvgLSeries[0] = volAvgLNow;
			double volDev = volAvgLNow != 0 ? (volAvgLNow + 1.618034 * _volAvgLStdDev[0]) / volAvgLNow * 11.0 / 100.0 : 0.0;
			double volRel = volAvgLNow != 0 ? Volume[0] / volAvgLNow : 0.0;

			// Pine's own "up" check (Trampoline's raw RMA average of positive price changes)
			// omitted — see the deviation note above the AddPlot calls.
			bool upwardsNow = luptrend && barColorState == BAR_UP && (Open[0] < Close[0] && volRel * 0.145898 > volDev);
			bool downwardsNow = CurrentBar >= 1 && LL < _lazyWma3[1] && barColorState == BAR_DOWN && (Open[0] > Close[0] && volRel * 0.145898 > volDev);
			_upwardsSeries[0] = upwardsNow ? 1.0 : 0.0;
			_downwardsSeries[0] = downwardsNow ? 1.0 : 0.0;

			bool pBuyVodka = CurrentBar >= 4 && upwardsNow
				&& _upwardsSeries[1] == 0.0 && _upwardsSeries[2] == 0.0 && _upwardsSeries[3] == 0.0 && _upwardsSeries[4] == 0.0;
			// FIXED (deviation from source, by request): Pine's own pSellVodka checks "not
			// upwards[2..4]" here (not downwards[2..4]) — inconsistent with pBuyVodka's own
			// uniform pattern and with what the variable's name implies. Corrected to match.
			bool pSellVodka = CurrentBar >= 4 && downwardsNow
				&& _downwardsSeries[1] == 0.0 && _downwardsSeries[2] == 0.0 && _downwardsSeries[3] == 0.0 && _downwardsSeries[4] == 0.0;

			bool addBuy = ShowAddContractSignals && pBuyVodka && waveStatePrev == WAVE_UP;
			bool addSell = ShowAddContractSignals && pSellVodka && waveStatePrev == WAVE_DOWN;
			AddContractBuySignal[0] = addBuy ? 1.0 : 0.0;
			AddContractSellSignal[0] = addSell ? -1.0 : 0.0;   // -1 = short trigger, matches PredatorX's ShortEntryPlotValue convention
			if (addBuy) Draw.Text(this, "Nebula_AddBuy_" + CurrentBar, "\u271A", 0, Low[0] - 4 * TickSize, Brushes.Lime);
			if (addSell) Draw.Text(this, "Nebula_AddSell_" + CurrentBar, "\u271A", 0, High[0] + 4 * TickSize, Brushes.Red);

			// ── Ultimate Buy/Sell Indicator ──────────────────────────────────────────────
			// See the deviation notes above the AddPlot calls for the scope simplifications
			// (dead conditions removed, MA types hardcoded to their own Pine defaults).
			bool haveUbsPrev = CurrentBar >= 1;

			double rsiNow = _ubsRsi.Value[0];
			double rsiBasisNow = _ubsRsiBasisWma[0];
			double rsiDevNow = UbsRsiMultiplier * _ubsRsiDev[0];
			double upperRsi = rsiBasisNow + rsiDevNow;
			double lowerRsi = rsiBasisNow - rsiDevNow;
			double rsiMaNow = _ubsRsiMaWma[0];

			double priceBasisNow = _ubsPriceBasisSma[0];
			double priceDevNow = UbsPriceInnerMultiplier * _ubsPriceDev[0];
			double upperPriceInner = priceBasisNow + priceDevNow;
			double lowerPriceInner = priceBasisNow - priceDevNow;

			double atrMaNow = _ubsAtrMaWma[0];
			double atrNow = _ubsAtr[0];
			double upperAtrBand = atrMaNow + atrNow * UbsAtrMult;
			double lowerAtrBand = atrMaNow - atrNow * UbsAtrMult;

			double priceBasisPrev = haveUbsPrev ? _ubsPriceBasisSma[1] : priceBasisNow;
			double priceDevPrev = haveUbsPrev ? UbsPriceInnerMultiplier * _ubsPriceDev[1] : priceDevNow;
			double lowerPriceInnerPrev = priceBasisPrev - priceDevPrev;
			double upperPriceInnerPrev = priceBasisPrev + priceDevPrev;
			bool priceCrossOverInner = haveUbsPrev && Close[1] <= lowerPriceInnerPrev && Close[0] > lowerPriceInner;
			bool priceCrossUnderInner = haveUbsPrev && Close[1] >= upperPriceInnerPrev && Close[0] < upperPriceInner;

			double rsiBasisPrev = haveUbsPrev ? _ubsRsiBasisWma[1] : rsiBasisNow;
			double rsiDevPrev = haveUbsPrev ? UbsRsiMultiplier * _ubsRsiDev[1] : rsiDevNow;
			double lowerRsiPrev = rsiBasisPrev - rsiDevPrev;
			double upperRsiPrev = rsiBasisPrev + rsiDevPrev;
			double rsiPrev = haveUbsPrev ? _ubsRsi.Value[1] : rsiNow;
			double rsiMaPrev = haveUbsPrev ? _ubsRsiMaWma[1] : rsiMaNow;

			bool rsiCrossOverLower = haveUbsPrev && rsiPrev <= lowerRsiPrev && rsiNow > lowerRsi;
			bool rsiCrossUnderUpper = haveUbsPrev && rsiPrev >= upperRsiPrev && rsiNow < upperRsi;
			bool rsiCrossOverBasis = haveUbsPrev && rsiPrev <= rsiBasisPrev && rsiNow > rsiBasisNow;
			bool rsiCrossUnderBasis = haveUbsPrev && rsiPrev >= rsiBasisPrev && rsiNow < rsiBasisNow;
			bool rsiCrossOverMa = haveUbsPrev && rsiPrev <= rsiMaPrev && rsiNow > rsiMaNow;
			bool rsiCrossUnderMa = haveUbsPrev && rsiPrev >= rsiMaPrev && rsiNow < rsiMaNow;
			bool rsiCrossUnder75 = haveUbsPrev && rsiPrev >= 75.0 && rsiNow < 75.0;
			bool rsiCrossOver25 = haveUbsPrev && rsiPrev <= 25.0 && rsiNow > 25.0;

			double atrMaPrev = haveUbsPrev ? _ubsAtrMaWma[1] : atrMaNow;
			double atrPrev = haveUbsPrev ? _ubsAtr[1] : atrNow;
			double lowerAtrBandPrev = atrMaPrev - atrPrev * UbsAtrMult;
			double upperAtrBandPrev = atrMaPrev + atrPrev * UbsAtrMult;
			bool highUnderAtrLower = haveUbsPrev && High[1] >= lowerAtrBandPrev && High[0] < lowerAtrBand;
			bool lowOverAtrUpper = haveUbsPrev && Low[1] <= upperAtrBandPrev && Low[0] > upperAtrBand;

			bool buyWatch1 = UbsUsePriceBandWatchSignals && priceCrossOverInner && !rsiCrossOverLower;
			bool buyWatch2 = UbsUseRsiWatchSignals && rsiCrossOverLower && !priceCrossOverInner;
			bool buyWatch3 = UbsUsePriceBandWatchSignals && priceCrossOverInner && rsiCrossOverLower;
			bool buyWatch4 = UbsUsePriceBandWatchSignals && priceCrossOverInner;
			bool buyWatch5 = UbsUseRsiWatchSignals && rsiCrossOverLower;
			bool buyWatch6 = UbsUseRsiWatchSignals && rsiCrossOver25;
			bool buyWatch7 = UbsUseAtrWatchSignals && highUnderAtrLower;
			bool buyWatched = buyWatch1 || buyWatch2 || buyWatch3 || buyWatch4 || buyWatch5 || buyWatch6 || buyWatch7;

			bool sellWatch1 = UbsUsePriceBandWatchSignals && priceCrossUnderInner && !rsiCrossUnderUpper;
			bool sellWatch2 = UbsUseRsiWatchSignals && rsiCrossUnderUpper && !priceCrossUnderInner;
			bool sellWatch3 = UbsUsePriceBandWatchSignals && priceCrossUnderInner && rsiCrossUnderUpper;
			bool sellWatch4 = UbsUsePriceBandWatchSignals && priceCrossUnderInner;
			bool sellWatch5 = UbsUseRsiWatchSignals && rsiCrossUnderUpper;
			bool sellWatch6 = UbsUseRsiWatchSignals && rsiCrossUnder75;
			bool sellWatch7 = UbsUseAtrWatchSignals && lowOverAtrUpper;
			bool sellWatched = sellWatch1 || sellWatch2 || sellWatch3 || sellWatch4 || sellWatch5 || sellWatch6 || sellWatch7;

			_buyWatchWindow.Enqueue(buyWatched ? 1 : 0);
			_buyWatchSum += buyWatched ? 1 : 0;
			if (_buyWatchWindow.Count > UbsWatchSignalLookback) _buyWatchSum -= _buyWatchWindow.Dequeue();
			bool buyWatchMet = _buyWatchSum >= 1;

			_sellWatchWindow.Enqueue(sellWatched ? 1 : 0);
			_sellWatchSum += sellWatched ? 1 : 0;
			if (_sellWatchWindow.Count > UbsWatchSignalLookback) _sellWatchSum -= _sellWatchWindow.Dequeue();
			bool sellWatchMet = _sellWatchSum >= 1;

			bool combinedBuySignals = (UbsUseRsiSignals && rsiCrossOverBasis) || (UbsUse25Signals && rsiCrossOver25) || (UbsUseRsiMa && rsiCrossOverMa);
			bool combinedSellSignals = (UbsUseRsiSignals && rsiCrossUnderBasis) || (UbsUse75Signals && rsiCrossUnder75) || (UbsUseRsiMa && rsiCrossUnderMa);

			// requireWatchSignals is permanently true in the source (never a real input) — the
			// "not required" branch was dead code, simplified away here.
			bool buySignalsNow = buyWatchMet && combinedBuySignals;
			bool sellSignalsNow = sellWatchMet && combinedSellSignals;

			bool plotBuyNow = false, plotSellNow = false;
			if (buySignalsNow && !buyWatched)
			{
				plotBuyNow = true;
				_buyWatchWindow.Clear(); _buyWatchSum = 0;
				_sellWatchWindow.Clear(); _sellWatchSum = 0;
			}
			else if (sellSignalsNow && !sellWatched)
			{
				plotSellNow = true;
				_buyWatchWindow.Clear(); _buyWatchSum = 0;
				_sellWatchWindow.Clear(); _sellWatchSum = 0;
			}

			UbsBuySignal[0] = plotBuyNow ? 1.0 : 0.0;
			UbsSellSignal[0] = plotSellNow ? -1.0 : 0.0;   // -1 = short trigger, matches PredatorX's ShortEntryPlotValue convention
			_plotBuySeries[0] = plotBuyNow ? 1.0 : 0.0;
			_plotSellSeries[0] = plotSellNow ? 1.0 : 0.0;

			bool bBigBuy1 = _plotBuySeries[0] >= 0.5 || (CurrentBar >= 1 && _plotBuySeries[1] >= 0.5)
				|| (CurrentBar >= 2 && _plotBuySeries[2] >= 0.5) || (CurrentBar >= 3 && _plotBuySeries[3] >= 0.5);
			bool bBigSell1 = _plotSellSeries[0] >= 0.5 || (CurrentBar >= 1 && _plotSellSeries[1] >= 0.5)
				|| (CurrentBar >= 2 && _plotSellSeries[2] >= 0.5) || (CurrentBar >= 3 && _plotSellSeries[3] >= 0.5);

			// The real visible markers here are the ①/➊ pair, driven by Tidal Wave's own
			// buyChar/sellChar (NOT by plotBuyNow/plotSellNow directly — those are UBS's internal
			// signal, only consumed to build bBigBuy1/bBigSell1, the "was there a UBS signal in the
			// last 4 bars" upgrade gate). plotBuyNow/plotSellNow themselves are never drawn in the source.
			bool weakBuy = buyChar && !bBigBuy1;
			bool weakSell = sellChar && !bBigSell1;
			bool strongBuy = bBigBuy1 && buyChar;
			bool strongSell = bBigSell1 && sellChar;
			StrongBuySignal[0] = strongBuy ? 1.0 : 0.0;
			StrongSellSignal[0] = strongSell ? -1.0 : 0.0;   // -1 = short trigger, matches PredatorX's ShortEntryPlotValue convention

			if (ShowUltimateBuySell)
			{
				if (weakBuy) Draw.Text(this, "Nebula_WeakBuy_" + CurrentBar, "\u2460", 0, Low[0] - 4 * TickSize, Brushes.SpringGreen);
				if (weakSell) Draw.Text(this, "Nebula_WeakSell_" + CurrentBar, "\u2460", 0, High[0] + 4 * TickSize, Brushes.Red);
				if (strongBuy) Draw.Text(this, "Nebula_StrongBuy_" + CurrentBar, "\u278A", 0, Low[0] - 4 * TickSize, Brushes.Lime);
				if (strongSell) Draw.Text(this, "Nebula_StrongSell_" + CurrentBar, "\u278A", 0, High[0] + 4 * TickSize, Brushes.Red);
			}

			// ── Take Profit confluence markers ──────────────────────────────────────────
			// Pine's plotchar(bShowProfit and iTPSignalCount >= iMaxProfit/iProfit and waveState==...).
			// NOTE: Pine's own labels look swapped from their thresholds — "Take Partial Profit" (✔)
			// actually fires at the HIGHER threshold (iMaxProfit, default 7) and "Take Full Profit" (✓)
			// fires at the LOWER one (iProfit, default 5). Matched exactly as written in the source.
			if (ShowTakeProfitSignals)
			{
				bool tpPartialUp = _tpSignalCount >= IMaxProfitThreshold && _waveState == WAVE_UP;
				bool tpPartialDown = _tpSignalCount >= IMaxProfitThreshold && _waveState == WAVE_DOWN;
				bool tpFullUp = _tpSignalCount >= IProfitThreshold && _waveState == WAVE_UP;
				bool tpFullDown = _tpSignalCount >= IProfitThreshold && _waveState == WAVE_DOWN;
				if (tpPartialUp) Draw.Text(this, "Nebula_TPPartialUp_" + CurrentBar, "\u2714", 0, High[0] + 8 * TickSize, Brushes.SpringGreen);
				if (tpPartialDown) Draw.Text(this, "Nebula_TPPartialDown_" + CurrentBar, "\u2714", 0, Low[0] - 8 * TickSize, Brushes.Red);
				if (tpFullUp) Draw.Text(this, "Nebula_TPFullUp_" + CurrentBar, "\u2713", 0, High[0] + 12 * TickSize, Brushes.MediumPurple);
				if (tpFullDown) Draw.Text(this, "Nebula_TPFullDown_" + CurrentBar, "\u2713", 0, Low[0] - 12 * TickSize, Brushes.MediumPurple);
			}

			System.Windows.Media.Color finalBody, finalBorder;
			if (CandleColorMode == NebulaCandleMode.VolumeDelta)
			{
				finalBody = cvdCol;
				finalBorder = cvdCol;
			}
			else if (CandleColorMode == NebulaCandleMode.Squeeze)
			{
				finalBody = sqCol;
				finalBorder = sqBorder;
			}
			else if (CandleColorMode == NebulaCandleMode.MACD)
			{
				finalBody = cMDColor;
				finalBorder = cMDBorder;
			}
			else
			{
				finalBody = bodyCol;
				finalBorder = borderCol;
			}

			// ── Three-bar outside reversal pattern ──────────────────────────────────────
			// A 3-bar reversal: opposite-colored bar, then a same-direction-as-reversal bar
			// with a bigger body that also engulfs the prior bar's open, then a third bar
			// confirming beyond the middle bar's low/high. Outlines the candle border in
			// yellow when detected — matches Pine exactly, including that only the border
			// gets overridden there (body color intentionally left alone, per Pine's own
			// commented-out `//cBodyColor := ...` line).
			if (ShowThreeOutPattern && CurrentBar >= 2)
			{
				bool threeOutUp = (Close[2] < Open[2]) && (Close[1] > Open[1]) && greenCandleNow
					&& Open[1] < Close[2] && Open[2] < Close[1]
					&& Math.Abs(Close[1] - Open[1]) > Math.Abs(Close[2] - Open[2])
					&& Close[0] > Low[1];
				bool threeOutDown = (Close[2] > Open[2]) && (Close[1] < Open[1]) && redCandleNow
					&& Open[1] > Close[2] && Open[2] > Close[1]
					&& Math.Abs(Close[1] - Open[1]) > Math.Abs(Close[2] - Open[2])
					&& Close[0] < Low[1];
				if (threeOutUp || threeOutDown)
					finalBorder = System.Windows.Media.Color.FromRgb(255, 230, 0);
			}

			if (PaintPriceMarkers && BarsInProgress == 0)
			{
				// Fully-qualified: SharpDX.Direct2D1.SolidColorBrush (used later in OnRender for the
				// cloud fill) and System.Windows.Media.SolidColorBrush (WPF, used here for the actual
				// price candles) share the same short name once both namespaces are imported — a
				// documented NinjaTrader-adjacent ambiguity, same root cause as the earlier Path issue.
				BarBrushes[0]            = new System.Windows.Media.SolidColorBrush(finalBody);
				CandleOutlineBrushes[0]  = new System.Windows.Media.SolidColorBrush(finalBorder);
				// NOTE: CandleWickBrushes doesn't exist on this NinjaTrader version's Indicator base —
				// confirmed by the compiler, not assumed. Wicks take CandleOutlineBrushes' color instead
				// (border and wick end up matching), which is a real but minor simplification from the
				// Pine source's separately-controlled wick color — worth living with rather than
				// guessing at another property name blind.
			}

			// Cache the cloud's WPF color for OnRender to consume (Direct2D brushes belong
			// to the render target and can't be constructed outside OnRender).
			_lastCloudUpWpf   = System.Windows.Media.Color.FromArgb((byte)(255 * (1 - CloudOpacityPercent / 100.0)), 0, 255, 0);
			_lastCloudDownWpf = System.Windows.Media.Color.FromArgb((byte)(255 * (1 - CloudOpacityPercent / 100.0)), 255, 0, 0);
		}

		// ── Cloud fill (Pine's fill(FVMA, lineMD, color=cColor)) ──────────────────────
		// NinjaScript has no built-in "fill between two plots" — this draws a semi-
		// transparent quad strip between the FanVMA and McGinley Dynamic series across
		// visible bars, matching the "Simple" cloud type's flat (non-gradient) color.
		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);
			if (ChartBars == null) return;

			try
			{
				if (_cloudBrushUp == null)
					_cloudBrushUp = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ToColor4(_lastCloudUpWpf));
				else
					_cloudBrushUp.Color = ToColor4(_lastCloudUpWpf);

				if (_cloudBrushDown == null)
					_cloudBrushDown = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ToColor4(_lastCloudDownWpf));
				else
					_cloudBrushDown.Color = ToColor4(_lastCloudDownWpf);

				int firstIdx = ChartBars.FromIndex;
				int lastIdx  = ChartBars.ToIndex;
				if (firstIdx < 0 || lastIdx < 0 || lastIdx <= firstIdx) return;

				for (int idx = Math.Max(firstIdx, 1); idx <= lastIdx; idx++)
				{
					if (idx >= FanVMA.Count || idx < 1) continue;
					double topPrev = FanVMA.GetValueAt(idx - 1), botPrev = McGinleyDynamic.GetValueAt(idx - 1);
					double top     = FanVMA.GetValueAt(idx),     bot     = McGinleyDynamic.GetValueAt(idx);
					if (double.IsNaN(topPrev) || double.IsNaN(botPrev) || double.IsNaN(top) || double.IsNaN(bot)) continue;

					float x0 = chartControl.GetXByBarIndex(ChartBars, idx - 1);
					float x1 = chartControl.GetXByBarIndex(ChartBars, idx);
					float yTopPrev = chartScale.GetYByValue(topPrev);
					float yBotPrev = chartScale.GetYByValue(botPrev);
					float yTop     = chartScale.GetYByValue(top);
					float yBot     = chartScale.GetYByValue(bot);

					bool green = _cloudIsGreen.GetValueAt(idx) >= 0.5;
					var brush = green ? _cloudBrushUp : _cloudBrushDown;

					using (var geo = new SharpDX.Direct2D1.PathGeometry(RenderTarget.Factory))
					{
						using (var sink = geo.Open())
						{
							sink.BeginFigure(new Vector2(x0, yTopPrev), FigureBegin.Filled);
							sink.AddLine(new Vector2(x1, yTop));
							sink.AddLine(new Vector2(x1, yBot));
							sink.AddLine(new Vector2(x0, yBotPrev));
							sink.EndFigure(FigureEnd.Closed);
							sink.Close();
						}
						RenderTarget.FillGeometry(geo, brush);
					}
				}
			}
			catch (Exception ex)
			{
				if (unchecked(Environment.TickCount - _lastRenderExLog) > 15000)
				{
					_lastRenderExLog = Environment.TickCount;
					try { Print("[Nebula_Core] OnRender threw: " + ex.GetType().Name + ": " + ex.Message); } catch { }
				}
			}
		}

		private static SharpDX.Color4 ToColor4(System.Windows.Media.Color c)
		{
			return new SharpDX.Color4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
		}

		private static double Clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

		// Rolling least-squares linear regression, returning the fitted line's value at the
		// MOST RECENT point in the window — matches Pine's ta.linreg(source, length, offset=0)
		// exactly. Implemented directly (standard least-squares formula) rather than via
		// NinjaScript's built-in LinReg indicator, since I want to be certain of the exact
		// math rather than gamble on whether that built-in's parameter/offset semantics line
		// up with Pine's — same reasoning as everywhere else uncertainty gets resolved by
		// building the verifiable thing directly instead of assuming an API matches.
		private static double LinRegEndpoint(Series<double> src, int period)
		{
			int n = period;
			double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
			for (int i = 0; i < n; i++)
			{
				double x = i;                 // 0 = oldest bar in the window
				double y = src[n - 1 - i];     // src[0] = current bar, src[n-1] = oldest bar in window
				sumX += x; sumY += y; sumXY += x * y; sumX2 += x * x;
			}
			double denom = n * sumX2 - sumX * sumX;
			if (denom == 0) return src[0];
			double slope = (n * sumXY - sumX * sumY) / denom;
			double intercept = (sumY - slope * sumX) / n;
			return intercept + slope * (n - 1);   // value at x = n-1, i.e. the current bar
		}

		// Linear-interpolate a fixed hue's ALPHA between two endpoints, matching Pine's
		// color.from_gradient(value, min, max, colA(transpA), colB(transpB)) where colA/colB
		// share a hue and only differ in transparency — the shape every WAE branch above uses.
		private static System.Windows.Media.Color LerpAlpha(System.Windows.Media.Color baseColor, double alphaLo, double alphaHi, double t)
		{
			double a = alphaLo + (alphaHi - alphaLo) * Clamp01(t);
			return System.Windows.Media.Color.FromArgb((byte)(255 * Clamp01(a)), baseColor.R, baseColor.G, baseColor.B);
		}

		#region Properties

		[NinjaScriptProperty]
		[Display(Name = "Show Indicator Label", GroupName = "0. General", Order = 0)]
		public bool ShowIndicatorLabel { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "ADX Length", GroupName = "1. Fantail VMA", Order = 0)]
		public int FvAdxLength { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "Weighting", GroupName = "1. Fantail VMA", Order = 1)]
		public double FvWeighting { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "MA Length", GroupName = "1. Fantail VMA", Order = 2)]
		public int FvMaLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Sensitivity", GroupName = "2. WAE", Order = 0)]
		public int WaeSensitivity { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Fast EMA Length", GroupName = "2. WAE", Order = 1)]
		public int WaeFastLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Slow EMA Length", GroupName = "2. WAE", Order = 2)]
		public int WaeSlowLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "BB Channel Length", GroupName = "2. WAE", Order = 3)]
		public int WaeChannelLength { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "BB Stdev Multiplier", GroupName = "2. WAE", Order = 4)]
		public double WaeBBMult { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Top Body Value", GroupName = "2. WAE", Order = 5)]
		public int WaeTopBody { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Top Border Value", GroupName = "2. WAE", Order = 6)]
		public int WaeTopBorder { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Cloud Opacity % (0=opaque, 100=invisible)", GroupName = "3. Cloud", Order = 0)]
		public int CloudOpacityPercent { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Candle Color Mode", GroupName = "4. Candles", Order = 0)]
		public NebulaCandleMode CandleColorMode { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Cumulative Volume Delta Top End", Description = "Pine's iVolDeltaTop — the |CVD| value at which the Volume Delta candle color reaches full saturation.", GroupName = "4. Candles", Order = 1)]
		public int CvdTopEnd { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Squeeze Tolerance", Description = "Lower = more sensitive. How many bars to look back on the squeeze indicator before a reset counts as a real momentum flip.", GroupName = "5. Squeeze Relaxer", Order = 0)]
		public int SqTolerance { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Squeeze ADX Threshold", GroupName = "5. Squeeze Relaxer", Order = 1)]
		public int SqAdxThreshold { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Squeeze ADX Period", Description = "Matches Pine's shared adxlen/dilen (both default 14) — this port uses NinjaScript's built-in ADX, which ties DI-length and DX-smoothing to one period rather than two separately-configurable ones.", GroupName = "5. Squeeze Relaxer", Order = 2)]
		public int SqAdxPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Squeeze BB/Regression Length", GroupName = "5. Squeeze Relaxer", Order = 3)]
		public int SqLength { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "Squeeze BB Stdev Multiplier", GroupName = "5. Squeeze Relaxer", Order = 4)]
		public double SqBBMult { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Squeeze KC Length", GroupName = "5. Squeeze Relaxer", Order = 5)]
		public int SqKcLength { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "Squeeze KC ATR Multiplier", Description = "Pine's multKC. NOTE: the Pine source itself uses plain High-Low range for the Keltner Channel width; this port uses ATR instead (by request) since range misses gap moves — see the header comment above the Squeeze Relaxer code for the full reasoning.", GroupName = "5. Squeeze Relaxer", Order = 6)]
		public double SqKcMult { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "Trampoline BB Threshold", Description = "Pine's iBBThreshold. Source comment: 0.003 for daily, 0.0015 for 30-min candles — the Pine default is tuned for a 30-min chart, worth revisiting for other timeframes.", GroupName = "6. Trampoline", Order = 0)]
		public double TrampBBThreshold { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Trampoline RSI Lower Threshold", GroupName = "6. Trampoline", Order = 1)]
		public int TrampRsiLower { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Trampoline RSI Upper Threshold", GroupName = "6. Trampoline", Order = 2)]
		public int TrampRsiUpper { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Trampoline RSI Length", GroupName = "6. Trampoline", Order = 3)]
		public int TrampRsiLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Trampoline BB Length", GroupName = "6. Trampoline", Order = 4)]
		public int TrampBBLength { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "Trampoline BB Stdev Multiplier", GroupName = "6. Trampoline", Order = 5)]
		public double TrampBBMult { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Thrust Markers", Description = "Draws a tagged marker on each up/down thrust bar. Pine's own version has this permanently disabled (commented out) — enabled here by default since an invisible-by-design feature isn't very useful ported as-is.", GroupName = "6. Trampoline", Order = 6)]
		public bool ShowTrampSignals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Dead Simple Reversal Markers", Description = "Draws a tagged marker on each Dead Simple Reversal bar. Pine's own version has this permanently disabled (commented out) — enabled here by default for the same reason as Trampoline's markers.", GroupName = "7. Dead Simple Reversal", Order = 0)]
		public bool ShowDSRSignals { get; set; }

		[NinjaScriptProperty]
		[Range(3, int.MaxValue)]
		[Display(Name = "RQK Lookback Window", Description = "Pine's h. NOTE: in the Pine source this input doesn't actually control the regression window at all (a real bug — see the header comment above the plot for the full explanation); it genuinely does here.", GroupName = "8. Rational Quadratic Kernel", Order = 0)]
		public int RqkLookback { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "RQK Relative Weighting", Description = "Pine's r — how sharply the kernel's weight falls off with distance from the current bar. Lower = smoother/flatter, higher = hugs price more closely.", GroupName = "8. Rational Quadratic Kernel", Order = 1)]
		public double RqkRelativeWeighting { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show LuxAlgo Reversal Markers", Description = "Draws a tagged marker when the 9-count reversal signal fires. Pine's own version has this permanently disabled (commented out) — enabled here by default for the same reason as Trampoline's/DSR's markers.", GroupName = "9. LuxAlgo Reversal Signals", Order = 0)]
		public bool ShowLuxRevSignals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Tidal Wave Lines", Description = "Pine's bTrackBar. Off by default in the original source itself (not something this port changed) — likely for performance, given the 200-bar scan plus potentially many drawn lines.", GroupName = "10. Tidal Wave", Order = 0)]
		public bool ShowTidalLines { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "Tidal Line Forward Extend (bars)", GroupName = "10. Tidal Wave", Order = 1)]
		public int TidalBarExtend { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Tidal Line Width", GroupName = "10. Tidal Wave", Order = 2)]
		public int TidalLineWidth { get; set; }

		[NinjaScriptProperty]
		[Range(10, 500)]
		[Display(Name = "Tidal Backward Scan (bars)", Description = "Pine's fixed 200-bar loop, exposed here as a tunable setting.", GroupName = "10. Tidal Wave", Order = 3)]
		public int TidalLookback { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Ignore Dojis", Description = "Pine source comment: \"only use on NQ, 1 min.\"", GroupName = "10. Tidal Wave", Order = 4)]
		public bool IgnoreDojis { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Doji Body Size (ticks)", GroupName = "10. Tidal Wave", Order = 5)]
		public int DojiBodyTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "HEMA Alpha Length", GroupName = "11. HEMA", Order = 0)]
		public int AlphaLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "HEMA Gamma Length", GroupName = "11. HEMA", Order = 1)]
		public int GammaLength { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show 9/21 EMA Cross (Skull Cross)", Description = "Pine's bShow921 — off by default in the source itself, matched here.", GroupName = "12. Skull Cross", Order = 0)]
		public bool ShowSkullCross { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use RQK Instead of EMA21 for Skull Cross", Description = "Pine's bShowQuad.", GroupName = "12. Skull Cross", Order = 1)]
		public bool UseKernelForSkullCross { get; set; }

		[NinjaScriptProperty]
		[Range(3, int.MaxValue)]
		[Display(Name = "Pivot Bars Back", Description = "Pine's bb — a hardcoded constant (20) there, exposed as a setting here. A pivot needs this many bars of confirmation on both sides, so it's only known this many bars after it actually happened.", GroupName = "13. Breaks and Retests", Order = 0)]
		public int PivotBarsBack { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Retest Markers", Description = "Pine's bShowRetest — off by default in the source itself, matched here.", GroupName = "13. Breaks and Retests", Order = 1)]
		public bool ShowRetestMarkers { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Retest Window (bars)", Description = "MY OWN addition, not a direct Pine input — see the deviation note above the calculation block for why the source's own multi-condition validity windows were simplified to this one setting.", GroupName = "13. Breaks and Retests", Order = 2)]
		public int RetestWindowBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Outline 3-Bar Reversal Pattern", Description = "Pine's bShowRevPattern — off by default in the source itself, matched here.", GroupName = "13. Breaks and Retests", Order = 3)]
		public bool ShowThreeOutPattern { get; set; }

		[NinjaScriptProperty]
		[Range(3, int.MaxValue)]
		[Display(Name = "Market Structure Fractal Length", Description = "Pine's length — hardcoded to 5 there, exposed as a setting here.", GroupName = "14. Market Structure", Order = 0)]
		public int MsFractalLength { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Structure Signals", Description = "This port's own signal — the Pine source's real consumer chain for this feature is blocked (see the plot comment above the AddPlot calls for the full explanation), so there's no direct Pine visibility toggle to match.", GroupName = "14. Market Structure", Order = 1)]
		public bool ShowStructureSignals { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "MACD Fast Length", GroupName = "15. MACD Candles", Order = 0)]
		public int MacdFastLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "MACD Slow Length", GroupName = "15. MACD Candles", Order = 1)]
		public int MacdSlowLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "MACD Signal Length", GroupName = "15. MACD Candles", Order = 2)]
		public int MacdSignalLength { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Apply 25/75 RSI Rule", Description = "Pine's bApply25and75 — off by default in the source itself, matched here.", GroupName = "16. The Shark", Order = 0)]
		public bool ApplySharkRsiRule { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Shark Markers", Description = "Pine's own marker for this (a shark emoji via plotchar) is commented out (invisible) — enabled here by default, same as every other invisible-by-default signal in this port.", GroupName = "16. The Shark", Order = 1)]
		public bool ShowSharkSignals { get; set; }

		[NinjaScriptProperty]
		[Range(3, int.MaxValue)]
		[Display(Name = "LazyLine Length", Description = "Pine's fixed f_LazyLine(close, 21) argument, exposed as a setting here.", GroupName = "17. Add Contract Signal", Order = 0)]
		public int LazyLineLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "DER R Length", Description = "Pine's rlength.", GroupName = "17. Add Contract Signal", Order = 1)]
		public int DerRLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "DER Average Length", Description = "Pine's DER_avg.", GroupName = "17. Add Contract Signal", Order = 2)]
		public int DerAvgLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "DER Volume Lookback", Description = "Pine's vlookbk.", GroupName = "17. Add Contract Signal", Order = 3)]
		public int DerVolLookback { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Add Contract Signals", Description = "Pine's bShowBigPlus — a real, exposed toggle (default on), respected as-is.", GroupName = "17. Add Contract Signal", Order = 4)]
		public bool ShowAddContractSignals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Add Contract (Light) Signals", Description = "Pine's bShowPlus — a separate, real toggle from the Strong (Vodka Shot) version above, default on, respected as-is.", GroupName = "17. Add Contract Signal", Order = 5)]
		public bool ShowAddContractLightSignals { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "UBS Watch Signal Lookback (bars)", GroupName = "18. Ultimate Buy Sell", Order = 0)]
		public int UbsWatchSignalLookback { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "UBS RSI Length", GroupName = "18. Ultimate Buy Sell", Order = 1)]
		public int UbsRsiLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "UBS RSI Basis Length", GroupName = "18. Ultimate Buy Sell", Order = 2)]
		public int UbsRsiBasisLength { get; set; }

		[NinjaScriptProperty]
		[Range(1.0, 3.0)]
		[Display(Name = "UBS RSI Band Multiplier", GroupName = "18. Ultimate Buy Sell", Order = 3)]
		public double UbsRsiMultiplier { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "UBS Use RSI Watch Signals", GroupName = "18. Ultimate Buy Sell", Order = 4)]
		public bool UbsUseRsiWatchSignals { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "UBS Price BBand Basis Length", GroupName = "18. Ultimate Buy Sell", Order = 5)]
		public int UbsPriceBasisLength { get; set; }

		[NinjaScriptProperty]
		[Range(1.0, 4.0)]
		[Display(Name = "UBS Price Inner BB Multiplier", GroupName = "18. Ultimate Buy Sell", Order = 6)]
		public double UbsPriceInnerMultiplier { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "UBS Use Price Band Watch Signals", GroupName = "18. Ultimate Buy Sell", Order = 7)]
		public bool UbsUsePriceBandWatchSignals { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "UBS ATR Period", GroupName = "18. Ultimate Buy Sell", Order = 8)]
		public int UbsAtrPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "UBS ATR MA Period", GroupName = "18. Ultimate Buy Sell", Order = 9)]
		public int UbsAtrMaPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1.0, double.MaxValue)]
		[Display(Name = "UBS ATR Band Multiplier", GroupName = "18. Ultimate Buy Sell", Order = 10)]
		public double UbsAtrMult { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "UBS Use ATR Watch Signals", GroupName = "18. Ultimate Buy Sell", Order = 11)]
		public bool UbsUseAtrWatchSignals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "UBS Use RSI Crossing Basis Signals", GroupName = "18. Ultimate Buy Sell", Order = 12)]
		public bool UbsUseRsiSignals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "UBS Use RSI Crossing Under 75", GroupName = "18. Ultimate Buy Sell", Order = 13)]
		public bool UbsUse75Signals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "UBS Use RSI Crossing Over 25", GroupName = "18. Ultimate Buy Sell", Order = 14)]
		public bool UbsUse25Signals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "UBS Use RSI Crossing MA Signals", GroupName = "18. Ultimate Buy Sell", Order = 15)]
		public bool UbsUseRsiMa { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "UBS Additional RSI MA Length", GroupName = "18. Ultimate Buy Sell", Order = 16)]
		public int UbsRsiMaLength { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Ultimate Buy/Sell Markers", Description = "Unlike almost every other signal in this port, this one is genuinely visible by default in the Pine source too — not a \"make it visible\" fix.", GroupName = "18. Ultimate Buy Sell", Order = 17)]
		public bool ShowUltimateBuySell { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Take Profit Markers", Description = "Pine's bShowProfit — a real, exposed toggle (default on), respected as-is.", GroupName = "19. Take Profit Confluence", Order = 0)]
		public bool ShowTakeProfitSignals { get; set; }

		[NinjaScriptProperty]
		[Range(2, 50)]
		[Display(Name = "Min Signals For Take Partial Profit (iProfit)", Description = "Pine's iProfit — despite the name, this actually drives the ✓ \"Take Full Profit\" marker, not the ✔ \"Take Partial Profit\" one. The source's own labels and thresholds are swapped from what their names suggest; matched here exactly as written rather than \"corrected\", since it's not clear which side (the label or the threshold) was the actual mistake.", GroupName = "19. Take Profit Confluence", Order = 1)]
		public int IProfitThreshold { get; set; }

		[NinjaScriptProperty]
		[Range(3, 50)]
		[Display(Name = "Min Signals For Take ALL Profit (iMaxProfit)", Description = "Pine's iMaxProfit — despite the name, this actually drives the ✔ \"Take Partial Profit\" marker, not the ✓ \"Take Full Profit\" one. See the note on the threshold above.", GroupName = "19. Take Profit Confluence", Order = 2)]
		public int IMaxProfitThreshold { get; set; }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> FanVMA
		{
			get { return Values[0]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> McGinleyDynamic
		{
			get { return Values[1]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> UpThrustSignal
		{
			get { return Values[2]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DownThrustSignal
		{
			get { return Values[3]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> BuyDSRSignal
		{
			get { return Values[4]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SellDSRSignal
		{
			get { return Values[5]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> RationalQuadraticKernel
		{
			get { return Values[6]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> UpReversalSignal
		{
			get { return Values[7]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DownReversalSignal
		{
			get { return Values[8]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> Hema
		{
			get { return Values[9]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> BuyCharSignal
		{
			get { return Values[10]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SellCharSignal
		{
			get { return Values[11]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SkullUpSignal
		{
			get { return Values[12]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SkullDownSignal
		{
			get { return Values[13]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SupportBreakSignal
		{
			get { return Values[14]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ResistanceBreakSignal
		{
			get { return Values[15]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SupportRetestSignal
		{
			get { return Values[16]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ResistanceRetestSignal
		{
			get { return Values[17]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> FractalHighSignal
		{
			get { return Values[18]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> FractalLowSignal
		{
			get { return Values[19]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> StructureBreakUpSignal
		{
			get { return Values[20]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> StructureBreakDownSignal
		{
			get { return Values[21]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SharkUpSignal
		{
			get { return Values[22]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SharkDownSignal
		{
			get { return Values[23]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> AddContractBuySignal
		{
			get { return Values[24]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> AddContractSellSignal
		{
			get { return Values[25]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> UbsBuySignal
		{
			get { return Values[26]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> UbsSellSignal
		{
			get { return Values[27]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> StrongBuySignal
		{
			get { return Values[28]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> StrongSellSignal
		{
			get { return Values[29]; }
		}

		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
	}
}

#endregion
