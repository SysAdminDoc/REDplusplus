using RED.Helper;
using Xunit;

namespace RED.Tests
{
    // CSV export hardening: directory names are fully attacker-controllable, so a
    // cell that a spreadsheet would evaluate as a formula (CWE-1236) is neutralized
    // with a leading single quote — including when whitespace precedes the trigger.
    public class RedExportTests
    {
        [Theory]
        [InlineData("=1+1", "'=1+1")]
        [InlineData("+cmd|'/c calc'!A1", "'+cmd|'/c calc'!A1")]
        [InlineData("@SUM(1)", "'@SUM(1)")]
        [InlineData("-2+3", "'-2+3")]
        [InlineData(" =1+1", "' =1+1")]      // leading space then formula — still neutralized
        [InlineData("   @x", "'   @x")]      // multiple leading spaces
        [InlineData("\t=bad", "'\t=bad")]    // leading tab
        [InlineData("normal name", "normal name")]
        [InlineData("C:\\path\\folder", "C:\\path\\folder")]
        public void EscapeCsvCell_NeutralizesFormulaInjection(string input, string expected)
        {
            Assert.Equal(expected, RedExportScanResults.EscapeCsvCell(input));
        }
    }
}
