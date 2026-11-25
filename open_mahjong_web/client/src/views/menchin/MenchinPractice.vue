<template>
  <div class="practice-summary">
    <label>难度
      <select v-model.number="level" @change="next">
        <option v-for="option in CHALLENGE_LEVELS" :key="option.level" :value="option.level">{{ tr(option.name) }}</option>
      </select>
    </label>
    <span>{{ tr('本次答对 {correct} / {answered} 题', { correct, answered }) }}</span>
  </div>
  <MenchinHand :key="number" :hand="question.hand" :answer="answer" :allow-no-wait="level === 2"
    :waits="revealed ? question.waits : undefined" @answer="submit">
    <button v-if="!revealed" type="button" class="practice-button" @click="revealed = true">查看待牌</button>
    <button type="button" class="practice-button" @click="next">换一手</button>
  </MenchinHand>
</template>

<script setup>
import { ref } from 'vue'
import { tr } from '@/i18n'
import { CHALLENGE_LEVELS, challengeQuestion, correctAnswer } from '../../../../shared/menchin.mjs'
import MenchinHand from './MenchinHand.vue'

const seen = new Set()
const level = ref(1)
const question = ref(challengeQuestion(level.value, seen))
const answered = ref(0)
const correct = ref(0)
const number = ref(1)
const answer = ref(null)
const revealed = ref(false)
function submit(tiles) {
  if (revealed.value) return
  answer.value = { tiles, correct: correctAnswer(tiles, question.value.waits) }
  answered.value++
  if (answer.value.correct) correct.value++
  revealed.value = true
}
function next() {
  if (seen.size >= 50) seen.clear()
  question.value = challengeQuestion(level.value, seen)
  number.value++
  answer.value = null
  revealed.value = false
}
</script>

<style scoped>
.practice-summary { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 18px; margin-bottom: 16px; border-bottom: 1px solid #ddd; padding-bottom: 12px; }
.practice-summary label { display: inline-flex; align-items: center; gap: 8px; }
.practice-summary span { font-size: 14px; color: #555; }
.practice-summary select, .practice-button { min-height: 40px; padding: 6px 12px; border: 1px solid #c8c8c8; background: white; color: #333; font: inherit; }
.practice-button { cursor: pointer; }
</style>
