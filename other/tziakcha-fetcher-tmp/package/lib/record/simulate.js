"use strict";

const { WINDS } = require("../core/config/game");
const { decodeWallHex, tileIdToBase } = require("../core/tiles");
const { ACTION_TYPES, decodeTziakchaAction } = require("./actions");
const {
  cloneGameState,
  createInitialGameState,
  setupWallAndDeal
} = require("./state");

function getStep(record) {
  if (!record || !record.step || typeof record.step !== "object") {
    throw new Error("record 缺少 step");
  }

  return record.step;
}

function validateStep(step) {
  if (typeof step.w !== "string") {
    throw new Error("step.w 必须是牌墙十六进制字符串");
  }

  if (typeof step.d !== "number") {
    throw new Error("step.d 必须是数字");
  }

  if (!Array.isArray(step.a)) {
    throw new Error("step.a 必须是动作数组");
  }
}

function getDiceArray(encodedDice) {
  return [
    encodedDice & 0x0f,
    (encodedDice >> 4) & 0x0f,
    (encodedDice >> 8) & 0x0f,
    (encodedDice >> 12) & 0x0f
  ];
}

function setLastActionFlags(state, wasKong, wasAddedKong) {
  state.lastActionWasKong = wasKong;
  state.lastActionWasAddedKong = wasAddedKong;
}

function removeLastDiscard(state, playerIndex) {
  if (playerIndex === null || playerIndex === undefined) {
    return;
  }

  const discards = state.players[playerIndex].discards;
  if (discards.length === 0) {
    return;
  }

  discards.pop();
}

function removeTileFromHand(player, tileId, playerIndex) {
  const tileIndex = player.handTiles.indexOf(tileId);
  if (tileIndex >= 0) {
    player.handTiles.splice(tileIndex, 1);
    return tileId;
  }

  const tileBase = tileIdToBase(tileId);
  const sameBaseIndex = player.handTiles.findIndex(
    handTileId => tileIdToBase(handTileId) === tileBase
  );
  if (sameBaseIndex < 0) {
    throw new Error(`玩家 ${playerIndex} 手牌中不存在牌 ${tileId}`);
  }

  return player.handTiles.splice(sameBaseIndex, 1)[0];
}

function removeTileByBaseFromHand(player, tileBase, playerIndex) {
  const expectedBase = tileIdToBase(tileBase);
  const tileIndex = player.handTiles.findIndex(
    tileId => tileIdToBase(tileId) === expectedBase
  );
  if (tileIndex < 0) {
    throw new Error(`玩家 ${playerIndex} 手牌中不存在牌型 ${tileBase}`);
  }

  return player.handTiles.splice(tileIndex, 1)[0];
}

function removeTilesByBaseFromHand(player, tileBase, count, playerIndex) {
  const removed = [];
  for (let index = 0; index < count; index += 1) {
    removed.push(removeTileByBaseFromHand(player, tileBase, playerIndex));
  }

  return removed;
}

function resolveOfferPlayerIndex(state, action) {
  if (action.type === ACTION_TYPES.CHI) {
    return (action.playerIndex + 3) % 4;
  }

  return (action.playerIndex - action.detail.offerDirection + 4) % 4;
}

function applyDiscard(state, action, player) {
  removeTileFromHand(player, action.detail.tileId, action.playerIndex);
  player.discards.push(action.detail.tileId);
  state.lastDiscardTile = action.detail.tileId;
  state.lastDiscardPlayerIndex = action.playerIndex;
  setLastActionFlags(state, false, false);
}

function applyDraw(state, action, player) {
  player.handTiles.push(action.detail.tileId);
  player.handTiles.sort((left, right) => left - right);
  player.lastDrawTile = action.detail.tileId;
  state.currentPlayerIndex = action.playerIndex;

  if (action.detail.backward) {
    state.wallBackIndex -= 1;
  } else {
    state.wallFrontIndex += 1;
  }

  setLastActionFlags(state, false, false);
}

function applyFlowerReplacement(state, action, player) {
  const flowerTileId = action.detail.flowerTileId;
  removeTileFromHand(player, flowerTileId, action.playerIndex);
  player.flowerCount += 1;
  player.flowerTiles.push(flowerTileId);
  player.handTiles.push(action.detail.drawnTileId);
  player.handTiles.sort((left, right) => left - right);
  player.lastDrawTile = action.detail.drawnTileId;
  state.currentPlayerIndex = action.playerIndex;
  state.wallBackIndex -= 1;
  setLastActionFlags(state, false, false);
}

function applyChi(state, action, player) {
  if (action.data === 0) {
    state.currentPlayerIndex = action.playerIndex;
    setLastActionFlags(state, false, false);
    return;
  }

  const offeredTileId = state.lastDiscardTile;
  const offerPlayerIndex = resolveOfferPlayerIndex(state, action);

  removeLastDiscard(state, offerPlayerIndex);

  let candidateTileIds = [...action.detail.candidateTileIds];
  if (candidateTileIds[0] < 0) {
    const tileBase = offeredTileId;
    candidateTileIds = [
      tileBase - 4 + action.detail.offsets[0],
      tileBase + action.detail.offsets[1],
      tileBase + 4 + action.detail.offsets[2]
    ];
  }

  const consumedTileIds = [];
  for (const tileId of candidateTileIds) {
    if (tileIdToBase(tileId) !== tileIdToBase(offeredTileId)) {
      consumedTileIds.push(
        removeTileByBaseFromHand(player, tileId, action.playerIndex)
      );
    }
  }

  const tileIds = candidateTileIds;
  let offerSequence = tileIds.findIndex(
    tileId => tileIdToBase(tileId) === tileIdToBase(offeredTileId)
  );
  if (offerSequence < 0) {
    offerSequence = 0;
  }

  player.melds.push({
    type: "chi",
    tileIds,
    consumedTileIds,
    offerDirection: action.detail.offerDirection,
    offerSequence,
    offeredTileId
  });
  state.currentPlayerIndex = action.playerIndex;
  setLastActionFlags(state, false, false);
}

function applyPeng(state, action, player) {
  if (action.data === 0) {
    state.currentPlayerIndex = action.playerIndex;
    setLastActionFlags(state, false, false);
    return;
  }

  const offeredTileId = state.lastDiscardTile;
  const offerPlayerIndex = resolveOfferPlayerIndex(state, action);

  removeLastDiscard(state, offerPlayerIndex);
  const tileIds = removeTilesByBaseFromHand(
    player,
    action.detail.tileBase,
    2,
    action.playerIndex
  );
  tileIds.push(offeredTileId);
  tileIds.sort((left, right) => left - right);

  player.melds.push({
    type: "peng",
    tileBase: action.detail.tileBase,
    tileIds,
    offerDirection: action.detail.offerDirection,
    offerSequence: action.detail.offerDirection + 1,
    offeredTileId
  });
  state.currentPlayerIndex = action.playerIndex;
  setLastActionFlags(state, false, false);
}

function applyAddedKong(state, action, player) {
  let removedTileId;
  try {
    removedTileId = removeTileFromHand(
      player,
      action.detail.actualTileId,
      action.playerIndex
    );
  } catch {
    removedTileId = removeTileByBaseFromHand(
      player,
      action.detail.tileBase,
      action.playerIndex
    );
  }

  const meld = player.melds.find(
    item =>
      item.type === "peng" &&
      tileIdToBase(item.tileBase) === tileIdToBase(action.detail.tileBase)
  );

  if (!meld) {
    throw new Error(
      `玩家 ${action.playerIndex} 没有可升级为加杠的碰 ${action.detail.tileBase}`
    );
  }

  meld.type = "gang";
  meld.upgradedFromPeng = true;
  meld.added = true;
  meld.concealed = false;
  meld.tileIds = [...(meld.tileIds || []), removedTileId].sort(
    (left, right) => left - right
  );
  state.lastDiscardTile = removedTileId;
  state.lastDiscardPlayerIndex = action.playerIndex;
  state.currentPlayerIndex = action.playerIndex;
  setLastActionFlags(state, true, true);
}

function applyGang(state, action, player) {
  state.currentPlayerIndex = action.playerIndex;

  if (action.data === 0) {
    setLastActionFlags(state, false, false);
    return;
  }

  if (action.detail.promoted) {
    applyAddedKong(state, action, player);
    return;
  }

  let offeredTileId = null;
  let offerSequence = 0;
  let tileIds;

  if (action.detail.concealed) {
    tileIds = removeTilesByBaseFromHand(
      player,
      action.detail.tileBase,
      4,
      action.playerIndex
    );
  } else {
    offeredTileId = state.lastDiscardTile;
    const offerPlayerIndex = resolveOfferPlayerIndex(state, action);
    removeLastDiscard(state, offerPlayerIndex);
    tileIds = removeTilesByBaseFromHand(
      player,
      action.detail.tileBase,
      3,
      action.playerIndex
    );
    tileIds.push(offeredTileId);
    offerSequence = action.detail.offerDirection + 1;
  }

  tileIds.sort((left, right) => left - right);
  player.melds.push({
    type: "gang",
    tileBase: action.detail.tileBase,
    tileIds,
    offerDirection: action.detail.offerDirection,
    offerSequence,
    offeredTileId,
    concealed: action.detail.concealed,
    upgradedFromPeng: false,
    added: false
  });
  setLastActionFlags(state, true, false);
}

function applyAction(state, action) {
  const player = state.players[action.playerIndex];

  switch (action.type) {
    case ACTION_TYPES.DISCARD:
      applyDiscard(state, action, player);
      break;
    case ACTION_TYPES.DRAW:
      applyDraw(state, action, player);
      break;
    case ACTION_TYPES.FLOWER_REPLACE:
      applyFlowerReplacement(state, action, player);
      break;
    case ACTION_TYPES.CHI:
      applyChi(state, action, player);
      break;
    case ACTION_TYPES.PENG:
      applyPeng(state, action, player);
      break;
    case ACTION_TYPES.GANG:
      applyGang(state, action, player);
      break;
    default:
      setLastActionFlags(state, false, false);
      break;
  }

  player.handTiles.sort((left, right) => left - right);
}

function simulateTziakchaRecord(record) {
  const step = getStep(record);
  validateStep(step);

  const state = createInitialGameState();
  const roundWind = WINDS[Math.floor((step.i || 0) / 4) % 4];
  state.roundWind = roundWind;

  setupWallAndDeal(state, {
    wall: decodeWallHex(step.w),
    dice: getDiceArray(step.d),
    dealerIndex: 0
  });
  state.roundWind = roundWind;

  const steps = step.a.map((actionTuple, index) => {
    const action = decodeTziakchaAction(actionTuple);
    const before = cloneGameState(state);
    applyAction(state, action);

    return {
      index,
      action,
      before,
      after: cloneGameState(state)
    };
  });

  return {
    recordId: record.id,
    initialHands: state.initialHands.map(hand => [...hand]),
    roundWind,
    resultFlags: {
      winnerMask: (step.b || 0) & 0x0f,
      discarderMask: ((step.b || 0) >> 4) & 0x0f
    },
    state,
    steps
  };
}

module.exports = {
  simulateTziakchaRecord
};
