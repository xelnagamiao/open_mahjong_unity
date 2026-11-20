function finiteNumber(value) {
  if (
    value == null ||
    typeof value === "boolean" ||
    (typeof value === "string" && !value.trim())
  )
    return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

export function rulePoolLabel(value) {
  if (!value) return "未提供规则池";
  const parts = String(value).split("|");
  if (parts.length < 6) return String(value);
  const rule =
    {
      guobiao: "国标",
      riichi: "立直",
      qingque: "青雀",
      sichuan: "四川",
      classical: "古典",
      changsha: "长沙",
      hongque: "红雀",
      hongzhong: "红中",
      wuhan: "武汉",
      shanxi: "山西",
      fuzhou: "福州",
      nanchang: "南昌",
      changchun: "长春",
      guangdong: "广东",
    }[parts[0]] || parts[0];
  const room =
    { match: "排位", custom: "自建", event: "赛事", events: "赛事" }[
      parts[3]
    ] || parts[3];
  const mode =
    {
      quanzhuang: "全庄",
      banzhuang: "半庄",
      dongfeng: "东风",
      "1/4": "东风",
      "1/4_rank": "东风",
      "2/4": "半庄",
      "2/4_rank": "半庄",
      "1/4_sanma_rank": "东风",
      "2/4_sanma_rank": "半庄",
      "1/3_rank": "东风",
      "2/3_rank": "半庄",
      "3/4": "西风",
      "4/4": "全庄",
      "4/4_rank": "全庄",
      "unlimited/4_rank": "无限",
    }[parts[4]] || parts[4];
  const tier =
    {
      beginner: "初级",
      intermediate: "中级",
      advanced: "高级",
      expert: "专家",
      mcrpl: "MCRPL",
      elo: "评级",
      unknown: "档位未记录",
    }[parts[5]] || parts[5];
  const variant = {
    doubles: "双打",
    doubles_duplicate: "复式双打",
    sanma: "三麻",
    washizu: "鹫巢",
    washizu_doubles: "鹫巢双打",
  }[parts[1].split("/").at(-1)];
  return `${rule}${variant ? `·${variant}` : ""} · ${parts[2]} 人 · ${room} · ${mode} · ${tier}`;
}

export function formatDate(value) {
  if (!value) return "—";
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? String(value)
    : date.toLocaleString("zh-CN", {
        timeZone: "Asia/Shanghai",
        hour12: false,
      });
}

export function evidenceReplayPath(evidence, donorId) {
  if (!evidence?.game_id) return "";
  const query = new URLSearchParams();
  const hand = finiteNumber(evidence.hand_index);
  const tick = finiteNumber(evidence.source_tick_index ?? evidence.tick_index);
  if (Number.isInteger(hand) && hand >= 1) query.set("round", String(hand));
  if (Number.isInteger(tick) && tick >= 0) query.set("node", String(tick));
  if (donorId != null && String(donorId)) query.set("player", String(donorId));
  return `/2d/record/${encodeURIComponent(evidence.game_id)}${query.size ? `?${query}` : ""}`;
}
