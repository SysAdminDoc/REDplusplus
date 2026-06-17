using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using RED.Helper;
using TXT = RED.RedGetText;

namespace RED
{
	public static class UndoManager
	{
		internal const int DefaultMaxManifests = 5;
		private const string ManifestPrefix = "RED++.undo.";
		private const string ManifestSuffix = ".json";
		private const string LatestManifestName = "RED++.undo.json";

		[DataContract]
		internal class Manifest
		{
			[DataMember(Name = "timestamp")] public string Timestamp { get; set; }
			[DataMember(Name = "deleteMode")] public string DeleteMode { get; set; }
			// The scan root(s) the run cleaned. Restore refuses any entry that is not
			// inside one of these, so a tampered/corrupt manifest cannot redirect a
			// recreate or a move-back to an arbitrary location. Absent in legacy
			// manifests, in which case only the structural checks apply.
			[DataMember(Name = "roots", IsRequired = false)] public List<string> Roots { get; set; }
			[DataMember(Name = "entries")] public List<ManifestEntry> Entries { get; set; }
		}

		[DataContract]
		internal class ManifestEntry
		{
			[DataMember(Name = "path")] public string Path { get; set; }
			[DataMember(Name = "movedTo", IsRequired = false)] public string MovedTo { get; set; }
			[DataMember(Name = "mode", IsRequired = false)] public string Mode { get; set; }
			[DataMember(Name = "isFile", IsRequired = false)] public bool IsFile { get; set; }
		}

		public class ManifestInfo
		{
			public string FilePath { get; set; }
			public string FileName { get; set; }
			public DateTime Timestamp { get; set; }
			public string DeleteMode { get; set; }
			public int EntryCount { get; set; }
		}

		public static string ManifestPath
		{
			get { return RuntimeData.GetTrustedDataFilePath(LatestManifestName); }
		}

		public static bool HasManifest
		{
			get
			{
				try { return ListManifests().Count > 0; }
				catch { return false; }
			}
		}

		public static List<ManifestInfo> ListManifests()
		{
			var result = new List<ManifestInfo>();
			try
			{
				string dir = Path.GetDirectoryName(ManifestPath);
				if (!Directory.Exists(dir)) return result;

				foreach (string file in Directory.GetFiles(dir, ManifestPrefix + "*" + ManifestSuffix))
				{
					string name = Path.GetFileName(file);
					if (name.Equals(LatestManifestName, StringComparison.OrdinalIgnoreCase))
						continue;

					Manifest m = LoadManifestFromPath(file);
					if (m == null || m.Entries == null) continue;

					DateTime ts;
					if (!DateTime.TryParse(m.Timestamp, out ts))
						ts = File.GetLastWriteTime(file);

					result.Add(new ManifestInfo
					{
						FilePath = file,
						FileName = name,
						Timestamp = ts,
						DeleteMode = m.DeleteMode ?? "Unknown",
						EntryCount = m.Entries.Count
					});
				}

				// Also check the single-file latest pointer if no timestamped files exist.
				// Do not auto-load legacy portable manifests from the exe directory: on a
				// shared install that file is attacker-writable by other local users.
				if (result.Count == 0 && File.Exists(ManifestPath))
				{
					Manifest m = LoadManifestFromPath(ManifestPath);
					if (m != null && m.Entries != null && m.Entries.Count > 0)
					{
						DateTime ts;
						if (!DateTime.TryParse(m.Timestamp, out ts))
							ts = File.GetLastWriteTime(ManifestPath);

						result.Add(new ManifestInfo
						{
							FilePath = ManifestPath,
							FileName = LatestManifestName,
							Timestamp = ts,
							DeleteMode = m.DeleteMode ?? "Unknown",
							EntryCount = m.Entries.Count
						});
					}
				}

				result.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
			}
			catch { }
			return result;
		}

		public static bool Restore(out int restored, out int failed, Action<string> log)
		{
			var manifests = ListManifests();
			if (manifests.Count == 0)
			{
				restored = 0;
				failed = 0;
				log?.Invoke(TXT.Translate("No undo manifest found - nothing to restore"));
				return false;
			}
			return Restore(manifests[0].FilePath, out restored, out failed, log);
		}

		public static bool Restore(string manifestPath, out int restored, out int failed, Action<string> log)
		{
			restored = 0;
			failed = 0;

			Manifest manifest = LoadManifestFromPath(manifestPath);
			if (manifest == null || manifest.Entries == null || manifest.Entries.Count == 0)
			{
				log?.Invoke(TXT.Translate("No undo manifest found - nothing to restore"));
				return false;
			}

			bool trustedManifest = IsTrustedManifestPath(manifestPath);
			if (!trustedManifest && !IsExplicitLegacyManifestSafe(manifest, log))
			{
				return false;
			}

			manifest.Entries.Sort((a, b) => (a.Path ?? "").Length.CompareTo((b.Path ?? "").Length));

			foreach (ManifestEntry entry in manifest.Entries)
			{
				if (string.IsNullOrWhiteSpace(entry.Path))
				{
					continue;
				}

				// A restore recreates directories and moves payloads back to paths read
				// verbatim from the manifest. Refuse anything that is not a fully-qualified
				// path free of ".." and (when the manifest records its roots) inside the
				// originally-cleaned tree, so a tampered or corrupt manifest cannot be
				// turned into an arbitrary create/move primitive.
				if (!IsRestoreTargetSafe(entry.Path, manifest.Roots))
				{
					failed++;
					log?.Invoke(TXT.Translate("Refused to restore unsafe or out-of-tree path: {0}", RedAssist.DQuote(entry.Path)));
					continue;
				}
				// The move-back SOURCE is constrained to the recorded roots too (the scan
				// root and the move-to folder), so a tampered manifest cannot point it at
				// an arbitrary file/dir and have Move relocate (destroy) it from its origin.
				if (!string.IsNullOrWhiteSpace(entry.MovedTo) && !IsRestoreTargetSafe(entry.MovedTo, manifest.Roots))
				{
					failed++;
					log?.Invoke(TXT.Translate("Refused to restore from unsafe move source: {0}", RedAssist.DQuote(entry.MovedTo)));
					continue;
				}

				try
				{
					if (entry.IsFile)
					{
						string parent = Path.GetDirectoryName(entry.Path);
						if (!string.IsNullOrEmpty(parent))
						{
							Directory.CreateDirectory(parent);
						}

						if (!string.IsNullOrWhiteSpace(entry.MovedTo) && File.Exists(entry.MovedTo))
						{
							if (File.Exists(entry.Path))
							{
								FileInfo existing = new FileInfo(entry.Path);
								if (existing.Length != 0)
								{
									throw new IOException(TXT.Translate("Original file exists and is no longer empty: {0}", RedAssist.DQuote(entry.Path)));
								}
								File.Delete(entry.Path);
							}
							try
							{
								File.Move(entry.MovedTo, entry.Path);
							}
							catch (IOException)
							{
								File.Copy(entry.MovedTo, entry.Path, overwrite: false);
								File.Delete(entry.MovedTo);
							}
						}
						else if (!File.Exists(entry.Path))
						{
							using (File.Create(entry.Path)) { }
						}
						restored++;
						log?.Invoke(TXT.Translate("Restored empty file: {0}", RedAssist.DQuote(entry.Path)));
						continue;
					}

					if (!string.IsNullOrWhiteSpace(entry.MovedTo) && Directory.Exists(entry.MovedTo))
					{
						string parent = Path.GetDirectoryName(entry.Path);
						if (!string.IsNullOrEmpty(parent))
						{
							Directory.CreateDirectory(parent);
						}
						if (Directory.Exists(entry.Path))
						{
							Directory.Delete(entry.Path, false);
						}
						Directory.Move(entry.MovedTo, entry.Path);
					}
					else
					{
						Directory.CreateDirectory(entry.Path);
					}

					restored++;
					log?.Invoke(TXT.Translate("Restored directory: {0}", RedAssist.DQuote(entry.Path)));
				}
				catch (Exception ex)
				{
					failed++;
					string kind = entry.IsFile ? TXT.Translate("file") : TXT.Translate("directory");
					log?.Invoke(TXT.Translate("Failed to restore {0}: {1} - {2}", kind, RedAssist.DQuote(entry.Path), ex.Message));
				}
			}

			if (failed == 0)
			{
				if (trustedManifest)
				{
					string restoredContent = null;
					try { restoredContent = File.ReadAllText(manifestPath, Encoding.UTF8).Trim(); }
					catch { }
					try { File.Delete(manifestPath); }
					catch { }
					// Also remove the latest-pointer if it pointed here
					try
					{
						if (!manifestPath.Equals(ManifestPath, StringComparison.OrdinalIgnoreCase)
							&& File.Exists(ManifestPath))
						{
							string latestContent = File.ReadAllText(ManifestPath, Encoding.UTF8).Trim();
							// The latest-pointer mirrors the most recent run. If it still
							// holds the run we just restored, delete it too so a second
							// restore does not re-create the directories we put back.
							if (restoredContent != null && string.Equals(latestContent, restoredContent, StringComparison.Ordinal))
							{
								File.Delete(ManifestPath);
							}
						}
					}
					catch { }
				}
			}

			return failed == 0 && restored > 0;
		}

		internal static Manifest LoadManifest()
		{
			return LoadManifestFromPath(ManifestPath);
		}

		internal static Manifest LoadManifestFromPath(string path)
		{
			try
			{
				if (!File.Exists(path))
				{
					return null;
				}

				string json = File.ReadAllText(path, Encoding.UTF8);
				using (var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(json)))
				{
					var serializer = new DataContractJsonSerializer(typeof(Manifest));
					return (Manifest)serializer.ReadObject(stream);
				}
			}
			catch
			{
				return null;
			}
		}

		internal static void WriteManifest(string deleteMode, IList<ManifestEntry> entries, Action<string> log)
		{
			WriteManifest(deleteMode, entries, null, log);
		}

		internal static void WriteManifest(string deleteMode, IList<ManifestEntry> entries, IList<string> roots, Action<string> log)
		{
			if (entries == null || entries.Count == 0) return;
			try
			{
				string dir = Path.GetDirectoryName(ManifestPath);
				string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
				string timestampedName = ManifestPrefix + timestamp + ManifestSuffix;
				string timestampedPath = Path.Combine(dir, timestampedName);

				string json = BuildManifestJson(deleteMode, entries, roots);

				// Write atomically: a crash or power loss mid-write must never leave
				// a truncated manifest, because it is the only recovery path for a
				// deletion run. Stage to a temp file, then move into place.
				AtomicWrite(timestampedPath, json);
				log?.Invoke(TXT.Translate("Undo manifest written: {0}", RedAssist.DQuote(timestampedPath)));

				// Also write the latest-pointer for backward compatibility
				try { AtomicWrite(ManifestPath, json); }
				catch { }

				RotateManifests(DefaultMaxManifests, log);
			}
			catch { }
		}

		private static string BuildManifestJson(string deleteMode, IList<ManifestEntry> entries, IList<string> roots)
		{
			var sb = new StringBuilder();
			sb.AppendLine("{");
			sb.AppendLine("  \"timestamp\": \"" + DateTime.Now.ToString("o") + "\",");
			sb.AppendLine("  \"deleteMode\": \"" + EscapeJson(deleteMode) + "\",");
			if (roots != null && roots.Count > 0)
			{
				sb.Append("  \"roots\": [");
				bool firstRoot = true;
				foreach (string r in roots)
				{
					if (string.IsNullOrWhiteSpace(r)) continue;
					if (!firstRoot) sb.Append(", ");
					sb.Append("\"" + EscapeJson(r) + "\"");
					firstRoot = false;
				}
				sb.AppendLine("],");
			}
			sb.AppendLine("  \"entries\": [");
			for (int i = 0; i < entries.Count; i++)
			{
				var entry = entries[i];
				sb.Append("    { \"path\": \"" + EscapeJson(entry.Path) + "\"");
				if (entry.MovedTo != null)
					sb.Append(", \"movedTo\": \"" + EscapeJson(entry.MovedTo) + "\"");
				sb.Append(", \"mode\": \"" + EscapeJson(entry.Mode) + "\"");
				if (entry.IsFile)
					sb.Append(", \"isFile\": true");
				sb.Append(" }");
				if (i < entries.Count - 1) sb.Append(",");
				sb.AppendLine();
			}
			sb.AppendLine("  ]");
			sb.AppendLine("}");
			return sb.ToString();
		}

		private static void RotateManifests(int maxCount, Action<string> log)
		{
			try
			{
				string dir = Path.GetDirectoryName(ManifestPath);
				var files = Directory.GetFiles(dir, ManifestPrefix + "*" + ManifestSuffix)
					.Where(f => !Path.GetFileName(f).Equals(LatestManifestName, StringComparison.OrdinalIgnoreCase))
					.OrderByDescending(f => f)
					.ToList();

				for (int i = maxCount; i < files.Count; i++)
				{
					try
					{
						File.Delete(files[i]);
						log?.Invoke(TXT.Translate("Rotated old undo manifest: {0}", Path.GetFileName(files[i])));
					}
					catch { }
				}
			}
			catch { }
		}

		private static void AtomicWrite(string path, string contents)
		{
			string tmp = path + ".tmp";
			File.WriteAllText(tmp, contents, new UTF8Encoding(false));
			if (File.Exists(path))
			{
				// File.Replace is atomic on NTFS and preserves nothing we need.
				File.Replace(tmp, path, null);
			}
			else
			{
				File.Move(tmp, path);
			}
		}

		private static bool IsTrustedManifestPath(string manifestPath)
		{
			if (string.IsNullOrWhiteSpace(manifestPath)) return false;
			try
			{
				string full = Path.GetFullPath(manifestPath);
				string trustedDir = Path.GetFullPath(RuntimeData.GetTrustedDataDirectory());
				return IsUnderDirectory(full, trustedDir);
			}
			catch { return false; }
		}

		private static bool IsExplicitLegacyManifestSafe(Manifest manifest, Action<string> log)
		{
			foreach (ManifestEntry entry in manifest.Entries)
			{
				if (entry == null) continue;

				if (!IsExplicitLegacyPathAllowed(entry.Path))
				{
					log?.Invoke(TXT.Translate("Refused to restore an explicit manifest outside the current user's safe profile boundary: {0}", RedAssist.DQuote(entry.Path)));
					return false;
				}

				if (!string.IsNullOrWhiteSpace(entry.MovedTo) && !IsExplicitLegacyPathAllowed(entry.MovedTo))
				{
					log?.Invoke(TXT.Translate("Refused to restore an explicit manifest whose move source is outside the current user's safe profile boundary: {0}", RedAssist.DQuote(entry.MovedTo)));
					return false;
				}
			}
			return true;
		}

		private static bool IsExplicitLegacyPathAllowed(string path)
		{
			if (!IsPathStructurallySafe(path)) return false;

			string full;
			try { full = Path.GetFullPath(path); }
			catch { return false; }

			if (IsUnderSystemDirectory(full)) return false;
			if (IsUnderSensitiveUserDirectory(full)) return false;

			string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			if (string.IsNullOrWhiteSpace(profile)) return false;
			return IsUnderDirectory(full, profile);
		}

		/// <summary>
		/// A restore target must be a fully-qualified path with no ".." segment (before
		/// or after normalization). Rejects relative paths, device paths, and traversal.
		/// </summary>
		private static bool IsPathStructurallySafe(string path)
		{
			if (string.IsNullOrWhiteSpace(path)) return false;
			if (!Path.IsPathFullyQualified(path)) return false;
			if (HasDotDotSegment(path)) return false;
			// Reject Win32 device/namespace prefixes (\\?\, \\.\): RED++ never writes
			// them to a manifest, and they bypass path normalization (MAX_PATH, trailing
			// dot/space stripping), so a tampered manifest could otherwise create on-disk
			// names the normal API would refuse.
			string t = path.TrimStart();
			if (t.StartsWith(@"\\?\", StringComparison.Ordinal) || t.StartsWith(@"\\.\", StringComparison.Ordinal)) return false;
			string full;
			try { full = Path.GetFullPath(path); }
			catch { return false; }
			return !HasDotDotSegment(full);
		}

		/// <summary>
		/// Structurally safe AND, when the manifest records its scan roots, inside one
		/// of them. A manifest with no roots (legacy v1.5.18, or a tamperer who stripped
		/// the field) passes the structural checks but is additionally refused from
		/// well-known system locations, so it cannot be used to create/move into
		/// C:\Windows, System32, or Program Files.
		/// </summary>
		private static bool IsRestoreTargetSafe(string path, IList<string> roots)
		{
			if (!IsPathStructurallySafe(path)) return false;

			string full;
			try { full = Path.GetFullPath(path); }
			catch { return false; }

			if (roots == null || roots.Count == 0)
			{
				return !IsUnderSystemDirectory(full);
			}

			foreach (string r in roots)
			{
				if (string.IsNullOrWhiteSpace(r)) continue;
				string rootFull;
				try { rootFull = Path.GetFullPath(r); }
				catch { continue; }

				if (full.Equals(rootFull, StringComparison.OrdinalIgnoreCase)) return true;
				string prefix = rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
				if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
			}
			return false;
		}

		private static bool IsUnderSystemDirectory(string fullPath)
		{
			foreach (Environment.SpecialFolder f in new[]
			{
				Environment.SpecialFolder.Windows,
				Environment.SpecialFolder.System,
				Environment.SpecialFolder.SystemX86,
				Environment.SpecialFolder.ProgramFiles,
				Environment.SpecialFolder.ProgramFilesX86,
			})
			{
				string sys;
				try { sys = Environment.GetFolderPath(f); }
				catch { continue; }
				if (string.IsNullOrEmpty(sys)) continue;

				if (fullPath.Equals(sys, StringComparison.OrdinalIgnoreCase)) return true;
				string prefix = sys.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
				if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
			}
			return false;
		}

		private static bool IsUnderSensitiveUserDirectory(string fullPath)
		{
			foreach (Environment.SpecialFolder f in new[]
			{
				Environment.SpecialFolder.Startup,
				Environment.SpecialFolder.CommonStartup,
			})
			{
				string dir;
				try { dir = Environment.GetFolderPath(f); }
				catch { continue; }
				if (string.IsNullOrWhiteSpace(dir)) continue;
				if (IsUnderDirectory(fullPath, dir)) return true;
			}
			return false;
		}

		private static bool IsUnderDirectory(string fullPath, string directory)
		{
			if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(directory)) return false;

			string full = Path.GetFullPath(fullPath);
			string dir = Path.GetFullPath(directory);
			if (full.Equals(dir, StringComparison.OrdinalIgnoreCase)) return true;

			string prefix = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
		}

		private static bool HasDotDotSegment(string path)
		{
			foreach (string seg in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
			{
				if (seg == "..") return true;
			}
			return false;
		}

		private static string EscapeJson(string s)
		{
			if (s == null) return string.Empty;
			var sb = new StringBuilder(s.Length + 8);
			foreach (char c in s)
			{
				switch (c)
				{
					case '\\': sb.Append("\\\\"); break;
					case '\"': sb.Append("\\\""); break;
					case '\b': sb.Append("\\b"); break;
					case '\f': sb.Append("\\f"); break;
					case '\n': sb.Append("\\n"); break;
					case '\r': sb.Append("\\r"); break;
					case '\t': sb.Append("\\t"); break;
					default:
						if (c < 0x20)
							sb.Append("\\u").Append(((int)c).ToString("x4"));
						else
							sb.Append(c);
						break;
				}
			}
			return sb.ToString();
		}
	}
}
