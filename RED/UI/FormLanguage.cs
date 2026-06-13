using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RED.Config;
using RED.Helper;

namespace RED.UI
{
	public partial class FormLanguage : Form
	{
		private Label languageLabel;
		private Label helperLabel;

		public FormLanguage(RedConfiguration config)
		{
			InitializeComponent();
			Config = config;
		}

		public string Language { get; private set; }

		private RedConfiguration Config;

		private void FormLanguage_Load(object sender, System.EventArgs e)
		{
			Text = RedGetText.Translate("Language");
			cboLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
			btnOK.Text = RedGetText.Translate("Apply");
			btnCancel.Text = RedGetText.Translate("Cancel");
			MinimumSize = new Size(380, 170);
			ClientSize = new Size(380, 168);
			EnsureChrome();
			LayoutChrome();
			DarkTheme.Apply(this);
			ApplyChromeTheme();
			Populate();
		}

		private void cboLanguage_SelectedIndexChanged(object sender, EventArgs e)
		{
			Language = cboLanguage.Items[cboLanguage.SelectedIndex].ToString();
		}

		private void Populate()
		{
			try
			{
				string langPath = RedGetText.GetLanguageFolder(Config.Runtime.ExecutablePath);
				cboLanguage.Items.Clear();
				cboLanguage.Items.Add(RedGetText.LanguageDefault);
				List<string> poFiles = Directory.GetFiles(langPath, "*.po").ToList();
				poFiles.Sort();
				foreach (string poFile in poFiles)
				{
					string langName = Path.GetFileNameWithoutExtension(poFile);
					cboLanguage.Items.Add(langName);
				}

				cboLanguage.SelectedIndex = 0;
				for (int i = 0; i < cboLanguage.Items.Count; i++)
				{
					if (cboLanguage.Items[i].ToString() == Config.Options.Language)
					{
						cboLanguage.SelectedIndex = i;
						break;
					}
				}
			}
			catch (Exception ex)
			{
				UiAssist.MsgBoxException(this, "RED+ Language", ex);
			}
		}

		private void EnsureChrome()
		{
			if (languageLabel != null)
			{
				return;
			}

			languageLabel = new Label
			{
				Name = "lbLanguage",
				AutoSize = false,
				Text = RedGetText.Translate("Display language")
			};
			helperLabel = new Label
			{
				Name = "lbLanguageHelp",
				AutoSize = false,
				Text = RedGetText.Words.RestartRequired
			};
			Controls.Add(languageLabel);
			Controls.Add(helperLabel);
		}

		private void LayoutChrome()
		{
			const int margin = 16;
			const int gap = 10;
			languageLabel.Bounds = new Rectangle(margin, margin, ClientSize.Width - (margin * 2), 20);
			cboLanguage.Bounds = new Rectangle(margin, languageLabel.Bottom + 6, ClientSize.Width - (margin * 2), 24);
			helperLabel.Bounds = new Rectangle(margin, cboLanguage.Bottom + 10, ClientSize.Width - (margin * 2), 34);
			btnOK.Size = new Size(96, 32);
			btnCancel.Size = new Size(96, 32);
			btnOK.Location = new Point(ClientSize.Width - margin - btnOK.Width, ClientSize.Height - margin - btnOK.Height);
			btnCancel.Location = new Point(btnOK.Left - gap - btnCancel.Width, btnOK.Top);
		}

		private void ApplyChromeTheme()
		{
			languageLabel.ForeColor = DarkTheme.Text;
			languageLabel.Font = new Font(Font.FontFamily, Font.Size + 1, FontStyle.Bold);
			helperLabel.ForeColor = DarkTheme.Subtext0;
		}
	}
}
