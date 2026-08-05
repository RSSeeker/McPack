using System.ComponentModel;
using System.IO.Compression;
using System.Linq;

namespace SingleFileMc.Packager;

internal sealed class MainForm : Form
{
    /// <summary>
    /// 同步清单条目: Manifest = 写入容器 .sfmc-sync 的行 ("源|目标", 无 | 时源=目标);
    /// Label = 界面显示文本。
    /// </summary>
    private sealed record SyncEntry(string Manifest, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly TextBox _txtGameDir;
    private readonly Button _btnGameDir;
    private readonly TextBox _txtJdkDir;
    private readonly Button _btnJdkDir;
    private readonly TextBox _txtOutput;
    private readonly Button _btnOutput;
    private readonly ComboBox _cmbVersion;
    private readonly CheckedListBox _lstSync;
    private readonly TextBox _txtSyncCustom;
    private readonly ProgressBar _progress;
    private readonly Label _lblProgress;
    private readonly TextBox _txtLog;
    private readonly Button _btnPackage;
    private readonly Button _btnCancel;

    private CancellationTokenSource? _cts;

    public MainForm()
    {
        Text = "SingleFileMc 打包器";
        // PHASE19: 窗口图标取自 exe 图标 (assets/app.ico)
        try
        {
            using Icon? exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (exeIcon is not null) { Icon = (Icon)exeIcon.Clone(); }
        }
        catch { /* 图标缺失不影响使用 */ }
        ClientSize = new Size(720, 620);
        MinimumSize = new Size(600, 540);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 3,
            RowCount = 9,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 自定义同步
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 进度
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 底部按钮
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

        AddLabel(layout, "打包版本:", row);
        _cmbVersion = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 4, 0),
            Height = 26,
        };
        _cmbVersion.SelectedIndexChanged += (_, _) => RefreshSyncList();
        layout.Controls.Add(_cmbVersion, 1, row);
        var btnRefreshVersion = new Button
        {
            Text = "刷新",
            AutoSize = true,
            Height = 26,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 0, 0),
        };
        btnRefreshVersion.Click += (_, _) => RefreshVersions();
        layout.Controls.Add(btnRefreshVersion, 2, row);
        row++;

        AddLabel(layout, "同步到 game/:", row);
        _lstSync = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 4, 0),
            Height = 90,
            IntegralHeight = false,
        };
        layout.Controls.Add(_lstSync, 1, row);
        var syncBtnPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 0, 0),
        };
        var btnRefreshSync = new Button { Text = "刷新", AutoSize = true, Height = 26, Margin = new Padding(0, 0, 0, 4) };
        btnRefreshSync.Click += (_, _) => RefreshSyncList();
        var btnSelectAll = new Button { Text = "全选", AutoSize = true, Height = 26, Margin = new Padding(0, 0, 0, 4) };
        btnSelectAll.Click += (_, _) => ToggleSelectAll();
        syncBtnPanel.Controls.Add(btnRefreshSync);
        syncBtnPanel.Controls.Add(btnSelectAll);
        layout.Controls.Add(syncBtnPanel, 2, row);
        row++;

        AddLabel(layout, "自定义同步:", row);
        _txtSyncCustom = AddTextBox(layout, row);
        var btnSyncAdd = AddButton(layout, "添加", row, OnAddSyncPath);
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
            Margin = new Padding(0, 4, 0, 0),
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
            RefreshVersions();
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

    /// <summary>扫描 versions/ 目录填充版本下拉框 (PHASE19: 强制单版本, 无"全部版本"选项)。</summary>
    private void RefreshVersions()
    {
        string? prev = _cmbVersion.SelectedItem as string;
        _cmbVersion.Items.Clear();

        string gameDir = _txtGameDir.Text.Trim();
        if (!string.IsNullOrEmpty(gameDir))
        {
            string versionsDir = Path.Combine(gameDir, "versions");
            if (Directory.Exists(versionsDir))
            {
                foreach (string dir in Directory.EnumerateDirectories(versionsDir))
                {
                    string name = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(name) || name.StartsWith('.')) continue;
                    if (File.Exists(Path.Combine(dir, name + ".json")))
                    {
                        _cmbVersion.Items.Add(name);
                    }
                }
            }
        }

        int idx = prev is not null ? _cmbVersion.Items.IndexOf(prev) : -1;
        _cmbVersion.SelectedIndex = idx >= 0 ? idx : (_cmbVersion.Items.Count > 0 ? 0 : -1);
    }

    /// <summary>全选/全不选 切换 (同步清单)。</summary>
    private void ToggleSelectAll()
    {
        bool allChecked = _lstSync.Items.Count > 0;
        for (int i = 0; i < _lstSync.Items.Count; i++)
        {
            if (!_lstSync.GetItemChecked(i)) { allChecked = false; break; }
        }
        for (int i = 0; i < _lstSync.Items.Count; i++)
        {
            _lstSync.SetItemChecked(i, !allChecked);
        }
    }

    /// <summary>添加自定义同步路径: 支持 "相对路径" 或 "容器源|gameDir目标"。</summary>
    private void OnAddSyncPath(object? sender, EventArgs e)
    {
        string input = _txtSyncCustom.Text.Trim().TrimStart('\uFEFF');
        if (string.IsNullOrEmpty(input)) return;

        string source = input;
        string dest = input;
        int sep = input.IndexOf('|');
        if (sep > 0)
        {
            source = input[..sep].Trim();
            dest = input[(sep + 1)..].Trim();
        }
        if (source.Length == 0 || dest.Length == 0) return;

        string gameDir = _txtGameDir.Text.Trim();
        if (!string.IsNullOrEmpty(gameDir))
        {
            string fullSource = Path.Combine(gameDir, source.Replace('/', '\\'));
            if (!Directory.Exists(fullSource))
            {
                MessageBox.Show(this, $"路径不存在: {source}\n(相对 .minecraft 根目录)", "同步路径无效",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        _lstSync.Items.Add(new SyncEntry(source + (sep > 0 ? "|" + dest : ""), $"{source} (自定义)"), true);
        _txtSyncCustom.Clear();
    }

    private void RefreshSyncList()
    {
        _lstSync.Items.Clear();
        string gameDir = _txtGameDir.Text.Trim();
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir)) return;

        string[] commonDirs = { "mods", "resourcepacks", "shaderpacks", "config", "saves" };

        // 官方布局: 目录直接在 gameDir 根下, 容器路径 = 目标路径 (manifest 行 = "mods")
        foreach (string sub in commonDirs)
        {
            string fullPath = Path.Combine(gameDir, sub);
            if (Directory.Exists(fullPath) && HasFiles(fullPath))
            {
                _lstSync.Items.Add(new SyncEntry(sub, sub), false);
            }
        }

        // HMCL 实例布局: 目录在 versions\<id>\ 下 (游戏数据根仍是 gameDir), 容器源
        // = "versions/<id>/<sub>", 但运行期 gameDir 目标是 game\<sub> —— manifest 用
        // "源|目标" 区分, 否则会同步到 game\versions\...\mods (游戏根本不读那里)。
        string versionsDir = Path.Combine(gameDir, "versions");
        if (Directory.Exists(versionsDir))
        {
            foreach (string inst in Directory.EnumerateDirectories(versionsDir))
            {
                string instName = Path.GetFileName(inst);
                if (string.IsNullOrEmpty(instName) || instName.StartsWith('.')) continue;
                // PHASE19: 强制单版本 —— 只列出所选版本的同步目录
                if (!string.Equals(instName, _cmbVersion.SelectedItem as string, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                foreach (string sub in commonDirs)
                {
                    string fullPath = Path.Combine(inst, sub);
                    if (!Directory.Exists(fullPath) || !HasFiles(fullPath)) continue;

                    string manifest = $"versions/{instName}/{sub}|{sub}";
                    _lstSync.Items.Add(new SyncEntry(manifest, $"{sub} (实例 {instName})"), false);
                }
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
        // PHASE19: 强制单版本打包 —— 必须显式选择一个版本
        string? selectedVersion = _cmbVersion.SelectedItem as string;
        if (string.IsNullOrEmpty(selectedVersion))
        {
            MessageBox.Show(this, "请选择要打包的版本 (打包器只支持单版本打包)。", "输入错误",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetUiState(packaging: true);

        _cts = new CancellationTokenSource();
        var syncPaths = _lstSync.CheckedItems.Cast<SyncEntry>().Select(e => e.Manifest).ToList();
        var engine = new PackagerEngine(gameDir, jdkDir, stubPath, outputPath, syncPaths, selectedVersion);
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
