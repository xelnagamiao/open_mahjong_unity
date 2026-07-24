"use strict";

const DEFAULT_BASE_URL = "https://tziakcha.net";

function getFetch(options = {}) {
  const fetchImpl = options.fetch || global.fetch;
  if (typeof fetchImpl !== "function") {
    throw new Error("当前环境没有 fetch，请通过 options.fetch 注入");
  }

  return fetchImpl;
}

function buildUrl(path, options = {}) {
  return new URL(path, options.baseUrl || DEFAULT_BASE_URL).toString();
}

function mergeHeaders(options = {}, headers = {}) {
  return {
    ...(options.headers || {}),
    ...headers
  };
}

function assertOk(response, endpoint) {
  if (!response.ok) {
    throw new Error(`HTTP ${response.status} for ${endpoint}`);
  }
}

module.exports = {
  DEFAULT_BASE_URL,
  assertOk,
  buildUrl,
  getFetch,
  mergeHeaders
};
