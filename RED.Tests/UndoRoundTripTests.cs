using System;
using System.Collections.Generic;
using System.IO;
using RED;
using Xunit;

namespace RED.Tests
{
    [Collection("UndoTests")]
    public sealed class UndoRoundTripTests : IDisposable
    {
        private readonly string _root;
        private readonly string _undoStore;
        private readonly string _oldTrustedOverride;

        public UndoRoundTripTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "redpp-undo-" + Guid.NewGuid().ToString("N"));
            _undoStore = Path.Combine(_root, "_undo_store");
            Directory.CreateDirectory(_root);
            _oldTrustedOverride = RuntimeData.TrustedDataDirectoryOverride;
            RuntimeData.TrustedDataDirectoryOverride = _undoStore;
        }

        public void Dispose()
        {
            RuntimeData.TrustedDataDirectoryOverride = _oldTrustedOverride;
            try { Directory.Delete(_root, true); } catch { }
        }

        [Fact]
        public void DirectMode_DeleteThenUndo_RestoresDirectories()
        {
            string emptyA = Path.Combine(_root, "tree", "emptyA");
            string emptyB = Path.Combine(_root, "tree", "sub", "emptyB");
            string nonEmpty = Path.Combine(_root, "tree", "kept");

            Directory.CreateDirectory(emptyA);
            Directory.CreateDirectory(emptyB);
            Directory.CreateDirectory(nonEmpty);
            File.WriteAllText(Path.Combine(nonEmpty, "content.txt"), "keep");

            Assert.True(Directory.Exists(emptyA));
            Assert.True(Directory.Exists(emptyB));

            var entries = new List<UndoManager.ManifestEntry>();

            SystemFunctions.SecureDeleteDirectory(emptyB, DeleteModes.Direct);
            entries.Add(new UndoManager.ManifestEntry
            {
                Path = emptyB,
                Mode = DeleteModes.Direct.ToString()
            });

            SystemFunctions.SecureDeleteDirectory(emptyA, DeleteModes.Direct);
            entries.Add(new UndoManager.ManifestEntry
            {
                Path = emptyA,
                Mode = DeleteModes.Direct.ToString()
            });

            Assert.False(Directory.Exists(emptyA));
            Assert.False(Directory.Exists(emptyB));
            Assert.True(Directory.Exists(nonEmpty));
            Assert.True(File.Exists(Path.Combine(nonEmpty, "content.txt")));

            string scanRoot = Path.Combine(_root, "tree");
            UndoManager.WriteManifest(
                DeleteModes.Direct.ToString(),
                entries,
                new[] { scanRoot },
                null);

            int restored, failed;
            bool ok = UndoManager.Restore(out restored, out failed, null);

            Assert.True(ok);
            Assert.Equal(2, restored);
            Assert.Equal(0, failed);
            Assert.True(Directory.Exists(emptyA));
            Assert.True(Directory.Exists(emptyB));
            Assert.True(File.Exists(Path.Combine(nonEmpty, "content.txt")));
        }

        [Fact]
        public void MoveMode_DeleteThenUndo_MovesBack()
        {
            string moveTarget = Path.Combine(_root, "moved");
            string emptyDir = Path.Combine(_root, "tree", "emptyMove");

            Directory.CreateDirectory(emptyDir);
            Directory.CreateDirectory(moveTarget);

            string movedTo = Path.Combine(moveTarget, "emptyMove");
            Directory.Move(emptyDir, movedTo);

            Assert.False(Directory.Exists(emptyDir));
            Assert.True(Directory.Exists(movedTo));

            var entries = new List<UndoManager.ManifestEntry>
            {
                new UndoManager.ManifestEntry
                {
                    Path = emptyDir,
                    MovedTo = movedTo,
                    Mode = DeleteModes.MoveToFolder.ToString()
                }
            };

            string scanRoot = Path.Combine(_root, "tree");
            UndoManager.WriteManifest(
                DeleteModes.MoveToFolder.ToString(),
                entries,
                new[] { scanRoot, moveTarget },
                null);

            int restored, failed;
            bool ok = UndoManager.Restore(out restored, out failed, null);

            Assert.True(ok);
            Assert.Equal(1, restored);
            Assert.Equal(0, failed);
            Assert.True(Directory.Exists(emptyDir));
            Assert.False(Directory.Exists(movedTo));
        }
    }
}
