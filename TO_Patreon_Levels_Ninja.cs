// =============================================================================
//  PatreonLevels.cs  -  NinjaTrader 8 indicator
//
//  Reads "PatreonLevels.txt" and shades every top/bottom zone for the chart's
//  instrument as a band that extends across the whole chart.
//
//  WHERE TO PUT THE FILE
//      Documents\NinjaTrader 8\PatreonLevels.txt
//      (the same folder that holds "bin", "templates", "db" ...)
//      A full path can also be typed into the "Levels file" input.
//
//  FILE FORMAT (one or more blocks, any number of lines)
//      ** $ES1!: **top,7820.84,bottom,7816.76,top,7786.07,bottom,7781.99,...
//      ** $GC1!: **top,4012.5,bottom,4008.1,...
//
//  - Spaces and '*' are ignored.
//  - "$ES1!:" -> ticker "ES", matched against the chart's master instrument
//    (ES 12-26 -> "ES"; MES does not match ES).
//  - A dangling "top"/"bottom" with no value is ignored.
//  - The file is re-checked every few seconds; zones update when it changes.
// =============================================================================

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class PatreonLevels : Indicator
    {
        private class Zone
        {
            public double Top;
            public double Bottom;
        }

        private readonly object        zoneLock = new object();
        private List<Zone>             zones    = new List<Zone>();
        private System.Threading.Timer reloadTimer;
        private int                    timerBusy;
        private string                 lastContent;
        private string                 matchRoot;
        private bool                   missingLogged;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Shades top/bottom zones from PatreonLevels.txt (Documents\\NinjaTrader 8) for the chart's instrument.";
                Name                     = "PatreonLevels";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = true;
                DisplayInDataBox         = false;
                DrawOnPricePanel         = true;
                PaintPriceMarkers        = false;
                IsSuspendedWhileInactive = false;
                ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;

                LevelsFile        = "PatreonLevels.txt";
                ZoneFill          = Brushes.DimGray;
                ZoneOpacity       = 15;
                DrawBorder        = false;
                ZoneBorder        = Brushes.DimGray;
                ZoneBorderOpacity = 40;
                ZoneBorderWidth   = 1;
                DrawBehindPrice   = true;
                ReloadSeconds     = 5;
                SymbolOverride    = string.Empty;
            }
            else if (State == State.Historical)
            {
                if (DrawBehindPrice)
                    SetZOrder(-1);
            }
            else if (State == State.DataLoaded)
            {
                string over = (SymbolOverride ?? string.Empty).Trim();
                matchRoot = over.Length > 0
                    ? over.ToUpperInvariant()
                    : (Instrument != null ? Instrument.MasterInstrument.Name.ToUpperInvariant() : string.Empty);

                LoadFile(true);

                int ms = Math.Max(1, ReloadSeconds) * 1000;
                reloadTimer = new System.Threading.Timer(OnReloadTimer, null, ms, ms);
            }
            else if (State == State.Terminated)
            {
                if (reloadTimer != null)
                {
                    reloadTimer.Dispose();
                    reloadTimer = null;
                }
            }
        }

        protected override void OnBarUpdate()
        {
            // Nothing per bar - zones are drawn in OnRender.
        }

        // ---------------------------------------------------------------- file

        private string ResolvePath()
        {
            string f = (LevelsFile ?? string.Empty).Trim().Trim('"');
            if (f.Length == 0)
                f = "PatreonLevels.txt";
            return Path.IsPathRooted(f) ? f : Path.Combine(NinjaTrader.Core.Globals.UserDataDir, f);
        }

        private void OnReloadTimer(object state)
        {
            if (Interlocked.CompareExchange(ref timerBusy, 1, 0) != 0)
                return;
            try
            {
                if (LoadFile(false) && ChartControl != null)
                    ChartControl.Dispatcher.InvokeAsync(() => ForceRefresh());
            }
            catch (Exception)
            {
                // never let a timer exception take NinjaTrader down
            }
            finally
            {
                Interlocked.Exchange(ref timerBusy, 0);
            }
        }

        // Returns true when the zones changed.
        private bool LoadFile(bool force)
        {
            string path    = ResolvePath();
            string content = null;

            try
            {
                if (File.Exists(path))
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                        content = sr.ReadToEnd();
                }
            }
            catch (IOException)
            {
                return false;   // file is being written right now - try again next tick
            }
            catch (UnauthorizedAccessException)
            {
                content = null;
            }

            if (content == null)
            {
                if (!missingLogged)
                {
                    string msg = "PatreonLevels: levels file not found: " + path;
                    Print(msg);
                    Log(msg, LogLevel.Warning);
                    missingLogged = true;
                }
                content = string.Empty;
            }
            else
            {
                missingLogged = false;
            }

            if (!force && content == lastContent)
                return false;

            lastContent = content;
            List<Zone> parsed = ParseZones(content, matchRoot);
            lock (zoneLock)
                zones = parsed;
            return true;
        }

        // "ES1!" -> "ES",  "M2K1!" -> "M2K",  "6E1!" -> "6E"
        private static string ExtractRoot(string raw)
        {
            string s = raw.TrimEnd('!');
            int end = s.Length;
            while (end > 0 && char.IsDigit(s[end - 1]))
                end--;
            return s.Substring(0, end).ToUpperInvariant();
        }

        private static List<Zone> ParseZones(string content, string target)
        {
            var result = new List<Zone>();
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(target))
                return result;

            // Drop spaces/tabs/'*', turn line breaks and ';' into separators.
            var sb = new StringBuilder(content.Length);
            foreach (char c in content)
            {
                if (c == ' ' || c == '\t' || c == '*' || c == '﻿')
                    continue;
                sb.Append(c == '\r' || c == '\n' || c == ';' ? ',' : c);
            }

            // Each block starts with '$'
            string[] blocks = sb.ToString().Split('$');
            for (int b = 1; b < blocks.Length; b++)
            {
                string block = blocks[b];
                int colon = block.IndexOf(':');
                if (colon <= 0)
                    continue;

                if (ExtractRoot(block.Substring(0, colon)) != target)
                    continue;

                string[] tokens = block.Substring(colon + 1).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                bool   haveTop = false, haveBottom = false;
                double top = 0, bottom = 0;

                for (int i = 0; i < tokens.Length; i++)
                {
                    string key = tokens[i].ToUpperInvariant();
                    if ((key == "TOP" || key == "BOTTOM") && i + 1 < tokens.Length)
                    {
                        double v;
                        if (double.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                        {
                            if (key == "TOP") { top = v;    haveTop = true; }
                            else              { bottom = v; haveBottom = true; }
                            i++;
                        }
                    }

                    if (haveTop && haveBottom)
                    {
                        result.Add(new Zone { Top = Math.Max(top, bottom), Bottom = Math.Min(top, bottom) });
                        haveTop = haveBottom = false;
                    }
                }
            }
            return result;
        }

        // -------------------------------------------------------------- render

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            List<Zone> snapshot;
            lock (zoneLock)
                snapshot = zones;

            if (snapshot == null || snapshot.Count == 0 || RenderTarget == null || ChartPanel == null)
                return;

            float left   = ChartPanel.X;
            float width  = ChartPanel.W;
            float panTop = ChartPanel.Y;
            float panBot = ChartPanel.Y + ChartPanel.H;

            SharpDX.Direct2D1.AntialiasMode oldMode = RenderTarget.AntialiasMode;
            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;

            SharpDX.Direct2D1.Brush fill   = ZoneFill.ToDxBrush(RenderTarget);
            fill.Opacity = ZoneOpacity / 100f;

            SharpDX.Direct2D1.Brush border = null;
            if (DrawBorder && ZoneBorderWidth > 0)
            {
                border = ZoneBorder.ToDxBrush(RenderTarget);
                border.Opacity = ZoneBorderOpacity / 100f;
            }

            try
            {
                foreach (Zone z in snapshot)
                {
                    float yTop = chartScale.GetYByValue(z.Top);
                    float yBot = chartScale.GetYByValue(z.Bottom);

                    if (yBot < panTop || yTop > panBot)
                        continue;   // off screen

                    var rect = new SharpDX.RectangleF(left, yTop, width, Math.Max(1f, yBot - yTop));
                    RenderTarget.FillRectangle(rect, fill);
                    if (border != null)
                        RenderTarget.DrawRectangle(rect, border, ZoneBorderWidth);
                }
            }
            finally
            {
                fill.Dispose();
                if (border != null)
                    border.Dispose();
                RenderTarget.AntialiasMode = oldMode;
            }
        }

        // ---------------------------------------------------------- properties

        [Display(Name = "Levels file", Description = "File name inside Documents\\NinjaTrader 8, or a full path.", Order = 1, GroupName = "1. File")]
        public string LevelsFile { get; set; }

        [Range(1, 3600)]
        [Display(Name = "Re-check every (seconds)", Order = 2, GroupName = "1. File")]
        public int ReloadSeconds { get; set; }

        [Display(Name = "Symbol override", Description = "Blank = use the chart's instrument (e.g. ES). Type a ticker to force one.", Order = 3, GroupName = "1. File")]
        public string SymbolOverride { get; set; }

        [XmlIgnore]
        [Display(Name = "Zone color", Order = 1, GroupName = "2. Zones")]
        public Brush ZoneFill { get; set; }

        [Browsable(false)]
        public string ZoneFillSerializable
        {
            get { return Serialize.BrushToString(ZoneFill); }
            set { ZoneFill = Serialize.StringToBrush(value); }
        }

        [Range(0, 100)]
        [Display(Name = "Zone opacity (%)", Order = 2, GroupName = "2. Zones")]
        public int ZoneOpacity { get; set; }

        [Display(Name = "Draw behind price", Order = 3, GroupName = "2. Zones")]
        public bool DrawBehindPrice { get; set; }

        [Display(Name = "Draw border", Order = 4, GroupName = "2. Zones")]
        public bool DrawBorder { get; set; }

        [XmlIgnore]
        [Display(Name = "Border color", Order = 5, GroupName = "2. Zones")]
        public Brush ZoneBorder { get; set; }

        [Browsable(false)]
        public string ZoneBorderSerializable
        {
            get { return Serialize.BrushToString(ZoneBorder); }
            set { ZoneBorder = Serialize.StringToBrush(value); }
        }

        [Range(0, 100)]
        [Display(Name = "Border opacity (%)", Order = 6, GroupName = "2. Zones")]
        public int ZoneBorderOpacity { get; set; }

        [Range(1, 10)]
        [Display(Name = "Border width", Order = 7, GroupName = "2. Zones")]
        public int ZoneBorderWidth { get; set; }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private PatreonLevels[] cachePatreonLevels;
		public PatreonLevels PatreonLevels()
		{
			return PatreonLevels(Input);
		}

		public PatreonLevels PatreonLevels(ISeries<double> input)
		{
			if (cachePatreonLevels != null)
				for (int idx = 0; idx < cachePatreonLevels.Length; idx++)
					if (cachePatreonLevels[idx] != null &&  cachePatreonLevels[idx].EqualsInput(input))
						return cachePatreonLevels[idx];
			return CacheIndicator<PatreonLevels>(new PatreonLevels(), input, ref cachePatreonLevels);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.PatreonLevels PatreonLevels()
		{
			return indicator.PatreonLevels(Input);
		}

		public Indicators.PatreonLevels PatreonLevels(ISeries<double> input )
		{
			return indicator.PatreonLevels(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.PatreonLevels PatreonLevels()
		{
			return indicator.PatreonLevels(Input);
		}

		public Indicators.PatreonLevels PatreonLevels(ISeries<double> input )
		{
			return indicator.PatreonLevels(input);
		}
	}
}

#endregion
