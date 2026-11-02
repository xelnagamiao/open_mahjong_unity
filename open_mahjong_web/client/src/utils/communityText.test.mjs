import test from 'node:test'
import assert from 'node:assert/strict'
import { communityText } from './communityText.js'

test('published language selection returns the authored text without modifying the record', () => {
  const item = { title: '柴の麻雀群', titleEn: "Chai's Mahjong Group", description: '讨论青雀规则', descriptionEn: 'Discuss Qingque rules.', actionLabel: '复制群号', actionLabelEn: 'Copy group number' }
  const before = structuredClone(item)
  assert.equal(communityText(item, 'title', 'zh-CN'), '柴の麻雀群')
  assert.equal(communityText(item, 'title', 'en'), "Chai's Mahjong Group")
  assert.equal(communityText(item, 'description', 'en-US'), 'Discuss Qingque rules.')
  assert.equal(communityText(item, 'actionLabel', 'en'), 'Copy group number')
  assert.equal(communityText(item, 'description', 'zh-CN'), '讨论青雀规则')
  assert.deepEqual(item, before)
})

test('older publications and deliberately empty English fields retain original content', () => {
  assert.equal(communityText({ description: '原简介' }, 'description', 'en'), '原简介')
  assert.equal(communityText({ title: '原名称', titleEn: ' ' }, 'title', 'en'), '原名称')
  assert.equal(communityText(null, 'title', 'en'), '')
})
