using System.Diagnostics;

namespace IotLinkLab
{
    /// <summary>
    /// 订阅监控台: 订阅任意主题列表, 实时显示消息流与计数统计, 可暂停接收。
    /// 通用能力, 不绑定温室协议。
    /// 布局: 订阅控制卡(48) + 消息流卡(60%) + 载荷详情卡(40%);
    /// 列宽按 ListView 客户区宽度动态分配, 列表与详情全部放进圆角容器。
    /// </summary>
    public class MonitorTab : UserControl
    {
        private ListView _list = null!;
        private TextBox _detail = null!, _topicInput = null!;
        private CheckBox _chkPause = null!;
        private Label _statLabel = null!, _detailTitle = null!, _detailMeta = null!, _listMeta = null!, _emptyLabel = null!;
        private SplitContainer _monSplit = null!;
        private TableLayoutPanel _body = null!;
        private Card _topCard = null!, _listCard = null!, _detailCard = null!;
        private FieldShell _topicShell = null!, _listShell = null!, _detailShell = null!;
        private bool _compactList;
        private bool _monSplitUser;
        private double _monSplitRatio = 0.60;
        private bool _monSplitSuppress;
        private bool _densityApplying;
        private int _fitDepth;          // 防「改列宽 → 滚动条变化 → 又触发改列宽」递归

        private readonly List<ListViewItem> _pending = new();
        private readonly Stopwatch _rateWatch = Stopwatch.StartNew();
        private readonly ToolTip _tip = new();
        private int _count;

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

            _body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _body.RowStyles.Add(new RowStyle(SizeType.Absolute, UiFit.S(48)));      // 订阅控制卡
            _body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));             // 列表 + 详情

            _topCard = new Card
            {
                Dock = DockStyle.Fill, Padding = new Padding(UiFit.S(Theme.GapMd), UiFit.S(Theme.GapSm), UiFit.S(Theme.GapMd), UiFit.S(Theme.GapSm)),
                Margin = new Padding(0, 0, 0, UiFit.S(Theme.GapMd))
            };
            _topCard.Controls.Add(BuildTopBar());
            _body.Controls.Add(_topCard, 0, 0);

            _monSplit = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                SplitterWidth = UiFit.S(8), BackColor = Theme.PageBg, Margin = new Padding(0)
            };
            // 最小高度由 ApplyMonSplit 自行夹取; 不在构造期设置 MinSize,
            // 否则控件尚未 Dock 出真实高度时会触发 SplitterDistance 校验异常。
            _monSplit.Panel1MinSize = 0;
            _monSplit.Panel2MinSize = 0;
            _monSplit.Panel1.BackColor = Theme.PageBg;
            _monSplit.Panel2.BackColor = Theme.PageBg;
            _monSplit.SplitterMoved += (s, e) =>
            {
                if (_monSplitSuppress || _monSplit.IsDisposed) return;
                int usable = _monSplit.Height - _monSplit.SplitterWidth;
                if (usable > UiFit.S(140) + UiFit.S(120))
                {
                    _monSplitRatio = (double)_monSplit.SplitterDistance / usable;
                    _monSplitUser = true;
                }
            };

            _listCard = BuildListCard();
            _detailCard = BuildDetailCard();
            _monSplit.Panel1.Controls.Add(_listCard);
            _monSplit.Panel2.Controls.Add(_detailCard);
            _body.Controls.Add(_monSplit, 0, 1);

            Controls.Add(_body);
            Resize += (s, e) => { ApplyResponsive(); ApplyMonSplit(); };
            ApplyResponsive();
            ApplyMonSplit();
        }

        private void ApplyResponsive()
        {
            if (_body == null || _body.IsDisposed || _densityApplying) return;
            _densityApplying = true;
            try
            {
                bool tight = Height < UiFit.S(560);
                int h = UiFit.S(tight ? 44 : 48);
                if ((int)_body.RowStyles[0].Height != h) _body.RowStyles[0].Height = h;
                int padY = UiFit.S(tight ? 6 : 8);
                _topCard.Padding = new Padding(UiFit.S(Theme.GapMd), padY, UiFit.S(Theme.GapMd), padY);
            }
            finally { _densityApplying = false; }
        }

        private void ApplyMonSplit()
        {
            if (_monSplit == null || _monSplit.IsDisposed || _monSplitUser) return;

            int usable = _monSplit.Height - _monSplit.SplitterWidth;
            if (usable <= UiFit.S(140) + UiFit.S(120)) return;

            int p1min = UiFit.S(140);
            int p2min = UiFit.S(120);
            int target = (int)Math.Round(usable * _monSplitRatio);
            target = Math.Max(p1min, Math.Min(usable - p2min, target));
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
                BackColor = Color.Transparent, Margin = new Padding(0)
            };
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
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
                Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, UiFit.S(Theme.GapSm), 0)
            };
            _topicInput = new TextBox
            {
                Font = Theme.Mono(9.5F), Text = "green/data", BorderStyle = BorderStyle.None
            };
            _topicShell = new FieldShell(_topicInput) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, UiFit.S(Theme.GapSm), 0) };

            var btnAdd = new RoundedButton
            {
                Text = "添加订阅", FillColor = Theme.Primary, Anchor = AnchorStyles.None,
                Height = UiFit.S(Theme.HeightButton), Margin = new Padding(0, 0, UiFit.S(Theme.GapSm), 0)
            };
            UiFit.SizeButton(btnAdd, padTotal: 32, minWidth: 88);
            btnAdd.Click += BtnAdd_Click;

            _chkPause = new CheckBox
            {
                Text = "暂停接收", AutoSize = true, Font = Theme.Body(), ForeColor = Theme.TextPrimary,
                BackColor = Color.Transparent, Anchor = AnchorStyles.None,
                Margin = new Padding(0, 0, UiFit.S(Theme.GapSm), 0)
            };

            var btnClear = new RoundedButton
            {
                Text = "清空记录", FillColor = Theme.TextSecondary, TextColor = Theme.TextSecondary, Outline = true,
                Anchor = AnchorStyles.None, Height = UiFit.S(Theme.HeightButton),
                Margin = new Padding(0, 0, UiFit.S(Theme.GapMd), 0)
            };
            UiFit.SizeButton(btnClear, padTotal: 32, minWidth: 88);
            btnClear.Click += (s, e) =>
            {
                _list.Items.Clear();
                _detail.Text = "选择一条消息查看完整载荷";
                _detailTitle.Text = "载荷全文";
                _detailMeta.Text = "";
                _count = 0;
                _emptyLabel.Visible = true;
                UpdateStat();
            };

            _statLabel = new Label
            {
                Text = "已接收 0 条", AutoSize = true, Font = Theme.Body(), ForeColor = Theme.Primary,
                BackColor = Color.Transparent, Anchor = AnchorStyles.Right, Margin = new Padding(0)
            };

            top.Controls.Add(lblTopic, 0, 0);
            top.Controls.Add(_topicShell, 1, 0);
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
        private Card BuildListCard()
        {
            var card = new Card
            {
                Dock = DockStyle.Fill, Padding = new Padding(UiFit.S(Theme.GapMd)),
                Margin = new Padding(0)
            };
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var head = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, UiFit.S(Theme.GapSm))
            };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            var title = new Label
            {
                Text = "消息流", AutoSize = true, Font = Theme.Section(), ForeColor = Theme.TextPrimary,
                BackColor = Color.Transparent, Anchor = AnchorStyles.Left, Margin = new Padding(0)
            };
            _listMeta = new Label
            {
                Text = "保留最近 400 条", AutoSize = true, Font = Theme.Meta(), ForeColor = Theme.TextTertiary,
                BackColor = Color.Transparent, Anchor = AnchorStyles.Right, Margin = new Padding(0)
            };
            head.Controls.Add(title, 0, 0);
            head.Controls.Add(_listMeta, 1, 0);
            grid.Controls.Add(head, 0, 0);

            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false,
                BackColor = Theme.FieldBg, BorderStyle = BorderStyle.None, Font = Theme.Mono(9F),
                HideSelection = false, MultiSelect = false, Margin = new Padding(0)
            };
            _list.Columns.Add("时间");
            _list.Columns.Add("主题");
            _list.Columns.Add("长度");
            _list.Columns.Add("载荷预览");
            // 行高 24: 借 ImageList 的高度把 ListView 行撑开, 避免 18px 行高过密
            _list.SmallImageList = new ImageList { ImageSize = new Size(1, UiFit.S(24)), ColorDepth = ColorDepth.Depth24Bit };
            _list.Resize += (s, e) => FitColumns(_list);
            _list.ClientSizeChanged += (s, e) => FitColumns(_list);   // 竖向滚动条出现/消失也会改变客户区宽度

            _list.SelectedIndexChanged += (s, e) =>
            {
                if (_list.SelectedItems.Count == 0) return;
                ListViewItem sel = _list.SelectedItems[0];
                // 完整载荷一直存在 Tag 里(见 OnMessage); 本表只有 4 列(下标 0~3),
                // 旧代码取 SubItems[4] 会抛越界异常, 这里优先 Tag, 再退回最后一个子项。
                string payload = sel.Tag as string ?? sel.SubItems[sel.SubItems.Count - 1].Text;
                _detail.Text = payload;
                _detailTitle.Text = "载荷全文";
                _detailMeta.Text = $"{payload.Length} 字符";
                MessageSelected?.Invoke(payload);
            };

            _listShell = new FieldShell(_list, multiline: true) { Dock = DockStyle.Fill, Margin = new Padding(0) };
            _emptyLabel = new Label
            {
                Dock = DockStyle.Fill, Text = "暂无消息 · 连接后自动订阅 green/data",
                TextAlign = ContentAlignment.MiddleCenter, Font = Theme.Meta(), ForeColor = Theme.TextTertiary,
                BackColor = Theme.FieldBg, Visible = true
            };
            _listShell.Controls.Add(_emptyLabel);
            _emptyLabel.BringToFront();

            grid.Controls.Add(_listShell, 0, 1);
            card.Controls.Add(grid);
            return card;
        }

        /// <summary>
        /// 列宽动态分配: 标准模式 时间108 / 主题20% / 长度60 / 预览=剩余;
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
                _compactList = clientW < UiFit.S(1200);
                int wTime = UiFit.S(_compactList ? 92 : 108);
                int wLength = _compactList ? 0 : UiFit.S(60);
                int wTopic = Math.Max(UiFit.S(_compactList ? 130 : 150), (int)Math.Round(clientW * 0.20));
                int wPreview = clientW - wTime - wTopic - wLength;

                int minPreview = UiFit.S(_compactList ? 160 : 220);
                if (wPreview < minPreview)
                {
                    // 窗口偏窄: 先把主题列压到 120 的下限, 保住预览列
                    wTopic = Math.Max(UiFit.S(120), wTopic - (minPreview - wPreview));
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
        private Card BuildDetailCard()
        {
            var card = new Card
            {
                Dock = DockStyle.Fill, Padding = new Padding(UiFit.S(Theme.GapMd)),
                Margin = new Padding(0)
            };
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var head = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, UiFit.S(Theme.GapSm))
            };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _detailTitle = new Label
            {
                Text = "载荷全文", AutoSize = true, Font = Theme.Section(), ForeColor = Theme.TextPrimary,
                BackColor = Color.Transparent, Anchor = AnchorStyles.Left, Margin = new Padding(0)
            };
            _detailMeta = new Label
            {
                Text = "", AutoSize = true, Font = Theme.Meta(), ForeColor = Theme.TextTertiary,
                BackColor = Color.Transparent, Anchor = AnchorStyles.Right, Margin = new Padding(0)
            };
            head.Controls.Add(_detailTitle, 0, 0);
            head.Controls.Add(_detailMeta, 1, 0);
            grid.Controls.Add(head, 0, 0);

            _detail = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                BackColor = Theme.FieldBg, Font = Theme.Mono(9.5F), BorderStyle = BorderStyle.None,
                WordWrap = false, Margin = new Padding(0), Text = "选择一条消息查看完整载荷"
            };
            _detailShell = new FieldShell(_detail, multiline: true) { Dock = DockStyle.Fill, Margin = new Padding(0) };
            grid.Controls.Add(_detailShell, 0, 1);

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
            _emptyLabel.Visible = false;
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
