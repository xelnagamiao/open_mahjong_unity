export const inventoryCategories = Object.freeze({
  item: '道具',
  character: '角色',
  cosmetic: '装扮',
})

export function inventoryCategoryLabel(category) {
  return Object.hasOwn(inventoryCategories, category)
    ? inventoryCategories[category]
    : category || '未分类'
}
