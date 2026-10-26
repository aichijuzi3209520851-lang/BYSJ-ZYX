// pages/actuator/actuator.js
// 执行器控制页: 订阅 green/data 展示设备实时状态, 发布控制指令到 green/control
// 控制链路: 小程序 → EMQX(green/control) → PC上位机(原文转发串口) → STM32
// 固件侧约定(main.c 命令任务):
//   mode-0 / mode-1           自动/手动模式(任意时刻生效)
//   DUOJ-0 / DUOJ-1           天窗关(0°) / 天窗开(180°) —— 仅手动模式生效
//   MOTOR-0 / MOTOR-1         风扇停 / 风扇全速 —— 仅手动模式生效
const { createMqttClient } = require('../../utils/mqtt')

const CONTROL_TOPIC = 'green/control'   // 指令发布主题, 与上位机订阅主题一致
const HISTORY_KEY = 'mqtt_history'      // 与传感器页共用同一份连接历史
const HISTORY_MAX = 5

Page({
  data: {
    mqttConnected: false,
    connecting: false,
    showConfig: false,
    mqttHost: '192.168.36.221',
    mqttPort: '8083',
    mqttTopic: '',        // 数据主题(green/data), 与传感器页同一份存储
    controlTopic: CONTROL_TOPIC,
    history: [],
    mode: 'auto',         // 'auto' 自动联动 | 'manual' 手动控制(固件上电默认自动)
    device: {
      servoStatus: null,  // 'ON' | 'OFF' | null(未收到数据)
      motorStatus: null
    },
    lastCmd: null,        // { text, time } 最近下发的指令
    lastUpdateTime: null
  },

  /* ---------- 运行期状态(不参与渲染) ---------- */
  _client: null,          // MQTT 客户端实例
  _activeConfig: null,    // 当前连接所用配置(判断配置是否变化)
  _manualClosed: false,   // 用户在本页主动断开标记

  onShow() {
    this.loadConfig()
    this.loadHistory()
    this.tryAutoConnect()
  },

  onUnload() {
    if (this._client) this._client.destroy()
  },

  /* ==================== 配置与历史(与传感器页共用存储) ==================== */

  loadConfig() {
    try {
      const host = wx.getStorageSync('mqtt_host')
      const port = wx.getStorageSync('mqtt_port')
      const topic = wx.getStorageSync('mqtt_topic')
      if (host) this.setData({ mqttHost: host })
      if (port) this.setData({ mqttPort: port })
      if (topic) this.setData({ mqttTopic: topic })
    } catch (error) {
      console.error('加载配置失败:', error)
    }
  },

  loadHistory() {
    try {
      this.setData({ history: wx.getStorageSync(HISTORY_KEY) || [] })
    } catch (error) {
      console.error('加载历史记录失败:', error)
    }
  },

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

  onHistoryTap(e) {
    const item = e.currentTarget.dataset.item
    if (!item) return
    this.setData({ mqttHost: item.host, mqttPort: item.port, mqttTopic: item.topic })
    wx.showToast({ title: '已填入所选配置', icon: 'none' })
  },

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

  saveConfigAndConnect() {
    const { mqttHost, mqttPort, mqttTopic } = this.data

    if (!mqttHost.trim() || !mqttPort.trim()) {
      wx.showToast({ title: '请填写服务器地址和端口', icon: 'none' })
      return
    }
    if (!mqttTopic.trim()) {
      wx.showToast({ title: '请输入数据主题(需与上位机发布主题一致)', icon: 'none' })
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
    this._manualClosed = false
    this.startClient({
      host: mqttHost.trim(),
      port: mqttPort.trim(),
      topic: mqttTopic.trim()
    })
  },

  /* ==================== 连接管理 ==================== */

  // onShow 时按需建连: 配置齐全且(无连接/配置变化/已断)才重连, 避免每次切 tab 都重连
  tryAutoConnect() {
    const { mqttHost, mqttPort, mqttTopic } = this.data
    if (!mqttTopic.trim()) return  // 未配置过, 等待用户点"配置"

    const cfg = { host: mqttHost.trim(), port: mqttPort.trim(), topic: mqttTopic.trim() }
    const same = this._activeConfig &&
      this._activeConfig.host === cfg.host &&
      this._activeConfig.port === cfg.port &&
      this._activeConfig.topic === cfg.topic

    if (this._client && same && (this.data.mqttConnected || this.data.connecting)) return
    if (this._manualClosed && same) return  // 用户主动断开后不自动重连
    this._manualClosed = false
    this.startClient(cfg)
  },

  startClient(cfg) {
    console.log('===== 执行器页 MQTT连接开始 =====', `ws://${cfg.host}:${cfg.port}/mqtt`, '数据主题:', cfg.topic)

    if (this._client) this._client.destroy()
    this._activeConfig = cfg

    this._client = createMqttClient({
      host: cfg.host,
      port: cfg.port,
      topics: [cfg.topic],
      clientIdPrefix: 'wx_actuator_',

      onState: (state, info) => this.handleClientState(state, info),
      onSubscribed: () => {
        this.saveHistory(cfg)
      },
      onPayload: (text) => this.handleSensorJson(text),
      onLog: (level, msg) => console.log(`[${level}] ${msg}`)
    })
    this._client.connect()
  },

  handleClientState(state, info) {
    if (state === 'connecting') {
      this.setData({ connecting: true })
    } else if (state === 'connected') {
      this.setData({ mqttConnected: true, connecting: false })
    } else if (state === 'reconnecting') {
      this.setData({ mqttConnected: false, connecting: false })
      wx.showToast({ title: `连接断开,${info.delay / 1000}s后自动重连`, icon: 'none' })
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

  disconnectMqtt() {
    this._manualClosed = true
    if (this._client) this._client.disconnect()
    this.setData({ mqttConnected: false, connecting: false })
    wx.showToast({ title: '已断开连接', icon: 'none' })
  },

  /* ==================== 数据处理 ==================== */

  // 解析 green/data, 只取设备状态相关字段(环境数据在传感器页展示)
  handleSensorJson(text) {
    let jsonData
    try {
      jsonData = JSON.parse(text)
    } catch (error) {
      console.error('✗ JSON解析失败:', error)
      return
    }

    const patch = {
      'device.servoStatus': jsonData.servoStatus || null,
      'device.motorStatus': jsonData.motorStatus || null,
      lastUpdateTime: this.formatTime(new Date())
    }

    // 固件模式回显: 上位机已把固件 Mode:AUTO/MAN 转为 mode:"auto"/"manual",
    // 以固件实际模式为准覆盖本地状态(避免与上位机按钮切换失步)
    if (jsonData.mode === 'auto' || jsonData.mode === 'manual') {
      patch.mode = jsonData.mode
    }

    this.setData(patch)
  },

  /* ==================== 控制指令 ==================== */

  // 模式切换: mode 命令任意时刻生效, 不受当前模式限制
  setMode(e) {
    const target = e.currentTarget.dataset.mode
    if (!target || target === this.data.mode) return

    if (!this.data.mqttConnected || !this._client) {
      wx.showToast({ title: '请先连接服务器再切换模式', icon: 'none' })
      return
    }

    const cmd = target === 'manual' ? 'mode-1' : 'mode-0'
    if (!this._client.publish(CONTROL_TOPIC, cmd, 1)) {   // QoS1: 控制指令可靠送达
      wx.showToast({ title: '指令发送失败', icon: 'none' })
      return
    }
    this.setData({
      mode: target,
      lastCmd: { text: cmd, time: this.formatTime(new Date()) }
    })
    wx.showToast({ title: target === 'manual' ? '已切换手动模式' : '已切换自动模式', icon: 'none' })
  },

  // 设备开关(窗户/风扇共用): 仅手动模式下允许下发
  sendCommand(cmd) {
    if (!this.data.mqttConnected || !this._client) {
      wx.showToast({ title: '请先连接服务器', icon: 'none' })
      return
    }
    if (this.data.mode !== 'manual') {
      wx.showToast({ title: '自动模式下设备自主联动, 请先切换手动模式', icon: 'none' })
      return
    }
    if (!this._client.publish(CONTROL_TOPIC, cmd, 1)) {   // QoS1: 控制指令可靠送达
      wx.showToast({ title: '指令发送失败', icon: 'none' })
      return
    }
    this.setData({ lastCmd: { text: cmd, time: this.formatTime(new Date()) } })
    wx.showToast({ title: '指令已下发', icon: 'none' })
  },

  onWindowOpen() { this.sendCommand('DUOJ-1') },
  onWindowClose() { this.sendCommand('DUOJ-0') },
  onFanOn() { this.sendCommand('MOTOR-1') },
  onFanOff() { this.sendCommand('MOTOR-0') },

  formatTime(date) {
    const hour = date.getHours().toString().padStart(2, '0')
    const minute = date.getMinutes().toString().padStart(2, '0')
    const second = date.getSeconds().toString().padStart(2, '0')
    return `${hour}:${minute}:${second}`
  }
})
