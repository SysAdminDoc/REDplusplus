using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RED.UI
{
    internal static class DarkTheme
    {
        // Catppuccin Mocha palette
        internal static readonly Color Base = Color.FromArgb(30, 30, 46);
        internal static readonly Color Mantle = Color.FromArgb(24, 24, 37);
        internal static readonly Color Crust = Color.FromArgb(17, 17, 27);
        internal static readonly Color Surface0 = Color.FromArgb(49, 50, 68);
        internal static readonly Color Surface1 = Color.FromArgb(69, 71, 90);
        internal static readonly Color Surface2 = Color.FromArgb(88, 91, 112);
        internal static readonly Color Overlay0 = Color.FromArgb(108, 112, 134);
        internal static readonly Color Text = Color.FromArgb(205, 214, 244);
        internal static readonly Color Subtext0 = Color.FromArgb(166, 173, 200);
        internal static readonly Color Subtext1 = Color.FromArgb(186, 194, 222);
        internal static readonly Color Blue = Color.FromArgb(137, 180, 250);
        internal static readonly Color Red = Color.FromArgb(243, 139, 168);
        internal static readonly Color Green = Color.FromArgb(166, 227, 161);
        internal static readonly Color Yellow = Color.FromArgb(249, 226, 175);
        internal static readonly Color Lavender = Color.FromArgb(180, 190, 254);
        internal static readonly Color Peach = Color.FromArgb(250, 179, 135);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        internal static void Apply(Form form)
        {
            SetDarkTitleBar(form.Handle);
            ApplyToControl(form);
        }

        private static void SetDarkTitleBar(IntPtr handle)
        {
            try
            {
                int value = 1;
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
                tb.ForeColor = Text;
                tb.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (control is RichTextBox rtb)
            {
                rtb.BackColor = Surface0;
                rtb.ForeColor = Text;
            }
            else if (control is Button btn)
            {
                btn.BackColor = Surface0;
                btn.ForeColor = Text;
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = Surface1;
                btn.FlatAppearance.MouseOverBackColor = Surface1;
                btn.FlatAppearance.MouseDownBackColor = Surface2;
            }
            else if (control is ComboBox cb)
            {
                cb.BackColor = Surface0;
                cb.ForeColor = Text;
                cb.FlatStyle = FlatStyle.Flat;
            }
            else if (control is NumericUpDown nud)
            {
                nud.BackColor = Surface0;
                nud.ForeColor = Text;
            }
            else if (control is CheckBox chk)
            {
                chk.ForeColor = Text;
            }
            else if (control is TreeView tv)
            {
                tv.BackColor = Mantle;
                tv.ForeColor = Text;
                tv.LineColor = Surface2;
            }
            else if (control is DataGridView dgv)
            {
                dgv.BackgroundColor = Mantle;
                dgv.GridColor = Surface1;
                dgv.DefaultCellStyle.BackColor = Base;
                dgv.DefaultCellStyle.ForeColor = Text;
                dgv.DefaultCellStyle.SelectionBackColor = Surface1;
                dgv.DefaultCellStyle.SelectionForeColor = Text;
                dgv.ColumnHeadersDefaultCellStyle.BackColor = Surface0;
                dgv.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                dgv.RowHeadersDefaultCellStyle.BackColor = Surface0;
                dgv.RowHeadersDefaultCellStyle.ForeColor = Text;
                dgv.EnableHeadersVisualStyles = false;
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
                gb.ForeColor = Subtext0;
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
                var color = e.Item.Selected ? Surface1 : Surface0;
                using (var brush = new SolidBrush(color))
                    e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
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
                e.TextColor = Text;
                base.OnRenderItemText(e);
            }
        }
    }
}
