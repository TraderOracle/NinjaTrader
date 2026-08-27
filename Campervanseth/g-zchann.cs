// Rolling Z-Score Channel — NinjaTrader 8
// Parity with RollingZScoreChannel.cpp (Sierra Chart port of the Pine v6 study).
//
// Install: copy this file to
//   Documents\NinjaTrader 8\bin\Custom\Indicators\
// then New > NinjaScript Editor > compile (F5), or Tools > Compile.
//
// Default Calculate is OnPriceChange: last-bar bands track live trades.
// OnEachTick is not more accurate for a price-based z-score (same last print
// yields the same value). Alerts fire on the first tick of a new bar using
// the bar that just closed.
//
// Percentile: Hyndman-Fan type 7 / Excel PERCENTILE.INC / Pine
//   ta.percentile_linear_interpolation.
// Stdev default: sample (n-1), matching Pine ta.stdev(src, len, false).
// IIR smoothers seed missing history as 0, matching Pine nz().

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public enum ZChBandMode
	{
		Adaptive,
		FixedZScore
	}

	public enum ZChSmoothType
	{
		LinReg,
		HullMA,
		SuperSmoother,
		TwoPoleGaussian
	}

	public enum ZChStdevMethod
	{
		SamplePineMatch,
		Population
	}

	public class RollingZScoreChannel : Indicator
	{
		private const int PercentileMaxLength = 500;
		private const double Pi = 3.14159265358979323846;
		private const double Sqrt2 = 1.41421356237309504880;
		private const double Eps = 1e-10;

		private Series<double> zRaw;
		private Series<double> zScore;
		private Series<double> zUpRaw;
		private Series<double> zDnRaw;
		private Series<double> zUp;
		private Series<double> zDn;
		private Series<double> locationSeries;
		private Series<double> smoothZ;
		private Series<double> smoothU;
		private Series<double> smoothD;

		private readonly Dictionary<int, Brush> brushCache = new Dictionary<int, Brush>();
		private readonly double[] pctBuf = new double[PercentileMaxLength];
		private Brush bearFillBrush;
		private Brush bullFillBrush;
		private Brush bearBgBrush;
		private Brush bullBgBrush;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"Adaptive rolling z-score channel. Sample stdev matches Pine ta.stdev(..., false). Re-entry markers require a prior close outside the band by default.";
				Name = "Rolling Z-Score Channel";
				Calculate = Calculate.OnPriceChange;
				IsOverlay = true;
				DisplayInDataBox = true;
				DrawOnPricePanel = true;
				PaintPriceMarkers = true;
				IsSuspendedWhileInactive = true;
				ScaleJustification = ScaleJustification.Right;
				MaximumBarsLookBack = MaximumBarsLookBack.Infinite;

				RollingWindow = 80;
				BandMode = ZChBandMode.Adaptive;
				FixedUpperZ = 2.0;
				FixedLowerZ = -2.0;
				ForceSymmetric = false;
				MinBandZ = 0.5;
				ShowInner = true;
				InnerFraction = 0.5;
				ShowBasis = true;
				UpperPercentile = 95.0;
				LowerPercentile = 5.0;
				PercentileLengthShort = 50;
				PercentileLengthMedium = 100;
				PercentileLengthLong = 200;
				PercentileOffset = 0;
				SmoothZScore = false;
				SmoothBandLevels = false;
				SmoothType = ZChSmoothType.TwoPoleGaussian;
				SmoothLength = 5;
				StdevMethod = ZChStdevMethod.SamplePineMatch;
				ReentryRequiresOutside = true;
				ShowSignals = true;
				ColorBars = false;
				ShowChannelFill = true;
				BasisGradient = true;
				BackgroundTintOnExtremes = false;
				FillOpacity = 30;

				Brush bear = FrozenRgb(255, 20, 147);
				Brush bull = FrozenRgb(57, 255, 20);
				Brush neu = FrozenRgb(120, 120, 200);

				AddPlot(new Stroke(bear, 2), PlotStyle.Line, "Upper Band");
				AddPlot(new Stroke(bull, 2), PlotStyle.Line, "Lower Band");
				AddPlot(new Stroke(neu, 2), PlotStyle.Line, "Basis");
				AddPlot(new Stroke(bear, 1), PlotStyle.Line, "Inner Upper");
				AddPlot(new Stroke(bull, 1), PlotStyle.Line, "Inner Lower");
				AddPlot(new Stroke(bull, 2), PlotStyle.TriangleUp, "Re-Entry Up");
				AddPlot(new Stroke(bear, 2), PlotStyle.TriangleDown, "Re-Entry Down");
			}
			else if (State == State.Configure)
			{
				BarsRequiredToPlot = BarsNeeded();
			}
			else if (State == State.DataLoaded)
			{
				zRaw = new Series<double>(this, MaximumBarsLookBack.Infinite);
				zScore = new Series<double>(this, MaximumBarsLookBack.Infinite);
				zUpRaw = new Series<double>(this, MaximumBarsLookBack.Infinite);
				zDnRaw = new Series<double>(this, MaximumBarsLookBack.Infinite);
				zUp = new Series<double>(this, MaximumBarsLookBack.Infinite);
				zDn = new Series<double>(this, MaximumBarsLookBack.Infinite);
				locationSeries = new Series<double>(this, MaximumBarsLookBack.Infinite);
				smoothZ = new Series<double>(this, MaximumBarsLookBack.Infinite);
				smoothU = new Series<double>(this, MaximumBarsLookBack.Infinite);
				smoothD = new Series<double>(this, MaximumBarsLookBack.Infinite);

				Color bearC = PlotSolidColor(0, Color.FromRgb(255, 20, 147));
				Color bullC = PlotSolidColor(1, Color.FromRgb(57, 255, 20));
				bearFillBrush = FrozenRgb(bearC.R, bearC.G, bearC.B);
				bullFillBrush = FrozenRgb(bullC.R, bullC.G, bullC.B);
				bearBgBrush = FrozenArgb(40, bearC.R, bearC.G, bearC.B);
				bullBgBrush = FrozenArgb(40, bullC.R, bullC.G, bullC.B);
			}
		}

		protected override void OnBarUpdate()
		{
			int roll = ClampInt(RollingWindow, 10, 500);
			int pctS = ClampInt(PercentileLengthShort, 10, PercentileMaxLength);
			int pctM = ClampInt(PercentileLengthMedium, 10, PercentileMaxLength);
			int pctL = ClampInt(PercentileLengthLong, 10, PercentileMaxLength);
			int pctOff = ClampInt(PercentileOffset, 0, 1);
			int smLen = ClampInt(SmoothLength, 2, 50);
			double minZ = MinBandZ < 0.1 ? 0.1 : MinBandZ;
			double innerFrac = InnerFraction;
			if (innerFrac < 0.1) innerFrac = 0.1;
			if (innerFrac > 0.9) innerFrac = 0.9;

			int need = BarsNeeded(roll, pctS, pctM, pctL, pctOff, smLen);
			if (CurrentBar < need)
				BarsRequiredToPlot = need;

			ClearBar();

			if (CurrentBar < roll - 1)
				return;

			double location = SMA(Input, roll)[0];
			double dispersion = StdevWindow(roll, StdevMethod == ZChStdevMethod.SamplePineMatch);
			locationSeries[0] = location;

			double zr = 0.0;
			if (dispersion > Eps)
				zr = (Input[0] - location) / dispersion;
			zRaw[0] = zr;

			double zs = zr;
			if (SmoothZScore)
			{
				zs = ApplySmooth(zRaw, smoothZ, smLen, SmoothType);
				smoothZ[0] = zs;
			}
			zScore[0] = zs;

			double zUpSel = 0.0;
			double zDnSel = 0.0;
			bool bandsValid = false;

			if (BandMode == ZChBandMode.FixedZScore)
			{
				zUpSel = FixedUpperZ;
				zDnSel = FixedLowerZ;
				bandsValid = true;
			}
			else
			{
				double upS = 0.0, upM = 0.0, upL = 0.0, dnS = 0.0, dnM = 0.0, dnL = 0.0;
				int firstValidZ = roll - 1;
				bool ok =
					PercentileLinear(zScore, pctS, pctOff, firstValidZ, UpperPercentile, out upS)
					&& PercentileLinear(zScore, pctM, pctOff, firstValidZ, UpperPercentile, out upM)
					&& PercentileLinear(zScore, pctL, pctOff, firstValidZ, UpperPercentile, out upL)
					&& PercentileLinear(zScore, pctS, pctOff, firstValidZ, LowerPercentile, out dnS)
					&& PercentileLinear(zScore, pctM, pctOff, firstValidZ, LowerPercentile, out dnM)
					&& PercentileLinear(zScore, pctL, pctOff, firstValidZ, LowerPercentile, out dnL);
				if (ok)
				{
					zUpSel = (upS + upM + upL) / 3.0;
					zDnSel = (dnS + dnM + dnL) / 3.0;
					bandsValid = true;
				}
			}

			if (!bandsValid)
				return;

			double zUpFlr = zUpSel < minZ ? minZ : zUpSel;
			double zDnFlr = zDnSel > -minZ ? -minZ : zDnSel;
			zUpRaw[0] = zUpFlr;
			zDnRaw[0] = zDnFlr;

			double zUpPre = zUpFlr;
			double zDnPre = zDnFlr;
			if (SmoothBandLevels)
			{
				zUpPre = ApplySmooth(zUpRaw, smoothU, smLen, SmoothType);
				zDnPre = ApplySmooth(zDnRaw, smoothD, smLen, SmoothType);
				smoothU[0] = zUpPre;
				smoothD[0] = zDnPre;
			}

			double zU = zUpPre;
			double zD = zDnPre;
			if (ForceSymmetric)
			{
				double mag = Math.Abs(zUpPre);
				double magDn = Math.Abs(zDnPre);
				if (magDn > mag)
					mag = magDn;
				zU = mag;
				zD = -mag;
			}

			zUp[0] = zU;
			zDn[0] = zD;

			double upper = location + zU * dispersion;
			double lower = location + zD * dispersion;
			double innerUp = location + zU * innerFrac * dispersion;
			double innerDn = location + zD * innerFrac * dispersion;

			Upper[0] = upper;
			Lower[0] = lower;
			if (ShowBasis)
				Basis[0] = location;
			if (ShowInner)
			{
				InnerUpper[0] = innerUp;
				InnerLower[0] = innerDn;
			}

			if (ShowChannelFill)
			{
				int opacity = ClampInt(FillOpacity, 0, 100);
				Draw.Region(this, "ZChUpperFill", CurrentBar, 0, Upper, locationSeries, null, bearFillBrush, opacity);
				Draw.Region(this, "ZChLowerFill", CurrentBar, 0, locationSeries, Lower, null, bullFillBrush, opacity);
			}
			else
			{
				RemoveDrawObject("ZChUpperFill");
				RemoveDrawObject("ZChLowerFill");
			}

			double srcVal = Input[0];
			bool inOb = srcVal > upper;
			bool inOs = srcVal < lower;

			if (BasisGradient && ShowBasis)
			{
				Color neuC = PlotSolidColor(2, Color.FromRgb(120, 120, 200));
				Color bearC = PlotSolidColor(0, Color.FromRgb(255, 20, 147));
				Color bullC = PlotSolidColor(1, Color.FromRgb(57, 255, 20));
				Brush grad;
				if (zs >= 0.0)
				{
					double top = zU > 0.01 ? zU : 0.01;
					grad = LerpBrush(neuC, bearC, zs / top);
				}
				else
				{
					double bot = zD < -0.01 ? zD : -0.01;
					grad = LerpBrush(bullC, neuC, (zs - bot) / (0.0 - bot));
				}
				PlotBrushes[2][0] = grad;
			}

			if (ColorBars)
			{
				Brush barC = Plots[2].Brush;
				if (BasisGradient && PlotBrushes[2][0] != null)
					barC = PlotBrushes[2][0];
				if (inOb)
					barC = Plots[0].Brush;
				else if (inOs)
					barC = Plots[1].Brush;
				BarBrush = barC;
				CandleOutlineBrush = barC;
			}

			if (BackgroundTintOnExtremes)
			{
				if (inOb)
					BackBrushes[0] = bearBgBrush;
				else if (inOs)
					BackBrushes[0] = bullBgBrush;
				else
					BackBrushes[0] = null;
			}

			bool reentryUp = false;
			bool reentryDn = false;
			if (CurrentBar >= 1 && !double.IsNaN(Lower[1]) && !double.IsNaN(Upper[1]))
			{
				double srcPrev = Input[1];
				double loPrev = Lower[1];
				double upPrev = Upper[1];

				if (!double.IsNaN(loPrev))
				{
					if (ReentryRequiresOutside)
						reentryUp = srcPrev < loPrev && srcVal > lower;
					else
						reentryUp = srcPrev <= loPrev && srcVal > lower;
				}
				if (!double.IsNaN(upPrev))
				{
					if (ReentryRequiresOutside)
						reentryDn = srcPrev > upPrev && srcVal < upper;
					else
						reentryDn = srcPrev >= upPrev && srcVal < upper;
				}
			}

			if (ShowSignals)
			{
				if (reentryUp)
					ReEntryUp[0] = Low[0];
				if (reentryDn)
					ReEntryDown[0] = High[0];
			}

			FireClosedBarAlerts();
		}

		private void ClearBar()
		{
			Upper[0] = double.NaN;
			Lower[0] = double.NaN;
			Basis[0] = double.NaN;
			InnerUpper[0] = double.NaN;
			InnerLower[0] = double.NaN;
			ReEntryUp[0] = double.NaN;
			ReEntryDown[0] = double.NaN;
			zRaw[0] = 0.0;
			zScore[0] = 0.0;
			zUpRaw[0] = double.NaN;
			zDnRaw[0] = double.NaN;
			zUp[0] = double.NaN;
			zDn[0] = double.NaN;
			locationSeries[0] = double.NaN;
			smoothZ[0] = 0.0;
			smoothU[0] = 0.0;
			smoothD[0] = 0.0;
		}

		private void FireClosedBarAlerts()
		{
			if (State != State.Realtime || !IsFirstTickOfBar || CurrentBar < 2)
				return;
			if (double.IsNaN(Upper[1]) || double.IsNaN(Lower[1]) || double.IsNaN(Upper[2]) || double.IsNaN(Lower[2]))
				return;

			double src1 = Input[1];
			double src2 = Input[2];
			double up1 = Upper[1];
			double up2 = Upper[2];
			double lo1 = Lower[1];
			double lo2 = Lower[2];

			bool reUp;
			bool reDn;
			if (ReentryRequiresOutside)
			{
				reUp = src2 < lo2 && src1 > lo1;
				reDn = src2 > up2 && src1 < up1;
			}
			else
			{
				reUp = src2 <= lo2 && src1 > lo1;
				reDn = src2 >= up2 && src1 < up1;
			}

			bool crossUpUpper = src2 < up2 && src1 > up1;
			bool crossDnLower = src2 > lo2 && src1 < lo1;

			if (crossUpUpper)
				Alert("ZChBreakUp", Priority.Medium, "Break Above Upper", string.Empty, 1, Brushes.White, Plots[0].Brush);
			if (crossDnLower)
				Alert("ZChBreakDn", Priority.Medium, "Break Below Lower", string.Empty, 1, Brushes.White, Plots[1].Brush);
			if (reDn)
				Alert("ZChReFromAbove", Priority.Medium, "Re-Entry From Above", string.Empty, 1, Brushes.White, Plots[0].Brush);
			if (reUp)
				Alert("ZChReFromBelow", Priority.Medium, "Re-Entry From Below", string.Empty, 1, Brushes.White, Plots[1].Brush);

			if (ShowBasis && !double.IsNaN(Basis[1]) && !double.IsNaN(Basis[2]))
			{
				double b1 = Basis[1];
				double b2 = Basis[2];
				if (src2 < b2 && src1 > b1)
					Alert("ZChBasisUp", Priority.Medium, "Basis Cross Up", string.Empty, 1, Brushes.White, Plots[2].Brush);
				if (src2 > b2 && src1 < b1)
					Alert("ZChBasisDn", Priority.Medium, "Basis Cross Down", string.Empty, 1, Brushes.White, Plots[2].Brush);
			}
		}

		private int BarsNeeded()
		{
			return BarsNeeded(
				ClampInt(RollingWindow, 10, 500),
				ClampInt(PercentileLengthShort, 10, PercentileMaxLength),
				ClampInt(PercentileLengthMedium, 10, PercentileMaxLength),
				ClampInt(PercentileLengthLong, 10, PercentileMaxLength),
				ClampInt(PercentileOffset, 0, 1),
				ClampInt(SmoothLength, 2, 50));
		}

		private int BarsNeeded(int roll, int pctS, int pctM, int pctL, int pctOff, int smLen)
		{
			int dataStart = roll - 1;
			if (BandMode == ZChBandMode.Adaptive)
			{
				int pctMax = pctS;
				if (pctM > pctMax) pctMax = pctM;
				if (pctL > pctMax) pctMax = pctL;
				dataStart = roll + pctMax + pctOff - 2;
			}
			if ((SmoothZScore || SmoothBandLevels) && dataStart < roll + smLen - 2)
				dataStart = roll + smLen - 2;
			if (dataStart < 0)
				dataStart = 0;
			return dataStart;
		}

		private double StdevWindow(int length, bool sample)
		{
			if (length < 1)
				return 0.0;
			if (sample && length < 2)
				return 0.0;
			if (CurrentBar < length - 1)
				return 0.0;

			double sum = 0.0;
			for (int i = 0; i < length; i++)
				sum += Input[i];
			double mean = sum / length;

			double sq = 0.0;
			for (int i = 0; i < length; i++)
			{
				double d = Input[i] - mean;
				sq += d * d;
			}

			double denom = sample ? (length - 1.0) : length;
			if (denom <= 0.0)
				return 0.0;
			return Math.Sqrt(sq / denom);
		}

		private bool PercentileLinear(Series<double> data, int length, int offset, int minValidIndex, double percentage, out double result)
		{
			result = 0.0;
			if (length < 1 || length > PercentileMaxLength || offset < 0)
				return false;

			// barsAgo window [offset, offset + length - 1] must sit on valid z bars.
			// CurrentBar - (offset + length - 1) is the oldest absolute index.
			int oldest = CurrentBar - (offset + length - 1);
			if (oldest < minValidIndex)
				return false;

			for (int i = 0; i < length; i++)
				pctBuf[i] = data[offset + i];
			Array.Sort(pctBuf, 0, length);

			if (percentage < 0.0) percentage = 0.0;
			if (percentage > 100.0) percentage = 100.0;

			double pos = (percentage / 100.0) * (length - 1);
			int lo = (int)pos;
			if (lo < 0) lo = 0;
			if (lo >= length) lo = length - 1;
			int hi = lo + 1;
			if (hi >= length) hi = length - 1;
			double frac = pos - lo;
			result = pctBuf[lo] + (pctBuf[hi] - pctBuf[lo]) * frac;
			return true;
		}

		private double ApplySmooth(Series<double> src, Series<double> iirOut, int length, ZChSmoothType type)
		{
			if (length < 2)
				length = 2;

			if (type == ZChSmoothType.LinReg)
				return LinReg(src, length)[0];
			if (type == ZChSmoothType.HullMA)
				return HMA(src, length)[0];
			if (type == ZChSmoothType.SuperSmoother)
				return SuperSmootherAt(src, iirOut, length);
			return Gaussian2PoleAt(src, iirOut, length);
		}

		private double SuperSmootherAt(Series<double> src, Series<double> y, int length)
		{
			if (length < 1)
				length = 1;
			double a1 = Math.Exp(-Sqrt2 * Pi / length);
			double b1 = 2.0 * a1 * Math.Cos(Sqrt2 * Pi / length);
			double c2 = b1;
			double c3 = -a1 * a1;
			double c1 = 1.0 - c2 - c3;
			double x0 = src[0];
			double x1 = CurrentBar >= 1 ? src[1] : 0.0;
			double y1 = CurrentBar >= 1 ? y[1] : 0.0;
			double y2 = CurrentBar >= 2 ? y[2] : 0.0;
			return c1 * (x0 + x1) * 0.5 + c2 * y1 + c3 * y2;
		}

		private double Gaussian2PoleAt(Series<double> src, Series<double> y, int length)
		{
			if (length < 1)
				length = 1;
			double beta = (1.0 - Math.Cos(2.0 * Pi / length)) / (Sqrt2 - 1.0);
			double alpha = -beta + Math.Sqrt(beta * beta + 2.0 * beta);
			double a2 = alpha * alpha;
			double om = 1.0 - alpha;
			double om2 = om * om;
			double x0 = src[0];
			double y1 = CurrentBar >= 1 ? y[1] : 0.0;
			double y2 = CurrentBar >= 2 ? y[2] : 0.0;
			return a2 * x0 + 2.0 * om * y1 - om2 * y2;
		}

		private static int ClampInt(int v, int lo, int hi)
		{
			if (v < lo) return lo;
			if (v > hi) return hi;
			return v;
		}

		private Color PlotSolidColor(int plotIndex, Color fallback)
		{
			SolidColorBrush sb = Plots[plotIndex].Brush as SolidColorBrush;
			if (sb != null)
				return sb.Color;
			return fallback;
		}

		private Brush LerpBrush(Color a, Color b, double t)
		{
			if (t < 0.0) t = 0.0;
			if (t > 1.0) t = 1.0;
			byte r = (byte)(a.R + (b.R - a.R) * t + 0.5);
			byte g = (byte)(a.G + (b.G - a.G) * t + 0.5);
			byte bl = (byte)(a.B + (b.B - a.B) * t + 0.5);
			return FrozenRgb(r, g, bl);
		}

		private Brush FrozenRgb(byte r, byte g, byte b)
		{
			int key = (r << 16) | (g << 8) | b;
			Brush br;
			if (brushCache.TryGetValue(key, out br))
				return br;
			br = new SolidColorBrush(Color.FromRgb(r, g, b));
			br.Freeze();
			brushCache[key] = br;
			return br;
		}

		private Brush FrozenArgb(byte a, byte r, byte g, byte b)
		{
			int key = (a << 24) | (r << 16) | (g << 8) | b;
			Brush br;
			if (brushCache.TryGetValue(key, out br))
				return br;
			br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
			br.Freeze();
			brushCache[key] = br;
			return br;
		}

		#region Plots
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> Upper { get { return Values[0]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> Lower { get { return Values[1]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> Basis { get { return Values[2]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> InnerUpper { get { return Values[3]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> InnerLower { get { return Values[4]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ReEntryUp { get { return Values[5]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ReEntryDown { get { return Values[6]; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ZScore { get { return zScore; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> UpperZ { get { return zUp; } }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> LowerZ { get { return zDn; } }
		#endregion

		#region Properties
		[NinjaScriptProperty]
		[Range(10, 500)]
		[Display(Name = "Rolling Window", Order = 1, GroupName = "Parameters")]
		public int RollingWindow { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Band Mode", Order = 2, GroupName = "Parameters")]
		public ZChBandMode BandMode { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, 6.0)]
		[Display(Name = "Fixed Upper Z", Order = 3, GroupName = "Parameters")]
		public double FixedUpperZ { get; set; }

		[NinjaScriptProperty]
		[Range(-6.0, -0.1)]
		[Display(Name = "Fixed Lower Z", Order = 4, GroupName = "Parameters")]
		public double FixedLowerZ { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Force Symmetric Bands", Order = 5, GroupName = "Parameters")]
		public bool ForceSymmetric { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, 3.0)]
		[Display(Name = "Minimum Band Z", Order = 6, GroupName = "Parameters")]
		public double MinBandZ { get; set; }

		[NinjaScriptProperty]
		[Range(50.0, 99.0)]
		[Display(Name = "Upper Percentile", Order = 1, GroupName = "Percentiles")]
		public double UpperPercentile { get; set; }

		[NinjaScriptProperty]
		[Range(1.0, 50.0)]
		[Display(Name = "Lower Percentile", Order = 2, GroupName = "Percentiles")]
		public double LowerPercentile { get; set; }

		[NinjaScriptProperty]
		[Range(10, 500)]
		[Display(Name = "Percentile Length Short", Order = 3, GroupName = "Percentiles")]
		public int PercentileLengthShort { get; set; }

		[NinjaScriptProperty]
		[Range(10, 500)]
		[Display(Name = "Percentile Length Medium", Order = 4, GroupName = "Percentiles")]
		public int PercentileLengthMedium { get; set; }

		[NinjaScriptProperty]
		[Range(10, 500)]
		[Display(Name = "Percentile Length Long", Order = 5, GroupName = "Percentiles")]
		public int PercentileLengthLong { get; set; }

		[NinjaScriptProperty]
		[Range(0, 1)]
		[Display(Name = "Percentile Offset", Description = "0 = include current bar (Pine). 1 = use z through previous bar.", Order = 6, GroupName = "Percentiles")]
		public int PercentileOffset { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Smooth Z-Score", Order = 1, GroupName = "Smoothing")]
		public bool SmoothZScore { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Smooth Band Levels", Order = 2, GroupName = "Smoothing")]
		public bool SmoothBandLevels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Smoothing Type", Order = 3, GroupName = "Smoothing")]
		public ZChSmoothType SmoothType { get; set; }

		[NinjaScriptProperty]
		[Range(2, 50)]
		[Display(Name = "Smoothing Length", Order = 4, GroupName = "Smoothing")]
		public int SmoothLength { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stdev Method", Order = 5, GroupName = "Smoothing")]
		public ZChStdevMethod StdevMethod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Inner Bands", Order = 1, GroupName = "Visuals")]
		public bool ShowInner { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, 0.9)]
		[Display(Name = "Inner Band Fraction", Order = 2, GroupName = "Visuals")]
		public double InnerFraction { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Basis Line", Order = 3, GroupName = "Visuals")]
		public bool ShowBasis { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Channel Fill", Order = 4, GroupName = "Visuals")]
		public bool ShowChannelFill { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Fill Opacity", Description = "NinjaTrader opacity 0-100 (higher is more opaque). 30 ≈ Sierra transparency 70.", Order = 5, GroupName = "Visuals")]
		public int FillOpacity { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Basis Gradient Coloring", Order = 6, GroupName = "Visuals")]
		public bool BasisGradient { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Background Tint on Extremes", Order = 7, GroupName = "Visuals")]
		public bool BackgroundTintOnExtremes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Color Bars", Order = 8, GroupName = "Visuals")]
		public bool ColorBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Re-Entry Requires Outside", Order = 1, GroupName = "Signals")]
		public bool ReentryRequiresOutside { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Plot Re-Entry Markers", Order = 2, GroupName = "Signals")]
		public bool ShowSignals { get; set; }
		#endregion
	}
}
