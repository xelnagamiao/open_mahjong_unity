<template>
  <div class="activities-page">
    <h2 class="page-title">活动设计</h2>
    <el-alert
      class="hint"
      type="info"
      :closable="false"
      title="保存只更新文案和图片，不会改变发布状态。正文可按任意顺序穿插文本、小图和大图。新建为草稿，发布、结束、下架在页面底部单独操作。"
    />

    <div class="layout">
      <el-card class="list-card" shadow="never">
        <template #header>
          <div class="card-head">
            <span>专栏列表</span>
            <el-button type="primary" size="small" :loading="creating" @click="createDraft">
              新建草稿
            </el-button>
          </div>
        </template>
        <el-table
          :data="items"
          row-key="id"
          v-loading="loading"
          size="small"
          highlight-current-row
          :current-row-key="form.id || undefined"
          @current-change="onSelect"
        >
          <el-table-column label="标题图" width="72">
            <template #default="{ row }">
              <img v-if="row.cover_url" class="thumb" :src="row.cover_url" alt="" />
              <div v-else class="thumb empty">无图</div>
            </template>
          </el-table-column>
          <el-table-column prop="title" label="名称" min-width="140" show-overflow-tooltip />
          <el-table-column label="状态" width="96">
            <template #default="{ row }">
              <el-tag :type="statusMeta(row.status).type" size="small">
                {{ statusMeta(row.status).label }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="sort" label="排序" width="64" />
        </el-table>
      </el-card>

      <el-card class="editor-card" shadow="never">
        <template #header>
          <div class="card-head">
            <div class="editor-title">
              <span>{{ form.id ? '编辑活动' : '请选择或新建活动' }}</span>
              <el-tag v-if="form.id" :type="statusMeta(form.status).type" size="small">
                {{ statusMeta(form.status).label }}
              </el-tag>
              <span v-if="dirty" class="dirty-dot">未保存</span>
            </div>
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
              <span class="field-hint">数字越大越靠前，新建会自动取当前最大值 + 1</span>
            </el-form-item>
            <el-form-item label="标题图片">
              <div class="cover-row">
                <img v-if="form.cover_url" class="cover-preview" :src="form.cover_url" alt="" />
                <div v-else class="cover-preview empty">未上传</div>
                <el-space wrap>
                  <el-upload
                    :show-file-list="false"
                    accept="image/jpeg,image/png,image/webp,image/gif"
                    :http-request="pickCover"
                  >
                    <el-button :loading="uploadingCover">
                      {{ form.cover_url ? '裁切并更换' : '裁切并上传' }}
                    </el-button>
                  </el-upload>
                  <el-button
                    v-if="form.cover_url"
                    :loading="removingCover"
                    @click="removeCover"
                  >
                    取消图片
                  </el-button>
                </el-space>
              </div>
              <div class="field-hint">上传后裁切到约 2.2:1，输出不超过 2MB</div>
            </el-form-item>
            <el-form-item label="正文">
              <div class="block-editor">
                <p class="block-hint">
                  文本、小图、大图可任意穿插。小图按正常尺寸居中；大图固定满宽、高度按原图比例。正文图不超过 12MB。
                </p>
                <div class="block-toolbar">
                  <el-button size="small" @click="addTextBlock">添加文本</el-button>
                  <el-upload
                    class="block-upload"
                    :show-file-list="false"
                    accept="image/jpeg,image/png,image/webp,image/gif"
                    :http-request="(opt) => uploadBodyImage(opt, 'small')"
                  >
                    <el-button size="small" :loading="uploadingImage === 'small'">上传小图</el-button>
                  </el-upload>
                  <el-upload
                    class="block-upload"
                    :show-file-list="false"
                    accept="image/jpeg,image/png,image/webp,image/gif"
                    :http-request="(opt) => uploadBodyImage(opt, 'large')"
                  >
                    <el-button size="small" :loading="uploadingImage === 'large'">上传大图</el-button>
                  </el-upload>
                </div>
                <div v-if="!form.blocks.length" class="block-empty">还没有正文，先添加文本或图片</div>
                <div
                  v-for="(block, index) in form.blocks"
                  :key="block.key"
                  class="block-card"
                >
                <div class="block-head">
                  <span class="block-type">{{ blockLabel(block) }}</span>
                  <el-space wrap>
                    <template v-if="block.type === 'text'">
                      <span class="block-size-label">字号</span>
                      <el-select v-model="block.fontSize" size="small" style="width: 88px">
                        <el-option
                          v-for="size in FONT_SIZES"
                          :key="size"
                          :label="`${size}px`"
                          :value="size"
                        />
                      </el-select>
                      <el-button size="small" @click="insertLink(index)">插入链接</el-button>
                    </template>
                    <el-button size="small" :disabled="index === 0" @click="moveBlock(index, -1)">上移</el-button>
                    <el-button
                      size="small"
                      :disabled="index === form.blocks.length - 1"
                      @click="moveBlock(index, 1)"
                    >
                      下移
                    </el-button>
                    <el-button
                      size="small"
                      type="danger"
                      plain
                      :loading="removingImageUrl === block.url"
                      @click="removeBlock(index)"
                    >
                      删除
                    </el-button>
                  </el-space>
                </div>
                <el-input
                  v-if="block.type === 'text'"
                  :ref="(el) => setTextRef(index, el)"
                  v-model="block.text"
                  type="textarea"
                  :rows="5"
                  maxlength="8000"
                  show-word-limit
                  placeholder="正文。选中文字后点「插入链接」，格式为 [文字](https://…)"
                />
                <div v-else class="block-image">
                  <img
                    :src="block.url"
                    alt=""
                    :class="block.size === 'large' ? 'is-large' : 'is-small'"
                  />
                  <el-input
                    v-model="block.href"
                    size="small"
                    placeholder="点击图片打开的链接，可空"
                    @blur="persistContent"
                  />
                </div>
              </div>
              </div>
            </el-form-item>
          </el-form>

          <div class="preview-block">
            <div class="preview-label">客户端场景预览（按通知页布局缩放）</div>
            <div ref="sceneWrap" class="scene-wrap" :style="{ height: `${900 * sceneScale}px` }">
              <div class="scene-canvas" :style="{ transform: `scale(${sceneScale})` }">
                <div class="scene-gutter" />
                <div class="scene-sidebar">
                  <div
                    v-for="row in sceneList"
                    :key="row.id"
                    class="preview-card"
                    :class="{
                      selected: row.id === form.id,
                      dimmed: row.status === 'offline' || row.status === 'draft',
                    }"
                  >
                    <div
                      class="preview-cover"
                      :style="row.cover_url ? { backgroundImage: `url(${row.cover_url})` } : {}"
                    >
                      {{ row.cover_url ? '' : '标题图片' }}
                    </div>
                    <div class="preview-title">{{ row.title || '活动名称' }}</div>
                  </div>
                </div>
                <div class="preview-detail">
                  <div class="preview-detail-head">{{ form.title || '活动名称' }}</div>
                  <div v-if="form.status === 'ended'" class="preview-ended">活动已结束</div>
                  <div class="preview-body-flow">
                    <template v-for="block in form.blocks" :key="block.key">
                      <div
                        v-if="block.type === 'text' && block.text"
                        class="preview-body"
                        :style="{ fontSize: `${block.fontSize || 22}px` }"
                        v-html="textToHtml(block.text)"
                      />
                      <a
                        v-else-if="block.type === 'image'"
                        class="preview-image"
                        :class="block.size === 'small' ? 'is-small' : 'is-large'"
                        :href="block.href || undefined"
                        :target="block.href ? '_blank' : undefined"
                        :rel="block.href ? 'noopener noreferrer' : undefined"
                        @click="onPreviewImageClick($event, block)"
                      >
                        <img :src="block.url" alt="" />
                        <span v-if="block.href" class="preview-image-link">点击跳转</span>
                      </a>
                    </template>
                  </div>
                </div>
                <div class="scene-gutter" />
              </div>
            </div>
            <div v-if="form.status === 'draft'" class="preview-hidden">草稿，通知页不显示</div>
            <div v-if="form.status === 'offline'" class="preview-hidden">已下架，通知页不显示</div>
          </div>

          <div class="lifecycle">
            <div class="lifecycle-title">发布与下架</div>
            <p class="lifecycle-hint">{{ lifecycleHint }}</p>
            <el-space wrap>
              <el-button
                v-if="form.status === 'draft' || form.status === 'offline'"
                type="primary"
                :loading="changingStatus"
                @click="changeStatus('published', publishConfirm)"
              >
                {{ form.status === 'offline' ? '重新发布' : '发布到通知页' }}
              </el-button>
              <el-button
                v-if="form.status === 'ended'"
                :loading="changingStatus"
                @click="changeStatus('published', reopenConfirm)"
              >
                恢复进行中
              </el-button>
              <el-button
                v-if="form.status === 'published'"
                type="warning"
                plain
                :loading="changingStatus"
                @click="changeStatus('ended', endConfirm)"
              >
                结束活动
              </el-button>
              <el-button
                v-if="form.status === 'published' || form.status === 'ended'"
                :loading="changingStatus"
                @click="changeStatus('offline', offlineConfirm)"
              >
                下架活动
              </el-button>
            </el-space>
          </div>
        </template>
        <el-empty v-else description="从左侧选择活动，或新建一份草稿" />
      </el-card>
    </div>

    <CoverCropDialog
      v-model="cropOpen"
      :file="cropFile"
      @confirm="uploadCroppedCover"
    />
  </div>
</template>

<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'
import CoverCropDialog from '@/components/CoverCropDialog.vue'

const STATUS_META = {
  draft: { label: '草稿', type: 'info' },
  published: { label: '已发布', type: 'success' },
  ended: { label: '已结束', type: 'warning' },
  offline: { label: '已下架', type: 'danger' },
}

const FONT_SIZES = [14, 18, 22, 26, 30, 36]

const items = ref([])
const loading = ref(false)
const creating = ref(false)
const saving = ref(false)
const removing = ref(false)
const changingStatus = ref(false)
const uploadingCover = ref(false)
const uploadingImage = ref('')
const removingCover = ref(false)
const removingImageUrl = ref('')
const savedSnapshot = ref('')
const cropOpen = ref(false)
const cropFile = ref(null)
const sceneWrap = ref(null)
const sceneScale = ref(0.48)
const textRefs = []

const form = reactive({
  id: '',
  title: '',
  cover_url: '',
  blocks: [],
  status: 'draft',
  sort: 0,
})

const dirty = computed(() => !!form.id && snapshotOf(form) !== savedSnapshot.value)

const sceneList = computed(() => {
  const byId = new Map()
  for (const row of items.value) {
    const visible = row.status === 'published' || row.status === 'ended' || row.id === form.id
    if (!visible) continue
    byId.set(row.id, row)
  }
  if (form.id) {
    byId.set(form.id, {
      ...byId.get(form.id),
      id: form.id,
      title: form.title,
      cover_url: form.cover_url,
      status: form.status,
      sort: form.sort,
    })
  }
  return [...byId.values()].sort((a, b) => (Number(b.sort) || 0) - (Number(a.sort) || 0))
})

const lifecycleHint = computed(() => {
  if (form.status === 'published') return '玩家打开通知即可看到。结束活动仍会显示，但会标明「活动已结束」；下架后通知页不再出现。'
  if (form.status === 'ended') return '玩家仍能在通知页看到，并显示「活动已结束」。下架后不再显示。'
  if (form.status === 'offline') return '通知页已不再显示。内容仍保留，可以重新发布。'
  return '当前是草稿。点保存只留下内容；点「发布到通知页」后玩家才能看到。'
})

function statusMeta(status) {
  return STATUS_META[status] || STATUS_META.draft
}

function blockLabel(block) {
  if (block.type === 'text') return '文本'
  return block.size === 'small' ? '小图' : '大图'
}

function newKey(prefix) {
  return `${prefix}-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`
}

function toBlocks(item) {
  if (Array.isArray(item?.blocks) && item.blocks.length) {
    return item.blocks.map((row, index) => {
      if (row.type === 'image') {
        return {
          type: 'image',
          size: row.size === 'small' ? 'small' : 'large',
          url: row.url,
          href: row.href || '',
          key: row.url || newKey(`img${index}`),
        }
      }
      return {
        type: 'text',
        text: row.text || '',
        fontSize: Number(row.fontSize) || 22,
        key: newKey(`text${index}`),
      }
    })
  }
  const blocks = []
  if (item?.body) {
    blocks.push({ type: 'text', text: item.body, fontSize: 22, key: newKey('text') })
  }
  const images = Array.isArray(item?.images) ? item.images : []
  for (const image of images) {
    const url = typeof image === 'string' ? image : image.url
    if (!url) continue
    blocks.push({
      type: 'image',
      size: image.size === 'small' ? 'small' : 'large',
      url,
      href: typeof image === 'string' ? '' : image.href || '',
      key: url,
    })
  }
  return blocks
}

function payloadBlocks() {
  return form.blocks.map((block) => {
    if (block.type === 'image') {
      return { type: 'image', size: block.size === 'small' ? 'small' : 'large', url: block.url, href: block.href || '' }
    }
    return { type: 'text', text: block.text || '', fontSize: Number(block.fontSize) || 22 }
  })
}

function snapshotOf(item) {
  return JSON.stringify({
    title: item.title || '',
    sort: Number(item.sort) || 0,
    blocks: (item.blocks || []).map((block) => {
      if (block.type === 'image') {
        return { type: 'image', size: block.size, url: block.url, href: block.href || '' }
      }
      return { type: 'text', text: block.text || '', fontSize: Number(block.fontSize) || 22 }
    }),
  })
}

function applyItem(item, { keepBlocks } = {}) {
  form.id = item.id
  form.title = item.title || ''
  form.cover_url = item.cover_url || ''
  form.status = item.status || (item.published ? 'published' : 'draft')
  form.sort = Number(item.sort) || 0
  if (!keepBlocks) form.blocks = toBlocks(item)
  savedSnapshot.value = snapshotOf(form)
}

function patchListRow(id, patch) {
  items.value = items.value.map((row) => (row.id === id ? { ...row, ...patch } : row))
}

function syncListFromForm() {
  if (!form.id) return
  patchListRow(form.id, {
    title: form.title,
    cover_url: form.cover_url,
    status: form.status,
    sort: form.sort,
    blocks: payloadBlocks(),
  })
  items.value = sortItems(items.value)
}

function clearForm() {
  form.id = ''
  form.title = ''
  form.cover_url = ''
  form.blocks = []
  form.status = 'draft'
  form.sort = 0
  savedSnapshot.value = ''
}

function contentPayload() {
  return {
    title: form.title,
    sort: form.sort,
    blocks: payloadBlocks(),
  }
}

function isSafeHref(href) {
  const value = String(href || '').trim()
  if (!value) return false
  const lower = value.toLowerCase()
  if (lower.startsWith('javascript:') || lower.startsWith('data:') || lower.startsWith('vbscript:')) return false
  return /^https?:\/\//i.test(value) || value.startsWith('/')
}

function escapeHtml(value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

function textToHtml(text) {
  const src = String(text || '')
  const re = /\[([^\]]+)\]\(([^)]+)\)/g
  let out = ''
  let last = 0
  let match
  while ((match = re.exec(src))) {
    out += escapeHtml(src.slice(last, match.index)).replace(/\n/g, '<br>')
    const label = escapeHtml(match[1])
    const href = String(match[2] || '').trim()
    if (isSafeHref(href)) {
      out += `<a href="${escapeHtml(href)}" target="_blank" rel="noopener noreferrer">${label}</a>`
    } else {
      out += label
    }
    last = match.index + match[0].length
  }
  out += escapeHtml(src.slice(last)).replace(/\n/g, '<br>')
  return out
}

function setTextRef(index, el) {
  textRefs[index] = el
}

function addTextBlock() {
  if (form.blocks.length >= 40) {
    ElMessage.error('正文块不能超过 40 个')
    return
  }
  form.blocks.push({ type: 'text', text: '', fontSize: 22, key: newKey('text') })
}

function moveBlock(index, delta) {
  const next = index + delta
  if (next < 0 || next >= form.blocks.length) return
  const copy = form.blocks.splice(index, 1)[0]
  form.blocks.splice(next, 0, copy)
}

async function removeBlock(index) {
  const block = form.blocks[index]
  if (block?.type === 'image' && block.url) {
    const filename = block.url.split('/').pop()
    if (filename && form.id) {
      removingImageUrl.value = block.url
      try {
        await adminApi.delete(`/activities/${form.id}/images/${encodeURIComponent(filename)}`)
      } catch (e) {
        ElMessage.error(e.response?.data?.message || '取消图片失败')
        removingImageUrl.value = ''
        return
      } finally {
        removingImageUrl.value = ''
      }
    }
  }
  form.blocks.splice(index, 1)
  if (block?.type === 'image') await persistContent()
}

async function insertLink(index) {
  const block = form.blocks[index]
  if (!block || block.type !== 'text') return
  const comp = textRefs[index]
  const textarea = comp?.textarea || comp?.$el?.querySelector?.('textarea')
  const start = textarea?.selectionStart ?? block.text.length
  const end = textarea?.selectionEnd ?? block.text.length
  const selected = block.text.slice(start, end) || '链接文字'
  try {
    const { value } = await ElMessageBox.prompt('填写 https://、http:// 或 / 开头的地址', '插入链接', {
      confirmButtonText: '插入',
      inputPattern: /^(https?:\/\/|\/)\S+$/i,
      inputErrorMessage: '链接需以 https://、http:// 或 / 开头',
    })
    const wrapped = `[${selected}](${value.trim()})`
    block.text = `${block.text.slice(0, start)}${wrapped}${block.text.slice(end)}`
    await nextTick()
    if (textarea) {
      const caret = start + wrapped.length
      textarea.focus()
      textarea.setSelectionRange(caret, caret)
    }
  } catch {
    /* cancel */
  }
}

async function loadList(selectId, { apply = true } = {}) {
  loading.value = true
  try {
    const res = await adminApi.get('/activities')
    const remote = res.data.data?.items || []
    const keepId = selectId || form.id
    items.value = sortItems(mergeLocalItem(remote, keepId))
    const current = items.value.find((row) => row.id === keepId)
    if (!current) return
    if (apply && !(form.id === current.id && dirty.value)) {
      applyItem(current)
    } else if (form.id === current.id) {
      form.status = current.status || form.status
      syncListFromForm()
    }
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载活动失败')
  } finally {
    loading.value = false
  }
}

function sortItems(list) {
  return list.slice().sort((a, b) => {
    const sortA = Number(a.sort) || 0
    const sortB = Number(b.sort) || 0
    if (sortA !== sortB) return sortB - sortA
    return String(b.updated_at || '').localeCompare(String(a.updated_at || ''))
  })
}

function mergeLocalItem(remote, keepId) {
  if (!keepId) return remote
  if (remote.some((row) => row.id === keepId)) return remote
  const local = items.value.find((row) => row.id === keepId)
  if (!local && form.id !== keepId) return remote
  const fallback = local || {
    id: form.id,
    title: form.title,
    cover_url: form.cover_url,
    blocks: payloadBlocks(),
    status: form.status,
    sort: form.sort,
  }
  return [fallback, ...remote.filter((row) => row.id !== fallback.id)]
}

function upsertItem(item) {
  if (!item?.id) return
  items.value = sortItems([item, ...items.value.filter((row) => row.id !== item.id)])
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
    })
    const created = res.data.data
    if (created?.id) {
      upsertItem(created)
      applyItem(created)
    }
    ElMessage.success(res.data.message || '已创建草稿')
    await loadList(created?.id, { apply: false })
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
    const res = await adminApi.put(`/activities/${form.id}`, contentPayload())
    applyItem(res.data.data)
    ElMessage.success(res.data.message || '内容已保存')
    await loadList(form.id, { apply: false })
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '保存失败')
  } finally {
    saving.value = false
  }
}

async function persistContent() {
  if (!form.id) return
  try {
    await adminApi.put(`/activities/${form.id}`, contentPayload())
    savedSnapshot.value = snapshotOf(form)
    syncListFromForm()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '保存失败')
  }
}

const publishConfirm = {
  title: '发布到通知页',
  message: '发布后玩家打开通知即可看到该活动。确认发布？',
}

const reopenConfirm = {
  title: '恢复进行中',
  message: '将取消「活动已结束」标记，玩家会再次看到进行中的活动。确认恢复？',
}

const endConfirm = {
  title: '结束活动',
  message: '结束后玩家仍能在通知页看到，但会显示「活动已结束」。确认结束？',
}

const offlineConfirm = {
  title: '下架活动',
  message: '下架后通知页不再显示该活动，内容仍保留，之后可以重新发布。确认下架？',
}

async function changeStatus(status, confirm) {
  if (!form.id) return
  try {
    await ElMessageBox.confirm(confirm.message, confirm.title, { type: 'warning' })
  } catch {
    return
  }
  changingStatus.value = true
  try {
    if (dirty.value) {
      await adminApi.put(`/activities/${form.id}`, contentPayload())
    }
    const res = await adminApi.post(`/activities/${form.id}/status`, { status })
    applyItem(res.data.data)
    ElMessage.success(res.data.message || '状态已更新')
    await loadList(form.id, { apply: false })
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '更新状态失败')
  } finally {
    changingStatus.value = false
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

function pickCover({ file }) {
  cropFile.value = file
  cropOpen.value = true
}

async function uploadCroppedCover(file) {
  if (!form.id) return
  uploadingCover.value = true
  try {
    const data = new FormData()
    data.append('file', file)
    const res = await adminApi.post(`/activities/${form.id}/cover`, data)
    form.cover_url = res.data.data?.cover_url || form.cover_url
    syncListFromForm()
    ElMessage.success('标题图片已更新')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '封面上传失败')
  } finally {
    uploadingCover.value = false
  }
}

async function uploadBodyImage({ file }, size) {
  if (!form.id) return
  if (form.blocks.length >= 40) {
    ElMessage.error('正文块不能超过 40 个')
    return
  }
  if (form.blocks.filter((row) => row.type === 'image').length >= 30) {
    ElMessage.error('正文图片最多 30 张')
    return
  }
  uploadingImage.value = size
  try {
    const data = new FormData()
    data.append('file', file)
    const res = await adminApi.post(`/activities/${form.id}/images`, data)
    const url = res.data.url
    if (!url) throw new Error('未返回图片地址')
    form.blocks.push({
      type: 'image',
      size: size === 'small' ? 'small' : 'large',
      url,
      href: '',
      key: url,
    })
    await persistContent()
    ElMessage.success(size === 'small' ? '小图已添加' : '大图已添加')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || e.message || '图片上传失败')
  } finally {
    uploadingImage.value = ''
  }
}

async function removeCover() {
  if (!form.id || !form.cover_url) return
  removingCover.value = true
  try {
    const res = await adminApi.delete(`/activities/${form.id}/cover`)
    form.cover_url = res.data.data?.cover_url || ''
    syncListFromForm()
    ElMessage.success('封面已取消')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '取消封面失败')
  } finally {
    removingCover.value = false
  }
}

function onPreviewImageClick(event, image) {
  if (!image.href) event.preventDefault()
}

let sceneObserver = null

function bindSceneObserver(el) {
  if (sceneObserver) sceneObserver.disconnect()
  if (!el) return
  sceneObserver = new ResizeObserver(() => {
    sceneScale.value = Math.max(0.28, el.clientWidth / 1920)
  })
  sceneObserver.observe(el)
  sceneScale.value = Math.max(0.28, el.clientWidth / 1920)
}

watch(sceneWrap, (el) => bindSceneObserver(el))

onMounted(() => {
  loadList()
  bindSceneObserver(sceneWrap.value)
})

onBeforeUnmount(() => {
  if (sceneObserver) sceneObserver.disconnect()
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
.list-card {
  min-width: 0;
}
.list-card :deep(.el-card__body) {
  padding: 8px 12px 12px;
}
.list-card :deep(.el-table) {
  width: 100%;
}
.list-card :deep(.el-table__body-wrapper) {
  max-height: min(70vh, 640px);
  overflow-y: auto;
}
.card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.editor-title {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.editor-card :deep(.el-form-item__content) {
  display: block;
}
.dirty-dot {
  color: #e6a23c;
  font-size: 12px;
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
  width: 176px;
  height: 80px;
  object-fit: cover;
  border-radius: 6px;
  background: #242e42;
}
.cover-preview.empty {
  display: flex;
  align-items: center;
  justify-content: center;
  color: #909399;
  font-size: 13px;
}
.field-hint-block {
  display: block;
  margin: 8px 0 12px;
  margin-left: 0;
}
.block-editor {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  width: 100%;
  min-width: 0;
}
.block-hint {
  display: block;
  width: 100%;
  margin: 0 0 10px;
  color: #909399;
  font-size: 12px;
  line-height: 1.6;
}
.block-toolbar {
  display: flex;
  flex-direction: row;
  flex-wrap: nowrap;
  align-items: center;
  gap: 8px;
  width: 100%;
  margin-bottom: 12px;
}
.block-upload {
  display: inline-flex;
  align-items: center;
  line-height: 1;
}
.block-upload :deep(.el-upload) {
  display: inline-flex;
  align-items: center;
  line-height: 1;
}
.block-toolbar :deep(.el-button) {
  margin: 0;
  height: 24px;
}
.block-empty {
  color: #909399;
  font-size: 13px;
  padding: 16px 0;
}
.block-card {
  width: 100%;
  border: 1px solid #ebeef5;
  border-radius: 8px;
  padding: 10px 12px 12px;
  margin-bottom: 10px;
  background: #fafafa;
  box-sizing: border-box;
}
.block-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 8px;
}
.block-type {
  font-weight: 600;
  color: #303133;
}
.block-size-label {
  color: #909399;
  font-size: 12px;
}
.block-image {
  width: 100%;
}
.block-image img {
  display: block;
  border-radius: 4px;
  background: #ebeef5;
  margin-bottom: 8px;
}
.block-image img.is-small {
  max-width: 324px;
  height: auto;
  margin-left: auto;
  margin-right: auto;
}
.block-image img.is-large {
  width: 100%;
  height: auto;
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
.scene-wrap {
  width: 100%;
  overflow: hidden;
  border-radius: 8px;
  background: #07080c;
}
.scene-canvas {
  width: 1920px;
  height: 900px;
  display: grid;
  grid-template-columns: 192px 384px 960px 384px;
  transform-origin: top left;
  background: rgba(0, 0, 0, 0.39);
}
.scene-gutter {
  min-width: 0;
}
.scene-sidebar {
  padding: 18px 18px 24px;
  box-sizing: border-box;
  overflow: auto;
}
.preview-card {
  width: 348px;
  background: #141c2e;
  border-radius: 8px;
  overflow: hidden;
  padding: 12px;
  box-sizing: border-box;
  margin-bottom: 12px;
}
.preview-card.dimmed {
  opacity: 0.72;
}
.preview-card.selected {
  background: #ffd16b;
}
.preview-cover {
  width: 100%;
  height: 148px;
  border-radius: 4px;
  background: #242e42 center/cover no-repeat;
  color: rgba(255, 255, 255, 0.35);
  font-size: 22px;
  display: flex;
  align-items: center;
  justify-content: center;
}
.preview-title {
  color: #ffe68c;
  font-size: 24px;
  line-height: 32px;
  min-height: 32px;
  margin-top: 8px;
  font-weight: 400;
}
.preview-card.selected .preview-title {
  color: #141c2e;
}
.preview-ended {
  color: #f59e0b;
  font-size: 13px;
  padding: 0 30px;
  font-weight: 600;
}
.preview-hidden {
  color: #9ca3af;
  font-size: 12px;
  margin-top: 8px;
}
.preview-detail {
  background: rgba(13, 15, 20, 0.98);
  overflow: auto;
}
.preview-detail-head {
  color: #fff;
  font-size: 30px;
  font-weight: 400;
  line-height: 72px;
  height: 72px;
  padding: 0 30px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.preview-body-flow {
  padding: 8px 8px 24px;
  display: flex;
  flex-direction: column;
  align-items: stretch;
  width: 100%;
  gap: 12px;
}
.preview-body {
  margin: 0;
  padding: 0 16px;
  width: 100%;
  box-sizing: border-box;
  color: #ebebeb;
  font-family: inherit;
  line-height: 1.45;
  word-break: break-word;
}
.preview-body :deep(a) {
  color: #7ec8ff;
}
.preview-image {
  display: block;
  width: 100%;
  flex: none;
  color: inherit;
  text-decoration: none;
}
.preview-image.is-large img {
  width: 100%;
  height: auto;
  display: block;
}
.preview-image.is-small {
  display: flex;
  flex-direction: column;
  align-items: center;
}
.preview-image.is-small img {
  max-width: 324px;
  width: auto;
  height: auto;
  display: block;
}
.preview-image-link {
  display: block;
  color: #fbbf24;
  font-size: 12px;
  margin-top: 4px;
  text-align: center;
}
.lifecycle {
  margin-top: 24px;
  padding: 16px;
  border: 1px solid #ebeef5;
  border-radius: 8px;
  background: #fafafa;
}
.lifecycle-title {
  font-weight: 600;
  margin-bottom: 6px;
}
.lifecycle-hint {
  margin: 0 0 12px;
  color: #606266;
  font-size: 13px;
  line-height: 1.6;
}
@media (max-width: 960px) {
  .layout {
    grid-template-columns: 1fr;
  }
}
</style>
