import test from 'node:test'
import assert from 'node:assert/strict'
import { validateMail, rewardLimit, mailRewardStatus, mailIcon } from '../src/utils/mailbox.js'

const catalog = Array.from({length:9}, (_, i) => ({ item_id: i + 100, name:`物品${i}`, category:'item', max_quantity:100, grant_enabled:true }))
const body = rewards => ({ user_id:101, sender:'系统', title:'活动奖励', content:'感谢参与\n请查收', reason:'测试', expires_at:null, rewards })
test('plain mail and up to eight distinct rewards are accepted, ninth is rejected', () => {
  assert.equal(validateMail(body([]), catalog), '')
  const rewards = catalog.slice(0,8).map(i => ({item_id:i.item_id, quantity:2}))
  assert.equal(validateMail(body(rewards), catalog), '')
  assert.match(validateMail(body([...rewards,{item_id:108,quantity:1}]), catalog), /8/)
})
test('duplicates, invalid quantities and unavailable/default items are rejected', () => {
  assert.match(validateMail(body([{item_id:100,quantity:1},{item_id:100,quantity:1}]),catalog), /一次/)
  for (const quantity of [0,-1,1.5,101,NaN,null]) assert.notEqual(validateMail(body([{item_id:100,quantity}]),catalog), '')
  for (const overrides of [{is_default:true}, {grant_enabled:false}]) assert.notEqual(validateMail(body([{item_id:100,quantity:1}]),[{...catalog[0],...overrides}]), '')
  assert.notEqual(validateMail(body([{item_id:999,quantity:1}]),catalog), '')
})
test('permanent rewards, text, recipients and expiry are validated', () => {
  assert.equal(rewardLimit({...catalog[0],category:'cosmetic'}),1)
  assert.notEqual(validateMail(body([{item_id:100,quantity:2}]),[{...catalog[0],category:'cosmetic'}]), '')
  for (const changes of [{user_id:'101'}, {user_id:10}, {content:'<b>奖励</b>'}, {title:''}, {reason:''}, {expires_at:'invalid'}, {expires_at:'2000-01-01T00:00:00Z'}]) assert.notEqual(validateMail({...body([]),...changes},catalog), '')
})
test('history distinguishes no attachment, claimed and expired rewards', () => {
  assert.equal(mailRewardStatus({rewards:[]}), '无附件')
  assert.equal(mailRewardStatus({rewards:[{}]}), '待领取')
  assert.equal(mailRewardStatus({rewards:[{}],expired:true}), '已过期')
  assert.equal(mailRewardStatus({rewards:[{}],expired:true,claimed:true}), '已领取')
  assert.equal(mailIcon({icon_path:'https://untrusted.invalid/icon.png'}), '')
})
