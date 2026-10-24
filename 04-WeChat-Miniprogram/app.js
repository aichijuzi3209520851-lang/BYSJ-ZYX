// app.js
App({
  onLaunch() {
    console.log('小程序启动')
  },
  globalData: {
    mqttConfig: {
      host: '192.168.36.221',  // MQTT服务器IP(与PC上位机使用同一台EMQX服务器)
      port: 8083,              // EMQX WebSocket端口
      topic: ''                // 订阅主题: 默认留空, 必须由用户手动输入(与上位机发布主题一致, 默认 green/data)
    }
  }
})
