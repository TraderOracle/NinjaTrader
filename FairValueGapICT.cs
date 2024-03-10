#region Using declarations
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using Brush = System.Windows.Media.Brush;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{

	[Gui.CategoryOrder("FVG Data Series", 1)]
	[Gui.CategoryOrder("Parameters", 3)]
	[Gui.CategoryOrder("Time Ranges", 5)]
	[Gui.CategoryOrder("FVG Colors", 7)]
	[Gui.CategoryOrder("FVG Data Series Label", 9)]

    public class FVGICT : Indicator
    {

		private bool IsDebug = false;

		#region Vars
		private int 		MIN_BARS_REQUIRED	= 4;
		private int 		iDataSeriesIdx 		= 1;
		private static String indiName			= "Fair Value Gap v0.0.3.1",	consequentEncroachmentTag	= "_CE";
		private String 		InstanceId,		sDataSeries;
		private bool 		isDataLoaded	= false; 
		private DateTime 	future,			sessionEnd;
		private TimeSpan 	tsSilverBullet1	= new TimeSpan(0, 03, 00, 0, 0); //  3:00:00 AM
		private TimeSpan 	tsSilverBullet2	= new TimeSpan(0, 10, 00, 0, 0); // 10:00:00 AM
		private TimeSpan 	tsSilverBullet3	= new TimeSpan(0, 14, 00, 0, 0); // 2:00:00 PM
		private TimeSpan 	tsEndTime1,		tsEndTime2,		tsEndTime3;
		private TimeSpan 	tsTZDifference;
		
		private List<FVG>	fvgList 		= new List<FVG>();
		private ATR 		atr;
		private SessionIterator sessionIterator;
		#endregion

		
        protected override void OnStateChange()
        {
            Debug("FVG >>>>> State."+ State);

            if (State == State.SetDefaults)
            {
				#region SetDefaults
				Description 			= @"Fair Value Gap (ICT)";
				Name 					= indiName; // "\"Fair Value Gap v0.0.2.3\"";
				Calculate 				= Calculate.OnBarClose;
				IsOverlay 				= true;
				DrawOnPricePanel 		= true;
				DisplayInDataBox 		= false;
//				DrawHorizontalGridLines	= false;
//				DrawVerticalGridLines 	= false;
				PaintPriceMarkers 		= false;
				ScaleJustification		 = NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive = true;

				UseFVGDataSeries 	= false;
				FVGBarsPeriodType 	= FVGPeriodTypes.Minute;
				FVGSeriesPeriod 	= 1;
				
				MaxDaysForward		= 50;
//				MaxLookbackBars 	= 500;
				UseATR 				= true;
				ImpulseFactor 		= 1.1;
				ATRPeriod 			= 10;
				MinimumFVGSize 		= 2;
				AllBarsSameDirection = true;
				HideFilledGaps 		= false;
				FillType 			= FVGFillType.CLOSE_THROUGH;
				DisplayCE 			= false;

				    tsTZDifference	= TimeZoneInfo.Local.BaseUtcOffset - TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time").BaseUtcOffset;
				UseTimeRange1		= false;
				StartTime1			= tsSilverBullet1.Add(tsTZDifference);
				TimeRangeMinutes1	= 60;
				UseTimeRange2		= false;
				StartTime2			= tsSilverBullet2.Add(tsTZDifference);
				TimeRangeMinutes2	= 60;
				UseTimeRange3		= false;
				StartTime3			= tsSilverBullet3.Add(tsTZDifference);
				TimeRangeMinutes3	= 60;
				
				DrawLabel 			= false;
				LabelPosition 		= TextPosition.TopRight;
				LabelFont 			= new SimpleFont("Verdana", 12);
				LabelTextBrush 		= Brushes.WhiteSmoke;
				LabelBorderBrush 	= Brushes.DimGray;
				LabelFillBrush 		= Brushes.Blue;
				LabelFillOpacity 	= 50;

				UpBrush 			= Brushes.LimeGreen;
				UpAreaBrush 		= Brushes.LimeGreen;
				UpGapFilledBrush	= Brushes.Green;
				DownBrush 			= Brushes.Crimson;
				DownAreaBrush 		= Brushes.Crimson;
				DownGapFilledBrush	= Brushes.DarkRed;
				ActiveAreaOpacity 	= 20;
				FilledAreaOpacity 	= 15;
				#endregion
			}
			else if (State == State.Configure)
			{
				#region Configure
				if(UseFVGDataSeries)	Debug("\tAdding " + FVGBarsPeriodType + " [" + FVGSeriesPeriod + "] Data Series");

				// Add additional data series
				if (UseFVGDataSeries)
				{
				    AddDataSeries((BarsPeriodType)FVGBarsPeriodType, FVGSeriesPeriod);
				    iDataSeriesIdx = 1;
				}
				else
				{
				    iDataSeriesIdx = 0;
				}
				// Helps keep track of draw object tags if multiple instances are present on the same chart.
				InstanceId	= Guid.NewGuid().ToString();	// This code produces a result similar to the following: 0f8fad5b-d9cb-469f-a165-70867728950e
				
				tsEndTime1	= StartTime1.Add(TimeSpan.FromMinutes(TimeRangeMinutes1));
				tsEndTime2	= StartTime1.Add(TimeSpan.FromMinutes(TimeRangeMinutes2));
				tsEndTime3	= StartTime1.Add(TimeSpan.FromMinutes(TimeRangeMinutes3));
				
				UpBrush.Freeze();
				UpAreaBrush.Freeze();
				UpGapFilledBrush.Freeze();
				DownBrush.Freeze();
				DownAreaBrush.Freeze();
				DownGapFilledBrush.Freeze();
				#endregion
			}
			else if (State == State.DataLoaded)
			{
				isDataLoaded = true;
				// Add ATR
				atr = ATR(BarsArray[iDataSeriesIdx], ATRPeriod);
				//Get series name value for Label
				sDataSeries =  "FVG("+ BarsArray[iDataSeriesIdx].BarsPeriod.ToString() +")";
            }
			else if (State == State.Historical)
			{
				sessionIterator = new SessionIterator(Bars);
				sessionIterator.GetNextSession(Bars.GetTime(0), true);
				sessionEnd	= sessionIterator.ActualSessionEnd;
				Debug("\tsessionEnd: " + sessionEnd);
            }
        }

		
        protected override void OnBarUpdate()
        {
            // Only operate on selected data series type
            if (BarsInProgress != iDataSeriesIdx || CurrentBars[iDataSeriesIdx] < MIN_BARS_REQUIRED)
			{
				Debug("\t\t Time: "+Time[0].ToString().PadRight(22)+" \t CB: "+CurrentBar);
				return;
			}
			
			// on new bars session, find the next trading session
			if (Bars.IsFirstBarOfSession)
			{
				Debug("Calculating the Session time for " + Time[0]);
				// use the current bar time to calculate the next session
				sessionIterator.GetNextSession(Time[0], true);
				sessionEnd	= sessionIterator.ActualSessionEnd;

				Debug(" \t .ActualSessionEnd: "+ sessionEnd);
			}

            // Nothing to do if current bar is earlier than lookback max
			// This does NOT do anything other than prevent FVG detection at the beginning of the chart !!!
//            if (CurrentBars[iDataSeriesIdx] <= (Bars.Count - Math.Min(Bars.Count, MaxLookbackBars)) + MIN_BARS_REQUIRED) return;

            if (DrawLabel)
            {
                Draw.TextFixed(this, "FVGLABEL_" + InstanceId, sDataSeries, LabelPosition, LabelTextBrush, LabelFont, LabelBorderBrush, LabelFillBrush, LabelFillOpacity);
            }

			Debug("Checking for FVGs that are filled. \t\t Time: "+Time[0].ToString().PadRight(22)+"\t CB: "+CurrentBar);
			// Mark FVGs that have been filled
			CheckFilledFVGs();

			bool isInTimeRange = !(UseTimeRange1 || UseTimeRange2 || UseTimeRange3);			//Debug("#1  isInTimeRange = "+isInTimeRange);

			if(UseTimeRange1 && Time[1].TimeOfDay.CompareTo(StartTime1) >= 0 && Time[0].TimeOfDay.CompareTo(tsEndTime1) <= 0)
				isInTimeRange = true;
			else if(UseTimeRange2 && Time[1].TimeOfDay.CompareTo(StartTime2) >= 0 && Time[0].TimeOfDay.CompareTo(tsEndTime2) <= 0)
				isInTimeRange = true;
			else if(UseTimeRange3 && Time[1].TimeOfDay.CompareTo(StartTime3) >= 0 && Time[0].TimeOfDay.CompareTo(tsEndTime3) <= 0)
				isInTimeRange = true;
			Debug("#2  isInTimeRange = "+isInTimeRange+" \t\t Time: "+Time[0]);
			
			Debug("Checking time range and impluse move filters.");

			// FVG only applies if there's been an impulse move
			if (isInTimeRange && ( !UseATR || (UseATR && Math.Abs(Highs[iDataSeriesIdx][1] - Lows[iDataSeriesIdx][1]) >= ImpulseFactor * atr.Value[0])) )
			{
				Debug("\tGot past filter test.");

				int daysToAdd	= (Time[0].DayOfWeek == DayOfWeek.Friday && MaxDaysForward > 0) ? MaxDaysForward+2 : MaxDaysForward;
				future = sessionEnd.AddDays(daysToAdd); // Times[iDataSeriesIdx][0].AddDays(ChartBars.Properties.DaysBack);

				// FVG while going UP
				// Low[0] > High[2]
				if ((!AllBarsSameDirection || Closes[iDataSeriesIdx][2] > Opens[iDataSeriesIdx][2] && Closes[iDataSeriesIdx][1] > Opens[iDataSeriesIdx][1] && Closes[iDataSeriesIdx][0] > Opens[iDataSeriesIdx][0]) && 
					Lows[iDataSeriesIdx][0] > Highs[iDataSeriesIdx][2] && (Math.Abs(Lows[iDataSeriesIdx][0] - Highs[iDataSeriesIdx][2]) >= MinimumFVGSize))
				{
					//Debug("\tUp FVG Found.");

					string tag = "FVGUP_" + InstanceId + "_" + CurrentBars[iDataSeriesIdx];
					FVG fvg = new FVG(tag, FVGType.S, Highs[iDataSeriesIdx][2], Lows[iDataSeriesIdx][0], Times[iDataSeriesIdx][2], future);
					Debug("\t*** Drawing Up FVG [ "+ fvg.StartDateTime +", "+ future +", "+ fvg.LowerPrice +", "+ fvg.UpperPrice +" ] ****");

					Draw.Rectangle(this, tag, false, fvg.StartDateTime, fvg.LowerPrice, future, fvg.UpperPrice, UpBrush, UpAreaBrush, ActiveAreaOpacity, true);

					if (DisplayCE) Draw.Line(this, tag + consequentEncroachmentTag, false, fvg.StartDateTime, fvg.ConsequentEncroachmentPrice, future, fvg.ConsequentEncroachmentPrice, UpBrush, DashStyleHelper.Dash, 1);
					fvgList.Add(fvg);
				}
				// FVG while going DOWN
				// High[0] < Low[2]
				if ((!AllBarsSameDirection || Closes[iDataSeriesIdx][2] < Opens[iDataSeriesIdx][2] && Closes[iDataSeriesIdx][1] < Opens[iDataSeriesIdx][1] && Closes[iDataSeriesIdx][0] < Opens[iDataSeriesIdx][0]) && 
					Highs[iDataSeriesIdx][0] < Lows[iDataSeriesIdx][2] && (Math.Abs(Highs[iDataSeriesIdx][0] - Lows[iDataSeriesIdx][2]) >= MinimumFVGSize))
				{
					//Debug("\tDown FVG Found.");

					string tag = "FVGDOWN_" + InstanceId + "_" + CurrentBars[iDataSeriesIdx];
					FVG fvg = new FVG(tag, FVGType.R, Highs[iDataSeriesIdx][0], Lows[iDataSeriesIdx][2], Times[iDataSeriesIdx][2], future);
					Debug("\t*** Drawing Down FVG [ "+ fvg.StartDateTime +", "+ future +", "+ fvg.UpperPrice +", "+ fvg.LowerPrice +" ] ****");

					Draw.Rectangle(this, tag, false, fvg.StartDateTime, fvg.UpperPrice, future, fvg.LowerPrice, DownBrush, DownAreaBrush, ActiveAreaOpacity, true);

					if (DisplayCE) Draw.Line(this, tag + consequentEncroachmentTag, false, fvg.StartDateTime, fvg.ConsequentEncroachmentPrice, future, fvg.ConsequentEncroachmentPrice, DownBrush, DashStyleHelper.Dash, 1);
					fvgList.Add(fvg);
				}
			}
		}




        private void CheckFilledFVGs()
        {
			//Debug("\tCheckFilledFVGs().");

            List<FVG> filled = new List<FVG>();

            foreach (FVG fvg in fvgList)
			{
                if (fvg.IsFilled) continue;

//                if (DrawObjects[fvg.Tag] != null && DrawObjects[fvg.Tag] is DrawingTools.Rectangle)
//                {
//                    //Update EndAnchor of Gap to Expand into future.
//                    Rectangle gapRect = (Rectangle)DrawObjects[fvg.Tag];
//                    gapRect.EndAnchor.Time = Times[iDataSeriesIdx][0].AddDays(ChartBars.Properties.DaysBack);

//                    if (DisplayCE && DrawObjects[fvg.Tag + consequentEncroachmentTag] != null && DrawObjects[fvg.Tag + consequentEncroachmentTag] is DrawingTools.Line)
//                    {
//                        DrawingTools.Line gapLine = (DrawingTools.Line)DrawObjects[fvg.Tag + consequentEncroachmentTag];
//                        gapLine.EndAnchor.Time = Times[iDataSeriesIdx][0].AddDays(ChartBars.Properties.DaysBack);
//                    }

//                }
				
				if (DrawObjects[fvg.Tag] != null)
				{
					// Gap has ended test.
					if(Times[iDataSeriesIdx][0] > fvg.EndDateTime)
					{
						fvg.IsExpired		= true;
						fvg.FilledDateTime	= fvg.EndDateTime;
						filled.Add(fvg);
					}
					// Gap has been filled test.
					else if(   (fvg.Type == FVGType.R && (FillType == FVGFillType.CLOSE_THROUGH ? (Closes[iDataSeriesIdx][0] >= fvg.UpperPrice) : (Highs[iDataSeriesIdx][0] >= fvg.UpperPrice) ))
							|| (fvg.Type == FVGType.S && (FillType == FVGFillType.CLOSE_THROUGH ? (Closes[iDataSeriesIdx][0] <= fvg.LowerPrice) : (Lows[iDataSeriesIdx][0] <= fvg.LowerPrice) )) )
						{
							fvg.IsFilled		= true;
							fvg.FilledDateTime	= Times[iDataSeriesIdx][0];
							filled.Add(fvg);
						}
//					else if (fvg.Type == FVGType.S && (FillType == FVGFillType.CLOSE_THROUGH ? (Closes[iDataSeriesIdx][0] <= fvg.LowerPrice) : (Lows[iDataSeriesIdx][0] <= fvg.LowerPrice)))
//					{
//						fvg.IsFilled = true;
//						fvg.FilledDateTime = Times[iDataSeriesIdx][0];
//						filled.Add(fvg);
//					}
				}
			}

            foreach (FVG fvg in filled)
            {
				if(fvg.IsFilled)
				{
					if (DrawObjects[fvg.Tag] != null)
					{
						var drawObject = DrawObjects[fvg.Tag];
						Rectangle rect = (Rectangle)drawObject;

						RemoveDrawObject(fvg.Tag);
						RemoveDrawObject(fvg.Tag + consequentEncroachmentTag);

						if (!HideFilledGaps)
						{
							Brush FilledBrush	= fvg.Type == FVGType.R ? DownGapFilledBrush : UpGapFilledBrush;

							rect = Draw.Rectangle(this, "FILLED" + fvg.Tag, false, fvg.StartDateTime, fvg.LowerPrice, fvg.FilledDateTime, fvg.UpperPrice, FilledBrush, FilledBrush, FilledAreaOpacity, true);
							//rect.OutlineStroke.Opacity = Math.Min(100, FilledAreaOpacity * 4);
						}
					}
					if (HideFilledGaps)
					{
						fvgList.Remove(fvg);
					}
				}
            }
        }


		
		public override string DisplayName
		{	get 
			{
				if(isDataLoaded)	return UseFVGDataSeries ? sDataSeries : "FVG";
				else				return indiName; 
		}	}		

        private void Debug(String str)
        {
            if (IsDebug) Print(this.Name + " :: " + str);
        }

		
		#region Properties

		#region FVG Data Series
		[NinjaScriptProperty]
		[Display(Name = "Use FVG Data Series", Description = "If enabled, a secondary data series will be used to calculate FVGs.", 		Order = 90, GroupName = "FVG Data Series")]
		public bool UseFVGDataSeries
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "FVG Data Series Type", 																							Order = 100, GroupName = "FVG Data Series")]
		public FVGPeriodTypes FVGBarsPeriodType
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "FVG Data Series Period", 																							Order = 200, GroupName = "FVG Data Series")]
		public int FVGSeriesPeriod
		{ get; set; }
		#endregion

		//----------------------------------------------------------------------------------
		#region Parameters
		[NinjaScriptProperty]
		[Range(0, 7300)] // Max 20yrs for Dayly charts.
		[Display(Name = "Max Days to extend forward", Description = "How manys days will FVGs be drawn into the future.", 								Order = 100, GroupName = "Parameters")]
		public int MaxDaysForward
		{ get; set; }

//		[NinjaScriptProperty]
//		[Range(3, int.MaxValue)]
//		[Display(Name = "Max Lookback Bars", Order = 100, GroupName = "Parameters")]
//		public int MaxLookbackBars
//		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require Impulse Move", Description = "If enabled, ATR settings will be used to filter FVGs.", 									Order = 190, GroupName = "Parameters")]
		public bool UseATR
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "Min. ATR Impulse Move", 																										Order = 200, GroupName = "Parameters")]
		public double ImpulseFactor
		{ get; set; }

		[NinjaScriptProperty]
		[Range(3, int.MaxValue)]
		[Display(Name = "ATR Period", 																													Order = 300, GroupName = "Parameters")]
		public int ATRPeriod
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0.000000001, double.MaxValue)]
		[Display(Name = "Min. FV Gap Size (Points)", 																									Order = 310, GroupName = "Parameters")]
		public double MinimumFVGSize
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require FVG bars in same direction.", Description = "Requires all three FVG bars to be up for upward gaps, and vice versa.", 	Order = 190, GroupName = "Parameters")]
		public bool AllBarsSameDirection
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "FVG Fill Condition", 																											Order = 325, GroupName = "Parameters")]
		public FVGFillType FillType
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Hide Filled FVG", 																												Order = 350, GroupName = "Parameters")]
		public bool HideFilledGaps
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Consequent Encroachment", 																								Order = 400, GroupName = "Parameters")]
		public bool DisplayCE
		{ get; set; }
		#endregion

		//----------------------------------------------------------------------------------
		#region Time Range
		
		[NinjaScriptProperty]
		[Display(Name="Restrict to Time Range #1", Description="Detect FVGs only during the specific time range?", 			Order = 0, GroupName = "Time Ranges")]
		public bool UseTimeRange1
		{ get; set; }

		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Start Time in EST (UTC-5:00)", Description="Enter Start time in Eastern Standard Time (UTC -5:00).",	Order = 2, GroupName ="Time Ranges")]
		public TimeSpan StartTime1
		{ get; set; }
		
		[Browsable(false)]
		public string startTime1
		{	get {return StartTime1.ToString();			}
			set {StartTime1 = TimeSpan.Parse(value);	}	}
		
//		int timeRange = 60;
        [RefreshProperties(RefreshProperties.All)] // Needed to refresh the property grid when the value changes
		[Range(1, 1439), NinjaScriptProperty]
		[Display(Name = "Time Range in minutes", Description = "Numbers of minutes for the time range.",					Order = 4, GroupName = "Time Ranges")]
		public int TimeRangeMinutes1
		{ get; set; }
//		{ 	get { return timeRange; 	}	set { timeRange = value;	} 		}
		
		
		[NinjaScriptProperty]
		[Display(Name="Restrict to Time Range #2", Description="Detect FVGs only during the specific time range?", 			Order = 10, GroupName = "Time Ranges")]
		public bool UseTimeRange2
		{ get; set; }

		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Start Time in EST (UTC-5:00)", Description="Enter Start time in Eastern Standard Time (UTC -5:00).",	Order = 12, GroupName ="Time Ranges")]
		public TimeSpan StartTime2
		{ get; set; }
		
		[Browsable(false)]
		public string startTime2
		{	get {return StartTime2.ToString();			}
			set {StartTime2 = TimeSpan.Parse(value);	}	}
		
//		int timeRange = 60;
        [RefreshProperties(RefreshProperties.All)] // Needed to refresh the property grid when the value changes
		[Range(1, 1439), NinjaScriptProperty]
		[Display(Name = "Time Range in minutes", Description = "Numbers of minutes for the time range.",					Order = 14, GroupName = "Time Ranges")]
		public int TimeRangeMinutes2
		{ get; set; }
//		{ 	get { return timeRange; 	}	set { timeRange = value;	} 		}
		
		
		[NinjaScriptProperty]
		[Display(Name="Restrict to Time Range #3", Description="Detect FVGs only during the specific time range?", 			Order = 20, GroupName = "Time Ranges")]
		public bool UseTimeRange3
		{ get; set; }

		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Start Time in EST (UTC-5:00)", Description="Enter Start time in Eastern Standard Time (UTC -5:00).",	Order = 22, GroupName ="Time Ranges")]
		public TimeSpan StartTime3
		{ get; set; }
		
		[Browsable(false)]
		public string startTime3
		{	get {return StartTime3.ToString();			}
			set {StartTime3 = TimeSpan.Parse(value);	}	}
		
//		int timeRange = 60;
        [RefreshProperties(RefreshProperties.All)] // Needed to refresh the property grid when the value changes
		[Range(1, 1439), NinjaScriptProperty]
		[Display(Name = "Time Range in minutes", Description = "Numbers of minutes for the time range.",					Order = 24, GroupName = "Time Ranges")]
		public int TimeRangeMinutes3
		{ get; set; }
//		{ 	get { return timeRange; 	}	set { timeRange = value;	} 		}
		#endregion

		//----------------------------------------------------------------------------------
		#region FVG Colors
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bullish FVG Border Color", Order = 0, GroupName = "FVG Colors")]
		public Brush UpBrush
		{ get; set; }

		[Browsable(false)]
		public string UpBrushSerializable
		{
		    get { return Serialize.BrushToString(UpBrush); }
		    set { UpBrush = Serialize.StringToBrush(value); }
		}

		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bullish FVG Area Color", Order = 2, GroupName = "FVG Colors")]
		public Brush UpAreaBrush
		{ get; set; }

		[Browsable(false)]
		public string UpAreaBrushSerializable
		{
		    get { return Serialize.BrushToString(UpAreaBrush); }
		    set { UpAreaBrush = Serialize.StringToBrush(value); }
		}

		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bullish FVG Filled Color", Order = 4, GroupName = "FVG Colors")]
		public Brush UpGapFilledBrush
		{ get; set; }

		[Browsable(false)]
		public string UpGapFilledBrushSerializable
		{
		    get { return Serialize.BrushToString(UpGapFilledBrush); }
		    set { UpGapFilledBrush = Serialize.StringToBrush(value); }
		}


		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bearish FVG Border Color", Order = 6, GroupName = "FVG Colors")]
		public Brush DownBrush
		{ get; set; }

		[Browsable(false)]
		public string DownBrushSerializable
		{
		    get { return Serialize.BrushToString(DownBrush); }
		    set { DownBrush = Serialize.StringToBrush(value); }
		}


		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bearish FVG Area Color", Order = 8, GroupName = "FVG Colors")]
		public Brush DownAreaBrush
		{ get; set; }

		[Browsable(false)]
		public string DownBrushAreaSerializable
		{
		    get { return Serialize.BrushToString(DownAreaBrush); }
		    set { DownAreaBrush = Serialize.StringToBrush(value); }
		}

		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bearish FVG Filled Color", Order = 10, GroupName = "FVG Colors")]
		public Brush DownGapFilledBrush
		{ get; set; }

		[Browsable(false)]
		public string DownGapFilledBrushSerializable
		{
		    get { return Serialize.BrushToString(DownGapFilledBrush); }
		    set { DownGapFilledBrush = Serialize.StringToBrush(value); }
		}

		
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Active Gap Opacity", Order = 20, GroupName = "FVG Colors")]
		public int ActiveAreaOpacity
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Filled Gap Opacity", Order = 22, GroupName = "FVG Colors")]
		public int FilledAreaOpacity
		{ get; set; }
		#endregion

		//----------------------------------------------------------------------------------
		#region FVG Data Series Label
		[NinjaScriptProperty]
		[Display(Name = "Display Label", Description = "Display the FVG Data Series Label", 			Order = 100, GroupName = "FVG Data Series Label")]
		public bool DrawLabel
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Label Position", Description = "FVG Data Series Label Position on Chart", 		Order = 200, GroupName = "FVG Data Series Label")]
		public TextPosition LabelPosition
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Label Font", Description = "FVG Data Series Label Font", 						Order = 300, GroupName = "FVG Data Series Label")]
		public SimpleFont LabelFont
		{ get; set; }

		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Label Text Color", Description = "FVG Data Series Label Text Color", 			Order = 400, GroupName = "FVG Data Series Label")]
		public Brush LabelTextBrush
		{ get; set; }

		[Browsable(false)]
		public string LabelTextBrushSerializable
		{
		    get { return Serialize.BrushToString(LabelTextBrush); }
		    set { LabelTextBrush = Serialize.StringToBrush(value); }
		}

		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Label Border Color", Description = "FVG Data Series Label Border Color", 		Order = 500, GroupName = "FVG Data Series Label")]
		public Brush LabelBorderBrush
		{ get; set; }

		[Browsable(false)]
		public string LabelBorderBrushSerializable
		{
		    get { return Serialize.BrushToString(LabelBorderBrush); }
		    set { LabelBorderBrush = Serialize.StringToBrush(value); }
		}

		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Label Fill Color", Description = "FVG Data Series Label Fill Color", 			Order = 600, GroupName = "FVG Data Series Label")]
		public Brush LabelFillBrush
		{ get; set; }

		[Browsable(false)]
		public string LabelFillBrushSerializable
		{
		    get { return Serialize.BrushToString(LabelFillBrush); }
		    set { LabelFillBrush = Serialize.StringToBrush(value); }
		}

		
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Label Fill Opacity", Description = "FVG Data Series Label Fill Opacity", 		Order = 700, GroupName = "FVG Data Series Label")]
		public int LabelFillOpacity
		{ get; set; }
		#endregion
		
		
        #region FVGList
        [Browsable(false)]
        [XmlIgnore()]
        public List<FVG> FVGList
        {
            get { return new List<FVG>(fvgList); }
        }
        #endregion

		#endregion
		
		
		// Support or Resistance FVG
		public enum FVGType
		{    S, R	}

		#region Class FVG
		public class FVG
		{
		    public string	Tag;
		    public FVGType	Type;
		    public double	UpperPrice;
		    public double	ConsequentEncroachmentPrice;
		    public double	LowerPrice;
		    public DateTime StartDateTime;
		    public DateTime EndDateTime;
		    public bool 	IsFilled;
		    public DateTime FilledDateTime;
		    public bool 	IsExpired;

		    public FVG(string tag, FVGType type, double lowerPrice, double uppperPrice, DateTime startDateTime, DateTime endDateTime)
		    {
		        this.Tag 			= tag;
		        this.Type			= type;
		        this.LowerPrice 	= lowerPrice;
		        this.UpperPrice 	= uppperPrice;
		        this.ConsequentEncroachmentPrice = (this.LowerPrice + this.UpperPrice) / 2.0;
		        this.StartDateTime	= startDateTime;
		        this.EndDateTime	= endDateTime;
		        this.IsFilled		= false;
		        this.FilledDateTime	= DateTime.MaxValue;// endDateTime;
		        this.IsExpired		= false;
		    }
		}
		#endregion

    }
	
	
	#region Enums
	// FVG fill type
	public enum FVGFillType
	{
		CLOSE_THROUGH,
		PIERCE_THROUGH
	}

	// Supported period types for FVG detection
	public enum FVGPeriodTypes
	{
		Tick = BarsPeriodType.Tick,
		Volume = BarsPeriodType.Volume,
		Second = BarsPeriodType.Second,
		Minute = BarsPeriodType.Minute,
		Day = BarsPeriodType.Day,
		Week = BarsPeriodType.Week,
		Month = BarsPeriodType.Month,
		Year = BarsPeriodType.Year
	}
	#endregion
	
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private FVGICT[] cacheFVGICT;
		public FVGICT FVGICT(bool useFVGDataSeries, FVGPeriodTypes fVGBarsPeriodType, int fVGSeriesPeriod, int maxDaysForward, bool useATR, double impulseFactor, int aTRPeriod, double minimumFVGSize, bool allBarsSameDirection, FVGFillType fillType, bool hideFilledGaps, bool displayCE, bool useTimeRange1, TimeSpan startTime1, int timeRangeMinutes1, bool useTimeRange2, TimeSpan startTime2, int timeRangeMinutes2, bool useTimeRange3, TimeSpan startTime3, int timeRangeMinutes3, Brush upBrush, Brush upAreaBrush, Brush upGapFilledBrush, Brush downBrush, Brush downAreaBrush, Brush downGapFilledBrush, int activeAreaOpacity, int filledAreaOpacity, bool drawLabel, TextPosition labelPosition, SimpleFont labelFont, Brush labelTextBrush, Brush labelBorderBrush, Brush labelFillBrush, int labelFillOpacity)
		{
			return FVGICT(Input, useFVGDataSeries, fVGBarsPeriodType, fVGSeriesPeriod, maxDaysForward, useATR, impulseFactor, aTRPeriod, minimumFVGSize, allBarsSameDirection, fillType, hideFilledGaps, displayCE, useTimeRange1, startTime1, timeRangeMinutes1, useTimeRange2, startTime2, timeRangeMinutes2, useTimeRange3, startTime3, timeRangeMinutes3, upBrush, upAreaBrush, upGapFilledBrush, downBrush, downAreaBrush, downGapFilledBrush, activeAreaOpacity, filledAreaOpacity, drawLabel, labelPosition, labelFont, labelTextBrush, labelBorderBrush, labelFillBrush, labelFillOpacity);
		}

		public FVGICT FVGICT(ISeries<double> input, bool useFVGDataSeries, FVGPeriodTypes fVGBarsPeriodType, int fVGSeriesPeriod, int maxDaysForward, bool useATR, double impulseFactor, int aTRPeriod, double minimumFVGSize, bool allBarsSameDirection, FVGFillType fillType, bool hideFilledGaps, bool displayCE, bool useTimeRange1, TimeSpan startTime1, int timeRangeMinutes1, bool useTimeRange2, TimeSpan startTime2, int timeRangeMinutes2, bool useTimeRange3, TimeSpan startTime3, int timeRangeMinutes3, Brush upBrush, Brush upAreaBrush, Brush upGapFilledBrush, Brush downBrush, Brush downAreaBrush, Brush downGapFilledBrush, int activeAreaOpacity, int filledAreaOpacity, bool drawLabel, TextPosition labelPosition, SimpleFont labelFont, Brush labelTextBrush, Brush labelBorderBrush, Brush labelFillBrush, int labelFillOpacity)
		{
			if (cacheFVGICT != null)
				for (int idx = 0; idx < cacheFVGICT.Length; idx++)
					if (cacheFVGICT[idx] != null && cacheFVGICT[idx].UseFVGDataSeries == useFVGDataSeries && cacheFVGICT[idx].FVGBarsPeriodType == fVGBarsPeriodType && cacheFVGICT[idx].FVGSeriesPeriod == fVGSeriesPeriod && cacheFVGICT[idx].MaxDaysForward == maxDaysForward && cacheFVGICT[idx].UseATR == useATR && cacheFVGICT[idx].ImpulseFactor == impulseFactor && cacheFVGICT[idx].ATRPeriod == aTRPeriod && cacheFVGICT[idx].MinimumFVGSize == minimumFVGSize && cacheFVGICT[idx].AllBarsSameDirection == allBarsSameDirection && cacheFVGICT[idx].FillType == fillType && cacheFVGICT[idx].HideFilledGaps == hideFilledGaps && cacheFVGICT[idx].DisplayCE == displayCE && cacheFVGICT[idx].UseTimeRange1 == useTimeRange1 && cacheFVGICT[idx].StartTime1 == startTime1 && cacheFVGICT[idx].TimeRangeMinutes1 == timeRangeMinutes1 && cacheFVGICT[idx].UseTimeRange2 == useTimeRange2 && cacheFVGICT[idx].StartTime2 == startTime2 && cacheFVGICT[idx].TimeRangeMinutes2 == timeRangeMinutes2 && cacheFVGICT[idx].UseTimeRange3 == useTimeRange3 && cacheFVGICT[idx].StartTime3 == startTime3 && cacheFVGICT[idx].TimeRangeMinutes3 == timeRangeMinutes3 && cacheFVGICT[idx].UpBrush == upBrush && cacheFVGICT[idx].UpAreaBrush == upAreaBrush && cacheFVGICT[idx].UpGapFilledBrush == upGapFilledBrush && cacheFVGICT[idx].DownBrush == downBrush && cacheFVGICT[idx].DownAreaBrush == downAreaBrush && cacheFVGICT[idx].DownGapFilledBrush == downGapFilledBrush && cacheFVGICT[idx].ActiveAreaOpacity == activeAreaOpacity && cacheFVGICT[idx].FilledAreaOpacity == filledAreaOpacity && cacheFVGICT[idx].DrawLabel == drawLabel && cacheFVGICT[idx].LabelPosition == labelPosition && cacheFVGICT[idx].LabelFont == labelFont && cacheFVGICT[idx].LabelTextBrush == labelTextBrush && cacheFVGICT[idx].LabelBorderBrush == labelBorderBrush && cacheFVGICT[idx].LabelFillBrush == labelFillBrush && cacheFVGICT[idx].LabelFillOpacity == labelFillOpacity && cacheFVGICT[idx].EqualsInput(input))
						return cacheFVGICT[idx];
			return CacheIndicator<FVGICT>(new FVGICT(){ UseFVGDataSeries = useFVGDataSeries, FVGBarsPeriodType = fVGBarsPeriodType, FVGSeriesPeriod = fVGSeriesPeriod, MaxDaysForward = maxDaysForward, UseATR = useATR, ImpulseFactor = impulseFactor, ATRPeriod = aTRPeriod, MinimumFVGSize = minimumFVGSize, AllBarsSameDirection = allBarsSameDirection, FillType = fillType, HideFilledGaps = hideFilledGaps, DisplayCE = displayCE, UseTimeRange1 = useTimeRange1, StartTime1 = startTime1, TimeRangeMinutes1 = timeRangeMinutes1, UseTimeRange2 = useTimeRange2, StartTime2 = startTime2, TimeRangeMinutes2 = timeRangeMinutes2, UseTimeRange3 = useTimeRange3, StartTime3 = startTime3, TimeRangeMinutes3 = timeRangeMinutes3, UpBrush = upBrush, UpAreaBrush = upAreaBrush, UpGapFilledBrush = upGapFilledBrush, DownBrush = downBrush, DownAreaBrush = downAreaBrush, DownGapFilledBrush = downGapFilledBrush, ActiveAreaOpacity = activeAreaOpacity, FilledAreaOpacity = filledAreaOpacity, DrawLabel = drawLabel, LabelPosition = labelPosition, LabelFont = labelFont, LabelTextBrush = labelTextBrush, LabelBorderBrush = labelBorderBrush, LabelFillBrush = labelFillBrush, LabelFillOpacity = labelFillOpacity }, input, ref cacheFVGICT);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.FVGICT FVGICT(bool useFVGDataSeries, FVGPeriodTypes fVGBarsPeriodType, int fVGSeriesPeriod, int maxDaysForward, bool useATR, double impulseFactor, int aTRPeriod, double minimumFVGSize, bool allBarsSameDirection, FVGFillType fillType, bool hideFilledGaps, bool displayCE, bool useTimeRange1, TimeSpan startTime1, int timeRangeMinutes1, bool useTimeRange2, TimeSpan startTime2, int timeRangeMinutes2, bool useTimeRange3, TimeSpan startTime3, int timeRangeMinutes3, Brush upBrush, Brush upAreaBrush, Brush upGapFilledBrush, Brush downBrush, Brush downAreaBrush, Brush downGapFilledBrush, int activeAreaOpacity, int filledAreaOpacity, bool drawLabel, TextPosition labelPosition, SimpleFont labelFont, Brush labelTextBrush, Brush labelBorderBrush, Brush labelFillBrush, int labelFillOpacity)
		{
			return indicator.FVGICT(Input, useFVGDataSeries, fVGBarsPeriodType, fVGSeriesPeriod, maxDaysForward, useATR, impulseFactor, aTRPeriod, minimumFVGSize, allBarsSameDirection, fillType, hideFilledGaps, displayCE, useTimeRange1, startTime1, timeRangeMinutes1, useTimeRange2, startTime2, timeRangeMinutes2, useTimeRange3, startTime3, timeRangeMinutes3, upBrush, upAreaBrush, upGapFilledBrush, downBrush, downAreaBrush, downGapFilledBrush, activeAreaOpacity, filledAreaOpacity, drawLabel, labelPosition, labelFont, labelTextBrush, labelBorderBrush, labelFillBrush, labelFillOpacity);
		}

		public Indicators.FVGICT FVGICT(ISeries<double> input , bool useFVGDataSeries, FVGPeriodTypes fVGBarsPeriodType, int fVGSeriesPeriod, int maxDaysForward, bool useATR, double impulseFactor, int aTRPeriod, double minimumFVGSize, bool allBarsSameDirection, FVGFillType fillType, bool hideFilledGaps, bool displayCE, bool useTimeRange1, TimeSpan startTime1, int timeRangeMinutes1, bool useTimeRange2, TimeSpan startTime2, int timeRangeMinutes2, bool useTimeRange3, TimeSpan startTime3, int timeRangeMinutes3, Brush upBrush, Brush upAreaBrush, Brush upGapFilledBrush, Brush downBrush, Brush downAreaBrush, Brush downGapFilledBrush, int activeAreaOpacity, int filledAreaOpacity, bool drawLabel, TextPosition labelPosition, SimpleFont labelFont, Brush labelTextBrush, Brush labelBorderBrush, Brush labelFillBrush, int labelFillOpacity)
		{
			return indicator.FVGICT(input, useFVGDataSeries, fVGBarsPeriodType, fVGSeriesPeriod, maxDaysForward, useATR, impulseFactor, aTRPeriod, minimumFVGSize, allBarsSameDirection, fillType, hideFilledGaps, displayCE, useTimeRange1, startTime1, timeRangeMinutes1, useTimeRange2, startTime2, timeRangeMinutes2, useTimeRange3, startTime3, timeRangeMinutes3, upBrush, upAreaBrush, upGapFilledBrush, downBrush, downAreaBrush, downGapFilledBrush, activeAreaOpacity, filledAreaOpacity, drawLabel, labelPosition, labelFont, labelTextBrush, labelBorderBrush, labelFillBrush, labelFillOpacity);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.FVGICT FVGICT(bool useFVGDataSeries, FVGPeriodTypes fVGBarsPeriodType, int fVGSeriesPeriod, int maxDaysForward, bool useATR, double impulseFactor, int aTRPeriod, double minimumFVGSize, bool allBarsSameDirection, FVGFillType fillType, bool hideFilledGaps, bool displayCE, bool useTimeRange1, TimeSpan startTime1, int timeRangeMinutes1, bool useTimeRange2, TimeSpan startTime2, int timeRangeMinutes2, bool useTimeRange3, TimeSpan startTime3, int timeRangeMinutes3, Brush upBrush, Brush upAreaBrush, Brush upGapFilledBrush, Brush downBrush, Brush downAreaBrush, Brush downGapFilledBrush, int activeAreaOpacity, int filledAreaOpacity, bool drawLabel, TextPosition labelPosition, SimpleFont labelFont, Brush labelTextBrush, Brush labelBorderBrush, Brush labelFillBrush, int labelFillOpacity)
		{
			return indicator.FVGICT(Input, useFVGDataSeries, fVGBarsPeriodType, fVGSeriesPeriod, maxDaysForward, useATR, impulseFactor, aTRPeriod, minimumFVGSize, allBarsSameDirection, fillType, hideFilledGaps, displayCE, useTimeRange1, startTime1, timeRangeMinutes1, useTimeRange2, startTime2, timeRangeMinutes2, useTimeRange3, startTime3, timeRangeMinutes3, upBrush, upAreaBrush, upGapFilledBrush, downBrush, downAreaBrush, downGapFilledBrush, activeAreaOpacity, filledAreaOpacity, drawLabel, labelPosition, labelFont, labelTextBrush, labelBorderBrush, labelFillBrush, labelFillOpacity);
		}

		public Indicators.FVGICT FVGICT(ISeries<double> input , bool useFVGDataSeries, FVGPeriodTypes fVGBarsPeriodType, int fVGSeriesPeriod, int maxDaysForward, bool useATR, double impulseFactor, int aTRPeriod, double minimumFVGSize, bool allBarsSameDirection, FVGFillType fillType, bool hideFilledGaps, bool displayCE, bool useTimeRange1, TimeSpan startTime1, int timeRangeMinutes1, bool useTimeRange2, TimeSpan startTime2, int timeRangeMinutes2, bool useTimeRange3, TimeSpan startTime3, int timeRangeMinutes3, Brush upBrush, Brush upAreaBrush, Brush upGapFilledBrush, Brush downBrush, Brush downAreaBrush, Brush downGapFilledBrush, int activeAreaOpacity, int filledAreaOpacity, bool drawLabel, TextPosition labelPosition, SimpleFont labelFont, Brush labelTextBrush, Brush labelBorderBrush, Brush labelFillBrush, int labelFillOpacity)
		{
			return indicator.FVGICT(input, useFVGDataSeries, fVGBarsPeriodType, fVGSeriesPeriod, maxDaysForward, useATR, impulseFactor, aTRPeriod, minimumFVGSize, allBarsSameDirection, fillType, hideFilledGaps, displayCE, useTimeRange1, startTime1, timeRangeMinutes1, useTimeRange2, startTime2, timeRangeMinutes2, useTimeRange3, startTime3, timeRangeMinutes3, upBrush, upAreaBrush, upGapFilledBrush, downBrush, downAreaBrush, downGapFilledBrush, activeAreaOpacity, filledAreaOpacity, drawLabel, labelPosition, labelFont, labelTextBrush, labelBorderBrush, labelFillBrush, labelFillOpacity);
		}
	}
}

#endregion
