#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
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
using System.Text.RegularExpressions;
#endregion

#region Enums
public enum FontSizeOption { Tiny, Small, Normal, Large }
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class McGrawPlaybook : Indicator
    {
        #region Fields
        private SessionIterator sessionIterator;
        private int sessionStartBar = 0;
        #endregion

        #region Parameters
        [NinjaScriptProperty]
        [Display(Name="Zones File Path", Order=1, GroupName="Parameters")]
        public string ZonesFilePath { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Font Size", Order=2, GroupName="Options")]
        public FontSizeOption FontSize { get; set; } = FontSizeOption.Normal;

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Line Width", Order=3, GroupName="Options")]
        public int LineWidth { get; set; } = 1;

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Box Transparency (%)", Order=4, GroupName="Options")]
        public int BoxTransparency { get; set; } = 50;

        [XmlIgnore]
        [Display(Name="Long Color", Order=5, GroupName="Colors")]
        public Brush LongColor { get; set; } = Brushes.Lime;
        [Browsable(false)]
        public string LongColorSerialize
        {
            get => Serialize.BrushToString(LongColor);
            set => LongColor = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [Display(Name="Short Color", Order=6, GroupName="Colors")]
        public Brush ShortColor { get; set; } = Brushes.Red;
        [Browsable(false)]
        public string ShortColorSerialize
        {
            get => Serialize.BrushToString(ShortColor);
            set => ShortColor = Serialize.StringToBrush(value);
        }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description        = "McGraw Playbook v2 — reads IF…THEN zones from a local text file, stacks labels by direction with padding";
                Name               = "McGrawPlaybook";
                Calculate          = Calculate.OnBarClose;
                IsOverlay          = true;
                BarsRequiredToPlot = 0;
                ZonesFilePath      = "";
            }
            else if (State == State.DataLoaded)
            {
                sessionIterator = new SessionIterator(Bars);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 ||
                CurrentBar < 1 ||
                sessionIterator == null ||
                string.IsNullOrWhiteSpace(ZonesFilePath) ||
                !File.Exists(ZonesFilePath))
                return;

            if (Bars.IsFirstBarOfSession)
                sessionStartBar = CurrentBar;

            int barsAgoStart = CurrentBar - sessionStartBar;
            if (barsAgoStart < 0) barsAgoStart = 0;

            // clear previous drawings
            foreach (var tag in DrawObjects.Select(o => o.Tag)
                                           .Where(t => t.StartsWith("McGraw"))
                                           .ToArray())
                RemoveDrawObject(tag);

            // 1) Read & filter "IF " lines
            var rawLines = File.ReadAllLines(ZonesFilePath)
                               .Select(l => l.Trim())
                               .Where(l => l.StartsWith("IF ", StringComparison.OrdinalIgnoreCase))
                               .ToArray();

            // 2) Parse into entries
            var entries = new List<(double price, bool isShort, bool isLong, string label)>();
            foreach (var raw in rawLines)
            {
                var m = Regex.Match(raw, @"[-+]?[0-9]*\.?[0-9]+");
                if (!m.Success || !double.TryParse(m.Value, out double price))
                    continue;

                bool isShort = Regex.IsMatch(raw, @"^IF\s*<", RegexOptions.IgnoreCase);
                bool isLong  = Regex.IsMatch(raw, @"^IF\s*>", RegexOptions.IgnoreCase);
                string desc  = raw.Replace("IF ", "").Replace("THEN ", "").Trim();
                string prefix = isShort ? "▼▼ " : isLong ? "▲▲ " : "";
                entries.Add((price, isShort, isLong, prefix + desc));
            }

            // 3) Group by price
            var priceGroups = entries
                .GroupBy(e => e.price)
                .OrderByDescending(g => g.Key);

            int globalIdx = 0;
            foreach (var pg in priceGroups)
            {
                double price = pg.Key;

                // 4) Within each price, subgroup by direction: Long first, then Short
                var dirGroups = pg.GroupBy(e => e.isShort ? "S" : "L")
                                  .OrderBy(g => g.Key);

                // Determine font size once
                int fs = FontSize switch
                {
                    FontSizeOption.Tiny   =>  8,
                    FontSizeOption.Small  => 10,
                    FontSizeOption.Normal => 12,
                    FontSizeOption.Large  => 14,
                    _                     => 12
                };
                var font = new SimpleFont("Arial", fs);

                int accumulator = 0;
                int dirIdx = 0;
                foreach (var dg in dirGroups)
                {
                    bool isShort = dg.Key == "S";
                    Color baseColor = isShort
                        ? ((SolidColorBrush)ShortColor).Color
                        : ((SolidColorBrush)LongColor).Color;

                    var brush = new SolidColorBrush(baseColor);
                    brush.Freeze();

                    // Draw one rectangle per price+direction
                    Draw.Rectangle(
                        this,
                        $"McGrawRect{globalIdx}_{dirIdx}",
                        false,
                        barsAgoStart, price,
                        0,            price,
                        brush, brush,
                        BoxTransparency
                    );

                    // Combine labels for this subgroup
                    var labels = dg.Select(e => e.label).ToArray();
                    string text = string.Join("\n", labels);

                    // Bump accumulator by number of lines in this subgroup
                    accumulator += labels.Length;
                    // yOffset = -fontSize * accumulator
                    int yOffset = (-fs * accumulator);

                    Draw.Text(
                        this,
                        $"McGrawText{globalIdx}_{dirIdx}",
                        false,
                        text,
                        0, price,
                        yOffset,
                        brush,
                        font,
                        TextAlignment.Center,
                        brush,
                        null,
                        0
                    );

                    // Add one extra line-space of padding before next subgroup
                    accumulator += 1;
                    dirIdx++;
                }

                globalIdx++;
            }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private McGrawPlaybook[] cacheMcGrawPlaybook;
		public McGrawPlaybook McGrawPlaybook(string zonesFilePath, FontSizeOption fontSize, int lineWidth, int boxTransparency)
		{
			return McGrawPlaybook(Input, zonesFilePath, fontSize, lineWidth, boxTransparency);
		}

		public McGrawPlaybook McGrawPlaybook(ISeries<double> input, string zonesFilePath, FontSizeOption fontSize, int lineWidth, int boxTransparency)
		{
			if (cacheMcGrawPlaybook != null)
				for (int idx = 0; idx < cacheMcGrawPlaybook.Length; idx++)
					if (cacheMcGrawPlaybook[idx] != null && cacheMcGrawPlaybook[idx].ZonesFilePath == zonesFilePath && cacheMcGrawPlaybook[idx].FontSize == fontSize && cacheMcGrawPlaybook[idx].LineWidth == lineWidth && cacheMcGrawPlaybook[idx].BoxTransparency == boxTransparency && cacheMcGrawPlaybook[idx].EqualsInput(input))
						return cacheMcGrawPlaybook[idx];
			return CacheIndicator<McGrawPlaybook>(new McGrawPlaybook(){ ZonesFilePath = zonesFilePath, FontSize = fontSize, LineWidth = lineWidth, BoxTransparency = boxTransparency }, input, ref cacheMcGrawPlaybook);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.McGrawPlaybook McGrawPlaybook(string zonesFilePath, FontSizeOption fontSize, int lineWidth, int boxTransparency)
		{
			return indicator.McGrawPlaybook(Input, zonesFilePath, fontSize, lineWidth, boxTransparency);
		}

		public Indicators.McGrawPlaybook McGrawPlaybook(ISeries<double> input , string zonesFilePath, FontSizeOption fontSize, int lineWidth, int boxTransparency)
		{
			return indicator.McGrawPlaybook(input, zonesFilePath, fontSize, lineWidth, boxTransparency);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.McGrawPlaybook McGrawPlaybook(string zonesFilePath, FontSizeOption fontSize, int lineWidth, int boxTransparency)
		{
			return indicator.McGrawPlaybook(Input, zonesFilePath, fontSize, lineWidth, boxTransparency);
		}

		public Indicators.McGrawPlaybook McGrawPlaybook(ISeries<double> input , string zonesFilePath, FontSizeOption fontSize, int lineWidth, int boxTransparency)
		{
			return indicator.McGrawPlaybook(input, zonesFilePath, fontSize, lineWidth, boxTransparency);
		}
	}
}

#endregion
