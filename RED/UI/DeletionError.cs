using System;
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
			label1.Text = "RED++ could not change this item. Choose how to continue:";
			btnAbort.Text = "Stop";
			btnIgnore.Text = "Skip item";
			btnIgnoreAllErrors.Text = "Skip all";
			CancelButton = btnAbort;
			AcceptButton = btnIgnore;
			tbPath.AccessibleName = "Path that could not be changed";
			tbErrorMessage.AccessibleName = "Deletion error details";
			btnAbort.AccessibleName = "Stop deletion";
			btnIgnore.AccessibleName = "Skip this error and continue";
			btnIgnoreAllErrors.AccessibleName = "Skip all future deletion errors in this run";
			tbPath.ReadOnly = true;
			tbErrorMessage.ReadOnly = true;
			DarkTheme.Apply(this);
			btnAbort.Focus();
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
