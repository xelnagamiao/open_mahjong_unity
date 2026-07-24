"use strict";

function decodeWallHex(wallHex) {
  if (typeof wallHex !== "string" || wallHex.length % 2 !== 0) {
    throw new Error("wall hex 必须是偶数长度字符串");
  }

  const result = [];
  for (let index = 0; index < wallHex.length; index += 2) {
    const hexPair = wallHex.slice(index, index + 2);
    if (!/^[\da-fA-F]{2}$/.test(hexPair)) {
      throw new Error("wall hex 包含非法十六进制字符");
    }

    const parsed = Number.parseInt(hexPair, 16);

    result.push(parsed);
  }

  return result;
}

function tileIdToBase(tileId) {
  return tileId >> 2;
}

function tileIdToGbTile(tileId) {
  if (tileId < 108) {
    return String((tileIdToBase(tileId) % 9) + 1);
  }

  if (tileId < 136) {
    return ["E", "S", "W", "N", "C", "F", "P"][(tileId - 108) >> 2];
  }

  return ["a", "b", "c", "d", "e", "f", "g", "h"][tileId - 136];
}

function groupHandToGbString(tileIds) {
  const groups = {
    m: [],
    p: [],
    s: [],
    z: []
  };

  for (const tileId of [...tileIds].sort((left, right) => left - right)) {
    if (tileId < 36) {
      groups.m.push(tileIdToGbTile(tileId));
    } else if (tileId < 72) {
      groups.s.push(tileIdToGbTile(tileId));
    } else if (tileId < 108) {
      groups.p.push(tileIdToGbTile(tileId));
    } else if (tileId < 136) {
      groups.z.push(tileIdToGbTile(tileId));
    }
  }

  return [
    groups.m.length ? `${groups.m.join("")}m` : "",
    groups.p.length ? `${groups.p.join("")}p` : "",
    groups.s.length ? `${groups.s.join("")}s` : "",
    groups.z.join("")
  ].join("");
}

module.exports = {
  decodeWallHex,
  groupHandToGbString,
  tileIdToBase,
  tileIdToGbTile
};
