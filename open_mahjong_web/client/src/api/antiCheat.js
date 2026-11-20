import adminApi from "./adminClient";

const ROOT = "/anti-cheat";
const data = (response) => response.data.data;
export const antiCheatApi = {
  startRobertReview: (request) =>
    adminApi.post(`${ROOT}/robert-reviews`, request).then(data),
  robertReview: (id, config = {}) =>
    adminApi
      .get(`${ROOT}/robert-reviews/${encodeURIComponent(id)}`, config)
      .then(data),
  cancelRobertReview: (id) =>
    adminApi
      .post(`${ROOT}/robert-reviews/${encodeURIComponent(id)}/cancel`)
      .then(data),
  overview: (params, config = {}) =>
    adminApi.get(`${ROOT}/overview`, { ...config, params }).then(data),
  settings: () => adminApi.get(`${ROOT}/settings`).then(data),
  saveSettings: (settings) =>
    adminApi.put(`${ROOT}/settings`, settings).then(data),
  pairs: (params = {}) => adminApi.get(`${ROOT}/pairs`, { params }).then(data),
  players: (params = {}, config = {}) =>
    adminApi.get(`${ROOT}/players`, { ...config, params }).then(data),
  player: (id, params = {}, config = {}) =>
    adminApi
      .get(`${ROOT}/players/${encodeURIComponent(id)}`, { ...config, params })
      .then(data),
  pair: (key, params = {}) =>
    adminApi
      .get(`${ROOT}/pairs/${encodeURIComponent(key)}`, { params })
      .then(data),
  updates: (params = {}) =>
    adminApi.get(`${ROOT}/updates`, { params }).then(data),
  update: () => adminApi.post(`${ROOT}/updates`).then(data),
  cancelUpdate: (id) =>
    adminApi
      .post(`${ROOT}/updates/${encodeURIComponent(id)}/cancel`)
      .then(data),
  review: (key, request) =>
    adminApi
      .patch(`${ROOT}/pairs/${encodeURIComponent(key)}/review`, request)
      .then(data),
};
