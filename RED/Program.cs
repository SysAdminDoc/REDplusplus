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
			string moveTarget = null;
			bool isSilent = false;
			bool isAutoSearch = false;
			bool isUndo = false;
			bool isDryRun = false;
			bool isJson = false;
			bool emptyFiles = false;
			bool quiet = false;
			string parseError = null;
			uint? minAgeOverride = null;
			int? maxDepthOverride = null;
			bool? respectGitIgnoreOverride = null;
			bool? useMftScanOverride = null;
			bool? ignoreHiddenOverride = null;
			bool? ignoreSystemOverride = null;

			for (int i = 1; i < args.Length; i++)
			{
				string arg = args[i].ToLowerInvariant();
				switch (arg)
				{
					case "-silent":
					case "--silent":
						isSilent = true;
						break;
					case "-autosearch":
					case "--autosearch":
						isAutoSearch = true;
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
					case "-quiet":
					case "--quiet":
						quiet = true;
						break;
					case "-emptyfiles":
					case "--emptyfiles":
						emptyFiles = true;
						break;
					case "-minage":
					case "--minage":
						if (i + 1 >= args.Length)
						{
							parseError = "Error: -minage requires a non-negative whole number of hours";
						}
						else
						{
							uint parsedMinAge;
							if (!uint.TryParse(args[++i], out parsedMinAge))
								parseError = "Error: -minage requires a non-negative whole number of hours";
							else
								minAgeOverride = parsedMinAge;
						}
						break;
					case "-maxdepth":
					case "--maxdepth":
						if (i + 1 >= args.Length)
						{
							parseError = "Error: -maxdepth requires -1 or a positive whole number";
						}
						else
						{
							int parsedMaxDepth;
							if (!int.TryParse(args[++i], out parsedMaxDepth) || parsedMaxDepth < -1)
								parseError = "Error: -maxdepth requires -1 or a positive whole number";
							else
								maxDepthOverride = parsedMaxDepth;
						}
						break;
					case "-gitignore":
					case "--gitignore":
						respectGitIgnoreOverride = true;
						break;
					case "-no-gitignore":
					case "--no-gitignore":
						respectGitIgnoreOverride = false;
						break;
					case "-mft":
					case "--mft":
						useMftScanOverride = true;
						break;
					case "-no-mft":
					case "--no-mft":
						useMftScanOverride = false;
						break;
					case "-hidden":
					case "--hidden":
					case "-include-hidden":
					case "--include-hidden":
						ignoreHiddenOverride = false;
						break;
					case "-ignore-hidden":
					case "--ignore-hidden":
						ignoreHiddenOverride = true;
						break;
					case "-system":
					case "--system":
					case "-include-system":
					case "--include-system":
						ignoreSystemOverride = false;
						break;
					case "-ignore-system":
					case "--ignore-system":
						ignoreSystemOverride = true;
						break;
					case "-path":
					case "--path":
						if (i + 1 >= args.Length)
							parseError = "Error: -path requires a directory argument";
						else
							paths.Add(args[++i]);
						break;
					case "-log":
					case "--log":
						if (i + 1 >= args.Length)
							parseError = "Error: -log requires a file path argument";
						else
							logFile = args[++i];
						break;
					case "-export":
					case "--export":
						if (i + 1 >= args.Length)
							parseError = "Error: -export requires a file path argument";
						else
							exportFile = args[++i];
						break;
					case "-mode":
					case "--mode":
						if (i + 1 >= args.Length)
							parseError = "Error: -mode requires a value (recycle|direct|move|simulate)";
						else
							modeOverride = args[++i].ToLowerInvariant();
						break;
					case "-moveto":
					case "--moveto":
					case "-move-to":
					case "--move-to":
						if (i + 1 >= args.Length)
							parseError = "Error: -moveto requires a directory argument";
						else
							moveTarget = args[++i];
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
						if (arg.StartsWith("-"))
						{
							parseError = "Error: unknown option '" + args[i] + "'";
						}
						else
						{
							paths.Add(args[i]);
						}
						break;
				}
			}

			if (parseError != null)
			{
				if (!quiet)
				{
					Console.Error.WriteLine(parseError);
					PrintUsage();
				}
				Environment.ExitCode = 1;
				return;
			}

			if (isUndo)
			{
				Environment.ExitCode = RunUndo(logFile, quiet);
				return;
			}

			if (!isAutoSearch && (isSilent || paths.Count > 0))
			{
				if (paths.Count == 0)
				{
					if (!quiet)
					{
						Console.Error.WriteLine("Error: -silent requires at least one -path");
						PrintUsage();
					}
					Environment.ExitCode = 1;
					return;
				}
				Environment.ExitCode = RunHeadless(paths, logFile, exportFile, modeOverride, moveTarget, isDryRun, isJson, emptyFiles, quiet,
					minAgeOverride, maxDepthOverride, respectGitIgnoreOverride, useMftScanOverride, ignoreHiddenOverride, ignoreSystemOverride);
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
		private static int RunUndo(string logFile, bool quiet)
		{
			var log = new StringBuilder();
			Action<string> logMsg = (msg) =>
			{
				msg = RED.Helper.RedAssist.SanitizeDisplay(msg);
				log.AppendLine(DateTime.Now.ToString("r") + "\t" + msg);
				if (!quiet) Console.WriteLine(msg);
			};

			int restored, failed;
			bool ok = UndoManager.Restore(out restored, out failed, logMsg);
			logMsg(string.Format("Restored: {0}, Failed: {1}", restored, failed));

			WriteLogFile(logFile, log, quiet);
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

		private static int RunHeadless(List<string> paths, string logFile, string exportFile, string modeOverride, string moveTarget, bool dryRun, bool jsonOutput, bool emptyFiles, bool quiet,
			uint? minAgeOverride, int? maxDepthOverride, bool? respectGitIgnoreOverride, bool? useMftScanOverride, bool? ignoreHiddenOverride, bool? ignoreSystemOverride)
		{
			var log = new StringBuilder();
			Action<string> logMsg = (msg) =>
			{
				// Bidi/zero-width chars in folder names could otherwise reorder the
				// rendered log line and misrepresent which path was deleted
				msg = RED.Helper.RedAssist.SanitizeDisplay(msg);
				string line = DateTime.Now.ToString("r") + "\t" + msg;
				log.AppendLine(line);
				if (!jsonOutput && !quiet) Console.WriteLine(msg);
			};
			Action<string> errorMsg = (msg) =>
			{
				msg = RED.Helper.RedAssist.SanitizeDisplay(msg);
				log.AppendLine(DateTime.Now.ToString("r") + "\t" + msg);
				if (!quiet) Console.Error.WriteLine(msg);
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
					errorMsg("Error: unknown -mode '" + modeOverride + "' (use recycle|direct|move|simulate)");
					WriteLogFile(logFile, log, quiet);
					return 1;
				}
				deleteMode = parsed;
			}

			if (deleteMode == DeleteModes.MoveToFolder)
			{
				if (string.IsNullOrWhiteSpace(moveTarget))
				{
					errorMsg("Error: -mode move requires -moveto <dir>");
					WriteLogFile(logFile, log, quiet);
					return 1;
				}
				SystemFunctions.MoveToFolderTarget = Environment.ExpandEnvironmentVariables(moveTarget);
			}

			bool hadErrors = false;
			int totalEmpty = 0, totalEmptyFiles = 0, totalDeleted = 0, totalFailed = 0;
			var allResults = new System.Collections.Generic.List<RedScanResultItem>();

			foreach (string rawPath in paths)
			{
				string targetPath = Environment.ExpandEnvironmentVariables(rawPath);
				var startDir = new System.IO.DirectoryInfo(targetPath);
				if (!startDir.Exists)
				{
					errorMsg("Error: directory does not exist: " + targetPath);
					hadErrors = true;
					continue;
				}

				var runData = new RuntimeData();
				runData.StartFolder = startDir;
				runData.HideScanErrors = config.Options.HideScanErrors;
				runData.HideDeletionErrors = true;
				runData.IgnoreEmptyFiles = config.Options.IgnoreEmptyFiles;
				runData.IgnoreHiddenFolders = ignoreHiddenOverride.HasValue ? ignoreHiddenOverride.Value : config.Options.IgnoreHiddenDirectories;
				runData.IgnoreSystemFolders = ignoreSystemOverride.HasValue ? ignoreSystemOverride.Value : config.Options.IgnoreSystemDirectories;
				runData.MinFolderAgeHours = minAgeOverride.HasValue ? minAgeOverride.Value : config.Options.MinDirectoryAgeHours;
				runData.MaxDepth = maxDepthOverride.HasValue ? maxDepthOverride.Value : config.Options.MaxDirectoryDepth;
				runData.InfiniteLoopDetectionCount = config.Options.InfiniteLoopDetectionCount;
				runData.DeleteMode = deleteMode;
				runData.PauseTime = config.Options.PauseBetweenDeletions;
				runData.HideIgnoredDirectories = config.Options.HideIgnoredDirectories;
				runData.RespectGitIgnore = respectGitIgnoreOverride.HasValue ? respectGitIgnoreOverride.Value : config.Options.RespectGitIgnore;
				runData.UseMftScan = useMftScanOverride.HasValue ? useMftScanOverride.Value : config.Options.UseMftScan;
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
				totalEmptyFiles += emptyFileCount;

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
					int deletedDirectories = 0;
					int deletedFiles = 0;
					int failedDirectories = 0;
					int failedFiles = 0;
					core.OnDeleteProcessFinished += (s, e) =>
					{
						deletedDirectories = e.DeletedFolderCount;
						deletedFiles = e.DeletedFileCount;
						failedDirectories = e.FailedFolderCount;
						failedFiles = e.FailedFileCount;
						failed = failedDirectories + failedFiles;
						deleteDone.Set();
					};
					core.OnDeleteError += (s, e) => { runErrors = true; deleteDone.Set(); };

					core.StartDeleteProcess();
					deleteDone.WaitOne();

					logMsg(string.Format("Deleted directories: {0}, Deleted files: {1}, Failed directories: {2}, Failed files: {3}", deletedDirectories, deletedFiles, failedDirectories, failedFiles));
					totalDeleted += deletedDirectories + deletedFiles;
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

			if (jsonOutput && !quiet)
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
					errorMsg("Failed to export: " + ex.Message);
					hadErrors = true;
				}
			}

			WriteLogFile(logFile, log, quiet);
			if (hadErrors || totalFailed > 0) return 1;
			if (deleteMode == DeleteModes.Simulate && (totalEmpty + totalEmptyFiles) > 0) return 11;
			return 0;
		}

		/// <summary>One NDJSON object per scanned result to stdout, for piping.</summary>
		private static void EmitJson(System.Collections.Generic.List<RedScanResultItem> results)
		{
			Console.WriteLine(string.Format("{{\"type\":\"meta\",\"schema\":1,\"version\":\"{0}\"}}", EscapeJson(GetFileVersion())));
			foreach (RedScanResultItem item in results)
			{
				Console.WriteLine(string.Format("{{\"type\":\"result\",\"path\":\"{0}\",\"status\":\"{1}\",\"reason\":\"{2}\"}}", EscapeJson(item.FullPath), item.SearchStatus, EscapeJson(item.StatusReason)));
			}
		}

		private static string EscapeJson(string value)
		{
			if (value == null) return string.Empty;
			var sb = new StringBuilder(value.Length + 8);
			foreach (char c in value)
			{
				switch (c)
				{
					case '\\': sb.Append(@"\\"); break;
					case '"': sb.Append("\\\""); break;
					case '\r': sb.Append(@"\r"); break;
					case '\n': sb.Append(@"\n"); break;
					case '\t': sb.Append(@"\t"); break;
					default:
						if (char.IsControl(c)) sb.Append("\\u").Append(((int)c).ToString("x4"));
						else sb.Append(c);
						break;
				}
			}
			return sb.ToString();
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
			Console.WriteLine("RED++ " + GetFileVersion());
		}

		private static string GetFileVersion()
		{
			var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(System.Reflection.Assembly.GetExecutingAssembly().Location);
			return vi.FileVersion;
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
  -moveto <dir>    Required with -mode move; move directories/files to <dir>.
  -minage <hours>  Override minimum directory age for this run.
  -maxdepth <n>    Override maximum scan depth for this run (-1 = infinite).
  -gitignore       Respect .gitignore rules for this run.
  -no-gitignore    Ignore .gitignore rules for this run.
  -mft             Try the MFT turbo scan for this run (admin required).
  -no-mft          Disable the MFT turbo scan for this run.
  -hidden          Include hidden directories for this run.
  -ignore-hidden   Ignore hidden directories for this run.
  -system          Include system directories for this run.
  -ignore-system   Ignore system directories for this run.
  -export <file>   Write results to .txt / .csv / .json (by extension).
  -json            Emit NDJSON to stdout (meta record, then result records).
  -quiet           Suppress stdout/stderr; use the process exit code/log file.
  -log <file>      Write a timestamped run log to <file>.
  -undo            Restore the directories deleted by the last run.
  -help, -version  Show this help / the version and exit.

Exit codes:
  0  Success, or simulate/dry-run found nothing.
  1  Errors, invalid arguments, failed deletions, or failed undo.
  11 Simulate/dry-run succeeded and found empty directories or files.");
		}

		private static void WriteLogFile(string logFile, StringBuilder log, bool quiet)
		{
			if (!string.IsNullOrWhiteSpace(logFile))
			{
				try
				{
					File.WriteAllText(logFile, log.ToString(), Encoding.UTF8);
				}
				catch (Exception ex)
				{
					if (!quiet) Console.Error.WriteLine("Failed to write log file: " + ex.Message);
				}
			}
		}
	}
}
