using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using RED.Helper;
using TXT = RED.RedGetText;

namespace RED
{
	/// <summary>
	/// Searches for empty directories
	/// </summary>
	public class FindEmptyDirectoryWorker : BackgroundWorker
	{
		private int folderCount = 0;

		public int FolderCount
		{
			get { return folderCount; }
		}

		public RuntimeData RunData { get; set; }

		public DeletionErrorEventArgs ErrorInfo { get; set; }

		public int PossibleEndlessLoop { get; set; }

		public FindEmptyDirectoryWorker()
		{
			WorkerReportsProgress = true;
			WorkerSupportsCancellation = true;
		}

		internal void ReportDirectoryStatus(DirectoryInfo directory, DirectorySearchStatusTypes type)
		{
			ReportDirectoryStatus(directory, type, null);
		}

		internal void ReportDirectoryStatus(DirectoryInfo directory, DirectorySearchStatusTypes type, string errorMessage)
		{
			var info = string.IsNullOrEmpty(errorMessage)
				? new FoundEmptyDirInfoEventArgs(directory, type)
				: new FoundEmptyDirInfoEventArgs(directory, type, errorMessage);

			if (type == DirectorySearchStatusTypes.Empty)
			{
				this.RunData.ScanResults.AddItem(info.ScanResult);
			}

			this.ReportProgress(0, info);
		}

		/// <summary>
		/// Maps a directory-read failure to a short, human-readable cause so the result
		/// row tells the user *why* a folder could not be read (and therefore was kept)
		/// instead of a generic "could not be read". Returns null for an unknown cause.
		/// </summary>
		internal static string DescribeAccessError(Exception ex)
		{
			if (ex == null) return null;
			// FastDirectoryEnumerator surfaces native enumeration failures as a
			// Win32Exception carrying the raw Win32 error code, so map that first.
			if (ex is System.ComponentModel.Win32Exception w32)
			{
				return DescribeWin32Error(w32.NativeErrorCode);
			}
			if (ex is UnauthorizedAccessException) return TXT.Translate("access denied");
			if (ex is PathTooLongException) return TXT.Translate("path too long");
			if (ex is DirectoryNotFoundException) return TXT.Translate("path no longer exists");
			if (ex is IOException)
			{
				int code = ex.HResult & 0xFFFF;
				if (code == 32 || code == 33) return TXT.Translate("in use by another process"); // SHARING/LOCK_VIOLATION
				return TXT.Translate("I/O error");
			}
			return null;
		}

		private static string DescribeWin32Error(int code)
		{
			switch (code)
			{
				case 5: return TXT.Translate("access denied");            // ERROR_ACCESS_DENIED
				case 19: return TXT.Translate("media is write-protected"); // ERROR_WRITE_PROTECT
				case 32:
				case 33: return TXT.Translate("in use by another process"); // SHARING / LOCK_VIOLATION
				case 2:
				case 3: return TXT.Translate("path no longer exists");     // FILE / PATH_NOT_FOUND
				case 206: return TXT.Translate("path too long");           // FILENAME_EXCED_RANGE
				default: return TXT.Translate("I/O error");
			}
		}

		private GitIgnoreParser gitIgnoreParser;

		protected override void OnDoWork(DoWorkEventArgs e)
		{
			DirectoryInfo startFolder = (DirectoryInfo)e.Argument;

			this.PossibleEndlessLoop = 0;

			// Multi-path scans append to the previous results so the delete pass
			// covers every scanned root, not just the most recent one.
			if (!this.RunData.AppendScanResults)
			{
				this.RunData.ScanResults.Clear();
				this.RunData.EmptyFileResults.Clear();
			}

			if (this.RunData.RespectGitIgnore)
			{
				gitIgnoreParser = GitIgnoreParser.LoadFromAncestors(startFolder.FullName);
			}
			else
			{
				gitIgnoreParser = null;
			}

			if (this.RunData.UseMftScan && TryMftScan(startFolder, e))
			{
				if (CancellationPending) { e.Cancel = true; }
				e.Result = 1;
				return;
			}

			try
			{
				if ((startFolder.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
				{
					string emsg = SystemFunctions.IsCloudPlaceholderDirectory(startFolder.FullName)
						? TXT.Translate("The start folder is a cloud placeholder directory and cannot be scanned safely: {0}", RedAssist.DQuote(startFolder.FullName))
						: TXT.Translate("The start folder is a reparse point (junction, symlink, or mount point) and cannot be scanned: {0}", RedAssist.DQuote(startFolder.FullName));
					this.RunData.AddLogMessage(emsg);
					this.ReportDirectoryStatus(startFolder, DirectorySearchStatusTypes.Error, emsg);
					e.Cancel = true;
					this.ErrorInfo = new DeletionErrorEventArgs(startFolder.FullName, emsg);
					return;
				}

				DirectorySearchStatusTypes rootStatusType = this.CheckIfDirectoryEmpty(startFolder, 1, gitIgnoreParser);

				this.ReportDirectoryStatus(startFolder, rootStatusType);

				if (this.PossibleEndlessLoop > this.RunData.InfiniteLoopDetectionCount)
				{
					string emsg = TXT.Translate("Detected possible infinite - loop somewhere in the target path {0} (symbolic links can cause this)", RedAssist.DQuote(startFolder.FullName));
					this.RunData.AddLogMessage(emsg);
					throw new Exception(emsg);
				}
			}
			catch (Exception ex)
			{
				e.Cancel = true;
				this.RunData.AddLogMessage(TXT.Translate("An error occurred during the scan process: " + ex.Message));
				this.ErrorInfo = new DeletionErrorEventArgs(startFolder.FullName, ex.Message);
				return;
			}

			if (CancellationPending)
			{
				this.RunData.AddLogMessage(TXT.Translate("Scan process was cancelled"));
				e.Cancel = true;
				e.Result = 0;
				return;
			}

			e.Result = 1;
		}

		/// <summary>
		/// A branch nested deeper than this almost certainly indicates a filesystem
		/// cycle (e.g. a junction the enumeration could not identify as a reparse
		/// point on some network filesystems). Each hit increments the loop counter;
		/// the scan aborts once it exceeds the configured detection count.
		/// </summary>
		private const int SuspiciousDepth = 256;

		/// <summary>
		/// Adds zero-byte files (not matched by the ignore list) to the empty-file
		/// results. Ignore-list files (e.g. an empty Thumbs.db) are skipped — they
		/// are trash handled by directory deletion, not standalone targets.
		/// </summary>
		private void CollectEmptyFiles(FileInfo[] fileList)
		{
			foreach (FileInfo file in fileList)
			{
				try
				{
					if (file.Length != 0) continue;

					int rawAttribs = (int)file.Attributes;
					const int RECALL_ON_DATA = 0x00400000;
					const int RECALL_ON_OPEN = 0x00040000;
					if ((rawAttribs & RECALL_ON_DATA) != 0 || (rawAttribs & RECALL_ON_OPEN) != 0) continue;

					string pattern;
					// IsOnList with ignoreEmptyFiles=false: only true name-based ignore
					// rules match here, so a truly-named trash file is excluded
					if (this.RunData.IgnoreFileNameList.IsOnList(file, 0, false, out pattern)) continue;

					this.RunData.EmptyFileResults.Add(file);
					this.RunData.AddLogMessage(TXT.Translate("Empty file queued for deletion: {0}", RedAssist.DQuote(file.FullName)));
				}
				catch { }
			}
		}

		private DirectorySearchStatusTypes CheckIfDirectoryEmpty(DirectoryInfo startDir, int depth, GitIgnoreParser gitIgnore)
		{
			if (this.PossibleEndlessLoop > this.RunData.InfiniteLoopDetectionCount)
			{
				this.ReportDirectoryStatus(startDir, DirectorySearchStatusTypes.Error, TXT.Translate("Aborted - possible infinite-loop detected"));
				return DirectorySearchStatusTypes.Error;
			}

			if (depth > SuspiciousDepth)
			{
				this.PossibleEndlessLoop++;
				this.RunData.AddLogMessage(TXT.Translate("Suspiciously deep directory nesting at {0} - possible filesystem loop", RedAssist.DQuote(startDir.FullName)));
				this.ReportDirectoryStatus(startDir, DirectorySearchStatusTypes.Error, TXT.Translate("Aborted - possible infinite-loop detected"));
				return DirectorySearchStatusTypes.Error;
			}

			try
			{
				// Thread.Sleep(500); -> ?

				if (this.RunData.MaxDepth != -1 && depth > this.RunData.MaxDepth)
				{
					return DirectorySearchStatusTypes.NotEmpty;
				}

				// Cancel process if the user hits stop
				if (CancellationPending)
				{
					return DirectorySearchStatusTypes.NotEmpty;
				}

				if (gitIgnore != null)
					gitIgnore = gitIgnore.ExtendForDirectory(startDir.FullName, this.RunData.StartFolder.FullName);

				this.folderCount++;

				// update status progress bar after 100 steps:
				if (this.folderCount % 100 == 0)
				{
					this.ReportProgress(folderCount, TXT.Translate("Checking directory: {0}", startDir.Name));
				}

				bool containsFiles = false;

				// A .redkeep marker file protects its directory (and everything below
				// it) from deletion — it travels with the folder across copies and
				// shares, unlike the per-config filter lists.
				if (RedKeepMarker.HasMarker(startDir))
				{
					string msg = TXT.Translate("Directory is protected by a {0} marker file: {1}", RedKeepMarker.MarkerFileName, RedAssist.DQuote(startDir.FullName));
					this.RunData.AddLogMessage(msg);
					if (!this.RunData.HideIgnoredDirectories)
					{
						this.ReportDirectoryStatus(startDir, DirectorySearchStatusTypes.NeverEmpty, msg);
					}
					return DirectorySearchStatusTypes.NotEmpty;
				}

				// NotBob - If this directory is on the NeverEmpty list treat it as if it contains files
				if (this.RunData.NeverEmptyDirectoryList.IsOnList(startDir))
				{
					containsFiles = true;
					string msg = TXT.Translate("Directory is on the NeverEmpty list: {0}", RedAssist.DQuote(startDir.FullName));
					this.RunData.AddLogMessage(msg);
					if (!this.RunData.HideIgnoredDirectories)
					{
						this.ReportDirectoryStatus(startDir, DirectorySearchStatusTypes.NeverEmpty, msg);
					}
				}

				if (!containsFiles)
				{
					// Get file list
					FileInfo[] fileList = null;

					// some directories could trigger an exception:
					Exception fileAccessError = null;
					try
					{
						fileList = FastDirectoryEnumerator.GetFiles(startDir);
					}
					catch (Exception ex)
					{
						fileList = null;
						fileAccessError = ex;
					}

					if (fileList == null)
					{
						// if containsFiles is true then the folder does not get deleted:
						containsFiles = true; // secure way
						string cause = DescribeAccessError(fileAccessError);
						string reason = string.IsNullOrEmpty(cause)
							? TXT.Translate("Could not read directory contents")
							: TXT.Translate("Could not read directory contents ({0})", cause);
						this.RunData.AddLogMessage(TXT.Translate("Could not read {0} ({1})", RedAssist.DQuote(startDir.FullName), cause ?? (fileAccessError != null ? fileAccessError.Message : TXT.Translate("unknown error"))));
						this.ReportDirectoryStatus(startDir, DirectorySearchStatusTypes.Error, reason);
					}
					else if (fileList.Length == 0)
					{
						containsFiles = false;
					}
					else
					{
						string delPattern = string.Empty;

						// loop trough files and cancel if containsFiles == true
						for (int f = 0; (f < fileList.Length && !containsFiles); f++)
						{
							FileInfo file = null;
							long filesize = 0;

							try
							{
								file = fileList[f];
								filesize = file.Length;
							}
							catch
							{
								// keep folder if there is a strange file that triggers an exception:
								containsFiles = true;
								break;
							}

							// Cloud-only placeholder files (OneDrive, iCloud) are real content
							const int FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000;
							const int FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x00040000;
							int rawAttribs = (int)file.Attributes;
							if ((rawAttribs & FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS) != 0 || (rawAttribs & FILE_ATTRIBUTE_RECALL_ON_OPEN) != 0)
							{
								containsFiles = true;
								break;
							}

							// It only takes one file to be found to stop the scan
							if (!this.RunData.IgnoreFileNameList.IsOnList(file, filesize, RunData.IgnoreEmptyFiles, out delPattern))
							{
								containsFiles = true;
							}
						}

						// Empty-files sister mode: collect standalone zero-byte files
						// (not on the ignore list) for deletion. Separate non-short-
						// circuiting pass so it finds every empty file, not just up to
						// the first real file that stops the directory check above.
						if (this.RunData.DeleteEmptyFiles)
						{
							CollectEmptyFiles(fileList);
						}
					}
				}

				// (CollectEmptyFiles is defined below; invoked above when DeleteEmptyFiles is on)

				List<DirectoryInfo> subFolderList = new List<DirectoryInfo>();
				try
				{
					subFolderList.AddRange(FastDirectoryEnumerator.GetDirectories(startDir));
				}
				catch (Exception ex)
				{
					// If we can not read the folder -> don't delete it:
					string cause = DescribeAccessError(ex);
					string reason = string.IsNullOrEmpty(cause)
						? TXT.Translate("Could not read subdirectories")
						: TXT.Translate("Could not read subdirectories ({0})", cause);
					this.RunData.AddLogMessage(TXT.Translate("Could not read subdirectories of {0} ({1})", RedAssist.DQuote(startDir.FullName), cause ?? ex.Message));
					this.ReportDirectoryStatus(startDir, DirectorySearchStatusTypes.Error, reason);
					return DirectorySearchStatusTypes.Error;
				}

				// The folder is empty, break here:
				if (!containsFiles && subFolderList.Count == 0)
				{
					return DirectorySearchStatusTypes.Empty;
				}

				bool allSubDirectoriesEmpty = true;

				// NotBob - sort subfolders to give a more 'natural' order to the displayed results
				subFolderList.Sort((x, y) => x.Name.CompareTo(y.Name));

				foreach (DirectoryInfo curDir in subFolderList)
				{
					FileAttributes attribs = curDir.Attributes;

					bool ignoreSystemDir = (this.RunData.IgnoreSystemFolders && ((attribs & FileAttributes.System) == FileAttributes.System));
					bool ignoreHiddenDir = (this.RunData.IgnoreHiddenFolders && ((attribs & FileAttributes.Hidden) == FileAttributes.Hidden));

					bool ignoreSubDirectory = (ignoreSystemDir || ignoreHiddenDir);

					if (!ignoreSubDirectory && this.RunData.IgnoreDirectoryNameList.IsOnList(curDir))
					{
						this.RunData.AddLogMessage(TXT.Translate("Aborted scan of {0} because it is on the ignore list", RedAssist.DQuote(curDir.FullName)));
						ignoreSubDirectory = true;
						// NotBob - option to exclude ignored directories from the scan window
						if (!this.RunData.HideIgnoredDirectories)
						{
							this.ReportDirectoryStatus(curDir, DirectorySearchStatusTypes.Ignore);
						}
					}

					if (!ignoreSubDirectory && gitIgnore != null && gitIgnore.HasRules)
					{
						string relativePath = curDir.FullName.Substring(this.RunData.StartFolder.FullName.Length);
						if (gitIgnore.IsIgnored(curDir.Name, relativePath))
						{
							this.RunData.AddLogMessage(TXT.Translate("Aborted scan of {0} because it is on the ignore list", RedAssist.DQuote(curDir.FullName)));
							ignoreSubDirectory = true;
							if (!this.RunData.HideIgnoredDirectories)
							{
								this.ReportDirectoryStatus(curDir, DirectorySearchStatusTypes.Ignore);
							}
						}
					}

					if (!ignoreSubDirectory && (attribs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
					{
						if (SystemFunctions.IsCloudPlaceholderDirectory(curDir.FullName))
						{
							this.RunData.AddLogMessage(TXT.Translate("Skipped cloud placeholder directory: {0}", RedAssist.DQuote(curDir.FullName)));
							this.ReportDirectoryStatus(curDir, DirectorySearchStatusTypes.NeverEmpty, TXT.Translate("cloud placeholder directory"));
						}
						else
						{
							this.RunData.AddLogMessage(TXT.Translate("Aborted scan of {0} because it is a symbolic link", RedAssist.DQuote(curDir.FullName)));
							this.ReportDirectoryStatus(curDir, DirectorySearchStatusTypes.Error, TXT.Translate("Aborted because directory is a symbolic link"));
						}
						ignoreSubDirectory = true;
					}

					// TODO: Implement more checks
					//else if ((attribs & FileAttributes.Device) == FileAttributes.Device) msg = "Device - Aborted - found";
					//else if ((attribs & FileAttributes.Encrypted) == FileAttributes.Encrypted) msg = "Encrypted -  found";
					// The file will not be indexed by the operating system's content indexing service.
					// else if ((attribs & FileAttributes.NotContentIndexed) == FileAttributes.NotContentIndexed) msg = "NotContentIndexed - Device found";
					//else if ((attribs & FileAttributes.Offline) == FileAttributes.Offline) msg = "Offline -  found";
					//else if ((attribs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly) msg = "ReadOnly -  found";
					//else if ((attribs & FileAttributes.Temporary) == FileAttributes.Temporary) msg = "Temporary -  found";

					// Scan sub folder:
					DirectorySearchStatusTypes subFolderStatus = DirectorySearchStatusTypes.NotEmpty;

					if (!ignoreSubDirectory)
					{
						// JRS ADDED check for AGE of folder
						if (curDir.CreationTime.AddHours(this.RunData.MinFolderAgeHours) < DateTime.Now)
						{
							subFolderStatus = this.CheckIfDirectoryEmpty(curDir, depth + 1, gitIgnore);
						}
						else
						{
							this.RunData.AddLogMessage(TXT.Translate("Directory {0} skipped because creation time [{1}] is < {2} hours old", RedAssist.DQuote(curDir.FullName), curDir.CreationTime.ToString(), this.RunData.MinFolderAgeHours.ToString()));
						}

						// Report status to the GUI
						if (subFolderStatus == DirectorySearchStatusTypes.Empty)
						{
							this.ReportDirectoryStatus(curDir, subFolderStatus);
						}
					}

					// this folder is not empty:
					if (subFolderStatus != DirectorySearchStatusTypes.Empty || ignoreSubDirectory)
					{
						allSubDirectoriesEmpty = false;
					}
				}

				// All subdirectories are empty
				return (allSubDirectoriesEmpty && !containsFiles) ? DirectorySearchStatusTypes.Empty : DirectorySearchStatusTypes.NotEmpty;
			}
			catch (Exception ex)
			{
				if (ex is System.IO.PathTooLongException)
				{
					this.RunData.AddLogMessage(TXT.Translate("Path too long: {0}", RedAssist.DQuote(startDir.FullName)));
				}
				else
				{
					this.RunData.AddLogMessage(TXT.Translate("An unknown error occurred while trying to scan directory: {0} - {1}", RedAssist.DQuote(startDir.FullName), ex.Message));
				}
				this.ReportDirectoryStatus(startDir, DirectorySearchStatusTypes.Error, ex.Message);
				return DirectorySearchStatusTypes.Error;
			}
		}

		private bool TryMftScan(DirectoryInfo startFolder, DoWorkEventArgs e)
		{
			if (this.RunData.DeleteEmptyFiles)
			{
				this.RunData.AddLogMessage("MFT scan: empty-file mode requires file sizes unavailable from USN records, falling back to standard scan");
				return false;
			}

			if (!MftScanner.IsSupportedVolume(startFolder.FullName))
			{
				this.RunData.AddLogMessage("MFT scan: not an NTFS or ReFS volume, falling back to standard scan");
				return false;
			}

			this.RunData.AddLogMessage("MFT scan: enumerating volume MFT...");
			this.ReportProgress(0, "MFT: reading volume index...");

			var scanner = new MftScanner();
			string volumeRoot = Path.GetPathRoot(startFolder.FullName);

			if (!scanner.EnumerateMft(volumeRoot, this))
			{
				this.RunData.AddLogMessage("MFT scan: enumeration failed (admin required), falling back to standard scan");
				return false;
			}

			MftScanner.Frn? startFrn = scanner.FindFrnByPath(startFolder.FullName);
			if (!startFrn.HasValue)
			{
				this.RunData.AddLogMessage("MFT scan: could not locate start folder in MFT, falling back to standard scan");
				return false;
			}

			this.RunData.AddLogMessage("MFT scan: analyzing directory tree...");
			this.ReportProgress(0, "MFT: finding empty directories...");

			scanner.FindEmptyDirectories(startFrn.Value, volumeRoot, this.RunData, this, ref this.folderCount, gitIgnoreParser, startFolder.FullName);

			this.ReportDirectoryStatus(startFolder, DirectorySearchStatusTypes.NotEmpty);

			this.RunData.AddLogMessage(string.Format("MFT scan complete: checked {0} directories", this.folderCount));
			return true;
		}
	}
}
