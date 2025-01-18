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
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class PaperHands : Indicator
	{
        private Brush	barColorDown	= Brushes.Red;
        private Brush	barColorUp      = Brushes.Lime;
        private Brush	shadowColor     = Brushes.DimGray;  // changed 4-27-2018 (was black which did not work on black background)
        private Pen     shadowPen       = null;
        private int     shadowWidth     = 1;
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description							= @"PaperHands";
				Name								= "PaperHands";
				Calculate							= Calculate.OnEachTick;
				IsOverlay							= true;
				DisplayInDataBox					= true;
				DrawOnPricePanel					= true;
				DrawHorizontalGridLines				= true;
				DrawVerticalGridLines				= true;
				PaintPriceMarkers					= false;
				
				ScaleJustification					= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive			= false;
				BarsRequiredToPlot					= 1;
				AddPlot(Brushes.Gray, "HAOpen");
				AddPlot(Brushes.Gray, "HAHigh");
				AddPlot(Brushes.Gray, "HALow");
				AddPlot(Brushes.Gray, "HAClose");
			}
		}

		protected override void OnBarUpdate()
		{
			BarBrushes[0] = Brushes.Transparent;
			CandleOutlineBrushes[0] = Brushes.Transparent;

			if (CurrentBar == 0)
            {				
                HAOpen[0] 	=	Open[0];
                HAHigh[0] 	=	High[0];
                HALow[0]	=	Low[0];
                HAClose[0]	=	Close[0];
                return;
            }

            HAClose[0]	=	((Open[0] + High[0] + Low[0] + Close[0]) * 0.25); // Calculate the close
            HAOpen[0]	=	((HAOpen[1] + HAClose[1]) * 0.5); // Calculate the open
            HAHigh[0]	=	(Math.Max(High[0], HAOpen[0])); // Calculate the high
            HALow[0]	=	(Math.Min(Low[0], HAOpen[0])); // Calculate the low	
		}

		#region Properties
		
		[XmlIgnore]
		[Display(Name="BarColorDown", Description="Color of Down bars", Order=2, GroupName="Visual")]
		public Brush BarColorDown
		{ 
			get { return barColorDown;}
			set { barColorDown = value;}
		}

		[Browsable(false)]
		public string BarColorDownSerializable
		{
			get { return Serialize.BrushToString(barColorDown); }
			set { barColorDown = Serialize.StringToBrush(value); }
		}			

		[XmlIgnore]
		[Display(Name="BarColorUp", Description="Color of Up bars", Order=1, GroupName="Visual")]
		public Brush BarColorUp
		{ 
			get { return barColorUp;}
			set { barColorUp = value;}
		}

		[Browsable(false)]
		public string BarColorUpSerializable
		{
			get { return Serialize.BrushToString(barColorUp); }
			set { barColorUp = Serialize.StringToBrush(value); }
		}			

		[XmlIgnore]
		[Display(Name="ShadowColor", Description="Wick/tail color", Order=3, GroupName="Visual")]
		public Brush ShadowColor
		{ 
			get { return shadowColor;}
			set { shadowColor = value;}
		}

		[Browsable(false)]
		public string ShadowColorSerializable
		{
			get { return Serialize.BrushToString(shadowColor); }
			set { shadowColor = Serialize.StringToBrush(value); }
		}			

		[Range(1, int.MaxValue)]
		[Display(Name="ShadowWidth", Description="Shadow (tail/wick) width", Order=4, GroupName="Visual")]
		public int ShadowWidth
		{ 
			get { return shadowWidth;}
			set { shadowWidth = value;}
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> HAOpen
		{
			get { return Values[0]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> HAHigh
		{
			get { return Values[1]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> HALow
		{
			get { return Values[2]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> HAClose
		{
			get { return Values[3]; }
		}
		
		#endregion
	
		#region Miscellaneous

       	public override void OnCalculateMinMax()
        {
            base.OnCalculateMinMax();
			
            if (Bars == null || ChartControl == null)
                return;

            for (int idx = ChartBars.FromIndex; idx <= ChartBars.ToIndex; idx++)
            {
                double tmpHigh 	= 	HAHigh.GetValueAt(idx);
                double tmpLow 	= 	HALow.GetValueAt(idx);
				
                if (tmpHigh != 0 && tmpHigh > MaxValue)
                    MaxValue = tmpHigh;
                if (tmpLow != 0 && tmpLow < MinValue)
                    MinValue = tmpLow;										
            }
        }		
		
		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
			
            if (Bars == null || ChartControl == null)
                return;			

            int barPaintWidth = Math.Max(3, 1 + 2 * ((int)ChartControl.BarWidth - 1) + 2 * shadowWidth);

            for (int idx = ChartBars.FromIndex; idx <= ChartBars.ToIndex; idx++)
            {
                if (idx - Displacement < 0 || idx - Displacement >= BarsArray[0].Count || ( idx - Displacement < BarsRequiredToPlot)) 
                    continue;
		
                double valH = HAHigh.GetValueAt(idx);
                double valL = HALow.GetValueAt(idx);
                double valC = HAClose.GetValueAt(idx);
                double valO = HAOpen.GetValueAt(idx);
                int x  = chartControl.GetXByBarIndex(ChartBars, idx);  //was chartControl.BarsArray[0]
                int y1 = chartScale.GetYByValue(valO);
                int y2 = chartScale.GetYByValue(valH);
                int y3 = chartScale.GetYByValue(valL);
                int y4 = chartScale.GetYByValue(valC);

				SharpDX.Direct2D1.Brush	shadowColordx 	= shadowColor.ToDxBrush(RenderTarget);  // prepare for the color to use
                var xy2 = new Vector2(x, y2);
                var xy3 = new Vector2(x, y3);
                RenderTarget.DrawLine(xy2, xy3, shadowColordx, shadowWidth);	

                if (y4 == y1)
				    RenderTarget.DrawLine( new Vector2( x - barPaintWidth / 2, y1),  new Vector2( x + barPaintWidth / 2, y1), shadowColordx, shadowWidth);
                else
                {
                    if (y4 > y1)
					{
						SharpDX.Direct2D1.Brush	barColorDowndx 	= barColorDown.ToDxBrush(RenderTarget);  // prepare for the color to use						
                        RenderTarget.FillRectangle( new RectangleF(x - barPaintWidth / 2, y1, barPaintWidth, y4 - y1), barColorDowndx);
						barColorDowndx.Dispose();
					}
                    else
					{
						SharpDX.Direct2D1.Brush	barColorUpdx 	= barColorUp.ToDxBrush(RenderTarget);  // prepare for the color to use
                        RenderTarget.FillRectangle( new RectangleF(x - barPaintWidth / 2, y4, barPaintWidth, y1 - y4),barColorUpdx);
						barColorUpdx.Dispose();
					}
                     RenderTarget.DrawRectangle( new RectangleF( x - barPaintWidth / 2 + (float)shadowWidth / 2,
                       Math.Min(y4, y1), barPaintWidth - (float)shadowWidth, Math.Abs(y4 - y1)), shadowColordx, shadowWidth);
				}	
				shadowColordx.Dispose();	
            }
        }		
			
		#endregion
	}
}


