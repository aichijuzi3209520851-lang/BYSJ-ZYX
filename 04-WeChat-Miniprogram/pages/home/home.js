// pages/home/home.js
Page({
  data: {
    // 封面轮播: 三张明亮的温室实拍(晨露苗株 / 天窗通风 / 金色时刻)
    slides: [
      { id: 'dew',    src: '/images/hero-dew.jpg' },
      { id: 'vent',   src: '/images/hero-vent.jpg' },
      { id: 'golden', src: '/images/hero-golden.jpg' }
    ],
    currentSlide: 0
  },

  onLoad(options) {
    console.log('首页加载')
  },

  // 轮播切换: 同步自定义指示点
  onSlideChange(e) {
    this.setData({ currentSlide: e.detail.current })
  },

  // 进入传感器页
  goToSensor() {
    wx.switchTab({
      url: '/pages/sensor/sensor'
    })
  },

  // 进入执行器页
  goToActuator() {
    wx.switchTab({
      url: '/pages/actuator/actuator'
    })
  }
})
