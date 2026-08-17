<template>
  <div class="activities-page">
    <h2 class="page-title">活动设计</h2>
    <el-alert
      class="hint"
      type="info"
      :closable="false"
      title="活动以静态文件发布：封面和正文图写入 /activity-assets，游戏客户端走 HTTP 拉取，不经过 WebSocket。"
    />

    <div class="layout">
      <el-card class="list-card" shadow="never">
        <template #header>
          <div class="card-head">
            <span>专栏列表</span>
            <el-button type="primary" size="small" :loading="creating" @click="createDraft">
              新建活动
            </el-button>
          </div>
        </template>
        <el-table
          :data="items"
          v-loading="loading"
          size="small"
          highlight-current-row
          @current-change="onSelect"
        >
          <el-table-column label="封面" width="72">
            <template #default="{ row }">
              <img v-if="row.cover_url" class="thumb" :src="row.cover_url" alt="" />
              <div v-else class="thumb empty">无图</div>
            </template>
          </el-table-column>
          <el-table-column prop="title" label="名称" min-width="140" show-overflow-tooltip />
          <el-table-column label="状态" width="80">
            <template #default="{ row }">
              <el-tag :type="row.published ? 'success' : 'info'" size="small">
                {{ row.published ? '已上架' : '草稿' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="sort" label="排序" width="64" />
        </el-table>
      </el-card>

      <el-card class="editor-card" shadow="never">
        <template #header>
          <div class="card-head">
            <span>{{ form.id ? '编辑活动' : '请选择或新建活动' }}</span>
            <el-space v-if="form.id">
              <el-button :loading="saving" type="primary" @click="save">保存</el-button>
              <el-button :loading="removing" type="danger" plain @click="remove">删除</el-button>
            </el-space>
          </div>
        </template>

        <template v-if="form.id">
          <el-form label-width="88px" @submit.prevent>
            <el-form-item label="活动名称">
              <el-input v-model="form.title" maxlength="80" show-word-limit />
            </el-form-item>
            <el-form-item label="排序">
              <el-input-number v-model="form.sort" :min="0" :max="9999" />
              <span class="field-hint">数字越小越靠前</span>
            </el-form-item>
            <el-form-item label="上架">
              <el-switch v-model="form.published" />
            </el-form-item>
            <el-form-item label="封面">
              <div class="cover-row">
                <img v-if="form.cover_url" class="cover-preview" :src="form.cover_url" alt="" />
                <div v-else class="cover-preview empty">未上传</div>
                <el-space wrap>
                  <el-upload
                    :show-file-list="false"
                    accept="image/jpeg,image/png,image/webp,image/gif"
                    :http-request="uploadCover"
                  >
                    <el-button :loading="uploadingCover">
                      {{ form.cover_url ? '更换封面' : '上传封面' }}
                    </el-button>
                  </el-upload>
                  <el-button
                    v-if="form.cover_url"
                    :loading="removingCover"
                    @click="removeCover"
                  >
                    取消封面
                  </el-button>
                </el-space>
              </div>
              <div class="field-hint">单张不超过 2MB，建议 16:9</div>
            </el-form-item>
            <el-form-item label="正文">
              <el-input
                v-model="form.body"
                type="textarea"
                :rows="8"
                maxlength="20000"
                show-word-limit
                placeholder="玩家点开活动后，标题下方展示这段文字"
              />
            </el-form-item>
            <el-form-item label="正文图片">
              <div class="body-images">
                <div v-for="url in form.image_urls" :key="url" class="body-image">
                  <img :src="url" alt="" />
                  <el-button
                    class="remove-image"
                    type="danger"
                    plain
                    size="small"
                    :loading="removingImageUrl === url"
                    @click="removeImage(url)"
                  >
                    取消上传
                  </el-button>
                </div>
              </div>
              <el-upload
                :show-file-list="false"
                accept="image/jpeg,image/png,image/webp,image/gif"
                :http-request="uploadBodyImage"
              >
                <el-button :loading="uploadingImage">添加图片</el-button>
              </el-upload>
            </el-form-item>
          </el-form>

          <div class="preview-block">
            <div class="preview-label">客户端预览</div>
            <div class="preview-card">
              <div class="preview-cover" :style="coverStyle">{{ form.cover_url ? '' : '封面' }}</div>
              <div class="preview-title">{{ form.title || '活动名称' }}</div>
            </div>
            <div class="preview-detail">
              <div class="preview-detail-head">{{ form.title || '活动名称' }}</div>
              <pre class="preview-body">{{ form.body || '正文将显示在这里' }}</pre>
            </div>
          </div>
        </template>
        <el-empty v-else description="从左侧选择活动，或新建一个专栏标签" />
      </el-card>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'

const items = ref([])
const loading = ref(false)
const creating = ref(false)
const saving = ref(false)
const removing = ref(false)
const uploadingCover = ref(false)
const uploadingImage = ref(false)
const removingCover = ref(false)
const removingImageUrl = ref('')

const form = reactive({
  id: '',
  title: '',
  body: '',
  cover_url: '',
  image_urls: [],
  published: false,
  sort: 0,
})

const coverStyle = computed(() =>
  form.cover_url
    ? { backgroundImage: `url(${form.cover_url})` }
    : {}
)

function applyItem(item) {
  form.id = item.id
  form.title = item.title || ''
  form.body = item.body || ''
  form.cover_url = item.cover_url || ''
  form.image_urls = Array.isArray(item.image_urls) ? [...item.image_urls] : []
  form.published = !!item.published
  form.sort = Number(item.sort) || 0
}

function clearForm() {
  form.id = ''
  form.title = ''
  form.body = ''
  form.cover_url = ''
  form.image_urls = []
  form.published = false
  form.sort = 0
}

async function loadList(selectId) {
  loading.value = true
  try {
    const res = await adminApi.get('/activities')
    items.value = res.data.data?.items || []
    const keepId = selectId || form.id
    const current = items.value.find((row) => row.id === keepId)
    if (current) applyItem(current)
    else if (!items.value.some((row) => row.id === form.id)) clearForm()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载活动失败')
  } finally {
    loading.value = false
  }
}

function onSelect(row) {
  if (row) applyItem(row)
}

async function createDraft() {
  creating.value = true
  try {
    const res = await adminApi.post('/activities', {
      title: '未命名活动',
      body: '',
      published: false,
      sort: items.value.length,
    })
    const created = res.data.data
    if (created?.id) applyItem(created)
    ElMessage.success(res.data.message || '已创建')
    await loadList(created?.id)
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '创建失败')
  } finally {
    creating.value = false
  }
}

async function save() {
  if (!form.id) return
  saving.value = true
  try {
    const res = await adminApi.put(`/activities/${form.id}`, {
      title: form.title,
      body: form.body,
      published: form.published,
      sort: form.sort,
    })
    applyItem(res.data.data)
    ElMessage.success(res.data.message || '已保存')
    await loadList(form.id)
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '保存失败')
  } finally {
    saving.value = false
  }
}

async function remove() {
  if (!form.id) return
  try {
    await ElMessageBox.confirm(`确认删除活动「${form.title}」？静态图片也会一并删除。`, '删除活动', {
      type: 'warning',
    })
  } catch {
    return
  }
  removing.value = true
  try {
    await adminApi.delete(`/activities/${form.id}`)
    ElMessage.success('已删除')
    clearForm()
    await loadList()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '删除失败')
  } finally {
    removing.value = false
  }
}

async function uploadCover({ file }) {
  if (!form.id) return
  uploadingCover.value = true
  try {
    const data = new FormData()
    data.append('file', file)
    const res = await adminApi.post(`/activities/${form.id}/cover`, data)
    applyItem(res.data.data)
    ElMessage.success('封面已更新')
    await loadList(form.id)
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '封面上传失败')
  } finally {
    uploadingCover.value = false
  }
}

async function uploadBodyImage({ file }) {
  if (!form.id) return
  uploadingImage.value = true
  try {
    const data = new FormData()
    data.append('file', file)
    const res = await adminApi.post(`/activities/${form.id}/images`, data)
    applyItem(res.data.data)
    ElMessage.success('图片已添加')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '图片上传失败')
  } finally {
    uploadingImage.value = false
  }
}

async function removeCover() {
  if (!form.id || !form.cover_url) return
  removingCover.value = true
  try {
    const res = await adminApi.delete(`/activities/${form.id}/cover`)
    applyItem(res.data.data)
    ElMessage.success('封面已取消')
    await loadList(form.id)
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '取消封面失败')
  } finally {
    removingCover.value = false
  }
}

async function removeImage(url) {
  const filename = url.split('/').pop()
  if (!filename || !form.id) return
  removingImageUrl.value = url
  try {
    const res = await adminApi.delete(`/activities/${form.id}/images/${encodeURIComponent(filename)}`)
    applyItem(res.data.data)
    ElMessage.success('图片已取消')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '取消图片失败')
  } finally {
    removingImageUrl.value = ''
  }
}

onMounted(() => {
  loadList()
})
</script>

<style scoped>
.page-title {
  margin: 0 0 16px;
}
.hint {
  margin-bottom: 16px;
}
.layout {
  display: grid;
  grid-template-columns: minmax(280px, 360px) minmax(0, 1fr);
  gap: 16px;
  align-items: start;
}
.card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.thumb {
  width: 48px;
  height: 32px;
  object-fit: cover;
  border-radius: 4px;
  background: #ebeef5;
}
.thumb.empty {
  display: flex;
  align-items: center;
  justify-content: center;
  color: #909399;
  font-size: 12px;
}
.field-hint {
  margin-left: 8px;
  color: #909399;
  font-size: 12px;
}
.cover-row {
  display: flex;
  align-items: center;
  gap: 12px;
}
.cover-preview {
  width: 160px;
  height: 90px;
  object-fit: cover;
  border-radius: 6px;
  background: #ebeef5;
}
.cover-preview.empty {
  display: flex;
  align-items: center;
  justify-content: center;
  color: #909399;
  font-size: 13px;
}
.body-images {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  margin-bottom: 8px;
}
.body-image {
  width: 120px;
}
.body-image .remove-image {
  width: 100%;
  margin-top: 6px;
}
.body-image img {
  width: 120px;
  height: 80px;
  object-fit: cover;
  border-radius: 6px;
  display: block;
  background: #ebeef5;
}
.preview-block {
  margin-top: 8px;
  padding-top: 16px;
  border-top: 1px solid #ebeef5;
}
.preview-label {
  color: #909399;
  font-size: 13px;
  margin-bottom: 10px;
}
.preview-card {
  width: 280px;
  background: #111827;
  border-radius: 10px;
  overflow: hidden;
  margin-bottom: 12px;
}
.preview-cover {
  height: 132px;
  background: #1f2937 center/cover no-repeat;
  color: #6b7280;
  display: flex;
  align-items: center;
  justify-content: center;
}
.preview-title {
  color: #fde68a;
  padding: 10px 12px 12px;
  font-weight: 600;
}
.preview-detail {
  max-width: 420px;
  background: #111827;
  border-radius: 10px;
  padding: 12px 14px;
}
.preview-detail-head {
  color: #fff;
  font-size: 16px;
  font-weight: 600;
  margin-bottom: 8px;
}
.preview-body {
  margin: 0;
  color: #e5e7eb;
  white-space: pre-wrap;
  font-family: inherit;
  font-size: 13px;
  line-height: 1.6;
}
@media (max-width: 960px) {
  .layout {
    grid-template-columns: 1fr;
  }
}
</style>
