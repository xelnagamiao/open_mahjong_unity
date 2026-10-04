<template>
  <div class="manual-editor">
    <div class="editor-heading"><strong>完整牌墙 {{ allTiles.length }} / {{ rule.tile_count }} 张</strong><el-button size="small" :disabled="disabled || !allTiles.length" @click="clear">清空全部</el-button></div>
    <div class="target-controls">
      <label>添加 / 移动到 <select v-model.number="targetSeat" :disabled="disabled" aria-label="目标座位"><option v-for="(name, seat) in seatNames" :key="seat" :value="seat">{{ name }}位 · {{ seat + 1 }} 号</option></select></label>
      <select v-model="targetZone" :disabled="disabled" aria-label="目标区域"><option value="hand">起始手牌</option><option value="wall">剩余牌山</option></select>
      <strong>{{ targetLabel }} {{ draft[targetSeat][targetZone].length }} / {{ zoneLimit(targetSeat, targetZone) }} 张</strong>
    </div>
    <div class="tile-palette" aria-label="可添加的麻将牌">
      <div v-for="(group, index) in tileGroups" :key="index" class="palette-row"><span class="palette-label">{{ ['万', '筒', '条', '字', '花'][index] }}</span><div class="palette-tiles">
        <button v-for="tile in group" :key="tile.tile" type="button" :aria-label="`添加${tile.label}到${targetLabel}`" :disabled="disabled || targetFull || (counts[tile.tile] || 0) >= tile.count" @click="add(tile.tile)"><TileChip :tile-id="tile.tile" size="md" :disabled="disabled || targetFull || (counts[tile.tile] || 0) >= tile.count" /><small>{{ counts[tile.tile] || 0 }} / {{ tile.count }}</small></button>
      </div></div>
    </div>
    <div class="selection-actions"><span>{{ selectionLabel }}</span><el-button size="small" :disabled="disabled || !selected || targetFull || sameTarget" @click="moveToTarget">移至当前区域</el-button><el-button size="small" :disabled="disabled || !selected || selected.index === 0" @click="shift(-1)">前移</el-button><el-button size="small" :disabled="disabled || !selected || selected.index >= selectedLength - 1" @click="shift(1)">后移</el-button><el-button size="small" type="danger" :disabled="disabled || !selected" @click="remove">删除选中牌</el-button></div>
    <div class="seat-grid">
      <section v-for="(player, seat) in draft" :key="seat" class="seat-section"><h3>{{ seat + 1 }} 号 · {{ seatNames[seat] }}位</h3>
        <div v-for="zone in ['hand', 'wall']" :key="zone" class="tile-zone" :class="{ active: targetSeat === seat && targetZone === zone }">
          <button type="button" class="zone-heading" :disabled="disabled" @click="targetSeat = seat; targetZone = zone">{{ zone === 'hand' ? '起始手牌' : '剩余牌山' }} <b>{{ player[zone].length }} / {{ zoneLimit(seat, zone) }}</b><span v-if="targetSeat === seat && targetZone === zone">当前添加区域</span></button>
          <div class="placed-tiles"><button v-for="(tile, index) in player[zone]" :key="index" type="button" :disabled="disabled" :aria-label="`选择${seatNames[seat]}位${zone === 'hand' ? '手牌' : '牌山'}第${index + 1}张${tileName(tile)}`" :class="{ selected: selected?.seat === seat && selected?.zone === zone && selected?.index === index }" @click="selected = { seat, zone, index }"><TileChip :tile-id="tile" size="sm" /><small>{{ index + 1 }}</small></button><span v-if="!player[zone].length" class="empty-zone">选择此区域后，点击上方牌面添加</span></div>
        </div>
      </section>
    </div>
  </div>
</template>

<script setup>
import { computed, ref } from 'vue'
import TileChip from './TileChip.vue'
import { createDuplicateDraft, duplicateRoundSeats, duplicateZoneLimit, flattenDuplicateDraft, moveDuplicateTile, tileCounts } from '@/utils/duplicateWalls'
const props = defineProps({ modelValue: { type: Array, required: true }, rule: { type: Object, required: true }, round: { type: Number, default: 1 }, disabled: Boolean })
const emit = defineEmits(['update:modelValue'])
const draft = computed(() => props.modelValue)
const seatNames = computed(() => duplicateRoundSeats(props.round).map(seat => ['东', '南', '西', '北'][seat]))
const dealerSeat = computed(() => duplicateRoundSeats(props.round).indexOf(0))
const targetSeat = ref(0)
const targetZone = ref('hand')
const selected = ref(null)
const allTiles = computed(() => flattenDuplicateDraft(draft.value))
const counts = computed(() => tileCounts(allTiles.value))
const tileGroups = computed(() => [1, 2, 3, 4, 5].map(suit => props.rule.tiles.filter(tile => Math.floor(tile.tile / 10) === suit)).filter(group => group.length))
const zoneLimit = (seat, zone) => duplicateZoneLimit(props.rule.tile_count, seat, zone, dealerSeat.value)
const targetLabel = computed(() => `${seatNames.value[targetSeat.value]}位${targetZone.value === 'hand' ? '手牌' : '牌山'}`)
const targetFull = computed(() => draft.value[targetSeat.value][targetZone.value].length >= zoneLimit(targetSeat.value, targetZone.value))
const sameTarget = computed(() => selected.value?.seat === targetSeat.value && selected.value?.zone === targetZone.value)
const selectedLength = computed(() => selected.value ? draft.value[selected.value.seat][selected.value.zone].length : 0)
const selectionLabel = computed(() => selected.value ? `已选${seatNames.value[selected.value.seat]}位${selected.value.zone === 'hand' ? '手牌' : '牌山'}第 ${selected.value.index + 1} 张` : '点击已放置的牌可选择、移动或删除')
const tileName = tile => props.rule.tiles.find(item => item.tile === tile)?.label || String(tile)
function change(edit) { const next = draft.value.map(seat => ({ hand: [...seat.hand], wall: [...seat.wall] })); edit(next); emit('update:modelValue', next) }
function add(tile) { if (props.disabled || targetFull.value || counts.value[tile] >= props.rule.tiles.find(item => item.tile === tile)?.count) return; change(next => next[targetSeat.value][targetZone.value].push(tile)) }
function clear() { selected.value = null; emit('update:modelValue', createDuplicateDraft()) }
function remove() { if (!selected.value || props.disabled) return; const source = selected.value; change(next => next[source.seat][source.zone].splice(source.index, 1)); selected.value = null }
function moveToTarget() { if (!selected.value || props.disabled) return; change(next => moveDuplicateTile(next, selected.value, { seat: targetSeat.value, zone: targetZone.value }, props.rule.tile_count, dealerSeat.value)); selected.value = null }
function shift(offset) { if (!selected.value || props.disabled) return; const source = selected.value; change(next => moveDuplicateTile(next, source, { ...source, index: source.index + offset }, props.rule.tile_count, dealerSeat.value)); selected.value = { ...source, index: source.index + offset } }
</script>

<style scoped>
.manual-editor { padding: 16px; background: #f6f8f7; border: 1px solid #dce5df; border-radius: 8px; }
.editor-heading,.target-controls,.selection-actions { display:flex;align-items:center;gap:10px;flex-wrap:wrap; }
.editor-heading { justify-content:space-between; }.empty-zone { color:#737d88;font-size:12px;line-height:1.6; }.target-controls { padding:10px 0; }.target-controls select { padding:7px;border:1px solid #b8c8c0;border-radius:4px;background:#fff;color:#234b42; }.target-controls strong { font-size:13px; }
.palette-row,.palette-tiles { display:flex;align-items:center;gap:5px; }.palette-row { margin:5px 0; }.palette-tiles { flex-wrap:wrap; }.palette-label { width:24px;flex:none;font-weight:600; }.tile-palette button,.placed-tiles button { border:0;background:transparent;padding:2px;display:flex;flex-direction:column;align-items:center;cursor:pointer; }.tile-palette small,.placed-tiles small { font-size:10px;color:#60766c;line-height:1.3; }.tile-palette button:disabled { opacity:.45;cursor:not-allowed; }
.selection-actions { padding:12px 0;min-height:48px; }.selection-actions>span { font-size:12px;margin-right:auto; }.selection-actions :deep(.el-button+.el-button) { margin-left:0; }.seat-grid { display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px; }.seat-section { min-width:0;background:#fff;border:1px solid #dce5df;border-radius:6px;padding:10px; }.seat-section h3 { margin:0 0 6px;font-size:14px; }.tile-zone { padding:7px;border:1px solid #e4e9e6;border-radius:4px;margin-top:7px; }.tile-zone.active { border-color:#17756a;background:#eff8f5; }.zone-heading { border:0;background:transparent;color:#234b42;cursor:pointer;width:100%;display:flex;gap:8px;align-items:center;padding:0 0 6px;text-align:left; }.zone-heading span { margin-left:auto;font-size:10px;color:#17756a; }.placed-tiles { display:flex;gap:1px;flex-wrap:wrap;min-height:55px;align-items:center; }.placed-tiles button.selected { outline:2px solid #17756a;border-radius:3px;background:#d9eee6; }
@media(max-width:760px){.manual-editor{padding:10px}.seat-grid{grid-template-columns:1fr}.palette-tiles{gap:2px}.target-controls{gap:6px}}
</style>
