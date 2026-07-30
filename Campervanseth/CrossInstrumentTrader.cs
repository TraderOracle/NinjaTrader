#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

public enum PanelCorner8
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public enum BracketLabelSide8
{
    Left,
    Right
}

public enum LimitClickMode8
{
    Neutral,
    LimitBuy,
    LimitSell
}

// This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
    // Drop this on the chart you use to read structure (e.g. a Mini tick chart).
    // Buy/Sell/Flatten route to whatever instrument/account you set below,
    // regardless of what instrument the chart itself is showing.
    public class CrossInstrumentTrader8 : Indicator
    {
        private Grid panel;
        private Grid quantityControl;

        private Button pnlButton;
        private Button closedPnlButton;
        private Button quantityButton;
        private Button quantityUpButton;
        private Button quantityDownButton;
        private Button limitModeButton;
        private Button buyButton;
        private Button sellButton;
        private Button flattenButton;
        private Button breakevenButton;
        private Button tickerButton;
        private Button clearButton;

        private Grid      dailyPnlGauge;
        private Border    gaugeNegativeFill;
        private Border    gaugePositiveFill;
        private Border    gaugeZeroMarker;
        private TextBlock gaugeLossLabel;
        private TextBlock gaugeZeroLabel;
        private TextBlock gaugeProfitLabel;

        private Account tradingAccount;
        private Instrument targetInstrument;
        private Instrument marketDataInstrument;
        private bool       marketDataSubscribed;
        private double     lastTargetPrice = double.NaN;
        private bool       showPnlInDollars;
        private double     closedDailyPnl;
        private double     displayedGaugePnl;
        private double     heldGaugePnl;
        private bool       holdGaugeAfterClose;

        // One-shot chart-click limit entry mode. The button cycles LMT -> LBY
        // -> LSL. A valid chart click submits one limit order and returns to LMT.
        private LimitClickMode8 limitClickMode = LimitClickMode8.Neutral;
        private bool            chartMouseSubscribed;
        private ChartControl    subscribedChartControl;

        // Daily risk lock. Once latched, it remains locked for the rest of the
        // chart Trading Hours session even if the final flatten fill slips back
        // inside the configured goal.
        private bool       dailyTradingLocked;
        private bool       dailyLockFlattenSent;
        private string     dailyLockReason = string.Empty;
        private DateTime   activeTradingDay = DateTime.MinValue;
        private SessionIterator sessionIterator;

        private readonly List<string> microInstruments = new List<string>();
        private int currentMicroIndex;

        // Contains only the fill arrows/text drawn during this indicator instance.
        // CLEAR removes these markers but does not flatten or cancel a live trade.
        private readonly HashSet<string> fillDrawTags = new HashSet<string>(StringComparer.Ordinal);

        // Tracks the horizontal chart line for each chart-click limit entry.
        // Order.Name is used because NinjaTrader can provide different Order
        // object instances for the same broker order during update events.
        private readonly Dictionary<string, string> limitOrderDrawTags = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, OrderAction> limitOrderActions = new Dictionary<string, OrderAction>(StringComparer.Ordinal);
        private readonly Dictionary<string, Order> limitOrders = new Dictionary<string, Order>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> limitOrderPrices = new Dictionary<string, double>(StringComparer.Ordinal);

        // Working limit lines can be grabbed and moved vertically. The visual
        // line previews the rounded price during the drag; releasing the mouse
        // submits Account.Change for the associated live order.
        private const double LimitLineDragSensitivityPixels = 10.0;
        private bool   isDraggingLimitLine;
        private string draggingLimitOrderName;
        private double draggingLimitOriginalPrice;
        private double draggingLimitPreviewPrice;

        private DateTime lastClickTime = DateTime.MinValue;
        private static readonly TimeSpan ClickCooldown = TimeSpan.FromMilliseconds(600);

        // Position state for the target instrument, tracked so we know when a
        // bracket cycle starts/ends and when quantity needs to be re-synced.
        private MarketPosition currentSide;
        private int            currentQty;
        private double         currentAvgPrice;
        private string         currentOcoId;

        private CrossBracketTool8 bracketTool;

        private static readonly SimpleFont LabelFont = new SimpleFont("Arial", 11);

        private void DrawText(string tag, string text, DateTime anchor, double y, Brush brush)
        {
            TextAlignment alignment = LabelsOnRight ? TextAlignment.Right : TextAlignment.Left;
            Draw.Text(this, tag, false, text, anchor, y, 0, brush, LabelFont, alignment, Brushes.Transparent, Brushes.Transparent, 0);
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description               = "Manual buy/sell/flatten buttons that route orders to a different instrument than the one on the chart (e.g. trade the Micro while reading the Mini's tick structure).";
                Name                      = "CrossInstrumentTrader";
                Calculate                 = Calculate.OnBarClose;
                IsOverlay                 = true;
                DisplayInDataBox          = false;
                PaintPriceMarkers         = false;
                IsSuspendedWhileInactive  = false;

                TargetInstrumentName = "MES 09-26";
                MicroInstrumentList  = "MES 09-26, MNQ 09-26, MYM 09-26, M2K 09-26";
                AccountName          = "Sim101";
                Quantity             = 1;
                ShowFillMarkers      = true;
                ShowFillText         = true;
                LabelsOnRight        = false;
                ButtonCorner         = PanelCorner8.TopLeft;
                BreakevenTicks       = 4;
                StartPnlInDollars     = true;

                DailyProfitGoal       = 1500;
                DailyLossGoal         = 1500;
                GaugeHeightPixels     = 14;
                GaugeFontSize         = 8;
                CloseAndLockAtDailyGoal = false;
                PositiveGaugeBrush     = Brushes.DarkGreen;
                NegativeGaugeBrush     = Brushes.DarkRed;
                ProfitGoalMetBrush     = Brushes.LimeGreen;
                LossGoalMetBrush       = Brushes.Red;

                LimitOrderLineBrush   = Brushes.Gold;

                BracketLabelSide      = BracketLabelSide8.Left;
                EntryLineBrush        = Brushes.DodgerBlue;
                StopLineBrush         = Brushes.OrangeRed;
                TargetLineBrush       = Brushes.MediumSeaGreen;
            }
            else if (State == State.DataLoaded)
            {
                showPnlInDollars = StartPnlInDollars;
                sessionIterator  = new SessionIterator(Bars);
                LoadMicroInstrumentList();
                ResolveAccountAndInstrument();
                RefreshClosedDailyPnl();
                displayedGaugePnl = closedDailyPnl;
                heldGaugePnl      = closedDailyPnl;
                SubscribeEvents();
                SubscribeTargetMarketData();
            }
            else if (State == State.Historical)
            {
                if (panel == null)
                    BuildPanel();

                SubscribeChartMouse();
            }
            else if (State == State.Terminated)
            {
                UnsubscribeChartMouse();
                UnsubscribeTargetMarketData();
                UnsubscribeEvents();
                TeardownPanel();
            }
        }

        protected override void OnBarUpdate()
        {
            if (sessionIterator == null || CurrentBar < 0)
                return;

            DateTime tradingDay;
            try
            {
                tradingDay = sessionIterator.GetTradingDay(Time[0]);
            }
            catch
            {
                return;
            }

            if (activeTradingDay == DateTime.MinValue)
            {
                activeTradingDay = tradingDay;
                return;
            }

            if (tradingDay == activeTradingDay)
                return;

            activeTradingDay = tradingDay;

            // Reset at the start of the chart's next Trading Hours session
            // (for futures this supports an ETH-day reset when an ETH template
            // is selected), rather than at midnight.
            if (State == State.Realtime)
                Dispatcher.InvokeAsync(() => ResetDailyTradingLock("new trading session"));
        }

        private void LoadMicroInstrumentList()
        {
            microInstruments.Clear();

            string raw = MicroInstrumentList ?? string.Empty;
            char[] separators = { ',', ';', '|', '\r', '\n' };

            foreach (string item in raw.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                string instrumentName = item.Trim();
                if (instrumentName.Length == 0)
                    continue;

                if (!microInstruments.Any(x => string.Equals(x, instrumentName, StringComparison.OrdinalIgnoreCase)))
                    microInstruments.Add(instrumentName);
            }

            // Keep the original Target Instrument usable even when it was not
            // included in the cycle list.
            if (!string.IsNullOrWhiteSpace(TargetInstrumentName)
                && !microInstruments.Any(x => string.Equals(x, TargetInstrumentName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                microInstruments.Insert(0, TargetInstrumentName.Trim());
            }

            if (microInstruments.Count == 0)
                microInstruments.Add("MES 09-26");

            currentMicroIndex = microInstruments.FindIndex(
                x => string.Equals(x, TargetInstrumentName, StringComparison.OrdinalIgnoreCase));

            if (currentMicroIndex < 0)
            {
                currentMicroIndex = 0;
                TargetInstrumentName = microInstruments[0];
            }
        }

        private void ResolveAccountAndInstrument()
        {
            tradingAccount = Account.All.FirstOrDefault(a => string.Equals(a.DisplayName, AccountName, StringComparison.OrdinalIgnoreCase));

            try
            {
                targetInstrument = Instrument.GetInstrument(TargetInstrumentName);
                if (targetInstrument == null)
                    Print("CrossInstrumentTrader: instrument '" + TargetInstrumentName + "' was not found.");
            }
            catch (Exception ex)
            {
                Print("CrossInstrumentTrader: could not resolve instrument '" + TargetInstrumentName + "': " + ex.Message);
                targetInstrument = null;
            }
        }

        private void SubscribeEvents()
        {
            if (tradingAccount == null)
                return;

            tradingAccount.PositionUpdate    += OnPositionUpdate;
            tradingAccount.ExecutionUpdate   += OnExecutionUpdate;
            tradingAccount.OrderUpdate       += OnOrderUpdate;
            tradingAccount.AccountItemUpdate += OnAccountItemUpdate;
        }

        private void UnsubscribeEvents()
        {
            if (tradingAccount == null)
                return;

            tradingAccount.PositionUpdate    -= OnPositionUpdate;
            tradingAccount.ExecutionUpdate   -= OnExecutionUpdate;
            tradingAccount.OrderUpdate       -= OnOrderUpdate;
            tradingAccount.AccountItemUpdate -= OnAccountItemUpdate;
        }

        private void RefreshClosedDailyPnl()
        {
            closedDailyPnl = 0;

            if (tradingAccount == null)
                return;

            try
            {
                double value = tradingAccount.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);
                if (value != double.MinValue && !double.IsNaN(value) && !double.IsInfinity(value))
                    closedDailyPnl = value;
            }
            catch (Exception ex)
            {
                Print("CrossInstrumentTrader closed PnL read error: " + ex.Message);
            }
        }

        private void OnAccountItemUpdate(object sender, AccountItemEventArgs e)
        {
            if (e == null || e.Account != tradingAccount || e.AccountItem != AccountItem.RealizedProfitLoss)
                return;

            double value = e.Value;
            if (value == double.MinValue || double.IsNaN(value) || double.IsInfinity(value))
                return;

            Dispatcher.InvokeAsync(() =>
            {
                closedDailyPnl = value;

                // Once the account posts the realized result for a completed
                // position, the live gauge settles to that value and remains
                // fixed until another position is opened.
                if (currentQty <= 0 || currentSide == MarketPosition.Flat)
                {
                    heldGaugePnl      = closedDailyPnl;
                    displayedGaugePnl = closedDailyPnl;
                    holdGaugeAfterClose = false;
                }

                UpdateClosedPnlDisplay();
                UpdateDailyPnlGauge();
            });
        }

        private void SubscribeTargetMarketData()
        {
            Instrument instrument = targetInstrument;
            if (instrument == null)
                return;

            marketDataInstrument = instrument;

            if (instrument.Dispatcher.HasShutdownStarted)
                return;

            instrument.Dispatcher.InvokeAsync(() =>
            {
                // The selected ticker may have changed before this queued action ran.
                if (marketDataInstrument != instrument || marketDataSubscribed)
                    return;

                instrument.MarketData.Update += OnTargetMarketData;
                marketDataSubscribed = true;

                MarketDataEventArgs snapshot = instrument.MarketData.Last;
                if (snapshot != null && snapshot.Price > 0)
                {
                    double snapshotPrice = snapshot.Price;
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (marketDataInstrument != instrument)
                            return;

                        lastTargetPrice = snapshotPrice;
                        UpdateOpenPnlDisplay();
                    });
                }
            });
        }

        private void UnsubscribeTargetMarketData()
        {
            Instrument instrument = marketDataInstrument;
            bool wasSubscribed    = marketDataSubscribed;

            marketDataInstrument = null;
            marketDataSubscribed = false;
            lastTargetPrice       = double.NaN;

            if (instrument == null || !wasSubscribed || instrument.Dispatcher.HasShutdownStarted)
                return;

            instrument.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    instrument.MarketData.Update -= OnTargetMarketData;
                }
                catch (Exception ex)
                {
                    Print("CrossInstrumentTrader market data unsubscribe error: " + ex.Message);
                }
            });
        }

        private void OnTargetMarketData(object sender, MarketDataEventArgs e)
        {
            Instrument instrument = marketDataInstrument;

            if (instrument == null || e == null || e.Instrument != instrument || e.MarketDataType != MarketDataType.Last)
                return;

            double price = e.Price;
            if (price <= 0)
                return;

            Dispatcher.InvokeAsync(() =>
            {
                if (marketDataInstrument != instrument)
                    return;

                lastTargetPrice = price;
                UpdateOpenPnlDisplay();
                UpdateDailyPnlGauge();
            });
        }

        private void OnPositionUpdate(object sender, PositionEventArgs e)
        {
            if (targetInstrument == null || e.Position == null || e.Position.Instrument != targetInstrument)
                return;

            Dispatcher.InvokeAsync(() => HandlePositionChanged(e.Position));
        }

        private void OnOrderUpdate(object sender, OrderEventArgs e)
        {
            if (e.Order == null || targetInstrument == null || e.Order.Instrument != targetInstrument)
                return;

            Dispatcher.InvokeAsync(() =>
            {
                bracketTool?.NotifyOrderUpdate(e.Order);
                HandleLimitOrderUpdate(e.Order);
            });
        }

        private static bool IsFinalOrderState(OrderState state)
        {
            return state == OrderState.Filled
                || state == OrderState.Cancelled
                || state == OrderState.Rejected;
        }

        private void DrawOrUpdateLimitOrderLine(string orderName, OrderAction action, double price)
        {
            if (string.IsNullOrEmpty(orderName) || price <= 0)
                return;

            string drawTag;
            if (!limitOrderDrawTags.TryGetValue(orderName, out drawTag))
            {
                drawTag = "XLimitLine8-" + orderName;
                limitOrderDrawTags[orderName] = drawTag;
            }

            limitOrderActions[orderName] = action;
            limitOrderPrices[orderName]  = price;

            Brush lineBrush = LimitOrderLineBrush ?? Brushes.Gold;
            Draw.HorizontalLine(this, drawTag, false, price, lineBrush, DashStyleHelper.Dash, 2);
            RefreshChart();
        }

        private void ReleaseLimitLineMouseCapture()
        {
            try
            {
                if (subscribedChartControl != null && Mouse.Captured == subscribedChartControl)
                    Mouse.Capture(null);
            }
            catch
            {
            }
        }

        private void ClearLimitLineDragState()
        {
            isDraggingLimitLine       = false;
            draggingLimitOrderName    = null;
            draggingLimitOriginalPrice = 0;
            draggingLimitPreviewPrice  = 0;
            ReleaseLimitLineMouseCapture();
        }

        private void CancelLimitLineDrag(bool restoreOriginalLine)
        {
            string orderName = draggingLimitOrderName;
            double originalPrice = draggingLimitOriginalPrice;

            ClearLimitLineDragState();

            if (!restoreOriginalLine || string.IsNullOrEmpty(orderName) || originalPrice <= 0)
                return;

            OrderAction action;
            if (limitOrderActions.TryGetValue(orderName, out action))
                DrawOrUpdateLimitOrderLine(orderName, action, originalPrice);
        }

        private void RemoveLimitOrderLine(string orderName)
        {
            if (string.IsNullOrEmpty(orderName))
                return;

            if (isDraggingLimitLine && string.Equals(draggingLimitOrderName, orderName, StringComparison.Ordinal))
                ClearLimitLineDragState();

            string drawTag;
            if (limitOrderDrawTags.TryGetValue(orderName, out drawTag))
            {
                RemoveDrawObject(drawTag);
                limitOrderDrawTags.Remove(orderName);
            }

            limitOrderActions.Remove(orderName);
            limitOrderPrices.Remove(orderName);
            limitOrders.Remove(orderName);
            RefreshChart();
        }

        private void HandleLimitOrderUpdate(Order order)
        {
            if (order == null || string.IsNullOrEmpty(order.Name))
                return;

            bool isTracked = limitOrderDrawTags.ContainsKey(order.Name);
            bool isOurLimit = order.Name.StartsWith("XLimitBuy-", StringComparison.Ordinal)
                || order.Name.StartsWith("XLimitSell-", StringComparison.Ordinal);

            if (!isTracked && !isOurLimit)
                return;

            // Always retain the newest Order instance. NinjaTrader can replace
            // the object reference between submission and later updates.
            limitOrders[order.Name] = order;

            if (IsFinalOrderState(order.OrderState))
            {
                RemoveLimitOrderLine(order.Name);
                return;
            }

            // Do not let a routine broker update snap the line back to its old
            // price while the user is actively previewing a drag.
            if (isDraggingLimitLine && string.Equals(draggingLimitOrderName, order.Name, StringComparison.Ordinal))
                return;

            double price = order.LimitPrice;
            if (price <= 0)
                return;

            OrderAction action;
            if (!limitOrderActions.TryGetValue(order.Name, out action))
                action = order.Name.StartsWith("XLimitBuy-", StringComparison.Ordinal) ? OrderAction.Buy : OrderAction.Sell;

            DrawOrUpdateLimitOrderLine(order.Name, action, price);
        }

        private void HandlePositionChanged(Position pos)
        {
            bool wasFlat = currentQty == 0;
            bool isFlat  = pos.MarketPosition == MarketPosition.Flat || pos.Quantity == 0;
            double gaugeBeforePositionUpdate = displayedGaugePnl;

            currentSide     = pos.MarketPosition;
            currentQty      = pos.Quantity;
            currentAvgPrice = pos.AveragePrice;

            if (wasFlat && !isFlat)
            {
                holdGaugeAfterClose = false;
                dailyLockFlattenSent = false;
                heldGaugePnl        = closedDailyPnl;
                currentOcoId = "XOCO-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                bracketTool = Draw.CrossBracket8(this, "XBracket8", currentAvgPrice, tradingAccount, targetInstrument, currentQty, currentSide, currentOcoId, BracketLabelSide == BracketLabelSide8.Right, showPnlInDollars, EntryLineBrush, StopLineBrush, TargetLineBrush);
                if (bracketTool != null)
                    bracketTool.Logged += msg => Print("CrossInstrumentTrader: " + msg);
            }
            else if (!wasFlat && !isFlat)
            {
                bracketTool?.UpdateEntry(currentAvgPrice, currentQty, currentSide);
            }
            else if (!wasFlat && isFlat)
            {
                // Freeze the last live closed + open reading while NinjaTrader
                // posts the final realized account update. This prevents the
                // gauge from snapping back to the pre-trade closed PnL.
                heldGaugePnl        = gaugeBeforePositionUpdate;
                displayedGaugePnl   = heldGaugePnl;
                holdGaugeAfterClose = true;

                currentQty = 0;
                dailyLockFlattenSent = false;
                bracketTool?.CancelAll();
                RemoveDrawObject("XBracket8");
                bracketTool  = null;
                currentOcoId = null;
            }

            UpdateOpenPnlDisplay();
            UpdateDailyPnlGauge();
            RefreshChart();
        }

        private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            if (targetInstrument == null || e.Execution == null || e.Execution.Instrument != targetInstrument)
                return;

            Execution exec = e.Execution;

            // Bracket leg resizing is driven directly off the fill itself
            // (authoritative and immediate) rather than the derived Position
            // event, which can race the filled order's own Filled field.
            // This must run regardless of the fill-marker display toggles.
            Dispatcher.InvokeAsync(() =>
            {
                if (bracketTool == null)
                    return;

                if (bracketTool.IsStopOrder(exec.Order))
                    bracketTool.NotifyLegFilled(true, exec.Quantity);
                else if (bracketTool.IsTargetOrder(exec.Order))
                    bracketTool.NotifyLegFilled(false, exec.Quantity);
                else
                    bracketTool.GrowLegs(exec.Quantity);
            });

            Print(string.Format("CrossInstrumentTrader: fill {0} {1} {2} @ {3}", exec.MarketPosition, exec.Quantity, TargetInstrumentName, exec.Price));

            if (!ShowFillMarkers)
                return;

            bool   isBuy = exec.MarketPosition == MarketPosition.Long;
            Brush  brush = isBuy ? Brushes.Lime : Brushes.Red;
            string tag   = "XFill-" + exec.ExecutionId;
            string label = exec.Quantity + " @ " + exec.Price.ToString("0.####");

            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    fillDrawTags.Add(tag);

                    if (isBuy)
                    {
                        Draw.ArrowUp(this, tag, true, exec.Time, exec.Price, brush);
                        if (ShowFillText)
                        {
                            string textTag = tag + "-txt";
                            fillDrawTags.Add(textTag);
                            DrawText(textTag, label, exec.Time, exec.Price - 10 * TickSize, brush);
                        }
                    }
                    else
                    {
                        Draw.ArrowDown(this, tag, true, exec.Time, exec.Price, brush);
                        if (ShowFillText)
                        {
                            string textTag = tag + "-txt";
                            fillDrawTags.Add(textTag);
                            DrawText(textTag, label, exec.Time, exec.Price + 10 * TickSize, brush);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Print("CrossInstrumentTrader draw error: " + ex.Message);
                }
            });
        }

        private void TogglePnlMode()
        {
            showPnlInDollars = !showPnlInDollars;
            bracketTool?.SetPnlDisplayMode(showPnlInDollars);
            UpdateOpenPnlDisplay();
            RefreshChart();
        }

        private void RefreshChart()
        {
            if (ChartControl != null)
                ChartControl.InvalidateVisual();
        }

        private double SignedPriceMove(double price)
        {
            if (currentSide == MarketPosition.Long)
                return price - currentAvgPrice;

            if (currentSide == MarketPosition.Short)
                return currentAvgPrice - price;

            return 0;
        }

        private double OpenPnlTicks(double price)
        {
            if (targetInstrument == null || targetInstrument.MasterInstrument == null || currentQty <= 0)
                return 0;

            double tickSize = targetInstrument.MasterInstrument.TickSize;
            if (tickSize <= 0)
                return 0;

            double totalContractTicks = SignedPriceMove(price) / tickSize * currentQty;
            return Math.Round(totalContractTicks, 0, MidpointRounding.AwayFromZero);
        }

        private double OpenPnlDollars(double price)
        {
            if (targetInstrument == null || targetInstrument.MasterInstrument == null || currentQty <= 0)
                return 0;

            return SignedPriceMove(price) * targetInstrument.MasterInstrument.PointValue * currentQty;
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
            return value.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " t";
        }

        private void UpdateOpenPnlDisplay()
        {
            if (pnlButton == null)
                return;

            bool hasOpenPosition = currentQty > 0
                && currentSide != MarketPosition.Flat
                && currentAvgPrice > 0
                && !double.IsNaN(lastTargetPrice)
                && !double.IsInfinity(lastTargetPrice)
                && lastTargetPrice > 0;

            double pnl = 0;

            if (hasOpenPosition)
                pnl = showPnlInDollars ? OpenPnlDollars(lastTargetPrice) : OpenPnlTicks(lastTargetPrice);

            pnlButton.Content = "OPEN " + (showPnlInDollars ? FormatSignedCurrency(pnl) : FormatSignedTicks(pnl));
            pnlButton.Background = pnl > 0
                ? Brushes.DarkGreen
                : pnl < 0
                    ? Brushes.DarkRed
                    : Brushes.DimGray;

            pnlButton.ToolTip = showPnlInDollars
                ? "Open PnL in dollars - click to show total contract ticks"
                : "Open PnL in total contract ticks - click to show dollars";
        }

        private void UpdateClosedPnlDisplay()
        {
            if (closedPnlButton == null)
                return;

            closedPnlButton.Content = "CLOSED " + FormatSignedCurrency(closedDailyPnl);

            if (closedDailyPnl >= Math.Max(1.0, DailyProfitGoal))
                closedPnlButton.Background = ProfitGoalMetBrush ?? Brushes.LimeGreen;
            else if (closedDailyPnl <= -Math.Max(1.0, DailyLossGoal))
                closedPnlButton.Background = LossGoalMetBrush ?? Brushes.Red;
            else if (closedDailyPnl > 0)
                closedPnlButton.Background = PositiveGaugeBrush ?? Brushes.DarkGreen;
            else if (closedDailyPnl < 0)
                closedPnlButton.Background = NegativeGaugeBrush ?? Brushes.DarkRed;
            else
                closedPnlButton.Background = Brushes.DimGray;

            closedPnlButton.ToolTip = "Selected account realized PnL (after commissions)";
        }

        private static string FormatGoalCurrency(double value, bool positive)
        {
            string prefix = positive ? "+$" : "-$";
            return prefix + Math.Abs(value).ToString("N0", CultureInfo.InvariantCulture);
        }

        private bool HasLiveOpenPnl()
        {
            return currentQty > 0
                && currentSide != MarketPosition.Flat
                && currentAvgPrice > 0
                && !double.IsNaN(lastTargetPrice)
                && !double.IsInfinity(lastTargetPrice)
                && lastTargetPrice > 0;
        }

        private double GetGaugePnlValue()
        {
            if (HasLiveOpenPnl())
            {
                // Live daily total: realized account PnL plus the open PnL of
                // the position managed by this trader panel.
                displayedGaugePnl = closedDailyPnl + OpenPnlDollars(lastTargetPrice);
                heldGaugePnl      = displayedGaugePnl;
                holdGaugeAfterClose = false;
                return displayedGaugePnl;
            }

            displayedGaugePnl = holdGaugeAfterClose ? heldGaugePnl : closedDailyPnl;
            return displayedGaugePnl;
        }

        private void ResetDailyTradingLock(string reason)
        {
            dailyTradingLocked  = false;
            dailyLockFlattenSent = false;
            dailyLockReason      = string.Empty;

            RefreshClosedDailyPnl();
            holdGaugeAfterClose = false;
            heldGaugePnl        = closedDailyPnl;
            displayedGaugePnl   = closedDailyPnl;

            UpdateTradingLockVisualState();
            UpdateClosedPnlDisplay();
            UpdateDailyPnlGauge();

            Print("CrossInstrumentTrader: daily trading lock reset for " + reason + ".");
        }

        private void UpdateTradingLockVisualState()
        {
            bool enabled = !dailyTradingLocked;

            if (buyButton != null)
            {
                buyButton.IsEnabled = enabled;
                buyButton.ToolTip = dailyTradingLocked
                    ? "Trading locked: " + dailyLockReason
                    : "Submit a market buy order";
            }

            if (sellButton != null)
            {
                sellButton.IsEnabled = enabled;
                sellButton.ToolTip = dailyTradingLocked
                    ? "Trading locked: " + dailyLockReason
                    : "Submit a market sell order";
            }

            if (limitModeButton != null)
            {
                limitModeButton.IsEnabled = enabled;

                if (dailyTradingLocked)
                {
                    limitClickMode = LimitClickMode8.Neutral;
                    UpdateLimitModeButton();
                    limitModeButton.ToolTip = "Trading locked: " + dailyLockReason;
                }
            }
        }

        private void FlattenForDailyLock()
        {
            if (dailyLockFlattenSent || currentQty <= 0 || currentSide == MarketPosition.Flat)
                return;

            if (tradingAccount == null || targetInstrument == null)
                return;

            dailyLockFlattenSent = true;

            try
            {
                // Cancel the custom protective legs first, then flatten the
                // selected instrument. Account.Flatten also handles any other
                // working orders for that instrument.
                bracketTool?.CancelAll();
                tradingAccount.Flatten(new[] { targetInstrument });
                Print("CrossInstrumentTrader: daily goal reached; flatten sent for "
                    + TargetInstrumentName + " on " + AccountName + ".");
            }
            catch (Exception ex)
            {
                dailyLockFlattenSent = false;
                Print("CrossInstrumentTrader daily-lock flatten error: " + ex.Message);
            }
        }

        private void EvaluateDailyTradingLock(double gaugePnl)
        {
            if (!CloseAndLockAtDailyGoal)
            {
                UpdateTradingLockVisualState();
                return;
            }

            double profitGoal = Math.Max(1.0, DailyProfitGoal);
            double lossGoal   = Math.Max(1.0, DailyLossGoal);

            if (!dailyTradingLocked)
            {
                bool profitHit = gaugePnl >= profitGoal;
                bool lossHit   = gaugePnl <= -lossGoal;

                if (profitHit || lossHit)
                {
                    dailyTradingLocked = true;
                    dailyLockFlattenSent = false;
                    dailyLockReason = profitHit
                        ? "daily profit goal reached (" + FormatSignedCurrency(gaugePnl) + ")"
                        : "daily loss limit reached (" + FormatSignedCurrency(gaugePnl) + ")";

                    // Preserve the exact trigger value while the flatten is
                    // being processed. The realized account event will settle
                    // the gauge after the closing fill posts.
                    displayedGaugePnl = gaugePnl;
                    heldGaugePnl      = gaugePnl;
                    holdGaugeAfterClose = true;

                    Print("CrossInstrumentTrader: TRADING LOCKED - " + dailyLockReason + ".");
                }
            }

            UpdateTradingLockVisualState();

            if (dailyTradingLocked)
                FlattenForDailyLock();
        }

        private void UpdateDailyPnlGauge()
        {
            double gaugePnl = GetGaugePnlValue();
            EvaluateDailyTradingLock(gaugePnl);

            if (dailyPnlGauge == null || gaugeNegativeFill == null || gaugePositiveFill == null)
                return;

            double lossGoal    = Math.Max(1.0, DailyLossGoal);
            double profitGoal  = Math.Max(1.0, DailyProfitGoal);
            double usableWidth = Math.Max(0.0, dailyPnlGauge.ActualWidth - 1.0);
            double totalGoal   = lossGoal + profitGoal;
            double lossWidth   = usableWidth * lossGoal / totalGoal;
            double profitWidth = usableWidth - lossWidth;

            double negativeFraction = gaugePnl < 0
                ? Math.Min(1.0, Math.Abs(gaugePnl) / lossGoal)
                : 0.0;
            double positiveFraction = gaugePnl > 0
                ? Math.Min(1.0, gaugePnl / profitGoal)
                : 0.0;

            gaugeNegativeFill.Width = lossWidth * negativeFraction;
            gaugePositiveFill.Width = profitWidth * positiveFraction;

            gaugeNegativeFill.Background = gaugePnl <= -lossGoal
                ? (LossGoalMetBrush ?? Brushes.Red)
                : (NegativeGaugeBrush ?? Brushes.DarkRed);
            gaugePositiveFill.Background = gaugePnl >= profitGoal
                ? (ProfitGoalMetBrush ?? Brushes.LimeGreen)
                : (PositiveGaugeBrush ?? Brushes.DarkGreen);

            if (gaugeLossLabel != null)
                gaugeLossLabel.Text = FormatGoalCurrency(lossGoal, false);
            if (gaugeZeroLabel != null)
                gaugeZeroLabel.Text = "$0";
            if (gaugeProfitLabel != null)
                gaugeProfitLabel.Text = FormatGoalCurrency(profitGoal, true);

            string gaugeDescription = HasLiveOpenPnl()
                ? "LIVE DAILY PnL (closed + open): " + FormatSignedCurrency(gaugePnl)
                : "DAILY CLOSED PnL: " + FormatSignedCurrency(gaugePnl);

            dailyPnlGauge.ToolTip = dailyTradingLocked
                ? gaugeDescription + " | TRADING LOCKED: " + dailyLockReason
                : gaugeDescription;
        }

        private Button CreatePanelButton(string text, Brush background, double minWidth)
        {
            return new Button
            {
                Content                    = text,
                Background                 = background,
                Foreground                 = Brushes.White,
                FontSize                   = 10,
                FontWeight                 = FontWeights.SemiBold,
                Padding                    = new Thickness(4, 0, 4, 0),
                Margin                     = new Thickness(1),
                Height                     = 24,
                MinWidth                   = minWidth,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment   = VerticalAlignment.Center
            };
        }

        private Grid CreateDailyPnlGauge()
        {
            double lossWeight   = Math.Max(1.0, DailyLossGoal);
            double profitWeight = Math.Max(1.0, DailyProfitGoal);

            Grid gauge = new Grid
            {
                Height              = GaugeHeightPixels,
                Margin              = new Thickness(1, 0, 1, 1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ClipToBounds        = true
            };

            gauge.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(lossWeight, GridUnitType.Star) });
            gauge.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Pixel) });
            gauge.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(profitWeight, GridUnitType.Star) });

            Border background = new Border
            {
                Background      = Brushes.Black,
                BorderBrush     = Brushes.DimGray,
                BorderThickness = new Thickness(1),
                Opacity         = 0.55
            };
            Grid.SetColumn(background, 0);
            Grid.SetColumnSpan(background, 3);
            gauge.Children.Add(background);

            gaugeNegativeFill = new Border
            {
                Background          = NegativeGaugeBrush ?? Brushes.DarkRed,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Stretch,
                Width               = 0,
                Opacity             = 0.85
            };
            Grid.SetColumn(gaugeNegativeFill, 0);
            gauge.Children.Add(gaugeNegativeFill);

            gaugePositiveFill = new Border
            {
                Background          = PositiveGaugeBrush ?? Brushes.DarkGreen,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment   = VerticalAlignment.Stretch,
                Width               = 0,
                Opacity             = 0.85
            };
            Grid.SetColumn(gaugePositiveFill, 2);
            gauge.Children.Add(gaugePositiveFill);

            gaugeZeroMarker = new Border
            {
                Background        = Brushes.Gainsboro,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetColumn(gaugeZeroMarker, 1);
            gauge.Children.Add(gaugeZeroMarker);

            gaugeLossLabel = new TextBlock
            {
                Foreground          = Brushes.White,
                FontSize            = GaugeFontSize,
                Margin              = new Thickness(3, 0, 1, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment   = VerticalAlignment.Center,
                IsHitTestVisible    = false
            };
            Grid.SetColumn(gaugeLossLabel, 0);
            gauge.Children.Add(gaugeLossLabel);

            gaugeZeroLabel = new TextBlock
            {
                Foreground          = Brushes.White,
                FontSize            = GaugeFontSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                IsHitTestVisible    = false
            };
            Grid.SetColumn(gaugeZeroLabel, 1);
            gauge.Children.Add(gaugeZeroLabel);

            gaugeProfitLabel = new TextBlock
            {
                Foreground          = Brushes.White,
                FontSize            = GaugeFontSize,
                Margin              = new Thickness(1, 0, 3, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Center,
                IsHitTestVisible    = false
            };
            Grid.SetColumn(gaugeProfitLabel, 2);
            gauge.Children.Add(gaugeProfitLabel);

            gauge.SizeChanged += (s, e) => UpdateDailyPnlGauge();
            return gauge;
        }

        private Grid CreateQuantityControl()
        {
            Grid grid = new Grid
            {
                Margin = new Thickness(1),
                Height = 24
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());

            quantityButton = CreatePanelButton("QTY " + Quantity, Brushes.DimGray, 48);
            quantityButton.Margin = new Thickness(0);
            quantityButton.Height = double.NaN;

            quantityUpButton = CreatePanelButton("▲", Brushes.Gray, 18);
            quantityUpButton.Margin   = new Thickness(1, 0, 0, 0);
            quantityUpButton.Padding  = new Thickness(0);
            quantityUpButton.Height   = double.NaN;
            quantityUpButton.FontSize = 8;
            quantityUpButton.Click   += (s, e) => AdjustQuantity(1);

            quantityDownButton = CreatePanelButton("▼", Brushes.Gray, 18);
            quantityDownButton.Margin   = new Thickness(1, 1, 0, 0);
            quantityDownButton.Padding  = new Thickness(0);
            quantityDownButton.Height   = double.NaN;
            quantityDownButton.FontSize = 8;
            quantityDownButton.Click   += (s, e) => AdjustQuantity(-1);

            Grid.SetColumn(quantityButton, 0);
            Grid.SetRow(quantityButton, 0);
            Grid.SetRowSpan(quantityButton, 2);

            Grid.SetColumn(quantityUpButton, 1);
            Grid.SetRow(quantityUpButton, 0);

            Grid.SetColumn(quantityDownButton, 1);
            Grid.SetRow(quantityDownButton, 1);

            grid.Children.Add(quantityButton);
            grid.Children.Add(quantityUpButton);
            grid.Children.Add(quantityDownButton);

            return grid;
        }

        private void ToggleLimitMode()
        {
            if (CloseAndLockAtDailyGoal && dailyTradingLocked)
            {
                limitClickMode = LimitClickMode8.Neutral;
                UpdateLimitModeButton();
                Print("CrossInstrumentTrader: limit entry blocked - " + dailyLockReason + ".");
                return;
            }

            switch (limitClickMode)
            {
                case LimitClickMode8.Neutral:
                    limitClickMode = LimitClickMode8.LimitBuy;
                    break;
                case LimitClickMode8.LimitBuy:
                    limitClickMode = LimitClickMode8.LimitSell;
                    break;
                default:
                    limitClickMode = LimitClickMode8.Neutral;
                    break;
            }

            UpdateLimitModeButton();
        }

        private void ResetLimitMode()
        {
            limitClickMode = LimitClickMode8.Neutral;
            UpdateLimitModeButton();
        }

        private void UpdateLimitModeButton()
        {
            if (limitModeButton == null)
                return;

            switch (limitClickMode)
            {
                case LimitClickMode8.LimitBuy:
                    limitModeButton.Content    = "LBY";
                    limitModeButton.Background = Brushes.LightGreen;
                    limitModeButton.Foreground = Brushes.Black;
                    limitModeButton.ToolTip    = "Limit Buy armed - click once on the price chart";
                    break;

                case LimitClickMode8.LimitSell:
                    limitModeButton.Content    = "LSL";
                    limitModeButton.Background = Brushes.LightCoral;
                    limitModeButton.Foreground = Brushes.Black;
                    limitModeButton.ToolTip    = "Limit Sell armed - click once on the price chart";
                    break;

                default:
                    limitModeButton.Content    = "LMT";
                    limitModeButton.Background = Brushes.DimGray;
                    limitModeButton.Foreground = Brushes.White;
                    limitModeButton.ToolTip    = "Click to arm LBY, click again for LSL; a chart click submits one limit order";
                    break;
            }
        }

        private void SubscribeChartMouse()
        {
            ChartControl chartControl = ChartControl;
            if (chartControl == null)
                return;

            chartControl.Dispatcher.InvokeAsync(() =>
            {
                if (chartMouseSubscribed)
                    return;

                chartControl.PreviewMouseLeftButtonDown += OnChartMouseLeftButtonDown;
                chartControl.PreviewMouseMove           += OnChartMouseMove;
                chartControl.PreviewMouseLeftButtonUp   += OnChartMouseLeftButtonUp;
                chartControl.LostMouseCapture           += OnChartLostMouseCapture;
                subscribedChartControl = chartControl;
                chartMouseSubscribed   = true;
            });
        }

        private void UnsubscribeChartMouse()
        {
            ChartControl chartControl = subscribedChartControl ?? ChartControl;
            chartMouseSubscribed = false;

            if (chartControl == null || chartControl.Dispatcher.HasShutdownStarted)
            {
                subscribedChartControl = null;
                return;
            }

            chartControl.Dispatcher.InvokeAsync(() =>
            {
                if (isDraggingLimitLine)
                    CancelLimitLineDrag(false);

                chartControl.PreviewMouseLeftButtonDown -= OnChartMouseLeftButtonDown;
                chartControl.PreviewMouseMove           -= OnChartMouseMove;
                chartControl.PreviewMouseLeftButtonUp   -= OnChartMouseLeftButtonUp;
                chartControl.LostMouseCapture           -= OnChartLostMouseCapture;

                if (subscribedChartControl == chartControl)
                    subscribedChartControl = null;
            });
        }

        private bool ClickCameFromPanelControl(object originalSource)
        {
            if (panel != null && panel.IsMouseOver)
                return true;

            DependencyObject current = originalSource as DependencyObject;

            while (current != null)
            {
                if (current == panel || current is Button)
                    return true;

                try
                {
                    current = VisualTreeHelper.GetParent(current);
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private ChartPanel GetPriceChartPanel(Point clickPoint)
        {
            if (ChartControl == null || ChartControl.ChartPanels == null || ChartControl.ChartPanels.Count == 0)
                return null;

            int panelIndex = PanelUI;
            if (panelIndex < 0 || panelIndex >= ChartControl.ChartPanels.Count)
                panelIndex = 0;

            ChartPanel chartPanel = ChartControl.ChartPanels[panelIndex];
            if (chartPanel == null)
                return null;

            bool insidePanel = clickPoint.X >= chartPanel.X
                && clickPoint.X <= chartPanel.X + chartPanel.W
                && clickPoint.Y >= chartPanel.Y
                && clickPoint.Y <= chartPanel.Y + chartPanel.H;

            return insidePanel ? chartPanel : null;
        }

        private ChartScale GetPriceChartScale(ChartPanel chartPanel)
        {
            if (chartPanel == null || chartPanel.Scales == null)
                return null;

            ChartScale firstScale   = null;
            ChartScale visibleScale = null;

            try
            {
                foreach (ChartScale scale in chartPanel.Scales)
                {
                    if (scale == null)
                        continue;

                    if (firstScale == null)
                        firstScale = scale;

                    if (visibleScale == null && scale.IsVisible)
                        visibleScale = scale;

                    if (scale.ScaleJustification == this.ScaleJustification)
                        return scale;
                }
            }
            catch
            {
            }

            return visibleScale ?? firstScale;
        }

        private double RoundLimitOrderPrice(string orderName, double rawPrice)
        {
            if (double.IsNaN(rawPrice) || double.IsInfinity(rawPrice) || rawPrice <= 0)
                return 0;

            Order order;
            Instrument instrument = limitOrders.TryGetValue(orderName ?? string.Empty, out order) && order != null
                ? order.Instrument
                : targetInstrument;

            if (instrument == null || instrument.MasterInstrument == null)
                return rawPrice;

            return instrument.MasterInstrument.RoundToTickSize(rawPrice);
        }

        private bool TryBeginLimitOrderDrag(Point clickPoint, ChartScale chartScale)
        {
            if ((CloseAndLockAtDailyGoal && dailyTradingLocked) || chartScale == null || limitOrderPrices.Count == 0)
                return false;

            string nearestOrderName = null;
            double nearestDistance  = double.MaxValue;
            double nearestPrice     = 0;

            foreach (KeyValuePair<string, double> item in limitOrderPrices.ToArray())
            {
                if (item.Value <= 0)
                    continue;

                Order order;
                if (!limitOrders.TryGetValue(item.Key, out order) || order == null || IsFinalOrderState(order.OrderState))
                    continue;

                double lineY;
                try
                {
                    lineY = chartScale.GetYByValue(item.Value);
                }
                catch
                {
                    continue;
                }

                double distance = Math.Abs(clickPoint.Y - lineY);
                if (distance < nearestDistance)
                {
                    nearestDistance  = distance;
                    nearestOrderName = item.Key;
                    nearestPrice     = item.Value;
                }
            }

            if (string.IsNullOrEmpty(nearestOrderName) || nearestDistance > LimitLineDragSensitivityPixels)
                return false;

            isDraggingLimitLine        = true;
            draggingLimitOrderName     = nearestOrderName;
            draggingLimitOriginalPrice = nearestPrice;
            draggingLimitPreviewPrice  = nearestPrice;

            try
            {
                if (ChartControl != null)
                    Mouse.Capture(ChartControl, CaptureMode.Element);
            }
            catch
            {
            }

            Print("CrossInstrumentTrader: dragging working limit " + nearestOrderName + ".");
            return true;
        }

        private bool UpdateLimitOrderDragPreview(Point mousePoint)
        {
            if (!isDraggingLimitLine || ChartControl == null || string.IsNullOrEmpty(draggingLimitOrderName))
                return false;

            ChartPanel chartPanel = GetPriceChartPanel(mousePoint);
            if (chartPanel == null)
                return false;

            ChartScale chartScale = GetPriceChartScale(chartPanel);
            if (chartScale == null)
                return false;

            double rawPrice;
            try
            {
                rawPrice = chartScale.GetValueByYWpf(mousePoint.Y);
            }
            catch
            {
                return false;
            }

            double roundedPrice = RoundLimitOrderPrice(draggingLimitOrderName, rawPrice);
            if (roundedPrice <= 0)
                return false;

            draggingLimitPreviewPrice = roundedPrice;

            OrderAction action;
            if (!limitOrderActions.TryGetValue(draggingLimitOrderName, out action))
                action = draggingLimitOrderName.StartsWith("XLimitBuy-", StringComparison.Ordinal)
                    ? OrderAction.Buy
                    : OrderAction.Sell;

            DrawOrUpdateLimitOrderLine(draggingLimitOrderName, action, roundedPrice);
            return true;
        }

        private void CommitLimitOrderDrag()
        {
            if (!isDraggingLimitLine || string.IsNullOrEmpty(draggingLimitOrderName))
                return;

            string orderName   = draggingLimitOrderName;
            double oldPrice    = draggingLimitOriginalPrice;
            double newPrice    = draggingLimitPreviewPrice;

            Order order;
            limitOrders.TryGetValue(orderName, out order);

            ClearLimitLineDragState();

            if (order == null || IsFinalOrderState(order.OrderState) || newPrice <= 0)
            {
                if (oldPrice > 0)
                {
                    OrderAction restoreAction;
                    if (limitOrderActions.TryGetValue(orderName, out restoreAction))
                        DrawOrUpdateLimitOrderLine(orderName, restoreAction, oldPrice);
                }
                return;
            }

            if (Math.Abs(newPrice - oldPrice) < 0.0000001)
                return;

            if (CloseAndLockAtDailyGoal && dailyTradingLocked)
            {
                OrderAction restoreAction;
                if (limitOrderActions.TryGetValue(orderName, out restoreAction) && oldPrice > 0)
                    DrawOrUpdateLimitOrderLine(orderName, restoreAction, oldPrice);
                return;
            }

            if (tradingAccount == null)
            {
                OrderAction restoreAction;
                if (limitOrderActions.TryGetValue(orderName, out restoreAction) && oldPrice > 0)
                    DrawOrUpdateLimitOrderLine(orderName, restoreAction, oldPrice);
                return;
            }

            try
            {
                order.LimitPriceChanged = newPrice;
                tradingAccount.Change(new[] { order });

                Print(string.Format(
                    CultureInfo.InvariantCulture,
                    "CrossInstrumentTrader: moved working limit {0} from {1} to {2}.",
                    orderName,
                    oldPrice,
                    newPrice));
            }
            catch (Exception ex)
            {
                double brokerPrice = order.LimitPrice > 0 ? order.LimitPrice : oldPrice;
                OrderAction restoreAction;
                if (limitOrderActions.TryGetValue(orderName, out restoreAction) && brokerPrice > 0)
                    DrawOrUpdateLimitOrderLine(orderName, restoreAction, brokerPrice);

                Print("CrossInstrumentTrader limit move error: " + ex.Message);
            }
        }

        private void OnChartMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e == null || ChartControl == null)
                return;

            // A new gesture clears any drag whose mouse-up was missed.
            if (isDraggingLimitLine)
                CancelLimitLineDrag(true);

            // Never interpret clicks on the trader panel itself as price clicks.
            if (ClickCameFromPanelControl(e.OriginalSource))
                return;

            Point clickPoint = e.GetPosition(ChartControl);
            ChartPanel chartPanel = GetPriceChartPanel(clickPoint);
            if (chartPanel == null)
                return;

            ChartScale chartScale = GetPriceChartScale(chartPanel);
            if (chartScale == null)
                return;

            // In neutral LMT mode, a click close to a working limit line begins
            // an order drag instead of placing a new order.
            if (limitClickMode == LimitClickMode8.Neutral)
            {
                if (TryBeginLimitOrderDrag(clickPoint, chartScale))
                    e.Handled = true;
                return;
            }

            LimitClickMode8 requestedMode = limitClickMode;

            try
            {
                double rawPrice = chartScale.GetValueByYWpf(clickPoint.Y);
                if (double.IsNaN(rawPrice) || double.IsInfinity(rawPrice) || rawPrice <= 0)
                    return;

                // Consume the chart click so it does not also select or begin
                // moving another chart object while the limit tool is armed.
                e.Handled = true;

                OrderAction action = requestedMode == LimitClickMode8.LimitBuy
                    ? OrderAction.Buy
                    : OrderAction.Sell;

                SubmitLimitOrder(action, rawPrice);
            }
            finally
            {
                // One-shot behavior prevents an accidental second order.
                ResetLimitMode();
            }
        }

        private void OnChartMouseMove(object sender, MouseEventArgs e)
        {
            if (!isDraggingLimitLine || e == null || ChartControl == null)
                return;

            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            if (UpdateLimitOrderDragPreview(e.GetPosition(ChartControl)))
                e.Handled = true;
        }

        private void OnChartMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isDraggingLimitLine || e == null || ChartControl == null)
                return;

            UpdateLimitOrderDragPreview(e.GetPosition(ChartControl));
            e.Handled = true;
            CommitLimitOrderDrag();
        }

        private void OnChartLostMouseCapture(object sender, MouseEventArgs e)
        {
            if (isDraggingLimitLine)
                CancelLimitLineDrag(true);
        }

        private void SubmitLimitOrder(OrderAction action, double rawPrice)
        {
            if (CloseAndLockAtDailyGoal && dailyTradingLocked)
            {
                Print("CrossInstrumentTrader: limit entry blocked - " + dailyLockReason + ".");
                UpdateTradingLockVisualState();
                return;
            }

            ResolveAccountAndInstrument();
            UpdatePanelLabels();

            if (tradingAccount == null)
            {
                Print("CrossInstrumentTrader: account '" + AccountName + "' not found.");
                return;
            }

            if (targetInstrument == null || targetInstrument.MasterInstrument == null)
            {
                Print("CrossInstrumentTrader: instrument '" + TargetInstrumentName + "' not found.");
                return;
            }

            double limitPrice = targetInstrument.MasterInstrument.RoundToTickSize(rawPrice);
            if (limitPrice <= 0)
                return;

            string orderName = null;

            try
            {
                string orderPrefix = action == OrderAction.Buy ? "XLimitBuy-" : "XLimitSell-";
                orderName = orderPrefix + Guid.NewGuid().ToString("N").Substring(0, 8);

                Order order = tradingAccount.CreateOrder(
                    targetInstrument,
                    action,
                    OrderType.Limit,
                    OrderEntry.Manual,
                    TimeInForce.Day,
                    Quantity,
                    limitPrice,
                    0,
                    string.Empty,
                    orderName,
                    Globals.MaxDate,
                    null);

                // Retain the live order object so the visible line can later be
                // dragged and changed through Account.Change().
                limitOrders[orderName] = order;

                // Draw immediately so the user can see the selected level while
                // the broker/order adapter processes the submission. The line is
                // then kept synchronized by OrderUpdate and removed at a final state.
                DrawOrUpdateLimitOrderLine(orderName, action, limitPrice);
                tradingAccount.Submit(new[] { order });

                Print(string.Format(
                    "CrossInstrumentTrader: submitted {0} LIMIT {1} {2} @ {3} on {4}",
                    action,
                    Quantity,
                    TargetInstrumentName,
                    limitPrice,
                    AccountName));
            }
            catch (Exception ex)
            {
                RemoveLimitOrderLine(orderName);
                Print("CrossInstrumentTrader limit order error: " + ex.Message);
            }
        }

        private void BuildPanel()
        {
            Dispatcher.InvokeAsync(() =>
            {
                HorizontalAlignment hAlign = (ButtonCorner == PanelCorner8.TopRight || ButtonCorner == PanelCorner8.BottomRight)
                    ? HorizontalAlignment.Right
                    : HorizontalAlignment.Left;
                VerticalAlignment vAlign = (ButtonCorner == PanelCorner8.BottomLeft || ButtonCorner == PanelCorner8.BottomRight)
                    ? VerticalAlignment.Bottom
                    : VerticalAlignment.Top;

                panel = new Grid
                {
                    Name                 = "CrossInstrumentTrader8Panel",
                    HorizontalAlignment  = hAlign,
                    VerticalAlignment    = vAlign,
                    Margin               = new Thickness(3)
                };

                panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                for (int i = 0; i < 10; i++)
                    panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                pnlButton = CreatePanelButton("OPEN $0.00", Brushes.DimGray, 88);
                pnlButton.Click += (s, e) => TogglePnlMode();

                closedPnlButton = CreatePanelButton("CLOSED $0.00", Brushes.DimGray, 96);
                closedPnlButton.Focusable        = false;
                closedPnlButton.IsHitTestVisible = false;

                quantityControl = CreateQuantityControl();

                limitModeButton = CreatePanelButton("LMT", Brushes.DimGray, 36);
                limitModeButton.Click += (s, e) => ToggleLimitMode();

                buyButton = CreatePanelButton("BUY", Brushes.DarkGreen, 38);
                buyButton.Click += (s, e) => SubmitOrder(OrderAction.Buy);

                sellButton = CreatePanelButton("SELL", Brushes.DarkRed, 38);
                sellButton.Click += (s, e) => SubmitOrder(OrderAction.Sell);

                flattenButton = CreatePanelButton("FLATTEN", Brushes.SlateGray, 52);
                flattenButton.Click += (s, e) => Flatten();

                breakevenButton = CreatePanelButton("BE+", Brushes.DarkGoldenrod, 34);
                breakevenButton.Click += (s, e) => SetBreakeven();

                tickerButton = CreatePanelButton(GetTickerDisplayName(), Brushes.SteelBlue, 42);
                tickerButton.ToolTip = "Ticker - click to cycle through Micro instruments";
                tickerButton.Click += (s, e) => CycleTicker();

                clearButton = CreatePanelButton("CLEAR", Brushes.DimGray, 42);
                clearButton.ToolTip = "Remove prior fill arrows and fill text from the chart";
                clearButton.Click += (s, e) => ClearTrades();

                Grid.SetRow(pnlButton, 0);
                Grid.SetColumn(pnlButton, 0);
                Grid.SetRow(closedPnlButton, 0);
                Grid.SetColumn(closedPnlButton, 1);
                Grid.SetRow(quantityControl, 0);
                Grid.SetColumn(quantityControl, 2);
				Grid.SetRow(tickerButton, 0);
                Grid.SetColumn(tickerButton, 3);
                Grid.SetRow(limitModeButton, 0);
                Grid.SetColumn(limitModeButton, 4);
                Grid.SetRow(buyButton, 0);
                Grid.SetColumn(buyButton, 5);
                Grid.SetRow(sellButton, 0);
                Grid.SetColumn(sellButton, 6);
                Grid.SetRow(flattenButton, 0);
                Grid.SetColumn(flattenButton, 7);
                Grid.SetRow(breakevenButton, 0);
                Grid.SetColumn(breakevenButton, 8);
                Grid.SetRow(clearButton, 0);
                Grid.SetColumn(clearButton, 9);

                panel.Children.Add(pnlButton);
                panel.Children.Add(closedPnlButton);
                panel.Children.Add(quantityControl);
                panel.Children.Add(limitModeButton);
                panel.Children.Add(buyButton);
                panel.Children.Add(sellButton);
                panel.Children.Add(flattenButton);
                panel.Children.Add(breakevenButton);
                panel.Children.Add(tickerButton);
                panel.Children.Add(clearButton);

                dailyPnlGauge = CreateDailyPnlGauge();
                Grid.SetRow(dailyPnlGauge, 1);
                Grid.SetColumn(dailyPnlGauge, 0);
                Grid.SetColumnSpan(dailyPnlGauge, 10);
                panel.Children.Add(dailyPnlGauge);

                UserControlCollection.Add(panel);
                UpdatePanelLabels();
            });
        }

        private void TeardownPanel()
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (panel != null && UserControlCollection.Contains(panel))
                    UserControlCollection.Remove(panel);

                panel              = null;
                pnlButton          = null;
                closedPnlButton    = null;
                dailyPnlGauge      = null;
                gaugeNegativeFill  = null;
                gaugePositiveFill  = null;
                gaugeZeroMarker    = null;
                gaugeLossLabel     = null;
                gaugeZeroLabel     = null;
                gaugeProfitLabel   = null;
                quantityControl    = null;
                quantityButton     = null;
                quantityUpButton   = null;
                quantityDownButton = null;
                limitModeButton    = null;
                buyButton          = null;
                sellButton         = null;
                flattenButton      = null;
                breakevenButton    = null;
                tickerButton       = null;
                clearButton        = null;
            });
        }

        private void UpdatePanelLabels()
        {
            UpdateOpenPnlDisplay();
            UpdateClosedPnlDisplay();
            UpdateDailyPnlGauge();
            UpdateLimitModeButton();
            UpdateTradingLockVisualState();

            if (quantityButton != null)
                quantityButton.Content = "QTY " + Quantity;

            if (tickerButton != null)
            {
                tickerButton.Content = GetTickerDisplayName();
                tickerButton.ToolTip = "Ticker: " + TargetInstrumentName + " - click to cycle";
            }
        }

        private string GetTickerDisplayName()
        {
            if (targetInstrument != null && targetInstrument.MasterInstrument != null)
                return targetInstrument.MasterInstrument.Name;

            if (string.IsNullOrWhiteSpace(TargetInstrumentName))
                return "TICKER";

            string[] parts = TargetInstrumentName.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : "TICKER";
        }

        private void AdjustQuantity(int change)
        {
            int newQuantity = Quantity + change;
            if (newQuantity < 1)
                newQuantity = 1;

            Quantity = newQuantity;
            UpdatePanelLabels();
            Print("CrossInstrumentTrader: order quantity set to " + Quantity);
        }

        private void CycleTicker()
        {
            ResetLimitMode();

            // Do not abandon management of an active position by switching the
            // target instrument underneath its live bracket.
            if (currentQty > 0 || bracketTool != null)
            {
                Print("CrossInstrumentTrader: flatten the current " + TargetInstrumentName + " position before changing ticker.");
                return;
            }

            if (microInstruments.Count == 0)
                LoadMicroInstrumentList();

            if (microInstruments.Count <= 1)
            {
                Print("CrossInstrumentTrader: add more instruments to the Micro instrument cycle setting.");
                return;
            }

            currentMicroIndex++;
            if (currentMicroIndex >= microInstruments.Count)
                currentMicroIndex = 0;

            UnsubscribeTargetMarketData();
            TargetInstrumentName = microInstruments[currentMicroIndex];

            currentSide     = MarketPosition.Flat;
            currentQty      = 0;
            currentAvgPrice = 0;
            currentOcoId    = null;

            ResolveAccountAndInstrument();
            SubscribeTargetMarketData();
            UpdatePanelLabels();

            Print("CrossInstrumentTrader: target instrument changed to " + TargetInstrumentName);
        }

        private void ClearTrades()
        {
            string[] tags = fillDrawTags.ToArray();
            foreach (string tag in tags)
                RemoveDrawObject(tag);

            fillDrawTags.Clear();
            Print("CrossInstrumentTrader: cleared prior fill markers from the chart.");
        }

        private void SubmitOrder(OrderAction action)
        {
            if (CloseAndLockAtDailyGoal && dailyTradingLocked)
            {
                Print("CrossInstrumentTrader: entry blocked - " + dailyLockReason + ".");
                UpdateTradingLockVisualState();
                return;
            }

            if ((DateTime.Now - lastClickTime) < ClickCooldown)
                return;
            lastClickTime = DateTime.Now;

            ResolveAccountAndInstrument();
            UpdatePanelLabels();

            if (tradingAccount == null)
            {
                Print("CrossInstrumentTrader: account '" + AccountName + "' not found.");
                return;
            }
            if (targetInstrument == null)
            {
                Print("CrossInstrumentTrader: instrument '" + TargetInstrumentName + "' not found.");
                return;
            }

            try
            {
                Order order = tradingAccount.CreateOrder(
                    targetInstrument,
                    action,
                    OrderType.Market,
                    OrderEntry.Manual,
                    TimeInForce.Day,
                    Quantity,
                    0, 0,
                    string.Empty,
                    "XTrade-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    Globals.MaxDate,
                    null);

                tradingAccount.Submit(new[] { order });
                Print(string.Format("CrossInstrumentTrader: submitted {0} {1} {2} on {3}", action, Quantity, TargetInstrumentName, AccountName));
            }
            catch (Exception ex)
            {
                Print("CrossInstrumentTrader order error: " + ex.Message);
            }
        }

        private void Flatten()
        {
            if ((DateTime.Now - lastClickTime) < ClickCooldown)
                return;
            lastClickTime = DateTime.Now;

            ResolveAccountAndInstrument();
            UpdatePanelLabels();

            if (tradingAccount == null || targetInstrument == null)
                return;

            try
            {
                tradingAccount.Flatten(new[] { targetInstrument });
                Print("CrossInstrumentTrader: flatten sent for " + TargetInstrumentName + " on " + AccountName);
            }
            catch (Exception ex)
            {
                Print("CrossInstrumentTrader flatten error: " + ex.Message);
            }
        }

        private void SetBreakeven()
        {
            if ((DateTime.Now - lastClickTime) < ClickCooldown)
                return;
            lastClickTime = DateTime.Now;

            if (bracketTool == null)
            {
                Print("CrossInstrumentTrader: no open position to set breakeven on.");
                return;
            }

            bracketTool.SetBreakeven(BreakevenTicks);
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Target instrument", Order = 1, GroupName = "Cross Trade")]
        public string TargetInstrumentName { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Micro instrument cycle (comma separated)", Order = 2, GroupName = "Cross Trade")]
        public string MicroInstrumentList { get; set; }

        [NinjaScriptProperty]
        [TypeConverter(typeof(AccountDisplayNameConverter))]
        [Display(Name = "Account", Order = 3, GroupName = "Cross Trade")]
        public string AccountName { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Quantity", Order = 4, GroupName = "Cross Trade")]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show fill markers on chart", Order = 5, GroupName = "Cross Trade")]
        public bool ShowFillMarkers { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show price/qty text on fill markers", Order = 6, GroupName = "Cross Trade")]
        public bool ShowFillText { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Fill marker labels on right side", Order = 7, GroupName = "Cross Trade")]
        public bool LabelsOnRight { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Button panel corner", Order = 8, GroupName = "Cross Trade")]
        public PanelCorner8 ButtonCorner { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Breakeven+ ticks", Order = 9, GroupName = "Cross Trade")]
        public int BreakevenTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Start PnL display in dollars", Order = 10, GroupName = "Cross Trade")]
        public bool StartPnlInDollars { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, double.MaxValue)]
        [Display(Name = "Daily profit goal ($)", Order = 1, GroupName = "Daily PnL")]
        public double DailyProfitGoal { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, double.MaxValue)]
        [Display(Name = "Daily loss goal ($)", Order = 2, GroupName = "Daily PnL")]
        public double DailyLossGoal { get; set; }

        [NinjaScriptProperty]
        [Range(4, 40)]
        [Display(Name = "Gauge height (pixels)", Order = 3, GroupName = "Daily PnL")]
        public int GaugeHeightPixels { get; set; }

        [NinjaScriptProperty]
        [Range(6, 20)]
        [Display(Name = "Gauge font size", Order = 4, GroupName = "Daily PnL")]
        public int GaugeFontSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Close position and lock trading at goal", Order = 5, GroupName = "Daily PnL")]
        public bool CloseAndLockAtDailyGoal { get; set; }

        [XmlIgnore]
        [Display(Name = "Positive PnL color", Order = 6, GroupName = "Daily PnL")]
        public Brush PositiveGaugeBrush { get; set; }

        [Browsable(false)]
        public string PositiveGaugeBrushSerialize
        {
            get { return Serialize.BrushToString(PositiveGaugeBrush); }
            set { PositiveGaugeBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Negative PnL color", Order = 7, GroupName = "Daily PnL")]
        public Brush NegativeGaugeBrush { get; set; }

        [Browsable(false)]
        public string NegativeGaugeBrushSerialize
        {
            get { return Serialize.BrushToString(NegativeGaugeBrush); }
            set { NegativeGaugeBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Profit goal met color", Order = 8, GroupName = "Daily PnL")]
        public Brush ProfitGoalMetBrush { get; set; }

        [Browsable(false)]
        public string ProfitGoalMetBrushSerialize
        {
            get { return Serialize.BrushToString(ProfitGoalMetBrush); }
            set { ProfitGoalMetBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Loss goal met color", Order = 9, GroupName = "Daily PnL")]
        public Brush LossGoalMetBrush { get; set; }

        [Browsable(false)]
        public string LossGoalMetBrushSerialize
        {
            get { return Serialize.BrushToString(LossGoalMetBrush); }
            set { LossGoalMetBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Working/draggable limit line color", Order = 1, GroupName = "Limit Entry")]
        public Brush LimitOrderLineBrush { get; set; }

        [Browsable(false)]
        public string LimitOrderLineBrushSerialize
        {
            get { return Serialize.BrushToString(LimitOrderLineBrush); }
            set { LimitOrderLineBrush = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Information side", Order = 1, GroupName = "Bracket")]
        public BracketLabelSide8 BracketLabelSide { get; set; }

        [XmlIgnore]
        [Display(Name = "Entry line color", Order = 2, GroupName = "Bracket")]
        public Brush EntryLineBrush { get; set; }

        [Browsable(false)]
        public string EntryLineBrushSerialize
        {
            get { return Serialize.BrushToString(EntryLineBrush); }
            set { EntryLineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Stop line color", Order = 3, GroupName = "Bracket")]
        public Brush StopLineBrush { get; set; }

        [Browsable(false)]
        public string StopLineBrushSerialize
        {
            get { return Serialize.BrushToString(StopLineBrush); }
            set { StopLineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Target line color", Order = 4, GroupName = "Bracket")]
        public Brush TargetLineBrush { get; set; }

        [Browsable(false)]
        public string TargetLineBrushSerialize
        {
            get { return Serialize.BrushToString(TargetLineBrush); }
            set { TargetLineBrush = Serialize.StringToBrush(value); }
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private CrossInstrumentTrader8[] cacheCrossInstrumentTrader8;
		public CrossInstrumentTrader8 CrossInstrumentTrader8(string targetInstrumentName, string microInstrumentList, string accountName, int quantity, bool showFillMarkers, bool showFillText, bool labelsOnRight, PanelCorner8 buttonCorner, int breakevenTicks, bool startPnlInDollars, double dailyProfitGoal, double dailyLossGoal, int gaugeHeightPixels, int gaugeFontSize, bool closeAndLockAtDailyGoal, BracketLabelSide8 bracketLabelSide)
		{
			return CrossInstrumentTrader8(Input, targetInstrumentName, microInstrumentList, accountName, quantity, showFillMarkers, showFillText, labelsOnRight, buttonCorner, breakevenTicks, startPnlInDollars, dailyProfitGoal, dailyLossGoal, gaugeHeightPixels, gaugeFontSize, closeAndLockAtDailyGoal, bracketLabelSide);
		}

		public CrossInstrumentTrader8 CrossInstrumentTrader8(ISeries<double> input, string targetInstrumentName, string microInstrumentList, string accountName, int quantity, bool showFillMarkers, bool showFillText, bool labelsOnRight, PanelCorner8 buttonCorner, int breakevenTicks, bool startPnlInDollars, double dailyProfitGoal, double dailyLossGoal, int gaugeHeightPixels, int gaugeFontSize, bool closeAndLockAtDailyGoal, BracketLabelSide8 bracketLabelSide)
		{
			if (cacheCrossInstrumentTrader8 != null)
				for (int idx = 0; idx < cacheCrossInstrumentTrader8.Length; idx++)
					if (cacheCrossInstrumentTrader8[idx] != null && cacheCrossInstrumentTrader8[idx].TargetInstrumentName == targetInstrumentName && cacheCrossInstrumentTrader8[idx].MicroInstrumentList == microInstrumentList && cacheCrossInstrumentTrader8[idx].AccountName == accountName && cacheCrossInstrumentTrader8[idx].Quantity == quantity && cacheCrossInstrumentTrader8[idx].ShowFillMarkers == showFillMarkers && cacheCrossInstrumentTrader8[idx].ShowFillText == showFillText && cacheCrossInstrumentTrader8[idx].LabelsOnRight == labelsOnRight && cacheCrossInstrumentTrader8[idx].ButtonCorner == buttonCorner && cacheCrossInstrumentTrader8[idx].BreakevenTicks == breakevenTicks && cacheCrossInstrumentTrader8[idx].StartPnlInDollars == startPnlInDollars && cacheCrossInstrumentTrader8[idx].DailyProfitGoal == dailyProfitGoal && cacheCrossInstrumentTrader8[idx].DailyLossGoal == dailyLossGoal && cacheCrossInstrumentTrader8[idx].GaugeHeightPixels == gaugeHeightPixels && cacheCrossInstrumentTrader8[idx].GaugeFontSize == gaugeFontSize && cacheCrossInstrumentTrader8[idx].CloseAndLockAtDailyGoal == closeAndLockAtDailyGoal && cacheCrossInstrumentTrader8[idx].BracketLabelSide == bracketLabelSide && cacheCrossInstrumentTrader8[idx].EqualsInput(input))
						return cacheCrossInstrumentTrader8[idx];
			return CacheIndicator<CrossInstrumentTrader8>(new CrossInstrumentTrader8(){ TargetInstrumentName = targetInstrumentName, MicroInstrumentList = microInstrumentList, AccountName = accountName, Quantity = quantity, ShowFillMarkers = showFillMarkers, ShowFillText = showFillText, LabelsOnRight = labelsOnRight, ButtonCorner = buttonCorner, BreakevenTicks = breakevenTicks, StartPnlInDollars = startPnlInDollars, DailyProfitGoal = dailyProfitGoal, DailyLossGoal = dailyLossGoal, GaugeHeightPixels = gaugeHeightPixels, GaugeFontSize = gaugeFontSize, CloseAndLockAtDailyGoal = closeAndLockAtDailyGoal, BracketLabelSide = bracketLabelSide }, input, ref cacheCrossInstrumentTrader8);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.CrossInstrumentTrader8 CrossInstrumentTrader8(string targetInstrumentName, string microInstrumentList, string accountName, int quantity, bool showFillMarkers, bool showFillText, bool labelsOnRight, PanelCorner8 buttonCorner, int breakevenTicks, bool startPnlInDollars, double dailyProfitGoal, double dailyLossGoal, int gaugeHeightPixels, int gaugeFontSize, bool closeAndLockAtDailyGoal, BracketLabelSide8 bracketLabelSide)
		{
			return indicator.CrossInstrumentTrader8(Input, targetInstrumentName, microInstrumentList, accountName, quantity, showFillMarkers, showFillText, labelsOnRight, buttonCorner, breakevenTicks, startPnlInDollars, dailyProfitGoal, dailyLossGoal, gaugeHeightPixels, gaugeFontSize, closeAndLockAtDailyGoal, bracketLabelSide);
		}

		public Indicators.CrossInstrumentTrader8 CrossInstrumentTrader8(ISeries<double> input , string targetInstrumentName, string microInstrumentList, string accountName, int quantity, bool showFillMarkers, bool showFillText, bool labelsOnRight, PanelCorner8 buttonCorner, int breakevenTicks, bool startPnlInDollars, double dailyProfitGoal, double dailyLossGoal, int gaugeHeightPixels, int gaugeFontSize, bool closeAndLockAtDailyGoal, BracketLabelSide8 bracketLabelSide)
		{
			return indicator.CrossInstrumentTrader8(input, targetInstrumentName, microInstrumentList, accountName, quantity, showFillMarkers, showFillText, labelsOnRight, buttonCorner, breakevenTicks, startPnlInDollars, dailyProfitGoal, dailyLossGoal, gaugeHeightPixels, gaugeFontSize, closeAndLockAtDailyGoal, bracketLabelSide);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.CrossInstrumentTrader8 CrossInstrumentTrader8(string targetInstrumentName, string microInstrumentList, string accountName, int quantity, bool showFillMarkers, bool showFillText, bool labelsOnRight, PanelCorner8 buttonCorner, int breakevenTicks, bool startPnlInDollars, double dailyProfitGoal, double dailyLossGoal, int gaugeHeightPixels, int gaugeFontSize, bool closeAndLockAtDailyGoal, BracketLabelSide8 bracketLabelSide)
		{
			return indicator.CrossInstrumentTrader8(Input, targetInstrumentName, microInstrumentList, accountName, quantity, showFillMarkers, showFillText, labelsOnRight, buttonCorner, breakevenTicks, startPnlInDollars, dailyProfitGoal, dailyLossGoal, gaugeHeightPixels, gaugeFontSize, closeAndLockAtDailyGoal, bracketLabelSide);
		}

		public Indicators.CrossInstrumentTrader8 CrossInstrumentTrader8(ISeries<double> input , string targetInstrumentName, string microInstrumentList, string accountName, int quantity, bool showFillMarkers, bool showFillText, bool labelsOnRight, PanelCorner8 buttonCorner, int breakevenTicks, bool startPnlInDollars, double dailyProfitGoal, double dailyLossGoal, int gaugeHeightPixels, int gaugeFontSize, bool closeAndLockAtDailyGoal, BracketLabelSide8 bracketLabelSide)
		{
			return indicator.CrossInstrumentTrader8(input, targetInstrumentName, microInstrumentList, accountName, quantity, showFillMarkers, showFillText, labelsOnRight, buttonCorner, breakevenTicks, startPnlInDollars, dailyProfitGoal, dailyLossGoal, gaugeHeightPixels, gaugeFontSize, closeAndLockAtDailyGoal, bracketLabelSide);
		}
	}
}

#endregion
