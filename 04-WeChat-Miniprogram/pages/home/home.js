// pages/home/home.js
// 首页: 封面轮播 + 作者名片 + 双入口(tabBar 页面无法被登录页拦截, 故 onShow 自检登录态)
const { guardLogin, clearLogin } = require('../../utils/auth')

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

  // 登录守卫: tabBar 页面可被直接切换进来, 每次显示都要自检
  onShow() {
    guardLogin()
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
  },

  // 退出登录: 二次确认后清登录态并回登录页
  onLogout() {
    wx.showModal({
      title: '退出登录',
      content: '退出后需要重新登入才能查看数据',
      confirmText: '退出',
      confirmColor: '#B9473D',
      success: (res) => {
        if (!res.confirm) return
        clearLogin()
        wx.reLaunch({ url: '/pages/login/login' })
      }
    })
  }
})
