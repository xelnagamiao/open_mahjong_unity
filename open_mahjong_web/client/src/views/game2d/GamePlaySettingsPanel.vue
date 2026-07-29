<template>
  <div class="scene-appearance-panel">
    <div class="scene-appearance-panel__header">
      <h3 class="scene-appearance-panel__title">对局设置</h3>
    </div>

    <label class="scene-appearance-panel__field">
      <span class="scene-appearance-panel__label">摸切快捷</span>
      <select
        class="scene-appearance-panel__select"
        :value="appearance.moqieShortcutMode"
        @change="$emit('moqie-shortcut', Number($event.target.value))"
      >
        <option :value="0">双击摸切</option>
        <option :value="1">右键摸切</option>
        <option :value="2">无快捷键</option>
      </select>
    </label>

    <label class="scene-appearance-panel__field">
      <span class="scene-appearance-panel__label">过牌快捷</span>
      <select
        class="scene-appearance-panel__select"
        :value="appearance.passShortcutMode"
        @change="$emit('pass-shortcut', Number($event.target.value))"
      >
        <option :value="0">右键取消</option>
        <option :value="1">双击取消</option>
        <option :value="2">无快捷键</option>
      </select>
    </label>

    <button
      type="button"
      class="assist-switch assist-switch--row"
      :class="{ 'is-on': settings.autoFlowerOnMatchStart }"
      :aria-pressed="settings.autoFlowerOnMatchStart"
      @click="$emit('assist-update', { autoFlowerOnMatchStart: !settings.autoFlowerOnMatchStart })"
    >
      <span class="assist-switch__label">整场开始前开启自动补花</span>
      <span class="assist-switch__track" aria-hidden="true">
        <span class="assist-switch__thumb" />
      </span>
    </button>
  </div>
</template>

<script setup>
defineProps({
  appearance: { type: Object, required: true },
  settings: { type: Object, required: true },
})

defineEmits(['moqie-shortcut', 'pass-shortcut', 'assist-update'])
</script>
