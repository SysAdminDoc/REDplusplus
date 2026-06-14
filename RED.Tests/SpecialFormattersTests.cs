using SecondLanguage;
using Xunit;

namespace RED.Tests
{
    // The C printf formatter clamps width/precision so a crafted format string
    // (e.g. "%9999999d") cannot drive a multi-megabyte allocation.
    public class SpecialFormattersTests
    {
        [Fact]
        public void C_NormalWidth_PadsCorrectly()
        {
            Assert.Equal("   42", SpecialFormatters.C("%5d", 42));
        }

        [Fact]
        public void C_HugeWidth_ClampedNotMegabytes()
        {
            string s = SpecialFormatters.C("%9999999d", 5);
            // Clamped to the 8192 ceiling instead of ~10 million chars.
            Assert.True(s.Length <= 8192, "width was not clamped: length=" + s.Length);
            Assert.EndsWith("5", s);
        }

        [Fact]
        public void C_HugeFloatPrecision_DoesNotExplode()
        {
            string s = SpecialFormatters.C("%.9999999f", 1.5);
            Assert.True(s.Length <= 8192 + 16, "precision was not clamped: length=" + s.Length);
        }

        [Fact]
        public void C_PercentLiteral_Unchanged()
        {
            Assert.Equal("100%", SpecialFormatters.C("100%%"));
        }
    }
}
