using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using RED.Helper;
using RED.Match;
using TXT = RED.RedGetText;

namespace RED
{
	/// <summary>
	/// Icon names (Warning: Entries are case sensitive)
	/// </summary>
	public enum DirectoryIcons
	{
		home,
		deleted,
		protected_icon,
		folder_warning
	}

	/// <summary>
	/// Handles tree related things
	///
	/// TODO: Handle null references within tree nodes
	/// </summary>
	public class TreeManager
	{
		public static Color ColorDoNotTouch { get { return RED.UI.DarkTheme.Kept; } }
		public static Color ColorProtected { get { return RED.UI.DarkTheme.Protected; } }
		public static Color ColortoBeDeleted { get { return RED.UI.DarkTheme.Eligible; } }

		/// <summary>
		/// True when a node's status icon marks it as an eligible deletion candidate.
		/// This is the single source of truth for "to be deleted" — derived from the
		/// stable image key, never from the theme-dependent ForeColor, so a restyle
		/// cannot change which nodes are eligible (or exported).
		/// </summary>
		internal static bool IsEligibleImageKey(string imageKey)
		{
			string key = imageKey ?? string.Empty;
			return key.StartsWith("folder", StringComparison.OrdinalIgnoreCase)
				&& key != "folder_warning"
				&& key != "folder_never_empty";
		}

		private TreeView treeView = null;
		private TreeNode rootNode = null;
		private string rootPath = string.Empty;

		private Label fastModeInfoLabel = null;

		private Dictionary<String, TreeNode> directoryToTreeNodeMapping = null;

		/// <summary>
		/// This dictionary holds the original properties of protected
		/// nodes so that they can be restored if the user undoes the action
		/// </summary>
		private Dictionary<string, object> nodePropsBackup = new Dictionary<string, object>();

		public event EventHandler<ProtectionStatusChangedEventArgs> OnProtectionStatusChanged;

		public event EventHandler<DeleteRequestFromTreeEventArgs> OnDeleteRequest;

		private bool fastMode { get; set; } = true;

		public TreeManager(TreeView dirTree, Label fastModeInfoLabel)
		{
			this.treeView = dirTree;
			this.treeView.MouseClick += new MouseEventHandler(this.tvFolders_MouseClick);

			this.fastModeInfoLabel = fastModeInfoLabel;

			this.resetTree();

			this.rootPath = string.Empty;
		}

		#region Incoming "events"

		public void SetFastMode(bool fastModeActive)
		{
			this.fastMode = fastModeActive;
			fastModeInfoLabel.Visible = fastModeActive;

			if (this.fastMode)
			{
				this.treeView.SuspendLayout();
			}
			else
			{
				this.clearFastMode();
				this.treeView.ResumeLayout();
			}
		}

		public void OnSearchStart(DirectoryInfo directory, bool multiRoot = false)
		{
			if (!multiRoot || this.rootNode == null)
			{
				this.resetTree();
			}

			if (this.fastMode)
			{
				suspendTreeViewForFastMode();
			}

			this.createRootNode(directory, DirectoryIcons.home, multiRoot);
		}

		public void OnSearchFinished()
		{
			this.showFastModeResults();
		}

		public void OnDeletionProcessStart()
		{
			if (this.fastMode)
			{
				this.treeView.Nodes.Clear();
				suspendTreeViewForFastMode();
			}
		}

		public void OnDeletionProcessFinished()
		{
			this.showFastModeResults();
		}

		public void RefreshTheme()
		{
			this.treeView.BackColor = RED.UI.DarkTheme.Mantle;
			this.treeView.ForeColor = RED.UI.DarkTheme.Text;
			this.treeView.LineColor = RED.UI.DarkTheme.Surface2;
			foreach (TreeNode node in this.treeView.Nodes)
			{
				refreshNodeTheme(node);
			}
		}

		public void OnProcessCancelled()
		{
			this.showFastModeResults();
		}

		internal void LoadImportedResults(IList<RedImportedScanRoot> roots)
		{
			this.resetTree();
			this.treeView.SuspendLayout();
			try
			{
				for (int i = 0; i < roots.Count; i++)
				{
					RedImportedScanRoot importRoot = roots[i];
					this.createRootNode(importRoot.RootDirectory, DirectoryIcons.home, i > 0);
					this.addRootNode();
					foreach (RedScanResultItem item in importRoot.Results)
					{
						this.AddOrUpdateDirectoryNode(item.Directory, item.SearchStatus, item.ErrorMessage);
					}
				}
				if (this.treeView.Nodes.Count > 0)
				{
					this.treeView.Nodes[0].EnsureVisible();
					this.treeView.ExpandAll();
				}
			}
			finally
			{
				this.treeView.ResumeLayout();
				this.clearFastMode();
			}
		}

		#endregion Incoming "events"

		private void suspendTreeViewForFastMode()
		{
			this.treeView.SuspendLayout();

			this.treeView.BackColor = RED.UI.DarkTheme.Surface0;
			this.fastModeInfoLabel.Visible = true;
		}

		private void clearFastMode()
		{
			this.treeView.BackColor = RED.UI.DarkTheme.Mantle;
			this.fastModeInfoLabel.Visible = false;
		}

		private void refreshNodeTheme(TreeNode node)
		{
			if (node == null)
			{
				return;
			}

			string key = node.ImageKey ?? string.Empty;
			if (key == "protected_icon" || key == "home_protected")
			{
				node.ForeColor = ColorProtected;
			}
			else if (IsEligibleImageKey(key))
			{
				node.ForeColor = ColortoBeDeleted;
			}
			else if (key == "folder_warning")
			{
				node.ForeColor = RED.UI.DarkTheme.Warning;
			}
			else
			{
				node.ForeColor = ColorDoNotTouch;
			}

			foreach (TreeNode child in node.Nodes)
			{
				refreshNodeTheme(child);
			}
		}

		private void showFastModeResults()
		{
			if (!this.fastMode)
			{
				return;
			}

			this.treeView.ResumeLayout();
			this.clearFastMode();

			this.addRootNode();

			// Scroll to root node and expand all dirs
			this.rootNode.EnsureVisible();
			this.treeView.ExpandAll();
		}

		/// <summary>
		/// Hack to selected the correct node
		/// </summary>
		private void tvFolders_MouseClick(object sender, MouseEventArgs e)
		{
			this.treeView.SelectedNode = this.treeView.GetNodeAt(e.X, e.Y);
		}

		private void resetTree()
		{
			this.rootNode = null;
			this.directoryToTreeNodeMapping = new Dictionary<string, TreeNode>();
			this.nodePropsBackup = new Dictionary<string, object>();

			this.treeView.Nodes.Clear();
		}

		private void createRootNode(DirectoryInfo directory, DirectoryIcons imageKey, bool append = false)
		{
			this.rootPath = directory.FullName.Trim(Path.DirectorySeparatorChar);

			string displayName = string.IsNullOrWhiteSpace(directory.Name) ? directory.FullName : directory.Name;
			rootNode = new TreeNode(RedAssist.SanitizeDisplay(displayName));
			rootNode.Tag = directory;
			rootNode.ImageKey = imageKey.ToString();
			rootNode.SelectedImageKey = imageKey.ToString();

			if (!append)
			{
				directoryToTreeNodeMapping = new Dictionary<String, TreeNode>();
			}
			directoryToTreeNodeMapping[directory.FullName] = rootNode;

			if (!this.fastMode)
			{
				addRootNode();
			}
		}

		private void addRootNode()
		{
			if (rootNode == null || treeView.Nodes.Contains(rootNode))
			{
				return;
			}

			this.treeView.Nodes.Add(rootNode);
		}

		private void scrollToNode(TreeNode node)
		{
			// Ignore when fast mode is enabled
			if (!this.fastMode)
			{
				node.EnsureVisible();
			}
		}

		/// <summary>
		/// Marks a folder with the warning or deleted icon
		/// </summary>
		/// <param name="path">Dir path</param>
		/// <param name="iconKey">Icon</param>
		internal void UpdateItemIcon(RedScanResultItem scanResult, DirectoryIcons iconKey)
		{
			TreeNode treeNode = this.findOrCreateDirectoryNodeByPath(scanResult.Directory);
			if (treeNode == null)
			{
				return;
			}

			treeNode.ImageKey = iconKey.ToString();
			treeNode.SelectedImageKey = iconKey.ToString();

			this.scrollToNode(treeNode);
		}

		// TODO: Find better code structure for the following two routines
		private TreeNode findOrCreateDirectoryNodeByPath(DirectoryInfo directory)
		{
			if (directory == null || string.IsNullOrWhiteSpace(directory.FullName))
			{
				return null;
			}

			if (directoryToTreeNodeMapping.ContainsKey(directory.FullName))
			{
				return directoryToTreeNodeMapping[directory.FullName];
			}
			else
			{
				return AddOrUpdateDirectoryNode(directory, DirectorySearchStatusTypes.NotEmpty, string.Empty);
			}
		}

		public TreeNode AddOrUpdateDirectoryNode(DirectoryInfo directory, DirectorySearchStatusTypes statusType, string optionalErrorMsg)
		{
			if (directoryToTreeNodeMapping.ContainsKey(directory.FullName))
			{
				// Just update the style if the node already exists
				TreeNode node = directoryToTreeNodeMapping[directory.FullName];
				applyNodeStyle(node, directory, statusType, optionalErrorMsg);
				return node;
			}

			//var directory = new DirectoryInfo(path);

			// Create new tree node
			TreeNode newTreeNode = new TreeNode(RedAssist.SanitizeDisplay(directory.Name));
			applyNodeStyle(newTreeNode, directory, statusType, optionalErrorMsg);
			newTreeNode.Tag = directory;

			if (directory.Parent == null ||
				directory.Parent.FullName.Trim(Path.AltDirectorySeparatorChar).Equals(this.rootPath, StringComparison.OrdinalIgnoreCase))
			{
				this.rootNode.Nodes.Add(newTreeNode);
			}
			else
			{
				TreeNode parentNode = this.findOrCreateDirectoryNodeByPath(directory.Parent);
				if (parentNode != null)
				{
					parentNode.Nodes.Add(newTreeNode);
				}
				else
				{
					this.rootNode.Nodes.Add(newTreeNode);
				}
			}

			directoryToTreeNodeMapping.Add(directory.FullName, newTreeNode);

			this.scrollToNode(newTreeNode);

			return newTreeNode;
		}

		private void applyNodeStyle(TreeNode treeNode, RedScanResultItem scanResult)
		{
			applyNodeStyle(treeNode, scanResult.Directory, scanResult.SearchStatus, scanResult.ErrorMessage);
		}

		private static string CleanStatusLabel(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return string.Empty;
			}

			return value
				.Replace(((char)0x00AB).ToString(), string.Empty)
				.Replace(((char)0x00BB).ToString(), string.Empty)
				.Replace(((char)0x00C2).ToString(), string.Empty)
				.Trim();
		}

		//private void applyNodeStyle(TreeNode treeNode, string path, DirectorySearchStatusTypes statusType, string optionalErrorMsg)
		private void applyNodeStyle(TreeNode treeNode, DirectoryInfo directory, DirectorySearchStatusTypes statusType, string optionalErrorMsg)
		{
			// TODO: use enums for icon names
			treeNode.ForeColor = (statusType == DirectorySearchStatusTypes.Empty) ? ColortoBeDeleted : ColorDoNotTouch;
			string iconKey = string.Empty;
			string accessibleReason = string.Empty;

			// Rebuild from the directory name: a node can be restyled more than once
			// (e.g. rescans) and appending again would stack «Empty»«Empty» suffixes
			string baseText = RedAssist.SanitizeDisplay(directory.Name);

			switch (statusType)
			{
				case DirectorySearchStatusTypes.Empty:
					int fileCount = 0;
					try
					{
						// Can throw if the directory vanished or access was revoked
						// between the scan thread finding it and the UI styling it
						fileCount = FastDirectoryEnumerator.GetFiles(directory).Length;
					}
					catch
					{
						fileCount = 0;
					}
					bool containsTrash = (fileCount > 0);

					FileAttributes attribs = 0;
					try { attribs = directory.Attributes; } catch { }

					iconKey = containsTrash ? "folder_trash_files" : "folder";
					if ((attribs & FileAttributes.Hidden) == FileAttributes.Hidden)
					{
						iconKey = containsTrash ? "folder_hidden_trash_files" : "folder_hidden";
					}
					if ((attribs & FileAttributes.Encrypted) == FileAttributes.Encrypted)
					{
						iconKey = containsTrash ? "folder_lock_trash_files" : "folder_lock";
					}
					if ((attribs & FileAttributes.System) == FileAttributes.System)
					{
						iconKey = containsTrash ? "folder_lock_trash_files" : "folder_lock";
					}
					if (containsTrash)
					{
						treeNode.ToolTipText = TXT.Translate("«ignored files: {0}»", fileCount);
						accessibleReason = TXT.Translate("Empty except for {0} ignored files - eligible for deletion", fileCount);
					}
					else
					{
						treeNode.ToolTipText = TXT.Translate("«Empty»");
						accessibleReason = TXT.Translate("Empty - eligible for deletion");
					}
					treeNode.Text = baseText + "  [" + CleanStatusLabel(treeNode.ToolTipText) + "]";
					break;

				case DirectorySearchStatusTypes.Ignore:
					iconKey = "protected_icon";
					treeNode.ForeColor = ColorProtected;
					treeNode.ToolTipText = TXT.Translate("«Ignored»");
					accessibleReason = TXT.Translate("Kept - matches an ignore filter rule");
					treeNode.Text = baseText + "  [" + CleanStatusLabel(treeNode.ToolTipText) + "]";
					break;

				case DirectorySearchStatusTypes.NeverEmpty:
					iconKey = "folder_never_empty";
					treeNode.ForeColor = ColorDoNotTouch;
					treeNode.ToolTipText = TXT.Translate("«Never Empty»");
					accessibleReason = TXT.Translate("Kept - matches a never-empty rule");
					treeNode.Text = baseText + "  [" + CleanStatusLabel(treeNode.ToolTipText) + "]";
					break;

				case DirectorySearchStatusTypes.Error:
					iconKey = "folder_warning";
					accessibleReason = string.IsNullOrWhiteSpace(optionalErrorMsg)
						? TXT.Translate("Kept - could not be read")
						: TXT.Translate("Kept - {0}", optionalErrorMsg.Replace("\r", " ").Replace("\n", " "));
					if (!string.IsNullOrWhiteSpace(optionalErrorMsg))
					{
						optionalErrorMsg = optionalErrorMsg.Replace("\r", string.Empty).Replace("\n", string.Empty);
						if (optionalErrorMsg.Length > 55)
						{
							optionalErrorMsg = optionalErrorMsg.Substring(0, 55) + "...";
						}
						treeNode.Text = baseText + " (" + optionalErrorMsg + ")";
					}
					break;

				default:
					break;
			}

			// Screen readers read TreeNode.Text; spell out the reason there (the
			// «glyph» markers alone are terse) so Narrator/NVDA announce why a
			// folder is kept, not just its name. TreeNode has no AccessibleName.
			if (!string.IsNullOrEmpty(accessibleReason) && statusType != DirectorySearchStatusTypes.Error)
			{
				treeNode.ToolTipText = accessibleReason;
			}

			if (treeNode != this.rootNode)
			{
				treeNode.ImageKey = iconKey;
				treeNode.SelectedImageKey = iconKey;
			}
		}

		/// <summary>
		/// Returns the selected folder path
		/// </summary>
		public string GetSelectedFolderPath()
		{
			if (this.treeView.SelectedNode != null && this.treeView.SelectedNode.Tag != null && this.treeView.SelectedNode.Tag is DirectoryInfo)
			{
				return ((DirectoryInfo)this.treeView.SelectedNode.Tag).FullName;
			}
			return string.Empty;
		}

		internal void DeleteSelectedDirectory()
		{
			if (this.treeView.SelectedNode != null && this.treeView.SelectedNode.Tag != null && this.treeView.SelectedNode.Tag is DirectoryInfo)
			{
				DirectoryInfo folder = (DirectoryInfo)this.treeView.SelectedNode.Tag;

				if (OnDeleteRequest != null)
				{
					OnDeleteRequest(this, new DeleteRequestFromTreeEventArgs(folder.FullName));
				}
			}
		}

		internal void RemoveNode(string path)
		{
			if (this.nodePropsBackup.ContainsKey(path))
			{
				this.nodePropsBackup.Remove(path);
			}

			if (this.directoryToTreeNodeMapping.ContainsKey(path))
			{
				this.directoryToTreeNodeMapping[path].Remove();
				this.directoryToTreeNodeMapping.Remove(path);
			}
		}

		#region Directory protection

		// NotBob - Added ProtectRoot t
		internal void ProtectRoot()
		{
			if (treeView.Nodes.Count > 0)
			{
				this.ProtectNode(treeView.Nodes[0]);
			}
		}

		internal void ProtectSelected()
		{
			if (treeView.SelectedNode != null)
			{
				this.ProtectNode(treeView.SelectedNode);
			}
		}

		internal void UnprotectSelected()
		{
			TreeNode node = treeView.SelectedNode;
			if (node == null)
			{
				return;
			}

			TreeNode parent = node.Parent;
			unprotectNode(node);

			// Symmetry: ProtectNode walks UP and protects ancestors, so unprotect must
			// release any ancestor that no longer has a protected descendant — otherwise
			// ancestors stay visually (and on the protected list) protected forever.
			releaseOrphanedAncestors(parent);
		}

		private void unprotectNode(TreeNode node)
		{
			// A node whose Tag is missing or not a DirectoryInfo (e.g. a placeholder
			// child) must be skipped rather than throwing while walking the tree.
			if (node?.Tag is DirectoryInfo directory)
			{
				if (restoreProtectedNodeVisual(node, directory))
				{
					if (OnProtectionStatusChanged != null)
					{
						OnProtectionStatusChanged(this, new ProtectionStatusChangedEventArgs(directory.FullName, false));
					}
				}

				// Unprotect all subnodes
				foreach (TreeNode subNode in node.Nodes)
				{
					this.unprotectNode(subNode);
				}
			}
		}

		/// <summary>
		/// Restores a node's pre-protection icon/colour and strips the "[Protected]"
		/// label ProtectNode appended. Returns false when the node was not protected.
		/// </summary>
		private bool restoreProtectedNodeVisual(TreeNode node, DirectoryInfo directory)
		{
			if (node == null || directory == null || !this.nodePropsBackup.ContainsKey(directory.FullName))
			{
				return false;
			}

			string[] propList = ((string)this.nodePropsBackup[directory.FullName]).Split('|');
			this.nodePropsBackup.Remove(directory.FullName);

			node.ImageKey = propList[0];
			node.SelectedImageKey = propList[0];
			node.ForeColor = Color.FromArgb(Int32.Parse(propList[1]));

			string protectedLabel = "  [" + TXT.Translate("Protected") + "]";
			if (node.Text.EndsWith(protectedLabel, StringComparison.Ordinal))
			{
				node.Text = node.Text.Substring(0, node.Text.Length - protectedLabel.Length);
			}

			return true;
		}

		private void releaseOrphanedAncestors(TreeNode node)
		{
			// ProtectNode stops walking up at the root node, so mirror that boundary.
			while (node != null && node != rootNode)
			{
				if (node.Tag is DirectoryInfo dir
					&& this.nodePropsBackup.ContainsKey(dir.FullName)
					&& !HasProtectedDescendant(dir.FullName))
				{
					if (restoreProtectedNodeVisual(node, dir) && OnProtectionStatusChanged != null)
					{
						OnProtectionStatusChanged(this, new ProtectionStatusChangedEventArgs(dir.FullName, false));
					}
				}
				node = node.Parent;
			}
		}

		private bool HasProtectedDescendant(string ancestorFullName)
		{
			string prefix = ancestorFullName.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			foreach (string key in this.nodePropsBackup.Keys)
			{
				if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}

		private void ProtectNode(TreeNode node)
		{
			if (node?.Tag is DirectoryInfo directory)
			{
				if (nodePropsBackup.ContainsKey(directory.FullName))
				{
					return;
				}

				if (OnProtectionStatusChanged != null)
				{
					OnProtectionStatusChanged(this, new ProtectionStatusChangedEventArgs(directory.FullName, true));
				}

				// Backup node props if the user changes his mind we can restore the node
				// TODO: I'm sure there is a better way to do this, maybe this info can be stored
				// in the node.Tag or we simply recreate this info like it's a new node.
				nodePropsBackup.Add(directory.FullName, node.ImageKey + "|" + node.ForeColor.ToArgb().ToString());
				if (node == rootNode)
				{
					node.ImageKey = "home_protected";
					node.SelectedImageKey = "home_protected";
				}
				else
				{
					node.ImageKey = "protected_icon";
					node.SelectedImageKey = "protected_icon";
				}
				node.ForeColor = ColorProtected;
				string protectedLabel = "  [" + TXT.Translate("Protected") + "]";
				if (!node.Text.Contains(protectedLabel))
				{
					node.Text += protectedLabel;
				}

				// Recursively protect directories
				if (node.Parent != this.rootNode)
				{
					ProtectNode(node.Parent);
				}
			}
		}

		#endregion Directory protection
	}
}
