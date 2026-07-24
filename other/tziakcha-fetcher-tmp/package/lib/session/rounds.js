"use strict";

const { fetchTziakchaRecordStep } = require("../record/fetch");
const { fetchTziakchaSession } = require("./fetch");
const { parseTziakchaSessionId } = require("../url");

async function fetchTziakchaSessionRounds(inputUrlOrId, options = {}) {
  const sessionId = parseTziakchaSessionId(inputUrlOrId);
  if (!sessionId) {
    throw new Error("无法从输入中解析 tziakcha 对局 id");
  }

  const session = await fetchTziakchaSession(sessionId, options);
  const records = await Promise.all(
    session.records.map(async record => ({
      ...record,
      step: await fetchTziakchaRecordStep(record.id, options)
    }))
  );

  return {
    ...session,
    records
  };
}

module.exports = {
  fetchTziakchaSessionRounds
};
