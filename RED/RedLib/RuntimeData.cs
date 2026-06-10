using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RED.Match;

namespace RED
{
	public class RuntimeData : IDisposable
	{
		private StreamWriter _logWriter;
		private bool _disposed;

		public RuntimeData()
		{
			this.LogMessages = new StringBuilder();
			this.ProtectedFolderList = new Dictionary<string, bool>();
			this.ScanResults = new RedScanResultItemList();

			try
			{
				_logWriter = new StreamWriter(GetWritableDataFilePath("RED++.log"), append: true, encoding: Encoding.UTF8) { AutoFlush = true };
			}
			catch
			{
				_logWriter = null;
			}
		}

		/// <summary>
		/// Returns a path for an app data file next to the executable (portable mode).
		/// Falls back to the %APPDATA% config folder when the install directory is not
		/// writable (e.g. Program Files), so logs and undo manifests are never lost silently.
		/// </summary>
		public static string GetWritableDataFilePath(string fileName)
		{
			string exeDir = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
			try
			{
				string probe = Path.Combine(exeDir, fileName + ".writetest");
				File.WriteAllText(probe, string.Empty);
				File.Delete(probe);
				return Path.Combine(exeDir, fileName);
			}
			catch
			{
				string appData = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
					"NotBob", "RemoveEmptyDirectories");
				Directory.CreateDirectory(appData);
				return Path.Combine(appData, fileName);
			}
		}

		public RedMatchItemList NeverEmptyDirectoryList = new RedMatchItemList(RedMatchFilterType.Directory);
		public RedMatchItemList IgnoreDirectoryNameList = new RedMatchItemList(RedMatchFilterType.Directory);
		public RedMatchItemList IgnoreFileNameList = new RedMatchItemList(RedMatchFilterType.Files);

		public DirectoryInfo StartFolder { get; set; }

        public bool HideScanErrors { get; set; }
        public bool HideDeletionErrors { get; set; }

		public DeleteModes DeleteMode { get; set; }
		public bool IgnoreEmptyFiles { get; set; }
		public bool IgnoreHiddenFolders { get; set; }
		public bool IgnoreSystemFolders { get; set; }
		public double PauseTime { get; set; }
		public uint MinFolderAgeHours { get; set; }

		public int MaxDepth { get; set; }
		public int InfiniteLoopDetectionCount { get; set; }

		public StringBuilder LogMessages = null;
		public Dictionary<string, bool> ProtectedFolderList = new Dictionary<string, bool>();

		public bool HideIgnoredDirectories { get; set; }
		public bool RespectGitIgnore { get; set; }
		public bool UseMftScan { get; set; }
		public bool DeleteEmptyFiles { get; set; }

		/// <summary>
		/// Standalone zero-byte files found when DeleteEmptyFiles is on. Kept
		/// separate from the directory ScanResults so the verified directory
		/// deletion pipeline is untouched; deleted in a pre-pass before directories.
		/// </summary>
		public List<FileInfo> EmptyFileResults = new List<FileInfo>();

		/// <summary>
		/// When true the next scan appends to ScanResults instead of clearing them.
		/// Used by multi-path (drag-and-drop) sequential scans.
		/// </summary>
		public bool AppendScanResults { get; set; }

		public RedScanResultItemList ScanResults { get; private set; }

		public void AddLogMessage(string msg)
		{
			string line = DateTime.Now.ToString("r") + "\t" + msg;
			this.LogMessages.AppendLine(line);
			try { _logWriter?.WriteLine(line); } catch { }
		}

		internal void AddLogSpacer()
		{
			if (this.LogMessages.Length > 0)
            {
                this.LogMessages.Append(Environment.NewLine);
            }
			try { _logWriter?.WriteLine(); } catch { }
        }

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			try
			{
				_logWriter?.Flush();
				_logWriter?.Dispose();
			}
			catch { }
			_logWriter = null;
		}
	}
}
