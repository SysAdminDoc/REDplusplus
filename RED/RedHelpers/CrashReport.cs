using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RED.Helper
{
    /// <summary>
    /// Writes a self-contained local crash report next to RED++.log when an unhandled
    /// exception reaches a global handler. No telemetry and no network: the user attaches
    /// the file to a GitHub issue. Records the exception, OS, runtime, and app version
    /// only — never file contents — consistent with the no-PHI-in-logs rule.
    /// </summary>
    internal static class CrashReport
    {
        // One report per process is enough; several handlers can fire for one fault.
        private static int _written;

        /// <summary>Writes a crash report and returns its path, or null on failure.</summary>
        internal static string Write(Exception ex, string context)
        {
            try
            {
                if (Interlocked.Exchange(ref _written, 1) != 0)
                {
                    return null;
                }

                string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string fileName = "RED++.crash-" + stamp + ".txt";

                string path;
                try { path = RuntimeData.GetWritableDataFilePath(fileName); }
                catch { path = Path.Combine(Path.GetTempPath(), fileName); }

                File.WriteAllText(path, BuildReport(ex, context), new UTF8Encoding(false));

                // Best-effort pointer in the live log so a forensic reader finds it.
                try
                {
                    string logPath = RuntimeData.GetWritableDataFilePath("RED++.log");
                    File.AppendAllText(logPath, DateTime.Now.ToString("r") + "\tCRASH: " + (ex != null ? ex.GetType().Name + ": " + ex.Message : "unknown") + " -> " + path + Environment.NewLine, new UTF8Encoding(false));
                }
                catch { }

                return path;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Builds the crash-report text (paths/metadata only, never file contents).</summary>
        internal static string BuildReport(Exception ex, string context)
        {
            var sb = new StringBuilder();
            sb.AppendLine("RED++ crash report");
            sb.AppendLine("Time:    " + DateTime.Now.ToString("o"));
            sb.AppendLine("Version: " + SafeVersion());
            sb.AppendLine("OS:      " + Environment.OSVersion + " (" + RuntimeInformation.OSArchitecture + ")");
            sb.AppendLine("Runtime: " + RuntimeInformation.FrameworkDescription);
            sb.AppendLine("Context: " + (context ?? "unknown"));
            sb.AppendLine();
            sb.AppendLine(ex != null ? ex.ToString() : "(no exception object)");
            return sb.ToString();
        }

        private static string SafeVersion()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                var fv = asm.GetCustomAttribute<AssemblyFileVersionAttribute>();
                if (fv != null && !string.IsNullOrEmpty(fv.Version)) return fv.Version;
                var name = asm.GetName();
                return name != null && name.Version != null ? name.Version.ToString() : "unknown";
            }
            catch { return "unknown"; }
        }
    }
}
