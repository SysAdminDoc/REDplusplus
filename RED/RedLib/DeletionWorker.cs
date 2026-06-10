using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using RED.Helper;
using TXT = RED.RedGetText;

namespace RED
{
	/// <summary>
	/// Deletes the empty directories RED found
	/// </summary>
	public class DeletionWorker : BackgroundWorker
	{
		public RuntimeData Data { get; set; }

		public int DeletedCount { get; set; }
		public int FailedCount { get; set; }
		public int ProtectedCount { get; set; }

		public int ListPos { get; set; }

		public DeletionErrorEventArgs ErrorInfo { get; set; }

		private List<UndoManifestEntry> undoEntries = new List<UndoManifestEntry>();

		// Survives error-continue cycles: after a deletion error the worker is re-run
		// to resume at ListPos, and re-sorting or forgetting already-deleted parents
		// would skip pending items or re-delete vanished ones.
		private readonly System.Collections.Generic.HashSet<string> deletedParents =
			new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public DeletionWorker()
		{
			WorkerReportsProgress = true;
			WorkerSupportsCancellation = true;

			this.ListPos = 0;
		}

		protected override void OnDoWork(DoWorkEventArgs e)
		{
			// This method will run on a thread other than the UI thread.
			// Be sure not to manipulate any Windows Forms controls created
			// on the UI thread from this method.

			if (CancellationPending)
			{
				e.Cancel = true;
				return;
			}

			bool stopNow = false;
			string errorMessage = string.Empty;
			this.ErrorInfo = null;

			if (this.ListPos == 0)
			{
				// Fresh run (not an error-continue resume): order parents before
				// children so one recursive delete covers a wholly-empty subtree.
				this.Data.ScanResults.Items.Sort((a, b) => a.FullPath.Length.CompareTo(b.FullPath.Length));
			}

			while (this.ListPos < this.Data.ScanResults.Count)
			{
				if (CancellationPending)
				{
					e.Cancel = true;
					WriteUndoManifest();
					return;
				}

				DirectoryDeletionStatusTypes status = DirectoryDeletionStatusTypes.Ignored;
				Match.RedScanResultItem scanResult = this.Data.ScanResults[this.ListPos];

				bool alreadyDeletedByParent = false;
				foreach (string parentPath in deletedParents)
				{
					if (scanResult.FullPath.StartsWith(parentPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
					{
						alreadyDeletedByParent = true;
						break;
					}
				}

				if (alreadyDeletedByParent)
				{
					status = DirectoryDeletionStatusTypes.Deleted;
					this.DeletedCount++;
					// Children removed by a parent's recursive delete need their own
					// undo entry or a restore would recreate only the subtree root.
					// (Move mode: the parent's move-back restores them; the recreate
					// is then a no-op on the already-existing directory.)
					undoEntries.Add(new UndoManifestEntry
					{
						Path = scanResult.FullPath,
						Mode = this.Data.DeleteMode.ToString(),
						MovedTo = null
					});
				}
				// Do not delete one time protected folders
				else if (!this.Data.ProtectedFolderList.ContainsKey(scanResult.FullPath))
				{
					try
					{
						string movedTo = this.secureDelete(scanResult.Directory);
						this.Data.AddLogMessage(TXT.Translate("Successfully deleted directory: {0}", RedAssist.DQuote(scanResult.FullPath)));
						status = DirectoryDeletionStatusTypes.Deleted;
						this.DeletedCount++;
						deletedParents.Add(scanResult.FullPath);
						undoEntries.Add(new UndoManifestEntry
						{
							Path = scanResult.FullPath,
							Mode = this.Data.DeleteMode.ToString(),
							MovedTo = movedTo
						});
					}
					catch (REDPermissionDeniedException ex)
					{
						errorMessage = ex.Message;
						this.Data.AddLogMessage(TXT.Translate("Directory is protected by the system: {0} - {1}", RedAssist.DQuote(scanResult.FullPath), RedGetText.Words.ErrorMessage1(errorMessage)));
						status = DirectoryDeletionStatusTypes.Protected;
						this.ProtectedCount++;
					}
					catch (Exception ex)
					{
						errorMessage = ex.Message;
						stopNow = (!this.Data.HideDeletionErrors);
						this.Data.AddLogMessage(TXT.Translate("Failed to delete directory: {0} - {1}", RedAssist.DQuote(scanResult.FullPath), RedGetText.Words.ErrorMessage1(errorMessage)));
						status = DirectoryDeletionStatusTypes.Warning;
						this.FailedCount++;
					}

					if (!stopNow && this.Data.PauseTime > 0)
					{
						Thread.Sleep(TimeSpan.FromMilliseconds(this.Data.PauseTime));
					}
				}
				else
				{
					status = DirectoryDeletionStatusTypes.Protected;
				}

				this.ReportProgress(1, new DeleteProcessUpdateEventArgs(this.ListPos, scanResult, status, this.Data.ScanResults.Count));

				this.ListPos++;

				if (stopNow)
				{
					// stop here for now
					if (string.IsNullOrWhiteSpace(errorMessage))
					{
						errorMessage = TXT.Translate(RedGetText.Words.ErorrUnknown);
					}

					e.Cancel = true;
					this.ErrorInfo = new DeletionErrorEventArgs(scanResult.FullPath, errorMessage);
					// Record what was already deleted even though the run stopped early
					WriteUndoManifest();
					return;
				}
			}

			e.Result = this.Data.ScanResults.Count;

			WriteUndoManifest();
		}

		private void WriteUndoManifest()
		{
			if (undoEntries.Count == 0) return;
			try
			{
				string manifestPath = RuntimeData.GetWritableDataFilePath("RED++.undo.json");

				var sb = new StringBuilder();
				sb.AppendLine("{");
				sb.AppendLine("  \"timestamp\": \"" + DateTime.Now.ToString("o") + "\",");
				sb.AppendLine("  \"deleteMode\": \"" + this.Data.DeleteMode.ToString() + "\",");
				sb.AppendLine("  \"entries\": [");
				for (int i = 0; i < undoEntries.Count; i++)
				{
					var entry = undoEntries[i];
					sb.Append("    { \"path\": \"" + EscapeJson(entry.Path) + "\"");
					if (entry.MovedTo != null)
						sb.Append(", \"movedTo\": \"" + EscapeJson(entry.MovedTo) + "\"");
					sb.Append(", \"mode\": \"" + entry.Mode + "\"");
					sb.Append(" }");
					if (i < undoEntries.Count - 1) sb.Append(",");
					sb.AppendLine();
				}
				sb.AppendLine("  ]");
				sb.AppendLine("}");

				File.WriteAllText(manifestPath, sb.ToString(), Encoding.UTF8);
				this.Data.AddLogMessage(TXT.Translate("Undo manifest written: {0}", RedAssist.DQuote(manifestPath)));
			}
			catch { }
		}

		private static string EscapeJson(string s)
		{
			return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}

		/// <returns>The actual MoveToFolder destination, null for other modes.</returns>
		private string secureDelete(DirectoryInfo emptyDirectory)
		{
			//var emptyDirectory = new DirectoryInfo(path);

			if (!emptyDirectory.Exists)
			{
				throw new Exception(TXT.Translate("Could not delete the directory because it does not exist anymore: {0}", RedAssist.DQuote(emptyDirectory.FullName)));
			}

			// Cleanup folder

			//String[] ignoreFileList = this.Data.GetIgnoreFileList();

			FileInfo[] Files = emptyDirectory.GetFiles();

			if (Files != null && Files.Length != 0)
			{
				// loop trough files and cancel if containsFiles == true
				for (int f = 0; f < Files.Length; f++)
				{
					FileInfo file = Files[f];

					string delPattern;
					bool deleteTrashFile = this.Data.IgnoreFileNameList.IsOnList(file, file.Length, this.Data.IgnoreEmptyFiles, out delPattern);

					// If only one file is good, then stop.
					if (deleteTrashFile)
					{
						try
						{
							SystemFunctions.SecureDeleteFile(file, this.Data.DeleteMode);
							this.Data.AddLogMessage(TXT.Translate("-> Successfully deleted file {0} because it matched the ignore pattern {1}", RedAssist.DQuote(file.FullName), RedAssist.DQuote(delPattern)));
						}
						catch (Exception ex)
						{
							string msg = TXT.Translate("Could not delete this empty (trash) file: {0}", RedAssist.DQuote(file.FullName));
							this.Data.AddLogMessage(msg + RedGetText.Words.ErrorMessage1(ex.Message));
							msg = msg + RedGetText.CrLf2 + RedGetText.Words.ErrorMessage1(ex.Message);
							if (ex is REDPermissionDeniedException)
							{
								throw new REDPermissionDeniedException(msg, ex);
							}
							else
							{
								throw new Exception(msg, ex);
							}
						}
					}
				}
			}

			// End cleanup

			// This function will ensure that the directory is really empty before it gets deleted
			string movedTo;
			SystemFunctions.SecureDeleteDirectory(emptyDirectory.FullName, this.Data.DeleteMode, out movedTo);
			return movedTo;
		}
	}

	internal class UndoManifestEntry
	{
		public string Path { get; set; }
		public string Mode { get; set; }
		public string MovedTo { get; set; }
	}
}