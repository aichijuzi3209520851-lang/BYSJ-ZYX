// pages/sensor/sensor.js
// 传感器数据页: 通过共享 MQTT 模块(utils/mqtt.js)订阅上位机发布的 JSON 数据
// 页面职责: 连接配置/历史管理 + 阈值判定展示 + 报警提醒
const { createMqttClient } = require('../../utils/mqtt')

/* 统一报警阈值: 与 STM32 固件(ALARM_*_MAX) / PC 上位机保持完全一致 */
const THRESHOLDS = {
  temperature: 26,   // 温度报警上限(°C)
  humidity: 85,      // 空气湿度报警上限(%RH)
  soilMoisture: 40   // 土壤湿度报警上限(%, 过湿)
}

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
      lightPercent: null,   // 光照强度百分比(上位机 v2.4 起新增, 不参与报警)
      airQuality: null,
      alarmType: null,
      warning: false
    },
    thresholds: THRESHOLDS,
    lastUpdateTime: null
  },

  /* ---------- 运行期状态(不参与渲染) ---------- */
  _client: null,           // 共享 MQTT 客户端实例
  _prevWarning: false,     // 上一帧报警状态(用于边沿触发震动)

  onLoad() {
    this.loadConfig()
    this.loadHistory()
  },

  // 每次回到本页都刷新配置/历史: 与执行器页共用存储, 配置可能已在彼处修改
  onShow() {
    this.loadConfig()
    this.loadHistory()
  },

  onUnload() {
    if (this._client) this._client.destroy()
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
    console.log('===== MQTT连接开始 =====', `ws://${mqttHost}:${mqttPort}/mqtt`, '主题:', mqttTopic)

    if (this._client) this._client.destroy()

    this._client = createMqttClient({
      host: mqttHost.trim(),
      port: mqttPort.trim(),
      topics: [mqttTopic.trim()],
      clientIdPrefix: 'wx_sensor_',

      onState: (state, info) => this.handleClientState(state, info),
      onSubscribed: () => {
        // 订阅成功才算"连接过", 此时才写入历史记录
        this.saveHistory({
          host: this.data.mqttHost,
          port: this.data.mqttPort,
          topic: this.data.mqttTopic
        })
        wx.showToast({ title: '订阅成功, 开始接收数据', icon: 'success' })
      },
      onPayload: (text) => this.handleSensorJson(text),
      onLog: (level, msg) => console.log(`[${level}] ${msg}`)
    })
    this._client.connect()
  },

  // 将客户端生命周期状态映射到页面 UI
  handleClientState(state, info) {
    if (state === 'connecting') {
      this.setData({ connecting: true })
    } else if (state === 'connected') {
      this.setData({ mqttConnected: true, connecting: false })
    } else if (state === 'reconnecting') {
      this.setData({ mqttConnected: false, connecting: false })
      wx.showToast({ title: `${info.reason},${info.delay / 1000}s后自动重连`, icon: 'none' })
    } else if (state === 'failed') {
      this.setData({ mqttConnected: false, connecting: false })
      wx.showModal({
        title: '连接失败',
        content: `${info.reason}。\n\n请检查:\n1. EMQX服务器是否在运行\n2. 手机与服务器是否同一局域网\n3. 上位机是否在发布数据`,
        showCancel: false
      })
    } else if (state === 'closed') {
      this.setData({ mqttConnected: false, connecting: false })
    }
  },

  // 用户手动断开(不触发自动重连)
  disconnectMqtt() {
    if (this._client) this._client.disconnect()
    this.setData({ mqttConnected: false, connecting: false })
    wx.showToast({ title: '已断开连接', icon: 'none' })
  },

  /* ==================== 数据处理 ==================== */

  // 解析 green/data 的 JSON 载荷并更新界面
  handleSensorJson(text) {
    let jsonData
    try {
      jsonData = JSON.parse(text)
    } catch (error) {
      console.error('✗ JSON解析失败:', error, text)
      return
    }

    const warning = !!(jsonData.warning || jsonData.hasAlarm)

    this.setData({
      sensorData: {
        temperature: jsonData.temperature,
        humidity: jsonData.humidity,
        // 上位机 v2.3 起字段名为 soilMoisture, 兼容旧 waterLevel
        soilMoisture: jsonData.soilMoisture != null ? jsonData.soilMoisture : jsonData.waterLevel,
        // 上位机 v2.4 起新增 lightPercent(固件 Light:%d%%); 光照仅展示, 不参与报警判定
        lightPercent: jsonData.lightPercent != null ? jsonData.lightPercent : null,
        airQuality: jsonData.airQuality,
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

  // 跳转执行器页(设备控制入口)
  goToActuator() {
    wx.switchTab({ url: '/pages/actuator/actuator' })
  },

  formatTime(date) {
    const hour = date.getHours().toString().padStart(2, '0')
    const minute = date.getMinutes().toString().padStart(2, '0')
    const second = date.getSeconds().toString().padStart(2, '0')
    return `${hour}:${minute}:${second}`
  }
})
