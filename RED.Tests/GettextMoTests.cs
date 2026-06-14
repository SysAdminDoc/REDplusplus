using System;
using System.IO;
using SecondLanguage;
using Xunit;

namespace RED.Tests
{
    // Locks in the v1.5.18 P0 hardening of the .mo loader: a crafted or truncated
    // catalog dropped next to the portable exe must fail with a catchable
    // IOException (loader falls back to untranslated English), never an
    // IndexOutOfRangeException or out-of-bounds read.
    public class GettextMoTests
    {
        private static byte[] LeUInt32(uint v)
        {
            return new[] { (byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24) };
        }

        [Fact]
        public void Load_HeaderShorterThan28Bytes_ThrowsIOException()
        {
            var t = new GettextMOTranslation();
            Assert.Throws<IOException>(() => t.Load(new byte[10]));
        }

        [Fact]
        public void Load_EmptyBuffer_ThrowsIOException()
        {
            var t = new GettextMOTranslation();
            Assert.Throws<IOException>(() => t.Load(new byte[0]));
        }

        [Fact]
        public void Load_BadMagic_ThrowsIOException()
        {
            var buf = new byte[28];
            // Valid length but garbage magic.
            for (int i = 0; i < 28; i++) buf[i] = 0xAB;
            var t = new GettextMOTranslation();
            Assert.Throws<IOException>(() => t.Load(buf));
        }

        [Fact]
        public void Load_StringCountOutOfRange_ThrowsIOException()
        {
            // Valid magic + revision, but a pathological string count that would
            // otherwise drive an enormous loop / out-of-range table read.
            var buf = new byte[28];
            Buffer.BlockCopy(LeUInt32(0x950412de), 0, buf, 0, 4); // magic LE
            Buffer.BlockCopy(LeUInt32(0), 0, buf, 4, 4);          // revision
            Buffer.BlockCopy(LeUInt32(0xFFFFFFFF), 0, buf, 8, 4); // stringCount
            Buffer.BlockCopy(LeUInt32(28), 0, buf, 12, 4);        // msgid table
            Buffer.BlockCopy(LeUInt32(28), 0, buf, 16, 4);        // msgstr table
            var t = new GettextMOTranslation();
            Assert.Throws<IOException>(() => t.Load(buf));
        }

        [Fact]
        public void Load_TableOffsetPastEnd_ThrowsIOException()
        {
            var buf = new byte[28];
            Buffer.BlockCopy(LeUInt32(0x950412de), 0, buf, 0, 4);
            Buffer.BlockCopy(LeUInt32(0), 0, buf, 4, 4);
            Buffer.BlockCopy(LeUInt32(1), 0, buf, 8, 4);            // one string
            Buffer.BlockCopy(LeUInt32(1000), 0, buf, 12, 4);       // msgid table past EOF
            Buffer.BlockCopy(LeUInt32(1000), 0, buf, 16, 4);       // msgstr table past EOF
            var t = new GettextMOTranslation();
            Assert.Throws<IOException>(() => t.Load(buf));
        }

        [Fact]
        public void SaveThenLoad_RoundTripsStrings()
        {
            var t = new GettextMOTranslation();
            t.SetString("Hello", "Bonjour");
            byte[] saved = t.Save();

            var t2 = new GettextMOTranslation();
            t2.Load(saved); // must not throw on its own well-formed output
            Assert.Equal("Bonjour", t2.GetString("Hello"));
        }
    }
}
