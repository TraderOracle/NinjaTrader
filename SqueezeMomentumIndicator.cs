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

///+--------------------------------------------------------------------------------------------------+
///|   Site:     http://fxstill.com                                                                   |
///|   Telegram: https://t.me/fxstill (Literature on cryptocurrencies, development and code. )        |
///|                                   Don't forget to subscribe!                                     |   
///+--------------------------------------------------------------------------------------------------+

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators.AUN_Indi
{
	/// <summary>
	/// Translate from Pine: Squeeze Momentum Indicator Author: [LazyBear]. Author Description:
	/// "This is a derivative of John Carter's "TTM Squeeze" volatility indicator, as discussed in his book 
	/// "Mastering the Trade" (chapter 11).	Black cicles on the midline show that the market just entered
	/// a squeeze ( Bollinger Bands are with in Keltner Channel). This signifies low volatility , market
	/// reparing itself for an explosive move (up or down). Gray cicles signify "Squeeze release".
	/// Mr.Carter suggests waiting till the first gray after a black cicle, and taking a position in the
	/// direction of the momentum (for ex., if momentum value is above zero, go long). Exit the position
	/// when the momentum changes (increase or decrease --- signified by a color change). My (limited)
	/// experience with this shows, an additional indicator like ADX / WaveTrend, is needed to not miss
	/// good entry points. Also, Mr.Carter uses simple momentum indicator , while I have used a
	/// different method (linreg based) to plot the histogram." 
	/// </summary>
	
	public class SqueezeMomentumIndicator : Indicator
	{
		private int iMinBar;
		private Series<double> data;
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Translate from Pine: Squeeze Momentum Indicator [LazyBear]. ";
				Name										= "SqueezeMomentumIndicator";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= false;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= false;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= true;
				LengthBB					                = 20;
				MultBB					                    = 2;
				LengthKC					                = 20;
				MultKC					                    = 2;
				BrushUpEnd					                = Brushes.ForestGreen;
				BrushDownBegin					            = Brushes.Red;
				BrushDownEnd					            = Brushes.Maroon;
				IsSqueeze					                = Brushes.RoyalBlue;
				NoSqueeze					                = Brushes.MintCream;

				AddPlot(new Stroke(Brushes.LightGreen, 2), PlotStyle.Bar, "SqueezeDef");
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Dot, "IsSqueezes");
			}
			else if (State == State.Configure)
			{
				iMinBar = Math.Max(LengthBB, LengthKC) + 1;
				data = new Series<double>(this);
			}
		}

		protected override void OnBarUpdate()
		{
			if(CurrentBar < iMinBar ) return;
			double bbt = Bollinger(MultBB, LengthBB).Upper[0];
			double bbb = Bollinger(MultBB, LengthBB).Lower[0];
			double kct = KeltnerChannel(MultKC, LengthKC).Upper[0];
			double kcb = KeltnerChannel(MultKC, LengthKC).Lower[0];
			
   			bool sqzOn  = (bbb > kcb) && (bbt < kct);
   			bool sqzOff = (bbb < kcb) && (bbt > kct);
   			bool noSqz  = (sqzOn == false)  && (sqzOff == false); 			
			
			double h = High[HighestBar(High, LengthKC)];			
			double l = Low[LowestBar(Low, LengthKC)];
			
			double avg = (h + l) / 2;
			avg = (avg + (kct + kcb) / 2) / 2;
			data[0] = Close[0] - avg;
			IsSqueezes[0] = 0.0;
			SqueezeDef[0] = LinReg(data, LengthKC)[0];
			if (SqueezeDef[0] > 0) {
      			if(SqueezeDef[0] < SqueezeDef[1])  PlotBrushes[0][0] = BrushUpEnd;
   			} else {
      			if(SqueezeDef[0] < SqueezeDef[1])  PlotBrushes[0][0] = BrushDownBegin;
				else  PlotBrushes[0][0] = BrushDownEnd;
		    }	
   			if (!noSqz) {
      			PlotBrushes[1][0] = (sqzOn)? IsSqueeze: NoSqueeze;
   			}			
		}// void OnBarUpdate()

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="LengthBB", Description="Bollinger Bands Period", Order=1, GroupName="Parameters")]
		public int LengthBB
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name="MultBB", Description="Bollinger Bands MultFactor", Order=2, GroupName="Parameters")]
		public double MultBB
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="LengthKC", Description="Keltner Channel Period", Order=3, GroupName="Parameters")]
		public int LengthKC
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name="MultKC", Description="Keltner Channel MultFactor", Order=4, GroupName="Parameters")]
		public double MultKC
		{ get; set; }
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SqueezeDef
		{
			get { return Values[0]; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> IsSqueezes
		{
			get { return Values[1]; }
		}		
		
/*
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SqueezeUpEnd
		{
			get { return Values[1]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SqueezeDwnBegin
		{
			get { return Values[2]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SqueezeDwnEnd
		{
			get { return Values[3]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> IsSqze
		{
			get { return Values[4]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> NoSqze
		{
			get { return Values[5]; }
		}
*/		
		
/*
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="BrushUpBegin", Description="Begin Bull Brush", Order=5, GroupName="Parameters")]
		public Brush BrushUpBegin
		{ get; set; }

		[Browsable(false)]
		public string BrushUpBeginSerializable
		{
			get { return Serialize.BrushToString(BrushUpBegin); }
			set { BrushUpBegin = Serialize.StringToBrush(value); }
		}			
*/
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="BrushUpEnd", Description="End Bull Brush", Order=6, GroupName="Parameters")]
		public Brush BrushUpEnd
		{ get; set; }

		[Browsable(false)]
		public string BrushUpEndSerializable
		{
			get { return Serialize.BrushToString(BrushUpEnd); }
			set { BrushUpEnd = Serialize.StringToBrush(value); }
		}			

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="BrushDownBegin", Description="Begin Bear Brush", Order=7, GroupName="Parameters")]
		public Brush BrushDownBegin
		{ get; set; }

		[Browsable(false)]
		public string BrushDownBeginSerializable
		{
			get { return Serialize.BrushToString(BrushDownBegin); }
			set { BrushDownBegin = Serialize.StringToBrush(value); }
		}			

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="BrushDownEnd", Description="End Bear Brush", Order=8, GroupName="Parameters")]
		public Brush BrushDownEnd
		{ get; set; }

		[Browsable(false)]
		public string BrushDownEndSerializable
		{
			get { return Serialize.BrushToString(BrushDownEnd); }
			set { BrushDownEnd = Serialize.StringToBrush(value); }
		}			
/*
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Squeeze", Description="Neutral Squeeze", Order=9, GroupName="Parameters")]
		public Brush Squeeze
		{ get; set; }

		[Browsable(false)]
		public string SqueezeSerializable
		{
			get { return Serialize.BrushToString(Squeeze); }
			set { Squeeze = Serialize.StringToBrush(value); }
		}			
*/
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="IsSqueeze", Description="Is Squeeze", Order=10, GroupName="Parameters")]
		public Brush IsSqueeze
		{ get; set; }

		[Browsable(false)]
		public string IsSqueezeSerializable
		{
			get { return Serialize.BrushToString(IsSqueeze); }
			set { IsSqueeze = Serialize.StringToBrush(value); }
		}			

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="NoSqueeze", Description="No Squeeze", Order=11, GroupName="Parameters")]
		public Brush NoSqueeze
		{ get; set; }

		[Browsable(false)]
		public string NoSqueezeSerializable
		{
			get { return Serialize.BrushToString(NoSqueeze); }
			set { NoSqueeze = Serialize.StringToBrush(value); }
		}			

		#endregion

	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AUN_Indi.SqueezeMomentumIndicator[] cacheSqueezeMomentumIndicator;
		public AUN_Indi.SqueezeMomentumIndicator SqueezeMomentumIndicator(int lengthBB, double multBB, int lengthKC, double multKC, Brush brushUpEnd, Brush brushDownBegin, Brush brushDownEnd, Brush isSqueeze, Brush noSqueeze)
		{
			return SqueezeMomentumIndicator(Input, lengthBB, multBB, lengthKC, multKC, brushUpEnd, brushDownBegin, brushDownEnd, isSqueeze, noSqueeze);
		}

		public AUN_Indi.SqueezeMomentumIndicator SqueezeMomentumIndicator(ISeries<double> input, int lengthBB, double multBB, int lengthKC, double multKC, Brush brushUpEnd, Brush brushDownBegin, Brush brushDownEnd, Brush isSqueeze, Brush noSqueeze)
		{
			if (cacheSqueezeMomentumIndicator != null)
				for (int idx = 0; idx < cacheSqueezeMomentumIndicator.Length; idx++)
					if (cacheSqueezeMomentumIndicator[idx] != null && cacheSqueezeMomentumIndicator[idx].LengthBB == lengthBB && cacheSqueezeMomentumIndicator[idx].MultBB == multBB && cacheSqueezeMomentumIndicator[idx].LengthKC == lengthKC && cacheSqueezeMomentumIndicator[idx].MultKC == multKC && cacheSqueezeMomentumIndicator[idx].BrushUpEnd == brushUpEnd && cacheSqueezeMomentumIndicator[idx].BrushDownBegin == brushDownBegin && cacheSqueezeMomentumIndicator[idx].BrushDownEnd == brushDownEnd && cacheSqueezeMomentumIndicator[idx].IsSqueeze == isSqueeze && cacheSqueezeMomentumIndicator[idx].NoSqueeze == noSqueeze && cacheSqueezeMomentumIndicator[idx].EqualsInput(input))
						return cacheSqueezeMomentumIndicator[idx];
			return CacheIndicator<AUN_Indi.SqueezeMomentumIndicator>(new AUN_Indi.SqueezeMomentumIndicator(){ LengthBB = lengthBB, MultBB = multBB, LengthKC = lengthKC, MultKC = multKC, BrushUpEnd = brushUpEnd, BrushDownBegin = brushDownBegin, BrushDownEnd = brushDownEnd, IsSqueeze = isSqueeze, NoSqueeze = noSqueeze }, input, ref cacheSqueezeMomentumIndicator);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AUN_Indi.SqueezeMomentumIndicator SqueezeMomentumIndicator(int lengthBB, double multBB, int lengthKC, double multKC, Brush brushUpEnd, Brush brushDownBegin, Brush brushDownEnd, Brush isSqueeze, Brush noSqueeze)
		{
			return indicator.SqueezeMomentumIndicator(Input, lengthBB, multBB, lengthKC, multKC, brushUpEnd, brushDownBegin, brushDownEnd, isSqueeze, noSqueeze);
		}

		public Indicators.AUN_Indi.SqueezeMomentumIndicator SqueezeMomentumIndicator(ISeries<double> input , int lengthBB, double multBB, int lengthKC, double multKC, Brush brushUpEnd, Brush brushDownBegin, Brush brushDownEnd, Brush isSqueeze, Brush noSqueeze)
		{
			return indicator.SqueezeMomentumIndicator(input, lengthBB, multBB, lengthKC, multKC, brushUpEnd, brushDownBegin, brushDownEnd, isSqueeze, noSqueeze);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AUN_Indi.SqueezeMomentumIndicator SqueezeMomentumIndicator(int lengthBB, double multBB, int lengthKC, double multKC, Brush brushUpEnd, Brush brushDownBegin, Brush brushDownEnd, Brush isSqueeze, Brush noSqueeze)
		{
			return indicator.SqueezeMomentumIndicator(Input, lengthBB, multBB, lengthKC, multKC, brushUpEnd, brushDownBegin, brushDownEnd, isSqueeze, noSqueeze);
		}

		public Indicators.AUN_Indi.SqueezeMomentumIndicator SqueezeMomentumIndicator(ISeries<double> input , int lengthBB, double multBB, int lengthKC, double multKC, Brush brushUpEnd, Brush brushDownBegin, Brush brushDownEnd, Brush isSqueeze, Brush noSqueeze)
		{
			return indicator.SqueezeMomentumIndicator(input, lengthBB, multBB, lengthKC, multKC, brushUpEnd, brushDownBegin, brushDownEnd, isSqueeze, noSqueeze);
		}
	}
}

#endregion
