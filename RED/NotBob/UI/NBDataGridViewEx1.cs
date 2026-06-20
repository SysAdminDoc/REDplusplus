using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NotBob.UI
{
	// Extended DataGridView
	// 2017.03.16 + Fill empty grid with blank shaded 'rows'
	// 2018.10.17 @ Allow for Column & Row headers not being visible
	// 2018.10.18 + Allow for alternating row styles if no hatchstyle is set
	// 2019.01.06 + Provide OnPostPaint event to provide PostPaint info
	// 2024.07.12 @ Add DoubleBuffering to prevent flickering during resize

	public class NBDataGridViewEx1 : DataGridView
	{
		public NBDataGridViewEx1()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
			AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
			ExHatchStyle = null;
		}

		public event OnPostPaintHandler OnPostPaint;

		public delegate void OnPostPaintHandler(object sender, int DisplayedHeight);

		public HatchStyle? ExHatchStyle { get; set; }

		// http://social.msdn.microsoft.com/Forums/en-US/winformsdatacontrols/thread/a44622c0-74e1-463b-97b9-27b87513747e#faq13
		// By default, the DataGridView leaves a gray background if the DataGridView size is larger than the data display area size which is needed.
		// To avoid this, we can derive from the DataGridView and override its OnPaint method to draw extra lines in the non-data area.
		protected override void OnPaint(PaintEventArgs e)
		{
			DoPaintPre(e);
			DoPaint(e);
			DoPaintPost(e);
		}

		private void DoPaintPre(PaintEventArgs e)
		{
			// Nothing to do here yet
		}

		private void DoPaint(PaintEventArgs e)
		{
			base.OnPaint(e);

			int rowHeight = RowTemplate.Height;
			int imgWidth = Width - 2;
			if (imgWidth <= 0 || rowHeight <= 0) return;
			Rectangle rFrame = new Rectangle(0, 0, imgWidth, rowHeight);
			Rectangle rFill = new Rectangle(1, 1, imgWidth - 2, rowHeight);
			Rectangle rRowHeader = new Rectangle(2, 2, RowHeadersWidth - 3, rowHeight);

			using (var pen = new Pen(GridColor, 1))
			using (var rowImg0 = new Bitmap(imgWidth, rowHeight))
			{
				Bitmap rowImg1 = null;
				try
				{
					using (var g0 = Graphics.FromImage(rowImg0))
					{
						g0.DrawRectangle(pen, rFrame);
						if (ExHatchStyle != null)
						{
							using (var hb = new HatchBrush(ExHatchStyle.Value, DefaultCellStyle.ForeColor, DefaultCellStyle.BackColor))
								g0.FillRectangle(hb, rFill);
							if (RowHeadersVisible)
							{
								using (var hb = new HatchBrush(ExHatchStyle.Value, DefaultCellStyle.ForeColor, RowHeadersDefaultCellStyle.BackColor))
									g0.FillRectangle(hb, rRowHeader);
							}
						}
						else
						{
							using (var sb = new SolidBrush(DefaultCellStyle.BackColor))
								g0.FillRectangle(sb, rFill);
							if (RowHeadersVisible)
							{
								using (var sb = new SolidBrush(RowHeadersDefaultCellStyle.BackColor))
									g0.FillRectangle(sb, rRowHeader);
							}
						}
						int w = RowHeadersVisible ? RowHeadersWidth - 1 : 0;
						for (int i = 0; i < ColumnCount; i++)
						{
							g0.DrawLine(pen, new Point(w, 0), new Point(w, rowHeight));
							w += Columns[i].Width;
						}
					}

					if (ExHatchStyle == null)
					{
						rowImg1 = new Bitmap(imgWidth, rowHeight);
						using (var g1 = Graphics.FromImage(rowImg1))
						{
							g1.DrawRectangle(pen, rFrame);
							using (var sb = new SolidBrush(AlternatingRowsDefaultCellStyle.BackColor))
								g1.FillRectangle(sb, rFill);
							if (RowHeadersVisible)
							{
								using (var sb = new SolidBrush(RowHeadersDefaultCellStyle.BackColor))
									g1.FillRectangle(sb, rRowHeader);
							}
							int w = RowHeadersVisible ? RowHeadersWidth - 1 : 0;
							for (int i = 0; i < ColumnCount; i++)
							{
								g1.DrawLine(pen, new Point(w, 0), new Point(w, rowHeight));
								w += Columns[i].Width;
							}
						}
					}

					int h = 0;
					foreach (DataGridViewRow row in Rows) { h += row.Height; }
					if (ColumnHeadersVisible) { h += ColumnHeadersHeight; }

					int loop = (Height - h) / rowHeight;
					if (ExHatchStyle != null)
					{
						for (int i = 0; i < loop + 1; i++)
							e.Graphics.DrawImage(rowImg0, 1, (i * rowHeight) + h);
					}
					else
					{
						bool alt = !(RowCount % 2 == 0);
						for (int i = 0; i < loop + 1; i++)
						{
							e.Graphics.DrawImage(alt ? rowImg1 : rowImg0, 1, (i * rowHeight) + h);
							alt = !alt;
						}
					}
				}
				finally
				{
					rowImg1?.Dispose();
				}
			}
		}

		private void DoPaintPost(PaintEventArgs e)
		{
			if (OnPostPaint != null)
			{
				OnPostPaint(this, GetDisplayedHeightEx());
			}
		}

		private int GetDisplayedHeightEx()
		{
			int h = 0;
			if (Rows.Count > 0)
			{
				h = Rows.GetRowsHeight(DataGridViewElementStates.None);
				if (ColumnHeadersVisible)
				{
					h += ColumnHeadersHeight;
				}
				// Fudge factor for borders, padding etc
				h += 3;
			}
			return h;
		}
	}
}