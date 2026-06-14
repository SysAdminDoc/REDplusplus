using RED.Helper;
using Xunit;

namespace RED.Tests
{
    public class RedAssistTests
    {
        [Theory]
        [InlineData(@"\\server\share\dir", true)]        // UNC share - no Recycle Bin
        [InlineData(@"\\?\UNC\server\share\dir", true)]  // extended-length UNC
        [InlineData(@"\\?\C:\dir", false)]               // local device path, not UNC
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsNoRecycleBinPath_DetectsUncPaths(string path, bool expected)
        {
            Assert.Equal(expected, RedAssist.IsNoRecycleBinPath(path));
        }
    }
}
