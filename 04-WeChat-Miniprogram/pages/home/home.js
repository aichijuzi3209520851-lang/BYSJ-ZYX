// pages/home/home.js
Page({
  data: {

  },

  onLoad(options) {
    console.log('首页加载')
  },

  // 跳转到传感器页面
  goToSensor() {
    wx.switchTab({
      url: '/pages/sensor/sensor'
    })
  }
})
