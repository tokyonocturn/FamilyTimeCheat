using System.Drawing;
using System.Windows.Forms;

namespace FamilyTimeChef.Studio
{
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(19, 21, 26);
        public static readonly Color Panel = Color.FromArgb(27, 30, 37);
        public static readonly Color PanelAlt = Color.FromArgb(33, 37, 45);
        public static readonly Color Border = Color.FromArgb(48, 53, 63);
        public static readonly Color Text = Color.FromArgb(230, 232, 236);
        public static readonly Color Muted = Color.FromArgb(154, 160, 170);
        public static readonly Color Accent = Color.FromArgb(232, 163, 61);
        public static readonly Color AccentDark = Color.FromArgb(180, 122, 40);
        public static readonly Color Good = Color.FromArgb(93, 190, 128);
        public static readonly Color Warn = Color.FromArgb(226, 120, 106);

        public static readonly Font H1 = new Font("Segoe UI Semibold", 15f);
        public static readonly Font H2 = new Font("Segoe UI Semibold", 10.5f);
        public static readonly Font Body = new Font("Segoe UI", 9.75f);
        public static readonly Font Mono = new Font("Consolas", 9.25f);

        public static Button Primary(string text)
        {
            var b = new Button
            {
                Text = text,
                Height = 38,
                FlatStyle = FlatStyle.Flat,
                BackColor = Accent,
                ForeColor = Color.FromArgb(28, 22, 12),
                Font = H2,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(244, 178, 82);
            return b;
        }

        public static Button Secondary(string text)
        {
            var b = new Button
            {
                Text = text,
                Height = 38,
                FlatStyle = FlatStyle.Flat,
                BackColor = PanelAlt,
                ForeColor = Text,
                Font = Body,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 47, 57);
            return b;
        }

        public static CheckBox Toggle(string text)
        {
            return new CheckBox
            {
                Text = text,
                AutoSize = true,
                ForeColor = Text,
                BackColor = Color.Transparent,
                Font = Body,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
        }

        public static Label Caption(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Muted,
                BackColor = Color.Transparent,
                Font = Body
            };
        }
    }
}
