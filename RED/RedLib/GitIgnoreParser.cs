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
        /// Load gitignore rules from all three Git-spec sources above the scan root:
        ///   1. Global gitignore (core.excludesFile or ~/.config/git/ignore) — lowest precedence
        ///   2. .git/info/exclude — repo-specific excludes
        ///   3. Per-directory ancestor .gitignore files (farthest first, closest last)
        /// The scan root's own .gitignore is NOT loaded here — it is picked up
        /// during traversal via ExtendForDirectory.
        /// </summary>
        public static GitIgnoreParser LoadFromAncestors(string directoryPath)
        {
            var parser = new GitIgnoreParser();
            var scanDir = new DirectoryInfo(directoryPath);
            string scanRoot = scanDir.FullName.TrimEnd('\\');

            string gitRootPath = null;
            var probe = new DirectoryInfo(directoryPath);
            while (probe != null)
            {
                string gitDir = Path.Combine(probe.FullName, ".git");
                if (Directory.Exists(gitDir) || File.Exists(gitDir))
                {
                    gitRootPath = probe.FullName.TrimEnd('\\');
                    break;
                }
                probe = probe.Parent;
            }

            string gitRootPrefix = "";
            if (gitRootPath != null && scanRoot.Length > gitRootPath.Length)
                gitRootPrefix = scanRoot.Substring(gitRootPath.Length + 1).Replace('\\', '/');

            // 1. Global gitignore (lowest precedence)
            string globalPath = FindGlobalGitignorePath();
            if (globalPath != null)
                parser.LoadRulesFromFile(globalPath, gitRootPrefix, "");

            // 2. .git/info/exclude
            if (gitRootPath != null)
            {
                string excludePath = Path.Combine(gitRootPath, ".git", "info", "exclude");
                parser.LoadRulesFromFile(excludePath, gitRootPrefix, "");
            }

            // 3. Per-directory ancestor .gitignore (above scan root only)
            if (gitRootPath != null)
            {
                var ancestors = new List<string[]>();
                var current = scanDir.Parent;
                while (current != null)
                {
                    string currentPath = current.FullName.TrimEnd('\\');
                    string prefix = "";
                    if (scanRoot.Length > currentPath.Length)
                        prefix = scanRoot.Substring(currentPath.Length + 1).Replace('\\', '/');

                    ancestors.Add(new[] { currentPath, prefix });

                    if (currentPath.Equals(gitRootPath, StringComparison.OrdinalIgnoreCase))
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
                    // A per-directory .gitignore's bare-name rule is scoped to that
                    // directory's own subtree, exactly like a path pattern. Without this
                    // guard a deep ".gitignore" line such as `dist` would wrongly ignore
                    // every `dist` folder tree-wide (siblings and ancestors included).
                    if (!string.IsNullOrEmpty(rule.ScopeDir)
                        && !pathToCheck.StartsWith(rule.ScopeDir + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    text = name;
                }

                if (rule.Pattern.IsMatch(text))
                {
                    ignored = !rule.Negated;
                }
            }

            return ignored;
        }

        private static string FindGlobalGitignorePath()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            string path = TryReadExcludesFile(Path.Combine(userProfile, ".gitconfig"), userProfile);
            if (path != null) return path;

            string xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(xdgConfig))
                xdgConfig = Path.Combine(userProfile, ".config");

            path = TryReadExcludesFile(Path.Combine(xdgConfig, "git", "config"), userProfile);
            if (path != null) return path;

            string defaultPath = Path.Combine(xdgConfig, "git", "ignore");
            if (File.Exists(defaultPath)) return defaultPath;

            return null;
        }

        private static string TryReadExcludesFile(string gitconfigPath, string userProfile)
        {
            if (!File.Exists(gitconfigPath)) return null;
            try
            {
                string content = File.ReadAllText(gitconfigPath);
                var match = Regex.Match(content, @"excludes[Ff]ile\s*=\s*(.+)");
                if (match.Success)
                {
                    string val = match.Groups[1].Value.Trim();
                    val = val.Replace("~/", userProfile.Replace('\\', '/') + "/");
                    val = Environment.ExpandEnvironmentVariables(val);
                    val = val.Replace('/', '\\');
                    if (File.Exists(val)) return val;
                }
            }
            catch { }
            return null;
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
