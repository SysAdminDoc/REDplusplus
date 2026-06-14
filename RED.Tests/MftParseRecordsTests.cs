using System;
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
    }
}
