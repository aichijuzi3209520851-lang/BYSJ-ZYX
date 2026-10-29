// pages/login/login.js
// 登入页: 教学/毕设简化鉴权 —— 账号密码在本页内部硬编码比对, 不接后端
// 登录成功后把标记写入本地存储(utils/auth.js), 再 switchTab 进首页;
// 已登录用户再次打开小程序时, onLoad 直接跳首页, 不再看到本页

const { isLoggedIn, saveLogin } = require('../../utils/auth')

/* 本地比对常量: 本项目是教学/毕设的简化鉴权, 不做后端校验, 也不在界面上暴露 */
const ACCOUNT = 'zyx'
const PASSWORD = 'zyx'

Page({
  data: {
    account: '',
    password: '',
    ready: false,       // 登录态判定完成后才显示表单, 避免已登录用户瞥见表单
    submitting: false
  },

  onLoad() {
    // 已有登录态: 直接回 tabBar 首页, 登录卡不渲染
    if (isLoggedIn()) {
      wx.switchTab({
        url: '/pages/home/home',
        // 极端情况下 switchTab 可能失败(app 冷启动时序), 兜底显示表单, 避免停在空白页
        fail: () => {
          this.setData({ ready: true })
        }
      })
      return
    }
    this.setData({ ready: true })
  },

  // 账号输入
  onAccountInput(e) {
    this.setData({ account: e.detail.value })
  },

  // 密码输入
  onPasswordInput(e) {
    this.setData({ password: e.detail.value })
  },

  // 点击登录 / 键盘确认
  onLogin() {
    if (this.data.submitting) return

    const account = (this.data.account || '').trim()
    const password = (this.data.password || '').trim()

    if (!account) {
      wx.showToast({ title: '请输入账号', icon: 'none' })
      return
    }
    if (!password) {
      wx.showToast({ title: '请输入密码', icon: 'none' })
      return
    }

    // 本地比对(明文比较, 不做哈希: 本项目不含敏感数据)
    if (account !== ACCOUNT || password !== PASSWORD) {
      wx.showToast({ title: '账号或密码不正确', icon: 'none' })
      return
    }

    this.setData({ submitting: true })
    saveLogin()

    // 清空输入, 避免返回本页时残留
    this.setData({ account: '', password: '' })

    wx.switchTab({
      url: '/pages/home/home',
      complete: () => {
        this.setData({ submitting: false })
      }
    })
  }
})
