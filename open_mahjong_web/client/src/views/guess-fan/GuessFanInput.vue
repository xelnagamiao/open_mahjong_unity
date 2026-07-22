<template>
  <form class="guess-form" @submit.prevent="submit">
    <div class="input-wrap">
      <input
        ref="inputEl"
        v-model="query"
        type="text"
        :disabled="disabled"
        placeholder="输入番种名或拼音搜索…"
        autocomplete="off"
        spellcheck="false"
        @focus="focused = true"
        @blur="onBlur"
        @keydown.down.prevent="moveSug(1)"
        @keydown.up.prevent="moveSug(-1)"
        @keydown.esc.prevent="closeSug"
        @keydown.enter.prevent="onEnter"
      />
      <ul v-if="showSug" class="sug">
        <li
          v-for="(f, i) in suggestions"
          :key="f.id"
          :class="{ on: i === sugIndex }"
          @mousedown.prevent="pick(f)"
        >
          <span>{{ f.names[0] }}</span>
          <small>{{ f.rules.map((r) => ruleLabel(r)).join('/') }}</small>
        </li>
      </ul>
    </div>
    <button type="submit" :disabled="disabled || !query.trim()">猜测</button>
  </form>
</template>

<script setup>
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { RULE_LABEL, suggestFans } from '@/constants/guessFanCatalog'

const props = defineProps({
  disabled: { type: Boolean, default: false },
  rules: { type: Array, default: () => ['guobiao', 'riichi'] },
})

const emit = defineEmits(['guess'])

const query = ref('')
const sugIndex = ref(-1)
const focused = ref(false)
const inputEl = ref(null)

const suggestions = computed(() => {
  if (!query.value.trim()) return []
  return suggestFans(query.value, props.rules, 20)
})
const showSug = computed(
  () => focused.value && !props.disabled && suggestions.value.length > 0,
)

watch(query, () => {
  sugIndex.value = -1
})

watch(
  () => props.disabled,
  (disabled) => {
    if (!disabled) refocus()
  },
)

function ruleLabel(r) {
  return RULE_LABEL[r] || r
}

function closeSug() {
  sugIndex.value = -1
  focused.value = false
  inputEl.value?.blur()
}

function onBlur() {
  // 等 mousedown.prevent 的 pick 先执行
  setTimeout(() => {
    focused.value = false
    sugIndex.value = -1
  }, 120)
}

function refocus() {
  nextTick(() => {
    if (!props.disabled) {
      focused.value = true
      inputEl.value?.focus()
    }
  })
}

function pick(f) {
  emit('guess', { id: f.id, name: f.names[0] })
  query.value = ''
  sugIndex.value = -1
  refocus()
}

function moveSug(dir) {
  if (!showSug.value) return
  const n = suggestions.value.length
  if (sugIndex.value < 0) {
    sugIndex.value = dir > 0 ? 0 : n - 1
    return
  }
  sugIndex.value = (sugIndex.value + dir + n) % n
}

function onEnter() {
  if (sugIndex.value >= 0 && suggestions.value[sugIndex.value]) {
    pick(suggestions.value[sugIndex.value])
    return
  }
  submit()
}

function submit() {
  const name = query.value.trim()
  if (!name || props.disabled) return
  if (suggestions.value[0]) {
    pick(suggestions.value[0])
    return
  }
  emit('guess', { name })
  query.value = ''
  sugIndex.value = -1
  refocus()
}

defineExpose({ focus: () => inputEl.value?.focus() })
onMounted(() => {
  localStorage.removeItem('guess-fan:recent-inputs')
  refocus()
})
</script>

<style scoped>
.guess-form {
  display: flex;
  gap: 10px;
  width: 100%;
  max-width: 560px;
  margin: 0 auto;
}

.input-wrap {
  position: relative;
  flex: 1;
  min-width: 0;
}

.guess-form input {
  width: 100%;
  box-sizing: border-box;
  padding: 11px 14px;
  border: 1px solid #dcdfe6;
  border-radius: 4px;
  background: #fff;
  color: #333;
  font-size: 15px;
}

.guess-form input:focus {
  outline: none;
  border-color: #409eff;
  box-shadow: 0 0 0 2px rgba(64, 158, 255, 0.15);
}

.guess-form button {
  padding: 0 18px;
  border: 0;
  border-radius: 4px;
  background: #409eff;
  color: #fff;
  font-weight: 600;
  cursor: pointer;
  font-family: inherit;
  flex-shrink: 0;
}

.guess-form button:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.sug {
  position: absolute;
  left: 0;
  right: 0;
  top: calc(100% + 4px);
  margin: 0;
  padding: 4px 0;
  list-style: none;
  background: #fff;
  border: 1px solid #e0e0e0;
  border-radius: 4px;
  max-height: 240px;
  overflow: auto;
  z-index: 40;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.12);
}

.sug li {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  padding: 9px 14px;
  cursor: pointer;
  font-size: 14px;
}

.sug li.on,
.sug li:hover {
  background: #ecf5ff;
}

.sug small {
  color: #999;
  flex-shrink: 0;
}
</style>
