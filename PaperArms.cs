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
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class PaperArms : Indicator
    {

        private bool colorRegion = true;
        private int regionOpacity = 20;
        private Brush regionUpColor = Brushes.Green;
        private Brush regionDownColor = Brushes.Red;
        private int StartIndex = 1;
        private int PriorIndex = 0;

        private Series<bool> upTrend = null;
        private bool previousTrend = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"PaperArms.";
                Name = "PaperArms";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                ArePlotsConfigurable = false;

                AddPlot(Brushes.Transparent, "MGD");
                AddPlot(Brushes.Transparent, "FVMA");

                upTrend = new Series<bool>(this);
            }
            else if (State == State.DataLoaded)
            {
            }
        }

        protected override void OnBarUpdate()
        {
			Values[0][0] = EMA(Close, 9)[0];
			Values[1][0] = EMA(Close, 21)[0];

            bool isUpTrend = Values[1][0] < Values[0][0];
            bool isDownTrend = Values[1][0] > Values[0][0];

            if (colorRegion && regionOpacity != 0)
            {
                if (IsFirstTickOfBar)
                    PriorIndex = StartIndex;
                int CountBars = CurrentBar - PriorIndex + 1 - Displacement;

                if (isUpTrend)
                {
                    if (StartIndex == CurrentBar)
                        RemoveDrawObject("Region" + CurrentBar);
                    if (CountBars <= CurrentBar)
                        Draw.Region(this, "Region" + PriorIndex, CountBars, -Displacement, Values[1], Values[0], null, regionUpColor, regionOpacity);
                }
                else if (isDownTrend)
                {
                    if (StartIndex == CurrentBar)
                        RemoveDrawObject("Region" + CurrentBar);
                    if (CountBars <= CurrentBar)
                        Draw.Region(this, "Region" + PriorIndex, CountBars, -Displacement, Values[1], Values[0], null, regionDownColor, regionOpacity);
                }
                StartIndex = CurrentBar;
            }

            if (isUpTrend)
                upTrend[0] = true;
            else if (isDownTrend)
                upTrend[0] = false;
            
            previousTrend = isUpTrend;
        }

        #region Properties

        [NinjaScriptProperty]
        [Display(Name = "Color Region", GroupName = "Options", Order = 4)]
        public bool ColorRegion
        {
            get { return colorRegion; }
            set { colorRegion = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Region Opacity", GroupName = "Options", Order = 7)]
        public int RegionOpacity
        {
            get { return regionOpacity; }
            set { regionOpacity = Math.Min(100, Math.Max(0, value)); }
        }

        [XmlIgnore()]
        [NinjaScriptProperty]
        [Display(Name = "RegionUpColor", GroupName = "Options", Order = 8)]
        public Brush RegionUpColor
        {
            get { return regionUpColor; }
            set { regionUpColor = value; }
        }

        [Browsable(false)]
        public string RegionUpColorSerialize
        {
            get { return Serialize.BrushToString(regionUpColor); }
            set { regionUpColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore()]
        [NinjaScriptProperty]
        [Display(Name = "RegionDownColor", GroupName = "Options", Order = 9)]
        public Brush RegionDownColor
        {
            get { return regionDownColor; }
            set { regionDownColor = value; }
        }

        [Browsable(false)]
        public string RegionDownColorSerialize
        {
            get { return Serialize.BrushToString(regionDownColor); }
            set { regionDownColor = Serialize.StringToBrush(value); }
        }

        [Browsable(false)]
        [XmlIgnore()]
        public Series<bool> UpTrend
        {
            get { return upTrend; }
        }

        [Browsable(false)]
        public Series<double> MGD
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> FVMA
        {
            get { return Values[1]; }
        }
        #endregion
    }
}
