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
        private Label _statLabel = null!;
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
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 顶栏: 高度随内容自适应, 不再固定 44 硬塞
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 60f));  // 列表
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 40f));  // 详情

            body.Controls.Add(BuildTopBar(), 0, 0);
            _list = BuildList();
            body.Controls.Add(_list, 0, 1);
            body.Controls.Add(BuildDetail(), 0, 2);
            Controls.Add(body);
        }

        // ------------------------------------------------------------------ 顶栏
        private Control BuildTopBar()
        {
            var top = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 12),              // 顶栏 → 列表留白 ≥ 10
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };

            var lblTopic = new Label
            {
                Text = "订阅主题", AutoSize = true, Font = Theme.Ui(9.5F, bold: true),
                ForeColor = Theme.TextSecondary, BackColor = Color.Transparent,
                Margin = new Padding(8, 15, 8, 8)              // 四边均 ≥ 8(6.2-1); 15 = (48-18)/2 垂直居中
            };
            top.Controls.Add(lblTopic);

            _topicInput = new TextBox
            {
                Font = Theme.Mono(9.5F), Text = "green/data", BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 12, 10, 8)              // 12 = 8 + (32-24)/2
            };
            // 宽度按实测文案算(U1 / 6.2-1), 不写死 200
            _topicInput.Width = UiFit.TextWidth("green/data", _topicInput.Font) + UiFit.Scale(_topicInput, 48);
            top.Controls.Add(_topicInput);

            var btnAdd = new RoundedButton { Text = "添加订阅", FillColor = Theme.Teal, Margin = new Padding(0, 8, 10, 8) };
            UiFit.SizeButton(btnAdd, padTotal: 40, minWidth: 96);   // 6.2-3: 按钮一律实测宽度
            btnAdd.Click += BtnAdd_Click;
            top.Controls.Add(btnAdd);

            var btnClear = new RoundedButton { Text = "清空记录", FillColor = Theme.TextTertiary, Margin = new Padding(0, 8, 10, 8) };
            UiFit.SizeButton(btnClear, padTotal: 40, minWidth: 96);
            btnClear.Click += (s, e) => { _list.Items.Clear(); _detail.Clear(); _count = 0; UpdateStat(); };
            top.Controls.Add(btnClear);

            _chkPause = new CheckBox
            {
                Text = "暂停接收", AutoSize = true, Font = Theme.Ui(9.5F), ForeColor = Theme.TextPrimary,
                BackColor = Color.Transparent,
                Margin = new Padding(8, 14, 10, 8)              // 14 = 8 + (32-20)/2; 左 8 与按钮组拉开距离
            };
            top.Controls.Add(_chkPause);

            _statLabel = new Label
            {
                Text = "已接收 0 条", AutoSize = true, Font = Theme.Ui(9.5F, bold: true),
                ForeColor = Theme.Primary, BackColor = Color.Transparent,
                Margin = new Padding(12, 15, 0, 8)
            };
            top.Controls.Add(_statLabel);

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
                HideSelection = false, MultiSelect = false,
                Margin = new Padding(0, 0, 0, 12)               // 列表 → 详情卡留白 ≥ 10
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
                MessageSelected?.Invoke(payload);
            };
            return list;
        }

        /// <summary>
        /// 列宽动态分配: 时间 110 / 主题 max(140, 客户区宽度*18%) / 长度 60 / 预览 = 剩余宽度。
        /// 用 ListView 的实际客户区宽度(已扣掉竖向滚动条), 所以不会冒出横向滚动条吃掉内容。
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
                int wTime = UiFit.Scale(list, 110);
                int wLength = UiFit.Scale(list, 60);
                int wTopic = Math.Max(UiFit.Scale(list, 140), (int)Math.Round(clientW * 0.18));
                int wPreview = clientW - wTime - wTopic - wLength;

                int minPreview = UiFit.Scale(list, 160);
                if (wPreview < minPreview)
                {
                    // 窗口偏窄: 先把主题列压到 110 的下限, 保住预览列
                    wTopic = Math.Max(UiFit.Scale(list, 110), wTopic - (minPreview - wPreview));
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

            Label title = UiFit.SectionTitle("载荷全文");
            title.Margin = new Padding(0, 0, 0, 8);
            grid.Controls.Add(title, 0, 0);

            _detail = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                BackColor = Theme.FieldBg, Font = Theme.Mono(9.5F), BorderStyle = BorderStyle.FixedSingle,
                WordWrap = false, Margin = new Padding(0)
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
