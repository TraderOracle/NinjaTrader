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
// Strategy with refined risk management logic and optimizations
namespace NinjaTrader.NinjaScript.Strategies.TOmethod
{
	public class RenkoWilliamsScalpStratv1 : Strategy
	{
		private double cDaily, pDaily;
		private double breakevenPrice;
		private bool breakevenTriggered = false;
		private WilliamsR williamsR;
		private double williamsRValue;
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"Custom Renko scalp strategy with Williams %R filter.";
				Name = "RenkoWilliamsScalpStratv1";
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds = 30;
				StartBehavior = StartBehavior.WaitUntilFlat;
				TimeInForce = TimeInForce.Gtc;
				BarsRequiredToTrade = 20;
				
				// Risk management properties
				WilliamsRperiod = 14;
				WilliamsRHighThreshold = -25;
				WilliamsRLowThreshold = -75;
				EnableProfitTarget = true;
				ProfitTarget = 500;
				EnableStopLoss = false;
				StopLoss = 50;
				EnableBreakEven = true;
				breakEvenTrigger = 50;
				breakEvenPlus = 50;
				DailyProfit = 500;
			    DailyLoss = 500;
			    StartTime = DateTime.Parse("09:30", System.Globalization.CultureInfo.InvariantCulture);
			    EndTime = DateTime.Parse("16:00", System.Globalization.CultureInfo.InvariantCulture);
			}
			else if (State == State.Configure)
			{
				AddDataSeries(Data.BarsPeriodType.Tick, 2000);
			}
			else if (State == State.DataLoaded)
			{				
				williamsR = WilliamsR(Closes[0], WilliamsRperiod);
			}
		}
		
		protected override void OnBarUpdate()
		{
			if (BarsInProgress == 1 && CurrentBars[1] < WilliamsRperiod) 
				return;
			if (BarsInProgress == 0 && CurrentBars[0] < WilliamsRperiod)
				return;
			
			if (Bars.IsFirstBarOfSession) pDaily = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
			
			if (IsTimeConstraintEnabled && (Time[0].TimeOfDay < StartTime.TimeOfDay || Time[0].TimeOfDay > EndTime.TimeOfDay))
				return;
			
			if (IsProfitLossLimitEnabled)
			{
				cDaily = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
				if (cDaily - pDaily >= DailyProfit || cDaily - pDaily <= -DailyLoss) 
					return;
			}
			
			if (BarsInProgress == 1)
			{
				williamsRValue = WilliamsR(BarsArray[1], WilliamsRperiod)[0];
			}
			
			if (BarsInProgress == 0)
			{
				if (williamsRValue > WilliamsRHighThreshold && Close[0] > Close[1])
				{
					EnterLong("LE");
					ApplyRiskManagement("LE", true);
				}
				else if (williamsRValue < WilliamsRLowThreshold && Close[0] < Close[1])
				{
					EnterShort("SE");
					ApplyRiskManagement("SE", false);
				}
			}
		}
		
		private void ApplyRiskManagement(string orderName, bool isLong)
		{
			if (EnableStopLoss)
			{
				SetStopLoss(orderName, CalculationMode.Ticks, StopLoss, false);
			}
			
			if (EnableProfitTarget)
			{
				SetProfitTarget(orderName, CalculationMode.Ticks, ProfitTarget);
			}
			
			if (EnableTrailingStop)
			{
				SetTrailStop(orderName, CalculationMode.Ticks, StopLoss, true);
			}
			
			if (EnableBreakEven && Position.MarketPosition != MarketPosition.Flat && !breakevenTriggered)
			{
				double unrealizedPL = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
			
				// Trigger break-even if unrealized PL exceeds the trigger
			    if (unrealizedPL >= TickSize * breakEvenTrigger)
			    {
					breakevenPrice = isLong ? Position.AveragePrice + breakEvenPlus * TickSize : Position.AveragePrice - breakEvenPlus * TickSize;
			    	SetStopLoss(CalculationMode.Ticks, breakevenPrice);
			    	breakevenTriggered = true;
			    }
			}
		}
		
		#region Parameters
		[NinjaScriptProperty]
		[Display(Name = "Williams R Period", GroupName = "Parameters", Order = 1)]
		public int WilliamsRperiod { get; set; }
		
		[NinjaScriptProperty]
		[Range(-100, 1)]
		[Display(Name = "Williams R High Threshold", GroupName = "Parameters", Order = 2)]
		public int WilliamsRHighThreshold { get; set; }
		
		[NinjaScriptProperty]
		[Range(-100, 1)]
		[Display(Name = "Williams R Low Threshold", GroupName = "Parameters", Order = 3)]
		public int WilliamsRLowThreshold { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Profit Target", GroupName = "Risk Management", Order = 1)]
		public bool EnableProfitTarget { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Profit Target in Ticks", GroupName = "Risk Management", Order = 2)]
		public int ProfitTarget { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Stop Loss", GroupName = "Risk Management", Order = 3)]
		public bool EnableStopLoss { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Stop Loss Ticks", GroupName = "Risk Management", Order = 4)]
		public int StopLoss { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Break Even", GroupName = "Risk Management", Order = 5)]
		public bool EnableBreakEven { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Break Even Trigger", GroupName = "Risk Management", Order = 6)]
		public int breakEvenTrigger { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Break Even Plus", GroupName = "Risk Management", Order = 7)]
		public int breakEvenPlus { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Trailing Stop", GroupName = "Risk Management", Order = 8)]
		public bool EnableTrailingStop { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Time Constraints", GroupName = "Trade Management", Order = 1)]
		public bool IsTimeConstraintEnabled { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Profit/Loss Limits", Order = 2, GroupName = "Trade Management")]
		public bool IsProfitLossLimitEnabled { get; set; }
		
		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Daily Profit Limit", Order = 3, GroupName = "Trade Management")]
		public double DailyProfit { get; set; }
		
		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Daily Loss Limit", Order = 4, GroupName = "Trade Management")]
		public double DailyLoss { get; set; }
		
		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "Start Time", Order = 5, GroupName = "Trade Management")]
		public DateTime StartTime { get; set; }
		
		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "End Time", Order = 6, GroupName = "Trade Management")]
		public DateTime EndTime { get; set; }
		
		#endregion
	}
}
