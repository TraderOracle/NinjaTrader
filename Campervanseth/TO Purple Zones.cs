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
	public class TWGammaExposureLevelsUpload : Indicator
	{
		private List<double> topLevels;
		private List<double> bottomLevels;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Draws horizontal price lines for TOP and BOTTOM levels with purple fill areas";
				Name										= "TW Gamma Exposure Levels Upload";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= true;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive					= true;

				// Default Parameters - Separate inputs for each instrument
				LevelsInputES								= "TOP,6100,BOTTOM,6080,TOP,6060,BOTTOM,6040";
				LevelsInputNQ								= "TOP,21500,BOTTOM,21400,TOP,21300,BOTTOM,21200";
				LevelsInputRTY								= "TOP,2300,BOTTOM,2280,TOP,2260,BOTTOM,2240";
				LevelsInputGC								= "TOP,2750,BOTTOM,2730,TOP,2710,BOTTOM,2690";

				// Support/Resistance inputs
				SRInputES									= "R:6105-Resistance1,6120-Resistance2,N:6100-Neutral,S:6095-Support1,6080-Support2";
				SRInputNQ									= "R:21550-Resistance1,21600-Resistance2,N:21500-Neutral,S:21450-Support1,21400-Support2";
				SRInputRTY									= "R:2310-Resistance1,2320-Resistance2,N:2300-Neutral,S:2290-Support1,2280-Support2";
				SRInputGC									= "R:2760-Resistance1,2770-Resistance2,N:2750-Neutral,S:2740-Support1,2730-Support2";

				LineColor									= Brushes.MediumOrchid;
				LineWidth									= 2;
				LineStyle									= DashStyleHelper.Solid;
				FillColor									= Brushes.MediumOrchid;
				FillOpacity									= 15;
				ShowLabels									= true;
				SRLabelFontSize								= 12;
			}
			else if (State == State.Configure)
			{
			}
			else if (State == State.DataLoaded)
			{
				topLevels = new List<double>();
				bottomLevels = new List<double>();
				ParseLevelsInput();
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 1)
				return;

			// Draw lines and fill areas on the first bar
			if (CurrentBar == 1)
			{
				DrawLevelsAndFills();
			}

			// Draw S/R labels on every bar so they stay visible on the right side
			DrawSupportResistanceLevels();
		}

		private string GetLevelsForCurrentInstrument()
		{
			// Get the instrument name
			string instrumentName = Instrument.MasterInstrument.Name.ToUpper();

			// Check which instrument and return appropriate levels
			if (instrumentName.Contains("ES"))
				return LevelsInputES;
			else if (instrumentName.Contains("NQ"))
				return LevelsInputNQ;
			else if (instrumentName.Contains("RTY"))
				return LevelsInputRTY;
			else if (instrumentName.Contains("GC"))
				return LevelsInputGC;
			else
				return ""; // No levels for unknown instruments
		}

		private void ParseLevelsInput()
		{
			topLevels.Clear();
			bottomLevels.Clear();

			// Determine which levels to use based on current instrument
			string levelsToUse = GetLevelsForCurrentInstrument();

			if (string.IsNullOrWhiteSpace(levelsToUse))
				return;

			try
			{
				string[] tokens = levelsToUse.Split(',');

				for (int i = 0; i < tokens.Length; i++)
				{
					string token = tokens[i].Trim().ToUpper();

					if (token == "TOP" && i + 1 < tokens.Length)
					{
						if (double.TryParse(tokens[i + 1].Trim(), out double price))
						{
							topLevels.Add(price);
							i++; // Skip the next token since we've already processed it
						}
					}
					else if (token == "BOTTOM" && i + 1 < tokens.Length)
					{
						if (double.TryParse(tokens[i + 1].Trim(), out double price))
						{
							bottomLevels.Add(price);
							i++; // Skip the next token since we've already processed it
						}
					}
				}
			}
			catch (Exception ex)
			{
				Print("Error parsing levels input: " + ex.Message);
			}
		}

		private void DrawLevelsAndFills()
		{
			// Make sure we have matching pairs of TOP and BOTTOM levels
			int pairCount = Math.Min(topLevels.Count, bottomLevels.Count);

			for (int i = 0; i < pairCount; i++)
			{
				double topPrice = topLevels[i];
				double bottomPrice = bottomLevels[i];

				// Draw TOP line
				string topLineTag = "TopLine_" + i;
				Draw.HorizontalLine(this, topLineTag, topPrice, LineColor, LineStyle, LineWidth);

				if (ShowLabels)
				{
					Draw.Text(this, "TopLabel_" + i, true, "TOP: " + topPrice.ToString("F2"),
						0, topPrice, 10, LineColor, new SimpleFont("Arial", 10) { Bold = true },
						TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
				}

				// Draw BOTTOM line
				string bottomLineTag = "BottomLine_" + i;
				Draw.HorizontalLine(this, bottomLineTag, bottomPrice, LineColor, LineStyle, LineWidth);

				if (ShowLabels)
				{
					Draw.Text(this, "BottomLabel_" + i, true, "BOTTOM: " + bottomPrice.ToString("F2"),
						0, bottomPrice, 10, LineColor, new SimpleFont("Arial", 10) { Bold = true },
						TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
				}

				// Draw filled region between TOP and BOTTOM
				string regionTag = "FillRegion_" + i;
				DrawFilledRegion(regionTag, topPrice, bottomPrice);
			}
		}

		private void DrawFilledRegion(string tag, double topPrice, double bottomPrice)
		{
			// Create a region fill using RegionHighlightY
			// The method signature requires: NinjaScriptBase, tag, autoScale, Y1, Y2, outlineBrush, areaBrush, areaOpacity
			Draw.RegionHighlightY(this, tag, true,
				Math.Min(topPrice, bottomPrice),
				Math.Max(topPrice, bottomPrice),
				Brushes.Transparent,
				FillColor,
				FillOpacity);
		}

		private string GetSRForCurrentInstrument()
		{
			// Get the instrument name
			string instrumentName = Instrument.MasterInstrument.Name.ToUpper();

			// Check which instrument and return appropriate S/R levels
			if (instrumentName.Contains("ES"))
				return SRInputES;
			else if (instrumentName.Contains("NQ"))
				return SRInputNQ;
			else if (instrumentName.Contains("RTY"))
				return SRInputRTY;
			else if (instrumentName.Contains("GC"))
				return SRInputGC;
			else
				return ""; // No S/R levels for unknown instruments
		}

		private void DrawSupportResistanceLevels()
		{
			string srInput = GetSRForCurrentInstrument();

			if (string.IsNullOrWhiteSpace(srInput))
				return;

			try
			{
				int srCounter = 0;
				int currentIndex = 0;

				while (currentIndex < srInput.Length)
				{
					// Find the next type indicator
					int rIndex = srInput.IndexOf("R:", currentIndex);
					int nIndex = srInput.IndexOf("N:", currentIndex);
					int sIndex = srInput.IndexOf("S:", currentIndex);

					// Determine which type comes first
					int nextIndex = -1;
					Brush lineColor = Brushes.Gray;

					if (rIndex != -1 && (rIndex < nIndex || nIndex == -1) && (rIndex < sIndex || sIndex == -1))
					{
						nextIndex = rIndex;
						lineColor = Brushes.Red;
					}
					else if (nIndex != -1 && (nIndex < sIndex || sIndex == -1))
					{
						nextIndex = nIndex;
						lineColor = Brushes.Gray;
					}
					else if (sIndex != -1)
					{
						nextIndex = sIndex;
						lineColor = Brushes.LimeGreen;
					}

					if (nextIndex == -1)
						break;

					// Find the end of this section (next type indicator or end of string)
					int sectionStart = nextIndex + 2;
					int sectionEnd = srInput.Length;

					int nextR = srInput.IndexOf("R:", sectionStart);
					int nextN = srInput.IndexOf("N:", sectionStart);
					int nextS = srInput.IndexOf("S:", sectionStart);

					if (nextR != -1)
						sectionEnd = Math.Min(sectionEnd, nextR);
					if (nextN != -1)
						sectionEnd = Math.Min(sectionEnd, nextN);
					if (nextS != -1)
						sectionEnd = Math.Min(sectionEnd, nextS);

					string section = srInput.Substring(sectionStart, sectionEnd - sectionStart).Trim();

					// Parse price-label pairs in this section
					string[] pairs = section.Split(',');

					foreach (string pair in pairs)
					{
						if (string.IsNullOrWhiteSpace(pair))
							continue;

						// Split by dash to get price and label
						string[] parts = pair.Split('-');

						if (parts.Length >= 2)
						{
							if (double.TryParse(parts[0].Trim(), out double price))
							{
								string label = parts[1].Trim();

								// Draw horizontal line only once
								if (CurrentBar == 1)
								{
									string lineTag = "SR_Line_" + srCounter;
									Draw.HorizontalLine(this, lineTag, price, lineColor, DashStyleHelper.Solid, 2);
								}

								// Draw label on every bar to keep it visible on the right (5 ticks above the line)
								string labelTag = "SR_Label_" + srCounter;
								string labelText = price.ToString("F2") + " - " + label;
								Draw.Text(this, labelTag, true, labelText,
									0, price, 5, lineColor, new SimpleFont("Arial", SRLabelFontSize),
									TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);

								srCounter++;
							}
						}
					}

					currentIndex = sectionEnd;
				}
			}
			catch (Exception ex)
			{
				Print("Error parsing S/R input: " + ex.Message);
			}
		}

		public override void OnRenderTargetChanged()
		{
			base.OnRenderTargetChanged();
			// Redraw when render target changes
			if (State == State.DataLoaded)
			{
				ParseLevelsInput();
			}
		}

		#region Properties

		[NinjaScriptProperty]
		[Display(Name="ES Levels", Description="ES levels: TOP,price,BOTTOM,price,TOP,price,BOTTOM,price", Order=1, GroupName="Instrument Levels")]
		public string LevelsInputES
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="NQ Levels", Description="NQ levels: TOP,price,BOTTOM,price,TOP,price,BOTTOM,price", Order=2, GroupName="Instrument Levels")]
		public string LevelsInputNQ
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="RTY Levels", Description="RTY levels: TOP,price,BOTTOM,price,TOP,price,BOTTOM,price", Order=3, GroupName="Instrument Levels")]
		public string LevelsInputRTY
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="GC Levels", Description="GC levels: TOP,price,BOTTOM,price,TOP,price,BOTTOM,price", Order=4, GroupName="Instrument Levels")]
		public string LevelsInputGC
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="ES S/R", Description="ES Support/Resistance: R:price-label,price-label,N:price-label,S:price-label", Order=5, GroupName="Instrument Levels")]
		public string SRInputES
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="NQ S/R", Description="NQ Support/Resistance: R:price-label,price-label,N:price-label,S:price-label", Order=6, GroupName="Instrument Levels")]
		public string SRInputNQ
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="RTY S/R", Description="RTY Support/Resistance: R:price-label,price-label,N:price-label,S:price-label", Order=7, GroupName="Instrument Levels")]
		public string SRInputRTY
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="GC S/R", Description="GC Support/Resistance: R:price-label,price-label,N:price-label,S:price-label", Order=8, GroupName="Instrument Levels")]
		public string SRInputGC
		{ get; set; }

		[XmlIgnore]
		[Display(Name="Line Color", Description="Color of the horizontal lines", Order=1, GroupName="Visual Settings")]
		public Brush LineColor
		{ get; set; }

		[Browsable(false)]
		public string LineColorSerializable
		{
			get { return Serialize.BrushToString(LineColor); }
			set { LineColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name="Line Width", Description="Width of the horizontal lines", Order=2, GroupName="Visual Settings")]
		public int LineWidth
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="Line Style", Description="Style of the horizontal lines", Order=3, GroupName="Visual Settings")]
		public DashStyleHelper LineStyle
		{ get; set; }

		[XmlIgnore]
		[Display(Name="Fill Color", Description="Color for the fill areas", Order=4, GroupName="Visual Settings")]
		public Brush FillColor
		{ get; set; }

		[Browsable(false)]
		public string FillColorSerializable
		{
			get { return Serialize.BrushToString(FillColor); }
			set { FillColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name="Fill Opacity", Description="Opacity of the fill areas (1-100%)", Order=5, GroupName="Visual Settings")]
		public int FillOpacity
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="Show Labels", Description="Show TOP/BOTTOM labels on lines", Order=6, GroupName="Visual Settings")]
		public bool ShowLabels
		{ get; set; }

		[NinjaScriptProperty]
		[Range(6, 20)]
		[Display(Name="S/R Label Font Size", Description="Font size for Support/Resistance labels", Order=7, GroupName="Visual Settings")]
		public int SRLabelFontSize
		{ get; set; }

		#endregion

		public override string DisplayName
		{
			get { return Name; }
		}
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TWGammaExposureLevelsUpload[] cacheTWGammaExposureLevelsUpload;
		public TWGammaExposureLevelsUpload TWGammaExposureLevelsUpload(string levelsInputES, string levelsInputNQ, string levelsInputRTY, string levelsInputGC, string sRInputES, string sRInputNQ, string sRInputRTY, string sRInputGC, int lineWidth, DashStyleHelper lineStyle, int fillOpacity, bool showLabels, int sRLabelFontSize)
		{
			return TWGammaExposureLevelsUpload(Input, levelsInputES, levelsInputNQ, levelsInputRTY, levelsInputGC, sRInputES, sRInputNQ, sRInputRTY, sRInputGC, lineWidth, lineStyle, fillOpacity, showLabels, sRLabelFontSize);
		}

		public TWGammaExposureLevelsUpload TWGammaExposureLevelsUpload(ISeries<double> input, string levelsInputES, string levelsInputNQ, string levelsInputRTY, string levelsInputGC, string sRInputES, string sRInputNQ, string sRInputRTY, string sRInputGC, int lineWidth, DashStyleHelper lineStyle, int fillOpacity, bool showLabels, int sRLabelFontSize)
		{
			if (cacheTWGammaExposureLevelsUpload != null)
				for (int idx = 0; idx < cacheTWGammaExposureLevelsUpload.Length; idx++)
					if (cacheTWGammaExposureLevelsUpload[idx] != null && cacheTWGammaExposureLevelsUpload[idx].LevelsInputES == levelsInputES && cacheTWGammaExposureLevelsUpload[idx].LevelsInputNQ == levelsInputNQ && cacheTWGammaExposureLevelsUpload[idx].LevelsInputRTY == levelsInputRTY && cacheTWGammaExposureLevelsUpload[idx].LevelsInputGC == levelsInputGC && cacheTWGammaExposureLevelsUpload[idx].SRInputES == sRInputES && cacheTWGammaExposureLevelsUpload[idx].SRInputNQ == sRInputNQ && cacheTWGammaExposureLevelsUpload[idx].SRInputRTY == sRInputRTY && cacheTWGammaExposureLevelsUpload[idx].SRInputGC == sRInputGC && cacheTWGammaExposureLevelsUpload[idx].LineWidth == lineWidth && cacheTWGammaExposureLevelsUpload[idx].LineStyle == lineStyle && cacheTWGammaExposureLevelsUpload[idx].FillOpacity == fillOpacity && cacheTWGammaExposureLevelsUpload[idx].ShowLabels == showLabels && cacheTWGammaExposureLevelsUpload[idx].SRLabelFontSize == sRLabelFontSize && cacheTWGammaExposureLevelsUpload[idx].EqualsInput(input))
						return cacheTWGammaExposureLevelsUpload[idx];
			return CacheIndicator<TWGammaExposureLevelsUpload>(new TWGammaExposureLevelsUpload(){ LevelsInputES = levelsInputES, LevelsInputNQ = levelsInputNQ, LevelsInputRTY = levelsInputRTY, LevelsInputGC = levelsInputGC, SRInputES = sRInputES, SRInputNQ = sRInputNQ, SRInputRTY = sRInputRTY, SRInputGC = sRInputGC, LineWidth = lineWidth, LineStyle = lineStyle, FillOpacity = fillOpacity, ShowLabels = showLabels, SRLabelFontSize = sRLabelFontSize }, input, ref cacheTWGammaExposureLevelsUpload);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TWGammaExposureLevelsUpload TWGammaExposureLevelsUpload(string levelsInputES, string levelsInputNQ, string levelsInputRTY, string levelsInputGC, string sRInputES, string sRInputNQ, string sRInputRTY, string sRInputGC, int lineWidth, DashStyleHelper lineStyle, int fillOpacity, bool showLabels, int sRLabelFontSize)
		{
			return indicator.TWGammaExposureLevelsUpload(Input, levelsInputES, levelsInputNQ, levelsInputRTY, levelsInputGC, sRInputES, sRInputNQ, sRInputRTY, sRInputGC, lineWidth, lineStyle, fillOpacity, showLabels, sRLabelFontSize);
		}

		public Indicators.TWGammaExposureLevelsUpload TWGammaExposureLevelsUpload(ISeries<double> input , string levelsInputES, string levelsInputNQ, string levelsInputRTY, string levelsInputGC, string sRInputES, string sRInputNQ, string sRInputRTY, string sRInputGC, int lineWidth, DashStyleHelper lineStyle, int fillOpacity, bool showLabels, int sRLabelFontSize)
		{
			return indicator.TWGammaExposureLevelsUpload(input, levelsInputES, levelsInputNQ, levelsInputRTY, levelsInputGC, sRInputES, sRInputNQ, sRInputRTY, sRInputGC, lineWidth, lineStyle, fillOpacity, showLabels, sRLabelFontSize);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TWGammaExposureLevelsUpload TWGammaExposureLevelsUpload(string levelsInputES, string levelsInputNQ, string levelsInputRTY, string levelsInputGC, string sRInputES, string sRInputNQ, string sRInputRTY, string sRInputGC, int lineWidth, DashStyleHelper lineStyle, int fillOpacity, bool showLabels, int sRLabelFontSize)
		{
			return indicator.TWGammaExposureLevelsUpload(Input, levelsInputES, levelsInputNQ, levelsInputRTY, levelsInputGC, sRInputES, sRInputNQ, sRInputRTY, sRInputGC, lineWidth, lineStyle, fillOpacity, showLabels, sRLabelFontSize);
		}

		public Indicators.TWGammaExposureLevelsUpload TWGammaExposureLevelsUpload(ISeries<double> input , string levelsInputES, string levelsInputNQ, string levelsInputRTY, string levelsInputGC, string sRInputES, string sRInputNQ, string sRInputRTY, string sRInputGC, int lineWidth, DashStyleHelper lineStyle, int fillOpacity, bool showLabels, int sRLabelFontSize)
		{
			return indicator.TWGammaExposureLevelsUpload(input, levelsInputES, levelsInputNQ, levelsInputRTY, levelsInputGC, sRInputES, sRInputNQ, sRInputRTY, sRInputGC, lineWidth, lineStyle, fillOpacity, showLabels, sRLabelFontSize);
		}
	}
}

#endregion
