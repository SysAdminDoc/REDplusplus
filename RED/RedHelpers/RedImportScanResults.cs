using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using RED.Match;
using TXT = RED.RedGetText;

namespace RED.Helper
{
	internal sealed class RedImportedScanRoot
	{
		public RedImportedScanRoot(DirectoryInfo rootDirectory)
		{
			RootDirectory = rootDirectory;
			Results = new List<RedScanResultItem>();
		}

		public DirectoryInfo RootDirectory { get; private set; }
		public List<RedScanResultItem> Results { get; private set; }
	}

	internal sealed class RedImportedScanResults
	{
		public RedImportedScanResults(List<RedImportedScanRoot> roots, RedScanResultItemList deletableResults, int reviewCount)
		{
			Roots = roots;
			DeletableResults = deletableResults;
			ReviewCount = reviewCount;
		}

		public List<RedImportedScanRoot> Roots { get; private set; }
		public RedScanResultItemList DeletableResults { get; private set; }
		public int ReviewCount { get; private set; }
	}

	internal static class RedImportScanResults
	{
		private const long MaxImportBytes = 64 * 1024 * 1024;
		private const int MaxImportRecords = 500_000;

		public static RedImportedScanResults ReadFile(string filename)
		{
			if (string.IsNullOrWhiteSpace(filename))
			{
				throw new ArgumentException(TXT.Translate("Import filename is empty"), "filename");
			}
			if (!File.Exists(filename))
			{
				throw new FileNotFoundException(TXT.Translate("Import file was not found"), filename);
			}

			long fileSize = new FileInfo(filename).Length;
			if (fileSize > MaxImportBytes)
			{
				throw new InvalidDataException(TXT.Translate("Import file is too large ({0} MB, maximum {1} MB).",
					(fileSize / (1024 * 1024)).ToString(), (MaxImportBytes / (1024 * 1024)).ToString()));
			}

			List<ImportRecord> records = ParseFile(filename);
			if (records.Count == 0)
			{
				throw new InvalidDataException(TXT.Translate("The import file does not contain any scan results."));
			}

			var allResults = new List<RedScanResultItem>();
			var deletableResults = new RedScanResultItemList();
			foreach (ImportRecord record in records)
			{
				string path = NormalizePath(record.Path);
				DirectorySearchStatusTypes status = ParseStatus(record.Status, path);
				RedScanResultItem item;
				if (record.IsFile)
				{
					item = new RedScanResultItem(new FileInfo(path), status, record.Reason ?? string.Empty);
				}
				else
				{
					item = new RedScanResultItem(new DirectoryInfo(path), status, record.Reason ?? string.Empty);
				}
				allResults.Add(item);
				if (status == DirectorySearchStatusTypes.Empty)
				{
					deletableResults.AddItem(item);
				}
			}

			return new RedImportedScanResults(BuildRoots(allResults), deletableResults, allResults.Count);
		}

		private static List<ImportRecord> ParseFile(string filename)
		{
			using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				int firstByte = -1;
				using (var peek = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true))
				{
					while ((firstByte = peek.Read()) != -1)
					{
						if (!char.IsWhiteSpace((char)firstByte)) break;
					}
				}

				if (firstByte == -1)
				{
					throw new InvalidDataException(TXT.Translate("The import file is empty."));
				}

				stream.Position = 0;

				if ((char)firstByte == '[')
				{
					return ParseArray(stream);
				}

				return ParseLineDelimited(stream);
			}
		}

		private static List<ImportRecord> ParseArray(Stream stream)
		{
			string json;
			using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true))
			{
				json = reader.ReadToEnd();
			}
			var parsed = Deserialize<List<ImportRecord>>(json);
			var records = FilterRecords(parsed);
			if (records.Count > MaxImportRecords)
			{
				throw new InvalidDataException(TXT.Translate("Import file contains too many records ({0}, maximum {1}).",
					records.Count.ToString(), MaxImportRecords.ToString()));
			}
			return records;
		}

		private static List<ImportRecord> ParseLineDelimited(Stream stream)
		{
			var records = new List<ImportRecord>();
			using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true))
			{
				string line;
				int lineNumber = 0;
				while ((line = reader.ReadLine()) != null)
				{
					lineNumber++;
					if (string.IsNullOrWhiteSpace(line))
					{
						continue;
					}

					ImportRecord record;
					try
					{
						record = Deserialize<ImportRecord>(line);
					}
					catch (Exception ex)
					{
						throw new InvalidDataException(TXT.Translate("Line {0} is not valid RED++ NDJSON: {1}", lineNumber, ex.Message), ex);
					}

					AddIfResult(records, record);

					if (records.Count > MaxImportRecords)
					{
						throw new InvalidDataException(TXT.Translate("Import file contains too many records (over {0}).",
							MaxImportRecords.ToString()));
					}
				}
			}
			return records;
		}

		private static List<ImportRecord> FilterRecords(IEnumerable<ImportRecord> parsed)
		{
			var records = new List<ImportRecord>();
			if (parsed == null)
			{
				return records;
			}
			foreach (ImportRecord record in parsed)
			{
				AddIfResult(records, record);
			}
			return records;
		}

		private static void AddIfResult(List<ImportRecord> records, ImportRecord record)
		{
			if (record == null)
			{
				return;
			}

			if (string.Equals(record.Type, "meta", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			if (!string.IsNullOrWhiteSpace(record.Type) &&
				!string.Equals(record.Type, "result", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException(TXT.Translate("Unknown RED++ import record type: {0}", record.Type));
			}

			records.Add(record);
		}

		private static T Deserialize<T>(string json)
		{
			byte[] data = Encoding.UTF8.GetBytes(json);
			using (var ms = new MemoryStream(data))
			{
				var serializer = new DataContractJsonSerializer(typeof(T));
				return (T)serializer.ReadObject(ms);
			}
		}

		private static string NormalizePath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				throw new InvalidDataException(TXT.Translate("A scan result is missing its path."));
			}

			string expanded = Environment.ExpandEnvironmentVariables(path.Trim());
			if (IsDriveRelativePath(expanded) || !Path.IsPathRooted(expanded))
			{
				throw new InvalidDataException(TXT.Translate("Import paths must be absolute: {0}", path));
			}

			try
			{
				return Path.GetFullPath(expanded);
			}
			catch (Exception ex)
			{
				throw new InvalidDataException(TXT.Translate("Import path is not valid: {0}", path), ex);
			}
		}

		private static bool IsDriveRelativePath(string path)
		{
			return path.Length >= 2 &&
				path[1] == ':' &&
				(path.Length == 2 || (path[2] != Path.DirectorySeparatorChar && path[2] != Path.AltDirectorySeparatorChar));
		}

		private static DirectorySearchStatusTypes ParseStatus(string status, string path)
		{
			if (string.IsNullOrWhiteSpace(status))
			{
				throw new InvalidDataException(TXT.Translate("Scan result is missing status: {0}", path));
			}

			DirectorySearchStatusTypes parsed;
			if (!Enum.TryParse(status.Trim(), true, out parsed))
			{
				throw new InvalidDataException(TXT.Translate("Unknown scan result status for {0}: {1}", path, status));
			}
			return parsed;
		}

		private static List<RedImportedScanRoot> BuildRoots(List<RedScanResultItem> results)
		{
			var byVolume = new Dictionary<string, List<RedScanResultItem>>(StringComparer.OrdinalIgnoreCase);
			foreach (RedScanResultItem item in results)
			{
				string root = Path.GetPathRoot(item.FullPath);
				if (string.IsNullOrWhiteSpace(root))
				{
					root = item.FullPath;
				}
				if (!byVolume.ContainsKey(root))
				{
					byVolume[root] = new List<RedScanResultItem>();
				}
				byVolume[root].Add(item);
			}

			var roots = new List<RedImportedScanRoot>();
			foreach (List<RedScanResultItem> group in byVolume.Values)
			{
				string commonParent = FindCommonParent(group);
				var importRoot = new RedImportedScanRoot(new DirectoryInfo(commonParent));
				foreach (RedScanResultItem item in group)
				{
					importRoot.Results.Add(item);
				}
				roots.Add(importRoot);
			}
			return roots;
		}

		private static string FindCommonParent(List<RedScanResultItem> results)
		{
			string common = GetParentOrSelf(results[0].FullPath);
			for (int i = 1; i < results.Count; i++)
			{
				while (!PathContains(common, results[i].FullPath))
				{
					DirectoryInfo parent = Directory.GetParent(common);
					if (parent == null)
					{
						string root = Path.GetPathRoot(common);
						return string.IsNullOrWhiteSpace(root) ? common : root;
					}
					common = parent.FullName;
				}
			}
			return common;
		}

		private static string GetParentOrSelf(string path)
		{
			DirectoryInfo directory = new DirectoryInfo(path);
			return directory.Parent == null ? directory.FullName : directory.Parent.FullName;
		}

		private static bool PathContains(string parent, string candidate)
		{
			string p = EnsureTrailingSeparator(Path.GetFullPath(parent));
			string c = EnsureTrailingSeparator(Path.GetFullPath(candidate));
			return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
		}

		private static string EnsureTrailingSeparator(string path)
		{
			path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return path + Path.DirectorySeparatorChar;
		}

		[DataContract]
		private sealed class ImportRecord
		{
			[DataMember(Name = "type")]
			public string Type { get; set; }

			[DataMember(Name = "kind")]
			public string Kind { get; set; }

			[DataMember(Name = "path")]
			public string Path { get; set; }

			[DataMember(Name = "status")]
			public string Status { get; set; }

			[DataMember(Name = "reason")]
			public string Reason { get; set; }

			public bool IsFile
			{
				get { return string.Equals(Kind, "file", StringComparison.OrdinalIgnoreCase); }
			}
		}
	}
}
