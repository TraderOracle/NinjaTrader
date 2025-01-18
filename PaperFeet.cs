
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
	public class PaperFeet : Indicator
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"PaperFeet";
				Name										= "PaperFeet";
				Calculate									= Calculate.OnEachTick;
				IsOverlay									= false;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= false;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive					= true;
				nFE					= 8;
				alertOn					= false;
				Glength					= 13;
				betaDev					= 8;
                data = DataTypeEnum.close;

                AddPlot(new Stroke(Brushes.WhiteSmoke, 3), PlotStyle.Line, "RSI");
				AddPlot(Brushes.Red, "OS");
				AddPlot(Brushes.Green, "OB");
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Hash, "M");
                AddPlot(Brushes.Transparent, "gamma");
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Hash, "FEh");
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Hash, "FEl");
			
            }
			else if (State == State.Configure)
			{
                w = new Series<double>(this);
                beta = new Series<double>(this);
                alpha= new Series<double>(this);
                Go = new Series<double>(this);
                Gh = new Series<double>(this);
                Gl = new Series<double>(this);
                Gc = new Series<double>(this);
				
                o = new Series<double>(this);
                h = new Series<double>(this);
                l = new Series<double>(this);
                c = new Series<double>(this);
                CU1 = new Series<double>(this);
                CU2 = new Series<double>(this);
                CU = new Series<double>(this);
                CD1 = new Series<double>(this);
                CD2 = new Series<double>(this);
                CD = new Series<double>(this);
                L0 = new Series<double>(this);
                L1 = new Series<double>(this);
                L2 = new Series<double>(this);
                L3 = new Series<double>(this);

                s1 = new Series<double>(this);

				HAOpen = new Series<double>(this);
                HAHigh = new Series<double>(this);
                HALow = new Series<double>(this);
                HAClose = new Series<double>(this);
            }
		}

        Series<double> w, beta, alpha, Go, Gh, Gl, Gc, o, h, l, c, CU1, CU2, CU, CD1, CD2, CD, L0, L1, L2, L3;
        Series<double> s1, HAOpen, HAHigh, HALow, HAClose;
        bool inited;
        protected override void OnBarUpdate()
		{
            if (CurrentBar < 4) 
                return;

            w[0] = (2 * Math.PI / Glength);
            beta[0] = (1 - Math.Cos(w[0])) / (Math.Pow(1.414, 2.0 / betaDev) - 1);
            alpha[0] = (-beta[0] + Math.Sqrt(beta[0] * beta[0] + 2 * beta[0]));
            Go[0] = Math.Pow(alpha[0], 4) * Open[0] +
                        4 * (1 - alpha[0]) *Go[1] - 6 * Math.Pow(1 - alpha[0], 2) * Go[2] +
                        4 * Math.Pow(1 - alpha[0], 3) * Go[3] - Math.Pow(1 - alpha[0], 4) * Go[4];
            Gh[0] = Math.Pow(alpha[0], 4) * High[0] +
                        4 * (1 - alpha[0]) *Gh[1] - 6 * Math.Pow(1 - alpha[0], 2) * Gh[2] +
                        4 * Math.Pow(1 - alpha[0], 3) * Gh[3] - Math.Pow(1 - alpha[0], 4) * Gh[4];
            Gl[0] = Math.Pow(alpha[0], 4) * Low[0] +
                        4 * (1 - alpha[0]) *Gl[1] - 6 * Math.Pow(1 - alpha[0], 2) * Gl[2] +
                        4 * Math.Pow(1 - alpha[0], 3) * Gl[3] - Math.Pow(1 - alpha[0], 4) * Gl[4];
            Gc[0] = Math.Pow(alpha[0], 4) * data_[0] +
                        4 * (1 - alpha[0]) *Gc[1] - 6 * Math.Pow(1 - alpha[0], 2) * Gc[2] +
                        4 * Math.Pow(1 - alpha[0], 3) * Gc[3] - Math.Pow(1 - alpha[0], 4) * Gc[4];

            o[0] = (Go[0] + Gc[1]) / 2;
            h[0] = Math.Max(Gh[0], Gc[1]);
            l[0] = Math.Min(Gl[0], Gc[1]);
            c[0] = (o[0] + h[0] + l[0] + Gc[0]) / 4;

            s1[0] = (Math.Max(Gh[0], Gc[1]) - Math.Min(Gl[0], Gc[1]));
            gamma[0] = Math.Log(SUM(s1, nFE)[0] /
                    (MAX(Gh, nFE)[0] - MIN(Gl, nFE)[0]))
                        / Math.Log(nFE);
            L0[0] = (1.0 - gamma[0]) * Gc[0] + (!inited ? 0 : gamma[0] * L0[1]) ;
            L1[0] = -gamma[0] * L0[0] + (!inited ? 0 : L0[1]) + (!inited ? 0 : gamma[0] * L1[1]);
            L2[0] = -gamma[0] * L1[0] + (!inited ? 0 : L1[1]) + (!inited ? 0 : gamma[0] * L2[1]);
            L3[0] = -gamma[0] * L2[0] + (!inited ? 0 : L2[1]) + (!inited ? 0 : gamma[0] * L3[1]);
            if (L0[0] >= L1[0])
             {
                CU1[0] = L0[0] - L1[0];
                CD1[0] = 0;
            } 
            else
            {
                CD1[0] = L1[0] - L0[0];
                CU1[0] = 0;
            }
            if (L1[0] >= L2[0])
            {
                CU2[0] = CU1[0] + L1[0] - L2[0];
                CD2[0] = CD1[0];
            }
            else
            {
                CD2[0] = CD1[0] + L2[0] - L1[0];
                CU2[0] = CU1[0];
            }
            if (L2[0] >= L3[0])
            {
                CU[0] = CU2[0] + L2[0] - L3[0];
                CD[0] = CD2[0];
            }
            else
            {
                CU[0] = CU2[0];
                CD[0] = CD2[0] + L3[0] - L2[0];
            }

            RSI[0] = (CU[0] + CD[0] != 0 ? CU[0] / (CU[0] + CD[0]) : 0); 
            OS[0] = 0.2;     //OS.HideBubble(); OS.HideTitle();
            OB[0] = 0.8;     //OB.HideBubble(); OB.HideTitle();
            M[0] = 0.5;      //M.SetStyle(Curve.long_dash);   //M.HideBubble(); M.HideTitle();
            FEh[0] = 0.618;  //FEh.SetStyle(Curve.short_DASH); //FEh.HideBubble(); FEh.HideTitle();
            FEl[0] = 0.382;  //FEl.SetStyle(Curve.short_DASH);  // FEl.HideBubble(); FEl.HideTitle();
            if (alertOn)
            {
                if (CrossBelow(RSI, 0.8, 1))
                {
                    Alert(CurrentBar + "CrossBelow", Priority.Medium, "Rsi Cross Below", SoundFileName_, 0, Brushes.Black, Brushes.Orange);
                    lastAlertedBar = CurrentBar;
                }
                if (CrossAbove(RSI, 0.2, 1))
                {
                    Alert(CurrentBar + "CrossAbove", Priority.Medium, "Rsi Cross Above", SoundFileName_, 0, Brushes.Black, Brushes.Orange);
                    lastAlertedBar = CurrentBar;
                }
            }

			Draw.Region(this, "OS", CurrentBar, 0, OS, 0, Brushes.Red, 50);
            Draw.Region(this, "OB", CurrentBar, 0, OB, 1, Brushes.Green, 50);
			
			HAClose[0]	=	((Open[0] + High[0] + Low[0] + Close[0]) * 0.25); // Calculate the close
            HAOpen[0]	=	((HAOpen[1] + HAClose[1]) * 0.5); // Calculate the open
            HAHigh[0]	=	(Math.Max(High[0], HAOpen[0])); // Calculate the high
            HALow[0]	=	(Math.Min(Low[0], HAOpen[0])); // Calculate the low	
			
			if (HAClose[0] > HAOpen[0] && HAClose[1] > HAOpen[1])
				Draw.Dot(this, "howdy1" + CurrentBar, false, 0, 0.5, Brushes.Lime);
			else if ((HAClose[0] > HAOpen[0] && HAClose[1] < HAOpen[1]) || (HAClose[0] < HAOpen[0] && HAClose[1] > HAOpen[1]))
				Draw.Dot(this, "howdy2" + CurrentBar, false, 0, 0.5, Brushes.Yellow);
			else if (HAClose[0] < HAOpen[0] && HAClose[1] < HAOpen[1])
				Draw.Dot(this, "howdy3" + CurrentBar, false, 0, 0.5, Brushes.Red);
			
            inited = true;
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "NFE", Order = 1, GroupName = "Parameters")]
        public int nFE
        { get; set; }

        [NinjaScriptProperty]
		[Display(Name="AlertOn", Order=2, GroupName="Parameters")]
		public bool alertOn
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Glength", Order=3, GroupName="Parameters")]
		public int Glength
		{ get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "BetaDev", Order = 4, GroupName = "Parameters")]
        public int betaDev
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "data", Order=5, GroupName = "Parameters")]
        public NinjaTrader.NinjaScript.Indicators.PaperFeet.DataTypeEnum data
        { get; set; }

        public enum DataTypeEnum { open, high, low, close }
        [Browsable(false)]
        [XmlIgnore]
        public ISeries<double> data_ { get { return (data == DataTypeEnum.open ? Open : (data == DataTypeEnum.high ? High : (data == DataTypeEnum.low ? Low : (data == DataTypeEnum.close ? Close : null)))); } }

        [NinjaScriptProperty]
        [Display(Name = "Enable Alert sound", Order = 5, GroupName = "Alerts")]
        public bool EnableAlerts_
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display(Name = "Alert sound", Order = 10, GroupName = "Alerts")]
        public string SoundFileName_
        {
            get { return soundFileName_; }
            set { soundFileName_ = value; }
        }
        private string soundFileName_ = NinjaTrader.Core.Globals.InstallDir + @"sounds\Alert3.wav";

        [NinjaScriptProperty]
        [Display(Name = "alert once per bar", Order = 15, GroupName = "Alerts")]
        public bool AlertOncePerBar_
        {
            get { return alertOncePerBar_; }
            set { alertOncePerBar_ = value; }
        }
        private bool alertOncePerBar_ = true;
        private int lastAlertedBar = 0;

        //
        [Browsable(false)]
		[XmlIgnore]
		public Series<double> RSI
		{
			get { return Values[0]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> OS
		{
			get { return Values[1]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> OB
		{
			get { return Values[2]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> M
		{
			get { return Values[3]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> gamma
        {
			get { return Values[4]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> FEh
		{
			get { return Values[5]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> FEl
		{
			get { return Values[6]; }
		} 
		
        #endregion

    }
}
