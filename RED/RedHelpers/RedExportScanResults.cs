using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using RED.Match;
using TXT = RED.RedGetText;

namespace RED.Helper
{
	class RedExportScanResults : IDisposable
    {
        public RedExportScanResults() { }


        public void Dispose() { }

        public void ExportToFile(RedScanResultItemList v)
        {
            Export(v, toClipboard: false);
        }

        public void ExportToCliboard(RedScanResultItemList v)
        {
            Export(v, toClipboard: true);
        }

        public void Export(RedScanResultItemList v, bool toClipboard)
        {
            try
            {
                if (v != null && v.Count > 0)
                {
                    if (toClipboard)
                    {
                        List<string> exportData = GetExportText(v);
                        Clipboard.SetText(string.Join(Environment.NewLine, exportData.ToArray()), TextDataFormat.UnicodeText);
                    }
                    else
                    {
                        ExportScanResultsToFile(v);
                    }
                }
                else
                {
                    UiAssist.MsgBoxInfo(TXT.Translate("There are no scan results to export. Scan or import results first."));
                }
            }
            catch (Exception ex)
            {
                UiAssist.MsgBoxException(TXT.Translate("Could not export scan results"), ex);
            }
        }

        private void ExportScanResultsToFile(RedScanResultItemList v)
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = TXT.Translate("Export scan results");
                dlg.Filter = TXT.Translate("Text files|*.txt|CSV files|*.csv|JSON files|*.json|PowerShell removal script|*.ps1|HTML report|*.html|All files|*.*");
                dlg.FilterIndex = 1;
                dlg.DefaultExt = "txt";
                dlg.FileName = "RED++_EmptyDirectories";
                dlg.OverwritePrompt = true;

                if (dlg.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.FileName))
                    return;

                string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                if (ext == ".csv")
                {
                    WriteCsv(v, dlg.FileName);
                }
                else if (ext == ".json")
                {
                    WriteJson(v, dlg.FileName);
                }
                else if (ext == ".ps1")
                {
                    WritePowerShellScript(v, dlg.FileName);
                }
                else if (ext == ".html" || ext == ".htm")
                {
                    WriteHtmlReport(v, dlg.FileName);
                }
                else
                {
                    File.WriteAllLines(dlg.FileName, GetExportText(v), Encoding.UTF8);
                }
            }
        }

        /// <summary>Public entry for the headless CLI -export path.</summary>
        public void WriteCsvFile(RedScanResultItemList v, string filename)
        {
            WriteCsv(v, filename);
        }

        /// <summary>Public entry for the headless CLI -export path.</summary>
        public void WriteJsonFile(RedScanResultItemList v, string filename)
        {
            WriteJson(v, filename);
        }

        /// <summary>Public entry for the headless CLI -export path.</summary>
        public void WritePs1File(RedScanResultItemList v, string filename)
        {
            WritePowerShellScript(v, filename);
        }

        /// <summary>Public entry for the headless CLI -export path.</summary>
        public void WriteHtmlFile(RedScanResultItemList v, string filename)
        {
            WriteHtmlReport(v, filename);
        }

        private void WriteCsv(RedScanResultItemList v, string filename)
        {
            var lines = new List<string> { "\"Kind\",\"Path\",\"Status\",\"Reason\"" };
            for (int i = 0; i < v.Count; i++)
            {
                string kind = v[i].Kind == Match.ResultKind.File ? "file" : "directory";
                lines.Add(string.Format("\"{0}\",\"{1}\",\"{2}\",\"{3}\"",
                    kind,
                    EscapeCsvCell(v[i].FullPath),
                    v[i].SearchStatus,
                    EscapeCsvCell(v[i].StatusReason)));
            }
            File.WriteAllLines(filename, lines, Encoding.UTF8);
        }

        /// <summary>
        /// Quotes a value for CSV and neutralizes spreadsheet formula injection
        /// (CWE-1236): a directory named e.g. =cmd|'/c calc'!A1 would otherwise
        /// execute when the export is opened in Excel/LibreOffice. Directory names
        /// are fully attacker-controllable, so any cell starting with =, +, -, @,
        /// tab or CR is prefixed with a single quote.
        /// </summary>
        internal static string EscapeCsvCell(string value)
        {
            value = value ?? string.Empty;
            if (value.Length > 0)
            {
                char c0 = value[0];
                // Some spreadsheets ignore leading whitespace and still evaluate a
                // formula, so test the first non-whitespace character too — a cell like
                // " =1+1" would otherwise slip past a first-character-only check.
                string trimmed = value.TrimStart();
                char cTrim = trimmed.Length > 0 ? trimmed[0] : '\0';
                if (IsFormulaTrigger(c0) || c0 == '\t' || c0 == '\r' || IsFormulaTrigger(cTrim))
                {
                    value = "'" + value;
                }
            }
            return value.Replace("\"", "\"\"");
        }

        private static bool IsFormulaTrigger(char c)
        {
            return c == '=' || c == '+' || c == '-' || c == '@';
        }

        private void WriteJson(RedScanResultItemList v, string filename)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[");
            for (int i = 0; i < v.Count; i++)
            {
                string kind = v[i].Kind == Match.ResultKind.File ? "file" : "directory";
                string escapedPath = EscapeJson(v[i].FullPath);
                string escapedReason = EscapeJson(v[i].StatusReason);
                sb.AppendFormat("  {{ \"kind\": \"{0}\", \"path\": \"{1}\", \"status\": \"{2}\", \"reason\": \"{3}\", \"ignoredFileCount\": {4} }}", kind, escapedPath, v[i].SearchStatus, escapedReason, v[i].IgnoredFileCount);
                if (i < v.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("]");
            File.WriteAllText(filename, sb.ToString(), Encoding.UTF8);
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

        /// <summary>
        /// Emits a reviewable PowerShell removal script (rmlint-style): the eligible
        /// directories as a list, a fail-safe `$Execute = $false` default, and a
        /// per-directory re-check that it is still file-free immediately before removal
        /// (Recycle Bin by default). The decision (this list) is decoupled from the
        /// action (running the script), which suits change-controlled / scheduled use.
        /// </summary>
        private void WritePowerShellScript(RedScanResultItemList v, string filename)
        {
            var dirs = new List<string>();
            for (int i = 0; i < v.Count; i++)
            {
                if (v[i].Kind == Match.ResultKind.Directory && v[i].SearchStatus == DirectorySearchStatusTypes.Empty)
                {
                    dirs.Add(v[i].FullPath);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("# RED++ removal script - review before running.");
            sb.AppendLine("# Each directory below was reported EMPTY by a RED++ dry-run.");
            sb.AppendLine("# SAFETY: nothing is removed until you set $Execute = $true, and every");
            sb.AppendLine("#   directory is re-checked to still contain no files immediately before removal.");
            sb.AppendLine("# Generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " - " + dirs.Count + (dirs.Count == 1 ? " eligible directory." : " eligible directories."));
            sb.AppendLine();
            sb.AppendLine("$Execute = $false   # set $true to actually remove the directories");
            sb.AppendLine("$Recycle = $true    # $true = Recycle Bin (recoverable); $false = permanent delete");
            sb.AppendLine();
            sb.AppendLine("$targets = @(");
            for (int i = 0; i < dirs.Count; i++)
            {
                sb.AppendLine("  '" + dirs[i].Replace("'", "''") + "'" + (i < dirs.Count - 1 ? "," : ""));
            }
            sb.AppendLine(")");
            sb.AppendLine();
            sb.AppendLine("Add-Type -AssemblyName Microsoft.VisualBasic");
            sb.AppendLine("$removed = 0; $skipped = 0");
            sb.AppendLine("foreach ($t in $targets) {");
            sb.AppendLine("  if (-not (Test-Path -LiteralPath $t)) { Write-Host \"skip (already gone): $t\"; $skipped++; continue }");
            sb.AppendLine("  $files = @(Get-ChildItem -LiteralPath $t -Recurse -Force -File -ErrorAction SilentlyContinue)");
            sb.AppendLine("  if ($files.Count -gt 0) { Write-Warning \"skip (not empty - $($files.Count) file(s)): $t\"; $skipped++; continue }");
            sb.AppendLine("  if (-not $Execute) { Write-Host \"would remove: $t\"; continue }");
            sb.AppendLine("  try {");
            sb.AppendLine("    if ($Recycle) { [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($t, 'OnlyErrorDialogs', 'SendToRecycleBin') }");
            sb.AppendLine("    else { Remove-Item -LiteralPath $t -Recurse -Force }");
            sb.AppendLine("    Write-Host \"removed: $t\"; $removed++");
            sb.AppendLine("  } catch { Write-Warning \"failed: $t -- $($_.Exception.Message)\"; $skipped++ }");
            sb.AppendLine("}");
            sb.AppendLine("Write-Host \"RED++ removal: removed=$removed skipped=$skipped (Execute=$Execute, Recycle=$Recycle)\"");

            // UTF-8 with BOM so Windows PowerShell 5.1 reads non-ASCII paths correctly.
            File.WriteAllText(filename, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>
        /// Emits a single self-contained HTML audit report (no external assets) of the
        /// full result set with run metadata and each row's status reason.
        /// </summary>
        private void WriteHtmlReport(RedScanResultItemList v, string filename)
        {
            int eligible = 0, files = 0, dirs = 0;
            for (int i = 0; i < v.Count; i++)
            {
                if (v[i].Kind == Match.ResultKind.File) files++; else dirs++;
                if (v[i].SearchStatus == DirectorySearchStatusTypes.Empty) eligible++;
            }

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.AppendLine("<title>RED++ scan report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{background:#1e1e2e;color:#cdd6f4;font:14px/1.5 'Segoe UI',system-ui,sans-serif;margin:24px}");
            sb.AppendLine("h1{color:#f38ba8;margin:0 0 4px} .meta{color:#a6adc8;margin:0 0 16px}");
            sb.AppendLine("table{border-collapse:collapse;width:100%}");
            sb.AppendLine("th,td{text-align:left;padding:6px 10px;border-bottom:1px solid #313244;vertical-align:top}");
            sb.AppendLine("th{color:#89b4fa;position:sticky;top:0;background:#181825}");
            sb.AppendLine("td.path{font-family:Consolas,monospace;word-break:break-all}");
            sb.AppendLine(".s-Empty{color:#f38ba8} .s-Ignore,.s-NeverEmpty{color:#a6adc8} .s-Error{color:#fab387}");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>RED++ scan report</h1>");
            sb.AppendLine("<p class=\"meta\">Generated " + HtmlEscape(DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
                + " &middot; " + v.Count + (v.Count == 1 ? " result" : " results")
                + " (" + dirs + (dirs == 1 ? " directory, " : " directories, ") + files + (files == 1 ? " file, " : " files, ")
                + eligible + " eligible for deletion).</p>");
            sb.AppendLine("<table><thead><tr><th>Kind</th><th>Path</th><th>Status</th><th>Reason</th></tr></thead><tbody>");
            for (int i = 0; i < v.Count; i++)
            {
                string kind = v[i].Kind == Match.ResultKind.File ? "file" : "directory";
                string status = v[i].SearchStatus.ToString();
                sb.AppendLine("<tr><td>" + kind + "</td><td class=\"path\">" + HtmlEscape(v[i].FullPath)
                    + "</td><td class=\"s-" + HtmlEscape(status) + "\">" + HtmlEscape(status)
                    + "</td><td>" + HtmlEscape(v[i].StatusReason) + "</td></tr>");
            }
            sb.AppendLine("</tbody></table></body></html>");

            File.WriteAllText(filename, sb.ToString(), new UTF8Encoding(false));
        }

        private static string HtmlEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
        }

        private List<string> GetExportText(RedScanResultItemList v)
        {
            List<string> respx = new List<string>();
            for (int i = 0; i < v.Count; i++)
            {
                respx.Add(v[i].FullPath);
            }
            return respx;
        }

        public void ExportToFile(List<string> v)
        {
            Export(v, toClipboard: false);
        }

        public void ExportToClipboard(List<string> v)
        {
            Export(v, toClipboard: true);
        }

        private void Export(List<string> v, bool toClipboard)
        {
            try
            {
                if (v != null && v.Count > 0)
                {
                    v.Sort();
                    if (toClipboard)
                    {
                        Clipboard.SetText(string.Join(Environment.NewLine, v.ToArray()), TextDataFormat.UnicodeText);
                    }
                    else
                    {
                        string filename = RedAssist.BrowseForSaveAsFilename("RED_EmptyDirectoryList.txt");
                        if (!string.IsNullOrWhiteSpace(filename))
                        {
                            File.WriteAllLines(filename, v);
                        }
                    }
                }
                else
                {
                    UiAssist.MsgBoxInfo(TXT.Translate("There are no directories to export. Scan or import results first."));
                }
            }
            catch (Exception ex)
            {
                UiAssist.MsgBoxException(TXT.Translate("Could not export directories"), ex);
            }
        }

        public void ExportToFile(TreeView tv)
        {
            Export(tv, toClipboard: false);
        }

        public void ExportToClipboard(TreeView tv)
        {
            Export(tv, toClipboard: true);
        }

        private void Export(TreeView tv, bool toClipboard)
        {
            try
            {
                List<string> exportData = BuildExportData(tv);
                if (exportData.Count > 0)
                {
                    if (toClipboard)
                    {
                        Clipboard.SetText(string.Join(Environment.NewLine, exportData.ToArray()), TextDataFormat.UnicodeText);
                    }
                    else
                    {
                        string filename = RedAssist.BrowseForSaveAsFilename("RED_EmptyDirectoryList+.txt");
                        if (!string.IsNullOrWhiteSpace(filename))
                        {
                            File.WriteAllLines(filename, exportData, Encoding.UTF8);
                        }
                    }
                }
                else
                {
                    UiAssist.MsgBoxInfo(TXT.Translate("There are no scan results to export. Scan or import results first."));
                }
            }
            catch (Exception ex)
            {
                UiAssist.MsgBoxException(TXT.Translate("Could not export tree view results"), ex);
            }
        }

        private List<string> BuildExportData(TreeView tv)
        {
            List<string> respx = new List<string>();
            if (tv != null && tv.Nodes.Count > 0)
            {
                WalkTreeviewNodes(tv.Nodes, respx);
            }
            return respx;
        }

        private void WalkTreeviewNodes(TreeNodeCollection nodes, List<string> exportData)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                TreeNode node = nodes[i];
                string npath = GetFullPathText(node);
                if (!string.IsNullOrWhiteSpace(npath))
                {
                    exportData.Add(npath);
                }
                if (node.Nodes.Count > 0)
                {
                    WalkTreeviewNodes(node.Nodes, exportData);
                }
            }
        }

        private string GetFullPathText(TreeNode node)
        {
            string npath = string.Empty;
            // Eligibility is decided from the status icon, not the (theme-dependent)
            // ForeColor: a restyle or theme refresh must never silently drop eligible
            // nodes from the exported deletion list or include kept ones.
            if (node != null && TreeManager.IsEligibleImageKey(node.ImageKey))
            {
                TreeNode pnode = node;
                do
                {
                    npath = pnode.Text + Path.DirectorySeparatorChar + npath;
                    pnode = pnode.Parent;
                } while (pnode != null);
            }
            return npath;
        }
    }
}
