<template>
  <section v-loading="loading" class="event-rooms" aria-label="赛事房间">
    <div class="room-overview">
      <span>共 <strong>{{ rooms.length }}</strong> 间房间 · <strong>{{ runningCount }}</strong> 间对局中</span>
    </div>
    <el-empty v-if="!rooms.length" description="暂无房间" :image-size="72" />
    <div v-else class="room-grid">
      <section v-for="group in groups" :key="group.id" class="room-group" :class="{ 'room-pair': group.paired }" :aria-label="group.paired ? '复式开闭室配对' : undefined">
        <header v-if="group.paired" class="room-pair-head">
          <strong>{{ sichuanDuplicateLabel(group.cards[0].room.sub_rule) }}</strong>
          <span>开室 ⇄ 闭室 · {{ group.cards[0].room.duplicate_round_count }} 副</span>
        </header>
      <article
        v-for="card in group.cards"
        :key="card.room.room_id"
        class="room-card"
        :class="{ 'is-running': card.room.is_game_running }"
        :aria-label="`${card.name}，房间 ${card.room.room_id}`"
      >
        <header class="room-card-head">
          <div class="room-title">
            <h4 :title="card.name" data-no-translate>{{ card.name }}</h4>
            <span class="room-id">房间 #{{ card.room.room_id }}</span>
          </div>
          <el-tag :type="card.room.is_game_running ? 'success' : 'info'" size="small" effect="light">
            {{ card.room.is_game_running ? '对局中' : card.room.duplicate_status === 'ready' ? '本室就绪' : card.room.duplicate_status === 'interrupted' ? '已中断' : card.room.duplicate_status === 'completed' ? '已结束' : '待开局' }}
          </el-tag>
        </header>
        <div class="room-card-meta">
          <span class="room-rule" :title="card.rule">{{ card.rule }}</span>
          <span>{{ card.playerCount }} / {{ card.seats.length }} 人</span>
        </div>
        <ul class="room-players" aria-label="座位玩家">
          <li v-for="(seat, index) in card.seats" :key="index" class="room-player" :class="{ 'is-empty': seat.empty, 'team-blue': card.room.duplicate_match_id && card.room.team_by_seat?.[index] === 0, 'team-red': card.room.duplicate_match_id && card.room.team_by_seat?.[index] === 1 }">
            <span class="room-seat" :aria-label="card.room.duplicate_match_id ? `${'东南西北'[index]}家 · ${card.room.team_by_seat?.[index] === 0 ? '蓝队' : '红队'}` : `座位 ${index + 1}`">{{ card.room.duplicate_match_id ? '东南西北'[index] : index + 1 }}</span>
            <span v-if="seat.empty" class="room-player-name">等待入座</span>
            <router-link
              v-else-if="!seat.isBot"
              class="room-player-link"
              :to="{ path: '/player-data', query: { player: String(seat.userId) } }"
              target="_blank"
              rel="noopener noreferrer"
              :title="`${seat.username} · UID ${seat.userId} · 在新标签页查看玩家数据`"
              :aria-label="`${seat.username}，查看玩家数据（新标签页）`"
            >
              <span class="room-player-name" data-no-translate>{{ seat.username }}</span>
              <el-icon class="room-player-arrow" aria-hidden="true"><TopRight /></el-icon>
            </router-link>
            <span v-else class="room-player-name" :title="seat.username" data-no-translate>{{ seat.username }}</span>
            <span v-if="!seat.empty" class="room-player-state" :class="{ 'is-ready': seat.ready || card.room.is_game_running }">
              {{ card.room.is_game_running ? (seat.isBot ? '机器人' : '对局中') : seat.isBot ? '机器人' : seat.isHost ? '房主' : seat.ready ? '已准备' : '未准备' }}
            </span>
          </li>
        </ul>
        <footer class="room-card-actions">
          <el-button v-if="allowRestart && card.room.duplicate_status === 'interrupted' && card.room.duplicate_table === 'open'" size="small" type="warning" plain :disabled="group.cards.some(c => c.room.is_game_running)" @click="$emit('restart-pair', card.room)">重开未完成副</el-button>
          <el-button size="small" type="primary" plain @click="selectedRoomId = card.room.room_id">
            <el-icon><Setting /></el-icon><span>查看配置</span>
          </el-button>
          <el-button
            link
            size="small"
            type="danger"
            :disabled="group.cards.some(c => c.room.is_game_running || c.room.event_seating_pending)"
            @click="$emit('delete', card.room)"
          >{{ group.paired ? '删除整组' : '删除' }}</el-button>
        </footer>
      </article>
      </section>
    </div>

    <el-dialog v-model="configVisible" title="房间配置" width="min(680px, calc(100vw - 24px))" top="8vh" append-to-body>
      <template v-if="selectedCard">
        <div class="room-config-head">
          <strong data-no-translate>{{ selectedCard.name }}</strong>
          <span>房间 #{{ selectedCard.room.room_id }} · {{ selectedCard.room.is_game_running ? '对局中' : '待开局' }}</span>
        </div>
        <dl class="room-config-list">
          <div v-for="item in selectedSettings" :key="item.label">
            <dt>{{ item.label }}</dt><dd>{{ item.value }}</dd>
          </div>
        </dl>
      </template>
      <template #footer><el-button @click="configVisible = false">关闭</el-button></template>
    </el-dialog>
  </section>
</template>

<script setup>
import { computed, ref } from 'vue'
import { Setting, TopRight } from '@element-plus/icons-vue'
import { eventRoomSettingsRows } from '@/utils/eventRoomSettings'
import { groupEventRoomCards, sichuanDuplicateLabel } from '@/utils/sichuanDuplicate'

const props = defineProps({
  rooms: { type: Array, default: () => [] },
  loading: { type: Boolean, default: false },
  ruleLabels: { type: Object, default: () => ({}) },
  allowRestart: { type: Boolean, default: false },
})
defineEmits(['delete', 'restart-pair'])

const selectedRoomId = ref(null)
const runningCount = computed(() => props.rooms.filter(room => room.is_game_running).length)
const cards = computed(() => props.rooms.map(room => {
  // Fixed seats contain -1 placeholders; arrival order in player_list must not move them.
  const seatList = room.seat_list || room.player_list || []
  const readyIds = new Set((room.ready_list || []).map(Number))
  const hostId = Number(room.host_user_id ?? room.player_list?.[0])
  const seats = Array.from({ length: room.max_player || 4 }, (_, index) => {
    const userId = seatList[index]
    if (userId == null || Number(userId) < 0) return { empty: true }
    const isBot = Number(userId) <= 10
    return {
      userId,
      username: room.player_settings?.[userId]?.username || (isBot ? '机器人' : `玩家 ${userId}`),
      isBot,
      isHost: !isBot && Number(userId) === hostId,
      ready: isBot || readyIds.has(Number(userId)),
    }
  })
  return {
    room,
    seats,
    name: room.room_name || `房间 ${room.room_id}`,
    rule: props.ruleLabels[room.room_rule] || room.room_rule || '未指定规则',
    playerCount: seats.filter(seat => !seat.empty).length,
  }
}))
const selectedCard = computed(() => cards.value.find(card => card.room.room_id === selectedRoomId.value))
const groups = computed(() => groupEventRoomCards(cards.value))
const configVisible = computed({
  get: () => Boolean(selectedCard.value),
  set: value => { if (!value) selectedRoomId.value = null },
})
const selectedSettings = computed(() => {
  if (!selectedCard.value) return []
  const room = selectedCard.value.room
  // The live API is flat. Always review this room's values, never the mutable event preset.
  const rows = eventRoomSettingsRows({ room_rule: room.room_rule, room_config: room }, props.ruleLabels)
    .filter(row => row.label !== '房间名')
  const flowers = rows.find(row => row.label === '花牌')
  if (flowers && typeof room.use_flowers === 'boolean') flowers.value = room.use_flowers ? '开启' : '关闭'
  return [
    { label: '房间人数', value: `${selectedCard.value.playerCount} / ${selectedCard.value.seats.length} 人` },
    { label: '房间密码', value: room.has_password ? '已设置' : '未设置' },
    ...rows,
  ]
})
</script>

<style scoped>
.event-rooms { container: event-rooms / inline-size; min-height: 150px; }
.room-overview { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 6px 16px; margin: 14px 0; color: #606266; font-size: 12px; line-height: 1.6; }
.room-overview strong { color: #303133; font-variant-numeric: tabular-nums; }
.room-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 14px; align-items: stretch; }
.room-group { display: contents; }
.room-pair { display: grid; grid-column: span 2; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; padding: 12px; border: 1px solid #cfdef0; border-radius: 13px; background: linear-gradient(130deg, #edf3fb, #e4edf9); }
.room-pair-head { grid-column: 1 / -1; display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: 5px 12px; padding: 0 2px 2px; color: #425c7c; font-size: 12px; }
.room-pair-head strong { font-size: 13px; }
.room-pair-head span { color: #647d9d; font-size: 11px; }
.room-pair .room-card { border-color: #d6e1ee; }
.room-card { display: flex; min-width: 0; flex-direction: column; overflow: hidden; border: 1px solid #e4e7ed; border-radius: 8px; background: #fff; transition: border-color .15s, box-shadow .15s; }
.room-card:hover { border-color: #b6d5f7; box-shadow: 0 3px 12px #3031330a; }
.room-card.is-running { border-top: 3px solid #67c23a; }
.room-card-head { display: flex; align-items: flex-start; justify-content: space-between; gap: 8px; padding: 14px 12px 8px; }
.room-title { min-width: 0; }
.room-title h4 { overflow: hidden; margin: 0 0 5px; color: #303133; font-size: 14px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
.room-id { color: #909399; font-size: 11px; overflow-wrap: anywhere; }
.room-card-head .el-tag { flex-shrink: 0; }
.room-card-meta { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin: 0 12px 8px; color: #909399; font-size: 12px; }
.room-card-meta > span:last-child { flex-shrink: 0; font-variant-numeric: tabular-nums; }
.room-rule { overflow: hidden; color: #606266; text-overflow: ellipsis; white-space: nowrap; }
.room-players { flex: 1; margin: 0; padding: 0 12px 10px; list-style: none; }
.room-player { display: flex; align-items: center; gap: 7px; min-height: 39px; border-top: 1px solid #f0f2f5; font-size: 12px; }
.room-seat { display: inline-flex; align-items: center; justify-content: center; flex: 0 0 21px; height: 21px; border-radius: 5px; background: #ecf5ff; color: #409eff; font-size: 11px; font-weight: 600; }
.room-player-name { display: block; flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.room-player-link { display: flex; align-items: center; gap: 3px; flex: 1; min-width: 0; min-height: 38px; color: #337ecc; text-decoration: none; }
.room-player-link:hover .room-player-name { text-decoration: underline; }
.room-player-link:focus-visible { outline: 2px solid #409eff; outline-offset: 1px; border-radius: 3px; }
.room-player-arrow { flex-shrink: 0; color: #a0cfff; font-size: 11px; }
.room-player-state { flex-shrink: 0; color: #909399; font-size: 11px; }
.room-player-state.is-ready { color: #529b2e; }
.room-player.is-empty { color: #a8abb2; }
.is-empty .room-seat { color: #a8abb2; background: #f5f7fa; }
.team-blue .room-seat { color: #3274b9; background: #e8f1fb; }
.team-red .room-seat { color: #bc444d; background: #fbecee; }
.room-card-actions { display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 10px 12px; border-top: 1px solid #ebeef5; background: #fafbfc; }
.room-card-actions .el-icon { margin-right: 4px; }
.room-config-head { display: flex; flex-direction: column; gap: 7px; margin-bottom: 16px; padding: 12px 14px; border-radius: 6px; background: #f5f7fa; overflow-wrap: anywhere; }
.room-config-head strong { color: #303133; font-size: 16px; }
.room-config-head span { color: #909399; font-size: 12px; }
.room-config-list { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 24px; max-height: 55vh; overflow-y: auto; scrollbar-gutter: stable; margin: 0; }
.room-config-list > div { display: flex; align-items: baseline; justify-content: space-between; gap: 12px; padding: 10px 0; border-bottom: 1px solid #ebeef5; font-size: 13px; }
.room-config-list dt { flex-shrink: 0; color: #909399; }
.room-config-list dd { margin: 0; color: #303133; text-align: right; overflow-wrap: anywhere; }
@container event-rooms (max-width: 900px) { .room-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@container event-rooms (max-width: 460px) { .room-grid { grid-template-columns: minmax(0, 1fr); } .room-pair { grid-column: span 1; grid-template-columns: minmax(0, 1fr); } }
@media (max-width: 560px) { .room-config-list { grid-template-columns: minmax(0, 1fr); } }
</style>
