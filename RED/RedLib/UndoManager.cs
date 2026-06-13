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
			get { return RuntimeData.GetWritableDataFilePath(LatestManifestName); }
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

				// Also check the legacy single-file manifest if no timestamped files exist
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

			manifest.Entries.Sort((a, b) => (a.Path ?? "").Length.CompareTo((b.Path ?? "").Length));

			foreach (ManifestEntry entry in manifest.Entries)
			{
				if (string.IsNullOrWhiteSpace(entry.Path))
				{
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
				try { File.Delete(manifestPath); }
				catch { }
				// Also remove the latest-symlink if it pointed here
				try
				{
					if (!manifestPath.Equals(ManifestPath, StringComparison.OrdinalIgnoreCase)
						&& File.Exists(ManifestPath))
					{
						string latestContent = File.ReadAllText(ManifestPath, Encoding.UTF8).Trim();
						string thisContent = File.ReadAllText(manifestPath, Encoding.UTF8).Trim();
						// If latest is identical to this one, clean it up too
					}
				}
				catch { }
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
			if (entries == null || entries.Count == 0) return;
			try
			{
				string dir = Path.GetDirectoryName(ManifestPath);
				string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
				string timestampedName = ManifestPrefix + timestamp + ManifestSuffix;
				string timestampedPath = Path.Combine(dir, timestampedName);

				string json = BuildManifestJson(deleteMode, entries);

				File.WriteAllText(timestampedPath, json, Encoding.UTF8);
				log?.Invoke(TXT.Translate("Undo manifest written: {0}", RedAssist.DQuote(timestampedPath)));

				// Also write the latest-pointer for backward compatibility
				try { File.WriteAllText(ManifestPath, json, Encoding.UTF8); }
				catch { }

				RotateManifests(DefaultMaxManifests, log);
			}
			catch { }
		}

		private static string BuildManifestJson(string deleteMode, IList<ManifestEntry> entries)
		{
			var sb = new StringBuilder();
			sb.AppendLine("{");
			sb.AppendLine("  \"timestamp\": \"" + DateTime.Now.ToString("o") + "\",");
			sb.AppendLine("  \"deleteMode\": \"" + EscapeJson(deleteMode) + "\",");
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

		private static string EscapeJson(string s)
		{
			if (s == null) return string.Empty;
			return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}
	}
}
