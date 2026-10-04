import { displayHongKongProfile } from '../utils/hongKongRoomConfig.js'

// Each profile links directly to its original rulebook.
export const hongKongRulebooks = [
  {
    subRule: 'hongkong/qingzhang', label: '清章十三',
    desc: '香港麻雀协会《香港麻雀总例》。',
    url: 'https://docs.google.com/document/d/1TgdYpE_5Qiht_lBco3BqN_ijZJDRWugwzRzv946raq0/edit',
    filename: null, downloadable: false, readLabel: '阅读协会原文',
  },
  {
    subRule: 'hongkong/new13_gametower', label: '新章十三（Wiki）',
    desc: 'Mahjong Wiki 广东十三张计分页的 IGS 分栏。',
    url: 'https://mahjong.wikidot.com/rules:guangdong-style-scoring',
    filename: null, downloadable: false, readLabel: '阅读 Mahjong Wiki（IGS）',
    readHint: '打开后请选择 IGS 分栏；默认的 Avg 是多个版本的汇总。',
  },
  {
    subRule: 'hongkong/new13_lianhuise', label: '新章十三（恋绘色）',
    desc: '恋绘色《香港新章规则书》。',
    url: '/rulebooks/lianhuise-new13.docx', filename: '香港新章规则书.docx',
  },
  {
    subRule: 'hongkong/qingzhang_lianhuise', label: '新章十三（恋绘色魔改）',
    desc: '《香港新派清章（恋绘色魔改版）》原文。',
    url: '/rulebooks/lianhuise-qingzhang-remix.docx', filename: '香港新派清章（恋绘色魔改版）.docx',
  },
  {
    subRule: 'hongkong/new16', label: '新章十六',
    desc: '香港麻雀协会《港式十六张新章麻雀总例》详述版。',
    url: 'https://docs.google.com/document/d/11WYyoWnJqe5eYpx-7DHE3OpvSRBwQd2NVcN9Ukcohyc/edit',
    filename: null, downloadable: false, readLabel: '阅读协会原文',
  },
].map(profile => ({ title: `${profile.label}规则书`, ...profile }))

export function hongKongRulebook(form = {}) {
  const subRule = displayHongKongProfile(form)
  return hongKongRulebooks.find(profile => profile.subRule === subRule) || hongKongRulebooks[0]
}
