/**
 * Guobiao (Chinese Official) hepai / fan calculation.
 * Ported from server guobiao_hepai_check.py (parity with Unity GBhepai.cs).
 */

export type HepaiResult = { fan: number; fanNames: string[] }

function removeFirst<T>(arr: T[], value: T): boolean {
  const i = arr.indexOf(value)
  if (i < 0) return false
  arr.splice(i, 1)
  return true
}

function isSubset<T>(small: Iterable<T>, big: Set<T> | Iterable<T>): boolean {
  const b = big instanceof Set ? big : new Set(big)
  for (const x of small) if (!b.has(x)) return false
  return true
}

/** Python-like `x in y` for Set / Array / string / object keys. */
function containsIn(container: any, item: any): boolean {
  if (container == null) return false
  if (container instanceof Set) return container.has(item)
  if (Array.isArray(container)) return container.includes(item)
  if (typeof container === 'string') return container.includes(String(item))
  if (typeof container === 'object') return Object.prototype.hasOwnProperty.call(container, item)
  return false
}

function arraysEqual(a: any, b: any): boolean {
  if (!Array.isArray(a) || !Array.isArray(b)) return a === b
  if (a.length !== b.length) return false
  for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false
  return true
}

function countOccurrences(obj: any, item: any): number {
  if (typeof obj === 'string') {
    if (typeof item === 'string' && item.length === 1) {
      let n = 0
      for (const c of obj) if (c === item) n++
      return n
    }
    // multi-char: count non-overlapping like Python
    let n = 0
    let i = 0
    const s = String(item)
    while (true) {
      const j = obj.indexOf(s, i)
      if (j < 0) break
      n++
      i = j + s.length
    }
    return n
  }
  if (Array.isArray(obj)) return obj.filter((x) => x === item).length
  return 0
}

export class PlayerTiles {
  hand_tiles: number[]
  combination_list: string[]
  complete_step: number
  fan_list: string[]
  point_count_dict: Record<string, number>
  fan_count_list: string[]
  initial_combination_count: number
  
  constructor(tiles_list: number[], combination_list: string[], complete_step: number) {
    this.hand_tiles = [...tiles_list].sort((a, b) => a - b)
    this.combination_list = [...combination_list]
    this.initial_combination_count = combination_list.length
    this.complete_step = complete_step
    this.fan_list = []
    this.point_count_dict = {}
    this.fan_count_list = []
  }
  
  deepCopy(): PlayerTiles {
    const n = new PlayerTiles([...this.hand_tiles], [...this.combination_list], this.complete_step)
    n.fan_list = [...this.fan_list]
    n.initial_combination_count = this.initial_combination_count
    return n
  }
}

export class Chinese_Hepai_Check {
  debug: boolean
  _count_model_dict: Record<string, number>
  
  static duanyao_set = new Set([12, 13, 14, 15, 16, 17, 18, 22, 23, 24, 25, 26, 27, 28, 32, 33, 34, 35, 36, 37, 38])
  
  static zipai_set = new Set([41, 42, 43, 44, 45, 46, 47])
  
  static wan_set = new Set([11, 12, 13, 14, 15, 16, 17, 18, 19])
  
  static bing_set = new Set([21, 22, 23, 24, 25, 26, 27, 28, 29])
  
  static tiao_set = new Set([31, 32, 33, 34, 35, 36, 37, 38, 39])
  
  static feng_set = new Set([41, 42, 43, 44])
  
  static zhongbaifa_set = new Set([45, 46, 47])
  
  static lvyise_set = new Set([32, 33, 34, 36, 38, 47])
  
  static hunyaojiu_set = new Set([11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47])
  
  static qingyaojiu_set = new Set([11, 19, 21, 29, 31, 39])
  
  static quanda_set = new Set([17, 18, 19, 27, 28, 29, 37, 38, 39])
  
  static quanzhong_set = new Set([14, 15, 16, 24, 25, 26, 34, 35, 36])
  
  static quanxiao_set = new Set([11, 12, 13, 21, 22, 23, 31, 32, 33])
  
  static dayuwu_set = new Set([16, 17, 18, 19, 26, 27, 28, 29, 36, 37, 38, 39])
  
  static xiaoyuwu_set = new Set([11, 12, 13, 14, 21, 22, 23, 24, 31, 32, 33, 34])
  
  static tuibudao_set = new Set([21, 22, 23, 24, 25, 28, 29, 46, 32, 34, 35, 36, 38, 39])
  
  static jiulianbaodeng_list = [1, 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9]
  
  static yiseshuanglonghui_list = [1, 1, 2, 2, 3, 3, 5, 5, 7, 7, 8, 8, 9, 9]
  
  static quandaiwu_set = new Set(["s14", "s15", "s16", "s24", "s25", "s26", "s34", "s35", "s36", "S14", "S15", "S16", "S24", "S25", "S26", "S34", "S35", "S36", "k15", "K15", "g15", "G15", "k25", "K25", "g25", "G25", "k35", "K35", "g35", "G35", "q15", "q25", "q35"])
  
  static fengke_set = new Set(["k41", "k42", "k43", "k44", "K41", "K42", "K43", "K44", "g41", "G41", "g42", "G42", "g43", "G43", "g44", "G44"])
  
  static jianke_set = new Set(["k45", "k46", "k47", "K45", "K46", "K47", "g45", "G45", "g46", "G46", "g47", "G47"])
  
  static fengke_quetou_set = new Set(["q41", "q42", "q43", "q44"])
  
  static jianke_quetou_set = new Set(["q45", "q46", "q47"])
  
  static quandaiyao_set = new Set(["s12", "s18", "s22", "s28", "s32", "s38", "S12", "S18", "S22", "S28", "S32", "S38", "k11", "k19", "k21", "k29", "k31", "k39", "k41", "k42", "k43", "k44", "k45", "k46", "k47", "K11", "K19", "K21", "K29", "K31", "K39", "K41", "K42", "K43", "K44", "K45", "K46", "K47", "g11", "g19", "g21", "g29", "g31", "g39", "g41", "g42", "g43", "g44", "g45", "g46", "g47", "G11", "G19", "G21", "G29", "G31", "G39", "G41", "G42", "G43", "G44", "G45", "G46", "G47", "q11", "q19", "q21", "q29", "q31", "q39", "q41", "q42", "q43", "q44", "q45", "q46", "q47"])
  
  static yaojiuke_set = new Set(["k11", "K11", "k19", "K19", "k21", "K21", "k29", "K29", "k31", "K31", "k39", "K39", "k41", "K41", "k42", "K42", "k43", "K43", "k44", "K44", "k45", "K45", "k46", "K46", "k47", "K47", "g11", "G11", "g19", "G19", "g21", "G21", "g29", "G29", "g31", "G31", "g39", "G39", "g41", "G41", "g42", "G42", "g43", "G43", "g44", "G44", "g45", "G45", "g46", "G46", "g47", "G47"])
  
  static repel_model_dict: Record<string, string[]> = { dasixi: (["pengpenghe", "quanfengke", "menfengke"] + (["yaojiuke"] * 4)), dasanyuan: (["yaojiuke"] * 3), lvyise: ["hunyise"], sigang: ["pengpenghe", "dandiaojiang"], jiulianbaodeng_dianhe: ["qingyise", "wuzi", "yaojiuke", "menqianqing"], jiulianbaodeng_zimo: ["qingyise", "wuzi", "buqiuren", "yaojiuke"], lianqidui_dianhe: ["qidui", "qingyise", "wuzi", "menqianqing"], lianqidui_zimo: ["qidui", "qingyise", "wuzi", "buqiuren"], shisanyao_dianhe: ["hunyaojiu", "wumenqi", "menqianqing"], shisanyao_zimo: ["hunyaojiu", "wumenqi", "buqiuren"], qingyaojiu: (["pengpenghe", "quandaiyao", "shuangtongke", "shuangtongke", "wuzi"] + (["yaojiuke"] * 4)), xiaosixi: (["sanfengke"] + (["yaojiuke"] * 3)), xiaosanyuan: (["shuangjianke"] + (["yaojiuke"] * 2)), ziyise: (["pengpenghe", "quandaiyao"] + (["yaojiuke"] * 4)), sianke_dianhe: ["pengpenghe", "menqianqing"], sianke_zimo: ["pengpenghe", "buqiuren"], yiseshuanglonghui: ["qingyise", "pinghe", "wuzi", "yibangao", "yibangao"], yisesitongshun: (["siguiyi"] * 4), yisesijiegao: ["pengpenghe"], yisesibugao: [], sangang: [], hunyaojiu: (["pengpenghe", "quandaiyao"] + (["yaojiuke"] * 4)), qiduizi_dianhe: ["menqianqing"], qiduizi_zimo: ["buqiuren"], qixingbukao_dianhe: ["quanbukao", "wumenqi", "menqianqing"], qixingbukao_zimo: ["quanbukao", "wumenqi", "buqiuren"], quanshuangke: ["pengpenghe", "duanyao", "wuzi"], qingyise: ["wuzi"], yisesantongshun: [], yisesanjiegao: [], quanda: ["dayuwu", "wuzi"], quanzhong: ["duanyao", "wuzi"], quanxiao: ["xiaoyuwu", "wuzi"], qinglong: [], sanseshuanglonghui: ["pinghe", "wuzi"], yisesanbugao: [], quandaiwu: ["duanyao", "wuzi"], santongke: [], sananke: [], quanbukao_dianhe: ["menqianqing"], quanbukao_zimo: ["buqiuren"], zuhelong: [], dayuwu: ["wuzi"], xiaoyuwu: ["wuzi"], sanfengke: [], hualong: [], tuibudao: ["queyimen"], sansesantongshun: [], sansesanjiegao: [], wufanhe: [], miaoshouhuichun: ["zimo"], haidilaoyue: [], gangshangkaihua: ["zimo"], qiangganghe: ["hejuezhang"], pengpenghe: [], hunyise: [], sansesanbugao: [], wumenqi: [], quanqiuren: ["dandiaojiang"], shuangangang: ["shuanganke"], shuangjianke: (["yaojiuke"] * 2), quandaiyao: [], buqiuren: ["zimo"], shuangminggang: [], hejuezhang: [], jianke: (["yaojiuke"] * 1), quanfengke: [], menfengke: [], menqianqing: [], pinghe: ["wuzi"], siguiyi: [], shuangtongke: [], shuanganke: [], angang: [], duanyao: ["wuzi"], yibangao: [], xixiangfeng: [], lianliu: [], laoshaofu: [], yaojiuke: [], minggang: [], queyimen: [], wuzi: [], bianzhang: [], qianzhang: [], dandiaojiang: [], zimo: [], huapai: [], mingangang: [] }
  
  static count_model_dict_default: Record<string, number> = { dasixi: 88, dasanyuan: 88, lvyise: 88, jiulianbaodeng: 88, sigang: 88, lianqidui: 88, shisanyao: 88, qingyaojiu: 64, xiaosixi: 64, xiaosanyuan: 64, ziyise: 64, sianke: 64, yiseshuanglonghui: 64, yisesitongshun: 48, yisesijiegao: 48, yisesibugao: 32, sangang: 32, hunyaojiu: 32, qiduizi: 24, qixingbukao: 24, quanshuangke: 24, qingyise: 24, yisesantongshun: 24, yisesanjiegao: 24, quanda: 24, quanzhong: 24, quanxiao: 24, qinglong: 16, sanseshuanglonghui: 16, yisesanbugao: 16, quandaiwu: 16, santongke: 16, sananke: 16, quanbukao: 12, zuhelong: 12, dayuwu: 12, xiaoyuwu: 12, sanfengke: 12, hualong: 8, tuibudao: 8, sansesantongshun: 8, sansesanjiegao: 8, wufanhe: 8, miaoshouhuichun: 8, haidilaoyue: 8, gangshangkaihua: 8, qiangganghe: 8, pengpenghe: 6, hunyise: 6, sansesanbugao: 6, wumenqi: 6, quanqiuren: 6, shuangangang: 6, shuangjianke: 6, quandaiyao: 4, buqiuren: 4, shuangminggang: 4, hejuezhang: 4, jianke: 2, quanfengke: 2, menfengke: 2, menqianqing: 2, pinghe: 2, siguiyi: 2, shuangtongke: 2, shuanganke: 2, angang: 2, duanyao: 2, yibangao: 1, xixiangfeng: 1, lianliu: 1, laoshaofu: 1, yaojiuke: 1, minggang: 1, queyimen: 1, wuzi: 1, bianzhang: 1, qianzhang: 1, dandiaojiang: 1, zimo: 1, huapai: 1, mingangang: 5 }
  
  static eng_to_chinese_dict: Record<string, any> = { dasixi: "大四喜", dasanyuan: "大三元", lvyise: "绿一色", jiulianbaodeng: "九莲宝灯", sigang: "四杠", sangang: "三杠", lianqidui: "连七对", shisanyao: "十三幺", qingyaojiu: "清幺九", xiaosixi: "小四喜", xiaosanyuan: "小三元", ziyise: "字一色", sianke: "四暗刻", yiseshuanglonghui: "一色双龙会", yisesitongshun: "一色四同顺", yisesijiegao: "一色四节高", yisesibugao: "一色四步高", hunyaojiu: "混幺九", qiduizi: "七对", qixingbukao: "七星不靠", quanshuangke: "全双刻", qingyise: "清一色", yisesantongshun: "一色三同顺", yisesanjiegao: "一色三节高", quanda: "全大", quanzhong: "全中", quanxiao: "全小", qinglong: "清龙", sanseshuanglonghui: "三色双龙会", yisesanbugao: "一色三步高", quandaiwu: "全带五", santongke: "三同刻", sananke: "三暗刻", quanbukao: "全不靠", zuhelong: "组合龙", dayuwu: "大于五", xiaoyuwu: "小于五", sanfengke: "三风刻", hualong: "花龙", tuibudao: "推不倒", sansesantongshun: "三色三同顺", sansesanjiegao: "三色三节高", wufanhe: "无番和", miaoshouhuichun: "妙手回春", haidilaoyue: "海底捞月", gangshangkaihua: "杠上开花", qiangganghe: "抢杠和", pengpenghe: "碰碰和", hunyise: "混一色", sansesanbugao: "三色三步高", wumenqi: "五门齐", quanqiuren: "全求人", shuangangang: "双暗杠", shuangjianke: "双箭刻", quandaiyao: "全带幺", buqiuren: "不求人", shuangminggang: "双明杠", hejuezhang: "和绝张", jianke: "箭刻", quanfengke: "圈风刻", menfengke: "门风刻", menqianqing: "门前清", pinghe: "平和", siguiyi: "四归一", shuangtongke: "双同刻", shuanganke: "双暗刻", angang: "暗杠", duanyao: "断幺", yibangao: "一般高", xixiangfeng: "喜相逢", lianliu: "连六", laoshaofu: "老少副", yaojiuke: "幺九刻", minggang: "明杠", queyimen: "缺一门", wuzi: "无字", bianzhang: "边张", qianzhang: "嵌张", dandiaojiang: "单钓将", zimo: "自摸", huapai: "花牌", mingangang: "明暗杠" }
  
  constructor(debug: boolean = false, count_dict: Record<string, number> | null | undefined = null) {
    this.debug = debug
    this._count_model_dict = (typeof count_dict !== 'undefined' && count_dict) ? count_dict : Chinese_Hepai_Check.count_model_dict_default
  }
  
  debug_print() {
    "只在debug模式下打印"
    if (this.debug) {
      this.debug_print(...args)
      this.debug_print(...args)
    }
  }
  
  static combination_to_tiles_dict = { s12: [11, 12, 13], s13: [12, 13, 14], s14: [13, 14, 15], s15: [14, 15, 16], s16: [15, 16, 17], s17: [16, 17, 18], s18: [17, 18, 19], s22: [21, 22, 23], s23: [22, 23, 24], s24: [23, 24, 25], s25: [24, 25, 26], s26: [25, 26, 27], s27: [26, 27, 28], s28: [27, 28, 29], s32: [31, 32, 33], s33: [32, 33, 34], s34: [33, 34, 35], s35: [34, 35, 36], s36: [35, 36, 37], s37: [36, 37, 38], s38: [37, 38, 39], S12: [11, 12, 13], S13: [12, 13, 14], S14: [13, 14, 15], S15: [14, 15, 16], S16: [15, 16, 17], S17: [16, 17, 18], S18: [17, 18, 19], S22: [21, 22, 23], S23: [22, 23, 24], S24: [23, 24, 25], S25: [24, 25, 26], S26: [25, 26, 27], S27: [26, 27, 28], S28: [27, 28, 29], S32: [31, 32, 33], S33: [32, 33, 34], S34: [33, 34, 35], S35: [34, 35, 36], S36: [35, 36, 37], S37: [36, 37, 38], S38: [37, 38, 39], k11: [11, 11, 11], k12: [12, 12, 12], k13: [13, 13, 13], k14: [14, 14, 14], k15: [15, 15, 15], k16: [16, 16, 16], k17: [17, 17, 17], k18: [18, 18, 18], k19: [19, 19, 19], k21: [21, 21, 21], k22: [22, 22, 22], k23: [23, 23, 23], k24: [24, 24, 24], k25: [25, 25, 25], k26: [26, 26, 26], k27: [27, 27, 27], k28: [28, 28, 28], k29: [29, 29, 29], k31: [31, 31, 31], k32: [32, 32, 32], k33: [33, 33, 33], k34: [34, 34, 34], k35: [35, 35, 35], k36: [36, 36, 36], k37: [37, 37, 37], k38: [38, 38, 38], k39: [39, 39, 39], k41: [41, 41, 41], k42: [42, 42, 42], k43: [43, 43, 43], k44: [44, 44, 44], k45: [45, 45, 45], k46: [46, 46, 46], k47: [47, 47, 47], K11: [11, 11, 11], K12: [12, 12, 12], K13: [13, 13, 13], K14: [14, 14, 14], K15: [15, 15, 15], K16: [16, 16, 16], K17: [17, 17, 17], K18: [18, 18, 18], K19: [19, 19, 19], K21: [21, 21, 21], K22: [22, 22, 22], K23: [23, 23, 23], K24: [24, 24, 24], K25: [25, 25, 25], K26: [26, 26, 26], K27: [27, 27, 27], K28: [28, 28, 28], K29: [29, 29, 29], K31: [31, 31, 31], K32: [32, 32, 32], K33: [33, 33, 33], K34: [34, 34, 34], K35: [35, 35, 35], K36: [36, 36, 36], K37: [37, 37, 37], K38: [38, 38, 38], K39: [39, 39, 39], K41: [41, 41, 41], K42: [42, 42, 42], K43: [43, 43, 43], K44: [44, 44, 44], K45: [45, 45, 45], K46: [46, 46, 46], K47: [47, 47, 47], q11: [11, 11], q12: [12, 12], q13: [13, 13], q14: [14, 14], q15: [15, 15], q16: [16, 16], q17: [17, 17], q18: [18, 18], q19: [19, 19], q21: [21, 21], q22: [22, 22], q23: [23, 23], q24: [24, 24], q25: [25, 25], q26: [26, 26], q27: [27, 27], q28: [28, 28], q29: [29, 29], q31: [31, 31], q32: [32, 32], q33: [33, 33], q34: [34, 34], q35: [35, 35], q36: [36, 36], q37: [37, 37], q38: [38, 38], q39: [39, 39], q41: [41, 41], q42: [42, 42], q43: [43, 43], q44: [44, 44], q45: [45, 45], q46: [46, 46], q47: [47, 47], g11: [11, 11, 11], g12: [12, 12, 12], g13: [13, 13, 13], g14: [14, 14, 14], g15: [15, 15, 15], g16: [16, 16, 16], g17: [17, 17, 17], g18: [18, 18, 18], g19: [19, 19, 19], g21: [21, 21, 21], g22: [22, 22, 22], g23: [23, 23, 23], g24: [24, 24, 24], g25: [25, 25, 25], g26: [26, 26, 26], g27: [27, 27, 27], g28: [28, 28, 28], g29: [29, 29, 29], g31: [31, 31, 31], g32: [32, 32, 32], g33: [33, 33, 33], g34: [34, 34, 34], g35: [35, 35, 35], g36: [36, 36, 36], g37: [37, 37, 37], g38: [38, 38, 38], g39: [39, 39, 39], g41: [41, 41, 41], g42: [42, 42, 42], g43: [43, 43, 43], g44: [44, 44, 44], g45: [45, 45, 45], g46: [46, 46, 46], g47: [47, 47, 47], G11: [11, 11, 11], G12: [12, 12, 12], G13: [13, 13, 13], G14: [14, 14, 14], G15: [15, 15, 15], G16: [16, 16, 16], G17: [17, 17, 17], G18: [18, 18, 18], G19: [19, 19, 19], G21: [21, 21, 21], G22: [22, 22, 22], G23: [23, 23, 23], G24: [24, 24, 24], G25: [25, 25, 25], G26: [26, 26, 26], G27: [27, 27, 27], G28: [28, 28, 28], G29: [29, 29, 29], G31: [31, 31, 31], G32: [32, 32, 32], G33: [33, 33, 33], G34: [34, 34, 34], G35: [35, 35, 35], G36: [36, 36, 36], G37: [37, 37, 37], G38: [38, 38, 38], G39: [39, 39, 39], G41: [41, 41, 41], G42: [42, 42, 42], G43: [43, 43, 43], G44: [44, 44, 44], G45: [45, 45, 45], G46: [46, 46, 46], G47: [47, 47, 47], z0: [11, 14, 17, 22, 25, 28, 33, 36, 39], z1: [11, 14, 17, 32, 35, 38, 23, 26, 29], z2: [21, 24, 27, 12, 15, 18, 33, 36, 39], z3: [21, 24, 27, 32, 35, 38, 13, 16, 19], z4: [31, 34, 37, 22, 25, 28, 13, 16, 19], z5: [31, 34, 37, 12, 15, 18, 23, 26, 29] }
  
  static yaojiu = new Set([11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47])
  
  static zipai = new Set([41, 42, 43, 44, 45, 46, 47])
  
  hepai_check(hand_list: number[], tiles_combination: string[], way_to_hepai: string[], get_tile: number): [number, string[]] {
    let complete_step = ((tiles_combination).length * 3)
    let player_tiles = new PlayerTiles(hand_list, tiles_combination, complete_step)
    this.debug_print("传参手牌：", player_tiles.hand_tiles, "传参组合：", player_tiles.combination_list, "传参和牌方式：", way_to_hepai, "传参和牌张：", get_tile)
    let player_tiles_list = []
    if (((player_tiles.hand_tiles).length === 14)) {
      if ((player_tiles_list.length === 0)) {
        this.GS_check(player_tiles, player_tiles_list)
      }
      if ((player_tiles_list.length === 0)) {
        this.QBK_check(player_tiles, player_tiles_list)
      }
      if ((player_tiles_list.length === 0)) {
        this.QD_check(player_tiles, player_tiles_list)
      }
    } else {
      this.QBK_check(player_tiles, player_tiles_list)
    }
    player_tiles_list.push(player_tiles)
    let check_done_list = []
    for (const player_tiles_item of player_tiles_list) {
      this.normal_check(player_tiles_item, check_done_list)
    }
    let fancount_time_start = 0
    let allow_list = []
    if (check_done_list.length > 0) {
      for (let i of check_done_list) {
        allow_list.push(this.fan_count(i, get_tile, way_to_hepai))
      }
    }
    let fancount_time_end = 0
    this.debug_print(`番种计算耗时：${(fancount_time_end - fancount_time_start)}秒`)
    allow_list = [...allow_list].sort((a, b) => ((x) => x[0])(b) - ((x) => x[0])(a))
    this.debug_print(`允许的番种：${allow_list}`)
    if ((allow_list.length === 0)) {
      return [0, []]
    }
    return allow_list[0]
  }
  
  GS_check(player_tiles: PlayerTiles, player_tiles_list: any[]) {
    let temp_player_tiles = player_tiles.deepCopy()
    let allow_same_id = true
    let same_tile_id = 0
    let hepai_step = 0
    for (const tile_id of temp_player_tiles.hand_tiles) {
      if (((containsIn(Chinese_Hepai_Check.yaojiu, tile_id)) && ((tile_id !== same_tile_id) || allow_same_id))) {
        if ((tile_id === same_tile_id)) {
          allow_same_id = false
        }
        same_tile_id = tile_id
        hepai_step += 1
      }
      if ((hepai_step === 14)) {
        temp_player_tiles.complete_step = 14
        temp_player_tiles.fan_list.push("shisanyao")
        player_tiles_list.push(temp_player_tiles)
      }
    }
  }
  
  QD_check(player_tiles: PlayerTiles, player_tiles_list: any[]) {
    let temp_player_tiles = player_tiles.deepCopy()
    let tile_counts = {  }
    for (const tile_id of temp_player_tiles.hand_tiles) {
      if ((containsIn(tile_counts, tile_id))) {
        tile_counts[tile_id] += 1
      } else {
        tile_counts[tile_id] = 1
      }
    }
    let double_pair = false
    for (const [tile_id, count] of Object.entries(tile_counts) as [string, number][]) {
      if ((count === 2)) {
        // pass
      } else if ((count === 4)) {
        double_pair = true
      } else {
        return false
      }
    }
    let tile_pointer = temp_player_tiles.hand_tiles[0]
    let _broke = false
    for (let i of temp_player_tiles.hand_tiles) {
      if ((((tile_pointer === i) || ((tile_pointer + 1) === i)) && (i <= 40))) {
        tile_pointer = i
      } else {
        _broke = true
        break
      }
    }
    if (!_broke) {
      if ((double_pair === false)) {
        temp_player_tiles.fan_list.push("lianqidui")
        temp_player_tiles.complete_step = 14
        player_tiles_list.push(temp_player_tiles)
        return false
      }
    }
    temp_player_tiles.complete_step = 14
    temp_player_tiles.fan_list.push("qiduizi")
    player_tiles_list.push(temp_player_tiles)
    return false
  }
  
  QBK_check(player_tiles: PlayerTiles, player_tiles_list: any[]) {
    let hand_kind_set = (new Set(player_tiles.hand_tiles)).size
    if ((hand_kind_set === 14)) {
      let QBK_case_list = [new Set([11, 14, 17, 22, 25, 28, 33, 36, 39, 41, 42, 43, 44, 45, 46, 47]), new Set([11, 14, 17, 32, 35, 38, 23, 26, 29, 41, 42, 43, 44, 45, 46, 47]), new Set([21, 24, 27, 12, 15, 18, 33, 36, 39, 41, 42, 43, 44, 45, 46, 47]), new Set([21, 24, 27, 32, 35, 38, 13, 16, 19, 41, 42, 43, 44, 45, 46, 47]), new Set([31, 34, 37, 22, 25, 28, 13, 16, 19, 41, 42, 43, 44, 45, 46, 47]), new Set([31, 34, 37, 12, 15, 18, 23, 26, 29, 41, 42, 43, 44, 45, 46, 47])]
      for (let [idx, case_] of QBK_case_list.entries()) {
        let QBK_set = new Set()
        for (let i of player_tiles.hand_tiles) {
          if ((containsIn(case_, i))) {
            QBK_set.add(i)
          }
        }
        if (((QBK_set).size === 14)) {
          let temp_player_tiles = player_tiles.deepCopy()
          temp_player_tiles.complete_step += 14
          temp_player_tiles.combination_list.push(`z${idx}`)
          let zipai_count = 0
          for (let i of QBK_set) {
            if ((containsIn(Chinese_Hepai_Check.zipai, i))) {
              zipai_count += 1
            }
          }
          if ((zipai_count === 7)) {
            temp_player_tiles.fan_list.push("qixingbukao")
            player_tiles_list.push(temp_player_tiles)
          } else if ((zipai_count === 5)) {
            temp_player_tiles.fan_list.push("quanbukao")
            temp_player_tiles.fan_list.push("zuhelong")
            player_tiles_list.push(temp_player_tiles)
          } else {
            temp_player_tiles.fan_list.push("quanbukao")
            player_tiles_list.push(temp_player_tiles)
          }
          return false
        }
      }
    } else if ((hand_kind_set >= 9)) {
      let ZHL_case_list = [new Set([11, 14, 17, 22, 25, 28, 33, 36, 39]), new Set([11, 14, 17, 32, 35, 38, 23, 26, 29]), new Set([21, 24, 27, 12, 15, 18, 33, 36, 39]), new Set([21, 24, 27, 32, 35, 38, 13, 16, 19]), new Set([31, 34, 37, 22, 25, 28, 13, 16, 19]), new Set([31, 34, 37, 12, 15, 18, 23, 26, 29])]
      for (let [index, case_] of ZHL_case_list.entries()) {
        let ZHL_set = new Set()
        for (let i of player_tiles.hand_tiles) {
          if ((containsIn(case_, i))) {
            ZHL_set.add(i)
          }
        }
        if (((ZHL_set).size === 9)) {
          let temp_player_tiles = player_tiles.deepCopy()
          temp_player_tiles.complete_step += 9
          temp_player_tiles.combination_list.push(`z${index}`)
          temp_player_tiles.fan_list.push("zuhelong")
          for (let i of case_) {
            removeFirst(temp_player_tiles.hand_tiles, i)
          }
          player_tiles_list.push(temp_player_tiles)
          return false
        }
      }
    } else {
      return false
    }
  }
  
  normal_check(player_tiles: PlayerTiles, check_done_list: any[]) {
    this.debug_print("player_tiles:", player_tiles.hand_tiles, player_tiles.complete_step, player_tiles.combination_list)
    if ((player_tiles.complete_step === 14)) {
      check_done_list.push(player_tiles)
      return
    } else if ((player_tiles.complete_step === 0)) {
      if ((!this.normal_check_block(player_tiles))) {
        return
      }
    }
    let all_list = this.normal_check_traverse_quetou(player_tiles)
    let end_list = []
    this.debug_print("所有雀头可能", [...all_list].map((i) => (i.hand_tiles)))
    let count_count = 0
    while (all_list.length > 0) {
      count_count += 1
      let temp_list = all_list.pop()
      this.normal_check_traverse_kezi(temp_list, all_list)
      this.normal_check_traverse_dazi(temp_list, all_list)
      if ((temp_list.complete_step === 14)) {
        end_list.push(temp_list)
      }
    }
    this.debug_print("计算次数：", count_count)
    let combination_class = null
    let temp_list = []
    for (let i of end_list) {
      i.combination_list.sort((a: any, b: any) => (a > b ? 1 : a < b ? -1 : 0))
      if (combination_class == null || !arraysEqual(i.combination_list, combination_class)) {
        combination_class = i.combination_list
        temp_list.push(i)
      }
    }
    end_list = temp_list
    this.debug_print("和牌类型的数量:", (end_list).length)
    for (let i of end_list) {
      this.debug_print("手牌", i.hand_tiles, "胡牌步数", i.complete_step, "胡牌组合", i.combination_list)
    }
    check_done_list.push(...end_list)
  }
  
  normal_check_block(player_tiles: PlayerTiles) {
    let block_count = (player_tiles.combination_list).length
    let tile_id_pointer = player_tiles.hand_tiles[0]
    for (const tile_id of player_tiles.hand_tiles) {
      if (((tile_id === tile_id_pointer) || (tile_id === (tile_id_pointer + 1)))) {
        // pass
      } else {
        block_count += 1
      }
      tile_id_pointer = tile_id
    }
    if ((block_count > 6)) {
      return false
    } else {
      return true
    }
  }
  
  normal_check_traverse_quetou(player_tiles: PlayerTiles) {
    let all_list = []
    let quetou_id_pointer = 0
    for (const tile_id of player_tiles.hand_tiles) {
      countOccurrences(player_tiles.hand_tiles, tile_id)
      if (((countOccurrences(player_tiles.hand_tiles, tile_id) >= 2) && (tile_id !== quetou_id_pointer))) {
        let temp_list = player_tiles.deepCopy()
        removeFirst(temp_list.hand_tiles, tile_id)
        removeFirst(temp_list.hand_tiles, tile_id)
        temp_list.complete_step += 2
        temp_list.combination_list.push(`q${tile_id}`)
        all_list.push(temp_list)
        quetou_id_pointer = tile_id
      }
    }
    let temp_list = player_tiles.deepCopy()
    all_list.push(temp_list)
    return all_list
  }
  
  normal_check_traverse_kezi(player_tiles: PlayerTiles, all_list: any[]) {
    let same_tile_id = 0
    for (const tile_id of player_tiles.hand_tiles) {
      if (((countOccurrences(player_tiles.hand_tiles, tile_id) >= 3) && (tile_id !== same_tile_id))) {
        let temp_list = player_tiles.deepCopy()
        removeFirst(temp_list.hand_tiles, tile_id)
        removeFirst(temp_list.hand_tiles, tile_id)
        removeFirst(temp_list.hand_tiles, tile_id)
        temp_list.complete_step += 3
        temp_list.combination_list.push(`K${tile_id}`)
        all_list.push(temp_list)
        same_tile_id = tile_id
      }
    }
  }
  
  normal_check_traverse_dazi(player_tiles: PlayerTiles, all_list: any[]) {
    let same_tile_id = 0
    for (const tile_id of player_tiles.hand_tiles) {
      if ((tile_id <= 40)) {
        if (((containsIn(player_tiles.hand_tiles, (tile_id + 1))) && (containsIn(player_tiles.hand_tiles, (tile_id + 2))) && (tile_id !== same_tile_id))) {
          let temp_list = player_tiles.deepCopy()
          removeFirst(temp_list.hand_tiles, tile_id)
          removeFirst(temp_list.hand_tiles, (tile_id + 1))
          removeFirst(temp_list.hand_tiles, (tile_id + 2))
          temp_list.complete_step += 3
          temp_list.combination_list.push(`S${(tile_id + 1)}`)
          all_list.push(temp_list)
          same_tile_id = tile_id
        }
      }
    }
  }
  
  fan_count_hand_check(player_tiles: PlayerTiles, hand_tiles_list: number[], get_tile: number) {
    this.debug_print("手牌", hand_tiles_list)
    if ((hand_tiles_list.length === 0)) {
      return
    }
    if (([...hand_tiles_list].every((i) => ((containsIn(Chinese_Hepai_Check.duanyao_set, i)))))) {
      player_tiles.fan_list.push("duanyao")
      if (([...hand_tiles_list].every((i) => ((containsIn(Chinese_Hepai_Check.quanzhong_set, i)))))) {
        player_tiles.fan_list.push("quanzhong")
      }
    }
    if ((([...hand_tiles_list].every((i) => ((containsIn((Chinese_Hepai_Check.wan_set | Chinese_Hepai_Check.zipai_set), i))))) || ([...hand_tiles_list].every((i) => ((containsIn((Chinese_Hepai_Check.bing_set | Chinese_Hepai_Check.zipai_set), i))))) || ([...hand_tiles_list].every((i) => ((containsIn((Chinese_Hepai_Check.tiao_set | Chinese_Hepai_Check.zipai_set), i))))))) {
      if ((([...hand_tiles_list].every((i) => ((containsIn(Chinese_Hepai_Check.wan_set, i))))) || ([...hand_tiles_list].every((i) => ((containsIn(Chinese_Hepai_Check.bing_set, i))))) || ([...hand_tiles_list].every((i) => ((containsIn(Chinese_Hepai_Check.tiao_set, i))))))) {
        let temp_tiles_list = (hand_tiles_list instanceof Set ? new Set(hand_tiles_list) : [...hand_tiles_list])
        this.debug_print("temp_tiles_list", temp_tiles_list)
        removeFirst(temp_tiles_list, get_tile)
        let save_list = []
        for (let i of temp_tiles_list) {
          let rank = (i % 10)
          save_list.push(rank)
        }
        this.debug_print(save_list)
        if (((player_tiles.initial_combination_count === 0) && arraysEqual(save_list, Chinese_Hepai_Check.jiulianbaodeng_list))) {
          player_tiles.fan_list.push("jiulianbaodeng")
        } else {
          player_tiles.fan_list.push("qingyise")
        }
      }
      if (([...hand_tiles_list].every((i) => ((containsIn(Chinese_Hepai_Check.lvyise_set, i)))))) {
        player_tiles.fan_list.push("lvyise")
      } else if (([...hand_tiles_list].every((i) => ((containsIn(Chinese_Hepai_Check.zipai_set, i)))))) {
        player_tiles.fan_list.push("ziyise")
      } else if (([...hand_tiles_list].some((i) => ((containsIn(Chinese_Hepai_Check.zipai_set, i)))))) {
        player_tiles.fan_list.push("hunyise")
      }
    }
    if ((!containsIn(player_tiles.fan_list, "ziyise"))) {
      if (([...hand_tiles_list].every((i) => ((containsIn(Chinese_Hepai_Check.hunyaojiu_set, i)))))) {
        if (([...hand_tiles_list].every((i) => ((containsIn(Chinese_Hepai_Check.qingyaojiu_set, i)))))) {
          player_tiles.fan_list.push("qingyaojiu")
        } else {
          player_tiles.fan_list.push("hunyaojiu")
        }
      }
    }
    if (([...hand_tiles_list].every((i) => ((containsIn(Chinese_Hepai_Check.dayuwu_set, i)))))) {
      if (([...hand_tiles_list].every((i) => ((containsIn(Chinese_Hepai_Check.quanda_set, i)))))) {
        player_tiles.fan_list.push("quanda")
      } else {
        player_tiles.fan_list.push("dayuwu")
      }
    } else if (([...hand_tiles_list].every((i) => ((containsIn(Chinese_Hepai_Check.xiaoyuwu_set, i)))))) {
      if (([...hand_tiles_list].every((i) => ((containsIn(Chinese_Hepai_Check.quanxiao_set, i)))))) {
        player_tiles.fan_list.push("quanxiao")
      } else {
        player_tiles.fan_list.push("xiaoyuwu")
      }
    }
    let suit_count = 0
    for (const suit_set of [Chinese_Hepai_Check.wan_set, Chinese_Hepai_Check.bing_set, Chinese_Hepai_Check.tiao_set]) {
      if (([...hand_tiles_list].some((i) => ((containsIn(suit_set, i)))))) {
        suit_count += 1
      }
    }
    if ((suit_count === 2)) {
      player_tiles.fan_list.push("queyimen")
    }
    if (([...hand_tiles_list].every((i) => ((!containsIn(Chinese_Hepai_Check.zipai_set, i)))))) {
      player_tiles.fan_list.push("wuzi")
    }
    if (([...hand_tiles_list].every((i) => ((containsIn(Chinese_Hepai_Check.tuibudao_set, i)))))) {
      player_tiles.fan_list.push("tuibudao")
    }
    let count_pointer = 0
    for (let i of hand_tiles_list) {
      if ((countOccurrences(hand_tiles_list, i) === 4)) {
        if (((!(containsIn(player_tiles.combination_list, new Set([`g${i}`, `G${i}`])))) && (count_pointer !== i))) {
          count_pointer = i
          player_tiles.fan_list.push("siguiyi")
        }
      }
    }
    if (([...hand_tiles_list].some((i) => ((containsIn(Chinese_Hepai_Check.zhongbaifa_set, i)))))) {
      if (([...hand_tiles_list].some((i) => ((containsIn(Chinese_Hepai_Check.feng_set, i)))))) {
        if (([...hand_tiles_list].some((i) => ((containsIn(Chinese_Hepai_Check.wan_set, i)))))) {
          if (([...hand_tiles_list].some((i) => ((containsIn(Chinese_Hepai_Check.bing_set, i)))))) {
            if (([...hand_tiles_list].some((i) => ((containsIn(Chinese_Hepai_Check.tiao_set, i)))))) {
              player_tiles.fan_list.push("wumenqi")
            }
          }
        }
      }
    }
  }
  
  fan_count_combination_check(player_tiles: PlayerTiles) {
    if (arraysEqual(player_tiles.combination_list, [])) {
      return
    }
    if (([...player_tiles.combination_list].every((i) => ((containsIn(Chinese_Hepai_Check.quandaiwu_set, i)))))) {
      player_tiles.fan_list.push("quandaiwu")
    }
    if (([...player_tiles.combination_list].every((i) => ((containsIn(Chinese_Hepai_Check.quandaiyao_set, i)))))) {
      player_tiles.fan_list.push("quandaiyao")
    }
    let jianke_count = 0
    let jianke_quetou = false
    for (let i of player_tiles.combination_list) {
      if ((containsIn(Chinese_Hepai_Check.jianke_set, i))) {
        jianke_count += 1
      }
      if ((containsIn(Chinese_Hepai_Check.jianke_quetou_set, i))) {
        jianke_quetou = true
      }
    }
    if ((jianke_count === 1)) {
      player_tiles.fan_list.push("jianke")
    }
    if ((jianke_count === 2)) {
      if (jianke_quetou) {
        player_tiles.fan_list.push("xiaosanyuan")
      } else {
        player_tiles.fan_list.push("shuangjianke")
      }
    }
    if ((jianke_count === 3)) {
      player_tiles.fan_list.push("dasanyuan")
    }
    let fengke_count = 0
    let fengke_quetou = false
    for (let i of player_tiles.combination_list) {
      if ((containsIn(Chinese_Hepai_Check.fengke_set, i))) {
        fengke_count += 1
      }
      if ((containsIn(Chinese_Hepai_Check.fengke_quetou_set, i))) {
        fengke_quetou = true
      }
    }
    if ((fengke_count === 3)) {
      if (fengke_quetou) {
        player_tiles.fan_list.push("xiaosixi")
      } else {
        player_tiles.fan_list.push("sanfengke")
      }
    } else if ((fengke_count === 4)) {
      player_tiles.fan_list.push("dasixi")
    }
    let yaojiuke_count = 0
    for (let i of player_tiles.combination_list) {
      if ((containsIn(Chinese_Hepai_Check.yaojiuke_set, i))) {
        yaojiuke_count += 1
        player_tiles.fan_list.push("yaojiuke")
      }
    }
  }
  
  fan_count_combination_str_check(player_tiles: PlayerTiles, combination_str: string, hand_tiles_list: number[]) {
    if ((combination_str === "")) {
      return
    }
    if ((((containsIn(combination_str, "z")) && ((countOccurrences(combination_str, "s") + countOccurrences(combination_str, "S")) === 1)) || ((countOccurrences(combination_str, "s") + countOccurrences(combination_str, "S")) === 4))) {
      if (([...hand_tiles_list].every((i) => ((i <= 40))))) {
        player_tiles.fan_list.push("pinghe")
      }
    }
    if (((countOccurrences(combination_str, "G") + countOccurrences(combination_str, "g")) === 4)) {
      player_tiles.fan_list.push("sigang")
    } else if (((countOccurrences(combination_str, "G") + countOccurrences(combination_str, "g")) === 3)) {
      player_tiles.fan_list.push("sangang")
    } else if ((countOccurrences(combination_str, "G") === 2)) {
      player_tiles.fan_list.push("shuangangang")
    } else if ((countOccurrences(combination_str, "g") === 2)) {
      player_tiles.fan_list.push("shuangminggang")
    } else if (((countOccurrences(combination_str, "g") === 1) && (countOccurrences(combination_str, "G") === 1))) {
      player_tiles.fan_list.push("mingangang")
    } else if ((countOccurrences(combination_str, "G") === 1)) {
      player_tiles.fan_list.push("angang")
    } else if ((countOccurrences(combination_str, "g") === 1)) {
      player_tiles.fan_list.push("minggang")
    }
    if (((countOccurrences(combination_str, "G") + countOccurrences(combination_str, "K")) === 4)) {
      player_tiles.fan_list.push("sianke")
    } else if (((countOccurrences(combination_str, "G") + countOccurrences(combination_str, "K")) === 3)) {
      player_tiles.fan_list.push("sananke")
    } else if (((countOccurrences(combination_str, "G") + countOccurrences(combination_str, "K")) === 2)) {
      player_tiles.fan_list.push("shuanganke")
    }
    if (((((countOccurrences(combination_str, "G") + countOccurrences(combination_str, "g")) + countOccurrences(combination_str, "K")) + countOccurrences(combination_str, "k")) === 4)) {
      player_tiles.fan_list.push("pengpenghe")
    }
  }
  
  fan_count_combination_sign_check(player_tiles: PlayerTiles, combination_str: string, way_to_hepai: string[]) {
    if ((combination_str === "")) {
      return
    }
    let save_dazi_sign = []
    let save_kezi_sign = []
    let save_quetou_sign = []
    for (let [index, tile_id] of [...combination_str].entries()) {
      if (((tile_id === "s") || (tile_id === "S"))) {
        save_dazi_sign.push((combination_str[(index + 1)] + combination_str[(index + 2)]))
      } else if (((tile_id === "k") || (tile_id === "K") || (tile_id === "g") || (tile_id === "G"))) {
        save_kezi_sign.push((combination_str[(index + 1)] + combination_str[(index + 2)]))
      } else if ((tile_id === "q")) {
        save_quetou_sign.push((combination_str[(index + 1)] + combination_str[(index + 2)]))
      }
    }
    save_dazi_sign.sort((a: any, b: any) => (a > b ? 1 : a < b ? -1 : 0))
    save_kezi_sign.sort((a: any, b: any) => (a > b ? 1 : a < b ? -1 : 0))
    this.debug_print("搭子标记：", save_dazi_sign)
    this.debug_print("刻子标记：", save_kezi_sign)
    if (((save_dazi_sign).length >= 2)) {
      let sign_pointer = Number(save_dazi_sign[0])
      let sign_count = 1
      for (let sign of save_dazi_sign) {
        if ((Number(sign) === (sign_pointer + 1))) {
          sign_count += 1
          sign_pointer = Number(sign)
        } else if ((Number(sign) === sign_pointer)) {
          // pass
        } else if ((sign_count <= 2)) {
          sign_count = 1
          sign_pointer = Number(sign)
        }
      }
      if ((sign_count === 3)) {
        player_tiles.fan_list.push("yisesanbugao")
      } else if ((sign_count === 4)) {
        player_tiles.fan_list.push("yisesibugao")
      }
      sign_pointer = Number(save_dazi_sign[0])
      sign_count = 1
      for (let sign of save_dazi_sign) {
        if ((Number(sign) === (sign_pointer + 2))) {
          sign_count += 1
          sign_pointer = Number(sign)
        } else if ((Number(sign) === sign_pointer)) {
          // pass
        } else if ((sign_count <= 2)) {
          sign_count = 1
          sign_pointer = Number(sign)
        }
      }
      if ((sign_count === 3)) {
        player_tiles.fan_list.push("yisesanbugao")
      } else if ((sign_count === 4)) {
        player_tiles.fan_list.push("yisesibugao")
      }
      let already_count = 0
      for (let i of save_dazi_sign) {
        if ((i !== already_count)) {
          if ((countOccurrences(save_dazi_sign, i) === 2)) {
            player_tiles.fan_list.push("yibangao")
          } else if ((countOccurrences(save_dazi_sign, i) === 3)) {
            player_tiles.fan_list.push("yisesantongshun")
          } else if ((countOccurrences(save_dazi_sign, i) === 4)) {
            player_tiles.fan_list.push("yisesitongshun")
          }
          already_count = i
        }
      }
      let sanseshuanglonghui_list = [new Set(["12", "18", "22", "28", "q35"]), new Set(["12", "18", "32", "38", "q25"]), new Set(["32", "38", "22", "28", "q15"])]
      for (const set of sanseshuanglonghui_list) {
        let shunzi_in_set = [...set].filter((i) => ((!i.startsWith("q")))).map((i) => (i))
        let quetou_in_set = [...set].filter((i) => (i.startsWith("q"))).map((i) => (i))
        if (([...shunzi_in_set].every((i) => ((containsIn(save_dazi_sign, i)))))) {
          if ((quetou_in_set && (containsIn(quetou_in_set, `q${save_quetou_sign[0]}`)))) {
            player_tiles.fan_list.push("sanseshuanglonghui")
            break
          }
        }
      }
      let wan_list = []
      let bing_list = []
      let tiao_list = []
      let all_list = []
      for (let sign of save_dazi_sign) {
        if ((sign[0] === "1")) {
          wan_list.push(sign[1])
          all_list.push(sign[1])
        } else if ((sign[0] === "2")) {
          bing_list.push(sign[1])
          all_list.push(sign[1])
        } else if ((sign[0] === "3")) {
          tiao_list.push(sign[1])
          all_list.push(sign[1])
        }
      }
      let suit_list = [wan_list, bing_list, tiao_list]
      for (const rank_list of suit_list) {
        if (((rank_list).length >= 3)) {
          if (([...rank_list].some((i) => ((i === "2"))))) {
            if (([...rank_list].some((i) => ((i === "5"))))) {
              if (([...rank_list].some((i) => ((i === "8"))))) {
                player_tiles.fan_list.push("qinglong")
                break
              }
            }
          }
        }
      }
      let hualong_form_list = [["2", "5", "8"], ["2", "8", "5"], ["5", "2", "8"], ["5", "8", "2"], ["8", "2", "5"], ["8", "5", "2"]]
      for (const form of hualong_form_list) {
        if ((containsIn(wan_list, form[0]))) {
          if ((containsIn(bing_list, form[1]))) {
            if ((containsIn(tiao_list, form[2]))) {
              player_tiles.fan_list.push("hualong")
              break
            }
          }
        }
      }
      let order_kind_list = [0, 1, 2]
      let counted_pointer_list = []
      for (const order of order_kind_list) {
        if ((order === 0)) {
          for (let i of suit_list[0]) {
            if ((containsIn(suit_list[1], i))) {
              if ((containsIn(suit_list[2], i))) {
                player_tiles.fan_list.push("sansesantongshun")
                break
              }
            }
          }
          for (let i of suit_list[0]) {
            i = Number(i)
            this.debug_print(i)
            if ((containsIn(suit_list[1], String((i + 1))))) {
              if ((containsIn(suit_list[2], String((i + 2))))) {
                player_tiles.fan_list.push("sansesanbugao")
                break
              }
              if ((containsIn(suit_list[2], String((i - 1))))) {
                player_tiles.fan_list.push("sansesanbugao")
                break
              }
            }
            if ((containsIn(suit_list[1], String((i - 1))))) {
              if ((containsIn(suit_list[2], String((i - 2))))) {
                player_tiles.fan_list.push("sansesanbugao")
                break
              }
              if ((containsIn(suit_list[2], String((i + 1))))) {
                player_tiles.fan_list.push("sansesanbugao")
                break
              }
            }
            if ((containsIn(suit_list[2], String((i + 1))))) {
              if ((containsIn(suit_list[1], String((i + 2))))) {
                player_tiles.fan_list.push("sansesanbugao")
                break
              }
              if ((containsIn(suit_list[1], String((i - 1))))) {
                player_tiles.fan_list.push("sansesanbugao")
                break
              }
            }
            if ((containsIn(suit_list[2], String((i - 1))))) {
              if ((containsIn(suit_list[1], String((i - 2))))) {
                player_tiles.fan_list.push("sansesanbugao")
                break
              }
              if ((containsIn(suit_list[1], String((i + 1))))) {
                player_tiles.fan_list.push("sansesanbugao")
                break
              }
            }
          }
          for (let i of suit_list[0]) {
            if ((((containsIn(suit_list[1], i)) || (containsIn(suit_list[2], i))) && (!containsIn(counted_pointer_list, i)))) {
              counted_pointer_list.push(i)
              player_tiles.fan_list.push("xixiangfeng")
            }
          }
        } else if ((order === 1)) {
          for (let i of suit_list[1]) {
            if ((((containsIn(suit_list[0], i)) || (containsIn(suit_list[2], i))) && (!containsIn(counted_pointer_list, i)))) {
              counted_pointer_list.push(i)
              player_tiles.fan_list.push("xixiangfeng")
            }
          }
        } else if ((order === 2)) {
          for (let i of suit_list[2]) {
            if ((((containsIn(suit_list[0], i)) || (containsIn(suit_list[1], i))) && (!containsIn(counted_pointer_list, i)))) {
              counted_pointer_list.push(i)
              player_tiles.fan_list.push("xixiangfeng")
            }
          }
        }
      }
      for (const list of [wan_list, bing_list, tiao_list]) {
        if (((list).length >= 2)) {
          for (let rank = 1; rank < 7; rank++) {
            let pair_count = Math.min(countOccurrences(list, String(rank)), countOccurrences(list, String((rank + 3))))
            for (let _ = 0; _ < pair_count; _++) {
              player_tiles.fan_list.push("lianliu")
            }
          }
        }
        let min_count = Math.min(countOccurrences(list, "2"), countOccurrences(list, "8"))
        if ((min_count !== 0)) {
          if (((min_count === 2) && (containsIn(player_tiles.fan_list, "qingyise")) && ((Number(save_quetou_sign[0]) % 10) === 5))) {
            player_tiles.fan_list.push("yiseshuanglonghui")
          } else {
            for (let i = 0; i < min_count; i++) {
              player_tiles.fan_list.push("laoshaofu")
            }
          }
        }
      }
    }
    if (((save_kezi_sign).length >= 2)) {
      let sign_pointer = Number(save_kezi_sign[0])
      let sign_count = 1
      for (let sign of save_kezi_sign) {
        let sign_val = Number(sign)
        if (((sign_val === (sign_pointer + 1)) && (sign_val <= 40))) {
          sign_count += 1
          sign_pointer = sign_val
        } else if ((sign_val === sign_pointer)) {
          // pass
        } else if ((sign_count <= 2)) {
          sign_count = 1
          sign_pointer = sign_val
        }
      }
      if ((sign_count >= 4)) {
        player_tiles.fan_list.push("yisesijiegao")
      } else if ((sign_count >= 3)) {
        player_tiles.fan_list.push("yisesanjiegao")
      }
      let wan_list = []
      let bing_list = []
      let tiao_list = []
      let all_list = []
      for (let sign of save_kezi_sign) {
        if ((sign[0] === "1")) {
          wan_list.push(sign[1])
          all_list.push(sign[1])
        } else if ((sign[0] === "2")) {
          bing_list.push(sign[1])
          all_list.push(sign[1])
        } else if ((sign[0] === "3")) {
          tiao_list.push(sign[1])
          all_list.push(sign[1])
        }
      }
      if (((all_list).length === 4)) {
        if (([...all_list].every((i) => ((containsIn(["2", "4", "6", "8"], i)))))) {
          if (save_quetou_sign.length > 0) {
            let quetou_id = Number(save_quetou_sign[0])
            if (((quetou_id < 40) && (containsIn([2, 4, 6, 8], (quetou_id % 10))))) {
              player_tiles.fan_list.push("quanshuangke")
            }
          }
        }
      }
      let already_count_list = []
      this.debug_print(all_list)
      for (let rank of all_list) {
        if (((countOccurrences(all_list, rank) >= 2) && (!containsIn(already_count_list, rank)))) {
          already_count_list.push(rank)
          if ((countOccurrences(all_list, rank) === 3)) {
            player_tiles.fan_list.push("santongke")
          } else if ((countOccurrences(all_list, rank) === 2)) {
            player_tiles.fan_list.push("shuangtongke")
          }
        }
      }
      this.debug_print(wan_list, bing_list, tiao_list)
      for (let i of wan_list) {
        if ((containsIn(bing_list, String((Number(i) + 1))))) {
          if ((containsIn(tiao_list, String((Number(i) + 2))))) {
            player_tiles.fan_list.push("sansesanjiegao")
            break
          }
          if ((containsIn(tiao_list, String((Number(i) - 1))))) {
            player_tiles.fan_list.push("sansesanjiegao")
            break
          }
        }
        if ((containsIn(bing_list, String((Number(i) - 1))))) {
          if ((containsIn(tiao_list, String((Number(i) - 2))))) {
            player_tiles.fan_list.push("sansesanjiegao")
            break
          }
          if ((containsIn(tiao_list, String((Number(i) + 1))))) {
            player_tiles.fan_list.push("sansesanjiegao")
            break
          }
        }
        if ((containsIn(tiao_list, String((Number(i) + 1))))) {
          if ((containsIn(bing_list, String((Number(i) + 2))))) {
            player_tiles.fan_list.push("sansesanjiegao")
            break
          }
          if ((containsIn(bing_list, String((Number(i) - 1))))) {
            player_tiles.fan_list.push("sansesanjiegao")
            break
          }
        }
        if ((containsIn(tiao_list, String((Number(i) - 1))))) {
          if ((containsIn(bing_list, String((Number(i) - 2))))) {
            player_tiles.fan_list.push("sansesanjiegao")
            break
          }
          if ((containsIn(bing_list, String((Number(i) + 1))))) {
            player_tiles.fan_list.push("sansesanjiegao")
            break
          }
        }
      }
    }
    let menfeng = "None"
    if ((containsIn(way_to_hepai, "自风东"))) {
      menfeng = "41"
    } else if ((containsIn(way_to_hepai, "自风南"))) {
      menfeng = "42"
    } else if ((containsIn(way_to_hepai, "自风西"))) {
      menfeng = "43"
    } else if ((containsIn(way_to_hepai, "自风北"))) {
      menfeng = "44"
    }
    let changfeng = "null"
    if ((containsIn(way_to_hepai, "场风东"))) {
      changfeng = "41"
    } else if ((containsIn(way_to_hepai, "场风南"))) {
      changfeng = "42"
    } else if ((containsIn(way_to_hepai, "场风西"))) {
      changfeng = "43"
    } else if ((containsIn(way_to_hepai, "场风北"))) {
      changfeng = "44"
    }
    if ((containsIn(save_kezi_sign, menfeng))) {
      player_tiles.fan_list.push("menfengke")
    }
    if ((containsIn(save_kezi_sign, changfeng))) {
      player_tiles.fan_list.push("quanfengke")
    }
    if ((menfeng === changfeng)) {
      way_to_hepai.push("门风圈风相同")
    }
  }
  
  fan_count_hepai_relationship_check(
    player_tiles: PlayerTiles,
    combination_str: string,
    get_tile: number,
    way_to_hepai: string[],
  ) {
    for (const i of way_to_hepai) {
      switch (i) {
        case '和单张': {
          if (get_tile % 10 === 3) {
            if (player_tiles.combination_list.includes(`S${get_tile - 1}`)) {
              player_tiles.fan_list.push('bianzhang')
              continue
            }
          } else if (get_tile % 10 === 7) {
            if (player_tiles.combination_list.includes(`S${get_tile + 1}`)) {
              player_tiles.fan_list.push('bianzhang')
              continue
            }
          }
          if (player_tiles.combination_list.includes(`S${get_tile}`)) {
            player_tiles.fan_list.push('qianzhang')
            continue
          }
          if (player_tiles.combination_list.includes(`q${get_tile}`)) {
            player_tiles.fan_list.push('dandiaojiang')
            continue
          }
          break
        }
        case 'last_deal':
        case '妙手回春':
          player_tiles.fan_list.push('miaoshouhuichun')
          break
        case '杠上开花':
          player_tiles.fan_list.push('gangshangkaihua')
          break
        case '抢杠和':
          player_tiles.fan_list.push('qiangganghe')
          break
        case '和绝张':
          player_tiles.fan_list.push('hejuezhang')
          break
        case '花牌':
          player_tiles.fan_list.push('huapai')
          break
        case 'last_cut':
        case '海底捞月':
          player_tiles.fan_list.push('haidilaoyue')
          break
        case '点和': {
          this.debug_print(player_tiles.combination_list.join(','))
          const small_count = countOccurrences(combination_str, 's')
            + countOccurrences(combination_str, 'k')
            + countOccurrences(combination_str, 'g')
          if (
            combination_str.length > 0 &&
            [...combination_str].every((c) => !['S', 'K', 'G', 'z'].includes(c)) &&
            way_to_hepai.includes('和单张')
          ) {
            player_tiles.fan_list.push('quanqiuren')
          } else if (small_count === 0) {
            player_tiles.fan_list.push('menqianqing')
          } else if (way_to_hepai.includes('暗转明')) {
            if (small_count === 1) player_tiles.fan_list.push('menqianqing')
          }
          break
        }
        case '自摸': {
          if ([...combination_str].every((c) => !['s', 'k', 'g'].includes(c))) {
            const special_fans = new Set([
              'qiduizi',
              'jiulianbaodeng',
              'lianqidui',
              'shisanyao',
              'sianke',
              'qixingbukao',
              'quanbukao',
            ])
            if (player_tiles.fan_list.some((f) => special_fans.has(f))) {
              player_tiles.fan_list.push('zimo')
            } else {
              player_tiles.fan_list.push('buqiuren')
            }
          } else {
            player_tiles.fan_list.push('zimo')
          }
          break
        }
      }
    }
  }
  
  fan_count_output(player_tiles: PlayerTiles, combination_str: string, zimo_or_not: boolean, way_to_hepai: string[]): [number, string[]] {
    let remaining = [...player_tiles.fan_list].filter((f) => ((f !== "huapai"))).map((f) => (f))
    if (((remaining.length === 0) || ([...remaining].every((f) => (((this._count_model_dict[f] ?? 0) === 0)))))) {
      player_tiles.fan_list.push("wufanhe")
    }
    let need_to_remove = []
    let max_yaojiuke_count = 0
    for (const fan of player_tiles.fan_list) {
      if ((containsIn(Chinese_Hepai_Check.repel_model_dict, fan))) {
        for (let i of Chinese_Hepai_Check.repel_model_dict[fan]) {
          if ((i !== "yaojiuke")) {
            need_to_remove.push(i)
          } else if ((i === "yaojiuke")) {
            let yaojiuke_count = countOccurrences(Chinese_Hepai_Check.repel_model_dict[fan], "yaojiuke")
            if ((yaojiuke_count > max_yaojiuke_count)) {
              max_yaojiuke_count = yaojiuke_count
            }
          }
        }
      } else if (zimo_or_not) {
        for (let i of Chinese_Hepai_Check.repel_model_dict[`${fan}_zimo`]) {
          if ((i !== "yaojiuke")) {
            need_to_remove.push(i)
          } else if ((i === "yaojiuke")) {
            let yaojiuke_count = countOccurrences(Chinese_Hepai_Check.repel_model_dict[`${fan}_zimo`], "yaojiuke")
            if ((yaojiuke_count > max_yaojiuke_count)) {
              max_yaojiuke_count = yaojiuke_count
            }
          }
        }
      } else {
        for (let i of Chinese_Hepai_Check.repel_model_dict[`${fan}_dianhe`]) {
          if ((i !== "yaojiuke")) {
            need_to_remove.push(i)
          } else if ((i === "yaojiuke")) {
            let yaojiuke_count = countOccurrences(Chinese_Hepai_Check.repel_model_dict[`${fan}_dianhe`], "yaojiuke")
            if ((yaojiuke_count > max_yaojiuke_count)) {
              max_yaojiuke_count = yaojiuke_count
            }
          }
        }
      }
    }
    if ((containsIn(player_tiles.fan_list, "sanfengke"))) {
      need_to_remove.push("yaojiuke")
      need_to_remove.push("yaojiuke")
      need_to_remove.push("yaojiuke")
    } else {
      if ((containsIn(player_tiles.fan_list, "quanfengke"))) {
        need_to_remove.push("yaojiuke")
      }
      if ((containsIn(player_tiles.fan_list, "menfengke"))) {
        if ((!containsIn(way_to_hepai, "门风圈风相同"))) {
          need_to_remove.push("yaojiuke")
        }
      }
    }
    this.debug_print("全部被添加的番种", player_tiles.fan_list)
    player_tiles.fan_list.sort((a: any, b: any) => (a > b ? 1 : a < b ? -1 : 0))
    this.debug_print("需要被阻挡的番种", need_to_remove)
    for (let i of need_to_remove) {
      if ((containsIn(player_tiles.fan_list, i))) {
        removeFirst(player_tiles.fan_list, i)
      }
    }
    this.debug_print("需要移除的幺九刻数量", max_yaojiuke_count)
    for (let i = 0; i < max_yaojiuke_count; i++) {
      if ((containsIn(player_tiles.fan_list, "yaojiuke"))) {
        removeFirst(player_tiles.fan_list, "yaojiuke")
      }
    }
    let repeatable_fan_list = []
    let origin_fan_list = []
    for (let i of player_tiles.fan_list) {
      if ((containsIn(new Set(["yibangao", "xixiangfeng", "lianliu", "laoshaofu"]), i))) {
        repeatable_fan_list.push(i)
      } else {
        origin_fan_list.push(i)
      }
    }
    this.debug_print("重复番种", repeatable_fan_list)
    if (((repeatable_fan_list).length > 0)) {
      if (([...new Set(["yiseshuanglonghui", "yisesitongshun", "yisesibugao", "sanseshuanglonghui"])].some((i) => ((containsIn(player_tiles.fan_list, i)))))) {
        player_tiles.fan_list = origin_fan_list
      } else if ((containsIn(player_tiles.fan_list, "yisesantongshun"))) {
        origin_fan_list.push(repeatable_fan_list[0])
      } else if ((containsIn(player_tiles.fan_list, "sansesanbugao"))) {
        origin_fan_list.push(repeatable_fan_list[0])
      } else if ((containsIn(player_tiles.fan_list, "sansesantongshun"))) {
        for (let i of repeatable_fan_list) {
          if ((containsIn(["yibangao", "lianliu", "laoshaofu"], i))) {
            origin_fan_list.push(i)
            break
          }
        }
      } else if ((containsIn(player_tiles.fan_list, "yisesanbugao"))) {
        origin_fan_list.push(repeatable_fan_list[0])
      } else if ((containsIn(player_tiles.fan_list, "qinglong"))) {
        for (let i of repeatable_fan_list) {
          if ((containsIn(["yibangao", "xixiangfeng"], i))) {
            origin_fan_list.push(i)
            break
          }
        }
      } else if ((containsIn(player_tiles.fan_list, "hualong"))) {
        origin_fan_list.push(repeatable_fan_list[0])
      } else {
        let max_fan_count = ((countOccurrences(combination_str, "s") + countOccurrences(combination_str, "S")) - 1)
        if (((repeatable_fan_list).length <= max_fan_count)) {
          origin_fan_list = origin_fan_list.concat(repeatable_fan_list)
        } else {
          for (let i = 0; i < max_fan_count; i++) {
            origin_fan_list.push(repeatable_fan_list[i])
          }
        }
      }
    }
    player_tiles.fan_list = origin_fan_list
    this.debug_print("最终番种", player_tiles.fan_list)
    if (((player_tiles.fan_list.length === 0) || ([...player_tiles.fan_list].every((f) => (((this._count_model_dict[f] ?? 0) === 0)))))) {
      player_tiles.fan_list = ["wufanhe"]
    }
    let fuji_set = new Set(["siguiyi", "shuangtongke", "yibangao", "xixiangfeng", "lianliu", "yaojiuke", "huapai"])
    let fuji_list = ["siguiyi", "shuangtongke", "yibangao", "xixiangfeng", "lianliu", "yaojiuke", "huapai"]
    let fan_count = 0
    let temp_fan_count_list = []
    for (let i of player_tiles.fan_list) {
      if ((!containsIn(fuji_set, i))) {
        fan_count += this._count_model_dict[i]
        this.debug_print(`添加番数${i},${this._count_model_dict[i]}`)
        temp_fan_count_list.push(`${Chinese_Hepai_Check.eng_to_chinese_dict[i]}`)
      }
    }
    for (let i of fuji_list) {
      if ((containsIn(player_tiles.fan_list, i))) {
        fan_count += (countOccurrences(player_tiles.fan_list, i) * this._count_model_dict[i])
        this.debug_print(`添加番数${i},${(countOccurrences(player_tiles.fan_list, i) * this._count_model_dict[i])}`)
        temp_fan_count_list.push(`${Chinese_Hepai_Check.eng_to_chinese_dict[i]}*${countOccurrences(player_tiles.fan_list, i)}`)
      }
    }
    player_tiles.fan_count_list = temp_fan_count_list
    this.debug_print("和牌文本", player_tiles.fan_count_list)
    this.debug_print("和牌得分", fan_count)
    return [fan_count, player_tiles.fan_count_list]
  }
  
  filter_zero_value_fans(fan_score: any, fan_count_list: any) {
    `
        剔除番值=0 的番种后返回，供外部在获取和牌结果后调用再 return 到服务器/客户端。
        国标标准无 0 值番，直接原样返回。
        `
    return [fan_score, fan_count_list]
  }
  
  fan_count(player_tiles: PlayerTiles, get_tile: number, way_to_hepai: string[]): [number, string[]] {
    let zimo_or_not = false
    if (([...way_to_hepai].some((i) => ((containsIn(["last_deal", "妙手回春", "自摸", "杠上开花"], i)))))) {
      zimo_or_not = true
    }
    if ((zimo_or_not === false)) {
      for (let i of player_tiles.combination_list) {
        if ((i === `G${get_tile}`)) {
          if (([...[`S${get_tile}`, `S${(get_tile + 1)}`, `S${(get_tile - 1)}`]].some((i) => ((containsIn(player_tiles.combination_list, i)))))) {
            // pass
          } else {
            removeFirst(player_tiles.combination_list, i)
            player_tiles.combination_list.push(`g${i[1]}${i[2]}`)
            way_to_hepai.push("暗转明")
            break
          }
        } else if ((i === `K${get_tile}`)) {
          if (([...[`S${get_tile}`, `S${(get_tile + 1)}`, `S${(get_tile - 1)}`]].some((i) => ((containsIn(player_tiles.combination_list, i)))))) {
            // pass
          } else {
            removeFirst(player_tiles.combination_list, i)
            player_tiles.combination_list.push(`k${i[1]}${i[2]}`)
            way_to_hepai.push("暗转明")
            break
          }
        }
      }
    }
    let hand_tiles_list = []
    let combination_str = ""
    if (([...["qiduizi", "lianqidui"]].some((i) => ((containsIn(player_tiles.fan_list, i)))))) {
      hand_tiles_list = player_tiles.hand_tiles
    } else if (([...["quanbukao", "qixingbukao"]].some((i) => ((containsIn(player_tiles.fan_list, i)))))) {
      hand_tiles_list = []
    } else {
      for (let i of player_tiles.combination_list) {
        if ((containsIn(Chinese_Hepai_Check.combination_to_tiles_dict, i))) {
          hand_tiles_list.push(...Chinese_Hepai_Check.combination_to_tiles_dict[i])
        }
        hand_tiles_list.sort((a: any, b: any) => (a > b ? 1 : a < b ? -1 : 0))
      }
    }
    for (let i of player_tiles.combination_list) {
      combination_str += i
    }
    this.debug_print("组合映射：", combination_str)
    this.debug_print("手牌映射：", hand_tiles_list)
    this.fan_count_hand_check(player_tiles, hand_tiles_list, get_tile)
    this.fan_count_combination_check(player_tiles)
    this.fan_count_combination_str_check(player_tiles, combination_str, hand_tiles_list)
    this.fan_count_combination_sign_check(player_tiles, combination_str, way_to_hepai)
    this.fan_count_hepai_relationship_check(player_tiles, combination_str, get_tile, way_to_hepai)
    this.debug_print("现在存在的组合", player_tiles.combination_list)
    let result = this.fan_count_output(player_tiles, combination_str, zimo_or_not, way_to_hepai)
    return result
  }
}


export function hepaiCheck(
  hand: number[],
  combinations: string[],
  wayToHepai: string[],
  getTile: number,
  debug = false,
): HepaiResult {
  const checker = new Chinese_Hepai_Check(debug)
  const [fan, fanNames] = checker.hepai_check(
    [...hand],
    [...combinations],
    [...wayToHepai],
    getTile,
  )
  return { fan, fanNames }
}

export function hepaiCheckXiaolin(
  hand: number[],
  combinations: string[],
  wayToHepai: string[],
  getTile: number,
  debug = false,
): HepaiResult {
  const checker = new Chinese_Hepai_Check(debug, COUNT_MODEL_DICT_XIAOLIN)
  const [fan, fanNames] = checker.hepai_check(
    [...hand],
    [...combinations],
    [...wayToHepai],
    getTile,
  )
  return filterZeroValueFansXiaolin(fan, fanNames)
}

const COUNT_MODEL_DICT_XIAOLIN: Record<string, number> = {
  dasixi: 88, dasanyuan: 64, lvyise: 88, jiulianbaodeng: 88, sigang: 88,
  lianqidui: 88, shisanyao: 88,
  qingyaojiu: 64, xiaosixi: 64, xiaosanyuan: 32, ziyise: 64, sianke: 64, yiseshuanglonghui: 64,
  yisesitongshun: 64, yisesijiegao: 64, yisesibugao: 32, sangang: 32, hunyaojiu: 32,
  qiduizi: 24, qixingbukao: 24, quanshuangke: 24,
  qingyise: 32, yisesantongshun: 24, yisesanjiegao: 24, quanda: 24, quanzhong: 24, quanxiao: 24,
  qinglong: 16, sanseshuanglonghui: 24, yisesanbugao: 16, quandaiwu: 16, santongke: 16, sananke: 16,
  quanbukao: 12, zuhelong: 12, dayuwu: 12, xiaoyuwu: 12, sanfengke: 24,
  hualong: 8, tuibudao: 8, sansesantongshun: 8, sansesanjiegao: 8, wufanhe: 8, miaoshouhuichun: 8, haidilaoyue: 8,
  gangshangkaihua: 8, qiangganghe: 8, pengpenghe: 6, hunyise: 12, sansesanbugao: 6, wumenqi: 6, quanqiuren: 6, shuangangang: 8, shuangjianke: 12,
  quandaiyao: 6, buqiuren: 0, shuangminggang: 4, hejuezhang: 0, jianke: 2, quanfengke: 2, menfengke: 2, menqianqing: 2,
  pinghe: 2, siguiyi: 2, shuangtongke: 2, shuanganke: 2, angang: 2, duanyao: 2, yibangao: 2, xixiangfeng: 1,
  lianliu: 1, laoshaofu: 1, yaojiuke: 0, minggang: 1, queyimen: 1, wuzi: 1, bianzhang: 1,
  qianzhang: 1, dandiaojiang: 1, zimo: 0, huapai: 1, mingangang: 5,
}

function filterZeroValueFansXiaolin(_fanScore: number, fanCountList: string[]): HepaiResult {
  const cnToValue: Record<string, number> = {}
  for (const [eng, cn] of Object.entries(Chinese_Hepai_Check.eng_to_chinese_dict)) {
    if (eng in COUNT_MODEL_DICT_XIAOLIN) cnToValue[cn as string] = COUNT_MODEL_DICT_XIAOLIN[eng]
  }
  const wufanheValue = cnToValue['无番和'] ?? 8
  const filtered: string[] = []
  let effective = 0
  for (const item of fanCountList) {
    let baseCn: string
    let count: number
    if (item != null && item.includes('*')) {
      const p = item.split('*')
      baseCn = p[0].trim()
      count = p.length > 1 && !Number.isNaN(Number(p[1].trim())) ? Number(p[1].trim()) : 1
    } else {
      baseCn = (item ?? '').trim()
      count = 1
    }
    const val = cnToValue[baseCn]
    if (val === 0) continue
    filtered.push(item)
    effective += (val ?? 0) * count
  }
  if (filtered.length === 0 || effective === 0) {
    return { fan: wufanheValue, fanNames: ['无番和'] }
  }
  return { fan: effective, fanNames: filtered }
}

/** Kshen variant stub — delegates to standard hepai for now. */
export function hepaiCheckKshen(
  hand: number[],
  combinations: string[],
  wayToHepai: string[],
  getTile: number,
  debug = false,
): HepaiResult {
  return hepaiCheck(hand, combinations, wayToHepai, getTile, debug)
}
