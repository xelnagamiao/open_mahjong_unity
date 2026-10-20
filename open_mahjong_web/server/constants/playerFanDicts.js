/** Generated fan names are keyed by the complete statistics subrule. */
const fanCatalog = require('../../shared/statisticsFans.json');
const { rules } = require('../../shared/statisticsRules.json');

const guobiaoFanDict = fanCatalog['guobiao/standard'];
const riichiFanDict = fanCatalog['riichi/standard'];
const qingqueFanDict = fanCatalog['qingque/standard'];
const classicalFanDict = fanCatalog['classical/standard'];
const INFO_FAN_DICT = { ...fanCatalog, nanque: require('./nanqueFanDict') };
// Retain old response keys for older clients; current clients select by subrule.
for (const rule of rules) {
  if (rule.fan_stats && !INFO_FAN_DICT[rule.fan_key]) {
    INFO_FAN_DICT[rule.fan_key] = fanCatalog[rule.sub_rule];
  }
}

module.exports = {
  guobiaoFanDict, qingqueFanDict, classicalFanDict, riichiFanDict, INFO_FAN_DICT,
  GUOBIAO_FAN_KEYS: Object.keys(guobiaoFanDict),
};
