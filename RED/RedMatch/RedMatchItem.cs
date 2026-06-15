using System.Text.RegularExpressions;

namespace RED.Match
{
	public class RedMatchItem
	{
		public RedMatchItem(RedMatchMethodType matchMethod, string matchText, bool enabled)
		{
			Populate(matchMethod, matchText, enabled);
		}

		public bool Enabled { get; set; }
		public RedMatchMethodType MatchMethod { get; private set; }
		public string MatchText { get; private set; }
		public string MatchTextToCompare { get; private set; }
		public Regex RegEx { get; private set; }
		public string MatchMethodPrefix { get { return MatchMethodToCode(MatchMethod); } }

		public override string ToString()
		{
			return FormatToString(this);
		}

		public void Populate(RedMatchMethodType matchMethod, string matchText, bool enabled)
		{
			try
			{
				if (!string.IsNullOrWhiteSpace(matchText))
				{
					MatchMethod = matchMethod;
					MatchText = matchText;
					if (matchMethod == RedMatchMethodType.RegExName || matchMethod == RedMatchMethodType.RegExPath)
					{
						// All other match methods compare case-insensitively (names are
						// lowercased before matching) — regex rules must behave the same.
						// Bound backtracking so a pathological user pattern (e.g. "(a+)+$")
						// cannot wedge the scan thread; a timeout is treated as no-match.
						RegEx = new Regex(ExpandRegEx(matchText), RegexOptions.IgnoreCase, System.TimeSpan.FromSeconds(1));
					}
					MatchText = matchText.Trim();
					MatchTextToCompare = MatchText.ToLowerInvariant();
				}
				Enabled = enabled;
			}
			catch
			{
				Enabled = false;
				MatchMethod = RedMatchMethodType.Invalid;
				RegEx = null;
			}
		}

		private string ExpandRegEx(string text)
		{
			string respx = text;
			if (text.Contains("*") || (text.StartsWith("/") && text.EndsWith("/")))
			{
				if (text.StartsWith("/") && text.EndsWith("/"))
				{
					respx = text.Substring(1, text.Length - 2);
				}
				else
				{
					string rxtext = Regex.Escape(text).Replace("\\*", ".*");
					respx = "^" + rxtext + "$";
				}
			}
			return respx;
		}

		/// <summary>
		/// Bounded regex match: a user pattern that exhausts its match timeout is
		/// treated as no-match rather than throwing out of the hot scan loop.
		/// </summary>
		internal static bool SafeIsMatch(Regex regex, string input)
		{
			if (regex == null) return false;
			try { return regex.IsMatch(input); }
			catch (RegexMatchTimeoutException) { return false; }
		}

		internal static string FormatToString(RedMatchItem item)
		{
			return FormatToString(item.Enabled, item.MatchMethod, item.MatchText);
		}

		internal static string FormatToString(bool enabled, RedMatchMethodType matchMethod, string matchText)
		{
			return string.Format("{0}|{1}|{2}", enabled ? "+" : "-", MatchMethodToCode(matchMethod), matchText);
		}

		internal static string MatchMethodToCode(RedMatchMethodType v)
		{
			switch (v)
			{
				case RedMatchMethodType.Contains: return "C";
				case RedMatchMethodType.Startwith: return "S";
				case RedMatchMethodType.Endswith: return "E";
				case RedMatchMethodType.NameExact: return "N";
				case RedMatchMethodType.NameExactWithPath: return "P";
				case RedMatchMethodType.RegExName: return "RN";
				case RedMatchMethodType.RegExPath: return "RP";
				default: return "?";
			}
		}

		internal static RedMatchMethodType CodeToMatchMethod(string v)
		{
			switch (v)
			{
				case "C": return RedMatchMethodType.Contains;
				case "S": return RedMatchMethodType.Startwith;
				case "E": return RedMatchMethodType.Endswith;
				case "N": return RedMatchMethodType.NameExact;
				case "P": return RedMatchMethodType.NameExactWithPath;
				case "RN": return RedMatchMethodType.RegExName;
				case "RP": return RedMatchMethodType.RegExPath;
				default: return RedMatchMethodType.Invalid;
			}
		}
	}
}