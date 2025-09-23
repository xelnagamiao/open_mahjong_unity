import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import axios from 'axios'

export const useAuthStore = defineStore('auth', () => {
  const user = ref(null)
  const token = ref(localStorage.getItem('token') || null)

  const isLoggedIn = computed(() => !!token.value)

  // 设置认证头
  if (token.value) {
    axios.defaults.headers.common['Authorization'] = `Bearer ${token.value}`
  }

  // 登录
  const login = async (username, password) => {
    try {
      const response = await axios.post('/api/auth/login', {
        username,
        password
      })

      if (response.data.success) {
        const { token: newToken, ...userData } = response.data.data
        token.value = newToken
        user.value = userData
        localStorage.setItem('token', newToken)
        axios.defaults.headers.common['Authorization'] = `Bearer ${newToken}`
        return { success: true }
      }
    } catch (error) {
      return {
        success: false,
        message: error.response?.data?.message || '登录失败'
      }
    }
  }
  
  // 注册
  const register = async (username, password) => {
    try {
      const response = await axios.post('/api/auth/register', {
        username,
        password
      })

      if (response.data.success) {
        const { token: newToken, ...userData } = response.data.data
        token.value = newToken
        user.value = userData
        localStorage.setItem('token', newToken)
        axios.defaults.headers.common['Authorization'] = `Bearer ${newToken}`
        return { success: true }
      }
    } catch (error) {
      return {
        success: false,
        message: error.response?.data?.message || '注册失败'
      }
    }
  }

  // 登出
  const logout = () => {
    token.value = null
    user.value = null
    localStorage.removeItem('token')
    delete axios.defaults.headers.common['Authorization']
  }

  // 获取用户信息
  const fetchProfile = async () => {
    try {
      const response = await axios.get('/api/auth/profile')
      if (response.data.success) {
        user.value = response.data.data
      }
    } catch (error) {
      console.error('获取用户信息失败:', error)
      logout()
    }
  }

  // 返回
  return {
    user,
    token,
    isLoggedIn,
    login,
    register,
    logout,
    fetchProfile
  }
}) 