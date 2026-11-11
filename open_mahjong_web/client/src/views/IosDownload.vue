<template>
  <div class="ios-download">
    <header class="page-banner">
      <h1>iOS下载</h1>
      <p>Salasasa iPhone / iPad 测试版</p>
    </header>

    <section class="panel release-panel" aria-live="polite">
      <p v-if="loading" class="status">正在获取最新版本…</p>
      <template v-else-if="error">
        <p class="error" role="alert">{{ error }}</p>
        <el-button @click="loadRelease">重新获取</el-button>
      </template>
      <template v-else-if="release?.status === 'available'">
        <h2>发行版本：{{ release.releaseVersion }}</h2>
        <p v-if="release.versionName" class="version">应用版本：{{ release.versionName }}</p>
        <p v-if="release.buildNumber && String(release.buildNumber) !== release.releaseVersion" class="version">构建号：{{ release.buildNumber }}</p>
        <p class="status">{{ channelHint }}</p>
        <a class="install-link" :href="release.installUrl" rel="noopener noreferrer">{{ installLabel }}</a>
        <p v-if="release.notes" class="notes" data-no-translate>{{ release.notes }}</p>
      </template>
      <template v-else>
        <h2>iOS测试版准备中</h2>
        <p class="status">安装包尚未发布。发布后，这里会提供最新版本和对应的安装方式。</p>
        <el-button disabled>安装包准备中</el-button>
      </template>
      <router-link class="android-link" to="/mobile-download?platform=android">Android手机下载</router-link>
    </section>

    <section v-if="!release || release.channel === 'sideload'" class="panel guide-panel">
      <h2>免费Apple账户安装说明</h2>
      <p>IPA下载后需要签名安装，不能直接在Safari中点击文件完成安装。</p>
      <ol>
        <li>准备免费Apple账户、iPhone或iPad，以及一台Windows或Mac电脑。</li>
        <li>在电脑上安装<a href="https://sideloadly.io/" target="_blank" rel="noopener noreferrer">Sideloadly</a>，连接设备并信任电脑，将本站发布的IPA导入工具。</li>
        <li>在签名工具中使用自己的Apple账户完成安装，并按设备提示信任开发者、开启开发者模式。</li>
      </ol>
      <p>免费签名通常有效7天，到期前需通过侧载工具刷新签名。游戏没有更新时也需要续签；应用内资源更新不能延长签名期限。</p>
      <p>Apple账户密码和验证码只在你使用的签名工具中输入，本站不会收集。</p>
      <a href="https://developer.apple.com/help/account/basics/about-your-developer-account" target="_blank" rel="noopener noreferrer">查看Apple免费账户与设备限制</a>
    </section>
    <section v-else class="panel guide-panel">
      <h2>安装与更新</h2>
      <p v-if="release.channel === 'testflight'">通过上方邀请链接，在TestFlight中安装和更新。每个测试构建最长可用90天，请及时安装后续版本。</p>
      <p v-else>通过上方App Store链接安装，后续在App Store中更新。</p>
    </section>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { fetchIosRelease } from '@/utils/iosDownload'

const release = ref(null)
const loading = ref(true)
const error = ref('')
const installLabel = computed(() => ({
  sideload: '下载IPA测试包', testflight: '前往TestFlight安装', appstore: '前往App Store安装',
})[release.value?.channel] || '查看安装方式')
const channelHint = computed(() => ({
  sideload: '此版本需要使用个人Apple账户侧载安装，请先阅读下方说明。',
  testflight: '请使用iPhone或iPad打开邀请链接。',
  appstore: '请使用iPhone或iPad打开商店页面。',
})[release.value?.channel] || '')

async function loadRelease() {
  loading.value = true
  error.value = ''
  release.value = null
  try {
    release.value = await fetchIosRelease()
  } catch (cause) {
    error.value = cause?.message || '暂时无法获取iOS版本，请稍后重试。'
  } finally {
    loading.value = false
  }
}
onMounted(loadRelease)
</script>

<style scoped>
.ios-download { --accent: #596579; color: #333; font-weight: 400; }
.page-banner { background: var(--accent); color: #fff; padding: 22px 20px; }
h1, h2 { font-weight: 400; }
.page-banner h1 { margin: 0 0 6px; font-size: 1.45rem; }
.page-banner p { margin: 0; font-size: 13px; line-height: 1.5; }
.panel { background: #fff; border: 1px solid #e0e0e0; border-top: 0; padding: 28px 20px; }
.panel h2 { margin: 0 0 12px; font-size: 1.2rem; }
.release-panel { text-align: center; }
.status, .version, .notes { font-size: 14px; line-height: 1.7; overflow-wrap: anywhere; }
.status, .version { color: #666; }
.notes { white-space: pre-wrap; }
.install-link { display: inline-flex; padding: 12px 28px; background: var(--accent); color: #fff; border-radius: 4px; text-decoration: none; }
.install-link:hover { background: #435063; }
.android-link { display: block; width: fit-content; margin: 20px auto 0; }
.guide-panel { line-height: 1.8; font-size: 14px; }
.guide-panel ol { padding-left: 24px; }
.guide-panel li + li { margin-top: 10px; }
.guide-panel a, .android-link { color: #405c86; text-decoration: underline; }
.error { color: #b42318; }
.ios-download :deep(.el-button) { font-weight: 400; }
</style>
