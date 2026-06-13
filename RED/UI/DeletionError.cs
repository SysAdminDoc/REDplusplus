using System;
using System.Drawing;
using System.Windows.Forms;

namespace RED.UI
{
	public partial class DeletionError : Form
	{
		public DeletionError()
		{
			InitializeComponent();
		}

		private void DeletionError_Load(object sender, EventArgs e)
		{
			Text = "Deletion needs attention";
			label1.Text = "RED++ could not change this item. The item was left unchanged.";
			btnAbort.Text = "Stop";
			btnIgnore.Text = "Skip item";
			btnIgnoreAllErrors.Text = "Skip all errors";
			CancelButton = btnAbort;
			AcceptButton = btnIgnore;
			tbPath.AccessibleName = "Path that could not be changed";
			tbErrorMessage.AccessibleName = "Deletion error details";
			btnAbort.AccessibleName = "Stop deletion";
			btnIgnore.AccessibleName = "Skip this error and continue";
			btnIgnoreAllErrors.AccessibleName = "Skip all future deletion errors in this run";
			tbPath.ReadOnly = true;
			tbErrorMessage.ReadOnly = true;
			tbErrorMessage.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
			MinimumSize = new Size(560, 300);
			ClientSize = new Size(560, 286);
			LayoutDeletionError();
			DarkTheme.Apply(this);
			panel1.BackColor = DarkTheme.Base;
			tbPath.BackColor = DarkTheme.Surface0;
			tbErrorMessage.BackColor = DarkTheme.Surface0;
			btnAbort.Focus();
		}

		private void LayoutDeletionError()
		{
			const int margin = 16;
			const int gap = 10;
			int buttonHeight = 32;
			int buttonTop = ClientSize.Height - margin - buttonHeight;

			panel1.Bounds = new Rectangle(margin, margin, ClientSize.Width - (margin * 2), buttonTop - margin - gap);
			label1.AutoSize = false;
			label1.Bounds = new Rectangle(0, 0, panel1.Width, 22);
			tbPath.Bounds = new Rectangle(0, label1.Bottom + 8, panel1.Width, 24);
			tbErrorMessage.Bounds = new Rectangle(0, tbPath.Bottom + 8, panel1.Width, panel1.Height - tbPath.Bottom - 8);

			btnIgnoreAllErrors.Size = new Size(124, buttonHeight);
			btnIgnore.Size = new Size(104, buttonHeight);
			btnAbort.Size = new Size(96, buttonHeight);
			btnIgnoreAllErrors.Location = new Point(ClientSize.Width - margin - btnIgnoreAllErrors.Width, buttonTop);
			btnIgnore.Location = new Point(btnIgnoreAllErrors.Left - gap - btnIgnore.Width, buttonTop);
			btnAbort.Location = new Point(btnIgnore.Left - gap - btnAbort.Width, buttonTop);
		}

		internal void SetPath(string path)
		{
			this.tbPath.Text = path;
		}

		internal void SetErrorMessage(string msg)
		{
			this.tbErrorMessage.Text = msg;
		}
	}
}
