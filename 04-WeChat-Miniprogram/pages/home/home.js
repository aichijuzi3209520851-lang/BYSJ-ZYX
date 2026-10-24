// pages/home/home.js
Page({
  data: {
    // 首页Hero轮播图(图片均来自Pexels, 免费商用无需署名)
    slides: [
      { id: 'greenhouse', src: '/images/hero-greenhouse.jpg' },   // 拱形大棚纵览
      { id: 'tomato',     src: '/images/hero-tomato.jpg' },       // 温室番茄行栽
      { id: 'strawberry', src: '/images/hero-strawberry.jpg' },   // 草莓大棚种植
      { id: 'smartfarm',  src: '/images/hero-smartfarm.jpg' }     // LED垂直农场(智慧农业)
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

  // 跳转到传感器页面
  goToSensor() {
    wx.switchTab({
      url: '/pages/sensor/sensor'
    })
  }
})
