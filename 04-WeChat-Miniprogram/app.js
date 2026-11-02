// app.js
App({
  onLaunch() {
    console.log('小程序启动')
  },
  /*
   * 这里曾放 globalData.mqttConfig.host = '192.168.36.221', 但全项目无人读取
   * (两个页面各自从 wx.getStorageSync('mqtt_host') 取值), 属于死配置。
   * 留着反而有害: 别人照抄这个写死的 IP, 而它在本机网段里根本不存在,
   * 于是又会出现「模拟器能连、真机连不上」。
   *
   * 服务器地址统一由用户在「连接设置」里填写, 存于 mqtt_host。
   * 必须填**电脑 Wi-Fi 网卡**的 IPv4 地址 —— VMware / Hyper-V / Tailscale
   * 那些虚拟网卡地址只有本机模拟器能连, 手机连不上; 端口 8083。
   */
  globalData: {}
})
