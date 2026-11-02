<template>
  <div class="beijing-room-config">
    <p class="description">{{ BEIJING_DESCRIPTION }}</p>
    <div class="settings">
      <el-form-item label="圈数"><el-select v-model="form.game_round" aria-label="北京麻将圈数"><el-option v-for="n in 4" :key="n" :value="n" :label="`${n}圈`" /></el-select></el-form-item>
      <el-form-item label="混儿方式"><el-select v-model="form.beijing_hun_mode" aria-label="混儿方式"><el-option v-for="(label, value) in BEIJING_HUN_MODES" :key="value" :value="value" :label="label" /></el-select></el-form-item>
      <el-form-item label="局时储备（秒）"><el-input-number v-model="form.round_timer" :min="0" :max="1000" :precision="0" /></el-form-item>
      <el-form-item label="步时（秒）"><el-input-number v-model="form.step_timer" :min="0" :max="100" :precision="0" /></el-form-item>
      <el-form-item v-if="showPassword" label="房间密码"><el-input v-model="form.password" type="password" show-password clearable /></el-form-item>
      <el-form-item label="听牌提示"><el-switch v-model="form.tips" /></el-form-item>
      <el-form-item label="剩余张数提示"><el-switch v-model="form.count_tips" /></el-form-item>
      <el-form-item label="指针提示"><el-switch v-model="form.pointer_tips" /></el-form-item>
      <el-form-item label="限制游客"><el-switch v-model="form.tourist_limit" /></el-form-item>
      <el-form-item label="允许观战"><el-switch v-model="form.allow_spectator" /></el-form-item>
    </div>
    <p class="description">136张无花牌，0翻可和；吃碰明杠后仅可自摸，打出混儿后本局不可和牌。庄家和牌连庄，其余轮庄。</p>
    <el-collapse>
      <el-collapse-item title="可选规则变体（默认关闭）" name="variants">
        <section v-for="group in BEIJING_OPTION_GROUPS" :key="group.label">
          <h4>{{ group.label }}</h4>
          <div class="settings">
            <el-form-item v-for="key in group.keys" :key="key" :label="BEIJING_OPTIONS[key]"><el-switch v-model="form['beijing_' + key]" /></el-form-item>
          </div>
        </section>
        <p v-if="form.beijing_half_hun" class="description">每张不按本体使用的混儿减1翻，最低保留1翻；原本0翻的和牌仍按0翻结算。</p>
      </el-collapse-item>
    </el-collapse>
    <a href="/rulebook/beijing" target="_blank" rel="noopener">阅读北京麻将（吃碰提）完整规则与变体说明</a>
  </div>
</template>
<script setup>
import { BEIJING_DESCRIPTION, BEIJING_HUN_MODES, BEIJING_OPTIONS, BEIJING_OPTION_GROUPS } from '@/utils/beijingRoomConfig.js'
defineProps({ form: { type: Object, required: true }, showPassword: { type: Boolean, default: true } })
</script>
<style scoped>
.settings { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 16px; }
.settings :deep(.el-select), .settings :deep(.el-input-number) { width: 100%; }
.description { color: var(--el-text-color-regular); line-height: 1.7; margin: 0 0 14px; }
h4 { margin: 14px 0; }
a { display: inline-block; margin-top: 16px; }
@media (max-width: 640px) { .settings { grid-template-columns: 1fr; } }
</style>
