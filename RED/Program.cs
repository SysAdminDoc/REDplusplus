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

		[STAThread]
		private static void Main()
		{
			string[] args = Environment.GetCommandLineArgs();
			string silentPath = null;
			string logFile = null;
			bool isSilent = false;

			for (int i = 1; i < args.Length; i++)
			{
				string arg = args[i].ToLowerInvariant();
				if (arg == "-silent" || arg == "--silent")
				{
					isSilent = true;
				}
				else if ((arg == "-path" || arg == "--path") && i + 1 < args.Length)
				{
					silentPath = args[++i];
				}
				else if ((arg == "-log" || arg == "--log") && i + 1 < args.Length)
				{
					logFile = args[++i];
				}
			}

			if (isSilent && !string.IsNullOrWhiteSpace(silentPath))
			{
				Environment.ExitCode = RunHeadless(silentPath, logFile);
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

		private static int RunHeadless(string targetPath, string logFile)
		{
			var log = new StringBuilder();
			Action<string> logMsg = (msg) =>
			{
				string line = DateTime.Now.ToString("r") + "\t" + msg;
				log.AppendLine(line);
				Console.WriteLine(msg);
			};

			targetPath = Environment.ExpandEnvironmentVariables(targetPath);
			var startDir = new System.IO.DirectoryInfo(targetPath);
			if (!startDir.Exists)
			{
				logMsg("Error: directory does not exist: " + targetPath);
				WriteLogFile(logFile, log);
				return 1;
			}

			RedConfiguration config = null;
			ConfigAssist.ConfigLoad(ref config, "RemoveEmptyDirectories");

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
			runData.DeleteMode = (DeleteModes)config.Options.DeleteMode;
			runData.PauseTime = config.Options.PauseBetweenDeletions;
			runData.HideIgnoredDirectories = config.Options.HideIgnoredDirectories;
			runData.RespectGitIgnore = config.Options.RespectGitIgnore;
			runData.UseMftScan = config.Options.UseMftScan;
			runData.IgnoreFileNameList.Transform(config.Filters.FilesToIgnore);
			runData.IgnoreDirectoryNameList.Transform(config.Filters.DirectoriesToIgnore);
			runData.NeverEmptyDirectoryList.Transform(config.Filters.DirectoriesNeverEmpty);

			logMsg("RED++ silent scan: " + targetPath);

			var core = new REDCore(runData);
			var scanDone = new ManualResetEvent(false);
			int emptyCount = 0;
			int failed = 0;
			bool hadErrors = false;

			core.OnFinishedScanForEmptyDirs += (s, e) =>
			{
				emptyCount = e.EmptyFolderCount;
				scanDone.Set();
			};
			core.OnCancelled += (s, e) => scanDone.Set();
			core.OnAborted += (s, e) => { hadErrors = true; scanDone.Set(); };
			core.OnError += (s, e) => { hadErrors = true; logMsg("Error: " + e.Message); scanDone.Set(); };

			core.SearchingForEmptyDirectories();
			scanDone.WaitOne();

			logMsg(string.Format("Found {0} empty directories", emptyCount));

			if (emptyCount > 0)
			{
				var deleteDone = new ManualResetEvent(false);
				int deleted = 0;

				core.OnDeleteProcessFinished += (s, e) =>
				{
					deleted = e.DeletedFolderCount;
					failed = e.FailedFolderCount;
					deleteDone.Set();
				};
				core.OnDeleteError += (s, e) => deleteDone.Set();
				core.OnCancelled += (s, e) => deleteDone.Set();

				core.StartDeleteProcess();
				deleteDone.WaitOne();

				logMsg(string.Format("Deleted: {0}, Failed: {1}", deleted, failed));
			}

			foreach (RedScanResultItem item in runData.ScanResults)
			{
				logMsg(item.FullPath);
			}

			runData.Dispose();
			WriteLogFile(logFile, log);
			return (hadErrors || failed > 0) ? 1 : 0;
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
