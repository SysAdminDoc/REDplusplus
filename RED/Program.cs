using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using NotBob.Config;
using RED.Config;
using RED.Match;
using TXT = RED.RedGetText;

namespace RED
{
	internal static class Program
	{
		private static Mutex singleInstanceMutex;

		[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool SetDefaultDllDirectories(uint directoryFlags);

		[System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
		private static extern bool SetDllDirectoryW(string lpPathName);

		private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

		/// <summary>
		/// A portable exe is often launched from Downloads, where an attacker-
		/// planted DLL sits in the default search path. Restrict unmanaged DLL
		/// resolution to System32 and drop the current directory before any
		/// further P/Invoke (Microsoft DLL-security guidance).
		/// </summary>
		private static void HardenDllSearchPath()
		{
			try
			{
				SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32);
				SetDllDirectoryW(string.Empty);
			}
			catch
			{
				// Hardening only — never block startup
			}
		}

		[STAThread]
		private static void Main()
		{
			HardenDllSearchPath();

			string[] args = Environment.GetCommandLineArgs();
			var paths = new List<string>();
			string logFile = null;
			string exportFile = null;
			string modeOverride = null;
			bool isSilent = false;
			bool isUndo = false;
			bool isDryRun = false;
			bool isJson = false;
			bool emptyFiles = false;

			for (int i = 1; i < args.Length; i++)
			{
				string arg = args[i].ToLowerInvariant();
				switch (arg)
				{
					case "-silent":
					case "--silent":
						isSilent = true;
						break;
					case "-undo":
					case "--undo":
						isUndo = true;
						break;
					case "-dryrun":
					case "--dryrun":
					case "-dry-run":
					case "--dry-run":
						isDryRun = true;
						break;
					case "-json":
					case "--json":
						isJson = true;
						break;
					case "-emptyfiles":
					case "--emptyfiles":
						emptyFiles = true;
						break;
					case "-path":
					case "--path":
						if (i + 1 < args.Length) paths.Add(args[++i]);
						break;
					case "-log":
					case "--log":
						if (i + 1 < args.Length) logFile = args[++i];
						break;
					case "-export":
					case "--export":
						if (i + 1 < args.Length) exportFile = args[++i];
						break;
					case "-mode":
					case "--mode":
						if (i + 1 < args.Length) modeOverride = args[++i].ToLowerInvariant();
						break;
					case "-help":
					case "--help":
					case "-h":
					case "/?":
						PrintUsage();
						Environment.ExitCode = 0;
						return;
					case "-version":
					case "--version":
					case "-v":
						PrintVersion();
						Environment.ExitCode = 0;
						return;
					default:
						// A bare path argument (not an -option) is treated as a scan root
						if (!arg.StartsWith("-") && !arg.StartsWith("/"))
						{
							paths.Add(args[i]);
						}
						break;
				}
			}

			if (isUndo)
			{
				Environment.ExitCode = RunUndo(logFile);
				return;
			}

			if (isSilent || paths.Count > 0)
			{
				if (paths.Count == 0)
				{
					Console.Error.WriteLine("Error: -silent requires at least one -path");
					PrintUsage();
					Environment.ExitCode = 1;
					return;
				}
				Environment.ExitCode = RunHeadless(paths, logFile, exportFile, modeOverride, isDryRun, isJson, emptyFiles);
				return;
			}

			bool createdNew;
			singleInstanceMutex = new Mutex(true, "Global\\REDplusplus_SingleInstance", out createdNew);

			if (!createdNew)
			{
				MessageBox.Show("RED++ is already running.", "RED++", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			try
			{
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);
				Application.Run(new UI.MainWindow());
			}
			finally
			{
				singleInstanceMutex.ReleaseMutex();
				singleInstanceMutex.Dispose();
			}
		}

		/// <summary>
		/// Headless restore of the last deletion run from RED++.undo.json.
		/// Exit 0 = everything restored, 1 = nothing to restore or failures.
		/// </summary>
		private static int RunUndo(string logFile)
		{
			var log = new StringBuilder();
			Action<string> logMsg = (msg) =>
			{
				msg = RED.Helper.RedAssist.SanitizeDisplay(msg);
				log.AppendLine(DateTime.Now.ToString("r") + "\t" + msg);
				Console.WriteLine(msg);
			};

			int restored, failed;
			bool ok = UndoManager.Restore(out restored, out failed, logMsg);
			logMsg(string.Format("Restored: {0}, Failed: {1}", restored, failed));

			WriteLogFile(logFile, log);
			return ok ? 0 : 1;
		}

		private static readonly System.Collections.Generic.Dictionary<string, DeleteModes> ModeAliases =
			new System.Collections.Generic.Dictionary<string, DeleteModes>(StringComparer.OrdinalIgnoreCase)
			{
				{ "recycle", DeleteModes.RecycleBin },
				{ "recyclebin", DeleteModes.RecycleBin },
				{ "direct", DeleteModes.Direct },
				{ "move", DeleteModes.MoveToFolder },
				{ "simulate", DeleteModes.Simulate },
				{ "dryrun", DeleteModes.Simulate },
			};

		private static int RunHeadless(List<string> paths, string logFile, string exportFile, string modeOverride, bool dryRun, bool jsonOutput, bool emptyFiles)
		{
			var log = new StringBuilder();
			Action<string> logMsg = (msg) =>
			{
				// Bidi/zero-width chars in folder names could otherwise reorder the
				// rendered log line and misrepresent which path was deleted
				msg = RED.Helper.RedAssist.SanitizeDisplay(msg);
				string line = DateTime.Now.ToString("r") + "\t" + msg;
				log.AppendLine(line);
				if (!jsonOutput) Console.WriteLine(msg);
			};

			// Never show dialogs in headless mode — a modal prompt would hang Task Scheduler
			ConfigAssist.SilentMode = true;

			RedConfiguration config = null;
			ConfigAssist.ConfigLoad(ref config, "RemoveEmptyDirectories");

			DeleteModes deleteMode = (DeleteModes)config.Options.DeleteMode;
			if (dryRun)
			{
				deleteMode = DeleteModes.Simulate;
			}
			else if (modeOverride != null)
			{
				DeleteModes parsed;
				if (!ModeAliases.TryGetValue(modeOverride, out parsed))
				{
					Console.Error.WriteLine("Error: unknown -mode '" + modeOverride + "' (use recycle|direct|move|simulate)");
					return 1;
				}
				deleteMode = parsed;
			}

			bool hadErrors = false;
			int totalEmpty = 0, totalDeleted = 0, totalFailed = 0;
			var allResults = new System.Collections.Generic.List<RedScanResultItem>();

			foreach (string rawPath in paths)
			{
				string targetPath = Environment.ExpandEnvironmentVariables(rawPath);
				var startDir = new System.IO.DirectoryInfo(targetPath);
				if (!startDir.Exists)
				{
					logMsg("Error: directory does not exist: " + targetPath);
					hadErrors = true;
					continue;
				}

				var runData = new RuntimeData();
				runData.StartFolder = startDir;
				runData.HideScanErrors = config.Options.HideScanErrors;
				runData.HideDeletionErrors = true;
				runData.IgnoreEmptyFiles = config.Options.IgnoreEmptyFiles;
				runData.IgnoreHiddenFolders = config.Options.IgnoreHiddenDirectories;
				runData.IgnoreSystemFolders = config.Options.IgnoreSystemDirectories;
				runData.MinFolderAgeHours = config.Options.MinDirectoryAgeHours;
				runData.MaxDepth = config.Options.MaxDirectoryDepth;
				runData.InfiniteLoopDetectionCount = config.Options.InfiniteLoopDetectionCount;
				runData.DeleteMode = deleteMode;
				runData.PauseTime = config.Options.PauseBetweenDeletions;
				runData.HideIgnoredDirectories = config.Options.HideIgnoredDirectories;
				runData.RespectGitIgnore = config.Options.RespectGitIgnore;
				runData.UseMftScan = config.Options.UseMftScan;
				runData.DeleteEmptyFiles = config.Options.DeleteEmptyFiles || emptyFiles;
				runData.IgnoreFileNameList.Transform(config.Filters.FilesToIgnore);
				runData.IgnoreDirectoryNameList.Transform(config.Filters.DirectoriesToIgnore);
				runData.NeverEmptyDirectoryList.Transform(config.Filters.DirectoriesNeverEmpty);

				logMsg(string.Format("RED++ scan ({0}{1}): {2}", deleteMode, dryRun ? ", dry-run" : "", targetPath));

				var core = new REDCore(runData);
				var scanDone = new ManualResetEvent(false);
				var deleteDone = new ManualResetEvent(false);
				int emptyCount = 0;
				int failed = 0;
				bool runErrors = false;

				core.OnFinishedScanForEmptyDirs += (s, e) => { emptyCount = e.EmptyFolderCount; scanDone.Set(); };
				core.OnCancelled += (s, e) => { scanDone.Set(); deleteDone.Set(); };
				core.OnAborted += (s, e) => { runErrors = true; scanDone.Set(); deleteDone.Set(); };
				core.OnError += (s, e) => { runErrors = true; logMsg("Error: " + e.Message); scanDone.Set(); deleteDone.Set(); };

				core.SearchingForEmptyDirectories();
				scanDone.WaitOne();

				int emptyFileCount = runData.EmptyFileResults.Count;
				logMsg(string.Format("Found {0} empty directories", emptyCount));
				if (runData.DeleteEmptyFiles)
				{
					logMsg(string.Format("Found {0} empty files", emptyFileCount));
				}
				totalEmpty += emptyCount;

				// Honor the same default as the GUI: protect the start folder itself
				if (config.Options.AutoProtectRoot)
				{
					core.AddProtectedFolder(startDir.FullName);
				}

				// Simulate/dry-run never deletes — scan results stand as the report.
				// The delete phase must also run when only empty files were found
				// (the empty-files pre-pass lives inside the deletion worker).
				if ((emptyCount > 0 || emptyFileCount > 0) && !runErrors && deleteMode != DeleteModes.Simulate)
				{
					int deleted = 0;
					core.OnDeleteProcessFinished += (s, e) => { deleted = e.DeletedFolderCount; failed = e.FailedFolderCount; deleteDone.Set(); };
					core.OnDeleteError += (s, e) => { runErrors = true; deleteDone.Set(); };

					core.StartDeleteProcess();
					deleteDone.WaitOne();

					logMsg(string.Format("Deleted: {0}, Failed: {1}", deleted, failed));
					totalDeleted += deleted;
					totalFailed += failed;
				}

				foreach (RedScanResultItem item in runData.ScanResults)
				{
					allResults.Add(item);
					if (!jsonOutput) logMsg(item.FullPath);
				}

				hadErrors |= runErrors;
				runData.Dispose();
			}

			if (jsonOutput)
			{
				EmitJson(allResults);
			}

			if (!string.IsNullOrWhiteSpace(exportFile))
			{
				try
				{
					WriteExportFile(exportFile, allResults);
					logMsg("Exported results to: " + exportFile);
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine("Failed to export: " + ex.Message);
					hadErrors = true;
				}
			}

			WriteLogFile(logFile, log);
			return (hadErrors || totalFailed > 0) ? 1 : 0;
		}

		/// <summary>One NDJSON object per scanned result to stdout, for piping.</summary>
		private static void EmitJson(System.Collections.Generic.List<RedScanResultItem> results)
		{
			foreach (RedScanResultItem item in results)
			{
				string path = (item.FullPath ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
				string reason = (item.StatusReason ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
				Console.WriteLine(string.Format("{{\"path\":\"{0}\",\"status\":\"{1}\",\"reason\":\"{2}\"}}", path, item.SearchStatus, reason));
			}
		}

		private static void WriteExportFile(string exportFile, System.Collections.Generic.List<RedScanResultItem> results)
		{
			var list = new Match.RedScanResultItemList();
			foreach (RedScanResultItem item in results) { list.AddItem(item); }

			string ext = Path.GetExtension(exportFile).ToLowerInvariant();
			using (var exporter = new RED.Helper.RedExportScanResults())
			{
				if (ext == ".csv") exporter.WriteCsvFile(list, exportFile);
				else if (ext == ".json") exporter.WriteJsonFile(list, exportFile);
				else File.WriteAllLines(exportFile, GetPathLines(results), Encoding.UTF8);
			}
		}

		private static System.Collections.Generic.IEnumerable<string> GetPathLines(System.Collections.Generic.List<RedScanResultItem> results)
		{
			foreach (RedScanResultItem item in results) yield return item.FullPath;
		}

		private static void PrintVersion()
		{
			var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(System.Reflection.Assembly.GetExecutingAssembly().Location);
			Console.WriteLine("RED++ " + vi.FileVersion);
		}

		private static void PrintUsage()
		{
			Console.WriteLine(@"RED++ - Remove Empty Directories

Usage:
  RED+.exe [-silent] -path <dir> [-path <dir> ...] [options]
  RED+.exe -undo [-log <file>]
  RED+.exe -help | -version

Options:
  -path <dir>      Scan root (repeatable). A bare path argument also works.
  -silent          Headless mode (no window). Implied when -path is given.
  -dryrun          Scan and report only; never delete (forces simulate mode).
  -emptyfiles      Also delete standalone zero-byte files (sister mode, opt-in).
  -mode <mode>     Override delete mode: recycle | direct | move | simulate.
  -export <file>   Write results to .txt / .csv / .json (by extension).
  -json            Emit one NDJSON object per result to stdout.
  -log <file>      Write a timestamped run log to <file>.
  -undo            Restore the directories deleted by the last run.
  -help, -version  Show this help / the version and exit.

Exit code: 0 = success, 1 = errors or failed deletions.");
		}

		private static void WriteLogFile(string logFile, StringBuilder log)
		{
			if (!string.IsNullOrWhiteSpace(logFile))
			{
				try
				{
					File.WriteAllText(logFile, log.ToString(), Encoding.UTF8);
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine("Failed to write log file: " + ex.Message);
				}
			}
		}
	}
}
