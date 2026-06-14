using System;
using System.IO;
using Xunit;

namespace RED.Tests
{
    // GitIgnore matching: anchoring (path vs name patterns), negation precedence,
    // and per-directory scoping via ExtendForDirectory.
    public sealed class GitIgnoreTests : IDisposable
    {
        private readonly string _root;

        public GitIgnoreTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "redpp-gi-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            // Make it look like a git repo so LoadFromAncestors anchors here.
            Directory.CreateDirectory(Path.Combine(_root, ".git"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private RED.GitIgnoreParser ParserWithRootGitignore(string contents)
        {
            File.WriteAllText(Path.Combine(_root, ".gitignore"), contents);
            var baseParser = RED.GitIgnoreParser.LoadFromAncestors(_root);
            // The scan root's own .gitignore is applied during traversal.
            return baseParser.ExtendForDirectory(_root, _root);
        }

        [Fact]
        public void NamePattern_MatchesAtAnyDepth()
        {
            var p = ParserWithRootGitignore("node_modules\n");
            Assert.True(p.IsIgnored("node_modules", "node_modules"));
            Assert.True(p.IsIgnored("node_modules", "src/a/node_modules"));
            Assert.False(p.IsIgnored("node_modules_x", "src/node_modules_x"));
        }

        [Fact]
        public void AnchoredPathPattern_OnlyMatchesFromRoot()
        {
            var p = ParserWithRootGitignore("/build\n");
            Assert.True(p.IsIgnored("build", "build"));
            Assert.False(p.IsIgnored("build", "src/build"));
        }

        [Fact]
        public void Negation_ReinstatesPreviouslyIgnored()
        {
            var p = ParserWithRootGitignore("*.log\n!keep.log\n");
            Assert.True(p.IsIgnored("debug.log", "debug.log"));
            Assert.False(p.IsIgnored("keep.log", "keep.log"));
        }

        [Fact]
        public void GlobstarPattern_MatchesAcrossDirectories()
        {
            var p = ParserWithRootGitignore("logs/**/tmp\n");
            Assert.True(p.IsIgnored("tmp", "logs/a/b/tmp"));
            Assert.True(p.IsIgnored("tmp", "logs/tmp"));
        }

        [Fact]
        public void CommentsAndBlankLines_Ignored()
        {
            var p = ParserWithRootGitignore("# a comment\n\n   \ncache\n");
            Assert.True(p.IsIgnored("cache", "cache"));
            Assert.False(p.IsIgnored("a", "a"));
        }
    }
}
