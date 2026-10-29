using System.Diagnostics;

namespace IotLinkLab
{
    /// <summary>
    /// 导出结果 / 文件内容查看对话框。
    /// 解决"导出后看不到内容"的问题: 路径、行数与文件正文同屏显示,
    /// 并提供「打开所在文件夹」「另存为」「复制全文」。
    /// </summary>
    public class ExportDialog : Form
    {
        private readonly string _path;
        private TextBox _content = null!;
        private Label _meta = null!;

        private const int MaxChars = 400_000;

        public ExportDialog(string title, string path, string hint)
        {
            _path = path;

            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.PageBg;
            Font = Theme.Body();
            AutoScaleMode = AutoScaleMode.None;
            MinimizeBox = false;
            ShowInTaskbar = false;

            Rectangle wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, UiFit.S(1280), UiFit.S(720));
            Size = new Size(Math.Min(UiFit.S(900), wa.Width - UiFit.S(80)), Math.Min(UiFit.S(640), wa.Height - UiFit.S(60)));
            MinimumSize = new Size(UiFit.S(560), UiFit.S(360));

            BuildUi(hint);
            LoadContent();
        }

        /// <summary>焦点放到正文末尾, 避免只读路径框首次获得焦点时整段被蓝色反选。</summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _content.SelectionStart = _content.TextLength;
            _content.SelectionLength = 0;
            _content.Focus();
        }

        private void BuildUi(string hint)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
                BackColor = Theme.PageBg, Padding = new Padding(UiFit.S(Theme.GapLg), UiFit.S(Theme.GapMd), UiFit.S(Theme.GapLg), UiFit.S(Theme.GapMd))
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 路径 + 提示
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, UiFit.S(48)));

            // ---------- 路径与说明 ----------
            var head = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent, Margin = new Padding(0) };
            head.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            head.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            head.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var pathBox = new TextBox
            {
                ReadOnly = true, Font = Theme.Mono(9F), HideSelection = true, TabStop = false,
                BackColor = Theme.FieldBg, BorderStyle = BorderStyle.None,
                Text = _path
            };
            var pathShell = new FieldShell(pathBox) { Dock = DockStyle.Top, Height = UiFit.S(Theme.HeightInput), Margin = new Padding(0) };
            head.Controls.Add(pathShell, 0, 0);

            var hintLabel = new Label
            {
                Text = hint, AutoSize = true, MaximumSize = new Size(UiFit.S(820), 0),
                Font = Theme.Meta(), ForeColor = Theme.TextTertiary, Margin = new Padding(0, UiFit.S(Theme.GapSm), 0, 0)
            };
            head.Controls.Add(hintLabel, 0, 1);

            _meta = new Label
            {
                Text = "", AutoSize = true, Font = Theme.Meta(), ForeColor = Theme.TextSecondary,
                Margin = new Padding(0, UiFit.S(Theme.GapXs), 0, UiFit.S(Theme.GapSm))
            };
            head.Controls.Add(_meta, 0, 2);

            root.Controls.Add(head, 0, 0);

            // ---------- 内容 ----------
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(UiFit.S(Theme.GapSm)) };
            _content = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = false,
                ScrollBars = ScrollBars.Both, BackColor = Theme.FieldBg,
                Font = Theme.Mono(9F), BorderStyle = BorderStyle.None
            };
            var contentShell = new FieldShell(_content, multiline: true) { Dock = DockStyle.Fill, Margin = new Padding(0) };
            card.Controls.Add(contentShell);
            root.Controls.Add(card, 0, 1);

            // ---------- 底部操作 ----------
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0)
            };

            var btnClose = new RoundedButton { Text = "关闭", Outline = true, FillColor = Theme.TextSecondary, TextColor = Theme.TextSecondary, Margin = new Padding(UiFit.S(Theme.GapSm), UiFit.S(Theme.GapSm), 0, 0) };
            btnClose.Click += (s, e) => Close();
            UiFit.SizeButton(btnClose, 44);

            var btnFolder = new RoundedButton { Text = "打开所在文件夹", Outline = true, FillColor = Theme.Teal, TextColor = Theme.Teal, Margin = new Padding(UiFit.S(Theme.GapSm), UiFit.S(Theme.GapSm), 0, 0) };
            btnFolder.Click += (s, e) => OpenFolder();
            UiFit.SizeButton(btnFolder, 40);

            var btnSaveAs = new RoundedButton { Text = "另存为…", FillColor = Theme.Primary, Margin = new Padding(UiFit.S(Theme.GapSm), UiFit.S(Theme.GapSm), 0, 0) };
            btnSaveAs.Click += (s, e) => SaveAs();
            UiFit.SizeButton(btnSaveAs, 40);

            bar.Controls.Add(btnClose);
            bar.Controls.Add(btnFolder);
            bar.Controls.Add(btnSaveAs);
            root.Controls.Add(bar, 0, 2);

            Controls.Add(root);
        }

        private void LoadContent()
        {
            try
            {
                var info = new FileInfo(_path);
                if (!info.Exists)
                {
                    _meta.Text = "文件不存在";
                    _content.Text = "(尚未生成)";
                    return;
                }

                string text;
                using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, detectEncodingFromByteOrderMarks: true))
                {
                    text = sr.ReadToEnd();
                }

                int lines = 1;
                foreach (char c in text) if (c == '\n') lines++;

                bool trimmed = text.Length > MaxChars;
                if (trimmed)
                {
                    // 大文件只显示末尾(最近记录), 完整内容仍在磁盘上
                    text = "…(文件较大, 以下仅显示最近部分, 完整内容见文件)…" + Environment.NewLine
                         + text[^MaxChars..];
                }

                _meta.Text = $"共 {lines} 行 · {info.Length / 1024.0:0.0} KB · 修改于 {info.LastWriteTime:HH:mm:ss}{(trimmed ? " · 仅显示末尾" : "")}";
                _content.Text = text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
                _content.SelectionStart = _content.TextLength;
                _content.ScrollToCaret();
            }
            catch (Exception ex)
            {
                _meta.Text = "读取失败";
                _content.Text = ex.Message;
            }
        }

        private void OpenFolder()
        {
            try
            {
                string? dir = Path.GetDirectoryName(_path);
                if (dir != null && Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "打开文件夹失败"); }
        }

        private void SaveAs()
        {
            using var dlg = new SaveFileDialog
            {
                Title = "另存为",
                FileName = Path.GetFileName(_path),
                Filter = "文本/CSV 文件 (*.log;*.txt;*.csv)|*.log;*.txt;*.csv|所有文件 (*.*)|*.*",
                InitialDirectory = AppPaths.ExportsDir
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                File.Copy(_path, dlg.FileName, true);
                MessageBox.Show(this, "已保存到:\n" + dlg.FileName, "另存为", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "另存为失败"); }
        }
    }
}
