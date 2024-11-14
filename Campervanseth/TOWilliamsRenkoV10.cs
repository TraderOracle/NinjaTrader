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
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	public class TOWilliamsRenkoV10 : Strategy
	{
		
		private bool DailyPNL;
		private double AccumulatedPNL;
		
		// Loads the WilliamsR Indicator with the secondary Data series (tick)
		private WilliamsR WilliamsR1;
		
		// Loads the WilliamsR Indicator with the primary Data series (chart)
		private WilliamsR WilliamsR2;
		
		
		
		//private EMA EMA1;
		//private HMA HMA1;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Trader Oracle WilliamsR Renko Bot - On a Renko Chart (40/8) T.O. Uses 2k Tick Data for a WilliamsR Indicator for entries above -20 for Long and below -80 for Short, then exits when there is an opposite candle on the Renko Chart. He also uses a Trailing Stop. This Bot can be configured to use either Tick Data for WilliamsR Entries --OR-- Renko Data. User set Profit Targets, Stop Distance, and Time of Day filters are configurable.";
				Name										= "TO Williams Renko V10";
				Calculate									= Calculate.OnBarClose;
				EntriesPerDirection							= 1;
				EntryHandling								= EntryHandling.UniqueEntries;
				IsExitOnSessionCloseStrategy				= true;
				ExitOnSessionCloseSeconds					= 30;
				IsFillLimitOnTouch							= false;
				MaximumBarsLookBack							= MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution							= OrderFillResolution.High;
				OrderFillResolutionType  					 = BarsPeriodType.Second;
   				OrderFillResolutionValue   					= 1;
				Slippage									= 0;
				StartBehavior								= StartBehavior.WaitUntilFlat;
				TimeInForce									= TimeInForce.Gtc;
				TraceOrders									= false;
				RealtimeErrorHandling						= RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling							= StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade							= 20;
				// Disable this property for performance gains in Strategy Analyzer optimizations
				// See the Help Guide for additional information
				IsInstantiatedOnEachOptimizationIteration	= true;
				
				UseLong							= false;
				UseShort						= false;
				Profit_Limit					= 5000;
				Loss_Limit						= -500;
				Start_Time						= DateTime.Parse("09:30", System.Globalization.CultureInfo.InvariantCulture);
				End_Time						= DateTime.Parse("15:45", System.Globalization.CultureInfo.InvariantCulture);
				Contract_Size					= 1;
				Profit_Target					= 80;
				TrailStopDistance				= 70;
				DailyPNL						= true;
				AccumulatedPNL					= 0;
				
				//LongTermTrendEMA_Filter			= 100;
				//ShortTermHMAFilter				= 20;
				
				TickChartValue				= 2000;
				
				UseTick						= true;
				UseRenko					= false;
				
				WilliamsLong				= -20;
				WilliamsShort				= -80;
				WilliamsRPeriod			= 14;
				
			}
			else if (State == State.Configure)
			{
				AddDataSeries(Data.BarsPeriodType.Tick, TickChartValue);
			}
			else if (State == State.DataLoaded)
			{	
				//This lets the williams R use Close Data from athe additional data series (tick)
				WilliamsR1				= WilliamsR(Closes[1], WilliamsRPeriod);
				
				//This lets the williams R use Close Data from the primary data series (current chart - Renko)
				WilliamsR2				= WilliamsR(Close, WilliamsRPeriod);
				
				
				//Tick Data Entries
				SetTrailStop(@"LongTick", CalculationMode.Ticks, TrailStopDistance, false);
				SetTrailStop(@"ShortTick", CalculationMode.Ticks, TrailStopDistance, false);
				SetProfitTarget(@"LongTick",CalculationMode.Ticks,Profit_Target,false);
				SetProfitTarget(@"ShortTick",CalculationMode.Ticks,Profit_Target,false);
				
				//Renko Data Entries
				SetTrailStop(@"LongRenko", CalculationMode.Ticks, TrailStopDistance, false);
				SetTrailStop(@"ShortRenko", CalculationMode.Ticks, TrailStopDistance, false);
				SetProfitTarget(@"LongRenko",CalculationMode.Ticks,Profit_Target,false);
				SetProfitTarget(@"ShortRenko",CalculationMode.Ticks,Profit_Target,false);
				
				//EMA1 = EMA(Closes[1], Convert.ToInt32(LongTermTrendEMA_Filter));
				//HMA1 = HMA(Closes[1], Convert.ToInt32(ShortTermHMAFilter));
				
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) 
				return;

			#region Set 1 PNL Controls
			// Set 1
			if (
				  // Profit and Loss Check
				((SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - AccumulatedPNL > Profit_Limit)
				 || (SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - AccumulatedPNL < Loss_Limit)))
			{
				DailyPNL = false;
			}
			
			if (CurrentBars[0] < 1
			|| CurrentBars[1] < 1)
				return;

			 // Set 2
			if 
				 // Reset PNL Time
				(Times[0][0].TimeOfDay < Times[0][1].TimeOfDay)
			{
				DailyPNL = true;
				AccumulatedPNL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
			}
			#endregion
			 
			
			#region  Set 3 - Tick Long Entries
			if (
				  // Basic Long PreReqs
				 //Ensures we are Flat before placing orders
				(Position.MarketPosition == MarketPosition.Flat)
				
				//Ensures we are within the specified times.
				 && (Times[0][0].TimeOfDay > Start_Time.TimeOfDay)
				 && (Times[0][0].TimeOfDay < End_Time.TimeOfDay)
				
				// Ensures Long trades are enabled.
				&& (UseLong == true)
				
				// Ensures "use tick data" is enabled
				&& (UseTick == true)
				
				// Ensures we are within the PNL Limits
				&& (DailyPNL == true)
				
				//Ensures the bar opens and closes above the HMA
				//&& (Close[0] > HMA1[0])
				//&& (Open[0] > HMA1[0])
				
				//Ensures there is a green bar on the Renko
				&& (Close[0] > Open[0])
				
				//Ensures the bar opens and closes above the EMA
				//&& (Close[0] > EMA1[0])
				//&& (Open[0] > EMA1[0])
				
				// Ensures that the 2k Tick WilliamsR starts below and Crosses above the -20 Line (not just randomly above)
				 && (WilliamsR1[1] < WilliamsLong)
				 && (WilliamsR1[0] > WilliamsLong))
			{
				
				EnterLong(Convert.ToInt32(Contract_Size), @"LongTick");
			}
			#endregion
			
			
			#region Set 5 Tick Short Entries
			if (
				  // Basic Short PreReqs
				  //Ensures we are Flat before placing orders
				(Position.MarketPosition == MarketPosition.Flat)
				
				//Ensures we are within the specified times.
				 && (Times[0][0].TimeOfDay > Start_Time.TimeOfDay)
				 && (Times[0][0].TimeOfDay < End_Time.TimeOfDay)
				
				// Ensures Short trades are enabled.
				 && (UseShort == true)
				
				// Ensures "use tick data" is enabled
				&& (UseTick == true)
				
				// Ensures we are within the PNL Limits
				&& (DailyPNL == true)
				
				//Ensures the bar opens and closes below the HMA
				//&& (Close[0] < HMA1[0])
				//&& (Open[0] < HMA1[0])
				
				//Ensures there is a red bar on the Renko
				&& (Close[0] < Open[0])
				
				//Ensures the bar opens and closes below the EMA
				//&& (Close[0] < EMA1[0])
				//&& (Open[0] < EMA1[0])
				
				// Ensures that the 2k Tick WilliamsR starts Ablve and Crosses below the -80 Line (not just randomly below)
				&& (WilliamsR1[1] > WilliamsShort)
				&& (WilliamsR1[0] < WilliamsShort))
			{
				
				EnterShort(Convert.ToInt32(Contract_Size), @"ShortTick");
			}
			#endregion
			
			
			#region Tick Exit Conditions	
			if 
				//Ensures we are Long so we dont submit additional sell orders
				((Position.MarketPosition == MarketPosition.Long)
				 
				// Ensures "use tick data" is enabled
				&& (UseTick == true)
				
				// WilliamsR Exit Condition using Renko Data - crosses below the -20 line
				//&& (WilliamsR2[0] < WilliamsLong)
				
				// Red Renko bar for exit
				&& (Close[0] < Open[0])
				
				)
			{
				ExitLong(Convert.ToInt32(Position.Quantity), @"LongExit", @"LongTick");
			}
			
			 //Ensures we are Short so we dont submit additional sell orders
			if ((Position.MarketPosition == MarketPosition.Short)
				
				// Ensures "use tick data" is enabled
				&& (UseTick == true)
				
				// WilliamsR Exit Condition using Renko Data - crosses above the -80 line
				 //&& (WilliamsR2[0] > WilliamsShort)
				
				// Green Renko bar for exit
				&& (Close[0] > Open[0])
				)
			{
				ExitShort(Convert.ToInt32(Position.Quantity), @"ShortExit", @"ShortTick");
			}
			#endregion
			
			#region  Set 9 - Renko Long Entries
			if (
				  // Basic Long PreReqs
				 //Ensures we are Flat before placing orders
				(Position.MarketPosition == MarketPosition.Flat)
				
				//Ensures we are within the specified times.
				 && (Times[0][0].TimeOfDay > Start_Time.TimeOfDay)
				 && (Times[0][0].TimeOfDay < End_Time.TimeOfDay)
				
				// Ensures Long trades are enabled.
				&& (UseLong == true)
				
				// Ensures "use tick data" is enabled
				&& (UseRenko == true)
				
				// Ensures we are within the PNL Limits
				&& (DailyPNL == true)
				
				//Ensures the bar opens and closes above the HMA
				//&& (Close[0] > HMA1[0])
				//&& (Open[0] > HMA1[0])
				
				//Ensures there is a green bar on the Renko
				&& (Close[0] > Open[0])
				
				//Ensures the bar opens and closes above the EMA
				//&& (Close[0] > EMA1[0])
				//&& (Open[0] > EMA1[0])
				
				// Ensures that the 2k Tick WilliamsR starts below and Crosses above the -20 Line (not just randomly above)
				 && (WilliamsR2[1] < WilliamsLong)
				 && (WilliamsR2[0] > WilliamsLong))
			{
				
				EnterLong(Convert.ToInt32(Contract_Size), @"LongRenko");
			}
			#endregion
			
			#region Set 5 Renko Short Entries
			if (
				  // Basic Short PreReqs
				  //Ensures we are Flat before placing orders
				(Position.MarketPosition == MarketPosition.Flat)
				
				//Ensures we are within the specified times.
				 && (Times[0][0].TimeOfDay > Start_Time.TimeOfDay)
				 && (Times[0][0].TimeOfDay < End_Time.TimeOfDay)
				
				// Ensures Short trades are enabled.
				 && (UseShort == true)
				
				// Ensures "use tick data" is enabled
				&& (UseRenko == true)
				
				// Ensures we are within the PNL Limits
				&& (DailyPNL == true)
				
				//Ensures the bar opens and closes below the HMA
				//&& (Close[0] < HMA1[0])
				//&& (Open[0] < HMA1[0])
				
				//Ensures there is a red bar on the Renko
				&& (Close[0] < Open[0])
				
				//Ensures the bar opens and closes below the EMA
				//&& (Close[0] < EMA1[0])
				//&& (Open[0] < EMA1[0])
				
				// Ensures that the 2k Tick WilliamsR starts Ablve and Crosses below the -80 Line (not just randomly below)
				&& (WilliamsR2[1] > WilliamsShort)
				&& (WilliamsR2[0] < WilliamsShort))
			{
				
				EnterShort(Convert.ToInt32(Contract_Size), @"ShortRenko");
			}
			#endregion
			
			#region Renko Exit Conditions	
			if 
				//Ensures we are Long so we dont submit additional sell orders
				((Position.MarketPosition == MarketPosition.Long)
				 
				//  Ensures "use Renko data" is enabled
				&& (UseRenko == true)
				
				// WilliamsR Exit Condition using Renko Data - crosses below the -20 line
				//&& (WilliamsR2[0] < WilliamsLong)
				
				// Red Renko bar for exit
				&& (Close[0] < Open[0])
				
				)
			{
				ExitLong(Convert.ToInt32(Position.Quantity), @"LongExit", @"LongRenko");
			}
			
			 //Ensures we are Short so we dont submit additional sell orders
			if ((Position.MarketPosition == MarketPosition.Short)
				
				// Ensures "use Renko data" is enabled
				&& (UseRenko == true)
				
				// WilliamsR Exit Condition using Renko Data - crosses above the -80 line
				 //&& (WilliamsR2[0] > WilliamsShort)
				
				// Green Renko bar for exit
				&& (Close[0] > Open[0])
				)
			{
				ExitShort(Convert.ToInt32(Position.Quantity), @"ShortExit", @"ShortRenko");
			}
			#endregion
		}

	#region CampervanSeth
	
		private string author 								= "CampervanSeth";
		private string version 								= "V7 November 7 2024";	
	#endregion
		
	#region Display Name
		// In order to trim the indicator's label we need to override the ToString() method.
			public override string DisplayName
				{
		            get { return Name ;}
				}	
	#endregion
				
				
		#region Properties
				
		[NinjaScriptProperty]
		[Display(Name="Use Tick Entrys (Select only 1)", Description="Use Tick Entry Data", Order=1, GroupName="Entry Data")]
		public bool UseTick
		{ get; set; }		
		
		[NinjaScriptProperty]
		[Display(Name="Use Renko Entrys (Select only 1)", Description="Use Renko Entry Data", Order=2, GroupName="Entry Data")]
		public bool UseRenko
		{ get; set; }	
		
		[NinjaScriptProperty]
		[Range(-50000, double.MaxValue)]
		[Display(Name="WilliamsR Short Trigger (NEG)", Description="Line the WilliamsR crosees to go Short", Order=3, GroupName="Entry Data")]
		public double WilliamsShort
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(-50000, double.MaxValue)]
		[Display(Name="WilliamsR Long Trigger (NEG)", Description="Line the WilliamsR crosees to go Long", Order=4, GroupName="Entry Data")]
		public double WilliamsLong
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="WilliamsR Period", Description="Number of bars for the calculation", Order=5, GroupName="Entry Data")]
		public int WilliamsRPeriod
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Use Long", Description="Use Long Entries", Order=1, GroupName="Restrictions")]
		public bool UseLong
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="Use Short", Description="Use Short Entries", Order=2, GroupName="Restrictions")]
		public bool UseShort
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="Profit Limit", Description="Stop Trading", Order=3, GroupName="Risk Management")]
		public double Profit_Limit
		{ get; set; }

		[NinjaScriptProperty]
		[Range(-100000, double.MaxValue)]
		[Display(Name="Loss Limit", Description="Stop Trading", Order=4, GroupName="Risk Management")]
		public double Loss_Limit
		{ get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name="Start Time", Description="Start Time", Order=5, GroupName="Restrictions")]
		public DateTime Start_Time
		{ get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name="End Time", Description="End Time", Order=6, GroupName="Restrictions")]
		public DateTime End_Time
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="Contract Qty", Description="# of Contracts Traded", Order=7, GroupName="Risk Management")]
		public int Contract_Size
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Trail Stop Distance", Description="StopLoss in Ticks", Order=8, GroupName="Risk Management")]
		public int TrailStopDistance
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Profit Target", Description="Target in Ticks", Order=8, GroupName="Risk Management")]
		public int Profit_Target
		{ get; set; }
		
		//[NinjaScriptProperty]
		//[Range(1, int.MaxValue)]
		//[Display(Name="Long Trend EMA Filter", Description="Slower EMA for trend confirmation, based on the Tick Chart", Order=11, GroupName="Parameters")]
		//public int LongTermTrendEMA_Filter
		//{ get; set; }
		
		//[NinjaScriptProperty]
		//[Range(1, int.MaxValue)]
		//[Display(Name="Short Term HMA Filter", Description="Faster HMA for trend confirmation, based on the Tick Chart", Order=11, GroupName="Parameters")]
		//public int ShortTermHMAFilter
		//{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Tick Chart value", Description="Value of the Tick Chart to be used for the Williams R Entries", Order=11, GroupName="Entry Data")]
		public int TickChartValue
		{ get; set; }
		
		#endregion

	}
}
