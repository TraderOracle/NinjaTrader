#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Windows.Media;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class CLPsychLevels : Indicator
    {
        private double lastReference = double.NaN;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "CLPsychLevels";
                Description = "CL psychological levels at .00, .20, .50 and .80, limited to configurable levels above and below price.";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = true;

                LookbackDays = 2;
                LevelsEachSide = 4;

                Show00 = true;
                Show20 = true;
                Show50 = true;
                Show80 = true;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            double reference = Math.Round(Close[0] * 10.0) / 10.0;
            if (reference == lastReference)
                return;

            lastReference = reference;
            RemoveDrawObjects();

            double price = Close[0];
            int eachSide = Math.Max(1, LevelsEachSide);
            List<double> above = new List<double>();
            List<double> below = new List<double>();

            int baseDollar = (int)Math.Floor(price);

            for (int d = -10; d <= 10; d++)
            {
                double whole = baseDollar + d;
                AddCandidate(whole, price, above, below);
                AddCandidate(whole + 0.20, price, above, below);
                AddCandidate(whole + 0.50, price, above, below);
                AddCandidate(whole + 0.80, price, above, below);
            }

            above.Sort();
            below.Sort();
            below.Reverse();

            for (int i = 0; i < Math.Min(eachSide, above.Count); i++)
                DrawPsychLevel("CLP_A_" + i, above[i]);

            for (int i = 0; i < Math.Min(eachSide, below.Count); i++)
                DrawPsychLevel("CLP_B_" + i, below[i]);
        }

        private void AddCandidate(double level, double price, List<double> above, List<double> below)
        {
            double rounded = Instrument.MasterInstrument.RoundToTickSize(level);

            if (!IsEnabledLevel(rounded))
                return;

            if (rounded >= price)
                above.Add(rounded);
            else
                below.Add(rounded);
        }

        private bool IsEnabledLevel(double price)
        {
            double frac = price - Math.Floor(price);
            int cents = (int)Math.Round(frac * 100.0);

            if (cents == 0)  return Show00;
            if (cents == 20) return Show20;
            if (cents == 50) return Show50;
            if (cents == 80) return Show80;
            return false;
        }

        private void DrawPsychLevel(string tag, double price)
        {
            double frac = price - Math.Floor(price);
            int cents = (int)Math.Round(frac * 100.0);

            Brush brush;

            // Three strong, high-contrast colors on a dark NinjaTrader chart.
            if (cents == 0)
                brush = Brushes.Lime;           // .00 whole dollar
            else if (cents == 20)
                brush = Brushes.DeepSkyBlue;    // .20
            else if (cents == 50)
                brush = Brushes.Orange;         // .50
            else
                brush = Brushes.Magenta;        // .80

            Draw.HorizontalLine(this, tag, price, brush);
        }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Lookback Days", Description = "Default 2. Reserved for session/history filtering.", Order = 1, GroupName = "Settings")]
        public int LookbackDays { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Levels Each Side", Description = "Number of nearby levels above and below current price.", Order = 2, GroupName = "Settings")]
        public int LevelsEachSide { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show .00", Order = 3, GroupName = "Levels")]
        public bool Show00 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show .20", Order = 4, GroupName = "Levels")]
        public bool Show20 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show .50", Order = 5, GroupName = "Levels")]
        public bool Show50 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show .80", Order = 6, GroupName = "Levels")]
        public bool Show80 { get; set; }
    }
}
