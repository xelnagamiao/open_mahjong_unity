<!-- 使用说明：仿雀渣 FAQ 目录分节结构，文案来自 Unity AboutUs -->
<template>
  <div class="usage-guide">
    <header class="page-banner">
      <h1>使用说明</h1>
      <p>平台简介、公平机制与常用功能说明。规则细则请参阅「规则书」。</p>
    </header>

    <div class="panel">
      <nav class="toc" aria-label="目录导航">
        <div class="toc-title">导航</div>
        <ul>
          <li v-for="sec in sections" :key="sec.id">
            <a :href="`#${sec.id}`" @click.prevent="scrollTo(sec.id)">{{ sec.title }}</a>
          </li>
        </ul>
      </nav>

      <section
        v-for="sec in sections"
        :id="sec.id"
        :key="sec.id"
        class="faq-section"
      >
        <h2>{{ sec.title }}</h2>

        <template v-if="sec.id === 'intro'">
          <ol class="faq-list">
            <li>
              <p>
                open_mahjong_unity 是一款基于 Unity / Python-FastAPI 的麻将平台项目，遵循
                MIT 许可协议，免费、开源，支持 PC / 安卓 / iOS 三端互通；目标是支持所有麻将规则，并提供给玩家自定义规则的选项。欢迎加入 QQ 群
                <a href="https://qm.qq.com/q/MGGZV58hOO" target="_blank" rel="noopener noreferrer">906497522</a>
                参与讨论、协助和测试。
              </p>
            </li>
            <li>
              <p>
                Salasasa（<a href="https://salasasa.cn" target="_blank" rel="noopener noreferrer">salasasa.cn</a>）是该项目的示例服务器，目前支持国标 / 立直 / 青雀 / 川麻 / 长麻以及一些子规则。
              </p>
            </li>
            <li>
              <p>
                可通过网页版「进入平台」、
                <a href="https://store.steampowered.com/app/4565740/Salasasa/" target="_blank" rel="noopener noreferrer">Steam 商店</a>
                或
                <router-link to="/mobile-download">手机版 APK</router-link>
                开始对局。Steam 版性能更高，商店页右下角可下载试用版。
              </p>
            </li>
          </ol>
        </template>

        <template v-else-if="sec.id === 'features'">
          <ol class="faq-list">
            <li>
              <p>多规则同台：国标、立直、青雀、四川麻将、长沙麻将等，规则书与牌例可在站内查阅。</p>
            </li>
            <li>
              <p>开源可自建：项目遵循 MIT，可自行部署服务器；公共平台若因攻击等原因无法维持，规则制定者仍可自建服务。</p>
            </li>
            <li>
              <p>
                公平可验证：对局开始前公布承诺值，结束后公布主随机种子与盐值，可用站内
                <router-link to="/seed-verify">种子验证</router-link>
                工具本地复现座位与配牌。
              </p>
            </li>
            <li>
              <p>
                自定义规则：可提交核心逻辑由项目组适配、发起 Pull Request，或委托开发（默认进入公共仓库并接受 MIT）。详见下文「添加自定义规则」。
              </p>
            </li>
            <li>
              <p>
                辅助工具：站内提供
                <router-link to="/paili">牌理</router-link>、
                <router-link to="/chinese">国标计算器</router-link>、
                <router-link to="/rulebook">规则书</router-link>
                等。
              </p>
            </li>
            <li>
              <p>
                赛事与数据：支持比赛报名与管理，并可查询
                <router-link to="/player-data">玩家历史</router-link>
                与
                <router-link to="/player-data/platform">平台统计</router-link>。
              </p>
            </li>
          </ol>
        </template>

        <template v-else-if="sec.id === 'start'">
          <ol class="faq-list">
            <li>
              <p>
                推荐先
                <router-link to="/login?redirect=/account">注册 / 登录</router-link>
                账户，再进入平台对局。账户面板可绑定邮箱、修改密码、申请或管理赛事。
              </p>
            </li>
            <li>
              <p>
                网页对战入口：首页「进入平台」或导航栏同名项，地址为
                <router-link to="/game-unity">/game-unity</router-link>。
              </p>
            </li>
            <li>
              <p>
                手机端请下载
                <router-link to="/mobile-download">Android APK</router-link>；iOS / PC 亦可关注 Steam 商店页说明。
              </p>
            </li>
            <li>
              <p>
                遇到规则疑问，请先查阅
                <router-link to="/rulebook">规则书</router-link>
                ；仍有问题可到 QQ 群反馈。测试期问题也可联系管理员 Q1448826180。
              </p>
            </li>
          </ol>
        </template>

        <template v-else-if="sec.id === 'seed'">
          <ol class="faq-list">
            <li>
              <p>
                为确保公平，每局开始时服务器生成主随机种子（master_seed，256 位）与盐值（salt）。对局进行中只公布承诺值（commitment），主种子在整局结束前不对外公开；结束后公布主种子，供玩家验证服务器未中途更换随机源。主种子贯穿所有小局，并用于开局随机座位分配。
              </p>
            </li>
            <li>
              <p>
                每个小局开始时，系统根据主种子与当前局序号派生局内随机种子（round_random_seed），用于打乱该局牌堆。
              </p>
            </li>
            <li>
              <p>
                对局中可在房间显示局数的左上角左键单击，查看本局承诺值与盐值，浮窗内可一键复制「承诺值：xxxx 盐值：xxxx」。结束后完整主种子会出现在结算界面，亦可在牌谱对局信息中查看，并使用网站
                <router-link to="/seed-verify">种子验证</router-link>
                （/seed-verify）本地复现随机座位与每局配牌、牌山。
              </p>
            </li>
            <li>
              <p>
                承诺值：将主种子格式化为 64 位十六进制字符串后与盐值拼接，再做 SHA-256。即
                <code>commitment = int(SHA256(format(master_seed, '064x') + salt).hexdigest(), 16)</code>。
                开局公布的承诺值应与结束后公布的主种子、盐值按上式一致。
              </p>
            </li>
            <li>
              <p>
                局内随机种子：同样将主种子格式化为 64 位十六进制后与局序号十进制字符串拼接，SHA-256 结果作为
                <code>random.seed()</code>
                输入。局序号因规则而异：立直、古典使用
                <code>round_index</code>
                （从 1 递增，连庄 / 本场也占序号）；国标、青雀使用
                <code>current_round</code>
                （风圈内局号，连庄不递增）。例如日麻东一局一本场，
                <code>round_index</code>
                为 2 而非 1。
              </p>
            </li>
            <li>
              <p>
                开局时还会用主种子对入场顺序中的玩家列表洗牌，得到整局固定座位（牌谱
                <code>player_entry_order</code>
                记录 shuffle 前的 user_id 顺序）。青雀等规则在特定局数还会用派生局种子随机换位。
              </p>
            </li>
            <li>
              <p>
                牌堆打乱使用 Python
                <code>random.shuffle()</code>
                ：先
                <code>random.seed(round_random_seed)</code>
                ，再做 Fisher-Yates 洗牌。相同种子产生相同牌序，过程完全可复现。示例代码：
              </p>
              <pre class="code-block"><code>import hashlib
import random

master_seed = 0x1234567890abcdef01234567890abcdef01234567890abcdef01234567890abcdef
salt = "0123456789abcdef0123456789abcdef"
commitment = int(hashlib.sha256((format(master_seed, '064x') + salt).encode()).hexdigest(), 16)

sth_tiles_set = {
    11, 12, 13, 14, 15, 16, 17, 18, 19,  # 万
    21, 22, 23, 24, 25, 26, 27, 28, 29,  # 饼
    31, 32, 33, 34, 35, 36, 37, 38, 39,  # 条
    41, 42, 43, 44,                      # 东南西北
    45, 46, 47,                          # 中白发
}
hua_tiles_set = {51, 52, 53, 54, 55, 56, 57, 58}  # 春夏秋冬 梅兰竹菊
tiles_list = []
for tile in sth_tiles_set:
    tiles_list.extend([tile] * 4)
tiles_list.extend(hua_tiles_set)

# 立直/古典用 round_index，国标/青雀用 current_round
round_number = 1
round_random_seed = int(
    hashlib.sha256((format(master_seed, '064x') + str(round_number)).encode()).hexdigest(),
    16,
)
random.seed(round_random_seed)
random.shuffle(tiles_list)</code></pre>
            </li>
          </ol>
        </template>

        <template v-else-if="sec.id === 'addrule'">
          <ol class="faq-list">
            <li>
              <p>
                添加自定义麻将规则有三种方式：① 提供核心逻辑文件，由项目组简单适配；② 发起 Pull Request，将新规则合并到 GitHub 仓库；③ 提供规则文件，委托项目开发人员开发。
              </p>
            </li>
            <li>
              <p>
                委托开发的规则会上传到公共代码仓库。若使用 PR，最好先在测试群沟通。提交到 git 时若无特殊声明，默认接受本项目 MIT 协议。公共平台不保证持续服务；若关停，规则制定者可自行部署服务器。
              </p>
            </li>
            <li>
              <p>一条规则的执行逻辑由三部分组成：</p>
              <ul class="sub-list">
                <li>麻将游戏服务器：匹配玩家状态与操作（切牌、吃碰杠、自摸、荣和等；以及补花、九种九牌、四风连打、换三张等特殊操作）</li>
                <li>听牌检查脚本：判断手牌是否符合合法听牌形状</li>
                <li>和牌检查脚本：结算和牌组合的牌型与分数</li>
              </ul>
            </li>
            <li>
              <p>
                若自定义规则只含基础操作，或可复用已有规则的操作，可不提供服务器脚本；若听牌仅含一般形、七对、十三幺、全不靠等常见牌型，可复用现有听牌检测；多数规则变化集中在和牌番役，一般只需提供完整和牌检查脚本。
              </p>
            </li>
            <li>
              <p>
                小贴士：服务器为 py-fastapi，服务器脚本需为 py 文件；听牌 / 和牌脚本可为 py、so 或其他语言编译产物。若需在客户端切牌后提示番数，因在 Unity 中执行，需提供 C# 源文件或编译后的 dll。开发文档见
                <router-link to="/docs">开发手册</router-link>。
              </p>
            </li>
          </ol>
        </template>

        <template v-else-if="sec.id === 'thanks'">
          <ul class="thanks-list">
            <li><span class="k">牌面提供者</span>雪枫 XueFun9</li>
            <li><span class="k">表情包提供者</span>影子</li>
            <li><span class="k">随机种子设计</span>Zoe</li>
            <li><span class="k">新编 MCR 编著者</span>Natsuki</li>
            <li><span class="k">青雀设计者</span>莫莫柴</li>
            <li><span class="k">浪涌麻将设计者</span>自恧</li>
            <li><span class="k">直播宣传</span>Cloud980Ti、轻轻的飘</li>
            <li>
              <span class="k">赞助</span>
              九曜、健哥、何苏、Null、莫莫柴、Zazaka、中山大学国标麻将同好会、kiki、东西喵、GitHub/baisebaoma
            </li>
            <li><span class="k">特别感谢</span>莫莫柴、码龙、Null、影子、chinkaku</li>
            <li><span class="k">支持</span>棋牌游戏研究院、立直麻雀研习社、柴 de 麻将群</li>
          </ul>
        </template>

        <template v-else-if="sec.id === 'links'">
          <ul class="link-list">
            <li>
              <span class="k">官方服务器</span>
              <a href="https://salasasa.cn" target="_blank" rel="noopener noreferrer">salasasa.cn</a>
            </li>
            <li>
              <span class="k">GitHub</span>
              <a href="https://github.com/xelnagamiao/open_mahjong_unity" target="_blank" rel="noopener noreferrer">
                github.com/xelnagamiao/open_mahjong_unity
              </a>
            </li>
            <li>
              <span class="k">语雀文档</span>
              <a
                href="https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#"
                target="_blank"
                rel="noopener noreferrer"
              >开发手册</a>
            </li>
            <li>
              <span class="k">交流测试群</span>
              <a href="https://qm.qq.com/q/MGGZV58hOO" target="_blank" rel="noopener noreferrer">906497522</a>
            </li>
            <li>
              <span class="k">Steam</span>
              <a href="https://store.steampowered.com/app/4565740/Salasasa/" target="_blank" rel="noopener noreferrer">
                Salasasa 商店页
              </a>
            </li>
          </ul>
        </template>

        <a class="back-top" href="#top" @click.prevent="scrollToTop">返回顶部</a>
      </section>
    </div>
  </div>
</template>

<script setup>
import { onMounted } from 'vue'

const sections = [
  { id: 'intro', title: '一、平台简介' },
  { id: 'features', title: '二、平台特性' },
  { id: 'start', title: '三、开始游戏' },
  { id: 'seed', title: '四、随机种子' },
  { id: 'addrule', title: '五、添加自定义规则' },
  { id: 'thanks', title: '六、鸣谢' },
  { id: 'links', title: '七、相关链接' },
]

function scrollTo(id) {
  const el = document.getElementById(id)
  if (!el) return
  el.scrollIntoView({ behavior: 'smooth', block: 'start' })
  history.replaceState(null, '', `#${id}`)
}

function scrollToTop() {
  window.scrollTo({ top: 0, behavior: 'smooth' })
  history.replaceState(null, '', window.location.pathname)
}

onMounted(() => {
  const hash = (window.location.hash || '').replace(/^#/, '')
  if (hash && sections.some((s) => s.id === hash)) {
    requestAnimationFrame(() => scrollTo(hash))
  }
})
</script>

<style scoped>
.usage-guide {
  --accent: #5470c6;
  --accent-deep: #3d5aad;
  color: #333;
}

.page-banner {
  background: var(--accent);
  color: #fff;
  padding: 22px 20px;
  margin-bottom: 0;
}

.page-banner h1 {
  margin: 0 0 6px;
  font-size: 1.45rem;
  font-weight: 700;
}

.page-banner p {
  margin: 0;
  font-size: 13px;
  line-height: 1.5;
  opacity: 0.95;
}

.panel {
  background: #fff;
  border: 1px solid #e0e0e0;
  border-top: 0;
  padding: 16px 20px 28px;
}

.toc {
  margin-bottom: 20px;
  padding-bottom: 14px;
  border-bottom: 1px solid #eee;
}

.toc-title {
  font-size: 13px;
  font-weight: 700;
  color: #555;
  margin-bottom: 8px;
}

.toc ul {
  margin: 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-wrap: wrap;
  gap: 6px 14px;
}

.toc a {
  color: var(--accent-deep);
  text-decoration: none;
  font-size: 13px;
}

.toc a:hover {
  text-decoration: underline;
}

.faq-section {
  padding: 18px 0 8px;
  border-bottom: 1px solid #eee;
  scroll-margin-top: 72px;
}

.faq-section:last-of-type {
  border-bottom: 0;
}

.faq-section h2 {
  margin: 0 0 12px;
  font-size: 1.12rem;
  font-weight: 700;
  color: #222;
  padding-bottom: 8px;
  border-bottom: 2px solid var(--accent);
  display: inline-block;
  min-width: 8em;
}

.faq-list {
  margin: 0;
  padding-left: 1.35em;
  color: #444;
}

.faq-list > li {
  margin-bottom: 12px;
  padding-left: 4px;
}

.faq-list > li::marker {
  font-weight: 600;
  color: var(--accent-deep);
}

.faq-list p {
  margin: 0;
  font-size: 14px;
  line-height: 1.7;
}

.faq-list a,
.link-list a {
  color: var(--accent-deep);
}

.sub-list {
  margin: 8px 0 0;
  padding-left: 1.2em;
  font-size: 13px;
  line-height: 1.65;
  color: #555;
}

.sub-list li {
  margin-bottom: 4px;
}

.code-block {
  margin: 10px 0 0;
  padding: 12px 14px;
  background: #f6f8fa;
  border: 1px solid #e8e8e8;
  overflow-x: auto;
  font-size: 12px;
  line-height: 1.55;
  color: #333;
}

.code-block code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  white-space: pre;
}

.faq-list code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  background: #f0f2f5;
  padding: 1px 5px;
  border-radius: 2px;
}

.thanks-list,
.link-list {
  margin: 0;
  padding: 0;
  list-style: none;
}

.thanks-list li,
.link-list li {
  font-size: 14px;
  line-height: 1.7;
  padding: 4px 0;
  color: #444;
}

.thanks-list .k,
.link-list .k {
  display: inline-block;
  min-width: 7.5em;
  color: #666;
  font-weight: 600;
  margin-right: 8px;
}

.back-top {
  display: inline-block;
  margin-top: 8px;
  font-size: 12px;
  color: #888;
  text-decoration: none;
}

.back-top:hover {
  color: var(--accent-deep);
  text-decoration: underline;
}

@media (max-width: 640px) {
  .panel {
    padding: 14px 14px 24px;
  }

  .thanks-list .k,
  .link-list .k {
    display: block;
    margin-bottom: 2px;
  }
}
</style>
