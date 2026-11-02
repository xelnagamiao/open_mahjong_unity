const hint = '改名成功时自动消耗 1 张改名卡，无需提前在背包中使用。'
export const renameCardMessages = {
  en: {
    '改名卡': 'Rename Cards',
    '持有 {count} 张': '{count} cards held',
    '（可用 {count} 张）': ' ({count} usable)',
    '暂无改名卡': 'No rename cards available',
    '改名卡暂不可用': 'Your rename cards are temporarily unavailable',
    '改名卡不足或暂不可用': 'No usable rename cards available',
    [hint]: 'A successful rename automatically consumes one rename card. No need to use it in your inventory first.',
  },
  ja: {
    '改名卡': '改名カード',
    '持有 {count} 张': '{count}枚所持',
    '（可用 {count} 张）': '（使用可能：{count}枚）',
    '暂无改名卡': '改名カードがありません',
    '改名卡暂不可用': '現在、改名カードは使用できません',
    '改名卡不足或暂不可用': '使用できる改名カードがありません',
    [hint]: '改名が成功すると改名カードを1枚消費します。事前に倉庫で使用する必要はありません。',
  },
  fr: {
    '改名卡': 'Cartes de changement de nom',
    '持有 {count} 张': '{count} cartes détenues',
    '（可用 {count} 张）': ' ({count} utilisables)',
    '暂无改名卡': 'Aucune carte de changement de nom disponible',
    '改名卡暂不可用': 'Vos cartes sont temporairement indisponibles',
    '改名卡不足或暂不可用': 'Aucune carte utilisable disponible',
    [hint]: "Un changement de nom réussi consomme automatiquement une carte. Il n'est pas nécessaire de l'utiliser dans l'inventaire au préalable.",
  },
}

export function renameCardTextPatterns(language) {
  const confirm = {
    en: name => `Change your username to “${name}”? A successful rename consumes one rename card and updates your name in all historical records. This cannot be undone.`,
    ja: name => `ユーザー名を「${name}」に変更しますか？成功すると改名カードを1枚消費し、過去の牌譜の名前も更新します。この操作は取り消せません。`,
    fr: name => `Changer votre nom en « ${name} » ? Un changement réussi consomme une carte et met à jour votre nom dans toutes les parties passées. Cette opération est irréversible.`,
  }
  return [
    { match: /^确定将用户名改为「(.+)」？改名成功时将消耗 1 张改名卡，并追溯更新该账号全部历史牌谱中的名字。此操作不可撤销。$/, replace: confirm[language] },
  ]
}
