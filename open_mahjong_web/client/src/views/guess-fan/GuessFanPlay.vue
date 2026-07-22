<template>
  <Teleport to="body">
    <div class="play-shell" role="dialog" aria-modal="true">
      <header class="play-top">
        <div class="meta-left">
          <strong class="title">{{ title }}</strong>
          <span v-if="subtitle" class="sub">{{ subtitle }}</span>
          <span v-if="statusText" class="status">{{ statusText }}</span>
          <span v-if="timeLimitSec > 0" class="limit-tag">限时 {{ timeLimitSec }}s</span>
        </div>
        <div class="meta-actions">
          <slot name="actions" />
          <button type="button" class="btn ghost" @click="emitLeave">离开</button>
        </div>
      </header>

      <main class="play-mid">
        <div class="mid-inner">
          <!-- 倒计时：对局中且有限时 -->
          <div v-if="remainSec != null" class="countdown" :class="{ warn: remainSec <= 10, zero: remainSec <= 0 }">
            <span class="cd-label">本局剩余</span>
            <span class="cd-num">{{ remainSec }}</span>
            <span class="cd-unit">秒</span>
          </div>

          <div class="guess-bar">
            <GuessFanInput
              :disabled="inputDisabled"
              :rules="rules"
              @guess="emit('guess', $event)"
            />
          </div>

          <div class="boards" :class="{ dual: showOpponent }">
            <GuessFanBoardPanel
              :label="meLabel"
              :used="myRows.length"
              :max-guesses="maxGuesses"
              mode="full"
              :rows="myRows"
              empty-text="输入番种名开始猜测"
            />
            <GuessFanBoardPanel
              v-if="showOpponent"
              :label="oppLabel"
              :used="oppPreviewRows.length"
              :max-guesses="maxGuesses"
              :correct="oppCorrect"
              mode="preview"
              :rows="oppPreviewRows"
              empty-text="等待对手猜测…"
            />
          </div>

          <div v-if="reveal" class="reveal">
            答案：<strong>{{ reveal.name }}</strong>
            · {{ (reveal.rules || []).join('/') }}
            · {{ (reveal.types || []).join('、') }}
            · 组数 {{ formatGroups(reveal.reqLength) }}
            · 番数 {{ reveal.fan }}
          </div>
          <p v-if="error" class="err">{{ error }}</p>
        </div>
      </main>

      <footer class="play-bot">
        <div class="progress" :class="{ dual: showOpponent }">
          <div class="prog-side">
            <span class="prog-name">{{ meLabel }}</span>
            <div class="slots">
              <i
                v-for="n in maxGuesses"
                :key="'ms' + n"
                class="slot"
                :class="slotClass(myRows, n - 1)"
              />
            </div>
          </div>
          <div v-if="showOpponent" class="prog-side">
            <span class="prog-name">{{ oppLabel }}</span>
            <div class="slots">
              <i
                v-for="n in maxGuesses"
                :key="'os' + n"
                class="slot"
                :class="slotClassPreview(oppPreviewRows, n - 1)"
              />
            </div>
          </div>
        </div>
      </footer>

      <div v-if="resultVisible" class="result-mask" role="status" aria-live="assertive">
        <section class="result-card">
          <div class="result-kicker">本局结算</div>
          <h2>{{ resultTitle }}</h2>
          <p class="result-message">{{ resultMessage }}</p>
          <div v-if="reveal" class="answer-card">
            <span>正确答案</span>
            <strong>{{ reveal.name }}</strong>
            <small>{{ (reveal.rules || []).join(' / ') }} · {{ (reveal.types || []).join('、') }} · {{ reveal.fan }} 番</small>
          </div>
          <div class="result-players">
            <article v-for="player in resultPlayers" :key="player.id" class="result-player">
              <header>
                <span class="player-avatar">{{ player.nick?.slice(0, 1) || '?' }}</span>
                <strong>{{ player.nick }}</strong>
                <span>{{ player.correct ? '猜中' : `${player.guesses?.length || 0} 次猜测` }}</span>
              </header>
              <ol v-if="player.guesses?.length" class="guess-history">
                <li v-for="(guess, index) in player.guesses" :key="`${player.id}-${index}`" :class="{ correct: guess.result?.correct }">
                  <span>{{ index + 1 }}</span><strong>{{ guess.name || '未知番种' }}</strong><em>{{ guess.result?.correct ? '命中' : '未命中' }}</em>
                </li>
              </ol>
              <p v-else class="no-guesses">本局没有提交猜测</p>
            </article>
          </div>
          <div v-if="nextRoundSec != null" class="next-round-tip">
            <strong>{{ nextRoundSec }}</strong> 秒后自动进入下一局
          </div>
          <div v-else class="next-round-tip finished">{{ resultFinishedText }}</div>
          <div v-if="nextRoundSec == null" class="result-actions">
            <button v-if="resultRestartVisible" type="button" class="result-restart" @click="emit('restart')">再来一局</button>
            <button type="button" class="result-leave" @click="emitLeave">{{ resultLeaveText }}</button>
          </div>
        </section>
      </div>

      <div v-if="startCountdownSec != null && startCountdownSec > 0" class="start-mask" role="status" aria-live="assertive">
        <strong :key="startCountdownSec" class="start-countdown">{{ startCountdownSec }}</strong>
      </div>
    </div>
  </Teleport>
</template>

<script setup>
import GuessFanInput from './GuessFanInput.vue'
import GuessFanBoardPanel from './GuessFanBoardPanel.vue'
import { MAX_GUESSES } from '@/constants/guessFanCatalog'

defineProps({
  title: { type: String, default: '猜番对抗' },
  subtitle: { type: String, default: '' },
  statusText: { type: String, default: '' },
  remainSec: { default: null },
  timeLimitSec: { type: Number, default: 0 },
  rules: { type: Array, default: () => ['guobiao', 'riichi'] },
  inputDisabled: { type: Boolean, default: false },
  myRows: { type: Array, default: () => [] },
  oppPreviewRows: { type: Array, default: () => [] },
  showOpponent: { type: Boolean, default: false },
  meLabel: { type: String, default: '我' },
  oppLabel: { type: String, default: '对手' },
  oppCorrect: { type: Boolean, default: false },
  maxGuesses: { type: Number, default: MAX_GUESSES },
  reveal: { type: Object, default: null },
  error: { type: String, default: '' },
  resultVisible: { type: Boolean, default: false },
  resultTitle: { type: String, default: '' },
  resultMessage: { type: String, default: '' },
  resultPlayers: { type: Array, default: () => [] },
  nextRoundSec: { default: null },
  startCountdownSec: { default: null },
  resultRestartVisible: { type: Boolean, default: false },
  resultFinishedText: { type: String, default: '对战已结束' },
  resultLeaveText: { type: String, default: '返回大厅' },
})

const emit = defineEmits(['guess', 'leave', 'restart'])
function emitLeave() {
  emit('leave')
}

function formatGroups(value) {
  if (value === '全体') return '全体'
  if (Array.isArray(value)) return `[${value.join(',')}] 组`
  return `${value} 组`
}

function slotClass(rows, idx) {
  const row = rows[idx]
  if (!row?.result) return 'empty'
  if (row.result.correct) return 'hit'
  return 'used'
}

function slotClassPreview(rows, idx) {
  const row = rows[idx]
  if (!row?.preview) return 'empty'
  if (row.preview.correct) return 'hit'
  return 'used'
}
</script>

<style scoped>
.play-shell {
  position: fixed;
  inset: 0;
  z-index: 4000;
  display: flex;
  flex-direction: column;
  background: #f5f5f5;
  color: #333;
  font-family: inherit;
}

.play-top {
  flex-shrink: 0;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 10px 16px;
  background: #1a1a1a;
  color: #fff;
  z-index: 2;
}

.meta-left {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 14px;
  min-width: 0;
  font-size: 13px;
}

.title {
  font-size: 15px;
}

.sub,
.status {
  color: rgba(255, 255, 255, 0.72);
}

.limit-tag {
  padding: 2px 8px;
  border: 1px solid rgba(255, 255, 255, 0.3);
  font-size: 12px;
  color: #79bbff;
}

.meta-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}

.btn {
  padding: 7px 12px;
  border: 0;
  background: #409eff;
  color: #fff;
  font-size: 13px;
  cursor: pointer;
  font-family: inherit;
}

.btn.ghost {
  background: transparent;
  border: 1px solid rgba(255, 255, 255, 0.35);
  color: #fff;
}

.btn.ghost:hover {
  border-color: #fff;
}

.play-mid {
  flex: 1;
  min-height: 0;
  overflow: auto;
  padding: 16px 16px 12px;
}

.mid-inner {
  max-width: 1100px;
  margin: 0 auto;
}

.countdown {
  display: flex;
  align-items: baseline;
  justify-content: center;
  gap: 8px;
  margin: 0 auto 14px;
  padding: 10px 18px;
  max-width: 280px;
  background: #1a1a1a;
  color: #fff;
  border-radius: 4px;
}

.countdown.warn {
  background: #b33a3a;
}

.countdown.zero {
  background: #666;
}

.cd-label {
  font-size: 12px;
  opacity: 0.75;
}

.cd-num {
  font-size: 28px;
  font-weight: 800;
  font-variant-numeric: tabular-nums;
  line-height: 1;
  min-width: 1.4em;
  text-align: center;
}

.cd-unit {
  font-size: 12px;
  opacity: 0.75;
}

.guess-bar {
  margin: 0 auto 16px;
  max-width: 560px;
  position: relative;
  z-index: 5;
}

.boards {
  display: grid;
  grid-template-columns: 1fr;
  gap: 16px;
}

.boards.dual {
  grid-template-columns: 1fr 1fr;
}

.reveal {
  margin: 14px 0 0;
  padding: 12px 14px;
  background: #ecf5ff;
  border: 1px solid #b3d8ff;
  font-size: 14px;
}

.err {
  margin: 10px 0 0;
  color: #f56c6c;
  font-size: 13px;
}

.play-bot {
  flex-shrink: 0;
  background: #fff;
  border-top: 1px solid #e4e7ed;
  padding: 12px 16px calc(12px + env(safe-area-inset-bottom, 0px));
  box-shadow: 0 -4px 16px rgba(0, 0, 0, 0.06);
  z-index: 2;
}

.progress {
  display: grid;
  grid-template-columns: 1fr;
  gap: 10px;
  max-width: 1100px;
  margin: 0 auto;
}

.progress.dual {
  grid-template-columns: 1fr 1fr;
}

.prog-side {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}

.prog-name {
  flex-shrink: 0;
  width: 4.5em;
  font-size: 13px;
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.slots {
  display: flex;
  gap: 6px;
  flex: 1;
  min-width: 0;
}

.slot {
  flex: 1;
  max-width: 36px;
  height: 10px;
  border-radius: 2px;
  background: #e4e7ed;
  font-style: normal;
}

.slot.used {
  background: #909399;
}

.slot.hit {
  background: #67c23a;
}

.result-mask {
  position: fixed;
  inset: 0;
  z-index: 20;
  display: grid;
  place-items: center;
  padding: 20px;
  background: rgba(10, 18, 28, 0.72);
  backdrop-filter: blur(5px);
}

.result-card {
  width: min(760px, 100%);
  max-height: min(86vh, 760px);
  overflow: auto;
  padding: 26px;
  border-radius: 14px;
  background: #fff;
  box-shadow: 0 24px 70px rgba(0, 0, 0, 0.35);
  text-align: center;
}

.result-kicker { color: #909399; font-size: 12px; letter-spacing: .18em; }
.result-card h2 { margin: 7px 0 4px; font-size: 25px; }
.result-message { margin: 0 0 16px; color: #606266; }
.answer-card { display: flex; flex-direction: column; gap: 4px; padding: 14px; border: 1px solid #b3d8ff; border-radius: 10px; background: #ecf5ff; }
.answer-card span, .answer-card small { color: #606266; font-size: 12px; }
.answer-card strong { color: #1677c8; font-size: 24px; }
.result-players { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-top: 14px; text-align: left; }
.result-player { min-width: 0; padding: 12px; border: 1px solid #e4e7ed; border-radius: 10px; }
.result-player header { display: flex; align-items: center; gap: 8px; }
.result-player header > strong { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.result-player header > span:last-child { color: #909399; font-size: 12px; }
.player-avatar { display: grid; place-items: center; width: 30px; height: 30px; border-radius: 50%; background: #1a1a1a; color: #fff; font-weight: 700; }
.guess-history { list-style: none; margin: 10px 0 0; padding: 0; }
.guess-history li { display: grid; grid-template-columns: 24px 1fr auto; gap: 7px; padding: 6px 2px; border-top: 1px solid #f0f0f0; font-size: 13px; }
.guess-history li > span, .guess-history em { color: #909399; font-style: normal; }
.guess-history li.correct strong, .guess-history li.correct em { color: #67c23a; }
.no-guesses { margin: 12px 0 0; color: #909399; font-size: 13px; }
.next-round-tip { margin-top: 18px; color: #606266; }
.next-round-tip strong { display: inline-grid; place-items: center; width: 34px; height: 34px; margin-right: 5px; border-radius: 50%; background: #409eff; color: #fff; font-size: 18px; }
.next-round-tip.finished { color: #409eff; font-weight: 700; }
.result-leave { margin-top: 14px; padding: 9px 22px; border: 0; border-radius: 6px; background: #409eff; color: #fff; cursor: pointer; }
.result-actions { display: flex; justify-content: center; gap: 10px; margin-top: 14px; }
.result-actions .result-leave { margin-top: 0; }
.result-restart { padding: 9px 22px; border: 1px solid #409eff; border-radius: 6px; background: #fff; color: #409eff; cursor: pointer; }

.start-mask { position: fixed; inset: 0; z-index: 30; display: grid; place-items: center; pointer-events: none; }
.start-countdown { color: #fff; font-size: clamp(88px, 18vw, 168px); line-height: 1; text-align: center; text-shadow: 0 4px 14px rgba(0, 0, 0, .78); animation: countdown-pop .35s ease-out; }
@keyframes countdown-pop { from { opacity: 0; transform: scale(.68); } to { opacity: 1; transform: scale(1); } }

@media (max-width: 860px) {
  .boards.dual,
  .progress.dual {
    grid-template-columns: 1fr;
  }
  .result-players { grid-template-columns: 1fr; }
  .result-card { padding: 18px; }
}
</style>
