<template>
  <article class="room-row" :class="{ 'is-running': room.is_game_running }" role="listitem" :aria-label="`房间 ${room.room_id} ${room.room_name || ''}`">
    <div class="room-summary">
      <span class="room-identity" :title="`${room.room_name || '未命名房间'} · 房间 ${room.room_id}`">
        <span class="room-id">#{{ room.room_id }}</span>
        <span class="room-name" data-no-translate>{{ room.room_name || '未命名房间' }}</span>
      </span>
      <span class="room-rule" :title="rule">{{ rule }}</span>
      <span class="room-venue" :title="venue">{{ venue }}</span>
      <span class="room-host" :title="room.host_user_id ? `${room.host_name || room.host_user_id} (${room.host_user_id})` : '待入座'">房主 {{ room.host_name || room.host_user_id || '待入座' }}</span>
      <span v-if="room.duplicate_match_id" class="room-pair">{{ room.duplicate_lobby ? '八人复式' : room.duplicate_table === 'open' ? '复式开室' : '复式闭室' }}</span>
      <span class="room-status" :class="{ running: room.is_game_running }">{{ roomStateLabel(room) }}</span>
      <span class="room-count">{{ occupiedCount }}/{{ seats.length }} 座 · {{ room.online_count || 0 }} 人在线<span v-if="waiters.length"> · {{ waiters.length }} 人候补</span></span>
    </div>
    <div class="room-members" aria-label="房间玩家">
      <span v-for="seat in seats" :key="seat.index" class="room-seat" :class="{ empty: !seat.player }" :title="seatTitle(seat)">
        <span class="seat-index">{{ seat.label }}</span>
        <template v-if="seat.player">
          <span class="player-name" data-no-translate>{{ seat.player.username || seat.player.user_id }}</span>
          <span class="player-uid">({{ seat.player.user_id }})</span>
          <span v-if="seat.player.user_id === room.host_user_id" class="host-badge">房主</span>
          <span class="player-state" :class="{ online: seat.player.online && !seat.player.is_bot, offline: !seat.player.online && !seat.player.is_bot }">{{ playerState(seat.player) }}</span>
        </template>
        <span v-else>等待入座</span>
      </span>
      <span v-for="player in waiters" :key="`waiting-${player.user_id}`" class="room-seat waiting" :title="`${player.username} (${player.user_id}) · 候补 · ${playerState(player)}`">
        <span class="seat-index">候补</span><span class="player-name" data-no-translate>{{ player.username || player.user_id }}</span><span class="player-uid">({{ player.user_id }})</span><span class="player-state">{{ playerState(player) }}</span>
      </span>
    </div>
    <div class="room-actions">
      <el-button size="small" type="danger" plain :loading="closing === room.room_id" :disabled="Boolean(closing) || room.event_seating_pending" @click="$emit('close', room)">关闭房间</el-button>
    </div>
  </article>
</template>

<script setup>
import { computed } from 'vue'
import { formatRule, formatSubRule } from '@/utils/gameMeta'
import { roomSeatItems, roomStateLabel } from '@/utils/adminRoomLayout'
const props = defineProps({ room: { type: Object, required: true }, closing: { type: String, default: '' } })
defineEmits(['close'])
const seats = computed(() => roomSeatItems(props.room))
const occupiedCount = computed(() => seats.value.filter(seat => seat.player).length)
const waiters = computed(() => (props.room.players || []).filter(player => player.waiting))
const rule = computed(() => props.room.sub_rule ? formatSubRule(props.room.sub_rule) : formatRule(props.room.room_rule))
const venue = computed(() => props.room.event_id ? `${props.room.venue_kind === 'base' ? '基地' : '赛事'} · ${props.room.event_name || props.room.event_id}` : '大厅房间')
function playerState(player) { return player.is_bot ? '机器人' : player.online ? '在线' : '离线' }
function seatTitle(seat) {
  if (!seat.player) return `座位 ${seat.label} · 等待入座`
  const player = seat.player
  return `座位 ${seat.label} · ${player.username || player.user_id} (${player.user_id})${player.user_id === props.room.host_user_id ? ' · 房主' : ''} · ${playerState(player)}`
}
</script>

<style scoped>
.room-row { display: grid; grid-template-columns: minmax(0, 1fr) auto; grid-template-rows: auto auto; gap: 6px 12px; padding: 10px 12px; border: 1px solid #e4e7ed; border-left: 3px solid #c0c4cc; border-radius: 6px; background: #fff; font-size: 12px; font-weight: 400; font-synthesis: none; }
.room-row.is-running { border-left-color: #67c23a; }
.room-summary { display: flex; align-items: center; gap: 14px; min-width: 0; overflow-x: auto; white-space: nowrap; min-height: 24px; color: #606266; }
.room-summary > span { flex-shrink: 0; }
.room-identity { display: inline-flex; align-items: center; gap: 8px; max-width: 290px; }
.room-id { color: #909399; font-variant-numeric: tabular-nums; }
.room-name { max-width: 200px; overflow: hidden; text-overflow: ellipsis; color: #303133; font-size: 13px; }
.room-rule, .room-venue, .room-host { max-width: 180px; overflow: hidden; text-overflow: ellipsis; }
.room-venue, .room-count, .room-host { color: #909399; }
.room-pair { color: #8064a2; }
.room-status { margin-left: auto; color: #909399; }
.room-status.running { color: #529b2e; }
.room-members { display: grid; grid-auto-flow: column; grid-auto-columns: minmax(210px, 1fr); gap: 8px; overflow-x: auto; min-width: 0; padding-bottom: 2px; }
.room-seat { display: flex; align-items: center; gap: 5px; min-width: 0; height: 26px; padding: 0 7px; border-radius: 4px; background: #f5f7fa; white-space: nowrap; }
.seat-index { color: #909399; font-size: 11px; }
.player-name { min-width: 0; overflow: hidden; text-overflow: ellipsis; color: #303133; }
.player-uid { color: #909399; font-size: 11px; }
.player-state { margin-left: auto; font-size: 11px; color: #909399; }
.player-state.online { color: #529b2e; }
.player-state.offline, .host-badge { color: #b7812b; }
.player-uid, .player-state, .host-badge { flex-shrink: 0; }
.host-badge { font-size: 11px; }
.empty { color: #a8abb2; }
.room-actions { grid-column: 2; grid-row: 1 / span 2; align-self: center; }
.room-actions :deep(.el-button) { font-weight: 400; font-synthesis: none; }
.room-summary::-webkit-scrollbar, .room-members::-webkit-scrollbar { height: 4px; }
@media (max-width: 640px) { .room-row { gap: 6px 8px; padding: 8px; } }
</style>
