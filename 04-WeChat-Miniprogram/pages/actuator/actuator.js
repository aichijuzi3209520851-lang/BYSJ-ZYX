// pages/actuator/actuator.js
// 执行器控制页: 订阅 green/data 展示设备实时状态, 发布控制指令到 green/control
// 控制链路: 小程序 → EMQX(green/control) → PC上位机(原文转发串口) → STM32
// 固件侧约定(main.c 命令任务):
//   mode-0 / mode-1           自动/手动模式(任意时刻生效)
//   DUOJ-0 / DUOJ-1           天窗关(0°) / 天窗开(180°) —— 仅手动模式生效
//   MOTOR-0 / MOTOR-1         风扇停 / 风扇全速 —— 仅手动模式生效
const {
  createMqttClient,
  DEFAULT_DATA_TOPIC,
  CONTROL_TOPIC,
  isControlTopic,
  checkHostReachability,
  describeVirtualHint,
  describeHostRisk
} = require('../../utils/mqtt')
const { guardLogin } = require('../../utils/auth')

const HISTORY_KEY = 'mqtt_history'      // 与传感器页共用同一份连接历史
const HISTORY_MAX = 5

Page({
  data: {
    mqttConnected: false,
    connecting: false,
    showConfig: false,
    mqttHost: '',
    mqttPort: '8083',
    hostHint: '',         // 地址填写提示(虚拟网卡 / 只在本机可用等)
    mqttTopic: '',        // 数据主题(green/data), 与传感器页同一份存储
    controlTopic: CONTROL_TOPIC,
    dataTopicDefault: DEFAULT_DATA_TOPIC,
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

  // 首行先做登录守卫: tabBar 页面可被直接切换进来, 未登录则 reLaunch 回登录页
  onShow() {
    if (!guardLogin()) return
    this.loadConfig()
    this.loadHistory()
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
      if (host) this.setData({ mqttHost: host, hostHint: checkHostReachability(host) || describeVirtualHint(host) })
      if (port) this.setData({ mqttPort: port })
      // 历史配置里可能残留被误填成指令主题的数据主题(如 green/control),
      // 直接丢弃不用, 否则一进页面就会重演"订阅自己的指令"的回环。
      // 顺手从本地存储里清掉, 免得下次启动又被读回来。
      if (topic && isControlTopic(topic)) {
        try { wx.removeStorageSync('mqtt_topic') } catch (e) { console.error('清除错误主题失败:', e) }
      } else if (topic) {
        this.setData({ mqttTopic: topic })
      }
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
    // 跳过错把指令主题存成数据主题的历史条目
    const topic = isControlTopic(item.topic) ? '' : item.topic
    this.setData({ mqttHost: item.host, mqttPort: item.port, mqttTopic: topic })
    if (!topic) {
      wx.showToast({ title: '该历史用的是指令主题, 请改填数据主题', icon: 'none' })
      return
    }
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

  onHostInput(e) {
    const value = e.detail.value
    // 边输入边提示: 虚拟网卡地址在模拟器里是"能连"的, 到手机上才失败,
    // 所以必须在填的时候就提醒, 而不是等连不上再猜。
    this.setData({ mqttHost: value, hostHint: checkHostReachability(value) || describeVirtualHint(value) })
  },
  onPortInput(e) { this.setData({ mqttPort: e.detail.value }) },
  onTopicInput(e) { this.setData({ mqttTopic: e.detail.value }) },

  saveConfigAndConnect() {
    const { mqttHost, mqttPort, mqttTopic } = this.data

    if (!mqttHost.trim() || !mqttPort.trim()) {
      wx.showToast({ title: '请填写服务器地址和端口', icon: 'none' })
      return
    }
    // 地址只在本机可达时给出明确提示 —— 这类地址在开发者工具模拟器里能连,
    // 到真机上必然失败, 不提示的话用户会以为是服务器或防火墙的问题。
    //
    // 分两级(与传感器页保持一致):
    //   block(172.x / 100.64-127 / 127.0.0.1) —— 手机一定到不了, 直接拦下
    //   warn (192.168.x.1 末位是 1)          —— 常见于虚拟网卡, 但用户可能真配了静态 .1,
    //                                          让他确认后放行
    // 两级都必须接进保存流程。早期版本只接 block, 而本机真正出事的那两个地址
    // (192.168.40.1 / 192.168.198.1)恰好都落在 warn 档, 于是保存时什么都不提示。
    const hostRisk = describeHostRisk(mqttHost)
    if (hostRisk.level === 'block') {
      wx.showModal({
        title: '这个地址手机连不上',
        content: hostRisk.message,
        showCancel: false,
        confirmText: '知道了'
      })
      return
    }
    if (hostRisk.level === 'warn') {
      wx.showModal({
        title: '确认这个地址吗',
        content: hostRisk.message,
        confirmText: '仍然使用',
        cancelText: '改一下',
        success: (res) => { if (res.confirm) this._saveAndStart() }
      })
      return
    }

    this._saveAndStart()
  },

  // 真正落地保存并建连(校验通过后调用)
  _saveAndStart() {
    const { mqttHost, mqttPort, mqttTopic } = this.data

    if (!mqttTopic.trim()) {
      wx.showToast({ title: '请输入数据主题(需与上位机发布主题一致)', icon: 'none' })
      return
    }
    // 数据主题必须与指令主题区分: 填成 green/control 会把本页发出的指令原样收回,
    // 再当 JSON 解析就刷 "Unexpected token 'm'/'D'/'M'"。
    if (isControlTopic(mqttTopic)) {
      wx.showModal({
        title: '主题填错了',
        content: `green/control 是下发指令的通道, 不能当数据主题订阅。数据主题应填 ${DEFAULT_DATA_TOPIC}。`,
        showCancel: false,
        confirmText: '知道了'
      })
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
    this.startClient({
      host: mqttHost.trim(),
      port: mqttPort.trim(),
      topic: mqttTopic.trim()
    })
  },

  /* ==================== 连接管理 ==================== */

  startClient(cfg) {
    console.log('===== 执行器页 MQTT连接开始 =====', `ws://${cfg.host}:${cfg.port}/mqtt`, '数据主题:', cfg.topic)

    if (this._client) this._client.destroy()

    this._client = createMqttClient({
      host: cfg.host,
      port: cfg.port,
      topics: [cfg.topic],
      clientIdPrefix: 'wx_actuator_',

      onState: (state) => this.handleClientState(state),
      onSubscribed: () => {
        this.saveHistory(cfg)
      },
      onPayload: (text) => this.handleSensorJson(text),
      onLog: (level, msg) => console.log(`[${level}] ${msg}`)
    })
    this._client.connect()
  },

  handleClientState(state) {
    if (state === 'connecting') {
      this.setData({ connecting: true })
    } else if (state === 'connected') {
      this.setData({ mqttConnected: true, connecting: false })
    } else if (state === 'failed' || state === 'closed') {
      // 不自动重连, 也不再弹失败/重连提示; 用户点"重连"手动发起
      this.setData({ mqttConnected: false, connecting: false })
    }
  },

  disconnectMqtt() {
    if (this._client) this._client.disconnect()
    this.setData({ mqttConnected: false, connecting: false })
    wx.showToast({ title: '已断开连接', icon: 'none' })
  },

  // 连接按钮: 已连接=断开, 未连接=用已保存配置手动重连, 无配置=打开配置
  onConnBtnTap() {
    if (this.data.connecting) return
    if (this.data.mqttConnected) {
      this.disconnectMqtt()
      return
    }
    if (this.data.mqttTopic.trim()) {
      this.reconnectMqtt()
      return
    }
    this.showMqttConfig()
  },

  // 手动重连: 使用当前已保存的服务器配置, 不自动重试
  reconnectMqtt() {
    const { mqttHost, mqttPort, mqttTopic } = this.data
    if (!mqttHost.trim() || !mqttPort.trim() || !mqttTopic.trim() || isControlTopic(mqttTopic)) {
      this.showMqttConfig()
      return
    }
    this.startClient({
      host: mqttHost.trim(),
      port: mqttPort.trim(),
      topic: mqttTopic.trim()
    })
  },

  /* ==================== 数据处理 ==================== */

  // 解析 green/data, 只取设备状态相关字段(环境数据在传感器页展示)
  handleSensorJson(text) {
    // 指令通道的纯文本指令(mode-1/DUOJ-1/MOTOR-1)不是 JSON。正常链路下已经订阅不到,
    // 但保留这道判断: 服务器侧若有主题转换或用户填错主题, 也不会再刷解析报错。
    const raw = String(text == null ? '' : text).trim()
    if (!raw.startsWith('{')) return

    let jsonData
    try {
      jsonData = JSON.parse(raw)
    } catch (error) {
      console.error('✗ JSON解析失败:', error, raw)
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
