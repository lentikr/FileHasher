using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;

namespace FileHasher
{
    public partial class MainForm : Form
    {
        // --- UI 控件 ---
        private Button btnSelectFiles;
        private Button btnSelectFolder;
        private Button btnCalculate;
        private Button btnStop;
        private Button btnRemoveSelected;
        private Button btnClearAll;
        private Button btnExportCsv;
        private FlowLayoutPanel pnlAlgorithms;
        private CheckBox chkMD5;
        private CheckBox chkSHA1;
        private CheckBox chkSHA256;
        private CheckBox chkSHA512;
        private ListView lvResults;
        private ProgressBar progressBar;
        private Label lblStatus;
        private ContextMenuStrip contextMenuStrip;
        private ToolStripMenuItem copyMenuItem;
        private ListViewHitTestInfo _rightClickHitInfo; // 保存右键点击时的命中信息

        private CancellationTokenSource _cancellationTokenSource;
        private CancellationToken token => _cancellationTokenSource != null ? _cancellationTokenSource.Token : CancellationToken.None;
        private int _processedCount = 0;

        public MainForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // --- 窗口基本属性 ---
            this.Text = "FileHasher";
            this.Size = new Size(1000, 650); // 稍微增大窗口以容纳更多列
            this.MinimumSize = new Size(800, 500);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 设置更好的字体（如果可用）
            try
            {
                this.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            }
            catch
            {
                // 如果字体不可用，使用默认字体
                this.Font = new Font("Microsoft Sans Serif", 9F, FontStyle.Regular, GraphicsUnit.Point);
            }

            // 设置窗口图标
            try
            {
                // 从嵌入资源中加载图标
                var assembly = Assembly.GetExecutingAssembly();
                var iconStream = assembly.GetManifestResourceStream("FileHasher.icon.ico");
                if (iconStream != null)
                {
                    this.Icon = new Icon(iconStream);
                }
            }
            catch
            {
                // 如果无法加载图标，尝试从文件加载
                try
                {
                    this.Icon = new Icon("icon.ico");
                }
                catch
                {
                    // 如果都失败了，则忽略错误
                }
            }

            // 启用拖放
            this.AllowDrop = true;
            this.DragEnter += OnDragEnter;
            this.DragDrop += OnDragDrop;

            // --- 控件布局 ---
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(10)
            };

            // --- 定义行样式 ---
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // --- 第一行：文件选择面板 ---
            var topPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            btnSelectFiles = new Button { Text = "选择文件...", Width = 120, Height = 30 };
            btnSelectFolder = new Button { Text = "选择目录...", Width = 120, Height = 30 };
            btnRemoveSelected = new Button { Text = "移除选中", Width = 120, Height = 30, Enabled = false };
            btnClearAll = new Button { Text = "全部清除", Width = 120, Height = 30, Enabled = false };
            btnExportCsv = new Button { Text = "导出", Width = 120, Height = 30, Enabled = false };
            topPanel.Controls.Add(btnSelectFiles);
            topPanel.Controls.Add(btnSelectFolder);
            topPanel.Controls.Add(btnRemoveSelected);
            topPanel.Controls.Add(btnClearAll);
            topPanel.Controls.Add(btnExportCsv);
            mainLayout.Controls.Add(topPanel, 0, 0);

            // --- 第二行：结果列表 ---
            lvResults = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = true,
                AllowDrop = true
            };
            lvResults.Columns.Add("文件路径", -2, HorizontalAlignment.Left);  // -2 表示自动调整到内容宽度
            lvResults.Columns.Add("大小", -2, HorizontalAlignment.Right);     // -2 表示自动调整到内容宽度

            // 允许用户调整列宽和重新排序
            lvResults.AllowColumnReorder = true;

            // 创建右键菜单
            CreateContextMenu();

            mainLayout.Controls.Add(lvResults, 0, 1);

            // --- 第三行：状态和进度条 ---
            var statusPanel = new Panel { Dock = DockStyle.Fill };
            lblStatus = new Label { Text = "请添加文件或将文件拖放到此处。", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            progressBar = new ProgressBar { Dock = DockStyle.Fill, Visible = false };
            statusPanel.Controls.Add(lblStatus);
            statusPanel.Controls.Add(progressBar);
            mainLayout.Controls.Add(statusPanel, 0, 2);

            // --- 第四行：算法选择和计算按钮 ---
            var bottomPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            pnlAlgorithms = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Height = 40 };
            chkMD5 = new CheckBox { Text = "MD5", Checked = true, Appearance = Appearance.Normal };
            chkSHA1 = new CheckBox { Text = "SHA1", Appearance = Appearance.Normal };
            chkSHA256 = new CheckBox { Text = "SHA256", Appearance = Appearance.Normal };
            chkSHA512 = new CheckBox { Text = "SHA512", Appearance = Appearance.Normal };
            pnlAlgorithms.Controls.AddRange(new Control[] { chkMD5, chkSHA1, chkSHA256, chkSHA512 });

            btnCalculate = new Button { Text = "开始计算", AutoSize = true, Font = new Font(this.Font, FontStyle.Bold), Height = 30, Anchor = AnchorStyles.Right };
            btnStop = new Button { Text = "停止", AutoSize = true, Enabled = false, Height = 30, Anchor = AnchorStyles.Right };

            bottomPanel.Controls.Add(pnlAlgorithms, 0, 0);
            bottomPanel.Controls.Add(btnCalculate, 1, 0);
            bottomPanel.Controls.Add(btnStop, 2, 0);

            mainLayout.Controls.Add(bottomPanel, 0, 3);

            this.Controls.Add(mainLayout);

            // --- 事件绑定 ---
            btnSelectFiles.Click += OnSelectFilesClick;
            btnSelectFolder.Click += OnSelectFolderClick;
            btnCalculate.Click += OnCalculateClick;
            btnStop.Click += OnStopClick;
            btnRemoveSelected.Click += OnRemoveSelectedClick;
            btnClearAll.Click += OnClearAllClick;
            btnExportCsv.Click += OnExportCsvClick;
            lvResults.SelectedIndexChanged += OnListViewSelectedIndexChanged;
            lvResults.DragEnter += OnDragEnter;
            lvResults.DragDrop += OnDragDrop;
            lvResults.MouseClick += OnListViewMouseClick;
        }

        // --- 事件处理器 ---
        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any())
            {
                var allFiles = new List<string>();
                foreach (var path in files)
                {
                    if (File.Exists(path))
                    {
                        allFiles.Add(path);
                    }
                    else if (Directory.Exists(path))
                    {
                        try
                        {
                            allFiles.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories));
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"无法访问目录 '{path}': {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
                AddFilesToListView(allFiles);
            }
        }

        private void OnRemoveSelectedClick(object sender, EventArgs e)
        {
            if (lvResults.SelectedItems.Count > 0)
            {
                foreach (ListViewItem item in lvResults.SelectedItems)
                {
                    lvResults.Items.Remove(item);
                }
                UpdateUIState();

                // 移除文件后自动调整列宽
                AutoResizeAllColumns();
            }
        }

        private void OnClearAllClick(object sender, EventArgs e)
        {
            lvResults.Items.Clear();
            UpdateUIState();

            // 清除文件后重置列宽为默认值
            ResetColumnWidths();
        }

        private void OnExportCsvClick(object sender, EventArgs e)
        {
            if (lvResults.Items.Count == 0)
            {
                MessageBox.Show("没有数据可以导出。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*";
                dialog.DefaultExt = "csv";
                dialog.FileName = $"FileHasher_Export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                dialog.Title = "导出到CSV文件";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        ExportToCsv(dialog.FileName);
                        lblStatus.Text = $"已成功导出到: {dialog.FileName}";
                        MessageBox.Show($"导出成功！\n文件保存至: {dialog.FileName}", "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void OnListViewSelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateUIState();
        }

        private void OnStopClick(object sender, EventArgs e)
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
            }
        }

        private void AddFilesToListView(IEnumerable<string> filePaths)
        {
            var existingFiles = new HashSet<string>(lvResults.Items.Cast<ListViewItem>().Select(item => item.Text));
            var newFiles = filePaths.Where(p => !existingFiles.Contains(p)).ToList();

            if (!newFiles.Any()) return;

            var items = newFiles.Select(path =>
            {
                try
                {
                    var fileInfo = new FileInfo(path);
                    var item = new ListViewItem(path)
                    {
                        Tag = fileInfo,
                        SubItems = { FormatFileSize(fileInfo.Length) }
                    };
                    return item;
                }
                catch (Exception ex)
                {
                    var errorItem = new ListViewItem(path)
                    {
                        SubItems = { "错误", ex.Message },
                        ForeColor = Color.Red
                    };
                    return errorItem;
                }
            }).ToArray();

            lvResults.Items.AddRange(items);
            UpdateUIState();

            // 添加文件后自动调整列宽
            AutoResizeAllColumns();
        }

        private void OnSelectFilesClick(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog { Multiselect = true, Title = "选择一个或多个文件" })
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    AddFilesToListView(dialog.FileNames);
                }
            }
        }

        private void OnSelectFolderClick(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog { Description = "选择一个目录，将计算其下所有文件" })
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var files = Directory.EnumerateFiles(dialog.SelectedPath, "*", SearchOption.AllDirectories);
                        AddFilesToListView(files);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"读取目录失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private async void OnCalculateClick(object sender, EventArgs e)
        {
            if (lvResults.Items.Count == 0)
            {
                MessageBox.Show("请先添加文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selectedAlgorithms = new List<string>();
            if (chkMD5.Checked) selectedAlgorithms.Add("MD5");
            if (chkSHA1.Checked) selectedAlgorithms.Add("SHA1");
            if (chkSHA256.Checked) selectedAlgorithms.Add("SHA256");
            if (chkSHA512.Checked) selectedAlgorithms.Add("SHA512");
            if (selectedAlgorithms.Count == 0)
            {
                MessageBox.Show("请至少选择一种哈希算法。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // --- 准备UI进行计算 ---
            _cancellationTokenSource = new CancellationTokenSource();
            UpdateUIState(isCalculating: true);

            // 清理旧结果并重置列
            ResetListViewColumns(selectedAlgorithms);

            // --- 开始计算 ---
            try
            {
                _processedCount = 0;
                progressBar.Value = 0;
                progressBar.Maximum = lvResults.Items.Count;

                var tasks = lvResults.Items.Cast<ListViewItem>()
                                         .Where(item => item.Tag is FileInfo)
                                         .Select(item => ProcessFileAsync(item, selectedAlgorithms, token));
                await Task.WhenAll(tasks);
            }
            finally
            {
                // --- 计算完成或被取消 ---
                UpdateUIState(isCalculating: false);
                if (token.IsCancellationRequested)
                {
                    lblStatus.Text = "计算已被取消。";
                }
                else
                {
                    lblStatus.Text = $"计算完成！共处理 {_processedCount} 个文件。";

                    // 自动调整所有列宽度以适应内容
                    AutoResizeAllColumns();
                }

                // 重置取消令牌源
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Dispose();
                }
                _cancellationTokenSource = null;
            }
        }

        private void ResetListViewColumns(List<string> algorithms)
        {
            // 移除除 "文件路径" 和 "大小" 之外的所有列
            while (lvResults.Columns.Count > 2)
            {
                lvResults.Columns.RemoveAt(2);
            }

            // 添加新的算法列
            foreach (var alg in algorithms)
            {
                lvResults.Columns.Add(alg, 150, HorizontalAlignment.Left);  // 设置合理的初始宽度和对齐方式
            }

            // 清空所有子项结果
            foreach (ListViewItem item in lvResults.Items)
            {
                while (item.SubItems.Count > 2)
                {
                    item.SubItems.RemoveAt(2);
                }
                item.ForeColor = SystemColors.WindowText;
            }
        }

        private async Task ProcessFileAsync(ListViewItem item, List<string> selectedAlgorithms, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var fileInfo = item.Tag as FileInfo;
            if (fileInfo == null)
            {
                return;
            }
            var filePath = fileInfo.FullName;

            try
            {
                this.Invoke(new Action(() =>
                {
                    item.ForeColor = Color.Blue;
                    lblStatus.Text = $"正在计算: {fileInfo.Name}";
                }));

                var progress = new Progress<long>(bytesRead =>
                {
                    if (fileInfo.Length > 0)
                    {
                        // 可以在这里更新每个文件的进度，但当前设计是更新总体进度
                    }
                });

                var hashes = await Task.Run(() => CalculateHashes(filePath, selectedAlgorithms, progress, token), token);

                token.ThrowIfCancellationRequested();

                this.Invoke(new Action(() =>
                {
                    foreach (var alg in selectedAlgorithms)
                    {
                        string hashValue;
                        if (hashes.TryGetValue(alg, out hashValue))
                        {
                            item.SubItems.Add(hashValue);
                        }
                        else
                        {
                            item.SubItems.Add("错误");
                        }
                    }
                    item.ForeColor = Color.Green;
                    Interlocked.Increment(ref _processedCount);
                    progressBar.Value = _processedCount;
                }));
            }
            catch (OperationCanceledException)
            {
                this.Invoke(new Action(() =>
                {
                    item.ForeColor = Color.Orange;
                }));
            }
            catch (Exception ex)
            {
                this.Invoke(new Action(() =>
                {
                    // 清理可能已存在的子项（以防万一）
                    while (item.SubItems.Count > 2)
                    {
                        item.SubItems.RemoveAt(2);
                    }

                    // 添加错误信息作为第一个结果
                    item.SubItems.Add($"计算出错: {ex.Message}");

                    // 用占位符填充剩余的算法列
                    while (item.SubItems.Count < lvResults.Columns.Count)
                    {
                        item.SubItems.Add("---");
                    }
                    item.ForeColor = Color.Red;
                }));
            }
        }

        private Dictionary<string, string> CalculateHashes(string filePath, List<string> algorithms, IProgress<long> progress, CancellationToken token)
        {
            var results = new Dictionary<string, string>();
            var hashers = new Dictionary<string, HashAlgorithm>();

            // 创建所需的哈希算法实例
            foreach (var name in algorithms)
            {
                hashers[name] = CreateHashAlgorithm(name);
            }

            const int bufferSize = 4096 * 1024; // 4MB buffer
            long totalBytesRead = 0;

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var buffer = new byte[bufferSize];
                    int bytesRead;
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        foreach (var hasher in hashers.Values)
                        {
                            hasher.TransformBlock(buffer, 0, bytesRead, null, 0);
                        }
                        totalBytesRead += bytesRead;
                        progress.Report(totalBytesRead);
                    }

                    // 完成哈希计算
                    foreach (var entry in hashers)
                    {
                        entry.Value.TransformFinalBlock(new byte[0], 0, 0);
                        results[entry.Key] = BitConverter.ToString(entry.Value.Hash).Replace("-", "").ToLowerInvariant();
                    }
                }
            }
            finally
            {
                // 清理哈希算法实例
                foreach (var hasher in hashers.Values)
                {
                    hasher.Dispose();
                }
            }

            return results;
        }

        // --- 辅助方法 ---
        private HashAlgorithm CreateHashAlgorithm(string name)
        {
            switch (name.ToUpperInvariant())
            {
                case "MD5":
                    return MD5.Create();
                case "SHA1":
                    return SHA1.Create();
                case "SHA256":
                    return SHA256.Create();
                case "SHA512":
                    return SHA512.Create();
                default:
                    throw new ArgumentException("不支持的哈希算法", nameof(name));
            }
        }

        private void UpdateUIState(bool isCalculating = false)
        {
            btnSelectFiles.Enabled = !isCalculating;
            btnSelectFolder.Enabled = !isCalculating;
            pnlAlgorithms.Enabled = !isCalculating;

            bool hasItems = lvResults.Items.Count > 0;
            bool hasSelection = lvResults.SelectedItems.Count > 0;

            btnCalculate.Enabled = !isCalculating && hasItems;
            btnStop.Enabled = isCalculating;
            btnClearAll.Enabled = !isCalculating && hasItems;
            btnRemoveSelected.Enabled = !isCalculating && hasItems && hasSelection;
            btnExportCsv.Enabled = !isCalculating && hasItems;

            progressBar.Visible = isCalculating;
            lblStatus.Visible = !isCalculating;

            if (!isCalculating)
            {
                if (hasItems)
                {
                    lblStatus.Text = $"准备计算 {lvResults.Items.Count} 个文件。";
                }
                else
                {
                    lblStatus.Text = "请添加文件或将文件拖放到此处。";
                }
            }
        }

        // 创建右键菜单
        private void CreateContextMenu()
        {
            contextMenuStrip = new ContextMenuStrip();

            copyMenuItem = new ToolStripMenuItem("复制");
            copyMenuItem.Click += OnCopyMenuItemClick;

            contextMenuStrip.Items.Add(copyMenuItem);
            contextMenuStrip.Opening += OnContextMenuOpening;

            lvResults.ContextMenuStrip = contextMenuStrip;
        }

        // 右键菜单打开时的事件处理
        private void OnContextMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 使用保存的右键点击命中信息
            copyMenuItem.Enabled = _rightClickHitInfo != null && _rightClickHitInfo.Item != null && _rightClickHitInfo.SubItem != null;
            copyMenuItem.Text = "复制";
        }

        // 处理ListView的鼠标点击事件
        private void OnListViewMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                // 保存右键点击时的命中测试信息
                _rightClickHitInfo = lvResults.HitTest(e.Location);

                // 右键点击时选择对应的项目
                if (_rightClickHitInfo.Item != null)
                {
                    lvResults.SelectedItems.Clear();
                    _rightClickHitInfo.Item.Selected = true;
                }
            }
        }

        // 复制菜单项的点击事件
        private void OnCopyMenuItemClick(object sender, EventArgs e)
        {
            // 使用保存的右键点击命中信息
            if (_rightClickHitInfo != null && _rightClickHitInfo.Item != null && _rightClickHitInfo.SubItem != null)
            {
                try
                {
                    string textToCopy = _rightClickHitInfo.SubItem.Text;
                    if (!string.IsNullOrEmpty(textToCopy))
                    {
                        Clipboard.SetText(textToCopy);

                        // 可选：显示复制成功的提示
                        lblStatus.Text = $"已复制: {(textToCopy.Length > 50 ? textToCopy.Substring(0, 50) + "..." : textToCopy)}";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"复制失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // 导出数据到CSV文件
        private void ExportToCsv(string filePath)
        {
            using (var writer = new System.IO.StreamWriter(filePath, false, System.Text.Encoding.UTF8))
            {
                // 写入表头
                var headers = new List<string>();
                foreach (ColumnHeader column in lvResults.Columns)
                {
                    headers.Add(EscapeCsvField(column.Text));
                }
                writer.WriteLine(string.Join(",", headers));

                // 写入数据行
                foreach (ListViewItem item in lvResults.Items)
                {
                    var values = new List<string>();

                    // 添加主项文本（第一列）
                    values.Add(EscapeCsvField(item.Text));

                    // 添加子项文本（其他列）
                    foreach (ListViewItem.ListViewSubItem subItem in item.SubItems.Cast<ListViewItem.ListViewSubItem>().Skip(1))
                    {
                        values.Add(EscapeCsvField(subItem.Text));
                    }

                    // 如果子项数量少于列数，用空字符串填充
                    while (values.Count < lvResults.Columns.Count)
                    {
                        values.Add("");
                    }

                    writer.WriteLine(string.Join(",", values));
                }
            }
        }

        // CSV字段转义处理
        private string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
            {
                return "";
            }

            // 如果字段包含逗号、双引号或换行符，需要用双引号包围
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\r") || field.Contains("\n"))
            {
                // 双引号需要转义为两个双引号
                field = field.Replace("\"", "\"\"");
                return $"\"{field}\"";
            }

            return field;
        }

        // 自动调整所有列宽度的辅助方法
        private void AutoResizeAllColumns()
        {
            if (lvResults.Columns.Count == 0) return;

            try
            {
                lvResults.BeginUpdate();

                for (int i = 0; i < lvResults.Columns.Count; i++)
                {
                    // 分别计算表头和内容的宽度
                    lvResults.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.HeaderSize);
                    int headerWidth = lvResults.Columns[i].Width;

                    lvResults.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.ColumnContent);
                    int contentWidth = lvResults.Columns[i].Width;

                    // 取两者中的较大值，这样既能显示完整表头，也能显示完整内容
                    lvResults.Columns[i].Width = Math.Max(headerWidth, contentWidth);
                }
            }
            finally
            {
                lvResults.EndUpdate();
            }
        }

        // 重置列宽为默认值的辅助方法
        private void ResetColumnWidths()
        {
            if (lvResults.Columns.Count < 2) return;

            try
            {
                lvResults.BeginUpdate();

                // 重置基本列的宽度
                lvResults.Columns[0].Width = 200;  // 文件路径
                lvResults.Columns[1].Width = 80;  // 大小

                // 重置算法列的宽度
                for (int i = 2; i < lvResults.Columns.Count; i++)
                {
                    lvResults.Columns[i].Width = 150;
                }
            }
            finally
            {
                lvResults.EndUpdate();
            }
        }

        // --- FormatFileSize 方法 ---
        private static string FormatFileSize(long bytes)
        {
            var unitMap = new[] { "B", "KB", "MB", "GB", "TB" };
            if (bytes == 0) return "0 B";
            int power = (int)Math.Floor(Math.Log(bytes, 1024));
            return $"{bytes / Math.Pow(1024, power):F2} {unitMap[power]}";
        }

        [STAThread]
        static void Main()
        {
            // 启用高DPI支持
            if (Environment.OSVersion.Version.Major >= 6)
            {
                SetProcessDPIAware();
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        // 启用高DPI感知
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }
}
