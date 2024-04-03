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
using System.Runtime.InteropServices;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class Ricochet : Indicator
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Indicator here.";
				Name										= "Ricochet";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= true;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive					= true;

                AddPlot(new Stroke(Brushes.Lime, 4), PlotStyle.Dot, "green");
                AddPlot(new Stroke(Brushes.Red, 4), PlotStyle.Dot, "red");
            }
			else if (State == State.Configure)
			{
			}
		}

		void JustDoIt()
		{
			var upTrades = Volume[0] * (Close[0] - Low[0]) / (High[0] - Low[0]);
			var dnTrades = Volume[0] * (High[0] - Close[0]) / (High[0] - Low[0]);
            var pupTrades = Volume[1] * (Close[1] - Low[1]) / (High[1] - Low[1]);
            var pdnTrades = Volume[1] * (High[1] - Close[1]) / (High[1] - Low[1]);

            Bollinger bb = Bollinger(2, 20);
            double bb_top = bb.Values[0][0];
            double bb_bottom = bb.Values[2][0];

            green[0] = Low[0] - (TickSize * 5);
            red[0] = High[0] + (TickSize * 5);

            Draw.Text(this, "B" + CurrentBar, "Woo", 0, Low[0] - (TickSize * 5), Brushes.White);

            if (upTrades > pdnTrades && upTrades > pupTrades && upTrades > dnTrades &&
				(Low[0] < bb_bottom || Low[0] < bb_bottom))
            {
                Draw.Text(this, "D" + CurrentBar, "Woo", 0, Low[0] - (TickSize * 5), Brushes.White);
            }
            if (dnTrades > pupTrades && dnTrades > pdnTrades && dnTrades > upTrades &&
                (High[0] > bb_top || High[0] > bb_top))
            {
                Draw.Text(this, "A" + CurrentBar, "Woo", 0, High[0] + (TickSize * 5), Brushes.White);
            }
        }
		
		protected override void OnBarUpdate()
		{
			JustDoIt();
		}

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> green
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> red
        {
            get { return Values[1]; }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Ricochet[] cacheRicochet;
		public Ricochet Ricochet()
		{
			return Ricochet(Input);
		}

		public Ricochet Ricochet(ISeries<double> input)
		{
			if (cacheRicochet != null)
				for (int idx = 0; idx < cacheRicochet.Length; idx++)
					if (cacheRicochet[idx] != null &&  cacheRicochet[idx].EqualsInput(input))
						return cacheRicochet[idx];
			return CacheIndicator<Ricochet>(new Ricochet(), input, ref cacheRicochet);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Ricochet Ricochet()
		{
			return indicator.Ricochet(Input);
		}

		public Indicators.Ricochet Ricochet(ISeries<double> input )
		{
			return indicator.Ricochet(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Ricochet Ricochet()
		{
			return indicator.Ricochet(Input);
		}

		public Indicators.Ricochet Ricochet(ISeries<double> input )
		{
			return indicator.Ricochet(input);
		}
	}
}

#endregion
