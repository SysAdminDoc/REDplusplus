using System;
using System.Text;
using Xunit;

namespace RED
{
    // Locks in the v1.5.18 P0 bounds-hardening of the USN/MFT record parser.
    // A corrupt or truncated USN buffer (snapshot race, partial read, crafted
    // volume) must never read out of bounds or throw — ParseRecords either skips
    // the bad record or stops cleanly.
    public class MftParseRecordsTests
    {
        private static void WriteInt32(byte[] buf, int offset, int value)
        {
            buf[offset + 0] = (byte)value;
            buf[offset + 1] = (byte)(value >> 8);
            buf[offset + 2] = (byte)(value >> 16);
            buf[offset + 3] = (byte)(value >> 24);
        }

        [Fact]
        public void ParseRecords_EmptyBuffer_NoThrow()
        {
            var s = new MftScanner();
            int count = 0;
            s.ParseRecords(new byte[8], 8, ref count, null);
            Assert.Equal(0, s.ParsedEntryCount);
        }

        [Fact]
        public void ParseRecords_RecordLengthLargerThanBuffer_StopsCleanly()
        {
            // First 8 bytes are the USN page header (skipped); record starts at 8.
            var buf = new byte[64];
            WriteInt32(buf, 8, 1000000); // recordLength far exceeds bytesReturned
            var s = new MftScanner();
            int count = 0;
            s.ParseRecords(buf, buf.Length, ref count, null);
            Assert.Equal(0, s.ParsedEntryCount);
        }

        [Fact]
        public void ParseRecords_NegativeRecordLength_StopsCleanly()
        {
            var buf = new byte[64];
            WriteInt32(buf, 8, -1);
            var s = new MftScanner();
            int count = 0;
            s.ParseRecords(buf, buf.Length, ref count, null);
            Assert.Equal(0, s.ParsedEntryCount);
        }

        [Fact]
        public void ParseRecords_RecordSmallerThanHeader_StopsCleanly()
        {
            var buf = new byte[64];
            WriteInt32(buf, 8, 10); // < V2 header (60) and < V3 header (76)
            var s = new MftScanner();
            int count = 0;
            s.ParseRecords(buf, buf.Length, ref count, null);
            Assert.Equal(0, s.ParsedEntryCount);
        }

        [Fact]
        public void ParseRecords_OversizedRecordLengthNearIntMax_NoOverflowRead()
        {
            // A huge recordLength must not wrap past int.MaxValue and slip the
            // in-buffer bounds check (the long-widening guard).
            var buf = new byte[128];
            WriteInt32(buf, 8, int.MaxValue);
            var s = new MftScanner();
            int count = 0;
            s.ParseRecords(buf, buf.Length, ref count, null);
            Assert.Equal(0, s.ParsedEntryCount);
        }

        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

        private static void WriteU16(byte[] b, int o, ushort v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
        private static void WriteU32(byte[] b, int o, uint v) { for (int i = 0; i < 4; i++) b[o + i] = (byte)(v >> (8 * i)); }
        private static void WriteU64(byte[] b, int o, ulong v) { for (int i = 0; i < 8; i++) b[o + i] = (byte)(v >> (8 * i)); }

        // Builds a USN_RECORD_V2 (64-bit FRN) record.
        private static byte[] BuildV2(ulong frn, ulong parent, uint attribs, string name)
        {
            byte[] nameBytes = Encoding.Unicode.GetBytes(name);
            int recordLength = 60 + nameBytes.Length;
            var r = new byte[recordLength];
            WriteU32(r, 0, (uint)recordLength);
            WriteU16(r, 4, 2);              // MajorVersion = 2
            WriteU64(r, 8, frn);
            WriteU64(r, 16, parent);
            WriteU32(r, 52, attribs);
            WriteU16(r, 56, (ushort)nameBytes.Length);
            WriteU16(r, 58, 60);           // FileNameOffset
            Array.Copy(nameBytes, 0, r, 60, nameBytes.Length);
            return r;
        }

        private static byte[] Page(params byte[][] records)
        {
            int total = 8; // leading next-FRN cursor
            foreach (var r in records) total += r.Length;
            var buf = new byte[total];
            int o = 8;
            foreach (var r in records) { Array.Copy(r, 0, buf, o, r.Length); o += r.Length; }
            return buf;
        }

        [Fact]
        public void ParseRecords_ValidV2Records_PopulatesEntries()
        {
            // root(frn 0) -> dir(10) -> file(20)
            var buf = Page(
                BuildV2(10, 0, FILE_ATTRIBUTE_DIRECTORY, "sub"),
                BuildV2(20, 10, 0, "file.txt"));
            var s = new MftScanner();
            int count = 0;
            s.ParseRecords(buf, buf.Length, ref count, null);
            Assert.Equal(2, s.ParsedEntryCount);
            Assert.Equal(2, count);
        }

        [Fact]
        public void IsEnumerationConsistent_AllParentsPresent_True()
        {
            var buf = Page(
                BuildV2(10, 0, FILE_ATTRIBUTE_DIRECTORY, "sub"),  // parent = root (0)
                BuildV2(20, 10, 0, "file.txt"));                  // parent = 10 (present)
            var s = new MftScanner();
            int count = 0;
            s.ParseRecords(buf, buf.Length, ref count, null);
            Assert.True(s.IsEnumerationConsistent());
        }

        [Fact]
        public void IsEnumerationConsistent_DanglingParent_FalseFailClosed()
        {
            // A file whose parent directory record (99) was dropped: the parent is
            // referenced but absent -> enumeration is incomplete -> fail closed.
            var buf = Page(BuildV2(20, 99, 0, "orphan.txt"));
            var s = new MftScanner();
            int count = 0;
            s.ParseRecords(buf, buf.Length, ref count, null);
            Assert.False(s.IsEnumerationConsistent());
        }
    }
}
