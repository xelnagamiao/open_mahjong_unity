"use strict";

const {
  assertOk,
  buildUrl,
  getFetch,
  mergeHeaders
} = require("../core/network");

function asNumber(value) {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }

  if (typeof value === "string") {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  return null;
}

function extractPlayers(raw) {
  if (!Array.isArray(raw.players)) {
    return [];
  }

  return raw.players.map(player => ({
    name: player.n || player.name || "",
    id: player.i || player.id
  }));
}

function extractRecords(raw) {
  if (!Array.isArray(raw.records)) {
    return [];
  }

  return raw.records
    .map((record, index) => {
      const id = record.i || record.id;
      return id ? { id, index } : null;
    })
    .filter(Boolean);
}

function getIsFinished(raw, records, periods) {
  if (periods !== null && periods > 0) {
    return records.length === periods;
  }

  if (raw.finished === true || raw.isFinished === true) {
    return true;
  }

  const finishTime = asNumber(raw.finish_time || raw.finishTime);
  if (finishTime !== null && finishTime > 0) {
    return true;
  }

  const progress = asNumber(raw.progress);
  if (progress !== null && periods !== null && periods > 0) {
    return progress >= periods - 1;
  }

  return false;
}

async function fetchTziakchaSession(sessionId, options = {}) {
  const endpoint = "/_qry/game/";
  const response = await getFetch(options)(
    buildUrl(`${endpoint}?id=${encodeURIComponent(sessionId)}`, options),
    {
      method: "POST",
      credentials: "include",
      headers: mergeHeaders(options)
    }
  );
  assertOk(response, endpoint);

  const raw = await response.json();
  const records = extractRecords(raw);
  const periods = asNumber(raw.periods);

  return {
    sessionId,
    players: extractPlayers(raw),
    records,
    periods,
    isFinished: getIsFinished(raw, records, periods),
    raw
  };
}

module.exports = {
  fetchTziakchaSession
};
