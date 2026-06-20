using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace RED
{
    // Undo restore round-trips and corrupt-manifest rejection. Restore recreates
    // empty directories (lossless) and moves Move-mode entries back; a corrupt
    // manifest must return null with no throw.
    [Collection("UndoTests")]
    public sealed class UndoManagerTests : IDisposable
    {
        private readonly string _root;

        public UndoManagerTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "redpp-undo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            RuntimeData.TrustedDataDirectoryOverride = null;
            try { Directory.Delete(_root, true); } catch { }
        }

        private string WriteManifest(string json)
        {
            string path = Path.Combine(_root, "manifest.json");
            File.WriteAllText(path, json);
            return path;
        }

        [Fact]
        public void LoadManifestFromPath_CorruptJson_ReturnsNullNoThrow()
        {
            string path = WriteManifest("{ this is not valid json ]]");
            var m = UndoManager.LoadManifestFromPath(path);
            Assert.Null(m);
        }

        [Fact]
        public void LoadManifestFromPath_MissingFile_ReturnsNull()
        {
            var m = UndoManager.LoadManifestFromPath(Path.Combine(_root, "nope.json"));
            Assert.Null(m);
        }

        [Fact]
        public void Restore_RecreatesDeletedEmptyDirectories()
        {
            string a = Path.Combine(_root, "a");
            string b = Path.Combine(_root, "a", "b");
            // (directories do not exist yet — simulate they were deleted)
            string json =
                "{ \"timestamp\": \"2026-06-14T00:00:00\", \"deleteMode\": \"Direct\", \"entries\": [" +
                "{ \"path\": \"" + Esc(a) + "\", \"mode\": \"Direct\" }," +
                "{ \"path\": \"" + Esc(b) + "\", \"mode\": \"Direct\" } ] }";
            string path = WriteManifest(json);

            int restored, failed;
            bool ok = UndoManager.Restore(path, out restored, out failed, null);

            Assert.True(ok);
            Assert.Equal(2, restored);
            Assert.Equal(0, failed);
            Assert.True(Directory.Exists(a));
            Assert.True(Directory.Exists(b));
        }

        [Fact]
        public void Restore_RecreatesDeletedEmptyFile()
        {
            string f = Path.Combine(_root, "empty.txt");
            string json =
                "{ \"timestamp\": \"2026-06-14T00:00:00\", \"deleteMode\": \"Direct\", \"entries\": [" +
                "{ \"path\": \"" + Esc(f) + "\", \"mode\": \"Direct\", \"isFile\": true } ] }";
            string path = WriteManifest(json);

            int restored, failed;
            bool ok = UndoManager.Restore(path, out restored, out failed, null);

            Assert.True(ok);
            Assert.Equal(1, restored);
            Assert.True(File.Exists(f));
            Assert.Equal(0, new FileInfo(f).Length);
        }

        [Fact]
        public void Restore_MoveModeEntry_MovesDirectoryBack()
        {
            string original = Path.Combine(_root, "orig");
            string movedTo = Path.Combine(_root, "moved-aside");
            Directory.CreateDirectory(movedTo); // the move destination still holds the dir
            string json =
                "{ \"timestamp\": \"2026-06-14T00:00:00\", \"deleteMode\": \"Move\", \"entries\": [" +
                "{ \"path\": \"" + Esc(original) + "\", \"movedTo\": \"" + Esc(movedTo) + "\", \"mode\": \"Move\" } ] }";
            string path = WriteManifest(json);

            int restored, failed;
            bool ok = UndoManager.Restore(path, out restored, out failed, null);

            Assert.True(ok);
            Assert.True(Directory.Exists(original));
            Assert.False(Directory.Exists(movedTo));
        }

        [Fact]
        public void Restore_PathInsideRoots_IsRestored()
        {
            string inside = Path.Combine(_root, "good");
            string json =
                "{ \"timestamp\": \"2026-06-14T00:00:00\", \"deleteMode\": \"Direct\", " +
                "\"roots\": [\"" + Esc(_root) + "\"], \"entries\": [" +
                "{ \"path\": \"" + Esc(inside) + "\", \"mode\": \"Direct\" } ] }";
            string path = WriteManifest(json);

            int restored, failed;
            bool ok = UndoManager.Restore(path, out restored, out failed, null);

            Assert.True(ok);
            Assert.Equal(1, restored);
            Assert.True(Directory.Exists(inside));
        }

        [Fact]
        public void Restore_PathOutsideRoots_IsRefused()
        {
            // A tampered manifest pointing outside the cleaned tree must be rejected.
            string outside = Path.Combine(Path.GetTempPath(), "redpp-evil-" + Guid.NewGuid().ToString("N"), "x");
            string json =
                "{ \"timestamp\": \"2026-06-14T00:00:00\", \"deleteMode\": \"Direct\", " +
                "\"roots\": [\"" + Esc(_root) + "\"], \"entries\": [" +
                "{ \"path\": \"" + Esc(outside) + "\", \"mode\": \"Direct\" } ] }";
            string path = WriteManifest(json);

            int restored, failed;
            bool ok = UndoManager.Restore(path, out restored, out failed, null);

            Assert.False(ok);
            Assert.Equal(0, restored);
            Assert.Equal(1, failed);
            Assert.False(Directory.Exists(outside));
        }

        [Fact]
        public void Restore_DotDotTraversalPath_IsRefused()
        {
            // ".." traversal is refused even when the manifest records no roots.
            string evil = Path.Combine(_root, "..", "redpp-escape-" + Guid.NewGuid().ToString("N"));
            string json =
                "{ \"timestamp\": \"x\", \"deleteMode\": \"Direct\", \"entries\": [" +
                "{ \"path\": \"" + Esc(evil) + "\", \"mode\": \"Direct\" } ] }";
            string path = WriteManifest(json);

            int restored, failed;
            bool ok = UndoManager.Restore(path, out restored, out failed, null);

            Assert.False(ok);
            Assert.Equal(0, restored);
            Assert.False(Directory.Exists(Path.GetFullPath(evil)));
        }

        [Fact]
        public void Restore_RelativePath_IsRefused()
        {
            string json =
                "{ \"timestamp\": \"x\", \"deleteMode\": \"Direct\", \"entries\": [" +
                "{ \"path\": \"relative\\\\sub\", \"mode\": \"Direct\" } ] }";
            string path = WriteManifest(json);

            int restored, failed;
            bool ok = UndoManager.Restore(path, out restored, out failed, null);

            Assert.False(ok);
            Assert.Equal(0, restored);
        }

        [Fact]
        public void Restore_MoveSourceOutsideRoots_IsRefused()
        {
            // A tampered move-mode manifest naming a system path as the move SOURCE must
            // not be allowed to relocate (and thereby destroy) it.
            string original = Path.Combine(_root, "orig");
            string evilSource = Path.Combine(Path.GetTempPath(), "redpp-sys-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(evilSource);
            try
            {
                string json =
                    "{ \"timestamp\": \"x\", \"deleteMode\": \"Move\", \"roots\": [\"" + Esc(_root) + "\"], \"entries\": [" +
                    "{ \"path\": \"" + Esc(original) + "\", \"movedTo\": \"" + Esc(evilSource) + "\", \"mode\": \"Move\" } ] }";
                string path = WriteManifest(json);

                int restored, failed;
                bool ok = UndoManager.Restore(path, out restored, out failed, null);

                Assert.False(ok);
                Assert.Equal(0, restored);
                Assert.True(Directory.Exists(evilSource)); // not relocated/destroyed
                Assert.False(Directory.Exists(original));
            }
            finally
            {
                try { Directory.Delete(evilSource, true); } catch { }
            }
        }

        [Fact]
        public void WriteManifest_UsesTrustedPerUserStore()
        {
            string trusted = Path.Combine(_root, "trusted-store");
            RuntimeData.TrustedDataDirectoryOverride = trusted;

            string restoredPath = Path.Combine(_root, "restored");
            UndoManager.WriteManifest(
                "Direct",
                new[]
                {
                    new UndoManager.ManifestEntry { Path = restoredPath, Mode = "Direct" }
                },
                new[] { _root },
                null);

            Assert.StartsWith(trusted, UndoManager.ManifestPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(UndoManager.ManifestPath));
            Assert.Single(UndoManager.ListManifests());
        }

        [Fact]
        public void Restore_ExplicitLegacyManifestOutsideUserProfile_IsRefused()
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string root = Path.GetPathRoot(profile);
            string outside = Path.Combine(root, "redpp-outside-profile-" + Guid.NewGuid().ToString("N"), "x");
            string json =
                "{ \"timestamp\": \"x\", \"deleteMode\": \"Direct\", \"entries\": [" +
                "{ \"path\": \"" + Esc(outside) + "\", \"mode\": \"Direct\" } ] }";
            string path = WriteManifest(json);

            var logs = new List<string>();
            int restored, failed;
            bool ok = UndoManager.Restore(path, out restored, out failed, m => logs.Add(m));

            Assert.False(ok);
            Assert.Equal(0, restored);
            Assert.Equal(0, failed);
            Assert.Contains(logs, l => l != null && l.Contains("safe profile boundary"));
            Assert.False(Directory.Exists(outside));
        }

        [Fact]
        public void Restore_ExplicitLegacyManifestTargetingStartup_IsRefused()
        {
            string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (string.IsNullOrWhiteSpace(startup))
            {
                return;
            }

            string target = Path.Combine(startup, "redpp-startup-" + Guid.NewGuid().ToString("N"));
            string json =
                "{ \"timestamp\": \"x\", \"deleteMode\": \"Direct\", \"entries\": [" +
                "{ \"path\": \"" + Esc(target) + "\", \"mode\": \"Direct\" } ] }";
            string path = WriteManifest(json);

            var logs = new List<string>();
            int restored, failed;
            bool ok = UndoManager.Restore(path, out restored, out failed, m => logs.Add(m));

            Assert.False(ok);
            Assert.Equal(0, restored);
            Assert.Contains(logs, l => l != null && l.Contains("safe profile boundary"));
            Assert.False(Directory.Exists(target));
        }

        [Fact]
        public void Restore_NoRoots_SystemDirectoryTarget_IsRefused()
        {
            // A manifest that omits "roots" (legacy or stripped by a tamperer) must still
            // be refused from a well-known system location — not merely fail on an OS
            // access-denied. We assert the explicit refusal, not just the side effect.
            string sysTarget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "redpp-evil-" + Guid.NewGuid().ToString("N"));
            string json =
                "{ \"timestamp\": \"x\", \"deleteMode\": \"Direct\", \"entries\": [" +
                "{ \"path\": \"" + Esc(sysTarget) + "\", \"mode\": \"Direct\" } ] }";
            string path = WriteManifest(json);

            var logs = new List<string>();
            int restored, failed;
            bool ok = UndoManager.Restore(path, out restored, out failed, m => logs.Add(m));

            Assert.False(ok);
            Assert.Equal(0, restored);
            Assert.Contains(logs, l => l != null && l.Contains("Refused"));
            Assert.False(Directory.Exists(sysTarget));
        }

        [Fact]
        public void Restore_DevicePrefixPath_IsRefused()
        {
            string evil = @"\\?\C:\Windows\Temp\redpp-evil-" + Guid.NewGuid().ToString("N");
            string json =
                "{ \"timestamp\": \"x\", \"deleteMode\": \"Direct\", \"entries\": [" +
                "{ \"path\": \"" + Esc(evil) + "\", \"mode\": \"Direct\" } ] }";
            string path = WriteManifest(json);

            var logs = new List<string>();
            int restored, failed;
            bool ok = UndoManager.Restore(path, out restored, out failed, m => logs.Add(m));

            Assert.False(ok);
            Assert.Equal(0, restored);
            Assert.Contains(logs, l => l != null && l.Contains("Refused"));
        }

        [Fact]
        public void Restore_EmptyManifest_ReturnsFalse()
        {
            string json = "{ \"timestamp\": \"x\", \"deleteMode\": \"Direct\", \"entries\": [] }";
            string path = WriteManifest(json);
            int restored, failed;
            bool ok = UndoManager.Restore(path, out restored, out failed, null);
            Assert.False(ok);
            Assert.Equal(0, restored);
        }

        private static string Esc(string s) { return s.Replace("\\", "\\\\"); }
    }
}
