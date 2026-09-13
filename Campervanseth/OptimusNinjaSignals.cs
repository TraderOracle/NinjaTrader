#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    [Gui.CategoryOrder("Optimization Filters", 1)]
    [Gui.CategoryOrder("Buy/Sell Filters", 2)]
    [Gui.CategoryOrder("Advanced Patterns", 3)]
    [Gui.CategoryOrder("Buy/Sell Symbols", 4)]
    [Gui.CategoryOrder("Colors", 5)]

    public class OptimusSignalsOnly : Indicator
    {
        private SMA EMAS, EMAF;
        private EMA trendEMA;
        private Series<double> MACD1, sqzData, SqueezeDef, AO;
        private bool sqRelaxUp = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Standalone Buy/Sell Signals with Optimization, Squeeze Relaxer, and Trampoline.";
                Name = "OptimusSignalsOnly";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;

                // Optimization Defaults
                bUseEMATrend = true;
                bUsePABreakout = false;

                // Pattern Defaults
                bShowTrampoline = true;
                bShowSqueezeRelaxer = true;

                // Core Defaults
                bUseFisher = true;
                bUseWaddah = true;
                bUseT3 = true;
                bUsePSAR = true;
                bUseSuperTrend = true;
                bUseSqueeze = false;
                bUseMACD = false;
                bUseAO = false;
                bUseHMA = false;
                iMinADX = 0;

                sBuySymbol = "▴";
                sSellSymbol = "▾";
                iBuySellSymbolSize = 12;
                iTickOffset = 7;

                Green_Brush = Brushes.Lime;
                Red_Brush = Brushes.Orange;
            }
            else if (State == State.Configure)
            {
                MACD1 = new Series<double>(this);
                sqzData = new Series<double>(this);
                SqueezeDef = new Series<double>(this);
                AO = new Series<double>(this);
            }
            else if (State == State.DataLoaded)
            {
                EMAF = SMA(3);
                EMAS = SMA(10);
                trendEMA = EMA(200);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 22) return;

            // ==========================================
            // CANDLE LOGIC (For Trampoline & Drawing)
            // ==========================================
            bool green = Close[0] > Open[0];
            bool red = Close[0] < Open[0];
            
            var c0G = Open[0] < Close[0];
            var c0R = Open[0] > Close[0];
            var c1G = Open[1] < Close[1];
            var c1R = Open[1] > Close[1];
            var c2G = Open[2] < Close[2];
            var c2R = Open[2] > Close[2];

            double locHigh = High[0] + (TickSize * iTickOffset);
            double locLow = Low[0] - (TickSize * iTickOffset);
            double locSwap = green ? locLow : locHigh; // Used for pattern offsets

            // ==========================================
            // CORE INDICATORS
            // ==========================================
            AO[0] = SMA(Median, 5)[0] - SMA(Median, 34)[0];
            bool bAOGreen = AO[0] > AO[1];

            // Squeeze & Squeeze Relaxer
            double kct = KeltnerChannel(2, 20).Upper[0];
            double kcb = KeltnerChannel(2, 20).Lower[0];
            double h = High[HighestBar(High, 20)];
            double l = Low[LowestBar(Low, 20)];
            double avg = (h + l) / 2;
            avg = (avg + (kct + kcb) / 2) / 2;
            sqzData[0] = Close[0] - avg;
            SqueezeDef[0] = LinReg(sqzData, 20)[0];
            bool sqeezeUp = SqueezeDef[0] > 0;

            if (bShowSqueezeRelaxer)
            {
                if (SqueezeDef[0] > 0)
                {
                    if (SqueezeDef[0] < SqueezeDef[1] && !sqRelaxUp)
                    {
                        Draw.Text(this, "SqZ" + CurrentBar, true, "✦", 0, locSwap, 0, Brushes.Yellow, new SimpleFont("Arial", 12), TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 40);
                        sqRelaxUp = true;
                    }
                }
                else
                {
                    if (SqueezeDef[0] > SqueezeDef[1] && sqRelaxUp)
                    {
                        Draw.Text(this, "SqZ" + CurrentBar, true, "✦", 0, locSwap, 0, Brushes.Yellow, new SimpleFont("Arial", 12), TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 40);
                        sqRelaxUp = false;
                    }
                }
            }

            // Trampoline Pattern
            if (bShowTrampoline)
            {
                RSI rsi = RSI(14, 1);
                Bollinger bb = Bollinger(2, 20);
                double bb_top = bb.Values[0][0];
                double bb_bottom = bb.Values[2][0];

                if (c0R && c1R && Close[0] < Close[1] && (rsi.Value[0] >= 70 || rsi.Value[1] >= 70 || rsi.Value[2] >= 70) &&
                    c2G && High[2] >= (bb_top - (TickSize * 30)))
                {
                    Draw.Text(this, "TR" + CurrentBar, true, "TR", 0, locSwap, 0, Brushes.Yellow, new SimpleFont("Arial", 9), TextAlignment.Center, Brushes.Transparent, Brushes.PowderBlue, 40);
                }

                if (c0G && c1G && Close[0] > Close[1] && (rsi.Value[0] < 25 || rsi.Value[1] < 25 || rsi.Value[2] < 25) &&
                    c2R && Low[2] <= (bb_bottom + (TickSize * 30)))
                {
                    Draw.Text(this, "TR" + CurrentBar, true, "TR", 0, locSwap, 0, Brushes.Yellow, new SimpleFont("Arial", 9), TextAlignment.Center, Brushes.Transparent, Brushes.PowderBlue, 40);
                }
            }

            MACD1[0] = EMAF[0] - EMAS[0];
            bool macdUp = (MACD1[0] - SMA(MACD1, 16)[0]) > 0;
            bool wadaUp = (MACD(20, 40, 9)[0] - MACD(20, 40, 9)[1]) * 160 >= 0;
            bool superUp = Supertrend(2, 11).Value[0] < Low[0];
            bool fisherUp = FisherTransform(10).Value[0] > FisherTransform(10).Value[1];
            bool psarUp = ParabolicSAR(0.02, 0.2, 0.02).Value[0] < Low[0];
            bool hullUp = HMA(14).Value[0] > HMA(14).Value[1];
            bool t3Up = Close[0] > T3(10, 2, 0.7).Value[0];
            double adxVal = ADX(10)[0];

            bool bShowUp = true;
            bool bShowDown = true;

            // Core Indicator Conditions
            if ((!macdUp && bUseMACD) || (!psarUp && bUsePSAR) || (!fisherUp && bUseFisher) || 
                (!t3Up && bUseT3) || (!wadaUp && bUseWaddah) || (!superUp && bUseSuperTrend) || 
                (!sqeezeUp && bUseSqueeze) || adxVal < iMinADX || (bUseHMA && !hullUp) || (bUseAO && !bAOGreen))
                bShowUp = false;

            if ((macdUp && bUseMACD) || (psarUp && bUsePSAR) || (fisherUp && bUseFisher) || 
                (t3Up && bUseT3) || (wadaUp && bUseWaddah) || (superUp && bUseSuperTrend) || 
                (sqeezeUp && bUseSqueeze) || adxVal < iMinADX || (bUseHMA && hullUp) || (bUseAO && bAOGreen))
                bShowDown = false;

            // ==========================================
            // OPTIMIZATION FILTERS
            // ==========================================
            if (bUseEMATrend)
            {
                if (Close[0] < trendEMA[0]) bShowUp = false; 
                if (Close[0] > trendEMA[0]) bShowDown = false; 
            }

            if (bUsePABreakout)
            {
                if (Close[0] <= High[1]) bShowUp = false; 
                if (Close[0] >= Low[1]) bShowDown = false; 
            }

            // Draw Standard Signals
            if (green && bShowUp)
            {
                Draw.Text(this, "Buy" + CurrentBar, true, sBuySymbol, 0, locLow, 0, Green_Brush, 
                    new SimpleFont("Arial", iBuySellSymbolSize), TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 100);
            }

            if (red && bShowDown)
            {
                Draw.Text(this, "Sell" + CurrentBar, true, sSellSymbol, 0, locHigh, 0, Red_Brush, 
                    new SimpleFont("Arial", iBuySellSymbolSize), TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 100);
            }
        }

        #region Properties
        
        [NinjaScriptProperty]
        [Display(Name = "Use 200 EMA Trend Filter", GroupName = "Optimization Filters", Order = 1)]
        public bool bUseEMATrend { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require PA Breakout", GroupName = "Optimization Filters", Order = 2)]
        public bool bUsePABreakout { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Waddah Explosion", GroupName = "Buy/Sell Filters", Order = 1)]
        public bool bUseWaddah { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Awesome Oscillator", GroupName = "Buy/Sell Filters", Order = 2)]
        public bool bUseAO { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Parabolic SAR", GroupName = "Buy/Sell Filters", Order = 3)]
        public bool bUsePSAR { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Squeeze Momentum", GroupName = "Buy/Sell Filters", Order = 4)]
        public bool bUseSqueeze { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Linda MACD", GroupName = "Buy/Sell Filters", Order = 5)]
        public bool bUseMACD { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hull Moving Avg", GroupName = "Buy/Sell Filters", Order = 6)]
        public bool bUseHMA { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SuperTrend", GroupName = "Buy/Sell Filters", Order = 7)]
        public bool bUseSuperTrend { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "T3", GroupName = "Buy/Sell Filters", Order = 8)]
        public bool bUseT3 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Fisher Transform", GroupName = "Buy/Sell Filters", Order = 9)]
        public bool bUseFisher { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Minimum ADX", GroupName = "Buy/Sell Filters", Order = 10)]
        public int iMinADX { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Trampoline", GroupName = "Advanced Patterns", Order = 1)]
        public bool bShowTrampoline { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Squeeze Relaxer", GroupName = "Advanced Patterns", Order = 2)]
        public bool bShowSqueezeRelaxer { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Buy Symbol", GroupName = "Buy/Sell Symbols", Order = 1)]
        public string sBuySymbol { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Sell Symbol", GroupName = "Buy/Sell Symbols", Order = 2)]
        public string sSellSymbol { get; set; }

        [NinjaScriptProperty]
        [Range(4, 72)]
        [Display(Name = "Symbol Size", GroupName = "Buy/Sell Symbols", Order = 3)]
        public int iBuySellSymbolSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Tick Offset", GroupName = "Buy/Sell Symbols", Order = 4)]
        public int iTickOffset { get; set; }

        [XmlIgnore]
        [Display(Name = "Buy Color", GroupName = "Colors", Order = 1)]
        public Brush Green_Brush { get; set; }

        [XmlIgnore]
        [Display(Name = "Sell Color", GroupName = "Colors", Order = 2)]
        public Brush Red_Brush { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private OptimusSignalsOnly[] cacheOptimusSignalsOnly;
		public OptimusSignalsOnly OptimusSignalsOnly(bool bUseEMATrend, bool bUsePABreakout, bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowTrampoline, bool bShowSqueezeRelaxer, string sBuySymbol, string sSellSymbol, int iBuySellSymbolSize, int iTickOffset)
		{
			return OptimusSignalsOnly(Input, bUseEMATrend, bUsePABreakout, bUseWaddah, bUseAO, bUsePSAR, bUseSqueeze, bUseMACD, bUseHMA, bUseSuperTrend, bUseT3, bUseFisher, iMinADX, bShowTrampoline, bShowSqueezeRelaxer, sBuySymbol, sSellSymbol, iBuySellSymbolSize, iTickOffset);
		}

		public OptimusSignalsOnly OptimusSignalsOnly(ISeries<double> input, bool bUseEMATrend, bool bUsePABreakout, bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowTrampoline, bool bShowSqueezeRelaxer, string sBuySymbol, string sSellSymbol, int iBuySellSymbolSize, int iTickOffset)
		{
			if (cacheOptimusSignalsOnly != null)
				for (int idx = 0; idx < cacheOptimusSignalsOnly.Length; idx++)
					if (cacheOptimusSignalsOnly[idx] != null && cacheOptimusSignalsOnly[idx].bUseEMATrend == bUseEMATrend && cacheOptimusSignalsOnly[idx].bUsePABreakout == bUsePABreakout && cacheOptimusSignalsOnly[idx].bUseWaddah == bUseWaddah && cacheOptimusSignalsOnly[idx].bUseAO == bUseAO && cacheOptimusSignalsOnly[idx].bUsePSAR == bUsePSAR && cacheOptimusSignalsOnly[idx].bUseSqueeze == bUseSqueeze && cacheOptimusSignalsOnly[idx].bUseMACD == bUseMACD && cacheOptimusSignalsOnly[idx].bUseHMA == bUseHMA && cacheOptimusSignalsOnly[idx].bUseSuperTrend == bUseSuperTrend && cacheOptimusSignalsOnly[idx].bUseT3 == bUseT3 && cacheOptimusSignalsOnly[idx].bUseFisher == bUseFisher && cacheOptimusSignalsOnly[idx].iMinADX == iMinADX && cacheOptimusSignalsOnly[idx].bShowTrampoline == bShowTrampoline && cacheOptimusSignalsOnly[idx].bShowSqueezeRelaxer == bShowSqueezeRelaxer && cacheOptimusSignalsOnly[idx].sBuySymbol == sBuySymbol && cacheOptimusSignalsOnly[idx].sSellSymbol == sSellSymbol && cacheOptimusSignalsOnly[idx].iBuySellSymbolSize == iBuySellSymbolSize && cacheOptimusSignalsOnly[idx].iTickOffset == iTickOffset && cacheOptimusSignalsOnly[idx].EqualsInput(input))
						return cacheOptimusSignalsOnly[idx];
			return CacheIndicator<OptimusSignalsOnly>(new OptimusSignalsOnly(){ bUseEMATrend = bUseEMATrend, bUsePABreakout = bUsePABreakout, bUseWaddah = bUseWaddah, bUseAO = bUseAO, bUsePSAR = bUsePSAR, bUseSqueeze = bUseSqueeze, bUseMACD = bUseMACD, bUseHMA = bUseHMA, bUseSuperTrend = bUseSuperTrend, bUseT3 = bUseT3, bUseFisher = bUseFisher, iMinADX = iMinADX, bShowTrampoline = bShowTrampoline, bShowSqueezeRelaxer = bShowSqueezeRelaxer, sBuySymbol = sBuySymbol, sSellSymbol = sSellSymbol, iBuySellSymbolSize = iBuySellSymbolSize, iTickOffset = iTickOffset }, input, ref cacheOptimusSignalsOnly);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.OptimusSignalsOnly OptimusSignalsOnly(bool bUseEMATrend, bool bUsePABreakout, bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowTrampoline, bool bShowSqueezeRelaxer, string sBuySymbol, string sSellSymbol, int iBuySellSymbolSize, int iTickOffset)
		{
			return indicator.OptimusSignalsOnly(Input, bUseEMATrend, bUsePABreakout, bUseWaddah, bUseAO, bUsePSAR, bUseSqueeze, bUseMACD, bUseHMA, bUseSuperTrend, bUseT3, bUseFisher, iMinADX, bShowTrampoline, bShowSqueezeRelaxer, sBuySymbol, sSellSymbol, iBuySellSymbolSize, iTickOffset);
		}

		public Indicators.OptimusSignalsOnly OptimusSignalsOnly(ISeries<double> input , bool bUseEMATrend, bool bUsePABreakout, bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowTrampoline, bool bShowSqueezeRelaxer, string sBuySymbol, string sSellSymbol, int iBuySellSymbolSize, int iTickOffset)
		{
			return indicator.OptimusSignalsOnly(input, bUseEMATrend, bUsePABreakout, bUseWaddah, bUseAO, bUsePSAR, bUseSqueeze, bUseMACD, bUseHMA, bUseSuperTrend, bUseT3, bUseFisher, iMinADX, bShowTrampoline, bShowSqueezeRelaxer, sBuySymbol, sSellSymbol, iBuySellSymbolSize, iTickOffset);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.OptimusSignalsOnly OptimusSignalsOnly(bool bUseEMATrend, bool bUsePABreakout, bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowTrampoline, bool bShowSqueezeRelaxer, string sBuySymbol, string sSellSymbol, int iBuySellSymbolSize, int iTickOffset)
		{
			return indicator.OptimusSignalsOnly(Input, bUseEMATrend, bUsePABreakout, bUseWaddah, bUseAO, bUsePSAR, bUseSqueeze, bUseMACD, bUseHMA, bUseSuperTrend, bUseT3, bUseFisher, iMinADX, bShowTrampoline, bShowSqueezeRelaxer, sBuySymbol, sSellSymbol, iBuySellSymbolSize, iTickOffset);
		}

		public Indicators.OptimusSignalsOnly OptimusSignalsOnly(ISeries<double> input , bool bUseEMATrend, bool bUsePABreakout, bool bUseWaddah, bool bUseAO, bool bUsePSAR, bool bUseSqueeze, bool bUseMACD, bool bUseHMA, bool bUseSuperTrend, bool bUseT3, bool bUseFisher, int iMinADX, bool bShowTrampoline, bool bShowSqueezeRelaxer, string sBuySymbol, string sSellSymbol, int iBuySellSymbolSize, int iTickOffset)
		{
			return indicator.OptimusSignalsOnly(input, bUseEMATrend, bUsePABreakout, bUseWaddah, bUseAO, bUsePSAR, bUseSqueeze, bUseMACD, bUseHMA, bUseSuperTrend, bUseT3, bUseFisher, iMinADX, bShowTrampoline, bShowSqueezeRelaxer, sBuySymbol, sSellSymbol, iBuySellSymbolSize, iTickOffset);
		}
	}
}

#endregion
