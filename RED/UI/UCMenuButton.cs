using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RED.UI
{
	public class UCMenuButton : Button
	{
		[DefaultValue(null), Browsable(true), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		public ContextMenuStrip Menu { get; set; }

		[DefaultValue(20), Browsable(true), DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		public int SplitWidth { get; set; }

		//protected override void OnMouseDown(MouseEventArgs mevent)
		protected override void OnClick(EventArgs e)
		{
			if (Menu != null)
			{
				Point menuLocation;
				Point screenPoint = this.PointToScreen(new Point(0, this.Height));
				if (screenPoint.Y + Menu.Size.Height > Screen.PrimaryScreen.WorkingArea.Height)
				{
					menuLocation = new Point(0, -Menu.Size.Height);
				}
				else
				{
					menuLocation = new Point(0, this.Height);
				}
				Menu.Show(this, menuLocation);
			}
		}

		protected override void OnEnabledChanged(EventArgs e)
		{
			base.OnEnabledChanged(e);
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs pevent)
		{
			base.OnPaint(pevent);

			if (Menu != null)
			{
				pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
				// Draw the arrow glyph on the right side of the button
				int effectiveSplitWidth = SplitWidth > 0 ? SplitWidth : 26;
				int arrowX = ClientRectangle.Width - 17;
				int arrowY = ClientRectangle.Height / 2 - 1;

				using (var arrowBrush = new SolidBrush(Enabled ? ForeColor : DarkTheme.DisabledText))
				{
					Point[] arrows = new[] { new Point(arrowX, arrowY), new Point(arrowX + 8, arrowY), new Point(arrowX + 4, arrowY + 5) };
					pevent.Graphics.FillPolygon(arrowBrush, arrows);
				}

				int lineX = ClientRectangle.Width - effectiveSplitWidth;
				int lineYFrom = 7;
				int lineYTo = ClientRectangle.Height - 7;
				using (Pen separatorPen = new Pen(DarkTheme.Surface2))
				{
					pevent.Graphics.DrawLine(separatorPen, lineX, lineYFrom, lineX, lineYTo);
				}

				if (Focused && ShowFocusCues)
				{
					using (Pen focusPen = new Pen(DarkTheme.Focus))
					{
						pevent.Graphics.DrawRectangle(focusPen, 2, 2, ClientRectangle.Width - 5, ClientRectangle.Height - 5);
					}
				}
			}
		}
	}
}
