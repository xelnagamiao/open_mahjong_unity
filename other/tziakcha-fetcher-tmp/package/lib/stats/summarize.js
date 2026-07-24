"use strict";

const { extractTziakchaRoundWinInfos } = require("../record/win");

function incrementCount(target, key, amount = 1) {
  target[key] = (target[key] || 0) + amount;
}

function createPlayerStats(player, playerIndex, rounds) {
  return {
    playerIndex,
    playerName: player?.name || `Seat ${playerIndex}`,
    playerId: player?.id,
    rounds,
    wins: 0,
    tsumoWins: 0,
    ronWins: 0,
    dealIns: 0,
    tsumoAgainst: 0,
    totalFan: 0,
    fanCounts: {}
  };
}

function summarizeTziakchaSession(session) {
  const totalRounds = Array.isArray(session.records)
    ? session.records.length
    : 0;
  const players = Array.from({ length: 4 }, (_, index) =>
    createPlayerStats(session.players?.[index], index, totalRounds)
  );
  const winInfos = extractTziakchaRoundWinInfos(session);
  const fanCounts = {};

  for (const info of winInfos) {
    for (const winner of info.winners) {
      const player = players[winner.playerIndex];
      if (!player) {
        continue;
      }

      player.wins += 1;
      player.totalFan += winner.totalFan;
      if (info.selfDraw) {
        player.tsumoWins += 1;
      } else {
        player.ronWins += 1;
      }

      for (const fanItem of winner.fanItems) {
        incrementCount(player.fanCounts, fanItem.fanName, fanItem.count);
        incrementCount(fanCounts, fanItem.fanName, fanItem.count);
      }
    }

    if (info.selfDraw) {
      const winnerIndexes = new Set(
        info.winners.map(winner => winner.playerIndex)
      );
      for (const player of players) {
        if (!winnerIndexes.has(player.playerIndex)) {
          player.tsumoAgainst += 1;
        }
      }
    } else {
      for (const discarder of info.discarders) {
        const player = players[discarder.playerIndex];
        if (player) {
          player.dealIns += 1;
        }
      }
    }
  }

  return {
    sessionId: session.sessionId,
    totalRounds,
    finishedRounds: winInfos.length,
    drawRounds: totalRounds - winInfos.length,
    players,
    fanCounts
  };
}

module.exports = {
  summarizeTziakchaSession
};
