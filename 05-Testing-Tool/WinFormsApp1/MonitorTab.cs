using System.Diagnostics;

namespace IotLinkLab
{
    /// <summary>
    /// 订阅监控台: 订阅任意主题列表, 实时显示消息流与计数统计,
    /// 可暂停接收。通用能力, 不绑定温室协议。
    /// </summary>
    public class MonitorTab : UserControl
    {
        private ListView _list = null!;
        private TextBox _detail = null!, _topicInput = null!;
        private CheckBox _chkPause = null!;
        private Label _statLabel = null!;
        private readonly List<ListViewItem> _pending = new();
        private readonly Stopwatch _rateWatch = Stopwatch.StartNew();
        private int _count;

        /// <summary>请求订阅某主题(Form 层转发给 MqttLink)。</summary>
        public event Action<string>? SubscribeRequested;
        /// <summary>选中某条消息时回调(payload), 供 JSON 高亮查看。</summary>
        public event Action<string>? MessageSelected;

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

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, BackColor = Color.Transparent };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 62f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38f));

            // ---------- 顶栏: 主题输入 + 统计 ----------
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, BackColor = Color.Transparent, WrapContents = false };
            top.Controls.Add(new Label { Text = "订阅主题", AutoSize = true, Font = Theme.Ui(9.5F, bold: true), ForeColor = Theme.TextSecondary, Margin = new Padding(2, 10, 6, 0) });
            _topicInput = new TextBox { Width = 200, Font = Theme.Mono(9.5F), Text = "green/data" };
            top.Controls.Add(_topicInput);
            var btnAdd = new RoundedButton { Text = "添加订阅", Width = 90, FillColor = Theme.Teal };
            btnAdd.Click += BtnAdd_Click;
            top.Controls.Add(btnAdd);
            var btnClear = new RoundedButton { Text = "清空记录", Width = 90, FillColor = Theme.TextTertiary };
            btnClear.Click += (s, e) => { _list.Items.Clear(); _detail.Clear(); _count = 0; UpdateStat(); };
            top.Controls.Add(btnClear);
            _chkPause = new CheckBox { Text = "暂停接收", AutoSize = true, Font = Theme.Ui(9.5F), Margin = new Padding(10, 8, 0, 0) };
            top.Controls.Add(_chkPause);
            _statLabel = new Label { Text = "已接收 0 条", AutoSize = true, Font = Theme.Ui(9.5F, bold: true), ForeColor = Theme.Primary, Margin = new Padding(12, 10, 0, 0) };
            top.Controls.Add(_statLabel);
            layout.Controls.Add(top, 0, 0);

            // ---------- 消息列表 ----------
            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false,
                BackColor = Theme.FieldBg, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Mono(9F),
                HideSelection = false
            };
            _list.Columns.Add("时间", 90);
            _list.Columns.Add("主题", 150);
            _list.Columns.Add("字节", 50);
            _list.Columns.Add("载荷预览", 620);
            _list.SelectedIndexChanged += (s, e) =>
            {
                if (_list.SelectedItems.Count > 0)
                {
                    string payload = _list.SelectedItems[0].SubItems[4].Text;
                    _detail.Text = payload;
                    MessageSelected?.Invoke(payload);
                }
            };
            layout.Controls.Add(_list, 0, 1);

            // ---------- 详情 ----------
            var detailWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
            var detailCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(8) };
            _detail = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                BackColor = Theme.FieldBg, Font = Theme.Mono(9.5F), BorderStyle = BorderStyle.None,
                WordWrap = false
            };
            detailCard.Controls.Add(_detail);
            detailWrap.Controls.Add(detailCard);
            layout.Controls.Add(detailWrap, 0, 2);

            Controls.Add(layout);
        }

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
