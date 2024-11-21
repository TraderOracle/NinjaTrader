using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.StrategyExecution;
using NinjaTrader.Core.FloatingPoint;
using System.Windows.Media;  // For Brush (Color)
using System.Windows;        // For DashStyleHelper
using NinjaTrader.NinjaScript.Strategy;  // Necessary for drawing

namespace NinjaTrader.NinjaScript.Indicators
{
    public class EqualHighLow : Indicator
    {
        // Define user-adjustable parameters
        private double tolerance = 0.0001;
        private int lookbackLength = 500;
        private int lineWidth = 1;
        private bool extendRight = false;
        private string lineStyle = "Solid";
        private Brush lineColorHigh = Brushes.Green;
        private Brush lineColorLow = Brushes.Red;

        private List<double> highList = new List<double>();
        private List<int> highTime = new List<int>();

        private List<double> lowList = new List<double>();
        private List<int> lowTime = new List<int>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Marks bars where High and Low are equal, or nearly equal with a tolerance";
                Name = "EqualHighLow";
                Calculate = MarketDataType.Last;
                IsOverlay = true;
            }
        }

        protected override void OnBarUpdate()
        {
            // Lookback limit for high and low tracking
            int minBar = CurrentBar - lookbackLength;

            // Add current high and low to their respective lists
            highList.Add(High[0]);
            highTime.Add(Time[0].ToString("yyyyMMddHHmmss").GetHashCode());  // Convert time to a unique integer

            lowList.Add(Low[0]);
            lowTime.Add(Time[0].ToString("yyyyMMddHHmmss").GetHashCode());  // Convert time to a unique integer

            // Remove items outside the lookback range
            if (highList.Count > lookbackLength)
            {
                highList.RemoveAt(0);
                highTime.RemoveAt(0);
            }
            if (lowList.Count > lookbackLength)
            {
                lowList.RemoveAt(0);
                lowTime.RemoveAt(0);
            }

            // Check for equal highs and lows
            for (int i = 0; i < highList.Count; i++)
            {
                // Compare each high with the current high
                if (Math.Abs(High[0] - highList[i]) <= tolerance)
                {
                    // Draw a line for equal highs
                    DrawEqualLine(highList[i], highTime[i], High[0], Time[0], lineColorHigh);
                }

                // Compare each low with the current low
                if (Math.Abs(Low[0] - lowList[i]) <= tolerance)
                {
                    // Draw a line for equal lows
                    DrawEqualLine(lowList[i], lowTime[i], Low[0], Time[0], lineColorLow);
                }
            }
        }

        private void DrawEqualLine(double price, int startTime, double endPrice, DateTime endTime, Brush lineColor)
        {
            string lineTag = "EqualHighLowLine" + price.ToString() + startTime.ToString();  // Unique tag

            // Line style selection
            DashStyleHelper dashStyle = DashStyleHelper.Solid;
            if (lineStyle == "Dash")
                dashStyle = DashStyleHelper.Dash;
            else if (lineStyle == "Dotted")
                dashStyle = DashStyleHelper.Dot;

            // Draw the line
            Draw.Line(this, lineTag, 0, price, -1, price, lineColor, dashStyle, lineWidth);

            // Optionally extend the line to the right
            if (extendRight)
            {
                // Extend the line beyond the current bar
                Draw.Line(this, lineTag + "Extend", 0, price, CurrentBar + 1, price, lineColor, dashStyle, lineWidth);
            }
        }

        #region Properties

        // Allow user customization of the appearance and settings
        [NinjaScriptProperty]
        public double Tolerance
        {
            get { return tolerance; }
            set { tolerance = value; }
        }

        [NinjaScriptProperty]
        public int LookbackLength
        {
            get { return lookbackLength; }
            set { lookbackLength = value; }
        }

        [NinjaScriptProperty]
        public int LineWidth
        {
            get { return lineWidth; }
            set { lineWidth = value; }
        }

        [NinjaScriptProperty]
        public bool ExtendRight
        {
            get { return extendRight; }
            set { extendRight = value; }
        }

        [NinjaScriptProperty]
        public string LineStyle
        {
            get { return lineStyle; }
            set { lineStyle = value; }
        }

        [NinjaScriptProperty]
        public Brush LineColorHigh
        {
            get { return lineColorHigh; }
            set { lineColorHigh = value; }
        }

        [NinjaScriptProperty]
        public Brush LineColorLow
        {
            get { return lineColorLow; }
            set { lineColorLow = value; }
        }

        #endregion
    }
}