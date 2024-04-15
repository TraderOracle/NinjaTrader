//+----------------------------------------------------------------------------------------------+
//| Copyright © <2021>  <LizardIndicators.com - powered by AlderLab UG>
//
//| This program is free software: you can redistribute it and/or modify
//| it under the terms of the GNU General Public License as published by
//| the Free Software Foundation, either version 3 of the License, or
//| any later version.
//|
//| This program is distributed in the hope that it will be useful,
//| but WITHOUT ANY WARRANTY; without even the implied warranty of
//| MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//| GNU General Public License for more details.
//|
//| By installing this software you confirm acceptance of the GNU
//| General Public License terms. You may find a copy of the license
//| here; http://www.gnu.org/licenses/
//+----------------------------------------------------------------------------------------------+

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
using NinjaTrader.NinjaScript.Indicators.LizardIndicators;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// This namespace holds indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.LizardIndicators
{
	/// <summary>
	/// The Multi TSI is based on the True Strength Index, but comes with additional algorithmic options for smoothing momentum and the signal line.
	/// </summary>
	/// 
	[Gui.CategoryOrder("Algorithmic Options", 1000100)]
	[Gui.CategoryOrder("Input Parameters", 1000200)]
	[Gui.CategoryOrder("Threshold Lines", 1000300)]
	[Gui.CategoryOrder("Display Options", 1000400)]
	[Gui.CategoryOrder("Paint Bars", 8000100)]
	[Gui.CategoryOrder("Sound Alerts", 8000200)]
	[Gui.CategoryOrder("Version", 8000300)]
	[TypeConverter("NinjaTrader.NinjaScript.Indicators.amaMultiTSITypeConverter")]
	public class amaMultiTSI : Indicator
	{
        private int                         momentumPeriod              = 1;
        private int                         smooth1                     = 20;
        private int                         smooth2                     = 9;
        private int                         smooth3                     = 1;
        private int                         signalPeriod                = 5;
		private int							displacement				= 0;
 		private int							totalBarsRequiredToPlot		= 0;
		private double						upperThreshold				= 25;
		private double						lowerThreshold				= -25;
       	private double                      epsilon                     = 0.0;
        private amaMultiTSISmoothType       smoothType                  = amaMultiTSISmoothType.EMA;
        private amaMultiTSISmoothType       signalType                  = amaMultiTSISmoothType.EMA;
        private amaMultiTSITrendDefinition  trendDefinition             = amaMultiTSITrendDefinition.Signal_Slope;
        private bool                        showTSI                     = true;
        private bool                        showSignal                  = true;
        private bool                        showHistogram               = true;
        private bool                        showPaintBars               = true;
        private bool                        soundAlerts                 = false;
		private bool						autoBarWidth				= true;
		private bool						calculateFromPriceData		= true;
        private bool                        candles                     = true;
		private SessionIterator				sessionIterator				= null;
		private Brush						tsiBrush					= Brushes.ForestGreen;
		private Brush						signalBrush					= Brushes.DarkOrange;
		private Brush						histogramBrush				= Brushes.Maroon;
		private Brush						upBrushUp					= Brushes.Lime;
		private Brush						upBrushDown					= Brushes.ForestGreen;
		private Brush						downBrushUp					= Brushes.Firebrick;
		private Brush						downBrushDown				= Brushes.Red;
		private Brush						neutralBrushUp				= Brushes.Goldenrod;
		private Brush						neutralBrushDown			= Brushes.Yellow;
		private Brush						upBrushOutline				= Brushes.Black;
		private Brush						downBrushOutline			= Brushes.Black;
		private Brush						neutralBrushOutline			= Brushes.Black;
		private Brush						upperlineBrush				= Brushes.Navy;
		private Brush						zerolineBrush				= Brushes.Navy;
		private Brush						lowerlineBrush				= Brushes.Navy;
		private Brush						trendBrush					= null;
		private Brush						priorTrendBrush				= null;
		private Brush						transparentBrush			= Brushes.Transparent;
		private Brush						alertBackBrush				= Brushes.Black;
		private int							barOpacity					= 3;
		private int							histogramOpacity			= 70;
		private int 						plot0Width 					= 2;
		private PlotStyle 					plot0Style					= PlotStyle.Line;
		private DashStyleHelper				dash0Style					= DashStyleHelper.Solid;
		private int 						plot1Width 					= 2;
		private PlotStyle 					plot1Style					= PlotStyle.Line;
		private DashStyleHelper				dash1Style					= DashStyleHelper.Solid;
		private int 						plot2Width 					= 3;
		private PlotStyle 					plot2Style					= PlotStyle.Bar;
		private DashStyleHelper				dash2Style					= DashStyleHelper.Solid;
		private int 						line0Width 					= 1;
		private DashStyleHelper				line0Style					= DashStyleHelper.Solid;
		private int 						line1Width 					= 1;
		private DashStyleHelper				line1Style					= DashStyleHelper.Solid;
		private int 						line2Width 					= 1;
		private DashStyleHelper				line2Style					= DashStyleHelper.Solid;
		private int							rearmTime					= 30;
		private string 						newUptrend					= "newuptrend.wav";
		private string 						newDowntrend				= "newdowntrend.wav";
		private string						newNeutraltrend				= "newneutraltrend.wav";
		private string						pathNewUptrend				= "";
		private string						pathNewDowntrend			= "";
		private string						pathNewNeutraltrend			= "";
		private string						versionString				= "v 2.0  -  April 7th, 2024";
		private Series<double>				absmom;
		private Series<double>				trend;
		private ISeries<double>				dNum;
		private ISeries<double>				dDen;
		private ISeries<double>				signal;
		private Momentum					mom;
		
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= "\r\nThe Multi TSI is based on the True Strength Index, but comes with additional algorithmic options for smoothing momentum and the signal line.";
				Name						= "amaMultiTSI";
				IsSuspendedWhileInactive	= false;
				ArePlotsConfigurable		= false;
				AreLinesConfigurable		= false;
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "TSI");	
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "Signal");
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "HistogramG");
                AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "HistogramR");
                AddLine(Brushes.DarkTurquoise, upperThreshold, "Upper");
				AddLine(Brushes.DarkTurquoise, 0, "Zeroline");
				AddLine(Brushes.DarkTurquoise, lowerThreshold, "Lower");
			}
			else if (State == State.Configure)
			{
				displacement = Displacement;
				BarsRequiredToPlot = Math.Max(smooth1 + smooth2 + smooth3 + signalPeriod, -displacement);
				totalBarsRequiredToPlot = Math.Max(BarsRequiredToPlot, BarsRequiredToPlot + displacement);
				if(showTSI)
				{	
					Plots[0].Brush 	= tsiBrush; 
					Plots[0].PlotStyle = plot0Style;
					Plots[0].DashStyleHelper = dash0Style;
					Plots[0].Width 	= plot0Width; 
				}
				else
					Plots[0].Brush = Brushes.Transparent;
				if(showSignal)
				{	
					Plots[1].Brush 	= signalBrush; 
					Plots[1].PlotStyle = plot1Style;
					Plots[1].DashStyleHelper = dash1Style;
					Plots[1].Width 	= plot1Width; 
				}	
				else
					Plots[1].Brush = Brushes.Transparent;
				if(showHistogram)
				{	
					Plots[2].Brush 	= histogramBrush;
					Plots[2].Opacity = histogramOpacity;
                    Plots[3].Opacity = histogramOpacity;
                    Plots[2].PlotStyle = plot2Style;
                    Plots[3].PlotStyle = plot2Style;
                    Plots[2].Width 	= plot2Width;
					Plots[2].DashStyleHelper = dash2Style;
                    Plots[2].AutoWidth = true;
                    Plots[3].AutoWidth = true;
				}
				else
					Plots[2].Brush = Brushes.Transparent;
				Lines[0].Value 	= upperThreshold;
				Lines[0].Brush 	= upperlineBrush; 
				Lines[0].DashStyleHelper = line0Style;
				Lines[0].Width 	= line0Width; 
				Lines[1].Brush 	= zerolineBrush; 
				Lines[1].DashStyleHelper = line1Style;
				Lines[1].Width 	= line1Width; 
				Lines[2].Value 	= lowerThreshold;
				Lines[2].Brush 	= lowerlineBrush;
				Lines[2].DashStyleHelper = line2Style;
				Lines[2].Width 	= line2Width;
			}	
			else if (State == State.DataLoaded)
			{
				absmom = new Series<double>(this, MaximumBarsLookBack.Infinite);
				trend = new Series<double>(this, MaximumBarsLookBack.Infinite);
				mom = Momentum(momentumPeriod);
				if(Input is PriceSeries)
					calculateFromPriceData = true;
				else
					calculateFromPriceData = false;
				switch (smoothType)
				{
					case amaMultiTSISmoothType.Median:
						dNum = amaMovingMedian(amaMovingMedian(amaMovingMedian(mom, smooth1), smooth2), smooth3);
						dDen = amaMovingMedian(amaMovingMedian(amaMovingMedian(absmom, smooth1), smooth2), smooth3);
						break;
					case amaMultiTSISmoothType.Adaptive_Laguerre: 
						dNum = amaAdaptiveLaguerreFilter(amaAdaptiveLaguerreFilter(amaAdaptiveLaguerreFilter(mom, smooth1), smooth2), smooth3);
						dDen = amaAdaptiveLaguerreFilter(amaAdaptiveLaguerreFilter(amaAdaptiveLaguerreFilter(absmom, smooth1), smooth2), smooth3);
						break;
					case amaMultiTSISmoothType.ADXVMA: 
						dNum = amaADXVMA(amaADXVMA(amaADXVMA(mom, smooth1), smooth2), smooth3);
						dDen = amaADXVMA(amaADXVMA(amaADXVMA(absmom, smooth1), smooth2), smooth3);
						break;
					case amaMultiTSISmoothType.Butterworth_2: 
						dNum = amaButterworthFilter(amaButterworthFilter(amaButterworthFilter(mom, 2, smooth1), 2, smooth2), 2, smooth3);
						dDen = amaButterworthFilter(amaButterworthFilter(amaButterworthFilter(absmom, 2, smooth1), 2, smooth2), 2, smooth3);
						break;
					case amaMultiTSISmoothType.Butterworth_3: 
						dNum = amaButterworthFilter(amaButterworthFilter(amaButterworthFilter(mom, 3, smooth1), 3, smooth2), 3, smooth3);
						dDen = amaButterworthFilter(amaButterworthFilter(amaButterworthFilter(absmom, 3, smooth1), 3, smooth2), 3, smooth3);
						break;
					case amaMultiTSISmoothType.DEMA: 
						dNum = DEMA(DEMA(DEMA(mom, smooth1), smooth2), smooth3);
						dDen = DEMA(DEMA(DEMA(absmom, smooth1), smooth2), smooth3);
						break;
					case amaMultiTSISmoothType.Distant_Coefficient_Filter: 
						dNum = amaDistantCoefficientFilter(amaDistantCoefficientFilter(amaDistantCoefficientFilter(mom, smooth1), smooth2), smooth3);
						dDen = amaDistantCoefficientFilter(amaDistantCoefficientFilter(amaDistantCoefficientFilter(absmom, smooth1), smooth2), smooth3);
						break;
					case amaMultiTSISmoothType.DWMA: 
						dNum = amaDWMA(amaDWMA(amaDWMA(mom, smooth1), smooth2), smooth3);
						dDen = amaDWMA(amaDWMA(amaDWMA(absmom, smooth1), smooth2), smooth3);
						break;
					case amaMultiTSISmoothType.EHMA: 
						dNum = amaEHMA(amaEHMA(amaEHMA(mom, smooth1), smooth2), smooth3);
						dDen = amaEHMA(amaEHMA(amaEHMA(absmom, smooth1), smooth2), smooth3);
						break;
					case amaMultiTSISmoothType.EMA: 
						dNum = EMA(EMA(EMA(mom, smooth1), smooth2), smooth3);
						dDen = EMA(EMA(EMA(absmom, smooth1), smooth2), smooth3);
						break;
					case amaMultiTSISmoothType.Gauss_2: 
						dNum = amaGaussianFilter(amaGaussianFilter(amaGaussianFilter(mom, 2, smooth1), 2, smooth2), 2, smooth3);
						dDen = amaGaussianFilter(amaGaussianFilter(amaGaussianFilter(absmom, 2, smooth1), 2, smooth2), 2, smooth3);
						break;
					case amaMultiTSISmoothType.Gauss_3: 
						dNum = amaGaussianFilter(amaGaussianFilter(amaGaussianFilter(mom, 3, smooth1), 3, smooth2), 3, smooth3);
						dDen = amaGaussianFilter(amaGaussianFilter(amaGaussianFilter(absmom, 3, smooth1), 3, smooth2), 3, smooth3);
						break;
					case amaMultiTSISmoothType.Gauss_4: 
						dNum = amaGaussianFilter(amaGaussianFilter(amaGaussianFilter(mom, 4, smooth1), 4, smooth2), 4, smooth3);
						dDen = amaGaussianFilter(amaGaussianFilter(amaGaussianFilter(absmom, 4, smooth1), 4, smooth2), 4, smooth3);
						break;
					case amaMultiTSISmoothType.HMA:
						if(smooth1 == 1 && smooth2 == 1 && smooth3 == 1)
						{	
							dNum = mom;
							dDen = absmom;
						}	
						else if(smooth1 == 1 && smooth2 == 1)
						{	
							dNum = HMA(mom, smooth3);
							dDen = HMA(absmom, smooth3);
						}	
						else if(smooth1 == 1 && smooth3 == 1)
						{	
							dNum = HMA(mom, smooth2);
							dDen = HMA(absmom, smooth2);
						}	
						else if(smooth2 == 1 && smooth3 == 1)
						{	
							dNum = HMA(mom, smooth1);
							dDen = HMA(absmom, smooth1);
						}	
						else if(smooth1 == 1) 
						{	
							dNum = HMA(HMA(mom, smooth2), smooth3);
							dDen = HMA(HMA(absmom, smooth2), smooth3);
						}	
						else if(smooth2 == 1)
						{	
							dNum = HMA(HMA(mom, smooth1), smooth3);
							dDen = HMA(HMA(absmom, smooth1), smooth3);
						}	
						else if(smooth3 == 1)
						{	
							dNum = HMA(HMA(mom, smooth1), smooth2);
							dDen = HMA(HMA(absmom, smooth1), smooth2);
						}
						else
						{	
							dNum = HMA(HMA(HMA(mom, smooth1), smooth2), smooth3);
							dDen = HMA(HMA(HMA(absmom, smooth1), smooth2), smooth3);
						}
						break;
					case amaMultiTSISmoothType.HoltEMA: 
						dNum = amaHoltEMA(amaHoltEMA(amaHoltEMA(mom, smooth1, smooth1), smooth2, smooth2), smooth3, smooth3);
						dDen = amaHoltEMA(amaHoltEMA(amaHoltEMA(absmom, smooth1, smooth1), smooth2, smooth2), smooth3, smooth3);
						break;
					case amaMultiTSISmoothType.Laguerre: 
						dNum = amaLaguerreFilter(amaLaguerreFilter(amaLaguerreFilter(mom, smooth1), smooth2), smooth3);
						dDen = amaLaguerreFilter(amaLaguerreFilter(amaLaguerreFilter(absmom, smooth1), smooth2), smooth3);
						break;
					case amaMultiTSISmoothType.LinReg: 
						if(smooth1 == 1 && smooth2 == 1 && smooth3 == 1)
						{	
							dNum = mom;
							dDen = absmom;
						}	
						else if(smooth1 == 1 && smooth2 == 1)
						{	
							dNum = LinReg(mom, smooth3);
							dDen = LinReg(absmom, smooth3);
						}	
						else if(smooth1 == 1 && smooth3 == 1)
						{	
							dNum = LinReg(mom, smooth2);
							dDen = LinReg(absmom, smooth2);
						}	
						else if(smooth2 == 1 && smooth3 == 1)
						{	
							dNum = LinReg(mom, smooth1);
							dDen = LinReg(absmom, smooth1);
						}	
						else if(smooth1 == 1) 
						{	
							dNum = LinReg(LinReg(mom, smooth2), smooth3);
							dDen = LinReg(LinReg(absmom, smooth2), smooth3);
						}	
						else if(smooth2 == 1)
						{	
							dNum = LinReg(LinReg(mom, smooth1), smooth3);
							dDen = LinReg(LinReg(absmom, smooth1), smooth3);
						}	
						else if(smooth3 == 1)
						{	
							dNum = LinReg(LinReg(mom, smooth1), smooth2);
							dDen = LinReg(LinReg(absmom, smooth1), smooth2);
						}
						else
						{	
							dNum = LinReg(LinReg(LinReg(mom, smooth1), smooth2), smooth3);
							dDen = LinReg(LinReg(LinReg(absmom, smooth1), smooth2), smooth3);
						}
						break;
					case amaMultiTSISmoothType.SMA: 
						dNum = SMA(SMA(SMA(mom, smooth1), smooth2), smooth3);
						dDen = SMA(SMA(SMA(absmom, smooth1), smooth2), smooth3);
						break;
					case amaMultiTSISmoothType.SuperSmoother_2: 
						dNum = amaSuperSmoother(amaSuperSmoother(amaSuperSmoother(mom, 2, smooth1), 2, smooth2), 2, smooth3);
						dDen = amaSuperSmoother(amaSuperSmoother(amaSuperSmoother(absmom, 2, smooth1), 2, smooth2), 2, smooth3);
						break;
					case amaMultiTSISmoothType.SuperSmoother_3: 
						dNum = amaSuperSmoother(amaSuperSmoother(amaSuperSmoother(mom, 3, smooth1), 3, smooth2), 3, smooth3);
						dDen = amaSuperSmoother(amaSuperSmoother(amaSuperSmoother(absmom, 3, smooth1), 3, smooth2), 3, smooth3);
						break;
					case amaMultiTSISmoothType.SWMA: 
						dNum = amaSWMA(amaSWMA(amaSWMA(mom, smooth1), smooth2), smooth3);
						dDen = amaSWMA(amaSWMA(amaSWMA(absmom, smooth1), smooth2), smooth3);
						break;
					case amaMultiTSISmoothType.TEMA: 
						dNum = TEMA(TEMA(TEMA(mom, smooth1), smooth2), smooth3);
						dDen = TEMA(TEMA(TEMA(absmom, smooth1), smooth2), smooth3);
						break;
					case amaMultiTSISmoothType.Tillson_T3: 
						dNum = amaTillsonT3(amaTillsonT3(amaTillsonT3(mom, amaT3CalcMode.Tillson, smooth1, 0.7), amaT3CalcMode.Tillson, smooth2, 0.7), amaT3CalcMode.Tillson, smooth3, 0.7);
						dDen = amaTillsonT3(amaTillsonT3(amaTillsonT3(absmom, amaT3CalcMode.Tillson, smooth1, 0.7), amaT3CalcMode.Tillson, smooth2, 0.7), amaT3CalcMode.Tillson, smooth3, 0.7);
						break;
					case amaMultiTSISmoothType.TMA: 
						dNum = TMA(TMA(TMA(mom, smooth1), smooth2), smooth3);
						dDen = TMA(TMA(TMA(absmom, smooth1), smooth2), smooth3);
						break;
					case amaMultiTSISmoothType.TWMA: 
						dNum = amaTWMA(amaTWMA(amaTWMA(mom, smooth1), smooth2), smooth3);
						dDen = amaTWMA(amaTWMA(amaTWMA(absmom, smooth1), smooth2), smooth3);
						break;
					case amaMultiTSISmoothType.Wilder: 
						dNum = EMA(EMA(EMA(mom, 2*smooth1 - 1), 2*smooth2 - 1), 2*smooth3 - 1);
						dDen = EMA(EMA(EMA(absmom, 2*smooth1 - 1), 2*smooth2 - 1), 2*smooth3 - 1);
						break;
					case amaMultiTSISmoothType.WMA: 
						dNum = WMA(WMA(WMA(mom, smooth1), smooth2), smooth3);
						dDen = WMA(WMA(WMA(absmom, smooth1), smooth2), smooth3);
						break;
					case amaMultiTSISmoothType.ZerolagTEMA: 
						dNum = amaZerolagTEMA(amaZerolagTEMA(amaZerolagTEMA(mom, smooth1), smooth2), smooth3);
						dDen = amaZerolagTEMA(amaZerolagTEMA(amaZerolagTEMA(absmom, smooth1), smooth2), smooth3);
						break;
					case amaMultiTSISmoothType.ZLEMA: 
						if(smooth1 == 1 && smooth2 == 1 && smooth3 == 1)
						{	
							dNum = mom;
							dDen = absmom;
						}	
						else if(smooth1 == 1 && smooth2 == 1)
						{	
							dNum = ZLEMA(mom, smooth3);
							dDen = ZLEMA(absmom, smooth3);
						}	
						else if(smooth1 == 1 && smooth3 == 1)
						{	
							dNum = ZLEMA(mom, smooth2);
							dDen = ZLEMA(absmom, smooth2);
						}	
						else if(smooth2 == 1 && smooth3 == 1)
						{	
							dNum = ZLEMA(mom, smooth1);
							dDen = ZLEMA(absmom, smooth1);
						}	
						else if(smooth1 == 1) 
						{	
							dNum = ZLEMA(ZLEMA(mom, smooth2), smooth3);
							dDen = ZLEMA(ZLEMA(absmom, smooth2), smooth3);
						}	
						else if(smooth2 == 1)
						{	
							dNum = ZLEMA(ZLEMA(mom, smooth1), smooth3);
							dDen = ZLEMA(ZLEMA(absmom, smooth1), smooth3);
						}	
						else if(smooth3 == 1)
						{	
							dNum = ZLEMA(ZLEMA(mom, smooth1), smooth2);
							dDen = ZLEMA(ZLEMA(absmom, smooth1), smooth2);
						}
						else
						{	
							dNum = ZLEMA(ZLEMA(ZLEMA(mom, smooth1), smooth2), smooth3);
							dDen = ZLEMA(ZLEMA(ZLEMA(absmom, smooth1), smooth2), smooth3);
						}
						break;
				}
				switch (signalType)
				{
					case amaMultiTSISmoothType.Median: 
						signal = amaMovingMedian(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.Adaptive_Laguerre: 
						signal = amaAdaptiveLaguerreFilter(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.ADXVMA: 
						signal = amaADXVMA(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.Butterworth_2: 
						signal = amaButterworthFilter(TSI, 2, signalPeriod);
						break;
					case amaMultiTSISmoothType.Butterworth_3: 
						signal = amaButterworthFilter(TSI, 3, signalPeriod);
						break;
					case amaMultiTSISmoothType.DEMA: 
						signal = DEMA(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.Distant_Coefficient_Filter: 
						signal = amaDistantCoefficientFilter(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.DWMA: 
						signal = amaDWMA(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.EHMA: 
						signal = amaEHMA(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.EMA: 
						signal = EMA(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.Gauss_2: 
						signal = amaGaussianFilter(TSI, 2, signalPeriod);
						break;
					case amaMultiTSISmoothType.Gauss_3: 
						signal = amaGaussianFilter(TSI, 3, signalPeriod);
						break;
					case amaMultiTSISmoothType.Gauss_4: 
						signal = amaGaussianFilter(TSI, 4, signalPeriod);
						break;
					case amaMultiTSISmoothType.HMA:
						if(signalPeriod == 1)
							signal = TSI;
						else
							signal = HMA(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.HoltEMA: 
						signal = amaHoltEMA(TSI, signalPeriod, signalPeriod);
						break;
					case amaMultiTSISmoothType.Laguerre: 
						signal = amaLaguerreFilter(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.LinReg:
						if(signalPeriod == 1)
							signal = TSI;
						else
							signal = LinReg(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.SMA: 
						signal = SMA(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.SuperSmoother_2: 
						signal = amaSuperSmoother(TSI, 2, signalPeriod);
						break;
					case amaMultiTSISmoothType.SuperSmoother_3: 
						signal = amaSuperSmoother(TSI, 3, signalPeriod);
						break;
					case amaMultiTSISmoothType.SWMA: 
						signal = amaSWMA(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.TEMA: 
						signal = TEMA(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.Tillson_T3: 
						signal = amaTillsonT3(TSI, amaT3CalcMode.Tillson, signalPeriod, 0.7);
						break;
					case amaMultiTSISmoothType.TMA: 
						signal = TMA(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.TWMA: 
						signal = amaTWMA(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.Wilder: 
						signal = EMA(TSI, 2*signalPeriod - 1);
						break;
					case amaMultiTSISmoothType.WMA: 
						signal = WMA(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.ZerolagTEMA: 
						signal = amaZerolagTEMA(TSI, signalPeriod);
						break;
					case amaMultiTSISmoothType.ZLEMA: 
						signal = ZLEMA(TSI, signalPeriod);
						break;
				}
		    	sessionIterator = new SessionIterator(Bars);
			}
		  	else if (State == State.Historical)
		 	{
				epsilon = TickSize / (10*(smooth1 + smooth2 + smooth3));
				pathNewUptrend = string.Format( @"{0}sounds\{1}", NinjaTrader.Core.Globals.InstallDir, newUptrend);
				pathNewDowntrend = string.Format( @"{0}sounds\{1}", NinjaTrader.Core.Globals.InstallDir, newDowntrend);
				pathNewNeutraltrend = string.Format( @"{0}sounds\{1}", NinjaTrader.Core.Globals.InstallDir, newNeutraltrend);
		  	}
		}

		protected override void OnBarUpdate()
		{
			if(CurrentBar < 1)
			{
				absmom[0] = 0.0;
				TSI[0] = 0.0;
				Signal[0] = 0.0;
				HistogramG[0] = 0.0;
                HistogramR[0] = 0.0;
                return;			
			}
			absmom[0] = Math.Abs(mom[0]);
			if(dDen[0] < epsilon)
				TSI[0] = CurrentBar == 0 ? 0 : TSI[1];
			else	
				TSI[0] = Math.Min(100, Math.Max(-100, 100 * dNum[0]/dDen[0]));
			Signal[0] = signal[0];

            Plots[2].Brush = Brushes.Lime;
            Plots[3].Brush = Brushes.Red;

            if ((TSI[0] - Signal[0]) > 0)
			{
                HistogramG[0] = TSI[0] - Signal[0];
				HistogramR[0] = 0;
            }
            else
			{
                HistogramR[0] = (TSI[0] - Signal[0]) * -1;
				HistogramG[0] = 0;
            }

            if (CurrentBar < BarsRequiredToPlot)
			{	
				trend[0] = 0.0;
				return;
			}
			
			if(trendDefinition == amaMultiTSITrendDefinition.Signal_Slope && calculateFromPriceData)
			{
				if(Signal[0] > Signal[1] && Signal[0] > lowerThreshold)
				{
					if(Input[0] > High[1] || Median[0] > High[1])
						trend[0] = 1;
					else
						trend[0] = trend[1];
				}
				else if (Signal[0] < Signal[1] && Signal[0] < upperThreshold)
				{
					if(Input[0] < Low[1] || Median[0] < Low[1])
						trend[0] = -1;
					else
						trend[0] = trend[1];
				}
				else
					trend[0] = 0;
			}
			else if(trendDefinition == amaMultiTSITrendDefinition.Signal_Slope)
			{
				if(Signal[0] > Signal[1] && Signal[0] > lowerThreshold)
				{
					if(Input[0] > Input[1])
						trend[0] = 1;
					else
						trend[0] = trend[1];
				}
				else if (Signal[0] < Signal[1] && Signal[0] < upperThreshold)
				{
					if(Input[0] < Input[1])
						trend[0] = -1;
					else
						trend[0] = trend[1];
				}
				else
					trend[0] = 0;
			}
			else if(trendDefinition == amaMultiTSITrendDefinition.TSI_Signal_Cross && calculateFromPriceData)
			{
				if(TSI[0] > Signal[0] && TSI[0] >= TSI[1] && Signal[0] > lowerThreshold)
				{
					if(Input[0] > High[1] || Median[0] > High[1])
						trend[0] = 1;
					else
						trend[0] = trend[1];
				}
				else if(TSI[0] < Signal[0] && TSI[0] <= TSI[1] && Signal[0] < upperThreshold)
				{
					if(Input[0] < Low[1] || Median[0] < Low[1])
						trend[0] = -1;
					else
						trend[0] = trend[1];
				}
				else 
					trend[0] = 0;
			}			
			else if(trendDefinition == amaMultiTSITrendDefinition.TSI_Signal_Cross)
			{
				if(TSI[0] > Signal[0] && TSI[0] >= TSI[1] && Signal[0] > lowerThreshold)
				{
					if(Input[0] > Input[1])
						trend[0] = 1;
					else
						trend[0] = trend[1];
				}
				else if(TSI[0] < Signal[0] && TSI[0] <= TSI[1] && Signal[0] < upperThreshold)
				{
					if(Input[0] < Input[1])
						trend[0] = -1;
					else
						trend[0] = trend[1];
				}
				else 
					trend[0] = 0;
			}
		
			if(showPaintBars && CurrentBar >= totalBarsRequiredToPlot)
			{
				if(displacement >= 0)
				{
					if(trend[displacement] > 0.5)
					{
						if(Open[0] < Close[0])
							BarBrushes[0] = upBrushUp;
						else
							BarBrushes[0] = upBrushDown;
						CandleOutlineBrushes[0] = upBrushOutline;
					}	
					else if(trend[displacement] < -0.5)
					{
						if(Open[0] < Close[0])
							BarBrushes[0] = downBrushUp;
						else
							BarBrushes[0] = downBrushDown;
						CandleOutlineBrushes[0] = downBrushOutline;
					}	
					else
					{
						if(Open[0] < Close[0])
							BarBrushes[0] = neutralBrushUp;
						else
							BarBrushes[0] = neutralBrushDown;
						CandleOutlineBrushes[0] = neutralBrushOutline;
					}	
				}
				else 
				{
					if(trend[0] > 0.5)
					{
						if(Open[-displacement] < Close[-displacement])
							BarBrushes[-displacement] = upBrushUp;
						else
							BarBrushes[-displacement] = upBrushDown;
						CandleOutlineBrushes[-displacement] = upBrushOutline;
					}	
					else if(trend[0] < -0.5)
					{
						if(Open[-displacement] < Close[-displacement])
							BarBrushes[-displacement] = downBrushUp;
						else
							BarBrushes[-displacement] = downBrushDown;
						CandleOutlineBrushes[-displacement] = downBrushOutline;
					}	
					else
					{
						if(Open[-displacement] < Close[-displacement])
							BarBrushes[-displacement] = neutralBrushUp;
						else
							BarBrushes[-displacement] = neutralBrushDown;
						CandleOutlineBrushes[-displacement] = neutralBrushOutline;
					}
				}
			}
			
			if (soundAlerts && State == State.Realtime && IsConnected())
			{
				if(trend[0] > 0.5 && trend[1] < 0.5)		
				{
					try
					{
							Alert("New_Uptrend", Priority.Medium,"New uptrend", pathNewUptrend, rearmTime, alertBackBrush, upBrushUp);
					}
					catch{}
				}
				else if(trend[0] < -0.5 && trend[1] > -0.5)	 
				{
					try
					{
							Alert("New_Downtrend", Priority.Medium,"New downtrend", pathNewDowntrend, rearmTime, alertBackBrush, downBrushDown);
					}
					catch{}
				}
				else if(trend[0] < 0.5 && trend[0] > -0.5 && (trend[1] > 0.5 || trend[1] < -0.5))	 
				{
					try
					{
							Alert("New_Neutraltrend", Priority.Medium,"New neutral trend", pathNewNeutraltrend, rearmTime, alertBackBrush, neutralBrushUp);
					}
					catch{}
				}
			}				
		}
		
		#region Properties
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> TSI
		{
			get { return Values[0]; }
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> Signal
		{
			get { return Values[1]; }
		}

        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> HistogramG
        {
            get { return Values[2]; }
        }

        [Browsable(false)]
		[XmlIgnore()]
		public Series<double> HistogramR
		{
			get { return Values[3]; }
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> Trend
		{
			get { return trend; }
		}
		
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Momentum smoothing", Description = "Select moving average type for smoothing momentum", GroupName = "Algorithmic Options", Order = 0)]
		public amaMultiTSISmoothType SmoothType
		{	
            get { return smoothType; }
            set { smoothType = value; }
		}
			
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Signal smoothing", Description = "Select moving average type for smoothing the signal line", GroupName = "Algorithmic Options", Order = 1)]
		public amaMultiTSISmoothType SignalType
		{	
            get { return signalType; }
            set { signalType = value; }
		}
			
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Trend definition", Description = "Select trend definition for the paint bars", GroupName = "Algorithmic Options", Order = 2)]
		public amaMultiTSITrendDefinition TrendDefinition
		{	
            get { return trendDefinition; }
            set { trendDefinition = value; }
		}
			
		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Momentum period", Description = "Select lookback period for momentum", GroupName = "Input Parameters", Order = 0)]
		public int MomentumPeriod
		{	
            get { return momentumPeriod; }
            set { momentumPeriod = value; }
		}
				
		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Smoothing period 1", Description = "Select first smoothing period for momentum", GroupName = "Input Parameters", Order = 1)]
		public int Smooth1
		{	
            get { return smooth1; }
            set { smooth1 = value; }
		}
		
		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Smoothing period 2", Description = "Select second smoothing period for momentum", GroupName = "Input Parameters", Order = 2)]
		public int Smooth2
		{	
            get { return smooth2; }
            set { smooth2 = value; }
		}
		
		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Smoothing period 3", Description = "Select third smoothing period for momentum", GroupName = "Input Parameters", Order = 3)]
		public int Smooth3
		{	
            get { return smooth3; }
            set { smooth3 = value; }
		}
		
		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Signal period", Description = "Select lookback period for signal line", GroupName = "Input Parameters", Order = 4)]
		public int SignalPeriod
		{	
            get { return signalPeriod; }
            set { signalPeriod = value; }
		}
				
		[Range(0.0, 100.0), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Upper threshold", GroupName = "Threshold Lines", Order = 0)]
		public double UpperThreshold
		{	
            get { return upperThreshold; }
            set { upperThreshold = value; }
		}
		
		[Range(-100.0, 0), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Lower threshold", GroupName = "Threshold Lines", Order = 1)]
		public double LowerThreshold
		{	
            get { return lowerThreshold; }
            set { lowerThreshold = value; }
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show TSI", GroupName = "Display Options", Order = 0)]
        public bool ShowTSI
        {
            get { return showTSI; }
            set { showTSI = value; }
        }
			
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show signal line", GroupName = "Display Options", Order = 1)]
        public bool ShowSignal
        {
            get { return showSignal; }
            set { showSignal = value; }
        }
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show histogram", GroupName = "Display Options", Order = 2)]
        public bool ShowHistogram
        {
            get { return showHistogram; }
            set { showHistogram = value; }
        }
			
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show paint bars", GroupName = "Display Options", Order = 3)]
        public bool ShowPaintBars
        {
            get { return showPaintBars; }
            set { showPaintBars = value; }
        }
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "TSI", Description = "Sets the color for the TSI plot", GroupName = "Plots", Order = 0)]
		public Brush TSIBrush
		{ 
			get {return tsiBrush;}
			set {tsiBrush = value;}
		}

		[Browsable(false)]
		public string TSIBrushSerializable
		{
			get { return Serialize.BrushToString(tsiBrush); }
			set { tsiBrush = Serialize.StringToBrush(value); }
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plot style TSI", Description = "Sets the plot style for the TSI", GroupName = "Plots", Order = 1)]
		public PlotStyle Plot0Style
		{	
            get { return plot0Style; }
            set { plot0Style = value; }
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Dash style TSI", Description = "Sets the dash style for the TSI plot", GroupName = "Plots", Order = 2)]
		public DashStyleHelper Dash0Style
		{
			get { return dash0Style; }
			set { dash0Style = value; }
		}
		
		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plot width TSI", Description = "Sets the plot width for the TSI plot", GroupName = "Plots", Order = 3)]
		public int Plot0Width
		{	
            get { return plot0Width; }
            set { plot0Width = value; }
		}
			
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Signal line", Description = "Sets the color for the signal plot", GroupName = "Plots", Order = 4)]
		public Brush SignalBrush
		{ 
			get {return signalBrush;}
			set {signalBrush = value;}
		}

		[Browsable(false)]
		public string SignalBrushSerializable
		{
			get { return Serialize.BrushToString(signalBrush); }
			set { signalBrush = Serialize.StringToBrush(value); }
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plot style signal", Description = "Sets the plot style for the signal line", GroupName = "Plots", Order = 5)]
		public PlotStyle Plot1Style
		{	
            get { return plot1Style; }
            set { plot1Style = value; }
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Dash style signal", Description = "Sets the dash style for the signal plot", GroupName = "Plots", Order = 6)]
		public DashStyleHelper Dash1Style
		{
			get { return dash1Style; }
			set { dash1Style = value; }
		}
		
		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plot width signal", Description = "Sets the plot width for the signal plot", GroupName = "Plots", Order = 7)]
		public int Plot1Width
		{	
            get { return plot1Width; }
            set { plot1Width = value; }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Histogram", Description = "Sets the color for the histogram", GroupName = "Plots", Order = 8)]
		public Brush HistogramBrush
		{ 
			get {return histogramBrush;}
			set {histogramBrush = value;}
		}

		[Browsable(false)]
		public string HistogramBrushSerializable
		{
			get { return Serialize.BrushToString(histogramBrush); }
			set { histogramBrush = Serialize.StringToBrush(value); }
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Auto-adjust bar width", Description = "Auto-adjusts bar width of histogram to match price bars", GroupName = "Plots", Order = 9)]
     	[RefreshProperties(RefreshProperties.All)] 
       	public bool AutoBarWidth
        {
            get { return autoBarWidth; }
            set { autoBarWidth = value; }
        }
		
		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plot width histogram", Description = "Sets the plot width for the histogram", GroupName = "Plots", Order = 10)]
		public int Plot2Width
		{	
            get { return plot2Width; }
            set { plot2Width = value; }
		}
				
		[Range(0, 100)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Opacity histogram", Description = "Sets the opacity for the histogram", GroupName = "Plots", Order = 11)]
		public int HistogramOpacity
		{	
            get { return histogramOpacity; }
            set { histogramOpacity = value; }
		}
				
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Upper threshold", Description = "Sets the color for the upper threshold line", GroupName = "Lines", Order = 0)]
		public Brush UpperlineBrush
		{ 
			get {return upperlineBrush;}
			set {upperlineBrush = value;}
		}

		[Browsable(false)]
		public string UpperlineBrushSerializable
		{
			get { return Serialize.BrushToString(upperlineBrush); }
			set { upperlineBrush = Serialize.StringToBrush(value); }
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Dash style", Description = "Sets the dash style for the upper threshold line", GroupName = "Lines", Order = 1)]
		public DashStyleHelper Line0Style
		{
			get { return line0Style; }
			set { line0Style = value; }
		}
		
		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plot width", Description = "Sets the plot width for the upper threshold line", GroupName = "Lines", Order = 2)]
		public int Line0Width
		{	
            get { return line0Width; }
            set { line0Width = value; }
		}
			
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Midline", Description = "Sets the color for the zeroline", GroupName = "Lines", Order = 3)]
		public Brush ZerolineBrush
		{ 
			get {return zerolineBrush;}
			set {zerolineBrush = value;}
		}

		[Browsable(false)]
		public string ZerolineBrushSerializable
		{
			get { return Serialize.BrushToString(zerolineBrush); }
			set { zerolineBrush = Serialize.StringToBrush(value); }
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Dash style", Description = "Sets the dash style for the zeroline", GroupName = "Lines", Order = 4)]
		public DashStyleHelper Line1Style
		{
			get { return line1Style; }
			set { line1Style = value; }
		}
		
		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plot width", Description = "Sets the plot width for the zeroline", GroupName = "Lines", Order = 5)]
		public int Line1Width
		{	
            get { return line1Width; }
            set { line1Width = value; }
		}
			
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Lower threshold", Description = "Sets the color for the lower threshold line", GroupName = "Lines", Order = 6)]
		public Brush LowerlineBrush
		{ 
			get {return lowerlineBrush;}
			set {lowerlineBrush = value;}
		}

		[Browsable(false)]
		public string LowerlineBrushSerializable
		{
			get { return Serialize.BrushToString(lowerlineBrush); }
			set { lowerlineBrush = Serialize.StringToBrush(value); }
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Dash style", Description = "Sets the dash style for the lower threshold line", GroupName = "Lines", Order = 7)]
		public DashStyleHelper Line2Style
		{
			get { return line2Style; }
			set { line2Style = value; }
		}
		
		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plot width", Description = "Sets the plot width for the lower threshold line", GroupName = "Lines", Order = 8)]
		public int Line2Width
		{	
            get { return line2Width; }
            set { line2Width = value; }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Bullish upclose", Description = "Sets the color for a bullish trend", GroupName = "Paint Bars", Order = 0)]
		public Brush UpBrushUp
		{ 
			get {return upBrushUp;}
			set {upBrushUp = value;}
		}

		[Browsable(false)]
		public string UpBrushUpSerializable
		{
			get { return Serialize.BrushToString(upBrushUp); }
			set { upBrushUp = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Bullish downclose", Description = "Sets the color for a bullish trend", GroupName = "Paint Bars", Order = 1)]
		public Brush UpBrushDown
		{ 
			get {return upBrushDown;}
			set {upBrushDown = value;}
		}

		[Browsable(false)]
		public string UpBrushDownSerializable
		{
			get { return Serialize.BrushToString(upBrushDown); }
			set { upBrushDown = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Bullish candle outline", Description = "Sets the color for candle outlines", GroupName = "Paint Bars", Order = 2)]
		public Brush UpBrushOutline
		{ 
			get {return upBrushOutline;}
			set {upBrushOutline = value;}
		}

		[Browsable(false)]
		public string UpBrushOutlineSerializable
		{
			get { return Serialize.BrushToString(upBrushOutline); }
			set { upBrushOutline = Serialize.StringToBrush(value); }
		}					
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Bearish upclose", Description = "Sets the color for a bearish trend", GroupName = "Paint Bars", Order = 3)]
		public Brush DownBrushUp
		{ 
			get {return downBrushUp;}
			set {downBrushUp = value;}
		}

		[Browsable(false)]
		public string DownBrushUpSerializable
		{
			get { return Serialize.BrushToString(downBrushUp); }
			set { downBrushUp = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Bearish downclose", Description = "Sets the color for a bearish trend", GroupName = "Paint Bars", Order = 4)]
		public Brush DownBrushDown
		{ 
			get {return downBrushDown;}
			set {downBrushDown = value;}
		}

		[Browsable(false)]
		public string DownBrushDownSerializable
		{
			get { return Serialize.BrushToString(downBrushDown); }
			set { downBrushDown = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Bearish candle outline", Description = "Sets the color for candle outlines", GroupName = "Paint Bars", Order = 5)]
		public Brush DownBrushOutline
		{ 
			get {return downBrushOutline;}
			set {downBrushOutline = value;}
		}

		[Browsable(false)]
		public string DownBrushOutlineSerializable
		{
			get { return Serialize.BrushToString(downBrushOutline); }
			set { downBrushOutline = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Neutral upclose", Description = "Sets the color for a neutral trend", GroupName = "Paint Bars", Order = 6)]
		public Brush NeutralBrushUp
		{ 
			get {return neutralBrushUp;}
			set {neutralBrushUp = value;}
		}

		[Browsable(false)]
		public string NeutralBrushUpSerializable
		{
			get { return Serialize.BrushToString(neutralBrushUp); }
			set { neutralBrushUp = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Neutral downclose", Description = "Sets the color for a neutral trend", GroupName = "Paint Bars", Order = 7)]
		public Brush NeutralBrushDown
		{ 
			get {return neutralBrushDown;}
			set {neutralBrushDown = value;}
		}

		[Browsable(false)]
		public string NeutralBrushDownSerializable
		{
			get { return Serialize.BrushToString(neutralBrushDown); }
			set { neutralBrushDown = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Neutral candle outline", Description = "Sets the color for candle outlines", GroupName = "Paint Bars", Order = 8)]
		public Brush NeutralBrushOutline
		{ 
			get {return neutralBrushOutline;}
			set {neutralBrushOutline = value;}
		}

		[Browsable(false)]
		public string NeutralBrushOutlineSerializable
		{
			get { return Serialize.BrushToString(neutralBrushOutline); }
			set { neutralBrushOutline = Serialize.StringToBrush(value); }
		}

		[Display(ResourceType = typeof(Custom.Resource), Name = "Sound alerts", GroupName = "Sound Alerts", Order = 0)]
        public bool SoundAlerts
        {
            get { return soundAlerts; }
            set { soundAlerts = value; }
        }
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "New uptrend", Description = "Sound file for new uptrend", GroupName = "Sound Alerts", Order = 1)]
		public string NewUptrend
		{	
            get { return newUptrend; }
            set { newUptrend = value; }
		}		
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "New downtrend", Description = "Sound file for new downtrend", GroupName = "Sound Alerts", Order = 2)]
		public string NewDowntrend
		{	
            get { return newDowntrend; }
            set { newDowntrend = value; }
		}		
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "New neutral trend", Description = "Sound file for new nuetral trend", GroupName = "Sound Alerts", Order = 3)]
		public string NewNeutraltrend
		{	
            get { return newNeutraltrend; }
            set { newNeutraltrend = value; }
		}		
		
		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Rearm time", Description = "Rearm time for alerts in seconds", GroupName = "Sound Alerts", Order = 4)]
		public int RearmTime
		{	
            get { return rearmTime; }
            set { rearmTime = value; }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Release and date", Description = "Release and date", GroupName = "Version", Order = 0)]
		public string VersionString
		{	
            get { return versionString; }
            set { ; }
		}
		#endregion
		
		#region Miscellaneous
		
		private bool IsConnected()
        {
			if ( Bars != null && Bars.Instrument.GetMarketDataConnection().PriceStatus == NinjaTrader.Cbi.ConnectionStatus.Connected
					&& sessionIterator.IsInSession(Now, true, true))
				return true;
			else
            	return false;
        }
		
		private DateTime Now
		{
          get 
			{ 
				DateTime now = (Bars.Instrument.GetMarketDataConnection().Options.Provider == NinjaTrader.Cbi.Provider.Playback ? Bars.Instrument.GetMarketDataConnection().Now : DateTime.Now); 

				if (now.Millisecond > 0)
					now = NinjaTrader.Core.Globals.MinDate.AddSeconds((long) System.Math.Floor(now.Subtract(NinjaTrader.Core.Globals.MinDate).TotalSeconds));

				return now;
			}
		}
		#endregion
	}
}

namespace NinjaTrader.NinjaScript.Indicators
{		
	public class amaMultiTSITypeConverter : NinjaTrader.NinjaScript.IndicatorBaseConverter
	{
		public override bool GetPropertiesSupported(ITypeDescriptorContext context) { return true; }

		public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object value, Attribute[] attributes)
		{
			PropertyDescriptorCollection propertyDescriptorCollection = base.GetPropertiesSupported(context) ? base.GetProperties(context, value, attributes) : TypeDescriptor.GetProperties(value, attributes);

			amaMultiTSI					thisMultiTSIInstance			= (amaMultiTSI) value;
			bool						autoBarWidthFromInstance		= thisMultiTSIInstance.AutoBarWidth;
		
			PropertyDescriptorCollection adjusted = new PropertyDescriptorCollection(null);
			
			foreach (PropertyDescriptor thisDescriptor in propertyDescriptorCollection)
			{
				if (autoBarWidthFromInstance && thisDescriptor.Name == "Plot2Width") 
					adjusted.Add(new PropertyDescriptorExtended(thisDescriptor, o => value, null, new Attribute[] {new BrowsableAttribute(false), }));
				else	
					adjusted.Add(thisDescriptor);
			}
			return adjusted;
		}
	}
}

#region Global Enums

public enum amaMultiTSISmoothType {Median, Adaptive_Laguerre, ADXVMA, Butterworth_2, Butterworth_3, DEMA, Distant_Coefficient_Filter, DWMA, EHMA, EMA, Gauss_2, Gauss_3, Gauss_4,
			 HMA, HoltEMA, Laguerre, LinReg, SMA, SuperSmoother_2, SuperSmoother_3, SWMA, TEMA, Tillson_T3, TMA, TWMA, Wilder, WMA, ZerolagTEMA, ZLEMA}
public enum amaMultiTSITrendDefinition {Signal_Slope, TSI_Signal_Cross}

#endregion

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private LizardIndicators.amaMultiTSI[] cacheamaMultiTSI;
		public LizardIndicators.amaMultiTSI amaMultiTSI(amaMultiTSISmoothType smoothType, amaMultiTSISmoothType signalType, amaMultiTSITrendDefinition trendDefinition, int momentumPeriod, int smooth1, int smooth2, int smooth3, int signalPeriod, double upperThreshold, double lowerThreshold)
		{
			return amaMultiTSI(Input, smoothType, signalType, trendDefinition, momentumPeriod, smooth1, smooth2, smooth3, signalPeriod, upperThreshold, lowerThreshold);
		}

		public LizardIndicators.amaMultiTSI amaMultiTSI(ISeries<double> input, amaMultiTSISmoothType smoothType, amaMultiTSISmoothType signalType, amaMultiTSITrendDefinition trendDefinition, int momentumPeriod, int smooth1, int smooth2, int smooth3, int signalPeriod, double upperThreshold, double lowerThreshold)
		{
			if (cacheamaMultiTSI != null)
				for (int idx = 0; idx < cacheamaMultiTSI.Length; idx++)
					if (cacheamaMultiTSI[idx] != null && cacheamaMultiTSI[idx].SmoothType == smoothType && cacheamaMultiTSI[idx].SignalType == signalType && cacheamaMultiTSI[idx].TrendDefinition == trendDefinition && cacheamaMultiTSI[idx].MomentumPeriod == momentumPeriod && cacheamaMultiTSI[idx].Smooth1 == smooth1 && cacheamaMultiTSI[idx].Smooth2 == smooth2 && cacheamaMultiTSI[idx].Smooth3 == smooth3 && cacheamaMultiTSI[idx].SignalPeriod == signalPeriod && cacheamaMultiTSI[idx].UpperThreshold == upperThreshold && cacheamaMultiTSI[idx].LowerThreshold == lowerThreshold && cacheamaMultiTSI[idx].EqualsInput(input))
						return cacheamaMultiTSI[idx];
			return CacheIndicator<LizardIndicators.amaMultiTSI>(new LizardIndicators.amaMultiTSI(){ SmoothType = smoothType, SignalType = signalType, TrendDefinition = trendDefinition, MomentumPeriod = momentumPeriod, Smooth1 = smooth1, Smooth2 = smooth2, Smooth3 = smooth3, SignalPeriod = signalPeriod, UpperThreshold = upperThreshold, LowerThreshold = lowerThreshold }, input, ref cacheamaMultiTSI);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.LizardIndicators.amaMultiTSI amaMultiTSI(amaMultiTSISmoothType smoothType, amaMultiTSISmoothType signalType, amaMultiTSITrendDefinition trendDefinition, int momentumPeriod, int smooth1, int smooth2, int smooth3, int signalPeriod, double upperThreshold, double lowerThreshold)
		{
			return indicator.amaMultiTSI(Input, smoothType, signalType, trendDefinition, momentumPeriod, smooth1, smooth2, smooth3, signalPeriod, upperThreshold, lowerThreshold);
		}

		public Indicators.LizardIndicators.amaMultiTSI amaMultiTSI(ISeries<double> input , amaMultiTSISmoothType smoothType, amaMultiTSISmoothType signalType, amaMultiTSITrendDefinition trendDefinition, int momentumPeriod, int smooth1, int smooth2, int smooth3, int signalPeriod, double upperThreshold, double lowerThreshold)
		{
			return indicator.amaMultiTSI(input, smoothType, signalType, trendDefinition, momentumPeriod, smooth1, smooth2, smooth3, signalPeriod, upperThreshold, lowerThreshold);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.LizardIndicators.amaMultiTSI amaMultiTSI(amaMultiTSISmoothType smoothType, amaMultiTSISmoothType signalType, amaMultiTSITrendDefinition trendDefinition, int momentumPeriod, int smooth1, int smooth2, int smooth3, int signalPeriod, double upperThreshold, double lowerThreshold)
		{
			return indicator.amaMultiTSI(Input, smoothType, signalType, trendDefinition, momentumPeriod, smooth1, smooth2, smooth3, signalPeriod, upperThreshold, lowerThreshold);
		}

		public Indicators.LizardIndicators.amaMultiTSI amaMultiTSI(ISeries<double> input , amaMultiTSISmoothType smoothType, amaMultiTSISmoothType signalType, amaMultiTSITrendDefinition trendDefinition, int momentumPeriod, int smooth1, int smooth2, int smooth3, int signalPeriod, double upperThreshold, double lowerThreshold)
		{
			return indicator.amaMultiTSI(input, smoothType, signalType, trendDefinition, momentumPeriod, smooth1, smooth2, smooth3, signalPeriod, upperThreshold, lowerThreshold);
		}
	}
}

#endregion
