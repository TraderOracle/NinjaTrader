#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
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
using NinjaTrader.NinjaScript.DrawingTools;
using System.Linq;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.Staging
{
    public class Domdicator_Staging2 : Indicator
    {
        #region Variables
        private readonly object orderLock = new object();
        private Dictionary<double, OrderInfo> renderBidOrders = new Dictionary<double, OrderInfo>();
        private Dictionary<double, OrderInfo> renderAskOrders = new Dictionary<double, OrderInfo>();
        private Dictionary<double, long> volumesByPriceRange = new Dictionary<double, long>();
        private int priceRangeTicks = 5;
        private int volumeThresholdPercent = 20;
        private float historicalOpacity = 0.6f;
        private int marginRight = 300;
        private int maxRightExtension = 225;
        private float maxTextSize = 28;
        private float minTextSize = 14;
        private Brush bidBrush;
        private Brush askBrush;
        private Brush textBrush;
        private long maxVolume = 0;
        private BarAlignment alignment = BarAlignment.Right;
        private double currentBidPrice;
        private double currentAskPrice;
        #endregion

        #region Properties
        [NinjaScriptProperty]
        [Range(200, 600)]
        [Display(Name = "DOM Panel Width", Description = "Width in pixels of the DOM visualization panel on the right side of the chart", Order = 1, GroupName = "Visual Settings")]
        public int MarginRight
        {
            get { return marginRight; }
            set { marginRight = value; }
        }

        [NinjaScriptProperty]
        [Range(50, 400)]
        [Display(Name = "Maximum Bar Width", Description = "Maximum width in pixels that volume bars can extend from the right side", Order = 2, GroupName = "Visual Settings")]
        public int MaxRightExtension
        {
            get { return maxRightExtension; }
            set { maxRightExtension = value; }
        }

        [NinjaScriptProperty]
        [Range(8, 24)]
        [Display(Name = "Volume Text Max Size", Description = "Maximum font size for volume numbers (used for largest volumes)", Order = 3, GroupName = "Visual Settings")]
        public float MaxTextSize
        {
            get { return maxTextSize; }
            set { maxTextSize = value; }
        }

        [NinjaScriptProperty]
        [Range(6, 16)]
        [Display(Name = "Volume Text Min Size", Description = "Minimum font size for volume numbers (used for smallest volumes)", Order = 4, GroupName = "Visual Settings")]
        public float MinTextSize
        {
            get { return minTextSize; }
            set { minTextSize = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Show Volume Numbers", Description = "Display numerical volume values next to the bars", Order = 5, GroupName = "Visual Settings")]
        public bool ShowVolumeText
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Price Level Group Size", Description = "Number of price ticks to group together when displaying volume text (higher values reduce text overlap)", Order = 6, GroupName = "Visual Settings")]
        public int PriceRangeTicks
        {
            get { return priceRangeTicks; }
            set { priceRangeTicks = Math.Max(1, Math.Min(20, value)); }
        }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Volume Display Threshold", Description = "Only show volumes that are above this percentage of the maximum volume (reduces visual clutter)", Order = 7, GroupName = "Visual Settings")]
        public int VolumeThresholdPercent
        {
            get { return volumeThresholdPercent; }
            set { volumeThresholdPercent = Math.Max(1, Math.Min(100, value)); }
        }

        [NinjaScriptProperty]
        [Range(10, 90)]
        [Display(Name = "Historical Orders Opacity", Description = "Opacity percentage for orders away from the current bid/ask price (lower values make historical orders more transparent)", Order = 8, GroupName = "Visual Settings")]
        public float HistoricalOpacity
        {
            get { return historicalOpacity * 100; }
            set { historicalOpacity = Math.Max(0.1f, Math.Min(0.9f, value / 100f)); }
        }

        [XmlIgnore]
        [Display(Name = "Bid Volume Color", Description = "Color used for bid volume bars", Order = 9, GroupName = "Visual Settings")]
        public Brush BidBrush
        {
            get { return bidBrush; }
            set { bidBrush = value; }
        }

        [Browsable(false)]
        public string BidBrushSerializable
        {
            get { return Serialize.BrushToString(bidBrush); }
            set { bidBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Ask Volume Color", Description = "Color used for ask volume bars", Order = 10, GroupName = "Visual Settings")]
        public Brush AskBrush
        {
            get { return askBrush; }
            set { askBrush = value; }
        }

        [Browsable(false)]
        public string AskBrushSerializable
        {
            get { return Serialize.BrushToString(askBrush); }
            set { askBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Volume Text Color", Description = "Color used for volume numbers", Order = 11, GroupName = "Visual Settings")]
        public Brush TextBrush
        {
            get { return textBrush; }
            set { textBrush = value; }
        }

        [Browsable(false)]
        public string TextBrushSerializable
        {
            get { return Serialize.BrushToString(textBrush); }
            set { textBrush = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Volume Bar Alignment", Description = "Align volume bars to the left, right, or center of the DOM panel", Order = 12, GroupName = "Visual Settings")]
        public BarAlignment Alignment
        {
            get { return alignment; }
            set { alignment = value; }
        }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "DOM visualization indicator with relative sizing (Staging2 Version)";
                Name = "Domdicator_Staging2";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = false;

                ShowVolumeText = true;
                MarginRight = 300;
                MaxRightExtension = 150;
                MaxTextSize = 14;
                MinTextSize = 10;
                PriceRangeTicks = 10;
                VolumeThresholdPercent = 60;
                HistoricalOpacity = 90;
                BidBrush = Brushes.Red;
                AskBrush = Brushes.LawnGreen;
                TextBrush = Brushes.White;
                Alignment = BarAlignment.Right;
                currentBidPrice = double.MinValue;
                currentAskPrice = double.MaxValue;
            }
            else if (State == State.Configure)
            {
                if (ChartControl != null)
                    ChartControl.Properties.BarMarginRight = marginRight;
            }
        }

        protected override void OnBarUpdate()
        {
            ForceRefresh();
        }

        protected override void OnMarketDepth(MarketDepthEventArgs marketDepthUpdate)
        {
            lock (orderLock)
            {
                if (marketDepthUpdate.Operation == Operation.Remove || marketDepthUpdate.Volume <= 0)
                {
                    if (marketDepthUpdate.MarketDataType == MarketDataType.Ask)
                        renderAskOrders.Remove(marketDepthUpdate.Price);
                    else if (marketDepthUpdate.MarketDataType == MarketDataType.Bid)
                        renderBidOrders.Remove(marketDepthUpdate.Price);
                }
                else if (marketDepthUpdate.Operation == Operation.Add || marketDepthUpdate.Operation == Operation.Update)
                {
                    var orderInfo = new OrderInfo
                    {
                        Price = marketDepthUpdate.Price,
                        Volume = marketDepthUpdate.Volume,
                        Time = marketDepthUpdate.Time,
                        IsHistorical = marketDepthUpdate.MarketDataType == MarketDataType.Ask ? 
                            (currentAskPrice != double.MaxValue && marketDepthUpdate.Price > currentAskPrice) :
                            (currentBidPrice != double.MinValue && marketDepthUpdate.Price < currentBidPrice)
                    };

                    if (marketDepthUpdate.MarketDataType == MarketDataType.Ask)
                        renderAskOrders[marketDepthUpdate.Price] = orderInfo;
                    else if (marketDepthUpdate.MarketDataType == MarketDataType.Bid)
                        renderBidOrders[marketDepthUpdate.Price] = orderInfo;
                }

                UpdateRenderCollections();
            }

            ForceRefresh();
        }

        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            bool priceChanged = false;
            double oldBid = currentBidPrice;
            double oldAsk = currentAskPrice;

            if (marketDataUpdate.MarketDataType == MarketDataType.Bid && currentBidPrice != marketDataUpdate.Price)
            {
                currentBidPrice = marketDataUpdate.Price;
                priceChanged = true;
            }
            else if (marketDataUpdate.MarketDataType == MarketDataType.Ask && currentAskPrice != marketDataUpdate.Price)
            {
                currentAskPrice = marketDataUpdate.Price;
                priceChanged = true;
            }

            if (priceChanged)
            {
                lock (orderLock)
                {
                    double safeBid = currentBidPrice;
                    double safeAsk = currentAskPrice;
                    
                    List<double> invalidKeys = renderBidOrders
                        .Where(o => safeBid != double.MinValue && o.Key > safeBid)
                        .Select(o => o.Key)
                        .ToList();
                        
                    foreach (double key in invalidKeys)
                        renderBidOrders.Remove(key);
                        
                    invalidKeys = renderAskOrders
                        .Where(o => safeAsk != double.MaxValue && o.Key < safeAsk)
                        .Select(o => o.Key)
                        .ToList();
                        
                    foreach (double key in invalidKeys)
                        renderAskOrders.Remove(key);

                    List<double> bidKeys = renderBidOrders.Keys.ToList();
                    foreach (var key in bidKeys)
                    {
                        var order = renderBidOrders[key];
                        order.IsHistorical = (key < safeBid);
                        renderBidOrders[key] = order;
                    }

                    List<double> askKeys = renderAskOrders.Keys.ToList();
                    foreach (var key in askKeys)
                    {
                        var order = renderAskOrders[key];
                        order.IsHistorical = (key > safeAsk);
                        renderAskOrders[key] = order;
                    }

                    UpdateRenderCollections();
                }

                try
                {
                    ForceRefresh();
                }
                catch (Exception)
                {
                    // Ignore refresh exceptions
                }
            }
        }

        private void UpdateRenderCollections()
        {
            lock (orderLock)
            {
                if (renderBidOrders == null)
                    renderBidOrders = new Dictionary<double, OrderInfo>();
                if (renderAskOrders == null)
                    renderAskOrders = new Dictionary<double, OrderInfo>();

                // Collect all volumes first
                var bidVolumes = new Dictionary<double, long>();
                var askVolumes = new Dictionary<double, long>();

                foreach (var kvp in renderBidOrders)
                {
                    if (kvp.Value.Volume > 0)
                    {
                        bidVolumes[kvp.Key] = kvp.Value.Volume;
                    }
                }

                foreach (var kvp in renderAskOrders)
                {
                    if (kvp.Value.Volume > 0)
                    {
                        askVolumes[kvp.Key] = kvp.Value.Volume;
                    }
                }

                if (bidVolumes.Count > 0 && askVolumes.Count > 0)
                {
                    maxVolume = Math.Max(bidVolumes.Values.Max(), askVolumes.Values.Max());
                }
                else if (bidVolumes.Count > 0)
                {
                    maxVolume = bidVolumes.Values.Max();
                }
                else if (askVolumes.Count > 0)
                {
                    maxVolume = askVolumes.Values.Max();
                }
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (ChartControl == null || ChartBars == null || BidBrush == null || AskBrush == null) 
                return;

            volumesByPriceRange.Clear();

            Dictionary<double, OrderInfo> threadSafeBidOrders;
            Dictionary<double, OrderInfo> threadSafeAskOrders;
            long currentMaxVolume;
            
            lock (orderLock)
            {
                threadSafeBidOrders = new Dictionary<double, OrderInfo>(renderBidOrders);
                threadSafeAskOrders = new Dictionary<double, OrderInfo>(renderAskOrders);
                currentMaxVolume = maxVolume;
            }

            if (currentMaxVolume == 0)
                return;

            ChartControl.Properties.BarMarginRight = marginRight;
            double tickSize = Instrument.MasterInstrument.TickSize;
            double visibleHigh = chartScale.MaxValue;
            double visibleLow = chartScale.MinValue;
            double visibleRange = visibleHigh - visibleLow;
            
            if (visibleRange <= 0 || Double.IsInfinity(visibleRange))
                return;

            float pixelsPerPrice = (float)ChartPanel.H / (float)visibleRange;
            float pixelsPerTick = pixelsPerPrice * (float)tickSize;
            float barHeight = Math.Max(2.0f, Math.Min(pixelsPerTick * 0.9f, 20.0f));

            var dxBrushBid = ((SolidColorBrush)BidBrush).ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
            var dxBrushAsk = ((SolidColorBrush)AskBrush).ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
            var dxBrushText = ((SolidColorBrush)TextBrush).ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;

            if (dxBrushBid == null || dxBrushAsk == null || dxBrushText == null)
                return;

            try
            {
                // Sort orders by volume
                var sortedAsks = threadSafeAskOrders
                    .OrderByDescending(x => x.Value.Volume)
                    .ToList();

                var sortedBids = threadSafeBidOrders
                    .OrderByDescending(x => x.Value.Volume)
                    .ToList();

                // Render asks
                foreach (var ask in sortedAsks)
                {
                    if (ask.Value.Volume <= 0 || ask.Key < visibleLow || ask.Key > visibleHigh ||
                        (currentAskPrice != double.MaxValue && ask.Key < currentAskPrice))
                        continue;

                    RenderDOMBar(ask.Key, ask.Value.Volume, false, currentMaxVolume, barHeight, chartScale, dxBrushAsk, dxBrushText, tickSize);
                }

                // Render bids
                foreach (var bid in sortedBids)
                {
                    if (bid.Value.Volume <= 0 || bid.Key < visibleLow || bid.Key > visibleHigh ||
                        (currentBidPrice != double.MinValue && bid.Key > currentBidPrice))
                        continue;

                    RenderDOMBar(bid.Key, bid.Value.Volume, true, currentMaxVolume, barHeight, chartScale, dxBrushBid, dxBrushText, tickSize);
                }
            }
            finally
            {
                dxBrushBid.Dispose();
                dxBrushAsk.Dispose();
                dxBrushText.Dispose();
            }
        }

        private void RenderDOMBar(double price, long volume, bool isBid, long maxVolume, float barHeight, 
            ChartScale chartScale, SharpDX.Direct2D1.SolidColorBrush brush, SharpDX.Direct2D1.SolidColorBrush textBrush,
            double tickSize)
        {
            float y = chartScale.GetYByValue(price);
            if (y < 0 || y > ChartPanel.H)
                return;

            // Calculate distance from current price
            double referencePrice = isBid ? currentBidPrice : currentAskPrice;
            double priceDistance = Math.Abs(price - referencePrice);
            int ticksAway = (int)(priceDistance / tickSize);
            
            float baseOpacity = 0.8f;
            float minOpacity = 0.15f;
            float distanceOpacity = baseOpacity;

            // Keep full opacity for first 40 ticks, then linear falloff
            if (ticksAway > 40)
            {
                // Linear decay over the next 80 ticks
                float fadeRange = 80f;
                distanceOpacity = baseOpacity * Math.Max(0, 1 - (ticksAway - 40) / fadeRange);
            }

            // If historical, further reduce opacity
            if (isBid ? (price < currentBidPrice) : (price > currentAskPrice))
            {
                distanceOpacity *= historicalOpacity;
            }

            // Ensure minimum opacity
            distanceOpacity = Math.Max(minOpacity, distanceOpacity);

            // Calculate volume ratio
            float volumeRatio = (float)volume / (float)maxVolume;

            // Calculate bar width and position
            float barWidth = Math.Min(maxRightExtension, volumeRatio * maxRightExtension);
            float x = ChartPanel.W - marginRight;
            
            if (alignment == BarAlignment.Center)
            {
                x -= maxRightExtension / 2;
            }
            else if (alignment == BarAlignment.Left)
            {
                x -= maxRightExtension;
            }

            // Create rectangle for the volume bar
            var rect = new SharpDX.RectangleF(
                x,
                y - barHeight / 2,
                barWidth,
                barHeight
            );

            // Set brush opacity and draw the bar
            brush.Opacity = distanceOpacity;
            RenderTarget.FillRectangle(rect, brush);

            // Draw volume text if enabled and volume meets threshold
            if (ShowVolumeText && (volumeRatio * 100 >= VolumeThresholdPercent))
            {
                float textSizeRange = maxTextSize - minTextSize;
                float textSize = Math.Min(maxTextSize, Math.Max(minTextSize, minTextSize + (textSizeRange * volumeRatio)));
                
                using (var textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory,
                    "Segoe UI", textSize))
                {
                    textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
                    textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;

                    float textX = rect.X + (alignment == BarAlignment.Right ? barWidth : 0) + 5;
                    var textRect = new SharpDX.RectangleF(
                        textX,
                        rect.Y,
                        50,
                        rect.Height
                    );

                    textBrush.Opacity = distanceOpacity;
                    RenderTarget.DrawText(
                        volume.ToString("N0"),
                        textFormat,
                        textRect,
                        textBrush
                    );

                    // Calculate price range key
                    double rangeKey = Math.Floor(price / (tickSize * priceRangeTicks)) * (tickSize * priceRangeTicks);
                    
                    // Update volumesByPriceRange with the maximum volume in this range
                    if (!volumesByPriceRange.ContainsKey(rangeKey))
                    {
                        volumesByPriceRange[rangeKey] = volume;
                    }
                    else
                    {
                        volumesByPriceRange[rangeKey] = Math.Max(volumesByPriceRange[rangeKey], volume);
                    }
                }
            }
        }

        private bool HasCloserVolumeText(HashSet<long> shownVolumes, long volume, double currentPrice, double referencePrice)
        {
            if (shownVolumes.Contains(volume))
            {
                double currentDistance = Math.Abs(currentPrice - referencePrice);
                return currentDistance > 0;
            }
            return false;
        }

        private class OrderInfo
        {
            public double Price { get; set; }
            public long Volume { get; set; }
            public DateTime Time { get; set; }
            public bool IsHistorical { get; set; }
        }

        private class VolumeTextInfo
        {
            public long Volume { get; set; }
            public SharpDX.RectangleF Rectangle { get; set; }
            public float TextWidth { get; set; }
            public float TextPadding { get; set; }
            public SharpDX.DirectWrite.TextFormat Format { get; set; }
            public bool IsBid { get; set; }
        }

        private class TextInfo
        {
            public double Price { get; set; }
            public long Volume { get; set; }
            private SharpDX.RectangleF _rectangle;
            public SharpDX.RectangleF Rectangle
            {
                get { return _rectangle; }
                set { _rectangle = value; }
            }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Staging.Domdicator_Staging2[] cacheDomdicator_Staging2;
		public Staging.Domdicator_Staging2 Domdicator_Staging2(int marginRight, int maxRightExtension, float maxTextSize, float minTextSize, bool showVolumeText, int priceRangeTicks, int volumeThresholdPercent, float historicalOpacity, BarAlignment alignment)
		{
			return Domdicator_Staging2(Input, marginRight, maxRightExtension, maxTextSize, minTextSize, showVolumeText, priceRangeTicks, volumeThresholdPercent, historicalOpacity, alignment);
		}

		public Staging.Domdicator_Staging2 Domdicator_Staging2(ISeries<double> input, int marginRight, int maxRightExtension, float maxTextSize, float minTextSize, bool showVolumeText, int priceRangeTicks, int volumeThresholdPercent, float historicalOpacity, BarAlignment alignment)
		{
			if (cacheDomdicator_Staging2 != null)
				for (int idx = 0; idx < cacheDomdicator_Staging2.Length; idx++)
					if (cacheDomdicator_Staging2[idx] != null && cacheDomdicator_Staging2[idx].MarginRight == marginRight && cacheDomdicator_Staging2[idx].MaxRightExtension == maxRightExtension && cacheDomdicator_Staging2[idx].MaxTextSize == maxTextSize && cacheDomdicator_Staging2[idx].MinTextSize == minTextSize && cacheDomdicator_Staging2[idx].ShowVolumeText == showVolumeText && cacheDomdicator_Staging2[idx].PriceRangeTicks == priceRangeTicks && cacheDomdicator_Staging2[idx].VolumeThresholdPercent == volumeThresholdPercent && cacheDomdicator_Staging2[idx].HistoricalOpacity == historicalOpacity && cacheDomdicator_Staging2[idx].Alignment == alignment && cacheDomdicator_Staging2[idx].EqualsInput(input))
						return cacheDomdicator_Staging2[idx];
			return CacheIndicator<Staging.Domdicator_Staging2>(new Staging.Domdicator_Staging2(){ MarginRight = marginRight, MaxRightExtension = maxRightExtension, MaxTextSize = maxTextSize, MinTextSize = minTextSize, ShowVolumeText = showVolumeText, PriceRangeTicks = priceRangeTicks, VolumeThresholdPercent = volumeThresholdPercent, HistoricalOpacity = historicalOpacity, Alignment = alignment }, input, ref cacheDomdicator_Staging2);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Staging.Domdicator_Staging2 Domdicator_Staging2(int marginRight, int maxRightExtension, float maxTextSize, float minTextSize, bool showVolumeText, int priceRangeTicks, int volumeThresholdPercent, float historicalOpacity, BarAlignment alignment)
		{
			return indicator.Domdicator_Staging2(Input, marginRight, maxRightExtension, maxTextSize, minTextSize, showVolumeText, priceRangeTicks, volumeThresholdPercent, historicalOpacity, alignment);
		}

		public Indicators.Staging.Domdicator_Staging2 Domdicator_Staging2(ISeries<double> input , int marginRight, int maxRightExtension, float maxTextSize, float minTextSize, bool showVolumeText, int priceRangeTicks, int volumeThresholdPercent, float historicalOpacity, BarAlignment alignment)
		{
			return indicator.Domdicator_Staging2(input, marginRight, maxRightExtension, maxTextSize, minTextSize, showVolumeText, priceRangeTicks, volumeThresholdPercent, historicalOpacity, alignment);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Staging.Domdicator_Staging2 Domdicator_Staging2(int marginRight, int maxRightExtension, float maxTextSize, float minTextSize, bool showVolumeText, int priceRangeTicks, int volumeThresholdPercent, float historicalOpacity, BarAlignment alignment)
		{
			return indicator.Domdicator_Staging2(Input, marginRight, maxRightExtension, maxTextSize, minTextSize, showVolumeText, priceRangeTicks, volumeThresholdPercent, historicalOpacity, alignment);
		}

		public Indicators.Staging.Domdicator_Staging2 Domdicator_Staging2(ISeries<double> input , int marginRight, int maxRightExtension, float maxTextSize, float minTextSize, bool showVolumeText, int priceRangeTicks, int volumeThresholdPercent, float historicalOpacity, BarAlignment alignment)
		{
			return indicator.Domdicator_Staging2(input, marginRight, maxRightExtension, maxTextSize, minTextSize, showVolumeText, priceRangeTicks, volumeThresholdPercent, historicalOpacity, alignment);
		}
	}
}

#endregion
