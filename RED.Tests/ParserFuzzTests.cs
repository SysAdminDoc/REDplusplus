using System;
using System.IO;
using System.Text;
using RED;
using SecondLanguage;
using Xunit;

namespace RED.Tests
{
    // Randomized + mutation-based fuzz/property tests for the three parsers that read
    // untrusted on-disk bytes: the NTFS USN/MFT record reader, the gettext .mo catalog
    // reader, and the undo-manifest JSON reader. v1.5.18 fixed several crashers by hand;
    // these feed thousands of pure-random and structured-mutation inputs and assert each
    // parser's bounded contract (never an unexpected unhandled exception). The seed is
    // fixed so a failure is reproducible.
    public sealed class ParserFuzzTests : IDisposable
    {
        private const int Seed = 0x5ED1E5;
        private const int Iterations = 3000;
        private readonly string _dir;

        public ParserFuzzTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "redpp-fuzz-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private static byte[] Mutate(Random rng, byte[] seedInput)
        {
            // Half the time mutate a known-valid input (finds boundary bugs); otherwise
            // emit pure-random bytes of a random length (finds gross out-of-bounds bugs).
            if (seedInput != null && seedInput.Length > 0 && rng.Next(2) == 0)
            {
                var buf = (byte[])seedInput.Clone();
                int ops = 1 + rng.Next(6);
                for (int i = 0; i < ops; i++)
                {
                    switch (rng.Next(4))
                    {
                        case 0: buf[rng.Next(buf.Length)] = (byte)rng.Next(256); break;            // bit/byte flip
                        case 1: // overwrite a 4-byte little-endian field with an extreme value
                            int o = rng.Next(Math.Max(1, buf.Length - 4));
                            uint v = rng.Next(3) == 0 ? 0xFFFFFFFF : (uint)rng.Next();
                            for (int k = 0; k < 4 && o + k < buf.Length; k++) buf[o + k] = (byte)(v >> (8 * k));
                            break;
                        case 2: // truncate
                            Array.Resize(ref buf, rng.Next(buf.Length + 1));
                            break;
                        case 3: // append random tail
                            int add = rng.Next(32);
                            int old = buf.Length;
                            Array.Resize(ref buf, old + add);
                            for (int k = 0; k < add; k++) buf[old + k] = (byte)rng.Next(256);
                            break;
                    }
                    if (buf.Length == 0) break;
                }
                return buf;
            }

            var rnd = new byte[rng.Next(0, 4096)];
            rng.NextBytes(rnd);
            return rnd;
        }

        [Fact]
        public void MftParseRecords_ArbitraryBytes_NeverThrows()
        {
            var rng = new Random(Seed);
            byte[] valid = BuildValidUsnPage();
            for (int i = 0; i < Iterations; i++)
            {
                byte[] buf = Mutate(rng, valid);
                // Also fuzz the claimed length independently of the real buffer length.
                int claimed = rng.Next(3) == 0 ? rng.Next(-8, buf.Length + 64) : buf.Length;
                var scanner = new MftScanner();
                int count = 0;
                Exception ex = Record.Exception(() => scanner.ParseRecords(buf, claimed, ref count, null));
                Assert.True(ex == null, "ParseRecords threw on fuzzed input (len=" + buf.Length + ", claimed=" + claimed + "): " + ex);
            }
        }

        [Fact]
        public void GettextMoLoad_ArbitraryBytes_OnlyThrowsIOException()
        {
            var rng = new Random(Seed + 1);
            byte[] valid = BuildValidMo();
            for (int i = 0; i < Iterations; i++)
            {
                byte[] buf = Mutate(rng, valid);
                Exception ex = Record.Exception(() => new GettextMOTranslation().Load(buf));
                Assert.True(ex == null || ex is IOException,
                    "Load threw an unexpected " + (ex == null ? "null" : ex.GetType().Name) + " on fuzzed input (len=" + buf.Length + ")");
            }
        }

        [Fact]
        public void UndoManifestLoad_ArbitraryBytes_NeverThrows()
        {
            var rng = new Random(Seed + 2);
            byte[] valid = Encoding.UTF8.GetBytes("{ \"timestamp\": \"x\", \"deleteMode\": \"Direct\", \"roots\": [\"C:\\\\x\"], \"entries\": [ { \"path\": \"C:\\\\x\\\\a\", \"mode\": \"Direct\" } ] }");
            string file = Path.Combine(_dir, "m.json");
            for (int i = 0; i < 800; i++)
            {
                byte[] buf = Mutate(rng, valid);
                File.WriteAllBytes(file, buf);
                Exception ex = Record.Exception(() => UndoManager.LoadManifestFromPath(file));
                Assert.True(ex == null, "LoadManifestFromPath threw on fuzzed input (len=" + buf.Length + "): " + ex);
            }
        }

        [Fact]
        public void GettextMo_RoundTrip_PreservesRandomStrings()
        {
            // Property: SetString(k, v) -> Save -> Load -> GetString(k) == v, for random
            // unicode-ish keys/values (the loader must read back its own well-formed output).
            var rng = new Random(Seed + 3);
            for (int i = 0; i < 300; i++)
            {
                string key = RandomString(rng, 1, 40);
                string val = RandomString(rng, 0, 60);
                var t = new GettextMOTranslation();
                t.SetString(key, val);
                byte[] saved = t.Save();

                var t2 = new GettextMOTranslation();
                t2.Load(saved);
                Assert.Equal(val, t2.GetString(key));
            }
        }

        private static string RandomString(Random rng, int min, int max)
        {
            int len = rng.Next(min, max + 1);
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++)
            {
                // Printable BMP range, skipping control + surrogate code points.
                char c;
                do { c = (char)rng.Next(0x20, 0xD7FF); } while (char.IsControl(c));
                sb.Append(c);
            }
            return sb.ToString();
        }

        // --- valid-seed builders (mirror the on-disk formats the parsers expect) ---

        private static void WriteU16(byte[] b, int o, ushort v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
        private static void WriteU32(byte[] b, int o, uint v) { for (int i = 0; i < 4; i++) b[o + i] = (byte)(v >> (8 * i)); }
        private static void WriteU64(byte[] b, int o, ulong v) { for (int i = 0; i < 8; i++) b[o + i] = (byte)(v >> (8 * i)); }

        private static byte[] BuildV2(ulong frn, ulong parent, uint attribs, string name)
        {
            byte[] nameBytes = Encoding.Unicode.GetBytes(name);
            int recordLength = 60 + nameBytes.Length;
            var r = new byte[recordLength];
            WriteU32(r, 0, (uint)recordLength);
            WriteU16(r, 4, 2);
            WriteU64(r, 8, frn);
            WriteU64(r, 16, parent);
            WriteU32(r, 52, attribs);
            WriteU16(r, 56, (ushort)nameBytes.Length);
            WriteU16(r, 58, 60);
            Array.Copy(nameBytes, 0, r, 60, nameBytes.Length);
            return r;
        }

        private static byte[] BuildValidUsnPage()
        {
            byte[] a = BuildV2(10, 0, 0x10, "sub");
            byte[] b = BuildV2(20, 10, 0, "file.txt");
            var buf = new byte[8 + a.Length + b.Length];
            Array.Copy(a, 0, buf, 8, a.Length);
            Array.Copy(b, 0, buf, 8 + a.Length, b.Length);
            return buf;
        }

        private static byte[] BuildValidMo()
        {
            var t = new GettextMOTranslation();
            t.SetString("Hello", "Bonjour");
            t.SetString("World", "Monde");
            return t.Save();
        }
    }
}
