/**
 * utils/mqtt.js
 * 最小 MQTT 3.1.1 over WebSocket 客户端(基于 wx.connectSocket 的手写协议实现)
 * 由传感器页(订阅 green/data)与执行器页(订阅 green/data + 发布 green/control)共用
 *
 * 协议子集: CONNECT/CONNACK, SUBSCRIBE/SUBACK, PUBLISH(QoS0收发/QoS1发布), PUBACK,
 *           PINGREQ/PINGRESP, DISCONNECT
 * 可靠性设计(资深物联网实践):
 *   1. CONNECT 携带 keep-alive, 并以 keep-alive/2 周期发送 PINGREQ 心跳
 *   2. 异常断开只上报状态, 不自动重连; 由用户在页面上手动重连
 *   3. CONNACK/SUBACK 返回码逐一校验, 失败通过 onState 上报
 *   4. 手动断开前发送 DISCONNECT 报文优雅退出, 避免 EMQX 侧会话滞留
 *
 * 用法:
 *   const client = createMqttClient({
 *     host: '192.168.x.x', port: '8083',
 *     topics: ['green/data'],            // CONNACK 后自动订阅的主题列表
 *     clientIdPrefix: 'wx_sensor_',     // 客户端ID前缀(随机后缀防冲突)
 *     onState(state, info) {},          // connecting|connected|failed|closed
 *     onSubscribed(topic, qos) {},      // 每个 SUBACK 成功后触发一次
 *     onPayload(text, topic) {},        // PUBLISH 载荷(UTF-8 文本)
 *     onLog(level, msg) {}              // 调试日志透传
 *   })
 *   client.connect()
 *   client.publish('green/control', 'DUOJ-1')   // QoS0 发布, 返回是否已发出
 *   client.publish('green/control', 'DUOJ-1', 1) // QoS1 发布(等PUBACK, 控制指令建议用)
 *   client.disconnect()                          // 用户主动断开(不重连)
 *   client.destroy()                             // 页体卸载时释放资源
 */

const KEEP_ALIVE = 60            // MQTT keep-alive(秒)
const PING_INTERVAL_MS = 30000   // PINGREQ 周期 = keep-alive/2

/* ---------- 主题约定 ---------- */

// 上行数据主题: 上位机/调试工具发布 JSON 传感器数据, 小程序订阅
const DEFAULT_DATA_TOPIC = 'green/data'

// 下行指令主题: 小程序发布纯文本指令('mode-1'/'DUOJ-1'/'MOTOR-1' 等), 上位机订阅
// 这是纯文本控制通道, 不承载数据。小程序绝不能订阅它 —— 否则自己发出的每条指令
// 都会被 EMQX 原样回送回来, 再被当成 JSON 解析, 刷出成片的
// "Unexpected token 'm', \"mode-1\" is not valid JSON"。
const CONTROL_TOPIC = 'green/control'

// 判断某个主题是不是指令通道(它承载纯文本指令, 不能当数据主题订阅)
function isControlTopic(topic) {
  return String(topic || '').trim() === CONTROL_TOPIC
}

/* ---------- 服务器地址校验 ---------- */

/**
 * 判断一个地址是否"只在本机可用"——也就是只能被开发者工具的模拟器连上,
 * 手机真机一定连不上。返回空串表示没发现问题, 返回非空串是需要提示用户的理由。
 *
 * 为什么需要这个: 模拟器跑在电脑上, 所以填任何本机网卡地址都能连(包括
 * 127.0.0.1 和各种虚拟网卡)。于是很容易误以为"地址是对的", 到了手机上才发现
 * 连不上。实测踩过的坑: 填 192.168.40.1(VMware VMnet8), 模拟器秒连,
 * 手机连不上 —— 因为手机所在的热点里根本不存在这个网段。
 *
 * 判定依据是各虚拟化/隧道软件的默认网段(它们装完机几乎都落在这些段里):
 *   - VMware  VMnet1 = 192.168.x.1 / VMnet8 = 192.168.x.1
 *   - Hyper-V = 172.16~31.0.0/20 (Default Switch 常见 172.20.144.1)
 *   - Tailscale = 100.64.0.0/10 (CGNAT 段)
 *   - WSL2 = 172.x.x.1
 * 只做提示不做拦截: 用户完全可能真的把 EMQX 装在某个虚拟网段上。
 */
function checkHostReachability(host) {
  const h = String(host || '').trim()
  if (!h) return '请填写电脑的局域网 IP 地址(手机需要能访问到它)'

  if (h === '127.0.0.1' || h === 'localhost') {
    return '127.0.0.1 / localhost 只在本机有效: 模拟器能连, 手机会连到手机自己。请填电脑的局域网 IP'
  }

  const m = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/.exec(h)
  if (!m) {
    // 不是 IPv4 字面量, 交给微信的域名校验, 这里不拦
    return ''
  }
  const a = Number(m[1])
  const b = Number(m[2])

  // Tailscale / CGNAT 段
  if (a === 100 && b >= 64 && b <= 127) {
    return `${h} 看起来是 Tailscale 等组网工具的分段, 手机没装对应客户端时连不上`
  }
  // Hyper-V / WSL2 段
  if (a === 172 && b >= 16 && b <= 31) {
    return `${h} 属于 172.16~31 网段, 通常是 Hyper-V / WSL 的虚拟网卡; 手机不在这个网段里`
  }
  return ''
}

// 以 IPv4 形式给出"疑似虚拟网卡"的补充提示(仅用于界面说明, 不参与拦截)
function describeVirtualHint(host) {
  const h = String(host || '').trim()
  const m = /^192\.168\.(\d{1,3})\.(\d{1,3})$/.exec(h)
  if (!m) return ''
  // VMware 默认把 VMnet1/VMnet8 放在 192.168.x.1, 宿主机侧地址末位恒为 1。
  // 这不能当成结论, 只是提示用户核对一下: 真正的电脑 IP 末位很少正好是 1。
  if (m[2] === '1') {
    return `${h} 末位是 .1, 常见于 VMware / Hyper-V 虚拟网卡(宿主机侧地址), 请确认它是你电脑 Wi-Fi 网卡的地址`
  }
  return ''
}

/**
 * 统一的地址风险判定。返回 { level, message }:
 *   level = 'block'  确定连不上, 应该拦下来
 *   level = 'warn'   高度可疑, 让用户确认后再放行
 *   level = ''       没发现问题
 *
 * 为什么分两级: 「172.x / 100.64-127 / 127.0.0.1」这些网段手机一定到不了, 属于硬错;
 * 而「192.168.x.1」只是常见于虚拟网卡, 用户完全可能给电脑配了静态 .1 地址,
 * 硬拦会误伤 —— 必须让用户自己确认。
 *
 * 两级都要在保存流程里真的用上。历史上只把 'block' 接进保存流程, 结果本机真正
 * 出问题的那两个地址(192.168.40.1 VMnet8 / 192.168.198.1 VMnet1)恰好都落在
 * 'warn' 档, 于是"保存时什么都没提示", 等于没修。这是被单元测试抓出来的。
 */
function describeHostRisk(host) {
  const blocked = checkHostReachability(host)
  if (blocked) return { level: 'block', message: blocked }
  const warned = describeVirtualHint(host)
  if (warned) return { level: 'warn', message: warned }
  return { level: '', message: '' }
}

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
  let packetIdSeq = 0
  let connected = false      // 是否已收到 CONNACK
  const pendingQos1 = {}     // QoS1发布待确认表: packetId → {topic,message,ts}, 收PUBACK后清除

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

  // PUBLISH: QoS0不保留 / QoS1需报文标识符并等PUBACK
  // 注: 不实现DUP超时重发, 依赖WebSocket可靠传输+EMQX确认; 连接断开时pending自然作废
  function buildPublish(topic, message, qos) {
    const topicBytes = utf8Bytes(topic)
    const payloadBytes = utf8Bytes(String(message))
    const body = [topicBytes.length >> 8, topicBytes.length & 0xFF, ...topicBytes]
    let packetId = null
    if (qos === 1) {
      packetId = nextPacketId()
      body.push(packetId >> 8, packetId & 0xFF)   // QoS1必须携带报文标识符
    }
    body.push(...payloadBytes)
    const headerByte = (qos === 1) ? 0x32 : 0x30  // 0x30=QoS0 PUBLISH, 0x32=QoS1
    return { bytes: [headerByte, ...encodeRemainingLength(body.length), ...body], packetId }
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
    setState('connecting', { url })

    try {
      // 捕获本次 SocketTask, 旧连接的迟到事件(重连/页面切换)一律忽略
      let task = null
      task = wx.connectSocket({
        url,
        protocols: ['mqtt'],  // EMQX WebSocket 子协议
        fail: (err) => {
          if (socketTask !== task) return
          log('error', 'WebSocket连接请求失败: ' + (err.errMsg || ''))
          setState('failed', { reason: '无法发起连接: ' + (err.errMsg || '未知错误'), url })
        }
      })
      if (!task) {
        log('error', 'WebSocket连接请求失败: 未返回 SocketTask')
        setState('failed', { reason: '无法创建连接', url })
        return
      }
      socketTask = task

      task.onOpen(() => {
        if (socketTask !== task) return
        log('info', 'WebSocket已建立, 发送MQTT CONNECT')
        sendBytes(buildConnect(clientId))
      })

      task.onMessage((res) => {
        if (socketTask !== task) return
        handlePacket(res.data)
      })

      task.onError((err) => {
        if (socketTask !== task) return
        log('error', 'WebSocket错误: ' + (err.errMsg || ''))
        handleAbnormalClose('连接出现错误')
      })

      task.onClose((res) => {
        if (socketTask !== task) return
        log('info', 'WebSocket关闭 code=' + res.code)
        handleAbnormalClose(res.code === 1000 ? '连接已断开' : '连接异常断开')
      })
    } catch (error) {
      log('error', '创建WebSocket异常: ' + error)
      setState('failed', { reason: '创建连接异常', url })
    }
  }

  // 统一处理异常断开: 停心跳并上报, 不自动重连
  function handleAbnormalClose(reason) {
    stopPingTimer()
    connected = false
    setState('closed', { reason })
  }

  function teardownSocket() {
    stopPingTimer()
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
              // 指令主题是纯文本控制通道, 订阅它只会把自己发出的指令原样收回来,
              // 再当 JSON 解析就刷 "Unexpected token" 报错 —— 直接跳过并给出提示。
              if (isControlTopic(topic)) {
                log('warn', `主题 ${topic} 是指令通道(纯文本控制), 不能作为数据主题订阅, 已跳过`)
                return
              }
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
      } else if (type === 4) {
        // PUBACK: QoS1发布确认(执行器页控制指令), 清除待确认记录
        const packetId = (bytes[2] << 8) + bytes[3]
        delete pendingQos1[packetId]
        log('info', 'PUBACK packetId=' + packetId)
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

  // QoS0/QoS1发布; 未连接时返回 false(调用方自行提示)
  function publish(topic, message, qos) {
    if (!connected || !socketTask) {
      log('warn', '未连接, 丢弃发布: ' + topic)
      return false
    }
    const level = (qos === 1) ? 1 : 0
    const built = buildPublish(topic, message, level)
    if (level === 1 && built.packetId != null) {
      pendingQos1[built.packetId] = { topic, message, ts: Date.now() }  // 登记待PUBACK确认
    }
    return sendBytes(built.bytes)
  }

  // 用户主动断开: 发送 DISCONNECT 后关闭
  function disconnect() {
    if (socketTask && connected) {
      try { socketTask.send({ data: new Uint8Array([0xE0, 0x00]).buffer }) } catch (e) { /* 忽略 */ }
    }
    teardownSocket()
    setState('closed', { reason: '主动断开' })
  }

  // 页面卸载时释放资源(不再上报状态)
  function destroy() {
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

module.exports = {
  createMqttClient,
  DEFAULT_DATA_TOPIC,
  CONTROL_TOPIC,
  isControlTopic,
  checkHostReachability,
  describeVirtualHint,
  describeHostRisk
}
