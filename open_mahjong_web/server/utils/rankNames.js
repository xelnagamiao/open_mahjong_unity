/** 与 open_mahjong_server/server/match/rank_calculator.py RANK_TABLE 一致 */
const RANK_NAMES = [
  '10级', '9级', '8级', '7级', '6级', '5级', '4级', '3级', '2级', '1级',
  '初段', '二段', '三段', '四段', '五段', '六段', '七段', '八段', '九段',
];

const RANK_NAME_TO_INDEX = Object.fromEntries(RANK_NAMES.map((name, i) => [name, i]));

const LEADERBOARD_MIN_USER_ID = 10000000;
const LEADERBOARD_LIMIT_DEFAULT = 100;

function isValidRankName(name) {
  return RANK_NAME_TO_INDEX[name] !== undefined;
}

module.exports = {
  RANK_NAMES,
  RANK_NAME_TO_INDEX,
  LEADERBOARD_MIN_USER_ID,
  LEADERBOARD_LIMIT_DEFAULT,
  isValidRankName,
};
