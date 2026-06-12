using System;
using System.Windows.Forms;

namespace RED.UI
{
	public partial class LogWindow : Form
	{
		public LogWindow()
		{
			InitializeComponent();
		}

		private void LogWindow_Load(object sender, EventArgs e)
		{
			Text = "RED++ Log";
			tbLog.ReadOnly = true;
			tbLog.AccessibleName = "RED++ log output";
			DarkTheme.Apply(this);
		}

		public void SetLog(string log)
		{
			this.tbLog.Text = string.IsNullOrWhiteSpace(log)
				? "No log entries for this session yet."
				: log;
			this.tbLog.SelectionStart = 0;
			this.tbLog.SelectionLength = 0;
		}

		private void tbLog_DoubleClick(object sender, EventArgs e)
		{
			this.tbLog.SelectAll();
		}
	}
}
