using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;
using NotBob.Config;
using NotBob.Lib;
using RED.Config;
using RED.Helper;
using RED.Match;

using TXT = RED.RedGetText;

namespace RED.UI
{
    public partial class MainWindow : Form
    {
        private REDCore Core = null;
        private TreeManager TreeMgr = null;
        private readonly RuntimeData RunData = new RuntimeData();
        private RedConfiguration RedConfig = null;
        private readonly Stopwatch RuntimeWatch = new Stopwatch();
        private bool AutoSearchOnStart = false;
        private readonly Queue<string> pendingScanPaths = new Queue<string>();

        // True while a queued multi-path scan is continuing: results and tree roots
        // append instead of replacing the previous root's output.
        private bool multiRootContinuation = false;
        private Panel treeEmptyStatePanel;
        private PictureBox treeEmptyStateIcon;
        private Label treeEmptyStateLabel;
        private Label treeEmptyStateDetailLabel;
        private Label treeEmptyStateTrustLabel;
        private Label treeEmptyStateStep1Label;
        private Label treeEmptyStateStep2Label;
        private Label treeEmptyStateStep3Label;
        private Label pathHintLabel;
        private Label actionHintLabel;
        private ToolStripStatusLabel progressPercentLabel;
        private bool premiumPolishEventsAttached = false;
        private bool reviewSurfaceHasCompletedRun = false;

        #region Init methods

        /// <summary>
        /// Constructor
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            UseWaitCursor = true;
        }

        /// <summary>
        /// On load
        /// </summary>
        private void MainWindow_Load(object sender, EventArgs e)
        {
            // NotBob - Use new icon, load new config info, initialise translations
            Icon = Properties.Resources.iconProject;
            Text = "RED++ - Remove Empty Directories+";
            ConfigLoad();

            #region Init RED core

            Core = new REDCore(RunData);

            // Attach events
            Core.OnError += new EventHandler<ErrorEventArgs>(Core_OnError);
            Core.OnCancelled += new EventHandler(Core_OnCancelled);
            Core.OnAborted += new EventHandler(Core_OnAborted);

            Core.OnProgressChanged += new EventHandler<ProgressChangedEventArgs>(Core_OnProgressChanged);
            Core.OnFoundEmptyDirectory += new EventHandler<FoundEmptyDirInfoEventArgs>(Core_OnFoundEmptyDir);
            Core.OnFinishedScanForEmptyDirs += new EventHandler<FinishedScanForEmptyDirsEventArgs>(Core_OnFoundFinishedScanForEmptyDirs);
            Core.OnDeleteProcessChanged += new EventHandler<DeleteProcessUpdateEventArgs>(Core_OnDeleteProcessChanged);
            Core.OnDeleteProcessFinished += new EventHandler<DeleteProcessFinishedEventArgs>(Core_OnDeleteProcessFinished);
            Core.OnDeleteError += new EventHandler<DeletionErrorEventArgs>(Core_OnDeleteError);

            #endregion Init RED core

            // Init tree manager / helper
            TreeMgr = new TreeManager(tvSearchResults, lbFastModeInfo);
            TreeMgr.SetFastMode(RedConfig.Options.FastSearchMode);
            TreeMgr.OnProtectionStatusChanged += new EventHandler<ProtectionStatusChangedEventArgs>(TreeMgr_OnProtectionStatusChanged);
            TreeMgr.OnDeleteRequest += new EventHandler<DeleteRequestFromTreeEventArgs>(TreeMgr_OnDeleteRequest);

            // Populate delete mode item list
            foreach (DeleteModes d in DeleteModeItem.GetList())
            {
                cbDeleteMode.Items.Add(new DeleteModeItem(d));
            }

            // Update labels
            lblRedStats.Text = string.Format("{0}: {1}", TXT.Words.DeletedSoFar, RedConfig.Volatile.CountOfDeletions);
            // NotBob - use file version info rather than product version.
            // Environment.ProcessPath is single-file safe (Assembly.Location is empty there).
            FileVersionInfo vi = FileVersionInfo.GetVersionInfo(Environment.ProcessPath);
            lbAppTitle.Text = string.Format("{0} v{1}", RedGetText.Red.Title, vi.FileVersion.ToString());
#if DEBUG
            lbAppTitle.Text += " (DBUG)";
#endif

            lbStatus.Text = string.Empty;
            // NotBob - Display BuildTime info on the About tab
            DateTime buildTime = RedAssist.GetBuildTime();
            lbNotBobInfoBuild.Text = string.Format("Build Time: {0} {1:MMMM, yyyy} @ {1:HH:mm}", buildTime.Day.ToOrdinal(), buildTime);
            uxToolTips.SetToolTip(picAboutLogo, RedConfig.Filename);

            if (SystemFunctions.IsAdmin())
            {
                Text += string.Format(" ({0})", TXT.Words.AdminMode);
            }

            ExplorerIntegrationCheck();

            UpdateContextMenu(cmTreeview, false);
            btnDelete.Enabled = false;

            DrawDirectoryIcons();

            SetProcessActiveLock(false);
            UiProgressBar(false);

            txtHelp.Text = Properties.Resources.helpAbout;

            ProcessCommandLineArgs();

            // NotBob - Update the UI from the saved config details
            ConfigToUI();
            ConfigRestoreWindowDetails();
            if (!string.IsNullOrWhiteSpace(RedConfig.Runtime.Volatile.LastUsedDirectory))
            {
                txtSearchDirectory.Text = RedConfig.Runtime.Volatile.LastUsedDirectory;
            }

            AddRestoreMenuItem();
            AddThemeMenu();
            AddEmptyFilesMenuItem();
            AddImportResultsMenuItem();

            SetAccessibleNames();
            ApplyPremiumPolish();
            DarkTheme.SetMode((ThemeMode)RedConfig.UI.ThemeMode);
            DarkTheme.Apply(this);
            RefreshThemeDependentUi();
            UpdateTreeEmptyState();
            StartForwardWatcher();
        }

        private System.Windows.Forms.Timer forwardWatchTimer;

        private void StartForwardWatcher()
        {
            try { if (File.Exists(Program.ForwardSignalPath)) File.Delete(Program.ForwardSignalPath); } catch { }
            forwardWatchTimer = new System.Windows.Forms.Timer { Interval = 500 };
            forwardWatchTimer.Tick += ForwardWatchTimer_Tick;
            forwardWatchTimer.Start();
        }

        private void ForwardWatchTimer_Tick(object sender, EventArgs e)
        {
            if (!File.Exists(Program.ForwardSignalPath)) return;

            string path = null;
            try
            {
                path = File.ReadAllText(Program.ForwardSignalPath, System.Text.Encoding.UTF8).Trim();
                File.Delete(Program.ForwardSignalPath);
            }
            catch { return; }

            if (string.IsNullOrWhiteSpace(path)) return;

            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();

            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
                path += Path.DirectorySeparatorChar;

            txtSearchDirectory.Text = path;
            tcMain.SelectedTab = tabSearch;
            btnSearch.PerformClick();
        }

        /// <summary>
        /// "Restore Last Deletion" lives in the Extras menu and is enabled only
        /// while an undo manifest exists. Deleted dirs were empty, so restore is a
        /// lossless recreate (Move mode entries are moved back).
        /// </summary>
        private void AddRestoreMenuItem()
        {
            var mnuItemRestore = new ToolStripMenuItem(TXT.Translate("&Restore Deletion"));
            mnuItemRestore.AccessibleName = TXT.Translate("Restore directories from a previous deletion");
            cmMenuExtras.Items.Insert(0, mnuItemRestore);
            cmMenuExtras.Items.Insert(1, new ToolStripSeparator());
            cmMenuExtras.Opening += (s, e) =>
            {
                mnuItemRestore.DropDownItems.Clear();
                var manifests = UndoManager.ListManifests();
                mnuItemRestore.Enabled = manifests.Count > 0;
                foreach (var info in manifests)
                {
                    string label = string.Format("{0} — {1} ({2})",
                        info.Timestamp.ToString("g"),
                        info.DeleteMode,
                        TXT.Translate("{0} entries", info.EntryCount));
                    var item = new ToolStripMenuItem(label);
                    item.AccessibleName = TXT.Translate("Restore deletion from {0}", info.Timestamp.ToString("g"));
                    string path = info.FilePath;
                    item.Click += (sender, args) => RestoreFromManifest(path);
                    mnuItemRestore.DropDownItems.Add(item);
                }
            };
        }

        /// <summary>
        /// Theme submenu (Dark / Light / System) in the Extras menu. Switching
        /// re-themes every open form live and persists the choice.
        /// </summary>
        private void AddThemeMenu()
        {
            var themeRoot = new ToolStripMenuItem(TXT.Translate("&Theme"));
            var dark = new ToolStripMenuItem(TXT.Translate("Dark")) { Tag = ThemeMode.Dark };
            var light = new ToolStripMenuItem(TXT.Translate("Light")) { Tag = ThemeMode.Light };
            var system = new ToolStripMenuItem(TXT.Translate("System")) { Tag = ThemeMode.System };

            EventHandler pick = (s, e) =>
            {
                var picked = (ThemeMode)((ToolStripMenuItem)s).Tag;
                RedConfig.UI.ThemeMode = (int)picked;
                DarkTheme.SetMode(picked);
                foreach (Form f in Application.OpenForms)
                {
                    DarkTheme.Apply(f);
                    if (f is MainWindow main)
                    {
                        main.RefreshThemeDependentUi();
                    }
                }
                dark.Checked = picked == ThemeMode.Dark;
                light.Checked = picked == ThemeMode.Light;
                system.Checked = picked == ThemeMode.System;
            };
            dark.Click += pick; light.Click += pick; system.Click += pick;

            var current = (ThemeMode)RedConfig.UI.ThemeMode;
            dark.Checked = current == ThemeMode.Dark;
            light.Checked = current == ThemeMode.Light;
            system.Checked = current == ThemeMode.System;

            themeRoot.DropDownItems.Add(dark);
            themeRoot.DropDownItems.Add(light);
            themeRoot.DropDownItems.Add(system);
            cmMenuExtras.Items.Insert(2, themeRoot);
        }

        /// <summary>
        /// Checkable "Delete empty files too" toggle in the Extras menu — opt-in
        /// sister mode that also removes standalone zero-byte files on the next scan.
        /// </summary>
        private void AddEmptyFilesMenuItem()
        {
            var item = new ToolStripMenuItem(TXT.Translate("Delete empty &files too"))
            {
                CheckOnClick = true,
                Checked = RedConfig.Options.DeleteEmptyFiles
            };
            item.AccessibleName = TXT.Translate("Also delete standalone zero-byte files");
            item.CheckedChanged += (s, e) => RedConfig.Options.DeleteEmptyFiles = item.Checked;
            cmMenuExtras.Items.Insert(3, item);
        }

        private void AddImportResultsMenuItem()
        {
            var item = new ToolStripMenuItem(TXT.Translate("&Import Saved Dry-Run Results..."));
            item.AccessibleName = TXT.Translate("Import saved dry-run results for review");
            item.Click += mnuItemImportDryRunResults_Click;
            cmMenuExtras.Items.Insert(Math.Min(5, cmMenuExtras.Items.Count), item);
            cmMenuExtras.Opening += (s, e) => item.Enabled = !UiIsBusy();
        }

        private void RestoreFromManifest(string manifestPath)
        {
            int restored, failed;
            UndoManager.Restore(manifestPath, out restored, out failed, msg => RunData.AddLogMessage(msg));

            string summary = TXT.Translate("Restored {0} directories (Failed: {1})", restored, failed);
            SetStatusAndLogMessage(summary);
            if (failed > 0)
            {
                UiAssist.MsgBoxError(summary + RedGetText.CrLf2 + TXT.Translate("See the log for details. The undo manifest was kept so you can retry."));
            }
            else
            {
                UiAssist.MsgBoxInfo(summary);
            }
        }

        private void SetAccessibleNames()
        {
            tabSearch.AccessibleName = TXT.Translate("Search");
            tabSearch.AccessibleDescription = TXT.Translate("Search for empty directories and empty files");
            txtSearchDirectory.AccessibleName = TXT.Translate("Search directory path");
            btnSearch.AccessibleName = TXT.Translate("Search for empty directories");
            btnDelete.AccessibleName = TXT.Translate("Delete empty directories");
            btnCancel.AccessibleName = TXT.Translate("Cancel current operation");
            btnExit.AccessibleName = TXT.Translate("Exit application");
            btnSearchDirectoryBrowseFor.AccessibleName = TXT.Translate("Browse for directory");
            tvSearchResults.AccessibleName = TXT.Translate("Search results tree");
            cbDeleteMode.AccessibleName = TXT.Translate("Delete mode selection");
            btnHelp.AccessibleName = TXT.Translate("Open help");
            btnCopyDebugInfo.AccessibleName = TXT.Translate("Copy debug information to clipboard");
            btnResetConfig.AccessibleName = TXT.Translate("Reset settings to defaults");
            btnResetFilters.AccessibleName = TXT.Translate("Reset filters to defaults");
            btnExplorerIntegrate.AccessibleName = TXT.Translate("Add Explorer context menu integration");
            btnExplorerRemove.AccessibleName = TXT.Translate("Remove Explorer context menu integration");
            cbFastSearchMode.AccessibleName = TXT.Translate("Enable fast search mode");
            cbSavePrompt.AccessibleName = TXT.Translate("Prompt to save settings on exit");
            cbIgnore0kbFiles.AccessibleName = TXT.Translate("Treat directories with empty files as empty");
            cbIgnoreHiddenFolders.AccessibleName = TXT.Translate("Ignore hidden directories");
            cbIgnoreSystemFolders.AccessibleName = TXT.Translate("Ignore system directories");
            cbHideScanErrors.AccessibleName = TXT.Translate("Hide errors during search");
            cbHideDeletionErrors.AccessibleName = TXT.Translate("Hide errors during deletion");
            cbHideIgnoredFolders.AccessibleName = TXT.Translate("Hide ignored directories from results");
            cbAutoProtectRoot.AccessibleName = TXT.Translate("Automatically protect the starting directory");
            cbRespectGitIgnore.AccessibleName = TXT.Translate("Respect .gitignore rules during scans");
            cbUseMftScan.AccessibleName = TXT.Translate("Use MFT turbo scan");
            cbClipboardDetection.AccessibleName = TXT.Translate("Detect paths in the clipboard");
            pbProgressStatus.AccessibleName = TXT.Translate("Operation progress");
            tvSearchResults.AccessibleDescription = TXT.Translate("Shows every reviewed directory and why it will be deleted, kept, or skipped.");
            lbStatus.AccessibleName = TXT.Translate("Detailed status");
            lbUiStatus.AccessibleName = TXT.Translate("Application state");
            tabSettings.AccessibleName = TXT.Translate("Settings");
            tabSettings1.AccessibleName = TXT.Translate("General Settings");
            tabSettings1.AccessibleDescription = TXT.Translate("Basic scan behavior, delete mode, and age filters");
            tabSettings2.AccessibleName = TXT.Translate("Advanced Settings");
            tabSettings2.AccessibleDescription = TXT.Translate("Explorer integration, MFT turbo scan, and clipboard detection");
            tabFilters.AccessibleName = TXT.Translate("Filters");
            tabFilters.AccessibleDescription = TXT.Translate("Rules that control which directories and files are ignored or protected");
            tabFilterFoldersIgnore.AccessibleName = TXT.Translate("Directories: Ignore");
            tabFilterFoldersNeverEmpty.AccessibleName = TXT.Translate("Directories: Never Empty");
            tabFiltersFilesIgnore.AccessibleName = TXT.Translate("Files: Ignore");
        }

        private void ApplyPremiumPolish()
        {
            gbFind.Text = TXT.Translate("Select Directory To Be Searched");
            lbIconDesc.Text = TXT.Translate("Result Legend");
            lbColorDoNotTouch.Text = TXT.Translate("Will not be deleted");
            lbColorToBeDeleted.Text = TXT.Translate("Will be deleted");
            lbColorProtected.Text = TXT.Translate("Protected");
            lbFastModeInfo.Text = TXT.Translate("Fast mode is on. Results appear when the scan finishes.");

            btnSearch.Text = TXT.Translate("&Scan");
            btnDelete.Text = TXT.Translate("&Review && Delete");
            btnCancel.Text = TXT.Translate("&Cancel");
            btnExit.Text = TXT.Words.Exit;
            uxMenuButtonExtras.Text = TXT.Translate("E&xtras");
            btnSearchDirectoryBrowseFor.Text = TXT.Translate("Browse...");
            cbIgnore0kbFiles.Text = TXT.Translate("Treat zero-byte files as empty");
            lbIgnore0kbFiles.Text = TXT.Translate("Directories containing only zero-byte files can be treated as empty.");
            cbIgnoreSystemFolders.Text = TXT.Translate("Ignore system directories (recommended)");
            cbIgnoreHiddenFolders.Text = TXT.Translate("Ignore hidden directories");
            cbHideDeletionErrors.Text = TXT.Translate("Continue past deletion errors");
            cbHideScanErrors.Text = TXT.Translate("Hide scan errors in the result tree");
            cbHideIgnoredFolders.Text = TXT.Translate("Hide ignored directories from results");
            cbAutoProtectRoot.Text = TXT.Translate("Protect the starting directory");
            cbFastSearchMode.Text = TXT.Translate("Fast result rendering");
            cbClipboardDetection.Text = TXT.Translate("Detect folder paths in the clipboard");
            cbSavePrompt.Text = TXT.Translate("Ask before saving");
            tabSettings1.Text = TXT.Translate("General");
            tabSettings2.Text = TXT.Translate("Advanced");
            gbSettings1a.Text = TXT.Translate("Scan behavior");
            gbDeleteMode.Text = TXT.Translate("Deletion mode");
            gbSettings2a.Text = TXT.Translate("Advanced scan rules");
            gbSettings2r.Text = TXT.Translate("Remember");
            gbAdvancedExtras.Text = TXT.Translate("Maintenance");
            cbRespectGitIgnore.Text = TXT.Translate("Respect .gitignore rules during scans");
            cbUseMftScan.Text = TXT.Translate("Use MFT turbo scan (administrator only)");
            lbUseMftScan.Text = TXT.Translate("Requires administrator rights; standard scan is used when unavailable.");
            lbPause1.Text = TXT.Translate("Pause between deletion steps (milliseconds)");
            lbnuPause2.Text = TXT.Translate("Useful for long runs when you may need time to cancel.");
            lbMaxDepth1.Text = TXT.Translate("Maximum scan depth (-1 = unlimited)");
            lbFolderAge1.Text = TXT.Translate("Minimum folder age before eligibility");
            lbInfiniteLoopDetectionCount.Text = TXT.Translate("Stop after this many loop-detection errors");
            gbExplorerIntegration.Text = TXT.Translate("Explorer integration");
            lbExplorerIntegration1.Text = TXT.Translate("Add RED++ to the Explorer folder context menu.");
            chkExplorerIntegrateAutoSearch.Text = TXT.Translate("Scan immediately");
            btnExplorerIntegrate.Text = TXT.Translate("Add to Explorer");
            btnExplorerRemove.Text = TXT.Translate("Remove");
            btnCopyDebugInfo.Text = TXT.Translate("Copy debug info");
            btnResetFilters.Text = TXT.Translate("Reset filters");
            btnResetConfig.Text = TXT.Translate("Reset settings");
            lbIgnoreFiles2.Text = TXT.Translate("Caution: ignored-file rules can make folders eligible for deletion. Keep patterns narrow.");
            lbFastSearchMode.Text = TXT.Translate("Keeps the interface responsive on very large directory trees by rendering results after the scan finishes.");
            lbClipboardDetection.Text = TXT.Translate("When a folder path is already on the clipboard, RED++ can offer it as the next scan root.");
            lbIgnoreFiles2.MaximumSize = new Size(220, 0);
            lbFastSearchMode.MaximumSize = new Size(585, 0);
            lbClipboardDetection.MaximumSize = new Size(585, 0);
            lbUseMftScan.MaximumSize = new Size(420, 0);
            lblExplorerIntegrationInfo.MaximumSize = new Size(340, 0);

            EnsureComfortableWindowBounds();
            EnsurePathHintLabel();
            EnsureActionHintLabel();

            tvSearchResults.HideSelection = false;
            tvSearchResults.FullRowSelect = true;
            tvSearchResults.ShowNodeToolTips = true;
            tvSearchResults.ItemHeight = Math.Max(22, tvSearchResults.ItemHeight);
            tvSearchResults.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            pnlIconDesc.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            txtSearchDirectory.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            btnSearchDirectoryBrowseFor.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            lblPickAFolder.Visible = false;
            stausStripMain.SizingGrip = false;
            EnsureStatusStripPolish();
            lbUiStatus.Size = new Size(280, lbUiStatus.Height);
            if (string.IsNullOrWhiteSpace(lbStatus.Text))
            {
                lbStatus.Text = TXT.Translate("0 items    |    Nothing to delete yet.");
            }
            pbProgressStatus.AutoSize = false;
            pbProgressStatus.Width = 420;
            pbProgressStatus.Visible = true;
            pnlActions.AutoSize = false;
            pnlActionsSearch.Dock = DockStyle.None;
            btnSearch.AutoSize = false;
            btnDelete.AutoSize = false;
            btnCancel.AutoSize = false;
            uxMenuButtonExtras.AutoSize = false;
            btnExit.AutoSize = false;
            btnSearchDirectoryBrowseFor.AutoSize = false;

            uxToolTips.SetToolTip(btnSearch, TXT.Translate("Scan the selected folder and show reviewable results before anything is deleted."));
            uxToolTips.SetToolTip(btnDelete, TXT.Translate("Change only eligible results after RED++ re-checks them for safety."));
            uxToolTips.SetToolTip(btnCancel, TXT.Translate("Cancel the current scan or deletion."));
            uxToolTips.SetToolTip(uxMenuButtonExtras, TXT.Translate("Open restore, import, export, log, language, and theme actions."));
            uxToolTips.SetToolTip(tvSearchResults, TXT.Translate("Right-click a result to open it, protect it, add it to filters, or delete one eligible branch."));
            uxToolTips.SetToolTip(txtSearchDirectory, TXT.Translate("Paste, type, browse, or drop one or more folders to scan."));
            uxToolTips.SetToolTip(btnSearchDirectoryBrowseFor, TXT.Translate("Choose the folder RED++ will scan."));

            if (!premiumPolishEventsAttached)
            {
                gbFind.Resize += (s, e) => AdjustSearchLayout();
                Resize += (s, e) =>
                {
                    LayoutMainChrome();
                    AdjustSearchLayout();
                };
                txtSearchDirectory.TextChanged += (s, e) =>
                {
                    reviewSurfaceHasCompletedRun = false;
                    UpdateTreeEmptyState();
                    UpdateActionHint();
                };
                premiumPolishEventsAttached = true;
            }

            EnsureTreeEmptyStateLabel();
            LayoutMainChrome();
            AdjustSearchLayout();
            UpdateActionHint();
        }

        private void EnsurePathHintLabel()
        {
            if (pathHintLabel != null)
            {
                return;
            }

            pathHintLabel = new Label
            {
                Name = "lbPathHint",
                AutoSize = false,
                UseMnemonic = false,
                TabStop = false,
                Text = TXT.Translate("Pick a root folder. RED++ scans first and only changes items after review.")
            };
            gbFind.Controls.Add(pathHintLabel);
            pathHintLabel.BringToFront();
        }

        private void EnsureActionHintLabel()
        {
            if (actionHintLabel != null)
            {
                return;
            }

            actionHintLabel = new Label
            {
                Name = "lbActionHint",
                AutoSize = false,
                AutoEllipsis = true,
                UseMnemonic = false,
                TabStop = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlActions.Controls.Add(actionHintLabel);
            actionHintLabel.BringToFront();
            actionHintLabel.Visible = false;
        }

        private void EnsureStatusStripPolish()
        {
            if (progressPercentLabel != null)
            {
                return;
            }

            progressPercentLabel = new ToolStripStatusLabel
            {
                Name = "lbProgressPercent",
                Text = "0%",
                AutoSize = false,
                Size = new Size(48, lbUiStatus.Height),
                TextAlign = ContentAlignment.MiddleRight,
                Alignment = ToolStripItemAlignment.Right
            };
            pbProgressStatus.Alignment = ToolStripItemAlignment.Right;
            stausStripMain.Items.Add(progressPercentLabel);
        }

        private void LayoutMainChrome()
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                return;
            }

            stausStripMain.Dock = DockStyle.Bottom;
            pnlActions.Dock = DockStyle.Bottom;
            tcMain.Dock = DockStyle.Fill;
            stausStripMain.Height = 56;
            pnlActions.Height = Math.Min(108, Math.Max(92, ClientSize.Height / 9));
            LayoutActionBar();
        }

        private void LayoutActionBar()
        {
            if (pnlActions.ClientSize.Width <= 0)
            {
                return;
            }

            const int margin = 24;
            const int gap = 18;
            int buttonHeight = Math.Min(68, Math.Max(56, pnlActions.ClientSize.Height - 28));
            int top = Math.Max(14, (pnlActions.ClientSize.Height - buttonHeight) / 2);

            int searchWidth = pnlActions.ClientSize.Width >= 1300 ? 238 : 220;
            int deleteWidth = pnlActions.ClientSize.Width >= 1300 ? 296 : 260;
            int cancelWidth = pnlActions.ClientSize.Width >= 1300 ? 206 : 186;
            btnSearch.Bounds = new Rectangle(margin, top, searchWidth, buttonHeight);
            btnDelete.Bounds = new Rectangle(btnSearch.Right + gap, top, deleteWidth, buttonHeight);
            btnCancel.Bounds = new Rectangle(btnDelete.Right + gap, top, cancelWidth, buttonHeight);

            pnlActionsSearch.Bounds = new Rectangle(0, 0, btnCancel.Right + gap, pnlActions.ClientSize.Height);

            int exitWidth = pnlActions.ClientSize.Width >= 1300 ? 216 : 160;
            int extrasWidth = pnlActions.ClientSize.Width >= 1300 ? 200 : 160;
            btnExit.Bounds = new Rectangle(
                Math.Max(margin, pnlActions.ClientSize.Width - margin - exitWidth),
                top,
                exitWidth,
                buttonHeight);
            uxMenuButtonExtras.Bounds = new Rectangle(
                Math.Max(margin, btnExit.Left - gap - extrasWidth),
                top,
                extrasWidth,
                buttonHeight);

            if (actionHintLabel != null)
            {
                actionHintLabel.Visible = false;
            }
        }

        private void EnsureComfortableWindowBounds()
        {
            Rectangle workingArea = Screen.FromControl(this).WorkingArea;
            int minWidth = Math.Min(1120, workingArea.Width);
            int minHeight = Math.Min(600, workingArea.Height);
            MinimumSize = new Size(Math.Max(MinimumSize.Width, minWidth), Math.Max(MinimumSize.Height, minHeight));

            if (Width < MinimumSize.Width)
            {
                Width = MinimumSize.Width;
            }
            if (Height < MinimumSize.Height)
            {
                Height = MinimumSize.Height;
            }

            if (Right > workingArea.Right)
            {
                Left = Math.Max(workingArea.Left, workingArea.Right - Width);
            }
            if (Bottom > workingArea.Bottom)
            {
                Top = Math.Max(workingArea.Top, workingArea.Bottom - Height);
            }
        }

        private void EnsureTreeEmptyStateLabel()
        {
            if (treeEmptyStatePanel != null)
            {
                return;
            }

            treeEmptyStatePanel = new Panel
            {
                Name = "pnlTreeEmptyState",
                TabStop = false
            };
            treeEmptyStatePanel.Click += (s, e) => txtSearchDirectory.Focus();
            treeEmptyStatePanel.Paint += TreeEmptyStatePanel_Paint;

            treeEmptyStateIcon = new PictureBox
            {
                Name = "picTreeEmptyState",
                Image = null,
                SizeMode = PictureBoxSizeMode.CenterImage,
                Visible = false,
                TabStop = false
            };
            treeEmptyStateIcon.Click += (s, e) => txtSearchDirectory.Focus();

            treeEmptyStateLabel = new Label
            {
                Name = "lbTreeEmptyStateTitle",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                UseMnemonic = false,
                TabStop = false,
                Text = TXT.Translate("Choose a folder to scan.")
            };
            treeEmptyStateLabel.Click += (s, e) => txtSearchDirectory.Focus();

            treeEmptyStateDetailLabel = new Label
            {
                Name = "lbTreeEmptyStateDetail",
                AutoSize = false,
                TextAlign = ContentAlignment.TopCenter,
                UseMnemonic = false,
                TabStop = false,
                Text = TXT.Translate("RED++ shows reviewable results before\r\nanything is deleted.")
            };
            treeEmptyStateDetailLabel.Click += (s, e) => txtSearchDirectory.Focus();

            treeEmptyStateTrustLabel = new Label
            {
                Name = "lbTreeEmptyStateTrust",
                AutoSize = false,
                TextAlign = ContentAlignment.TopCenter,
                UseMnemonic = false,
                TabStop = false,
                Text = string.Empty
            };
            treeEmptyStateTrustLabel.Click += (s, e) => txtSearchDirectory.Focus();

            treeEmptyStateStep1Label = CreateEmptyStateStepLabel(TXT.Translate("Pick a root folder, then scan."));
            treeEmptyStateStep2Label = CreateEmptyStateStepLabel(TXT.Translate("Results are shown for review."));
            treeEmptyStateStep3Label = CreateEmptyStateStepLabel(TXT.Translate("Nothing is deleted until you confirm."));

            treeEmptyStatePanel.Controls.Add(treeEmptyStateIcon);
            treeEmptyStatePanel.Controls.Add(treeEmptyStateLabel);
            treeEmptyStatePanel.Controls.Add(treeEmptyStateDetailLabel);
            treeEmptyStatePanel.Controls.Add(treeEmptyStateTrustLabel);
            treeEmptyStatePanel.Controls.Add(treeEmptyStateStep1Label);
            treeEmptyStatePanel.Controls.Add(treeEmptyStateStep2Label);
            treeEmptyStatePanel.Controls.Add(treeEmptyStateStep3Label);
            gbFind.Controls.Add(treeEmptyStatePanel);
            treeEmptyStatePanel.BringToFront();
            tvSearchResults.LocationChanged += (s, e) => PositionTreeEmptyStateLabel();
            tvSearchResults.SizeChanged += (s, e) => PositionTreeEmptyStateLabel();
            PositionTreeEmptyStateLabel();
        }

        private Label CreateEmptyStateStepLabel(string text)
        {
            var label = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false,
                TabStop = false,
                Text = text
            };
            label.Click += (s, e) => txtSearchDirectory.Focus();
            return label;
        }

        private void PositionTreeEmptyStateLabel()
        {
            if (treeEmptyStatePanel == null)
            {
                return;
            }

            treeEmptyStatePanel.Bounds = new Rectangle(
                tvSearchResults.Left + 1,
                tvSearchResults.Top + 1,
                Math.Max(160, tvSearchResults.Width - 2),
                Math.Max(80, tvSearchResults.Height - 2));

            int contentWidth = Math.Min(560, Math.Max(220, treeEmptyStatePanel.ClientSize.Width - 72));
            int contentHeight = 350;
            int left = Math.Max(12, (treeEmptyStatePanel.ClientSize.Width - contentWidth) / 2);
            int top = Math.Max(18, (treeEmptyStatePanel.ClientSize.Height - contentHeight) / 2);

            treeEmptyStateIcon.Bounds = new Rectangle(left + (contentWidth - 92) / 2, top, 92, 78);
            treeEmptyStateLabel.Bounds = new Rectangle(left, treeEmptyStateIcon.Bottom + 22, contentWidth, 34);
            treeEmptyStateDetailLabel.Bounds = new Rectangle(left, treeEmptyStateLabel.Bottom + 10, contentWidth, 58);
            treeEmptyStateTrustLabel.Bounds = new Rectangle(left, treeEmptyStateDetailLabel.Bottom + 14, contentWidth, 2);
            int stepLeft = left + Math.Max(98, (contentWidth - 430) / 2);
            int stepTop = treeEmptyStateTrustLabel.Bottom + 20;
            int stepWidth = Math.Min(420, contentWidth - (stepLeft - left));
            treeEmptyStateStep1Label.Bounds = new Rectangle(stepLeft + 38, stepTop, stepWidth - 38, 30);
            treeEmptyStateStep2Label.Bounds = new Rectangle(stepLeft + 38, stepTop + 43, stepWidth - 38, 30);
            treeEmptyStateStep3Label.Bounds = new Rectangle(stepLeft + 38, stepTop + 86, stepWidth - 38, 30);
            treeEmptyStatePanel.Invalidate();
        }

        private void TreeEmptyStatePanel_Paint(object sender, PaintEventArgs e)
        {
            if (treeEmptyStatePanel == null)
            {
                return;
            }

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Rectangle bounds = treeEmptyStatePanel.ClientRectangle;
            using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                bounds,
                DarkTheme.Mantle,
                DarkTheme.Base,
                45f))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }

            Rectangle icon = treeEmptyStateIcon.Bounds;
            DrawFolderGlyph(e.Graphics, icon);
            DrawStepGlyph(e.Graphics, new Point(treeEmptyStateStep1Label.Left - 30, treeEmptyStateStep1Label.Top + 6), 0);
            DrawStepGlyph(e.Graphics, new Point(treeEmptyStateStep2Label.Left - 30, treeEmptyStateStep2Label.Top + 6), 1);
            DrawStepGlyph(e.Graphics, new Point(treeEmptyStateStep3Label.Left - 30, treeEmptyStateStep3Label.Top + 6), 2);
        }

        private void DrawFolderGlyph(Graphics g, Rectangle bounds)
        {
            Rectangle folder = new Rectangle(bounds.Left + 8, bounds.Top + 18, bounds.Width - 16, bounds.Height - 25);
            var tab = new Point[]
            {
                new Point(folder.Left, folder.Top + 11),
                new Point(folder.Left + 12, folder.Top + 11),
                new Point(folder.Left + 20, folder.Top),
                new Point(folder.Left + 40, folder.Top),
                new Point(folder.Left + 50, folder.Top + 11),
                new Point(folder.Right, folder.Top + 11),
                new Point(folder.Right, folder.Bottom),
                new Point(folder.Left, folder.Bottom)
            };
            using (var pen = new Pen(DarkTheme.Overlay0, 3))
            {
                pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                g.DrawPolygon(pen, tab);
            }
        }

        private void DrawStepGlyph(Graphics g, Point origin, int index)
        {
            Rectangle r = new Rectangle(origin.X, origin.Y, 20, 20);
            if (index == 0)
            {
                using (var pen = new Pen(DarkTheme.Blue, 3))
                {
                    g.DrawEllipse(pen, r.X, r.Y, 12, 12);
                    g.DrawLine(pen, r.X + 12, r.Y + 12, r.X + 20, r.Y + 20);
                }
            }
            else if (index == 1)
            {
                Point[] shield =
                {
                    new Point(r.X + 10, r.Y),
                    new Point(r.X + 18, r.Y + 4),
                    new Point(r.X + 16, r.Y + 15),
                    new Point(r.X + 10, r.Y + 20),
                    new Point(r.X + 4, r.Y + 15),
                    new Point(r.X + 2, r.Y + 4)
                };
                using (var brush = new SolidBrush(DarkTheme.Green))
                using (var pen = new Pen(ControlPaint.Light(DarkTheme.Green), 1))
                {
                    g.FillPolygon(brush, shield);
                    g.DrawPolygon(pen, shield);
                }
                using (var pen = new Pen(Color.White, 2))
                {
                    g.DrawLines(pen, new[] { new Point(r.X + 6, r.Y + 10), new Point(r.X + 9, r.Y + 13), new Point(r.X + 15, r.Y + 7) });
                }
            }
            else
            {
                using (var pen = new Pen(DarkTheme.Red, 2))
                {
                    g.DrawRectangle(pen, r.X + 5, r.Y + 7, 11, 11);
                    g.DrawLine(pen, r.X + 3, r.Y + 5, r.X + 18, r.Y + 5);
                    g.DrawLine(pen, r.X + 8, r.Y + 3, r.X + 13, r.Y + 3);
                    g.DrawLine(pen, r.X + 8, r.Y + 10, r.X + 8, r.Y + 16);
                    g.DrawLine(pen, r.X + 13, r.Y + 10, r.X + 13, r.Y + 16);
                }
            }
        }

        private void AdjustSearchLayout()
        {
            if (gbFind.ClientSize.Width <= 0 || gbFind.ClientSize.Height <= 0)
            {
                return;
            }

            EnsurePathHintLabel();
            const int margin = 14;
            const int gap = 8;
            int sideWidth = gbFind.ClientSize.Width >= 1280 ? 262 : 220;
            bool showLegend = gbFind.ClientSize.Width >= 900;
            int inputTop = 54;
            int inputHeight = 46;

            pnlIconDesc.Visible = showLegend;
            if (showLegend)
            {
                pnlIconDesc.Left = gbFind.ClientSize.Width - sideWidth - margin;
                pnlIconDesc.Width = sideWidth;
                btnSearchDirectoryBrowseFor.Width = Math.Min(170, sideWidth);
                btnSearchDirectoryBrowseFor.Left = pnlIconDesc.Left - btnSearchDirectoryBrowseFor.Width - gap * 2;
            }
            else
            {
                btnSearchDirectoryBrowseFor.Width = 132;
                btnSearchDirectoryBrowseFor.Left = gbFind.ClientSize.Width - btnSearchDirectoryBrowseFor.Width - margin;
            }

            txtSearchDirectory.Left = margin;
            txtSearchDirectory.Top = inputTop;
            btnSearchDirectoryBrowseFor.Top = inputTop - 1;
            btnSearchDirectoryBrowseFor.Height = inputHeight + 2;
            txtSearchDirectory.Width = Math.Max(120, btnSearchDirectoryBrowseFor.Left - txtSearchDirectory.Left - gap);
            pathHintLabel.Visible = false;

            int resultsTop = txtSearchDirectory.Bottom + 13;
            int treeRight = showLegend ? pnlIconDesc.Left - gap : gbFind.ClientSize.Width - margin;
            tvSearchResults.Left = margin;
            tvSearchResults.Top = resultsTop;
            tvSearchResults.Width = Math.Max(160, treeRight - tvSearchResults.Left);
            tvSearchResults.Height = Math.Max(120, gbFind.ClientSize.Height - tvSearchResults.Top - margin);
            if (showLegend)
            {
                pnlIconDesc.Top = tvSearchResults.Top;
                pnlIconDesc.Height = tvSearchResults.Height;
                LayoutLegend();
            }
            lbFastModeInfo.AutoSize = false;
            lbFastModeInfo.Bounds = new Rectangle(
                tvSearchResults.Left + 16,
                tvSearchResults.Top + 16,
                Math.Max(120, tvSearchResults.Width - 32),
                24);
            lbFastModeInfo.TextAlign = ContentAlignment.MiddleCenter;
            tvSearchResults.BringToFront();
            pnlIconDesc.BringToFront();
            txtSearchDirectory.BringToFront();
            btnSearchDirectoryBrowseFor.BringToFront();
            PositionTreeEmptyStateLabel();
            if (treeEmptyStatePanel != null)
            {
                treeEmptyStatePanel.BringToFront();
            }
        }

        private void LayoutLegend()
        {
            if (!pnlIconDesc.Visible)
            {
                return;
            }

            int x = 10;
            int y = 34;
            int labelWidth = Math.Max(60, pnlIconDesc.ClientSize.Width - 40);
            lbIconDesc.Location = new Point(x, 10);
            lbIconDesc.AutoSize = false;
            lbIconDesc.Width = Math.Max(80, pnlIconDesc.ClientSize.Width - 20);
            lbIconDesc.Height = 18;
            lbIconDesc.TextAlign = ContentAlignment.MiddleLeft;

            foreach (Control control in pnlIconDesc.Controls)
            {
                if (control.Name == "picIcon")
                {
                    control.Location = new Point(x, y);
                    y += control.Height + 6;
                }
                else if (control.Name == "picLabel")
                {
                    control.AutoSize = false;
                    control.Location = new Point(x + 22, control.Location.Y);
                    control.Width = labelWidth;
                    control.Height = 22;
                    control.Top = y - control.Height - 5;
                }
            }

            int swatchTop = Math.Max(y + 12, pnlIconDesc.ClientSize.Height - 88);
            lbHorzLine1.Bounds = new Rectangle(0, swatchTop - 10, pnlIconDesc.ClientSize.Width, 1);
            LayoutLegendSwatch(pnlColorDoNoTouch, lbColorDoNotTouch, swatchTop);
            LayoutLegendSwatch(pnlColorToBeDeleted, lbColorToBeDeleted, swatchTop + 26);
            LayoutLegendSwatch(pnlColorProtected, lbColorProtected, swatchTop + 52);
        }

        private void LayoutLegendSwatch(Panel swatch, Label label, int top)
        {
            swatch.Bounds = new Rectangle(10, top + 2, 14, 14);
            label.AutoSize = false;
            label.Bounds = new Rectangle(32, top, Math.Max(60, pnlIconDesc.ClientSize.Width - 42), 20);
            label.TextAlign = ContentAlignment.MiddleLeft;
        }

        private void RefreshThemeDependentUi()
        {
            if (treeEmptyStatePanel != null)
            {
                treeEmptyStatePanel.BackColor = DarkTheme.Mantle;
                treeEmptyStatePanel.Invalidate();
            }

            if (treeEmptyStateIcon != null)
            {
                treeEmptyStateIcon.BackColor = DarkTheme.Mantle;
            }

            if (treeEmptyStateLabel != null)
            {
                treeEmptyStateLabel.BackColor = Color.Transparent;
                treeEmptyStateLabel.ForeColor = DarkTheme.Text;
                treeEmptyStateLabel.Font = new Font(Font.FontFamily, Font.Size + 2, FontStyle.Bold);
            }

            if (treeEmptyStateDetailLabel != null)
            {
                treeEmptyStateDetailLabel.BackColor = Color.Transparent;
                treeEmptyStateDetailLabel.ForeColor = DarkTheme.Subtext1;
                treeEmptyStateDetailLabel.Font = new Font(Font.FontFamily, Font.Size + 1, FontStyle.Regular);
            }

            if (treeEmptyStateTrustLabel != null)
            {
                treeEmptyStateTrustLabel.BackColor = Color.Transparent;
                treeEmptyStateTrustLabel.ForeColor = DarkTheme.Subtext0;
                treeEmptyStateTrustLabel.Font = new Font(Font.FontFamily, Font.Size, FontStyle.Regular);
            }

            if (treeEmptyStateStep1Label != null)
            {
                treeEmptyStateStep1Label.BackColor = Color.Transparent;
                treeEmptyStateStep1Label.ForeColor = DarkTheme.Subtext1;
                treeEmptyStateStep1Label.Font = new Font(Font.FontFamily, Font.Size + 1, FontStyle.Regular);
                treeEmptyStateStep2Label.BackColor = Color.Transparent;
                treeEmptyStateStep2Label.ForeColor = DarkTheme.Subtext1;
                treeEmptyStateStep2Label.Font = treeEmptyStateStep1Label.Font;
                treeEmptyStateStep3Label.BackColor = Color.Transparent;
                treeEmptyStateStep3Label.ForeColor = DarkTheme.Subtext1;
                treeEmptyStateStep3Label.Font = treeEmptyStateStep1Label.Font;
            }

            if (pathHintLabel != null)
            {
                pathHintLabel.BackColor = DarkTheme.Base;
                pathHintLabel.ForeColor = DarkTheme.Subtext0;
            }

            if (actionHintLabel != null)
            {
                actionHintLabel.BackColor = DarkTheme.Surface0;
                actionHintLabel.ForeColor = DarkTheme.Subtext1;
            }

            pnlIconDesc.BackColor = DarkTheme.Surface0;
            lbIconDesc.BackColor = DarkTheme.Surface0;
            lbIconDesc.ForeColor = DarkTheme.Text;
            lbColorDoNotTouch.BackColor = DarkTheme.Surface0;
            lbColorDoNotTouch.ForeColor = DarkTheme.Text;
            lbColorToBeDeleted.BackColor = DarkTheme.Surface0;
            lbColorToBeDeleted.ForeColor = DarkTheme.Text;
            lbColorProtected.BackColor = DarkTheme.Surface0;
            lbColorProtected.ForeColor = DarkTheme.Text;
            foreach (Control control in pnlIconDesc.Controls)
            {
                if (control.Name == "picLabel")
                {
                    control.BackColor = DarkTheme.Surface0;
                    control.ForeColor = DarkTheme.Text;
                }
            }
            lbHorzLine1.BackColor = DarkTheme.Surface2;
            pnlColorDoNoTouch.BackColor = TreeManager.ColorDoNotTouch;
            pnlColorToBeDeleted.BackColor = TreeManager.ColortoBeDeleted;
            pnlColorProtected.BackColor = TreeManager.ColorProtected;
            pnlColorDoNoTouch.BorderStyle = BorderStyle.FixedSingle;
            pnlColorToBeDeleted.BorderStyle = BorderStyle.FixedSingle;
            pnlColorProtected.BorderStyle = BorderStyle.FixedSingle;
            lbFastModeInfo.BackColor = DarkTheme.Surface0;
            lbFastModeInfo.ForeColor = DarkTheme.Subtext0;
            lbUiStatus.ForeColor = UiIsBusy() ? DarkTheme.Warning : DarkTheme.Green;
            lbStatus.ForeColor = DarkTheme.Text;
            if (progressPercentLabel != null)
            {
                progressPercentLabel.ForeColor = DarkTheme.Text;
            }
            pnlActions.BackColor = DarkTheme.Surface0;
            pnlActionsSearch.BackColor = DarkTheme.Surface0;
            StyleActionButtons();
            TreeMgr?.RefreshTheme();
            UpdateTreeEmptyState();
            UpdateActionHint();
        }

        private void StyleActionButtons()
        {
            StyleButton(btnSearch, DarkTheme.Blue, DarkTheme.Base, true);
            StyleButton(btnDelete, DarkTheme.Red, DarkTheme.Base, btnDelete.Enabled);
            StyleButton(btnCancel, DarkTheme.Surface1, DarkTheme.Base, btnCancel.Enabled);
            StyleButton(uxMenuButtonExtras, DarkTheme.Surface1, DarkTheme.Base, uxMenuButtonExtras.Enabled);
            StyleButton(btnExit, DarkTheme.Surface1, DarkTheme.Base, btnExit.Enabled);
        }

        private void StyleButton(Button button, Color accent, Color fallback, bool active)
        {
            if (DarkTheme.IsHighContrast)
            {
                return;
            }

            bool enabled = button.Enabled;
            button.Font = new Font(Font.FontFamily, Font.Size + 2, FontStyle.Regular);
            if (button == btnSearch || button == btnDelete)
            {
                button.Font = new Font(Font.FontFamily, Font.Size + 2, FontStyle.Bold);
            }
            button.BackColor = active && enabled ? accent : fallback;
            button.ForeColor = enabled
                ? (active ? (DarkTheme.IsHighContrast ? SystemColors.HighlightText : Color.White) : DarkTheme.Text)
                : DarkTheme.DisabledText;
            button.FlatAppearance.BorderColor = enabled
                ? (active ? accent : DarkTheme.Surface1)
                : DarkTheme.Surface0;
            button.FlatAppearance.MouseOverBackColor = active ? ControlPaint.Light(accent, 0.12f) : DarkTheme.ButtonHover;
            button.FlatAppearance.MouseDownBackColor = active ? ControlPaint.Dark(accent, 0.08f) : DarkTheme.ButtonDown;
        }

        private void UpdateTreeEmptyState()
        {
            if (treeEmptyStateLabel == null)
            {
                return;
            }

            bool shouldShow = !UiIsBusy() && tvSearchResults.Nodes.Count == 0;
            if (shouldShow)
            {
                if (reviewSurfaceHasCompletedRun)
                {
                    treeEmptyStateLabel.Text = TXT.Translate("No eligible results found.");
                    treeEmptyStateDetailLabel.Text = TXT.Translate("The selected folder was checked. Nothing is currently queued for deletion.");
                    treeEmptyStateTrustLabel.Text = TXT.Translate("Kept and protected items stay untouched.");
                }
                else if (!string.IsNullOrWhiteSpace(txtSearchDirectory.Text))
                {
                    treeEmptyStateLabel.Text = TXT.Translate("Choose a folder to scan.");
                    treeEmptyStateDetailLabel.Text = TXT.Translate("RED++ shows reviewable results before\r\nanything is deleted.");
                    treeEmptyStateTrustLabel.Text = string.Empty;
                }
                else
                {
                    treeEmptyStateLabel.Text = TXT.Translate("Choose a folder to scan.");
                    treeEmptyStateDetailLabel.Text = TXT.Translate("RED++ shows reviewable results before\r\nanything is deleted.");
                    treeEmptyStateTrustLabel.Text = string.Empty;
                }
            }
            treeEmptyStatePanel.Visible = shouldShow;
            if (shouldShow)
            {
                treeEmptyStatePanel.BringToFront();
            }
            UpdateActionHint();
        }

        private void UpdateActionHint()
        {
            if (actionHintLabel == null)
            {
                return;
            }

            if (UiIsBusy())
            {
                actionHintLabel.Text = TXT.Translate("Working. Progress and detailed status appear below.");
            }
            else if (btnDelete.Enabled)
            {
                actionHintLabel.Text = TXT.Translate("Review the marked results, then choose Review & Delete.");
            }
            else if (reviewSurfaceHasCompletedRun)
            {
                actionHintLabel.Text = TXT.Translate("Scan complete. No eligible items are currently queued.");
            }
            else if (!string.IsNullOrWhiteSpace(txtSearchDirectory.Text))
            {
                actionHintLabel.Text = TXT.Translate("Ready to scan. Results are reviewed before cleanup.");
            }
            else
            {
                actionHintLabel.Text = TXT.Translate("Choose a folder or drop folders into the window.");
            }
        }

        /// <summary>
        /// Check if RED+ has been added to the Explorer Context menu
        /// </summary>
        private void ExplorerIntegrationCheck()
        {
            gbExplorerIntegration.Enabled = true;
            lblExplorerIntegrationInfo.Text = TXT.Translate("This is a Per User setting");

            string command;
            int isIntegrated = SystemFunctions.IsRegKeyIntegratedIntoWindowsExplorer(out command);

            switch (isIntegrated)
            {
                case 2:
                    // Integrated with HKCU method
                    btnExplorerIntegrate.Enabled = false;
                    btnExplorerRemove.Enabled = true;
                    break;
                case 1:
                    // Integrated with Legacy HKCR method. Requires Admin rights
                    btnExplorerRemove.Image = Properties.Resources.x16_Shield1;
                    btnExplorerIntegrate.Enabled = false;
                    btnExplorerRemove.Enabled = true;
                    lblExplorerIntegrationInfo.Text = TXT.Translate("Start the application as an Admin user to change this");
                    if (SystemFunctions.IsAdmin())
                    {
                        lblExplorerIntegrationInfo.ForeColor = DarkTheme.DisabledText;
                    }
                    else
                    {
                        gbExplorerIntegration.Enabled = false;
                        lblExplorerIntegrationInfo.Font = new Font(DefaultFont, FontStyle.Bold);
                    }
                    break;
                case -1:
                    // Error determining integration
                    btnExplorerIntegrate.Enabled = true;
                    btnExplorerRemove.Enabled = false;
                    lblExplorerIntegrationInfo.Visible = true;
                    lblExplorerIntegrationInfo.Text = TXT.Translate("Unable to determine explorer integration status");
                    break;
                default:
                    // Not currently integrated
                    btnExplorerIntegrate.Enabled = true;
                    btnExplorerRemove.Enabled = false;
                    if (RedAssist.IsDrivePathRemovable(Application.ExecutablePath))
                    {
                        gbExplorerIntegration.Enabled = false;
                        lblExplorerIntegrationInfo.Visible = true;
                        lblExplorerIntegrationInfo.Text = TXT.Translate("Running from a removable drive. Cannot integrate");
                    }
                    break;
            }
            chkExplorerIntegrateAutoSearch.Enabled = btnExplorerIntegrate.Enabled;
            chkExplorerIntegrateAutoSearch.Visible = chkExplorerIntegrateAutoSearch.Enabled;
            if (!string.IsNullOrWhiteSpace(command))
            {
                uxToolTips.SetToolTip(btnExplorerRemove, command);
            }
        }

        /// <summary>
        /// Read and apply command line arguments
        /// </summary>
        private void ProcessCommandLineArgs()
        {
            RedConfig.Runtime.Volatile.LastUsedDirectory = string.Empty;
            string[] args = Environment.GetCommandLineArgs();

            if (args.Length > 1)
            {
                args[0] = string.Empty;

                // Extract any switches
                int i = 1;
                while (i < args.Length && args[i].StartsWith("-"))
                {
                    if (args[i].ToLowerInvariant() == "-autosearch")
                    {
                        AutoSearchOnStart = true;
                    }
                    args[i] = string.Empty;
                    i++;
                }
                // Any remaining args are treated as a pathname
                string path = string.Join(string.Empty, args).Replace("\"", string.Empty).Trim();
                if (path.Length > 0)
                {
                    // add ending backslash
                    if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    {
                        path += Path.DirectorySeparatorChar.ToString();
                    }

                    RedConfig.Runtime.Volatile.LastUsedDirectory = path;
                }
                else
                {
                    AutoSearchOnStart = false;
                }
            }
        }

        private void DrawDirectoryIcons()
        {
            #region Set and display folder status icons

            Dictionary<string, string> icons = new Dictionary<string, string>
            {
                { "home", TXT.Words.Root },
                { "folder", TXT.Words.Empty },
                { "folder_trash_files", TXT.Words.ContainsTrash },
                { "folder_hidden", TXT.Words.Hidden },
                { "folder_lock", TXT.Words.Locked },
                { "folder_never_empty", TXT.Words.NeverEmpty },
                { "folder_warning", TXT.Words.Failed },
                { "protected_icon", TXT.Words.Protected },
                { "deleted", TXT.Words.Deleted }
            };

            int xpos = 6;
            int ypos = 30;

            foreach (string key in icons.Keys)
            {
                Image Icon = (Image)ilFolderIcons.Images[key];

                PictureBox picIcon = new PictureBox
                {
                    Image = Icon,
                    Location = new Point(xpos, ypos),
                    Name = "picIcon",
                    Size = new Size(Icon.Width, Icon.Height)
                };

                Label picLabel = new Label
                {
                    Text = icons[key],
                    Location = new Point(xpos + Icon.Width + 2, ypos + 2),
                    Name = "picLabel",
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft
                };

                pnlIconDesc.Controls.Add(picIcon);
                pnlIconDesc.Controls.Add(picLabel);

                ypos += Icon.Height + 6;
            }

            RefreshThemeDependentUi();

            #endregion Set and display folder status icons
        }

        #endregion Init methods

        #region Step 1: Scan for empty directories

        /// <summary>
        /// Starts the Scan-Progress
        /// </summary>
        private void btnSearch_Click(object sender, EventArgs e)
        {
            // Check given folder
            DirectoryInfo selectedDirectory;
            try
            {
                selectedDirectory = new DirectoryInfo(SanitizeDirectoryName(txtSearchDirectory.Text));

                if (!selectedDirectory.Exists)
                {
                    UiAssist.MsgBoxError(this, TXT.Translate("Choose an existing local, UNC, or network folder before scanning."));
                    return;
                }
            }
            catch (Exception ex)
            {
                UiAssist.MsgBoxException(this, TXT.Translate("The given directory caused a problem"), ex);
                return;
            }

            SetProcessActiveLock(true);
            UiProgressBar(true);
            reviewSurfaceHasCompletedRun = false;
            UpdateTreeEmptyState();
            btnDelete.Enabled = false;
            StyleActionButtons();
            UpdateContextMenu(cmTreeview, false);

            RunData.StartFolder = selectedDirectory;
            UpdateRuntimeDataObject();

            RunData.AppendScanResults = multiRootContinuation;
            TreeMgr.OnSearchStart(RunData.StartFolder, multiRootContinuation);
            multiRootContinuation = false;

            RunData.AddLogSpacer();
            SetStatusAndLogMessage(TXT.Translate("Scanning for empty directories..."));

            RuntimeWatch.Reset();
            RuntimeWatch.Start();

            tcMain.SelectedTab = tabSearch;

            btnSearch.Enabled = false;
            Core.SearchingForEmptyDirectories();
        }

        private string SanitizeDirectoryName(string dirName)
        {
            string respx = dirName;
            if (!string.IsNullOrWhiteSpace(dirName))
            {
                respx = Environment.ExpandEnvironmentVariables(respx);

                if (respx.StartsWith(@"\\") && respx.EndsWith(@"\"))
                {
                    respx = respx.TrimEnd(new[] { '\\' });
                }
            }
            return respx;
        }

        private void Core_OnProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            lbStatus.Text = (string)e.UserState;
        }

        private void Core_OnFoundEmptyDir(object sender, FoundEmptyDirInfoEventArgs e)
        {
            TreeMgr.AddOrUpdateDirectoryNode(e.ScanResult.Directory, e.ScanResult.SearchStatus, e.ScanResult.ErrorMessage);
        }

        private void Core_OnFoundFinishedScanForEmptyDirs(object sender, FinishedScanForEmptyDirsEventArgs e)
        {
            // Search finished

            RuntimeWatch.Stop();
            string runtime = string.Format("{0:D2}:{1:D2}.{2:D2}", RuntimeWatch.Elapsed.Minutes, RuntimeWatch.Elapsed.Seconds, RuntimeWatch.Elapsed.Milliseconds);
            string scanSummary = (e.EmptyFolderCount + e.EmptyFileCount) == 0
                ? TXT.Translate("Scan complete: no eligible empty directories or empty files found. Checked {0} directories in {1}.", e.FolderCount, runtime)
                : TXT.Translate("Scan complete: {0} empty directories and {1} empty files eligible. Checked {2} directories in {3}.", e.EmptyFolderCount, e.EmptyFileCount, e.FolderCount, runtime);
            SetStatusAndLogMessage(scanSummary);

            if (RedConfig.Options.AutoProtectRoot)
            {
                TreeMgr.ProtectRoot();
            }

            UiProgressBar(false, true, Math.Max(1, e.EmptyFolderCount + e.EmptyFileCount));
            UpdateContextMenu(cmTreeview, true);
            SetProcessActiveLock(false);
            btnSearch.Enabled = true;
            //btnSearch.Text = TXT.Translate("&Search Again");
            btnDelete.Enabled = (e.EmptyFolderCount > 0 || e.EmptyFileCount > 0);
            reviewSurfaceHasCompletedRun = true;
            StyleActionButtons();

            TreeMgr.OnSearchFinished();
            UpdateTreeEmptyState();

            if (pendingScanPaths.Count > 0)
            {
                string nextPath = pendingScanPaths.Dequeue();
                txtSearchDirectory.Text = nextPath;
                multiRootContinuation = true;
                btnSearch.PerformClick();
            }
        }

        #endregion Step 1: Scan for empty directories

        #region Step 2: Delete empty directories

        private void btnDelete_Click(object sender, EventArgs e)
        {
            UpdateRuntimeDataObject();

            if (RunData.DeleteMode == DeleteModes.MoveToFolder)
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = TXT.Translate("Select the folder where empty directories and empty files will be moved to");
                    dlg.ShowNewFolderButton = true;
                    if (dlg.ShowDialog(this) != DialogResult.OK)
                        return;
                    SystemFunctions.MoveToFolderTarget = dlg.SelectedPath;
                }
            }

            if (RunData.DeleteMode != DeleteModes.Simulate)
            {
                int totalCount = RunData.ScanResults.Count;
                int protectedCount = RunData.ProtectedFolderList.Count;
                int deleteCount = totalCount - protectedCount;
                int fileDeleteCount = RunData.EmptyFileResults.Count;

                string action = BuildDeleteConfirmationMessage(deleteCount, fileDeleteCount);
                if (protectedCount > 0)
                {
                    action += RedGetText.CrLf1 + TXT.Translate("{0} protected directories will be skipped.", protectedCount);
                }

                if (DialogResult.No == UiAssist.MsgBoxYesNo(this, action, MessageBoxDefaultButton.Button2))
                {
                    return;
                }
            }

            RunData.AddLogSpacer();
            SetStatusAndLogMessage(TXT.Translate("Deletion started. RED++ will re-check each item before changing it."));

            UiProgressBar(true, true, Math.Max(1, RunData.ScanResults.Count + RunData.EmptyFileResults.Count));
            UpdateTreeEmptyState();
            UpdateContextMenu(cmTreeview, false);
            SetProcessActiveLock(true);
            btnSearch.Enabled = false;
            btnDelete.Enabled = false;
            StyleActionButtons();

            TreeMgr.OnDeletionProcessStart();

            RuntimeWatch.Reset();
            RuntimeWatch.Start();

            Core.StartDeleteProcess();
        }

        private void Core_OnDeleteProcessChanged(object sender, DeleteProcessUpdateEventArgs e)
        {
            switch (e.Status)
            {
                case DirectoryDeletionStatusTypes.Deleted:
                    lbStatus.Text = string.Format("{0} ({1} of {2})", TXT.Translate("Deleting eligible results"), e.ProgressStatus + 1, e.FolderCount);
                    TreeMgr.UpdateItemIcon(e.ScanResult, DirectoryIcons.deleted);
                    break;

                case DirectoryDeletionStatusTypes.Protected:
                    TreeMgr.UpdateItemIcon(e.ScanResult, DirectoryIcons.protected_icon);
                    break;

                default:
                    TreeMgr.UpdateItemIcon(e.ScanResult, DirectoryIcons.folder_warning);
                    break;
            }

            pbProgressStatus.Value = e.ProgressStatus;
            if (progressPercentLabel != null && pbProgressStatus.Maximum > 0)
            {
                int pct = (int)Math.Round((double)e.ProgressStatus * 100d / pbProgressStatus.Maximum);
                progressPercentLabel.Text = Math.Max(0, Math.Min(100, pct)).ToString() + "%";
            }
        }

        private void Core_OnDeleteError(object sender, DeletionErrorEventArgs e)
        {
            DeletionError errorDialog = new DeletionError();

            errorDialog.SetPath(e.Path);
            errorDialog.SetErrorMessage(e.ErrorMessage);

            DialogResult dialogResult = errorDialog.ShowDialog(this);

            errorDialog.Dispose();

            if (dialogResult == DialogResult.Abort)
            {
                Core.AbortDeletion();
            }
            else
            {
                // Hack: retry means -> ignore all errors
                if (dialogResult == DialogResult.Retry)
                {
                    RunData.HideDeletionErrors = true;
                }

                Core.ContinueDeleteProcess();
            }
        }

        private void Core_OnDeleteProcessFinished(object sender, DeleteProcessFinishedEventArgs e)
        {
            RuntimeWatch.Stop();
            string runtime = string.Format("{0:D2}:{1:D2}.{2:D2}", RuntimeWatch.Elapsed.Minutes, RuntimeWatch.Elapsed.Seconds, RuntimeWatch.Elapsed.Milliseconds);
            SetStatusAndLogMessage(string.Format(TXT.Translate("Deletion complete: {0} directories and {1} files changed; {2} directory failures, {3} file failures, {4} skipped. Runtime: {5}"), e.DeletedFolderCount, e.DeletedFileCount, e.FailedFolderCount, e.FailedFileCount, e.ProtectedCount, runtime));

            UiProgressBar(false);
            SetProcessActiveLock(false);
            btnSearch.Enabled = true;
            btnDelete.Enabled = false;
            reviewSurfaceHasCompletedRun = true;
            StyleActionButtons();

            // Increase deletion statistics (ignore overflows).
            unchecked { RedConfig.Runtime.Volatile.CountOfDeletions += e.DeletedFolderCount; }
            lblRedStats.Text = string.Format("{0}: {1}", TXT.Words.DeletedSoFar, RedConfig.Volatile.CountOfDeletions + RedConfig.Runtime.Volatile.CountOfDeletions);

            TreeMgr.OnDeletionProcessFinished();
            UpdateTreeEmptyState();
        }

        private string BuildDeleteConfirmationMessage(int deleteCount, int fileDeleteCount)
        {
            string countSummary = TXT.Translate("{0} empty directories and {1} empty files are eligible.", deleteCount, fileDeleteCount);
            string safety = TXT.Translate("RED++ will re-check every item immediately before changing it.");
            switch (RunData.DeleteMode)
            {
                case DeleteModes.MoveToFolder:
                    return TXT.Translate("Move eligible results to the selected folder?")
                        + RedGetText.CrLf2
                        + countSummary + RedGetText.CrLf1
                        + safety + RedGetText.CrLf1
                        + TXT.Translate("Move target: {0}", SystemFunctions.MoveToFolderTarget);
                case DeleteModes.Direct:
                    return TXT.Translate("Permanently delete eligible results?")
                        + RedGetText.CrLf2
                        + countSummary + RedGetText.CrLf1
                        + TXT.Translate("Direct mode bypasses the Recycle Bin.") + RedGetText.CrLf1
                        + safety;
                default:
                    return TXT.Translate("Recycle eligible results?")
                        + RedGetText.CrLf2
                        + countSummary + RedGetText.CrLf1
                        + TXT.Translate("Windows will move items to the Recycle Bin when available.") + RedGetText.CrLf1
                        + safety;
            }
        }

        #endregion Step 2: Delete empty directories

        #region Process core events / callbacks

        private void Core_OnCancelled(object sender, EventArgs e)
        {
            UiProgressBar(false);

            if (Core.CurrentProcessStep == WorkflowSteps.DeleteProcessRunning)
            {
                SetStatusAndLogMessage(TXT.Translate("Deletion canceled."));
            }
            else
            {
                SetStatusAndLogMessage(TXT.Translate("Operation canceled."));
            }

            SetProcessActiveLock(false);
            btnSearch.Enabled = true;
            btnDelete.Enabled = (RunData.ScanResults.Count > 0 || RunData.EmptyFileResults.Count > 0);
            StyleActionButtons();

            TreeMgr.OnProcessCancelled();
            UpdateTreeEmptyState();
        }

        private void Core_OnAborted(object sender, EventArgs e)
        {
            UiProgressBar(false);

            if (Core.CurrentProcessStep == WorkflowSteps.DeleteProcessRunning)
            {
                SetStatusAndLogMessage(TXT.Translate("Deletion stopped."));
            }
            else
            {
                SetStatusAndLogMessage(TXT.Translate("Operation stopped."));
            }

            SetProcessActiveLock(false);
            btnSearch.Enabled = true;
            btnDelete.Enabled = (RunData.ScanResults.Count > 0 || RunData.EmptyFileResults.Count > 0);
            StyleActionButtons();

            TreeMgr.OnProcessCancelled();
            UpdateTreeEmptyState();
        }

        private void Core_OnError(object sender, ErrorEventArgs e)
        {
            UiProgressBar(false);
            UiAssist.MsgBoxError(this, string.Format("{0}:{1}{2}", TXT.Words.Error, RedGetText.CrLf2, e.Message));
            UpdateTreeEmptyState();
        }

        #endregion Process core events / callbacks

        #region Tree view related methods

        /// <summary>
        /// User clicks twice on a folder
        /// </summary>
        private void tvSearchResults_DoubleClick(object sender, EventArgs e)
        {
            SystemFunctions.OpenDirectoryWithExplorer(TreeMgr.GetSelectedFolderPath());
        }

        private void tsmiOpenDirectory_Click(object sender, EventArgs e)
        {
            SystemFunctions.OpenDirectoryWithExplorer(TreeMgr.GetSelectedFolderPath());
        }

        private void tsmiSearchOnlyThisDirectory_Click(object sender, EventArgs e)
        {
            txtSearchDirectory.Text = TreeMgr.GetSelectedFolderPath();
            btnSearch.PerformClick();
        }

        private void tsmiProtectDirectoryOnce_Click(object sender, EventArgs e)
        {
            TreeMgr.ProtectSelected();
        }

        private void tsmiUnprotectDirectory_Click(object sender, EventArgs e)
        {
            TreeMgr.UnprotectSelected();
        }

        private void tsmiAddToFilterDirectoryIgnore_Click(object sender, EventArgs e)
        {
            if (tvSearchResults.SelectedNode == null)
            {
                return;
            }

            TreeMgr.ProtectSelected();
            RedConfig.Filters.AddDirectoryToIgnore("+|P|" + ((DirectoryInfo)tvSearchResults.SelectedNode.Tag).FullName);

            // ConfigToUI rewrites the path box from the saved config — keep what
            // the user is currently working with
            string currentPath = txtSearchDirectory.Text;
            ConfigToUI();
            txtSearchDirectory.Text = currentPath;

            // Focus Directories to Ignore Filter tab
            tcMain.SelectedTab = tabFilters;
            tcFilters.SelectedTab = tabFilterFoldersIgnore;
        }

        private void tsmiDeleteDirectory_Click(object sender, EventArgs e)
        {
            TreeMgr.DeleteSelectedDirectory();
        }

        private void tsmiExpandAll_Click(object sender, EventArgs e)
        {
            tvSearchResults.ExpandAll();
        }

        private void tsmiCollapseAll_Click(object sender, EventArgs e)
        {
            tvSearchResults.CollapseAll();
        }

        private void TreeMgr_OnProtectionStatusChanged(object sender, ProtectionStatusChangedEventArgs e)
        {
            if (e.Protected)
            {
                Core.AddProtectedFolder(e.Path);
            }
            else
            {
                Core.RemoveProtected(e.Path);
            }
        }

        private void TreeMgr_OnDeleteRequest(object sender, DeleteRequestFromTreeEventArgs e)
        {
            try
            {
                string deletePath = e.Directory;

                // To simplify the code here there is only the RecycleBinWithQuestion or simulate possible here
                // (all others will be ignored)
                SystemFunctions.ManuallyDeleteDirectory(deletePath, (DeleteModes)RedConfig.Options.DeleteMode);

                // Remove root node
                TreeMgr.RemoveNode(deletePath);
                reviewSurfaceHasCompletedRun = true;
                UpdateTreeEmptyState();

                SetStatusAndLogMessage(TXT.Translate("Deleted selected eligible branch: \"{0}\"", deletePath));

                // Disable the delete button because the user has to re-scan after he manually deleted a directory
                btnDelete.Enabled = false;
                StyleActionButtons();
            }
            catch (System.OperationCanceledException)
            {
                // The user canceled the deletion
            }
            catch (Exception ex)
            {
                string emsg = string.Format(TXT.Translate("Could not manually delete \"{0}\" because of the following error"), e.Directory);
                emsg = string.Format("{0}{1}{2}", emsg, RedGetText.CrLf2, ex.Message);
                RunData.AddLogMessage(emsg);
                UiAssist.MsgBoxError(this, emsg);
            }
        }

        private void mnuExportResultsToFile_Click(object sender, EventArgs e)
        {
            //RedAssist.ExportDirectoryList(this.Data, exportToFile: true);
            using (RedExportScanResults export = new RedExportScanResults())
            {
                export.ExportToFile(RunData.ScanResults);
            }
        }

        private void mnuExportResultsToClipboard_Click(object sender, EventArgs e)
        {
            using (RedExportScanResults export = new RedExportScanResults())
            {
                export.ExportToClipboard(RunData.ScanResults);
            }
        }

        private void mnuItemImportDryRunResults_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = TXT.Translate("Import Saved Dry-Run Results");
                dlg.Filter = TXT.Translate("RED++ Results (*.json;*.jsonl;*.ndjson)|*.json;*.jsonl;*.ndjson|All Files (*.*)|*.*");
                dlg.FilterIndex = 1;
                dlg.CheckFileExists = true;
                dlg.Multiselect = false;

                if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.FileName))
                {
                    return;
                }

                try
                {
                    RedImportedScanResults imported = RedImportScanResults.ReadFile(dlg.FileName);
                    RunData.ScanResults.Clear();
                    RunData.EmptyFileResults.Clear();

                    int importedFileCount = 0;
                    foreach (RedScanResultItem item in imported.DeletableResults)
                    {
                        if (item.Kind == Match.ResultKind.File)
                        {
                            RunData.EmptyFileResults.Add(new System.IO.FileInfo(item.FullPath));
                            importedFileCount++;
                        }
                        else
                        {
                            RunData.ScanResults.AddItem(item);
                        }
                    }

                    RunData.ProtectedFolderList = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    RunData.StartFolder = imported.Roots.Count == 1 ? imported.Roots[0].RootDirectory : null;

                    var directoryRoots = new List<RedImportedScanRoot>();
                    foreach (RedImportedScanRoot root in imported.Roots)
                    {
                        var dirRoot = new RedImportedScanRoot(root.RootDirectory);
                        foreach (RedScanResultItem item in root.Results)
                        {
                            if (item.Kind != Match.ResultKind.File)
                                dirRoot.Results.Add(item);
                        }
                        if (dirRoot.Results.Count > 0)
                            directoryRoots.Add(dirRoot);
                    }
                    TreeMgr.LoadImportedResults(directoryRoots);

                    tcMain.SelectedTab = tabSearch;
                    UiProgressBar(false, true, Math.Max(1, RunData.ScanResults.Count + RunData.EmptyFileResults.Count));
                    UpdateContextMenu(cmTreeview, true);
                    SetProcessActiveLock(false);
                    btnSearch.Enabled = true;
                    btnDelete.Enabled = RunData.ScanResults.Count > 0 || RunData.EmptyFileResults.Count > 0;
                    reviewSurfaceHasCompletedRun = true;
                    StyleActionButtons();

                    string statusMsg = importedFileCount > 0
                        ? TXT.Translate(
                            "Imported {0} review records from {1}. {2} empty directories and {3} empty files are eligible after safety checks.",
                            imported.ReviewCount,
                            Path.GetFileName(dlg.FileName),
                            RunData.ScanResults.Count,
                            importedFileCount)
                        : TXT.Translate(
                            "Imported {0} review records from {1}. {2} empty directories are eligible after safety checks.",
                            imported.ReviewCount,
                            Path.GetFileName(dlg.FileName),
                            RunData.ScanResults.Count);

                    SetStatusAndLogMessage(statusMsg);
                    UpdateTreeEmptyState();
                }
                catch (Exception ex)
                {
                    UiAssist.MsgBoxException(this, TXT.Translate("Could not import saved dry-run results"), ex);
                    btnDelete.Enabled = RunData.ScanResults.Count > 0;
                    StyleActionButtons();
                    UpdateTreeEmptyState();
                }
            }
        }

        #endregion Tree view related methods

        #region GUI related functions / events

        private void SetProcessActiveLock(bool isActive)
        {
            UiBusy(isActive);
            btnCancel.Enabled = isActive;
            txtSearchDirectory.Enabled = !isActive;
            btnSearchDirectoryBrowseFor.Enabled = !isActive;

            uxMenuButtonExtras.Enabled = !isActive;
            tcFilters.Enabled = !isActive;
            tcSettings.Enabled = !isActive;
            StyleActionButtons();
            UpdateActionHint();
        }

        private void UiProgressBar(bool isActive, bool isDeleting = false, int maximum = 100)
        {
            pbProgressStatus.Style = isDeleting ? ProgressBarStyle.Blocks : ProgressBarStyle.Marquee;
            pbProgressStatus.MarqueeAnimationSpeed = isDeleting ? 0 : 25;
            pbProgressStatus.Step = 1;
            pbProgressStatus.Minimum = 0;
            pbProgressStatus.Maximum = maximum;
            pbProgressStatus.Value = 0;

            if (isActive)
            {
                pbProgressStatus.Visible = true;
                if (progressPercentLabel != null)
                {
                    progressPercentLabel.Text = isDeleting ? "0%" : string.Empty;
                }
            }
            else
            {
                pbProgressStatus.Visible = true;
                pbProgressStatus.Style = ProgressBarStyle.Blocks;
                pbProgressStatus.MarqueeAnimationSpeed = 0;
                pbProgressStatus.Value = 0;
                if (progressPercentLabel != null)
                {
                    progressPercentLabel.Text = "0%";
                }
            }
        }

        private void UiBusy(bool isBusy)
        {
            UseWaitCursor = isBusy;
            if (isBusy)
            {
                lbUiStatus.Text = TXT.Words.Busy;
                lbUiStatus.ForeColor = DarkTheme.Warning;
            }
            else
            {
                lbUiStatus.Text = TXT.Words.Ready;
                lbUiStatus.ForeColor = DarkTheme.Green;
                if (string.IsNullOrWhiteSpace(lbStatus.Text) || lbStatus.Text == TXT.Translate("No results yet."))
                {
                    lbStatus.Text = TXT.Translate("0 items    |    Nothing to delete yet.");
                }
            }
            UpdateActionHint();
        }

        private bool UiIsBusy()
        {
            return UseWaitCursor;
        }

        private void UiClipboardCheck()
        {
            if (!UiIsBusy())
            {
                // Detect paths in the clipboard
                if (cbClipboardDetection.Checked && Clipboard.ContainsText(TextDataFormat.Text))
                {
                    string clipValue = Clipboard.GetText(TextDataFormat.Text);
                    // Remove any leading or trailing quotes
                    clipValue = clipValue.Trim('"');
                    if (!clipValue.Contains("\n"))
                    {
                        if (clipValue.Contains(":" + Path.DirectorySeparatorChar.ToString()))
                        {
                            // add ending backslash
                            if (!clipValue.EndsWith(Path.DirectorySeparatorChar.ToString()))
                            {
                                clipValue += Path.DirectorySeparatorChar.ToString();
                            }
                            if (Directory.Exists(clipValue))
                            {
                                string qmsg = string.Format("{0}{1}{2}", TXT.Translate("Use this value as the start directory?"), RedGetText.CrLf2, clipValue);
                                if (UiAssist.BAskYesNo(qmsg, MessageBoxDefaultButton.Button1))
                                {
                                    RedConfig.Runtime.Volatile.LastUsedDirectory = clipValue;
                                    txtSearchDirectory.Text = clipValue;
                                }
                            }
                        }
                    }
                }
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            Core.CancelCurrentProcess();
        }

        private void SetStatusAndLogMessage(string msg)
        {
            lbStatus.Text = msg;
            RunData.AddLogMessage(msg);
            UpdateActionHint();
        }

        /// <summary>
        /// Part of the drag & drop functions
        /// (you can drag a folder into RED)
        /// </summary>
        private void MainWindow_DragDrop(object sender, DragEventArgs e)
        {
            if (UiIsBusy())
            {
                return;
            }

            string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop, false);
            var validPaths = new List<string>();
            foreach (string p in paths)
            {
                string trimmed = p.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && Directory.Exists(trimmed))
                    validPaths.Add(trimmed);
            }

            if (validPaths.Count == 0) return;

            txtSearchDirectory.Text = validPaths[0];
            pendingScanPaths.Clear();
            for (int i = 1; i < validPaths.Count; i++)
                pendingScanPaths.Enqueue(validPaths[i]);

            // Dropping folders is an explicit action — start scanning right away
            if (btnSearch.Enabled)
            {
                btnSearch.PerformClick();
            }
            UpdateTreeEmptyState();
        }

        /// <summary>
        /// Part of the drag & drop functions
        /// (you can drag a folder into RED)
        /// </summary>
        private void MainWindow_DragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.None;
            }
            else
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void txtSearchDirectory_Enter(object sender, EventArgs e)
        {
            UiClipboardCheck();
        }

        private void txtSearchDirectory_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            txtSearchDirectory.SelectAll();
        }

        private void btnExit_Click(object sender, EventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Let the user select a folder
        /// </summary>
        private void btnSearchDirectoryBrowseFor_Click(object sender, EventArgs e)
        {
            // Start browsing from whatever is currently in the path box
            string startFrom = !string.IsNullOrWhiteSpace(txtSearchDirectory.Text)
                ? txtSearchDirectory.Text
                : RedConfig.Runtime.Volatile.LastUsedDirectory;
            txtSearchDirectory.Text = SystemFunctions.ChooseDirectoryDialog(startFrom);
        }

        private void mnuShowLog_Click(object sender, EventArgs e)
        {
            LogWindow logWindow = new LogWindow();
            logWindow.SetLog(Core.GetLogMessages());
            logWindow.ShowDialog(this);
            logWindow.Dispose();
        }

        private void MainWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (Core == null || Core.CurrentProcessStep == WorkflowSteps.Idle)
            {
                bool ask = cbSavePrompt.Checked || ModifierKeys.HasFlag(Keys.Alt);
                ConfigUpdateAndSave(ask);
                RunData.Dispose();
            }
            else
            {
                e.Cancel = true;
                UiAssist.MsgBoxWarning(this, TXT.Translate("A scan or deletion is still running. Cancel it before closing RED++."));
            }
        }

        private void txtSearchDirectory_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && btnSearch.Enabled && !UiIsBusy())
            {
                e.SuppressKeyPress = true;
                btnSearch.PerformClick();
            }
        }

        private void tvSearchResults_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && tvSearchResults.SelectedNode != null && !UiIsBusy())
            {
                e.SuppressKeyPress = true;
                TreeMgr.DeleteSelectedDirectory();
            }
        }

        private void MainWindow_Shown(object sender, EventArgs e)
        {
            UiBusy(false);
            AdjustSearchLayout();
            RefreshThemeDependentUi();
            BeginInvoke((Action)(() =>
            {
                AdjustSearchLayout();
                RefreshThemeDependentUi();
            }));
            if (AutoSearchOnStart && txtSearchDirectory.Text.Length > 0)
            {
                AutoSearchOnStart = false;
                btnSearch.PerformClick();
            }
            else
            {
                UiClipboardCheck();
            }
            UpdateTreeEmptyState();
        }

        private void lbUiStatus_DoubleClick(object sender, EventArgs e)
        {
            //ChangeOfLanguage();
        }

        private void mnuItemLanguage_Click(object sender, EventArgs e)
        {
            ChangeOfLanguage();
        }

        // NotBob - Draw a border around ToolStrip buttons
        private void UiToolstripButton_Paint(object sender, PaintEventArgs e)
        {
            ToolStripItem item = (ToolStripItem)sender;
            ControlPaint.DrawBorder(e.Graphics, new Rectangle(0, 0, item.Width, item.Height), DarkTheme.Surface1, ButtonBorderStyle.Solid);
        }

        private void cbFastSearchMode_CheckedChanged(object sender, EventArgs e)
        {
            lbFastModeInfo.Visible = cbFastSearchMode.Checked; ;
        }

        private void tcMain_SelectedIndexChanged(object sender, EventArgs e)
        {
            //pnlActionsSearch.Enabled = (tcMain.SelectedTab == tabSearch);

            if (tcMain.SelectedTab == tabSettings)
            {
                if (RedConfig != null && RedConfig.IsReadOnly)
                {
                    btnResetConfig.Enabled = false;
                    lbStatus.Text = TXT.Translate("Settings are read-only and cannot be changed");
                }
            }
            else
            {
                UpdateActionHint();
            }
        }

        #endregion GUI related functions / events

        #region Config and misc stuff

        private void btnResetFilters_Click(object sender, EventArgs e)
        {
            if (DialogResult.Yes == UiAssist.MsgBoxYesNo(this, TXT.Translate("Reset ALL FILTERS to their default values?")))
            {
                ConfigFromUI();
                RedConfig.Filters.SetToDefaults();
                ConfigToUI();
            }
        }

        private void btnResetConfig_Click(object sender, EventArgs e)
        {
            if (DialogResult.Yes == UiAssist.MsgBoxYesNo(this, TXT.Translate("Reset SETTINGS (excluding filters) to their default values?")))
            {
                ConfigFromUI();
                RedConfig.Options.SetToDefaults();
                ConfigToUI();
                TreeMgr.SetFastMode(RedConfig.Options.FastSearchMode);
            }
        }

        private void btnExplorerIntegrate_Click(object sender, EventArgs e)
        {
            SystemFunctions.ExplorerIntegrationAdd(chkExplorerIntegrateAutoSearch.Checked);
            ExplorerIntegrationCheck();
        }

        private void btnExplorerRemove_Click(object sender, EventArgs e)
        {
            SystemFunctions.ExplorerIntegrationRemove();
            ExplorerIntegrationCheck();
        }

        private void linkLabelProjectHomepage_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://github.com/SysAdminDoc/REDplusplus/");
        }

        private void linkLabelJonasJohnRed_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://github.com/hxseven/Remove-Empty-Directories/");
        }

        private void linkLabelCheckForUpdates_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://github.com/SysAdminDoc/REDplusplus/releases/");
        }

        private void linkLabelFeedback_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://github.com/SysAdminDoc/REDplusplus/issues/");
        }

        private void btnHelp_Click(object sender, EventArgs e)
        {
            if (File.Exists(RedConfig.Runtime.HelpFile))
            {
                Process.Start(RedConfig.Runtime.HelpFile);
            }
            else
            {
                UiAssist.MsgBoxInfo($"Help File Not Found\r\n{RedConfig.Runtime.HelpFile}");
            }
        }

        private void btnCopyDebugInfo_Click(object sender, EventArgs e)
        {
            try
            {
                RedDebug dbug = new RedDebug();
                string info = dbug.GatherDebugInfo(RedConfig);
                Clipboard.SetText(info, TextDataFormat.Text);
                UiAssist.MsgBoxInfo(this, TXT.Translate("Copied Debug Information to clipboard"));
            }
            catch (Exception ex)
            {
                UiAssist.MsgBoxException(this, TXT.Translate("Could not copy the debug information to clipboard"), ex);
            }
        }

        private void cmTreeview_Opening(object sender, CancelEventArgs e)
        {
            bool hasSelection = tvSearchResults.SelectedNode != null;
            bool isDeletionCandidate = hasSelection && tvSearchResults.SelectedNode.ForeColor == TreeManager.ColortoBeDeleted;
            bool hasResults = tvSearchResults.Nodes.Count > 0;
            tsmiOpenFolder.Enabled = hasSelection;
            tsmiSearchOnlyThisDirectory.Enabled = hasSelection && !UiIsBusy();
            tsmiExpandAll.Enabled = hasResults;
            tsmiCollapseAll.Enabled = hasResults;
            tsmiProtectDirectoryOnce.Enabled = hasSelection && !UiIsBusy();
            tsmiUnprotectDirectory.Enabled = hasSelection && !UiIsBusy();
            tsmiAddToFilterDirectoryIgnore.Enabled = hasSelection && !UiIsBusy();
            tsmiDeleteDirectory.Enabled = isDeletionCandidate && !UiIsBusy();
        }

        private void UpdateRuntimeDataObject()
        {
            ConfigFromUI();

            RunData.HideDeletionErrors = RedConfig.Options.HideDeletionErrors;
            RunData.HideScanErrors = RedConfig.Options.HideScanErrors;

            RunData.IgnoreEmptyFiles = RedConfig.Options.IgnoreEmptyFiles;
            RunData.IgnoreHiddenFolders = RedConfig.Options.IgnoreHiddenDirectories;
            RunData.IgnoreSystemFolders = RedConfig.Options.IgnoreSystemDirectories;
            RunData.MinFolderAgeHours = RedConfig.Options.MinDirectoryAgeHours;
            RunData.MaxDepth = RedConfig.Options.MaxDirectoryDepth;
            RunData.InfiniteLoopDetectionCount = RedConfig.Options.InfiniteLoopDetectionCount;
            RunData.DeleteMode = (DeleteModes)RedConfig.Options.DeleteMode;
            RunData.PauseTime = RedConfig.Options.PauseBetweenDeletions;
            RunData.HideIgnoredDirectories = RedConfig.Options.HideIgnoredDirectories;
            RunData.RespectGitIgnore = RedConfig.Options.RespectGitIgnore;
            RunData.UseMftScan = RedConfig.Options.UseMftScan;
            RunData.DeleteEmptyFiles = RedConfig.Options.DeleteEmptyFiles;
            // NotBob use dedicated RedMatchItemLists for all the filters
            RunData.IgnoreFileNameList.Transform(RedConfig.Filters.FilesToIgnore);
            RunData.IgnoreDirectoryNameList.Transform(RedConfig.Filters.DirectoriesToIgnore);
            RunData.NeverEmptyDirectoryList.Transform(RedConfig.Filters.DirectoriesNeverEmpty);
        }

        private void NotBobConfigInfo_DoubleClick(object sender, EventArgs e)
        {
            SystemFunctions.OpenDirectoryWithExplorer(RedConfig.Runtime.ConfigPath);
        }

        /// <summary>
        /// Enables/disables all items in the context menu
        /// </summary>
        /// <param name="contextMenuStrip"></param>
        /// <param name="enable"></param>
        private void UpdateContextMenu(ContextMenuStrip contextMenuStrip, bool enable)
        {
            foreach (ToolStripItem item in contextMenuStrip.Items)
            {
                item.Enabled = enable;
            }
            uxMenuButtonExtras.Enabled = enable;
        }

        #endregion Config and misc stuff

        #region NotBob Config

        private void ConfigLoad()
        {
            ConfigAssist.ConfigLoad(ref RedConfig, "RemoveEmptyDirectories");
            ConfigLanguage(RedConfig.Options.Language);
        }

        private void ConfigUpdateAndSave(bool ask = false)
        {
            ConfigFromUI();
            ConfigAssist.ConfigSaveWithPrompt(RedConfig, ask);
        }

        private void ConfigLanguage(string language)
        {
            try
            {
                string langFolder = RedGetText.GetLanguageFolder(RedConfig.Runtime.ExecutablePath);
                if (Directory.Exists(langFolder))
                {
                    if (RedGetText.LoadLanguage(language, RedConfig.Runtime.ExecutablePath))
                    {
                        RedConfig.Options.Language = language;
                        RedGetText.UI.TranslateControls(this);
                    }
                    else
                    {
                        RedConfig.Options.Language = null;
                    }
                }
                else
                {
                    mnuItemLanguage.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                UiAssist.MsgBoxException(this, TXT.Translate("Error during config initialisation (Runtime)"), ex);
            }
        }

        // Update the UI with current configuration settings
        private void ConfigToUI()
        {
            cbSavePrompt.Checked = RedConfig.Options.SavePrompt;

            cbFastSearchMode.Checked = RedConfig.Options.FastSearchMode;
            lbFastModeInfo.Visible = RedConfig.Options.FastSearchMode;
            TreeMgr.SetFastMode(RedConfig.Options.FastSearchMode);

            cbHideScanErrors.Checked = RedConfig.Options.HideScanErrors;
            cbHideDeletionErrors.Checked = RedConfig.Options.HideDeletionErrors;

            cbAutoProtectRoot.Checked = RedConfig.Options.AutoProtectRoot;
            cbClipboardDetection.Checked = RedConfig.Options.ClipboardPathDetection;
            cbHideIgnoredFolders.Checked = RedConfig.Options.HideIgnoredDirectories;
            cbIgnore0kbFiles.Checked = RedConfig.Options.IgnoreEmptyFiles;
            cbIgnoreHiddenFolders.Checked = RedConfig.Options.IgnoreHiddenDirectories;
            cbIgnoreSystemFolders.Checked = RedConfig.Options.IgnoreSystemDirectories;
            cbRespectGitIgnore.Checked = RedConfig.Options.RespectGitIgnore;
            cbUseMftScan.Checked = RedConfig.Options.UseMftScan;
            bool mftAvailable = SystemFunctions.IsAdmin();
            cbUseMftScan.Enabled = mftAvailable;
            lbUseMftScan.Enabled = mftAvailable;
            string mftRequirement = TXT.Translate("Requires administrator rights; falls back to standard scan when unavailable");
            uxToolTips.SetToolTip(cbUseMftScan, mftRequirement);
            uxToolTips.SetToolTip(lbUseMftScan, mftRequirement);

            // Clamp values from the config file — a hand-edited or corrupt entry
            // must degrade to a sane default instead of crashing at startup
            int deleteModeIndex = (int)RedConfig.Options.DeleteMode;
            cbDeleteMode.SelectedIndex = (deleteModeIndex >= 0 && deleteModeIndex < cbDeleteMode.Items.Count)
                ? deleteModeIndex
                : (int)DeleteModes.RecycleBin;

            nuFolderAge.Value = ClampToRange(nuFolderAge, RedConfig.Options.MinDirectoryAgeHours);
            nuInfiniteLoopDetectionCount.Value = ClampToRange(nuInfiniteLoopDetectionCount, RedConfig.Options.InfiniteLoopDetectionCount);
            nuMaxDepth.Value = ClampToRange(nuMaxDepth, RedConfig.Options.MaxDirectoryDepth);
            nuPause.Value = ClampToRange(nuPause, RedConfig.Options.PauseBetweenDeletions);

            flIgnoreFolders.Populate(RedConfig.Filters.DirectoriesToIgnore, RedMatchFilterType.Directory);
            flNeverEmptyFolders.Populate(RedConfig.Filters.DirectoriesNeverEmpty, RedMatchFilterType.Directory);
            flIgnoreFiles.Populate(RedConfig.Filters.FilesToIgnore, RedMatchFilterType.Files);

            txtSearchDirectory.Text = RedConfig.Volatile.LastUsedDirectory;

            cbRememberWindowDetails.Checked = RedConfig.Options.RememberWindowDetails;
            cbRememberLastUsedDirectory.Checked = RedConfig.Options.RememberLastUsedDirectory;
            cbRememberDeletionStats.Checked = RedConfig.Options.RememberDeletionStats;

            if (RedConfig != null && RedConfig.IsReadOnly)
            {
                foreach (GroupBox item in tabSettings1.Controls)
                {
                    item.Enabled = false;
                }
                foreach (GroupBox item in tabSettings2.Controls)
                {
                    item.Enabled = false;
                }
                gbAdvancedExtras.Enabled = true;
                btnResetConfig.Enabled = false;
            }
            btnHelp.Enabled = File.Exists(RedConfig.Runtime.HelpFile);
        }

        private static decimal ClampToRange(NumericUpDown nud, decimal value)
        {
            if (value < nud.Minimum) return nud.Minimum;
            if (value > nud.Maximum) return nud.Maximum;
            return value;
        }

        private void ConfigRestoreWindowDetails()
        {
            // Ensure that we have valid values before trying to restore them
            if (RedConfig.UI.WinMainLocation.IsEmpty)
            {
                RedConfig.UI.WinMainLocation = Location;
            }
            if (RedConfig.UI.WinMainSize.IsEmpty)
            {
                RedConfig.UI.WinMainSize = Size;
            }
            Size restoredSize = RedConfig.UI.WinMainSize;
            restoredSize.Width = Math.Max(MinimumSize.Width, restoredSize.Width);
            restoredSize.Height = Math.Max(MinimumSize.Height, restoredSize.Height);

            Rectangle workingArea = Screen.GetWorkingArea(RedConfig.UI.WinMainLocation);
            restoredSize.Width = Math.Min(restoredSize.Width, workingArea.Width);
            restoredSize.Height = Math.Min(restoredSize.Height, workingArea.Height);

            Point restoredLocation = RedAssist.GetScreenValidLocation(RedConfig.UI.WinMainLocation);
            if (restoredLocation.X + restoredSize.Width > workingArea.Right)
            {
                restoredLocation.X = Math.Max(workingArea.Left, workingArea.Right - restoredSize.Width);
            }
            if (restoredLocation.Y + restoredSize.Height > workingArea.Bottom)
            {
                restoredLocation.Y = Math.Max(workingArea.Top, workingArea.Bottom - restoredSize.Height);
            }

            RedConfig.UI.WinMainLocation = restoredLocation;
            RedConfig.UI.WinMainSize = restoredSize;

            Location = restoredLocation;
            Size = restoredSize;

            tcMain.SelectedTab = tabSearch;
        }

        private void ConfigFromUI()
        {
            try
            {
                RedConfig.Options.SavePrompt = cbSavePrompt.Checked;

                RedConfig.Options.FastSearchMode = cbFastSearchMode.Checked;

                RedConfig.Options.AutoProtectRoot = cbAutoProtectRoot.Checked;
                RedConfig.Options.ClipboardPathDetection = cbClipboardDetection.Checked;
                RedConfig.Options.HideIgnoredDirectories = cbHideIgnoredFolders.Checked;
                RedConfig.Options.HideScanErrors = cbHideScanErrors.Checked;
                RedConfig.Options.IgnoreEmptyFiles = cbIgnore0kbFiles.Checked;
                RedConfig.Options.HideDeletionErrors = cbHideDeletionErrors.Checked;
                RedConfig.Options.IgnoreHiddenDirectories = cbIgnoreHiddenFolders.Checked;
                RedConfig.Options.IgnoreSystemDirectories = cbIgnoreSystemFolders.Checked;
                RedConfig.Options.RespectGitIgnore = cbRespectGitIgnore.Checked;
                if (cbUseMftScan.Enabled)
                {
                    RedConfig.Options.UseMftScan = cbUseMftScan.Checked;
                }
                RedConfig.Options.DeleteModeInt = cbDeleteMode.SelectedIndex;

                RedConfig.Options.MinDirectoryAgeHours = (uint)nuFolderAge.Value;
                RedConfig.Options.InfiniteLoopDetectionCount = (int)nuInfiniteLoopDetectionCount.Value;
                RedConfig.Options.MaxDirectoryDepth = (int)nuMaxDepth.Value;
                RedConfig.Options.PauseBetweenDeletions = (int)nuPause.Value;

                RedConfig.Runtime.Volatile.LastUsedDirectory = txtSearchDirectory.Text;

                if (RedAssist.FilterListUpdate(flIgnoreFolders.GetStringList(), RedConfig.Filters.DirectoriesToIgnore))
                {
                    RedConfig.DataIsDirty = true;
                }
                if (RedAssist.FilterListUpdate(flNeverEmptyFolders.GetStringList(), RedConfig.Filters.DirectoriesNeverEmpty))
                {
                    RedConfig.DataIsDirty = true;
                }
                if (RedAssist.FilterListUpdate(flIgnoreFiles.GetStringList(), RedConfig.Filters.FilesToIgnore))
                {
                    RedConfig.DataIsDirty = true;
                }

                RedConfig.Options.RememberDeletionStats = cbRememberDeletionStats.Checked;
                if (RedConfig.Options.RememberDeletionStats)
                {
                    // Fold the session counter in exactly once — ConfigFromUI runs on
                    // every scan, and re-adding would inflate the statistics
                    unchecked { RedConfig.Volatile.CountOfDeletions += RedConfig.Runtime.Volatile.CountOfDeletions; }
                    RedConfig.Runtime.Volatile.CountOfDeletions = 0;
                }

                // Save UI details if required
                RedConfig.Options.RememberWindowDetails = cbRememberWindowDetails.Checked;
                if (RedConfig.Options.RememberWindowDetails)
                {
                    RedConfig.UI.WinMainLocation = Location;
                    RedConfig.UI.WinMainSize = Size;
                }

                RedConfig.Options.RememberLastUsedDirectory = cbRememberLastUsedDirectory.Checked;
                if (RedConfig.Options.RememberLastUsedDirectory)
                {
                    RedConfig.Volatile.LastUsedDirectory = RedConfig.Runtime.Volatile.LastUsedDirectory;
                }
            }
            catch (Exception ex)
            {
                UiAssist.MsgBoxException(this, TXT.Translate("Error trying to set configuration details"), ex);
            }
        }

        #endregion NotBob Config

        private void ChangeOfLanguage()
        {
            using (FormLanguage frm = new FormLanguage(RedConfig))
            {
                if (DialogResult.OK == frm.ShowDialog(this))
                {
                    ConfigLanguage(frm.Language);
                    SetAccessibleNames();
                    ApplyPremiumPolish();
                    RefreshThemeDependentUi();
                    UiAssist.MsgBoxInfo(this, TXT.Words.RestartRequired);
                }
            }
        }
    }
}
