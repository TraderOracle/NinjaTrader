#region Using declarations

// Right-click background and choose Remove and Sorty Usings, so don't risk causing
// compiler errors on other's computers.  Rare but has bitten me before.

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


#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	[Gui.CategoryOrder(GENERAL_GROUP, 1)]
	[Gui.CategoryOrder(LINES_GROUP, 2)]
	public class GexBot : Indicator
	{
		#region Constants
		private const string sVersion = "version 2.2";
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
				dotSize = 5;
				bShowMaxChange = true;
				bShowStatus = true;

				PositiveLine = new Stroke(Brushes.Lime, DashStyleHelper.Solid, 2);
				NegativeLine = new Stroke(Brushes.Orange, DashStyleHelper.Solid, 2);
			}
			else if (State == State.Configure)
			{
				exceptionCount = 0;

				if (string.IsNullOrWhiteSpace(Greek)) Greek = "none";
				isGreekNone = Greek.Equals("none");
				//Print("GexBot.OnStateChange -> Greek: " + Greek);

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

				CreateBrushes();

				FetchData();
				MaxChanges();
			}
			else if (State == State.Terminated)
			{
				DisposeBrushes();

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

		// convert Brush to SharpDX Brush and cache for better performance
		private void CreateBrushes()
		{
			if (RenderTarget != null)
			{
				dotBrushes = new SharpDX.Direct2D1.Brush[]
				{
					Brushes.White.ToDxBrush(RenderTarget),
					Brushes.Lime.ToDxBrush(RenderTarget),
					Brushes.Green.ToDxBrush(RenderTarget),
					Brushes.DarkGreen.ToDxBrush(RenderTarget),
					Brushes.Red.ToDxBrush(RenderTarget),
				};
			}
		}

		// Dispose of all cached SharpDX.Direct2D1.Brushes
		private void DisposeBrushes()
		{
			if (dotBrushes != null)
			{
				foreach (var brush in dotBrushes)
				{
					if (brush != null)
					{
						brush.Dispose();
					}
				}
			}
		}

		#endregion


		#region Timed Event / Render

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{

			if (chartControl == null || chartScale == null || bInProgress)
				return;

			if (exceptionCount > EXCEPTION_LIMIT)
				return;

			base.OnRender(chartControl, chartScale);


			// =====================================================
			// Save current AntialiasMode and reset back at the end
			SharpDX.Direct2D1.AntialiasMode oldAntialiasMode = RenderTarget.AntialiasMode;
			RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;
			// =====================================================
			// =====================================================


			try
			{
				// moved to State.Configure
				//if (string.IsNullOrWhiteSpace(Greek)) Greek = "none";

				if (bShowStatus)
				{
					int ia = VolGex.IndexOf('.');
					if (ia != -1)
						VolGex = VolGex.Substring(0, ia);
					int iYPos = 130;
					int iXPos = iPos;

					// use string.Format for performance and preventing string objects created with every concat/+
					DrawText(iXPos, iYPos, string.Format("{0} {1} ({2}/{3})", INDICATOR_NAME, sVersion, SubscriptionType, nextFull), 16, Brushes.White);
					if (isGreekNone)
					{
						iYPos += 25;
						if (VolGex.Contains("-"))
							DrawText(iXPos, iYPos, "Net GEX: " + VolGex, 15, Brushes.Orange);
						else
							DrawText(iXPos, iYPos, "Net GEX: " + VolGex, 15, Brushes.Lime);
					}
					iYPos += 25;
					DrawText(iXPos, iYPos, "Major Positive: " + VolMajPos, 14, Brushes.Lime);
					iYPos += 23;
					DrawText(iXPos, iYPos, "Major Negative: " + VolMinNeg, 14, Brushes.Orange);
					iYPos += 23;
					if (isGreekNone)
					{
						DrawText(iXPos, iYPos, "Zero Gamma: " + Vol0Gamma, 14, Brushes.Yellow);
						iYPos += 23;
						DrawText(iXPos, iYPos, "Delta Reversal: " + DeltaReversal, 14, Brushes.Yellow);
					}
				}

				if (bShowMaxChange && isGreekNone)
				{
					if (lc != null && lc.Count > 0)
					{
						foreach (changes ch in lc)
						{
							if (ch.volume > 0)
								DrawText((float)chartControl.ActualWidth - 100,
									(float)chartScale.GetYByValue(ch.price), ch.price + " at " + ch.volume.ToString("F2") + " MM", 16, Brushes.Lime);
							else
								DrawText((float)chartControl.ActualWidth - 100,
									(float)chartScale.GetYByValue(ch.price), ch.price + " at " + ch.volume.ToString("F2") + " MM", 16, Brushes.Orange);
						}
					}
				}

				// strike      5813.77,
				// call ivol   0.20759955355058616,
				// put ivol    0.19385544133713904,
				// greek       - 112.12473444830518,

				//  -100.45876882490465,
				//	-141.50639139903303,
				//	-3.8148399741158269
				if (PositiveLine != null && PositiveLine.BrushDX != null
					&& NegativeLine != null && NegativeLine.BrushDX != null)
				{
					if (!isGreekNone)
					{
						if (ll != null && ll.Count > 0)
						{
							foreach (lines l in ll)
							{
								double xStart = chartControl.GetXByBarIndex(ChartBars, ChartBars.FromIndex);
								double yMiddle = chartScale.GetYByValue(l.price);

								double dCall = Math.Abs(l.call);
								dCall = dCall * chartControl.ActualWidth;
								double xCallEnd = xStart + dCall;
								SharpDX.Direct2D1.Ellipse eli1 = new SharpDX.Direct2D1.Ellipse(new Vector2((float)xCallEnd, (float)yMiddle), dotSize, dotSize);

								// use brush of positive line...assuming that is desire since colors were same
								RenderTarget.FillEllipse(eli1, PositiveLine.BrushDX);

								double dPut = Math.Abs(l.put);
								dPut = dPut * chartControl.ActualWidth;
								double xPutEnd = xStart + dPut;

								SharpDX.Direct2D1.Ellipse eli2 = new SharpDX.Direct2D1.Ellipse(new Vector2((float)xPutEnd, (float)yMiddle), dotSize, dotSize);

								// use brush of negative line...assuming that is desire since colors were same
								RenderTarget.FillEllipse(eli2, NegativeLine.BrushDX);
							}
						}
					}

					if (ld != null && ld.Count > 0)
					{
						foreach (dots l in ld)
						{
							double finalVol = Math.Abs(l.volume);
							double xStart = chartControl.GetXByBarIndex(ChartBars, ChartBars.FromIndex);
							//if (lineLoc.Equals("Right"))
							//{
							//    xStart = chartControl.ActualWidth + rightOffset;
							//    if (finalVol > 0)
							//        finalVol = finalVol * -1;
							//}
							//else if (lineLoc.Equals("Left"))
							//{
							//if (finalVol < 0)
							//    finalVol = finalVol * -1;
							//}
							finalVol = (finalVol * (WidthFactor / 100)) * chartControl.ActualWidth;
							double yMiddle = chartScale.GetYByValue(l.price);
							double xEnd = xStart + finalVol;

							SharpDX.Direct2D1.Ellipse eli = new SharpDX.Direct2D1.Ellipse(new Vector2((float)xEnd, (float)yMiddle), dotSize, dotSize);

							// used array of cached brushes
							var idx = l.i - 1;
							if (idx >= 0)
							{
								RenderTarget.FillEllipse(eli, dotBrushes[idx]);
							}
						}
					}

					if (ll != null && ll.Count > 0)
					{
						foreach (lines l in ll)
						{
							double finalVol = l.volume;
							double xStart = chartControl.GetXByBarIndex(ChartBars, ChartBars.FromIndex);
							//if (lineLoc.Equals("Right"))
							//{
							//    xStart = chartControl.ActualWidth + rightOffset;
							//    if (finalVol > 0)
							//        finalVol = finalVol * -1;
							//}
							//else if(lineLoc.Equals("Left"))
							//{
							if (finalVol < 0)
								finalVol = finalVol * -1;
							//}

							finalVol = (finalVol * (WidthFactor / 100)) * chartControl.ActualWidth;

							//double xStart = chartControl.GetXByBarIndex(ChartBars, ChartBars.FromIndex) + (chartControl.ActualWidth) - 1000;
							double yMiddle = chartScale.GetYByValue(l.price);
							double xEnd = xStart + finalVol;

							// Change to use Stroke for lines
							var startVector = new Vector2((float)xStart, (float)yMiddle);
							var endVector = new Vector2((float)xEnd, (float)yMiddle);

							Stroke stroke = (l.volume > 0) ? PositiveLine : NegativeLine;
							RenderTarget.DrawLine(startVector, endVector, stroke.BrushDX, stroke.Width, stroke.StrokeStyle);
						}
					}
				}
			}
			#region exception handling
			catch (Exception ex)
			{
				if (exceptionCount < EXCEPTION_LIMIT)
				{
					exceptionCount++;
					string message = string.Format("{0} {1}.OnRender() > EXCEPTION: {2}", Instrument.FullName, INDICATOR_NAME, ex);

					//if (ex.InnerException != null)
					//{
					//	message = string.Format("{0}; inner exception: {1}", message, ex.InnerException.Message);
					//}

					Log(message, Cbi.LogLevel.Error);

					Print("#################");
					Print(string.Format("{0} - {1}", DateTime.Now, message));
					Print("#################");
				}
			}
			finally
			{
				// =====================================================
				// =====================================================
				// Restore AntialiasMode to original setting
				RenderTarget.AntialiasMode = oldAntialiasMode;
				// =====================================================
				// =====================================================
			}
			#endregion
		}

		public override void OnRenderTargetChanged()
		{
			base.OnRenderTargetChanged();

			DisposeBrushes();
			CreateBrushes();

			if (RenderTarget != null)
			{
				if (NegativeLine != null)
					NegativeLine.RenderTarget = RenderTarget;

				if (PositiveLine != null)
					PositiveLine.RenderTarget = RenderTarget;
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

			// Too many different calls for me to spend the time to caches the brushes
			// This will work just fine
			using SharpDX.Direct2D1.Brush brushDX = br.ToDxBrush(RenderTarget); // disposes of brushDX when out of scope
			RenderTarget.DrawText(text, textFormat, new RectangleF(x, y, x + 200, y + 50), brushDX);
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
				//Print("Calling " + "https://api.gexbot.com/" + ticker + "/" + SubscriptionType + "/" + nextFull + "?key=" + APIKey);
				//HttpResponseMessage response = await http.GetAsync("https://api.gexbot.com/" +
				//	ticker + "/" +
				//	SubscriptionType + "/" +
				//	nextFull + "?key=" +
				//	APIKey);

				//Print("stuff: " + stuff);
				//Print("maxChangesUrl: " + fetchDataUrl);

				HttpResponseMessage response = await http.GetAsync(fetchDataUrl);
				response.EnsureSuccessStatusCode();
				string jsonResponse = await response.Content.ReadAsStringAsync();
				JObject jo = JObject.Parse(jsonResponse);
				//Print("jsonResponse = " + jsonResponse);
				//Print("response len: " + jsonResponse.Length + ", JObject count: " + jo.Count);

				string sSection = "strikes";
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
					sSection = "mini_contracts";
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
		[TypeConverter(typeof(GreekType))]
		[Display(Name = "Greek Type", GroupName = GENERAL_GROUP, Order = 5)]
		public string Greek { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Greek Dot Size", GroupName = GENERAL_GROUP, Order = 6)]
		public int dotSize { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Line Length Ratio", GroupName = LINES_GROUP, Order = 7)]
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


		// LINES_GROUP

		[Display(Name = "Positive Line", GroupName = LINES_GROUP, Order = 2)]
		public Stroke PositiveLine
		{ get; set; }

		[Display(Name = "Negative Line", GroupName = LINES_GROUP, Order = 3)]
		public Stroke NegativeLine
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
		public GexBot GexBot(string aPIKey, string ticker, GexSubscriptionType subscriptionType, string nextFull, string greek, int dotSize, double widthFactor, float convFactor, int iRefresh, int iCouchRefresh)
		{
			return GexBot(Input, aPIKey, ticker, subscriptionType, nextFull, greek, dotSize, widthFactor, convFactor, iRefresh, iCouchRefresh);
		}

		public GexBot GexBot(ISeries<double> input, string aPIKey, string ticker, GexSubscriptionType subscriptionType, string nextFull, string greek, int dotSize, double widthFactor, float convFactor, int iRefresh, int iCouchRefresh)
		{
			if (cacheGexBot != null)
				for (int idx = 0; idx < cacheGexBot.Length; idx++)
					if (cacheGexBot[idx] != null && cacheGexBot[idx].APIKey == aPIKey && cacheGexBot[idx].ticker == ticker && cacheGexBot[idx].SubscriptionType == subscriptionType && cacheGexBot[idx].nextFull == nextFull && cacheGexBot[idx].Greek == greek && cacheGexBot[idx].dotSize == dotSize && cacheGexBot[idx].WidthFactor == widthFactor && cacheGexBot[idx].convFactor == convFactor && cacheGexBot[idx].iRefresh == iRefresh && cacheGexBot[idx].iCouchRefresh == iCouchRefresh && cacheGexBot[idx].EqualsInput(input))
						return cacheGexBot[idx];
			return CacheIndicator<GexBot>(new GexBot(){ APIKey = aPIKey, ticker = ticker, SubscriptionType = subscriptionType, nextFull = nextFull, Greek = greek, dotSize = dotSize, WidthFactor = widthFactor, convFactor = convFactor, iRefresh = iRefresh, iCouchRefresh = iCouchRefresh }, input, ref cacheGexBot);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.GexBot GexBot(string aPIKey, string ticker, GexSubscriptionType subscriptionType, string nextFull, string greek, int dotSize, double widthFactor, float convFactor, int iRefresh, int iCouchRefresh)
		{
			return indicator.GexBot(Input, aPIKey, ticker, subscriptionType, nextFull, greek, dotSize, widthFactor, convFactor, iRefresh, iCouchRefresh);
		}

		public Indicators.GexBot GexBot(ISeries<double> input , string aPIKey, string ticker, GexSubscriptionType subscriptionType, string nextFull, string greek, int dotSize, double widthFactor, float convFactor, int iRefresh, int iCouchRefresh)
		{
			return indicator.GexBot(input, aPIKey, ticker, subscriptionType, nextFull, greek, dotSize, widthFactor, convFactor, iRefresh, iCouchRefresh);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.GexBot GexBot(string aPIKey, string ticker, GexSubscriptionType subscriptionType, string nextFull, string greek, int dotSize, double widthFactor, float convFactor, int iRefresh, int iCouchRefresh)
		{
			return indicator.GexBot(Input, aPIKey, ticker, subscriptionType, nextFull, greek, dotSize, widthFactor, convFactor, iRefresh, iCouchRefresh);
		}

		public Indicators.GexBot GexBot(ISeries<double> input , string aPIKey, string ticker, GexSubscriptionType subscriptionType, string nextFull, string greek, int dotSize, double widthFactor, float convFactor, int iRefresh, int iCouchRefresh)
		{
			return indicator.GexBot(input, aPIKey, ticker, subscriptionType, nextFull, greek, dotSize, widthFactor, convFactor, iRefresh, iCouchRefresh);
		}
	}
}

#endregion
