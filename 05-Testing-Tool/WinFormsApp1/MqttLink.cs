using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using System.Text;

namespace IotLinkLab
{
    /// <summary>
    /// MQTT 链路服务(MQTTnet 4.3.7): 连接/发布/订阅 + 指数退避自动重连。
    /// 支持 TCP(1883) 与 WebSocket(8083) 两种接入, 事件回调全部投递回 UI 线程由调用方处理。
    /// 设计为通用组件: 不含任何温室业务, 业务在 FrameCodec/UI 层。
    /// </summary>
    public class MqttLink : IAsyncDisposable
    {
        private IMqttClient? _client;
        private CancellationTokenSource? _reconnectCts;
        private string _host = "127.0.0.1";
        private int _port = 1883;
        private bool _useWs;
        private bool _manualClose;
        private int _reconnectAttempt;

        public bool IsConnected => _client?.IsConnected ?? false;

        /// <summary>(state, detail) state: connecting / connected / reconnecting / disconnected / failed</summary>
        public event Action<string, string>? StateChanged;
        /// <summary>(topic, payload)</summary>
        public event Action<string, string>? PayloadReceived;
        public event Action<string>? Log;

        public async Task ConnectAsync(string host, int port, bool useWs)
        {
            await DisconnectCore(manual: true);
            _host = host; _port = port; _useWs = useWs;
            _manualClose = false;
            _reconnectCts = new CancellationTokenSource();
            StateChanged?.Invoke("connecting", $"ws://{host}:{port}/mqtt");
            await ConnectCoreAsync();
            _ = ReconnectLoopAsync(_reconnectCts.Token);
        }

        private async Task ConnectCoreAsync()
        {
            var factory = new MqttFactory();
            _client = factory.CreateMqttClient();
            _client.ApplicationMessageReceivedAsync += OnMessageAsync;
            _client.ConnectedAsync += _ =>
            {
                _reconnectAttempt = 0;
                StateChanged?.Invoke("connected", $"{_host}:{_port}");
                return Task.CompletedTask;
            };

            var builder = new MqttClientOptionsBuilder()
                .WithProtocolVersion(MqttProtocolVersion.V311)
                .WithClientId($"IotLinkLab_{Guid.NewGuid().ToString()[..8]}")
                .WithCleanSession()
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

            if (_useWs)
                builder.WithWebSocketServer(o => o.WithUri($"ws://{_host}:{_port}/mqtt"));
            else
                builder.WithTcpServer(_host, _port);

            var options = builder.Build();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var result = await _client.ConnectAsync(options, cts.Token);
            if (result.ResultCode != MqttClientConnectResultCode.Success)
                throw new Exception($"CONNACK: {result.ResultCode}");
        }

        private async Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                string payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                PayloadReceived?.Invoke(e.ApplicationMessage.Topic, payload);
            }
            catch (Exception ex) { Log?.Invoke("接收消息解析失败: " + ex.Message); }
            await Task.CompletedTask;
        }

        /// <summary>指数退避重连(2s/4s/8s... 上限 30s), 手动断开时退出。</summary>
        private async Task ReconnectLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, ct);
                    if (_manualClose || ct.IsCancellationRequested) break;
                    if (_client is { IsConnected: true }) continue;

                    _reconnectAttempt++;
                    int delay = Math.Min(30000, (int)Math.Pow(2, Math.Min(_reconnectAttempt, 5)) * 1000);
                    StateChanged?.Invoke("reconnecting", $"第 {_reconnectAttempt} 次重连({delay / 1000}s 后)");
                    await Task.Delay(delay, ct);
                    if (_manualClose) break;
                    Log?.Invoke($"自动重连 #{_reconnectAttempt} ...");
                    await ConnectCoreAsync();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log?.Invoke("重连失败: " + ex.Message);
                }
            }
        }

        public async Task SubscribeAsync(string topic)
        {
            if (_client is not { IsConnected: true }) throw new InvalidOperationException("MQTT 未连接");
            var opts = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic(topic).WithAtLeastOnceQoS())
                .Build();
            var res = await _client.SubscribeAsync(opts);
            foreach (var item in res.Items)
            {
                if (item.ResultCode is MqttClientSubscribeResultCode.GrantedQoS0
                    or MqttClientSubscribeResultCode.GrantedQoS1 or MqttClientSubscribeResultCode.GrantedQoS2)
                    Log?.Invoke($"订阅成功: {item.TopicFilter.Topic}");
                else
                    throw new Exception($"订阅被拒绝({item.ResultCode}): {topic}");
            }
        }

        public async Task PublishAsync(string topic, string payload, bool qos1)
        {
            if (_client is not { IsConnected: true }) throw new InvalidOperationException("MQTT 未连接");
            var msg = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(payload))
                .WithQualityOfServiceLevel(qos1
                    ? MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce
                    : MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
                .WithRetainFlag(false)
                .Build();
            await _client.PublishAsync(msg);
        }

        public async Task DisconnectAsync()
        {
            await DisconnectCore(manual: true);
            StateChanged?.Invoke("disconnected", "主动断开");
        }

        private async Task DisconnectCore(bool manual)
        {
            _manualClose = manual;
            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();
            _reconnectCts = null;
            if (_client != null)
            {
                _client.ApplicationMessageReceivedAsync -= OnMessageAsync;
                try
                {
                    if (_client.IsConnected)
                    {
                        var opts = new MqttClientDisconnectOptionsBuilder()
                            .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                            .Build();
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await _client.DisconnectAsync(opts, cts.Token);
                    }
                }
                catch { /* 断开失败不阻断退出 */ }
                _client.Dispose();
                _client = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectCore(manual: true);
            GC.SuppressFinalize(this);
        }
    }
}
