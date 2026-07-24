"use strict";

const ACTION_TYPES = {
  NONE: 0,
  FLOWER_REPLACE: 1,
  DISCARD: 2,
  CHI: 3,
  PENG: 4,
  GANG: 5,
  WIN: 6,
  DRAW: 7,
  PASS: 8,
  ABANDON: 9
};

const ACTION_TYPE_NAMES = {
  [ACTION_TYPES.NONE]: "none",
  [ACTION_TYPES.FLOWER_REPLACE]: "flowerReplace",
  [ACTION_TYPES.DISCARD]: "discard",
  [ACTION_TYPES.CHI]: "chi",
  [ACTION_TYPES.PENG]: "peng",
  [ACTION_TYPES.GANG]: "gang",
  [ACTION_TYPES.WIN]: "win",
  [ACTION_TYPES.DRAW]: "draw",
  [ACTION_TYPES.PASS]: "pass",
  [ACTION_TYPES.ABANDON]: "abandon"
};

module.exports = {
  ACTION_TYPES,
  ACTION_TYPE_NAMES
};
