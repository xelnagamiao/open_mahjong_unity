"use strict";

const { countFan } = require("gb-mahjong-js");
const { WINDS } = require("../core/config/game");
const { groupHandToGbString, tileIdToGbTile } = require("../core/tiles");
const { simulateTziakchaRecord } = require("./simulate");
const { parseTziakchaWinFanItems } = require("./win");

function getSeatIndexesFromMask(mask) {
  const seats = [];

  for (let seat = 0; seat < 4; seat += 1) {
    if (((mask >> seat) & 1) === 1) {
      seats.push(seat);
    }
  }

  return seats;
}

function buildProblem(code, message, extras = {}) {
  return {
    code,
    message,
    ...extras
  };
}

function reportProblem(problems, options, problem) {
  problems.push(problem);

  if (typeof options.onProblem === "function") {
    options.onProblem(problem);
    return;
  }

  if (options.throwOnProblem === false) {
    return;
  }

  throw new Error(problem.message);
}

function getWinnerScriptData(step, winnerSeat, problems, options) {
  const winData = Array.isArray(step.y) ? step.y[winnerSeat] : null;

  if (winData && typeof winData === "object") {
    return winData;
  }

  reportProblem(
    problems,
    options,
    buildProblem(
      "WIN_DATA_MISSING",
      `step.y 缺少与赢家 seat ${winnerSeat} 对应的和牌数据`,
      { winnerSeat }
    )
  );

  return null;
}

function removeFirstTile(tileIds, tileId) {
  const result = [...tileIds];
  const index = result.indexOf(tileId);

  if (index >= 0) {
    result.splice(index, 1);
  }

  return result;
}

function resolveSeatPlayer(step, seat, options = {}) {
  const seatPlayer = Array.isArray(step.p) ? step.p[seat] : null;
  const playerName = seatPlayer?.n || seatPlayer?.name || null;
  const players = options.players || options.sessionPlayers;
  let playerIndex = seat;

  if (playerName && Array.isArray(players)) {
    const resolvedIndex = players.findIndex(
      player => (player?.name || player?.n) === playerName
    );
    if (resolvedIndex >= 0) {
      playerIndex = resolvedIndex;
    }
  }

  return {
    seat,
    playerIndex,
    playerName
  };
}

function getTileSuitSuffix(tileId) {
  if (tileId < 36) {
    return "m";
  }

  if (tileId < 72) {
    return "s";
  }

  if (tileId < 108) {
    return "p";
  }

  return "";
}

function formatSingleGbTile(tileId) {
  return `${tileIdToGbTile(tileId)}${getTileSuitSuffix(tileId)}`;
}

function getMeldTileIds(meld) {
  if (Array.isArray(meld.tileIds) && meld.tileIds.length > 0) {
    return [...meld.tileIds];
  }

  if (typeof meld.tileBase !== "number") {
    return [];
  }

  const count = meld.type === "gang" ? 4 : 3;
  return Array.from({ length: count }, (_, index) => meld.tileBase + index);
}

function buildGbPackString(meld) {
  const tileIds = getMeldTileIds(meld);
  if (tileIds.length === 0) {
    return "";
  }

  const body = groupHandToGbString(tileIds);
  if (typeof meld.offerDirection === "number" && meld.offerDirection > 0) {
    return `[${body},${meld.offerDirection}]`;
  }

  return `[${body}]`;
}

function buildEnvFlags(simulated, winnerSeat, selfDraw, isRobbingKong) {
  const seaLast =
    simulated.state.wallFrontIndex > simulated.state.wallBackIndex;

  return {
    roundWind: simulated.roundWind,
    seatWind: WINDS[winnerSeat],
    selfDraw,
    lastCopy: false,
    seaLast,
    robbingKong: isRobbingKong
  };
}

function serializeEnvFlags(envFlags) {
  return [
    envFlags.roundWind,
    envFlags.seatWind,
    envFlags.selfDraw ? "1" : "0",
    envFlags.lastCopy ? "1" : "0",
    envFlags.seaLast ? "1" : "0",
    envFlags.robbingKong ? "1" : "0"
  ].join("");
}

function buildHandStrings(player, winTile, selfDraw) {
  const packString = player.melds.map(buildGbPackString).join("");
  const handTileIds = selfDraw
    ? removeFirstTile(player.handTiles, winTile)
    : [...player.handTiles];
  const handBody = groupHandToGbString(handTileIds);
  const winTileString =
    winTile === null || winTile === undefined
      ? ""
      : formatSingleGbTile(winTile);

  return {
    gbHandTilesString: `${packString}${handBody}${winTileString}`,
    formattedHand: [...player.handTiles]
      .sort((left, right) => left - right)
      .join(" ")
  };
}

function inferWinTile(simulated, winnerSeat, selfDraw) {
  if (selfDraw) {
    const player = simulated.state.players[winnerSeat];
    if (typeof player.lastDrawTile === "number") {
      return player.lastDrawTile;
    }

    const drawStep = [...simulated.steps]
      .reverse()
      .find(
        step => step.action.type === 7 && step.action.playerIndex === winnerSeat
      );
    return drawStep ? drawStep.action.detail.tileId : null;
  }

  if (typeof simulated.state.lastDiscardTile === "number") {
    return simulated.state.lastDiscardTile;
  }

  const discardStep = [...simulated.steps]
    .reverse()
    .find(step => step.action.type === 2);
  return discardStep ? discardStep.action.detail.tileId : null;
}

function inferRobbingKong(simulated, selfDraw) {
  if (selfDraw || simulated.steps.length < 2) {
    return false;
  }

  const lastNonWinStep = [...simulated.steps]
    .slice(0, -1)
    .reverse()
    .find(step => ![6, 8].includes(step.action.type));

  return Boolean(
    lastNonWinStep &&
      lastNonWinStep.action.type === 5 &&
      lastNonWinStep.action.detail &&
      lastNonWinStep.action.detail.promoted
  );
}

function defaultFanCalculator(input) {
  return countFan(`${input.hand}|${input.envFlags}`);
}

function analyzeTziakchaRecord(record, options = {}) {
  const simulated = simulateTziakchaRecord(record);
  const problems = [];
  const step = record.step || {};
  const winnerSeats = getSeatIndexesFromMask(simulated.resultFlags.winnerMask);
  const discarderSeats = getSeatIndexesFromMask(
    simulated.resultFlags.discarderMask
  );

  if (winnerSeats.length === 0) {
    reportProblem(
      problems,
      options,
      buildProblem("WINNER_MISSING", "step.b 未包含赢家")
    );
  }

  if (winnerSeats.length > 1) {
    reportProblem(
      problems,
      options,
      buildProblem("MULTIPLE_WINNERS", "暂不支持一炮多响的单赢家分析", {
        winnerSeats
      })
    );
  }

  if (discarderSeats.length > 1) {
    reportProblem(
      problems,
      options,
      buildProblem("MULTIPLE_DISCARDERS", "step.b 包含多个点炮者标记", {
        discarderSeats
      })
    );
  }

  const winnerSeat = winnerSeats[0];
  const discarderSeat = discarderSeats.length > 0 ? discarderSeats[0] : null;
  const selfDraw = discarderSeat === null || discarderSeat === winnerSeat;
  const winner = resolveSeatPlayer(step, winnerSeat, options);
  const discarder =
    discarderSeat === null
      ? null
      : resolveSeatPlayer(step, discarderSeat, options);
  const winData = getWinnerScriptData(step, winnerSeat, problems, options);
  const isRobbingKong = inferRobbingKong(simulated, selfDraw);
  const envFlags = buildEnvFlags(
    simulated,
    winnerSeat,
    selfDraw,
    isRobbingKong
  );
  const envFlagString = serializeEnvFlags(envFlags);
  const player = simulated.state.players[winnerSeat];
  const winTile = inferWinTile(simulated, winnerSeat, selfDraw);
  const handStrings = buildHandStrings(player, winTile, selfDraw);
  const fanDetails = parseTziakchaWinFanItems(winData ? winData.t : null);
  const scriptedTotalFan =
    winData && typeof winData.f === "number"
      ? winData.f
      : fanDetails.reduce((sum, item) => sum + item.totalFan, 0);

  const calculatorInput = {
    hand: handStrings.gbHandTilesString,
    winTile,
    winnerSeat,
    roundWind: simulated.roundWind,
    seatWind: WINDS[winnerSeat],
    selfDraw,
    envFlags: envFlagString,
    packs: player.melds.map(getMeldTileIds),
    flowers: [...player.flowerTiles]
  };

  return {
    recordId: record.id,
    winner,
    discarder,
    selfDraw,
    winTile,
    roundWind: simulated.roundWind,
    seatWind: WINDS[winnerSeat],
    envFlags,
    envFlagString,
    formattedHand: handStrings.formattedHand,
    gbHandTilesString: handStrings.gbHandTilesString,
    handStringForGb: `${handStrings.gbHandTilesString}|${envFlagString}`,
    scriptedWin: {
      totalFan: scriptedTotalFan,
      fanDetails
    },
    calculatedFan:
      typeof options.fanCalculator === "function"
        ? options.fanCalculator(calculatorInput)
        : defaultFanCalculator(calculatorInput),
    problems,
    simulated
  };
}

module.exports = {
  analyzeTziakchaRecord
};
