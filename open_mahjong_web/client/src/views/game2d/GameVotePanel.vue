<template>
  <section v-if="info && info.phase !== 'idle'" class="vote-panel" aria-live="polite">
    <header class="vote-panel__header">
      <strong>{{ title }}</strong>
      <span v-if="countdown > 0">({{ Math.ceil(countdown) }})</span>
    </header>

    <div v-if="humanVotes.length" class="vote-panel__players">
      <div
        v-for="item in humanVotes"
        :key="item.seat"
        class="vote-panel__player"
        :class="`is-${item.vote}`"
      >
        <span>{{ item.name }}</span>
        <small>{{ voteLabel(item.vote) }}</small>
      </div>
    </div>

    <footer v-if="canVote" class="vote-panel__actions">
      <button type="button" class="is-agree" @click="$emit('respond', 'agree')">同意</button>
      <button type="button" class="is-refuse" @click="$emit('respond', 'refuse')">拒绝</button>
    </footer>
    <footer v-else-if="info.phase === 'paused'" class="vote-panel__actions">
      <button type="button" class="is-resume" @click="$emit('resume')">解除暂停</button>
    </footer>
  </section>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  info: { type: Object, default: null },
  players: { type: Array, default: () => [] },
  selfSeat: { type: Number, default: 0 },
  countdown: { type: Number, default: 0 },
})

defineEmits(['respond', 'resume'])

const title = computed(() => {
  const info = props.info
  if (!info) return ''
  const tally = `${Number(info.agree ?? 0)}/${Number(info.total ?? 0)}`
  switch (info.phase) {
    case 'voting':
      return info.vote_type === 'end' ? `投票结束对局 ${tally}` : `投票暂停对局 ${tally}`
    case 'pause_pending': return '下一步操作以后暂停'
    case 'paused': return '对局已暂停'
    case 'resume_voting': return `投票解除暂停 ${tally}`
    case 'rejected':
      return info.vote_type === 'end'
        ? '结束对局被拒绝'
        : info.vote_type === 'resume' ? '解除暂停被拒绝' : '暂停对局被拒绝'
    case 'resume_countdown': return '即将解除暂停'
    case 'end_countdown':
      return info.vote_type === 'end'
        ? `玩家同意结束对局 ${tally}`
        : `即将结束对局 ${tally}`
    default: return ''
  }
})

const humanVotes = computed(() => Object.entries(props.info?.votes ?? {})
  .filter(([, vote]) => vote !== 'bot')
  .map(([seat, vote]) => {
    const player = props.players.find((item) => Number(item.player_index) === Number(seat))
    return {
      seat,
      vote,
      name: player?.username || `玩家 ${Number(seat) + 1}`,
    }
  })
  .sort((left, right) => Number(left.seat) - Number(right.seat)))

const selfVote = computed(() => props.info?.votes?.[String(props.selfSeat)] ?? 'none')
const canVote = computed(() => (
  ['voting', 'resume_voting'].includes(props.info?.phase)
  && !['agree', 'refuse'].includes(selfVote.value)
))

function voteLabel(vote) {
  if (vote === 'agree') return '同意'
  if (vote === 'refuse') return '拒绝'
  return '等待'
}
</script>

<style scoped>
.vote-panel {
  position: absolute;
  z-index: 62;
  top: 14px;
  left: 50%;
  width: min(520px, calc(100% - 28px));
  transform: translateX(-50%);
  overflow: hidden;
  border: 1px solid rgba(35, 35, 35, 0.7);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.98);
  color: #222;
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.3);
}

.vote-panel__header {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: 42px;
  padding: 0 14px;
  border-bottom: 1px solid #d0d0d0;
  background: #f2f2f2;
  font-size: 15px;
}

.vote-panel__players {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 7px;
  padding: 10px;
}

.vote-panel__player {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 2px;
  padding: 7px 5px;
  border-radius: 7px;
  background: #555957;
  color: white;
  text-align: center;
}

.vote-panel__player.is-agree { background: #3a9a4b; }
.vote-panel__player.is-refuse { background: #bd4141; }
.vote-panel__player span { overflow: hidden; font-size: 12px; text-overflow: ellipsis; white-space: nowrap; }
.vote-panel__player small { opacity: 0.85; font-size: 10px; }

.vote-panel__actions {
  display: flex;
  justify-content: center;
  gap: 10px;
  padding: 0 10px 11px;
}

.vote-panel__actions button {
  min-width: 92px;
  padding: 7px 14px;
  border: 1px solid rgba(30, 30, 28, 0.25);
  border-radius: 7px;
  color: #fff;
  font: inherit;
  cursor: pointer;
}

.vote-panel__actions .is-agree,
.vote-panel__actions .is-resume { background: #368b46; }
.vote-panel__actions .is-refuse { background: #b63e3e; }

@media (max-width: 560px) {
  .vote-panel__players { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
</style>
