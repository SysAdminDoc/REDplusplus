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

        /// <summary>
        /// Load .gitignore files from directories ABOVE the scan root up to the
        /// .git root. The scan root's own .gitignore is NOT loaded here — it is
        /// picked up during traversal via ExtendForDirectory so that nested rules
        /// are handled uniformly.
        ///
        /// Rules are ordered farthest-ancestor first so that last-match-wins in
        /// IsIgnored gives closer (later) rules higher precedence — matching Git's
        /// documented precedence order.
        /// </summary>
        public static GitIgnoreParser LoadFromAncestors(string directoryPath)
        {
            var parser = new GitIgnoreParser();
            var scanDir = new DirectoryInfo(directoryPath);
            string scanRoot = scanDir.FullName.TrimEnd('\\');

            string gitDirAtRoot = Path.Combine(scanDir.FullName, ".git");
            if (Directory.Exists(gitDirAtRoot) || File.Exists(gitDirAtRoot))
                return parser;

            var ancestors = new List<string[]>();
            var current = scanDir.Parent;
            while (current != null)
            {
                string currentPath = current.FullName.TrimEnd('\\');
                string prefix = "";
                if (scanRoot.Length > currentPath.Length)
                    prefix = scanRoot.Substring(currentPath.Length + 1).Replace('\\', '/');

                ancestors.Add(new[] { currentPath, prefix });

                string gitDir = Path.Combine(current.FullName, ".git");
                if (Directory.Exists(gitDir) || File.Exists(gitDir))
                    break;
                current = current.Parent;
            }

            ancestors.Reverse();
            foreach (var item in ancestors)
            {
                parser.LoadRulesFromFile(
                    Path.Combine(item[0], ".gitignore"),
                    item[1], "");
            }

            return parser;
        }

        /// <summary>
        /// Return a parser with this parser's rules plus rules from a .gitignore
        /// in the specified directory. If no .gitignore exists, returns this
        /// instance unchanged (no allocation).
        /// </summary>
        public GitIgnoreParser ExtendForDirectory(string directoryPath, string scanRootPath)
        {
            string gitignorePath = Path.Combine(directoryPath, ".gitignore");
            if (!File.Exists(gitignorePath)) return this;

            var extended = new GitIgnoreParser();
            extended.rules.AddRange(this.rules);

            string scanRoot = scanRootPath.TrimEnd('\\', '/');
            string dir = directoryPath.TrimEnd('\\', '/');
            string scopeDir = "";
            if (dir.Length > scanRoot.Length)
                scopeDir = dir.Substring(scanRoot.Length + 1).Replace('\\', '/');

            extended.LoadRulesFromFile(gitignorePath, "", scopeDir);
            return extended;
        }

        private void LoadRulesFromFile(string gitignorePath, string ancestorPrefix, string scopeDir)
        {
            if (!File.Exists(gitignorePath)) return;
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

                    bool isPath = trimmed.Contains("/");

                    rules.Add(new GitIgnoreRule
                    {
                        Pattern = GlobToRegex(trimmed, isPath),
                        Negated = negated,
                        IsPathPattern = isPath,
                        AncestorPrefix = ancestorPrefix,
                        ScopeDir = scopeDir
                    });
                }
            }
            catch { }
        }

        public bool IsIgnored(string name, string relativePath)
        {
            bool ignored = false;
            string pathToCheck = relativePath.Replace('\\', '/').TrimStart('/');

            foreach (var rule in rules)
            {
                string text;
                if (rule.IsPathPattern)
                {
                    if (!string.IsNullOrEmpty(rule.ScopeDir))
                    {
                        string scopePrefix = rule.ScopeDir + "/";
                        if (!pathToCheck.StartsWith(scopePrefix, StringComparison.OrdinalIgnoreCase))
                            continue;
                        text = pathToCheck.Substring(scopePrefix.Length);
                    }
                    else if (!string.IsNullOrEmpty(rule.AncestorPrefix))
                    {
                        text = rule.AncestorPrefix + "/" + pathToCheck;
                    }
                    else
                    {
                        text = pathToCheck;
                    }
                }
                else
                {
                    text = name;
                }

                if (rule.Pattern.IsMatch(text))
                {
                    ignored = !rule.Negated;
                }
            }

            return ignored;
        }

        private static Regex GlobToRegex(string glob, bool isPathPattern)
        {
            string pattern = glob.Replace("\\", "/");
            if (isPathPattern) pattern = pattern.TrimStart('/');
            pattern = Regex.Escape(pattern);
            pattern = pattern.Replace("\\*\\*", "<<GLOBSTAR>>");
            pattern = pattern.Replace("\\*", "[^/]*");
            pattern = pattern.Replace("\\?", "[^/]");
            pattern = pattern.Replace("<<GLOBSTAR>>/", "(?:.*/)?");
            pattern = pattern.Replace("/<<GLOBSTAR>>", "(?:/.*)?");
            pattern = pattern.Replace("<<GLOBSTAR>>", ".*");

            if (isPathPattern)
                pattern = "^" + pattern + "(?:$|/)";
            else
                pattern = "(?:^|/)" + pattern + "(?:$|/)";

            return new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        private class GitIgnoreRule
        {
            public Regex Pattern { get; set; }
            public bool Negated { get; set; }
            public bool IsPathPattern { get; set; }
            public string AncestorPrefix { get; set; }
            public string ScopeDir { get; set; }
        }
    }
}
