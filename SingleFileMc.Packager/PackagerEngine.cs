using System.IO.Compression;

namespace SingleFileMc.Packager;

internal sealed class PackagerEngine
{
    private readonly string _gameDir;
    private readonly string _jdkDir;
    private readonly string _stubPath;
    private readonly string _outputPath;
    private readonly IReadOnlyList<string> _syncPaths;
    private readonly string? _selectedVersion;

    private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Plain Craft Launcher 2.exe",
        ".gitignore",
        "PCL",
    };

    public event EventHandler<ProgressEventArgs>? ProgressChanged;
    public event EventHandler<LogEventArgs>? LogMessage;

    public PackagerEngine(string gameDir, string jdkDir, string stubPath, string outputPath,
        IReadOnlyList<string>? syncPaths = null, string? selectedVersion = null)
    {
        _gameDir = Path.GetFullPath(gameDir);
        _jdkDir = Path.GetFullPath(jdkDir);
        _stubPath = stubPath;
        _outputPath = outputPath;
        _syncPaths = syncPaths ?? Array.Empty<string>();
        _selectedVersion = string.IsNullOrWhiteSpace(selectedVersion) ? null : selectedVersion.Trim();
        // PHASE19: 强制单版本打包 —— 不指定版本直接拒绝, 避免静默打包全部版本
        if (_selectedVersion is null)
        {
            throw new ArgumentException("必须指定要打包的版本 (selectedVersion)", nameof(selectedVersion));
        }
    }

    public bool Pack(CancellationToken ct)
    {
        Log("开始打包...");
        Log($"游戏目录: {_gameDir}");
        Log($"JDK 目录: {_jdkDir}");
        Log($"Stub: {_stubPath}");
        Log($"输出: {_outputPath}");

        Report(0, "正在扫描文件...");

        string tempDir = Path.Combine(Path.GetTempPath(), "sfmc_pack_" + Guid.NewGuid().ToString("N")[..8]);
        string containerZip = Path.Combine(tempDir, "container.zip");

        try
        {
            Directory.CreateDirectory(tempDir);

            ct.ThrowIfCancellationRequested();

            List<string> allFiles = new();
            List<string> allDirs = new();

            ScanDirectory(_gameDir, "minecraft", allFiles, allDirs, ct, _selectedVersion);
            ScanDirectory(_jdkDir, "openjdk", allFiles, allDirs, ct);

            Log($"扫描完成: {allFiles.Count} 个文件, {allDirs.Count} 个目录");

            ct.ThrowIfCancellationRequested();
            Report(10, "正在创建容器...");

            int totalFiles = allFiles.Count;
            int processed = 0;

            using (FileStream fs = File.Create(containerZip))
            using (ZipArchive zip = new(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (string dir in allDirs.OrderBy(x => x, StringComparer.Ordinal))
                {
                    zip.CreateEntry(dir + "/", CompressionLevel.NoCompression);
                }

                foreach (string fileInfo in allFiles.OrderBy(x => x, StringComparer.Ordinal))
                {
                    ct.ThrowIfCancellationRequested();

                    string[] parts = fileInfo.Split('|', 2);
                    string entryName = parts[0];
                    string sourcePath = parts[1];

                    ZipArchiveEntry ze = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
                    using Stream dst = ze.Open();
                    using FileStream src = File.OpenRead(sourcePath);
                    src.CopyTo(dst);

                    processed++;
                    if (processed % 100 == 0 || processed == totalFiles)
                    {
                        int pct = 10 + (int)(processed * 80L / totalFiles);
                        Report(pct, $"正在打包 {processed}/{totalFiles} 个文件...");
                    }
                }

                if (_syncPaths.Count > 0)
                {
                    string manifest = string.Join("\n", _syncPaths);
                    ZipArchiveEntry mf = zip.CreateEntry("minecraft/.sfmc-sync", CompressionLevel.NoCompression);
                    using Stream mfStream = mf.Open();
                    using var mfWriter = new StreamWriter(mfStream);
                    mfWriter.Write(manifest);
                    Log($"同步清单: {string.Join(", ", _syncPaths)}");
                }

                // PHASE19: 版本选择标记 —— 多版本 .minecraft 打包时启动器优先读它 (否则
                // AutoDetectVersionId 取 versions/ 下第一个含 json 的目录, 可能与所选不符)。
                if (_selectedVersion is not null)
                {
                    ZipArchiveEntry mv = zip.CreateEntry("minecraft/.sfmc-version", CompressionLevel.NoCompression);
                    using Stream mvStream = mv.Open();
                    using var mvWriter = new StreamWriter(mvStream);
                    mvWriter.Write(_selectedVersion);
                    Log($"打包版本: {_selectedVersion} (其余版本目录已跳过)");
                }
            }

            long zipLen = new FileInfo(containerZip).Length;
            Log($"容器创建完成: {zipLen / (1024.0 * 1024.0):F1} MB");

            ct.ThrowIfCancellationRequested();
            Report(92, "正在拼接...");

            string outputDir = Path.GetDirectoryName(_outputPath)!;
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            long stubLen = new FileInfo(_stubPath).Length;
            Log($"Stub 大小: {stubLen / (1024.0 * 1024.0):F2} MB");

            using (FileStream dst = File.Create(_outputPath))
            {
                using (FileStream src = File.OpenRead(_stubPath))
                {
                    src.CopyTo(dst);
                }
                Report(96, "正在追加容器...");
                using (FileStream src = File.OpenRead(containerZip))
                {
                    src.CopyTo(dst);
                }
            }

            long finalLen = new FileInfo(_outputPath).Length;
            Log($"最终 exe: {finalLen / (1024.0 * 1024.0):F1} MB ({stubLen} + {zipLen} bytes)");

            Report(100, "完成");
            return true;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
                Log("警告: 临时目录清理失败");
            }
        }
    }

    private void ScanDirectory(string rootDir, string zipPrefix, List<string> allFiles, List<string> allDirs,
        CancellationToken ct, string? onlyVersion = null)
    {
        if (!Directory.Exists(rootDir))
        {
            throw new DirectoryNotFoundException($"目录不存在: {rootDir}");
        }

        string rootName = Path.GetFileName(rootDir);
        Log($"  扫描: {rootDir} -> {zipPrefix}/");

        var dirStack = new Stack<(string Dir, string EntryPrefix)>();
        dirStack.Push((rootDir, zipPrefix));

        while (dirStack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var (currentDir, currentPrefix) = dirStack.Pop();

            foreach (string subDir in Directory.GetDirectories(currentDir))
            {
                string subName = Path.GetFileName(subDir);
                if (IsExcluded(subName, subDir)) continue;

                // PHASE19: 选择单版本时, 只打包 versions/<所选版本>, 其余版本目录整体跳过
                // (assets/libraries/根目录等共享树仍然全量打包)。
                if (onlyVersion is not null
                    && currentPrefix.EndsWith("/versions", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(subName, onlyVersion, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string entryPrefix = currentPrefix + "/" + subName;
                allDirs.Add(entryPrefix);
                dirStack.Push((subDir, entryPrefix));
            }

            foreach (string file in Directory.GetFiles(currentDir))
            {
                string fileName = Path.GetFileName(file);
                if (IsExcluded(fileName, file)) continue;

                string entryName = currentPrefix + "/" + fileName;
                allFiles.Add(entryName + "|" + file);
            }
        }
    }

    private static bool IsExcluded(string name, string fullPath)
    {
        if (ExcludedNames.Contains(name)) return true;
        if (name.StartsWith("Log", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (string seg in fullPath.Split(Path.DirectorySeparatorChar))
        {
            if (seg.Equals("PCL", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private void Report(int percent, string status)
    {
        ProgressChanged?.Invoke(this, new ProgressEventArgs(percent, status));
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(this, new LogEventArgs(message));
    }
}
