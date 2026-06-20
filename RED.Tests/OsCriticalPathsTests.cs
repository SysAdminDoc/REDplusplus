using System;
using System.IO;
using RED;
using Xunit;

namespace RED.Tests
{
    public class OsCriticalPathsTests
    {
        private static readonly string SystemDrive;

        static OsCriticalPathsTests()
        {
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string root = Path.GetPathRoot(winDir);
            SystemDrive = string.IsNullOrEmpty(root) ? "C:" : root.TrimEnd(Path.DirectorySeparatorChar);
        }

        [Fact]
        public void Inetpub_IsProtected()
        {
            Assert.True(OsCriticalPaths.IsProtected(SystemDrive + @"\inetpub"));
        }

        [Fact]
        public void PerfLogs_IsProtected()
        {
            Assert.True(OsCriticalPaths.IsProtected(SystemDrive + @"\PerfLogs"));
        }

        [Fact]
        public void ConfigMsi_IsProtected()
        {
            Assert.True(OsCriticalPaths.IsProtected(SystemDrive + @"\Config.Msi"));
        }

        [Fact]
        public void Recovery_IsProtected()
        {
            Assert.True(OsCriticalPaths.IsProtected(SystemDrive + @"\Recovery"));
        }

        [Fact]
        public void CaseInsensitive()
        {
            Assert.True(OsCriticalPaths.IsProtected(SystemDrive + @"\INETPUB"));
            Assert.True(OsCriticalPaths.IsProtected(SystemDrive + @"\perflogs"));
            Assert.True(OsCriticalPaths.IsProtected(SystemDrive + @"\config.msi"));
        }

        [Fact]
        public void TrailingSeparator_StillProtected()
        {
            Assert.True(OsCriticalPaths.IsProtected(SystemDrive + @"\inetpub\"));
        }

        [Fact]
        public void DifferentDrive_NotProtected()
        {
            string otherDrive = SystemDrive == "C:" ? "D:" : "C:";
            Assert.False(OsCriticalPaths.IsProtected(otherDrive + @"\inetpub"));
        }

        [Fact]
        public void SubdirectoryOfProtected_NotBlocked()
        {
            Assert.False(OsCriticalPaths.IsProtected(SystemDrive + @"\inetpub\wwwroot"));
        }

        [Fact]
        public void NormalDirectory_NotProtected()
        {
            Assert.False(OsCriticalPaths.IsProtected(SystemDrive + @"\Users\Public"));
            Assert.False(OsCriticalPaths.IsProtected(@"D:\Projects\inetpub"));
        }

        [Fact]
        public void NullOrEmpty_NotProtected()
        {
            Assert.False(OsCriticalPaths.IsProtected((string)null));
            Assert.False(OsCriticalPaths.IsProtected(""));
        }

        [Fact]
        public void DirectoryInfo_Overload()
        {
            Assert.True(OsCriticalPaths.IsProtected(new DirectoryInfo(SystemDrive + @"\inetpub")));
            Assert.False(OsCriticalPaths.IsProtected(new DirectoryInfo(SystemDrive + @"\Temp")));
            Assert.False(OsCriticalPaths.IsProtected((DirectoryInfo)null));
        }
    }
}
