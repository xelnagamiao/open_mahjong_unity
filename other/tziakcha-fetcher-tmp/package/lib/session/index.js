"use strict";

const { fetchTziakchaSession } = require("./fetch");
const { fetchTziakchaSessionRounds } = require("./rounds");

module.exports = {
  fetch: fetchTziakchaSession,
  fetchRounds: fetchTziakchaSessionRounds
};
