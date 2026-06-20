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

        [Fact]
        public void CharacterClass_MatchesSingleChar()
        {
            var p = ParserWithRootGitignore("[Bb]uild\n");
            Assert.True(p.IsIgnored("Build", "Build"));
            Assert.True(p.IsIgnored("build", "build"));
            Assert.False(p.IsIgnored("xbuild", "xbuild"));
        }

        [Fact]
        public void NegatedCharacterClass_ExcludesChars()
        {
            var p = ParserWithRootGitignore("[!0-9]start\n");
            Assert.True(p.IsIgnored("astart", "astart"));
            Assert.False(p.IsIgnored("1start", "1start"));
        }

        [Fact]
        public void CharacterClass_InPathPattern()
        {
            var p = ParserWithRootGitignore("src/[Tt]est\n");
            Assert.True(p.IsIgnored("Test", "src/Test"));
            Assert.True(p.IsIgnored("test", "src/test"));
            Assert.False(p.IsIgnored("Test", "lib/Test"));
        }

        [Fact]
        public void NamePattern_InSubdirGitignore_IsScopedToThatSubtree()
        {
            // A bare-name rule in a per-directory .gitignore must only affect that
            // directory's subtree, not every directory of that name elsewhere (Git
            // per-directory scope). Regression guard: previously it applied tree-wide.
            string sub = Path.Combine(_root, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, ".gitignore"), "dist\n");

            var atSub = RED.GitIgnoreParser.LoadFromAncestors(_root).ExtendForDirectory(sub, _root);

            Assert.True(atSub.IsIgnored("dist", "sub/dist"));     // in scope -> ignored
            Assert.True(atSub.IsIgnored("dist", "sub/a/dist"));   // deeper in scope -> ignored
            Assert.False(atSub.IsIgnored("dist", "other/dist"));  // different subtree -> NOT ignored
            Assert.False(atSub.IsIgnored("dist", "dist"));        // at root -> NOT ignored
        }
    }
}
