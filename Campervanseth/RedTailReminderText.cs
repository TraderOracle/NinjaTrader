#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
#endregion

// Enum declared at global scope so NT8's auto-generated indicator code can't collide with it.
public enum RedTailReminderAlign
{
	Left,
	Center,
	Right
}

namespace NinjaTrader.NinjaScript.Indicators
{
	/// <summary>
	/// RedTailReminderText
	/// Multi-line reminder note, horizontally centered on the chart panel.
	/// Alignment controls how the lines sit inside the centered block, not where the block sits.
	/// The first line can carry its own font / size / bold / italic.
	/// Text is typed line-by-line, or loaded from a .txt file that auto-refreshes on save.
	/// </summary>
	public class RedTailReminderText : Indicator
	{
		#region Text file cache
		private DateTime	lastFileCheck		= DateTime.MinValue;
		private DateTime	lastFileWriteUtc	= DateTime.MinValue;
		private string		cachedFileText		= string.Empty;
		private string		lastCheckedPath		= string.Empty;
		#endregion

		#region State
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= @"Multi-line reminder note centered at the top of the chart panel.";
				Name						= "RedTailReminderText";
				Calculate					= Calculate.OnPriceChange;
				IsOverlay					= true;
				DrawOnPricePanel			= true;
				DisplayInDataBox			= false;
				PaintPriceMarkers			= false;
				IsAutoScale					= false;
				IsSuspendedWhileInactive	= true;

				// Text source
				UseTextFile		= false;
				TextFilePath	= string.Empty;

				Line1	= "Daily Reminder";
				Line2	= string.Empty;
				Line3	= string.Empty;
				Line4	= string.Empty;
				Line5	= string.Empty;
				Line6	= string.Empty;
				Line7	= string.Empty;
				Line8	= string.Empty;
				Line9	= string.Empty;
				Line10	= string.Empty;

				// Appearance
				ReminderFont	= new SimpleFont("Arial", 15);
				TextBrush		= Brushes.Gold;
				Opacity			= 100;
				LineSpacing		= 0;

				// First line style
				StyleFirstLine		= true;
				FirstLineFont		= new SimpleFont("Arial", 18) { Bold = true };
				FirstLineAlignment	= RedTailReminderAlign.Center;
				FirstLineGap		= 2;
				UseFirstLineColor	= false;
				FirstLineBrush		= Brushes.White;

				// Placement
				Alignment	= RedTailReminderAlign.Center;
				XOffset		= 0;
				YOffset		= 10;
			}
			else if (State == State.Configure)
			{
				if (TextBrush != null && !TextBrush.IsFrozen && TextBrush.CanFreeze)
					TextBrush.Freeze();

				if (FirstLineBrush != null && !FirstLineBrush.IsFrozen && FirstLineBrush.CanFreeze)
					FirstLineBrush.Freeze();
			}
		}

		protected override void OnBarUpdate() { /* nothing to calculate */ }
		#endregion

		#region Text assembly
		/// <summary>Builds the display string from whichever source is active.</summary>
		private string BuildText()
		{
			if (UseTextFile)
				return GetFileText();

			List<string> lines = new List<string>
			{
				Line1 ?? string.Empty, Line2 ?? string.Empty, Line3 ?? string.Empty,
				Line4 ?? string.Empty, Line5 ?? string.Empty, Line6 ?? string.Empty,
				Line7 ?? string.Empty, Line8 ?? string.Empty, Line9 ?? string.Empty,
				Line10 ?? string.Empty
			};

			// Trim trailing blanks so unused fields don't pad the block.
			int last = lines.Count - 1;
			while (last >= 0 && string.IsNullOrWhiteSpace(lines[last]))
				last--;

			if (last < 0)
				return string.Empty;

			return string.Join("\n", lines.GetRange(0, last + 1).ToArray());
		}

		/// <summary>Reads the note file, re-reading only when it changes on disk.</summary>
		private string GetFileText()
		{
			if (string.IsNullOrWhiteSpace(TextFilePath))
				return string.Empty;

			// Throttle disk checks to once per second.
			DateTime now = DateTime.UtcNow;
			if (TextFilePath == lastCheckedPath && (now - lastFileCheck).TotalMilliseconds < 1000)
				return cachedFileText;

			lastFileCheck	= now;
			lastCheckedPath	= TextFilePath;

			try
			{
				if (!File.Exists(TextFilePath))
				{
					cachedFileText = string.Empty;
					return cachedFileText;
				}

				DateTime writeUtc = File.GetLastWriteTimeUtc(TextFilePath);
				if (writeUtc == lastFileWriteUtc)
					return cachedFileText;

				lastFileWriteUtc = writeUtc;

				// FileShare.ReadWrite so Notepad can hold the file open.
				using (FileStream fs = new FileStream(TextFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				using (StreamReader sr = new StreamReader(fs))
					cachedFileText = sr.ReadToEnd();

				cachedFileText = cachedFileText.TrimEnd('\r', '\n');
			}
			catch (Exception ex)
			{
				Print("RedTailReminderText: could not read " + TextFilePath + " - " + ex.Message);
			}

			return cachedFileText;
		}
		#endregion

		#region Render
		/// <summary>Maps our alignment enum to a DirectWrite text alignment.</summary>
		private static SharpDX.DirectWrite.TextAlignment ToDwAlign(RedTailReminderAlign align)
		{
			switch (align)
			{
				case RedTailReminderAlign.Left:		return SharpDX.DirectWrite.TextAlignment.Leading;
				case RedTailReminderAlign.Right:		return SharpDX.DirectWrite.TextAlignment.Trailing;
				default:							return SharpDX.DirectWrite.TextAlignment.Center;
			}
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (RenderTarget == null || chartControl == null || ChartPanel == null)
				return;

			string text = BuildText();
			if (string.IsNullOrWhiteSpace(text))
				return;

			// Normalize line breaks, and still honor a literal "\n" typed into a line field.
			text = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\\n", "\n");

			// Split line 1 from the rest. When first-line styling is off, everything is "body".
			string headText = string.Empty;
			string bodyText = text;

			if (StyleFirstLine)
			{
				int nl = text.IndexOf('\n');
				if (nl < 0)
				{
					headText = text;
					bodyText = string.Empty;
				}
				else
				{
					headText = text.Substring(0, nl);
					bodyText = text.Substring(nl + 1);
				}
			}

			bool hasHead = !string.IsNullOrWhiteSpace(headText);
			bool hasBody = !string.IsNullOrWhiteSpace(bodyText);
			if (!hasHead && !hasBody)
				return;

			SharpDX.DirectWrite.TextFormat	headFormat	= null;
			SharpDX.DirectWrite.TextFormat	bodyFormat	= null;
			SharpDX.DirectWrite.TextLayout	headLayout	= null;
			SharpDX.DirectWrite.TextLayout	bodyLayout	= null;
			SharpDX.Direct2D1.Brush			dxHeadBrush	= null;
			SharpDX.Direct2D1.Brush			dxBodyBrush	= null;

			try
			{
				float panelW = ChartPanel.W;
				float panelH = ChartPanel.H;

				// ---- Build the header layout (its own font, measured left-aligned first) ----
				if (hasHead)
				{
					SimpleFont hf = FirstLineFont ?? ReminderFont;
					headFormat					= hf.ToDirectWriteTextFormat();
					headFormat.WordWrapping		= SharpDX.DirectWrite.WordWrapping.NoWrap;
					headFormat.TextAlignment	= SharpDX.DirectWrite.TextAlignment.Leading;

					headLayout = new SharpDX.DirectWrite.TextLayout(
						NinjaTrader.Core.Globals.DirectWriteFactory, headText, headFormat, panelW, panelH);
				}

				// ---- Build the body layout ----
				if (hasBody)
				{
					bodyFormat					= ReminderFont.ToDirectWriteTextFormat();
					bodyFormat.WordWrapping		= SharpDX.DirectWrite.WordWrapping.NoWrap;
					bodyFormat.TextAlignment	= SharpDX.DirectWrite.TextAlignment.Leading;

					bodyLayout = new SharpDX.DirectWrite.TextLayout(
						NinjaTrader.Core.Globals.DirectWriteFactory, bodyText, bodyFormat, panelW, panelH);

					if (LineSpacing != 0)
					{
						float lineHeight = Math.Max(1f, (float)ReminderFont.Size * 1.35f + LineSpacing);
						bodyLayout.SetLineSpacing(
							SharpDX.DirectWrite.LineSpacingMethod.Uniform, lineHeight, lineHeight * 0.8f);
					}
				}

				// ---- Block width = widest line across BOTH layouts ----
				float headWidth	= (headLayout != null) ? headLayout.Metrics.Width : 0f;
				float bodyWidth	= (bodyLayout != null) ? bodyLayout.Metrics.Width : 0f;
				float blockWidth = Math.Min(Math.Max(headWidth, bodyWidth), panelW);

				if (blockWidth <= 0f)
					return;

				// Each layout gets the same box, so alignment happens against the widest line,
				// and the header can align independently of the body.
				if (headLayout != null)
				{
					headLayout.MaxWidth			= blockWidth;
					headLayout.TextAlignment	= ToDwAlign(FirstLineAlignment);
				}

				if (bodyLayout != null)
				{
					bodyLayout.MaxWidth			= blockWidth;
					bodyLayout.TextAlignment	= ToDwAlign(Alignment);
				}

				// The block itself is always centered on the panel. Offsets nudge it.
				float x = ChartPanel.X + (panelW - blockWidth) / 2f + XOffset;
				float y = ChartPanel.Y + YOffset;

				// ---- Brushes: header truly gets its own, because it's a separate draw call ----
				System.Windows.Media.Brush wpfBody = TextBrush ?? Brushes.White;
				dxBodyBrush			= wpfBody.ToDxBrush(RenderTarget);
				dxBodyBrush.Opacity	= Opacity / 100f;

				if (hasHead)
				{
					System.Windows.Media.Brush wpfHead =
						(UseFirstLineColor && FirstLineBrush != null) ? FirstLineBrush : wpfBody;

					dxHeadBrush			= wpfHead.ToDxBrush(RenderTarget);
					dxHeadBrush.Opacity	= Opacity / 100f;
				}

				SharpDX.Direct2D1.AntialiasMode prior = RenderTarget.AntialiasMode;
				RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;

				if (headLayout != null)
				{
					RenderTarget.DrawTextLayout(new SharpDX.Vector2(x, y), headLayout, dxHeadBrush);
					y += headLayout.Metrics.Height + FirstLineGap;
				}

				if (bodyLayout != null)
					RenderTarget.DrawTextLayout(new SharpDX.Vector2(x, y), bodyLayout, dxBodyBrush);

				RenderTarget.AntialiasMode = prior;
			}
			catch (Exception ex)
			{
				Print("RedTailReminderText render error: " + ex.Message);
			}
			finally
			{
				if (dxHeadBrush != null)	dxHeadBrush.Dispose();
				if (dxBodyBrush != null)	dxBodyBrush.Dispose();
				if (headLayout != null)		headLayout.Dispose();
				if (bodyLayout != null)		bodyLayout.Dispose();
				if (headFormat != null)		headFormat.Dispose();
				if (bodyFormat != null)		bodyFormat.Dispose();
			}
		}
		#endregion

		#region Properties

		// ---------- 01. Text source ----------
		[Display(Name = "Use text file", Description = "Read the note from a .txt file instead of the line fields. Edit in Notepad; the chart refreshes on save.", Order = 1, GroupName = "01. Text source")]
		public bool UseTextFile { get; set; }

		[Display(Name = "Text file", Description = "Path to a .txt file containing your reminder", Order = 2, GroupName = "01. Text source")]
		[PropertyEditor("NinjaTrader.Gui.Tools.FilePathPicker")]
		public string TextFilePath { get; set; }

		// ---------- 02. Lines ----------
		[Display(Name = "Line 1",  Order = 1,  GroupName = "02. Lines")] public string Line1  { get; set; }
		[Display(Name = "Line 2",  Order = 2,  GroupName = "02. Lines")] public string Line2  { get; set; }
		[Display(Name = "Line 3",  Order = 3,  GroupName = "02. Lines")] public string Line3  { get; set; }
		[Display(Name = "Line 4",  Order = 4,  GroupName = "02. Lines")] public string Line4  { get; set; }
		[Display(Name = "Line 5",  Order = 5,  GroupName = "02. Lines")] public string Line5  { get; set; }
		[Display(Name = "Line 6",  Order = 6,  GroupName = "02. Lines")] public string Line6  { get; set; }
		[Display(Name = "Line 7",  Order = 7,  GroupName = "02. Lines")] public string Line7  { get; set; }
		[Display(Name = "Line 8",  Order = 8,  GroupName = "02. Lines")] public string Line8  { get; set; }
		[Display(Name = "Line 9",  Order = 9,  GroupName = "02. Lines")] public string Line9  { get; set; }
		[Display(Name = "Line 10", Order = 10, GroupName = "02. Lines")] public string Line10 { get; set; }

		// ---------- 03. First line style ----------
		[Display(Name = "Style first line separately", Description = "Give line 1 its own font, size, bold and italic", Order = 1, GroupName = "03. First line style")]
		public bool StyleFirstLine { get; set; }

		[Display(Name = "First line font", Description = "Font family, size, bold, italic for line 1", Order = 2, GroupName = "03. First line style")]
		public SimpleFont FirstLineFont { get; set; }

		[Display(Name = "First line alignment", Description = "How line 1 sits inside the centered block, independent of the body", Order = 3, GroupName = "03. First line style")]
		public RedTailReminderAlign FirstLineAlignment { get; set; }

		[Range(-40, 80)]
		[Display(Name = "Gap below first line", Description = "Extra pixels between line 1 and the body", Order = 4, GroupName = "03. First line style")]
		public int FirstLineGap { get; set; }

		[Display(Name = "First line uses own color", Order = 5, GroupName = "03. First line style")]
		public bool UseFirstLineColor { get; set; }

		[XmlIgnore]
		[Display(Name = "First line color", Order = 6, GroupName = "03. First line style")]
		public System.Windows.Media.Brush FirstLineBrush { get; set; }

		[Browsable(false)]
		public string FirstLineBrushSerialize
		{
			get { return Serialize.BrushToString(FirstLineBrush); }
			set { FirstLineBrush = Serialize.StringToBrush(value); }
		}

		// ---------- 04. Appearance ----------
		[Display(Name = "Body font & size", Description = "Font family, size, bold/italic for lines 2+", Order = 1, GroupName = "04. Appearance")]
		public SimpleFont ReminderFont { get; set; }

		[XmlIgnore]
		[Display(Name = "Text color", Order = 2, GroupName = "04. Appearance")]
		public System.Windows.Media.Brush TextBrush { get; set; }

		[Browsable(false)]
		public string TextBrushSerialize
		{
			get { return Serialize.BrushToString(TextBrush); }
			set { TextBrush = Serialize.StringToBrush(value); }
		}

		[Range(1, 100)]
		[Display(Name = "Opacity %", Order = 3, GroupName = "04. Appearance")]
		public int Opacity { get; set; }

		[Range(-20, 60)]
		[Display(Name = "Extra line spacing", Description = "Extra pixels between lines. 0 = automatic", Order = 4, GroupName = "04. Appearance")]
		public int LineSpacing { get; set; }

		// ---------- 05. Placement ----------
		[Display(Name = "Alignment", Description = "How lines sit inside the centered block: Left, Center or Right", Order = 1, GroupName = "05. Placement")]
		public RedTailReminderAlign Alignment { get; set; }

		[Range(-10000, 10000)]
		[Display(Name = "X offset", Description = "Positive moves the whole block right", Order = 2, GroupName = "05. Placement")]
		public int XOffset { get; set; }

		[Range(-10000, 10000)]
		[Display(Name = "Y offset", Description = "Positive moves the whole block down from the top of the panel", Order = 3, GroupName = "05. Placement")]
		public int YOffset { get; set; }

		#endregion
	}
}
