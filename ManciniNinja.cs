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
using NinjaTrader.NinjaScript.SuperDomColumns;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.IO;
using System.Net;
using System.Xml.Linq;
using System.Runtime.Remoting.Contexts;
using System.Windows.Media.TextFormatting;
using System.Windows.Markup;
using Infragistics.Windows.DataPresenter;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class ConfigurableHorizontalLine : Indicator
    {
//        private String Support;
//        private String Resist;

//        private Brush colSupport;
//        private Brush colResist;
//        private Brush colMajorSupport;
//        private Brush colMajorResist;

        //[Browsable(false)]
        //public string LineColorSerializable
        //{
        //    get { return Serialize.BrushToString(LineColor); }
        //    set { LineColor = Serialize.StringToBrush(value); }
        //}

        //[NinjaScriptProperty]
        //[Display(Name = "Line Style", Description = "The style of the horizontal line.", Order = 3, GroupName = "Parameters")]
        //public DashStyleHelper LineStyle { get; set; }

        //[NinjaScriptProperty]
        //[Range(1, int.MaxValue)]
        //[Display(Name = "Line Width", Description = "The width of the horizontal line.", Order = 4, GroupName = "Parameters")]
        //public int LineWidth { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = @"Draws Adam Mancini lines";
                Name                                        = "Mancini Lines";
                Calculate                                   = Calculate.OnEachTick;
                IsOverlay                                   = true;

                Support                                     = "5200 (major), 5093, 5088 (major)";
                Resist                                      = "5330 (major), 5536, 5442";
                colSupport                                  = Brushes.Green;
                colMajorSupport                             = Brushes.Lime;
                colResist                                   = Brushes.Red;
                colMajorResist                              = Brushes.Orange;
            }
        }

		private void DrawResist()
		{
            //Print(Support);
            int idx = 1;
            String[] sr = Resist.Split(',');
            foreach (string s in sr)
            {
				string sa = s.Trim();
                //Print(sa);
				
                if (s.Contains("-"))
                {
					Print("Dash");
                    if (s.Contains("major"))
                    {
                        sa = s.Replace("(major)", "").Trim();
                        sa = sa.Substring(0, 4);
						Print("6666**" + sa + "!!!!");
                        double dPrice = Convert.ToDouble(sa);
                        Draw.HorizontalLine(this, "bitch" + idx, dPrice, colMajorResist, DashStyleHelper.Solid, 1);
                    }
                    else
                    {
						Print("7777**" + sa + "****");
						sa = sa.Substring(0, 4);
                        double dPrice = Convert.ToDouble(sa);
                        Draw.HorizontalLine(this, "bitch" + idx, dPrice, colResist, DashStyleHelper.Solid, 1);
                    }
                }
                else
                {
                    if (sa.Contains("major"))
                    {
                        sa = sa.Replace("(major)", "").Trim();
						Print("8888**" + sa + "****");
                        double dPrice = Convert.ToDouble(sa.Trim());
                        Draw.HorizontalLine(this, "bitch" + idx, dPrice, colMajorResist, DashStyleHelper.Solid, 1);
                    }
                    else
                    {
						Print("9999**" + sa + "****");
                        double dPrice = Convert.ToDouble(sa);
                        Draw.HorizontalLine(this, "bitch" + idx, dPrice, colResist, DashStyleHelper.Solid, 1);
                    }
                }
                idx++;
            }
		}
		
		private void DrawSupport()
		{
            //Print(Support);
            int idx = 1;
            String[] sr = Support.Split(',');
            foreach (string s in sr)
            {
				string sa = s.Trim();
                //Print(sa);
				
                if (s.Contains("-"))
                {
					Print("Dash");
                    if (s.Contains("major"))
                    {
                        sa = s.Replace("(major)", "").Trim();
                        sa = sa.Substring(0, 4);
						Print("2222**" + sa + "!!!!");
                        double dPrice = Convert.ToDouble(sa);
                        Draw.HorizontalLine(this, "ho" + idx, dPrice, colMajorSupport, DashStyleHelper.Solid, 1);
                    }
                    else
                    {
						sa = sa.Substring(0, 4);
						Print("3333**" + sa + "****");
                        double dPrice = Convert.ToDouble(sa);
                        Draw.HorizontalLine(this, "ho" + idx, dPrice, colSupport, DashStyleHelper.Solid, 1);
                    }
                }
                else
                {
                    if (sa.Contains("major"))
                    {
                        sa = sa.Replace("(major)", "").Trim();
						Print("4444**" + sa + "****");
                        double dPrice = Convert.ToDouble(sa.Trim());
                        Draw.HorizontalLine(this, "ho" + idx, dPrice, colMajorSupport, DashStyleHelper.Solid, 1);
                    }
                    else
                    {
						Print("5555**" + sa + "****");
                        double dPrice = Convert.ToDouble(sa);
                        Draw.HorizontalLine(this, "ho" + idx, dPrice, colSupport, DashStyleHelper.Solid, 1);
                    }
                }
                idx++;
            }
		}
		
        protected override void OnBarUpdate()
        {
            if (Bars.IsLastBarOfSession || IsFirstTickOfBar)
            {
                DrawSupport();
		        DrawResist();
            }
        }
		
		#region Properties
		
		[NinjaScriptProperty]
        [Display(Name = "Mancini Supports", Description = "5343,5233,6046, etc", Order = 1, GroupName = "Lines")]
        public string Support { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mancini Resistances", Description = "5343,5233,6046, etc", Order = 2, GroupName = "Lines")]
        public string Resist { get; set; }

		[XmlIgnore]
        [Display(Name = "Support Color", Order = 3, GroupName = "Colors")]
        public Brush colSupport { get; set; }

		[XmlIgnore]
        [Display(Name = "Resistance Color", Order = 4, GroupName = "Colors")]
        public Brush colResist { get; set; }

		[XmlIgnore]
        [Display(Name = "Major Support Color", Order = 5, GroupName = "Colors")]
        public Brush colMajorSupport { get; set; }

		[XmlIgnore]
        [Display(Name = "Major Resistance Color", Order = 6, GroupName = "Colors")]
        public Brush colMajorResist { get; set; }

		#endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ConfigurableHorizontalLine[] cacheConfigurableHorizontalLine;
		public ConfigurableHorizontalLine ConfigurableHorizontalLine(string support, string resist)
		{
			return ConfigurableHorizontalLine(Input, support, resist);
		}

		public ConfigurableHorizontalLine ConfigurableHorizontalLine(ISeries<double> input, string support, string resist)
		{
			if (cacheConfigurableHorizontalLine != null)
				for (int idx = 0; idx < cacheConfigurableHorizontalLine.Length; idx++)
					if (cacheConfigurableHorizontalLine[idx] != null && cacheConfigurableHorizontalLine[idx].Support == support && cacheConfigurableHorizontalLine[idx].Resist == resist && cacheConfigurableHorizontalLine[idx].EqualsInput(input))
						return cacheConfigurableHorizontalLine[idx];
			return CacheIndicator<ConfigurableHorizontalLine>(new ConfigurableHorizontalLine(){ Support = support, Resist = resist }, input, ref cacheConfigurableHorizontalLine);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ConfigurableHorizontalLine ConfigurableHorizontalLine(string support, string resist)
		{
			return indicator.ConfigurableHorizontalLine(Input, support, resist);
		}

		public Indicators.ConfigurableHorizontalLine ConfigurableHorizontalLine(ISeries<double> input , string support, string resist)
		{
			return indicator.ConfigurableHorizontalLine(input, support, resist);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ConfigurableHorizontalLine ConfigurableHorizontalLine(string support, string resist)
		{
			return indicator.ConfigurableHorizontalLine(Input, support, resist);
		}

		public Indicators.ConfigurableHorizontalLine ConfigurableHorizontalLine(ISeries<double> input , string support, string resist)
		{
			return indicator.ConfigurableHorizontalLine(input, support, resist);
		}
	}
}

#endregion