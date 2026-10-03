import playerApi from './playerClient'
import { isDuplicateWallRule } from '../utils/duplicateWalls.js'

async function request(method, url, options = {}) {
  const response = await playerApi.request({ baseURL: '/api/duplicate-walls', method, url, ...options })
  if (response.data?.success === false) throw new Error(response.data.message || '复式牌墙操作失败')
  return response.data?.data
}

export const duplicateWallsApi = {
  catalog: () => request('get', '/catalog'),
  mine: (params) => request('get', '/mine', { params }),
  create: (data) => {
    if (!isDuplicateWallRule(data?.rule)) return Promise.reject(new Error('复式牌墙仅支持国标麻将'))
    return request('post', '/', { data })
  },
  unlock: (id) => request('patch', `/${encodeURIComponent(id)}`, { data: { is_unlocked: true } }),
  remove: (id) => request('delete', `/${encodeURIComponent(id)}`),
  publicList: (key) => request('get', '/public', { params: { key } }),
  publicDetail: (key) => request('get', `/public/${encodeURIComponent(key)}`),
}
