using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using RED.Match;
using Xunit;

namespace RED.Tests
{
    // Table-driven coverage of the filter rule engine (flag|code|text). Matching
    // is case-insensitive everywhere; directory rules containing a separator match
    // against the full path, otherwise the name.
    public class RedMatchTests
    {
        private static RedMatchItemList BuildDirList(params string[] rules)
        {
            var list = new RedMatchItemList();
            list.Transform(new List<string>(rules), RedMatchFilterType.Directory);
            return list;
        }

        [Theory]
        [InlineData("+|N|node_modules", @"C:\proj\node_modules", true)]   // exact name
        [InlineData("+|N|node_modules", @"C:\proj\NODE_MODULES", true)]   // case-insensitive
        [InlineData("+|N|node_modules", @"C:\proj\node_modules_x", false)]// exact, not contains
        [InlineData("+|C|cache", @"C:\proj\my-cache-dir", true)]          // contains
        [InlineData("+|S|.", @"C:\proj\.git", true)]                      // startswith
        [InlineData("+|E|.bak", @"C:\proj\old.bak", true)]                // endswith
        [InlineData("-|N|node_modules", @"C:\proj\node_modules", false)]  // disabled rule
        public void DirectoryRule_Matches(string rule, string path, bool expected)
        {
            var list = BuildDirList(rule);
            Assert.Equal(expected, list.IsOnList(new DirectoryInfo(path)));
        }

        [Theory]
        [InlineData(@"+|C|temp/cache", @"C:\x\temp\cache\sub", true)]   // '/' path pattern matches '\' path
        [InlineData(@"+|C|temp\cache", @"C:\x\temp\cache\sub", true)]   // '\' still works (regression guard)
        [InlineData(@"+|P|c:/x/proj", @"C:\x\proj", true)]             // '/' exact-path rule
        [InlineData(@"+|C|temp/cache", @"C:\x\tempcache\sub", false)]  // no false positive without the separator
        public void DirectoryRule_NormalizesPathSeparators(string rule, string path, bool expected)
        {
            // A directory filter written with '/' must match the same as one written
            // with '\'; previously only '\' was recognized as a path pattern.
            var list = BuildDirList(rule);
            Assert.Equal(expected, list.IsOnList(new DirectoryInfo(path)));
        }

        [Fact]
        public void RegexNameRule_MatchesCaseInsensitive()
        {
            var list = BuildDirList("+|RN|^temp[0-9]+$");
            Assert.True(list.IsOnList(new DirectoryInfo(@"C:\x\Temp42")));
            Assert.False(list.IsOnList(new DirectoryInfo(@"C:\x\temp")));
        }

        [Fact]
        public void RegexRule_PathologicalPattern_DoesNotHangAndYieldsNoMatch()
        {
            // A catastrophic-backtracking pattern against a long name must hit the
            // bounded match timeout and resolve to "no match" instead of wedging the
            // scan thread. Regression guard for the missing regex matchTimeout.
            var list = BuildDirList("+|RN|/(a+)+$/");
            var dir = new DirectoryInfo(@"C:\x\" + new string('a', 44) + "!");
            var sw = Stopwatch.StartNew();
            bool hit = list.IsOnList(dir);
            sw.Stop();
            Assert.False(hit);
            Assert.True(sw.ElapsedMilliseconds < 5000, "regex match should time out fast, took " + sw.ElapsedMilliseconds + "ms");
        }

        [Fact]
        public void WildcardCodelessRule_BecomesNameRegex()
        {
            // A codeless entry with a wildcard is auto-detected as a name regex.
            var list = new RedMatchItemList();
            list.Transform(new List<string> { "*.tmp" }, RedMatchFilterType.Directory);
            Assert.True(list.IsOnList(new DirectoryInfo(@"C:\x\build.tmp")));
            Assert.False(list.IsOnList(new DirectoryInfo(@"C:\x\build.tmpx")));
        }

        [Fact]
        public void ExplicitPathCodeIsHonoredNotTreatedAsWildcard()
        {
            // "P|" path-exact must be honored literally even with a wildcard-like char.
            var list = BuildDirList(@"+|P|c:\proj\node_modules");
            Assert.True(list.IsOnList(new DirectoryInfo(@"C:\proj\node_modules")));
            Assert.False(list.IsOnList(new DirectoryInfo(@"C:\other\node_modules")));
        }

        [Fact]
        public void EmptyFileRule_FlagsZeroByteFiles()
        {
            var list = new RedMatchItemList();
            list.Transform(new List<string>(), RedMatchFilterType.Files);
            string pattern;
            // ignoreEmptyFiles=true, size 0 -> flagged
            Assert.True(list.IsOnList(new FileInfo(@"C:\x\empty.dat"), 0, true, out pattern));
            // non-empty -> not flagged by the empty-file shortcut
            Assert.False(list.IsOnList(new FileInfo(@"C:\x\full.dat"), 10, true, out pattern));
        }

        [Fact]
        public void NoRules_NeverMatches()
        {
            var list = BuildDirList();
            Assert.False(list.IsOnList(new DirectoryInfo(@"C:\anything")));
        }

        [Fact]
        public void StatusReason_TrulyEmpty_NoIgnoredFileCount()
        {
            var item = new RedScanResultItem(
                new DirectoryInfo(@"C:\empty"), DirectorySearchStatusTypes.Empty);
            Assert.Equal(0, item.IgnoredFileCount);
            Assert.Contains("Empty", item.StatusReason);
            Assert.DoesNotContain("ignored", item.StatusReason);
        }

        [Fact]
        public void StatusReason_IgnoredFiles_DistinctFromTrulyEmpty()
        {
            var item = new RedScanResultItem(
                new DirectoryInfo(@"C:\has-trash"), DirectorySearchStatusTypes.Empty);
            item.IgnoredFileCount = 3;
            Assert.Contains("3", item.StatusReason);
            Assert.Contains("ignored", item.StatusReason);
        }

        [Fact]
        public void KeptReasonDetail_TakesPrecedenceOverIgnoredFileCount()
        {
            var item = new RedScanResultItem(
                new DirectoryInfo(@"C:\x"), DirectorySearchStatusTypes.Empty);
            item.IgnoredFileCount = 2;
            item.KeptReasonDetail = "custom reason";
            Assert.Equal("custom reason", item.StatusReason);
        }
    }

    public class UpdateCheckTests
    {
        [Theory]
        [InlineData("1.5.19", "1.5.18", 1)]
        [InlineData("1.5.18", "1.5.18", 0)]
        [InlineData("1.5.17", "1.5.18", -1)]
        [InlineData("2.0.0", "1.99.99", 1)]
        [InlineData("1.5.18.1", "1.5.18.0", 1)]
        [InlineData("1.5.18", "1.5.18.0", 0)]
        public void CompareVersions_OrdersCorrectly(string a, string b, int expected)
        {
            int result = UpdateCheck.CompareVersions(a, b);
            Assert.Equal(expected, Math.Sign(result));
        }
    }
}
