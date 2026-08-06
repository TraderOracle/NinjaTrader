// ============================================================================
//  RedTailAutoFRVP  —  part of the RedTail Indicator Suite
//  Copyright (c) 2026 Jason ("RedTail").  All rights reserved.
//
//  Author / contact:
//      X (Twitter):  @_hawkeye_13
//      Discord:      jason_5427
//
//  LICENSE — Source-available, NON-COMMERCIAL use only.
//
//  Permission is granted, free of charge, to any individual to use, copy, and
//  modify this software for personal, non-commercial trading and educational
//  purposes, subject to ALL of the following conditions:
//
//    1. This copyright and license notice must be retained, in full and
//       unmodified, in all copies or substantial portions of the software,
//       including any modified or derivative versions.
//
//    2. You may NOT sell, license, sublicense, rent, lease, or otherwise
//       commercialize this software or any derivative of it — whether on its
//       own, bundled with other products or services, or as part of a paid
//       indicator package, subscription, signal service, course, or prop/funded
//       offering — without the prior written permission of the copyright holder.
//
//    3. You may NOT redistribute this software (modified or unmodified) while
//       removing or obscuring its authorship, RedTail branding, or this notice,
//       or in any manner that misrepresents its origin.
//
//  This software is provided "AS IS", without warranty of any kind, express or
//  implied. The author is not liable for any claim, damages, or losses arising
//  from its use. Trading futures involves substantial risk of loss.
//
//  For commercial licensing inquiries, contact the author at the handles above.
// ============================================================================

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// RedTailAutoFRVP
// -----------------------------------------------------------------------------
// Auto supply/demand indicator. Detects consolidation/balance zones with an
// expanding ATR-relative "box", then draws a Fixed-Range Volume Profile band
// over each zone exposing POC / VAH / VAL (instead of a full rectangle).
//
// Per-zone deletion: hold the delete modifier (default Ctrl) and left-click
// inside a zone you disagree with. That single zone is hidden and stays hidden
// across reloads (the hidden set is keyed by the zone's start-bar time and is
// serialized with the indicator). ClearAllHiddenZones un-hides everything.
// -----------------------------------------------------------------------------

namespace NinjaTrader.NinjaScript.Indicators.RedTail
{
	// Enums must live at namespace scope (NOT nested in the class) or NinjaTrader's
	// auto-generated property wrappers can't resolve them -> CS0246.
	public enum RedTailHeightMode { AtrMultiple, FixedTicks }
	public enum RedTailDeleteModifier { Control, Alt, Shift }
	public enum RedTailStrengthMode { Departure, Time, Either, Both }
	public enum RedTailWeakDisplay { Show, Dim, Hide }
	public enum RedTailZoneState { Fresh, Tested, Mitigated }
	public enum RedTailButtonCorner { TopLeft, TopRight, BottomLeft, BottomRight }

	public class RedTailAutoFRVP : Indicator
	{
		#region Types
		private class Zone
		{
			public long      Key;        // stable id = start bar time ticks
			public DateTime  StartTime;
			public DateTime  EndTime;
			public int       StartBarIdx;
			public int       EndBarIdx;
			public double    POC;
			public double    VAH;
			public double    VAL;

			// strength classification
			public int       Bars;            // time in base
			public int       Dir;             // breakout direction (+1 up, -1 down)
			public double    RefEdge;         // consolidation edge price the move departs from
			public double    Height;          // value-area height (departure normalizer)
			public double    MaxExcursion;    // furthest departure away from RefEdge
			public int       DepartBarsLeft;  // bars remaining in the departure window
			public double    Departure;       // MaxExcursion / Height
			public bool      Strong;
			public double    Delta;           // net buy/sell delta accumulated in the base

			// mitigation lifecycle
			public RedTailZoneState State;    // Fresh -> Tested -> Mitigated
			public bool      HasLeft;         // price has cleared the band since forming
			public int       Touches;         // distinct return-touches since forming
			public bool      Inside;          // price currently inside the band (for touch counting)
			public int       Flips;           // role reversals (supply<->demand breakthroughs)
			public int       LastTouchBar;    // CurrentBar of the most recent band touch (for abandoned cleanup)

			public double    Volume;          // total profile volume in the base

			public double    SrcHigh;         // high/low of the source bars (for the source-bar highlight)
			public double    SrcLow;
		}

		private class DeleteHandle
		{
			public float L, T, W, H;
			public long  Key;
		}

		private class HoverBox
		{
			public float L, T, R, B, PocY;
			public long  Key;
		}
		#endregion

		#region Fields
		private readonly List<Zone> zones = new List<Zone>();
		private readonly HashSet<long> hiddenKeys = new HashSet<long>();
		private readonly HashSet<long> pinnedKeys = new HashSet<long>();

		// forming box state
		private bool     boxActive;
		private int      boxStartIdx;
		private int      boxEndIdx;
		private int      boxBars;
		private double   boxHigh;
		private double   boxLow;
		private double   boxCloseHi;
		private double   boxCloseLo;
		private DateTime boxStartTime;

		private ATR atr;

		// last two painted bar times (for projecting the right edge N bars forward)
		private DateTime lastBarTime = DateTime.MinValue;
		private DateTime prevBarTime = DateTime.MinValue;
		private double   lastPrice;
		private DateTime lastPriceRefresh = DateTime.MinValue;

		// delta (bid/ask) accumulation — needs Tick Replay for historical bars
		private readonly Dictionary<int, double> barDelta = new Dictionary<int, double>();
		private double cumBarDelta;
		private int    accumBar = -1;
		private double currentBid;
		private double currentAsk;
		private double lastTradePrice;
		private int    lastTickSign;

		// screen rects of the per-zone delete buttons, rebuilt every render
		private readonly List<DeleteHandle> deleteHandles = new List<DeleteHandle>();
		private readonly List<DeleteHandle> pinHandles    = new List<DeleteHandle>();
		private System.Windows.Point        mousePos;

		// on-chart "show all zones <-> near-price only" toggle button
		private bool                 showAllZones;
		private SharpDX.RectangleF   toggleRect;
		private bool                 toggleRectValid;

		// band hover regions + current hover state (for hover-to-reveal delete button)
		private readonly List<HoverBox> hoverBoxes = new List<HoverBox>();
		private bool hasHover;
		private long hoveredKey;

		// cached for the mouse handler (set each OnRender)
		private ChartControl cachedControl;
		private ChartScale   cachedScale;
		private bool         handlersAttached;
		private DateTime     lastHoverRefresh = DateTime.MinValue;
		#endregion

		#region OnStateChange
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name                        = "RedTailAutoFRVP";
				Description                 = "Auto supply/demand zones drawn as Fixed-Range Volume Profiles (POC/VAH/VAL) over detected consolidations.";
				Calculate                   = Calculate.OnBarClose;
				IsOverlay                   = true;
				DisplayInDataBox            = false;
				DrawOnPricePanel            = true;
				IsSuspendedWhileInactive    = true;
				PaintPriceMarkers           = false;
				MaximumBarsLookBack         = MaximumBarsLookBack.Infinite;

				// ---- Detection ----
				MinConsolidationBars        = 8;
				MaxConsolidationBars        = 60;     // 0 = unlimited
				HeightMode                  = RedTailHeightMode.AtrMultiple;
				AtrPeriod                   = 14;
				MaxZoneHeightAtr            = 1.5;
				MaxZoneHeightTicks          = 40;
				BreakoutBufferTicks         = 4;
				UseCloseBand                = true;
				ProfileRows                 = 50;
				ValueAreaPct                = 70;

				// ---- Strength ----
				StrengthMode                = RedTailStrengthMode.Departure;
				DepartureBars               = 10;
				MinDepartureRatio           = 1.5;
				StrongMinBars               = 15;
				WeakZoneDisplay             = RedTailWeakDisplay.Dim;
				WeakOpacityPct              = 40;

				// ---- Supply / Demand coloring ----
				ColorBySupplyDemand         = true;
				DemandColor                 = Brushes.LimeGreen;
				SupplyColor                 = Brushes.Crimson;
				ShowSourceBars              = false;
				SourceBarsColor             = Brushes.SteelBlue;
				SourceBarsOpacityPct        = 22;
				ColorLinesBySupplyDemand    = false;

				// ---- Mitigation ----
				EnableMitigation            = true;
				FlipOnBreakthrough          = true;

				RetireOnFlips               = true;
				MaxFlips                    = 3;
				RetireOnTouches             = true;
				MaxTouches                  = 5;
				RetireWhenAbandoned         = true;
				AbandonAtr                  = 6.0;
				AbandonBars                 = 300;
				MitigatedDisplay            = RedTailWeakDisplay.Dim;
				MitigatedOpacityPct         = 25;
				TestedOpacityPct            = 65;

				// ---- Delta ----
				EnableDelta                 = false;
				ShowDelta                   = true;
				RequireDeltaConfirm         = false;
				MinDeltaConfirm             = 0;
				MaxZonesToShow              = 8;      // 0 = all
				MaxStoredZones              = 150;    // 0 = unlimited; prunes oldest non-pinned
				PrioritizeNearPrice         = true;
				PriceRefreshMs              = 100;
				ReplaceOverlapping          = true;
				OverlapThreshold            = 0;

				// ---- Display ----
				ShowValueAreaFill           = true;
				ValueAreaColor              = Brushes.Gray;
				ValueAreaOpacity            = 25;
				ExtendRight                 = true;
				ExtendBars                  = 5;
				ShowDeleteHandles           = true;
				HoverToShowDelete           = true;

				ShowPOC                     = true;
				POCColor                    = Brushes.Red;
				POCWidth                    = 2;
				POCStyle                    = DashStyleHelper.Dot;

				ShowVAHVAL                  = true;
				VAColor                     = Brushes.DimGray;
				VAWidth                     = 1;
				VAStyle                     = DashStyleHelper.Dash;

				ShowLabels                  = false;
				ShowPrices                  = false;

				// ---- Interaction ----
				DeleteModifier              = RedTailDeleteModifier.Control;
				ShowTooltip                 = true;
				ShowAllZonesButton          = true;
				ButtonCorner                = RedTailButtonCorner.TopLeft;
				ButtonOffsetX               = 8;
				ButtonOffsetY               = 8;
				ClearAllHiddenZones         = false;

				// Data-only plots for automation (transparent so they don't draw over the zones).
				// Nearest zone below price = support; nearest above = resistance.
				AddPlot(Brushes.Transparent, "Support VAH");
				AddPlot(Brushes.Transparent, "Support POC");
				AddPlot(Brushes.Transparent, "Support VAL");
				AddPlot(Brushes.Transparent, "Resistance VAH");
				AddPlot(Brushes.Transparent, "Resistance POC");
				AddPlot(Brushes.Transparent, "Resistance VAL");
				// status signals: Strong = 1/0, State = 0 fresh / 1 tested (NaN = no zone on that side)
				AddPlot(Brushes.Transparent, "Support Strong");
				AddPlot(Brushes.Transparent, "Support State");
				AddPlot(Brushes.Transparent, "Resistance Strong");
				AddPlot(Brushes.Transparent, "Resistance State");
				// net base delta of the nearest support/resistance zone (NaN when delta disabled or no zone)
				AddPlot(Brushes.Transparent, "Support Delta");
				AddPlot(Brushes.Transparent, "Resistance Delta");
			}
			else if (State == State.Configure)
			{
				zones.Clear();
				boxActive = false;
			}
			else if (State == State.DataLoaded)
			{
				atr = ATR(AtrPeriod);
				zones.Clear();
				boxActive = false;
				barDelta.Clear();
				cumBarDelta = 0;
				accumBar    = -1;
				currentBid  = 0;
				currentAsk  = 0;
				lastTradePrice = 0;
				lastTickSign   = 0;
			}
			else if (State == State.Historical)
			{
				AttachHandlers();
			}
			else if (State == State.Terminated)
			{
				DetachHandlers();
			}
		}
		#endregion

		#region Detection (OnBarUpdate)
		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) return;

			prevBarTime = lastBarTime;
			lastBarTime = Time[0];
			lastPrice   = Close[0];

			if (State == State.Realtime)
				ForceRefresh();   // re-rank the near-price zones as soon as a bar closes

			if (CurrentBar < 2 || CurrentBar < AtrPeriod) return;

			if (ClearAllHiddenZones && hiddenKeys.Count > 0)
			{
				hiddenKeys.Clear();
				ForceRefresh();
			}

			double th     = CurrentThreshold();
			double buffer  = BreakoutBufferTicks * TickSize;

			UpdatePendingDepartures();   // measure how far recent zones have departed
			UpdateMitigation();          // track fresh -> tested -> mitigated as price returns
			RetireZones();               // hard-remove flip-churned / eroded / abandoned zones
			UpdateLevelPlots();          // expose nearest support/resistance levels as plots

			if (!boxActive)
			{
				StartBox();
				return;
			}

			double nH      = Math.Max(boxHigh, High[0]);
			double nL      = Math.Min(boxLow,  Low[0]);
			double cHi     = Math.Max(boxCloseHi, Close[0]);
			double cLo     = Math.Min(boxCloseLo, Close[0]);
			double sizeHi  = UseCloseBand ? cHi : nH;   // wick-tolerant height test
			double sizeLo  = UseCloseBand ? cLo : nL;
			bool   tooTall = (sizeHi - sizeLo) > th;
			bool   brokeUp = Close[0] > boxHigh + buffer;   // breakout off the full range (forgiving)
			bool   brokeDn = Close[0] < boxLow  - buffer;
			bool   tooLong = MaxConsolidationBars > 0 && boxBars >= MaxConsolidationBars;

			if (tooTall || brokeUp || brokeDn || tooLong)
			{
				// box ended on the PREVIOUS bar; this bar is the breakout/restart bar
				int dir = brokeUp ? 1 : brokeDn ? -1 : (Close[0] >= (boxHigh + boxLow) * 0.5 ? 1 : -1);
				if (boxBars >= MinConsolidationBars)
					FinalizeZone(boxStartIdx, CurrentBar - 1, dir);

				StartBox();
			}
			else
			{
				boxHigh    = nH;
				boxLow     = nL;
				boxCloseHi = cHi;
				boxCloseLo = cLo;
				boxEndIdx  = CurrentBar;
				boxBars++;
			}
		}

		// Tracks bid/ask, accumulates per-bar buy/sell delta, and drives the live near-price ranking.
		protected override void OnMarketData(MarketDataEventArgs e)
		{
			if (e.MarketDataType == MarketDataType.Ask) { currentAsk = e.Price; return; }
			if (e.MarketDataType == MarketDataType.Bid) { currentBid = e.Price; return; }
			if (e.MarketDataType != MarketDataType.Last) return;

			// --- per-bar delta (classify each trade as buy or sell) ---
			if (EnableDelta)
			{
				if (CurrentBar != accumBar)
				{
					if (accumBar >= 0) barDelta[accumBar] = cumBarDelta;   // finalize prior bar
					accumBar    = CurrentBar;
					cumBarDelta = 0;
				}

				// prefer the bid/ask carried on the trade event (these replay under Tick Replay),
				// fall back to separately-tracked quotes, then to a tick-direction rule
				double ask = e.Ask > 0 ? e.Ask : currentAsk;
				double bid = e.Bid > 0 ? e.Bid : currentBid;

				int sign = 0;
				if (ask > 0 && bid > 0)
				{
					if (e.Price >= ask)      sign = 1;
					else if (e.Price <= bid) sign = -1;
				}
				if (sign == 0)   // between spread, or no quote available -> tick rule
				{
					if (e.Price > lastTradePrice)      sign = 1;
					else if (e.Price < lastTradePrice) sign = -1;
					else                               sign = lastTickSign;
				}

				cumBarDelta += sign * e.Volume;
				if (sign != 0) lastTickSign = sign;
				lastTradePrice = e.Price;
			}

			// --- live near-price ranking ---
			if (PrioritizeNearPrice && e.Price != lastPrice)
			{
				lastPrice = e.Price;
				DateTime now = DateTime.UtcNow;
				if (PriceRefreshMs <= 0 || (now - lastPriceRefresh).TotalMilliseconds >= PriceRefreshMs)
				{
					lastPriceRefresh = now;
					ForceRefresh();
				}
			}
		}

		private void StartBox()
		{
			boxActive    = true;
			boxStartIdx  = CurrentBar;
			boxEndIdx    = CurrentBar;
			boxBars      = 1;
			boxHigh      = High[0];
			boxLow       = Low[0];
			boxCloseHi   = Close[0];
			boxCloseLo   = Close[0];
			boxStartTime = Time[0];
		}

		private double CurrentThreshold()
		{
			if (HeightMode == RedTailHeightMode.FixedTicks)
				return MaxZoneHeightTicks * TickSize;

			double a = (atr != null && CurrentBar >= AtrPeriod) ? atr[0] : 0;
			if (a <= 0) a = 10 * TickSize; // sane fallback before ATR warms up
			return a * MaxZoneHeightAtr;
		}

		private void FinalizeZone(int startIdx, int endIdx, int dir)
		{
			if (endIdx < startIdx) return;

			double poc, vah, val, totalVol, srcHi, srcLo;
			if (!ComputeZoneProfile(startIdx, endIdx, out poc, out vah, out val, out totalVol, out srcHi, out srcLo))
				return;

			long key = boxStartTime.Ticks;

			// avoid duplicates on real-time re-finalize
			for (int i = 0; i < zones.Count; i++)
				if (zones[i].Key == key) return;

			// A new zone at the same price level supersedes older overlapping zones.
			if (ReplaceOverlapping)
			{
				double newH = vah - val;
				for (int i = zones.Count - 1; i >= 0; i--)
				{
					Zone old = zones[i];
					double ov = Math.Min(old.VAH, vah) - Math.Max(old.VAL, val);
					if (ov <= 0) continue;
					double minH = Math.Min(newH, old.VAH - old.VAL);
					if (minH <= 0) continue;
					if (ov / minH >= OverlapThreshold)
						zones.RemoveAt(i);
				}
			}

			double refEdge = dir > 0 ? boxHigh : boxLow;
			double height  = Math.Max(TickSize, vah - val);
			double exc0    = dir > 0 ? (High[0] - refEdge) : (refEdge - Low[0]);
			if (exc0 < 0) exc0 = 0;

			Zone z = new Zone
			{
				Key            = key,
				StartTime      = boxStartTime,
				EndTime        = Time[CurrentBar - endIdx],
				StartBarIdx    = startIdx,
				EndBarIdx      = endIdx,
				POC            = poc,
				VAH            = vah,
				VAL            = val,
				Bars           = boxBars,
				Dir            = dir,
				RefEdge        = refEdge,
				Height         = height,
				MaxExcursion   = exc0,
				DepartBarsLeft = Math.Max(1, DepartureBars),
				Departure      = exc0 / height,
				Delta          = EnableDelta ? SumZoneDelta(startIdx, endIdx) : 0,
				Volume         = totalVol,
				SrcHigh        = srcHi,
				SrcLow         = srcLo,
				LastTouchBar   = endIdx
			};
			ClassifyZone(z);
			zones.Add(z);
			PruneZones();
		}

		// Bounds memory on long sessions: drop the oldest non-pinned zones beyond the cap,
		// and trim the per-bar delta map to a recent window (older bars are never re-summed).
		private void PruneZones()
		{
			if (MaxStoredZones > 0)
			{
				int i = 0;
				while (zones.Count > MaxStoredZones && i < zones.Count)
				{
					if (pinnedKeys.Contains(zones[i].Key)) { i++; continue; }
					zones.RemoveAt(i);
				}
			}

			if (EnableDelta && barDelta.Count > 6000)
			{
				int cutoff = CurrentBar - 5000;
				List<int> stale = new List<int>();
				foreach (int k in barDelta.Keys)
					if (k < cutoff) stale.Add(k);
				for (int j = 0; j < stale.Count; j++)
					barDelta.Remove(stale[j]);
			}
		}

		// Track how far price departs from each recent zone over its departure window.
		private void UpdatePendingDepartures()
		{
			for (int i = 0; i < zones.Count; i++)
			{
				Zone z = zones[i];
				if (z.DepartBarsLeft <= 0) continue;

				double exc = z.Dir > 0 ? (High[0] - z.RefEdge) : (z.RefEdge - Low[0]);
				if (exc > z.MaxExcursion) z.MaxExcursion = exc;

				z.DepartBarsLeft--;
				z.Departure = z.Height > 0 ? z.MaxExcursion / z.Height : 0;
				ClassifyZone(z);
			}
		}

		private void ClassifyZone(Zone z)
		{
			bool dep  = z.Departure >= MinDepartureRatio;
			bool time = z.Bars >= StrongMinBars;
			bool strong;
			switch (StrengthMode)
			{
				case RedTailStrengthMode.Time:   strong = time;         break;
				case RedTailStrengthMode.Either: strong = dep || time;  break;
				case RedTailStrengthMode.Both:   strong = dep && time;  break;
				default:                         strong = dep;          break; // Departure
			}

			// optionally require delta to confirm the zone direction (buying in demand, selling in supply)
			if (RequireDeltaConfirm)
			{
				bool conf = z.Dir > 0 ? z.Delta >= MinDeltaConfirm : z.Delta <= -MinDeltaConfirm;
				strong = strong && conf;
			}

			z.Strong = strong;
		}

		private double SumZoneDelta(int startIdx, int endIdx)
		{
			double sum = 0;
			for (int idx = startIdx; idx <= endIdx; idx++)
			{
				double d;
				if (barDelta.TryGetValue(idx, out d)) sum += d;
			}
			return sum;
		}

		// Publishes the nearest support (zone below price) and resistance (zone above price)
		// VAH/POC/VAL into the plots so strategies can reference them. NaN = no zone on that side.
		private void UpdateLevelPlots()
		{
			double price = Close[0];
			Zone sup = null, res = null;
			double supDist = double.MaxValue, resDist = double.MaxValue;

			for (int i = 0; i < zones.Count; i++)
			{
				Zone z = zones[i];
				if (hiddenKeys.Contains(z.Key)) continue;
				if (EnableMitigation && z.State == RedTailZoneState.Mitigated) continue;

				if (z.POC <= price)
				{
					double d = price - z.POC;
					if (d < supDist) { supDist = d; sup = z; }
				}
				else
				{
					double d = z.POC - price;
					if (d < resDist) { resDist = d; res = z; }
				}
			}

			if (sup != null)
			{
				SupportVAH[0] = sup.VAH; SupportPOC[0] = sup.POC; SupportVAL[0] = sup.VAL;
				SupportStrong[0] = sup.Strong ? 1 : 0;
				SupportState[0]  = (int)sup.State;
				SupportDelta[0]  = EnableDelta ? sup.Delta : double.NaN;
			}
			else
			{
				SupportVAH[0] = double.NaN; SupportPOC[0] = double.NaN; SupportVAL[0] = double.NaN;
				SupportStrong[0] = double.NaN; SupportState[0] = double.NaN; SupportDelta[0] = double.NaN;
			}

			if (res != null)
			{
				ResistanceVAH[0] = res.VAH; ResistancePOC[0] = res.POC; ResistanceVAL[0] = res.VAL;
				ResistanceStrong[0] = res.Strong ? 1 : 0;
				ResistanceState[0]  = (int)res.State;
				ResistanceDelta[0]  = EnableDelta ? res.Delta : double.NaN;
			}
			else
			{
				ResistanceVAH[0] = double.NaN; ResistancePOC[0] = double.NaN; ResistanceVAL[0] = double.NaN;
				ResistanceStrong[0] = double.NaN; ResistanceState[0] = double.NaN; ResistanceDelta[0] = double.NaN;
			}
		}

		// Fresh -> Tested (price returned and touched the band) -> Mitigated (closed through the POC).
		private void UpdateMitigation()
		{
			if (!EnableMitigation) return;

			double buffer = BreakoutBufferTicks * TickSize;

			for (int i = 0; i < zones.Count; i++)
			{
				Zone z = zones[i];

				// flip / polarity reversal: a close fully through the far edge reverses the role
				// (broken supply -> demand, broken demand -> supply). Takes precedence over,
				// and can revive, a mitigated zone.
				if (FlipOnBreakthrough && z.HasLeft)
				{
					bool brokeThrough = z.Dir > 0 ? Close[0] < z.VAL - buffer : Close[0] > z.VAH + buffer;
					if (brokeThrough)
					{
						z.Dir     = -z.Dir;
						// a flipped zone has demonstrably been traded through, so it is not "fresh";
						// it re-arms as Tested on the new side (still live, but interacted with).
						z.State   = RedTailZoneState.Tested;
						z.HasLeft = false;                    // re-arm the "price cleared the band" check
						z.Inside  = false;
						z.Flips++;
						continue;
					}
				}

				if (z.State == RedTailZoneState.Mitigated) continue;

				// wait until price has actually cleared the band before counting any return
				if (!z.HasLeft)
				{
					bool away = z.Dir > 0 ? Low[0] > z.VAH : High[0] < z.VAL;
					if (away) z.HasLeft = true;
					continue;
				}

				bool touched = High[0] >= z.VAL && Low[0] <= z.VAH;
				if (touched && !z.Inside) z.Touches++;   // count each fresh re-entry
				if (touched) z.LastTouchBar = CurrentBar;
				z.Inside = touched;

				if (z.State == RedTailZoneState.Fresh && touched)
					z.State = RedTailZoneState.Tested;

				bool throughPoc = z.Dir > 0 ? Close[0] < z.POC : Close[0] > z.POC;
				if (throughPoc)
					z.State = RedTailZoneState.Mitigated;
			}
		}

		// Hard-removes zones that are no longer meaningful levels. Three independent rules,
		// each toggleable; pinned zones are never auto-removed. Soft fading (mitigated/weak)
		// is handled separately at render time — this is true deletion from memory.
		private bool RetireQualifies(Zone z, double atr0)
		{
			if (RetireOnFlips   && MaxFlips   > 0 && z.Flips   >= MaxFlips)   return true;  // flip-churn pivot
			if (RetireOnTouches && MaxTouches > 0 && z.Touches >= MaxTouches) return true;  // eroded by retests

			// consumed and abandoned: mitigated, price well away, and no touch in a while
			if (RetireWhenAbandoned && z.State == RedTailZoneState.Mitigated && atr0 > 0)
			{
				bool far  = Math.Abs(Close[0] - z.POC) > AbandonAtr * atr0;
				bool gone = (CurrentBar - z.LastTouchBar) > AbandonBars;
				if (far && gone) return true;
			}
			return false;
		}

		private void RetireZones()
		{
			double atr0 = (atr != null && CurrentBar >= 0) ? atr[0] : 0;
			for (int i = zones.Count - 1; i >= 0; i--)
			{
				Zone z = zones[i];
				if (pinnedKeys.Contains(z.Key)) continue;   // never auto-remove pinned zones
				if (RetireQualifies(z, atr0))
					zones.RemoveAt(i);
			}
		}

		// True when a zone is exactly one trigger away from being retired (for a tooltip warning).
		private string RetireWarning(Zone z)
		{
			if (RetireOnFlips   && MaxFlips   > 1 && z.Flips   == MaxFlips   - 1) return "retires on next flip";
			if (RetireOnTouches && MaxTouches > 1 && z.Touches == MaxTouches - 1) return "retires on next touch";
			return null;
		}

		// Reuses the RedTailFRVP volume-distribution + 70% value-area math.
		private bool ComputeZoneProfile(int startIdx, int endIdx, out double poc, out double vah, out double val, out double totalVol, out double srcHi, out double srcLo)
		{
			poc = vah = val = totalVol = 0;
			srcHi = srcLo = 0;

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

			int rows = Math.Max(2, ProfileRows);
			double interval = (hi - lo) / (rows - 1);
			if (interval <= 0) return false;

			double[] vol = new double[rows];

			for (int idx = startIdx; idx <= endIdx; idx++)
			{
				int ba = CurrentBar - idx;
				if (ba < 0 || ba > CurrentBar) continue;

				double bl = Low[ba];
				double bh = High[ba];
				double bv = Volume[ba];

				int mn = Clamp((int)Math.Floor((bl - lo) / interval), 0, rows - 1);
				int mx = Clamp((int)Math.Ceiling((bh - lo) / interval), 0, rows - 1);
				int touched = mx - mn + 1;
				if (touched <= 0) continue;

				double per = bv / touched;
				for (int j = mn; j <= mx; j++)
					vol[j] += per;
			}

			int pocI = 0;
			double maxV = 0;
			double total = 0;
			for (int i = 0; i < rows; i++)
			{
				total += vol[i];
				if (vol[i] > maxV) { maxV = vol[i]; pocI = i; }
			}
			if (maxV <= 0) return false;

			double target = total * ValueAreaPct / 100.0;
			int up = pocI, dn = pocI;
			double sum = vol[pocI];
			while (sum < target)
			{
				double vUp = (up < rows - 1) ? vol[up + 1] : 0;
				double vDn = (dn > 0)        ? vol[dn - 1] : 0;
				if (vUp == 0 && vDn == 0) break;
				if (vUp >= vDn) { sum += vUp; up++; }
				else            { sum += vDn; dn--; }
			}

			poc = lo + pocI * interval;
			vah = lo + up   * interval;
			val = lo + dn   * interval;
			totalVol = total;
			return true;
		}

		private static int Clamp(int v, int min, int max)
		{
			return v < min ? min : (v > max ? max : v);
		}

		// 0 if price is inside the value area, else distance to the nearest edge
		private static double ZoneDistance(Zone z, double price)
		{
			if (price >= z.VAL && price <= z.VAH) return 0;
			return price < z.VAL ? z.VAL - price : price - z.VAH;
		}
		#endregion

		#region Rendering
		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			cachedControl = chartControl;
			cachedScale   = chartScale;

			if (chartControl == null || chartScale == null) return;
			if (zones.Count == 0) return;

			SharpDX.Direct2D1.RenderTarget rt = RenderTarget;
			if (rt == null) return;

			ChartPanel cp = chartControl.ChartPanels[chartScale.PanelIndex];
			int panelLeft  = cp.X;
			int panelRight = cp.X + cp.W;

			// pick which zones to show: pinned always, then closest-to-price / most-recent up to the cap
			List<Zone> pinnedVisible = new List<Zone>();
			List<Zone> visible = new List<Zone>();
			for (int i = 0; i < zones.Count; i++)
			{
				Zone z = zones[i];
				if (hiddenKeys.Contains(z.Key)) continue;

				if (pinnedKeys.Contains(z.Key)) { pinnedVisible.Add(z); continue; }

				if (WeakZoneDisplay == RedTailWeakDisplay.Hide && !z.Strong) continue;
				if (EnableMitigation && MitigatedDisplay == RedTailWeakDisplay.Hide && z.State == RedTailZoneState.Mitigated) continue;
				visible.Add(z);
			}

			if (PrioritizeNearPrice && lastPrice > 0)
				visible.Sort((a, b) =>
				{
					int c = ZoneDistance(a, lastPrice).CompareTo(ZoneDistance(b, lastPrice));
					return c != 0 ? c : b.StartTime.CompareTo(a.StartTime);
				});
			else
				visible.Sort((a, b) => b.StartTime.CompareTo(a.StartTime));

			if (!showAllZones && MaxZonesToShow > 0 && visible.Count > MaxZonesToShow)
				visible = visible.GetRange(0, MaxZonesToShow);

			visible.AddRange(pinnedVisible);   // pinned bypass the cap

			// project the right edge to N bars past the last painted bar
			float spacing = 5f;
			if (lastBarTime != DateTime.MinValue && prevBarTime != DateTime.MinValue && lastBarTime != prevBarTime)
			{
				float a = chartControl.GetXByTime(prevBarTime);
				float b = chartControl.GetXByTime(lastBarTime);
				if (b > a) spacing = b - a;
			}
			float xExtend = chartControl.GetXByTime(lastBarTime) + ExtendBars * spacing;

			deleteHandles.Clear();
			pinHandles.Clear();
			hoverBoxes.Clear();

			using (var handleBg = ToBrush(rt, Brushes.Black, 0.70f))
			using (var handleFg = ToBrush(rt, Brushes.White, 1f))
			using (var pinBg    = ToBrush(rt, Brushes.Goldenrod, 0.90f))
			using (var pocStroke = MakeStroke(rt, POCStyle))
			using (var vaStroke  = MakeStroke(rt, VAStyle))
			{
				foreach (Zone z in visible)
				{
					// colors: supply/demand by breakout direction (fall back to plain colors)
					Brush fillCol = ColorBySupplyDemand ? (z.Dir > 0 ? DemandColor : SupplyColor) : ValueAreaColor;
					bool  sdLines = ColorBySupplyDemand && ColorLinesBySupplyDemand;
					Brush lineCol = sdLines ? fillCol : VAColor;
					Brush pocCol  = sdLines ? fillCol : POCColor;

					// opacity: weak dimming x mitigation state
					float op = 1f;
					if (!z.Strong && WeakZoneDisplay == RedTailWeakDisplay.Dim)
						op *= WeakOpacityPct / 100f;
					if (EnableMitigation)
					{
						if (z.State == RedTailZoneState.Tested)
							op *= TestedOpacityPct / 100f;
						else if (z.State == RedTailZoneState.Mitigated && MitigatedDisplay == RedTailWeakDisplay.Dim)
							op *= MitigatedOpacityPct / 100f;
					}

					float xLeft  = chartControl.GetXByTime(z.StartTime);
					float xRight = ExtendRight ? xExtend : chartControl.GetXByTime(z.EndTime);

					if (xLeft  < panelLeft)  xLeft  = panelLeft;
					if (xRight > panelRight) xRight = panelRight;
					if (xRight <= xLeft) continue;

					float yVAH = chartScale.GetYByValue(z.VAH);
					float yVAL = chartScale.GetYByValue(z.VAL);
					float yPOC = chartScale.GetYByValue(z.POC);

					float yTop = Math.Min(yVAH, yVAL);
					float yBot = Math.Max(yVAH, yVAL);

					var fillBr = ToBrush(rt, fillCol, ValueAreaOpacity / 100f * op);
					var pocB   = ToBrush(rt, pocCol, op);
					var vaB    = ToBrush(rt, lineCol, op);

					// source-bar highlight: box the actual candles that formed the zone.
					// Drawn first (behind the VA fill/lines) and only across the source span,
					// not the right-extension, so it hugs the consolidation candles.
					if (ShowSourceBars && z.SrcHigh > z.SrcLow)
					{
						float xs0 = chartControl.GetXByTime(z.StartTime);
						float xs1 = chartControl.GetXByTime(z.EndTime);
						if (xs0 < panelLeft)  xs0 = panelLeft;
						if (xs1 > panelRight) xs1 = panelRight;
						if (xs1 > xs0)
						{
							float ysHi = chartScale.GetYByValue(z.SrcHigh);
							float ysLo = chartScale.GetYByValue(z.SrcLow);
							using (var srcBr = ToBrush(rt, SourceBarsColor, SourceBarsOpacityPct / 100f * op))
								rt.FillRectangle(new SharpDX.RectangleF(xs0, ysHi, xs1 - xs0, ysLo - ysHi), srcBr);
						}
					}

					// value-area fill
					if (ShowValueAreaFill)
					{
						var rect = new SharpDX.RectangleF(xLeft, yTop, xRight - xLeft, yBot - yTop);
						rt.FillRectangle(rect, fillBr);
					}

					// VAH / VAL boundaries
					if (ShowVAHVAL)
					{
						rt.DrawLine(new SharpDX.Vector2(xLeft, yVAH), new SharpDX.Vector2(xRight, yVAH), vaB, VAWidth, vaStroke);
						rt.DrawLine(new SharpDX.Vector2(xLeft, yVAL), new SharpDX.Vector2(xRight, yVAL), vaB, VAWidth, vaStroke);
					}

					// POC
					if (ShowPOC)
						rt.DrawLine(new SharpDX.Vector2(xLeft, yPOC), new SharpDX.Vector2(xRight, yPOC), pocB, POCWidth, pocStroke);

					// delete + pin buttons (pop-up) at the right edge, centered on the POC
					float hs = 14f;
					bool  isPinned = pinnedKeys.Contains(z.Key);
					float labelX = xRight + 3;
					if (ShowDeleteHandles)
						labelX = xRight + 2 + hs + 3 + hs + 5;   // reserve room for both buttons

					// persistent pin marker so pinned zones are identifiable without hovering
					if (isPinned)
					{
						var dot = new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(xRight - 5, yPOC), 3.5f, 3.5f);
						rt.FillEllipse(dot, pinBg);
					}

					// hover region = the whole band strip (reveals buttons / drives the tooltip)
					if ((ShowDeleteHandles && HoverToShowDelete) || ShowTooltip)
					{
						float ht = yTop, hb = yBot;
						const float minH = 12f;
						if (hb - ht < minH) { float padv = (minH - (hb - ht)) / 2f; ht -= padv; hb += padv; }
						hoverBoxes.Add(new HoverBox { L = xLeft, T = ht, R = xRight + (hs * 2) + 12, B = hb, PocY = yPOC, Key = z.Key });
					}

					bool drawButton = ShowDeleteHandles && (!HoverToShowDelete || (hasHover && hoveredKey == z.Key));
					if (drawButton)
					{
						float hT = yPOC - hs / 2f;

						// delete (X)
						float hL = xRight + 2;
						if (hL + hs + 3 + hs > panelRight) hL = panelRight - (hs + 3 + hs) - 1;
						var rrDel = new SharpDX.Direct2D1.RoundedRectangle { Rect = new SharpDX.RectangleF(hL, hT, hs, hs), RadiusX = 3f, RadiusY = 3f };
						rt.FillRoundedRectangle(rrDel, handleBg);
						rt.DrawRoundedRectangle(rrDel, handleFg, 1f);
						float pad = 4f;
						rt.DrawLine(new SharpDX.Vector2(hL + pad, hT + pad), new SharpDX.Vector2(hL + hs - pad, hT + hs - pad), handleFg, 1.5f);
						rt.DrawLine(new SharpDX.Vector2(hL + hs - pad, hT + pad), new SharpDX.Vector2(hL + pad, hT + hs - pad), handleFg, 1.5f);
						deleteHandles.Add(new DeleteHandle { L = hL, T = hT, W = hs, H = hs, Key = z.Key });

						// pin (filled gold when pinned, hollow when not)
						float pL = hL + hs + 3;
						var rrPin = new SharpDX.Direct2D1.RoundedRectangle { Rect = new SharpDX.RectangleF(pL, hT, hs, hs), RadiusX = 3f, RadiusY = 3f };
						rt.FillRoundedRectangle(rrPin, isPinned ? pinBg : handleBg);
						rt.DrawRoundedRectangle(rrPin, handleFg, 1f);
						var pinDot = new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(pL + hs / 2f, hT + hs / 2f), 3f, 3f);
						rt.FillEllipse(pinDot, handleFg);
						pinHandles.Add(new DeleteHandle { L = pL, T = hT, W = hs, H = hs, Key = z.Key });
					}

					// labels
					if (ShowLabels)
					{
						if (ShowPOC)    DrawText(rt, pocB, Label("POC", z.POC), labelX, yPOC);
						if (ShowVAHVAL)
						{
							DrawText(rt, vaB, Label("VAH", z.VAH), labelX, yVAH);
							DrawText(rt, vaB, Label("VAL", z.VAL), labelX, yVAL);
						}
					}

					// delta value (net buy/sell during the base), colored by sign
					if (ShowDelta && EnableDelta)
					{
						using (var dBr = ToBrush(rt, z.Delta >= 0 ? DemandColor : SupplyColor, op))
						{
							string dTxt = "\u0394 " + (z.Delta >= 0 ? "+" : "") + z.Delta.ToString("N0");
							DrawText(rt, dBr, dTxt, labelX, yTop - 16);
						}
					}

					fillBr.Dispose();
					pocB.Dispose();
					vaB.Dispose();
				}

				// hover tooltip with the zone's stats
				if (ShowTooltip && hasHover)
				{
					Zone hz = null;
					for (int i = 0; i < zones.Count; i++)
						if (zones[i].Key == hoveredKey) { hz = zones[i]; break; }
					if (hz != null)
						DrawTooltip(rt, handleBg, handleFg, hz, (float)mousePos.X, (float)mousePos.Y, cp);
				}

				// on-chart toggle: near-price subset <-> all zones (drawn last so it stays on top)
				if (ShowAllZonesButton)
				{
					float bw = 52f, bh = 18f;
					float ox = ButtonOffsetX, oy = ButtonOffsetY;
					float bx, by;
					switch (ButtonCorner)
					{
						case RedTailButtonCorner.TopRight:
							bx = cp.X + cp.W - bw - ox; by = cp.Y + oy; break;
						case RedTailButtonCorner.BottomLeft:
							bx = cp.X + ox;             by = cp.Y + cp.H - bh - oy; break;
						case RedTailButtonCorner.BottomRight:
							bx = cp.X + cp.W - bw - ox; by = cp.Y + cp.H - bh - oy; break;
						default: // TopLeft
							bx = cp.X + ox;             by = cp.Y + oy; break;
					}
					toggleRect      = new SharpDX.RectangleF(bx, by, bw, bh);
					toggleRectValid = true;

					var rr = new SharpDX.Direct2D1.RoundedRectangle { Rect = toggleRect, RadiusX = 4f, RadiusY = 4f };
					rt.FillRoundedRectangle(rr, showAllZones ? pinBg : handleBg);
					rt.DrawRoundedRectangle(rr, handleFg, 1f);

					using (var btf = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", 10))
					{
						btf.TextAlignment      = SharpDX.DirectWrite.TextAlignment.Center;
						btf.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
						using (var btl = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, showAllZones ? "ALL" : "NEAR", btf, bw, bh))
							rt.DrawTextLayout(new SharpDX.Vector2(bx, by), btl, handleFg);
					}
				}
				else toggleRectValid = false;
			}
		}

		private void DrawTooltip(SharpDX.Direct2D1.RenderTarget rt, SharpDX.Direct2D1.Brush bg,
								 SharpDX.Direct2D1.Brush fg, Zone z, float mx, float my, ChartPanel cp)
		{
			string type  = z.Dir > 0 ? "Demand" : "Supply";
			if (z.Flips > 0) type += " (flipped)";
			string sting = z.Strong ? "Strong" : "Weak";
			int    age   = CurrentBar - z.EndBarIdx;

			// never label a level "Fresh" once price has actually touched it
			string state = (z.State == RedTailZoneState.Fresh && z.Touches > 0)
						 ? "Tested" : z.State.ToString();

			List<string> lines = new List<string>
			{
				type + "  -  " + sting + "  -  " + state,
				"POC "   + z.POC.ToString("F2") + "   VA " + z.VAL.ToString("F2") + " / " + z.VAH.ToString("F2"),
				"Volume "     + z.Volume.ToString("N0"),
				"Departure "  + z.Departure.ToString("F2") + "x   Bars " + z.Bars,
				"Age "        + age + " bars   Touches " + z.Touches + (z.Flips > 0 ? "   Flips " + z.Flips : "")
			};
			if (EnableDelta)
				lines.Add("Delta " + (z.Delta >= 0 ? "+" : "") + z.Delta.ToString("N0"));

			string warn = RetireWarning(z);
			if (warn != null)
				lines.Add("\u26A0 " + warn);

			float pad = 6f, lineH = 15f;
			int maxChars = 0;
			for (int i = 0; i < lines.Count; i++)
				if (lines[i].Length > maxChars) maxChars = lines[i].Length;
			float w = maxChars * 6.6f + pad * 2;
			if (w < 140f) w = 140f;
			float h = lines.Count * lineH + pad * 2;
			float x = mx + 16, y = my + 12;
			if (x + w > cp.X + cp.W) x = mx - w - 16;
			if (y + h > cp.Y + cp.H) y = cp.Y + cp.H - h - 2;
			if (x < cp.X) x = cp.X + 2;
			if (y < cp.Y) y = cp.Y + 2;

			var panel = new SharpDX.Direct2D1.RoundedRectangle { Rect = new SharpDX.RectangleF(x, y, w, h), RadiusX = 4f, RadiusY = 4f };
			rt.FillRoundedRectangle(panel, bg);
			rt.DrawRoundedRectangle(panel, fg, 1f);

			string text = string.Join("\n", lines);
			using (var tf = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", 11))
			{
				tf.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;
				using (var tl = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, text, tf, w, h))
				{
					rt.DrawTextLayout(new SharpDX.Vector2(x + pad, y + pad), tl, fg);
				}
			}
		}

		private string Label(string tag, double price)
		{
			return ShowPrices ? tag + " " + price.ToString("F2") : tag;
		}

		private void DrawText(SharpDX.Direct2D1.RenderTarget rt, SharpDX.Direct2D1.Brush br, string text, float x, float y)
		{
			using (var tf = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", 11))
			using (var tl = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, text, tf, 120, 16))
			{
				rt.DrawTextLayout(new SharpDX.Vector2(x + 3, y - 8), tl, br);
			}
		}

		private SharpDX.Direct2D1.SolidColorBrush ToBrush(SharpDX.Direct2D1.RenderTarget rt, Brush wpfBrush, float opacity)
		{
			SolidColorBrush scb = wpfBrush as SolidColorBrush;
			System.Windows.Media.Color c = scb != null ? scb.Color : Colors.Gray;
			var c4 = new SharpDX.Color4(c.R / 255f, c.G / 255f, c.B / 255f, opacity);
			return new SharpDX.Direct2D1.SolidColorBrush(rt, c4);
		}

		private SharpDX.Direct2D1.StrokeStyle MakeStroke(SharpDX.Direct2D1.RenderTarget rt, DashStyleHelper style)
		{
			// NT8's SharpDX does not render the predefined DashStyle.Dot/Dash reliably.
			// Use DashStyle.Custom with an explicit dash array + round caps (proven in RedTailFRVPFib).
			float[] dashes;
			switch (style)
			{
				case DashStyleHelper.Dash:       dashes = new float[] { 4f, 3f };                       break;
				case DashStyleHelper.Dot:        dashes = new float[] { 0.5f, 2f };                     break;
				case DashStyleHelper.DashDot:    dashes = new float[] { 4f, 2f, 0.5f, 2f };             break;
				case DashStyleHelper.DashDotDot: dashes = new float[] { 4f, 2f, 0.5f, 2f, 0.5f, 2f };   break;
				default:
					return new SharpDX.Direct2D1.StrokeStyle(rt.Factory,
						new SharpDX.Direct2D1.StrokeStyleProperties { DashStyle = SharpDX.Direct2D1.DashStyle.Solid });
			}

			return new SharpDX.Direct2D1.StrokeStyle(rt.Factory,
				new SharpDX.Direct2D1.StrokeStyleProperties
				{
					DashStyle = SharpDX.Direct2D1.DashStyle.Custom,
					DashCap   = SharpDX.Direct2D1.CapStyle.Round,
					StartCap  = SharpDX.Direct2D1.CapStyle.Round,
					EndCap    = SharpDX.Direct2D1.CapStyle.Round
				},
				dashes);
		}
		#endregion

		#region Mouse / per-zone deletion
		private void AttachHandlers()
		{
			if (handlersAttached || ChartControl == null) return;
			ChartControl.Dispatcher.InvokeAsync(() =>
			{
				ChartControl.MouseDown  += OnChartMouseDown;
				ChartControl.MouseMove  += OnChartMouseMove;
				ChartControl.MouseLeave += OnChartMouseLeave;
				handlersAttached = true;
			});
		}

		private void DetachHandlers()
		{
			if (!handlersAttached || ChartControl == null) return;
			try
			{
				ChartControl.Dispatcher.InvokeAsync(() =>
				{
					ChartControl.MouseDown  -= OnChartMouseDown;
					ChartControl.MouseMove  -= OnChartMouseMove;
					ChartControl.MouseLeave -= OnChartMouseLeave;
				});
			}
			catch { }
			handlersAttached = false;
		}

		private bool ModifierHeld()
		{
			switch (DeleteModifier)
			{
				case RedTailDeleteModifier.Alt:   return (Keyboard.Modifiers & ModifierKeys.Alt)     == ModifierKeys.Alt;
				case RedTailDeleteModifier.Shift: return (Keyboard.Modifiers & ModifierKeys.Shift)   == ModifierKeys.Shift;
				default:                          return (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
			}
		}

		private void OnChartMouseDown(object sender, MouseButtonEventArgs e)
		{
			if (e.ChangedButton != MouseButton.Left) return;
			if (cachedControl == null || cachedScale == null) return;

			Point p = e.GetPosition(cachedControl);

			// on-chart show-all toggle
			if (ShowAllZonesButton && toggleRectValid &&
				p.X >= toggleRect.X && p.X <= toggleRect.X + toggleRect.Width &&
				p.Y >= toggleRect.Y && p.Y <= toggleRect.Y + toggleRect.Height)
			{
				showAllZones = !showAllZones;
				e.Handled = true;
				ForceRefresh();
				return;
			}

			// 1) delete button (pop-up) click — no modifier required
			if (ShowDeleteHandles)
			{
				for (int i = deleteHandles.Count - 1; i >= 0; i--)
				{
					DeleteHandle h = deleteHandles[i];
					if (p.X >= h.L && p.X <= h.L + h.W && p.Y >= h.T && p.Y <= h.T + h.H)
					{
						hiddenKeys.Add(h.Key);
						e.Handled = true;
						ForceRefresh();
						return;
					}
				}
			}

			// pin button toggles pin (a pinned zone survives the near-price filter)
			for (int i = pinHandles.Count - 1; i >= 0; i--)
			{
				DeleteHandle h = pinHandles[i];
				if (p.X >= h.L && p.X <= h.L + h.W && p.Y >= h.T && p.Y <= h.T + h.H)
				{
					if (!pinnedKeys.Remove(h.Key)) pinnedKeys.Add(h.Key);
					e.Handled = true;
					ForceRefresh();
					return;
				}
			}

			// 2) modifier + click anywhere inside a zone
			if (!ModifierHeld()) return;                 // normal clicks (e.g. Chart Trader) pass through untouched

			DateTime clickTime;
			try { clickTime = cachedControl.GetTimeByX((int)p.X); }
			catch { return; }

			double clickPrice = cachedScale.GetValueByY((float)p.Y);

			Zone hit = null;
			double bestDist = double.MaxValue;

			for (int i = zones.Count - 1; i >= 0; i--)
			{
				Zone z = zones[i];
				if (hiddenKeys.Contains(z.Key)) continue;
				if (clickTime < z.StartTime) continue;            // band runs from start to the right

				double pad = (z.VAH - z.VAL) * 0.10;              // small forgiveness band
				if (clickPrice < z.VAL - pad || clickPrice > z.VAH + pad) continue;

				double d = Math.Abs(clickPrice - z.POC);
				if (d < bestDist) { bestDist = d; hit = z; }
			}

			if (hit != null)
			{
				hiddenKeys.Add(hit.Key);
				e.Handled = true;
				ForceRefresh();
			}
		}

		private void OnChartMouseMove(object sender, MouseEventArgs e)
		{
			if (cachedControl == null) return;
			if (!ShowTooltip && !(HoverToShowDelete && ShowDeleteHandles)) return;

			mousePos = e.GetPosition(cachedControl);
			Point p = mousePos;

			long newKey = 0;
			bool found  = false;
			float best  = float.MaxValue;

			for (int i = hoverBoxes.Count - 1; i >= 0; i--)
			{
				HoverBox hb = hoverBoxes[i];
				if (p.X >= hb.L && p.X <= hb.R && p.Y >= hb.T && p.Y <= hb.B)
				{
					float d = Math.Abs((float)p.Y - hb.PocY);
					if (d < best) { best = d; newKey = hb.Key; found = true; }
				}
			}

			if (found != hasHover || newKey != hoveredKey)
			{
				hasHover   = found;
				hoveredKey = newKey;
				ForceRefresh();
			}
			else if (ShowTooltip && found)
			{
				// keep the tooltip following the cursor (throttled)
				DateTime now = DateTime.UtcNow;
				if ((now - lastHoverRefresh).TotalMilliseconds >= 40)
				{
					lastHoverRefresh = now;
					ForceRefresh();
				}
			}
		}

		private void OnChartMouseLeave(object sender, MouseEventArgs e)
		{
			if (!hasHover) return;
			hasHover   = false;
			hoveredKey = 0;
			ForceRefresh();
		}
		#endregion

		#region Hidden-zone persistence
		// Serialized with the indicator so deletions survive reload / template save.
		[Browsable(false)]
		public string HiddenZoneKeys
		{
			get { return string.Join(",", hiddenKeys); }
			set
			{
				hiddenKeys.Clear();
				if (string.IsNullOrEmpty(value)) return;
				foreach (string s in value.Split(','))
				{
					long v;
					if (long.TryParse(s, out v)) hiddenKeys.Add(v);
				}
			}
		}

		[Browsable(false)]
		public string PinnedZoneKeys
		{
			get { return string.Join(",", pinnedKeys); }
			set
			{
				pinnedKeys.Clear();
				if (string.IsNullOrEmpty(value)) return;
				foreach (string s in value.Split(','))
				{
					long v;
					if (long.TryParse(s, out v)) pinnedKeys.Add(v);
				}
			}
		}
		#endregion

		#region Properties
		// ---- Plot accessors (for strategies / automation) ----
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SupportVAH    { get { return Values[0]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SupportPOC    { get { return Values[1]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SupportVAL    { get { return Values[2]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ResistanceVAH { get { return Values[3]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ResistancePOC { get { return Values[4]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ResistanceVAL { get { return Values[5]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SupportStrong    { get { return Values[6]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SupportState     { get { return Values[7]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ResistanceStrong { get { return Values[8]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ResistanceState  { get { return Values[9]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SupportDelta      { get { return Values[10]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ResistanceDelta   { get { return Values[11]; } }

		[NinjaScriptProperty]
		[Range(2, int.MaxValue)]
		[Display(Name = "Min consolidation bars", Order = 0, GroupName = "1. Detection")]
		public int MinConsolidationBars { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Max consolidation bars (0 = off)", Order = 1, GroupName = "1. Detection")]
		public int MaxConsolidationBars { get; set; }

		[Display(Name = "Zone height mode", Order = 2, GroupName = "1. Detection")]
		public RedTailHeightMode HeightMode { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "ATR period", Order = 3, GroupName = "1. Detection")]
		public int AtrPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "Max zone height (ATR x)", Order = 4, GroupName = "1. Detection")]
		public double MaxZoneHeightAtr { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Max zone height (ticks)", Order = 5, GroupName = "1. Detection")]
		public int MaxZoneHeightTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Breakout buffer (ticks)", Order = 6, GroupName = "1. Detection")]
		public int BreakoutBufferTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use closes for band (wick-tolerant)", Order = 14, GroupName = "1. Detection")]
		public bool UseCloseBand { get; set; }

		[NinjaScriptProperty]
		[Range(2, int.MaxValue)]
		[Display(Name = "Profile rows", Order = 7, GroupName = "1. Detection")]
		public int ProfileRows { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Value area %", Order = 8, GroupName = "1. Detection")]
		public double ValueAreaPct { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Max zones to show (0 = all)", Order = 9, GroupName = "1. Detection")]
		public int MaxZonesToShow { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Max zones stored (0 = unlimited)", Order = 13, GroupName = "1. Detection")]
		public int MaxStoredZones { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Prioritize zones near price", Order = 12, GroupName = "1. Detection")]
		public bool PrioritizeNearPrice { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Near-price refresh throttle (ms, 0 = every tick)", Order = 13, GroupName = "1. Detection")]
		public int PriceRefreshMs { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Replace overlapping zones", Order = 10, GroupName = "1. Detection")]
		public bool ReplaceOverlapping { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, 1.0)]
		[Display(Name = "Overlap threshold (0-1)", Order = 11, GroupName = "1. Detection")]
		public double OverlapThreshold { get; set; }

		// ---- Strength ----
		[Display(Name = "Strength basis", Order = 0, GroupName = "4. Strength")]
		public RedTailStrengthMode StrengthMode { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Departure window (bars)", Order = 1, GroupName = "4. Strength")]
		public int DepartureBars { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "Min departure ratio (zone heights)", Order = 2, GroupName = "4. Strength")]
		public double MinDepartureRatio { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Strong min bars (time)", Order = 3, GroupName = "4. Strength")]
		public int StrongMinBars { get; set; }

		[Display(Name = "Weak zones", Order = 4, GroupName = "4. Strength")]
		public RedTailWeakDisplay WeakZoneDisplay { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Weak zone opacity %", Order = 5, GroupName = "4. Strength")]
		public int WeakOpacityPct { get; set; }

		// ---- Supply / Demand ----
		[NinjaScriptProperty]
		[Display(Name = "Color by supply / demand", Order = 17, GroupName = "2. Display")]
		public bool ColorBySupplyDemand { get; set; }

		[XmlIgnore]
		[Display(Name = "Demand color (broke up)", Order = 18, GroupName = "2. Display")]
		public Brush DemandColor { get; set; }

		[Browsable(false)]
		public string DemandColorSerialize
		{
			get { return Serialize.BrushToString(DemandColor); }
			set { DemandColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Supply color (broke down)", Order = 19, GroupName = "2. Display")]
		public Brush SupplyColor { get; set; }

		[Browsable(false)]
		public string SupplyColorSerialize
		{
			get { return Serialize.BrushToString(SupplyColor); }
			set { SupplyColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Highlight source bars", Order = 20, GroupName = "2. Display")]
		public bool ShowSourceBars { get; set; }

		[XmlIgnore]
		[Display(Name = "Source bar color", Order = 21, GroupName = "2. Display")]
		public Brush SourceBarsColor { get; set; }

		[Browsable(false)]
		public string SourceBarsColorSerialize
		{
			get { return Serialize.BrushToString(SourceBarsColor); }
			set { SourceBarsColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Source bar opacity %", Order = 22, GroupName = "2. Display")]
		public int SourceBarsOpacityPct { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Also color POC/VAH/VAL lines", Order = 20, GroupName = "2. Display")]
		public bool ColorLinesBySupplyDemand { get; set; }

		// ---- Mitigation ----
		[NinjaScriptProperty]
		[Display(Name = "Enable mitigation", Order = 0, GroupName = "5. Mitigation")]
		public bool EnableMitigation { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Flip role on breakthrough (supply<->demand)", Order = 4, GroupName = "5. Mitigation")]
		public bool FlipOnBreakthrough { get; set; }

		[Display(Name = "Mitigated zones", Order = 1, GroupName = "5. Mitigation")]
		public RedTailWeakDisplay MitigatedDisplay { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Mitigated opacity %", Order = 2, GroupName = "5. Mitigation")]
		public int MitigatedOpacityPct { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Tested opacity %", Order = 3, GroupName = "5. Mitigation")]
		public int TestedOpacityPct { get; set; }

		// ---- Delta ----
		[NinjaScriptProperty]
		[Display(Name = "Enable delta (needs Tick Replay for history)", Order = 0, GroupName = "6. Delta")]
		public bool EnableDelta { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show delta value", Order = 1, GroupName = "6. Delta")]
		public bool ShowDelta { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require delta to confirm strong", Order = 2, GroupName = "6. Delta")]
		public bool RequireDeltaConfirm { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Min delta to confirm", Order = 3, GroupName = "6. Delta")]
		public int MinDeltaConfirm { get; set; }

		// ---- Removal ----
		[NinjaScriptProperty]
		[Display(Name = "Retire flip-churned zones", Order = 0, GroupName = "7. Removal")]
		public bool RetireOnFlips { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Max flips before retire", Order = 1, GroupName = "7. Removal")]
		public int MaxFlips { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Retire eroded (over-tested) zones", Order = 2, GroupName = "7. Removal")]
		public bool RetireOnTouches { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Max touches before retire", Order = 3, GroupName = "7. Removal")]
		public int MaxTouches { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Retire consumed + abandoned zones", Order = 4, GroupName = "7. Removal")]
		public bool RetireWhenAbandoned { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "Abandoned: distance away (ATR x)", Order = 5, GroupName = "7. Removal")]
		public double AbandonAtr { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Abandoned: bars since last touch", Order = 6, GroupName = "7. Removal")]
		public int AbandonBars { get; set; }

		// ---- Display ----
		[NinjaScriptProperty]
		[Display(Name = "Show value area fill", Order = 0, GroupName = "2. Display")]
		public bool ShowValueAreaFill { get; set; }

		[XmlIgnore]
		[Display(Name = "Value area color", Order = 1, GroupName = "2. Display")]
		public Brush ValueAreaColor { get; set; }

		[Browsable(false)]
		public string ValueAreaColorSerialize
		{
			get { return Serialize.BrushToString(ValueAreaColor); }
			set { ValueAreaColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Value area opacity %", Order = 2, GroupName = "2. Display")]
		public int ValueAreaOpacity { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Extend zones right", Order = 3, GroupName = "2. Display")]
		public bool ExtendRight { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Extend bars past last bar", Order = 14, GroupName = "2. Display")]
		public int ExtendBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show delete buttons", Order = 15, GroupName = "2. Display")]
		public bool ShowDeleteHandles { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Delete buttons on hover only", Order = 16, GroupName = "2. Display")]
		public bool HoverToShowDelete { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show POC", Order = 4, GroupName = "2. Display")]
		public bool ShowPOC { get; set; }

		[XmlIgnore]
		[Display(Name = "POC color", Order = 5, GroupName = "2. Display")]
		public Brush POCColor { get; set; }

		[Browsable(false)]
		public string POCColorSerialize
		{
			get { return Serialize.BrushToString(POCColor); }
			set { POCColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "POC width", Order = 6, GroupName = "2. Display")]
		public int POCWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "POC style", Order = 7, GroupName = "2. Display")]
		public DashStyleHelper POCStyle { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show VAH/VAL", Order = 8, GroupName = "2. Display")]
		public bool ShowVAHVAL { get; set; }

		[XmlIgnore]
		[Display(Name = "VAH/VAL color", Order = 9, GroupName = "2. Display")]
		public Brush VAColor { get; set; }

		[Browsable(false)]
		public string VAColorSerialize
		{
			get { return Serialize.BrushToString(VAColor); }
			set { VAColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "VAH/VAL width", Order = 10, GroupName = "2. Display")]
		public int VAWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "VAH/VAL style", Order = 11, GroupName = "2. Display")]
		public DashStyleHelper VAStyle { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show labels", Order = 12, GroupName = "2. Display")]
		public bool ShowLabels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show prices on labels", Order = 13, GroupName = "2. Display")]
		public bool ShowPrices { get; set; }

		// ---- Interaction ----
		[Display(Name = "Delete modifier (modifier + left-click)", Order = 0, GroupName = "3. Interaction")]
		public RedTailDeleteModifier DeleteModifier { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show hover tooltip (zone stats)", Order = 2, GroupName = "3. Interaction")]
		public bool ShowTooltip { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show all/near toggle button", Order = 3, GroupName = "3. Interaction")]
		public bool ShowAllZonesButton { get; set; }

		[Display(Name = "Button corner", Order = 4, GroupName = "3. Interaction")]
		public RedTailButtonCorner ButtonCorner { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Button X offset (px)", Order = 5, GroupName = "3. Interaction")]
		public int ButtonOffsetX { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Button Y offset (px)", Order = 6, GroupName = "3. Interaction")]
		public int ButtonOffsetY { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Clear all hidden zones", Order = 1, GroupName = "3. Interaction")]
		public bool ClearAllHiddenZones { get; set; }
		#endregion
	}
}
