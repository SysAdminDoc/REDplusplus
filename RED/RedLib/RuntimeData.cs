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
				string logPath = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "RED++.log");
				_logWriter = new StreamWriter(logPath, append: true, encoding: Encoding.UTF8) { AutoFlush = true };
			}
			catch
			{
				_logWriter = null;
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
