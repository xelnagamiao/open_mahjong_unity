<template>
  <div>
    <p>山西麻将 · MIL试行2023版。136张、十三张手牌、无花无癞子；须暗扣报听，有和必和，和牌时结杠。</p>
    <div class="shanxi-common-fields">
      <el-form-item label="圈数"><el-input-number v-model="form.game_round" :min="1" :max="4" :precision="0" /></el-form-item>
      <el-form-item label="局时（秒）"><el-input-number v-model="form.round_timer" :min="0" :max="1000" :precision="0" /></el-form-item>
      <el-form-item label="步时（秒）"><el-input-number v-model="form.step_timer" :min="0" :max="100" :precision="0" /></el-form-item>
      <el-form-item label="听牌提示"><el-switch v-model="form.tips" /></el-form-item>
      <el-form-item label="限制游客"><el-switch v-model="form.tourist_limit" /></el-form-item>
      <el-form-item label="允许观战"><el-switch v-model="form.allow_spectator" /></el-form-item>
    </div>
    <el-form-item v-if="showPassword" label="密码"><el-input v-model="form.password" show-password /></el-form-item>
    <a href="/rulebooks/mil/山西麻将（推广）竞赛规则（试行2023版）.pdf" target="_blank" rel="noopener">阅读 MIL 规则书原文</a>
  </div>
</template>
<script setup>
defineProps({ form: { type: Object, required: true }, showPassword: Boolean })
</script>
<style scoped>
.shanxi-common-fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 0 16px; }
p { color: #606266; line-height: 1.7; }
</style>
