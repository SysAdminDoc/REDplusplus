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

				// Empty-files pre-pass (opt-in). Done before directories and on the
				// fresh run only, isolated from the verified directory pipeline.
				DeleteEmptyFiles();
			}

			if (this.Data.DeleteMode == DeleteModes.RecycleBin ||
				this.Data.DeleteMode == DeleteModes.RecycleBinShowErrors ||
				this.Data.DeleteMode == DeleteModes.RecycleBinWithQuestion)
			{
				BatchRecycleRun(e);
				return;
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

		/// <summary>
		/// All Recycle Bin modes delete through one IFileOperation transaction —
		/// per-call shell setup made large runs take minutes (RED+ reported ~3 min
		/// for 250 dirs) and the legacy VisualBasic call could raise modal dialogs
		/// even headless. Every path is reparse/emptiness re-verified immediately
		/// before queueing; per-item results arrive through the progress sink.
		/// </summary>
		private void BatchRecycleRun(DoWorkEventArgs e)
		{
			bool silent = NotBob.Config.ConfigAssist.SilentMode;
			bool allowConfirmation = this.Data.DeleteMode == DeleteModes.RecycleBinWithQuestion && !silent;
			bool allowErrorUi = (this.Data.DeleteMode == DeleteModes.RecycleBinShowErrors ||
								 this.Data.DeleteMode == DeleteModes.RecycleBinWithQuestion) && !silent;

			var queuedPaths = new List<string>();
			var queuedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var positionByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			var resultByPath = new Dictionary<string, RecycleBinOperation.ItemResult>(StringComparer.OrdinalIgnoreCase);
			// (position, item, queued ancestor) — resolved from the ancestor's result
			var deferredChildren = new List<Tuple<int, Match.RedScanResultItem, string>>();
			int total = this.Data.ScanResults.Count;

			// Phase 1: verify and queue topmost dirs only. Children of a queued dir
			// vanish with their parent's recursive recycle — queueing them too would
			// just produce unreportable not-found items in the sink.
			for (int pos = this.ListPos; pos < total; pos++)
			{
				if (CancellationPending)
				{
					e.Cancel = true;
					WriteUndoManifest();
					return;
				}

				Match.RedScanResultItem scanResult = this.Data.ScanResults[pos];

				if (this.Data.ProtectedFolderList.ContainsKey(scanResult.FullPath))
				{
					this.ReportProgress(1, new DeleteProcessUpdateEventArgs(pos, scanResult, DirectoryDeletionStatusTypes.Protected, total));
					continue;
				}

				string queuedAncestor = null;
				foreach (string rootPath in queuedRoots)
				{
					if (scanResult.FullPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
					{
						queuedAncestor = rootPath;
						break;
					}
				}
				if (queuedAncestor != null)
				{
					deferredChildren.Add(Tuple.Create(pos, scanResult, queuedAncestor));
					continue;
				}

				try
				{
					cleanupTrashFiles(scanResult.Directory);
					SystemFunctions.VerifyRecycleSafe(scanResult.FullPath);
				}
				catch (REDPermissionDeniedException ex)
				{
					this.Data.AddLogMessage(TXT.Translate("Directory is protected by the system: {0} - {1}", RedAssist.DQuote(scanResult.FullPath), RedGetText.Words.ErrorMessage1(ex.Message)));
					this.ProtectedCount++;
					this.ReportProgress(1, new DeleteProcessUpdateEventArgs(pos, scanResult, DirectoryDeletionStatusTypes.Protected, total));
					continue;
				}
				catch (Exception ex)
				{
					this.Data.AddLogMessage(TXT.Translate("Failed to delete directory: {0} - {1}", RedAssist.DQuote(scanResult.FullPath), RedGetText.Words.ErrorMessage1(ex.Message)));
					this.FailedCount++;
					this.ReportProgress(1, new DeleteProcessUpdateEventArgs(pos, scanResult, DirectoryDeletionStatusTypes.Warning, total));
					continue;
				}

				queuedPaths.Add(scanResult.FullPath);
				queuedRoots.Add(scanResult.FullPath);
				positionByPath[scanResult.FullPath] = pos;
			}

			// Phase 2: one shell transaction for every verified topmost dir.
			// Results may arrive with a null/different path for vanished items, so
			// queue order is the fallback key (DeleteItem order is preserved).
			try
			{
				int resultIndex = 0;
				RecycleBinOperation.RecycleBatch(
					queuedPaths,
					allowConfirmation,
					allowErrorUi,
					() => CancellationPending,
					result =>
					{
						string path = result.Path;
						if (path == null || !positionByPath.ContainsKey(path))
						{
							path = (resultIndex < queuedPaths.Count) ? queuedPaths[resultIndex] : null;
						}
						resultIndex++;
						if (path != null)
						{
							resultByPath[path] = result;
						}
					});
			}
			catch (Exception ex)
			{
				// The transaction itself failed (COM error before any item ran)
				this.Data.AddLogMessage(TXT.Translate("Recycle Bin operation failed: {0}", ex.Message));
			}

			// Phase 3: report queued roots from their sink results, then resolve
			// children from their ancestor's outcome
			foreach (string path in queuedPaths)
			{
				int pos = positionByPath[path];
				Match.RedScanResultItem scanResult = this.Data.ScanResults[pos];
				RecycleBinOperation.ItemResult result;
				bool deleted = resultByPath.TryGetValue(path, out result)
					? (result.Succeeded || IsNotFoundHResult(result.HResult))
					: !Directory.Exists(path); // no sink result (cancelled/skipped) — trust the filesystem

				if (deleted)
				{
					this.Data.AddLogMessage(TXT.Translate("Successfully deleted directory: {0}", RedAssist.DQuote(path)));
					this.DeletedCount++;
					undoEntries.Add(new UndoManifestEntry { Path = path, Mode = this.Data.DeleteMode.ToString(), MovedTo = null });
					this.ReportProgress(1, new DeleteProcessUpdateEventArgs(pos, scanResult, DirectoryDeletionStatusTypes.Deleted, total));
				}
				else
				{
					string detail = (result != null) ? new System.ComponentModel.Win32Exception(result.HResult & 0xFFFF).Message : TXT.Translate("Operation did not complete");
					this.Data.AddLogMessage(TXT.Translate("Failed to delete directory: {0} - {1}", RedAssist.DQuote(path), RedGetText.Words.ErrorMessage1(detail)));
					this.FailedCount++;
					this.ReportProgress(1, new DeleteProcessUpdateEventArgs(pos, scanResult, DirectoryDeletionStatusTypes.Warning, total));
				}
			}

			foreach (Tuple<int, Match.RedScanResultItem, string> child in deferredChildren)
			{
				bool parentDeleted = resultByPath.ContainsKey(child.Item3)
					? (resultByPath[child.Item3].Succeeded || IsNotFoundHResult(resultByPath[child.Item3].HResult))
					: !Directory.Exists(child.Item3);

				if (parentDeleted)
				{
					this.DeletedCount++;
					undoEntries.Add(new UndoManifestEntry { Path = child.Item2.FullPath, Mode = this.Data.DeleteMode.ToString(), MovedTo = null });
					this.ReportProgress(1, new DeleteProcessUpdateEventArgs(child.Item1, child.Item2, DirectoryDeletionStatusTypes.Deleted, total));
				}
				else
				{
					this.FailedCount++;
					this.ReportProgress(1, new DeleteProcessUpdateEventArgs(child.Item1, child.Item2, DirectoryDeletionStatusTypes.Warning, total));
				}
			}

			this.ListPos = total;
			if (CancellationPending)
			{
				e.Cancel = true;
			}
			else
			{
				e.Result = total;
			}

			WriteUndoManifest();
		}

		private static bool IsNotFoundHResult(int hr)
		{
			const int E_FILENOTFOUND = unchecked((int)0x80070002);
			const int E_PATHNOTFOUND = unchecked((int)0x80070003);
			return hr == E_FILENOTFOUND || hr == E_PATHNOTFOUND;
		}

		/// <summary>
		/// Deletes the standalone zero-byte files collected during the scan, via the
		/// active delete mode. Kept entirely separate from the directory deletion
		/// pipeline; failures are logged and counted but never abort the run.
		/// </summary>
		private void DeleteEmptyFiles()
		{
			if (this.Data.EmptyFileResults == null || this.Data.EmptyFileResults.Count == 0)
			{
				return;
			}

			foreach (System.IO.FileInfo file in this.Data.EmptyFileResults)
			{
				if (CancellationPending) return;
				try
				{
					file.Refresh();
					if (!file.Exists) continue;
					// Guard: only delete if still zero bytes (the scan may be stale)
					if (file.Length != 0)
					{
						this.Data.AddLogMessage(TXT.Translate("Skipped file because it is no longer empty: {0}", RedAssist.DQuote(file.FullName)));
						continue;
					}

					SystemFunctions.SecureDeleteFile(file, this.Data.DeleteMode);
					this.Data.AddLogMessage(TXT.Translate("Successfully deleted empty file: {0}", RedAssist.DQuote(file.FullName)));
					this.DeletedCount++;
					undoEntries.Add(new UndoManifestEntry
					{
						Path = file.FullName,
						Mode = this.Data.DeleteMode.ToString(),
						MovedTo = null,
						IsFile = true
					});
				}
				catch (Exception ex)
				{
					this.Data.AddLogMessage(TXT.Translate("Failed to delete empty file: {0} - {1}", RedAssist.DQuote(file.FullName), RedGetText.Words.ErrorMessage1(ex.Message)));
					this.FailedCount++;
				}
			}
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
					if (entry.IsFile)
						sb.Append(", \"isFile\": true");
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
			if (!emptyDirectory.Exists)
			{
				throw new Exception(TXT.Translate("Could not delete the directory because it does not exist anymore: {0}", RedAssist.DQuote(emptyDirectory.FullName)));
			}

			cleanupTrashFiles(emptyDirectory);

			// This function will ensure that the directory is really empty before it gets deleted
			string movedTo;
			SystemFunctions.SecureDeleteDirectory(emptyDirectory.FullName, this.Data.DeleteMode, out movedTo);
			return movedTo;
		}

		/// <summary>
		/// Deletes the ignorable (trash) files inside a directory that the scan
		/// classified as empty, so the directory delete that follows can succeed.
		/// </summary>
		private void cleanupTrashFiles(DirectoryInfo emptyDirectory)
		{
			if (!emptyDirectory.Exists)
			{
				throw new Exception(TXT.Translate("Could not delete the directory because it does not exist anymore: {0}", RedAssist.DQuote(emptyDirectory.FullName)));
			}

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
		}
	}

	internal class UndoManifestEntry
	{
		public string Path { get; set; }
		public string Mode { get; set; }
		public string MovedTo { get; set; }
		public bool IsFile { get; set; }
	}
}