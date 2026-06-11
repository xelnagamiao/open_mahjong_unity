<!-- 随机种子验证：输入对局结束公布的主种子，在浏览器内复现随机座位与每一局的配牌/牌山 -->
<template>
  <div class="seed-verify">
    <div class="page-header">
      <h1>随机种子验证</h1>
      <p class="subtitle">
        对局开始前服务器公布 承诺值 = SHA256(主种子 + 盐值)，对局结束后公布主种子与盐值。
        在此输入主种子即可在本地复现整场对局的随机座位与每一局的配牌、牌山，验证服务器未在中途更换种子。
        所有计算均在浏览器内完成，不经过服务器。
      </p>
    </div>

    <section class="card main-card">
      <header class="card-header"><span>输入</span></header>

      <div class="row">
        <label class="row-label">规则</label>
        <el-select v-model="form.rule" size="small" style="width: 140px">
          <el-option label="立直麻将" value="riichi" />
          <el-option label="国标麻将" value="guobiao" />
          <el-option label="古典麻将" value="classical" />
          <el-option label="青雀麻将" value="qingque" />
        </el-select>
        <template v-if="form.rule === 'riichi'">
          <el-switch v-model="form.redDora" size="small" />
          <span class="hint">赤宝牌（与对局设置一致）</span>
        </template>
      </div>

      <div class="row">
        <label class="row-label">主种子</label>
        <el-input
          v-model="form.masterSeed"
          placeholder="对局结束时公布的随机主种子（十六进制或十进制）"
          size="small"
          clearable
        />
      </div>

      <div class="row">
        <label class="row-label">对局局数</label>
        <el-input-number v-model="form.rounds" :min="1" :max="64" size="small" />
        <span class="hint">本场实际打的局数；立直 / 古典的连庄也算一局（局种子序号递增）</span>
      </div>

      <div class="row">
        <label class="row-label">入场顺序</label>
        <el-input
          v-for="(name, i) in form.names"
          :key="i"
          v-model="form.names[i]"
          :placeholder="`玩家${i + 1}`"
          size="small"
          style="width: 110px"
        />
        <span class="hint">牌谱 player_entry_order 的顺序，留空则用 玩家1-4</span>
      </div>

      <div class="row">
        <label class="row-label">承诺值（选填）</label>
        <el-input
          v-model="form.commitment"
          placeholder="可直接粘贴游戏内复制的「承诺值：xxxx 盐值：xxxx」"
          size="small"
          clearable
        />
        <el-input
          v-model="form.salt"
          placeholder="盐值"
          size="small"
          style="width: 220px"
          clearable
        />
      </div>

      <div class="row">
        <el-button type="primary" size="small" @click="generate">开始验证</el-button>
        <span v-if="error" class="hint warn">{{ error }}</span>
      </div>
    </section>

    <template v-if="result">
      <section v-if="result.commitCheck" :class="['card', 'banner-card', result.commitCheck.ok ? 'ok' : 'bad']">
        <strong>{{ result.commitCheck.ok ? '承诺验证通过' : '承诺验证失败' }}</strong>
        <span class="mono">SHA256(主种子 + 盐值) = {{ result.commitCheck.actual }}</span>
      </section>

      <section class="card">
        <header class="card-header"><span>初始随机座位（主种子洗牌入场顺序）</span></header>
        <div class="seat-list">
          <div v-for="(p, i) in result.seats" :key="i" class="seat-item">
            <span class="seat-wind">{{ WINDS[i] }}</span>
            <span class="seat-name">{{ p.name }}</span>
            <span class="hint">（入场第 {{ p.entry }} 位）</span>
          </div>
        </div>
        <p class="hint">
          座位即牌谱中的 p0-p3；东（p0）为第 1 局庄家。每局发牌按 p0→p3 各 13 张依次从牌山头部抓取{{
            result.rule === 'guobiao' ? '，国标庄家额外多抓 1 张（共 14 张）' : ''
          }}。
        </p>
      </section>

      <section class="card">
        <header class="card-header"><span>每局配牌与牌山</span></header>
        <el-collapse v-model="openRounds">
          <el-collapse-item v-for="(r, idx) in result.rounds" :key="idx" :name="idx">
            <template #title>
              <span>第 {{ idx + 1 }} 局（局种子序号 {{ idx + 1 }}）</span>
            </template>
            <div class="round-detail">
              <div class="kv"><span class="k">局种子</span><span class="mono">{{ r.roundSeedHex }}</span></div>
              <div v-if="r.riichi" class="kv">
                <span class="k">宝牌指示牌</span>
                <TileMiniGlyph :tile-id="glyphId(r.riichi.doraIndicator)" />
                <span class="k" style="margin-left: 12px">里宝指示牌</span>
                <TileMiniGlyph :tile-id="glyphId(r.riichi.uraDoraIndicator)" />
              </div>
              <div v-for="(hand, p) in r.hands" :key="p" class="kv">
                <span class="k">p{{ p }}{{ p === 0 ? '（庄家）' : '' }}</span>
                <span class="tiles">
                  <TileMiniGlyph
                    v-for="(t, ti) in hand"
                    :key="ti"
                    :tile-id="glyphId(t)"
                    :class="{ aka: isAka(t) }"
                  />
                </span>
                <span class="mono small">{{ hand.map(notation).join(' ') }}</span>
              </div>
              <div class="kv wall">
                <span class="k">牌山（发牌后，按摸牌顺序）</span>
                <span class="mono small">{{ r.wallRemaining.map(notation).join(' ') }}</span>
              </div>
            </div>
          </el-collapse-item>
        </el-collapse>
      </section>
    </template>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { tileIdToNotation } from '@/composables/useMahjongTiles'
import TileMiniGlyph from '@/components/TileMiniGlyph.vue'
import {
  parseMasterSeed,
  computeCommitmentHex,
  shuffleSeats,
  simulateRound
} from '@/utils/seedVerify'

const WINDS = ['东', '南', '西', '北']

const form = ref({
  rule: 'riichi',
  redDora: true,
  masterSeed: '',
  rounds: 8,
  names: ['', '', '', ''],
  commitment: '',
  salt: ''
})
const error = ref('')
const result = ref(null)
const openRounds = ref([0])

// 兼容直接粘贴「承诺值：xxxx 盐值：xxxx」整段文本
function extractCommitmentSalt() {
  let commitment = form.value.commitment.trim()
  let salt = form.value.salt.trim()
  const combined = commitment.match(/承诺值[:：]\s*([0-9a-fA-F]+)/)
  if (combined) {
    commitment = combined[1]
    const saltMatch = form.value.commitment.match(/盐值[:：]\s*(\S+)/)
    if (saltMatch && !salt) salt = saltMatch[1]
  }
  return { commitment: commitment.toLowerCase().replace(/^0x/, ''), salt }
}

function generate() {
  error.value = ''
  result.value = null
  try {
    const ms = parseMasterSeed(form.value.masterSeed)
    const names = form.value.names.map((s, i) => (s.trim() || `玩家${i + 1}`))
    const seats = shuffleSeats(ms, names.map((name, i) => ({ name, entry: i + 1 })))

    let commitCheck = null
    const { commitment, salt } = extractCommitmentSalt()
    if (commitment) {
      const actual = computeCommitmentHex(ms, salt)
      commitCheck = { ok: actual === commitment, actual }
    }

    const total = Math.min(Math.max(1, Number(form.value.rounds) || 1), 64)
    const rounds = []
    for (let n = 1; n <= total; n++) {
      rounds.push(simulateRound(form.value.rule, ms, n, form.value.redDora))
    }
    result.value = { rule: form.value.rule, seats, commitCheck, rounds }
    openRounds.value = [0]
  } catch (e) {
    error.value = e.message || String(e)
  }
}

// 赤宝牌（105/205/305）用普通 5 的字形 + 红色显示
const AKA_TO_NORMAL = { 105: 15, 205: 25, 305: 35 }
function isAka(t) {
  return t in AKA_TO_NORMAL
}
function glyphId(t) {
  return AKA_TO_NORMAL[t] || t
}
function notation(t) {
  if (t === 105) return '0m'
  if (t === 205) return '0p'
  if (t === 305) return '0s'
  return tileIdToNotation(t)
}
</script>

<style scoped>
.seed-verify {
  max-width: 960px;
  margin: 0 auto;
  padding: 16px;
}

.page-header h1 {
  margin: 0 0 6px;
  font-size: 22px;
  color: #fff;
  text-shadow: 1px 1px 2px rgba(0, 0, 0, 0.25);
}

.subtitle {
  color: #fff;
  font-size: 13px;
  line-height: 1.6;
  margin: 0 0 14px;
  opacity: 0.95;
}

.card {
  background: #fff;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  padding: 14px 16px;
  margin-bottom: 14px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-weight: 600;
  margin-bottom: 10px;
}

.row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 10px;
  flex-wrap: wrap;
}

.row-label {
  width: 104px;
  flex-shrink: 0;
  font-size: 13px;
  color: #444;
}

.hint {
  color: #999;
  font-size: 12px;
}

.hint.warn {
  color: #e55353;
}

.banner-card {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.banner-card.ok {
  border-color: #67c23a;
  background: #f0f9eb;
  color: #529b2e;
}

.banner-card.bad {
  border-color: #f56c6c;
  background: #fef0f0;
  color: #c45656;
}

.seat-list {
  display: flex;
  gap: 18px;
  flex-wrap: wrap;
  margin-bottom: 8px;
}

.seat-item {
  display: flex;
  align-items: center;
  gap: 6px;
}

.seat-wind {
  display: inline-flex;
  width: 24px;
  height: 24px;
  align-items: center;
  justify-content: center;
  border-radius: 4px;
  background: #545c64;
  color: #fff;
  font-size: 13px;
}

.seat-name {
  font-weight: 600;
}

.round-detail .kv {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  margin-bottom: 6px;
  flex-wrap: wrap;
}

.round-detail .k {
  flex-shrink: 0;
  min-width: 96px;
  color: #666;
  font-size: 12px;
  line-height: 20px;
}

.tiles {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
}

.mono {
  font-family: Consolas, Menlo, monospace;
  font-size: 12px;
  word-break: break-all;
}

.mono.small {
  color: #888;
  line-height: 20px;
}

.aka {
  color: #e02020;
}
</style>
