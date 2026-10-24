// pages/sensor/sensor.js
// 传感器数据页: 通过 MQTT over WebSocket 订阅上位机发布的 JSON 数据
// 稳定性设计(资深物联网实践):
//   1. CONNECT 携带 keep-alive, 并以 keep-alive/2 周期发送 PINGREQ 心跳
//   2. 异常断开自动重连(指数退避, 上限 MAX_RECONNECT 次), 手动断开不重连
//   3. CONNACK/SUBACK 返回码逐一校验, 失败给出明确提示
//   4. 关闭前发送 DISCONNECT 报文优雅退出, 避免 EMQX 侧会话滞留
const app = getApp()

/* 统一报警阈值: 与 STM32 固件(ALARM_*_MAX) / PC 上位机保持完全一致 */
const THRESHOLDS = {
  temperature: 26,   // 温度报警上限(°C)
  humidity: 85,      // 空气湿度报警上限(%RH)
  soilMoisture: 40   // 土壤湿度报警上限(%, 过湿)
}

const KEEP_ALIVE = 60            // MQTT keep-alive(秒)
const PING_INTERVAL_MS = 30000   // PINGREQ 周期 = keep-alive/2
const MAX_RECONNECT = 3          // 异常断开后的最大自动重连次数
const HISTORY_KEY = 'mqtt_history'
const HISTORY_MAX = 5

Page({
  data: {
    mqttConnected: false,
    connecting: false,
    showConfig: false,
    mqttHost: '192.168.36.221',
    mqttPort: '8083',
    mqttTopic: '',        // 默认留空, 强制用户手动输入(任务要求)
    history: [],          // 成功连接过的 {host, port, topic} 历史记录
    sensorData: {
      temperature: null,
      humidity: null,
      soilMoisture: null,
      airQuality: null,
      servoStatus: null,
      motorStatus: null,
      alarmType: null,
      warning: false
    },
    thresholds: THRESHOLDS,
    lastUpdateTime: null
  },

  /* ---------- 运行期状态(不参与渲染) ---------- */
  _socketTask: null,       // wx SocketTask
  _pingTimer: null,        // PINGREQ 心跳定时器
  _reconnectTimer: null,   // 重连等待定时器
  _reconnectCount: 0,      // 已重连次数
  _manualClosed: false,    // 用户主动断开标记
  _prevWarning: false,     // 上一帧报警状态(用于边沿触发震动)

  onLoad() {
    this.loadConfig()
    this.loadHistory()
  },

  onUnload() {
    this.teardownSocket()
  },

  /* ==================== 配置与历史 ==================== */

  // 加载持久化的连接配置
  loadConfig() {
    try {
      const host = wx.getStorageSync('mqtt_host')
      const port = wx.getStorageSync('mqtt_port')
      const topic = wx.getStorageSync('mqtt_topic')
      if (host) this.setData({ mqttHost: host })
      if (port) this.setData({ mqttPort: port })
      if (topic) this.setData({ mqttTopic: topic })  // 历史上填过才回填, 否则保持空白
    } catch (error) {
      console.error('加载配置失败:', error)
    }
  },

  // 加载连接成功过的历史记录
  loadHistory() {
    try {
      const history = wx.getStorageSync(HISTORY_KEY) || []
      this.setData({ history })
    } catch (error) {
      console.error('加载历史记录失败:', error)
    }
  },

  // 连接成功(收到SUBACK)后保存历史: 去重、最新在前、最多 HISTORY_MAX 条
  saveHistory(entry) {
    try {
      let history = (wx.getStorageSync(HISTORY_KEY) || []).filter(
        (item) => !(item.host === entry.host && item.port === entry.port && item.topic === entry.topic)
      )
      history.unshift(entry)
      history = history.slice(0, HISTORY_MAX)
      wx.setStorageSync(HISTORY_KEY, history)
      this.setData({ history })
    } catch (error) {
      console.error('保存历史记录失败:', error)
    }
  },

  // 点击历史记录 → 回填到输入框(用户确认后再点"保存并连接")
  onHistoryTap(e) {
    const item = e.currentTarget.dataset.item
    if (!item) return
    this.setData({
      mqttHost: item.host,
      mqttPort: item.port,
      mqttTopic: item.topic
    })
    wx.showToast({ title: '已填入所选配置', icon: 'none' })
  },

  // 删除单条历史
  onHistoryRemove(e) {
    const index = e.currentTarget.dataset.index
    const history = this.data.history.filter((_, i) => i !== index)
    this.setData({ history })
    try {
      wx.setStorageSync(HISTORY_KEY, history)
    } catch (error) {
      console.error('删除历史记录失败:', error)
    }
  },

  /* ==================== 配置弹窗 ==================== */

  showMqttConfig() {
    this.setData({ showConfig: true })
  },

  hideConfig() {
    this.setData({ showConfig: false })
  },

  stopPropagation() {},

  onHostInput(e) { this.setData({ mqttHost: e.detail.value }) },
  onPortInput(e) { this.setData({ mqttPort: e.detail.value }) },
  onTopicInput(e) { this.setData({ mqttTopic: e.detail.value }) },

  // 保存配置并连接
  saveConfigAndConnect() {
    const { mqttHost, mqttPort, mqttTopic } = this.data

    if (!mqttHost.trim() || !mqttPort.trim()) {
      wx.showToast({ title: '请填写服务器地址和端口', icon: 'none' })
      return
    }
    // 订阅主题必须由用户显式输入(默认空白, 不再内置 LCW2)
    if (!mqttTopic.trim()) {
      wx.showToast({ title: '请输入订阅主题(需与上位机发布主题一致)', icon: 'none' })
      return
    }

    try {
      wx.setStorageSync('mqtt_host', mqttHost.trim())
      wx.setStorageSync('mqtt_port', mqttPort.trim())
      wx.setStorageSync('mqtt_topic', mqttTopic.trim())
    } catch (error) {
      console.error('保存配置失败:', error)
    }

    this.setData({ showConfig: false })
    this.connectMqtt()
  },

  /* ==================== MQTT 连接管理 ==================== */

  connectMqtt() {
    const { mqttHost, mqttPort, mqttTopic } = this.data
    const connectUrl = `ws://${mqttHost}:${mqttPort}/mqtt`

    console.log('===== MQTT连接开始 =====', connectUrl, '主题:', mqttTopic)

    this.teardownSocket()          // 防止旧连接残留
    this._manualClosed = false
    this._reconnectCount = 0
    this.setData({ connecting: true })

    try {
      const socketTask = wx.connectSocket({
        url: connectUrl,
        protocols: ['mqtt'],       // EMQX WebSocket 子协议
        success: () => console.log('✓ WebSocket连接请求已发送'),
        fail: (err) => {
          console.error('✗ WebSocket连接请求失败:', err)
          this.setData({ connecting: false })
          wx.showModal({
            title: '连接失败',
            content: `无法连接到 ${connectUrl}\n${err.errMsg || '未知错误'}\n\n请检查:\n1. EMQX是否已启动\n2. IP与端口是否正确\n3. 防火墙是否放行 ${mqttPort} 端口`,
            showCancel: false
          })
        }
      })
      this._socketTask = socketTask

      socketTask.onOpen(() => {
        console.log('✓ WebSocket已建立, 发送MQTT CONNECT')
        // keep-alive 60s, clean session; EMQX 将在 1.5×keep-alive 无心跳/数据时断开,
        // 因此必须配合 PINGREQ 心跳(见 startPingTimer)
        this.sendMqttConnect(socketTask, 'wx_sensor_' + Math.random().toString(16).substring(2, 10))
      })

      socketTask.onMessage((res) => this.handleMqttMessage(res.data))

      socketTask.onError((err) => {
        console.error('✗ WebSocket错误:', err)
        this.handleAbnormalClose('连接出现错误')
      })

      socketTask.onClose((res) => {
        console.log('✗ WebSocket关闭 code=', res.code, 'reason=', res.reason)
        this.handleAbnormalClose(res.code === 1000 ? '连接已断开' : '连接异常断开')
      })
    } catch (error) {
      console.error('✗ 创建WebSocket异常:', error)
      this.setData({ connecting: false })
    }
  },

  // 统一处理异常断开: 停心跳 → 自动重连(未超次数) → 提示
  handleAbnormalClose(reason) {
    this.stopPingTimer()
    const wasConnected = this.data.mqttConnected
    this.setData({ mqttConnected: false, connecting: false })

    if (this._manualClosed) return

    if (this._reconnectCount < MAX_RECONNECT) {
      // 指数退避: 2s, 4s, 8s
      const delay = 2000 * Math.pow(2, this._reconnectCount)
      this._reconnectCount += 1
      console.log(`⏳ ${delay / 1000}s后进行第${this._reconnectCount}次重连...`)
      wx.showToast({ title: `${reason},${delay / 1000}s后自动重连`, icon: 'none' })
      if (this._reconnectTimer) clearTimeout(this._reconnectTimer)
      this._reconnectTimer = setTimeout(() => this.connectMqtt(), delay)
    } else {
      wx.showModal({
        title: '连接失败',
        content: `${reason}, 已自动重试${MAX_RECONNECT}次仍失败。\n\n请检查:\n1. EMQX服务器是否在运行\n2. 手机与服务器是否同一局域网\n3. 上位机是否在发布数据`,
        showCancel: false
      })
    }
    void wasConnected
  },

  // 用户手动断开: 发送 DISCONNECT 后关闭, 不触发自动重连
  disconnectMqtt() {
    this._manualClosed = true
    if (this._socketTask) {
      try {
        this._socketTask.send({ data: new Uint8Array([0xE0, 0x00]).buffer })  // DISCONNECT
      } catch (e) { /* 连接可能已断, 忽略 */ }
      this._socketTask.close({
        success: () => console.log('✓ 已主动断开')
      })
    }
    this.teardownSocket()
    this.setData({ mqttConnected: false, connecting: false })
    wx.showToast({ title: '已断开连接', icon: 'none' })
  },

  // 释放定时器与套接字
  teardownSocket() {
    this.stopPingTimer()
    if (this._reconnectTimer) {
      clearTimeout(this._reconnectTimer)
      this._reconnectTimer = null
    }
    if (this._socketTask) {
      try { this._socketTask.close({}) } catch (e) { /* 忽略 */ }
      this._socketTask = null
    }
  },

  /* ==================== MQTT 报文构造(3.1.1 最小子集) ==================== */

  // CONNECT: 协议级 MQTT 4, clean session, keep-alive = KEEP_ALIVE
  sendMqttConnect(socketTask, clientId) {
    const protocolName = 'MQTT'
    const packet = [0x10]  // CONNECT 固定头

    const variableHeader = []
    variableHeader.push(0, protocolName.length)
    for (let i = 0; i < protocolName.length; i++) variableHeader.push(protocolName.charCodeAt(i))
    variableHeader.push(4)     // protocol level 4 (MQTT 3.1.1)
    variableHeader.push(0x02)  // connect flags: clean session
    variableHeader.push((KEEP_ALIVE >> 8) & 0xFF, KEEP_ALIVE & 0xFF)  // keep-alive

    const clientIdBytes = []
    clientIdBytes.push(0, clientId.length)
    for (let i = 0; i < clientId.length; i++) clientIdBytes.push(clientId.charCodeAt(i))

    const remainingLength = variableHeader.length + clientIdBytes.length
    packet.push(remainingLength)
    packet.push(...variableHeader, ...clientIdBytes)

    socketTask.send({ data: new Uint8Array(packet).buffer })
    console.log('✓ CONNECT已发送, keep-alive =', KEEP_ALIVE, 's')
  },

  // SUBSCRIBE: QoS 0 订阅, packet id = 1
  sendMqttSubscribe(socketTask, topic) {
    const packet = [0x82]  // SUBSCRIBE 固定头
    const topicBytes = []
    topicBytes.push(0, topic.length)
    for (let i = 0; i < topic.length; i++) topicBytes.push(topic.charCodeAt(i))
    topicBytes.push(0)  // QoS 0

    const body = [0, 1, ...topicBytes]  // packet identifier = 1
    packet.push(body.length)
    packet.push(...body)

    socketTask.send({ data: new Uint8Array(packet).buffer })
    console.log('✓ SUBSCRIBE已发送, 主题:', topic)
  },

  // PINGREQ: 心跳保活, 防止 EMQX 按 keep-alive 判定离线
  sendPingReq() {
    if (!this._socketTask || !this.data.mqttConnected) return
    try {
      this._socketTask.send({ data: new Uint8Array([0xC0, 0x00]).buffer })  // PINGREQ
    } catch (e) {
      console.error('✗ PINGREQ发送失败:', e)
    }
  },

  startPingTimer() {
    this.stopPingTimer()
    this._pingTimer = setInterval(() => this.sendPingReq(), PING_INTERVAL_MS)
  },

  stopPingTimer() {
    if (this._pingTimer) {
      clearInterval(this._pingTimer)
      this._pingTimer = null
    }
  },

  /* ==================== 报文解析 ==================== */

  handleMqttMessage(data) {
    try {
      const bytes = new Uint8Array(data)
      const type = bytes[0] >> 4

      if (type === 2) {
        // CONNACK: bytes[3] 为返回码, 0=接受
        const returnCode = bytes[3]
        if (returnCode === 0) {
          console.log('✓ CONNACK接受, 开始订阅')
          this.setData({ mqttConnected: true, connecting: false })
          this.startPingTimer()
          setTimeout(() => this.sendMqttSubscribe(this._socketTask, this.data.mqttTopic), 300)
        } else {
          const reasons = ['连接被接受', '协议版本不支持', '客户端ID不合法', '服务器不可用', '用户名密码错误', '未授权']
          this.handleAbnormalClose(`服务器拒绝连接: ${reasons[returnCode] || returnCode}`)
        }
      } else if (type === 9) {
        // SUBACK: bytes[4] 为授予QoS, 0x80=失败
        const granted = bytes.length > 4 ? bytes[4] : 0
        if (granted === 0x80) {
          this.handleAbnormalClose('订阅被拒绝(检查主题权限)')
          return
        }
        console.log('✓ SUBACK订阅成功, QoS', granted)
        this.setData({ connecting: false })
        // 订阅成功才算"连接过", 此时才写入历史记录
        this.saveHistory({
          host: this.data.mqttHost,
          port: this.data.mqttPort,
          topic: this.data.mqttTopic
        })
        wx.showToast({ title: '订阅成功, 开始接收数据', icon: 'success' })
      } else if (type === 3) {
        // PUBLISH
        this.parsePublish(bytes)
      } else if (type === 13) {
        // PINGRESP: 心跳应答, 连接健康
        console.log('♥ PINGRESP')
      } else {
        console.log('收到其他报文类型:', type)
      }
    } catch (error) {
      console.error('✗ 解析MQTT报文失败:', error)
    }
  },

  // 解析 PUBLISH 载荷并更新界面
  parsePublish(bytes) {
    // 可变长度剩余长度字段
    let pos = 1
    let multiplier = 1
    let remainingLength = 0
    let byte
    do {
      byte = bytes[pos++]
      remainingLength += (byte & 0x7F) * multiplier
      multiplier *= 128
    } while ((byte & 0x80) !== 0)

    // 跳过主题
    const topicLength = (bytes[pos] << 8) + bytes[pos + 1]
    pos += 2 + topicLength

    // QoS>0 的 PUBLISH 载荷前有 2 字节报文标识符(本页以QoS0订阅, 一般不会出现)
    const qos = (bytes[0] >> 1) & 0x03
    if (qos > 0) pos += 2

    const payloadBytes = bytes.slice(pos)
    let payload = ''
    for (let i = 0; i < payloadBytes.length; i++) payload += String.fromCharCode(payloadBytes[i])

    const jsonData = JSON.parse(payload)
    const warning = !!(jsonData.warning || jsonData.hasAlarm)

    this.setData({
      sensorData: {
        temperature: jsonData.temperature,
        humidity: jsonData.humidity,
        // 上位机 v2.3 起字段名为 soilMoisture, 兼容旧 waterLevel
        soilMoisture: jsonData.soilMoisture != null ? jsonData.soilMoisture : jsonData.waterLevel,
        airQuality: jsonData.airQuality,
        servoStatus: jsonData.servoStatus,
        motorStatus: jsonData.motorStatus,
        alarmType: jsonData.alarmType || null,
        warning
      },
      lastUpdateTime: this.formatTime(new Date())
    })

    // 报警状态由"正常→报警"跳变时震动提醒一次, 避免持续刷屏
    if (warning && !this._prevWarning) {
      wx.vibrateShort({ type: 'heavy' })
    }
    this._prevWarning = warning
  },

  formatTime(date) {
    const hour = date.getHours().toString().padStart(2, '0')
    const minute = date.getMinutes().toString().padStart(2, '0')
    const second = date.getSeconds().toString().padStart(2, '0')
    return `${hour}:${minute}:${second}`
  }
})
