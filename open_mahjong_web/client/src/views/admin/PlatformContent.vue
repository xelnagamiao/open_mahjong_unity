<template>
  <div class="content-admin" v-loading="loading">
    <header class="page-heading"><h2>鸣谢与社群</h2><span v-if="document">已发布版本 {{ document.revision }}</span><el-button @click="reload">重新载入</el-button><el-button type="primary" :loading="saving" :disabled="!document" @click="publish">发布内容</el-button></header>
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <el-tabs v-if="document" v-model="tab">
      <el-tab-pane label="社群" name="communities">
        <el-alert title="仅超级管理员可修改。名称、入群方式、二维码与上下架发布后同步到客户端，无需重新构建 Unity。" type="info" :closable="false" class="notice" />
        <div class="communities-editor">
          <aside class="entry-list"><button v-for="item in document.communities" :key="item.id" :class="{ selected: item.id === selectedId }" @click="selectedId = item.id"><strong>{{ item.title }}</strong><span>{{ channels[item.kind] }} · {{ item.enabled ? '已启用' : '已下架' }}</span></button><el-button @click="addCommunity">新增社群</el-button></aside>
          <el-card v-if="selected" shadow="never" class="entry-form">
            <el-form label-position="top">
              <div class="fields"><el-form-item label="社群名称"><el-input v-model="selected.title" maxlength="64" /></el-form-item><el-form-item label="渠道"><el-select v-model="selected.kind"><el-option v-for="(label, value) in channels" :key="value" :label="label" :value="value" /></el-select></el-form-item><el-form-item label="标签"><el-input v-model="selected.tag" maxlength="24" /></el-form-item><el-form-item label="排序（数字小的在前）"><el-input-number v-model="selected.sort" :min="-9999" :max="9999" /></el-form-item></div>
              <el-form-item label="社群简介"><el-input v-model="selected.description" type="textarea" :rows="3" maxlength="480" show-word-limit /></el-form-item>
              <el-form-item label="英文社群名称（留空使用原名称）"><el-input v-model="selected.titleEn" maxlength="96" /></el-form-item>
              <el-form-item label="英文社群简介（留空使用原简介）"><el-input v-model="selected.descriptionEn" type="textarea" :rows="4" maxlength="960" show-word-limit /></el-form-item>
              <div class="fields"><el-form-item label="群号 / 频道"><el-input v-model="selected.number" maxlength="64" /></el-form-item><el-form-item label="入群方式"><el-select v-model="selected.joinMode"><el-option label="复制群号 / 频道" value="number"/><el-option label="外部邀请链接" value="link"/><el-option label="显示二维码" value="qr"/><el-option label="暂时关闭" value="closed"/></el-select></el-form-item><el-form-item label="按钮文字"><el-input v-model="selected.actionLabel" maxlength="16" /></el-form-item><el-form-item label="失效时间（留空为长期有效）"><el-date-picker v-model="selected.expiresAt" type="datetime" value-format="YYYY-MM-DDTHH:mm:ssZ" clearable @change="selected.expiresAt ||= ''" /></el-form-item></div>
              <el-form-item v-if="selected.joinMode === 'link'" label="邀请链接"><el-input v-model="selected.linkUrl" placeholder="https://…" /></el-form-item>
              <el-form-item label="英文按钮文字（留空使用现有翻译）"><el-input v-model="selected.actionLabelEn" maxlength="48" /></el-form-item>
              <div v-if="selected.joinMode === 'qr'" class="qr-editor"><img v-if="selected.qrUrl" :src="selected.qrUrl" alt="当前二维码"/><div><el-upload :show-file-list="false" :http-request="uploadQr" accept="image/png,image/jpeg" :disabled="uploading"><el-button :loading="uploading">上传 / 替换二维码</el-button></el-upload><p>PNG / JPEG，64–2048 像素，不超过 2MB</p><el-checkbox v-model="selected.qrIsExample">标记为示例二维码</el-checkbox><el-input v-model="selected.qrCaption" type="textarea" :rows="2" maxlength="160" placeholder="二维码说明" aria-label="二维码说明"/></div></div>
              <el-form-item v-if="selected.joinMode === 'closed'" label="关闭提示"><el-input v-model="selected.closedMessage" maxlength="64" /></el-form-item>
              <el-form-item v-if="selected.joinMode === 'closed'" label="英文关闭提示（留空使用现有翻译）"><el-input v-model="selected.closedMessageEn" maxlength="128" /></el-form-item>
              <div class="form-actions"><el-switch v-model="selected.enabled" active-text="在客户端显示"/><el-button type="danger" plain @click="removeCommunity">删除这张卡片</el-button></div>
            </el-form>
          </el-card>
        </div>
      </el-tab-pane>
      <el-tab-pane label="鸣谢" name="credits">
        <el-card v-for="item in document.credits" :key="item.id" shadow="never" class="credit-row"><div class="credit-heading"><el-input v-model="item.title" aria-label="鸣谢分组名称" maxlength="64"/><el-input-number v-model="item.sort" :min="-9999" :max="9999" aria-label="鸣谢排序"/><el-checkbox v-model="item.enabled">启用</el-checkbox><el-button type="danger" link @click="document.credits = document.credits.filter(x => x.id !== item.id)">删除</el-button></div><el-input v-model="item.text" type="textarea" :rows="3" maxlength="5000" show-word-limit aria-label="鸣谢正文"/></el-card>
        <el-button @click="document.credits.push({ id: `credit_${Date.now()}`, title: '新的分组', text: '', sort: document.credits.length * 10 + 10, enabled: true })">新增鸣谢分组</el-button><el-form-item label="结束语" class="closing"><el-input v-model="document.thanksClosing" maxlength="200" /></el-form-item>
      </el-tab-pane>
      <el-tab-pane label="风格与预览" name="appearance">
        <div class="appearance-options"><div><strong>卡片风格</strong><el-radio-group v-model="document.appearance.style"><el-radio-button v-for="style in communityStyles" :key="style.value" :value="style.value" :label="style.value">{{ style.label }}</el-radio-button></el-radio-group></div><div><strong>配色</strong><el-radio-group v-model="document.appearance.palette"><el-radio-button v-for="(palette, key) in communityPalettes" :key="key" :value="key" :label="key">{{ palette.label }}</el-radio-button></el-radio-group></div></div>
        <el-form-item label="内容预览语言"><el-radio-group v-model="previewLanguage"><el-radio-button value="zh-CN" label="zh-CN">简体中文</el-radio-button><el-radio-button value="en" label="en">English</el-radio-button></el-radio-group></el-form-item>
        <CommunityCards :items="visibleCommunities" :appearance="document.appearance" :language="previewLanguage" />
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'
import CommunityCards from '@/components/CommunityCards.vue'
import { communityStyles, communityPalettes } from '@/utils/communityAppearance'
const document = ref(null), loading = ref(false), saving = ref(false), uploading = ref(false), error = ref(''), selectedId = ref(''), tab = ref('communities')
const previewLanguage = ref('zh-CN')
const channels = { qq: 'QQ', wechat: '微信', telegram: 'Telegram', discord: 'Discord' }
const selected = computed(() => document.value?.communities.find(x => x.id === selectedId.value))
const visibleCommunities = computed(() => [...document.value.communities].filter(x => x.enabled).sort((a, b) => a.sort - b.sort))
let savedJson = ''
async function load() {
  loading.value = true; error.value = ''
  try { document.value = (await adminApi.get('/platform-content')).data.data; savedJson = JSON.stringify(document.value); selectedId.value = document.value.communities[0]?.id || '' }
  catch (err) { error.value = err.response?.data?.message || '内容加载失败' }
  finally { loading.value = false }
}
async function reload() {
  if (document.value && JSON.stringify(document.value) !== savedJson) {
    try { await ElMessageBox.confirm('放弃本次未发布的修改并重新载入？', '重新载入', { confirmButtonText: '重新载入', cancelButtonText: '取消' }) } catch { return }
  }
  await load()
}
async function publish() {
  saving.value = true; error.value = ''
  try { document.value = (await adminApi.put('/platform-content', document.value)).data.data; savedJson = JSON.stringify(document.value); ElMessage.success('已发布到客户端') }
  catch (err) { error.value = err.response?.data?.message || '发布失败，当前编辑内容已保留' }
  finally { saving.value = false }
}
function addCommunity() {
  const item = { id: `community_${Date.now()}`, title: '新的社群', titleEn: '', kind: 'qq', number: '', tag: '', description: '', descriptionEn: '', sort: document.value.communities.length * 10 + 10, enabled: false, joinMode: 'closed', actionLabel: '前往社群', actionLabelEn: '', linkUrl: '', qrUrl: '', qrIsExample: false, qrCaption: '', closedMessage: '暂未开放', closedMessageEn: '', expiresAt: '' }
  document.value.communities.push(item); selectedId.value = item.id
}
async function removeCommunity() {
  try { await ElMessageBox.confirm(`从本次发布内容中移除「${selected.value.title}」？`, '移除社群', { confirmButtonText: '移除', cancelButtonText: '取消' }) } catch { return }
  document.value.communities = document.value.communities.filter(x => x.id !== selectedId.value); selectedId.value = document.value.communities[0]?.id || ''
}
async function uploadQr({ file, onSuccess, onError }) {
  const target = selected.value
  uploading.value = true
  try { const data = new FormData(); data.append('file', file); const result = (await adminApi.post('/platform-content/qr', data)).data.data; target.qrUrl = result.url; target.qrIsExample = false; target.qrCaption = '请使用微信扫描二维码入群。'; onSuccess(result); ElMessage.success('二维码已上传，请发布内容使其生效') }
  catch (err) { ElMessage.error(err.response?.data?.message || '上传失败'); onError(err) }
  finally { uploading.value = false }
}
onMounted(load)
</script>

<style scoped>
.entry-list strong,.appearance-options strong,.page-heading h2{font-weight:400}
.page-heading{display:flex;align-items:center;gap:14px;margin-bottom:18px}.page-heading h2{margin:0 auto 0 0}.page-heading>span{font-size:13px;color:#697586}.notice{margin-bottom:18px}.communities-editor{display:grid;grid-template-columns:240px minmax(0,1fr);gap:18px}.entry-list{display:flex;flex-direction:column;gap:8px}.entry-list>button:not(.el-button){text-align:left;padding:14px;border:1px solid #d5dce3;background:#fff;cursor:pointer;color:#344353}.entry-list>button.selected{border-color:#409eff;background:#ecf5ff}.entry-list strong,.entry-list span{display:block}.entry-list span{margin-top:6px;font-size:12px;color:#6c7783}.fields{display:grid;grid-template-columns:1fr 1fr;gap:0 18px}.fields :deep(.el-select),.fields :deep(.el-input-number),.fields :deep(.el-date-editor){width:100%}.qr-editor{display:flex;gap:24px;align-items:flex-start;margin:4px 0 22px}.qr-editor img{width:170px;height:170px;object-fit:contain;border:1px solid #d9dfe5;background:white}.qr-editor>div{flex:1}.qr-editor p{font-size:12px;color:#718092}.qr-editor :deep(.el-textarea){margin-top:12px}.form-actions{display:flex;justify-content:space-between;margin-top:20px}.credit-row{margin-bottom:14px}.credit-heading{display:flex;gap:12px;margin-bottom:12px;align-items:center}.credit-heading>.el-input{flex:1}.closing{margin-top:20px}.appearance-options{display:grid;gap:18px;margin:10px 0 25px}.appearance-options strong{display:block;margin-bottom:12px}@media(max-width:1000px){.communities-editor{grid-template-columns:1fr}.entry-list{display:grid;grid-template-columns:1fr 1fr}.fields{grid-template-columns:1fr}.page-heading{flex-wrap:wrap}.qr-editor{flex-wrap:wrap}}
</style>
