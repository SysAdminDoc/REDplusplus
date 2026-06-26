using System;
using System.IO;
using System.Text.Json;
using RED.Helper;
using RED.Match;
using Xunit;

namespace RED.Tests
{
    public sealed class RedExportTests : IDisposable
    {
        private readonly string _dir;

        public RedExportTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "redpp-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private RedScanResultItemList BuildSampleResults()
        {
            string dirA = Path.Combine(_dir, "emptyA");
            string dirB = Path.Combine(_dir, "kept");
            Directory.CreateDirectory(dirA);
            Directory.CreateDirectory(dirB);

            var list = new RedScanResultItemList();
            list.AddItem(new RedScanResultItem(new DirectoryInfo(dirA), DirectorySearchStatusTypes.Empty));
            list.AddItem(new RedScanResultItem(new DirectoryInfo(dirB), DirectorySearchStatusTypes.NeverEmpty));
            return list;
        }

        [Theory]
        [InlineData("=1+1", "'=1+1")]
        [InlineData("+cmd|'/c calc'!A1", "'+cmd|'/c calc'!A1")]
        [InlineData("@SUM(1)", "'@SUM(1)")]
        [InlineData("-2+3", "'-2+3")]
        [InlineData(" =1+1", "' =1+1")]
        [InlineData("   @x", "'   @x")]
        [InlineData("\t=bad", "'\t=bad")]
        [InlineData("normal name", "normal name")]
        [InlineData("C:\\path\\folder", "C:\\path\\folder")]
        public void EscapeCsvCell_NeutralizesFormulaInjection(string input, string expected)
        {
            Assert.Equal(expected, RedExportScanResults.EscapeCsvCell(input));
        }

        [Theory]
        [InlineData("hello", "hello")]
        [InlineData("a\\b", "a\\\\b")]
        [InlineData("a\"b", "a\\\"b")]
        [InlineData("a\nb", "a\\nb")]
        [InlineData("a\tb", "a\\tb")]
        [InlineData("a\bb", "a\\bb")]
        [InlineData("a\fb", "a\\fb")]
        public void EscapeJson_HandlesSpecialCharacters(string input, string expected)
        {
            Assert.Equal(expected, RedExportScanResults.EscapeJson(input));
        }

        [Fact]
        public void WriteCsv_ContainsHeaderAndRows()
        {
            var results = BuildSampleResults();
            string file = Path.Combine(_dir, "out.csv");
            using (var exporter = new RedExportScanResults())
            {
                exporter.WriteCsv(results, file);
            }

            string[] lines = File.ReadAllLines(file);
            Assert.True(lines.Length >= 3, "Expected header + 2 data rows");
            Assert.StartsWith("\"Kind\"", lines[0]);
            Assert.Contains("emptyA", lines[1]);
            Assert.Contains("Empty", lines[1]);
            Assert.Contains("NeverEmpty", lines[2]);
        }

        [Fact]
        public void WriteJson_ProducesValidJsonArray()
        {
            var results = BuildSampleResults();
            string file = Path.Combine(_dir, "out.json");
            using (var exporter = new RedExportScanResults())
            {
                exporter.WriteJson(results, file);
            }

            string json = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.Equal(2, doc.RootElement.GetArrayLength());

            var first = doc.RootElement[0];
            Assert.Equal("directory", first.GetProperty("kind").GetString());
            Assert.Contains("emptyA", first.GetProperty("path").GetString());
            Assert.Equal("Empty", first.GetProperty("status").GetString());
        }

        [Fact]
        public void WritePowerShellScript_ContainsOnlyEligibleDirs()
        {
            var results = BuildSampleResults();
            string file = Path.Combine(_dir, "out.ps1");
            using (var exporter = new RedExportScanResults())
            {
                exporter.WritePowerShellScript(results, file);
            }

            string content = File.ReadAllText(file);
            Assert.Contains("$Execute = $false", content);
            Assert.Contains("emptyA", content);
            Assert.DoesNotContain("kept", content);
        }

        [Fact]
        public void WriteHtmlReport_ContainsAllResults()
        {
            var results = BuildSampleResults();
            string file = Path.Combine(_dir, "out.html");
            using (var exporter = new RedExportScanResults())
            {
                exporter.WriteHtmlReport(results, file);
            }

            string content = File.ReadAllText(file);
            Assert.Contains("<html", content);
            Assert.Contains("emptyA", content);
            Assert.Contains("kept", content);
        }
    }
}
