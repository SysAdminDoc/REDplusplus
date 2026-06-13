using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace RED.UI
{
	public partial class FormRtfHelp : Form
	{
		public FormRtfHelp()
		{
			InitializeComponent();
		}

		public string Title
		{
            get { return _Title; }
			set
			{
				_Title = value;
				this.Text = "RED Help: " + _Title;
			}
		}

		private string _Title;
		
        public string HelpText { get; set; }

		private void FormRedMatchHelp_Load(object sender, EventArgs e)
		{
			this.Icon = Properties.Resources.iconProject;
			this.FormBorderStyle = FormBorderStyle.Sizable;
			this.MaximizeBox = true;
			this.MinimumSize = new Size(560, 460);
			this.btnHelp1Cancel.Visible = false;
			this.btnHelp1OK.Text = "Close";
			this.rtfHelpText.ReadOnly = true;
			this.rtfHelpText.BorderStyle = BorderStyle.None;
			this.pnlHelpActions.AutoSize = false;
			this.pnlHelpActions.Height = 48;
			LayoutChrome();
			this.Resize += (s, args) => LayoutChrome();
			DarkTheme.Apply(this);
			ApplyChromeTheme();
		}

		private void FormRtfHelp_Shown(object sender, EventArgs e)
		{
			LoadRtfFromHelpText();
		}

		private void btnHelp1OK_Click(object sender, EventArgs e)
		{
			Close();
		}

		private void btnHelp1Cancel_Click(object sender, EventArgs e)
		{
#if DEBUG
			rtfHelpText.SaveFile($"!{Title}_HelpText.rtf", RichTextBoxStreamType.RichText);
			File.WriteAllText($"!{Title}_HelpText.txt", HelpText);
#endif
		}

		private void LoadRtfFromHelpText()
		{
			using (MemoryStream stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(HelpText)))
			{
				rtfHelpText.LoadFile(stream, RichTextBoxStreamType.RichText);
			}
		}

		private void LayoutChrome()
		{
			const int margin = 14;
			pnlHelpActions.Height = 48;
			pnlHelp1.Padding = new Padding(margin);
			btnHelp1OK.Size = new Size(96, 32);
			btnHelp1OK.Location = new Point(ClientSize.Width - margin - btnHelp1OK.Width, 8);
		}

		private void ApplyChromeTheme()
		{
			pnlHelpActions.BackColor = DarkTheme.Surface0;
			pnlHelp1.BackColor = DarkTheme.Mantle;
			rtfHelpText.BackColor = DarkTheme.Mantle;
			rtfHelpText.ForeColor = DarkTheme.Text;
		}
	}
}
