<template>
  <label
    class="language-select"
    :class="{ 'language-select--lobby': variant === 'lobby' }"
    :aria-label="t('语言')"
  >
    <svg
      class="language-select__icon"
      aria-hidden="true"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="1.8"
    >
      <circle cx="12" cy="12" r="9" />
      <path d="M3 12h18M12 3c2.4 2.5 3.7 5.5 3.7 9S14.4 18.5 12 21M12 3C9.6 5.5 8.3 8.5 8.3 12s1.3 6.5 3.7 9" />
    </svg>
    <select :value="locale" @change="setLocale($event.target.value)">
      <option value="zh-CN">简体中文</option>
      <option value="zh-TW">中(繁/台)</option>
      <option value="zh-HK">中(繁/港)</option>
      <option value="en">English</option>
      <option value="ja">日本語</option>
    </select>
  </label>
</template>

<script setup>
import { useI18n } from '@/i18n'

defineProps({
  variant: {
    type: String,
    default: 'nav',
  },
})

const { locale, setLocale, t } = useI18n()
</script>

<style scoped>
.language-select {
  display: inline-flex;
  align-items: center;
  height: 34px;
  margin: 0 4px;
  border: 1px solid #4b5563;
  color: #bbb;
  background: #222;
}

.language-select__icon {
  width: 17px;
  height: 17px;
  margin-left: 9px;
  flex: none;
  color: #9cf;
}

.language-select select {
  max-width: 116px;
  height: 100%;
  padding: 0 24px 0 6px;
  border: 0;
  outline: 0;
  color: inherit;
  background: transparent;
  font: inherit;
  font-size: 13px;
  cursor: pointer;
}

.language-select option {
  color: #222;
  background: #fff;
}

.language-select--lobby {
  border-color: #dcdfe6;
  border-radius: 4px;
  color: #606266;
  background: #fff;
  transition: border-color .16s, box-shadow .16s;
}

.language-select--lobby:hover {
  border-color: #a8abb2;
}

.language-select--lobby:focus-within {
  border-color: #1677ff;
  box-shadow: 0 0 0 2px rgba(22, 119, 255, .12);
}

.language-select--lobby .language-select__icon {
  color: #1677ff;
}

@media (max-width: 768px) {
  .language-select {
    margin-left: auto;
  }

  .language-select select {
    max-width: 102px;
  }
}
</style>
