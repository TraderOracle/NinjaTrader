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
	public class TOWilliamsRenko : Strategy
	{
		private bool Long;
		private bool Short;
		private bool DailyPNL;
		private double AccumulatedPNL;
		
		private WilliamsR WilliamsR1;
		
		private WilliamsR WilliamsR2;
		
		
		
		private EMA EMA1;
		private HMA HMA1;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"This uses the WilliamsR for Long Entries when it crosses ABOVE the -20 line using 2k Tick Data, and Short Entries below -80. This uses the WilliamsR for EXITS Long Positions when it crosses Below the -20, and exits Short Positions when it crosses above the -80 line using NinzaRenko Data. Also has a Trailing Stop set to 70 ticks as a safety. Also has Max Profit/Loss Settings and TOD limiters.";
				Name										= "TO Williams Renko";
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
				UseLong					= false;
				UseShort					= false;
				Profit_Limit					= 5000;
				Loss_Limit					= -500;
				Start_Time						= DateTime.Parse("09:30", System.Globalization.CultureInfo.InvariantCulture);
				End_Time						= DateTime.Parse("15:45", System.Globalization.CultureInfo.InvariantCulture);
				Contract_Size					= 1;
				Profit_Target					= 80;
				TrailStopDistance					= 70;
				Long					= true;
				Short					= true;
				DailyPNL					= true;
				AccumulatedPNL					= 0;
				
				LongTermTrendEMA_Filter					= 100;
				ShortTermHMAFilter						= 20;
				
				TickChartValue				= 2000;
			}
			else if (State == State.Configure)
			{
				AddDataSeries(Data.BarsPeriodType.Tick, TickChartValue);
			}
			else if (State == State.DataLoaded)
			{				
				WilliamsR1				= WilliamsR(Closes[1], 14);
				WilliamsR2				= WilliamsR(Close, 14);
				SetTrailStop(@"Long", CalculationMode.Ticks, TrailStopDistance, false);
				SetTrailStop(@"Short", CalculationMode.Ticks, TrailStopDistance, false);
				SetProfitTarget(@"Long",CalculationMode.Ticks,Profit_Target,false);
				SetProfitTarget(@"Short",CalculationMode.Ticks,Profit_Target,false);
				EMA1				= EMA(Closes[1], Convert.ToInt32(LongTermTrendEMA_Filter));
				HMA1				= HMA(Closes[1], Convert.ToInt32(ShortTermHMAFilter));
				
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) 
				return;

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
			
			 // Set 3
			if (
				 // Basic Long PreReqs
				((Position.MarketPosition == MarketPosition.Flat)
				 && (Times[0][0].TimeOfDay > Start_Time.TimeOfDay)
				 && (Times[0][0].TimeOfDay < End_Time.TimeOfDay)
				 && (UseLong == true))
				&& (DailyPNL == true)
				&& (Close[0] > HMA1[0])
				&& (Open[0] > HMA1[0])
				&& (Close[0] > Open[0])
				
				&& (Close[0] > EMA1[0])
				&& (Open[0] > EMA1[0])
				
				 && (WilliamsR1[1] < -20)
				 && (WilliamsR1[0] > -20))
			{
				
				EnterLong(Convert.ToInt32(Contract_Size), @"Long");
			}
			
			 // Set 4
			if (
				 // Basic Short PreReqs
				((Position.MarketPosition == MarketPosition.Flat)
				 && (Times[0][0].TimeOfDay > Start_Time.TimeOfDay)
				 && (Times[0][0].TimeOfDay < End_Time.TimeOfDay)
				 && (UseShort == true))
				&& (DailyPNL == true)
				&& (Close[0] < HMA1[0])
				&& (Open[0] < HMA1[0])
				&& (Close[0] < Open[0])
				
				&& (Close[0] < EMA1[0])
				&& (Open[0] < EMA1[0])
				
				 && (WilliamsR1[1] > -80)
				 && (WilliamsR1[0] < -80))
			{
				
				EnterShort(Convert.ToInt32(Contract_Size), @"Short");
			}
			
			 // Set 5
			if ((Position.MarketPosition == MarketPosition.Long)
				 && (WilliamsR2[0] < -20))
			{
				ExitLong(Convert.ToInt32(Position.Quantity), @"LongExit", @"Long");
			}
			
			 // Set 6
			if ((Position.MarketPosition == MarketPosition.Short)
				 && (WilliamsR2[0] > -80))
			{
				ExitShort(Convert.ToInt32(Position.Quantity), @"ShortExit", @"Short");
			}
			
		}

		#region CampervanSeth
	
		private string author 								= "CampervanSeth";
		private string version 								= "V7 November 7 2024";	
		#endregion
		
			// In order to trim the indicator's label we need to override the ToString() method.
			public override string DisplayName
				{
		            get { return Name ;}
				}	
		
		#region Properties
		[NinjaScriptProperty]
		[Display(Name="UseLong", Description="Use Long Entries", Order=1, GroupName="Parameters")]
		public bool UseLong
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="UseShort", Description="Use Short Entries", Order=2, GroupName="Parameters")]
		public bool UseShort
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="Profit_Limit", Description="Stop Trading", Order=3, GroupName="Parameters")]
		public double Profit_Limit
		{ get; set; }

		[NinjaScriptProperty]
		[Range(-50000, double.MaxValue)]
		[Display(Name="Loss_Limit", Description="Stop Trading", Order=4, GroupName="Parameters")]
		public double Loss_Limit
		{ get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name="Start_Time", Description="Start Time", Order=5, GroupName="Parameters")]
		public DateTime Start_Time
		{ get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name="End_Time", Description="End Time", Order=6, GroupName="Parameters")]
		public DateTime End_Time
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="Contract_Size", Description="# of Contracts Traded", Order=7, GroupName="Parameters")]
		public int Contract_Size
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="TrailStopDistance", Description="StopLoss in Ticks", Order=8, GroupName="Parameters")]
		public int TrailStopDistance
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Profit target", Description="Target in Ticks", Order=8, GroupName="Parameters")]
		public int Profit_Target
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Long Trend EMA Filter", Description="Slower EMA for trend confirmation, based on the Tick Chart", Order=11, GroupName="Parameters")]
		public int LongTermTrendEMA_Filter
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Short Term HMA Filter", Description="Faster HMA for trend confirmation, based on the Tick Chart", Order=11, GroupName="Parameters")]
		public int ShortTermHMAFilter
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Tick Chart value", Description="Value of the Tick Chart to be used for the Williams R Entries", Order=11, GroupName="Parameters")]
		public int TickChartValue
		{ get; set; }
		
		#endregion

	}
}
