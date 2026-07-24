"use strict";

function createPlayerState() {
  return {
    handTiles: [],
    melds: [],
    discards: [],
    flowerTiles: [],
    flowerCount: 0,
    initialHandTiles: [],
    lastDrawTile: null
  };
}

function createInitialGameState() {
  return {
    players: Array.from({ length: 4 }, () => createPlayerState()),
    initialHands: [[], [], [], []],
    wall: [],
    wallFrontIndex: 0,
    wallBackIndex: -1,
    dealerIndex: 0,
    currentPlayerIndex: -1,
    lastDiscardTile: null,
    lastDiscardPlayerIndex: null,
    lastActionWasKong: false,
    lastActionWasAddedKong: false,
    roundIndex: 0,
    roundWind: null
  };
}

function cloneGameState(state) {
  return JSON.parse(JSON.stringify(state));
}

function normalizeDice(dice) {
  if (!Array.isArray(dice) || dice.length !== 4) {
    return [0, 0, 0, 0];
  }

  return dice.map(value => (typeof value === "number" ? value : 0));
}

function setupWallAndDeal(state, { wall, dice, dealerIndex }) {
  const normalizedDice = normalizeDice(dice);
  const wallBreakPos =
    (dealerIndex - (normalizedDice[0] + normalizedDice[1] - 1) + 12) % 4;
  let startPos =
    wallBreakPos * 36 +
    (normalizedDice[0] +
      normalizedDice[1] +
      normalizedDice[2] +
      normalizedDice[3]) *
      2;
  startPos %= wall.length;

  state.wall = [...wall.slice(startPos), ...wall.slice(0, startPos)];
  state.wallFrontIndex = 0;
  state.wallBackIndex = state.wall.length - 1;
  state.dealerIndex = dealerIndex;
  state.currentPlayerIndex = dealerIndex;
  state.lastDiscardTile = null;
  state.lastDiscardPlayerIndex = null;
  state.lastActionWasKong = false;
  state.lastActionWasAddedKong = false;

  for (const player of state.players) {
    player.handTiles = [];
    player.melds = [];
    player.discards = [];
    player.flowerTiles = [];
    player.flowerCount = 0;
    player.initialHandTiles = [];
    player.lastDrawTile = null;
  }

  for (let round = 0; round < 3; round += 1) {
    for (let offset = 0; offset < 4; offset += 1) {
      const playerIndex = (dealerIndex + offset) % 4;
      for (let draw = 0; draw < 4; draw += 1) {
        state.players[playerIndex].handTiles.push(
          state.wall[state.wallFrontIndex]
        );
        state.wallFrontIndex += 1;
      }
    }
  }

  for (let offset = 0; offset < 4; offset += 1) {
    const playerIndex = (dealerIndex + offset) % 4;
    state.players[playerIndex].handTiles.push(state.wall[state.wallFrontIndex]);
    state.wallFrontIndex += 1;
  }

  state.players[dealerIndex].handTiles.push(state.wall[state.wallFrontIndex]);
  state.wallFrontIndex += 1;

  state.initialHands = state.players.map(player => {
    player.handTiles.sort((left, right) => left - right);
    player.initialHandTiles = [...player.handTiles];
    return [...player.initialHandTiles];
  });

  return state;
}

module.exports = {
  cloneGameState,
  createInitialGameState,
  setupWallAndDeal
};
