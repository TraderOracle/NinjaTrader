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

#endregion



#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		
		private LizardIndicators.amaMACDBBLines[] cacheamaMACDBBLines;
		private MACDBBLINES[] cacheMACDBBLINES;
		private VWAP[] cacheVWAP;
		private LizardIndicators.amaStdDev[] cacheamaStdDev;

		
		public LizardIndicators.amaMACDBBLines amaMACDBBLines(int fast, int slow, int bandPeriod, double stdDevMultiplier)
		{
			return amaMACDBBLines(Input, fast, slow, bandPeriod, stdDevMultiplier);
		}

		public MACDBBLINES MACDBBLINES(int fast, int slow, int smoothing, Brush upColor, Brush dwColor, int stDev, int period, int bandFillOpacity, Brush bandFillColor, bool showCross, Brush crossAlert, Brush crossAlert2)
		{
			return MACDBBLINES(Input, fast, slow, smoothing, upColor, dwColor, stDev, period, bandFillOpacity, bandFillColor, showCross, crossAlert, crossAlert2);
		}

		public VWAP VWAP()
		{
			return VWAP(Input);
		}

		public LizardIndicators.amaStdDev amaStdDev(int period)
		{
			return amaStdDev(Input, period);
		}


		
		public LizardIndicators.amaMACDBBLines amaMACDBBLines(ISeries<double> input, int fast, int slow, int bandPeriod, double stdDevMultiplier)
		{
			if (cacheamaMACDBBLines != null)
				for (int idx = 0; idx < cacheamaMACDBBLines.Length; idx++)
					if (cacheamaMACDBBLines[idx].Fast == fast && cacheamaMACDBBLines[idx].Slow == slow && cacheamaMACDBBLines[idx].BandPeriod == bandPeriod && cacheamaMACDBBLines[idx].StdDevMultiplier == stdDevMultiplier && cacheamaMACDBBLines[idx].EqualsInput(input))
						return cacheamaMACDBBLines[idx];
			return CacheIndicator<LizardIndicators.amaMACDBBLines>(new LizardIndicators.amaMACDBBLines(){ Fast = fast, Slow = slow, BandPeriod = bandPeriod, StdDevMultiplier = stdDevMultiplier }, input, ref cacheamaMACDBBLines);
		}

		public MACDBBLINES MACDBBLINES(ISeries<double> input, int fast, int slow, int smoothing, Brush upColor, Brush dwColor, int stDev, int period, int bandFillOpacity, Brush bandFillColor, bool showCross, Brush crossAlert, Brush crossAlert2)
		{
			if (cacheMACDBBLINES != null)
				for (int idx = 0; idx < cacheMACDBBLINES.Length; idx++)
					if (cacheMACDBBLINES[idx].Fast == fast && cacheMACDBBLINES[idx].Slow == slow && cacheMACDBBLINES[idx].Smoothing == smoothing && cacheMACDBBLINES[idx].UpColor == upColor && cacheMACDBBLINES[idx].DwColor == dwColor && cacheMACDBBLINES[idx].StDev == stDev && cacheMACDBBLINES[idx].Period == period && cacheMACDBBLINES[idx].BandFillOpacity == bandFillOpacity && cacheMACDBBLINES[idx].BandFillColor == bandFillColor && cacheMACDBBLINES[idx].ShowCross == showCross && cacheMACDBBLINES[idx].CrossAlert == crossAlert && cacheMACDBBLINES[idx].CrossAlert2 == crossAlert2 && cacheMACDBBLINES[idx].EqualsInput(input))
						return cacheMACDBBLINES[idx];
			return CacheIndicator<MACDBBLINES>(new MACDBBLINES(){ Fast = fast, Slow = slow, Smoothing = smoothing, UpColor = upColor, DwColor = dwColor, StDev = stDev, Period = period, BandFillOpacity = bandFillOpacity, BandFillColor = bandFillColor, ShowCross = showCross, CrossAlert = crossAlert, CrossAlert2 = crossAlert2 }, input, ref cacheMACDBBLINES);
		}

		public VWAP VWAP(ISeries<double> input)
		{
			if (cacheVWAP != null)
				for (int idx = 0; idx < cacheVWAP.Length; idx++)
					if ( cacheVWAP[idx].EqualsInput(input))
						return cacheVWAP[idx];
			return CacheIndicator<VWAP>(new VWAP(), input, ref cacheVWAP);
		}

		public LizardIndicators.amaStdDev amaStdDev(ISeries<double> input, int period)
		{
			if (cacheamaStdDev != null)
				for (int idx = 0; idx < cacheamaStdDev.Length; idx++)
					if (cacheamaStdDev[idx].Period == period && cacheamaStdDev[idx].EqualsInput(input))
						return cacheamaStdDev[idx];
			return CacheIndicator<LizardIndicators.amaStdDev>(new LizardIndicators.amaStdDev(){ Period = period }, input, ref cacheamaStdDev);
		}

	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		
		public Indicators.LizardIndicators.amaMACDBBLines amaMACDBBLines(int fast, int slow, int bandPeriod, double stdDevMultiplier)
		{
			return indicator.amaMACDBBLines(Input, fast, slow, bandPeriod, stdDevMultiplier);
		}

		public Indicators.MACDBBLINES MACDBBLINES(int fast, int slow, int smoothing, Brush upColor, Brush dwColor, int stDev, int period, int bandFillOpacity, Brush bandFillColor, bool showCross, Brush crossAlert, Brush crossAlert2)
		{
			return indicator.MACDBBLINES(Input, fast, slow, smoothing, upColor, dwColor, stDev, period, bandFillOpacity, bandFillColor, showCross, crossAlert, crossAlert2);
		}

		public Indicators.VWAP VWAP()
		{
			return indicator.VWAP(Input);
		}

		public Indicators.LizardIndicators.amaStdDev amaStdDev(int period)
		{
			return indicator.amaStdDev(Input, period);
		}


		
		public Indicators.LizardIndicators.amaMACDBBLines amaMACDBBLines(ISeries<double> input , int fast, int slow, int bandPeriod, double stdDevMultiplier)
		{
			return indicator.amaMACDBBLines(input, fast, slow, bandPeriod, stdDevMultiplier);
		}

		public Indicators.MACDBBLINES MACDBBLINES(ISeries<double> input , int fast, int slow, int smoothing, Brush upColor, Brush dwColor, int stDev, int period, int bandFillOpacity, Brush bandFillColor, bool showCross, Brush crossAlert, Brush crossAlert2)
		{
			return indicator.MACDBBLINES(input, fast, slow, smoothing, upColor, dwColor, stDev, period, bandFillOpacity, bandFillColor, showCross, crossAlert, crossAlert2);
		}

		public Indicators.VWAP VWAP(ISeries<double> input )
		{
			return indicator.VWAP(input);
		}

		public Indicators.LizardIndicators.amaStdDev amaStdDev(ISeries<double> input , int period)
		{
			return indicator.amaStdDev(input, period);
		}
	
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		
		public Indicators.LizardIndicators.amaMACDBBLines amaMACDBBLines(int fast, int slow, int bandPeriod, double stdDevMultiplier)
		{
			return indicator.amaMACDBBLines(Input, fast, slow, bandPeriod, stdDevMultiplier);
		}

		public Indicators.MACDBBLINES MACDBBLINES(int fast, int slow, int smoothing, Brush upColor, Brush dwColor, int stDev, int period, int bandFillOpacity, Brush bandFillColor, bool showCross, Brush crossAlert, Brush crossAlert2)
		{
			return indicator.MACDBBLINES(Input, fast, slow, smoothing, upColor, dwColor, stDev, period, bandFillOpacity, bandFillColor, showCross, crossAlert, crossAlert2);
		}

		public Indicators.VWAP VWAP()
		{
			return indicator.VWAP(Input);
		}

		public Indicators.LizardIndicators.amaStdDev amaStdDev(int period)
		{
			return indicator.amaStdDev(Input, period);
		}


		
		public Indicators.LizardIndicators.amaMACDBBLines amaMACDBBLines(ISeries<double> input , int fast, int slow, int bandPeriod, double stdDevMultiplier)
		{
			return indicator.amaMACDBBLines(input, fast, slow, bandPeriod, stdDevMultiplier);
		}

		public Indicators.MACDBBLINES MACDBBLINES(ISeries<double> input , int fast, int slow, int smoothing, Brush upColor, Brush dwColor, int stDev, int period, int bandFillOpacity, Brush bandFillColor, bool showCross, Brush crossAlert, Brush crossAlert2)
		{
			return indicator.MACDBBLINES(input, fast, slow, smoothing, upColor, dwColor, stDev, period, bandFillOpacity, bandFillColor, showCross, crossAlert, crossAlert2);
		}

		public Indicators.VWAP VWAP(ISeries<double> input )
		{
			return indicator.VWAP(input);
		}

		public Indicators.LizardIndicators.amaStdDev amaStdDev(ISeries<double> input , int period)
		{
			return indicator.amaStdDev(input, period);
		}

	}
}

#endregion
