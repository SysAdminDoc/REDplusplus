using System;
using System.Windows.Forms;
using TXT = RED.RedGetText;

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
			Text = TXT.Translate("RED++ Log");
			tbLog.ReadOnly = true;
			tbLog.AccessibleName = TXT.Translate("RED++ log output");
			DarkTheme.Apply(this);
		}

		public void SetLog(string log)
		{
			this.tbLog.Text = string.IsNullOrWhiteSpace(log)
				? TXT.Translate("No log entries for this session yet.")
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
