import axios from 'axios'
import { ElMessage } from 'element-plus'

export const api = axios.create({
  baseURL: '/api/panel',
  timeout: 30_000,
  withCredentials: true,
})

api.interceptors.response.use(
  (res) => res,
  (err) => {
    if (err?.response?.status === 401 && window.location.pathname !== '/ui/login') {
      window.location.href = `/ui/login?redirect=${encodeURIComponent(window.location.pathname + window.location.search)}`
      return Promise.reject(err)
    }

    const message = err?.response?.data?.message || err?.response?.data?.title || err?.message
    if (message && err?.response?.data?.code !== 'CHAT_CREATE_COOLDOWN') ElMessage.error(message)
    return Promise.reject(err)
  },
)
