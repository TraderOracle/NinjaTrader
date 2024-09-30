#region Using declarations

using Newtonsoft.Json.Linq;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using SharpDX;
using SharpDX.DirectWrite;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Timers;
using System.Windows.Media;
using NinjaTrader.NinjaScript.Indicators;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
using SharpDX.Direct2D1;
using Brush = System.Windows.Media.Brush;
using Ellipse = SharpDX.Direct2D1.Ellipse;
using EllipseGeometry = SharpDX.Direct2D1.EllipseGeometry;
using SharpDX.DirectWrite;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using System.Xml.Serialization;

#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	[Gui.CategoryOrder(GENERAL_GROUP, 1)]
	[Gui.CategoryOrder(LINES_GROUP, 2)]
	public class GexBot : Indicator
	{
		#region Constants
		private const string sVersion = "version 2.3";
		private const string INDICATOR_NAME = "GexBot";

		private const string GENERAL_GROUP = "General";
		private const string LINES_GROUP = "Lines";

		internal const int EXCEPTION_LIMIT = 2;
		#endregion

		#region Variables

		protected int exceptionCount;

		private HttpClient http;

		private Timer timer;
		private Timer CouchTimer;
		private bool bInProgress = false;
        private StrokeStyleProperties strokeStyleProperties;
        private SharpDX.Direct2D1.DashStyle dashes = SharpDX.Direct2D1.DashStyle.Solid;

        private string VolGex = "";
		private string Vol0Gamma = "";
		private string VolMajPos = "";
		private string VolMinNeg = "";
		private string DeltaReversal = "";
		private string Spot = "";
		private string OIGex = "";
		private string OIMajPos = "";
		private string OIMinNeg = "";

		public struct changes
		{
			public double volume;
			public double price;
		}

		// all objects not needed in State.SetDefaults should be instantiated in State.Configure since opening the indicators dialog creates an instance of every single indicator only to be released.
		List<changes> lc;   // = new List<changes>();

		public struct lines
		{
			public double volume;
			public double oi;
			public double price;
			public double call;
			public double put;
		}
		List<lines> ll; // = new List<lines>();

		public struct dots
		{
			public double volume;
			public double price;
			public int i;
		}
		List<dots> ld;  // = new List<dots>();


		// Cached Brushes
		SharpDX.Direct2D1.Brush[] dotBrushes;

		bool isGreekNone;
		string maxChangesUrl;
		string fetchDataUrl;

		#endregion

		#region DisplayName override
		public override string DisplayName
		{
			get
			{
				return (State == State.SetDefaults) ? INDICATOR_NAME : string.Empty;
			}
		}
		#endregion

		#region OnStateChange

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = INDICATOR_NAME;
				Name = INDICATOR_NAME;
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = true;
				DrawOnPricePanel = true;
				DrawHorizontalGridLines = true;
				DrawVerticalGridLines = true;
				PaintPriceMarkers = true;
				ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive = true;

				SubscriptionType = GexSubscriptionType.Classic;	// =  "classic";
				//lineLoc = "Left";
				ticker = "ES_SPX";
				nextFull = "zero";
				APIKey = "yOtbw77tOpXp";
				Greek = "none";
				iPos = 130;
				convFactor = 1;
				iRefresh = 5;
				iCouchRefresh = 3;
				rightOffset = 500;
				WidthFactor = 1;
				dotSize = 2;
                GreekdotSize = 7;
                bShowMaxChange = false;
				bShowStatus = true;
                Negative = Brushes.Orange;
                Positive = Brushes.Lime;
            }
			else if (State == State.Configure)
			{
				exceptionCount = 0;

				if (string.IsNullOrWhiteSpace(Greek)) 
					Greek = "none";
				isGreekNone = Greek.Equals("none");

				string subType = SubscriptionType.ToString().ToLower();
				string sB = SubscriptionType == GexSubscriptionType.Classic ? "zero/maxchange" : "full/maxchange";
				maxChangesUrl = string.Format("https://api.gexbot.com/{0}/{1}/{2}?key={3}", ticker, subType, sB, APIKey);
				fetchDataUrl = string.Format("https://api.gexbot.com/{0}/{1}/{2}?key={3}", ticker, subType, nextFull, APIKey);

				lc = new List<changes>();
				ld = new List<dots>();
				ll = new List<lines>();

				http = new HttpClient();

				CouchTimer = new System.Timers.Timer(iCouchRefresh * 1000);
				CouchTimer.Elapsed += OnFuckYoCouch;
				CouchTimer.Enabled = true;

				timer = new System.Timers.Timer(iRefresh * 1000);
				timer.Elapsed += OnTimedEvent;
				timer.Enabled = true;

				FetchData();
				MaxChanges();
			}
			else if (State == State.Terminated)
			{
				if (CouchTimer != null)
				{
					CouchTimer.Enabled = false;
					CouchTimer.Elapsed -= OnFuckYoCouch;
					CouchTimer = null;
				}
				if (timer != null)
				{
					timer.Enabled = false;
					timer.Elapsed -= OnTimedEvent;
					timer = null;
				}
			}
		}

		#endregion

		#region Timed Event / Render

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);

			if (chartControl == null || chartScale == null || bInProgress)
				return;

			if (Greek.IsNullOrEmpty())
				Greek = "none";

			if (bShowStatus)
			{
				try
				{
					int ia = VolGex.IndexOf('.');
					if (ia != -1)
						VolGex = VolGex.Substring(0, ia);
					int iYPos = 130;
					int iXPos = iPos;
					DrawText(iXPos, iYPos, "GexBot " + sVersion + " (" + SubscriptionType.ToString().ToLower() + "/" + nextFull + ")", 16, Brushes.White);
					if (Greek.Equals("none"))
					{
						if (VolGex.Contains("-"))
							DrawText(iXPos, iYPos += 25, "Net GEX: " + VolGex, 15, Brushes.Orange);
						else
							DrawText(iXPos, iYPos += 25, "Net GEX: " + VolGex, 15, Brushes.Lime);
					}
					DrawText(iXPos, iYPos += 23, "Major Positive: " + VolMajPos, 14, Brushes.Lime);
					DrawText(iXPos, iYPos += 23, "Major Negative: " + VolMinNeg, 14, Brushes.Orange);
					if (Greek.Equals("none"))
					{
						DrawText(iXPos, iYPos += 23, "Zero Gamma: " + Vol0Gamma, 14, Brushes.Yellow);
						DrawText(iXPos, iYPos += 23, "Delta Reversal: " + DeltaReversal, 14, Brushes.Yellow);
					}
				}
				catch { }
			}

			if (bShowMaxChange && Greek.Equals("none"))
			{
				foreach (changes ch in lc)
					try
					{
						if (ch.volume > 0)
							DrawText((float)chartControl.ActualWidth - 100,
								(float)chartScale.GetYByValue(ch.price), ch.price + " at " + ch.volume.ToString("F2") + " MM", 16, Brushes.Lime);
						else
							DrawText((float)chartControl.ActualWidth - 100,
							(float)chartScale.GetYByValue(ch.price), ch.price + " at " + ch.volume.ToString("F2") + " MM", 16, Brushes.Orange);
					}
					catch { }
			}

			if (!Greek.Equals("none"))
			{
				foreach (lines l in ll)
					try
					{
						double xStart = chartControl.GetXByBarIndex(ChartBars, ChartBars.FromIndex);
						double yMiddle = chartScale.GetYByValue(l.price);

						double dCall = Math.Abs(l.call);
						dCall = dCall * chartControl.ActualWidth;
						double xCallEnd = xStart + dCall;
						Ellipse eli1 = new Ellipse(new Vector2((float)xCallEnd, (float)yMiddle), GreekdotSize, GreekdotSize);
						RenderTarget.FillEllipse(eli1, Brushes.Lime.ToDxBrush(RenderTarget));

						double dPut = Math.Abs(l.put);
						dPut = dPut * chartControl.ActualWidth;
						double xPutEnd = xStart + dPut;
						Ellipse eli2 = new Ellipse(new Vector2((float)xPutEnd, (float)yMiddle), GreekdotSize, GreekdotSize);
						RenderTarget.FillEllipse(eli2, Brushes.Orange.ToDxBrush(RenderTarget));
					}
					catch { }
			}

			foreach (dots l in ld)
			{
				try
				{
					double finalVol = Math.Abs(l.volume);
					double xStart = chartControl.GetXByBarIndex(ChartBars, ChartBars.FromIndex);
					finalVol = (finalVol * (WidthFactor / 100)) * chartControl.ActualWidth;
					double yMiddle = chartScale.GetYByValue(l.price);
					double xEnd = xStart + finalVol;

					Ellipse eli = new Ellipse(new Vector2((float)xEnd, (float)yMiddle), 3, 3);
					switch (l.i)
					{
						case 1:
							RenderTarget.FillEllipse(eli, Brushes.White.ToDxBrush(RenderTarget));
							break;
						case 2:
							RenderTarget.FillEllipse(eli, Brushes.Lime.ToDxBrush(RenderTarget));
							break;
						case 3:
							RenderTarget.FillEllipse(eli, Brushes.Green.ToDxBrush(RenderTarget));
							break;
						case 4:
							RenderTarget.FillEllipse(eli, Brushes.DarkGreen.ToDxBrush(RenderTarget));
							break;
						case 5:
							RenderTarget.FillEllipse(eli, Brushes.Red.ToDxBrush(RenderTarget));
							break;
					}
				}
				catch { }
			}

			foreach (lines l in ll)
			{
				try
				{
					double finalVol = Math.Abs(l.volume);
					double xStart = chartControl.GetXByBarIndex(ChartBars, ChartBars.FromIndex);
					finalVol = (finalVol * (WidthFactor / 100)) * chartControl.ActualWidth;

					double yMiddle = chartScale.GetYByValue(l.price);
					double xEnd = xStart + finalVol;
					strokeStyleProperties = new StrokeStyleProperties
					{
						DashStyle = dashes
					};

					if (l.volume > 0)
						RenderTarget.DrawLine(new Vector2((float)xStart, (float)yMiddle), new Vector2((float)xEnd, (float)yMiddle), Positive.ToDxBrush(RenderTarget), 2, new StrokeStyle(Core.Globals.D2DFactory, strokeStyleProperties));
					else
						RenderTarget.DrawLine(new Vector2((float)xStart, (float)yMiddle), new Vector2((float)xEnd, (float)yMiddle), Negative.ToDxBrush(RenderTarget), 2, new StrokeStyle(Core.Globals.D2DFactory, strokeStyleProperties));
				}
				catch { }
			}
		}

		private void OnFuckYoCouch(object sender, ElapsedEventArgs e)
		{
			if (isGreekNone)
				MaxChanges();
		}

		private void OnTimedEvent(object sender, ElapsedEventArgs e)
		{
			FetchData();
		}

        private void DrawText(float x, float y, string text, int fontSize, SolidColorBrush br)
        {
            using TextFormat textFormat = new TextFormat(Core.Globals.DirectWriteFactory, "Arial", FontWeight.Normal, FontStyle.Normal, fontSize);
            RenderTarget.DrawText(text, textFormat, new RectangleF(x, y, x + 200, y + 50), br.ToDxBrush(RenderTarget));
        }

        #endregion

        #region Fetch Data

        private void AddAcross(double price, double vol)
		{
			if (lc != null)
			{
				if (lc.Count > 0)
				{
					foreach (changes chh in lc)
						if (price == chh.price)
						{
							vol += chh.volume;
							lc.Remove(chh);
							break;
						}
				}
				changes chr = new changes();
				chr.price = price;
				chr.volume = vol;
				lc.Add(chr);
			}
		}

		private async void MaxChanges()
		{
			try
			{
				lc.Clear();
				//Print("maxChangesUrl: " + maxChangesUrl);
				HttpResponseMessage response = await http.GetAsync(maxChangesUrl);
				response.EnsureSuccessStatusCode();

				string jsonResponse = await response.Content.ReadAsStringAsync();
				JObject jo = JObject.Parse(jsonResponse);

				//Print("response len: " + jsonResponse.Length+", JObject count: " + jo.Count);

				var clientarray = jo["current"].Value<JArray>();
				AddAcross(clientarray[0].Value<double>(), clientarray[1].Value<double>());
				var clientarray0 = jo["one"].Value<JArray>();
				AddAcross(clientarray0[0].Value<double>(), clientarray0[1].Value<double>());
				var clientarray1 = jo["five"].Value<JArray>();
				AddAcross(clientarray1[0].Value<double>(), clientarray1[1].Value<double>());
				var clientarray2 = jo["ten"].Value<JArray>();
				AddAcross(clientarray2[0].Value<double>(), clientarray2[1].Value<double>());
				var clientarray3 = jo["fifteen"].Value<JArray>();
				AddAcross(clientarray3[0].Value<double>(), clientarray3[1].Value<double>());
				var clientarray4 = jo["thirty"].Value<JArray>();
				AddAcross(clientarray4[0].Value<double>(), clientarray4[1].Value<double>());
			}
			catch (Exception ex)
			{
				Print(string.Format("{0}.MaxChanges() - Error fetching data: ", INDICATOR_NAME, ex.Message));
			}
			bInProgress = false;
		}

		private async void FetchData()
		{
			int idx = 0;
			List<lines> llT = new List<lines>();
			List<dots> ldT = new List<dots>();

			// moved to State.Configure
			//if (Greek.IsNullOrEmpty()) Greek = "none";

			if (!isGreekNone)
			{
				SubscriptionType = GexSubscriptionType.State;	// "state";
				nextFull = Greek.Split(' ')[0];
				if (Greek.Contains("1dte"))
					nextFull = "one" + nextFull;
			}

			try
			{
                fetchDataUrl = string.Format("https://api.gexbot.com/{0}/{1}/{2}?key={3}", ticker, SubscriptionType.ToString().ToLower(), nextFull, APIKey);
                Print("fetchDataUrl = " + fetchDataUrl);
                HttpResponseMessage response = await http.GetAsync(fetchDataUrl);
				response.EnsureSuccessStatusCode();
				string jsonResponse = await response.Content.ReadAsStringAsync();
				JObject jo = JObject.Parse(jsonResponse);
				//Print("jsonResponse = " + jsonResponse);

				string sSection = isGreekNone ? "strikes" : "mini_contracts";
                if (isGreekNone)
				{
					VolGex = jo["sum_gex_vol"].Value<string>();
					OIGex = jo["sum_gex_oi"].Value<string>();
					//Print("sum_gex_oi = " + OIGex);
					DeltaReversal = jo["delta_risk_reversal"].Value<string>();
					Spot = jo["spot"].Value<string>();
					Vol0Gamma = jo["zero_gamma"].Value<string>();
					VolMajPos = jo["major_pos_vol"].Value<string>();
					OIMajPos = jo["major_pos_oi"].Value<string>();
					VolMinNeg = jo["major_neg_vol"].Value<string>();
					OIMinNeg = jo["major_neg_oi"].Value<string>();
					//Print("major_pos_vol = " + OIMajPos);
				}
				else
				{
					VolMajPos = jo["major_positive"].Value<string>();
					VolMinNeg = jo["major_negative"].Value<string>();
				}

				var clientarray = jo[sSection].Value<JArray>();
				//Print("clientarray = " + clientarray);
				foreach (JArray item in clientarray)
				{
					if (isGreekNone)
					{
						double price = item[0].ToObject<Double>();
						double volume = item[1].ToObject<Double>();
						double oi = item[2].ToObject<Double>();
						lines line = new lines();
						line.price = price * convFactor;
						line.volume = volume;
						line.oi = oi;
						llT.Add(line);
						var xxx = item[3].Value<JArray>();
						int i = 1;
						foreach (Double qqq in xxx)
						{
							dots dotz = new dots();
							dotz.price = price;
							dotz.volume = qqq;
							dotz.i = i;
							ldT.Add(dotz);
							//Print(price + " = " + qqq);
							i++;
						}
						idx++;
					}
					else
					{
						double price = item[0].ToObject<Double>();
						double call = item[1].ToObject<Double>();
						double put = item[2].ToObject<Double>();
						double sgreek = item[3].ToObject<Double>();
						lines line = new lines();
						line.price = price * convFactor;
						line.volume = sgreek;
						line.call = call;
						line.put = put;
						llT.Add(line);
						//var xxx = item[4].Value<JArray>();
						//int i = 1;
						//foreach (Double qqq in xxx)
						//{
						//    dots dotz = new dots();
						//    dotz.price = price;
						//    dotz.volume = qqq;
						//    dotz.i = i;
						//    ldT.Add(dotz);
						//    //Print(price + " = " + qqq);
						//    i++;
						//}
						idx++;
					}
				}
			}
			catch (Exception ex)
			{
				Print(string.Format("{0}.FetchData() - Error fetching data: ", INDICATOR_NAME, ex.Message));
			}

			bInProgress = true;
			ll = llT;
			ld = ldT;
			bInProgress = false;
		}

		#endregion

		#region TypeConverter

		public class GreekType : TypeConverter
		{
			public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;

			public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => false;

			public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
			{
				return new StandardValuesCollection(new string[] { "none", "delta 0dte", "gamma 0dte", "charm 0dte", "vanna 0dte", "delta 1dte", "gamma 1dte", "charm 1dte", "vanna 1dte" });
			}
		}

		public class ZeroFullType : TypeConverter
		{
			public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;

			public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => false;

			public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
			{
				return new StandardValuesCollection(new string[] { "zero", "full", "one" });
			}
		}

		public class LineLocation : TypeConverter
		{
			public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;

			public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => false;

			public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
			{
				return new StandardValuesCollection(new string[] { "Left", "Right" });
			}
		}

		public class SymbolList : TypeConverter
		{
			public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;

			public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => false;

			public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
			{
				return new StandardValuesCollection(new string[] {
					"SPX","ES_SPX","NDX","NQ_NDX","QQQ","TQQQ","AAPL","TSLA",
					"MSFT","AMZN","NVDA","META","VIX","GOOG","IWM","TLT","GLD","USO" });
			}
		}

		#endregion

		#region Variables

		[Display(Name = "Version", GroupName = GENERAL_GROUP, Order = 0)]
		public string IndicatorVersion { get { return sVersion; } set { } }

		[NinjaScriptProperty]
		[Display(Name = "API Key", GroupName = GENERAL_GROUP, Order = 1)]
		public string APIKey { get; set; }

		[NinjaScriptProperty]
		[TypeConverter(typeof(SymbolList))]
		[Display(Name = "Symbol", GroupName = GENERAL_GROUP, Order = 2)]
		public string ticker { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Subscription Type", GroupName = GENERAL_GROUP, Order = 3)]
		public GexSubscriptionType SubscriptionType { get; set; }

		// FULL for the full aggregation (up to 90 days out)
		// ZERO for only 0dte(or nearest expiry for ticker without any 0dte's)
		// ONE for 1dte (or second nearest expiry for ticker without any 0/1dte's)

		[NinjaScriptProperty]
		[TypeConverter(typeof(ZeroFullType))]
		[Display(Name = "Zero/Full/One", GroupName = GENERAL_GROUP,
			Description = "ONE for 1dte, ZERO for only 0dte, FULL for the full aggregation", Order = 4)]
		public string nextFull { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Standard Dot Size", GroupName = GENERAL_GROUP, Order = 5)]
        public int dotSize { get; set; }

        [NinjaScriptProperty]
		[TypeConverter(typeof(GreekType))]
		[Display(Name = "Greek Type", GroupName = GENERAL_GROUP, Order = 6)]
		public string Greek { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Greek Dot Size", GroupName = GENERAL_GROUP, Order = 7)]
		public int GreekdotSize { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Line Length Ratio", GroupName = GENERAL_GROUP, Order = 8)]
		public double WidthFactor { get; set; }

		//[NinjaScriptProperty]
		//[TypeConverter(typeof(LineLocation))]
		//[Display(Name = "Line Position", GroupName = GENERAL_GROUP, Order = 8)]
		//public string lineLoc { get; set; }

		[Display(Name = "X Position of Status Text", GroupName = GENERAL_GROUP, Order = 9)]
		public int iPos { get; set; }

		[Display(Name = "Offset From Right", GroupName = GENERAL_GROUP, Order = 10)]
		public int rightOffset { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Conversion Factor", GroupName = GENERAL_GROUP, Order = 11)]
		public float convFactor { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Main Refresh (seconds)", GroupName = GENERAL_GROUP, Order = 12)]
		public int iRefresh { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "MaxChange Refresh (seconds)", GroupName = GENERAL_GROUP, Order = 13)]
		public int iCouchRefresh { get; set; }

		[Display(Name = "Show Max Change on Prices", GroupName = GENERAL_GROUP, Order = 14)]
		public bool bShowMaxChange { get; set; }

		[Display(Name = "Show Status Text", GroupName = GENERAL_GROUP, Order = 15)]
		public bool bShowStatus { get; set; }

        [XmlIgnore]
        [Display(Name = "Positive Line Color", GroupName = "Colors", Order = 16)]
        public Brush Positive
        { get; set; }

        [XmlIgnore]
        [Display(Name = "Negative Line Color", GroupName = "Colors", Order = 17)]
        public Brush Negative
        { get; set; }

		#endregion
	}

	public enum GexSubscriptionType
	{
		Classic,
		State
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private GexBot[] cacheGexBot;
		public GexBot GexBot(string aPIKey, string ticker, GexSubscriptionType subscriptionType, string nextFull, int dotSize, string greek, int greekdotSize, double widthFactor, float convFactor, int iRefresh, int iCouchRefresh)
		{
			return GexBot(Input, aPIKey, ticker, subscriptionType, nextFull, dotSize, greek, greekdotSize, widthFactor, convFactor, iRefresh, iCouchRefresh);
		}

		public GexBot GexBot(ISeries<double> input, string aPIKey, string ticker, GexSubscriptionType subscriptionType, string nextFull, int dotSize, string greek, int greekdotSize, double widthFactor, float convFactor, int iRefresh, int iCouchRefresh)
		{
			if (cacheGexBot != null)
				for (int idx = 0; idx < cacheGexBot.Length; idx++)
					if (cacheGexBot[idx] != null && cacheGexBot[idx].APIKey == aPIKey && cacheGexBot[idx].ticker == ticker && cacheGexBot[idx].SubscriptionType == subscriptionType && cacheGexBot[idx].nextFull == nextFull && cacheGexBot[idx].dotSize == dotSize && cacheGexBot[idx].Greek == greek && cacheGexBot[idx].GreekdotSize == greekdotSize && cacheGexBot[idx].WidthFactor == widthFactor && cacheGexBot[idx].convFactor == convFactor && cacheGexBot[idx].iRefresh == iRefresh && cacheGexBot[idx].iCouchRefresh == iCouchRefresh && cacheGexBot[idx].EqualsInput(input))
						return cacheGexBot[idx];
			return CacheIndicator<GexBot>(new GexBot(){ APIKey = aPIKey, ticker = ticker, SubscriptionType = subscriptionType, nextFull = nextFull, dotSize = dotSize, Greek = greek, GreekdotSize = greekdotSize, WidthFactor = widthFactor, convFactor = convFactor, iRefresh = iRefresh, iCouchRefresh = iCouchRefresh }, input, ref cacheGexBot);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.GexBot GexBot(string aPIKey, string ticker, GexSubscriptionType subscriptionType, string nextFull, int dotSize, string greek, int greekdotSize, double widthFactor, float convFactor, int iRefresh, int iCouchRefresh)
		{
			return indicator.GexBot(Input, aPIKey, ticker, subscriptionType, nextFull, dotSize, greek, greekdotSize, widthFactor, convFactor, iRefresh, iCouchRefresh);
		}

		public Indicators.GexBot GexBot(ISeries<double> input , string aPIKey, string ticker, GexSubscriptionType subscriptionType, string nextFull, int dotSize, string greek, int greekdotSize, double widthFactor, float convFactor, int iRefresh, int iCouchRefresh)
		{
			return indicator.GexBot(input, aPIKey, ticker, subscriptionType, nextFull, dotSize, greek, greekdotSize, widthFactor, convFactor, iRefresh, iCouchRefresh);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.GexBot GexBot(string aPIKey, string ticker, GexSubscriptionType subscriptionType, string nextFull, int dotSize, string greek, int greekdotSize, double widthFactor, float convFactor, int iRefresh, int iCouchRefresh)
		{
			return indicator.GexBot(Input, aPIKey, ticker, subscriptionType, nextFull, dotSize, greek, greekdotSize, widthFactor, convFactor, iRefresh, iCouchRefresh);
		}

		public Indicators.GexBot GexBot(ISeries<double> input , string aPIKey, string ticker, GexSubscriptionType subscriptionType, string nextFull, int dotSize, string greek, int greekdotSize, double widthFactor, float convFactor, int iRefresh, int iCouchRefresh)
		{
			return indicator.GexBot(input, aPIKey, ticker, subscriptionType, nextFull, dotSize, greek, greekdotSize, widthFactor, convFactor, iRefresh, iCouchRefresh);
		}
	}
}

#endregion
