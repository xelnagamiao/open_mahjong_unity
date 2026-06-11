import { ElMessage, ElMessageBox } from 'element-plus'

/** 蓝奏云 Android 安装包（大文件外链，不占本站带宽） */
export const MOBILE_DOWNLOAD = {
  url: 'https://wwavc.lanzoue.com/i6wXV3rose6d',
  password: '3ro1',
  title: 'salasasa 手机版下载',
}

export function promptMobileDownload() {
  ElMessageBox.confirm(
    `解压 / 访问密码：<strong style="font-size:1.1em;letter-spacing:0.08em;">${MOBILE_DOWNLOAD.password}</strong><br><br>` +
      `点击「前往下载」在新标签页打开；若浏览器拦截，请允许弹窗后重试。`,
    MOBILE_DOWNLOAD.title,
    {
      confirmButtonText: '前往下载',
      cancelButtonText: '复制密码',
      dangerouslyUseHTMLString: true,
      distinguishCancelAndClose: true,
    }
  )
    .then(() => {
      window.open(MOBILE_DOWNLOAD.url, '_blank', 'noopener,noreferrer')
    })
    .catch((action) => {
      if (action !== 'cancel') return
      navigator.clipboard
        .writeText(MOBILE_DOWNLOAD.password)
        .then(() => ElMessage.success('密码已复制到剪贴板'))
        .catch(() => ElMessage.warning(`请手动复制密码：${MOBILE_DOWNLOAD.password}`))
    })
}
