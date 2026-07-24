"use strict";

const {
  assertOk,
  buildUrl,
  getFetch,
  mergeHeaders
} = require("../core/network");

async function decodeRecordStep(recordId, raw, decompressZlibBase64, options) {
  const normalizedOptions = options || {};

  if (raw.script === "<Decoded>" && raw.step && typeof raw.step === "object") {
    return raw.step;
  }

  if (typeof raw.script !== "string" || !raw.script) {
    throw new Error(`record ${recordId} 缺少 script`);
  }

  const decode = normalizedOptions.decompressZlibBase64 || decompressZlibBase64;

  try {
    return JSON.parse(await decode(raw.script));
  } catch (error) {
    throw new Error(`record ${recordId} script 解码失败: ${error.message}`);
  }
}

function createRecordFetchApi(decompressZlibBase64) {
  async function fetchTziakchaRecord(recordId, options = {}) {
    const endpoint = "/_qry/record/";
    const response = await getFetch(options)(buildUrl(endpoint, options), {
      method: "POST",
      headers: mergeHeaders(options, {
        "content-type": "application/x-www-form-urlencoded; charset=UTF-8"
      }),
      body: new URLSearchParams({ id: recordId }).toString()
    });
    assertOk(response, endpoint);

    const raw = await response.json();
    const step = await decodeRecordStep(
      recordId,
      raw,
      decompressZlibBase64,
      options
    );

    return {
      id: raw.id || recordId,
      belongs: raw.belongs,
      script: "<Decoded>",
      step,
      raw
    };
  }

  async function fetchTziakchaRecordStep(recordId, options = {}) {
    const record = await fetchTziakchaRecord(recordId, options);
    return record.step;
  }

  return {
    decompressZlibBase64,
    decodeRecordStep,
    fetchTziakchaRecord,
    fetchTziakchaRecordStep
  };
}

module.exports = {
  createRecordFetchApi,
  decodeRecordStep
};
