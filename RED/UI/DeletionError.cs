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
			DarkTheme.Apply(this);
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