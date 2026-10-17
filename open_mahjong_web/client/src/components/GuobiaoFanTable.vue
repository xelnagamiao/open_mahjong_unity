<template>
  <div class="guobiao-fan-table">
    <button ref="trigger" type="button" class="scene-appearance-toggle__button guobiao-fan-table__button"
      :aria-expanded="open" @click="show">{{ tr('查看规则') }}</button>
    <Teleport to="body">
      <div v-if="open" class="fan-table-backdrop" @click.self="close">
        <section ref="dialog" class="fan-table" :class="{ 'is-dark': interfaceTheme === 'dark' }" :lang="locale" data-no-translate role="dialog" aria-modal="true"
          aria-labelledby="fan-table-title" @keydown="onDialogKeydown">
          <header class="fan-table__header">
            <h2 id="fan-table-title">{{ tr('查看规则') }}</h2>
            <select v-model="selectedRule" class="fan-table__rule" :aria-label="tr('麻将规则')">
              <option value="guobiao">{{ tr('国标麻将') }}</option>
            </select>
            <button type="button" class="fan-table__manual" @click="openManual">
              {{ tr('打开完整说明书') }}
            </button>
            <button type="button" class="fan-table__close" @click="close">{{ tr('关闭窗口') }}</button>
          </header>
          <div class="fan-table__workspace">
            <nav class="fan-table__pages" :aria-label="tr('规则页面')" @keydown="onPageKeydown">
              <button v-for="page in pages" :id="'fan-table-page-' + page.id" :key="page.id" type="button"
                :aria-pressed="currentPage === page.id" :aria-controls="page.id === 'fans' ? 'fan-table-fans' : 'fan-table-guide'"
                :class="{ 'is-selected': currentPage === page.id }" @click="selectPage(page.id)">{{ tr(page.title) }}</button>
            </nav>
            <div id="fan-table-fans" v-show="currentPage === 'fans'" class="fan-table__content">
          <div class="fan-table__tabs" role="tablist" :aria-label="tr('番数')" @keydown="onTabKeydown">
            <button v-for="fan in fanValues" :id="'fan-table-tab-' + fan" :key="fan" type="button" role="tab"
              :aria-selected="currentFan === fan" aria-controls="fan-table-entries" :tabindex="currentFan === fan ? 0 : -1"
              :class="{ 'is-selected': currentFan === fan }" @click="selectFan(fan)">
              {{ formatFanCount(fan) }}
            </button>
          </div>
          <div id="fan-table-entries" ref="list" class="fan-table__list" role="tabpanel"
            :aria-labelledby="'fan-table-tab-' + currentFan" tabindex="0">
            <article v-for="entry in visibleEntries" :key="entry.number" class="fan-table__entry">
              <div class="fan-table__name">
                <h3>{{ translateFanName(entry.name) }}</h3>
                <span>{{ formatFanCount(entry.fan) }}</span>
              </div>
              <div class="fan-table__body">
                <p class="fan-table__description">{{ tr(entry.description) }}</p>
                <div class="fan-table__hand" :style="{ maxWidth: (entry.tileCount * 52 + (entry.groups.length - 1) * 10) + 'px' }">
                  <div v-for="(group, groupIndex) in entry.groups" :key="groupIndex" class="fan-table__meld" :style="{ flex: group.length }">
                    <GuobiaoFanTile v-for="(tile, tileIndex) in group" :key="tileIndex" :tile-id="tile"
                      :face-down="fanExampleFaceDown(entry, entry.groups, groupIndex, tileIndex)" />
                  </div>
                </div>
                <p v-if="entry.note" class="fan-table__note">{{ tr(entry.note) }}</p>
              </div>
            </article>
          </div>
            </div>
            <div v-show="currentPage !== 'fans'" id="fan-table-guide" ref="guideList" class="fan-table__list fan-table__guide"
              role="region" :aria-labelledby="'fan-table-page-' + currentPage" tabindex="0">
              <article v-for="section in guideSections" :key="section.id" class="fan-table__entry">
                <div class="fan-table__name"><h3>{{ tr(section.title) }}</h3></div>
                <div class="fan-table__body"><p class="fan-table__description">{{ tr(section.body) }}</p></div>
              </article>
            </div>
          </div>
        </section>
      </div>
    </Teleport>
  </div>
</template>

<script setup>
import { computed, nextTick, ref } from 'vue'
import { ElMessageBox } from 'element-plus'
import GuobiaoFanTile from '@/components/GuobiaoFanTile.vue'
import catalog from '@/constants/guobiaoFanTable.json'
import guide from '@/constants/guobiaoRuleGuide.json'
import { fanExampleFaceDown, parseFanExample } from '@/utils/guobiaoFanExample'
import { locale, tr } from '@/i18n'
import { formatFanCount, translateFanName } from '@/i18n/fanNames'

defineProps({ interfaceTheme: { type: String, default: 'light' } })

const open = ref(false)
const currentFan = ref(1)
const selectedRule = ref('guobiao')
const trigger = ref(null)
const dialog = ref(null)
const list = ref(null)
const guideList = ref(null)
const currentPage = ref('fans')
const pages = [{ id: 'fans', title: '番种表' }, ...guide.pages]
const pageScroll = { fans: 0, rules: 0, scoring: 0 }
const guideSections = computed(() => guide.pages.find(page => page.id === currentPage.value)?.sections ?? [])
const fanValues = [...new Set(catalog.entries.map((entry) => entry.fan))].sort((a, b) => a - b)
const entries = catalog.entries.map((entry) => {
  const groups = parseFanExample(entry.example)
  return { ...entry, groups, tileCount: groups.reduce((sum, group) => sum + group.length, 0) }
})
const visibleEntries = computed(() => entries.filter((entry) => entry.fan === currentFan.value))

async function openManual() {
  try {
    await ElMessageBox.confirm(tr('将在新窗口中打开国标麻将完整说明书。'), tr('打开完整说明书'), {
      confirmButtonText: tr('打开说明书'), cancelButtonText: tr('返回规则'),
      closeOnClickModal: false, zIndex: 2300,
    })
    window.open('/rulebook/guobiao', '_blank', 'noopener,noreferrer')
  } catch {
    // Cancel leaves the selected category and scroll position intact.
  }
  dialog.value?.querySelector('.fan-table__manual')?.focus()
}

async function show() {
  currentFan.value = 1
  currentPage.value = 'fans'
  for (const page of pages) pageScroll[page.id] = 0
  selectedRule.value = 'guobiao'
  open.value = true
  await nextTick()
  dialog.value?.querySelector('#fan-table-page-fans')?.focus()
}
async function selectPage(id, focus = false) {
  if (currentPage.value !== id) {
    const previousList = currentPage.value === 'fans' ? list.value : guideList.value
    pageScroll[currentPage.value] = previousList?.scrollTop ?? 0
    currentPage.value = id
    await nextTick()
    const nextList = id === 'fans' ? list.value : guideList.value
    if (nextList) nextList.scrollTop = pageScroll[id]
  }
  if (focus) dialog.value?.querySelector('#fan-table-page-' + id)?.focus()
}
function onPageKeydown(event) {
  const index = pages.findIndex(page => page.id === currentPage.value)
  const targets = { ArrowUp: (index + pages.length - 1) % pages.length,
    ArrowLeft: (index + pages.length - 1) % pages.length,
    ArrowDown: (index + 1) % pages.length, ArrowRight: (index + 1) % pages.length,
    Home: 0, End: pages.length - 1 }
  if (!(event.key in targets)) return
  event.preventDefault()
  selectPage(pages[targets[event.key]].id, true)
}
async function close() {
  open.value = false
  await nextTick()
  trigger.value?.focus()
}
async function selectFan(fan, focus = false) {
  currentFan.value = fan
  await nextTick()
  if (list.value) list.value.scrollTop = 0
  if (focus) {
    const selected = dialog.value?.querySelector('[aria-selected="true"]')
    selected?.focus()
    selected?.scrollIntoView({ block: 'nearest', inline: 'nearest' })
  }
}
function onTabKeydown(event) {
  const index = fanValues.indexOf(currentFan.value)
  const targets = { ArrowLeft: (index + fanValues.length - 1) % fanValues.length,
    ArrowRight: (index + 1) % fanValues.length, Home: 0, End: fanValues.length - 1 }
  if (!(event.key in targets)) return
  event.preventDefault()
  selectFan(fanValues[targets[event.key]], true)
}
function onDialogKeydown(event) {
  if (event.key === 'Escape') {
    event.preventDefault()
    event.stopPropagation()
    close()
  } else if (event.key === 'Tab') {
    const controls = [...dialog.value.querySelectorAll('a[href], select, button:not([tabindex="-1"]), [tabindex="0"]')]
      .filter(control => control.getClientRects().length > 0)
    const first = controls[0]
    const last = controls.at(-1)
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus() }
  }
}
</script>

<style scoped>
.guobiao-fan-table, .fan-table { font-family: system-ui, -apple-system, "Segoe UI", "Microsoft YaHei", sans-serif; }
.guobiao-fan-table__button { width: 100%; min-height: 36px; white-space: nowrap; }
.fan-table-backdrop { position: fixed; inset: 0; z-index: 2200; display: grid; place-items: center; padding: 20px; background: rgb(0 0 0 / 48%); }
.fan-table {
  --rule-surface: var(--omu-surface, #fff);
  --rule-subtle: #f5f7fa;
  --rule-border: var(--omu-border-strong, #dcdfe6);
  --rule-text: var(--omu-text, #303133);
  --rule-secondary: var(--omu-text-soft, #606266);
  --rule-accent: var(--omu-accent-strong, #337ecc);
  --rule-selected: var(--omu-accent-soft, #ecf5ff);
  box-sizing: border-box; display: flex; flex-direction: column; gap: 16px;
  width: min(1120px, 100%); min-width: 0; height: min(840px, calc(100dvh - 40px)); min-height: 0;
  padding: 24px; border: 1px solid var(--rule-border); border-radius: var(--omu-radius, 15px);
  background: var(--rule-surface); color: var(--rule-text); box-shadow: 0 16px 56px rgb(0 0 0 / 20%);
  color-scheme: light;
}
.fan-table.is-dark {
  --rule-surface: #202225; --rule-subtle: #292c30; --rule-border: #454a52;
  --rule-text: #edf0f4; --rule-secondary: #b9c0cb; --rule-accent: #85c0ff; --rule-selected: #213c59;
  color-scheme: dark;
}
.fan-table__header { display: grid; grid-template-columns: minmax(0, 1fr) auto auto auto; align-items: center; gap: 12px; }
.fan-table__header h2 { margin: 0; font-size: 22px; font-weight: 600; line-height: 1.3; overflow-wrap: anywhere; }
.fan-table__rule, .fan-table__manual, .fan-table__close {
  box-sizing: border-box; min-height: 36px; min-width: 0; padding: 8px 12px;
  border: 1px solid var(--rule-border); border-radius: var(--omu-radius-sm, 8px);
  background: var(--rule-surface); color: var(--rule-text); font: inherit; font-size: 14px; line-height: 1.35;
}
.fan-table__rule { width: 176px; max-width: 100%; cursor: pointer; }
.fan-table__manual { color: var(--rule-accent); cursor: pointer; }
.fan-table__close { cursor: pointer; }
.fan-table__workspace { display: flex; flex-direction: column; flex: 1; min-width: 0; min-height: 0; gap: 14px; }
.fan-table__pages { display: flex; gap: 6px; border-bottom: 1px solid var(--rule-border); padding-bottom: 10px; }
.fan-table__pages button {
  min-width: 0; min-height: 38px; padding: 8px 16px; border: 1px solid transparent; border-radius: 8px;
  background: transparent; color: var(--rule-secondary); font: inherit; font-size: 15px; line-height: 1.4; cursor: pointer;
}
.fan-table__pages button.is-selected { color: var(--rule-accent); background: var(--rule-selected); font-weight: 600; }
.fan-table__content { display: flex; flex-direction: column; flex: 1; min-width: 0; min-height: 0; gap: 12px; }
.fan-table__tabs { display: flex; flex-shrink: 0; gap: 6px; overflow-x: auto; padding: 3px 3px 6px; scrollbar-width: thin; }
.fan-table__tabs button {
  flex: 1 0 auto; min-width: 46px; min-height: 34px; padding: 6px 10px;
  border: 1px solid var(--rule-border); border-radius: 6px; background: var(--rule-surface); color: var(--rule-secondary);
  font: inherit; font-size: 14px; line-height: 1.4; white-space: nowrap; cursor: pointer;
}
.fan-table__tabs button.is-selected { border-color: var(--rule-accent); background: var(--rule-selected); color: var(--rule-accent); font-weight: 600; }
.fan-table button:hover { border-color: var(--rule-accent); color: var(--rule-accent); }
.fan-table :focus-visible { outline: 2px solid var(--rule-accent); outline-offset: 2px; }
.fan-table__list { flex: 1; min-height: 0; overflow-y: auto; overscroll-behavior: contain; padding: 2px 6px 2px 2px; scrollbar-width: thin; scrollbar-color: var(--rule-border) transparent; }
.fan-table__guide { min-width: 0; }
.fan-table__guide .fan-table__description { white-space: pre-line; line-height: 1.8; }
.fan-table__entry { margin-bottom: 12px; border: 1px solid var(--rule-border); border-radius: 10px; background: var(--rule-surface); }
.fan-table__entry:last-child { margin-bottom: 0; }
.fan-table__name { display: flex; justify-content: space-between; align-items: baseline; gap: 16px; padding: 12px 16px 0; }
.fan-table__name h3 { margin: 0; font-size: 17px; line-height: 1.5; font-weight: 600; overflow-wrap: anywhere; }
.fan-table__name span { padding: 2px 8px; border-radius: 5px; background: var(--rule-selected); color: var(--rule-accent); font-size: 13px; white-space: nowrap; }
.fan-table__body { padding: 8px 16px 16px; }
.fan-table__entry p { margin: 0; line-height: 1.7; overflow-wrap: anywhere; }
.fan-table__description { font-size: 15px; color: var(--rule-secondary); }
.fan-table__hand { display: flex; gap: 10px; width: 100%; margin-top: 12px; }
.fan-table__meld { display: flex; min-width: 0; }
.fan-table__hand :deep(.guobiao-fan-tile) { flex: 1; min-width: 0; }
.fan-table__note { color: var(--rule-secondary); font-size: 13px; }
.fan-table__entry .fan-table__note { margin-top: 10px; padding: 8px 10px; border-radius: 6px; background: var(--rule-subtle); }
@media (max-width: 700px) {
  .fan-table-backdrop { padding: 10px; }
  .fan-table { padding: 16px; gap: 12px; height: calc(100dvh - 20px); border-radius: 12px; }
  .fan-table__header { grid-template-columns: minmax(0, 1fr) auto; gap: 10px; }
  .fan-table__header h2 { font-size: 20px; }
  .fan-table__close { grid-column: 2; grid-row: 1; justify-self: end; }
  .fan-table__rule { grid-column: 1; grid-row: 2; width: 100%; }
  .fan-table__manual { grid-column: 2; grid-row: 2; max-width: 170px; }
  .fan-table__pages { gap: 4px; padding-bottom: 8px; overflow-x: auto; scrollbar-width: thin; }
  .fan-table__pages button { flex: 1 0 auto; padding: 8px 10px; font-size: 14px; white-space: nowrap; }
  .fan-table__workspace { gap: 10px; }
  .fan-table__name { padding: 10px 12px 0; }
  .fan-table__name h3 { font-size: 16px; }
  .fan-table__body { padding: 8px 12px 12px; }
  .fan-table__description { font-size: 14px; }
  .fan-table__hand { gap: 4px; }
}
@media (max-height: 500px) and (orientation: landscape) {
  .fan-table-backdrop { padding: 8px; }
  .fan-table { height: calc(100dvh - 16px); padding: 12px 16px; gap: 8px; }
  .fan-table__workspace, .fan-table__content { gap: 8px; }
  .fan-table__pages { padding-bottom: 6px; }
}
</style>
