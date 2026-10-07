<!-- 使用说明：仿雀渣 FAQ 目录分节结构，文案来自 Unity AboutUs -->
<template>
  <div class="usage-guide">
    <header class="page-banner">
      <h1>使用说明</h1>
      <p>平台简介、对局机制、猜番对抗、办赛说明与平台规约。规则细则请参阅「规则书」。</p>
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
                    可通过网页版「<router-link to="/game-unity">进入平台</router-link>」、
                    <a href="https://store.steampowered.com/app/4565740/Salasasa/" target="_blank" rel="noopener noreferrer">Steam 商店</a>
                    或
                    <router-link to="/mobile-download">手机版 APK</router-link>
                    开始对局。点此可进入<router-link to="/2d">Salasasa2D客户端</router-link>，体验不同风格，2D客户端可以与3D客户端互通对局。
                  </p>
                </li>
              </ol>
            </template>

            <template v-else-if="sec.id === 'features'">
              <ol class="faq-list">
                <li>
                  <p>多规则对局：平台支持国标、立直、青雀、四川麻将、长沙麻将等多个规则。</p>
                </li>
                <li>
                  <p>
                    公平性承诺：平台承诺不会对特定的用户或ip针对性的使用控牌，优待等非公平性行为，并且保证自己的核心代码与开源项目open_mahjong_unity的行为一致，详情可见<button type="button" class="guide-text-button" @click="regulationsVisible = true">Salasasa-萨拉飒飒麻将平台账户规约</button>
                  </p>
                </li>
                <li>
                  <p>
                    自定义规则：平台提供自由模式，在自由模式中可以使用虚拟牌桌进行不受规则制约的对局；如果想提交自己的规则，也可以通过发起 Pull Request，提交核心代码或委托开发等形式在平台的下一个发行版中发布您的自定义规则，平台官方也会持续推进地方麻将规则的适配进程。具体详见下文「<a href="#addrule" @click.prevent="scrollTo('addrule')">添加自定义规则</a>」。
                  </p>
                </li>
                <li>
                  <p>
                    辅助工具：平台提供
                    <router-link to="/paili">牌理</router-link>、
                    <router-link to="/calc/chinese">国标计算器</router-link>、
                    <router-link to="/guess-fan">猜番对抗</router-link>、
                    <router-link to="/rulebook">规则书</router-link>
                    等麻将相关的必要工具。
                  </p>
                </li>
                <li>
                  <p>
                    赛事与数据：平台支持<a href="#events" @click.prevent="scrollTo('events')">申办赛事</a>与赛事管理，并可查询<router-link to="/player-data">玩家对局</router-link>、<router-link to="/player-data/platform">平台统计</router-link>、<router-link to="/player-data/duplicate">复式密钥查询</router-link>等所有数据，平台的完整对局数据都公开展示并且支持被用作数据统计、ai训练、制作视频等任意用途，平台也授权任何人均可以使用这些公共领域的数据，任何人都可以随意打包，分发，学习salasasa的对局数据，并且用作商业目的。（玩家账户的段位信息、头像、签名；比赛、基地的公告等非对局数据不在此列）
                  </p>
                </li>
                <li>
                  <p>
                    对局机制：平台致力于推广与发展竞技麻将的完整体验，国标等规则可选开启
                    <a href="#meld-protect" @click.prevent="scrollTo('meld-protect')">鸣牌保护</a>
                    与
                    <a href="#tactical-call" @click.prevent="scrollTo('tactical-call')">战术鸣牌</a>，
                    以在网麻中保留部分线下信息节奏与战术空间。日麻也支持流局听牌扣牌等还原真实场景的完整设计
                  </p>
                </li>
              </ol>
            </template>

            <template v-else-if="sec.id === 'start'">
              <ol class="faq-list">
                <li>
                  <p>
                    点击此处
                    <router-link to="/login?redirect=/guide">注册 / 登录</router-link>
                    账户，再进入平台对局。此处的<router-link to="/account">账户面板</router-link>可绑定邮箱、修改密码、申请或管理赛事。
                  </p>
                </li>
                <li>
                  <p>
                    网页对战入口：
                    <router-link to="/game-unity">https://salasasa.cn/game-unity</router-link>
                  </p>
                </li>
                <li>
                  <p>
                    手机端请下载<router-link to="/mobile-download">Android APK</router-link>；PC端点击此处进入
                    <a href="https://store.steampowered.com/app/4565740/Salasasa/" target="_blank" rel="noopener noreferrer">Steam 商店页</a>，ios端稍缓适配
                  </p>
                </li>
                <li>
                  <p>
                    如有规则方面的疑问，可在此处查询完整的<router-link to="/rulebook">规则书</router-link>文本；如果在游戏过程中发现任何问题，可以到QQ群<a href="https://qm.qq.com/q/MGGZV58hOO" target="_blank" rel="noopener noreferrer">906497522</a>反馈，也可以直接联系测试群群主及开发者Q1448826180。
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
                  <p>房间配置中可开关战术鸣牌。关闭时走「等待最高优先级操作执行完成」的原始流程。开启时，首次询问仍使用房间步时加剩余局时；只有有效申请后的抢断再询问使用独立5秒，不扣普通局时。</p>
                  <p>推倒和（MIL 2024）按规则书第六节允许升级申请：B 吃、C 碰后，B 仍可改报和牌；本张牌主动选择“放弃”则退出竞争。纯真人新房默认开启，有机器人时自动关闭；鸣牌保护是另一项功能。</p>
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
                  <pre class="code-block"><code>{{ seedExampleCode }}</code></pre>
                </li>
              </ol>
            </template>

            <template v-else-if="sec.id === 'guess-fan'">
              <ol class="faq-list">
                <li>
                  <p>请先使用 salasasa 游戏账号登录后再进入猜番对抗。</p>
                  <p><router-link to="/guess-fan">猜番对抗</router-link></p>
                </li>
                <li>
                  <p>■ 个人训练、创建房间、匹配</p>
                  <ul class="sub-list">
                    <li><strong>个人训练</strong><p>开启单人猜番</p></li>
                    <li><strong>创建房间</strong><p>开设联机房间，不计统计数据</p></li>
                    <li>规则集：立直、BO5、限时60s、8次猜番机会、计入排行</li>
                    <li>规则集：国标+立直、BO5、限时60s、8次猜番机会、计入排行</li>
                  </ul>
                </li>
                <li>
                  <h3>什么是关联提示</h3>
                  <p>黄色提示代表所猜番种的一列数据与正确答案的对应数据列存在以下任意一种关联：</p>
                  <ul class="sub-list">
                    <li><b>同义、同名关联：</b>两个规则中的同名番，或共享同一种番型的同义番。例如答案是立直「一气通贯」，猜国标「清龙」，名字显示黄色；只有猜中题库中的那个具体番种才显示绿色。<br />示例 1：答案一气通贯 [一气通贯、清龙]，猜出清龙 [清龙、一气通贯] 时名字一侧会显示关联的黄色提示。<br />示例 2：答案平和 [国标]，猜测时选择平和 [日麻]，同名的平和一侧会显示关联的黄色提示。<br />示例 3：答案全大 [全大，全中，全小]，猜出全中时名字一侧会显示关联的黄色提示；只有猜中全大本身才显示绿色。</li>
                    <li><b>类型关联：</b>在答案是多类型番时，猜测到该类型番的任意一个副类型时，则显示黄色；只有猜中具体番种的主类型时，才显示绿色。<br />示例 1：答案组合龙 [顺子系、全不靠系]，猜出三色三步高 [顺子系] 时番种类型一侧会显示关联的黄色提示。<br />示例 2：答案里宝牌 [条件系、偶然系]，猜出立直 [条件系] 时番种类型一侧会显示关联的黄色提示。</li>
                    <li><b>浮动番数关联：</b>同一立直役可能因门清、副露拥有两个番数。例如一气通贯为门清 2 番、副露 1 番；猜测命中该役的另一种合法番数时显示黄色，命中本题实际抽到的番数才绿色。<br />示例：答案一气通贯 [2, 1]，猜出红宝牌 [1] 时番种番数一侧会显示关联的黄色提示。</li>
                  </ul>
                  <p><b>关闭关联提示：</b>所有上述黄色提示都会变成灰色，只保留完全匹配的绿色和数值方向箭头，提升对局难度。</p>
                </li>
                <li>
                  <h3>同时拥有多个值的番种是如何标定的</h3>
                  <ol class="sub-list">
                    <li><b>出现概率原则</b>：组合龙复合全不靠的概率为 29.3%（161/550），因此组合龙应当被优先计为顺子系、其次再被计为全不靠系，即 [顺子系、全不靠系]。</li>
                    <li><b>先置逻辑优先原则</b>：在能够预测到导致和牌的行动的具体番数的情况下，优先记条件系，其次记偶然系。例如一发、里宝牌、抢杠、岭上开花、海底捞月应当记 [条件系、偶然系]；天和、地和应当记 [偶然系]；立直、双立直、和绝张应当记 [条件系]。</li>
                    <li><b>复计只计其一原则</b>：在可以复计的番种当中，例如四归一、花牌、红宝牌，应当计单个番种的番值，例如四归一应当记 [2]。</li>
                    <li><b>食下役一律副值原则</b>：涉及食下的番种，应当以番种门清状态下的原生番数为主番数，食下番数为副番数，例如纯全带幺九应当记 [3、2]。抽题时答案番数一律取主番数（门清原生番数），例如纯全带幺九抽 3 番、混全带幺九抽 2 番。</li>
                    <li><b>声明特殊番种原则</b>：四归一的组数规定为 [4、3、2]；组合龙、九莲宝灯的组数规定为 [1]；全不靠、七星不靠与一色系番种的组数规定为 [全体]；对子系的组数规定为 [7]；流局满贯的番数为 [5]。</li>
                    <li><b>避免重复猜取原则</b>：涉及番种意义相近但是需要重复猜取的番种将会开启关联提示或合并，例如答案「全大」的番名规定为 [全大，全中，全小]，答案「宝牌」的番名规定为[宝牌，赤宝牌，里宝牌]，答案「海底捞月」的番名规定为[海底捞月，河底捞鱼，妙手回春]；役牌·白、役牌·中、自风·南等番种合并为役牌、自风与场风。立直与双立直、混一色与清一色之类猜取较为简单的不设关联提示。</li>
                  </ol>
                </li>
                <li>
                  <h3>番种如何归类</h3>
                  <ul class="sub-list">
                    <li><b>顺子系</b>：由特定顺子组合构成的番种，例如清龙、三色同顺、三色三步高等。</li>
                    <li><b>刻子系</b>：由刻子、雀头或杠构成的番种，例如小三元、三暗刻、三风刻等。</li>
                    <li><b>对子系</b>：由对子构成的番种，例如七对子、连七对、大七星等。</li>
                    <li><b>全体系</b>：满足整手牌固定特征的番种，例如清一色、断幺九、全带五、全带幺；组数显示「全体」。</li>
                    <li><b>全不靠系</b>：包括全不靠、七星不靠、组合龙（副归类）等。</li>
                    <li><b>特殊系</b>：由特殊规定的牌型和牌构成的番种，例如十三幺、九莲宝灯。</li>
                    <li><b>条件系</b>：满足特定条件构成的番种，例如立直、一发、门前清、不求人、岭上开花（副归类）、宝牌等。</li>
                    <li><b>偶然系</b>：满足偶然条件构成的番种，例如岭上开花、里宝牌、宝牌（副归类）等。</li>
                  </ul>
                </li>
                <li>
                  <h3>对战与排行</h3>
                  <p>个人训练和自建房可选择 6、8、10 或 12 次猜测机会，以及 40、60、80 或 100 秒限时；系统匹配固定为 8 次、60 秒。有人猜中、次数用尽或时间结束后展示答案与双方猜测，6 秒后自动进入下一局。</p>
                  <p>匹配分为「国标+立直」与「立直」两个池，各自独立排队与计分；排行榜默认展示国标+立直，可切换成立直查看。</p>
                  <p>只有系统匹配计入排行榜。战胜高分对手获得更多积分，负于低分对手扣分更多。</p>
                </li>
                <li>
                  <p>
                    <b>Elo 积分规则</b><br />
                    初始 1000 分，仅系统匹配计分，自建房不计。<br />
                    K 值为 32：胜者加分与败者扣分数值相同。<br />
                    战胜高分玩家加分更多，战胜低分玩家加分更少；反之亦然。<br />
                    计算式：变化值 = 32 ×（实际结果 − 预期胜率），结果四舍五入。
                  </p>
                </li>
              </ol>
            </template>

            <template v-else-if="sec.id === 'events'">
              <ol class="faq-list">
                <li>
                  <p><router-link to="/account#sec-apply-event">提交办赛申请</router-link></p>
                </li>
                <li>
                  <p><strong>Salasasa 平台赛事规约</strong></p>
                  <ol class="sub-list">
                    <li>赛事的组织、奖金等各项成本开支不得低于报名费的 80%，如赛事奖励中提供更上级比赛名额或特殊奖励的，可以进行独立核算。</li>
                    <li>收取报名费的赛事必须明确规定赛事规则与奖励方式；该内容同样受赛事介绍中平台可能对赛制争端进行介入的声明约束。</li>
                    <li>涉及烟、酒、槟榔等成瘾品赞助的赛事，须提前联系平台管理员后再申报。</li>
                  </ol>
                </li>
                <li>
                  <p><strong>赛事名称</strong></p>
                  <p>赛事名称必须提交中文、英文或其他语言的完整赛事全称，不得使用简写、表述不清或者有公共性质的赛事名称；错误案例：国标麻将比赛、FST杯；正确案例：第一届神秘嘉宾杯国标麻将比赛、咕咕嘎嘎国标麻将群内赛。</p>
                </li>
                <li>
                  <p><strong>拟定开始时间 / 拟定结束时间</strong></p>
                  <p>拟定开始时间和结束时间只是申请的开启赛事和关闭赛事的大致时间范围，赛事的开启与关闭将由比赛管理员自行决定；
                  如果是长期的月赛或者季度赛，可以不设截止时间或连续申报比赛，在确定拟定日期以后也可以随时进行更改。</p>
                </li>
                <li>
                  <p><strong>赛事介绍</strong></p>
                  <p>赛事介绍中必须包含明确的报名联系方式，对于实际赛程这里可以不予规定、即使予以规定，后期也可以进行更改。但是，如果赛事在实际赛程中规定了某些赛事规则或者奖励方式但未达成，或者在临时更改赛制以后出现了争议或争端，平台也会一定程度上对赛事进行一些介入监管；包括且不限于，批评、取消办赛资格、封禁个别账户等惩罚，请务必注意这一点。</p>
                </li>
                <li>
                  <p><strong>赛事负责人</strong></p>
                  <p>请填写真实姓名与24小时内能够联系上的联系方式，手机号仅作为实名验证使用，如果不希望平台使用手机号进行联系，可以在备注内填写偏好的联系方式，平台绝不会通过偏好联系方式以外的方式联系您；如果不希望暴露真实姓名，可以填写惯用id，只需要手机号是实名的即可。</p>
                </li>
                <li>
                  <p><strong>备注</strong></p>
                  <p>给予管理员的说明，或不希望在公开介绍中展示、但需预先告知的特殊声明。修改申请、保存修改、重新提交时填写的内容会按时间追加到双方往来备注栏。</p>
                </li>
                <li>
                  <p><router-link to="/account#sec-manage-event">赛事管理</router-link></p>
                  <p>查看办赛申请与已注册赛事；点击「管理」在下方展开管理面板。</p>
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
                  <p>具体认证可在测试群联系群主 Xe 办理。</p>
                </li>
              </ol>
            </template>

            <template v-else-if="sec.id === 'contribute'">
              <ol class="faq-list">
                <li>
                  <p>
                    若想对项目本身提出意见、提交美术 / 音频资源或 Pull Request，或希望私有部署、关注项目进展，可加入 OMU 平台开发群
                    <strong><a href="https://qm.qq.com/q/MualpyNFoi" target="_blank" rel="noopener noreferrer">1084537740</a></strong>。
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
              <p class="sponsor-text">赞助可联系群主Xe，赞助者在数据站可以额外获得下载牌谱的限额，也可以提前准入中级场，赞助100元添加鸣谢名单</p>
            </template>

            <template v-else-if="sec.id === 'thanks'">
              <ul class="thanks-list">
                <li>
                  <span class="k">开发团队</span>
                  <span data-no-translate>{{ creditLine("Xelnaga(主程)  雪枫XueFun9(牌面提供者)  Zoe(随机种子设计)  Natsuki(新编MCR编著者)  坏狗影子(表情包提供者)  GitHub/baisebaoma(高性能罗伯特设计) troooobot(长沙麻将实现)  Zoe(台湾麻将实现)  二階堂 ヒロ(中心盘设计)  莫莫柴(青雀、2D桌面)  Shq、Zoe(bug修复)  salasasasa数据bot(PCX1078)  插件开发(新手求教)") }}</span>
                </li>
                <li>
                  <span class="k">自定义规则设计</span>
                  <span data-no-translate>{{ creditLine("莫莫柴(青雀)  关兆豪先生(中庸麻将)  Null(虹雀)  K神传说(K神麻将)  恋绘色(香港清章恋绘色魔改)  小林(国标小林改)  蓝十(国标蓝十改)  自恧(浪涌麻将)  南瓜饼(南雀)") }}</span>
                </li>
                <li>
                  <span class="k">地方规则考据</span>
                  <span data-no-translate>{{ creditLine("上海清混碰(何苏)  古典麻将(莫莫柴、何苏、古明地梦)  宜兴麻将(苦鱼)  Mil推广规则(MIL国际麻将联盟)") }}</span>
                </li>
                <li>
                  <span class="k">直播宣传</span>
                  <span data-no-translate>Cloud980Ti  轻轻的飘  等候1234  立直喵  Ce/Est1al  鬼头刀鱼fish  神源之心  方片杰克  Ni9e_9  大柠劳斯</span>
                </li>
                <li>
                  <span class="k">赞助</span>
                  <span data-no-translate>九曜、kiki、健哥、何苏、Null、莫莫柴、恋绘色、Zazaka、中山大学国标麻将同好会、东西喵、山东大学老年人活动中心、Sw123</span>
                </li>
                <li>
                  <span class="k">特别感谢</span>
                  <span data-no-translate>{{ creditLine("莫莫柴、码龙、Null、坏狗影子、何苏、FFMJ法国麻将协会(翻译支持)") }}</span>
                </li>
                <li>
                  <span class="k">支持社团</span>
                  <span data-no-translate>棋牌游戏研究院、立直麻雀研习社、柴の麻将群、雀渣玩家群、中山大学国标麻将同好会</span>
                </li>
                <li>
                  <span class="k">早期测试</span>
                  <span data-no-translate>夜色祢 chlorine 陪练的命运</span>
                </li>
              </ul>
              <p class="thanks-message">以及游玩salasasa平台的所有玩家！</p>
            </template>

            <template v-else-if="sec.id === 'links'">
              <ul class="link-list">
                <li>
                  <span class="k"><span>官方服务器</span>：</span>
                  <a href="https://salasasa.cn" target="_blank" rel="noopener noreferrer">salasasa.cn</a>
                </li>
                <li>
                  <span class="k"><span>GitHub</span>：</span>
                  <a href="https://github.com/xelnagamiao/open_mahjong_unity" target="_blank" rel="noopener noreferrer">github.com/xelnagamiao/open_mahjong_unity</a>
                </li>
                <li>
                  <span class="k"><span>语雀文档</span>：</span>
                  <a
                    href="https://www.yuque.com/xelnaga-yjcgq/zkwfgr/lusmvid200iez36q?singleDoc#"
                    target="_blank"
                    rel="noopener noreferrer"
                  >开发手册</a>
                </li>
                <li>
                  <span class="k"><span>交流测试群</span>：</span>
                  <a href="https://qm.qq.com/q/MGGZV58hOO" target="_blank" rel="noopener noreferrer">906497522</a>
                </li>
                <li>
                  <span class="k"><span>OMU 开发群</span>：</span>
                  <a href="https://qm.qq.com/q/MualpyNFoi" target="_blank" rel="noopener noreferrer">1084537740</a>
                </li>
                <li>
                  <span class="k"><span>语音聊天室</span>：</span>
                  <a href="https://oopz.cn/i/zzCOJF" target="_blank" rel="noopener noreferrer">oopz.cn/i/zzCOJF</a>
                </li>
                <li>
                  <span class="k"><span>Steam</span>：</span>
                  <a href="https://store.steampowered.com/app/4565740/Salasasa/" target="_blank" rel="noopener noreferrer">Salasasa 商店页</a>
                </li>
              </ul>
            </template>
            <template v-else-if="sec.id === 'regulations'">
              <div class="regulations-text" data-no-translate>{{ localizedRegulations }}</div>
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
    <el-dialog v-model="regulationsVisible" title="Salasasa-萨拉飒飒麻将平台账户规约"
      width="min(760px, 94vw)" append-to-body :close-on-click-modal="false">
      <div class="regulations-body" tabindex="0" aria-label="账户规约全文"><span data-no-translate>{{ regulationsBody }}</span></div>
      <template #footer>
        <el-button @click="regulationsVisible = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { locale, tr } from '@/i18n'
import accountRegulations from '@/content/accountRegulations.txt?raw'
import accountRegulationsFr from '@/content/accountRegulations.fr.txt?raw'

const regulationsVisible = ref(false)
const localizedRegulations = computed(() => locale.value === 'fr' ? accountRegulationsFr : accountRegulations)
const regulationsBody = computed(() => {
  const text = localizedRegulations.value
  return text.slice(text.indexOf('\n') + 1).trim()
})

function creditLine(source) {
  if (locale.value !== 'fr') return source
  return source.replace(/\(([^)]+)\)/g, (_, role) => `(${tr(role)})`)
    .replace(/(^|\s)([^()\s]+)\(/g, (_, space, name) => {
      const label = ['上海清混碰', '古典麻将', '宜兴麻将', 'Mil推广规则', '插件开发'].includes(name) ? tr(name) : name
      return `${space}${label} (`
    })
    .replace('FFMJ法国麻将协会', 'FFMJ — Fédération Française de Mah-Jong')
}

const seedCodeLabels = {
  'zh-CN': {
    characters: '万', dots: '饼', bamboos: '条', winds: '东南西北', dragons: '中白发',
    flowers: '春夏秋冬 梅兰竹菊', round: '立直/古典用 round_index，国标/青雀用 current_round',
  },
  'zh-TW': {
    characters: '萬', dots: '筒', bamboos: '索', winds: '東南西北', dragons: '中白發',
    flowers: '春夏秋冬 梅蘭竹菊', round: '立直/古典使用 round_index，國標/青雀使用 current_round',
  },
  'zh-HK': {
    characters: '萬', dots: '筒', bamboos: '索', winds: '東南西北', dragons: '中白發',
    flowers: '春夏秋冬 梅蘭竹菊', round: '立直/古典使用 round_index，國標/青雀使用 current_round',
  },
  en: {
    characters: 'Characters', dots: 'Dots', bamboos: 'Bamboos', winds: 'East, South, West, North',
    dragons: 'Red, White, Green', flowers: 'Spring, Summer, Autumn, Winter; Plum, Orchid, Bamboo, Chrysanthemum',
    round: 'Riichi/Classical use round_index; MCR/Qingque use current_round',
  },
  fr: {
    characters: 'Caractères', dots: 'Cercles', bamboos: 'Bambous', winds: 'Est, Sud, Ouest, Nord',
    dragons: 'Rouge, Blanc, Vert', flowers: 'Printemps, Été, Automne, Hiver ; Prunier, Orchidée, Bambou, Chrysanthème',
    round: 'Riichi/Classique : round_index ; MCR/Qingque : current_round',
  },
  ja: {
    characters: '萬子', dots: '筒子', bamboos: '索子', winds: '東・南・西・北',
    dragons: '中・白・發', flowers: '春・夏・秋・冬、梅・蘭・竹・菊',
    round: 'リーチ/古典では round_index、国標/青雀では current_round を使用',
  },
}

const seedExampleCode = computed(() => {
  const labels = seedCodeLabels[locale.value] || seedCodeLabels['zh-CN']
  return `import hashlib
import random

master_seed = 0x1234567890abcdef01234567890abcdef01234567890abcdef01234567890abcdef
salt = "0123456789abcdef0123456789abcdef"
commitment = int(hashlib.sha256((format(master_seed, '064x') + salt).encode()).hexdigest(), 16)

sth_tiles_set = {
    11, 12, 13, 14, 15, 16, 17, 18, 19,  # ${labels.characters}
    21, 22, 23, 24, 25, 26, 27, 28, 29,  # ${labels.dots}
    31, 32, 33, 34, 35, 36, 37, 38, 39,  # ${labels.bamboos}
    41, 42, 43, 44,                      # ${labels.winds}
    45, 46, 47,                          # ${labels.dragons}
}
hua_tiles_set = {51, 52, 53, 54, 55, 56, 57, 58}  # ${labels.flowers}
tiles_list = []
for tile in sth_tiles_set:
    tiles_list.extend([tile] * 4)
tiles_list.extend(hua_tiles_set)

# ${labels.round}
round_number = 1
round_random_seed = int(
    hashlib.sha256((format(master_seed, '064x') + str(round_number)).encode()).hexdigest(),
    16,
)
random.seed(round_random_seed)
random.shuffle(tiles_list)`
})

const sections = [
  { id: 'intro', title: '一、平台简介' },
  { id: 'features', title: '二、平台功能' },
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
  { id: 'regulations', title: '十五、萨拉飒飒平台规约' },
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

.faq-list p,
.sponsor-text {
  margin: 0;
  font-size: 14px;
  line-height: 1.7;
}

.sponsor-text {
  color: #444;
}

.faq-list a,
.link-list a {
  color: var(--accent-deep);
}

.guide-text-button {
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--accent-deep);
  font: inherit;
  text-align: left;
  text-decoration: underline;
  cursor: pointer;
}

.regulations-body {
  max-height: 58vh;
  overflow-y: auto;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  font-size: 14px;
  line-height: 1.85;
  color: #303133;
  padding: 0 12px 0 2px;
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

.thanks-list,
.thanks-message {
  overflow-wrap: anywhere;
}

.thanks-message {
  margin: 12px 0;
  font-size: 14px;
  line-height: 1.7;
  color: #444;
}

.regulations-text {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  font-size: 14px;
  line-height: 1.7;
  color: #444;
}

.thanks-list li,
.link-list li {
  font-size: 14px;
  line-height: 1.7;
  padding: 4px 0;
  color: #444;
}

.thanks-list li {
  display: grid;
  grid-template-columns: 7.5em minmax(0, 1fr);
  column-gap: 8px;
  align-items: start;
}

.thanks-list .k {
  color: #666;
  font-weight: 600;
}

.thanks-list li > span {
  min-width: 0;
}

.link-list .k {
  display: inline-block;
  min-width: 7.5em;
  color: #666;
  font-weight: 600;
  margin-right: 8px;
}

.link-list li {
  display: grid;
  grid-template-columns: 7.5em minmax(0, 1fr);
  gap: 2px 12px;
  align-items: baseline;
  padding: 6px 0;
}

.link-list .k {
  min-width: 0;
  margin: 0;
}

.link-list a {
  min-width: 0;
  max-width: 100%;
  justify-self: start;
  overflow-wrap: anywhere;
  text-underline-offset: 3px;
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

  .thanks-list li {
    grid-template-columns: minmax(0, 1fr);
  }

  .link-list li {
    grid-template-columns: minmax(0, 1fr);
    padding: 8px 0;
  }
}
</style>
