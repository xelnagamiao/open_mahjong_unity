<template>
  <MenchinHand :key="number" :hand="question.hand" :answer="answer" :waits="revealed ? question.waits : undefined" @answer="submit">
    <button v-if="!revealed" type="button" class="practice-button" @click="revealed = true">查看待牌</button>
    <button type="button" class="practice-button" @click="next">换一手</button>
  </MenchinHand>
</template>

<script setup>
import { ref } from 'vue'
import { correctAnswer, newQuestion } from '../../../../shared/menchin.mjs'
import MenchinHand from './MenchinHand.vue'

const seen = new Set()
const question = ref(newQuestion(2, seen))
const number = ref(1)
const answer = ref(null)
const revealed = ref(false)
function submit(tiles) {
  answer.value = { tiles, correct: correctAnswer(tiles, question.value.waits) }
  revealed.value = true
}
function next() {
  if (seen.size >= 50) seen.clear()
  question.value = newQuestion(2, seen)
  number.value++
  answer.value = null
  revealed.value = false
}
</script>

<style scoped>
.practice-button { padding: 8px 14px; border: 1px solid #c8c8c8; background: white; color: #333; font: inherit; cursor: pointer; }
</style>
