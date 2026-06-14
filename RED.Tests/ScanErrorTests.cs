using System;
using System.IO;
using RED;
using Xunit;

namespace RED.Tests
{
    // When a directory cannot be read the scan now names the specific OS cause, so
    // the result row's Reason tells the user WHY a folder was kept instead of a
    // generic "could not be read".
    public class ScanErrorTests
    {
        [Fact]
        public void DescribeAccessError_MapsWin32ErrorCodes()
        {
            // FastDirectoryEnumerator throws Win32Exception with the raw native code.
            Assert.Equal("access denied", FindEmptyDirectoryWorker.DescribeAccessError(new System.ComponentModel.Win32Exception(5)));
            Assert.Equal("in use by another process", FindEmptyDirectoryWorker.DescribeAccessError(new System.ComponentModel.Win32Exception(32)));
            Assert.Equal("in use by another process", FindEmptyDirectoryWorker.DescribeAccessError(new System.ComponentModel.Win32Exception(33)));
            Assert.Equal("path no longer exists", FindEmptyDirectoryWorker.DescribeAccessError(new System.ComponentModel.Win32Exception(3)));
            Assert.Equal("path too long", FindEmptyDirectoryWorker.DescribeAccessError(new System.ComponentModel.Win32Exception(206)));
            Assert.Equal("I/O error", FindEmptyDirectoryWorker.DescribeAccessError(new System.ComponentModel.Win32Exception(1234)));
        }

        [Fact]
        public void DescribeAccessError_NamesCommonCauses()
        {
            Assert.Equal("access denied", FindEmptyDirectoryWorker.DescribeAccessError(new UnauthorizedAccessException()));
            Assert.Equal("path too long", FindEmptyDirectoryWorker.DescribeAccessError(new PathTooLongException()));
            Assert.Equal("path no longer exists", FindEmptyDirectoryWorker.DescribeAccessError(new DirectoryNotFoundException()));
            Assert.Equal("in use by another process", FindEmptyDirectoryWorker.DescribeAccessError(new IOException("locked", unchecked((int)0x80070020))));
            Assert.Equal("I/O error", FindEmptyDirectoryWorker.DescribeAccessError(new IOException("generic")));
        }

        [Fact]
        public void DescribeAccessError_UnknownOrNull_ReturnsNull()
        {
            Assert.Null(FindEmptyDirectoryWorker.DescribeAccessError(null));
            Assert.Null(FindEmptyDirectoryWorker.DescribeAccessError(new ArgumentException("x")));
        }
    }
}
