using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace RED
{
    internal class GitIgnoreParser
    {
        private readonly List<GitIgnoreRule> rules = new List<GitIgnoreRule>();

        public bool HasRules { get { return rules.Count > 0; } }

        public static GitIgnoreParser LoadFromAncestors(string directoryPath)
        {
            var parser = new GitIgnoreParser();
            var dir = new DirectoryInfo(directoryPath);

            while (dir != null)
            {
                string gitignorePath = Path.Combine(dir.FullName, ".gitignore");
                if (File.Exists(gitignorePath))
                {
                    try
                    {
                        string[] lines = File.ReadAllLines(gitignorePath);
                        foreach (string line in lines)
                        {
                            string trimmed = line.Trim();
                            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                                continue;

                            bool negated = false;
                            if (trimmed.StartsWith("!"))
                            {
                                negated = true;
                                trimmed = trimmed.Substring(1);
                            }

                            trimmed = trimmed.TrimEnd('/');
                            if (string.IsNullOrEmpty(trimmed)) continue;

                            parser.rules.Add(new GitIgnoreRule
                            {
                                Pattern = GlobToRegex(trimmed),
                                Negated = negated,
                                IsPathPattern = trimmed.Contains("/")
                            });
                        }
                    }
                    catch { }
                }

                string gitDir = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(gitDir) || File.Exists(gitDir))
                    break;

                dir = dir.Parent;
            }

            return parser;
        }

        public bool IsIgnored(string name, string relativePath)
        {
            bool ignored = false;
            string nameToCheck = name;
            string pathToCheck = relativePath.Replace('\\', '/');

            foreach (var rule in rules)
            {
                string text = rule.IsPathPattern ? pathToCheck : nameToCheck;
                if (rule.Pattern.IsMatch(text))
                {
                    ignored = !rule.Negated;
                }
            }

            return ignored;
        }

        private static Regex GlobToRegex(string glob)
        {
            string pattern = glob.Replace("\\", "/");
            pattern = Regex.Escape(pattern);
            pattern = pattern.Replace("\\*\\*", "<<GLOBSTAR>>");
            pattern = pattern.Replace("\\*", "[^/]*");
            pattern = pattern.Replace("\\?", "[^/]");
            pattern = pattern.Replace("<<GLOBSTAR>>", ".*");
            pattern = "(?:^|/)" + pattern + "(?:$|/)";
            return new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        private class GitIgnoreRule
        {
            public Regex Pattern { get; set; }
            public bool Negated { get; set; }
            public bool IsPathPattern { get; set; }
        }
    }
}
