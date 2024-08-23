using System;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.Data;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class ConfigurableHorizontalLine : Indicator
    {
        private String Support;
        private String Resist;

        private Brush colSupport;
        private Brush colResist;
        private Brush colMajorSupport;
        private Brush colMajorResist;

        [NinjaScriptProperty]
        [Display(Name = "Mancini Supports", Description = "5343,5233,6046, etc", Order = 1, GroupName = "Lines")]
        public String Support { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mancini Resistances", Description = "5343,5233,6046, etc", Order = 2, GroupName = "Lines")]
        public String Resist { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Support Color", Order = 3, GroupName = "Colors")]
        public Brush colSupport { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Resistance Color", Order = 4, GroupName = "Colors")]
        public Brush colResist { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Major Support Color", Order = 5, GroupName = "Colors")]
        public Brush colMajorSupport { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Major Resistance Color", Order = 6, GroupName = "Colors")]
        public Brush colMajorResist { get; set; }

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

                Support                                     = "";
                Resist                                      = "";
                colSupport                                  = Brushes.Green;
                colMajorSupport                             = Brushes.Lime;
                colResist                                   = Brushes.Red;
                colMajorResist                              = Brushes.Orange;
            }
        }

        protected override void OnBarUpdate()
        {
            int idx = 1;
            String[] sr = Support.Split(',');
            foreach (String s in sr)
            {
                if (s.Contains("-"))
                {
                    if (s.Contains("Major"))
                    {
                        s = s.Replace(" (Major)", "").Trim();
                        s = s.SubString(0, 4);
                        double dPrice = Convert.Double(s.Trim());
                        Draw.HorizontalLine(this, "ho" + idx, dPrice, colMajorSupport, DashStyleHelper.Solid, 1);
                    }
                    else
                    {
                        s = s.SubString(0, 4);
                        double dPrice = Convert.Double(s.Trim());
                        Draw.HorizontalLine(this, "ho" + idx, dPrice, colSupport, DashStyleHelper.Solid, 1);
                    }
                }
                else
                {
                    if (s.Contains("Major"))
                    {
                        s = s.Replace(" (Major)", "").Trim();
                        double dPrice = Convert.Double(s.Trim());
                        Draw.HorizontalLine(this, "ho" + idx, dPrice, colMajorSupport, DashStyleHelper.Solid, 1);
                    }
                    else
                    {
                        double dPrice = Convert.Double(s.Trim());
                        Draw.HorizontalLine(this, "ho" + idx, dPrice, colSupport, DashStyleHelper.Solid, 1);
                    }
                }
                idx++;
            }
        }
    }
}