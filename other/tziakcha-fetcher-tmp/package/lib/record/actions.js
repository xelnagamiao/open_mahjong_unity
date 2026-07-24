"use strict";

const { ACTION_TYPES, ACTION_TYPE_NAMES } = require("../core/config/actions");

function decodeDetail(type, data) {
  switch (type) {
    case ACTION_TYPES.FLOWER_REPLACE:
      return {
        drawnTileId: data & 0xff,
        flowerTileId: ((data >> 8) & 0x0f) + 136,
        auto: Boolean(data & 0x1000)
      };
    case ACTION_TYPES.DISCARD:
      return {
        tileId: data & 0xff,
        handPlayed: Boolean((data >> 8) & 1),
        playMode: (data >> 9) & 3
      };
    case ACTION_TYPES.CHI: {
      const tileBase = (data & 0x3f) << 2;
      const offsets = [(data >> 10) & 3, (data >> 12) & 3, (data >> 14) & 3];

      return {
        baseTileId: tileBase,
        tileBase,
        offerDirection: (data >> 6) & 3,
        offsets,
        candidateTileIds: [
          tileBase - 4 + offsets[0],
          tileBase + offsets[1],
          tileBase + 4 + offsets[2]
        ]
      };
    }

    case ACTION_TYPES.PENG: {
      const tileBase = (data & 0x3f) << 2;
      const offset = (data >> 10) & 3;

      return {
        baseTileId: tileBase,
        tileBase,
        offerDirection: (data >> 6) & 3,
        offset,
        actualTileId: tileBase + offset
      };
    }

    case ACTION_TYPES.GANG: {
      const tileBase = (data & 0x3f) << 2;
      const offerDirection = (data >> 6) & 3;
      const offset = (data >> 10) & 3;

      return {
        baseTileId: tileBase,
        tileBase,
        offerDirection,
        offset,
        actualTileId: tileBase + offset,
        promoted: (data & 0x0300) === 0x0300,
        concealed: offerDirection === 0
      };
    }

    case ACTION_TYPES.WIN:
      return {
        auto: Boolean(data & 1),
        fan: data >> 1
      };
    case ACTION_TYPES.DRAW:
      return {
        tileId: data & 0xff,
        backward: Boolean(data & 0x0100)
      };
    case ACTION_TYPES.PASS:
      return {
        mode: data & 3
      };
    default:
      return {};
  }
}

function decodeTziakchaAction(action) {
  if (!Array.isArray(action) || action.length < 3) {
    throw new Error("tziakcha action 必须是 [combined, data, time] 数组");
  }

  const [combined, data, time] = action;
  if (
    typeof combined !== "number" ||
    typeof data !== "number" ||
    typeof time !== "number"
  ) {
    throw new Error("tziakcha action combined/data/time 必须是数字");
  }

  const type = combined & 0x0f;

  return {
    playerIndex: (combined >> 4) & 3,
    type,
    typeName: ACTION_TYPE_NAMES[type] || `unknown(${type})`,
    data,
    time,
    detail: decodeDetail(type, data)
  };
}

module.exports = {
  ACTION_TYPES,
  decodeTziakchaAction
};
