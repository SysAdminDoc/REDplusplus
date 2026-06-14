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

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(IntPtr hWnd);

		[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
		private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

		internal static readonly string ForwardSignalPath = Path.Combine(Path.GetTempPath(), "REDplusplus_forward.path");

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
			string undoManifest = null;
			bool isSilent = false;
			bool isAutoSearch = false;
			bool isUndo = false;
			bool useEventLog = false;
			bool isDryRun = false;
			bool isJson = false;
			bool emptyFiles = false;
			bool quiet = false;
			bool useClassicUi = false;
			string parseError = null;
			uint? minAgeOverride = null;
			int? maxDepthOverride = null;
			bool? respectGitIgnoreOverride = null;
			bool? useMftScanOverride = null;
			bool? ignoreHiddenOverride = null;
			bool? ignoreSystemOverride = null;
			var excludePatterns = new List<string>();
			var protectPatterns = new List<string>();
			string profileName = null;
			string saveProfileName = null;
			bool listProfiles = false;

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
						if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
							undoManifest = args[++i];
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
					case "-classic":
					case "--classic":
						useClassicUi = true;
						break;
					case "-emptyfiles":
					case "--emptyfiles":
						emptyFiles = true;
						break;
					case "-eventlog":
					case "--eventlog":
						useEventLog = true;
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
					case "-exclude":
					case "--exclude":
						if (i + 1 >= args.Length)
							parseError = "Error: -exclude requires a pattern argument";
						else
							excludePatterns.Add(args[++i]);
						break;
					case "-protect":
					case "--protect":
						if (i + 1 >= args.Length)
							parseError = "Error: -protect requires a pattern argument";
						else
							protectPatterns.Add(args[++i]);
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
					case "-profile":
					case "--profile":
						if (i + 1 >= args.Length)
							parseError = "Error: -profile requires a profile name";
						else
							profileName = args[++i];
						break;
					case "-saveprofile":
					case "--saveprofile":
					case "-save-profile":
					case "--save-profile":
						if (i + 1 >= args.Length)
							parseError = "Error: -saveprofile requires a profile name";
						else
							saveProfileName = args[++i];
						break;
					case "-listprofiles":
					case "--listprofiles":
					case "-list-profiles":
					case "--list-profiles":
						listProfiles = true;
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

			if (listProfiles)
			{
				List<RedProfile> all = ProfileStore.LoadAll();
				if (all.Count == 0) { Console.WriteLine("No saved profiles."); }
				foreach (RedProfile prof in all)
				{
					string roots = prof.Paths != null ? string.Join(", ", prof.Paths) : "";
					Console.WriteLine(string.Format("{0}	[{1}]	{2}", prof.Name, prof.Mode ?? "default", roots));
				}
				Environment.ExitCode = 0;
				return;
			}

			if (profileName != null)
			{
				RedProfile prof = ProfileStore.Get(profileName);
				if (prof == null)
				{
					if (!quiet) Console.Error.WriteLine("Error: profile not found: " + profileName);
					Environment.ExitCode = 1;
					return;
				}
				// Explicit command-line arguments win; the profile only fills gaps.
				if (paths.Count == 0 && prof.Paths != null) paths.AddRange(prof.Paths);
				if (modeOverride == null && !string.IsNullOrEmpty(prof.Mode)) modeOverride = prof.Mode.ToLowerInvariant();
				if (moveTarget == null && !string.IsNullOrEmpty(prof.MoveTo)) moveTarget = prof.MoveTo;
				if (!emptyFiles && prof.EmptyFiles) emptyFiles = true;
				if (!minAgeOverride.HasValue && prof.MinAgeHours.HasValue && prof.MinAgeHours.Value >= 0) minAgeOverride = (uint)prof.MinAgeHours.Value;
				if (!maxDepthOverride.HasValue && prof.MaxDepth.HasValue) maxDepthOverride = prof.MaxDepth.Value;
				if (!respectGitIgnoreOverride.HasValue && prof.GitIgnore.HasValue) respectGitIgnoreOverride = prof.GitIgnore.Value;
				if (!useMftScanOverride.HasValue && prof.Mft.HasValue) useMftScanOverride = prof.Mft.Value;
				if (!ignoreHiddenOverride.HasValue && prof.IgnoreHidden.HasValue) ignoreHiddenOverride = prof.IgnoreHidden.Value;
				if (!ignoreSystemOverride.HasValue && prof.IgnoreSystem.HasValue) ignoreSystemOverride = prof.IgnoreSystem.Value;
			}

			if (saveProfileName != null)
			{
				var prof = new RedProfile
				{
					Name = saveProfileName,
					Paths = new List<string>(paths),
					Mode = modeOverride,
					MoveTo = moveTarget,
					EmptyFiles = emptyFiles,
					MinAgeHours = minAgeOverride.HasValue ? (int?)(int)minAgeOverride.Value : null,
					MaxDepth = maxDepthOverride,
					GitIgnore = respectGitIgnoreOverride,
					Mft = useMftScanOverride,
					IgnoreHidden = ignoreHiddenOverride,
					IgnoreSystem = ignoreSystemOverride
				};
				string saveErr;
				if (ProfileStore.Save(prof, out saveErr))
				{
					if (!quiet) Console.WriteLine("Saved profile: " + saveProfileName);
					Environment.ExitCode = 0;
				}
				else
				{
					if (!quiet) Console.Error.WriteLine("Error: could not save profile: " + saveErr);
					Environment.ExitCode = 1;
				}
				return;
			}

			if (isUndo)
			{
				Environment.ExitCode = RunUndo(logFile, quiet, undoManifest);
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
				Environment.ExitCode = RunHeadless(paths, logFile, exportFile, modeOverride, moveTarget, isDryRun, isJson, emptyFiles, quiet, useEventLog,
					minAgeOverride, maxDepthOverride, respectGitIgnoreOverride, useMftScanOverride, ignoreHiddenOverride, ignoreSystemOverride,
					excludePatterns, protectPatterns);
				return;
			}

			bool createdNew;
			singleInstanceMutex = new Mutex(true, "Global\\REDplusplus_SingleInstance", out createdNew);

			if (!createdNew)
			{
				if (paths.Count > 0)
				{
					try { File.WriteAllText(ForwardSignalPath, paths[0], Encoding.UTF8); } catch { }
				}
				IntPtr hwnd = FindWindow(null, "RED++ - Remove Empty Directories+");
				if (hwnd == IntPtr.Zero) hwnd = FindWindow(null, "Remove Empty Directories+");
				if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd);
				return;
			}

			try
			{
				if (useClassicUi)
				{
					Application.EnableVisualStyles();
					Application.SetCompatibleTextRenderingDefault(false);
					Application.Run(new UI.MainWindow());
				}
				else
				{
					var app = new System.Windows.Application
					{
						ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose
					};
					app.Run(new UI.Wpf.ModernMainWindow(paths.Count > 0 ? paths[0] : null, isAutoSearch));
				}
			}
			finally
			{
				singleInstanceMutex.ReleaseMutex();
				singleInstanceMutex.Dispose();
			}
		}

		/// <summary>
		/// Headless restore from an undo manifest. Without a manifest argument,
		/// restores the most recent run. With a timestamp or file path, restores
		/// that specific manifest.
		/// Exit 0 = everything restored, 1 = nothing to restore or failures.
		/// </summary>
		private static int RunUndo(string logFile, bool quiet, string manifestArg)
		{
			// Undo is a non-interactive CLI path (documented `RED+.exe -undo`).
			// Keep config loading dialog-free so a scripted/scheduled restore cannot hang.
			ConfigAssist.SilentMode = true;
			var log = new StringBuilder();
			Action<string> logMsg = (msg) =>
			{
				msg = RED.Helper.RedAssist.SanitizeDisplay(msg);
				log.AppendLine(DateTime.Now.ToString("r") + "\t" + msg);
				if (!quiet) Console.WriteLine(msg);
			};

			int restored, failed;
			bool ok;

			if (!string.IsNullOrEmpty(manifestArg))
			{
				string resolvedPath = ResolveManifestArg(manifestArg);
				if (resolvedPath == null)
				{
					logMsg("Error: no manifest found matching '" + manifestArg + "'");
					if (!quiet)
					{
						var available = UndoManager.ListManifests();
						if (available.Count > 0)
						{
							logMsg("Available manifests:");
							foreach (var m in available)
								logMsg(string.Format("  {0}  {1}  ({2} entries)", m.Timestamp.ToString("s"), m.DeleteMode, m.EntryCount));
						}
					}
					WriteLogFile(logFile, log, quiet);
					return 1;
				}
				ok = UndoManager.Restore(resolvedPath, out restored, out failed, logMsg);
			}
			else
			{
				ok = UndoManager.Restore(out restored, out failed, logMsg);
			}

			logMsg(string.Format("Restored: {0}, Failed: {1}", restored, failed));
			WriteLogFile(logFile, log, quiet);
			return ok ? 0 : 1;
		}

		private static string ResolveManifestArg(string arg)
		{
			if (File.Exists(arg)) return arg;

			var manifests = UndoManager.ListManifests();

			// Unambiguous matches first: exact filename or exact timestamp.
			foreach (var m in manifests)
			{
				if (m.FileName.Equals(arg, StringComparison.OrdinalIgnoreCase)
					|| m.Timestamp.ToString("yyyy-MM-dd_HH-mm-ss") == arg
					|| m.Timestamp.ToString("s") == arg)
					return m.FilePath;
			}

			// Fall back to a substring match, but only if it is unambiguous —
			// restoring the wrong run would recreate the wrong directories.
			string match = null;
			int matchCount = 0;
			foreach (var m in manifests)
			{
				if (m.FileName.IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					match = m.FilePath;
					matchCount++;
				}
			}
			return matchCount == 1 ? match : null;
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

		private static int RunHeadless(List<string> paths, string logFile, string exportFile, string modeOverride, string moveTarget, bool dryRun, bool jsonOutput, bool emptyFiles, bool quiet, bool useEventLog,
			uint? minAgeOverride, int? maxDepthOverride, bool? respectGitIgnoreOverride, bool? useMftScanOverride, bool? ignoreHiddenOverride, bool? ignoreSystemOverride,
			List<string> excludePatterns, List<string> protectPatterns)
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
				string expandedMoveTarget = Environment.ExpandEnvironmentVariables(moveTarget);
				// A relative move target resolves against the process working directory,
				// which for a scheduled task is typically C:\Windows\System32 — a surprising
				// and dangerous destination. Require an absolute path.
				if (!System.IO.Path.IsPathRooted(expandedMoveTarget))
				{
					errorMsg("Error: -moveto must be an absolute path: " + expandedMoveTarget);
					WriteLogFile(logFile, log, quiet);
					return 1;
				}
				SystemFunctions.MoveToFolderTarget = expandedMoveTarget;
			}

			bool hadErrors = false;
			int totalEmpty = 0, totalEmptyFiles = 0, totalDeleted = 0, totalFailed = 0;
			var allResults = new System.Collections.Generic.List<RedScanResultItem>();
			var runStopwatch = System.Diagnostics.Stopwatch.StartNew();

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

				foreach (string pattern in excludePatterns)
				{
					runData.IgnoreDirectoryNameList.AddItem(true, RedMatchMethodType.NameExact, pattern);
				}
				foreach (string pattern in protectPatterns)
				{
					runData.NeverEmptyDirectoryList.AddItem(true, RedMatchMethodType.NameExact, pattern);
				}

				logMsg(string.Format("RED++ scan ({0}{1}): {2}", deleteMode, dryRun ? ", dry-run" : "", targetPath));

				var core = new REDCore(runData);
				var scanDone = new ManualResetEvent(false);
				var deleteDone = new ManualResetEvent(false);
				int emptyCount = 0;
				int failed = 0;
				bool runErrors = false;
				int progressDirCount = 0;
				DateTime lastProgressTime = DateTime.UtcNow;

				core.OnProgressChanged += (s, ev) =>
				{
					progressDirCount++;
					if (!quiet && (DateTime.UtcNow - lastProgressTime).TotalSeconds >= 2)
					{
						lastProgressTime = DateTime.UtcNow;
						if (jsonOutput)
						{
							Console.Error.WriteLine(string.Format("{{\"type\":\"progress\",\"directories\":{0}}}", progressDirCount));
						}
						else
						{
							Console.Error.Write(string.Format("\rScanning: {0:N0} directories examined...", progressDirCount));
						}
					}
				};

				core.OnFinishedScanForEmptyDirs += (s, e) => { emptyCount = e.EmptyFolderCount; scanDone.Set(); };
				core.OnCancelled += (s, e) => { scanDone.Set(); deleteDone.Set(); };
				core.OnAborted += (s, e) => { runErrors = true; scanDone.Set(); deleteDone.Set(); };
				core.OnError += (s, e) => { runErrors = true; logMsg("Error: " + e.Message); scanDone.Set(); deleteDone.Set(); };

				core.SearchingForEmptyDirectories();
				scanDone.WaitOne();

				if (!quiet && !jsonOutput && progressDirCount > 0)
				{
					Console.Error.Write("\r" + new string(' ', 60) + "\r");
				}

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

				foreach (System.IO.FileInfo fi in runData.EmptyFileResults)
				{
					var fileItem = new RedScanResultItem(fi, DirectorySearchStatusTypes.Empty, "Empty file - zero bytes");
					allResults.Add(fileItem);
					if (!jsonOutput) logMsg(fi.FullName);
				}

				hadErrors |= runErrors;
				runData.Dispose();
			}

			runStopwatch.Stop();
			double elapsedSeconds = runStopwatch.Elapsed.TotalSeconds;

			// Run-impact summary: counts and wall-clock duration. (Reclaimed bytes are
			// intentionally omitted — RED++ only removes empty directories and
			// zero-byte files, so the figure would always be ~0.)
			logMsg(string.Format(
				"Run complete in {0:N1}s: {1} empty director{2}, {3} empty file{4}, {5} deleted, {6} failed",
				elapsedSeconds,
				totalEmpty, totalEmpty == 1 ? "y" : "ies",
				totalEmptyFiles, totalEmptyFiles == 1 ? "" : "s",
				totalDeleted, totalFailed));

			if (jsonOutput && !quiet)
			{
				EmitJson(allResults, totalEmpty, totalEmptyFiles, totalDeleted, totalFailed, runStopwatch.Elapsed.TotalMilliseconds);
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
			int exitCode;
			if (hadErrors || totalFailed > 0) exitCode = 1;
			else if (deleteMode == DeleteModes.Simulate && (totalEmpty + totalEmptyFiles) > 0) exitCode = 11;
			else exitCode = 0;

			if (useEventLog)
			{
				EventLogWriter.WriteRunSummary(
					string.Join("; ", paths),
					deleteMode.ToString(),
					totalEmpty, totalEmptyFiles, totalDeleted, totalFailed, exitCode);
			}

			return exitCode;
		}

		/// <summary>One NDJSON object per scanned result to stdout, for piping.</summary>
		private static void EmitJson(System.Collections.Generic.List<RedScanResultItem> results,
			int emptyDirs, int emptyFiles, int deleted, int failed, double elapsedMs)
		{
			Console.WriteLine(string.Format(
				"{{\"type\":\"meta\",\"schema\":3,\"version\":\"{0}\",\"emptyDirectories\":{1},\"emptyFiles\":{2},\"deleted\":{3},\"failed\":{4},\"elapsedMs\":{5}}}",
				EscapeJson(GetFileVersion()), emptyDirs, emptyFiles, deleted, failed,
				((long)elapsedMs).ToString(System.Globalization.CultureInfo.InvariantCulture)));
			foreach (RedScanResultItem item in results)
			{
				string kind = item.Kind == ResultKind.File ? "file" : "directory";
				Console.WriteLine(string.Format("{{\"type\":\"result\",\"kind\":\"{0}\",\"path\":\"{1}\",\"status\":\"{2}\",\"reason\":\"{3}\"}}", kind, EscapeJson(item.FullPath), item.SearchStatus, EscapeJson(item.StatusReason)));
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
			// Single-file safe: Assembly.Location is empty in a published single-file
			// bundle, which would throw here. Environment.ProcessPath is the apphost
			// exe (it carries the version resource); fall back to the embedded
			// AssemblyFileVersion attribute if the path is unavailable.
			try
			{
				string path = Environment.ProcessPath;
				if (!string.IsNullOrEmpty(path))
				{
					string fv = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion;
					if (!string.IsNullOrEmpty(fv)) return fv;
				}
			}
			catch { }
			var attr = (System.Reflection.AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(
				System.Reflection.Assembly.GetExecutingAssembly(), typeof(System.Reflection.AssemblyFileVersionAttribute));
			return attr != null ? attr.Version : "0.0.0";
		}

		private static void PrintUsage()
		{
			Console.WriteLine(@"RED++ - Remove Empty Directories

Usage:
  RED+.exe [-silent] -path <dir> [-path <dir> ...] [options]
  RED+.exe -undo [<manifest>] [-log <file>]
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
  -exclude <name>  Skip directories matching <name> (repeatable, composable).
  -protect <name>  Prevent deletion of dirs matching <name> (repeatable).
  -export <file>   Write results to .txt / .csv / .json (by extension).
  -json            Emit NDJSON to stdout (meta record, then result records).
  -quiet           Suppress stdout/stderr; use the process exit code/log file.
  -log <file>      Write a timestamped run log to <file>.
  -undo [manifest]  Restore directories from the most recent (or specified) run.
  -profile <name>  Run a saved profile (command-line args still override it).
  -saveprofile <name>  Save the current options as a named profile and exit.
  -listprofiles    List saved profiles and exit.
  -eventlog        Write a summary event to the Windows Application Event Log.
  -classic         Open the legacy Windows Forms interface.
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
