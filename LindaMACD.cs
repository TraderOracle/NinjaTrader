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
	public class LindaMACD : Indicator
	{
		private SMA SMAS;
		private SMA SMAF;
		private	Series<double>		MACD1;
		private	Series<double>		LMACD;
		private bool psarUp = false;
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Histogram for MACD-Signal Line";
				Name										= "LindaMACD";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= false;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= true;
				
				AddPlot(new Stroke(Brushes.Green, 4), PlotStyle.Bar,	"LINDA MACD");
				Plots[0].AutoWidth = true;
			}
			else if (State == State.Configure)
			{
				MACD1		= new Series<double>(this);
				LMACD		= new Series<double>(this);
			}
			else if (State == State.DataLoaded)
			{				
				SMAF				= SMA(3);
				SMAS				= SMA(10);
			}
		}

		protected override void OnBarUpdate()
		{
			SolidColorBrush br;
			
			ParabolicSAR sar = ParabolicSAR(0.02, 0.2, 0.02);
			bool sarUp = sar.Value[0] < Low[0] ? true : false;
			
			MACD1[0] = SMAF[0]-SMAS[0];
			if(MACD1[0] - SMA(MACD1,16)[0] > 0)
				LMACD[0] = MACD1[0] - SMA(MACD1,16)[0] ;
			
			if(MACD1[0] - SMA(MACD1,16)[0] <= 0)
				LMACD[0] = -1 * (MACD1[0] - SMA(MACD1,16)[0]);

			Value[0] = LMACD[0];
			br = MACD1[0] - SMA(MACD1,16)[0] > 0 ? Brushes.Green : Brushes.Firebrick;
			
			if (sarUp && !psarUp && br == Brushes.Green)
				br = Brushes.Lime;
			if (!sarUp && psarUp && br == Brushes.Firebrick)
				br = Brushes.Red;	
				
			psarUp = sarUp;
			
			PlotBrushes[0][0] = br;
		}
	
	#region Properties
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> LINDAMACD
		{
			get { return Values[0]; }
		}
	#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private LindaMACD[] cacheLindaMACD;
		public LindaMACD LindaMACD()
		{
			return LindaMACD(Input);
		}

		public LindaMACD LindaMACD(ISeries<double> input)
		{
			if (cacheLindaMACD != null)
				for (int idx = 0; idx < cacheLindaMACD.Length; idx++)
					if (cacheLindaMACD[idx] != null &&  cacheLindaMACD[idx].EqualsInput(input))
						return cacheLindaMACD[idx];
			return CacheIndicator<LindaMACD>(new LindaMACD(), input, ref cacheLindaMACD);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.LindaMACD LindaMACD()
		{
			return indicator.LindaMACD(Input);
		}

		public Indicators.LindaMACD LindaMACD(ISeries<double> input )
		{
			return indicator.LindaMACD(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.LindaMACD LindaMACD()
		{
			return indicator.LindaMACD(Input);
		}

		public Indicators.LindaMACD LindaMACD(ISeries<double> input )
		{
			return indicator.LindaMACD(input);
		}
	}
}

#endregion
