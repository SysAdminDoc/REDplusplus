using System;
using System.Drawing;
using System.Windows.Forms;
using TXT = RED.RedGetText;

namespace RED.UI
{
	public partial class LogWindow : Form
	{
		private Panel headerPanel;
		private Panel actionPanel;
		private Label titleLabel;
		private Label metaLabel;
		private Button copyButton;
		private Button closeButton;

		public LogWindow()
		{
			InitializeComponent();
		}

		private void LogWindow_Load(object sender, EventArgs e)
		{
			Text = TXT.Translate("RED++ Log");
			tbLog.ReadOnly = true;
			tbLog.AccessibleName = TXT.Translate("RED++ log output");
			tbLog.BorderStyle = BorderStyle.FixedSingle;
			tbLog.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
			MinimumSize = new Size(680, 460);
			EnsureChrome();
			LayoutChrome();
			Resize += (s, args) => LayoutChrome();
			DarkTheme.Apply(this);
			ApplyChromeTheme();
		}

		public void SetLog(string log)
		{
			this.tbLog.Text = string.IsNullOrWhiteSpace(log)
				? TXT.Translate("No log entries for this session yet.")
				: log;
			this.tbLog.SelectionStart = 0;
			this.tbLog.SelectionLength = 0;
			UpdateMeta();
		}

		private void tbLog_DoubleClick(object sender, EventArgs e)
		{
			this.tbLog.SelectAll();
		}

		private void EnsureChrome()
		{
			if (headerPanel != null)
			{
				return;
			}

			headerPanel = new Panel { Name = "pnlLogHeader" };
			actionPanel = new Panel { Name = "pnlLogActions" };
			titleLabel = new Label
			{
				Name = "lbLogTitle",
				AutoSize = false,
				TextAlign = ContentAlignment.MiddleLeft,
				Text = TXT.Translate("Session log")
			};
			metaLabel = new Label
			{
				Name = "lbLogMeta",
				AutoSize = false,
				TextAlign = ContentAlignment.MiddleLeft
			};
			copyButton = new Button
			{
				Name = "btnCopyLog",
				Text = TXT.Translate("Copy log")
			};
			closeButton = new Button
			{
				Name = "btnCloseLog",
				Text = TXT.Translate("Close"),
				DialogResult = DialogResult.OK
			};

			copyButton.Click += (s, e) =>
			{
				Clipboard.SetText(tbLog.Text, TextDataFormat.Text);
				metaLabel.Text = TXT.Translate("Copied to clipboard.");
			};
			closeButton.Click += (s, e) => Close();

			headerPanel.Controls.Add(titleLabel);
			headerPanel.Controls.Add(metaLabel);
			actionPanel.Controls.Add(copyButton);
			actionPanel.Controls.Add(closeButton);
			Controls.Add(headerPanel);
			Controls.Add(actionPanel);
			UpdateMeta();
		}

		private void LayoutChrome()
		{
			if (headerPanel == null)
			{
				return;
			}

			const int margin = 14;
			const int gap = 10;
			int headerHeight = 56;
			int actionHeight = 48;
			headerPanel.Bounds = new Rectangle(0, 0, ClientSize.Width, headerHeight);
			actionPanel.Bounds = new Rectangle(0, ClientSize.Height - actionHeight, ClientSize.Width, actionHeight);
			tbLog.Dock = DockStyle.None;
			tbLog.Bounds = new Rectangle(margin, headerHeight, Math.Max(120, ClientSize.Width - (margin * 2)), Math.Max(80, ClientSize.Height - headerHeight - actionHeight));

			titleLabel.Bounds = new Rectangle(margin, 8, ClientSize.Width - (margin * 2), 22);
			metaLabel.Bounds = new Rectangle(margin, titleLabel.Bottom, ClientSize.Width - (margin * 2), 20);
			closeButton.Size = new Size(96, 32);
			copyButton.Size = new Size(112, 32);
			closeButton.Location = new Point(ClientSize.Width - margin - closeButton.Width, 8);
			copyButton.Location = new Point(closeButton.Left - gap - copyButton.Width, 8);
		}

		private void ApplyChromeTheme()
		{
			if (headerPanel == null)
			{
				return;
			}

			headerPanel.BackColor = DarkTheme.Surface0;
			actionPanel.BackColor = DarkTheme.Surface0;
			titleLabel.BackColor = DarkTheme.Surface0;
			titleLabel.ForeColor = DarkTheme.Text;
			titleLabel.Font = new Font(Font.FontFamily, Font.Size + 1, FontStyle.Bold);
			metaLabel.BackColor = DarkTheme.Surface0;
			metaLabel.ForeColor = DarkTheme.Subtext0;
			tbLog.BackColor = DarkTheme.Mantle;
			tbLog.ForeColor = DarkTheme.Text;
		}

		private void UpdateMeta()
		{
			if (metaLabel == null)
			{
				return;
			}

			int lines = string.IsNullOrEmpty(tbLog.Text) ? 0 : tbLog.Lines.Length;
			metaLabel.Text = TXT.Translate("{0} log lines", lines);
		}
	}
}
