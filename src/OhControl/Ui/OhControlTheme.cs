using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OhControl.Ui
{
    internal static class OhControlTheme
    {
        public static readonly Color Background = Color.FromArgb(18, 21, 25);
        public static readonly Color Surface = Color.FromArgb(27, 31, 36);
        public static readonly Color SurfaceRaised = Color.FromArgb(34, 39, 45);
        public static readonly Color Border = Color.FromArgb(53, 60, 68);
        public static readonly Color TextPrimary = Color.FromArgb(236, 239, 242);
        public static readonly Color TextSecondary = Color.FromArgb(157, 166, 176);
        public static readonly Color Accent = Color.FromArgb(91, 167, 129);
        public static readonly Color AccentMuted = Color.FromArgb(39, 75, 58);
        public static readonly Color Warning = Color.FromArgb(219, 168, 76);
        public static readonly Color WarningMuted = Color.FromArgb(78, 61, 28);
        public static readonly Color Danger = Color.FromArgb(215, 91, 91);
        public static readonly Color DangerMuted = Color.FromArgb(79, 37, 39);
        public static readonly Color Radio = Color.FromArgb(117, 177, 222);
        public static readonly Color RadioMuted = Color.FromArgb(37, 60, 79);

        public static Font Font(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }

        public static void StylePrimaryButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Accent;
            button.ForeColor = Color.FromArgb(13, 24, 19);
            button.Font = Font(9.5f, FontStyle.Bold);
            button.Height = 36;
            button.Padding = new Padding(14, 0, 14, 0);
            button.Cursor = Cursors.Hand;
        }

        public static void StyleSecondaryButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = SurfaceRaised;
            button.ForeColor = TextPrimary;
            button.Font = Font(9.5f, FontStyle.Regular);
            button.Height = 36;
            button.Padding = new Padding(12, 0, 12, 0);
            button.Cursor = Cursors.Hand;
        }

        public static void StyleTextBox(TextBox textBox)
        {
            textBox.BackColor = SurfaceRaised;
            textBox.ForeColor = TextPrimary;
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.Font = Font(9.5f);
        }

        public static void StyleComboBox(ComboBox comboBox)
        {
            comboBox.BackColor = SurfaceRaised;
            comboBox.ForeColor = TextPrimary;
            comboBox.FlatStyle = FlatStyle.Flat;
            comboBox.Font = Font(9.5f);
        }
    }

    internal sealed class AviationCard : Panel
    {
        public AviationCard()
        {
            BackColor = OhControlTheme.Surface;
            Padding = new Padding(18);
            Margin = new Padding(0, 0, 12, 12);
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            using (var pen = new Pen(OhControlTheme.Border))
            {
                var rect = ClientRectangle;
                rect.Width -= 1;
                rect.Height -= 1;
                e.Graphics.DrawRectangle(pen, rect);
            }
        }
    }

    internal sealed class StatusPill : Label
    {
        public StatusPill()
        {
            AutoSize = true;
            Font = OhControlTheme.Font(8.5f, FontStyle.Bold);
            ForeColor = OhControlTheme.TextSecondary;
            BackColor = OhControlTheme.SurfaceRaised;
            Padding = new Padding(10, 5, 10, 5);
            TextAlign = ContentAlignment.MiddleCenter;
        }

        public void SetNeutral(string text)
        {
            Text = text;
            ForeColor = OhControlTheme.TextSecondary;
            BackColor = OhControlTheme.SurfaceRaised;
        }

        public void SetGood(string text)
        {
            Text = text;
            ForeColor = OhControlTheme.Accent;
            BackColor = OhControlTheme.AccentMuted;
        }

        public void SetWarning(string text)
        {
            Text = text;
            ForeColor = OhControlTheme.Warning;
            BackColor = OhControlTheme.WarningMuted;
        }

        public void SetDanger(string text)
        {
            Text = text;
            ForeColor = OhControlTheme.Danger;
            BackColor = OhControlTheme.DangerMuted;
        }

        public void SetRadio(string text)
        {
            Text = text;
            ForeColor = OhControlTheme.Radio;
            BackColor = OhControlTheme.RadioMuted;
        }
    }
}
