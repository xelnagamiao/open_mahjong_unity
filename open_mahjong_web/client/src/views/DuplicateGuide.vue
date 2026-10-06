<template>
  <article class="duplicate-guide">
    <header class="page-banner">
      <h1>创建复式牌墙</h1>
      <p>通过手动构建、随机种子复现、新建随机种子设置不同类型的复式密钥，供开设对局使用。</p>
    </header>
    <div class="panel">
      <nav class="entry-grid" aria-label="复式牌墙创建入口">
        <router-link :to="{ path: '/account', hash: '#sec-duplicate-personal' }" class="entry-card personal-entry">
          <h2>为个人创建</h2>
          <p>个人账户每天最多创建 5 个复式密钥，同时存储 25 个。</p>
          <span class="entry-action">进入个人管理面板 →</span>
        </router-link>
        <router-link :to="{ path: '/account', hash: '#sec-duplicate-event' }" class="entry-card event-entry">
          <h2>为赛事创建</h2>
          <p>每场赛事每天最多创建 25 个复式密钥，同时存储 100 个，赛事管理员共用额度。创建赛事房间时可自动生成密钥，批量创建同样计入额度。</p>
          <span class="entry-action">选择赛事并创建 →</span>
        </router-link>
      </nav>
      <section class="guide-section">
        <h2>复式麻将说明</h2>
        <p>复式麻将是指在比赛开始之前为每位玩家设置独立的手牌和牌山，并且提前规定好手牌和牌山的顺序，让多名玩家同时游玩同一副牌的同一个位置，最后比对这些相同位置玩家的最终数据，从而判断名次的规则。</p>
        <p><strong>例1：</strong><br>在个人赛中，一共有16名玩家，分成4桌玩同一副牌，每桌都有东南西北四位玩家，分别比对每一桌的东、南、西、北四个风位的四位玩家的最终数据，得出每个风位的最终优胜者</p>
        <p><strong>例2:</strong><br>在团队赛中，一共有16名玩家，每四位玩家为一队，每一队的四位玩家分别落座同一副牌的东南西北四个位置，最终比对每个队伍的顺位数据，得出四支队伍的最终优胜者</p>
        <p>可以看出，在复式规则下，个人赛可以保证与自己竞争的其他玩家有着相同的配牌和摸牌，团队赛可以保证每个队伍和自己竞争的其他队伍都有着相同的配牌和摸牌，虽然还是不能消除吃碰等副露互相影响带来的随机性问题，但是总体上确实公平了很多，对于受时间限制影响，赛程较短的比赛，复式规则有很大的价值。</p>
        <p>以上是复式麻将应用的经典例子，复式规则还有许多其他用法，在此不过多赘述，以下是复式麻将有别于正式规则的一些注意点</p>
        <ol>
          <li>标准国标规则是在牌山没有牌可以摸取的时候判定荒庄，而复式规则下，是四位玩家中有一位玩家在应当摸牌的时候没有剩余的卡牌可以摸取，当局将荒庄结束</li>
          <li>标准国标规则是在牌山最后一张牌被摸取的时候判定妙手回春，在牌山清零以后，打出最后一张牌造成的点和添加海底捞月；而复式规则下，如果自己的下家无牌可摸，自己摸的最后一张牌判定妙手回春，打出最后一张牌造成的点和添加海底捞月，与标准国标规则相同的，最后一张牌打出时仅允许点和，不得执行吃、碰、杠等操作。</li>
          <li>标准国标规则会根据每局的随机种子随机打乱房间中四位玩家的初始座次，而复式规则下，房间的一、二、三、四位直接对应初始风位的东、南、西、北。</li>
        </ol>
      </section>

      <section class="guide-section">
        <h2>生成复式牌山与密钥说明</h2>
        <p>平台提供三种创建复式牌山的方式</p>
        <div class="type-grid">
          <div><h3>手动牌山</h3><p>通过手动添加卡牌的方式构建手牌和牌山</p></div>
          <div><h3>复现牌山</h3><p>输入选中的随机种子构建手牌和牌山</p></div>
          <div><h3>密钥牌山</h3><p>自动生成随机种子生成牌墙，构建后的手牌和牌山创建者不可知</p></div>
        </div>
        <p>以上三种创建复式牌山的方式都会生成一个密钥，创建复式牌山者可以使用这个密钥创建复式房间，用户在房间面板也能看到复式是否开启，具体是这三种设置的哪一种，对于密钥的功能，可见以下描述。</p>
        <ol>
          <li>个人用户每天可以创建5个复式密钥，最多可以同时保留25个密钥</li>
          <li>作为赛事管理员的用户可以为申办完成的赛事每天创建25个复式牌墙，最多同时可以保留100个密钥</li>
          <li>创建完成的复式密钥会作为锁定状态存在，密钥管理员可以解锁密钥，密钥只有在解锁以后才能被删除</li>
          <li>复式比赛不保存本地牌谱，服务器牌谱在密钥解锁之前不得阅览，密钥解锁时间可以在数据站中查到，即便是被删除的密钥，在数据站中仍然保存密钥创建时间、解锁时间、是否删除或删除时间的具体信息，也可以查阅到根据密钥所创建的所有对局场次，供玩家验证。</li>
          <li>无论是手动牌山和复现牌山，办赛方都是提前知道本场对局的牌山信息的，只有密钥牌山在未解锁情况下创建的房间，并且对局中的选手不会泄露牌局信息的情况下，复式比赛对办赛方和参赛选手才是足够保密的。一个合理实践是按照“复式麻将说明”的两个例子，使用密钥牌山创建并行的复式对局，在对局结束以后解锁已经对局结束的密钥，再使用新的未使用过的密钥创建新的并行对局，由于所有可能透露信息的选手都在局中，这样的比赛方式是相当公平的。</li>
        </ol>
        <router-link to="/player-data/duplicate">查询已解禁的复式密钥</router-link>
      </section>
    </div>
  </article>
</template>

<style scoped>
.duplicate-guide { --accent: #17756a; --accent-deep: #125f56; color: #333; }
.page-banner { background: var(--accent); color: #fff; padding: 22px 20px; }
.page-banner h1 { margin: 0 0 6px; font-size: 1.45rem; font-weight: 700; }
.page-banner p { color: inherit; margin: 0; font-size: 13px; line-height: 1.5; opacity: .95; }
.panel { background: #fff; border: 1px solid #e0e0e0; border-top: 0; padding: 20px 20px 28px; }
.entry-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; }
.entry-card { display: flex; flex-direction: column; min-width: 0; min-height: 140px; padding: 22px 18px; color: #fff; text-decoration: none; box-shadow: 0 4px 14px rgba(0, 0, 0, .12); }
.personal-entry { background: var(--accent); }
.event-entry { background: #5470c6; }
.entry-card:hover { filter: brightness(1.05); }
.entry-card:focus-visible { outline: 2px solid var(--accent-deep); outline-offset: 3px; }
.entry-card h2 { color: inherit; margin: 0 0 8px; font-size: 1.2rem; font-weight: 700; }
.entry-card p { color: inherit; margin: 0 0 16px; font-size: 13px; line-height: 1.5; opacity: .95; }
.entry-action { margin-top: auto; font-size: 13px; font-weight: 600; }
.guide-section { padding: 18px 0 8px; border-bottom: 1px solid #eee; }
.guide-section:last-of-type { border-bottom: 0; }
.guide-section h2 { display: inline-block; min-width: 8em; margin: 0 0 12px; padding-bottom: 8px; border-bottom: 2px solid var(--accent); font-size: 1.12rem; font-weight: 700; color: #222; }
.guide-section h3 { margin: 0 0 8px; font-size: 14px; font-weight: 600; color: #333; }
.guide-section p, .guide-section li, .guide-section > a { font-size: 14px; line-height: 1.7; }
.guide-section p { margin: 12px 0; color: #444; }
.guide-section ol { margin: 0; padding-left: 1.35em; color: #444; }
.guide-section li { margin-bottom: 12px; padding-left: 4px; }
.guide-section li::marker { font-weight: 600; color: var(--accent-deep); }
.type-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
.type-grid > div { padding: 14px 16px; border: 1px solid #e0e0e0; background: #fafafa; }
.type-grid p { margin: 0; font-size: 13px; }
.guide-section > a { color: var(--accent-deep); }
@media (max-width: 640px) { .panel { padding: 14px 14px 24px; } .entry-grid, .type-grid { grid-template-columns: 1fr; } }
</style>
