<template>
  <div class="mc game-lobby">
    <header class="welcome"><h1>门清大战</h1></header>

    <template v-if="page === 'home'">
      <section class="modes">
        <h2 class="section-bar">■ 模式选择</h2>
        <nav class="action-grid" aria-label="模式选择">
          <router-link class="action-card practice" to="/menchin/practice"><span>休闲训练</span><small>不限时 · 随时换牌</small></router-link>
          <router-link class="action-card challenge" to="/menchin/challenge"><span>限时挑战</span><small>50 题 · 每题 20 秒</small></router-link>
          <div class="action-card pvp">
            <router-link class="match-entry" to="/menchin/pvp?entry=match"><span>PvP 匹配</span></router-link>
            <div class="pvp-options">
              <small>双人万字 · 20 局</small>
              <router-link class="bot-entry" to="/menchin/pvp?entry=bot">机器人对战</router-link>
            </div>
          </div>
          <router-link class="action-card create" to="/menchin/pvp?entry=create"><span>开设房间</span><small>好友约战</small></router-link>
        </nav>
      </section>
      <div class="home-layout lobby-columns">
        <div class="home-main lobby-room-column">
          <h2 class="section-bar lobby-section-title">■ 个人成绩</h2>
          <section class="panel">
            <div class="account">
              <span v-if="auth.token" data-no-translate>{{ auth.username }}</span><span v-else>本机成绩</span>
              <router-link v-if="!auth.token" :to="loginRoute">登录参与排行</router-link>
            </div>
            <dl v-if="profile" class="stats">
              <div><dt>等级</dt><dd>{{ levelFor(profile.points) }}</dd></div>
              <div><dt>累计积分</dt><dd>{{ profile.points }}</dd></div>
              <div><dt>正确率</dt><dd>{{ profile.answered ? Math.round(100 * profile.correct / profile.answered) + '%' : '—' }}</dd></div>
              <div><dt>最高连对</dt><dd>{{ profile.bestStreak }}</dd></div>
            </dl>
            <p v-else role="status">{{ boardStatus === 'error' ? '成绩读取失败' : '正在读取成绩' }}</p>
            <p v-if="auth.token && profile" class="ranked-record">
              <template v-if="profile.matches">{{ tr('排位 Elo {rating} · {wins} 胜 {losses} 负', { rating: profile.rating, wins: profile.wins, losses: profile.matches - profile.wins }) }}</template>
              <template v-else>尚无排位战绩</template>
            </p>
          </section>
          <section class="rooms-section lobby-room-section" aria-labelledby="menchin-rooms-title">
            <h2 id="menchin-rooms-title" class="section-bar lobby-section-title">■ 房间列表</h2>
            <div class="lobby-room-panel">
              <div class="side-tools">
                <form class="join-code" @submit.prevent="joinByCode">
                  <input v-model.trim="roomCode" aria-label="房间码" placeholder="房间码" inputmode="numeric" maxlength="6" pattern="[0-9]{6}" required />
                  <button class="panel-btn" :disabled="!validRoomCode">加入</button>
                </form>
                <button type="button" class="panel-btn ghost" :disabled="roomsStatus === 'loading'" @click="refreshRooms">刷新</button>
              </div>
              <p v-if="roomsStatus === 'error'" class="room-status" role="status">房间列表读取失败</p>
              <p v-else-if="roomsStatus === 'loading'" class="room-status" role="status">正在读取房间</p>
              <ul v-else-if="rooms.length" class="room-list lobby-room-list" aria-labelledby="menchin-rooms-title" tabindex="0">
                <li v-for="room in rooms" :key="room.code" class="room-item">
                  <div class="room-main">
                    <span class="code">{{ room.code }}</span>
                    <div class="room-players" aria-label="房间玩家">
                      <span class="player-chip">
                        <i data-no-translate aria-hidden="true">{{ Array.from(room.host)[0] }}</i>
                        <b data-no-translate :title="room.host">{{ room.host }}</b>
                        <small>房主</small>
                      </span>
                      <span class="player-chip waiting">等待玩家</span>
                    </div>
                    <span class="meta">1/2</span>
                  </div>
                  <router-link class="panel-btn" :to="roomLink(room.code)">加入</router-link>
                </li>
              </ul>
              <p v-else class="room-status">暂无等待中的房间</p>
            </div>
          </section>
        </div>
        <aside class="leaderboards">
          <MenchinLeaderboard :rows="scores" :duel-rows="duelScores" :status="boardStatus" @retry="retry" />
        </aside>
      </div>
      <section class="game-guide" aria-labelledby="menchin-guide-title">
        <h2 id="menchin-guide-title" class="section-bar">■ 门清大战说明</h2>
        <div class="panel">
          <p>休闲训练可随时查看待牌或换牌。限时挑战每题 20 秒，越快答对，得分越高。</p>
          <p>双人对战使用 36 张万字牌，20 局交替先手。和牌得 1 分，诈和给对手 1 分，流局双方 0 分。机器人可直接开局，仅真人匹配计入 Elo。</p>
        </div>
      </section>
    </template>

    <template v-else>
      <nav class="navigation" aria-label="模式导航">
        <router-link class="back" to="/menchin">返回门清大战</router-link>
        <router-link v-if="page !== 'practice'" to="/menchin/practice">休闲训练</router-link>
        <router-link v-if="page !== 'challenge'" to="/menchin/challenge">限时挑战</router-link>
      </nav>

      <template v-if="page === 'practice'">
        <h2 class="section-bar">■ 休闲训练</h2>
        <section class="panel"><MenchinPractice /></section>
      </template>

      <template v-else-if="page === 'challenge'">
        <h2 class="section-bar">■ 限时挑战</h2>
        <section class="panel">
          <p v-if="error" class="notice" role="alert">{{ error }} <button type="button" class="secondary" @click="retry">重试</button></p>
          <div v-if="game" class="run-summary">
            <span>{{ tr('第 {round} / {limit} 题', { round: game.round, limit: game.limit }) }}</span>
            <span>{{ CHALLENGE_LEVELS[game.level - 1].name }}</span>
            <span>{{ tr('得分 {score}', { score: player.points }) }}</span>
            <span v-if="game.phase === 'playing'" class="time" role="timer">{{ tr('剩余 {seconds} 秒', { seconds: secondsLeft }) }}</span>
          </div>
          <template v-if="game?.phase === 'finished'">
            <div class="result" role="status">
              <span>{{ endTitle[game.endReason] }}</span>
              <span>{{ tr('答对 {count} 题', { count: player.correct }) }}</span>
              <span v-if="game.saving === 'saving'">正在保存成绩</span>
              <span v-if="game.saving === 'retry'">正在重试保存成绩</span>
            </div>
          </template>
          <MenchinHand v-if="game" :key="game.id + ':' + game.round" :hand="game.hand" :answer="player.answer"
            :allow-no-wait="game.level === 2" :waits="game.waits || undefined"
            :disabled="game.phase !== 'playing' || !connected || busy || remainingMs <= 0"
            @answer="tiles => play('answer', { tiles })">
            <span v-if="game.phase === 'playing' && game.round > 1" role="status">{{ tr('上题 +{points} 分', { points: game.lastPoints }) }}</span>
          </MenchinHand>
          <template v-if="!game || game.phase === 'finished'">
            <template v-if="!game">
              <p class="instructions">每题限时 20 秒，最多 50 题。答对后自动出下一题，答错或超时结算。</p>
              <p class="instructions">每剩 0.1 秒得 1 分，按难度倍率计分。中途退出不计成绩。</p>
            </template>
            <div class="start-row">
              <label>难度
                <select v-model.number="level">
                  <option v-for="option in CHALLENGE_LEVELS" :key="option.level" :value="option.level">{{ option.level }} · {{ tr(option.name) }} ×{{ option.multiplier }}</option>
                </select>
              </label>
              <button type="button" class="primary" :disabled="!connected || busy" @click="startChallenge(level)">{{ game ? '再来一次' : '开始挑战' }}</button>
              <span v-if="!connected && !error" role="status">正在连接挑战服务</span>
            </div>
          </template>
        </section>
      </template>

      <MenchinLeaderboard v-else-if="page === 'rankings'" :rows="scores" :status="boardStatus" @retry="retry" />
    </template>
  </div>
</template>

<script setup>
import { computed, ref, toRef, watch } from 'vue'
import { onBeforeRouteLeave, useRouter } from 'vue-router'
import { tr } from '@/i18n'
import { CHALLENGE_LEVELS, levelFor } from '../../../shared/menchin.mjs'
import MenchinHand from './menchin/MenchinHand.vue'
import MenchinPractice from './menchin/MenchinPractice.vue'
import MenchinLeaderboard from './menchin/MenchinLeaderboard.vue'
import { useMenchin } from './menchin/useMenchin'

const props = defineProps({ page: { type: String, default: 'home' } })
const { auth, profile, game, scores, duelScores, boardStatus, rooms, roomsStatus, refreshRooms,
  connected, busy, error, now, play, startChallenge, exitChallenge, retry } = useMenchin(toRef(props, 'page'))
const router = useRouter()
const roomCode = ref('')
const validRoomCode = computed(() => /^\d{6}$/.test(roomCode.value))
const roomLink = code => ({ name: 'MenchinDuel', query: { entry: 'joinRoom', code } })
function joinByCode() {
  if (validRoomCode.value) router.push(roomLink(roomCode.value))
}
const loginRoute = { path: '/login', query: { redirect: '/menchin' } }
const level = ref(1)
const endTitle = { wrong: '答错，挑战结束', timeout: '时间到，挑战结束', complete: '50 题全部完成' }
const player = computed(() => game.value?.players[0])
const remainingMs = computed(() => Math.max(0, (game.value?.endsAt ?? 0) - now.value))
const secondsLeft = computed(() => (Math.ceil(remainingMs.value / 100) / 10).toFixed(1))
watch(() => game.value?.id, () => { if (game.value) level.value = game.value.level })
onBeforeRouteLeave((to, from) => {
  if (from.name === 'MenchinChallenge' && to.name !== from.name) exitChallenge()
})
</script>

<style scoped>
.mc { color: #333; font-weight: 400; }
.mc h1, .mc h2, .mc th { margin: 0; font-weight: 400; }
.welcome { margin-bottom: 18px; padding: 22px 20px; background: #1a1a1a; color: white; text-align: center; }
.welcome h1 { font-size: 1.55rem; }
.modes { margin-bottom: 18px; }
.home-main, .leaderboards { min-width: 0; }
.rooms-section, .game-guide { margin-top: 18px; }
.lobby-room-panel { padding: 12px 14px; border: 1px solid #e0e0e0; background: white; }
.room-status { margin: 0; padding: 24px 8px; text-align: center; color: #777; font-size: 13px; }
.game-guide p { margin: 0; color: #606266; font-size: 13px; line-height: 1.65; }
.game-guide p + p { margin-top: 6px; }
.mc .section-bar { background: rgba(0, 0, 0, .75); color: white; padding: 6px 12px; font-size: 13px; margin-bottom: 12px; }
.action-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 16px; }
.action-card { box-sizing: border-box; display: flex; flex-direction: column; align-items: flex-start; justify-content: center; width: 100%; min-height: 98px; padding: 14px 16px; border: 0; border-radius: 0; text-align: left; font: inherit; text-decoration: none; color: white; box-shadow: 0 4px 14px rgba(0, 0, 0, .12); }
.action-card span { font-size: 1.05rem; font-weight: 700; margin-bottom: 6px; }
.action-card small { font-size: 12px; line-height: 1.45; }
.practice { background: #9b59b6; }
.challenge { background: #45b7d1; }
.pvp { position: relative; background: #c0392b; }
.pvp a { color: inherit; }
.match-entry { text-decoration: none; }
.match-entry span { display: block; }
.match-entry::after { content: ''; position: absolute; inset: 0; }
.pvp-options { display: flex; align-items: baseline; flex-wrap: wrap; gap: 2px 12px; }
.bot-entry { position: relative; font-size: 13px; text-underline-offset: 3px; }
.bot-entry::after { content: ''; position: absolute; inset: -5px -3px; }
.create { background: #e6a23c; }
a.action-card:hover, .pvp:hover { filter: brightness(1.05); }
.panel { padding: 18px; background: white; border: 1px solid #ddd; }
.navigation, .start-row, .result, .run-summary, .account { display: flex; flex-wrap: wrap; align-items: center; gap: 10px 16px; }
.navigation { margin-bottom: 14px; gap: 8px; }
.navigation a, .primary, .secondary { display: inline-flex; box-sizing: border-box; min-height: 42px; align-items: center; justify-content: center; padding: 8px 14px; border: 1px solid #c8c8c8; background: white; color: #333; text-decoration: none; font: inherit; }
.navigation .back { background: #454545; border-color: #454545; color: white; }
.primary { background: #2475b9; border-color: #2475b9; color: white; cursor: pointer; }
.secondary { cursor: pointer; }
.primary:disabled { opacity: .55; cursor: default; }
.mc :is(a, button, select):focus-visible { outline: 2px solid #176ac4; outline-offset: 2px; }
.account { justify-content: space-between; }
.account a { color: #246aab; }
.ranked-record { margin: 12px 0 0; padding-top: 10px; border-top: 1px solid #eee; color: #606060; font-size: 13px; }
.stats { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 16px; margin: 16px 0 0; }
.stats dt, .instructions, .empty { color: #606060; }
.stats dd { margin: 4px 0 0; font-size: 18px; }
.instructions, .empty, .result, .notice { margin: 0 0 10px; }
.panel > :last-child { margin-bottom: 0; }
.start-row { margin: 16px 0; }
.start-row label { display: inline-flex; flex-wrap: wrap; align-items: center; gap: 8px; min-width: 0; max-width: 100%; }
select { min-height: 42px; padding: 6px 10px; border: 1px solid #bbb; background: white; color: #333; font: inherit; }
.start-row select { box-sizing: border-box; min-width: 0; max-width: 100%; }
.run-summary { border-bottom: 1px solid #ddd; margin-bottom: 16px; padding-bottom: 12px; }
.result { padding: 10px 12px; background: #f4f4f4; }
.time { color: #a6302a; font-variant-numeric: tabular-nums; margin-left: auto; }
.notice { color: #a6302a; }
@media (max-width: 900px) {
  .action-grid { grid-template-columns: 1fr 1fr; }
}
@media (max-width: 600px) {
  .action-grid { grid-template-columns: minmax(0, 1fr); }
  .panel { padding: 12px 10px; }
  .navigation a { min-height: 44px; }
  .stats { grid-template-columns: 1fr 1fr; }
}
</style>
