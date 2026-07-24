<!-- 使用说明：仿雀渣 FAQ 目录分节结构，文案来自 Unity AboutUs -->
<template>
  <div class="usage-guide">
    <header class="page-banner">
      <h1>使用说明</h1>
      <p>平台简介、对局机制、猜番对抗与办赛说明。规则细则请参阅「规则书」。</p>
    </header>

    <div class="panel">
      <div class="guide-body">
        <div class="guide-main">
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
                    <router-link to="/guess-fan">猜番对抗</router-link>、
                    <router-link to="/rulebook">规则书</router-link>
                    等。
                  </p>
                </li>
                <li>
                  <p>
                    赛事与数据：支持
                    <a href="#events" @click.prevent="scrollTo('events')">申办赛事</a>
                    与管理，并可查询
                    <router-link to="/player-data">玩家历史</router-link>
                    与
                    <router-link to="/player-data/platform">平台统计</router-link>。
                  </p>
                </li>
                <li>
                  <p>
                    对局机制：国标等规则可选开启
                    <a href="#meld-protect" @click.prevent="scrollTo('meld-protect')">鸣牌保护</a>
                    与
                    <a href="#tactical-call" @click.prevent="scrollTo('tactical-call')">战术鸣牌</a>，
                    以在网麻中保留部分线下信息节奏与战术空间。
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

            <template v-else-if="sec.id === 'meld-protect'">
              <ol class="faq-list">
                <li>
                  <p>
                    在玩家 A 出牌时，若玩家 B、C 可对该出牌进行操作，则 B、C 会提前看到这张出牌；玩家 D 则会延迟一个「鸣牌保护时间」才看到出牌。若在鸣牌保护时间内 B、C 对出牌进行了操作，玩家 D 会立刻看见出牌，并在「第一追赶时间」之后再看见 B、C 其一执行的鸣牌申请或鸣牌。
                  </p>
                </li>
                <li>
                  <p>
                    由于玩家 D 必定延迟一个第一追赶时间才看见 B、C 其一的鸣牌，若 B、C 在吃碰后快速出牌，这张出牌在 D 的视角中会显得「即时」。因此平台在触发鸣牌保护后进行出牌时，会保证至少再经过「第二追赶时间」才播放动画，避免在 D 的视角中出牌紧贴在吃碰之后生效。
                  </p>
                </li>
                <li>
                  <p>当前默认配置：</p>
                  <ul class="sub-list">
                    <li>鸣牌保护时间：1.3s</li>
                    <li>第一追赶时间：0.7s</li>
                    <li>第二追赶时间：0.5s</li>
                  </ul>
                </li>
                <li>
                  <p>
                    <strong>可操作玩家</strong>：可以执行操作、立刻看见行动的玩家。若在吃碰选项出现的 2 秒以内执行吃碰或 pass，则能避免他家获取「有人能够吃碰」的额外信息。
                  </p>
                </li>
                <li>
                  <p>
                    <strong>受保护玩家</strong>：在鸣牌保护时间内受到信息制约的玩家。该设计可在网麻的高效对局中保留一些线下对局的信息节奏。
                  </p>
                </li>
                <li>
                  <p>房间配置中可开关鸣牌保护（国标 / 青雀 / 四川等规则支持）。存在可和牌机会时，本区间通常不启用，以避免和牌面板与出牌揭示贴脸。</p>
                </li>
              </ol>
            </template>

            <template v-else-if="sec.id === 'tactical-call'">
              <ol class="faq-list">
                <li>
                  <p>
                    战术鸣牌的设计来源于雀渣：在其他家申请鸣牌以后，可用更高优先级的操作打断他家更低优先级的操作，以模拟线下刻意「碰断吃」等战术。
                  </p>
                </li>
                <li>
                  <p>
                    示例一：玩家 A 出牌 T，玩家 B 可吃、玩家 C 可碰。此时 C 选择跳过，但 B 点击了吃牌；由于仍存在可执行的更高优先级操作，系统会再次询问 C 是否要碰，以模拟国标线下允许刻意用碰打断别人吃的情形。
                  </p>
                </li>
                <li>
                  <p>
                    示例二：玩家 A 出牌 T，玩家 B 可吃或和牌，玩家 C 可碰。B 选择吃，C 用碰打断了 B 的吃；此时 B 不再能用和去打断 C 的碰。这符合国标不允许玩家在鸣牌发声后更改自己要执行动作的规则。
                  </p>
                </li>
                <li>
                  <p>房间配置中可开关战术鸣牌。关闭时走「等待最高优先级操作执行完成」的原始流程。</p>
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

            <template v-else-if="sec.id === 'guess-fan'">
              <ol class="faq-list">
                <li>
                  <p>
                    <router-link to="/guess-fan">猜番对抗</router-link>
                    是站内猜番小游戏：根据提示逐步缩小范围，猜出国标或立直番种。需先登录 salasasa 游戏账号。
                  </p>
                </li>
                <li>
                  <p>入口模式：</p>
                  <ul class="sub-list">
                    <li><strong>个人训练</strong>：单人猜番，可自选番种集，不计入排行。</li>
                    <li><strong>创建房间</strong>：开设联机房间与好友对战，不计统计数据。</li>
                    <li><strong>系统匹配</strong>：规则集为国标+立直、BO5、限时 60 秒、每局最多 8 次猜测，计入排行榜。</li>
                  </ul>
                </li>
                <li>
                  <p>
                    每局有人猜中或时间结束后展示答案与双方猜测，约 6 秒后自动进入下一局。排行榜仅统计系统匹配；初始 Elo 1000，K 值为 32；战胜高分对手加分更多，负于低分对手扣分更多。
                  </p>
                </li>
                <li>
                  <p>
                    <strong>关联提示（黄色）</strong>不是「接近答案」的单一含义，而是该列与答案存在以下任一种关联；可在对局设置中关闭关联提示，关闭后黄色变为灰色，只保留完全匹配的绿色与数值方向箭头。
                  </p>
                  <ul class="sub-list">
                    <li>
                      <strong>同义、同名关联：</strong>两个规则中的同名番，或共享同一种番型的同义番。例如答案是立直「一气通贯」，猜国标「清龙」，名字显示黄色；只有猜中题库中的那个具体番种才会绿色。
                    </li>
                    <li>
                      <strong>类型关联：</strong>答案是多类型番时，猜到该类型下任意副类型显示黄色；猜中具体番种的主类型才绿色。例如答案组合龙 [顺子系、全不靠系]，猜三色三步高 [顺子系] 时类型一侧为黄。
                    </li>
                    <li>
                      <strong>浮动番数关联：</strong>同一立直役可能因门清 / 副露拥有两个番数。命中该役的另一种合法番数显示黄色，命中本题实际抽到的番数才绿色。
                    </li>
                  </ul>
                </li>
                <li>
                  <p><strong>多值番种的标定原则：</strong></p>
                  <ul class="sub-list">
                    <li>出现概率原则：组合龙复合全不靠概率约 29.3%，故组合龙优先计为顺子系、其次全不靠系，即 [顺子系、全不靠系]。</li>
                    <li>先置逻辑优先：能预判导致和牌行动番数时，优先记条件系，其次偶然系。例如一发、里宝牌记 [条件系、偶然系]；天和、地和记 [偶然系]；立直、双立直记 [条件系]。</li>
                    <li>复计只计其一：四归一、花牌、红宝牌等按单个番种番值计，例如四归一记 [2]。</li>
                    <li>食下役一律副值：涉及食下的番种以门清原生番数为准，例如纯全带幺九记 [3、2]。</li>
                    <li>声明特殊：四归一组数为 [4、3、2]；组合龙、九莲宝灯为 [1]；全不靠、七星不靠与一色系为 [全体]；对子系为 [7]。</li>
                  </ul>
                </li>
                <li>
                  <p><strong>番种归类：</strong></p>
                  <ul class="sub-list">
                    <li>顺子系：清龙、三色同顺、三色三步高等。</li>
                    <li>刻子系：小三元、三暗刻、三风刻等。</li>
                    <li>对子系：七对子、连七对、大七星等。</li>
                    <li>全体系：清一色、断幺九、全带五、全带幺等；组数显示「全体」。</li>
                    <li>全不靠系：全不靠、七星不靠、组合龙（副归类）等。</li>
                    <li>特殊系：十三幺、九莲宝灯等。</li>
                    <li>条件系：立直、一发、门前清、不求人、岭上开花（副归类）、宝牌等。</li>
                    <li>偶然系：岭上开花、里宝牌、宝牌（副归类）等。</li>
                  </ul>
                </li>
              </ol>
            </template>

            <template v-else-if="sec.id === 'events'">
              <ol class="faq-list">
                <li>
                  <p>
                    登录后打开
                    <router-link to="/account">账户面板</router-link>
                    ，在「提交办赛申请」中填写资料并提交；平台管理员审核通过后，赛事进入「已注册」状态，申请人成为赛事主管理员。
                  </p>
                </li>
                <li>
                  <p>申请字段说明：</p>
                  <ul class="sub-list">
                    <li><strong>赛事名称</strong>（必填）：对外展示的比赛名称。</li>
                    <li>
                      <strong>拟定开始 / 结束时间</strong>（开始必填，结束可选）：仅为开启与关闭的大致时间范围，实际开启与关闭由比赛管理员自行决定。长期月赛或季度赛可不设截止时间，或连续申报；拟定日期确定后亦可随时更改。
                    </li>
                    <li>
                      <strong>赛事介绍</strong>（必填）：必须包含明确的报名联系方式。实际赛程此处可不写死，即使写了后期也可改。若介绍中承诺了规则或奖励却未兑现，或临时改赛制引发争议，平台可能介入监管（批评、取消办赛资格、封禁个别账户等）。
                    </li>
                    <li><strong>备注</strong>（可选）：给审核管理员的说明，或不希望展示在赛事介绍中、但需预先告知的特殊声明。</li>
                  </ul>
                </li>
                <li>
                  <p>
                    审核通过后，在账户「赛事管理」中可查看申请记录与已注册赛事，点击「管理赛事」展开管理面板。生命周期大致为：已注册 → 开始赛事（开启后可创建比赛房间）→ 关闭赛事（封存，仍可查看数据）→ 如需再开则提交「申请重新开启」，由平台管理员审核。
                  </p>
                </li>
                <li>
                  <p>
                    主管理员可添加子管理员协助办赛；修改赛事名或简介需再次提交平台审核，通过后才会在公开页生效。赛事公开页地址形如
                    <code>/events/赛事ID</code>。
                  </p>
                </li>
              </ol>
            </template>

            <template v-else-if="sec.id === 'addrule'">
              <ol class="faq-list">
                <li>
                  <p>
                    平台会逐步添加现存的地方麻将规则。若有想添加的自制规则，可咨询管理员 Xe，或阅读本节说明。添加自定义麻将规则有三种方式：① 提供核心逻辑文件，由项目组简单适配；② 发起 Pull Request，将新规则合并到 GitHub 仓库；③ 提供规则文件，委托项目开发人员开发。
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

            <template v-else-if="sec.id === 'rank-credit'">
              <ol class="faq-list">
                <li>
                  <p>
                    持有国标业余或职业段位证书者，可通过证书认证获得不大于 1 级的段位赋分，或申请 MCRPL 资格。
                  </p>
                </li>
                <li>
                  <p>申请时请附上：</p>
                  <ul class="sub-list">
                    <li>数字 UID</li>
                    <li>段位（及证书）</li>
                    <li>网麻战绩（可选）</li>
                  </ul>
                </li>
                <li>
                  <p>具体认证可在测试群联系管理员 Xe 办理。</p>
                </li>
              </ol>
            </template>

            <template v-else-if="sec.id === 'contribute'">
              <ol class="faq-list">
                <li>
                  <p>
                    若想对项目本身提出意见、提交美术 / 音频资源或 Pull Request，或希望私有部署、关注项目进展，可加入 OMU 平台开发群
                    <strong>10845740</strong>。
                  </p>
                </li>
                <li>
                  <p>
                    代码仓库见
                    <a href="https://github.com/xelnagamiao/open_mahjong_unity" target="_blank" rel="noopener noreferrer">GitHub</a>；
                    开发文档见
                    <router-link to="/docs">开发手册</router-link>
                    。日常游玩与 bug 反馈请优先使用交流测试群。
                  </p>
                </li>
              </ol>
            </template>

            <template v-else-if="sec.id === 'sponsor'">
              <ol class="faq-list">
                <li>
                  <p>赞助可联系管理员 Xe。赞助无法获得任何特权。</p>
                </li>
                <li>
                  <p>赞助满 100 元可上鸣谢名单（见下文「鸣谢」）。</p>
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
                  <span class="k">OMU 开发群</span>
                  10845740
                </li>
                <li>
                  <span class="k">语音聊天室</span>
                  <a href="https://oopz.cn/i/zzCOJF" target="_blank" rel="noopener noreferrer">oopz.cn/i/zzCOJF</a>
                </li>
                <li>
                  <span class="k">Steam</span>
                  <a href="https://store.steampowered.com/app/4565740/Salasasa/" target="_blank" rel="noopener noreferrer">
                    Salasasa 商店页
                  </a>
                </li>
              </ul>
            </template>
          </section>
        </div>

        <aside class="toc" aria-label="目录导航">
          <div class="toc-sticky">
            <div class="toc-title">导航</div>
            <ul>
              <li v-for="sec in sections" :key="sec.id">
                <a
                  :href="`#${sec.id}`"
                  :class="{ active: activeId === sec.id }"
                  @click.prevent="scrollTo(sec.id)"
                >{{ sec.title }}</a>
              </li>
            </ul>
            <a class="back-top" href="#top" @click.prevent="scrollToTop">返回顶部</a>
          </div>
        </aside>
      </div>
    </div>
  </div>
</template>

<script setup>
import { onMounted, onUnmounted, ref } from 'vue'

const sections = [
  { id: 'intro', title: '一、平台简介' },
  { id: 'features', title: '二、平台特性' },
  { id: 'start', title: '三、开始游戏' },
  { id: 'meld-protect', title: '四、鸣牌保护' },
  { id: 'tactical-call', title: '五、战术鸣牌' },
  { id: 'seed', title: '六、随机种子' },
  { id: 'guess-fan', title: '七、猜番对抗' },
  { id: 'events', title: '八、申办赛事' },
  { id: 'addrule', title: '九、添加自定义规则' },
  { id: 'rank-credit', title: '十、赋分' },
  { id: 'contribute', title: '十一、参与项目' },
  { id: 'sponsor', title: '十二、赞助' },
  { id: 'thanks', title: '十三、鸣谢' },
  { id: 'links', title: '十四、相关链接' },
]

const activeId = ref(sections[0].id)

function scrollTo(id) {
  const el = document.getElementById(id)
  if (!el) return
  activeId.value = id
  el.scrollIntoView({ behavior: 'smooth', block: 'start' })
  history.replaceState(null, '', `#${id}`)
}

function scrollToTop() {
  activeId.value = sections[0].id
  window.scrollTo({ top: 0, behavior: 'smooth' })
  history.replaceState(null, '', window.location.pathname)
}

function updateActiveFromScroll() {
  const offset = 96
  let current = sections[0].id
  for (const sec of sections) {
    const el = document.getElementById(sec.id)
    if (!el) continue
    if (el.getBoundingClientRect().top - offset <= 0) {
      current = sec.id
    }
  }
  activeId.value = current
}

onMounted(() => {
  const hash = (window.location.hash || '').replace(/^#/, '')
  if (hash && sections.some((s) => s.id === hash)) {
    requestAnimationFrame(() => scrollTo(hash))
  }
  window.addEventListener('scroll', updateActiveFromScroll, { passive: true })
  updateActiveFromScroll()
})

onUnmounted(() => {
  window.removeEventListener('scroll', updateActiveFromScroll)
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

.guide-body {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 196px;
  gap: 28px;
  align-items: start;
}

.guide-main {
  min-width: 0;
}

.toc {
  font-size: 13px;
  position: sticky;
  top: 72px;
  align-self: start;
  z-index: 20;
}

.toc-sticky {
  padding: 10px 0 10px 14px;
  border-left: 1px solid #e8e8e8;
  max-height: calc(100vh - 88px);
  overflow-y: auto;
}

.toc-title {
  font-size: 13px;
  font-weight: 700;
  color: #555;
  margin-bottom: 10px;
}

.toc ul {
  margin: 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.toc a {
  color: var(--accent-deep);
  text-decoration: none;
  font-size: 13px;
  line-height: 1.45;
  display: block;
}

.toc a:hover {
  text-decoration: underline;
}

.toc a.active {
  color: #222;
  font-weight: 700;
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
  margin-top: 14px;
  padding-top: 10px;
  border-top: 1px solid #eee;
  font-size: 12px;
  color: #888;
  text-decoration: none;
}

.back-top:hover {
  color: var(--accent-deep);
  text-decoration: underline;
}

@media (max-width: 860px) {
  .guide-body {
    grid-template-columns: 1fr;
    gap: 0;
  }

  .toc {
    order: -1;
    margin-bottom: 12px;
    position: static;
  }

  .toc-sticky {
    padding: 0 0 14px;
    border-left: 0;
    border-bottom: 1px solid #eee;
    max-height: none;
    overflow: visible;
  }

  .toc ul {
    flex-direction: row;
    flex-wrap: wrap;
    gap: 6px 14px;
  }

  .toc a {
    display: inline;
  }

  .back-top {
    display: block;
    margin-top: 10px;
    padding-top: 0;
    border-top: 0;
  }
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
