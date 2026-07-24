"use strict";

module.exports = {
  config: {
    ...require("./config/actions"),
    ...require("./config/game")
  },
  tiles: require("./tiles"),
  network: require("./network")
};
