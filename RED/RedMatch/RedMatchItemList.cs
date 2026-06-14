using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace RED.Match
{
	public class RedMatchItemList : IEnumerable<RedMatchItem>
	{
		public RedMatchItemList()
		{
			FilterType = RedMatchFilterType.Generic;
			Items = new List<RedMatchItem>();
		}

		public RedMatchItemList(RedMatchFilterType filterType) : this()
		{
			FilterType = filterType;
		}

		// Default Indexer for this object
		public RedMatchItem this[int index]
		{
			get { return Items[index]; }
			set { Items[index] = value; }
		}

		public IEnumerator<RedMatchItem> GetEnumerator()
		{
			return Items.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return this.GetEnumerator();
		}

		public RedMatchFilterType FilterType { get; private set; }

		public List<RedMatchItem> Items { get; private set; }

		public List<string> ToStringList()
		{
			List<string> list = new List<string>();
			foreach (RedMatchItem item in Items)
			{
				list.Add(item.ToString());
			}
			return list;
		}

		public void AddItem(bool enabled, RedMatchMethodType matchMethod, string matchText)
		{
			AddItem(RedMatchItem.FormatToString(enabled, matchMethod, matchText));
		}

		private void AddItem(string v)
		{
			/// The text is a pipe delimited string in the order "EnabledFlag|MatchCode|MatchText"
			/// EnabledFflag is a single character. + for enabled, - for disabled
			/// MatchCode is a short code indicating MatchMethod to be used
			/// MatchText is the text to be matched against directory or file names
			string enabledFlag;
			string matchCode;
			string matchText;
			bool codeWasExplicit;

			if (!string.IsNullOrWhiteSpace(v))
			{
				string[] items = v.Trim().Split('|');
				switch (items.Length)
				{
					case 1:
						enabledFlag = "+";
						matchCode = "N";
						matchText = v;
						codeWasExplicit = false;
						break;
					case 2:
						enabledFlag = "+";
						matchCode = items[0];
						matchText = items[1];
						codeWasExplicit = true;
						break;
					case 3:
						enabledFlag = items[0];
						matchCode = items[1];
						matchText = items[2];
						codeWasExplicit = true;
						break;
					default:
						// invalid
						enabledFlag = "-";
						matchCode = "?";
						matchText = v;
						codeWasExplicit = false;
						break;
				}

				// Auto-detect wildcard/regex syntax only for codeless entries. An
				// explicit code (e.g. "P|" path-exact) is honored as written, so a
				// literal-ish wildcard no longer silently becomes a name regex.
				if (!codeWasExplicit &&
					(matchText.Contains("*") || (matchText.StartsWith("/") && matchText.EndsWith("/"))))
				{
					matchCode = "RN";
				}

				RedMatchMethodType matchMethod = RedMatchItem.CodeToMatchMethod(matchCode);

				if (matchMethod != RedMatchMethodType.Invalid && !string.IsNullOrWhiteSpace(matchText))
				{
					Items.Add(new RedMatchItem(matchMethod, matchText, enabledFlag == "+" ? true : false));
				}
			}
		}

		public void Transform(List<string> v, RedMatchFilterType filterType)
		{
			FilterType = filterType;
			Transform(v);
		}

		public void Transform(List<string> v)
		{
			Items.Clear();
			foreach (string item in v)
			{
				if (!string.IsNullOrWhiteSpace(item))
				{
					AddItem(item);
				}
			}
		}

		public bool IsOnList(DirectoryInfo dirinfo)
		{
			string nameToCheck = dirinfo.Name.ToLowerInvariant();
			string pathToCheck = dirinfo.FullName.ToLowerInvariant();
			return IsOnList(nameToCheck, pathToCheck, 0, false, out _);
		}

		public bool IsOnList(FileInfo fileinfo, long filesize, bool ignoreEmptyFiles, out string delPattern)
		{
			string nameToCheck = fileinfo.Name.ToLowerInvariant();
			string pathToCheck = fileinfo.FullName.ToLowerInvariant();
			return IsOnList(nameToCheck, pathToCheck, filesize, ignoreEmptyFiles, out delPattern);
		}

		private static string NormalizeSeparators(string s)
		{
			// Windows treats '/' and '\' as path separators and neither is a legal
			// filename character, so a directory filter written with '/' (e.g.
			// "temp/cache") must match the same as "temp\cache". Canonicalize to '\'.
			return string.IsNullOrEmpty(s) ? s : s.Replace('/', '\\');
		}

		private string GetCheckText(string matchText, string nameToCheck, string pathToCheck)
		{
			if (FilterType == RedMatchFilterType.Directory)
			{
				// A pattern containing either separator is a path pattern; compare it
				// against the full path. Previously only '\' was recognized, so a rule
				// written with '/' silently degraded to name-only matching and never fired.
				bool looksLikePath = matchText.IndexOf('\\') >= 0 || matchText.IndexOf('/') >= 0;
				return looksLikePath ? pathToCheck : nameToCheck;
			}
			else
			{
				return nameToCheck;
			}
		}

		private bool IsOnList(string nameToCheck, string pathToCheck, long filesize, bool ignoreEmptyFiles, out string delPattern)
		{
			delPattern = "";

			if (FilterType == RedMatchFilterType.Files && ignoreEmptyFiles && filesize == 0)
			{
				delPattern = "[Empty file]";
				return true;
			}

			for (int i = 0; i < Items.Count; i++)
			{
				RedMatchItem matchItem = Items[i];
				if (!matchItem.Enabled)
					continue;

				string textToCheck;
				bool hit = false;

				switch (matchItem.MatchMethod)
				{
					case RedMatchMethodType.NameExact:
						hit = (nameToCheck == matchItem.MatchTextToCompare);
						break;
					case RedMatchMethodType.Contains:
						textToCheck = NormalizeSeparators(GetCheckText(matchItem.MatchTextToCompare, nameToCheck, pathToCheck));
						hit = textToCheck.Contains(NormalizeSeparators(matchItem.MatchTextToCompare));
						break;
					case RedMatchMethodType.Endswith:
						textToCheck = NormalizeSeparators(GetCheckText(matchItem.MatchTextToCompare, nameToCheck, pathToCheck));
						hit = textToCheck.EndsWith(NormalizeSeparators(matchItem.MatchTextToCompare));
						break;
					case RedMatchMethodType.Startwith:
						textToCheck = NormalizeSeparators(GetCheckText(matchItem.MatchTextToCompare, nameToCheck, pathToCheck));
						hit = textToCheck.StartsWith(NormalizeSeparators(matchItem.MatchTextToCompare));
						break;
					case RedMatchMethodType.RegExName:
						hit = RedMatchItem.SafeIsMatch(matchItem.RegEx, nameToCheck);
						break;
					case RedMatchMethodType.RegExPath:
						if (FilterType == RedMatchFilterType.Directory)
							hit = RedMatchItem.SafeIsMatch(matchItem.RegEx, pathToCheck);
						break;
					case RedMatchMethodType.NameExactWithPath:
						if (FilterType == RedMatchFilterType.Directory)
							hit = (NormalizeSeparators(pathToCheck) == NormalizeSeparators(matchItem.MatchTextToCompare));
						break;
				}

				if (hit)
				{
					delPattern = matchItem.MatchText;
					return true;
				}
			}

			return false;
		}
	}
}