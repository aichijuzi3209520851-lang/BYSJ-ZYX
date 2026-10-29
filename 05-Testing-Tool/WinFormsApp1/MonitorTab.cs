using System.Diagnostics;

namespace IotLinkLab
{
    /// <summary>
    /// 订阅监控台: 订阅任意主题列表, 实时显示消息流与计数统计, 可暂停接收。
    /// 通用能力, 不绑定温室协议。
    /// 布局按 UI-SPEC 6.2: 顶栏 AutoSize 且控件间距 ≥ 8; 列宽按 ListView 客户区宽度动态分配;
    /// 列表与详情卡留白 ≥ 10; 说明性长句一律改 ToolTip, 不占版面。
    /// </summary>
    public class MonitorTab : UserControl
    {
        private ListView _list = null!;
        private TextBox _detail = null!, _topicInput = null!;
        private CheckBox _chkPause = null!;
        private Label _statLabel = null!, _detailTitle = null!;
        private SplitContainer _monSplit = null!;
        private bool _compactList;
        private bool _monSplitUser;
        private double _monSplitRatio = 0.58;
        private bool _monSplitSuppress;
        private readonly List<ListViewItem> _pending = new();
        private readonly Stopwatch _rateWatch = Stopwatch.StartNew();
        private readonly ToolTip _tip = new();
        private int _count;
        private int _fitDepth;          // 防「改列宽 → 滚动条变化 → 又触发改列宽」递归

        /// <summary>请求订阅某主题(Form 层转发给 MqttLink)。</summary>
        public event Action<string>? SubscribeRequested;
        /// <summary>选中某条消息时回调(payload), 供 JSON 高亮查看。</summary>
        public event Action<string>? MessageSelected;
        /// <summary>日志通道(UI-SPEC 第 5 节冻结契约的一部分, 由 Form 层按需挂接)。</summary>
#pragma warning disable CS0067   // 契约成员: 本类当前不主动发日志, 保留供 Form 层订阅
        public event Action<string>? Log;
#pragma warning restore CS0067

        public string[] Topics { get; private set; } = { "green/data" };

        public MonitorTab()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.Transparent;
            DoubleBuffered = true;

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 顶栏
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // 列表 + 详情(可拖拽)

            body.Controls.Add(BuildTopBar(), 0, 0);

            _monSplit = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                SplitterWidth = 8, BackColor = Theme.PageBg,
                Panel1MinSize = 140, Panel2MinSize = 112, Margin = new Padding(0)
            };
            _monSplit.Panel1.BackColor = Theme.PageBg;
            _monSplit.Panel2.BackColor = Theme.PageBg;
            _monSplit.SplitterMoved += (s, e) =>
            {
                if (_monSplitSuppress || _monSplit.IsDisposed) return;
                int usable = _monSplit.Height - _monSplit.SplitterWidth;
                if (usable > _monSplit.Panel1MinSize + _monSplit.Panel2MinSize)
                {
                    _monSplitRatio = (double)_monSplit.SplitterDistance / usable;
                    _monSplitUser = true;
                }
            };

            _list = BuildList();
            _monSplit.Panel1.Controls.Add(_list);
            _monSplit.Panel2.Controls.Add(BuildDetail());
            body.Controls.Add(_monSplit, 0, 1);

            Controls.Add(body);
            Resize += (s, e) => ApplyMonSplit();
        }

        private void ApplyMonSplit()
        {
            if (_monSplit == null || _monSplit.IsDisposed || _monSplitUser) return;

            int usable = _monSplit.Height - _monSplit.SplitterWidth;
            if (usable <= _monSplit.Panel1MinSize + _monSplit.Panel2MinSize) return;

            int target = (int)Math.Round(usable * _monSplitRatio);
            target = Math.Max(_monSplit.Panel1MinSize, Math.Min(usable - _monSplit.Panel2MinSize, target));
            if (_monSplit.SplitterDistance == target) return;

            _monSplitSuppress = true;
            try { _monSplit.SplitterDistance = target; }
            finally { _monSplitSuppress = false; }
        }

        // ------------------------------------------------------------------ 顶栏
        private Control BuildTopBar()
        {
            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, Theme.GapMd)
            };
            top.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.HeightButton));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var lblTopic = new Label
            {
                Text = "订阅主题", AutoSize = true, Font = Theme.Ui(9.5F, bold: true),
                ForeColor = Theme.TextSecondary, BackColor = Color.Transparent,
                Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, Theme.GapSm, 0)
            };
            _topicInput = new TextBox
            {
                Font = Theme.Mono(9.5F), Text = "green/data", BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 4, 10, 0)
            };

            var btnAdd = new RoundedButton { Text = "添加订阅", FillColor = Theme.Teal, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 10, 0) };
            UiFit.SizeButton(btnAdd, padTotal: 40, minWidth: 96);
            btnAdd.Click += BtnAdd_Click;

            _chkPause = new CheckBox
            {
                Text = "暂停接收", AutoSize = true, Font = Theme.Ui(9F), ForeColor = Theme.TextPrimary,
                BackColor = Color.Transparent, Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 10, 0)
            };

            var btnClear = new RoundedButton { Text = "清空记录", FillColor = Theme.TextTertiary, Outline = true, TextColor = Theme.TextSecondary, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 10, 0) };
            UiFit.SizeButton(btnClear, padTotal: 40, minWidth: 96);
            btnClear.Click += (s, e) =>
            {
                _list.Items.Clear();
                _detail.Clear();
                _detailTitle.Text = "载荷全文";
                _count = 0;
                UpdateStat();
            };

            _statLabel = new Label
            {
                Text = "已接收 0 条", AutoSize = true, Font = Theme.Ui(9.5F, bold: true),
                ForeColor = Theme.Primary, BackColor = Color.Transparent,
                Anchor = AnchorStyles.Right, Margin = new Padding(0, 8, 0, 0)
            };

            top.Controls.Add(lblTopic, 0, 0);
            top.Controls.Add(_topicInput, 1, 0);
            top.Controls.Add(btnAdd, 2, 0);
            top.Controls.Add(_chkPause, 3, 0);
            top.Controls.Add(btnClear, 4, 0);
            top.Controls.Add(_statLabel, 5, 0);

            _tip.SetToolTip(lblTopic, "订阅主题: 支持 MQTT 通配符, 例如 green/data、greenhouse/+/data");
            _tip.SetToolTip(_topicInput, "输入主题后点「添加订阅」，回车不会发送");
            _tip.SetToolTip(btnAdd, "订阅该主题并开始接收消息");
            _tip.SetToolTip(btnClear, "清空列表与详情，计数归零");
            _tip.SetToolTip(_chkPause, "勾选后新消息不再进列表（订阅仍在后台保持）");
            _tip.SetToolTip(_statLabel, "已接收总条数 · 每分钟接收速率 · 内存缓冲条数");
            return top;
        }

        // ------------------------------------------------------------------ 列表
        private ListView BuildList()
        {
            var list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false,
                BackColor = Theme.FieldBg, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Mono(9F),
                HideSelection = false, MultiSelect = false, Margin = new Padding(0)
            };
            list.Columns.Add("时间");
            list.Columns.Add("主题");
            list.Columns.Add("长度");
            list.Columns.Add("载荷预览");
            // 列宽不写死: 按 ListView 实际客户区宽度动态分配, 保证不出现横向滚动条
            list.Resize += (s, e) => FitColumns(list);
            list.ClientSizeChanged += (s, e) => FitColumns(list);   // 竖向滚动条出现/消失也会改变客户区宽度

            list.SelectedIndexChanged += (s, e) =>
            {
                if (list.SelectedItems.Count == 0) return;
                ListViewItem sel = list.SelectedItems[0];
                // 完整载荷一直存在 Tag 里(见 OnMessage); 本表只有 4 列(下标 0~3),
                // 旧代码取 SubItems[4] 会抛越界异常, 这里优先 Tag, 再退回最后一个子项。
                string payload = sel.Tag as string ?? sel.SubItems[sel.SubItems.Count - 1].Text;
                _detail.Text = payload;
                _detailTitle.Text = _compactList
                    ? $"载荷全文 · {payload.Length} 字符"
                    : "载荷全文";
                MessageSelected?.Invoke(payload);
            };
            return list;
        }

        /// <summary>
        /// 列宽动态分配: 标准模式 时间112 / 主题20% / 长度64 / 预览=剩余;
        /// 紧凑模式隐藏长度列, 完整长度显示在详情标题中, 保证预览列仍可用。
        /// </summary>
        private void FitColumns(ListView list)
        {
            if (list.IsDisposed || list.Columns.Count < 4 || _fitDepth > 2) return;
            // 留 2px 安全内缩: 列宽合计严格小于客户区宽度, 绝不溢出成横向滚动条
            int clientW = list.ClientSize.Width - UiFit.Scale(list, 2);
            if (clientW <= 0) return;

            _fitDepth++;
            try
            {
                _compactList = clientW < UiFit.Scale(list, 1200);
                int wTime = UiFit.Scale(list, _compactList ? 96 : 112);
                int wLength = _compactList ? 0 : UiFit.Scale(list, 64);
                int wTopic = Math.Max(UiFit.Scale(list, _compactList ? 140 : 160), (int)Math.Round(clientW * 0.20));
                int wPreview = clientW - wTime - wTopic - wLength;

                int minPreview = UiFit.Scale(list, _compactList ? 180 : 240);
                if (wPreview < minPreview)
                {
                    // 窗口偏窄: 先把主题列压到 120 的下限, 保住预览列
                    wTopic = Math.Max(UiFit.Scale(list, 120), wTopic - (minPreview - wPreview));
                    wPreview = clientW - wTime - wTopic - wLength;
                }
                if (wPreview < 0) wPreview = 0;   // 极端窄窗口: 宁可留空也不超出客户区

                list.BeginUpdate();
                SetColumn(list, 0, wTime);
                SetColumn(list, 1, wTopic);
                SetColumn(list, 2, wLength);
                SetColumn(list, 3, wPreview);
                list.EndUpdate();
            }
            finally
            {
                _fitDepth--;
            }
        }

        /// <summary>只在宽度真的变化时才赋值, 避免无谓的布局抖动/重绘。</summary>
        private static void SetColumn(ListView list, int index, int width)
        {
            if (list.Columns[index].Width != width) list.Columns[index].Width = width;
        }

        // ------------------------------------------------------------------ 详情
        private Control BuildDetail()
        {
            var card = new Card
            {
                Dock = DockStyle.Fill, Padding = new Padding(Theme.Pad),
                Margin = new Padding(0)
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                BackColor = Color.Transparent, Margin = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _detailTitle = UiFit.SectionTitle("载荷全文");
            _detailTitle.Margin = new Padding(0, 0, 0, 8);
            grid.Controls.Add(_detailTitle, 0, 0);

            _detail = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                BackColor = Theme.FieldBg, Font = Theme.Mono(9.5F), BorderStyle = BorderStyle.FixedSingle,
                WordWrap = false, Margin = new Padding(0), Text = "选择一条消息查看完整载荷"
            };
            grid.Controls.Add(_detail, 0, 1);

            card.Controls.Add(grid);
            return card;
        }

        // ------------------------------------------------------------------ 行为(与原实现一致)
        private void BtnAdd_Click(object? sender, EventArgs e)
        {
            string topic = _topicInput.Text.Trim();
            if (topic.Length == 0) return;
            if (!Topics.Contains(topic))
            {
                Topics = Topics.Append(topic).ToArray();
                SubscribeRequested?.Invoke(topic);
            }
            _topicInput.Text = "";
        }

        /// <summary>收到消息(Form 层投递, 已在 UI 线程)。</summary>
        public void OnMessage(string topic, string payload)
        {
            if (_chkPause.Checked) return;
            _count++;
            var item = new ListViewItem(DateTime.Now.ToString("HH:mm:ss.fff"));
            item.SubItems.Add(topic);
            item.SubItems.Add(payload.Length.ToString());
            string preview = payload.Replace("\r", "").Replace("\n", " ");
            if (preview.Length > 120) preview = preview[..120] + "…";
            item.SubItems.Add(preview);
            item.Tag = payload;
            _pending.Add(item);
            if (_pending.Count > 200) _pending.RemoveAt(0);
            _list.BeginUpdate();
            _list.Items.Add(item);
            // 列表滚动窗口: 超过 400 条移除最旧, 保证最新消息始终可见
            while (_list.Items.Count > 400) _list.Items.RemoveAt(0);
            _list.EndUpdate();
            _list.EnsureVisible(_list.Items.Count - 1);
            UpdateStat();
        }

        private void UpdateStat()
        {
            double rate = _rateWatch.Elapsed.TotalSeconds > 0 ? _count / _rateWatch.Elapsed.TotalMinutes : 0;
            _statLabel.Text = $"已接收 {_count} 条 · {rate:0}/min · 缓冲 {_pending.Count}";
        }

        public void SetTopics(string[] topics)
        {
            Topics = topics;
            foreach (var t in topics) SubscribeRequested?.Invoke(t);
        }
    }
}
