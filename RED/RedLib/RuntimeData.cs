using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using RED.Match;

namespace RED
{
	public class RuntimeData : IDisposable
	{
		private const long MaxLogBytes = 5L * 1024L * 1024L;
		private StreamWriter _logWriter;
		private bool _disposed;
		internal static string TrustedDataDirectoryOverride { get; set; }

		public RuntimeData()
		{
			this.LogMessages = new StringBuilder();
			this.ProtectedFolderList = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			this.ScanResults = new RedScanResultItemList();

			try
			{
				string logPath = GetWritableDataFilePath("RED++.log");
				string rotatedPath;
				bool rotated = RotateLogIfNeeded(logPath, out rotatedPath);

				_logWriter = new StreamWriter(logPath, append: true, encoding: Encoding.UTF8) { AutoFlush = true };
				if (rotated)
				{
					_logWriter.WriteLine(DateTime.Now.ToString("r") + "\t" + "Rotated log file to " + rotatedPath);
				}
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

		/// <summary>
		/// Security-sensitive recovery state goes in a per-user, ACL-restricted
		/// directory instead of beside the portable exe. A shared/writable install
		/// directory must not let one Windows account plant an undo manifest that a
		/// later elevated run by another account will trust.
		/// </summary>
		public static string GetTrustedDataDirectory()
		{
			if (!string.IsNullOrWhiteSpace(TrustedDataDirectoryOverride))
			{
				Directory.CreateDirectory(TrustedDataDirectoryOverride);
				return TrustedDataDirectoryOverride;
			}

			string dir = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"NotBob", "RemoveEmptyDirectories");
			Directory.CreateDirectory(dir);
			ApplyRestrictedDirectoryAcl(dir);
			return dir;
		}

		public static string GetTrustedDataFilePath(string fileName)
		{
			return Path.Combine(GetTrustedDataDirectory(), fileName);
		}

		private static void ApplyRestrictedDirectoryAcl(string dir)
		{
			try
			{
				var identity = WindowsIdentity.GetCurrent();
				if (identity == null || identity.User == null) return;

				var security = new DirectorySecurity();
				var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
				var propagation = PropagationFlags.None;
				security.SetOwner(identity.User);
				security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
				security.AddAccessRule(new FileSystemAccessRule(identity.User, FileSystemRights.FullControl, inheritance, propagation, AccessControlType.Allow));
				security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inheritance, propagation, AccessControlType.Allow));
				security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inheritance, propagation, AccessControlType.Allow));
				new DirectoryInfo(dir).SetAccessControl(security);
			}
			catch
			{
				// The location is still per-user LocalAppData if ACL rewriting is
				// blocked by policy/filesystem. Never fall back to a shared exe folder.
			}
		}

		private static bool RotateLogIfNeeded(string logPath, out string rotatedPath)
		{
			rotatedPath = logPath + ".1";
			try
			{
				var info = new FileInfo(logPath);
				if (!info.Exists || info.Length <= MaxLogBytes)
				{
					return false;
				}

				if (File.Exists(rotatedPath))
				{
					File.Delete(rotatedPath);
				}
				File.Move(logPath, rotatedPath);
				return true;
			}
			catch
			{
				// Rotation failed (e.g. the log is held open elsewhere). The log is a
				// forensic/undo aid, so never truncate it — leave the existing file
				// intact and let the caller reopen it in append mode.
				return false;
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
		public Dictionary<string, bool> ProtectedFolderList = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

		public bool HideIgnoredDirectories { get; set; }
		public bool RespectGitIgnore { get; set; }
		public bool UseMftScan { get; set; }
		public bool DeleteEmptyFiles { get; set; }
		public int ParallelScanDegree { get; set; }

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

		private readonly Lock _logLock = new Lock();

		public void AddLogMessage(string msg)
		{
			string line = DateTime.Now.ToString("r") + "\t" + msg;
			lock (_logLock)
			{
				this.LogMessages.AppendLine(line);
				try { _logWriter?.WriteLine(line); } catch { }
			}
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
