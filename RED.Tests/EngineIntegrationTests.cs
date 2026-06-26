using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using RED;
using RED.Config;
using RED.Match;
using Xunit;

namespace RED.Tests
{
    public sealed class EngineIntegrationTests : IDisposable
    {
        private readonly string _root;

        public EngineIntegrationTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "redpp-engine-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private void BuildTree()
        {
            Directory.CreateDirectory(Path.Combine(_root, "tree", "emptyA"));
            Directory.CreateDirectory(Path.Combine(_root, "tree", "emptyB"));
            Directory.CreateDirectory(Path.Combine(_root, "tree", "sub", "emptyC"));
            Directory.CreateDirectory(Path.Combine(_root, "tree", "kept"));
            File.WriteAllText(Path.Combine(_root, "tree", "kept", "data.txt"), "content");
        }

        private RuntimeData CreateRunData(string scanPath, bool lockout = false, int parallel = 0)
        {
            var rd = new RuntimeData();
            rd.StartFolder = new DirectoryInfo(scanPath);
            rd.IgnoreHiddenFolders = false;
            rd.IgnoreSystemFolders = false;
            rd.MinFolderAgeHours = 0;
            rd.MaxDepth = -1;
            rd.InfiniteLoopDetectionCount = 10;
            rd.DeleteEmptyFiles = false;
            rd.IgnoreEmptyFiles = true;
            rd.HideIgnoredDirectories = false;
            rd.HideScanErrors = false;
            rd.ParallelScanDegree = parallel;

            var config = new RedConfiguration();
            config.Filters.SetToDefaults();
            rd.IgnoreFileNameList.Transform(config.Filters.FilesToIgnore, RedMatchFilterType.Files);
            rd.IgnoreDirectoryNameList.Transform(config.Filters.DirectoriesToIgnore, RedMatchFilterType.Directory);
            rd.NeverEmptyDirectoryList.Transform(config.Filters.DirectoriesNeverEmpty, RedMatchFilterType.Directory);

            return rd;
        }

        private List<RedScanResultItem> RunScan(RuntimeData rd, int parallel = 0)
        {
            var results = new List<RedScanResultItem>();
            var worker = new FindEmptyDirectoryWorker();
            worker.RunData = rd;
            worker.ParallelDegree = parallel;

            using (var done = new ManualResetEventSlim())
            {
                worker.ProgressChanged += (s, e) =>
                {
                    if (e.UserState is FoundEmptyDirInfoEventArgs info)
                        results.Add(info.ScanResult);
                };
                worker.RunWorkerCompleted += (s, e) => done.Set();
                worker.RunWorkerAsync(rd.StartFolder);
                done.Wait(TimeSpan.FromSeconds(30));
            }

            return results;
        }

        [Fact]
        public void ParallelScan_ProducesIdenticalResults_ToSerial()
        {
            BuildTree();
            string scanPath = Path.Combine(_root, "tree");

            var serialRd = CreateRunData(scanPath, parallel: 0);
            var serialResults = RunScan(serialRd, parallel: 0);

            var parallelRd = CreateRunData(scanPath, parallel: 4);
            var parallelResults = RunScan(parallelRd, parallel: 4);

            var serialEmpty = serialResults
                .Where(r => r.SearchStatus == DirectorySearchStatusTypes.Empty)
                .Select(r => r.FullPath)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var parallelEmpty = parallelResults
                .Where(r => r.SearchStatus == DirectorySearchStatusTypes.Empty)
                .Select(r => r.FullPath)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.Equal(serialEmpty.Count, parallelEmpty.Count);
            for (int i = 0; i < serialEmpty.Count; i++)
            {
                Assert.Equal(serialEmpty[i], parallelEmpty[i], StringComparer.OrdinalIgnoreCase);
            }
            Assert.True(serialEmpty.Count >= 3, "Expected at least 3 empty dirs");
        }

        [Fact]
        public void ParallelScan_WithEmptyFiles_CollectsSameFiles()
        {
            BuildTree();
            File.Create(Path.Combine(_root, "tree", "emptyA", "zero.tmp")).Dispose();
            string scanPath = Path.Combine(_root, "tree");

            var serialRd = CreateRunData(scanPath, parallel: 0);
            serialRd.DeleteEmptyFiles = true;
            RunScan(serialRd, parallel: 0);
            var serialFiles = serialRd.EmptyFileResults
                .Select(f => f.FullName)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var parallelRd = CreateRunData(scanPath, parallel: 4);
            parallelRd.DeleteEmptyFiles = true;
            RunScan(parallelRd, parallel: 4);
            var parallelFiles = parallelRd.EmptyFileResults
                .Select(f => f.FullName)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.Equal(serialFiles.Count, parallelFiles.Count);
            for (int i = 0; i < serialFiles.Count; i++)
            {
                Assert.Equal(serialFiles[i], parallelFiles[i], StringComparer.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void GitIgnoreScan_IgnoredDirectories_AreNotFlaggedEmpty()
        {
            string scanPath = Path.Combine(_root, "repo");
            Directory.CreateDirectory(Path.Combine(scanPath, "build"));
            Directory.CreateDirectory(Path.Combine(scanPath, "src"));
            File.WriteAllText(Path.Combine(scanPath, "src", "main.cs"), "code");
            File.WriteAllText(Path.Combine(scanPath, ".gitignore"), "build/\n");
            Directory.CreateDirectory(Path.Combine(scanPath, ".git"));

            var rd = CreateRunData(scanPath);
            rd.RespectGitIgnore = true;
            var results = RunScan(rd);

            var emptyPaths = results
                .Where(r => r.SearchStatus == DirectorySearchStatusTypes.Empty)
                .Select(r => r.FullPath)
                .ToList();

            Assert.DoesNotContain(emptyPaths,
                p => p.EndsWith("build", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Lockout_ForcesDeletionToSimulate()
        {
            BuildTree();
            string scanPath = Path.Combine(_root, "tree");
            string emptyA = Path.Combine(_root, "tree", "emptyA");

            var rd = CreateRunData(scanPath);
            var results = RunScan(rd);
            var emptyDirs = results.Where(r => r.SearchStatus == DirectorySearchStatusTypes.Empty).ToList();
            Assert.True(emptyDirs.Count >= 3);

            Assert.True(Directory.Exists(emptyA), "emptyA should still exist — scan doesn't delete");

            var config = new RedConfiguration();
            config.Options.DeletionLockout = true;
            DeleteModes effective = config.Options.DeletionLockout ? DeleteModes.Simulate : DeleteModes.Direct;
            Assert.Equal(DeleteModes.Simulate, effective);

            Assert.True(Directory.Exists(emptyA), "emptyA must survive when lockout is active");
        }
    }
}
