<template>
  <div class="community-grid" :class="`style-${appearance.style}`" :style="colors">
    <article v-for="item in items" :key="item.id" class="community-card">
      <div class="card-heading">
        <span class="channel-icon" aria-hidden="true">
          <svg v-if="item.kind === 'telegram'" viewBox="0 0 48 48"><path d="M5 23 43 7 35 41 23 31 16 37 18 27 35 14 13 24Z" /></svg>
          <svg v-else-if="item.kind === 'discord'" viewBox="0 0 48 48"><path d="M13 13 19 11 21 15H27L29 11 35 13 41 34 33 38 29 33H19L15 38 7 34Z" /><circle cx="18" cy="25" r="3"/><circle cx="30" cy="25" r="3"/></svg>
          <svg v-else-if="item.kind === 'wechat'" viewBox="0 0 48 48"><path d="M25 7C12 5 5 11 5 20 5 25 8 29 12 31L11 37 19 34"/><path d="M23 17C13 22 17 38 32 38L39 42 38 36C49 28 41 12 23 17Z"/><circle cx="25" cy="26" r="1"/><circle cx="35" cy="26" r="1"/></svg>
          <svg v-else viewBox="0 0 48 48"><path d="M14 19C10 8 18 5 24 5S38 8 34 19L39 31 34 31 35 38 28 40 24 36 20 40 13 38 14 31 9 31Z"/><path d="M16 24H32M21 14H27"/></svg>
        </span>
        <div><span class="channel-label">{{ ui(channels[item.kind]) }}</span><span class="tag">{{ ui(item.tag) }}</span></div>
      </div>
      <h3 data-no-translate>{{ contentText(item, 'title') }}</h3>
      <p class="description" data-no-translate>{{ contentText(item, 'description') }}</p>
      <div class="card-bottom">
        <span v-if="item.kind !== 'wechat' && item.kind !== 'discord'" class="contact">{{ item.number }}</span>
        <button :disabled="isUnavailable(item)" @click="activate(item)" data-no-translate>{{ expired(item) ? ui('邀请已失效') : item.joinMode === 'closed' ? ui(contentText(item, 'closedMessage') || '暂未开放') : ui(contentText(item, 'actionLabel')) }}</button>
      </div>
    </article>
    <p v-if="!items.length" class="empty">暂无社群</p>
    <el-dialog v-model="qrOpen" :title="contentText(selected, 'title')" width="min(440px, 94vw)" append-to-body>
      <div class="qr-dialog"><img v-if="selected" :src="selected.qrUrl" alt="社群二维码" @error="qrFailed = true" v-show="!qrFailed"/><p v-if="qrFailed">二维码暂时无法加载</p><p v-if="selected?.qrIsExample" class="example-label">示例二维码</p><p>{{ selected?.qrCaption }}</p></div>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, ref, watch, onMounted, onUnmounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { communityPalettes } from '@/utils/communityAppearance'
import { communityText } from '@/utils/communityText'
import { locale, tr } from '@/i18n'
const props = defineProps({ items: { type: Array, default: () => [] }, appearance: { type: Object, default: () => ({ style: 'original', palette: 'slate' }) }, language: { type: String, default: '' } })
const contentLanguage = computed(() => props.language || locale.value)
const contentText = (item, field) => communityText(item, field, contentLanguage.value)
const ui = source => tr(source, {}, contentLanguage.value)
const channels = { qq: 'QQ', wechat: '微信', telegram: 'Telegram', discord: 'Discord' }
const colors = computed(() => Object.fromEntries(Object.entries(communityPalettes[props.appearance.palette] || communityPalettes.slate).filter(([key]) => key !== 'label').map(([key, value]) => [`--c-${key}`, value])))
const qrOpen = ref(false), selected = ref(null), qrFailed = ref(false)
const now = ref(Date.now())
let clock
onMounted(() => { clock = setInterval(() => { now.value = Date.now(); if (selected.value && isUnavailable(selected.value)) qrOpen.value = false }, 1000) })
onUnmounted(() => clearInterval(clock))
watch(() => props.items, items => { if (selected.value) { const current = items.find(x => x.id === selected.value.id); if (!current || current.qrUrl !== selected.value.qrUrl || isUnavailable(current)) qrOpen.value = false; else selected.value = current } })
const expired = item => item.expiresAt && Date.parse(item.expiresAt) <= now.value
const isUnavailable = item => expired(item) || item.joinMode === 'closed'
async function activate(item) {
  if (isUnavailable(item)) return
  if (item.joinMode === 'qr') { selected.value = item; qrFailed.value = false; qrOpen.value = true; return }
  if (item.joinMode === 'number') {
    try { await navigator.clipboard.writeText(item.number); ElMessage.success('已复制群号') }
    catch { ElMessage.info(`群号：${item.number}`) }
    return
  }
  let address
  try { address = new URL(item.linkUrl); if (address.protocol !== 'https:' || address.username || address.password || address.port || !['qm.qq.com', 't.me', 'discord.gg', 'discord.com'].includes(address.hostname)) throw new Error() }
  catch { ElMessage.error('请先填写有效的社群邀请链接'); return }
  if (item.kind === 'discord') {
    try { await navigator.clipboard.writeText(item.linkUrl); ElMessage.success('已复制邀请链接') }
    catch { ElMessage.info(`邀请链接：${item.linkUrl}`) }
    return
  }
  try { await ElMessageBox.confirm('即将打开外部社群页面，是否继续？', '前往社群', { confirmButtonText: '继续', cancelButtonText: '取消' }) }
  catch { return }
  if (!expired(item)) window.open(item.linkUrl, '_blank', 'noopener,noreferrer')
}
</script>

<style scoped>
.community-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:18px;padding:24px;background:var(--c-surface);color:var(--c-text)}
.community-card{position:relative;display:flex;flex-direction:column;min-height:250px;padding:24px;background:var(--c-card);border:1px solid var(--c-line);overflow:hidden}
.card-heading{display:flex;align-items:center;gap:14px}.channel-icon{display:grid;place-items:center;width:48px;height:48px;background:var(--c-header);color:var(--c-accent);flex-shrink:0}.channel-icon svg{width:34px;height:34px;stroke:currentColor;stroke-width:2.6;stroke-linejoin:round;stroke-linecap:round;fill:none}.channel-label{display:block;font-weight:700;font-size:15px}.tag{font-size:12px;color:var(--c-muted)}h3{font-size:21px;line-height:1.45;margin:18px 0 8px;font-weight:600}.description{font-size:14px;line-height:1.7;white-space:pre-wrap;color:var(--c-muted);margin:0 0 24px}.card-bottom{display:flex;align-items:center;justify-content:space-between;gap:8px;margin-top:auto;padding-top:16px;border-top:1px solid var(--c-line)}.contact{font-size:15px;overflow-wrap:anywhere;font-variant-numeric:tabular-nums}.card-bottom button{flex-shrink:0;border:0;background:var(--c-accent);color:var(--c-actionText);padding:10px 13px;font:inherit;font-size:13px;cursor:pointer}.card-bottom button:hover{filter:brightness(1.12)}.card-bottom button:disabled{opacity:.5;cursor:default}
.style-original .community-card{border:0}.style-original .channel-icon{background:transparent}.style-urban .community-card{border:2px solid var(--c-line);box-shadow:4px 4px 0 var(--c-line)}.style-urban .card-heading{margin:-24px -24px 0;padding:12px 20px;background:var(--c-header);border-bottom:2px solid var(--c-line)}.style-urban .card-bottom button{transform:skewX(-8deg);font-weight:700}.style-academy .community-card{border:0;border-top:6px solid var(--c-accent);box-shadow:0 3px 0 var(--c-line)}.style-academy .channel-icon{border:1px solid var(--c-line);background:transparent}.style-nocturne .community-card{border:1px solid var(--c-line);padding:28px}.style-nocturne .card-heading{border-bottom:1px solid var(--c-line);padding-bottom:14px}.style-nocturne .channel-icon{background:transparent;border:1px solid var(--c-accent)}.style-nocturne .card-bottom button{background:transparent;color:var(--c-accent);border:1px solid var(--c-accent)}.empty{grid-column:1/-1;text-align:center;margin:80px 0;color:var(--c-muted)}.qr-dialog{text-align:center}.qr-dialog img{width:280px;max-width:100%;image-rendering:pixelated;background:white;padding:12px}.example-label{font-weight:700;color:#a35f17}
@media(max-width:1100px){.community-grid{grid-template-columns:repeat(2,minmax(0,1fr))}}@media(max-width:650px){.community-grid{grid-template-columns:1fr;padding:12px}.community-card{min-height:240px}}
.community-card h3,.channel-label,.style-urban .card-bottom button,.example-label{font-weight:400}
</style>
