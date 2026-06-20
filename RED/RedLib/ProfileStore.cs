using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace RED
{
	/// <summary>
	/// A reusable, named set of headless run settings (roots + delete mode +
	/// per-run toggles) so a scheduled task can reference <c>-profile nightly</c>
	/// instead of a long argument list. Stored in a dedicated JSON file next to
	/// the config — deliberately separate from the XML config so it can never
	/// destabilize the (silent-mode-critical) config load path.
	/// </summary>
	[DataContract]
	public class RedProfile
	{
		[DataMember(Name = "name")] public string Name { get; set; }
		[DataMember(Name = "paths")] public List<string> Paths { get; set; }
		[DataMember(Name = "mode", EmitDefaultValue = false)] public string Mode { get; set; }
		[DataMember(Name = "moveTo", EmitDefaultValue = false)] public string MoveTo { get; set; }
		[DataMember(Name = "emptyFiles")] public bool EmptyFiles { get; set; }
		[DataMember(Name = "minAgeHours", EmitDefaultValue = false)] public int? MinAgeHours { get; set; }
		[DataMember(Name = "maxDepth", EmitDefaultValue = false)] public int? MaxDepth { get; set; }
		[DataMember(Name = "gitignore", EmitDefaultValue = false)] public bool? GitIgnore { get; set; }
		[DataMember(Name = "mft", EmitDefaultValue = false)] public bool? Mft { get; set; }
		[DataMember(Name = "ignoreHidden", EmitDefaultValue = false)] public bool? IgnoreHidden { get; set; }
		[DataMember(Name = "ignoreSystem", EmitDefaultValue = false)] public bool? IgnoreSystem { get; set; }
		[DataMember(Name = "lockout", EmitDefaultValue = false)] public bool? Lockout { get; set; }
		[DataMember(Name = "parallel", EmitDefaultValue = false)] public int? Parallel { get; set; }
		[DataMember(Name = "exclude", EmitDefaultValue = false)] public List<string> Exclude { get; set; }
		[DataMember(Name = "protect", EmitDefaultValue = false)] public List<string> Protect { get; set; }
	}

	[DataContract]
	internal class ProfileFile
	{
		[DataMember(Name = "profiles")] public List<RedProfile> Profiles { get; set; }
	}

	public static class ProfileStore
	{
		private const string FileName = "RED+.profiles.json";

		public static string StorePath { get { return RuntimeData.GetWritableDataFilePath(FileName); } }

		public static List<RedProfile> LoadAll()
		{
			try
			{
				string path = StorePath;
				if (!File.Exists(path)) return new List<RedProfile>();
				using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
				{
					var serializer = new DataContractJsonSerializer(typeof(ProfileFile));
					var file = serializer.ReadObject(stream) as ProfileFile;
					return file != null && file.Profiles != null ? file.Profiles : new List<RedProfile>();
				}
			}
			catch
			{
				return new List<RedProfile>();
			}
		}

		public static RedProfile Get(string name)
		{
			if (string.IsNullOrWhiteSpace(name)) return null;
			return LoadAll().FirstOrDefault(p =>
				string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>Adds or replaces a profile by name. Returns false (with a
		/// reason) if the write failed; never throws.</summary>
		public static bool Save(RedProfile profile, out string error)
		{
			error = null;
			if (profile == null || string.IsNullOrWhiteSpace(profile.Name))
			{
				error = "Profile name is required.";
				return false;
			}
			profile.Name = profile.Name.Trim();
			// A control character (tab/newline) would corrupt the tab-delimited
			// -listprofiles output; an unbounded name bloats the store.
			if (profile.Name.Length > 128)
			{
				error = "Profile name must be 128 characters or fewer.";
				return false;
			}
			foreach (char c in profile.Name)
			{
				if (char.IsControl(c))
				{
					error = "Profile name must not contain control characters.";
					return false;
				}
			}
			try
			{
				List<RedProfile> all = LoadAll();
				all.RemoveAll(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
				all.Add(profile);

				var file = new ProfileFile { Profiles = all };
				string path = StorePath;
				string tmp = path + ".tmp";
				using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write))
				{
					var serializer = new DataContractJsonSerializer(typeof(ProfileFile));
					serializer.WriteObject(stream, file);
				}
				if (File.Exists(path)) { File.Replace(tmp, path, null); }
				else { File.Move(tmp, path); }
				return true;
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}
		}

		public static bool Delete(string name)
		{
			try
			{
				List<RedProfile> all = LoadAll();
				int removed = all.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
				if (removed == 0) return false;

				var file = new ProfileFile { Profiles = all };
				string path = StorePath;
				string tmp = path + ".tmp";
				using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write))
				{
					var serializer = new DataContractJsonSerializer(typeof(ProfileFile));
					serializer.WriteObject(stream, file);
				}
				if (File.Exists(path)) { File.Replace(tmp, path, null); }
				else { File.Move(tmp, path); }
				return true;
			}
			catch
			{
				return false;
			}
		}
	}
}
