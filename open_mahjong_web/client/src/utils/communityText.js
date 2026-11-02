export function communityText(item, field, language = 'zh-CN') {
  const translated = item?.[`${field}En`]
  if (String(language).toLowerCase().startsWith('en') && typeof translated === 'string' && translated.trim()) return translated
  return typeof item?.[field] === 'string' ? item[field] : ''
}
