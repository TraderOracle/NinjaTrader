//+----------------------------------------------------------------------------------------------+
//| Copyright © <2022>  <LizardIndicators.com - powered by AlderLab UG>
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
	/// The SuperTrend U11 is a trailing stop which can also be used as a trend filter. The long stop is calculated by subtracting a multiple of a volatility measure from a moving average or moving median. 
	/// The short stop is calculated by adding a multiple of the volatility measure to the moving average or moving median. The trend information is exposed through a public data series and can be directly accessed 
	/// via an automated strategy or or a market analyzer column."; 
	/// </summary>
	[Gui.CategoryOrder("Algorithmic Options", 1000100)]
	[Gui.CategoryOrder("Input Parameters", 1000200)]
	[Gui.CategoryOrder("Display Options", 1000300)]
	[Gui.CategoryOrder("Trade Signals", 8000100)]
	[Gui.CategoryOrder("Paint Bars", 8000200)]
	[Gui.CategoryOrder("Sound Alerts", 8000300)]
	[Gui.CategoryOrder("Version", 8000400)]
	[TypeConverter("NinjaTrader.NinjaScript.Indicators.amaSuperTrendU11TypeConverter")]
	public class amaSuperTrendU11 : Indicator
	{
		private int 						basePeriod					= 8; 
        private int 						volaPeriod					= 15; 
		private int							displacement				= 0;
		private int							totalBarsRequiredToPlot		= 0;
		private int							shift						= 0;
		private double 						multiplier 					= 2.5; 
		private bool						reverseIntraBar				= false;
		private bool 						showTriangles 				= true;
		private bool 						showPaintBars 				= true;
		private bool						showStopDots				= true;
		private bool 						showStopLine				= true;
		private bool						soundAlerts					= false;
		private bool 						gap0						= false;
		private bool 						gap1						= false;
		private bool						stoppedOut					= false;
		private bool						drawTriangleUp				= false;
		private bool						drawTriangleDown			= false;
		private bool						calculateFromPriceData		= true;
		private bool						indicatorIsOnPricePanel		= true;
		private bool						midbandTypeNotAllowed		= false;
		private bool						offsetFormulaNotAllowed		= false;
		private bool						errorMessage				= false;
		private double						movingBase					= 0.0;
		private double						firstSquare					= 0.0;
		private double						lastSquare					= 0.0;
		private double						firstAbsDev					= 0.0;
		private double						dist						= 0.0;
		private double 						offset						= 0.0;
		private double						trailingAmount				= 0.0;
		private double						low							= 0.0;
		private double						high						= 0.0;
		private double						labelOffset					= 0.0;
		private amaSuperTrendU11BaseType 	thisBaseType				= amaSuperTrendU11BaseType.Median; 
		private amaSuperTrendU11OffsetType 	thisOffsetType				= amaSuperTrendU11OffsetType.Wilder; 
		private amaSuperTrendU11VolaType 	thisVolaType				= amaSuperTrendU11VolaType.True_Range; 
		private SessionIterator				sessionIterator				= null;
		private int 						plot0Width 					= 2;
		private PlotStyle 					plot0Style					= PlotStyle.Dot;
		private int 						plot1Width 					= 1;
		private PlotStyle 					plot1Style					= PlotStyle.Line;
		private Brush						upBrush						= Brushes.Blue;
		private Brush						downBrush					= Brushes.Red;
		private Brush						bullishSignalBrush			= Brushes.LightSkyBlue;
		private Brush						bearishSignalBrush			= Brushes.OrangeRed;
		private Brush						signalBackBrush				= Brushes.Black;
		private Brush						upBrushUp					= Brushes.Blue;
		private Brush						upBrushDown					= Brushes.LightSkyBlue;
		private Brush						downBrushUp					= Brushes.LightCoral;
		private Brush						downBrushDown				= Brushes.Red;
		private Brush						upBrushOutline				= Brushes.Black;
		private Brush						downBrushOutline			= Brushes.Black;
		private Brush						errorBrush					= Brushes.Black;
		private SimpleFont					dotFont						= null;
		private SimpleFont					triangleFont				= null;
		private SimpleFont					errorFont;
		private int							triangleFontSize			= 15;
		private string						dotString					= "n";
		private string						triangleStringUp			= "";
		private string						triangleStringDown			= "";
		private string						errorText1					= "Please select a different moving average, mean or mode, when an indicator is used as input series for the amaSuperTrendU11.";
		private string						errorText2					= "Please do not set the amaSuperTrendU11 to 'Reverse intra-bar', when an indicator is used as input series.";
		private string						errorText3					= "Don't set the offset formula for the amaSuperTrendU11 to 'Range', when an indicator is used as input series.";
		private string						errorText4					= "The amaSuperTrendU11 cannot be used with a negative displacement.";
		private int							rearmTime					= 30;
		private string 						newUptrend					= "newuptrend.wav";
		private string 						newDowntrend				= "newdowntrend.wav";
		private string 						potentialUptrend			= "potentialuptrend.wav";
		private string 						potentialDowntrend			= "potentialdowntrend.wav";
		private string						pathNewUptrend				= "";
		private string						pathNewDowntrend			= "";
		private string						pathPotentialUptrend		= "";
		private string						pathPotentialDowntrend		= "";
		private string						versionString				= "v 3.1  -  March 12, 2022";
		private Series<double>				preliminaryTrend;
		private Series<double>				trend;
		private Series<double>				sumAbsDev;
		private Series<double>				sumSquares;
		private Series<double>				currentStopLong;
		private Series<double>				currentStopShort;
		private ISeries<double>				baseline;
		private ISeries<double>				rangeSeries;
		private ISeries<double>				offsetSeries;
		private ATR							barVolatility;
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= "\r\nThe SuperTrend U11 is a trailing stop which can also be used as a trend filter. The long stop is calculated by subtracting a multiple of a volatility measure"
											  + " from a moving average or moving median. The short stop is calculated by adding a multiple of a volatility measure to the moving average or moving median."
											  + "The trend information is exposed through a public data series and can be directly accessed via automated strategies or a market analyzer column."; 
				Name						= "amaSuperTrendU11";
				Calculate					= Calculate.OnPriceChange;
				IsSuspendedWhileInactive	= false;
				IsOverlay					= true;
				ArePlotsConfigurable		= false;
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Dot, "StopDot");	
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "StopLine");
				AddPlot(new Stroke(Brushes.Transparent, 2), PlotStyle.Dot, "ReverseDot");
			}
			else if (State == State.Configure)
			{
				if(Calculate == Calculate.OnPriceChange)
					switch(thisBaseType)
					{
						case amaSuperTrendU11BaseType.Median_VWTPO: 
							Calculate = Calculate.OnEachTick;
							break;
						case amaSuperTrendU11BaseType.Mean_VWTPO: 
							Calculate = Calculate.OnEachTick;
							break;
						case amaSuperTrendU11BaseType.VWMA: 
							Calculate = Calculate.OnEachTick;
							break;
						default:
							break;
					}	
				displacement = Displacement;
				BarsRequiredToPlot = Math.Max(basePeriod, 6*volaPeriod);
				totalBarsRequiredToPlot = BarsRequiredToPlot + displacement;
				if(Calculate == Calculate.OnBarClose && !reverseIntraBar)
					shift = displacement + 1;
				else
					shift = displacement;
		
				Plots[0].PlotStyle = plot0Style;
				Plots[0].Width = plot0Width;
				Plots[1].PlotStyle = plot1Style;
				Plots[1].Width = plot1Width;
				Plots[2].PlotStyle = plot0Style;
				Plots[2].Width = plot0Width;
			}
			else if (State == State.DataLoaded)
			{
				preliminaryTrend = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				trend = new Series<double>(this, MaximumBarsLookBack.Infinite);
				sumAbsDev = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				sumSquares = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				currentStopLong = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				currentStopShort = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				barVolatility = ATR(Closes[0], 256);
				midbandTypeNotAllowed = false;
				offsetFormulaNotAllowed = false;
				if(Input is PriceSeries || Input is Bars)
					calculateFromPriceData = true;
				else
					calculateFromPriceData = false;
				switch (thisBaseType)
				{
					case amaSuperTrendU11BaseType.Median: 
						baseline = amaMovingMedian(Inputs[0], basePeriod);
						break;
					case amaSuperTrendU11BaseType.Median_TPO: 
						if(calculateFromPriceData)
							baseline = amaMovingMedianTPO(Inputs[0], basePeriod, true);
						else
							midbandTypeNotAllowed = true;
						break;
					case amaSuperTrendU11BaseType.Median_VWTPO: 
						if(calculateFromPriceData)
							baseline = amaMovingMedianVWTPO(Inputs[0], basePeriod, true);
						else
							midbandTypeNotAllowed = true;
						break;
					case amaSuperTrendU11BaseType.Mean_TPO: 
						if(calculateFromPriceData)
							baseline = amaMovingMeanTPO(Inputs[0], basePeriod);
						else
							midbandTypeNotAllowed = true;
						break;
					case amaSuperTrendU11BaseType.Mean_VWTPO: 
						if(calculateFromPriceData)
							baseline = amaMovingMeanVWTPO(Inputs[0], basePeriod);
						else
							midbandTypeNotAllowed = true;
						break;
					case amaSuperTrendU11BaseType.Adaptive_Laguerre: 
						baseline = amaAdaptiveLaguerreFilter(Inputs[0], basePeriod);
						break;
					case amaSuperTrendU11BaseType.ADXVMA: 
						baseline = amaADXVMA(Inputs[0], basePeriod);
						break;
					case amaSuperTrendU11BaseType.Butterworth_2: 
						baseline = amaButterworthFilter(Inputs[0], 2, basePeriod);
						break;
					case amaSuperTrendU11BaseType.Butterworth_3: 
						baseline = amaButterworthFilter(Inputs[0], 3, basePeriod);
						break;
					case amaSuperTrendU11BaseType.DEMA: 
						baseline = DEMA(Inputs[0], basePeriod);
						break;
					case amaSuperTrendU11BaseType.Distant_Coefficient_Filter: 
						baseline = amaDistantCoefficientFilter(Inputs[0], basePeriod);
						break;
					case amaSuperTrendU11BaseType.DWMA: 
						baseline = amaDWMA(Inputs[0], basePeriod);
						break;
					case amaSuperTrendU11BaseType.EHMA: 
						baseline = amaEHMA(Inputs[0], basePeriod);
						break;
					case amaSuperTrendU11BaseType.EMA: 
						baseline = EMA(Inputs[0], basePeriod);
						break;
					case amaSuperTrendU11BaseType.Gauss_2: 
						baseline = amaGaussianFilter(Inputs[0], 2, basePeriod);
						break;
					case amaSuperTrendU11BaseType.Gauss_3: 
						baseline = amaGaussianFilter(Inputs[0], 3, basePeriod);
						break;
					case amaSuperTrendU11BaseType.Gauss_4: 
						baseline = amaGaussianFilter(Inputs[0], 4, basePeriod);
						break;
					case amaSuperTrendU11BaseType.HMA:
						if(basePeriod > 1)
							baseline = HMA(Inputs[0], basePeriod);
						else
							baseline = Inputs[0];
						break;
					case amaSuperTrendU11BaseType.HoltEMA: 
						baseline = amaHoltEMA(Inputs[0], basePeriod, basePeriod);
						break;
					case amaSuperTrendU11BaseType.Laguerre: 
						baseline = amaLaguerreFilter(Inputs[0], basePeriod);
						break;
					case amaSuperTrendU11BaseType.LinReg:
						if(basePeriod > 1)
							baseline = LinReg(Inputs[0], basePeriod);
						else
							baseline = Inputs[0];
						break;
					case amaSuperTrendU11BaseType.RWMA: 
						if(calculateFromPriceData)
							baseline = amaRWMA(Inputs[0], basePeriod);
						else
							midbandTypeNotAllowed = true;
						break;
					case amaSuperTrendU11BaseType.SMA: 
						baseline = SMA(Inputs[0], basePeriod);
						break;
					case amaSuperTrendU11BaseType.SuperSmoother_2: 
						baseline = amaSuperSmoother(Inputs[0], 2, basePeriod);
						break;
					case amaSuperTrendU11BaseType.SuperSmoother_3: 
						baseline = amaSuperSmoother(Inputs[0], 3, basePeriod);
						break;
					case amaSuperTrendU11BaseType.SWMA: 
						baseline = amaSWMA(Inputs[0], basePeriod);
						break;
					case amaSuperTrendU11BaseType.TEMA: 
						baseline = TEMA(Inputs[0], basePeriod);
						break;
					case amaSuperTrendU11BaseType.Tillson_T3: 
						baseline = amaTillsonT3(Inputs[0], amaT3CalcMode.Tillson, basePeriod, 0.7);
						break;
					case amaSuperTrendU11BaseType.TMA: 
						baseline = TMA(Inputs[0], basePeriod);
						break;
					case amaSuperTrendU11BaseType.TWMA: 
						baseline = amaTWMA(Inputs[0], basePeriod);
						break;
					case amaSuperTrendU11BaseType.VWMA: 
						baseline = VWMA(Inputs[0], basePeriod);
						break;
					case amaSuperTrendU11BaseType.Wilder: 
						baseline = EMA(Inputs[0], 2*basePeriod-1);
						break;
					case amaSuperTrendU11BaseType.WMA: 
						baseline = WMA(Inputs[0], basePeriod);
						break;
					case amaSuperTrendU11BaseType.ZerolagHATEMA: 
						if(calculateFromPriceData)
							baseline = amaZerolagHATEMA(Inputs[0], basePeriod);
						else
							midbandTypeNotAllowed = true;
						break;
					case amaSuperTrendU11BaseType.ZerolagTEMA: 
						baseline = amaZerolagTEMA(Inputs[0], basePeriod);
						break;
					case amaSuperTrendU11BaseType.ZLEMA:
						if(basePeriod > 1)
							baseline = ZLEMA(Inputs[0], basePeriod);
						else
							baseline = Inputs[0];
						break;
				}
				switch (thisVolaType)
				{
					case amaSuperTrendU11VolaType.Range:
						if(calculateFromPriceData)
							rangeSeries = Range();
						else
							offsetFormulaNotAllowed = true;
						break;
					case amaSuperTrendU11VolaType.True_Range:
						rangeSeries = amaATR(Inputs[0], amaATRCalcMode.Wilder, 1);
						break;
				}
				if (thisVolaType == amaSuperTrendU11VolaType.Range || thisVolaType == amaSuperTrendU11VolaType.True_Range)
				{
					switch (thisOffsetType)
					{
						case amaSuperTrendU11OffsetType.Median: 
							offsetSeries = amaMovingMedian(rangeSeries, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.Adaptive_Laguerre: 
							offsetSeries = amaAdaptiveLaguerreFilter(rangeSeries, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.ADXVMA: 
							offsetSeries = amaADXVMA(rangeSeries, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.Butterworth_2: 
							offsetSeries = amaButterworthFilter(rangeSeries, 2, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.Butterworth_3: 
							offsetSeries = amaButterworthFilter(rangeSeries, 3, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.DEMA: 
							offsetSeries = DEMA(rangeSeries, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.Distant_Coefficient_Filter: 
							offsetSeries = amaDistantCoefficientFilter(rangeSeries, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.DWMA: 
							offsetSeries = amaDWMA(rangeSeries, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.EHMA: 
							offsetSeries = amaEHMA(rangeSeries, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.EMA: 
							offsetSeries = EMA(rangeSeries, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.Gauss_2: 
							offsetSeries = amaGaussianFilter(rangeSeries, 2, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.Gauss_3: 
							offsetSeries = amaGaussianFilter(rangeSeries, 3, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.Gauss_4: 
							offsetSeries = amaGaussianFilter(rangeSeries, 4, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.HMA: 
							if(volaPeriod > 1)
								offsetSeries = HMA(rangeSeries, volaPeriod);
							else
								offsetSeries = rangeSeries;
							break;
						case amaSuperTrendU11OffsetType.HoltEMA: 
							offsetSeries = amaHoltEMA(rangeSeries, volaPeriod, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.Laguerre: 
							offsetSeries = amaLaguerreFilter(rangeSeries, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.LinReg: 
							if(volaPeriod > 1)
								offsetSeries = LinReg(rangeSeries, volaPeriod);
							else
								offsetSeries = rangeSeries;
							break;
						case amaSuperTrendU11OffsetType.SMA: 
							offsetSeries = SMA(rangeSeries, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.SuperSmoother_2: 
							offsetSeries = amaSuperSmoother(rangeSeries, 2, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.SuperSmoother_3: 
							offsetSeries = amaSuperSmoother(rangeSeries, 3, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.SWMA: 
							offsetSeries = amaSWMA(rangeSeries, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.TEMA: 
							offsetSeries = TEMA(rangeSeries, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.Tillson_T3: 
							offsetSeries = amaTillsonT3(rangeSeries, amaT3CalcMode.Tillson, volaPeriod, 0.7);
							break;
						case amaSuperTrendU11OffsetType.TMA: 
							offsetSeries = TMA(rangeSeries, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.TWMA: 
							offsetSeries = amaTWMA(rangeSeries, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.VWMA: 
							offsetSeries = VWMA(rangeSeries, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.Wilder: 
							offsetSeries = EMA(rangeSeries, 2*volaPeriod-1);
							break;
						case amaSuperTrendU11OffsetType.WMA: 
							offsetSeries = WMA(rangeSeries, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.ZerolagTEMA: 
							offsetSeries = amaZerolagTEMA(rangeSeries, volaPeriod);
							break;
						case amaSuperTrendU11OffsetType.ZLEMA: 
							if(volaPeriod > 1)
								offsetSeries = ZLEMA(rangeSeries, volaPeriod);
							else
								offsetSeries = rangeSeries;
							break;
					}
				}						
		    	sessionIterator = new SessionIterator(Bars);
			}
			else if (State == State.Historical)
			{
				if(ChartBars != null)
				{	
					errorBrush = ChartControl.Properties.AxisPen.Brush;
					errorBrush.Freeze();
					errorFont = new SimpleFont("Arial", 24);
					indicatorIsOnPricePanel = (ChartPanel.PanelIndex == ChartBars.Panel);
				}	
				else
					indicatorIsOnPricePanel = false;
				errorMessage = false;
				if(midbandTypeNotAllowed)
				{
					DrawOnPricePanel = false;
					Draw.TextFixed(this, "error text1", errorText1, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);  
					errorMessage = true;
					return;
				}
				else if(reverseIntraBar && !calculateFromPriceData)
				{
					DrawOnPricePanel = false;
					Draw.TextFixed(this, "error text2", errorText2, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);  
					errorMessage = true;
					return;
				}
				else if(offsetFormulaNotAllowed)
				{
					DrawOnPricePanel = false;
					Draw.TextFixed(this, "error text 3", errorText3, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);  
					errorMessage = true;
					return;
				}
				else if(displacement < 0)
				{
					DrawOnPricePanel = false;
					Draw.TextFixed(this, "error text 4", errorText4, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);  
					errorMessage = true;
					return;
				}
				gap0 = (plot0Style == PlotStyle.Line)||(plot0Style == PlotStyle.Square);
				gap1 = (plot1Style == PlotStyle.Line)||(plot1Style == PlotStyle.Square);
				dotFont = new SimpleFont("Webdings", plot1Width + 2);
				triangleStringUp = char.ConvertFromUtf32(0x25B2);
				triangleStringDown = char.ConvertFromUtf32(0x25BC);
 				triangleFont = new SimpleFont("Arial", triangleFontSize);
				pathNewUptrend = string.Format( @"{0}sounds\{1}", NinjaTrader.Core.Globals.InstallDir, newUptrend);
				pathNewDowntrend = string.Format( @"{0}sounds\{1}", NinjaTrader.Core.Globals.InstallDir, newDowntrend);
				pathPotentialUptrend = string.Format( @"{0}sounds\{1}", NinjaTrader.Core.Globals.InstallDir, potentialUptrend);
				pathPotentialDowntrend = string.Format( @"{0}sounds\{1}", NinjaTrader.Core.Globals.InstallDir, potentialDowntrend);
			}	
		}

		protected override void OnBarUpdate()
        {
			if(errorMessage)
				return;
			
			if(CurrentBar < 2)
			{
				if(IsFirstTickOfBar)
				{
					preliminaryTrend[0] = 1.0;
					trend[0] = 1.0;
					if(CurrentBar == 0)
					{
						sumAbsDev.Reset();
						sumSquares.Reset();
						currentStopLong.Reset();
						currentStopShort.Reset();
					}
					else if(CurrentBar == 1)
					{
						movingBase = baseline[1];
						if(thisVolaType == amaSuperTrendU11VolaType.Range || thisVolaType == amaSuperTrendU11VolaType.True_Range)
							offset = Math.Max(TickSize, offsetSeries[1]);
						trailingAmount = multiplier * offset;
						sumAbsDev[0] = 0.0;
						sumSquares[0] = 0.0;
						currentStopLong[0] = movingBase - trailingAmount;
						currentStopShort[0] = movingBase + trailingAmount;
					}
					StopDot.Reset();
					StopLine.Reset();
					Values[2].Reset();
					PlotBrushes[0][0] = Brushes.Transparent;
					PlotBrushes[1][0] = Brushes.Transparent;
					PlotBrushes[2][0] = Brushes.Transparent;
				}
				return;
			}			
				
			if(IsFirstTickOfBar)
			{	
				movingBase = baseline[1];
				if(thisVolaType == amaSuperTrendU11VolaType.Range)
					offset = Math.Max(TickSize, offsetSeries[1]);
				else if(thisVolaType == amaSuperTrendU11VolaType.True_Range)
					offset = Math.Max(TickSize, offsetSeries[1]);
				else if(thisVolaType == amaSuperTrendU11VolaType.Res_Mean_Abs_Dev)
				{	
					if(CurrentBar <= volaPeriod)
					{
						sumAbsDev[0] = sumAbsDev[1] + Math.Abs(Input[1] - movingBase);
						offset = Math.Max(TickSize, sumAbsDev[0]/CurrentBar);
					}
					else
					{
						firstAbsDev = Math.Abs(Input[volaPeriod+1] - baseline[volaPeriod+1]);
						sumAbsDev[0] = sumAbsDev[1] - firstAbsDev + Math.Abs(Input[1] - movingBase);
						offset = Math.Max(TickSize, sumAbsDev[0]/volaPeriod);
					}
				}
				else if(thisVolaType == amaSuperTrendU11VolaType.Res_RMS_Dev)
				{	
					if(CurrentBar <= volaPeriod)
					{
						dist = Input[1] - movingBase;
						lastSquare = dist * dist;
						sumSquares[0] = sumSquares[1] + lastSquare;
						if(sumSquares[0] > 0)
							offset = Math.Max(TickSize, Math.Sqrt(sumSquares[0]/CurrentBar));
						else
							offset = TickSize;
					}
					else
					{
						dist = Input[volaPeriod+1] - baseline[volaPeriod+1];
						firstSquare = dist * dist;
						dist = Input[1] - movingBase;
						lastSquare = dist * dist;
						sumSquares[0] = sumSquares[1] - firstSquare + lastSquare;
						if(sumSquares[0] > 0)
							offset = Math.Max(TickSize, Math.Sqrt(sumSquares[0]/volaPeriod));
						else
							offset = TickSize;
					}
				}
				else
					offset = Math.Max(TickSize, offsetSeries[1]);
				trailingAmount = multiplier * offset;
				labelOffset = 0.3 * barVolatility[1];
				if(preliminaryTrend[1] > 0.5)
				{
					currentStopShort[0] = movingBase + trailingAmount;
					if(preliminaryTrend[2] > 0.5)
						currentStopLong[0] = Math.Max(currentStopLong[1], movingBase - trailingAmount);
					else
						currentStopLong[0] = Math.Max(Values[2][1], movingBase - trailingAmount); 
					StopDot[0] = currentStopLong[0];
					Values[2][0] = currentStopShort[0];
					StopLine[0] = currentStopLong[0];
					if(showStopDots)
					{	
						if(gap0 && preliminaryTrend[2] < -0.5)
							PlotBrushes[0][0]= Brushes.Transparent;
						else
							PlotBrushes[0][0] = upBrush;
					}
					else
						PlotBrushes[0][0]= Brushes.Transparent;
					if(showStopLine)
					{	
						if(gap1 && preliminaryTrend[2] < -0.5)
							PlotBrushes[1][0]= Brushes.Transparent;
						else
							PlotBrushes[1][0] = upBrush;
					}
					else
						PlotBrushes[1][0]= Brushes.Transparent;
				}
				else	
				{	
					currentStopLong[0] = movingBase - trailingAmount;
					if(preliminaryTrend[2] < -0.5)
						currentStopShort[0] = Math.Min(currentStopShort[1], movingBase + trailingAmount);
					else
						currentStopShort[0] = Math.Min(Values[2][1], movingBase + trailingAmount);
					StopDot[0] = currentStopShort[0];
					Values[2][0] = currentStopLong[0];
					StopLine[0] = currentStopShort[0];
					if(showStopDots)
					{	
						if(gap0 && preliminaryTrend[2] > 0.5)
							PlotBrushes[0][0]= Brushes.Transparent;
						else
							PlotBrushes[0][0] = downBrush;
					}
					else
						PlotBrushes[0][0]= Brushes.Transparent;
					if(showStopLine)
					{	
						if(gap1 && preliminaryTrend[2] > 0.5)
							PlotBrushes[1][0]= Brushes.Transparent;
						else
							PlotBrushes[1][0] = downBrush;
					}
					else
						PlotBrushes[1][0]= Brushes.Transparent;
				}
				
				if(showStopLine && CurrentBar >= BarsRequiredToPlot)
				{	
					DrawOnPricePanel = false;
					if(plot1Style == PlotStyle.Line && reverseIntraBar) 
					{
						if(preliminaryTrend[1] > 0.5 && preliminaryTrend[2] < -0.5)
							Draw.Line(this, "line" + CurrentBar, false, 1-displacement, Values[2][1], -displacement, StopLine[0], upBrush, DashStyleHelper.Solid, plot1Width);
						else if(preliminaryTrend[1] < -0.5 && preliminaryTrend[2] > 0.5)
							Draw.Line(this, "line" + CurrentBar, false, 1-displacement, Values[2][1], -displacement, StopLine[0], downBrush, DashStyleHelper.Solid, plot1Width);
					}
					else if(plot1Style == PlotStyle.Square && !showStopDots) 
					{
						if(trend[1] > 0.5 && trend[2] < -0.5)
							Draw.Text(this, "dot" + CurrentBar, false, dotString, -displacement, StopLine[0], 0 , upBrush, dotFont, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0); 
						else if(trend[1] < -0.5 && trend[2] > 0.5)
							Draw.Text(this, "dot" + CurrentBar, false, dotString, -displacement, StopLine[0], 0 , downBrush, dotFont, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0); 
					}
				}
				
				if(showPaintBars && CurrentBar >= BarsRequiredToPlot)
				{
					if (preliminaryTrend[1] > 0.5)
					{	
						if(Open[0] < Close[0])
							BarBrushes[-displacement] = upBrushUp;
						else
							BarBrushes[-displacement] = upBrushDown;
						CandleOutlineBrushes[-displacement] = upBrushOutline;
					}	
					else
					{	
						if(Open[0] < Close[0])
							BarBrushes[-displacement] = downBrushUp;
						else
							BarBrushes[-displacement] = downBrushDown;
						CandleOutlineBrushes[-displacement] = downBrushOutline;
					}
				}
				stoppedOut = false;
			}
			
			if(reverseIntraBar) // only one trend change per bar is permitted
			{
				if(!stoppedOut)
				{
					if (preliminaryTrend[1] > 0.5 && Low[0] < currentStopLong[0])
					{
						preliminaryTrend[0] = -1.0;
						stoppedOut = true;
						if(showStopDots && !gap0)
							PlotBrushes[2][0] = downBrush;
					}	
					else if (preliminaryTrend[1] < -0.5 && High[0] > currentStopShort[0])
					{
						preliminaryTrend[0] = 1.0;
						stoppedOut = true;
						if(showStopDots && !gap0)
							PlotBrushes[2][0] = upBrush;
					}
					else
						preliminaryTrend[0] = preliminaryTrend[1];
				}
			}
			else 
			{
				if(calculateFromPriceData)
				{
					if (preliminaryTrend[1] > 0.5 && Close[0] < currentStopLong[0])
						preliminaryTrend[0] = - 1.0;
					else if (preliminaryTrend[1] < -0.5 && Close[0] > currentStopShort[0])
						preliminaryTrend[0] = 1.0;
					else
						preliminaryTrend[0] = preliminaryTrend[1];
				}
				else
				{
					if (preliminaryTrend[1] > 0.5 && Input[0] < currentStopLong[0])
						preliminaryTrend[0] = - 1.0;
					else if (preliminaryTrend[1] < -0.5 && Input[0] > currentStopShort[0])
						preliminaryTrend[0] = 1.0;
					else
						preliminaryTrend[0] = preliminaryTrend[1];
				}	
			}
					
			// this information can be accessed by a strategy
			if(Calculate == Calculate.OnBarClose)
				trend[0] = preliminaryTrend[0];
			else if(IsFirstTickOfBar && !reverseIntraBar)
				trend[0] = preliminaryTrend[1];
			else if(reverseIntraBar)
				trend[0] = preliminaryTrend[0];
			
			if(CurrentBar > totalBarsRequiredToPlot)
			{
				if(showPaintBars)
				{
					if(trend[shift] > 0)
					{
						if(Open[0] < Close[0])
							BarBrushes[0] = upBrushUp;
						else
							BarBrushes[0] = upBrushDown;
						CandleOutlineBrushes[0] = upBrushOutline;
					}
					else
					{	
						if(Open[0] < Close[0])
							BarBrushes[0] = downBrushUp;
						else
							BarBrushes[0] = downBrushDown;
						CandleOutlineBrushes[0] = downBrushOutline;
					}
				}
				if(showTriangles)
				{
					DrawOnPricePanel = true;
					if(Calculate == Calculate.OnBarClose)
					{	
						if(trend[displacement] > 0.5 && trend[displacement+1] < -0.5)
							Draw.Text(this, "triangle" + CurrentBar, false, triangleStringUp, 0, Low[0] - labelOffset, -triangleFontSize, bullishSignalBrush, triangleFont, TextAlignment.Center, Brushes.Transparent, signalBackBrush, 100);
						else if(trend[displacement] < -0.5 && trend[displacement+1] > 0.5)
							Draw.Text(this, "triangle" + CurrentBar, false, triangleStringDown, 0, High[0] + labelOffset, triangleFontSize, bearishSignalBrush, triangleFont, TextAlignment.Center, Brushes.Transparent, signalBackBrush, 100);
					}
					else if (reverseIntraBar)
					{
						if(IsFirstTickOfBar)
						{
							drawTriangleUp = false;
							drawTriangleDown = false;
						}	
						if(!drawTriangleUp && trend[displacement] > 0.5 && trend[displacement+1] < -0.5)
						{	
							low = Low[0];
							Draw.Text(this, "triangle" + CurrentBar, false, triangleStringUp, 0, low - labelOffset, -triangleFontSize, bullishSignalBrush, triangleFont, TextAlignment.Center, Brushes.Transparent, signalBackBrush, 100);
							drawTriangleUp = true;
						}	
						else if(drawTriangleUp && (IsFirstTickOfBar || Low[0] < low))
						{	
							low = Low[0];
							Draw.Text(this, "triangle" + CurrentBar, false, triangleStringUp, 0, low - labelOffset, -triangleFontSize, bullishSignalBrush, triangleFont, TextAlignment.Center, Brushes.Transparent, signalBackBrush, 100);
						}
						if(!drawTriangleDown && trend[displacement] < -0.5 && trend[displacement+1] > 0.5)
						{	
							drawTriangleDown = true;
							high = High[0];
							Draw.Text(this, "triangle" + CurrentBar, false, triangleStringDown, 0, high + labelOffset, triangleFontSize, bearishSignalBrush, triangleFont, TextAlignment.Center, Brushes.Transparent, signalBackBrush, 100);
						}	
						else if(drawTriangleDown && (IsFirstTickOfBar || High[0] > high))
						{	
							high = High[0];
							Draw.Text(this, "triangle" + CurrentBar, false, triangleStringDown, 0, high + labelOffset, triangleFontSize, bearishSignalBrush, triangleFont, TextAlignment.Center, Brushes.Transparent, signalBackBrush, 100);
						}
					}	
					else if (IsFirstTickOfBar)	
					{
						if(trend[displacement] > 0.5 && trend[displacement+1] < -0.5)
							Draw.Text(this, "triangle" + CurrentBar, false, triangleStringUp, 1, Low[1] - labelOffset, -triangleFontSize, bullishSignalBrush, triangleFont, TextAlignment.Center, Brushes.Transparent, signalBackBrush, 100);
						else if(trend[displacement] < -0.5 && trend[displacement+1] > 0.5)
							Draw.Text(this, "triangle" + CurrentBar, false, triangleStringDown, 1, High[1] + labelOffset, triangleFontSize, bearishSignalBrush, triangleFont, TextAlignment.Center, Brushes.Transparent, signalBackBrush, 100);
					}	
				}	
			}
			
			if (soundAlerts && State == State.Realtime && IsConnected() && (Calculate == Calculate.OnBarClose || reverseIntraBar))
			{
				if(preliminaryTrend[0] > 0.5 && preliminaryTrend[1] < -0.5)		
				{
					try
					{
							Alert("New_Uptrend", Priority.Medium,"New Uptrend", pathNewUptrend, rearmTime, signalBackBrush, bullishSignalBrush);
					}
					catch{}
				}
				else if(preliminaryTrend[0] < -0.5 && preliminaryTrend[1] > 0.5)	 
				{
					try
					{
							Alert("New_Downtrend", Priority.Medium,"New Downtrend", pathNewDowntrend, rearmTime, signalBackBrush, bearishSignalBrush);
					}
					catch{}
				}
			}				
			if (soundAlerts && State == State.Realtime && IsConnected() && Calculate != Calculate.OnBarClose && !reverseIntraBar)
			{
				if(preliminaryTrend[0] > 0.5 && preliminaryTrend[1] < -0.5)		
				{
					try
					{
							Alert("Potential_Uptrend", Priority.Medium,"Potential Uptrend", pathPotentialUptrend, rearmTime, signalBackBrush, bullishSignalBrush);
					}
					catch{}
				}
				else if(preliminaryTrend[0] < -0.5 && preliminaryTrend[1] > 0.5)	 
				{
					try
					{
							Alert("Potential_Downtrend", Priority.Medium,"Potential Downtrend", pathPotentialDowntrend, rearmTime, signalBackBrush, bearishSignalBrush);
					}
					catch{}
				}
			}	
		}

		#region Properties
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> StopDot
		{
			get { return Values[0]; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> StopLine
		{
			get { return Values[1]; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> Trend
		{
			get { return trend; }
		}
		
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Baseline smoothing", Description = "Select moving average type or moving median type for the base line", GroupName = "Algorithmic Options", Order = 0)]
		public amaSuperTrendU11BaseType ThisBaseType
		{	
            get { return thisBaseType; }
            set { thisBaseType = value; }
		}
			
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Offset formula", Description = "Select volatility formula used for offset smoothing", GroupName = "Algorithmic Options", Order = 1)]
 		[RefreshProperties(RefreshProperties.All)] 
		public amaSuperTrendU11VolaType ThisVolaType
		{	
            get { return thisVolaType; }
            set { thisVolaType = value; }
		}
			
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Offset smoothing", Description = "Select moving average type or moving median type for the offset", GroupName = "Algorithmic Options", Order = 2)]
		public amaSuperTrendU11OffsetType ThisOffsetType
		{	
            get { return thisOffsetType; }
            set { thisOffsetType = value; }
		}
			
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Reverse intra-bar", Description = "Select between trend change intra-bar or at the bar close", GroupName = "Algorithmic Options", Order = 3)]
        public bool ReverseIntraBar
        {
            get { return reverseIntraBar; }
            set { reverseIntraBar = value; }
        }
		
		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Baseline period", Description = "Sets the lookback period for the baseline", GroupName = "Input Parameters", Order = 0)]
		public int BasePeriod
		{	
            get { return basePeriod; }
            set { basePeriod = value; }
		}
			
		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Offset period", Description = "Sets the lookback period for the volatility measure", GroupName = "Input Parameters", Order = 1)]
		public int RangePeriod
		{	
            get { return volaPeriod; }
            set { volaPeriod = value; }
		}
			
		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Offset multiplier", Description = "Sets the multiplier for the volatility offset", GroupName = "Input Parameters", Order = 2)]
		public double Multiplier
		{	
            get { return multiplier; }
            set { multiplier = value; }
		}
			
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show stop dots", GroupName = "Display Options", Order = 0)]
        public bool ShowStopDots
        {
            get { return showStopDots; }
            set { showStopDots = value; }
        }
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show stop line", GroupName = "Display Options", Order = 1)]
        public bool ShowStopLine
        {
            get { return showStopLine; }
            set { showStopLine = value; }
        }
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show paint bars", GroupName = "Display Options", Order = 2)]
        public bool ShowPaintBars
        {
            get { return showPaintBars; }
            set { showPaintBars = value; }
        }
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show triangles", GroupName = "Display Options", Order = 3)]
        public bool ShowTriangles
        {
            get { return showTriangles; }
            set { showTriangles = value; }
        }
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Bullish color", Description = "Sets the color for the trailing stop", GroupName = "Plots", Order = 0)]
		public Brush UpBrush
		{ 
			get {return upBrush;}
			set {upBrush = value;}
		}

		[Browsable(false)]
		public string UpBrushSerializable
		{
			get { return Serialize.BrushToString(upBrush); }
			set { upBrush = Serialize.StringToBrush(value); }
		}					
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Bearish color", Description = "Sets the color for the trailing stop", GroupName = "Plots", Order = 1)]
		public Brush DownBrush
		{ 
			get {return downBrush;}
			set {downBrush = value;}
		}

		[Browsable(false)]
		public string DownBrushSerializable
		{
			get { return Serialize.BrushToString(downBrush); }
			set { downBrush = Serialize.StringToBrush(value); }
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plotstyle stop dots", Description = "Sets the plot style for the stop dots", GroupName = "Plots", Order = 2)]
		public PlotStyle Plot0Style
		{	
            get { return plot0Style; }
            set { plot0Style = value; }
		}
		
		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Dot size", Description = "Sets the size for the stop dots", GroupName = "Plots", Order = 3)]
		public int Plot0Width
		{	
            get { return plot0Width; }
            set { plot0Width = value; }
		}
			
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plotstyle stop line", Description = "Sets the plot style for the stop line", GroupName = "Plots", Order = 4)]
		public PlotStyle Plot1Style
		{	
            get { return plot1Style; }
            set { plot1Style = value; }
		}
		
		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Width stop line", Description = "Sets the width for the stop line", GroupName = "Plots", Order = 5)]
		public int Plot1Width
		{	
            get { return plot1Width; }
            set { plot1Width = value; }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Bullish signal", Description = "Sets the forecolor for a bullish signal", GroupName = "Trade Signals", Order = 0)]
		public Brush BullishSignalBrush
		{ 
			get {return bullishSignalBrush;}
			set {bullishSignalBrush = value;}
		}

		[Browsable(false)]
		public string BullishSignalBrushSerializable
		{
			get { return Serialize.BrushToString(bullishSignalBrush); }
			set { bullishSignalBrush = Serialize.StringToBrush(value); }
		}					
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Bearish signal", Description = "Sets the forecolor for a bearish signal", GroupName = "Trade Signals", Order = 1)]
		public Brush BearishSignalBrush
		{ 
			get {return bearishSignalBrush;}
			set {bearishSignalBrush = value;}
		}

		[Browsable(false)]
		public string BearishSignalBrushSerializable
		{
			get { return Serialize.BrushToString(bearishSignalBrush); }
			set { bearishSignalBrush = Serialize.StringToBrush(value); }
		}					
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Signal backcolor", Description = "Sets the backcolor for the triangles", GroupName = "Trade Signals", Order = 2)]
		public Brush SignalBackBrush
		{ 
			get {return signalBackBrush;}
			set {signalBackBrush = value;}
		}

		[Browsable(false)]
		public string SignalBackBrushSerializable
		{
			get { return Serialize.BrushToString(signalBackBrush); }
			set { signalBackBrush = Serialize.StringToBrush(value); }
		}					
		
		[Range(1, 256)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Triangle size", Description = "Allows for adjusting the triangle size", GroupName = "Trade Signals", Order = 3)]
		public int TriangleFontSize
		{	
            get { return triangleFontSize; }
            set { triangleFontSize = value; }
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
		[Display(ResourceType = typeof(Custom.Resource), Name = "Bullish candle outline", Description = "Sets the color for bullish candle outlines", GroupName = "Paint Bars", Order = 2)]
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
		[Display(ResourceType = typeof(Custom.Resource), Name = "Bearish candle outline", Description = "Sets the color for bearish candle outlines", GroupName = "Paint Bars", Order = 5)]
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
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Sound alerts", GroupName = "Sound Alerts", Order = 0)]
        public bool SoundAlerts
        {
            get { return soundAlerts; }
            set { soundAlerts = value; }
        }
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "New uptrend", Description = "Sound file for confirmed new uptrend", GroupName = "Sound Alerts", Order = 1)]
		public string NewUptrend
		{	
            get { return newUptrend; }
            set { newUptrend = value; }
		}		
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "New downtrend", Description = "Sound file for confirmed new downtrend", GroupName = "Sound Alerts", Order = 2)]
		public string NewDowntrend
		{	
            get { return newDowntrend; }
            set { newDowntrend = value; }
		}		
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Potential uptrend", Description = "Sound file for potential new uptrend", GroupName = "Sound Alerts", Order = 3)]
		public string PotentialUptrend
		{	
            get { return potentialUptrend; }
            set { potentialUptrend = value; }
		}		
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Potential downtrend", Description = "Sound file for potential new downtrend", GroupName = "Sound Alerts", Order = 4)]
		public string PotentialDowntrend
		{	
            get { return potentialDowntrend; }
            set { potentialDowntrend = value; }
		}				
		
		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Rearm time", Description = "Rearm time for alerts in seconds", GroupName = "Sound Alerts", Order = 5)]
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
			set { ;}
		}
		#endregion
		
		#region Miscellaneous

		public override string FormatPriceMarker(double price)
		{
			if(indicatorIsOnPricePanel)
				return Instrument.MasterInstrument.FormatPrice(Instrument.MasterInstrument.RoundToTickSize(price));
			else
				return base.FormatPriceMarker(price);
		}
		
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
	public class amaSuperTrendU11TypeConverter : NinjaTrader.NinjaScript.IndicatorBaseConverter
	{
		public override bool GetPropertiesSupported(ITypeDescriptorContext context) { return true; }

		public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object value, Attribute[] attributes)
		{
			PropertyDescriptorCollection propertyDescriptorCollection = base.GetPropertiesSupported(context) ? base.GetProperties(context, value, attributes) : TypeDescriptor.GetProperties(value, attributes);

			amaSuperTrendU11			thisSuperTrendInstance		= (amaSuperTrendU11) value;
			amaSuperTrendU11VolaType	volaTypeFromInstance		= thisSuperTrendInstance.ThisVolaType;
			
			PropertyDescriptorCollection adjusted = new PropertyDescriptorCollection(null);
			
			foreach (PropertyDescriptor thisDescriptor in propertyDescriptorCollection)
			{
				if ((volaTypeFromInstance == amaSuperTrendU11VolaType.Res_Mean_Abs_Dev || volaTypeFromInstance == amaSuperTrendU11VolaType.Res_RMS_Dev) && thisDescriptor.Name == "ThisOffsetType")
					adjusted.Add(new PropertyDescriptorExtended(thisDescriptor, o => value, null, new Attribute[] {new BrowsableAttribute(false), }));
				else	
					adjusted.Add(thisDescriptor);
			}
			return adjusted;
		}
	}
}

#region Global Enums

public enum amaSuperTrendU11BaseType 
{
	Median, 
	Median_TPO, 
	Median_VWTPO, 
	Mean_TPO, 
	Mean_VWTPO, 
	Adaptive_Laguerre, 
	ADXVMA, 
	Butterworth_2, 
	Butterworth_3, 
	DEMA, 
	Distant_Coefficient_Filter, 
	DWMA, 
	EHMA, 
	EMA, 
	Gauss_2, 
	Gauss_3, 
	Gauss_4, 
	HMA, 
	HoltEMA, 
	Laguerre, 
	LinReg, 
	RWMA, 
	SMA, 
	SuperSmoother_2, 
	SuperSmoother_3, 
	SWMA, 
	TEMA, 
	Tillson_T3, 
	TMA, 
	TWMA, 
	VWMA, 
	Wilder, 
	WMA, 
	ZerolagHATEMA, 
	ZerolagTEMA, 
	ZLEMA
}

public enum amaSuperTrendU11OffsetType 
{
	Median, 
	Adaptive_Laguerre, 
	ADXVMA, 
	Butterworth_2, 
	Butterworth_3, 
	DEMA, 
	Distant_Coefficient_Filter, 
	DWMA, 
	EHMA, 
	EMA, 
	Gauss_2, 
	Gauss_3, 
	Gauss_4, 
	HMA, 
	HoltEMA, 
	Laguerre, 
	LinReg, 
	SMA, 
	SuperSmoother_2, 
	SuperSmoother_3, 
	SWMA, 
	TEMA, 
	Tillson_T3, 
	TMA, 
	TWMA, 
	VWMA, 
	Wilder, 
	WMA, 
	ZerolagTEMA, 
	ZLEMA
}

public enum	amaSuperTrendU11VolaType 
{
	Range, 
	True_Range, 
	Res_Mean_Abs_Dev, 
	Res_RMS_Dev
}

#endregion

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private LizardIndicators.amaSuperTrendU11[] cacheamaSuperTrendU11;
		public LizardIndicators.amaSuperTrendU11 amaSuperTrendU11(amaSuperTrendU11BaseType thisBaseType, amaSuperTrendU11VolaType thisVolaType, amaSuperTrendU11OffsetType thisOffsetType, bool reverseIntraBar, int basePeriod, int rangePeriod, double multiplier)
		{
			return amaSuperTrendU11(Input, thisBaseType, thisVolaType, thisOffsetType, reverseIntraBar, basePeriod, rangePeriod, multiplier);
		}

		public LizardIndicators.amaSuperTrendU11 amaSuperTrendU11(ISeries<double> input, amaSuperTrendU11BaseType thisBaseType, amaSuperTrendU11VolaType thisVolaType, amaSuperTrendU11OffsetType thisOffsetType, bool reverseIntraBar, int basePeriod, int rangePeriod, double multiplier)
		{
			if (cacheamaSuperTrendU11 != null)
				for (int idx = 0; idx < cacheamaSuperTrendU11.Length; idx++)
					if (cacheamaSuperTrendU11[idx] != null && cacheamaSuperTrendU11[idx].ThisBaseType == thisBaseType && cacheamaSuperTrendU11[idx].ThisVolaType == thisVolaType && cacheamaSuperTrendU11[idx].ThisOffsetType == thisOffsetType && cacheamaSuperTrendU11[idx].ReverseIntraBar == reverseIntraBar && cacheamaSuperTrendU11[idx].BasePeriod == basePeriod && cacheamaSuperTrendU11[idx].RangePeriod == rangePeriod && cacheamaSuperTrendU11[idx].Multiplier == multiplier && cacheamaSuperTrendU11[idx].EqualsInput(input))
						return cacheamaSuperTrendU11[idx];
			return CacheIndicator<LizardIndicators.amaSuperTrendU11>(new LizardIndicators.amaSuperTrendU11(){ ThisBaseType = thisBaseType, ThisVolaType = thisVolaType, ThisOffsetType = thisOffsetType, ReverseIntraBar = reverseIntraBar, BasePeriod = basePeriod, RangePeriod = rangePeriod, Multiplier = multiplier }, input, ref cacheamaSuperTrendU11);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.LizardIndicators.amaSuperTrendU11 amaSuperTrendU11(amaSuperTrendU11BaseType thisBaseType, amaSuperTrendU11VolaType thisVolaType, amaSuperTrendU11OffsetType thisOffsetType, bool reverseIntraBar, int basePeriod, int rangePeriod, double multiplier)
		{
			return indicator.amaSuperTrendU11(Input, thisBaseType, thisVolaType, thisOffsetType, reverseIntraBar, basePeriod, rangePeriod, multiplier);
		}

		public Indicators.LizardIndicators.amaSuperTrendU11 amaSuperTrendU11(ISeries<double> input , amaSuperTrendU11BaseType thisBaseType, amaSuperTrendU11VolaType thisVolaType, amaSuperTrendU11OffsetType thisOffsetType, bool reverseIntraBar, int basePeriod, int rangePeriod, double multiplier)
		{
			return indicator.amaSuperTrendU11(input, thisBaseType, thisVolaType, thisOffsetType, reverseIntraBar, basePeriod, rangePeriod, multiplier);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.LizardIndicators.amaSuperTrendU11 amaSuperTrendU11(amaSuperTrendU11BaseType thisBaseType, amaSuperTrendU11VolaType thisVolaType, amaSuperTrendU11OffsetType thisOffsetType, bool reverseIntraBar, int basePeriod, int rangePeriod, double multiplier)
		{
			return indicator.amaSuperTrendU11(Input, thisBaseType, thisVolaType, thisOffsetType, reverseIntraBar, basePeriod, rangePeriod, multiplier);
		}

		public Indicators.LizardIndicators.amaSuperTrendU11 amaSuperTrendU11(ISeries<double> input , amaSuperTrendU11BaseType thisBaseType, amaSuperTrendU11VolaType thisVolaType, amaSuperTrendU11OffsetType thisOffsetType, bool reverseIntraBar, int basePeriod, int rangePeriod, double multiplier)
		{
			return indicator.amaSuperTrendU11(input, thisBaseType, thisVolaType, thisOffsetType, reverseIntraBar, basePeriod, rangePeriod, multiplier);
		}
	}
}

#endregion
