#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class DailyLevels : Indicator
    {
        private Dictionary<string, double> levelsDictionary = new Dictionary<string, double>();
        private double nyOpenPrice = 0;
        private DateTime nyOpenTime = DateTime.MinValue;
        private bool nyOpenCaptured = false;
        private double prevDayHigh = 0;
        private double prevDayLow = 0;
        private DateTime prevDayDate = DateTime.MinValue;
        private double orHigh = 0;
        private double orLow = double.MaxValue;
        private DateTime orDate = DateTime.MinValue;
        private bool orCompleted = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                 = @"Optimized Daily Levels and Breakout Zones Indicator";
                Name                        = "DailyLevels";
                Calculate                   = Calculate.OnBarClose;
                IsOverlay                   = true;
                DisplayInDataBox            = true;
                DrawOnPricePanel            = true;
                PaintPriceMarkers           = true;
                ScaleJustification          = ScaleJustification.Right;
                IsSuspendedWhileInactive    = true;

                LevelsInput = "XR2, 30200; XR1, 30060; R2, 29970; R1, 29870; L1, 29760; G1, 29730; H1, 29500; Bl+, 29470; Bl, 29360; Go, 29270; Br, 29160; Br+, 29040; -H1, 28950; -G1, 28850; -L1, 28700; -R1, 28600; -XR1, 28500; -XR2, 28400";
                ShowLabels = true;
                LabelYOffset = 2.0; // Positive value lifts label above the line in points/ticks

                // Bullish Zones
                ShowBullishZones = true;
                BullZone_FillColor = Brushes.DarkSlateGray;
                BullZone_FillOpacity = 15;
                BullZone_BorderColor = Brushes.Teal;
                BullZone_BorderOpacity = 80;
                BullZone_BorderThickness = 1;
                BullZone_BorderStyle = DashStyleHelper.Solid;
                BullZone_ShowLabels = true;

                // Bearish Zones
                ShowBearishZones = true;
                BearZone_FillColor = Brushes.DarkRed;
                BearZone_FillOpacity = 15;
                BearZone_BorderColor = Brushes.Crimson;
                BearZone_BorderOpacity = 80;
                BearZone_BorderThickness = 1;
                BearZone_BorderStyle = DashStyleHelper.Solid;
                BearZone_ShowLabels = true;

                // Core Levels
                XR1_Thickness = 2; XR1_Color = Brushes.Magenta; XR1_Opacity = 80; XR1_Style = DashStyleHelper.Solid; XR1_ShowLabel = true;
                R1_Thickness = 2; R1_Color = Brushes.Red; R1_Opacity = 80; R1_Style = DashStyleHelper.Solid; R1_ShowLabel = true;
                L1_Thickness = 2; L1_Color = Brushes.Blue; L1_Opacity = 80; L1_Style = DashStyleHelper.Solid; L1_ShowLabel = true;
                Go_Thickness = 2; Go_Color = Brushes.Gold; Go_Opacity = 80; Go_Style = DashStyleHelper.Solid; Go_ShowLabel = true;

                Default_Thickness = 1; Default_Color = Brushes.Gray; Default_Opacity = 60; Default_Style = DashStyleHelper.Dash; Default_ShowLabel = true;

                // Session References
                ShowNYOpen = true; NYOpen_Thickness = 2; NYOpen_Color = Brushes.Cyan; NYOpen_Opacity = 90; NYOpen_Style = DashStyleHelper.Solid; NYOpen_ShowLabel = true;
                ShowPrevDayHigh = true; PrevDayHigh_Thickness = 2; PrevDayHigh_Color = Brushes.Lime; PrevDayHigh_Opacity = 85; PrevDayHigh_Style = DashStyleHelper.Dash; PrevDayHigh_ShowLabel = true;
                ShowPrevDayLow = true; PrevDayLow_Thickness = 2; PrevDayLow_Color = Brushes.Red; PrevDayLow_Opacity = 85; PrevDayLow_Style = DashStyleHelper.Dash; PrevDayLow_ShowLabel = true;

                ShowOpeningRange = true;
                ORStartTime = new TimeSpan(9, 30, 0);
                OREndTime = new TimeSpan(9, 45, 0);
                ORHigh_Thickness = 2; ORHigh_Color = Brushes.Orange; ORHigh_Opacity = 85; ORHigh_Style = DashStyleHelper.Solid; ORHigh_ShowLabel = true;
                ORLow_Thickness = 2; ORLow_Color = Brushes.Orange; ORLow_Opacity = 85; ORLow_Style = DashStyleHelper.Solid; ORLow_ShowLabel = true;

                EnableLevelMerging = false;
                MergingThreshold = 5.0;
                UseAutoScaling = true;
            }
            else if (State == State.DataLoaded)
            {
                ParseLevels();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || CurrentBar < 20)
                return;

            CheckForNYOpen();
            CheckForPrevDayLevels();
            CheckForOpeningRange();

            DrawLevelsAndZones();
        }

        private void ParseLevels()
        {
            levelsDictionary.Clear();
            if (string.IsNullOrWhiteSpace(LevelsInput)) return;

            string[] levels = LevelsInput.Split(';');
            foreach (string level in levels)
            {
                if (string.IsNullOrWhiteSpace(level)) continue;
                string[] parts = level.Split(',');
                if (parts.Length == 2 && double.TryParse(parts[1].Trim(), out double price))
                {
                    levelsDictionary[parts[0].Trim()] = price;
                }
            }
        }

        private void DrawLevelsAndZones()
        {
            DrawZones();

            foreach (var kvp in levelsDictionary)
            {
                if (!ShouldShowLevel(kvp.Key)) continue;

                var settings = GetLevelSettings(kvp.Key);
                SolidColorBrush brush = GetBrush(settings.Color, settings.Opacity);

                string tag = "DailyLvl_" + kvp.Key.Replace("+", "P").Replace("-", "M");
                Draw.Ray(this, tag + "_L", false, CurrentBar, kvp.Value, 0, kvp.Value, brush, settings.Style, settings.Thickness);

                if (ShowLabels && settings.ShowLabel)
                {
                    // Draw text slightly ABOVE the level line by adding LabelYOffset
                    Draw.Text(this, tag + "_T", false, kvp.Key, -5, kvp.Value + LabelYOffset, 0, brush,
                              new NinjaTrader.Gui.Tools.SimpleFont("Arial", 9), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                }
            }

            DrawSessionReferences();
        }

        private void DrawZones()
        {
            if (ShowBullishZones)
            {
                TryDrawZone("BullZone1", "Bl", "Bl+", "BullBO.Z1", BullZone_FillColor, BullZone_FillOpacity, BullZone_BorderColor, BullZone_BorderOpacity, BullZone_BorderThickness, BullZone_BorderStyle, BullZone_ShowLabels);
                TryDrawZone("BullZone2", "G1", "H1", "BullBO.Z2", BullZone_FillColor, BullZone_FillOpacity, BullZone_BorderColor, BullZone_BorderOpacity, BullZone_BorderThickness, BullZone_BorderStyle, BullZone_ShowLabels);
                TryDrawZone("BullZone3", "XR1", "XR2", "BullBO.Z3", BullZone_FillColor, BullZone_FillOpacity, BullZone_BorderColor, BullZone_BorderOpacity, BullZone_BorderThickness, BullZone_BorderStyle, BullZone_ShowLabels);
            }

            if (ShowBearishZones)
            {
                TryDrawZone("BearZone1", "Br+", "Br", "BearBO.Z1", BearZone_FillColor, BearZone_FillOpacity, BearZone_BorderColor, BearZone_BorderOpacity, BearZone_BorderThickness, BearZone_BorderStyle, BearZone_ShowLabels);
                TryDrawZone("BearZone2", "-H1", "-G1", "BearBO.Z2", BearZone_FillColor, BearZone_FillOpacity, BearZone_BorderColor, BearZone_BorderOpacity, BearZone_BorderThickness, BearZone_BorderStyle, BearZone_ShowLabels);
                TryDrawZone("BearZone3", "-XR1", "-XR2", "BearBO.Z3", BearZone_FillColor, BearZone_FillOpacity, BearZone_BorderColor, BearZone_BorderOpacity, BearZone_BorderThickness, BearZone_BorderStyle, BearZone_ShowLabels);
            }
        }

        private void TryDrawZone(string key, string k1, string k2, string label, Brush fill, int fillOpacity, Brush border, int borderOpacity, int thickness, DashStyleHelper style, bool showLabel)
        {
            if (levelsDictionary.ContainsKey(k1) && levelsDictionary.ContainsKey(k2))
            {
                DrawZone(key, levelsDictionary[k1], levelsDictionary[k2], label, fill, fillOpacity, border, borderOpacity, thickness, style, showLabel);
            }
        }

        private void DrawZone(string zoneName, double p1, double p2, string label, Brush fillColor, int fillOpacity, Brush borderColor, int borderOpacity, int borderThickness, DashStyleHelper borderStyle, bool showLabel)
        {
            double bottomPrice = Math.Min(p1, p2);
            double topPrice = Math.Max(p1, p2);

            SolidColorBrush fillBrush = GetBrush(fillColor, fillOpacity);
            SolidColorBrush borderBrush = GetBrush(borderColor, borderOpacity);

            Draw.Rectangle(this, zoneName + "_Fill", false, CurrentBar, bottomPrice, 0, topPrice, fillBrush, fillBrush, fillOpacity);
            Draw.Ray(this, zoneName + "_Top", false, CurrentBar, topPrice, 0, topPrice, borderBrush, borderStyle, borderThickness);
            Draw.Ray(this, zoneName + "_Bottom", false, CurrentBar, bottomPrice, 0, bottomPrice, borderBrush, borderStyle, borderThickness);

            if (showLabel)
            {
                double midPrice = (bottomPrice + topPrice) / 2;
                Draw.Text(this, zoneName + "_Lbl", false, label, -10, midPrice, 0, borderBrush, new NinjaTrader.Gui.Tools.SimpleFont("Arial", 9) { Bold = true }, TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
            }
        }

        private bool ShouldShowLevel(string label)
        {
            string norm = label.Replace("-", "").ToUpper();
            if (ShowBullishZones && (norm == "BL" || norm == "BL+" || norm == "H1" || norm == "G1")) return false;
            if (ShowBearishZones && (norm == "BR" || norm == "BR+" || (label.StartsWith("-") && (norm == "H1" || norm == "G1")))) return false;
            return true;
        }

        private void DrawSessionReferences()
        {
            if (ShowNYOpen && nyOpenPrice > 0)
            {
                SolidColorBrush nyBrush = GetBrush(NYOpen_Color, NYOpen_Opacity);
                Draw.Ray(this, "NYOpen_L", false, CurrentBar, nyOpenPrice, 0, nyOpenPrice, nyBrush, NYOpen_Style, NYOpen_Thickness);
                if (NYOpen_ShowLabel) 
                    Draw.Text(this, "NYOpen_T", false, "NYO", -5, nyOpenPrice + LabelYOffset, 0, nyBrush, new NinjaTrader.Gui.Tools.SimpleFont("Arial", 9), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
            }

            if (ShowPrevDayHigh && prevDayHigh > 0)
            {
                SolidColorBrush pdhBrush = GetBrush(PrevDayHigh_Color, PrevDayHigh_Opacity);
                Draw.Ray(this, "PDH_L", false, CurrentBar, prevDayHigh, 0, prevDayHigh, pdhBrush, PrevDayHigh_Style, PrevDayHigh_Thickness);
                if (PrevDayHigh_ShowLabel) 
                    Draw.Text(this, "PDH_T", false, "PDH", -5, prevDayHigh + LabelYOffset, 0, pdhBrush, new NinjaTrader.Gui.Tools.SimpleFont("Arial", 9), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
            }

            if (ShowPrevDayLow && prevDayLow > 0 && prevDayLow != double.MaxValue)
            {
                SolidColorBrush pdlBrush = GetBrush(PrevDayLow_Color, PrevDayLow_Opacity);
                Draw.Ray(this, "PDL_L", false, CurrentBar, prevDayLow, 0, prevDayLow, pdlBrush, PrevDayLow_Style, PrevDayLow_Thickness);
                if (PrevDayLow_ShowLabel) 
                    Draw.Text(this, "PDL_T", false, "PDL", -5, prevDayLow + LabelYOffset, 0, pdlBrush, new NinjaTrader.Gui.Tools.SimpleFont("Arial", 9), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
            }
        }

        private SolidColorBrush GetBrush(Brush baseBrush, int opacity)
        {
            SolidColorBrush brush = (baseBrush is SolidColorBrush sb) ? sb.Clone() : new SolidColorBrush(Colors.Gray);
            brush.Opacity = opacity / 100.0;
            return brush;
        }

        private (int Thickness, Brush Color, int Opacity, DashStyleHelper Style, bool ShowLabel) GetLevelSettings(string label)
        {
            string norm = label.Replace("-", "").ToUpper();
            switch (norm)
            {
                case "XR1": return (XR1_Thickness, XR1_Color, XR1_Opacity, XR1_Style, XR1_ShowLabel);
                case "R1":  return (R1_Thickness, R1_Color, R1_Opacity, R1_Style, R1_ShowLabel);
                case "L1":  return (L1_Thickness, L1_Color, L1_Opacity, L1_Style, L1_ShowLabel);
                case "GO":  return (Go_Thickness, Go_Color, Go_Opacity, Go_Style, Go_ShowLabel);
                default:    return (Default_Thickness, Default_Color, Default_Opacity, Default_Style, Default_ShowLabel);
            }
        }

        private void CheckForNYOpen()
        {
            if (!ShowNYOpen) return;
            DateTime timeET = TimeZoneInfo.ConvertTime(Time[0], TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
            if (timeET.Date != nyOpenTime.Date) { nyOpenCaptured = false; nyOpenPrice = 0; }
            if (timeET.TimeOfDay >= new TimeSpan(9, 30, 0) && timeET.TimeOfDay < new TimeSpan(9, 35, 0) && !nyOpenCaptured)
            {
                nyOpenPrice = Open[0];
                nyOpenTime = Time[0];
                nyOpenCaptured = true;
            }
        }

        private void CheckForPrevDayLevels()
        {
            if ((!ShowPrevDayHigh && !ShowPrevDayLow) || CurrentBar < 1) return;
            DateTime timeET = TimeZoneInfo.ConvertTime(Time[0], TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
            DateTime session = (timeET.TimeOfDay >= new TimeSpan(18, 0, 0)) ? timeET.Date : timeET.Date.AddDays(-1);

            if (session != prevDayDate)
            {
                FindPrevHighLow(session);
                prevDayDate = session;
            }
        }

        private void FindPrevHighLow(DateTime session)
        {
            double h = double.MinValue, l = double.MaxValue;
            bool found = false;
            DateTime prevSession = session.AddDays((session.DayOfWeek == DayOfWeek.Monday) ? -3 : -1);

            for (int i = 1; i <= CurrentBar && i <= 1000; i++)
            {
                DateTime barET = TimeZoneInfo.ConvertTime(Time[i], TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
                DateTime barSession = (barET.TimeOfDay >= new TimeSpan(18, 0, 0)) ? barET.Date : barET.Date.AddDays(-1);

                if (barSession == prevSession)
                {
                    found = true;
                    h = Math.Max(h, High[i]);
                    l = Math.Min(l, Low[i]);
                }
                else if (found && barSession < prevSession) break;
            }

            if (found) { prevDayHigh = h; prevDayLow = l; }
        }

        private void CheckForOpeningRange()
        {
            if (!ShowOpeningRange || CurrentBar < 1) return;
            DateTime timeET = TimeZoneInfo.ConvertTime(Time[0], TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));

            if (timeET.Date != orDate)
            {
                orHigh = 0; orLow = double.MaxValue; orCompleted = false; orDate = timeET.Date;
            }

            TimeSpan tod = timeET.TimeOfDay;
            if (tod >= ORStartTime && tod <= OREndTime)
            {
                orHigh = Math.Max(orHigh, High[0]);
                orLow = Math.Min(orLow, Low[0]);
            }
            else if (tod > OREndTime) { orCompleted = true; }
        }

        #region Properties
        [NinjaScriptProperty] [Display(Name = "Levels Input", GroupName = "1. Daily Levels")] public string LevelsInput { get; set; }
        [NinjaScriptProperty] [Display(Name = "Show Labels", GroupName = "1. Daily Levels")] public bool ShowLabels { get; set; }
        [NinjaScriptProperty] [Display(Name = "Label Y Offset (Points)", Description = "Price distance to lift labels above the level line", GroupName = "1. Daily Levels")] public double LabelYOffset { get; set; }

        [NinjaScriptProperty] [Display(Name = "Show Bullish Zones", Order = 1, GroupName = "2. Zones - Bullish")] public bool ShowBullishZones { get; set; }
        [XmlIgnore] [Display(Name = "Fill Color", Order = 2, GroupName = "2. Zones - Bullish")] public Brush BullZone_FillColor { get; set; }
        [Browsable(false)] public string BullZone_FillColorSerializable { get => Serialize.BrushToString(BullZone_FillColor); set => BullZone_FillColor = Serialize.StringToBrush(value); }
        [Range(1, 100)] [NinjaScriptProperty] [Display(Name = "Fill Opacity (%)", Order = 3, GroupName = "2. Zones - Bullish")] public int BullZone_FillOpacity { get; set; }
        [XmlIgnore] [Display(Name = "Border Color", Order = 4, GroupName = "2. Zones - Bullish")] public Brush BullZone_BorderColor { get; set; }
        [Browsable(false)] public string BullZone_BorderColorSerializable { get => Serialize.BrushToString(BullZone_BorderColor); set => BullZone_BorderColor = Serialize.StringToBrush(value); }
        [Range(1, 100)] [NinjaScriptProperty] [Display(Name = "Border Opacity (%)", Order = 5, GroupName = "2. Zones - Bullish")] public int BullZone_BorderOpacity { get; set; }
        [Range(1, 10)] [NinjaScriptProperty] [Display(Name = "Border Thickness", Order = 6, GroupName = "2. Zones - Bullish")] public int BullZone_BorderThickness { get; set; }
        [Display(Name = "Border Style", Order = 7, GroupName = "2. Zones - Bullish")] public DashStyleHelper BullZone_BorderStyle { get; set; }
        [NinjaScriptProperty] [Display(Name = "Show Zone Labels", Order = 8, GroupName = "2. Zones - Bullish")] public bool BullZone_ShowLabels { get; set; }

        [NinjaScriptProperty] [Display(Name = "Show Bearish Zones", Order = 1, GroupName = "2. Zones - Bearish")] public bool ShowBearishZones { get; set; }
        [XmlIgnore] [Display(Name = "Fill Color", Order = 2, GroupName = "2. Zones - Bearish")] public Brush BearZone_FillColor { get; set; }
        [Browsable(false)] public string BearZone_FillColorSerializable { get => Serialize.BrushToString(BearZone_FillColor); set => BearZone_FillColor = Serialize.StringToBrush(value); }
        [Range(1, 100)] [NinjaScriptProperty] [Display(Name = "Fill Opacity (%)", Order = 3, GroupName = "2. Zones - Bearish")] public int BearZone_FillOpacity { get; set; }
        [XmlIgnore] [Display(Name = "Border Color", Order = 4, GroupName = "2. Zones - Bearish")] public Brush BearZone_BorderColor { get; set; }
        [Browsable(false)] public string BearZone_BorderColorSerializable { get => Serialize.BrushToString(BearZone_BorderColor); set => BearZone_BorderColor = Serialize.StringToBrush(value); }
        [Range(1, 100)] [NinjaScriptProperty] [Display(Name = "Border Opacity (%)", Order = 5, GroupName = "2. Zones - Bearish")] public int BearZone_BorderOpacity { get; set; }
        [Range(1, 10)] [NinjaScriptProperty] [Display(Name = "Border Thickness", Order = 6, GroupName = "2. Zones - Bearish")] public int BearZone_BorderThickness { get; set; }
        [Display(Name = "Border Style", Order = 7, GroupName = "2. Zones - Bearish")] public DashStyleHelper BearZone_BorderStyle { get; set; }
        [NinjaScriptProperty] [Display(Name = "Show Zone Labels", Order = 8, GroupName = "2. Zones - Bearish")] public bool BearZone_ShowLabels { get; set; }

        [Range(1, 10)] [NinjaScriptProperty] [Display(Name = "XR1 Thickness", GroupName = "3. Core Levels")] public int XR1_Thickness { get; set; }
        [XmlIgnore] [Display(Name = "XR1 Color", GroupName = "3. Core Levels")] public Brush XR1_Color { get; set; }
        [Browsable(false)] public string XR1_ColorSerializable { get => Serialize.BrushToString(XR1_Color); set => XR1_Color = Serialize.StringToBrush(value); }
        [Range(1, 100)] [NinjaScriptProperty] [Display(Name = "XR1 Opacity (%)", GroupName = "3. Core Levels")] public int XR1_Opacity { get; set; }
        [Display(Name = "XR1 Style", GroupName = "3. Core Levels")] public DashStyleHelper XR1_Style { get; set; }
        [NinjaScriptProperty] [Display(Name = "XR1 Show Label", GroupName = "3. Core Levels")] public bool XR1_ShowLabel { get; set; }

        [Range(1, 10)] [NinjaScriptProperty] [Display(Name = "R1 Thickness", GroupName = "3. Core Levels")] public int R1_Thickness { get; set; }
        [XmlIgnore] [Display(Name = "R1 Color", GroupName = "3. Core Levels")] public Brush R1_Color { get; set; }
        [Browsable(false)] public string R1_ColorSerializable { get => Serialize.BrushToString(R1_Color); set => R1_Color = Serialize.StringToBrush(value); }
        [Range(1, 100)] [NinjaScriptProperty] [Display(Name = "R1 Opacity (%)", GroupName = "3. Core Levels")] public int R1_Opacity { get; set; }
        [Display(Name = "R1 Style", GroupName = "3. Core Levels")] public DashStyleHelper R1_Style { get; set; }
        [NinjaScriptProperty] [Display(Name = "R1 Show Label", GroupName = "3. Core Levels")] public bool R1_ShowLabel { get; set; }

        [Range(1, 10)] [NinjaScriptProperty] [Display(Name = "L1 Thickness", GroupName = "3. Core Levels")] public int L1_Thickness { get; set; }
        [XmlIgnore] [Display(Name = "L1 Color", GroupName = "3. Core Levels")] public Brush L1_Color { get; set; }
        [Browsable(false)] public string L1_ColorSerializable { get => Serialize.BrushToString(L1_Color); set => L1_Color = Serialize.StringToBrush(value); }
        [Range(1, 100)] [NinjaScriptProperty] [Display(Name = "L1 Opacity (%)", GroupName = "3. Core Levels")] public int L1_Opacity { get; set; }
        [Display(Name = "L1 Style", GroupName = "3. Core Levels")] public DashStyleHelper L1_Style { get; set; }
        [NinjaScriptProperty] [Display(Name = "L1 Show Label", GroupName = "3. Core Levels")] public bool L1_ShowLabel { get; set; }

        [Range(1, 10)] [NinjaScriptProperty] [Display(Name = "Go Thickness", GroupName = "3. Core Levels")] public int Go_Thickness { get; set; }
        [XmlIgnore] [Display(Name = "Go Color", GroupName = "3. Core Levels")] public Brush Go_Color { get; set; }
        [Browsable(false)] public string Go_ColorSerializable { get => Serialize.BrushToString(Go_Color); set => Go_Color = Serialize.StringToBrush(value); }
        [Range(1, 100)] [NinjaScriptProperty] [Display(Name = "Go Opacity (%)", GroupName = "3. Core Levels")] public int Go_Opacity { get; set; }
        [Display(Name = "Go Style", GroupName = "3. Core Levels")] public DashStyleHelper Go_Style { get; set; }
        [NinjaScriptProperty] [Display(Name = "Go Show Label", GroupName = "3. Core Levels")] public bool Go_ShowLabel { get; set; }

        [Range(1, 10)] [NinjaScriptProperty] [Display(Name = "Default Thickness", GroupName = "Default Level")] public int Default_Thickness { get; set; }
        [XmlIgnore] [Display(Name = "Default Color", GroupName = "Default Level")] public Brush Default_Color { get; set; }
        [Browsable(false)] public string Default_ColorSerializable { get => Serialize.BrushToString(Default_Color); set => Default_Color = Serialize.StringToBrush(value); }
        [Range(1, 100)] [NinjaScriptProperty] [Display(Name = "Default Opacity (%)", GroupName = "Default Level")] public int Default_Opacity { get; set; }
        [Display(Name = "Default Style", GroupName = "Default Level")] public DashStyleHelper Default_Style { get; set; }
        [NinjaScriptProperty] [Display(Name = "Default Show Label", GroupName = "Default Level")] public bool Default_ShowLabel { get; set; }

        [NinjaScriptProperty] [Display(Name = "Show NY Open", GroupName = "6. Reference - NY Open")] public bool ShowNYOpen { get; set; }
        [Range(1, 10)] [NinjaScriptProperty] [Display(Name = "NY Open Thickness", GroupName = "6. Reference - NY Open")] public int NYOpen_Thickness { get; set; }
        [XmlIgnore] [Display(Name = "NY Open Color", GroupName = "6. Reference - NY Open")] public Brush NYOpen_Color { get; set; }
        [Browsable(false)] public string NYOpen_ColorSerializable { get => Serialize.BrushToString(NYOpen_Color); set => NYOpen_Color = Serialize.StringToBrush(value); }
        [Range(1, 100)] [NinjaScriptProperty] [Display(Name = "NY Open Opacity (%)", GroupName = "6. Reference - NY Open")] public int NYOpen_Opacity { get; set; }
        [Display(Name = "NY Open Style", GroupName = "6. Reference - NY Open")] public DashStyleHelper NYOpen_Style { get; set; }
        [NinjaScriptProperty] [Display(Name = "NY Open Show Label", GroupName = "6. Reference - NY Open")] public bool NYOpen_ShowLabel { get; set; }

        [NinjaScriptProperty] [Display(Name = "Show Previous Day High", GroupName = "6. Reference - Prev Day High")] public bool ShowPrevDayHigh { get; set; }
        [Range(1, 10)] [NinjaScriptProperty] [Display(Name = "PDH Thickness", GroupName = "6. Reference - Prev Day High")] public int PrevDayHigh_Thickness { get; set; }
        [XmlIgnore] [Display(Name = "PDH Color", GroupName = "6. Reference - Prev Day High")] public Brush PrevDayHigh_Color { get; set; }
        [Browsable(false)] public string PrevDayHigh_ColorSerializable { get => Serialize.BrushToString(PrevDayHigh_Color); set => PrevDayHigh_Color = Serialize.StringToBrush(value); }
        [Range(1, 100)] [NinjaScriptProperty] [Display(Name = "PDH Opacity (%)", GroupName = "6. Reference - Prev Day High")] public int PrevDayHigh_Opacity { get; set; }
        [Display(Name = "PDH Style", GroupName = "6. Reference - Prev Day High")] public DashStyleHelper PrevDayHigh_Style { get; set; }
        [NinjaScriptProperty] [Display(Name = "PDH Show Label", GroupName = "6. Reference - Prev Day High")] public bool PrevDayHigh_ShowLabel { get; set; }

        [NinjaScriptProperty] [Display(Name = "Show Previous Day Low", GroupName = "6. Reference - Prev Day Low")] public bool ShowPrevDayLow { get; set; }
        [Range(1, 10)] [NinjaScriptProperty] [Display(Name = "PDL Thickness", GroupName = "6. Reference - Prev Day Low")] public int PrevDayLow_Thickness { get; set; }
        [XmlIgnore] [Display(Name = "PDL Color", GroupName = "6. Reference - Prev Day Low")] public Brush PrevDayLow_Color { get; set; }
        [Browsable(false)] public string PrevDayLow_ColorSerializable { get => Serialize.BrushToString(PrevDayLow_Color); set => PrevDayLow_Color = Serialize.StringToBrush(value); }
        [Range(1, 100)] [NinjaScriptProperty] [Display(Name = "PDL Opacity (%)", GroupName = "6. Reference - Prev Day Low")] public int PrevDayLow_Opacity { get; set; }
        [Display(Name = "PDL Style", GroupName = "6. Reference - Prev Day Low")] public DashStyleHelper PrevDayLow_Style { get; set; }
        [NinjaScriptProperty] [Display(Name = "PDL Show Label", GroupName = "6. Reference - Prev Day Low")] public bool PrevDayLow_ShowLabel { get; set; }

        [NinjaScriptProperty] [Display(Name = "Enable Level Merging", GroupName = "7. Advanced - Level Merging")] public bool EnableLevelMerging { get; set; }
        [NinjaScriptProperty] [Display(Name = "Merging Threshold (Points)", GroupName = "7. Advanced - Level Merging")] [Range(0.1, 50.0)] public double MergingThreshold { get; set; }
        [NinjaScriptProperty] [Display(Name = "Use Auto Scaling", GroupName = "7. Advanced - Level Merging")] public bool UseAutoScaling { get; set; }

        [NinjaScriptProperty] [Display(Name = "Show Opening Range", GroupName = "6. Reference - Opening Range")] public bool ShowOpeningRange { get; set; }
        [NinjaScriptProperty] [Display(Name = "OR Start Time", GroupName = "6. Reference - Opening Range")] public TimeSpan ORStartTime { get; set; }
        [NinjaScriptProperty] [Display(Name = "OR End Time", GroupName = "6. Reference - Opening Range")] public TimeSpan OREndTime { get; set; }
        [Range(1, 10)] [NinjaScriptProperty] [Display(Name = "ORH Thickness", GroupName = "6. Reference - Opening Range")] public int ORHigh_Thickness { get; set; }
        [XmlIgnore] [Display(Name = "ORH Color", GroupName = "6. Reference - Opening Range")] public Brush ORHigh_Color { get; set; }
        [Browsable(false)] public string ORHigh_ColorSerializable { get => Serialize.BrushToString(ORHigh_Color); set => ORHigh_Color = Serialize.StringToBrush(value); }
        [Range(1, 100)] [NinjaScriptProperty] [Display(Name = "ORH Opacity (%)", GroupName = "6. Reference - Opening Range")] public int ORHigh_Opacity { get; set; }
        [Display(Name = "ORH Style", GroupName = "6. Reference - Opening Range")] public DashStyleHelper ORHigh_Style { get; set; }
        [NinjaScriptProperty] [Display(Name = "ORH Show Label", GroupName = "6. Reference - Opening Range")] public bool ORHigh_ShowLabel { get; set; }

        [Range(1, 10)] [NinjaScriptProperty] [Display(Name = "ORL Thickness", GroupName = "6. Reference - Opening Range")] public int ORLow_Thickness { get; set; }
        [XmlIgnore] [Display(Name = "ORL Color", GroupName = "6. Reference - Opening Range")] public Brush ORLow_Color { get; set; }
        [Browsable(false)] public string ORLow_ColorSerializable { get => Serialize.BrushToString(ORLow_Color); set => ORLow_Color = Serialize.StringToBrush(value); }
        [Range(1, 100)] [NinjaScriptProperty] [Display(Name = "ORL Opacity (%)", GroupName = "6. Reference - Opening Range")] public int ORLow_Opacity { get; set; }
        [Display(Name = "ORL Style", GroupName = "6. Reference - Opening Range")] public DashStyleHelper ORLow_Style { get; set; }
        [NinjaScriptProperty] [Display(Name = "ORL Show Label", GroupName = "6. Reference - Opening Range")] public bool ORLow_ShowLabel { get; set; }
        #endregion
    }
}