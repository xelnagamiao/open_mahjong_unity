/**
 * 猜番对抗题库：国标 + 立直
 * @typedef {'guobiao'|'riichi'} FanRule
 * @typedef {'顺子系'|'刻子系'|'对子系'|'全体系'|'全不靠系'|'特殊系'|'条件系'|'偶然系'} FanType
 * @typedef {number|number[]|'全体'} ReqLength
 * @typedef {{
 *   id: string,
 *   names: string[],
 *   rules: FanRule[],
 *   types: FanType[],
 *   reqLength: ReqLength,
 *   fan: number|number[]|string,
 *   relatedIds: string[],
 * }} GuessFan
 */

/** @type {GuessFan[]} */
const GUOBIAO = [
  { id: 'guobiao:dasixi', names: ['大四喜'], rules: ['guobiao'], types: ['刻子系'], reqLength: 4, fan: 88, relatedIds: ['riichi:daisushi'] },
  { id: 'guobiao:dasanyuan', names: ['大三元'], rules: ['guobiao'], types: ['刻子系'], reqLength: 3, fan: 88, relatedIds: ['riichi:daisangen'] },
  { id: 'guobiao:lvyise', names: ['绿一色'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 88, relatedIds: ['riichi:ryuuiisou'] },
  { id: 'guobiao:jiulianbaodeng', names: ['九莲宝灯'], rules: ['guobiao'], types: ['特殊系'], reqLength: 1, fan: 88, relatedIds: ['riichi:chuurenpoutou', 'riichi:junsei_chuuren'] },
  { id: 'guobiao:sigang', names: ['四杠'], rules: ['guobiao'], types: ['刻子系'], reqLength: 4, fan: 88, relatedIds: ['riichi:suukantsu'] },
  { id: 'guobiao:lianqidui', names: ['连七对'], rules: ['guobiao'], types: ['对子系'], reqLength: 7, fan: 88, relatedIds: [] },
  { id: 'guobiao:shisanyao', names: ['十三幺'], rules: ['guobiao'], types: ['特殊系'], reqLength: 1, fan: 88, relatedIds: ['riichi:kokushi', 'riichi:kokushi_13'] },
  { id: 'guobiao:qingyaojiu', names: ['清幺九'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 64, relatedIds: ['riichi:chinroutou'] },
  { id: 'guobiao:xiaosixi', names: ['小四喜'], rules: ['guobiao'], types: ['刻子系'], reqLength: 4, fan: 64, relatedIds: ['riichi:shousuushi'] },
  { id: 'guobiao:xiaosanyuan', names: ['小三元'], rules: ['guobiao'], types: ['刻子系'], reqLength: 3, fan: 64, relatedIds: ['riichi:shousangen'] },
  { id: 'guobiao:ziyise', names: ['字一色'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 64, relatedIds: ['riichi:tsuuiisou'] },
  { id: 'guobiao:sianke', names: ['四暗刻'], rules: ['guobiao'], types: ['刻子系'], reqLength: 4, fan: 64, relatedIds: ['riichi:suuankou', 'riichi:suuankou_tanki'] },
  { id: 'guobiao:yiseshuanglonghui', names: ['一色双龙会'], rules: ['guobiao'], types: ['顺子系'], reqLength: 4, fan: 64, relatedIds: [] },
  { id: 'guobiao:yisesitongshun', names: ['一色四同顺'], rules: ['guobiao'], types: ['顺子系'], reqLength: 4, fan: 48, relatedIds: [] },
  { id: 'guobiao:yisesijiegao', names: ['一色四节高'], rules: ['guobiao'], types: ['刻子系'], reqLength: 4, fan: 48, relatedIds: [] },
  { id: 'guobiao:yisesibugao', names: ['一色四步高'], rules: ['guobiao'], types: ['顺子系'], reqLength: 4, fan: 32, relatedIds: [] },
  { id: 'guobiao:sangang', names: ['三杠'], rules: ['guobiao'], types: ['刻子系'], reqLength: 3, fan: 32, relatedIds: ['riichi:sankantsu'] },
  { id: 'guobiao:hunyaojiu', names: ['混幺九'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 32, relatedIds: ['riichi:honroutou'] },
  { id: 'guobiao:qiduizi', names: ['七对', '七对子'], rules: ['guobiao'], types: ['对子系'], reqLength: 7, fan: 24, relatedIds: ['riichi:chiitoitsu'] },
  { id: 'guobiao:qixingbukao', names: ['七星不靠'], rules: ['guobiao'], types: ['全不靠系'], reqLength: '全体', fan: 24, relatedIds: [] },
  { id: 'guobiao:quanshuangke', names: ['全双刻'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 24, relatedIds: [] },
  { id: 'guobiao:qingyise', names: ['清一色'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 24, relatedIds: ['riichi:chinitsu'] },
  { id: 'guobiao:yisesantongshun', names: ['一色三同顺'], rules: ['guobiao'], types: ['顺子系'], reqLength: 3, fan: 24, relatedIds: [] },
  { id: 'guobiao:yisesanjiegao', names: ['一色三节高'], rules: ['guobiao'], types: ['刻子系'], reqLength: 3, fan: 24, relatedIds: [] },
  { id: 'guobiao:quanda', names: ['全大'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 24, relatedIds: [] },
  { id: 'guobiao:quanzhong', names: ['全中'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 24, relatedIds: [] },
  { id: 'guobiao:quanxiao', names: ['全小'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 24, relatedIds: [] },
  { id: 'guobiao:qinglong', names: ['清龙', '一气通贯'], rules: ['guobiao'], types: ['顺子系'], reqLength: 3, fan: 16, relatedIds: ['riichi:ittsu'] },
  { id: 'guobiao:sanseshuanglonghui', names: ['三色双龙会'], rules: ['guobiao'], types: ['顺子系'], reqLength: 4, fan: 16, relatedIds: [] },
  { id: 'guobiao:yisesanbugao', names: ['一色三步高'], rules: ['guobiao'], types: ['顺子系'], reqLength: 3, fan: 16, relatedIds: [] },
  { id: 'guobiao:quandaiwu', names: ['全带五'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 16, relatedIds: [] },
  { id: 'guobiao:santongke', names: ['三同刻'], rules: ['guobiao'], types: ['刻子系'], reqLength: 3, fan: 16, relatedIds: ['riichi:sanshoku_doukou'] },
  { id: 'guobiao:sananke', names: ['三暗刻'], rules: ['guobiao'], types: ['刻子系'], reqLength: 3, fan: 16, relatedIds: ['riichi:sanankou'] },
  { id: 'guobiao:quanbukao', names: ['全不靠'], rules: ['guobiao'], types: ['全不靠系'], reqLength: '全体', fan: 12, relatedIds: [] },
  { id: 'guobiao:zuhelong', names: ['组合龙'], rules: ['guobiao'], types: ['顺子系', '全不靠系'], reqLength: 1, fan: 12, relatedIds: [] },
  { id: 'guobiao:dayuwu', names: ['大于五'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 12, relatedIds: [] },
  { id: 'guobiao:xiaoyuwu', names: ['小于五'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 12, relatedIds: [] },
  { id: 'guobiao:sanfengke', names: ['三风刻'], rules: ['guobiao'], types: ['刻子系'], reqLength: 3, fan: 12, relatedIds: [] },
  { id: 'guobiao:hualong', names: ['花龙'], rules: ['guobiao'], types: ['顺子系'], reqLength: 3, fan: 8, relatedIds: [] },
  { id: 'guobiao:tuibudao', names: ['推不倒'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 8, relatedIds: [] },
  { id: 'guobiao:sansesantongshun', names: ['三色三同顺'], rules: ['guobiao'], types: ['顺子系'], reqLength: 3, fan: 8, relatedIds: ['riichi:sanshoku'] },
  { id: 'guobiao:sansesanjiegao', names: ['三色三节高'], rules: ['guobiao'], types: ['刻子系'], reqLength: 3, fan: 8, relatedIds: [] },
  { id: 'guobiao:wufanhe', names: ['无番和'], rules: ['guobiao'], types: ['条件系'], reqLength: '条件', fan: 8, relatedIds: [] },
  { id: 'guobiao:miaoshouhuichun', names: ['妙手回春'], rules: ['guobiao'], types: ['条件系', '偶然系'], reqLength: '条件', fan: 8, relatedIds: ['riichi:haitei'] },
  { id: 'guobiao:haidilaoyue', names: ['海底捞月'], rules: ['guobiao'], types: ['条件系', '偶然系'], reqLength: '条件', fan: 8, relatedIds: ['riichi:haitei'] },
  { id: 'guobiao:gangshangkaihua', names: ['杠上开花'], rules: ['guobiao'], types: ['条件系', '偶然系'], reqLength: '条件', fan: 8, relatedIds: ['riichi:rinshan'] },
  { id: 'guobiao:qiangganghe', names: ['抢杠和'], rules: ['guobiao'], types: ['条件系', '偶然系'], reqLength: '条件', fan: 8, relatedIds: ['riichi:chankan'] },
  { id: 'guobiao:pengpenghe', names: ['碰碰和', '对对和'], rules: ['guobiao'], types: ['刻子系'], reqLength: 4, fan: 6, relatedIds: ['riichi:toitoi'] },
  { id: 'guobiao:hunyise', names: ['混一色'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 6, relatedIds: ['riichi:honitsu'] },
  { id: 'guobiao:sansesanbugao', names: ['三色三步高'], rules: ['guobiao'], types: ['顺子系'], reqLength: 3, fan: 6, relatedIds: [] },
  { id: 'guobiao:wumenqi', names: ['五门齐'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 6, relatedIds: [] },
  { id: 'guobiao:quanqiuren', names: ['全求人'], rules: ['guobiao'], types: ['条件系'], reqLength: '全体', fan: 6, relatedIds: [] },
  { id: 'guobiao:shuangangang', names: ['双暗杠'], rules: ['guobiao'], types: ['刻子系'], reqLength: 2, fan: 6, relatedIds: [] },
  { id: 'guobiao:shuangjianke', names: ['双箭刻'], rules: ['guobiao'], types: ['刻子系'], reqLength: 2, fan: 6, relatedIds: [] },
  { id: 'guobiao:quandaiyao', names: ['全带幺'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 4, relatedIds: ['riichi:chanta', 'riichi:junchan'] },
  { id: 'guobiao:buqiuren', names: ['不求人'], rules: ['guobiao'], types: ['条件系'], reqLength: '全体', fan: 4, relatedIds: ['riichi:menzen_tsumo'] },
  { id: 'guobiao:shuangminggang', names: ['双明杠'], rules: ['guobiao'], types: ['刻子系'], reqLength: 2, fan: 4, relatedIds: [] },
  { id: 'guobiao:hejuezhang', names: ['和绝张'], rules: ['guobiao'], types: ['条件系'], reqLength: '条件', fan: 4, relatedIds: [] },
  { id: 'guobiao:jianke', names: ['箭刻'], rules: ['guobiao'], types: ['刻子系'], reqLength: 1, fan: 2, relatedIds: ['riichi:yakuhai_haku', 'riichi:yakuhai_hatsu', 'riichi:yakuhai_chun'] },
  { id: 'guobiao:quanfengke', names: ['圈风刻'], rules: ['guobiao'], types: ['刻子系'], reqLength: 1, fan: 2, relatedIds: ['riichi:bakaze_e', 'riichi:bakaze_s', 'riichi:bakaze_w', 'riichi:bakaze_n'] },
  { id: 'guobiao:menfengke', names: ['门风刻'], rules: ['guobiao'], types: ['刻子系'], reqLength: 1, fan: 2, relatedIds: ['riichi:jikaze_e', 'riichi:jikaze_s', 'riichi:jikaze_w', 'riichi:jikaze_n'] },
  { id: 'guobiao:menqianqing', names: ['门前清'], rules: ['guobiao'], types: ['条件系'], reqLength: '全体', fan: 2, relatedIds: ['riichi:menzen_tsumo'] },
  { id: 'guobiao:pinghe', names: ['平和'], rules: ['guobiao'], types: ['顺子系'], reqLength: 4, fan: 2, relatedIds: ['riichi:pinfu'] },
  { id: 'guobiao:siguiyi', names: ['四归一'], rules: ['guobiao'], types: ['条件系'], reqLength: [4, 3, 2], fan: 2, relatedIds: [] },
  { id: 'guobiao:shuangtongke', names: ['双同刻'], rules: ['guobiao'], types: ['刻子系'], reqLength: 2, fan: 2, relatedIds: [] },
  { id: 'guobiao:shuanganke', names: ['双暗刻'], rules: ['guobiao'], types: ['刻子系'], reqLength: 2, fan: 2, relatedIds: [] },
  { id: 'guobiao:angang', names: ['暗杠'], rules: ['guobiao'], types: ['刻子系'], reqLength: 1, fan: 2, relatedIds: [] },
  { id: 'guobiao:duanyao', names: ['断幺', '断幺九'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 2, relatedIds: ['riichi:tanyao'] },
  { id: 'guobiao:yibangao', names: ['一般高'], rules: ['guobiao'], types: ['顺子系'], reqLength: 2, fan: 1, relatedIds: ['riichi:iipeikou'] },
  { id: 'guobiao:xixiangfeng', names: ['喜相逢'], rules: ['guobiao'], types: ['顺子系'], reqLength: 2, fan: 1, relatedIds: [] },
  { id: 'guobiao:lianliu', names: ['连六'], rules: ['guobiao'], types: ['顺子系'], reqLength: 2, fan: 1, relatedIds: [] },
  { id: 'guobiao:laoshaofu', names: ['老少副'], rules: ['guobiao'], types: ['顺子系'], reqLength: 2, fan: 1, relatedIds: [] },
  { id: 'guobiao:yaojiuke', names: ['幺九刻'], rules: ['guobiao'], types: ['刻子系'], reqLength: 1, fan: 1, relatedIds: [] },
  { id: 'guobiao:minggang', names: ['明杠'], rules: ['guobiao'], types: ['刻子系'], reqLength: 1, fan: 1, relatedIds: [] },
  { id: 'guobiao:queyimen', names: ['缺一门'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 1, relatedIds: [] },
  { id: 'guobiao:wuzi', names: ['无字'], rules: ['guobiao'], types: ['全体系'], reqLength: '全体', fan: 1, relatedIds: [] },
  { id: 'guobiao:bianzhang', names: ['边张'], rules: ['guobiao'], types: ['条件系'], reqLength: 0, fan: 1, relatedIds: [] },
  { id: 'guobiao:qianzhang', names: ['嵌张'], rules: ['guobiao'], types: ['条件系'], reqLength: 0, fan: 1, relatedIds: [] },
  { id: 'guobiao:dandiaojiang', names: ['单钓将'], rules: ['guobiao'], types: ['条件系'], reqLength: 0, fan: 1, relatedIds: [] },
  { id: 'guobiao:zimo', names: ['自摸'], rules: ['guobiao'], types: ['条件系'], reqLength: '条件', fan: 1, relatedIds: ['riichi:menzen_tsumo'] },
  { id: 'guobiao:huapai', names: ['花牌'], rules: ['guobiao'], types: ['条件系', '偶然系'], reqLength: '条件', fan: 1, relatedIds: [] },
  { id: 'guobiao:mingangang', names: ['明暗杠'], rules: ['guobiao'], types: ['刻子系'], reqLength: 2, fan: 5, relatedIds: [] },
]

/** @type {GuessFan[]} */
const RIICHI = [
  { id: 'riichi:riichi', names: ['立直'], rules: ['riichi'], types: ['条件系'], reqLength: '条件', fan: 1, relatedIds: [] },
  { id: 'riichi:double_riichi', names: ['双立直', '两立直'], rules: ['riichi'], types: ['条件系'], reqLength: '条件', fan: 2, relatedIds: [] },
  { id: 'riichi:ippatsu', names: ['一发'], rules: ['riichi'], types: ['条件系', '偶然系'], reqLength: '条件', fan: 1, relatedIds: [] },
  { id: 'riichi:menzen_tsumo', names: ['门前清自摸和'], rules: ['riichi'], types: ['条件系'], reqLength: '条件', fan: 1, relatedIds: ['guobiao:menqianqing', 'guobiao:buqiuren', 'guobiao:zimo'] },
  { id: 'riichi:pinfu', names: ['平和'], rules: ['riichi'], types: ['顺子系'], reqLength: 4, fan: 1, relatedIds: ['guobiao:pinghe'] },
  { id: 'riichi:tanyao', names: ['断幺九', '断幺'], rules: ['riichi'], types: ['全体系'], reqLength: '全体', fan: 1, relatedIds: ['guobiao:duanyao'] },
  { id: 'riichi:iipeikou', names: ['一杯口'], rules: ['riichi'], types: ['顺子系'], reqLength: 2, fan: 1, relatedIds: ['guobiao:yibangao'] },
  { id: 'riichi:yakuhai_haku', names: ['役牌·白'], rules: ['riichi'], types: ['刻子系'], reqLength: 1, fan: 1, relatedIds: ['guobiao:jianke'] },
  { id: 'riichi:yakuhai_hatsu', names: ['役牌·发'], rules: ['riichi'], types: ['刻子系'], reqLength: 1, fan: 1, relatedIds: ['guobiao:jianke'] },
  { id: 'riichi:yakuhai_chun', names: ['役牌·中'], rules: ['riichi'], types: ['刻子系'], reqLength: 1, fan: 1, relatedIds: ['guobiao:jianke'] },
  { id: 'riichi:jikaze_e', names: ['自风·东'], rules: ['riichi'], types: ['刻子系'], reqLength: 1, fan: 1, relatedIds: ['guobiao:menfengke'] },
  { id: 'riichi:jikaze_s', names: ['自风·南'], rules: ['riichi'], types: ['刻子系'], reqLength: 1, fan: 1, relatedIds: ['guobiao:menfengke'] },
  { id: 'riichi:jikaze_w', names: ['自风·西'], rules: ['riichi'], types: ['刻子系'], reqLength: 1, fan: 1, relatedIds: ['guobiao:menfengke'] },
  { id: 'riichi:jikaze_n', names: ['自风·北'], rules: ['riichi'], types: ['刻子系'], reqLength: 1, fan: 1, relatedIds: ['guobiao:menfengke'] },
  { id: 'riichi:bakaze_e', names: ['场风·东'], rules: ['riichi'], types: ['刻子系'], reqLength: 1, fan: 1, relatedIds: ['guobiao:quanfengke'] },
  { id: 'riichi:bakaze_s', names: ['场风·南'], rules: ['riichi'], types: ['刻子系'], reqLength: 1, fan: 1, relatedIds: ['guobiao:quanfengke'] },
  { id: 'riichi:bakaze_w', names: ['场风·西'], rules: ['riichi'], types: ['刻子系'], reqLength: 1, fan: 1, relatedIds: ['guobiao:quanfengke'] },
  { id: 'riichi:bakaze_n', names: ['场风·北'], rules: ['riichi'], types: ['刻子系'], reqLength: 1, fan: 1, relatedIds: ['guobiao:quanfengke'] },
  { id: 'riichi:rinshan', names: ['岭上开花'], rules: ['riichi'], types: ['条件系', '偶然系'], reqLength: '条件', fan: 1, relatedIds: ['guobiao:gangshangkaihua'] },
  { id: 'riichi:chankan', names: ['枪杠', '抢杠'], rules: ['riichi'], types: ['条件系', '偶然系'], reqLength: '条件', fan: 1, relatedIds: ['guobiao:qiangganghe'] },
  { id: 'riichi:haitei', names: ['海底捞月'], rules: ['riichi'], types: ['条件系', '偶然系'], reqLength: '条件', fan: 1, relatedIds: ['guobiao:haidilaoyue', 'guobiao:miaoshouhuichun'] },
  { id: 'riichi:houtei', names: ['河底捞鱼'], rules: ['riichi'], types: ['条件系', '偶然系'], reqLength: '条件', fan: 1, relatedIds: [] },
  { id: 'riichi:dora', names: ['宝牌'], rules: ['riichi'], types: ['条件系', '偶然系'], reqLength: '条件', fan: 1, relatedIds: [] },
  { id: 'riichi:aka_dora', names: ['赤宝牌'], rules: ['riichi'], types: ['条件系', '偶然系'], reqLength: '条件', fan: 1, relatedIds: [] },
  { id: 'riichi:ura_dora', names: ['里宝牌'], rules: ['riichi'], types: ['条件系', '偶然系'], reqLength: '条件', fan: 1, relatedIds: [] },
  { id: 'riichi:sanshoku_doukou', names: ['三色同刻'], rules: ['riichi'], types: ['刻子系'], reqLength: 3, fan: 2, relatedIds: ['guobiao:santongke'] },
  { id: 'riichi:sankantsu', names: ['三杠子'], rules: ['riichi'], types: ['刻子系'], reqLength: 3, fan: 2, relatedIds: ['guobiao:sangang'] },
  { id: 'riichi:toitoi', names: ['对对和', '碰碰和'], rules: ['riichi'], types: ['刻子系'], reqLength: 4, fan: 2, relatedIds: ['guobiao:pengpenghe'] },
  { id: 'riichi:sanankou', names: ['三暗刻'], rules: ['riichi'], types: ['刻子系'], reqLength: 3, fan: 2, relatedIds: ['guobiao:sananke'] },
  { id: 'riichi:shousangen', names: ['小三元'], rules: ['riichi'], types: ['刻子系'], reqLength: 3, fan: 2, relatedIds: ['guobiao:xiaosanyuan'] },
  { id: 'riichi:honroutou', names: ['混老头'], rules: ['riichi'], types: ['全体系'], reqLength: '全体', fan: 2, relatedIds: ['guobiao:hunyaojiu'] },
  { id: 'riichi:chiitoitsu', names: ['七对子', '七对'], rules: ['riichi'], types: ['对子系'], reqLength: 7, fan: 2, relatedIds: ['guobiao:qiduizi'] },
  { id: 'riichi:chanta', names: ['混全带幺九'], rules: ['riichi'], types: ['全体系'], reqLength: '全体', fan: [2, 1], relatedIds: ['guobiao:quandaiyao'] },
  { id: 'riichi:ittsu', names: ['一气通贯', '清龙'], rules: ['riichi'], types: ['顺子系'], reqLength: 3, fan: [2, 1], relatedIds: ['guobiao:qinglong'] },
  { id: 'riichi:sanshoku', names: ['三色同顺'], rules: ['riichi'], types: ['顺子系'], reqLength: 3, fan: [2, 1], relatedIds: ['guobiao:sansesantongshun'] },
  { id: 'riichi:ryanpeikou', names: ['二杯口'], rules: ['riichi'], types: ['顺子系'], reqLength: 4, fan: 3, relatedIds: [] },
  { id: 'riichi:junchan', names: ['纯全带幺九'], rules: ['riichi'], types: ['全体系'], reqLength: '全体', fan: [3, 2], relatedIds: ['guobiao:quandaiyao'] },
  { id: 'riichi:honitsu', names: ['混一色'], rules: ['riichi'], types: ['全体系'], reqLength: '全体', fan: [3, 2], relatedIds: ['guobiao:hunyise'] },
  { id: 'riichi:chinitsu', names: ['清一色'], rules: ['riichi'], types: ['全体系'], reqLength: '全体', fan: [6, 5], relatedIds: ['guobiao:qingyise'] },
  { id: 'riichi:tenhou', names: ['天和'], rules: ['riichi'], types: ['偶然系'], reqLength: '条件', fan: '役满', relatedIds: [] },
  { id: 'riichi:chiihou', names: ['地和'], rules: ['riichi'], types: ['偶然系'], reqLength: '条件', fan: '役满', relatedIds: [] },
  { id: 'riichi:daisangen', names: ['大三元'], rules: ['riichi'], types: ['刻子系'], reqLength: 3, fan: '役满', relatedIds: ['guobiao:dasanyuan'] },
  { id: 'riichi:suuankou', names: ['四暗刻'], rules: ['riichi'], types: ['刻子系'], reqLength: 4, fan: '役满', relatedIds: ['guobiao:sianke'] },
  { id: 'riichi:tsuuiisou', names: ['字一色'], rules: ['riichi'], types: ['全体系'], reqLength: '全体', fan: '役满', relatedIds: ['guobiao:ziyise'] },
  { id: 'riichi:ryuuiisou', names: ['绿一色'], rules: ['riichi'], types: ['全体系'], reqLength: '全体', fan: '役满', relatedIds: ['guobiao:lvyise'] },
  { id: 'riichi:chinroutou', names: ['清老头'], rules: ['riichi'], types: ['全体系'], reqLength: '全体', fan: '役满', relatedIds: ['guobiao:qingyaojiu'] },
  { id: 'riichi:kokushi', names: ['国士无双'], rules: ['riichi'], types: ['特殊系'], reqLength: 1, fan: '役满', relatedIds: ['guobiao:shisanyao'] },
  { id: 'riichi:shousuushi', names: ['小四喜'], rules: ['riichi'], types: ['刻子系'], reqLength: 4, fan: '役满', relatedIds: ['guobiao:xiaosixi'] },
  { id: 'riichi:suukantsu', names: ['四杠子'], rules: ['riichi'], types: ['刻子系'], reqLength: 4, fan: '役满', relatedIds: ['guobiao:sigang'] },
  { id: 'riichi:chuurenpoutou', names: ['九莲宝灯'], rules: ['riichi'], types: ['特殊系'], reqLength: 1, fan: '役满', relatedIds: ['guobiao:jiulianbaodeng'] },
  { id: 'riichi:suuankou_tanki', names: ['四暗刻单骑'], rules: ['riichi'], types: ['刻子系'], reqLength: 4, fan: '双倍役满', relatedIds: ['guobiao:sianke'] },
  { id: 'riichi:kokushi_13', names: ['国士无双十三面'], rules: ['riichi'], types: ['特殊系'], reqLength: 1, fan: '双倍役满', relatedIds: ['guobiao:shisanyao'] },
  { id: 'riichi:junsei_chuuren', names: ['纯正九莲宝灯'], rules: ['riichi'], types: ['特殊系'], reqLength: 1, fan: '双倍役满', relatedIds: ['guobiao:jiulianbaodeng'] },
  { id: 'riichi:daisushi', names: ['大四喜'], rules: ['riichi'], types: ['刻子系'], reqLength: 4, fan: '双倍役满', relatedIds: ['guobiao:dasixi'] },
  { id: 'riichi:nagashi_mangan', names: ['流局满贯'], rules: ['riichi'], types: ['条件系'], reqLength: '条件', fan: '满贯', relatedIds: [] },
]

/** @type {GuessFan[]} */
const GUESS_FAN_CATALOG = [...GUOBIAO, ...RIICHI].map((fan) => ({
  ...fan,
  reqLength: fan.reqLength === '条件' ? 0 : fan.reqLength,
}))

/** @type {Record<string, GuessFan>} */
const GUESS_FAN_BY_ID = Object.fromEntries(GUESS_FAN_CATALOG.map((f) => [f.id, f]))

const RULE_LABEL = {
  guobiao: '国标',
  riichi: '立直',
}

const MAX_GUESSES = 8

/**
 * @param {FanRule[]} rules
 * @returns {GuessFan[]}
 */
function filterCatalogByRules(rules) {
  const set = new Set(rules)
  return GUESS_FAN_CATALOG.filter((f) => f.rules.some((r) => set.has(r)))
}

/**
 * @param {GuessFan} fan
 * @returns {number|string}
 */
function rollFanValue(fan) {
  if (Array.isArray(fan.fan)) {
    return fan.fan[Math.floor(Math.random() * fan.fan.length)]
  }
  return fan.fan
}

/**
 * @param {FanRule[]} rules
 * @returns {{ fan: GuessFan, rolledFan: number|string }}
 */
function rollAnswer(rules) {
  const pool = filterCatalogByRules(rules)
  if (!pool.length) throw new Error('题库为空')
  const fan = pool[Math.floor(Math.random() * pool.length)]
  return { fan, rolledFan: rollFanValue(fan) }
}

/**
 * 按名称查找（精确匹配 names；优先正式名 names[0]，再按 preferredRules）
 * @param {string} name
 * @param {FanRule[]} [preferredRules]
 * @returns {GuessFan|null}
 */
function findFanByName(name, preferredRules = ['guobiao', 'riichi']) {
  const q = String(name || '').trim()
  if (!q) return null
  const hits = GUESS_FAN_CATALOG.filter((f) => f.names.includes(q))
  if (!hits.length) return null

  const primary = hits.filter((f) => f.names[0] === q)
  const pool = primary.length ? primary : hits

  for (const r of preferredRules) {
    const hit = pool.find((f) => f.rules.includes(r))
    if (hit) return hit
  }
  return pool[0]
}

/**
 * @param {string} query
 * @param {FanRule[]} rules
 * @param {number} [limit]
 */
function suggestFans(query, rules, limit = 12) {
  const q = String(query || '').trim()
  const pool = filterCatalogByRules(rules)
  // 空输入不展示默认列表，避免下拉常驻干扰
  if (!q) return []
  const scored = []
  for (const f of pool) {
    let score = 0
    for (const n of f.names) {
      if (n === q) score = Math.max(score, 100)
      else if (n.startsWith(q)) score = Math.max(score, 80)
      else if (n.includes(q)) score = Math.max(score, 50)
    }
    if (score > 0) scored.push({ f, score })
  }
  scored.sort((a, b) => b.score - a.score || a.f.names[0].localeCompare(b.f.names[0], 'zh'))
  const buckets = rules.map((rule) => scored.filter(({ f }) => f.rules.includes(rule)))
  const result = []
  const seen = new Set()
  while (result.length < limit && buckets.some((bucket) => bucket.length)) {
    for (const bucket of buckets) {
      const item = bucket.shift()
      if (!item || seen.has(item.f.id)) continue
      seen.add(item.f.id)
      result.push(item.f)
      if (result.length >= limit) break
    }
  }
  return result
}

/**
 * @param {number|string} fan
 */
function formatFanDisplay(fan) {
  if (typeof fan === 'number') return String(fan)
  return String(fan)
}

/**
 * 展示用番数：数组显示为 2/1
 * @param {number|number[]|string} fan
 */
function formatFanField(fan) {
  if (Array.isArray(fan)) return fan.join('/')
  return formatFanDisplay(fan)
}

module.exports = { GUESS_FAN_CATALOG, GUESS_FAN_BY_ID, RULE_LABEL, MAX_GUESSES, filterCatalogByRules, rollFanValue, rollAnswer, findFanByName, suggestFans, formatFanDisplay, formatFanField };
