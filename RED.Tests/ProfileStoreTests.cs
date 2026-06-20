using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace RED
{
    // Round-trips a saved profile through the JSON store, including the nullable
    // override fields, then removes it. Verifies the headless -profile feature's
    // persistence layer.
    public class ProfileStoreTests
    {
        [Fact]
        public void SaveGetDelete_RoundTripsAllFields()
        {
            string name = "redpp-test-" + System.Guid.NewGuid().ToString("N");
            var profile = new RedProfile
            {
                Name = name,
                Paths = new List<string> { @"C:\one", @"D:\two" },
                Mode = "recycle",
                MoveTo = null,
                EmptyFiles = true,
                MinAgeHours = 24,
                MaxDepth = 5,
                GitIgnore = true,
                Mft = false,
                IgnoreHidden = null,
                IgnoreSystem = true
            };

            try
            {
                string error;
                Assert.True(ProfileStore.Save(profile, out error), error);

                RedProfile loaded = ProfileStore.Get(name);
                Assert.NotNull(loaded);
                Assert.Equal(new[] { @"C:\one", @"D:\two" }, loaded.Paths.ToArray());
                Assert.Equal("recycle", loaded.Mode);
                Assert.True(loaded.EmptyFiles);
                Assert.Equal(24, loaded.MinAgeHours);
                Assert.Equal(5, loaded.MaxDepth);
                Assert.True(loaded.GitIgnore);
                Assert.False(loaded.Mft);
                Assert.Null(loaded.IgnoreHidden);
                Assert.True(loaded.IgnoreSystem);
            }
            finally
            {
                ProfileStore.Delete(name);
            }

            Assert.Null(ProfileStore.Get(name));
        }

        [Fact]
        public void SaveGetDelete_RoundTripsExtendedFields()
        {
            string name = "redpp-ext-" + System.Guid.NewGuid().ToString("N");
            var profile = new RedProfile
            {
                Name = name,
                Paths = new List<string> { @"\\server\share" },
                Mode = "direct",
                EmptyFiles = false,
                Lockout = true,
                Parallel = 8,
                Exclude = new List<string> { "temp", "cache" },
                Protect = new List<string> { ".git", ".svn" }
            };

            try
            {
                string error;
                Assert.True(ProfileStore.Save(profile, out error), error);

                RedProfile loaded = ProfileStore.Get(name);
                Assert.NotNull(loaded);
                Assert.Equal("direct", loaded.Mode);
                Assert.True(loaded.Lockout);
                Assert.Equal(8, loaded.Parallel);
                Assert.Equal(new[] { "temp", "cache" }, loaded.Exclude.ToArray());
                Assert.Equal(new[] { ".git", ".svn" }, loaded.Protect.ToArray());
            }
            finally
            {
                ProfileStore.Delete(name);
            }

            Assert.Null(ProfileStore.Get(name));
        }

        [Fact]
        public void Get_UnknownName_ReturnsNull()
        {
            Assert.Null(ProfileStore.Get("redpp-nonexistent-" + System.Guid.NewGuid().ToString("N")));
        }

        [Fact]
        public void Save_BlankName_Fails()
        {
            string error;
            Assert.False(ProfileStore.Save(new RedProfile { Name = " " }, out error));
        }
    }
}
