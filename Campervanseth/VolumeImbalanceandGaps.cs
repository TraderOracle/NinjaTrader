#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

/// 01/27/2025
/// Converted to NT8 from the TradingView script "Volume Gaps and Imbalance" by OutofOptions
/// This source code is subject to the terms of the Mozilla Public License 2.0 at https://mozilla.org/MPL/2.0/
/// 
/// 01/29/2025
/// Added properties, defaults and revised cleanupgaps() to allow previously filled gaps to be plotted
/// Added option to not draw the text for filled gaps
/// Added audio alert option
/// 



namespace NinjaTrader.NinjaScript.Indicators.Dimension
{
	public class VolumeGapsAndImbalances : Indicator
	{
		private class Imbalance
		{
			public bool Bullish { get; set; }
			public int BarNumber { get; set; }
			public double Top { get; set; }
			public double Bottom { get; set; }
			public string BoxTag { get; set; }
			public bool IsGap { get; set; }
		}
		
		[NinjaScriptProperty]
		[Range(2, 100)]
		[Display(Name = "VIs to Show", Description = "Maximum number of VIs to show of each type", Order = 1, GroupName = "Parameters")]
		public int VisToShow { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Hide Filled Gaps", Description = "When enabled, filled gaps will be removed from the chart", Order = 2, GroupName = "Parameters")]
		public bool HideFilledGaps { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Hide Text on Filled", Description = "When enabled, text labels will be hidden for filled gaps", Order = 3, GroupName = "Parameters")]
		public bool HideTextOnFilled { get; set; }
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bullish VI Color", Description = "Color for bullish volume imbalances", Order = 4, GroupName = "Parameters")]
		public Brush BullishColor { get; set; }
		
		[Browsable(false)]
		public string BullishColorSerializable
		{
			get { return Serialize.BrushToString(BullishColor); }
			set { BullishColor = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Bearish VI Color", Description = "Color for bearish volume imbalances", Order = 5, GroupName = "Parameters")]
		public Brush BearishColor { get; set; }
		
		[Browsable(false)]
		public string BearishColorSerializable
		{
			get { return Serialize.BrushToString(BearishColor); }
			set { BearishColor = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Box Opacity", Description = "Opacity of the box fill", Order = 6, GroupName = "Parameters")]
		public int BoxOpacity { get; set; }
		
		[NinjaScriptProperty]
		[Range(8, 24)]
		[Display(Name = "Text Size", Description = "Size of the label text", Order = 7, GroupName = "Parameters")]
		public int TextSize { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Play Audio Alert on Gap", Description = "When enabled, an audio alert will be played when a gap or imbalance is created", Order = 8, GroupName = "Parameters")]
		public bool AudioAlert { get; set; }


        private List<Imbalance> bullishImbalances;
        private List<Imbalance> bearishImbalances;

		protected override void OnStateChange()
		{
				if (State == State.SetDefaults)
				{
				Description 								= @"Identifies and displays volume imbalances and gaps in price action";
				Name 										= "Volume Gaps and Imbalances";
				Calculate 									= Calculate.OnBarClose;
				IsOverlay 									= true;
				DisplayInDataBox 							= true;
				DrawOnPricePanel 							= true;
				DrawHorizontalGridLines 					= true;
				DrawVerticalGridLines 						= true;
				PaintPriceMarkers 							= true;
				ScaleJustification 							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive	 				= true;

				// Default settings
				VisToShow 									= 100;
				HideFilledGaps								= false;
				HideTextOnFilled							= true;
				BullishColor 								= Brushes.LimeGreen;
				BearishColor 								= Brushes.Red;
				BoxOpacity 									= 10;
				TextSize 									= 10;
				AudioAlert									= false;
		}
		else if (State == State.Configure)
		{
			bullishImbalances = new List<Imbalance>();
			bearishImbalances = new List<Imbalance>();
		}
		}


        protected override void OnBarUpdate()
        {
			if (CurrentBar < 1) return;

			double curHigh = Math.Max(Open[0], Close[0]);
			double curLow = Math.Min(Open[0], Close[0]);
			double prevHigh = Math.Max(Open[1], Close[1]);
			double prevLow = Math.Min(Open[1], Close[1]);

            CleanupImbalances();
			
			// Check for bearish imbalance
			if (prevLow > curHigh)
			{
				var imb = new Imbalance
				{
				    Bullish = false,
				    BarNumber = CurrentBar - 1,
				    Top = prevLow,
				    Bottom = curHigh,
				    IsGap = Low[1] - High[0] > 0,
				    BoxTag = $"VI_{CurrentBar - 1}_BEAR"
				};
				bearishImbalances.Add(imb);
			}
			// Check for bullish imbalance
			else if (prevHigh < curLow)
			{
				var imb = new Imbalance
				{
					Bullish = true,
					BarNumber = CurrentBar - 1,
					Top = prevHigh,
					Bottom = curLow,
					IsGap = Low[0] - High[1] > 0,
					BoxTag = $"VI_{CurrentBar - 1}_BULL"
				};
				bullishImbalances.Add(imb);
			}

            // Since we're using Calculate.OnBarClose, we use Count - 2
            if (CurrentBar == Count - 2)
            {
                RenderImbalances();
            }
		}

        private void CleanupImbalances()
        {
            if (HideFilledGaps)
            {
                // If HideFilledGaps is enabled, remove filled imbalances completely
                bearishImbalances.RemoveAll(imb =>
                {
                    bool shouldRemove = High[0] >= imb.Bottom;
                    if (shouldRemove)
                    {
                        RemoveDrawObject(imb.BoxTag);
                        RemoveDrawObject(imb.BoxTag + "_text");
                    }
                    return shouldRemove;
                });

                bullishImbalances.RemoveAll(imb =>
                {
                    bool shouldRemove = Low[0] <= imb.Bottom;
                    if (shouldRemove)
                    {
                        RemoveDrawObject(imb.BoxTag);
                        RemoveDrawObject(imb.BoxTag + "_text");
                    }
                    return shouldRemove;
                });
            }
            else
            {
                // Check bearish imbalances
                for (int i = bearishImbalances.Count - 1; i >= 0; i--)
                {
                    var imb = bearishImbalances[i];
                    if (High[0] >= imb.Bottom)
                    {
                        // Remove original rectangles
                        RemoveDrawObject(imb.BoxTag);
                        RemoveDrawObject(imb.BoxTag + "_text");

                        // Redraw rectangle ending at current time
                        Draw.Rectangle(this, imb.BoxTag, false, Time[CurrentBar - imb.BarNumber], imb.Top, Time[0], imb.Bottom, BearishColor, BearishColor, BoxOpacity);

                        // Only redraw text if HideTextOnFilled is false
                        if (!HideTextOnFilled)
                        {
                            int ticks = (int)Math.Ceiling(Math.Abs(imb.Top - imb.Bottom) / TickSize);
                            string label = $"{ticks} tks ({(imb.IsGap ? "gap 🧲" : "vi")}) 🚩";
                            
                            Draw.Text(this, imb.BoxTag + "_text", 
                                false, label, Time[0], imb.Top, 6,
                                ChartControl.Properties.ChartText,
                                new SimpleFont("Arial", TextSize),
                                TextAlignment.Right,
                                Brushes.Transparent,
                                Brushes.Transparent,
                                0);
                        }

                        // Remove from list after redrawing
                        bearishImbalances.RemoveAt(i);
                    }
                }

                // Check bullish imbalances
                for (int i = bullishImbalances.Count - 1; i >= 0; i--)
                {
                    var imb = bullishImbalances[i];
                    if (Low[0] <= imb.Bottom)
                    {
                        // Remove original drawings
                        RemoveDrawObject(imb.BoxTag);
                        RemoveDrawObject(imb.BoxTag + "_text");

                        // Redraw rectangle ending at current time
                        Draw.Rectangle(this, imb.BoxTag, false, 
                            Time[CurrentBar - imb.BarNumber], imb.Top,  // Start at original bar
                            Time[0], imb.Bottom,           // End at current time
                            BullishColor, BullishColor, BoxOpacity);

                        // Only redraw text if HideTextOnFilled is false
                        if (!HideTextOnFilled)
                        {
                            int ticks = (int)Math.Ceiling(Math.Abs(imb.Top - imb.Bottom) / TickSize);
                            string label = $"{ticks} tks ({(imb.IsGap ? "gap 🧲" : "vi")}) 🚩";
                            
                            Draw.Text(this, imb.BoxTag + "_text", 
                                false, label, Time[0], imb.Top, 6,
                                ChartControl.Properties.ChartText,
                                new SimpleFont("Arial", TextSize),
                                TextAlignment.Right,
                                Brushes.Transparent,
                                Brushes.Transparent,
                                0);
                        }

                        // Remove from list after redrawing
                        bullishImbalances.RemoveAt(i);
                    }
                }
            }
        }

		private void RenderImbalances()
		{
			RenderImbalanceList(bearishImbalances, false);
			RenderImbalanceList(bullishImbalances, true);
		}

		private void RenderImbalanceList(List<Imbalance> imbalances, bool isBullish)
		{
			int startIdx = Math.Max(0, imbalances.Count - VisToShow);
			for (int i = startIdx; i < imbalances.Count; i++)
			{
			var imb = imbalances[i];
			int ticks = (int)Math.Ceiling(Math.Abs(imb.Top - imb.Bottom) / TickSize);
			string label = $"{ticks} tks ({(imb.IsGap ? "gap 🧲" : "vi")})";
			
			Brush boxColor = isBullish ? BullishColor : BearishColor;
			
			// Calculate bars ago
			int startBarsAgo = CurrentBar - imb.BarNumber;
			int endBarsAgo = 0; // Current bar
			
			// Draw or update rectangle with fill color and opacity
			Draw.Rectangle(this, imb.BoxTag, false, startBarsAgo, imb.Top, endBarsAgo, imb.Bottom, 
				boxColor, boxColor, BoxOpacity);
			
			// Check for partial mitigation
			if ((isBullish && Low[0] <= imb.Bottom) || (!isBullish && High[0] >= imb.Bottom))
			{
			    label += " 🚩";
			}
			
			// Draw or update text with proper formatting - now at current bar (right side)
			Draw.Text(this, imb.BoxTag + "_text", 
				false,              					// isAutoScale 
				label,             						// text
				endBarsAgo,        						// barsAgo (now at current bar)
				imb.Top,            					// y
				6,               					    // yPixelOffset (2 pixels above the box)
				ChartControl.Properties.ChartText,  	// textBrush (using chart's text color)
				new SimpleFont("Arial", TextSize),		// font
				TextAlignment.Right,                	// alignment (changed to Right)
				Brushes.Transparent,                	// outlineBrush
				Brushes.Transparent,                	// areaBrush
				0);                                 	// areaOpacity
			
			if(AudioAlert)
			{
				PlaySound(NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav");
			}
			
			}
		}
    }
} 

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Dimension.VolumeGapsAndImbalances[] cacheVolumeGapsAndImbalances;
		public Dimension.VolumeGapsAndImbalances VolumeGapsAndImbalances(int visToShow, bool hideFilledGaps, bool hideTextOnFilled, Brush bullishColor, Brush bearishColor, int boxOpacity, int textSize, bool audioAlert)
		{
			return VolumeGapsAndImbalances(Input, visToShow, hideFilledGaps, hideTextOnFilled, bullishColor, bearishColor, boxOpacity, textSize, audioAlert);
		}

		public Dimension.VolumeGapsAndImbalances VolumeGapsAndImbalances(ISeries<double> input, int visToShow, bool hideFilledGaps, bool hideTextOnFilled, Brush bullishColor, Brush bearishColor, int boxOpacity, int textSize, bool audioAlert)
		{
			if (cacheVolumeGapsAndImbalances != null)
				for (int idx = 0; idx < cacheVolumeGapsAndImbalances.Length; idx++)
					if (cacheVolumeGapsAndImbalances[idx] != null && cacheVolumeGapsAndImbalances[idx].VisToShow == visToShow && cacheVolumeGapsAndImbalances[idx].HideFilledGaps == hideFilledGaps && cacheVolumeGapsAndImbalances[idx].HideTextOnFilled == hideTextOnFilled && cacheVolumeGapsAndImbalances[idx].BullishColor == bullishColor && cacheVolumeGapsAndImbalances[idx].BearishColor == bearishColor && cacheVolumeGapsAndImbalances[idx].BoxOpacity == boxOpacity && cacheVolumeGapsAndImbalances[idx].TextSize == textSize && cacheVolumeGapsAndImbalances[idx].AudioAlert == audioAlert && cacheVolumeGapsAndImbalances[idx].EqualsInput(input))
						return cacheVolumeGapsAndImbalances[idx];
			return CacheIndicator<Dimension.VolumeGapsAndImbalances>(new Dimension.VolumeGapsAndImbalances(){ VisToShow = visToShow, HideFilledGaps = hideFilledGaps, HideTextOnFilled = hideTextOnFilled, BullishColor = bullishColor, BearishColor = bearishColor, BoxOpacity = boxOpacity, TextSize = textSize, AudioAlert = audioAlert }, input, ref cacheVolumeGapsAndImbalances);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Dimension.VolumeGapsAndImbalances VolumeGapsAndImbalances(int visToShow, bool hideFilledGaps, bool hideTextOnFilled, Brush bullishColor, Brush bearishColor, int boxOpacity, int textSize, bool audioAlert)
		{
			return indicator.VolumeGapsAndImbalances(Input, visToShow, hideFilledGaps, hideTextOnFilled, bullishColor, bearishColor, boxOpacity, textSize, audioAlert);
		}

		public Indicators.Dimension.VolumeGapsAndImbalances VolumeGapsAndImbalances(ISeries<double> input , int visToShow, bool hideFilledGaps, bool hideTextOnFilled, Brush bullishColor, Brush bearishColor, int boxOpacity, int textSize, bool audioAlert)
		{
			return indicator.VolumeGapsAndImbalances(input, visToShow, hideFilledGaps, hideTextOnFilled, bullishColor, bearishColor, boxOpacity, textSize, audioAlert);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Dimension.VolumeGapsAndImbalances VolumeGapsAndImbalances(int visToShow, bool hideFilledGaps, bool hideTextOnFilled, Brush bullishColor, Brush bearishColor, int boxOpacity, int textSize, bool audioAlert)
		{
			return indicator.VolumeGapsAndImbalances(Input, visToShow, hideFilledGaps, hideTextOnFilled, bullishColor, bearishColor, boxOpacity, textSize, audioAlert);
		}

		public Indicators.Dimension.VolumeGapsAndImbalances VolumeGapsAndImbalances(ISeries<double> input , int visToShow, bool hideFilledGaps, bool hideTextOnFilled, Brush bullishColor, Brush bearishColor, int boxOpacity, int textSize, bool audioAlert)
		{
			return indicator.VolumeGapsAndImbalances(input, visToShow, hideFilledGaps, hideTextOnFilled, bullishColor, bearishColor, boxOpacity, textSize, audioAlert);
		}
	}
}

#endregion
