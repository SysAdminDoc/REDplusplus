using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RED.UI
{
    internal enum ThemeMode { Dark = 0, Light = 1, System = 2 }

    /// <summary>
    /// Holds the named palette colors. Two instances exist (Catppuccin Mocha dark
    /// and Latte light); <see cref="DarkTheme.Active"/> points at whichever the
    /// resolved theme selects. All <c>DarkTheme.Base</c>-style references read
    /// through properties so call sites need no change.
    /// </summary>
    internal sealed class ThemePalette
    {
        public Color Base, Mantle, Crust, Surface0, Surface1, Surface2, Overlay0;
        public Color Text, Subtext0, Subtext1;
        public Color Blue, Red, Green, Yellow, Lavender, Peach;
        public bool IsDark;
        public bool IsHighContrast;
    }

    internal static class DarkTheme
    {
        // Catppuccin Mocha (dark) — the default.
        private static readonly ThemePalette Mocha = new ThemePalette
        {
            IsDark = true,
            Base = Color.FromArgb(30, 30, 46),
            Mantle = Color.FromArgb(24, 24, 37),
            Crust = Color.FromArgb(17, 17, 27),
            Surface0 = Color.FromArgb(49, 50, 68),
            Surface1 = Color.FromArgb(69, 71, 90),
            Surface2 = Color.FromArgb(88, 91, 112),
            Overlay0 = Color.FromArgb(108, 112, 134),
            Text = Color.FromArgb(205, 214, 244),
            Subtext0 = Color.FromArgb(166, 173, 200),
            Subtext1 = Color.FromArgb(186, 194, 222),
            Blue = Color.FromArgb(137, 180, 250),
            Red = Color.FromArgb(243, 139, 168),
            Green = Color.FromArgb(166, 227, 161),
            Yellow = Color.FromArgb(249, 226, 175),
            Lavender = Color.FromArgb(180, 190, 254),
            Peach = Color.FromArgb(250, 179, 135),
        };

        // Catppuccin Latte (light). "Surface" tones darken (not lighten) so borders
        // and selection still read against the light Base.
        private static readonly ThemePalette Latte = new ThemePalette
        {
            IsDark = false,
            Base = Color.FromArgb(239, 241, 245),
            Mantle = Color.FromArgb(230, 233, 239),
            Crust = Color.FromArgb(220, 224, 232),
            Surface0 = Color.FromArgb(204, 208, 218),
            Surface1 = Color.FromArgb(188, 192, 204),
            Surface2 = Color.FromArgb(172, 176, 190),
            Overlay0 = Color.FromArgb(156, 160, 176),
            Text = Color.FromArgb(76, 79, 105),
            Subtext0 = Color.FromArgb(108, 111, 133),
            Subtext1 = Color.FromArgb(92, 95, 119),
            Blue = Color.FromArgb(30, 102, 245),
            Red = Color.FromArgb(210, 15, 57),
            Green = Color.FromArgb(64, 160, 43),
            Yellow = Color.FromArgb(223, 142, 29),
            Lavender = Color.FromArgb(114, 135, 253),
            Peach = Color.FromArgb(254, 100, 11),
        };

        private static readonly ThemePalette HighContrast = new ThemePalette
        {
            IsDark = true,
            IsHighContrast = true,
            Base = SystemColors.Window,
            Mantle = SystemColors.Window,
            Crust = SystemColors.Control,
            Surface0 = SystemColors.Control,
            Surface1 = SystemColors.Highlight,
            Surface2 = SystemColors.HotTrack,
            Overlay0 = SystemColors.GrayText,
            Text = SystemColors.WindowText,
            Subtext0 = SystemColors.ControlText,
            Subtext1 = SystemColors.ControlText,
            Blue = SystemColors.HotTrack,
            Red = SystemColors.Highlight,
            Green = SystemColors.Highlight,
            Yellow = SystemColors.InfoText,
            Lavender = SystemColors.HotTrack,
            Peach = SystemColors.Highlight,
        };

        internal static ThemePalette Active = Mocha;

        internal static Color Base => Active.Base;
        internal static Color Mantle => Active.Mantle;
        internal static Color Crust => Active.Crust;
        internal static Color Surface0 => Active.Surface0;
        internal static Color Surface1 => Active.Surface1;
        internal static Color Surface2 => Active.Surface2;
        internal static Color Overlay0 => Active.Overlay0;
        internal static Color Text => Active.Text;
        internal static Color Subtext0 => Active.Subtext0;
        internal static Color Subtext1 => Active.Subtext1;
        internal static Color Blue => Active.Blue;
        internal static Color Red => Active.Red;
        internal static Color Green => Active.Green;
        internal static Color Yellow => Active.Yellow;
        internal static Color Lavender => Active.Lavender;
        internal static Color Peach => Active.Peach;
        internal static bool IsHighContrast => Active.IsHighContrast;
        internal static bool IsDark => Active.IsDark;
        internal static Color Eligible => Active.IsHighContrast ? SystemColors.Highlight : Red;
        internal static Color Protected => Active.IsHighContrast ? SystemColors.HotTrack : Blue;
        internal static Color Kept => Active.IsHighContrast ? SystemColors.GrayText : Subtext0;
        internal static Color Warning => Active.IsHighContrast ? SystemColors.HighlightText : Yellow;
        internal static Color Focus => Active.IsHighContrast ? SystemColors.Highlight : Blue;
        internal static Color Button => Active.IsHighContrast ? SystemColors.Control : Surface0;
        internal static Color ButtonHover => Active.IsHighContrast ? SystemColors.Highlight : Surface1;
        internal static Color ButtonDown => Active.IsHighContrast ? SystemColors.HotTrack : Surface2;
        internal static Color DisabledText => Active.IsHighContrast ? SystemColors.GrayText : Overlay0;
        internal static Font UiFont => SystemFonts.MessageBoxFont;

        /// <summary>
        /// Selects the active palette. System reads the Windows app-theme registry
        /// value (AppsUseLightTheme); anything but an explicit 0 is treated as light.
        /// </summary>
        internal static void SetMode(ThemeMode mode)
        {
            if (SystemInformation.HighContrast)
            {
                Active = HighContrast;
                return;
            }

            bool light;
            switch (mode)
            {
                case ThemeMode.Light:
                    light = true;
                    break;
                case ThemeMode.System:
                    light = SystemUsesLightTheme();
                    break;
                default:
                    light = false;
                    break;
            }
            Active = light ? Latte : Mocha;
        }

        private static bool SystemUsesLightTheme()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = key?.GetValue("AppsUseLightTheme");
                    if (v is int i)
                    {
                        return i != 0;
                    }
                }
            }
            catch { }
            return false; // default to dark when unknown
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        internal static void Apply(Form form)
        {
            form.Font = UiFont;
            SetTitleBar(form.Handle);
            ApplyToControl(form);
        }

        private static void SetTitleBar(IntPtr handle)
        {
            if (Active.IsHighContrast)
            {
                return;
            }

            try
            {
                int value = Active.IsDark ? 1 : 0;
                DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));
            }
            catch { }
        }

        internal static void ApplyToControl(Control control)
        {
            control.BackColor = GetBackColor(control);
            control.ForeColor = GetForeColor(control);

            if (control is TextBox tb)
            {
                tb.BackColor = Surface0;
                tb.ForeColor = tb.Enabled ? Text : DisabledText;
                tb.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (control is RichTextBox rtb)
            {
                rtb.BackColor = Surface0;
                rtb.ForeColor = rtb.Enabled ? Text : DisabledText;
            }
            else if (control is Button btn)
            {
                btn.BackColor = Button;
                btn.ForeColor = btn.Enabled ? Text : DisabledText;
                btn.FlatStyle = FlatStyle.Flat;
                btn.UseVisualStyleBackColor = false;
                btn.MinimumSize = new Size(0, Math.Max(30, btn.MinimumSize.Height));
                btn.Padding = new Padding(10, 3, 10, 3);
                btn.FlatAppearance.BorderColor = btn.Enabled ? Surface1 : Surface0;
                btn.FlatAppearance.BorderSize = 1;
                btn.FlatAppearance.MouseOverBackColor = ButtonHover;
                btn.FlatAppearance.MouseDownBackColor = ButtonDown;
            }
            else if (control is ComboBox cb)
            {
                cb.BackColor = Surface0;
                cb.ForeColor = cb.Enabled ? Text : DisabledText;
                cb.FlatStyle = FlatStyle.Flat;
            }
            else if (control is NumericUpDown nud)
            {
                nud.BackColor = Surface0;
                nud.ForeColor = nud.Enabled ? Text : DisabledText;
            }
            else if (control is CheckBox chk)
            {
                chk.ForeColor = chk.Enabled ? Text : DisabledText;
                chk.FlatStyle = FlatStyle.Flat;
                chk.Padding = new Padding(0, 2, 0, 2);
            }
            else if (control is TreeView tv)
            {
                tv.BackColor = Mantle;
                tv.ForeColor = Text;
                tv.LineColor = Surface2;
                tv.HideSelection = false;
                tv.FullRowSelect = true;
                tv.ShowNodeToolTips = true;
                tv.ItemHeight = Math.Max(22, tv.ItemHeight);
                tv.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (control is DataGridView dgv)
            {
                dgv.BackgroundColor = Mantle;
                dgv.GridColor = Surface1;
                dgv.BorderStyle = BorderStyle.None;
                dgv.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
                dgv.DefaultCellStyle.BackColor = Base;
                dgv.DefaultCellStyle.ForeColor = Text;
                dgv.DefaultCellStyle.SelectionBackColor = Surface1;
                dgv.DefaultCellStyle.SelectionForeColor = Text;
                dgv.AlternatingRowsDefaultCellStyle.BackColor = Active.IsHighContrast ? Base : Mantle;
                dgv.AlternatingRowsDefaultCellStyle.ForeColor = Text;
                dgv.ColumnHeadersDefaultCellStyle.BackColor = Surface0;
                dgv.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = Surface1;
                dgv.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
                dgv.RowHeadersDefaultCellStyle.BackColor = Surface0;
                dgv.RowHeadersDefaultCellStyle.ForeColor = Text;
                dgv.EnableHeadersVisualStyles = false;
                dgv.RowTemplate.Height = Math.Max(26, dgv.RowTemplate.Height);
                dgv.ColumnHeadersHeight = Math.Max(28, dgv.ColumnHeadersHeight);
            }
            else if (control is TabControl tc)
            {
                tc.BackColor = Base;
                // Tab headers ignore BackColor/ForeColor — owner-draw them so the
                // header strip doesn't stay system light-gray on the dark theme
                tc.DrawMode = TabDrawMode.OwnerDrawFixed;
                tc.DrawItem -= TabControl_DrawItem;
                tc.DrawItem += TabControl_DrawItem;
            }
            else if (control is TabPage tp)
            {
                tp.BackColor = Base;
                tp.ForeColor = Text;
            }
            else if (control is GroupBox gb)
            {
                gb.ForeColor = Subtext1;
            }
            else if (control is LinkLabel ll)
            {
                ll.LinkColor = Blue;
                ll.VisitedLinkColor = Lavender;
                ll.ActiveLinkColor = Peach;
            }
            else if (control is ProgressBar)
            {
                control.BackColor = Surface0;
            }
            else if (control is ToolStrip ts)
            {
                ts.BackColor = Surface0;
                ts.ForeColor = Text;
                ts.Renderer = new DarkToolStripRenderer();
                foreach (ToolStripItem item in ts.Items)
                {
                    ApplyToToolStripItem(item);
                }
            }
            else if (control is Panel pnl)
            {
                pnl.BackColor = Base;
                pnl.ForeColor = Text;
            }
            else if (control is StatusStrip ss)
            {
                ss.BackColor = Surface0;
                ss.ForeColor = Text;
                ss.Renderer = new DarkToolStripRenderer();
            }

            if (control.ContextMenuStrip != null)
            {
                ApplyToContextMenu(control.ContextMenuStrip);
            }

            foreach (Control child in control.Controls)
            {
                ApplyToControl(child);
            }
        }

        private static void TabControl_DrawItem(object sender, DrawItemEventArgs e)
        {
            var tc = (TabControl)sender;
            if (e.Index < 0 || e.Index >= tc.TabPages.Count)
            {
                return;
            }

            TabPage page = tc.TabPages[e.Index];
            bool selected = (e.Index == tc.SelectedIndex);
            Rectangle r = tc.GetTabRect(e.Index);

            using (var brush = new SolidBrush(selected ? Surface1 : Mantle))
            {
                e.Graphics.FillRectangle(brush, r);
            }

            Image img = null;
            if (tc.ImageList != null)
            {
                if (!string.IsNullOrEmpty(page.ImageKey) && tc.ImageList.Images.ContainsKey(page.ImageKey))
                {
                    img = tc.ImageList.Images[page.ImageKey];
                }
                else if (page.ImageIndex >= 0 && page.ImageIndex < tc.ImageList.Images.Count)
                {
                    img = tc.ImageList.Images[page.ImageIndex];
                }
            }

            Rectangle textRect = r;
            if (img != null)
            {
                int iconY = r.Y + (r.Height - img.Height) / 2;
                e.Graphics.DrawImage(img, r.X + 4, iconY);
                textRect = new Rectangle(r.X + 4 + img.Width, r.Y, r.Width - img.Width - 8, r.Height);
            }

            TextRenderer.DrawText(e.Graphics, page.Text, tc.Font, textRect,
                selected ? Text : Subtext0,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (selected)
            {
                using (var pen = new Pen(Focus, 2))
                {
                    e.Graphics.DrawLine(pen, r.Left + 4, r.Bottom - 2, r.Right - 4, r.Bottom - 2);
                }
            }
        }

        private static void ApplyToContextMenu(ContextMenuStrip cms)
        {
            cms.BackColor = Surface0;
            cms.ForeColor = Text;
            cms.Renderer = new DarkToolStripRenderer();
            foreach (ToolStripItem item in cms.Items)
            {
                ApplyToToolStripItem(item);
            }
        }

        private static void ApplyToToolStripItem(ToolStripItem item)
        {
            item.BackColor = Surface0;
            item.ForeColor = Text;
            if (item is ToolStripDropDownButton ddb)
            {
                foreach (ToolStripItem child in ddb.DropDownItems)
                {
                    ApplyToToolStripItem(child);
                }
            }
            else if (item is ToolStripMenuItem mi)
            {
                foreach (ToolStripItem child in mi.DropDownItems)
                {
                    ApplyToToolStripItem(child);
                }
            }
        }

        private static Color GetBackColor(Control c)
        {
            if (c is Form) return Base;
            if (c is Panel) return Base;
            if (c is TabPage) return Base;
            if (c is GroupBox) return Base;
            return Base;
        }

        private static Color GetForeColor(Control c)
        {
            if (c is Label) return Subtext1;
            return Text;
        }

        private class DarkToolStripRenderer : ToolStripProfessionalRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var brush = new SolidBrush(Surface0))
                    e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                Color color = Surface0;
                if (e.Item.Pressed)
                {
                    color = Surface2;
                }
                else if (e.Item.Selected)
                {
                    color = Surface1;
                }
                using (var brush = new SolidBrush(color))
                    e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            }

            protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
            {
                Color color = Surface0;
                if (e.Item.Pressed)
                {
                    color = ButtonDown;
                }
                else if (e.Item.Selected)
                {
                    color = ButtonHover;
                }

                Rectangle bounds = new Rectangle(Point.Empty, e.Item.Size);
                using (var brush = new SolidBrush(color))
                    e.Graphics.FillRectangle(brush, bounds);
                if (e.Item.Selected || e.Item.Pressed)
                {
                    using (var pen = new Pen(Focus))
                        e.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
                }
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using (var pen = new Pen(Surface2))
                    e.Graphics.DrawLine(pen, 0, y, e.Item.Width, y);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (var pen = new Pen(Surface1))
                    e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? Text : DisabledText;
                base.OnRenderItemText(e);
            }
        }
    }
}
