using System.ComponentModel;
using System.IO.Compression;
using System.Linq;

namespace SingleFileMc.Packager;

internal sealed class MainForm : Form
{
    private readonly TextBox _txtGameDir;
    private readonly Button _btnGameDir;
    private readonly TextBox _txtJdkDir;
    private readonly Button _btnJdkDir;
    private readonly TextBox _txtOutput;
    private readonly Button _btnOutput;
    private readonly CheckedListBox _lstSync;
    private readonly ProgressBar _progress;
    private readonly Label _lblProgress;
    private readonly TextBox _txtLog;
    private readonly Button _btnPackage;
    private readonly Button _btnCancel;

    private CancellationTokenSource? _cts;

    public MainForm()
    {
        Text = "SingleFileMc 打包器";
        ClientSize = new Size(720, 620);
        MinimumSize = new Size(600, 540);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 3,
            RowCount = 6,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Controls.Add(layout);

        int row = 0;
        AddLabel(layout, "游戏目录 (.minecraft):", row);
        _txtGameDir = AddTextBox(layout, row);
        _btnGameDir = AddButton(layout, "浏览...", row, OnBrowseGameDir);
        row++;

        AddLabel(layout, "JDK 目录:", row);
        _txtJdkDir = AddTextBox(layout, row);
        _btnJdkDir = AddButton(layout, "浏览...", row, OnBrowseJdkDir);
        row++;

        AddLabel(layout, "输出文件:", row);
        _txtOutput = AddOutputTextBox(layout, row);
        _btnOutput = AddButton(layout, "浏览...", row, OnBrowseOutput);
        row++;

        AddLabel(layout, "同步到 game/:", row);
        _lstSync = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 4, 0),
            Height = 80,
            IntegralHeight = false,
        };
        layout.Controls.Add(_lstSync, 1, row);
        var btnRefreshSync = new Button
        {
            Text = "刷新",
            AutoSize = true,
            Height = 26,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 0, 0),
        };
        btnRefreshSync.Click += (_, _) => RefreshSyncList();
        layout.Controls.Add(btnRefreshSync, 2, row);
        row++;

        _progress = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 22,
        };
        _lblProgress = new Label
        {
            Text = "就绪",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left,
        };
        var progressPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 4),
        };
        progressPanel.Controls.Add(_progress);
        progressPanel.Controls.Add(_lblProgress);
        layout.Controls.Add(progressPanel, 1, row);
        layout.SetColumnSpan(progressPanel, 2);
        row++;

        _txtLog = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Consolas", 9F),
            Margin = new Padding(0, 4, 0, 4),
        };
        layout.Controls.Add(_txtLog, 1, row);
        layout.SetColumnSpan(_txtLog, 2);
        row++;

        var btnPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 8, 0, 0),
        };
        _btnCancel = new Button
        {
            Text = "取消",
            Width = 90,
            Height = 32,
            Enabled = false,
        };
        _btnCancel.Click += OnCancel;
        _btnPackage = new Button
        {
            Text = "打包",
            Width = 90,
            Height = 32,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        _btnPackage.FlatAppearance.BorderSize = 0;
        _btnPackage.Click += OnPackage;
        btnPanel.Controls.Add(_btnCancel);
        btnPanel.Controls.Add(_btnPackage);
        layout.Controls.Add(btnPanel, 1, row);
        layout.SetColumnSpan(btnPanel, 2);
    }

    private static Label AddLabel(TableLayoutPanel panel, string text, int row)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleRight,
            Anchor = AnchorStyles.Right,
            Padding = new Padding(0, 6, 8, 0),
        };
        panel.Controls.Add(label, 0, row);
        return label;
    }

    private static TextBox AddTextBox(TableLayoutPanel panel, int row)
    {
        var tb = new TextBox
        {
            Dock = DockStyle.Fill,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 4, 4, 0),
        };
        panel.Controls.Add(tb, 1, row);
        return tb;
    }

    private static string ExtractStub()
    {
        if (StubData.Binary is { Length: > 0 } bytes)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "SingleFileMc.stub.exe");
            File.WriteAllBytes(tmp, bytes);
            return tmp;
        }

        string stubDir = AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(stubDir, "SingleFileMc.exe"),
            Path.GetFullPath(Path.Combine(stubDir, "..", "SingleFileMc", "bin", "Release", "net10.0", "win-x64", "publish", "SingleFileMc.exe")),
            Path.GetFullPath(Path.Combine(stubDir, "..", "SingleFileMc", "bin", "Debug", "net10.0", "win-x64", "publish", "SingleFileMc.exe")),
            Path.GetFullPath(Path.Combine(stubDir, "..", "..", "..", "..", "SingleFileMc", "bin", "Release", "net10.0", "win-x64", "publish", "SingleFileMc.exe")),
            Path.GetFullPath(Path.Combine(stubDir, "..", "..", "..", "..", "SingleFileMc", "bin", "Debug", "net10.0", "win-x64", "publish", "SingleFileMc.exe")),
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        return "";
    }

    private static TextBox AddOutputTextBox(TableLayoutPanel panel, int row)
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string output = Path.Combine(desktop, "SingleFileMc.exe");
        var tb = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = output,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 4, 4, 0),
        };
        panel.Controls.Add(tb, 1, row);
        return tb;
    }

    private static Button AddButton(TableLayoutPanel panel, string text, int row, EventHandler handler)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 26,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 0, 0),
        };
        btn.Click += handler;
        panel.Controls.Add(btn, 2, row);
        return btn;
    }

    private void OnBrowseGameDir(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "选择 Minecraft 游戏目录 (.minecraft)", InitialDirectory = GetMcDefaultDir() };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _txtGameDir.Text = dlg.SelectedPath;
            RefreshSyncList();
        }
    }

    private void OnBrowseJdkDir(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "选择 JDK 根目录 (含 bin/server/jvm.dll)" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _txtJdkDir.Text = dlg.SelectedPath;
        }
    }

    private void OnBrowseOutput(object? sender, EventArgs e)
    {
        using var dlg = new SaveFileDialog { Title = "保存打包后的 exe", Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*", DefaultExt = ".exe", FileName = "SingleFileMc.exe" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _txtOutput.Text = dlg.FileName;
        }
    }

    private static string GetMcDefaultDir()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string official = Path.Combine(appData, ".minecraft");
        if (Directory.Exists(official)) return official;
        string exeDir = AppContext.BaseDirectory;
        for (int i = 0; i < 5; i++)
        {
            string mc = Path.Combine(exeDir, ".minecraft");
            if (Directory.Exists(mc)) return mc;
            exeDir = Path.GetDirectoryName(exeDir) ?? "";
        }
        return appData;
    }

    private void RefreshSyncList()
    {
        _lstSync.Items.Clear();
        string gameDir = _txtGameDir.Text.Trim();
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir)) return;

        string[] commonDirs = { "mods", "resourcepacks", "shaderpacks", "config" };
        foreach (string sub in commonDirs)
        {
            string fullPath = Path.Combine(gameDir, sub);
            if (Directory.Exists(fullPath) && HasFiles(fullPath))
            {
                _lstSync.Items.Add(sub, false);
            }
        }
    }

    private static bool HasFiles(string dir)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(dir).Any();
        }
        catch
        {
            return false;
        }
    }

    private async void OnPackage(object? sender, EventArgs e)
    {
        string gameDir = _txtGameDir.Text.Trim();
        string jdkDir = _txtJdkDir.Text.Trim();
        string stubPath = ExtractStub();
        string outputPath = _txtOutput.Text.Trim();

        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            MessageBox.Show(this, "请选择有效的游戏目录 (.minecraft)。", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrEmpty(jdkDir) || !Directory.Exists(jdkDir))
        {
            MessageBox.Show(this, "请选择有效的 JDK 目录。", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string jvmDll = Path.Combine(jdkDir, "bin", "server", "jvm.dll");
        if (!File.Exists(jvmDll))
        {
            MessageBox.Show(this, $"JDK 目录中未找到 bin/server/jvm.dll:\n{jdkDir}", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrEmpty(stubPath) || !File.Exists(stubPath))
        {
            MessageBox.Show(this, "未找到内嵌的启动器 Stub。\n请确保打包器与 SingleFileMc 项目一起构建。", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrEmpty(outputPath))
        {
            MessageBox.Show(this, "请指定输出文件路径。", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetUiState(packaging: true);

        _cts = new CancellationTokenSource();
        var syncPaths = _lstSync.CheckedItems.Cast<string>().ToList();
        var engine = new PackagerEngine(gameDir, jdkDir, stubPath, outputPath, syncPaths);
        engine.ProgressChanged += OnEngineProgress;
        engine.LogMessage += OnEngineLog;

        try
        {
            bool ok = await Task.Run(() => engine.Pack(_cts.Token), _cts.Token);
            if (ok)
            {
                AppendLog("=== 打包完成! ===");
                AppendLog($"输出: {outputPath}");
                AppendLog($"大小: {new FileInfo(outputPath).Length / (1024.0 * 1024.0):F1} MB");
                UpdateProgress(100, "完成");
                MessageBox.Show(this, $"打包成功!\n\n{outputPath}\n\n大小: {new FileInfo(outputPath).Length / (1024.0 * 1024.0):F1} MB", "打包完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("=== 用户取消 ===");
            UpdateProgress(0, "已取消");
        }
        catch (Exception ex)
        {
            AppendLog($"错误: {ex.Message}");
            AppendLog(ex.StackTrace ?? "");
            UpdateProgress(0, "失败");
            MessageBox.Show(this, $"打包失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetUiState(packaging: false);
            _cts.Dispose();
            _cts = null;
        }
    }

    private void OnCancel(object? sender, EventArgs e)
    {
        _cts?.Cancel();
    }

    private void OnEngineProgress(object? sender, ProgressEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateProgress(e.Percent, e.Status));
        }
        else
        {
            UpdateProgress(e.Percent, e.Status);
        }
    }

    private void OnEngineLog(object? sender, LogEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(e.Message));
        }
        else
        {
            AppendLog(e.Message);
        }
    }

    private void UpdateProgress(int percent, string status)
    {
        _progress.Value = Math.Clamp(percent, 0, 100);
        _lblProgress.Text = $"{percent}% - {status}";
    }

    private void AppendLog(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        _txtLog.AppendText($"[{timestamp}] {message}{Environment.NewLine}");
        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.ScrollToCaret();
    }

    private void SetUiState(bool packaging)
    {
        _txtGameDir.Enabled = !packaging;
        _btnGameDir.Enabled = !packaging;
        _txtJdkDir.Enabled = !packaging;
        _btnJdkDir.Enabled = !packaging;
        _txtOutput.Enabled = !packaging;
        _btnOutput.Enabled = !packaging;
        _btnPackage.Enabled = !packaging;
        _btnCancel.Enabled = packaging;
        if (!packaging)
        {
            _progress.Value = 0;
            _lblProgress.Text = packaging ? "" : "就绪";
        }
    }
}

internal sealed class ProgressEventArgs : EventArgs
{
    public int Percent { get; }
    public string Status { get; }
    public ProgressEventArgs(int percent, string status) { Percent = percent; Status = status; }
}

internal sealed class LogEventArgs : EventArgs
{
    public string Message { get; }
    public LogEventArgs(string message) { Message = message; }
}