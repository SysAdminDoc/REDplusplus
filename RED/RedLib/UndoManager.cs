using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using RED.Helper;
using TXT = RED.RedGetText;

namespace RED
{
	/// <summary>
	/// Reads RED++.undo.json (written by DeletionWorker after every run) and
	/// restores the deleted directories. Deleted directories were empty by
	/// definition, so recreating the path is a lossless restore for Recycle Bin
	/// and Direct modes; MoveToFolder entries are moved back from their recorded
	/// destination.
	/// </summary>
	public static class UndoManager
	{
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
		}

		public static string ManifestPath
		{
			get { return RuntimeData.GetWritableDataFilePath("RED++.undo.json"); }
		}

		public static bool HasManifest
		{
			get
			{
				try { return File.Exists(ManifestPath); }
				catch { return false; }
			}
		}

		/// <summary>
		/// Restores every entry in the manifest. Returns true when all entries were
		/// restored; the manifest file is consumed (deleted) only on full success so
		/// a partial restore can be retried.
		/// </summary>
		public static bool Restore(out int restored, out int failed, Action<string> log)
		{
			restored = 0;
			failed = 0;

			Manifest manifest = LoadManifest();
			if (manifest == null || manifest.Entries == null || manifest.Entries.Count == 0)
			{
				log?.Invoke(TXT.Translate("No undo manifest found - nothing to restore"));
				return false;
			}

			// Parents before children so Directory.Move targets exist; mkdir does
			// this implicitly but moved subtrees need their parent first
			manifest.Entries.Sort((a, b) => (a.Path ?? "").Length.CompareTo((b.Path ?? "").Length));

			foreach (ManifestEntry entry in manifest.Entries)
			{
				if (string.IsNullOrWhiteSpace(entry.Path))
				{
					continue;
				}

				try
				{
					if (!string.IsNullOrWhiteSpace(entry.MovedTo) && Directory.Exists(entry.MovedTo))
					{
						string parent = Path.GetDirectoryName(entry.Path);
						if (!string.IsNullOrEmpty(parent))
						{
							Directory.CreateDirectory(parent);
						}
						if (Directory.Exists(entry.Path))
						{
							// Original re-appeared (e.g. parent already restored as empty
							// dir, or app recreated it) — the moved copy wins only if the
							// original is empty
							Directory.Delete(entry.Path, false);
						}
						Directory.Move(entry.MovedTo, entry.Path);
					}
					else
					{
						// The directory was empty when deleted - recreating it (and any
						// missing parents) is a complete restore
						Directory.CreateDirectory(entry.Path);
					}

					restored++;
					log?.Invoke(TXT.Translate("Restored directory: {0}", RedAssist.DQuote(entry.Path)));
				}
				catch (Exception ex)
				{
					failed++;
					log?.Invoke(TXT.Translate("Failed to restore directory: {0} - {1}", RedAssist.DQuote(entry.Path), ex.Message));
				}
			}

			if (failed == 0)
			{
				try { File.Delete(ManifestPath); }
				catch { }
			}

			return failed == 0 && restored > 0;
		}

		internal static Manifest LoadManifest()
		{
			try
			{
				if (!File.Exists(ManifestPath))
				{
					return null;
				}

				// Re-encode without BOM — DataContractJsonSerializer rejects the
				// UTF-8 BOM that File.WriteAllText(Encoding.UTF8) prepends
				string json = File.ReadAllText(ManifestPath, Encoding.UTF8);
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
	}
}
