using System;
using SecondLanguage;
using Xunit;

namespace RED.Tests
{
    // Locks in the v1.5.18 P0 hardening of the Plural-Forms parser: a crafted
    // catalog header must never crash the process (StackOverflow from deep
    // nesting is uncatchable) and must never throw at TranslatePlural time
    // (divide/modulo by zero deferred to evaluation).
    public class GettextPluralTests
    {
        [Fact]
        public void Parse_EnglishRule_TwoPlurals()
        {
            int n;
            GettextPluralConverterFunc f;
            GettextPluralParser.Parse("nplurals=2; plural=(n != 1);", out n, out f);
            Assert.Equal(2, n);
            Assert.Equal(0, f(1));   // singular
            Assert.Equal(1, f(0));   // plural
            Assert.Equal(1, f(2));
        }

        [Fact]
        public void Parse_PolishRule_ThreePlurals()
        {
            int n;
            GettextPluralConverterFunc f;
            GettextPluralParser.Parse(
                "nplurals=3; plural=(n==1 ? 0 : n%10>=2 && n%10<=4 && (n%100<10 || n%100>=20) ? 1 : 2);",
                out n, out f);
            Assert.Equal(3, n);
            Assert.Equal(0, f(1));
            Assert.Equal(1, f(2));
            Assert.Equal(2, f(5));
        }

        [Fact]
        public void Parse_ModuloByZero_DoesNotThrowAtEvaluation()
        {
            int n;
            GettextPluralConverterFunc f;
            GettextPluralParser.Parse("nplurals=2; plural=(n%0);", out n, out f);
            // Evaluation must be safe: n%0 is treated as 0, never DivideByZeroException.
            int idx = f(5);
            Assert.Equal(0, idx);
        }

        [Fact]
        public void Parse_DivideByZero_DoesNotThrowAtEvaluation()
        {
            int n;
            GettextPluralConverterFunc f;
            GettextPluralParser.Parse("nplurals=2; plural=(n/0);", out n, out f);
            Assert.Equal(0, f(7));
        }

        [Fact]
        public void Parse_DeeplyNestedParens_ThrowsFormatExceptionNotStackOverflow()
        {
            int n;
            GettextPluralConverterFunc f;
            string deep = "nplurals=2; plural=" + new string('(', 5000) + "n" + new string(')', 5000) + ";";
            // A catchable FormatException (too long / too deep), never a process kill.
            Assert.Throws<FormatException>(() => GettextPluralParser.Parse(deep, out n, out f));
        }

        [Fact]
        public void Parse_MissingNplurals_ThrowsFormatException()
        {
            int n;
            GettextPluralConverterFunc f;
            Assert.Throws<FormatException>(() => GettextPluralParser.Parse("plural=(n != 1);", out n, out f));
        }
    }
}
