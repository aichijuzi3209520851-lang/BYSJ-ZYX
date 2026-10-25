/**
 * utils/mqtt.js
 * 最小 MQTT 3.1.1 over WebSocket 客户端(基于 wx.connectSocket 的手写协议实现)
 * 由传感器页(订阅 green/data)与执行器页(订阅 green/data + 发布 green/control)共用
 *
 * 协议子集: CONNECT/CONNACK, SUBSCRIBE/SUBACK, PUBLISH(QoS0 收发), PINGREQ/PINGRESP, DISCONNECT
 * 可靠性设计(资深物联网实践):
 *   1. CONNECT 携带 keep-alive, 并以 keep-alive/2 周期发送 PINGREQ 心跳
 *   2. 异常断开自动重连(指数退避 2s/4s/8s, 上限 MAX_RECONNECT 次), 手动断开不重连
 *   3. CONNACK/SUBACK 返回码逐一校验, 失败通过 onState 上报
 *   4. 手动断开前发送 DISCONNECT 报文优雅退出, 避免 EMQX 侧会话滞留
 *
 * 用法:
 *   const client = createMqttClient({
 *     host: '192.168.x.x', port: '8083',
 *     topics: ['green/data'],            // CONNACK 后自动订阅的主题列表
 *     clientIdPrefix: 'wx_sensor_',     // 客户端ID前缀(随机后缀防冲突)
 *     onState(state, info) {},          // connecting|connected|reconnecting|failed|closed
 *     onSubscribed(topic, qos) {},      // 每个 SUBACK 成功后触发一次
 *     onPayload(text, topic) {},        // PUBLISH 载荷(UTF-8 文本)
 *     onLog(level, msg) {}              // 调试日志透传
 *   })
 *   client.connect()
 *   client.publish('green/control', 'DUOJ-1')   // QoS0 发布, 返回是否已发出
 *   client.disconnect()                          // 用户主动断开(不重连)
 *   client.destroy()                             // 页体卸载时释放资源
 */

const KEEP_ALIVE = 60            // MQTT keep-alive(秒)
const PING_INTERVAL_MS = 30000   // PINGREQ 周期 = keep-alive/2
const MAX_RECONNECT = 3          // 异常断开后的最大自动重连次数
const RECONNECT_BASE_MS = 2000   // 重连基准退避(指数递增: 2s/4s/8s)

/* ---------- 编码工具 ---------- */

// 字符串 → UTF-8 字节数组(控制指令虽为 ASCII, 仍按标准编码保证通用性)
function utf8Bytes(str) {
  const out = []
  for (let i = 0; i < str.length; i++) {
    const code = str.codePointAt(i)
    if (code > 0xFFFF) i++ // 代理对已由 codePointAt 合并, 跳过低位代理
    if (code < 0x80) {
      out.push(code)
    } else if (code < 0x800) {
      out.push(0xC0 | (code >> 6), 0x80 | (code & 0x3F))
    } else if (code < 0x10000) {
      out.push(0xE0 | (code >> 12), 0x80 | ((code >> 6) & 0x3F), 0x80 | (code & 0x3F))
    } else {
      out.push(0xF0 | (code >> 18), 0x80 | ((code >> 12) & 0x3F),
        0x80 | ((code >> 6) & 0x3F), 0x80 | (code & 0x3F))
    }
  }
  return out
}

// 剩余长度字段(MQTT 变长编码: 每 7 位一组, 最高位置 1 表示续位)
function encodeRemainingLength(len) {
  const bytes = []
  do {
    let byte = len % 128
    len = Math.floor(len / 128)
    if (len > 0) byte |= 0x80
    bytes.push(byte)
  } while (len > 0)
  return bytes
}

/* ---------- 客户端工厂 ---------- */

function createMqttClient(opts) {
  let socketTask = null
  let pingTimer = null
  let reconnectTimer = null
  let reconnectCount = 0
  let packetIdSeq = 0
  let manualClosed = false   // 用户主动断开标记(断开后不再自动重连)
  let connected = false      // 是否已收到 CONNACK

  const log = (level, msg) => { if (opts.onLog) opts.onLog(level, msg) }
  const setState = (state, info) => { if (opts.onState) opts.onState(state, info || {}) }

  const nextPacketId = () => {
    packetIdSeq = (packetIdSeq % 0xFFFF) + 1
    return packetIdSeq
  }

  /* ==================== 报文构造 ==================== */

  // CONNECT: 协议级 MQTT 4, clean session, keep-alive = KEEP_ALIVE
  function buildConnect(clientId) {
    const packet = [0x10] // CONNECT 固定头
    const variableHeader = []
    variableHeader.push(0, 4)
    for (let i = 0; i < 4; i++) variableHeader.push('MQTT'.charCodeAt(i))
    variableHeader.push(4)     // protocol level 4 (MQTT 3.1.1)
    variableHeader.push(0x02)  // connect flags: clean session
    variableHeader.push((KEEP_ALIVE >> 8) & 0xFF, KEEP_ALIVE & 0xFF)

    const clientIdBytes = utf8Bytes(clientId)
    const idLen = [clientIdBytes.length >> 8, clientIdBytes.length & 0xFF]

    const body = [...variableHeader, ...idLen, ...clientIdBytes]
    packet.push(...encodeRemainingLength(body.length), ...body)
    return packet
  }

  // SUBSCRIBE: QoS 0 订阅指定主题
  function buildSubscribe(topic) {
    const topicBytes = utf8Bytes(topic)
    const body = [
      (packetIdSeq >> 8) & 0xFF, packetIdSeq & 0xFF,   // 报文标识符(调用前已由 nextPacketId 分配)
      topicBytes.length >> 8, topicBytes.length & 0xFF,
      ...topicBytes,
      0  // QoS 0
    ]
    return [0x82, ...encodeRemainingLength(body.length), ...body]
  }

  // PUBLISH: QoS 0 / 不保留 / 不重发
  function buildPublish(topic, message) {
    const topicBytes = utf8Bytes(topic)
    const payloadBytes = utf8Bytes(String(message))
    const body = [topicBytes.length >> 8, topicBytes.length & 0xFF, ...topicBytes, ...payloadBytes]
    return [0x30, ...encodeRemainingLength(body.length), ...body]
  }

  function sendBytes(bytes) {
    if (!socketTask) return false
    try {
      socketTask.send({ data: new Uint8Array(bytes).buffer })
      return true
    } catch (error) {
      log('error', '发送失败: ' + (error && error.errMsg ? error.errMsg : error))
      return false
    }
  }

  /* ==================== 心跳 ==================== */

  function startPingTimer() {
    stopPingTimer()
    pingTimer = setInterval(() => {
      if (connected) sendBytes([0xC0, 0x00]) // PINGREQ
    }, PING_INTERVAL_MS)
  }

  function stopPingTimer() {
    if (pingTimer) {
      clearInterval(pingTimer)
      pingTimer = null
    }
  }

  /* ==================== 连接管理 ==================== */

  function connect() {
    const url = `ws://${opts.host}:${opts.port}/mqtt`
    const clientId = (opts.clientIdPrefix || 'wx_mqtt_') +
      Date.now().toString(16) + Math.random().toString(16).slice(2, 6)

    teardownSocket()
    manualClosed = false
    reconnectCount = 0
    setState('connecting', { url })

    try {
      socketTask = wx.connectSocket({
        url,
        protocols: ['mqtt'],  // EMQX WebSocket 子协议
        fail: (err) => {
          log('error', 'WebSocket连接请求失败: ' + (err.errMsg || ''))
          setState('failed', { reason: '无法发起连接: ' + (err.errMsg || '未知错误'), url })
        }
      })

      socketTask.onOpen(() => {
        log('info', 'WebSocket已建立, 发送MQTT CONNECT')
        sendBytes(buildConnect(clientId))
      })

      socketTask.onMessage((res) => handlePacket(res.data))

      socketTask.onError((err) => {
        log('error', 'WebSocket错误: ' + (err.errMsg || ''))
        handleAbnormalClose('连接出现错误')
      })

      socketTask.onClose((res) => {
        log('info', 'WebSocket关闭 code=' + res.code)
        handleAbnormalClose(res.code === 1000 ? '连接已断开' : '连接异常断开')
      })
    } catch (error) {
      log('error', '创建WebSocket异常: ' + error)
      setState('failed', { reason: '创建连接异常', url })
    }
  }

  // 统一处理异常断开: 停心跳 → 自动重连(未超次数) → 上报失败
  function handleAbnormalClose(reason) {
    stopPingTimer()
    connected = false
    if (manualClosed) {
      setState('closed', { reason })
      return
    }

    if (reconnectCount < MAX_RECONNECT) {
      const delay = RECONNECT_BASE_MS * Math.pow(2, reconnectCount)
      reconnectCount += 1
      log('info', `${delay / 1000}s后进行第${reconnectCount}次重连`)
      setState('reconnecting', { reason, delay, attempt: reconnectCount })
      if (reconnectTimer) clearTimeout(reconnectTimer)
      reconnectTimer = setTimeout(() => connect(), delay)
    } else {
      setState('failed', { reason: `${reason}, 已自动重试${MAX_RECONNECT}次仍失败` })
    }
  }

  function teardownSocket() {
    stopPingTimer()
    if (reconnectTimer) {
      clearTimeout(reconnectTimer)
      reconnectTimer = null
    }
    if (socketTask) {
      try { socketTask.close({}) } catch (e) { /* 连接可能已断, 忽略 */ }
      socketTask = null
    }
    connected = false
  }

  /* ==================== 报文解析 ==================== */

  function handlePacket(data) {
    try {
      const bytes = new Uint8Array(data)
      const type = bytes[0] >> 4

      if (type === 2) {
        // CONNACK: bytes[3] 为返回码, 0=接受
        const returnCode = bytes[3]
        if (returnCode === 0) {
          log('info', 'CONNACK接受, 开始订阅')
          connected = true
          setState('connected', {})
          startPingTimer()
          // 稍作等待再订阅, 与原实现节奏一致
          setTimeout(() => {
            (opts.topics || []).forEach((topic) => {
              nextPacketId()
              sendBytes(buildSubscribe(topic))
            })
          }, 300)
        } else {
          const reasons = ['连接被接受', '协议版本不支持', '客户端ID不合法', '服务器不可用', '用户名密码错误', '未授权']
          handleAbnormalClose('服务器拒绝连接: ' + (reasons[returnCode] || returnCode))
        }
      } else if (type === 9) {
        // SUBACK: bytes[4] 为授予QoS, 0x80=失败
        const granted = bytes.length > 4 ? bytes[4] : 0
        if (granted === 0x80) {
          handleAbnormalClose('订阅被拒绝(检查主题权限)')
          return
        }
        log('info', 'SUBACK订阅成功, QoS ' + granted)
        if (opts.onSubscribed) opts.onSubscribed(granted)
      } else if (type === 3) {
        // PUBLISH
        const parsed = parsePublish(bytes)
        if (parsed && opts.onPayload) opts.onPayload(parsed.text, parsed.topic)
      } else if (type === 13) {
        // PINGRESP: 心跳应答, 连接健康
        log('info', 'PINGRESP')
      } else {
        log('info', '收到其他报文类型: ' + type)
      }
    } catch (error) {
      log('error', '解析MQTT报文失败: ' + error)
    }
  }

  // 解析 PUBLISH: 变长剩余长度 → 跳过主题 → (QoS>0 再跳过报文标识符) → 载荷
  function parsePublish(bytes) {
    let pos = 1
    let multiplier = 1
    let remainingLength = 0
    let byte
    do {
      byte = bytes[pos++]
      remainingLength += (byte & 0x7F) * multiplier
      multiplier *= 128
    } while ((byte & 0x80) !== 0)

    const topicLength = (bytes[pos] << 8) + bytes[pos + 1]
    let topic = ''
    for (let i = 0; i < topicLength; i++) topic += String.fromCharCode(bytes[pos + 2 + i])
    pos += 2 + topicLength

    const qos = (bytes[0] >> 1) & 0x03
    if (qos > 0) pos += 2

    const payloadBytes = bytes.slice(pos)
    let text = ''
    for (let i = 0; i < payloadBytes.length; i++) text += String.fromCharCode(payloadBytes[i])
    return { topic, text }
  }

  /* ==================== 对外接口 ==================== */

  // QoS0 发布; 未连接时返回 false(调用方自行提示)
  function publish(topic, message) {
    if (!connected || !socketTask) {
      log('warn', '未连接, 丢弃发布: ' + topic)
      return false
    }
    return sendBytes(buildPublish(topic, message))
  }

  // 用户主动断开: 发送 DISCONNECT 后关闭, 不触发自动重连
  function disconnect() {
    manualClosed = true
    if (socketTask && connected) {
      try { socketTask.send({ data: new Uint8Array([0xE0, 0x00]).buffer }) } catch (e) { /* 忽略 */ }
    }
    teardownSocket()
    setState('closed', { reason: '主动断开' })
  }

  // 页面卸载时释放资源(不再上报状态)
  function destroy() {
    manualClosed = true
    teardownSocket()
  }

  return {
    connect,
    publish,
    disconnect,
    destroy,
    isConnected: () => connected
  }
}

module.exports = { createMqttClient }
