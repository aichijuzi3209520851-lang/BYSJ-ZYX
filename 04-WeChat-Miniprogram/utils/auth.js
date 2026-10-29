/**
 * utils/auth.js
 * 登录态管理(教学/毕设简化实现: 仅本地存储标记, 不接后端鉴权)
 *
 * 设计说明:
 *   1. 账号密码的比对发生在 pages/login/login.js 内部, 本模块只负责状态的读写
 *   2. tabBar 三个页面无法被登录页拦截跳转, 因此各自在 onShow 首行调用 guardLogin()
 *   3. 存储值用对象承载时间戳, 便于日后追溯登录时刻(读取时按真值判断)
 *   4. 存储键用 login_state, 与 mqtt_host/mqtt_port/mqtt_topic/mqtt_history 命名空间区分
 */

const LOGIN_KEY = 'login_state'

// 读取登录态: 未登录或读失败都视为未登录
function isLoggedIn() {
  try {
    return !!wx.getStorageSync(LOGIN_KEY)
  } catch (e) {
    return false
  }
}

// 写入登录态(登录成功后调用)
function saveLogin() {
  try {
    wx.setStorageSync(LOGIN_KEY, { at: Date.now() })
  } catch (e) {
    console.error('登录态写入失败', e)
  }
}

// 清除登录态(退出登录时调用)
function clearLogin() {
  try {
    wx.removeStorageSync(LOGIN_KEY)
  } catch (e) {
    console.error('登录态清除失败', e)
  }
}

// tabBar 页面登录守卫: 未登录则回登录页并返回 false, 已登录返回 true
function guardLogin() {
  if (isLoggedIn()) return true
  wx.reLaunch({ url: '/pages/login/login' })
  return false
}

module.exports = {
  LOGIN_KEY,
  isLoggedIn,
  saveLogin,
  clearLogin,
  guardLogin
}
