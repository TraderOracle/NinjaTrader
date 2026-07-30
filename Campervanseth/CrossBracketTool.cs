#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
#endregion

// A custom drawing tool (not an indicator plot) so it gets real mouse drag
// interactivity, modeled closely on NinjaTrader's own built-in RiskReward
// drawing tool. Click-drag from the entry line: dragging toward profit for
// the current side creates/moves the target, dragging toward loss creates/
// moves the stop. Once a leg has a live order, its own line can be dragged
// directly to modify that order's price.
namespace NinjaTrader.NinjaScript.DrawingTools
{
    public class CrossBracketTool8 : DrawingTool
    {
        private const int CursorSensitivity = 12;

        private ChartAnchor editingAnchor;
        private bool        draggingFromEntry;
        private bool        dragResolvedIsStop;

        [Browsable(false)] public ChartAnchor EntryAnchor  { get; set; }
        [Browsable(false)] public ChartAnchor StopAnchor   { get; set; }
        [Browsable(false)] public ChartAnchor TargetAnchor { get; set; }

        [Browsable(false)]
        public override IEnumerable<ChartAnchor> Anchors { get { return new[] { EntryAnchor, StopAnchor, TargetAnchor }; } }

        [Browsable(false)] public Account        TradingAccount   { get; private set; }
        [Browsable(false)] public Instrument      TargetInstrument { get; private set; }
        [Browsable(false)] public int             Quantity         { get; private set; }
        [Browsable(false)] public MarketPosition  Side             { get; private set; }
        [Browsable(false)] public string          OcoId            { get; private set; }

        [Browsable(false)] public bool  StopLive    { get; private set; }
        [Browsable(false)] public bool  TargetLive  { get; private set; }
        [Browsable(false)] public Order StopOrder    { get; private set; }
        [Browsable(false)] public Order TargetOrder  { get; private set; }

        [Browsable(false)] public bool LabelsOnRight       { get; set; }
        [Browsable(false)] public bool DisplayPnlInDollars { get; private set; }

        public Stroke EntryStroke  { get; set; }
        public Stroke StopStroke   { get; set; }
        public Stroke TargetStroke { get; set; }

        public event Action<string> Logged;

        public override object Icon { get { return null; } }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "CrossBracketTool";

                EntryAnchor  = new ChartAnchor { DisplayName = "Entry",  IsEditing = false, DrawingTool = this };
                StopAnchor   = new ChartAnchor { DisplayName = "Stop",   IsEditing = false, DrawingTool = this };
                TargetAnchor = new ChartAnchor { DisplayName = "Target", IsEditing = false, DrawingTool = this };

                EntryStroke  = new Stroke(Brushes.DodgerBlue,     DashStyleHelper.Solid, 2f);
                StopStroke   = new Stroke(Brushes.OrangeRed,      DashStyleHelper.Dash,  2f);
                TargetStroke = new Stroke(Brushes.MediumSeaGreen, DashStyleHelper.Dash,  2f);
            }
            else if (State == State.Terminated)
            {
                Dispose();
            }
        }

        // Called once right after the tool is created, before it is shown.
        public void Activate(Account account, Instrument instrument, int quantity, MarketPosition side, string ocoId, bool labelsOnRight, bool displayPnlInDollars, Brush entryBrush, Brush stopBrush, Brush targetBrush)
        {
            TradingAccount   = account;
            TargetInstrument = instrument;
            Quantity         = quantity;
            Side             = side;
            OcoId            = ocoId;
            LabelsOnRight       = labelsOnRight;
            DisplayPnlInDollars = displayPnlInDollars;

            EntryStroke  = new Stroke(entryBrush  ?? Brushes.DodgerBlue,     DashStyleHelper.Solid, 2f);
            StopStroke   = new Stroke(stopBrush   ?? Brushes.OrangeRed,      DashStyleHelper.Dash,  2f);
            TargetStroke = new Stroke(targetBrush ?? Brushes.MediumSeaGreen, DashStyleHelper.Dash,  2f);

            EntryAnchor.IsEditing  = false;
            StopAnchor.IsEditing   = false;
            TargetAnchor.IsEditing = false;

            IsLocked     = false;
            DrawingState = DrawingState.Normal;
        }

        // Called when the position's average price / side / size changes but stays open.
        public void UpdateEntry(double avgPrice, int quantity, MarketPosition side)
        {
            EntryAnchor.Price = avgPrice;
            Quantity          = quantity;
            Side              = side;
        }

        public void SetPnlDisplayMode(bool displayPnlInDollars)
        {
            DisplayPnlInDollars = displayPnlInDollars;
        }

        // Order.Quantity is the ORIGINAL submitted size and never changes as an
        // order partially fills - Order.Filled is the cumulative filled amount.
        // The order's actual remaining working size is Quantity - Filled, so
        // that's what has to match the new desired protective size, not Quantity
        // itself. Otherwise a leg that just partially filled (correctly leaving
        // the right remainder resting) gets mistaken for "still full size" and
        // gets shrunk a second time.
        private static int RemainingQty(Order order)
        {
            return order.Quantity - order.Filled;
        }

        // Grows the live legs when a plain entry order fills (an execution that
        // is neither the stop nor the target order), driven directly off the
        // fill's own quantity rather than the derived Position event - the
        // stop/target orders are entirely unrelated to this fill, so their
        // Quantity/Filled fields can't be racing it.
        public void GrowLegs(int byQty)
        {
            if (StopOrder != null && StopLive)
                ResizeLeg(StopOrder, RemainingQty(StopOrder) + byQty, true);

            if (TargetOrder != null && TargetLive)
                ResizeLeg(TargetOrder, RemainingQty(TargetOrder) + byQty, false);
        }

        // Called when the stop or target order itself fills (fully or
        // partially). The leg that just filled needs no resize - its own
        // remaining already reflects the reduced position by construction,
        // and checking its own (possibly not-yet-refreshed) Filled field here
        // is exactly what caused the earlier bug. Only the sibling leg needs
        // to shrink, computed from the fill's own quantity directly.
        public void NotifyLegFilled(bool isStopLeg, int filledQty)
        {
            Order sibling     = isStopLeg ? TargetOrder : StopOrder;
            bool  siblingLive = isStopLeg ? TargetLive : StopLive;

            if (sibling == null || !siblingLive)
                return;

            ResizeLeg(sibling, RemainingQty(sibling) - filledQty, !isStopLeg);
        }

        private void ResizeLeg(Order order, int newRemaining, bool isStop)
        {
            if (newRemaining <= 0)
                return;

            try
            {
                order.QuantityChanged = newRemaining + order.Filled;
                TradingAccount.Change(new[] { order });
                Log(string.Format("{0} remaining resized to {1}", isStop ? "stop" : "target", newRemaining));
            }
            catch (Exception ex)
            {
                Log((isStop ? "stop" : "target") + " resize error: " + ex.Message);
            }
        }

        // NinjaTrader's OrderUpdate/ExecutionUpdate events can hand back a
        // different Order object instance than the one CreateOrder/Submit
        // returned, even for the same broker order - reference equality (==)
        // silently fails and misroutes fills. Order.Name is the one thing we
        // set ourselves at creation and can rely on to match consistently.
        private static bool SameOrder(Order a, Order b)
        {
            return a != null && b != null && a.Name == b.Name;
        }

        public bool IsStopOrder(Order order)   { return SameOrder(order, StopOrder); }
        public bool IsTargetOrder(Order order) { return SameOrder(order, TargetOrder); }

        // The broker's own order state is the source of truth for where a
        // line should render - always resync the visual anchor to it here,
        // regardless of what our local drag/mouse state thinks happened.
        // This makes the chart self-heal from any local desync (a missed
        // mouse-up, a race, whatever) the moment the next order update
        // arrives, rather than trusting our own optimistic local mutation
        // forever.
        public void NotifyOrderUpdate(Order order)
        {
            if (StopOrder != null && SameOrder(order, StopOrder) && order.StopPrice > 0)
                StopAnchor.Price = order.StopPrice;

            if (TargetOrder != null && SameOrder(order, TargetOrder) && order.LimitPrice > 0)
                TargetAnchor.Price = order.LimitPrice;

            bool isDone = order.OrderState == OrderState.Filled || order.OrderState == OrderState.Cancelled || order.OrderState == OrderState.Rejected;
            if (!isDone)
                return;

            if (StopOrder != null && SameOrder(order, StopOrder))
            {
                StopLive  = false;
                StopOrder = null;
            }
            if (TargetOrder != null && SameOrder(order, TargetOrder))
            {
                TargetLive  = false;
                TargetOrder = null;
            }
        }

        public void CancelAll()
        {
            CancelWorking(StopOrder);
            CancelWorking(TargetOrder);
            StopOrder   = null;
            TargetOrder = null;
            StopLive    = false;
            TargetLive  = false;
        }

        private void CancelWorking(Order order)
        {
            if (order == null || TradingAccount == null)
                return;

            if (order.OrderState == OrderState.Working || order.OrderState == OrderState.Accepted || order.OrderState == OrderState.PartFilled)
            {
                try { TradingAccount.Cancel(new[] { order }); }
                catch (Exception ex) { Log("cancel error: " + ex.Message); }
            }
        }

        private void Log(string msg)
        {
            Logged?.Invoke(msg);
        }

        // NinjaTrader can call drawing-tool callbacks while a chart tab is being
        // created, reloaded, moved, or closed. During those transitions one or
        // more chart objects (most commonly ChartScale) can briefly be null.
        // Never pass a null chartScale into ChartAnchor.GetPoint().
        private static bool HasChartContext(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale)
        {
            return chartControl != null && chartPanel != null && chartScale != null;
        }

        private static bool IsUsableY(double y)
        {
            return !double.IsNaN(y) && !double.IsInfinity(y);
        }

        private double RoundPrice(double price)
        {
            if (TargetInstrument == null || TargetInstrument.MasterInstrument == null)
                return price;

            return TargetInstrument.MasterInstrument.RoundToTickSize(price);
        }

        private void SubmitLeg(bool isStop, double price)
        {
            if (TradingAccount == null || TargetInstrument == null || Quantity <= 0)
                return;

            OrderAction action = Side == MarketPosition.Long ? OrderAction.Sell : OrderAction.Buy;
            OrderType   type   = isStop ? OrderType.StopMarket : OrderType.Limit;

            try
            {
                Order order = TradingAccount.CreateOrder(
                    TargetInstrument,
                    action,
                    type,
                    OrderEntry.Manual,
                    TimeInForce.Day,
                    Quantity,
                    isStop ? 0 : price,
                    isStop ? price : 0,
                    OcoId,
                    (isStop ? "XStop-" : "XTarget-") + Guid.NewGuid().ToString("N").Substring(0, 6),
                    Core.Globals.MaxDate,
                    null);

                TradingAccount.Submit(new[] { order });

                if (isStop) { StopOrder = order; StopLive = true; StopAnchor.Price = price; }
                else        { TargetOrder = order; TargetLive = true; TargetAnchor.Price = price; }

                Log(string.Format("{0} order created @ {1} qty {2}", isStop ? "stop" : "target", price, Quantity));
            }
            catch (Exception ex)
            {
                Log((isStop ? "stop" : "target") + " create error: " + ex.Message);
            }
        }

        // Creates the stop at breakeven+ticks if none exists yet, or moves the
        // existing one there. "Ticks in profit" always means past the entry
        // in the direction that favors the current side.
        public void SetBreakeven(int ticks)
        {
            if (TargetInstrument == null || Quantity <= 0)
                return;

            double tick  = TargetInstrument.MasterInstrument.TickSize;
            double price = Side == MarketPosition.Long
                ? EntryAnchor.Price + ticks * tick
                : EntryAnchor.Price - ticks * tick;

            price = RoundPrice(price);

            if (StopLive)
                ModifyLeg(true, price);
            else
                SubmitLeg(true, price);
        }

        private void ModifyLeg(bool isStop, double price)
        {
            Order order = isStop ? StopOrder : TargetOrder;
            if (order == null || TradingAccount == null)
                return;

            try
            {
                if (isStop) order.StopPriceChanged  = price;
                else        order.LimitPriceChanged = price;

                TradingAccount.Change(new[] { order });

                if (isStop) StopAnchor.Price = price;
                else        TargetAnchor.Price = price;

                Log(string.Format("{0} order moved to {1}", isStop ? "stop" : "target", price));
            }
            catch (Exception ex)
            {
                Log((isStop ? "stop" : "target") + " modify error: " + ex.Message);
            }
        }

        private double LineY(ChartAnchor anchor, ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale)
        {
            if (anchor == null || !HasChartContext(chartControl, chartPanel, chartScale))
                return double.NaN;

            try
            {
                return anchor.GetPoint(chartControl, chartPanel, chartScale).Y;
            }
            catch (ArgumentNullException)
            {
                // A chart object became unavailable during a tab/reload transition.
                return double.NaN;
            }
        }

        public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
        {
            if (IsLocked || !HasChartContext(chartControl, chartPanel, chartScale))
                return null;

            double stopY = StopLive ? LineY(StopAnchor, chartControl, chartPanel, chartScale) : double.NaN;
            if (IsUsableY(stopY) && Math.Abs(point.Y - stopY) <= CursorSensitivity)
                return Cursors.SizeNS;

            double targetY = TargetLive ? LineY(TargetAnchor, chartControl, chartPanel, chartScale) : double.NaN;
            if (IsUsableY(targetY) && Math.Abs(point.Y - targetY) <= CursorSensitivity)
                return Cursors.SizeNS;

            double entryY = LineY(EntryAnchor, chartControl, chartPanel, chartScale);
            if (IsUsableY(entryY) && Math.Abs(point.Y - entryY) <= CursorSensitivity)
                return Cursors.SizeNS;

            return null;
        }

        public override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
        {
            if (chartControl == null || chartScale == null || EntryAnchor == null)
                return new Point[0];

            int panelIndex = chartScale.PanelIndex;
            if (panelIndex < 0 || panelIndex >= chartControl.ChartPanels.Count)
                return new Point[0];

            ChartPanel chartPanel = chartControl.ChartPanels[panelIndex];
            if (chartPanel == null)
                return new Point[0];

            List<Point> pts = new List<Point>();

            try
            {
                pts.Add(EntryAnchor.GetPoint(chartControl, chartPanel, chartScale));

                if (StopLive && StopAnchor != null)
                    pts.Add(StopAnchor.GetPoint(chartControl, chartPanel, chartScale));

                if (TargetLive && TargetAnchor != null)
                    pts.Add(TargetAnchor.GetPoint(chartControl, chartPanel, chartScale));
            }
            catch (ArgumentNullException)
            {
                // ChartScale can disappear between the initial guard and GetPoint.
                return new Point[0];
            }

            return pts.ToArray();
        }

        public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
        {
            return true;
        }

        public override void OnCalculateMinMax()
        {
            MinValue = double.MaxValue;
            MaxValue = double.MinValue;

            if (!IsVisible || EntryAnchor == null)
                return;

            MinValue = MaxValue = EntryAnchor.Price;

            if (StopLive)
            {
                MinValue = Math.Min(MinValue, StopAnchor.Price);
                MaxValue = Math.Max(MaxValue, StopAnchor.Price);
            }
            if (TargetLive)
            {
                MinValue = Math.Min(MinValue, TargetAnchor.Price);
                MaxValue = Math.Max(MaxValue, TargetAnchor.Price);
            }
        }

        public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (IsLocked || dataPoint == null || !HasChartContext(chartControl, chartPanel, chartScale))
                return;

            // A prior drag whose mouse-up never reached us (mouse released
            // outside the panel, capture lost, etc.) would otherwise leave a
            // stale in-progress edit hanging around forever. Starting a new
            // gesture always clears whatever the last one left behind first.
            if (draggingFromEntry || editingAnchor != null)
            {
                draggingFromEntry = false;
                editingAnchor     = null;
                DrawingState      = DrawingState.Normal;
                Log("cleared a stuck drag from an earlier gesture");
            }

            double y = dataPoint.GetPoint(chartControl, chartPanel, chartScale).Y;

            if (StopLive && Math.Abs(y - LineY(StopAnchor, chartControl, chartPanel, chartScale)) <= CursorSensitivity)
            {
                editingAnchor = StopAnchor;
                DrawingState  = DrawingState.Editing;
                return;
            }

            if (TargetLive && Math.Abs(y - LineY(TargetAnchor, chartControl, chartPanel, chartScale)) <= CursorSensitivity)
            {
                editingAnchor = TargetAnchor;
                DrawingState  = DrawingState.Editing;
                return;
            }

            if (Math.Abs(y - LineY(EntryAnchor, chartControl, chartPanel, chartScale)) <= CursorSensitivity)
            {
                draggingFromEntry = true;
                DrawingState      = DrawingState.Editing;
            }
        }

        public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (IsLocked || !IsVisible || dataPoint == null || !HasChartContext(chartControl, chartPanel, chartScale))
                return;

            if (draggingFromEntry)
            {
                bool above  = dataPoint.Price > EntryAnchor.Price;
                bool isLong = Side == MarketPosition.Long;

                dragResolvedIsStop = isLong ? !above : above;

                if (dragResolvedIsStop)
                    StopAnchor.Price = dataPoint.Price;
                else
                    TargetAnchor.Price = dataPoint.Price;
            }
            else if (editingAnchor != null)
            {
                editingAnchor.Price = dataPoint.Price;
            }
        }

        public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (dataPoint == null || !HasChartContext(chartControl, chartPanel, chartScale))
            {
                draggingFromEntry = false;
                editingAnchor     = null;
                DrawingState      = DrawingState.Normal;
                return;
            }

            if (draggingFromEntry)
            {
                draggingFromEntry = false;
                DrawingState      = DrawingState.Normal;

                if (TargetInstrument == null || TargetInstrument.MasterInstrument == null)
                    return;

                double rawPrice = dragResolvedIsStop ? StopAnchor.Price : TargetAnchor.Price;
                double price    = RoundPrice(rawPrice);
                double minDist  = TargetInstrument.MasterInstrument.TickSize * 2;

                if (Math.Abs(price - EntryAnchor.Price) < minDist)
                    return;

                if (dragResolvedIsStop)
                {
                    if (StopLive) ModifyLeg(true, price);
                    else          SubmitLeg(true, price);
                }
                else
                {
                    if (TargetLive) ModifyLeg(false, price);
                    else            SubmitLeg(false, price);
                }
                return;
            }

            if (editingAnchor != null)
            {
                DrawingState = DrawingState.Normal;
                double price = RoundPrice(editingAnchor.Price);

                if (editingAnchor == StopAnchor)
                    ModifyLeg(true, price);
                else if (editingAnchor == TargetAnchor)
                    ModifyLeg(false, price);

                editingAnchor = null;
            }
        }

        public override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (!IsVisible || EntryAnchor == null || chartControl == null || chartScale == null || RenderTarget == null)
                return;

            int panelIndex = PanelIndex;
            if (panelIndex < 0 || panelIndex >= chartControl.ChartPanels.Count)
                panelIndex = chartScale.PanelIndex;

            if (panelIndex < 0 || panelIndex >= chartControl.ChartPanels.Count)
                return;

            ChartPanel chartPanel = chartControl.ChartPanels[panelIndex];
            if (!HasChartContext(chartControl, chartPanel, chartScale))
                return;

            if (EntryStroke == null || StopStroke == null || TargetStroke == null)
                return;

            EntryStroke.RenderTarget  = RenderTarget;
            StopStroke.RenderTarget   = RenderTarget;
            TargetStroke.RenderTarget = RenderTarget;

            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;

            DrawFullWidthLine(EntryAnchor, EntryStroke, chartControl, chartPanel, chartScale, EntryLabel());

            if (StopLive)
                DrawFullWidthLine(StopAnchor, StopStroke, chartControl, chartPanel, chartScale, LegLabel(true));

            if (TargetLive)
                DrawFullWidthLine(TargetAnchor, TargetStroke, chartControl, chartPanel, chartScale, LegLabel(false));

            if (draggingFromEntry)
            {
                Stroke      previewStroke = dragResolvedIsStop ? StopStroke : TargetStroke;
                ChartAnchor previewAnchor = dragResolvedIsStop ? StopAnchor : TargetAnchor;
                DrawFullWidthLine(previewAnchor, previewStroke, chartControl, chartPanel, chartScale, LegLabel(dragResolvedIsStop));
            }
        }

        private string EntryLabel()
        {
            return string.Format("{0} {1} @ {2}", Side, Quantity, EntryAnchor.Price.ToString("0.####"));
        }

        private double SignedPriceMove(double price)
        {
            if (Side == MarketPosition.Long)
                return price - EntryAnchor.Price;

            if (Side == MarketPosition.Short)
                return EntryAnchor.Price - price;

            return 0;
        }

        private double EstimatedTicks(double price)
        {
            if (TargetInstrument == null || TargetInstrument.MasterInstrument == null || Quantity <= 0)
                return 0;

            double tickSize = TargetInstrument.MasterInstrument.TickSize;
            if (tickSize <= 0)
                return 0;

            double totalContractTicks = SignedPriceMove(price) / tickSize * Quantity;
            return Math.Round(totalContractTicks, 0, MidpointRounding.AwayFromZero);
        }

        private double EstimatedDollars(double price)
        {
            if (TargetInstrument == null || TargetInstrument.MasterInstrument == null || Quantity <= 0)
                return 0;

            return SignedPriceMove(price) * TargetInstrument.MasterInstrument.PointValue * Quantity;
        }

        private static string FormatSignedCurrency(double value)
        {
            if (value > 0)
                return "+$" + value.ToString("0.00", CultureInfo.InvariantCulture);

            if (value < 0)
                return "-$" + Math.Abs(value).ToString("0.00", CultureInfo.InvariantCulture);

            return "$0.00";
        }

        private static string FormatSignedTicks(double value)
        {
            return value.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " ticks";
        }

        private string EstimatedPnlText(double price)
        {
            return DisplayPnlInDollars
                ? FormatSignedCurrency(EstimatedDollars(price))
                : FormatSignedTicks(EstimatedTicks(price));
        }

        private string LegLabel(bool isStop)
        {
            double price = isStop ? StopAnchor.Price : TargetAnchor.Price;
            return string.Format(
                "{0} {1} @ {2} | EST {3}",
                isStop ? "STOP" : "TARGET",
                Quantity,
                price.ToString("0.####"),
                EstimatedPnlText(price));
        }

        private void DrawFullWidthLine(ChartAnchor anchor, Stroke stroke, ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, string label)
        {
            if (anchor == null || stroke == null || RenderTarget == null || !HasChartContext(chartControl, chartPanel, chartScale))
                return;

            Point p;
            try
            {
                p = anchor.GetPoint(chartControl, chartPanel, chartScale);
            }
            catch (ArgumentNullException)
            {
                return;
            }

            if (!IsUsableY(p.Y) || stroke.BrushDX == null)
                return;

            SharpDX.Vector2 start = new SharpDX.Vector2((float)chartPanel.X, (float)p.Y);
            SharpDX.Vector2 end   = new SharpDX.Vector2((float)(chartPanel.X + chartPanel.W), (float)p.Y);

            RenderTarget.DrawLine(start, end, stroke.BrushDX, stroke.Width, stroke.StrokeStyle);

            if (chartControl.Properties == null || Core.Globals.DirectWriteFactory == null)
                return;

            SimpleFont wpfFont = chartControl.Properties.LabelFont ?? new SimpleFont();
            using (SharpDX.DirectWrite.TextFormat textFormat = wpfFont.ToDirectWriteTextFormat())
            using (SharpDX.DirectWrite.TextLayout textLayout = new SharpDX.DirectWrite.TextLayout(
                Core.Globals.DirectWriteFactory, label ?? string.Empty, textFormat, chartPanel.W, textFormat.FontSize))
            {
                float textX = LabelsOnRight
                    ? (float)(chartPanel.X + chartPanel.W - textLayout.Metrics.Width - 4)
                    : (float)(chartPanel.X + 4);

                RenderTarget.DrawTextLayout(
                    new SharpDX.Vector2(textX, (float)(p.Y - textLayout.Metrics.Height - 2)),
                    textLayout,
                    stroke.BrushDX,
                    SharpDX.Direct2D1.DrawTextOptions.NoSnap);
            }
        }
    }

    public static partial class Draw
    {
        public static CrossBracketTool8 CrossBracket8(NinjaScriptBase owner, string tag, double entryPrice, Account account, Instrument instrument, int quantity, MarketPosition side, string ocoId, bool labelsOnRight, bool displayPnlInDollars, Brush entryBrush, Brush stopBrush, Brush targetBrush)
        {
            if (owner == null || string.IsNullOrEmpty(tag) || account == null || instrument == null || quantity <= 0)
                return null;

            CrossBracketTool8 tool = DrawingTool.GetByTagOrNew(owner, typeof(CrossBracketTool8), tag, null) as CrossBracketTool8;
            if (tool == null)
                return null;

            DrawingTool.SetDrawingToolCommonValues(tool, tag, true, owner, false);

            ChartAnchor anchor = DrawingTool.CreateChartAnchor(owner, 0, DateTime.Now, entryPrice);
            if (anchor == null || tool.EntryAnchor == null || tool.StopAnchor == null || tool.TargetAnchor == null)
                return null;

            anchor.CopyDataValues(tool.EntryAnchor);
            anchor.CopyDataValues(tool.StopAnchor);
            anchor.CopyDataValues(tool.TargetAnchor);

            tool.Activate(account, instrument, quantity, side, ocoId, labelsOnRight, displayPnlInDollars, entryBrush, stopBrush, targetBrush);

            tool.SetState(State.Active);
            return tool;
        }
    }
}
