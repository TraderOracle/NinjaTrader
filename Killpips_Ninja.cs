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
using System.Net.Http;
using System.Windows.Forms;
using System.Web.Script.Serialization;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class Killpips : Indicator
    {
        private HttpClient http;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Killpips v1.1";
                Name = "Killpips";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                iTextSize = 12;
				iTextOffset = 15;
				iLineWidth = 2;
                kvo1 = Brushes.Violet;
                kvo2 = Brushes.Turquoise;
                max = Brushes.Red;
                min = Brushes.Lime;
                b50 = Brushes.Aqua;
                b0 = Brushes.Blue;
                vix = Brushes.BurlyWood;

                nqData = "$NQ1!: vix r1, 19064, vix r2, 19095, vix s1, 18692, vix s2, 18661, range k max, 19368, range k+50%, 19168, range k 0, 18964, range k-50%, 18762, range k min, 18558, kvo, 19269, kvo, 19070, kvo, 18860, kvo, 18665, kvo, 18563, kvo, 19090";
                esData = "$ES1!: vix r1, 5552, vix r2, 5561, vix s1, 5443, vix s2, 5434, range k max, 5593, kvo, 5573, range k+50%, 5554, kvo, 5532, range k 0, 5511.5, kvo, 5491, range k-50%, 5470.50, kvo, 5450, range k min, 5429.50, kvo, 5555.50, kvo, 5503";
                ymData = "$YM1!: vix r1, 41173, vix r2, 41242, vix s1, 40370, vix s2, 40303, range k max, 41323, kvo, 41235, range k+50%, 41076, kvo, 40995, range k 0, 40832, kvo, 40750, range k-50%, 40581, kvo, 40504, range k min, 40339, kvo, 40849, kvo, 41211";
                RTYData = "$RTY1!: vix r1, 2155.7, vix r2, 2159.3, vix s1, 2113.6, vix s2, 2110.1, range k max, 2181.9, range k+50%, 2159.6, range k 0, 2137.7, range k-50%, 2115.4, range k min, 2093.6, kvo, 2170, kvo, 2147, kvo, 2125, kvo, 2103, kvo, 2133.3, kvo, 2161.4";
                CLData = "$CL1!: vix r1, 69.82, vix r2, 69.94, vix r1, 68.46, vix s2, 68.35, range k max, 71.79, range k+50%, 70.48, range k 0, 69.14, range k-50%, 67.82, range k min, 66.51, kvo, 67.15, kvo, 68.45, kvo, 69.79, kvo, 71.13, kvo, 69.15, kvo, 70.90";
                NGData = "$NG1!: vix r1, 2.281, vix r2, 2.285, vix s1, 2.237, vix s2, 2.233, range k max, 2.385, range k+50%, 2.320, range k 0, 2.253, range k-50%, 2.188, range k min, 2.123, kvo, 2.156, kvo, 2.223, kvo, 2.289, kvo, 2.353, kvo, 2.117, kvo, 2.248";
                FDAXData = "$FDAX1!: vix r1, 18766, vix r2, 18797, vix s1, 18399, vix s2, 18369, range k max, 18838, range k+50%, 18732, range k 0, 18625, range k-50%, 18517, range k min, 18410, kvo, 18465, kvo, 18572, kvo, 18680, kvo, 18787";
                GCData = "$GC1!: vix r1, 2565.8, vix r2, 2570.1, vix s1, 2515.7, vix s2, 2511.6, range k max, 2576, range k+50%, 2559.6, range k 0, 2543, range k-50%, 2526.6, range k min, 2510.1, kvo, 2567, kvo, 2551.5, kvo, 2534, kvo, 2517, kvo, 2511.4, kvo, 2537.1";
                SPXData = "$SPX: vix r1, 5543, vix r2, 5553, vix s1, 5435, vix s2, 5426, range k max, 5569, kvo, 5552, range k+50%, 5535, kvo, 5519, range k 0, 5503, kvo, 5486, range k-50%, 5470.12, kvo, 5453, range k min, 5437, kvo, 5564 ";
                NDXData = "$NDX: vix r1, 19069, vix r2, 19101, vix s1, 18697, vix s2, 18667, range k max, 19225, kvo, 19151, range k+50%, 19078, kvo, 19004, range k 0, 18930, kvo, 18856, range k-50%, 18782, kvo, 18708, range k min, 18634";
            }
            else if (State == State.Configure)
            {
                http = new HttpClient();
            }
        }

        private Brush GetColor(string s)
        {
            if (s.Contains("kvo1"))
                return kvo1;
            else if (s.Contains("kvo2"))
                return kvo2;
            else if (s.Contains("max"))
                return max;
            else if (s.Contains("min"))
                return min;
            else if (s.Contains("k+50%"))
                return b50;
            else if (s.Contains("k 0"))
                return b0;
            else if (s.Contains("vix"))
                return vix;

            return Brushes.White;
        }

        private string CheckInstrument(string instrumentFullName)
        {
			Print("instrumentFullName: " + instrumentFullName);
            instrumentFullName = MapToMiniContract(instrumentFullName);
            if (instrumentFullName.StartsWith("NQ"))
                return nqData;
            else if (instrumentFullName.StartsWith("YM"))
                return ymData;
            else if (instrumentFullName.StartsWith("GC"))
                return GCData;
            else if (instrumentFullName.StartsWith("CL"))
                return CLData;
            else if (instrumentFullName.StartsWith("ES"))
                return esData;
            else if (instrumentFullName.StartsWith("RTY"))
                return RTYData;
            else if (instrumentFullName.StartsWith("NG"))
                return NGData;
            else if (instrumentFullName.StartsWith("FDAX"))
                return FDAXData;
            else if (instrumentFullName.StartsWith("SPX"))
                return SPXData;
            else if (instrumentFullName.StartsWith("NDX"))
                return NDXData;
            else
                return "";
        }

        private string MapToMiniContract(string instrumentFullName)
		{
		    if (instrumentFullName.StartsWith("MNQ"))
		        return "NQ";
            else if (instrumentFullName.StartsWith("MGC"))
		        return "GC";
            else if (instrumentFullName.StartsWith("MCL"))
		        return "CL";
            else if (instrumentFullName.StartsWith("MES"))
		        return "ES";
            else if (instrumentFullName.StartsWith("MYM"))
		        return "YM";
            else
                return instrumentFullName;
		}

        private async void FetchData()
        {
            SimpleFont sf = new SimpleFont
            {
                Bold = false,
                Size = iTextSize
            };

            try
            {
                string sLine = CheckInstrument(Instrument.FullName).Split(':')[1];
				Print("FetchData: " + sLine);
				
                int i = 0;
                string[] sb = sLine.Split(',');
                string price = string.Empty;
                string desc = string.Empty;
                foreach (string sr in sb)
                {
                    if (i % 2 != 0)
                        price = sr.Trim();
                    else
                        desc = sr.Trim();

                    if (!string.IsNullOrEmpty(price) && !string.IsNullOrEmpty(desc))
                    {
                        Print("Price: |" + price + "| desc=|" + desc + "|");
                        double pr = double.Parse(price);

                        double textPosition = pr - (TickSize * iTextOffset);

                        Draw.HorizontalLine(this, "Line" + i, pr, GetColor(desc), DashStyleHelper.Solid, iLineWidth);
                        Draw.Text(this, "lbl" + i, true, desc, -1, pr, 0, GetColor(desc), sf, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 40);

                        price = string.Empty;
                        desc = string.Empty;
                    }
                    i++;
                }
            }
            catch (Exception ex)
            {
                Print("Error fetching data: " + ex.Message);
            }
        }

        
        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || !Bars.IsLastBarOfSession)
                return;

            FetchData();
        }

        #region Parameters

        [NinjaScriptProperty]
        [Display(Name = "NQ String", GroupName = "General", Order = 1)]
        public string nqData { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "ES String", GroupName = "General", Order = 1)]
        public string esData { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "YM String", GroupName = "General", Order = 1)]
        public string ymData { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "RTY String", GroupName = "General", Order = 1)]
        public string RTYData { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "CL String", GroupName = "General", Order = 1)]
        public string CLData { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "NG String", GroupName = "General", Order = 1)]
        public string NGData { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "FDAX String", GroupName = "General", Order = 1)]
        public string FDAXData { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "GC String", GroupName = "General", Order = 1)]
        public string GCData { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "SPX String", GroupName = "General", Order = 1)]
        public string SPXData { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "NDX String", GroupName = "General", Order = 1)]
        public string NDXData { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Text Size", GroupName = "General", Order = 2)]
        public int iTextSize { get; set; }
		
		[NinjaScriptProperty]
        [Display(Name = "Text Offset", GroupName = "General", Order = 3)]
        public int iTextOffset { get; set; }
		
		[NinjaScriptProperty]
        [Display(Name = "Line Width", GroupName = "General", Order = 4)]
        public int iLineWidth { get; set; }

        [XmlIgnore]
        [Display(Name = "kvo1 Color", GroupName = "Colors", Order = 2)]
        public Brush kvo1 { get; set; }

        [XmlIgnore]
        [Display(Name = "kvo2 Color", GroupName = "Colors", Order = 2)]
        public Brush kvo2 { get; set; }

        [XmlIgnore]
        [Display(Name = "vix Color", GroupName = "Colors", Order = 2)]
        public Brush vix { get; set; }

        [XmlIgnore]
        [Display(Name = "max Color", GroupName = "Colors", Order = 2)]
        public Brush max { get; set; }

        [XmlIgnore]
        [Display(Name = "min Color", GroupName = "Colors", Order = 2)]
        public Brush min { get; set; }

        [XmlIgnore]
        [Display(Name = "50% Color", GroupName = "Colors", Order = 2)]
        public Brush b50 { get; set; }

        [XmlIgnore]
        [Display(Name = "Range 0% Color", GroupName = "Colors", Order = 2)]
        public Brush b0 { get; set; }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Killpips[] cacheKillpips;
		public Killpips Killpips(string nqData, string esData, string ymData, string rTYData, string cLData, string nGData, string fDAXData, string gCData, string sPXData, string nDXData, int iTextSize, int iTextOffset, int iLineWidth)
		{
			return Killpips(Input, nqData, esData, ymData, rTYData, cLData, nGData, fDAXData, gCData, sPXData, nDXData, iTextSize, iTextOffset, iLineWidth);
		}

		public Killpips Killpips(ISeries<double> input, string nqData, string esData, string ymData, string rTYData, string cLData, string nGData, string fDAXData, string gCData, string sPXData, string nDXData, int iTextSize, int iTextOffset, int iLineWidth)
		{
			if (cacheKillpips != null)
				for (int idx = 0; idx < cacheKillpips.Length; idx++)
					if (cacheKillpips[idx] != null && cacheKillpips[idx].nqData == nqData && cacheKillpips[idx].esData == esData && cacheKillpips[idx].ymData == ymData && cacheKillpips[idx].RTYData == rTYData && cacheKillpips[idx].CLData == cLData && cacheKillpips[idx].NGData == nGData && cacheKillpips[idx].FDAXData == fDAXData && cacheKillpips[idx].GCData == gCData && cacheKillpips[idx].SPXData == sPXData && cacheKillpips[idx].NDXData == nDXData && cacheKillpips[idx].iTextSize == iTextSize && cacheKillpips[idx].iTextOffset == iTextOffset && cacheKillpips[idx].iLineWidth == iLineWidth && cacheKillpips[idx].EqualsInput(input))
						return cacheKillpips[idx];
			return CacheIndicator<Killpips>(new Killpips(){ nqData = nqData, esData = esData, ymData = ymData, RTYData = rTYData, CLData = cLData, NGData = nGData, FDAXData = fDAXData, GCData = gCData, SPXData = sPXData, NDXData = nDXData, iTextSize = iTextSize, iTextOffset = iTextOffset, iLineWidth = iLineWidth }, input, ref cacheKillpips);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Killpips Killpips(string nqData, string esData, string ymData, string rTYData, string cLData, string nGData, string fDAXData, string gCData, string sPXData, string nDXData, int iTextSize, int iTextOffset, int iLineWidth)
		{
			return indicator.Killpips(Input, nqData, esData, ymData, rTYData, cLData, nGData, fDAXData, gCData, sPXData, nDXData, iTextSize, iTextOffset, iLineWidth);
		}

		public Indicators.Killpips Killpips(ISeries<double> input , string nqData, string esData, string ymData, string rTYData, string cLData, string nGData, string fDAXData, string gCData, string sPXData, string nDXData, int iTextSize, int iTextOffset, int iLineWidth)
		{
			return indicator.Killpips(input, nqData, esData, ymData, rTYData, cLData, nGData, fDAXData, gCData, sPXData, nDXData, iTextSize, iTextOffset, iLineWidth);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Killpips Killpips(string nqData, string esData, string ymData, string rTYData, string cLData, string nGData, string fDAXData, string gCData, string sPXData, string nDXData, int iTextSize, int iTextOffset, int iLineWidth)
		{
			return indicator.Killpips(Input, nqData, esData, ymData, rTYData, cLData, nGData, fDAXData, gCData, sPXData, nDXData, iTextSize, iTextOffset, iLineWidth);
		}

		public Indicators.Killpips Killpips(ISeries<double> input , string nqData, string esData, string ymData, string rTYData, string cLData, string nGData, string fDAXData, string gCData, string sPXData, string nDXData, int iTextSize, int iTextOffset, int iLineWidth)
		{
			return indicator.Killpips(input, nqData, esData, ymData, rTYData, cLData, nGData, fDAXData, gCData, sPXData, nDXData, iTextSize, iTextOffset, iLineWidth);
		}
	}
}

#endregion
