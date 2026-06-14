using System;
using System.IO;
using NotBob.Config;
using RED.Config;
using Xunit;

namespace RED.Tests
{
    // Config schema versioning: an older-schema file is upgraded to the current version
    // and the original is backed up; a current/newer file is left untouched.
    public sealed class ConfigMigrationTests : IDisposable
    {
        private readonly string _dir;

        public ConfigMigrationTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "redpp-cfg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        [Fact]
        public void MigrateIfNeeded_OldSchema_BumpsVersionAndBacksUp()
        {
            var config = new RedConfiguration { SchemaVersion = 0 }; // pre-versioning file
            string file = Path.Combine(_dir, "RED+.cfg");
            File.WriteAllText(file, "<RED.PLUS />");

            bool migrated = ConfigAssist.MigrateIfNeeded(config, file);

            Assert.True(migrated);
            Assert.Equal(RedConfiguration.CurrentSchemaVersion, config.SchemaVersion);
            Assert.True(File.Exists(file + ".v0.bak"), "pre-migration file should be backed up");
        }

        [Fact]
        public void MigrateIfNeeded_CurrentSchema_IsNoOp()
        {
            var config = new RedConfiguration { SchemaVersion = RedConfiguration.CurrentSchemaVersion };
            string file = Path.Combine(_dir, "RED+.cfg");
            File.WriteAllText(file, "<RED.PLUS />");

            bool migrated = ConfigAssist.MigrateIfNeeded(config, file);

            Assert.False(migrated);
            Assert.False(File.Exists(file + ".v" + RedConfiguration.CurrentSchemaVersion + ".bak"));
        }

        [Fact]
        public void MigrateIfNeeded_NewerSchema_IsNotDowngraded()
        {
            var config = new RedConfiguration { SchemaVersion = RedConfiguration.CurrentSchemaVersion + 5 };
            bool migrated = ConfigAssist.MigrateIfNeeded(config, Path.Combine(_dir, "RED+.cfg"));

            Assert.False(migrated);
            Assert.Equal(RedConfiguration.CurrentSchemaVersion + 5, config.SchemaVersion);
        }
    }
}
