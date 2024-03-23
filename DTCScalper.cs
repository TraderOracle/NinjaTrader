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
	public class DTCScalper : Indicator
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Indicator here.";
				Name										= "DTCScalper";
				Calculate									= Calculate.OnEachTick;
				IsOverlay									= false;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive					= true;
						
				AddPlot(new Stroke(Brushes.Lime, 4), PlotStyle.Dot, "green");
				AddPlot(new Stroke(Brushes.Red, 4), PlotStyle.Dot, "red");
				AddPlot(new Stroke(Brushes.Lime, 1), PlotStyle.Line, "greens");
				AddPlot(new Stroke(Brushes.Red, 1), PlotStyle.Line, "reds");
			}
			else if (State == State.Configure)
			{
			}
		}

		void DrawShit()
		{
			var up = Volume[0] * (Close[0] - Low[0]) / (High[0] - Low[0]);
			var down = Volume[0] * (High[0] - Close[0]) / (High[0] - Low[0]);
			
			green[0] = up;
			red[0] = down;
			greens[0] = up;
			reds[0] = down;
			
			//Draw.Text(this, "tag1", "Text to draw", 10, 10, Brushes.White);
		}
		
		protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
{
	DrawShit();
}

		protected override void OnBarUpdate()
		{
			DrawShit();
		}
	
[Browsable(false)]
[XmlIgnore]
public Series<double> greens
{
  get { return Values[0]; }
}

[Browsable(false)]
[XmlIgnore]
public Series<double> reds
{
  get { return Values[1]; }
}

[Browsable(false)]
[XmlIgnore]
public Series<double> green
{
  get { return Values[2]; }
}

[Browsable(false)]
[XmlIgnore]
public Series<double> red
{
  get { return Values[3]; }
}

	}
	
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private DTCScalper[] cacheDTCScalper;
		public DTCScalper DTCScalper()
		{
			return DTCScalper(Input);
		}

		public DTCScalper DTCScalper(ISeries<double> input)
		{
			if (cacheDTCScalper != null)
				for (int idx = 0; idx < cacheDTCScalper.Length; idx++)
					if (cacheDTCScalper[idx] != null &&  cacheDTCScalper[idx].EqualsInput(input))
						return cacheDTCScalper[idx];
			return CacheIndicator<DTCScalper>(new DTCScalper(), input, ref cacheDTCScalper);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.DTCScalper DTCScalper()
		{
			return indicator.DTCScalper(Input);
		}

		public Indicators.DTCScalper DTCScalper(ISeries<double> input )
		{
			return indicator.DTCScalper(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.DTCScalper DTCScalper()
		{
			return indicator.DTCScalper(Input);
		}

		public Indicators.DTCScalper DTCScalper(ISeries<double> input )
		{
			return indicator.DTCScalper(input);
		}
	}
}

#endregion
