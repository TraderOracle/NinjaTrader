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
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{
    [Gui.CategoryOrder("Parameters", 1)]
    [Gui.CategoryOrder("Text Color", 2)]

    public class BarCount : Indicator
    {
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Displays Bar Count";
                Name = "BarCount";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                //Disable this property if your indicator requires custom values that cumulate with each new market data event. 
                //See Help Guide for additional information.
                IsSuspendedWhileInactive = true;

                TBCheck = true;
                BACheck = true;
                TBBrush = Brushes.DarkOrange;
                BABrush = Brushes.Red;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

			if (CurrentBar > 10)
            	TotalBars();

        }

        private void TotalBars()
        {
            Supertrend st = Supertrend(2, 10);
			bool superUp = st.Value[0] < Low[0] ? true : false;
			
            FisherTransform ft = FisherTransform(10);
			bool fisherUp = ft.Value[0] > 0 ? true : false;

            WaddahAttarExplosion wae = WaddahAttarExplosion(150, 30, 15, 1, false, 1, false, false, false, false);
			bool wadaUp = wae.Value[0] > 0 ? true : false;
			
			ParabolicSAR sar = ParabolicSAR(0.02, 0.2, 0.02);
			bool sarUp = sar.Value[0] < Low[0] ? true : false;
			
			Bollinger bb = Bollinger(2, 20);
			double bb_top = bb.Values[0][0];
			double bb_bottom = bb.Values[2][0];

			ADX x = ADX(10);
            KAMA kama = KAMA(2, 9, 109);
            T3 t3 = T3(10, 1, 1);
            RSI rsi = RSI(14, 1);
            HMA hma = HMA(14);
			
            var red = Close[0] < Open[0];
            var green = Close[0] > Open[0];
            var c0G = Open[0] < Close[0];
            var c0R = Open[0] > Close[0];
            var c1G = Open[1] < Close[1];
            var c1R = Open[1] > Close[1];
            var c2G = Open[2] < Close[2];
            var c2R = Open[2] > Close[2];
            var c3G = Open[3] < Close[3];
            var c3R = Open[3] > Close[3];
            var c4G = Open[4] < Close[4];
            var c4R = Open[4] > Close[4];

            var c0Body = Math.Abs(Close[0] - Open[0]);
            var c1Body = Math.Abs(Close[1] - Open[1]);
            var c2Body = Math.Abs(Close[2] - Open[2]);
            var c3Body = Math.Abs(Close[3] - Open[3]);
            var c4Body = Math.Abs(Close[4] - Open[4]);			
			
            var upWickLarger = c0R && Math.Abs(High[0] - Open[0]) > Math.Abs(Low[0] - Close[0]);

			var downWickLarger = c0G && Math.Abs(Low[0] - Open[0]) > Math.Abs(Close[0] - High[0]);

            var ThreeOutUp = c2R && c1G && c0G && Open[1] < Close[2] && Open[2] < Close[1] && Math.Abs(Open[1] - Close[1]) > Math.Abs(Open[2] - Close[2]) && Close[0] > Low[1];

            var ThreeOutDown = c2G && c1R && c0R && Open[1] > Close[2] && Open[2] > Close[1] && Math.Abs(Open[1] - Close[1]) > Math.Abs(Open[2] - Close[2]) && Close[0] < Low[1];

            var eqHigh = c0R && c1R && c2G && c3G && (High[1] > bb_top || High[2] > bb_top) && Close[0] < Close[1] && (Open[1] == Close[2] || Open[1] == Close[2] + TickSize || Open[1] + TickSize == Close[2]);

            var eqLow = c0G && c1G && c2R && c3R && (Low[1] < bb_bottom || Low[2] < bb_bottom) && Close[0] > Close[1] && (Open[1] == Close[2] || Open[1] == Close[2] + TickSize || Open[1] + TickSize == Close[2]);

            // if (bVolumeImbalances)
            {
                // var highPen = new Pen(new SolidBrush(Color.RebeccaPurple)) { Width = 2 };
                if (green && c1G && Open[0] > Close[1])
                {
					Draw.Text(this, "Total Bars " + CurrentBar, "GAP", 0, High[0] + 1 * TickSize, TBBrush);
                    // HorizontalLinesTillTouch.Add(new LineTillTouch(pbar, Open[0], highPen));
                    // _negWhite[pbar] = candle.Low - (_tick * 2);
                }
                if (red && c1R && Open[0] < Close[1])
                {
					Draw.Text(this, "Total Bars " + CurrentBar, "GAP", 0, High[0] + 1 * TickSize, TBBrush);
                    // HorizontalLinesTillTouch.Add(new LineTillTouch(pbar, Open[0], highPen));
                    // _posWhite[pbar] = candle.High + (_tick * 2);
                }
            }
			
			Print(bb.Values[0][0] + " " + bb.Values[1][0] +  " " + bb.Values[2][0]);
			
			if (Close[0] > hma.Value[0] && false)
            	Draw.Text(this, "Total Bars " + CurrentBar, "Dot", 0, High[0] + 1 * TickSize, TBBrush);
            //Draw.ArrowUp(this, CurrentBar.ToString(), true, DateTime.Now, Low[0] - 1 * TickSize, TBBrush);
			
        }

        #region Parameters
        [Range(0, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Bars Ago Count", GroupName = "Parameters", Order = 1)]
        public bool BACheck
        { get; set; }

        [Range(0, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Total Bars Count", GroupName = "Parameters", Order = 2)]
        public bool TBCheck
        { get; set; }

        [XmlIgnore]
        [Display(Name = "Total Bars", GroupName = "Text Color", Order = 1)]
        public Brush TBBrush
        { get; set; }

        [XmlIgnore]
        [Display(Name = "Bars Ago", GroupName = "Text Color", Order = 2)]
        public Brush BABrush
        { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BarCount[] cacheBarCount;
		public BarCount BarCount(bool bACheck, bool tBCheck)
		{
			return BarCount(Input, bACheck, tBCheck);
		}

		public BarCount BarCount(ISeries<double> input, bool bACheck, bool tBCheck)
		{
			if (cacheBarCount != null)
				for (int idx = 0; idx < cacheBarCount.Length; idx++)
					if (cacheBarCount[idx] != null && cacheBarCount[idx].BACheck == bACheck && cacheBarCount[idx].TBCheck == tBCheck && cacheBarCount[idx].EqualsInput(input))
						return cacheBarCount[idx];
			return CacheIndicator<BarCount>(new BarCount(){ BACheck = bACheck, TBCheck = tBCheck }, input, ref cacheBarCount);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BarCount BarCount(bool bACheck, bool tBCheck)
		{
			return indicator.BarCount(Input, bACheck, tBCheck);
		}

		public Indicators.BarCount BarCount(ISeries<double> input , bool bACheck, bool tBCheck)
		{
			return indicator.BarCount(input, bACheck, tBCheck);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BarCount BarCount(bool bACheck, bool tBCheck)
		{
			return indicator.BarCount(Input, bACheck, tBCheck);
		}

		public Indicators.BarCount BarCount(ISeries<double> input , bool bACheck, bool tBCheck)
		{
			return indicator.BarCount(input, bACheck, tBCheck);
		}
	}
}

#endregion
