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
using System.Windows.Forms;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators {

    public class WaddahAttarExplosion : Indicator {

        protected int LastTime1 = 1;
        protected int LastTime2 = 1;
        protected int LastTime3 = 1;
        protected int LastTime4 = 1;
        protected int Status = 0, PrevStatus = -1;
        protected double bask, bbid;
        protected bool bIsGreen;

        private string sound = NinjaTrader.Core.Globals.InstallDir + @"\sounds\waddah.wav";

        protected override void OnStateChange() {
            if (State == State.SetDefaults) {
                Description = @"Waddah Attar Explosion from MT4 recreated for NinjaTrader8 by Jeremy Bankes";
                Name = "WaddahAttarExplosion";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                //Disable this property if your indicator requires custom values that cumulate with each new market data event. 
                //See Help Guide for additional information.
                IsSuspendedWhileInactive = true;
                Sensitive = 150;
                DeadZonePip = 30;
                ExplosionPower = 15;
                TrendPower = 15;
                AlertWindow = true;
                AlertCount = 500;
                AlertLong = true;
                AlertShort = true;
                AlertExitLong = true;
                AlertExitShort = true;
                AddPlot(new Stroke(Brushes.Green, 2), PlotStyle.Bar, "Histogram1");
                AddPlot(new Stroke(Brushes.Magenta, 2), PlotStyle.Bar, "Histogram2");
                AddPlot(Brushes.Blue, "Line1");
                AddPlot(new Stroke(Brushes.Blue, 2), PlotStyle.Dot, "Line2");
            }
            //Print(State);
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < 3) 
                return;

            double Trend1, Trend2, Explo1, Explo2, Dead;
            double pwrt, pwre;
            Trend1 = (MACD(20, 40, 9)[0] - MACD(20, 40, 9)[1]) * Sensitive;
            Trend2 = (MACD(20, 40, 9)[2] - MACD(20, 40, 9)[3]) * Sensitive;
            Explo1 = Bollinger(2, 20).Upper[0] - Bollinger(2, 20).Lower[0];
            Explo2 = Bollinger(2, 20).Upper[1] - Bollinger(2, 20).Lower[1];
            Dead = TickSize * DeadZonePip;

            SolidColorBrush br = Brushes.Green;
            if (Trend1 < 0)
                br = Brushes.Firebrick;

            if (!bIsGreen && Trend1 > 0 && Trend1 > Explo1 && Trend1 > Trend2 && br == Brushes.Green && PlotBrushes[0][1] != Brushes.Lime)
            {
                br = Brushes.Lime;
                PlotBrushes[0][0] = br;
                bIsGreen = true;
            }
            if (bIsGreen && Trend1 < 0 && Math.Abs(Trend1) > Explo1 && Math.Abs(Trend1) > Math.Abs(Trend2) && br == Brushes.Firebrick && PlotBrushes[1][1] != Brushes.Red)
            {
                br = Brushes.Red;
                PlotBrushes[1][0] = br;
                bIsGreen = false;
            }

            if (Trend1 >= 0) Values[0][0] = Trend1;
            if (Trend1 < 0) Values[1][0] = (-1 * Trend1);
            Values[2][0] = Explo1;

            if (Trend1 > 0 && Trend1 > Explo1 && Trend1 > Trend2 && LastTime1 < AlertCount && AlertLong == true)
            {
                pwrt = 100 * (Trend1 - Trend2) / Trend1;
                pwre = 100 * (Explo1 - Explo2) / Explo1;
                bask = GetCurrentAsk();
                string message = String.Format("{0}- {1}, - BUY ({2:F5}) Trend PWR {3:F0} - Exp PWR {4:F0}", LastTime1, Instrument.FullName, bask, pwrt, pwre);
                if (AlertWindow == true)
                    Alert("Alert", Priority.Medium, message, sound, 10, Brushes.Black, Brushes.BlanchedAlmond);
                Print(message);
                LastTime1++;
                Status = 1;
            }
            if (Trend1 < 0 && Math.Abs(Trend1) > Explo1 && Math.Abs(Trend1) > Math.Abs(Trend2) && LastTime2 < AlertCount && AlertShort == true)
            {
                pwrt = 100 * (Math.Abs(Trend1) - Math.Abs(Trend2)) / Math.Abs(Trend1);
                pwre = 100 * (Explo1 - Explo2) / Explo1;
                bbid = GetCurrentBid();
                string message = String.Format("{0}- {1}, - SELL ({2:F5}) Trend PWR {3:F0} - Exp PWR {4:F0}", LastTime2, Instrument.FullName, bbid, pwrt, pwre);
                if (AlertWindow == true)
                    Alert("Alert", Priority.Medium, message, sound, 10, Brushes.Black, Brushes.BlanchedAlmond);
                Print(message);
                LastTime2++;
                Status = 2;
            }
            PrevStatus = Status;
            if (Status != PrevStatus)
            {
                LastTime1 = 1;
                LastTime2 = 1;
                LastTime3 = 1;
                LastTime4 = 1;
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Sensitive", Description = "Sensitive Value", Order = 1, GroupName = "Parameters")]
        public int Sensitive { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "DeadZonePip", Description = "Dead Zone Pip Value", Order = 2, GroupName = "Parameters")]
        public int DeadZonePip { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ExplosionPower", Description = "Explosion Power Value", Order = 3, GroupName = "Parameters")]
        public int ExplosionPower { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "TrendPower", Description = "Trend Power Value", Order = 4, GroupName = "Parameters")]
        public int TrendPower { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "AlertWindow", Description = "Determines whether output is displayed in an alert window or the console", Order = 5, GroupName = "Parameters")]
        public bool AlertWindow { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "AlertCount", Description = "Limits the amount of alerts per status update", Order = 6, GroupName = "Parameters")]
        public int AlertCount { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "AlertLong", Description = "Determines wether you will get long alerts", Order = 7, GroupName = "Parameters")]
        public bool AlertLong { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "AlertShort", Description = "Determines wether you will get short alerts", Order = 8, GroupName = "Parameters")]
        public bool AlertShort { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "AlertExitLong", Description = "Determines wether you will get long exit alerts", Order = 9, GroupName = "Parameters")]
        public bool AlertExitLong { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "AlertExitShort", Description = "Determines wether you will get short exit alerts", Order = 10, GroupName = "Parameters")]
        public bool AlertExitShort { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Histogram1 {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Histogram2 {
            get { return Values[1]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Line1 {
            get { return Values[2]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Line2 {
            get { return Values[3]; }
        }
        #endregion

    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private WaddahAttarExplosion[] cacheWaddahAttarExplosion;
		public WaddahAttarExplosion WaddahAttarExplosion(int sensitive, int deadZonePip, int explosionPower, int trendPower, bool alertWindow, int alertCount, bool alertLong, bool alertShort, bool alertExitLong, bool alertExitShort)
		{
			return WaddahAttarExplosion(Input, sensitive, deadZonePip, explosionPower, trendPower, alertWindow, alertCount, alertLong, alertShort, alertExitLong, alertExitShort);
		}

		public WaddahAttarExplosion WaddahAttarExplosion(ISeries<double> input, int sensitive, int deadZonePip, int explosionPower, int trendPower, bool alertWindow, int alertCount, bool alertLong, bool alertShort, bool alertExitLong, bool alertExitShort)
		{
			if (cacheWaddahAttarExplosion != null)
				for (int idx = 0; idx < cacheWaddahAttarExplosion.Length; idx++)
					if (cacheWaddahAttarExplosion[idx] != null && cacheWaddahAttarExplosion[idx].Sensitive == sensitive && cacheWaddahAttarExplosion[idx].DeadZonePip == deadZonePip && cacheWaddahAttarExplosion[idx].ExplosionPower == explosionPower && cacheWaddahAttarExplosion[idx].TrendPower == trendPower && cacheWaddahAttarExplosion[idx].AlertWindow == alertWindow && cacheWaddahAttarExplosion[idx].AlertCount == alertCount && cacheWaddahAttarExplosion[idx].AlertLong == alertLong && cacheWaddahAttarExplosion[idx].AlertShort == alertShort && cacheWaddahAttarExplosion[idx].AlertExitLong == alertExitLong && cacheWaddahAttarExplosion[idx].AlertExitShort == alertExitShort && cacheWaddahAttarExplosion[idx].EqualsInput(input))
						return cacheWaddahAttarExplosion[idx];
			return CacheIndicator<WaddahAttarExplosion>(new WaddahAttarExplosion(){ Sensitive = sensitive, DeadZonePip = deadZonePip, ExplosionPower = explosionPower, TrendPower = trendPower, AlertWindow = alertWindow, AlertCount = alertCount, AlertLong = alertLong, AlertShort = alertShort, AlertExitLong = alertExitLong, AlertExitShort = alertExitShort }, input, ref cacheWaddahAttarExplosion);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.WaddahAttarExplosion WaddahAttarExplosion(int sensitive, int deadZonePip, int explosionPower, int trendPower, bool alertWindow, int alertCount, bool alertLong, bool alertShort, bool alertExitLong, bool alertExitShort)
		{
			return indicator.WaddahAttarExplosion(Input, sensitive, deadZonePip, explosionPower, trendPower, alertWindow, alertCount, alertLong, alertShort, alertExitLong, alertExitShort);
		}

		public Indicators.WaddahAttarExplosion WaddahAttarExplosion(ISeries<double> input , int sensitive, int deadZonePip, int explosionPower, int trendPower, bool alertWindow, int alertCount, bool alertLong, bool alertShort, bool alertExitLong, bool alertExitShort)
		{
			return indicator.WaddahAttarExplosion(input, sensitive, deadZonePip, explosionPower, trendPower, alertWindow, alertCount, alertLong, alertShort, alertExitLong, alertExitShort);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.WaddahAttarExplosion WaddahAttarExplosion(int sensitive, int deadZonePip, int explosionPower, int trendPower, bool alertWindow, int alertCount, bool alertLong, bool alertShort, bool alertExitLong, bool alertExitShort)
		{
			return indicator.WaddahAttarExplosion(Input, sensitive, deadZonePip, explosionPower, trendPower, alertWindow, alertCount, alertLong, alertShort, alertExitLong, alertExitShort);
		}

		public Indicators.WaddahAttarExplosion WaddahAttarExplosion(ISeries<double> input , int sensitive, int deadZonePip, int explosionPower, int trendPower, bool alertWindow, int alertCount, bool alertLong, bool alertShort, bool alertExitLong, bool alertExitShort)
		{
			return indicator.WaddahAttarExplosion(input, sensitive, deadZonePip, explosionPower, trendPower, alertWindow, alertCount, alertLong, alertShort, alertExitLong, alertExitShort);
		}
	}
}

#endregion
