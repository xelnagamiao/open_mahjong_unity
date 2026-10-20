<template>
  <main
    class="mahjongGame replay-page"
    :class="{ 'is-black-tile-face': appearance.tileFaceTheme === 'black' }"
    :style="{ background: appearance.backgroundColorOutside }"
  >
    <div class="replay-layout" :style="{ background: appearance.backgroundColorOutside }">
      <section class="replay-board" :style="{ background: appearance.backgroundColorTable }">
        <div
          ref="stageElement"
          class="game-stage"
          @click.capture="onBoardStep(1)"
          @contextmenu.capture.prevent="onBoardStep(-1)"
          @wheel.capture.prevent="onBoardWheel"
        />

        <ReplayIndependentWall
          v-if="replay && sceneReady && !loading && !errorMessage"
          :key="String(route.params.gameId)"
          :tiles="wallTilesWithHints"
          :remaining="remainingWall.length"
          :hints-enabled="chongHintEnabled"
          :shows-ron-danger="!isHangzhouReplay"
          :tile-asset="mmcrTileAsset"
          :round-index="roundIndex"
          :stage-element="stageElement"
        />

        <div v-if="replay && sceneReady" class="replay-board-tools">
          <button type="button" :class="{ 'is-active': showOtherHands }" @click="toggleOtherHands">
            {{ tr(showOtherHands ? '隐藏他家手牌' : '显示他家手牌') }}
          </button>
          <button type="button" :class="{ 'is-active': playWinAnimation }" @click="playWinAnimation = !playWinAnimation">{{ tr('播放和牌动画') }}</button>
          <button
            type="button"
            :class="{ 'is-active': chongHintEnabled }"
            :title="tr(isHangzhouReplay ? '查看牌山时标出摸牌预测（仅自摸）' : '标红他家待牌；查看牌山时标出摸牌预测')"
            @click="toggleChongHint"
          >
            {{ tr(isHangzhouReplay ? '摸牌预测' : '铳张提示') }}
          </button>
          <button type="button" :class="{ 'is-active': showMoqieMode }" @click="toggleMoqieHint">{{ tr('显示手摸切') }}</button>
          <button type="button" :class="{ 'is-active': wallVisible }" @click="wallVisible = !wallVisible">{{ tr('查看牌山') }}</button>
          <button type="button" :class="{ 'is-active': scoreboardOpen }" @click="scoreboardOpen = !scoreboardOpen">
            {{ tr(scoreboardOpen ? '关闭计分板' : '打开计分板') }}
          </button>
          <button type="button" :class="{ 'is-active': settingsOpen }" @click="settingsOpen = !settingsOpen">{{ tr('游戏设置') }}</button>
          <GuobiaoFanTable v-if="detail?.rule === 'guobiao'" />
        </div>

        <div v-if="wallVisible" class="replay-wall-layer">
        <section class="replay-wall" :aria-label="tr('当前牌山')">
          <header>
            <strong>{{ tr('牌山阅览') }}</strong>
            <span>{{ tr('剩余 {count} 张', { count: remainingWall.length }) }}</span>
            <span v-if="chongHintEnabled" class="replay-wall__legend">
              <i v-if="!isHangzhouReplay" class="is-danger">{{ tr('铳张') }}</i><i class="is-predicted">{{ tr('摸牌预测') }}</i>
            </span>
            <button type="button" @click="wallVisible = false">×</button>
          </header>
          <div class="replay-wall__content">
            <details v-if="duplicateInitialPartitions.length" class="duplicate-initial-wall">
              <summary>{{ tr('本局完整初始牌墙（{count} 张）', { count: duplicateInitialPartitions.reduce((total, tiles) => total + tiles.length, 0) }) }}</summary>
              <section v-for="(tiles, original) in duplicateInitialPartitions" :key="original" class="replay-wall__section">
                <h3>{{ tr('{player} 号 · {wind}位 · 前 {count} 张为起始手牌', { player: original + 1, wind: tr(winds[original]), count: original === 0 ? 14 : 13 }) }}</h3>
                <div class="replay-wall__hand-tiles"><span v-for="(tile, index) in tiles" :key="index"><TileFaceImage :src="mmcrTileAsset(tile)" :face-id="mmcrFaceId(tile)" :scale="0.86" :alt="tr('第 {index} 张', { index: index + 1 })" /></span></div>
              </section>
            </details>
            <section
              v-for="(hand, seat) in initialHands"
              :key="`initial-hand-${seat}`"
              class="replay-wall__section replay-wall__section--hand"
            >
              <h3>{{ tr('{wind}家初始手牌', { wind: tr(winds[seat]) }) }}</h3>
              <div class="replay-wall__hand-tiles">
                <span v-for="(tile, index) in hand" :key="`${seat}-${index}-${tile}`">
                  <TileFaceImage :src="mmcrTileAsset(tile)" :face-id="mmcrFaceId(tile)" :scale="0.86" />
                </span>
              </div>
            </section>
            <section v-for="group in wallGroups" :key="group.label" class="replay-wall__section replay-wall__section--wall">
              <h3>{{ tr(group.label) }}</h3>
              <div class="replay-wall__rows">
                <div v-for="row in Math.ceil(group.tiles.length / 4)" :key="`wall-row-${row}`" class="replay-wall__row">
                  <span
                    v-for="(item, offset) in group.tiles.slice((row - 1) * 4, row * 4)"
                    :key="`${row}-${offset}-${item.tile}`"
                    :class="{
                      'is-consumed': item.consumed,
                      'is-danger': item.isDanger,
                      'is-predicted': item.isPredicted,
                    }"
                  >
                    <TileFaceImage :src="mmcrTileAsset(item.tile)" :face-id="mmcrFaceId(item.tile)" :scale="0.86" />
                  </span>
                </div>
              </div>
            </section>
          </div>
        </section>
        </div>

        <GameScoreboardPanel
          v-if="scoreboardOpen"
          selectable
          :players="scoreboardPlayers"
          :settlements="scoreboardSettlements"
          :round-label-format="appearance.roundLabelFormat"
          :round-labeler="replayRoundLabeler"
          @select-row="jumpToScoreboardRound"
          @close="scoreboardOpen = false"
        />

        <div v-if="settingsOpen" class="replay-settings-layer" @click.self="settingsOpen = false">
          <section class="replay-settings-panel">
            <button type="button" class="replay-settings-panel__close" @click="settingsOpen = false">×</button>
            <SceneAppearancePanel
              :appearance="appearance"
              :background-image-name="backgroundImage?.name ?? null"
              :background-image-loading="backgroundImageLoading"
              :volume="volume"
              @volume="changeVolume"
              @round-label-format="setAppearanceField('roundLabelFormat', $event)"
              @table-color="setAppearanceField('backgroundColorTable', $event)"
              @outside-color="setAppearanceField('backgroundColorOutside', $event)"
              @image-enabled="setAppearanceField('backgroundImageEnabled', $event)"
              @image-alpha="setAppearanceField('backgroundImageAlpha', $event)"
              @image-selected="uploadBackgroundImage"
              @image-cleared="clearBackgroundImage"
              @cover-color="setTileCoverColor"
              @add-cover-color="addTileCoverColor"
              @remove-cover-color="removeTileCoverColor"
              @reorder-cover-colors="reorderTileCoverColors"
              @select-cover-index="selectTileCoverIndex"
              @cover-rotate-mode="setAppearanceField('tileCoverRotateMode', $event)"
              @flower-area-display="setAppearanceField('flowerAreaDisplay', $event)"
              @flower-area-color="setAppearanceField('flowerAreaColor', $event)"
              @flower-area-alpha="setAppearanceField('flowerAreaAlpha', $event)"
              @flower-area-label-color="setAppearanceField('flowerAreaLabelColor', $event)"
              @flower-area-count-color="setAppearanceField('flowerAreaCountColor', $event)"
              @flower-area-label-scale="setAppearanceField('flowerAreaLabelScale', $event)"
              @tile-face-theme="setAppearanceField('tileFaceTheme', $event)"
              @tile-label-mode="setAppearanceField('tileLabelMode', $event)"
              @flower-face-theme="setAppearanceField('flowerFaceTheme', $event)"
              @font-theme="setAppearanceField('fontTheme', $event)"
              @latin-font-theme="setAppearanceField('latinFontTheme', $event)"
              @reset="resetAppearance"
            />
          </section>
        </div>

        <div
          v-if="resultPanelVisible && roundResult"
          class="end-result-layer replay-end-result"
          :class="{ 'is-instant-fans': !playWinAnimation }"
        >
          <section
            v-if="roundResult.kind === 'draw'"
            class="end-result-panel end-result-panel--draw"
            :aria-label="tr('本局流局')"
          >
            <div
              v-for="seat in resultPlayers"
              :key="`draw-${seat.seat}`"
              class="end-result-draw-delta"
              :class="`is-rel-${seat.relative}`"
            >
              <span class="end-result-draw-delta__name" data-no-translate>{{ seat.player }}</span>
              <span
                class="end-result-draw-delta__change"
                :class="seat.value > 0 ? 'is-plus' : seat.value < 0 ? 'is-minus' : 'is-zero'"
              >
                {{ seat.value > 0 ? '+' : '' }}{{ seat.value }}
              </span>
            </div>
            <div class="end-result-draw-title">{{ tr('流局') }}</div>
            <button type="button" class="end-result-ready-button" @click="finishTerminalResult">{{ tr('确定') }}</button>
          </section>

          <section v-else class="end-result-panel end-result-panel--win" :aria-label="tr('本局和牌结算')">
            <div v-if="changshaInfo?.exchange?.settlements.length > 1" class="replay-viewpoints replay-winners">
              <button v-for="win in changshaInfo.exchange.settlements" :key="win.winner" type="button" :class="{ 'is-active': roundResult.winnerSeat === win.winner }" @click="exchangeResultSeat = win.winner">
                {{ replay?.playerForSeat(currentRound, win.winner)?.username || `玩家 ${win.winner + 1}` }} +{{ win.payment }}分
              </button>
            </div>
            <div v-if="resultClosedTiles.length || resultWinTile" class="end-result-hand" :style="resultHandStyle">
              <span v-for="(tile, index) in resultClosedTiles" :key="`closed-${index}`" class="end-result-tile">
                <TileFaceImage :src="mmcrTileAsset(tile)" :face-id="mmcrFaceId(tile)" />
              </span>
              <span v-if="resultMeldTiles.length" class="end-result-hand__split" />
              <span v-for="(tile, index) in resultMeldTiles" :key="`meld-${index}`" class="end-result-tile">
                <TileFaceImage :src="mmcrTileAsset(tile)" :face-id="mmcrFaceId(tile)" />
              </span>
              <span class="end-result-hand__split" />
              <span v-if="resultWinTile" class="end-result-tile is-winning">
                <TileFaceImage :src="mmcrTileAsset(resultWinTile)" :face-id="mmcrFaceId(resultWinTile)" :alt="tr('和牌张')" />
              </span>
            </div>
            <div v-if="changshaInfo?.birds.length" class="end-result-flowers" :aria-label="tr('鸟牌')">
              <span class="end-result-flowers__label">{{ tr('鸟牌') }}</span>
              <span v-for="(tile, index) in changshaInfo.birds" :key="`bird-${index}`" class="end-result-tile end-result-tile--flower">
                <TileFaceImage :src="mmcrTileAsset(salasasaTileToMmcr(tile))" :face-id="mmcrFaceId(salasasaTileToMmcr(tile))" />
              </span>
            </div>
            <div v-if="resultFlowerTiles.length" class="end-result-flowers">
              <span class="end-result-flowers__label">{{ tr('花') }}</span>
              <span v-for="(tile, index) in resultFlowerTiles" :key="`flower-${index}`" class="end-result-tile end-result-tile--flower">
                <TileFaceImage :src="mmcrTileAsset(tile)" :face-id="mmcrFaceId(tile)" />
              </span>
            </div>
            <div class="end-result-fan-grid" :style="{ minHeight: fanGridMinHeight }">
              <div
                v-for="(fan, index) in resultFans"
                :key="`${fan.name}-${index}`"
                class="end-result-fan"
                :class="{ 'is-visible': index < revealedFanCount }"
                :style="{ '--fan-name-size': `${fanNameFontSize(translateFanName(fan.name))}px` }"
              >
                <span>{{ translateFanName(fan.name) }}</span>
                <strong>{{ tr(fan.value) }}</strong>
              </div>
            </div>
            <div class="end-result-total" :class="{ 'is-visible': showResultTotal }">
              <div class="end-result-total__line">
                <span class="end-result-total__method">{{ winMethodLabel }}</span>
                <strong>{{ roundResult.fan }}</strong><span>{{ tr(detail?.rule === 'changchun' ? '基础番' : detail?.rule === 'guizhou' ? '基本分' : ['fuzhou', 'malaysia', 'yixing', 'hangzhou', 'jiujiang', 'shanxi', 'nanchang', 'shangrao', 'changsha'].includes(detail?.rule || '') ? '分' : detail?.rule === 'wenzhou' ? '倍' : '番') }}</span>
              </div>
            </div>
            <div class="end-result-diamond">
              <article
                v-for="slot in diamondSlots"
                :key="`result-seat-${slot.relative}`"
                class="end-result-seat"
                :class="[`is-rel-${slot.relative}`, { 'is-winner': slot.seat === roundResult.winnerSeat }]"
              >
                <strong class="end-result-seat__name" data-no-translate>{{ slot.player }}</strong>
                <div class="end-result-seat__score">
                  <span>{{ slot.score }}</span>
                  <span :class="slot.value > 0 ? 'is-plus' : slot.value < 0 ? 'is-minus' : 'is-zero'">
                    {{ slot.value > 0 ? '+' : '' }}{{ slot.value }}
                  </span>
                </div>
              </article>
            </div>
            <button v-if="showResultConfirm" type="button" class="end-result-ready-button" @click="finishTerminalResult">{{ tr('确定') }}</button>
          </section>
        </div>

        <HongzhongReplayDetail v-if="detail?.rule === 'hongzhong'" :info="hongzhongInfo" :ledger="hongzhongKongEvents" />
        <ChangchunReplayDetail v-if="changchunInfo" :info="changchunInfo" />
        <section v-if="shangraoInfo" class="nanchang-replay-info" aria-label="上饶宝牌与结算">
          <strong>指示牌 {{ wenzhouTileName(shangraoInfo.indicator) }} · 宝牌 {{ wenzhouTileName(shangraoInfo.bao) }}</strong>
          <p>仅自摸；蓝色牌为杠补自动弃牌，不能吃碰杠。宝牌杠弃不计飞宝。</p>
          <p v-if="shangraoInfo.ledger">本副净分：{{ shangraoInfo.ledger.changes.join(' / ') }}</p>
        </section>
        <section v-if="nanchangInfo" class="nanchang-replay-info" :aria-label="tr('南昌精牌与结算')">
          <strong>{{ tr('正精 {primary} · 副精 {secondary}', { primary: tr(wenzhouTileName(nanchangInfo.indicator)), secondary: tr(wenzhouTileName(nanchangInfo.secondary)) }) }}</strong>
          <p>{{ tr('四人单打 · 精牌翻开后仍在牌墙中，可正常摸取。') }}</p>
          <p v-for="row in nanchangLedgerRows(nanchangInfo)" :key="row.seat">{{ tr('{seat}：精分{jing}，杠分{kong}，和牌{win}，本副{total}分', { seat: tr(['东起','南起','西起','北起'][row.original]), jing: row.jing, kong: row.kong, win: row.win, total: row.total }) }}</p>
        </section>
        <section v-if="fuzhouInfo" class="wenzhou-replay-info" aria-label="福州开金与宣言">
          <strong>金牌 {{ fuzhouInfo.gold_tile ? wenzhouTileName(fuzhouInfo.gold_tile) : '未开金' }} · 连庄 {{ fuzhouInfo.dealer_streak || 0 }} 次</strong>
          <p>留牌 {{ fuzhouDisplay.reserve }} 张；{{ fuzhouDisplay.lastFour ? '末四张，只摸不打' : '字牌及八花均作花牌' }}</p>
          <p v-for="(decl,index) in fuzhouInfo.declarations" :key="index">{{ ['东起','南起','西起','北起'][fuzhouInfo.seat_to_original?.[index] ?? index] }}：{{ decl.youjin === 2 ? '二游' : decl.youjin === 1 ? '一游' : decl.golden_middle ? '听金坎' : decl.discarded_gold ? '已打金，仅自摸' : '普通行牌' }}</p>
        </section>
        <section v-if="wenzhouInfo" class="wenzhou-replay-info" :aria-label="tr('温州翻财与结算')">
          <strong>{{ tr('财神 {tile} · 连庄{count}次', { tile: tr(wenzhouTileName(wenzhouInfo.caishen)), count: wenzhouInfo.dealer_streak || 0 }) }}</strong>
          <p>{{ wenzhouInfo.caishen === 46 ? tr('白板为财神；牌面保留实体身份。') : tr('白板固定代{tile}；牌面保留实体身份。', { tile: tr(wenzhouTileName(wenzhouInfo.white_natural)) }) }}</p>
          <p v-for="row in wenzhouLedgerRows(wenzhouInfo)" :key="row.seat">{{ tr('{seat}：财神{count}张，财分{caishen}，本局{total}分', { seat: tr(['东起','南起','西起','北起'][row.original]), count: row.count, caishen: row.caishen, total: row.total }) }}</p>
          <p v-for="(entry, index) in wenzhouLedgerEntries(wenzhouInfo)" :key="`ledger-${index}`">{{ tr(entry) }}</p>
        </section>
        <GuizhouReplayLedger v-if="guizhouInfo?.ledger" :info="guizhouInfo" :players="detail?.players || []" />
        <HangzhouReplayState :info="hangzhouInfo" />
        <ChangshaReplayState v-if="changshaInfo" :info="changshaInfo" :tile-asset="mmcrTileAsset" />
        <GuangdongReplayLedger v-if="guangdongInfo" :info="guangdongInfo" :players="detail?.players || []" :seats="currentRound?.seats || [0, 1, 2, 3]" />
        <div v-if="loading || errorMessage" class="replay-state">
          <div class="replay-state__card">
            <strong>{{ tr(errorMessage ? '无法打开牌谱' : '正在读取牌谱') }}</strong>
            <span>{{ tr(errorMessage || '正在重建 2D 牌桌…') }}</span>
            <button v-if="errorMessage" type="button" @click="router.push('/2d')">{{ tr('返回 2D 大厅') }}</button>
          </div>
        </div>

        <div v-if="replay && sceneReady" class="replay-controls" :aria-label="tr('牌谱播放控制')">
          <div class="replay-controls__row">
            <button type="button" :title="tr('上一局')" :disabled="terminalLocked" @click="changeRound(-1)">«</button>
            <button type="button" :title="tr('上一步')" :disabled="terminalLocked" @click="step(-1)">‹</button>
            <button type="button" class="is-play" :disabled="terminalLocked" @click="togglePlaying">{{ tr(playing ? '暂停' : '播放') }}</button>
            <button type="button" :title="tr('下一步')" :disabled="terminalLocked" @click="step(1)">›</button>
            <button type="button" :title="tr('下一局')" :disabled="terminalLocked" @click="changeRound(1)">»</button>
            <label class="replay-controls__select">
              <span>{{ tr('局') }}</span>
              <select :value="roundIndex" :disabled="terminalLocked" @change="selectRound(Number(($event.target as HTMLSelectElement).value))">
                <option v-for="(round, index) in replay.rounds" :key="`round-select-${index}`" :value="index">
                  {{ roundSelectLabel(round, index) }}
                </option>
              </select>
            </label>
            <label class="replay-controls__select">
              <span>{{ tr('巡目') }}</span>
              <select :value="xunmuSelectValue" :disabled="terminalLocked" @change="selectXunmu(($event.target as HTMLSelectElement).value)">
                <option v-for="(_, index) in currentXunmuNodes" :key="`xunmu-${index}`" :value="String(index)">
                  {{ tr('{count}巡', { count: index }) }}
                </option>
                <option v-if="currentXunmuNodes.at(-1) !== maxNode" value="end">{{ tr('本局终点') }}</option>
              </select>
            </label>
            <span class="replay-controls__status">
              {{ tr(actionLabel) }} · {{ node }}/{{ maxNode }}
            </span>
          </div>
          <input
            v-model.number="node"
            class="replay-controls__range"
            type="range"
            min="0"
            :max="maxNode"
            step="1"
            :disabled="terminalLocked"
            :aria-label="tr('牌谱节点')"
          />
        </div>
      </section>

      <aside class="replay-sidebar">
        <header class="replay-header">
          <div>
            <p class="replay-header__eyebrow">{{ tr('SALASASA 2D 牌谱') }}</p>
            <h1>{{ detail?.game_id || route.params.gameId }}</h1>
          </div>
          <button type="button" class="replay-icon-button" :title="tr('返回大厅')" @click="router.push('/2d')">×</button>
        </header>

        <div v-if="!localRecord" class="replay-share">
          <div class="replay-share__split">
            <div class="replay-share__row">
              <button type="button" class="replay-primary-button replay-primary-button--2d" @click="copyShareLink('2d')">
                {{ tr(copiedKind === '2d' ? '链接已复制' : '复制分享链接') }}
              </button>
              <button
                type="button"
                class="replay-primary-button replay-primary-button--2d-node"
                @click="copyShareLink('2d-node')"
              >
                {{ tr(copiedKind === '2d-node' ? '已复制' : '复制当前Node') }}
              </button>
            </div>
            <div class="replay-share__row">
              <button type="button" class="replay-primary-button replay-primary-button--3d" @click="copyShareLink('3d')">
                {{ tr(copiedKind === '3d' ? '已复制' : '复制3D链接') }}
              </button>
              <button
                type="button"
                class="replay-primary-button replay-primary-button--3d-node"
                @click="copyShareLink('3d-node')"
              >
                {{ tr(copiedKind === '3d-node' ? '已复制' : '复制3D Node') }}
              </button>
            </div>
          </div>
        </div>

        <section v-if="replay" class="replay-section">
          <h2>{{ tr('选择小局') }}</h2>
          <div class="replay-rounds">
            <button
              v-for="(_, index) in replay.rounds"
              :key="index"
              type="button"
              :disabled="terminalLocked"
              :class="[{ 'is-active': roundIndex === index }, roundOutcomeClass(index)]"
              @mouseenter="moveRoundTooltip(index, $event)"
              @mousemove="moveRoundTooltip(index, $event)"
              @mouseleave="hoveredRoundIndex = null"
              @focus="showRoundTooltipFromFocus(index, $event)"
              @blur="hoveredRoundIndex = null"
              @click="selectRound(index)"
            >
              {{ index + 1 }}
            </button>
          </div>
          <div
            v-if="hoveredRoundIndex != null"
            class="replay-round-tooltip"
            :style="{ left: `${roundTooltipPosition.x}px`, top: `${roundTooltipPosition.y}px` }"
          >
            <strong>{{ tr('第 {round} 局分数变动', { round: hoveredRoundIndex + 1 }) }}</strong>
            <span
              v-for="row in roundScoreChangeRows(hoveredRoundIndex)"
              :key="`${hoveredRoundIndex}-${row.original}`"
              :class="{ 'is-viewer': row.original === viewerOriginal }"
            >
              <em data-no-translate>{{ row.username }}</em>
              <b :class="{ 'is-plus': row.change > 0, 'is-minus': row.change < 0 }">
                {{ row.change > 0 ? '+' : '' }}{{ row.change }}
              </b>
            </span>
          </div>
        </section>

        <section v-if="replay" class="replay-section">
          <h2>{{ tr('查看视角') }}</h2>
          <div class="replay-viewpoints">
            <button
              v-for="player in viewpointPlayers"
              :key="player.original"
              type="button"
              :class="{ 'is-active': viewerOriginal === player.original }"
              @click="viewerOriginal = player.original"
            >
              <span :title="tr(player.wind)">{{ player.windShort }}</span>
              <strong data-no-translate>{{ player.username }}</strong>
              <small>{{ player.score }}</small>
            </button>
          </div>
        </section>

        <section v-if="detail" class="replay-section replay-meta">
          <h2>{{ tr('对局信息') }}</h2>
          <div ref="gameInfoScrollElement" class="replay-meta__scroll" @wheel.prevent="onGameInfoWheel">
            <dl>
              <div v-for="row in gameInfoRows" :key="row.label">
                <dt>{{ tr(row.label) }}</dt>
                <dd :class="{ 'is-code': row.code }" :data-no-translate="row.verbatim ? '' : null">{{ row.verbatim ? row.value : tr(row.value) }}</dd>
              </div>
            </dl>
          </div>
        </section>

        <p class="replay-keyboard">{{ tr('鼠标左键下一步、右键上一步，滚轮切换巡目；键盘 ←/→ 步进，Shift + ←/→ 切换小局，空格播放或暂停。') }}</p>
      </aside>
    </div>
  </main>
</template>

<script setup lang="ts">
import { huahunFanPoints } from "../../utils/guobiaoHuahun.js"
import { computed, nextTick, onBeforeUnmount, onMounted, provide, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { washizuDoublesSummary, captainResultReason } from '@/utils/washizuDoubles.js'
import { sichuanDoublesSummary } from '@/utils/sichuanReplay.js'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import { ElMessage } from 'element-plus'
import { MahjongScene } from '@/game2d/game/scene/MahjongScene'
import { GAME_SOUND_ASSETS, getPreloadedSoundUrl } from '@/game2d/game/resources'
import { playerProfileUrl, publicApiGet, publicRecordUrl } from '@/game2d/salasasa/api'
import { isExternalRecord, externalPlayerName } from '@/utils/recordConvert/externalPlayers.js'
import { RecordReplay, isRecordSilentTick, type PublicGameRecord, type RecordRound, type RecordTick } from '@/game2d/replay/recordReplay'
import { isLocalReplayRecord, loadLocalReplayRecord } from '@/game2d/replay/localReplayRecord'
import { getLocalGameRecord, isLocalOnlyGameId } from '@/utils/localGameRecordStore'
import {
  loadStoredSceneAppearance,
  loadStoredVolume,
  resetStoredSceneAppearance,
  saveStoredSceneAppearance,
  saveStoredVolume,
} from '@/game2d/lib/storage'
import {
  clearStoredSceneBackgroundImage,
  loadStoredSceneBackgroundImage,
  saveStoredSceneBackgroundImage,
} from '@/game2d/lib/sceneBackgroundImage'
import {
  DEFAULT_SCENE_APPEARANCE,
  MAX_TILE_COVER_COLORS,
  normalizeSceneAppearanceSettings,
} from '@/game2d/lib/sceneAppearance'
import { tingpaiCheck } from '@/game2d/calc/guobiao'
import { buildLocalWaitData } from '@/game2d/calc/guobiao/waitTips'
import { buildShanxiWaitData, shanxiWaits, shanxiMeldTiles, shanxiKnownConcealedDiscards } from '@/game2d/calc/shanxi/waitTips'
import { mmcrTileToSalasasa, salasasaTileToMmcr } from '@/game2d/salasasa/gameAdapter'
import { formatFanField, resolveFanLabel } from '@/constants/guessFanCatalog'
import { lanshiFanPoints } from '@/game2d/calc/guobiao/lanshiV4'
import { formatFanCount, translateFanName } from '@/i18n/fanNames'
import { locale, roundLabelKey, tr } from '@/i18n'
import { replaySharePath } from '@/utils/recordShareLink'
import { riichiWinLineLabel } from '@/utils/riichiWinRequirements.js'
import {
  mmcrSettlementSortKey,
  splitSettlementHand,
} from '@/game2d/lib/settlementHand'
import { mmcrFaceId, tileFaceAssetUrl } from '@/game2d/lib/tileFaceAsset'
import { shouldShowTileLabels } from '@/game2d/lib/tileLabels'
import type { ActiveSessionSnapshot, MeldSnapshot } from '@/game2d/game/scene/types'
import GameScoreboardPanel from './GameScoreboardPanel.vue'
import GuobiaoFanTable from '@/components/GuobiaoFanTable.vue'
import ReplayIndependentWall from './ReplayIndependentWall.vue'
import SceneAppearancePanel from './SceneAppearancePanel.vue'
import TileFaceImage from './TileFaceImage.vue'
import GuizhouReplayLedger from '@/components/GuizhouReplayLedger.vue'
import { guizhouInfoAt, guizhouReadyAt, parseGuizhouFan } from '@/utils/guizhouReplay.js'
import { buildGuizhouWaitData, guizhouRonWaitingTiles } from '@/game2d/calc/guizhou/waitTips'
import { relativeSeat } from '@/game2d/lib/seatLayout.js'
import { parseFuzhouFan, fuzhouInfoAt, fuzhouHintsAt, fuzhouWaitDataAt, fuzhouReserve, isFuzhouV2 } from '@/utils/fuzhouReplay.js'
import { parseMalaysiaFan, malaysiaInfoAt } from '@/utils/malaysiaReplay.js'
import { parseYixingFan } from '@/utils/yixingReplay.js'
import { parseWenzhouFan, wenzhouInfoAt, wenzhouTileName, wenzhouLedgerRows, wenzhouLedgerEntries, wenzhouWaitData, wenzhouWaitsAt } from '@/utils/wenzhouReplay.js'
import { parseNanchangFan, nanchangInfoAt, nanchangLedgerRows, nanchangWaitData, nanchangWaitsAt } from '@/utils/nanchangReplay.js'
import {parseShangraoFan,shangraoInfoAt,shangraoWallAt,shangraoVisibleIndicators} from '@/utils/shangraoReplay.js'
import { jokerFirstSortKey } from '@/game2d/lib/handJokers.js'
import { parseHangzhouFan, hangzhouInfoAt, hangzhouKnownTiles, hangzhouWaitDataAt, hangzhouNormalDrawableWallIndices } from '@/utils/hangzhouReplay.js'
import HangzhouReplayState from '@/components/HangzhouReplayState.vue'
import ChangshaReplayState from '@/components/ChangshaReplayState.vue'
import { isChangshaRecord, changshaInfoAt } from '@/utils/changshaReplay.js'
import { changshaWaits, changshaMeldTiles, buildChangshaWaitData, exchangeSixWaits } from '@/utils/changshaWaits.js'
import { exchangeSixResultTick } from '@/utils/changshaExchangeSixReplay.js'
import HongzhongReplayDetail from '@/components/HongzhongReplayDetail.vue'
import ChangchunReplayDetail from '@/components/ChangchunReplayDetail.vue'
import { changchunInfoAt, isChangchunRecord, CHANGCHUN_FANS } from '@/utils/changchunReplay.js'
import { buildChangchunWaitData, qualifiedWaits as changchunQualifiedWaits } from '@/game2d/calc/changchun'
import { jiujiangWaitDataAt, jiujiangHintsAt, jiujiangKnownTiles, parseJiujiangFan } from '@/utils/jiujiangReplay.js'
import { parseShanxiFan } from '@/utils/shanxiPresentation.js'
import { hongzhongInfoAt, hongzhongKongEventsAt, parseHongzhongFan, hongzhongWaitDataAt, hongzhongKnownTiles } from '@/utils/hongzhongReplay.js'
import { guangdongInfoAt, isGuangdongMilRecord, parseGuangdongFan } from '@/utils/guangdongReplay.js'
import { regionalReplayHints, regionalReplayWaitData } from '@/utils/regionalReplayHints.js'
import GuangdongReplayLedger from '@/components/GuangdongReplayLedger.vue'

const route = useRoute()
const router = useRouter()
const playerAuth = usePlayerAuthStore()
const stageElement = ref<HTMLElement | null>(null)
const loading = ref(true)
const errorMessage = ref('')
const detail = ref<PublicGameRecord | null>(null)
const replay = ref<RecordReplay | null>(null)
const roundIndex = ref(0)
const node = ref(0)
const viewerOriginal = ref(0)
const actionLabel = ref('局初')
const playing = ref(false)
const sceneReady = ref(false)
const copiedKind = ref<'2d' | '2d-node' | '3d' | '3d-node' | null>(null)
const localRecord = computed(() => {
  const gameId = String(route.params.gameId || '')
  return isLocalReplayRecord(gameId) || isLocalOnlyGameId(gameId)
})
const currentScores = ref<number[]>([])
const showOtherHands = ref(true)
const playWinAnimation = ref(false)
const chongHintEnabled = ref(true)
const isHangzhouReplay = computed(() => detail.value?.rule === 'hangzhou')
const replayRoundLabeler = computed(() => isHangzhouReplay.value || detail.value?.rule === 'fuzhou' ? hangzhouRoundLabel
  : detail.value?.rule === 'shanxi' ? shanxiRoundLabel : null)

function shanxiRoundLabel(round: number) {
  return tr('第{round}庄', { round })
}
const hangzhouRoundLabel = (number: number) => tr(`第${number}局`)
const showMoqieMode = ref(true)
const gameInfoScrollElement = ref<HTMLElement | null>(null)
const wallVisible = ref(false)
const resultPanelVisible = ref(false)
const revealedFanCount = ref(0)
const showResultTotal = ref(false)
const showResultConfirm = ref(false)
const terminalLocked = ref(false)
const scoreboardOpen = ref(false)
const settingsOpen = ref(false)
const hoveredRoundIndex = ref<number | null>(null)
const roundTooltipPosition = ref({ x: 0, y: 0 })
const currentRanks = ref<Record<string, string>>({})
const backgroundImage = ref<Awaited<ReturnType<typeof loadStoredSceneBackgroundImage>>>(null)
const backgroundImageLoading = ref(true)
const appearance = ref(loadStoredSceneAppearance())
provide('game2d.tileLabelsEnabled', computed(() => shouldShowTileLabels(appearance.value.tileLabelMode, locale.value)))
const volume = ref(loadStoredVolume())
let scene: MahjongScene | null = null
let playTimer: number | null = null
let skipNextPositionRender = false
let resultTimers: number[] = []
let activeTerminalKey = ''

const maxNode = computed(() => replay.value?.rounds[roundIndex.value]?.action_ticks?.length ?? 0)
const currentRound = computed(() => replay.value?.rounds[roundIndex.value])
const winds = ['东', '南', '西', '北']
const viewpointPlayers = computed(() => {
  if (!replay.value || !currentRound.value) return []
  return Array.from({length: replay.value?.playerCount || 4}, (_, original) => {
    const seat = replay.value!.rounds[roundIndex.value].seats?.[original] ?? original
    const player = replay.value!.playerForSeat(currentRound.value!, seat)
    return {
      original,
      wind: winds[seat] || String(seat + 1),
      windShort: locale.value === 'en' ? ['E', 'S', 'W', 'N'][seat]
        : locale.value === 'fr' ? ['E', 'S', 'O', 'N'][seat] : tr(winds[seat] || String(seat + 1)),
      username: player?.username || tr(`玩家 ${original + 1}`),
      score: currentScores.value[original] ?? player?.score ?? 0,
    }
  })
})

const formattedCreatedAt = computed(() => {
  if (!detail.value?.created_at) return '—'
  const date = new Date(detail.value.created_at)
  return Number.isNaN(date.getTime()) ? detail.value.created_at : date.toLocaleString(locale.value, { hour12: false })
})

const roomLabel = computed(() => {
  if (detail.value?.room_type === 'match') return '匹配房间'
  if (detail.value?.room_type === 'events') return '赛事房间'
  return '自定义房间'
})

interface GameInfoRow {
  label: string
  value: string
  code?: boolean
  verbatim?: boolean
}

function hasGameTitleField(title: Record<string, unknown>, key: string): boolean {
  return Object.prototype.hasOwnProperty.call(title, key) && title[key] != null
}

function enabledLabel(value: unknown): string {
  return tr(value === true || value === 1 || String(value).toLowerCase() === 'true' ? '开' : '关')
}

function compactRecordTime(value: unknown): string {
  const text = String(value ?? '').trim()
  if (!text) return '—'
  return text.replace('T', ' ').replace(/(\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?$/, '')
}

function ruleDisplayName(rule: unknown): string {
  const names: Record<string, string> = {
    guobiao: '国标麻将',
    riichi: '立直麻将',
    classical: '古典麻将',
    qingque: '青雀麻将',
    sichuan: '四川麻将',
    changsha: '长沙麻将',
    taiwan: '台湾麻将',
    hongkong: '香港麻将',
    guangdong: '广东麻将',
    guizhou: '贵州麻将',
    malaysia: '马来西亚三人麻将',
    yixing: '宜兴麻将',
    fuzhou: '福州麻将',
    wenzhou: '温州麻将',
    nanchang: '南昌麻将',
    shangrao: '上饶麻将',
    hangzhou: '杭州麻将',
    hongzhong: '红中麻将',
    jiujiang: '九江麻将',
    changchun: '长春麻将',
    shanxi: '山西麻将',
  }
  const key = String(rule ?? '').toLowerCase()
  return names[key] || String(rule || '—')
}

function subRuleDisplayName(subRule: unknown): string {
  const names: Record<string, string> = {
    'guobiao/standard': '国标标准',
    'guobiao/huahun': '国标（花混癞子）',
  'guobiao/sixteen': '国标麻将（十六张）',
  'guobiao/four_jokers': '国标麻将(四癞子)',
    'riichi/washizu_doubles': '鹫巢麻将（双打）',
    'sichuan/doubles': '血战双打·2V2', 'sichuan/xueliu_doubles': '血流双打·2V2',
    'hongzhong/mil2024': 'MIL 红中2024',
    'jiujiang/standard': '九江麻将',
    'changchun/mil2024': 'MIL 长春2024',
    'hangzhou/mil2025': 'MIL 杭州2025',
    'shanxi/mil2023': 'MIL 山西2023',
    'fuzhou/standard': '福州十六张',
  }
  const key = String(subRule ?? '')
  return names[key] || key || '—'
}

function roundModeLabel(maxRound: unknown, queueType: unknown): string {
  if (detail.value?.rule === 'fuzhou') return tr('{count} 局', { count: Math.max(1, Number(maxRound) || 1) * (isFuzhouV2(detail.value?.record?.game_title?.rule_version) ? 4 : 6) })
  if (detail.value?.rule === 'shanxi') {
    const count = Number(maxRound)
    return Number.isFinite(count) && count > 0 ? tr('{count}庄', { count: Math.trunc(count) * 4 }) : '—'
  }
  if (isHangzhouReplay.value) {
    const count = Number(maxRound)
    return Number.isFinite(count) && count > 0 ? tr('{count} 局', { count: Math.trunc(count) * 4 }) : '—'
  }
  const queue = String(queueType ?? '').toLowerCase()
  if (queue.endsWith('_dongfeng')) return '东风'
  if (queue.endsWith('_banzhuang')) return '半庄'
  if (queue.endsWith('_quanzhuang')) return '全庄'
  const count = Number(maxRound)
  if (count === 1) return '东风'
  if (count === 2) return '半庄'
  if (count === 4) return '全庄'
  return Number.isFinite(count) && count > 0 ? tr('{count} 圈', { count }) : '—'
}

function matchTierLabel(value: unknown): string {
  const labels: Record<string, string> = {
    beginner: '初级场',
    intermediate: '中级场',
    advanced: '高级场',
    mcrpl: 'MCRPL',
  }
  const key = String(value ?? '').toLowerCase()
  return labels[key] || String(value || '—')
}

const gameInfoRows = computed<GameInfoRow[]>(() => {
  const title = detail.value?.record?.game_title || {}
  const rows: GameInfoRow[] = []
  const push = (label: string, value: unknown, code = false, verbatim = code || /^玩家 |^入场顺序$/.test(label)) => {
    const text = String(value ?? '').trim()
    if (text) rows.push({ label, value: text, code, verbatim })
  }
  const queueType = title.match_queue_type
  const queueTier = String(queueType ?? '').split('_')[0]

  push('规则', ruleDisplayName(title.rule ?? detail.value?.rule))
  const captain = washizuDoublesSummary(detail.value)
  if (captain) {
    for (const player of captain.players) push(player.role, player.name, false, true)
    if (captain.result) {
      const result = captain.result
      push('主将点数', `A队 ${result.scores[0]} / B队 ${result.scores[1]}`)
      push('比赛分', `A队 ${result.match_points[0]} / B队 ${result.match_points[1]}`)
      push('胜负判定', captainResultReason(result))
      if (result.busted_captain_scores) push('击飞时主将点数', `A队 ${result.busted_captain_scores[0]} / B队 ${result.busted_captain_scores[1]}`)
    }
  }
  const doubles = sichuanDoublesSummary(detail.value)
  if (doubles) {
    for (const team of doubles.teams) push(team.name, team.players.join(' / '), false, true)
    if (doubles.result) {
      const result = doubles.result
      const scoreText = (scores: Record<number, number>) => doubles.teams.map(team => `${tr(team.name)} ${scores[team.id]}`).join(' / ')
      if (result.small_scores && result.imp_scores) {
        push('团队累计副分', scoreText(result.small_scores))
        push('团队累计 IMP', scoreText(result.imp_scores))
        push('胜负依据', result.reason === 'imp' ? 'IMP' : '副分')
      } else push(result.reason === 'imp' ? '团队累计 IMP' : '团队累计副分', scoreText(result.scores))
      push('盘分', scoreText(result.match_points))
      push('胜负判定', result.reason === 'last_positive_board' ? '同分，追平队胜' : result.reason === 'draw' ? '平局，各0.5盘分' : '按团队累计副分')
    }
  }
  push('房间', roomLabel.value)
  push('完成时间', hasGameTitleField(title, 'end_time') ? compactRecordTime(title.end_time) : formattedCreatedAt.value)
  if (hasGameTitleField(title, 'sub_rule') || detail.value?.sub_rule) {
    push('子规则', subRuleDisplayName(title.sub_rule ?? detail.value?.sub_rule))
  }
  if (detail.value?.match_type && detail.value?.rule !== 'fuzhou') {
    push('对局类型', String(detail.value.match_type).includes('rank') ? '排位对局' : detail.value.match_type)
  }
  if (hasGameTitleField(title, 'match_tier') || queueTier) {
    push('场次', matchTierLabel(title.match_tier ?? queueTier))
  }
  if (hasGameTitleField(title, 'max_round') || queueType) {
    push('局制', roundModeLabel(title.max_round, queueType))
  }
  if (title.rule === 'fuzhou') push('底分', `${title.detailed_config?.base_score ?? (isFuzhouV2(title.rule_version) ? 15 : 10)}分`)
  else if (title.rule === 'jiujiang') push('起和分', '1分（含花分）')
  else if (detail.value?.rule === 'shanxi') push('起和牌值', '自摸≥3点，点和≥6点（须报听）')
  else if (hasGameTitleField(title, 'hepai_limit')) push('起和番', tr(title.rule === 'riichi' ? '{count} 番（不含宝牌）' : '{count} 番', { count: Number(title.hepai_limit) || 0 }))
  if (title.rule === 'riichi') push('起和线', riichiWinLineLabel(title.detailed_config?.minimum_win_score ?? 0))
  if (title.rule === 'shanghai' && (title.sub_rule || 'shanghai/qiaoma') === 'shanghai/qiaoma') {
    push('清三混二碰二', enabledLabel(title.detailed_config?.qing3_hun2_peng2 === true))
    push('开启天敲', title.detailed_config?.enable_tian_qiao === true ? '开（2番）' : '关')
  }
  if (title.rule === 'shanghai' && title.sub_rule === 'shanghai/lajihe') {
    push('自摸再加一底', enabledLabel(title.detailed_config?.self_draw_extra_base === true))
  }
  if (isChangshaRecord(detail.value)) {
    const config = changshaInfoAt(currentRound.value, node.value, title)
    push('开杠张数', `${config.openKongCount}张`)
    push('卡摸（小胡仅自摸）', enabledLabel(config.kamo))
    push('封顶方式', config.capType === 'big_hu' ? `${config.capValue}大胡（鸟另计）` : config.capType === 'score' ? `${config.capValue}分（含鸟）` : '不封顶')
  }
  if (hasGameTitleField(title, 'round_timer')) push('局时', tr('{count} 秒', { count: Number(title.round_timer) || 0 }))
  if (hasGameTitleField(title, 'step_timer')) push('步时', tr('{count} 秒', { count: Number(title.step_timer) || 0 }))
  if (hasGameTitleField(title, 'open_cuohe')) push('错和', enabledLabel(title.open_cuohe))
  if (hasGameTitleField(title, 'tips')) push('对局提示', enabledLabel(title.tips))
  if (hasGameTitleField(title, 'allow_spectator')) push('允许观战', enabledLabel(title.allow_spectator))
  if (hasGameTitleField(title, 'tactical_call')) push('战术鸣牌', enabledLabel(title.tactical_call))
  if (hasGameTitleField(title, 'claim_protection')) push('鸣牌保护', enabledLabel(title.claim_protection))
  if (hasGameTitleField(title, 'is_player_set_random_seed')) {
    push('场景复现', enabledLabel(title.is_player_set_random_seed))
  }
  if (hasGameTitleField(title, 'duplicate_wall_type')) push('复式', ({ manual: '手动牌山', seed: '复现牌山', key: '密钥牌山' } as Record<string, string>)[String(title.duplicate_wall_type)] || title.duplicate_wall_type)
  if (hasGameTitleField(title, 'duplicate_key')) push('复式密钥', title.duplicate_key, true)
  if (hasGameTitleField(title, 'duplicate_round_count')) push('复式局数', tr('{count} 局', { count: title.duplicate_round_count }))
  if (hasGameTitleField(title, 'duplicate_seed')) push('复式种子', title.duplicate_seed, true)
  if (hasGameTitleField(title, 'use_flowers')) push('花牌', enabledLabel(title.use_flowers))
  if (title.sub_rule === 'guobiao/huahun') {
    push('无癞子奖励', title.no_joker_bonus === 'none' ? '不额外加分' : title.no_joker_bonus === 'double' ? '无癞子翻倍' : '无癞子+8分')
    push('黑花递增计分', enabledLabel(title.black_flower_progressive !== false))
  }
  if (['guobiao/huahun', 'guobiao/four_jokers'].includes(title.sub_rule)) push('癞子多面听', enabledLabel(!!title.joker_multi_wait))
  if (title.tian_di_ren_he) push('天地人和', '开启（各8番）')
  if (hasGameTitleField(title, 'start_time')) push('开始时间', compactRecordTime(title.start_time))

  const entryOrder = Array.isArray(title.player_entry_order) ? title.player_entry_order : []
  if (entryOrder.length) {
    const usernames = entryOrder.map((userId) => {
      const player = detail.value?.players.find((item) => Number(item.user_id) === Number(userId))
      return player?.username || String(userId)
    })
    push('入场顺序', usernames.join(' → '))
  }
  for (let index = 0; index < 4; index += 1) {
    const name = title[`p${index}_name`]
    const userId = isExternalRecord(title)
      ? title[`p${index}_external_id`] ?? title[`p${index}_tziakcha_id`]
      : title[`p${index}_uid`]
    if (name != null || userId != null) {
      push(`玩家 ${index}`, `${name || '—'}${userId != null ? ` · ID ${userId}` : ''}`)
    }
  }
  if (hasGameTitleField(title, 'commitment_hex')) push('承诺值', title.commitment_hex, true)
  if (hasGameTitleField(title, 'salt')) push('盐值', title.salt, true)
  if (hasGameTitleField(title, 'master_seed_hex')) push('主种子', title.master_seed_hex, true)
  return rows
})

/**
 * 错和 tick：fan 列表包含「错和」的 hu_* 动作。服务端记录格式中错和无 end、
 * 对局继续，因此它只是普通节点，不能被当作本局终点（真正的和牌/流局在其后）。
 */
function isCuoheTick(tick: RecordTick | undefined): boolean {
  if (!tick?.length) return false
  const action = String(tick[0] ?? '')
  if (!action.startsWith('hu_')) return false
  return Array.isArray(tick[3]) && tick[3].includes('错和')
}

const hongzhongKongEvents = computed(() => detail.value?.rule === 'hongzhong' ? hongzhongKongEventsAt(currentRound.value, node.value) : [])
const changshaInfo = computed(() => isChangshaRecord(detail.value) ? changshaInfoAt(currentRound.value, node.value, detail.value?.record?.game_title) : null)
const hongzhongInfo = computed(() => detail.value?.rule === 'hongzhong' ? hongzhongInfoAt(currentRound.value, node.value) : null)
const changchunInfo = computed(() => detail.value?.rule === 'changchun' ? changchunInfoAt(currentRound.value, node.value, currentRound.value?.seats?.[viewerOriginal.value] ?? viewerOriginal.value) : null)
const hangzhouInfo = computed(() => detail.value?.rule === 'hangzhou' ? hangzhouInfoAt(currentRound.value, node.value) : null)
const fuzhouInfo = computed(() => detail.value?.rule === 'fuzhou' ? fuzhouInfoAt(currentRound.value,node.value) : null)
const fuzhouDisplay = computed(() => {
  if (!fuzhouInfo.value || !replay.value) return {reserve:18,lastFour:false}
  const snapshot=replay.value.build(roundIndex.value,node.value).snapshot
  const reserve=fuzhouReserve(snapshot.seats.map(seat=>({melds:seat.melds,flowers:(seat.flower_tiles || []).map(mmcrTileToSalasasa)})), fuzhouInfo.value.rule_version ?? detail.value?.record?.game_title?.rule_version)
  return {reserve,lastFour:snapshot.state.remaining_tile_count<=4}
})
const wenzhouInfo = computed(() => detail.value?.rule === 'wenzhou' ? wenzhouInfoAt(currentRound.value, node.value) : null)
const shangraoInfo = computed(() => detail.value?.rule === 'shangrao' ? shangraoInfoAt(currentRound.value,node.value) : null)
const nanchangInfo = computed(() => detail.value?.rule === 'nanchang' ? nanchangInfoAt(currentRound.value, node.value) : null)
const guizhouInfo = computed(() => detail.value?.rule === 'guizhou' ? guizhouInfoAt(currentRound.value, node.value) : null)
const guangdongInfo = computed(() => isGuangdongMilRecord(detail.value) ? guangdongInfoAt(currentRound.value, node.value) : null)

const exchangeResultSeat = ref<number | null>(null)
watch([roundIndex, node], () => { exchangeResultSeat.value = null })
const roundResult = computed(() => {
  const round = currentRound.value
  if (!round || node.value <= 0) return null
  const ticks = (round.action_ticks || []).slice(0, node.value)
  let tick = [...ticks].reverse().find((item) => {
    if (isCuoheTick(item)) return false
    const action = String(item?.[0] ?? '')
    return action.startsWith('hu_') || action === 'liuju' || action === 'ryuukyoku'
  })
  if (!tick) return null
  if (changshaInfo.value?.exchange) tick = exchangeSixResultTick(tick, changshaInfo.value.exchange.settlements, exchangeResultSeat.value)
  const action = String(tick[0] ?? '')
  const malaysiaInfo = detail.value?.rule === 'malaysia' ? malaysiaInfoAt(round, node.value) : null
  const scoreValues = shangraoInfo.value?.ledger?.changes ?? (malaysiaInfo?.round_changes ? round.seats!.map((_, seat) => malaysiaInfo.round_changes[round.seats!.indexOf(seat)]) : nanchangInfo.value?.ledger?.changes ? nanchangInfo.value.ledger.changes : wenzhouInfo.value?.round_changes ? wenzhouInfo.value.round_changes : action === 'liuju' && guizhouInfo.value?.ledger ? guizhouInfo.value.ledger.score_changes
    : action === 'liuju' && changshaInfo.value?.exchange ? [...ticks].reverse().find(t => t[0] === 'changsha_exchange_six_draw_scores')?.[1]
    : action === 'ryuukyoku' || action === 'liuju' ? tick[2] : tick[4])
  const changes = Array.isArray(scoreValues)
    ? scoreValues.slice(0, 4).map((value, seat) => ({
      seat,
      player: replay.value?.playerForSeat(round, seat)?.username || tr(`玩家 ${seat + 1}`),
      value: Number(value) || 0,
    }))
    : []
  if (!action.startsWith('hu_')) return { kind: 'draw' as const, changes, tick, action }
  const winnerSeat = Number(tick[1]) || 0
  return {
    kind: 'win' as const,
    player: replay.value?.playerForSeat(round, winnerSeat)?.username || tr(`玩家 ${winnerSeat + 1}`),
    fan: guangdongInfo.value ? Number(guangdongInfo.value.result?.fan ?? (Array.isArray(tick[3]) ? tick[3].reduce((sum, label) => { const parts = String(label).split('|'); return sum + (parts[0] === 'GD' && /^\d+$/.test(parts[2] || '') ? Number(parts[2]) : 0) }, 0) : 0)) : Number(tick[2]) || 0,
    fans: Array.isArray(tick[3]) ? tick[3].map(String) : [],
    changes,
    tick,
    action,
    winnerSeat,
  }
})

const resultPosition = computed(() => (
  replay.value ? replay.value.build(roundIndex.value, node.value, viewerOriginal.value, true) : null
))
const resultWinner = computed(() => {
  if (roundResult.value?.kind !== 'win') return null
  return resultPosition.value?.snapshot.seats.find((seat) => seat.seat_index === roundResult.value!.winnerSeat) ?? null
})
const resultWinTile = computed(() => {
  if (roundResult.value?.kind !== 'win') return 0
  const fromTick = Number(roundResult.value.tick[5])
  if (fromTick > 0) return salasasaTileToMmcr(fromTick)
  return resultWinner.value?.drawn_tile ?? 0
})
const resultClosedTiles = computed(() => {
  const tiles = [...(resultWinner.value?.hand_tiles ?? [])]
  if (isChangshaRecord(detail.value) && resultWinner.value?.drawn_tile != null) {
    return tiles.sort((left, right) => mmcrSettlementSortKey(left) - mmcrSettlementSortKey(right))
  }
  if (resultWinner.value?.drawn_tile != null) tiles.push(resultWinner.value.drawn_tile)
  return splitSettlementHand(
    tiles,
    resultWinTile.value,
    resultWinner.value?.melds.length ?? 0,
    tile => jokerFirstSortKey(tile, resultPosition.value?.snapshot.hand_joker_tiles ?? [], mmcrSettlementSortKey),
  )
})
function expandResultMeld(meld: MeldSnapshot): number[] {
  if (meld.type === 'sequence') return [meld.tile - 1, meld.tile, meld.tile + 1]
  return Array(meld.type === 'kong' ? 4 : 3).fill(meld.tile)
}
const resultMeldTiles = computed(() => (resultWinner.value?.melds ?? []).flatMap(expandResultMeld))
// Five melds and extra kong tiles should leave the winning tile visible.
// On narrow screens keep readable tiles and the existing horizontal scroll.
const resultHandStyle = computed(() => {
  if (detail.value?.rule !== 'fuzhou') return undefined
  const tileCount = resultClosedTiles.value.length + resultMeldTiles.value.length + (resultWinTile.value ? 1 : 0)
  return { '--end-result-tile-width': `clamp(24px, calc((100% - 40px) / ${Math.max(1, tileCount)}), 42px)` }
})
const resultFlowerTiles = computed(() => resultWinner.value?.flower_tiles ?? [])
const resultFans = computed(() => roundResult.value?.kind === 'win'
  ? roundResult.value.fans.map((name) => {
    if (isChangshaRecord(detail.value)) return { name, value: '' }
    if ((detail.value?.sub_rule || detail.value?.record?.game_title?.sub_rule) === "guobiao/huahun" && huahunFanPoints(name) != null) return {name, value: `${huahunFanPoints(name)}番`}
    if (detail.value?.rule === 'shanxi') return parseShanxiFan(name)
    const guizhouFan = detail.value?.rule === 'guizhou' ? parseGuizhouFan(name) : null
    if (guizhouFan) return guizhouFan
    const fuzhouFan = detail.value?.rule === 'fuzhou' ? parseFuzhouFan(name) : null
    if (fuzhouFan) return fuzhouFan
    const malaysiaFan = detail.value?.rule === 'malaysia' ? parseMalaysiaFan(name) : null
    if (malaysiaFan) return malaysiaFan
    const yixingFan = detail.value?.rule === 'yixing' ? parseYixingFan(name) : null
    if (yixingFan) return yixingFan
    const wenzhouFan = detail.value?.rule === 'wenzhou' ? parseWenzhouFan(name) : null
    if (wenzhouFan) return wenzhouFan
    if (detail.value?.rule === 'shangrao') return parseShangraoFan(name) || {name,value:''}
    if (detail.value?.rule === 'nanchang') return parseNanchangFan(name) || {name,value:""}
    const hangzhouFan = detail.value?.rule === 'hangzhou' ? parseHangzhouFan(name) : null
    if (hangzhouFan) return hangzhouFan
    if (detail.value?.rule === 'jiujiang') return parseJiujiangFan(name)
    if (detail.value?.rule === 'hongzhong') return parseHongzhongFan(name) || { name, value: `${hongzhongInfo.value?.detail?.fan_values?.[name] ?? 0}番` }
    if (detail.value?.rule === 'changchun') return { name, value: `${CHANGCHUN_FANS[name] ?? 0}番` }
    if (detail.value?.rule === 'shanghai' && (detail.value.sub_rule || detail.value.record?.game_title?.sub_rule || 'shanghai/qiaoma') === 'shanghai/qiaoma') {
      const encoded = name.match(/^(.+)（(\d+)(番|花)）$/)
      if (encoded) return { name: encoded[1], value: `${encoded[2]}${encoded[3]}` }
      const profile = detail.value.record?.game_title?.detailed_config?.qing3_hun2_peng2 === true
      const fans: Record<string, number> = { '清一色': profile ? 3 : 2, '混一色': profile ? 2 : 1, '碰碰和': profile ? 2 : 1, '门清': 1, '大吊车': 1, '杠开': 1, '抢杠': 1, '天敲': 2 }
      return { name, value: fans[name] == null ? '' : `${fans[name]}番` }
    }
    const guangdongFan = guangdongInfo.value ? parseGuangdongFan(name) : null
    if (guangdongFan) return guangdongFan
    if ((detail.value?.sub_rule || detail.value?.record?.game_title?.sub_rule) === 'guobiao/lanshi') {
      const points = lanshiFanPoints(name)
      return { name: translateFanName(name), value: points == null ? '' : `${points}分` }
    }
    const { definition, totalValue } = resolveFanLabel(name, ['guobiao'])
    const openingFan = detail.value?.record?.game_title?.tian_di_ren_he && ['天和', '地和', '人和'].includes(name)
    const value = openingFan ? '8番' : definition
      ? `${totalValue ?? formatFanField(definition.fan)}番`
      : ''
    return {
      name: translateFanName(name),
      value: value ? formatFanCount(value.replace(/番$/, '')) : '',
    }
  })
  : [])
function fanNameFontSize(value: string): number {
  const units = Array.from(String(value || '')).reduce(
    (total, character) => total + (/^[\x00-\xff]$/.test(character) ? 0.55 : 1),
    0,
  )
  if (units <= 7) return 20
  return Math.max(12, Math.round((20 * 7 / units) * 10) / 10)
}
const winMethodLabel = computed(() => {
  if (detail.value?.rule === 'hangzhou' && hangzhouInfo.value?.ledger?.source === 'ten_winds') return tr('十风和')
  return roundResult.value?.kind === 'win' && roundResult.value.action === 'hu_self' ? tr('自摸') : tr('点和')
})
const fanGridMinHeight = computed(() => `${Math.max(1, Math.ceil((resultFans.value.length || 1) / 2)) * 36}px`)
const resultPlayers = computed(() => {
  const round = currentRound.value
  const snapshot = resultPosition.value?.snapshot
  if (!round || !snapshot) return []
  const viewerSeat = round.seats?.[viewerOriginal.value] ?? viewerOriginal.value
  const changes = new Map((roundResult.value?.changes ?? []).map((change) => [change.seat, change.value]))
  return snapshot.seats.map((seat) => ({
    seat: seat.seat_index,
    relative: relativeSeat(seat.seat_index, viewerSeat, replay.value?.playerCount || 4),
    player: seat.username || tr(`玩家 ${seat.seat_index + 1}`),
    score: seat.score,
    value: changes.get(seat.seat_index) ?? 0,
  }))
})
const diamondSlots = computed(() => {
  const byRelative = new Map(resultPlayers.value.map((item) => [item.relative, item]))
  return [2, 3, 1, 0].map((relative) => byRelative.get(relative) ?? {
    seat: -1, relative, player: '—', score: 0, value: 0,
  })
})
const remainingWall = computed(() => replay.value?.remainingWallAt(roundIndex.value, node.value) ?? [])
const wallTiles = computed(() => replay.value?.wallViewAt(roundIndex.value, node.value) ?? [])
const replayDangerBySeat = computed(() => {
  const snapshot = resultPosition.value?.snapshot
  const dangerBySeat = new Map<number, Set<number>>()
  if (!snapshot || !chongHintEnabled.value || ['hangzhou', 'hongzhong'].includes(detail.value?.rule || '')) return dangerBySeat

  const waitsBySeat = new Map<number, Set<number>>()
  for (const seat of snapshot.seats) {
    waitsBySeat.set(seat.seat_index, waitingTilesForSnapshotSeat(seat))
  }

  const hiddenSeat = waitingForDrawAfterCut(snapshot) ? Number(snapshot.state.last_actor) : -1
  for (const seat of snapshot.seats) {
    const danger = new Set<number>()
    if (seat.seat_index !== hiddenSeat) {
      for (const [otherSeat, waits] of waitsBySeat) {
        if (otherSeat === seat.seat_index) continue
        for (const tile of waits) danger.add(tile)
      }
    }
    dangerBySeat.set(seat.seat_index, danger)
  }
  return dangerBySeat
})
const predictedWallIndices = computed(() => {
  const snapshot = resultPosition.value?.snapshot
  const tiles = wallTiles.value
  const predicted = new Set<number>()
  if (!snapshot || !chongHintEnabled.value || tiles.length === 0) return predicted
  if (tiles.some(item => item.originalPlayer !== undefined)) {
    if (tiles.some(item => item.commonActive)) {
      const anchor = waitingForDrawAfterCut(snapshot) ? Number(snapshot.state.last_actor) : Number(snapshot.state.current_player)
      const offset = (Number(snapshot.viewer.seat_index) - anchor + 3) % 4
      let remaining = 0
      tiles.forEach((item, index) => {
        if (!item.consumed && remaining++ % 4 === offset) predicted.add(index)
      })
      return predicted
    }
    tiles.forEach((item, index) => {
      if (!item.consumed && item.originalPlayer === viewerOriginal.value) predicted.add(index)
    })
    return predicted
  }

  let front = 0
  while (front < tiles.length && tiles[front].consumed) front += 1
  const currentPlayer = Number(snapshot.state.current_player)
  const anchor = waitingForDrawAfterCut(snapshot)
    ? Number(snapshot.state.last_actor)
    : currentPlayer
  const viewerSeat = Number(snapshot.viewer.seat_index)
  const offset = (viewerSeat - anchor + 4) % 4

  if (detail.value?.rule === 'shangrao') {
    shangraoWallAt(currentRound.value, node.value).normalIndices.forEach((index: number, order: number) => {
      if ((order + 1) % 4 === offset) predicted.add(index)
    })
    return predicted
  }

  const hangzhouDrawable = detail.value?.rule === 'hangzhou' || detail.value?.record.game_title?.rule === 'hangzhou'
    ? hangzhouNormalDrawableWallIndices(tiles) : null
  for (let index = front + offset - 1; index < tiles.length; index += 4) {
    if (index < front) continue
    if (!tiles[index].consumed && (!hangzhouDrawable || hangzhouDrawable.has(index))) predicted.add(index)
  }
  return predicted
})
const wallTilesWithHints = computed(() => {
  const dangerTiles = new Set<number>()
  for (const waits of replayDangerBySeat.value.values()) {
    for (const tile of waits) dangerTiles.add(tile)
  }
  return wallTiles.value.map((item, index) => ({
    ...item,
    isDanger: chongHintEnabled.value && dangerTiles.has(item.tile),
    isPredicted: chongHintEnabled.value && predictedWallIndices.value.has(index),
  }))
})
const initialHands = computed(() => replay.value?.initialHandsAt(roundIndex.value, node.value) ?? [])
const duplicateInitialPartitions = computed(() => {
  const title = detail.value?.record.game_title
  const tiles = title?.duplicate_round_tiles?.[Number(currentRound.value?.current_round || 1) - 1] || title?.duplicate_tiles
  if (!Array.isArray(tiles) || ![136, 144].includes(tiles.length)) return []
  const perSeat = tiles.length / 4
  return [0, 1, 2, 3].map(seat => tiles.slice(seat * perSeat, (seat + 1) * perSeat).map(salasasaTileToMmcr))
})
const wallGroups = computed(() => {
  if (Array.isArray(currentRound.value?.duplicate_walls)) {
    const groups = [0, 1, 2, 3].map(original => ({ label: tr('{player} 号玩家固定牌山', { player: original + 1 }), tiles: wallTilesWithHints.value.filter(item => item.originalPlayer === original && !item.shared) }))
    if (currentRound.value.duplicate_wall_layout === 'xueliu') groups.push({ label: '公共牌墙', tiles: wallTilesWithHints.value.filter(item => item.shared) })
    return groups
  }
  return [{ label: '牌山', tiles: wallTilesWithHints.value }]
})
const currentXunmuNodes = computed(() => replay.value?.xunmuNodes(roundIndex.value, viewerOriginal.value) ?? [0])
const currentXunmu = computed(() => {
  let index = 0
  currentXunmuNodes.value.forEach((target, targetIndex) => {
    if (target <= node.value) index = targetIndex
  })
  return index
})
const xunmuSelectValue = computed(() => (
  node.value === maxNode.value && currentXunmuNodes.value.at(-1) !== maxNode.value
    ? 'end'
    : String(currentXunmu.value)
))
const scoreboardPlayers = computed(() => {
  if (!replay.value || !detail.value) return []
  return Array.from({length: replay.value?.playerCount || 4}, (_, original) => {
    const player = detail.value!.players.find((item) => item.original_player_index === original)
      || detail.value!.players[original]
    return {
      player_index: original,
      original_player_index: original,
      user_id: player?.user_id ?? 0,
      username: player?.username ?? tr(`玩家 ${original + 1}`),
      score: Number(player?.score ?? 0),
      score_history: replay.value!.rounds.map((_, index) => {
        const value = replay.value!.roundScoreChangesByOriginal(index)[original] ?? 0
        return value > 0 ? `+${value}` : String(value)
      }),
      round_number_history: replay.value!.rounds.map((round, index) => (
        Number(round.current_round ?? index + 1)
      )),
    }
  })
})
const scoreboardSettlements = computed(() => replay.value?.rounds.map((round) => {
  const terminal = terminalTick(round)
  if (!terminal) return '—'
  const action = String(terminal[0] ?? '')
  if (action === 'liuju' || action === 'ryuukyoku') return '流局'
  const fans = Array.isArray(terminal[3]) ? terminal[3].map(String) : []
  const mainFan = fans.find((fan) => !fan.startsWith('花牌')) || fans[0]
  if (detail.value?.rule === 'shangrao') return parseShangraoFan(mainFan)?.name || `${Number(terminal[2]) || 0}分`
  return (detail.value?.rule === 'fuzhou' ? parseFuzhouFan(mainFan)?.name : detail.value?.rule === 'malaysia' ? parseMalaysiaFan(mainFan)?.name : detail.value?.rule === 'guizhou' ? parseGuizhouFan(mainFan)?.name : detail.value?.rule === 'yixing' ? parseYixingFan(mainFan)?.name : detail.value?.rule === 'nanchang' ? parseNanchangFan(mainFan)?.name : detail.value?.rule === 'wenzhou' ? parseWenzhouFan(mainFan)?.name : detail.value?.rule === 'hangzhou' ? parseHangzhouFan(mainFan)?.name : detail.value?.rule === 'shanxi' ? parseShanxiFan(mainFan).name : isGuangdongMilRecord(detail.value) ? parseGuangdongFan(mainFan)?.name : mainFan) || `${Number(terminal[2]) || 0}${['fuzhou', 'malaysia', 'guizhou', 'yixing', 'hangzhou', 'jiujiang', 'shanxi', 'nanchang', 'shangrao'].includes(detail.value?.rule || '') ? '分' : '番'}`
}) ?? [])

function stopPlaying() {
  playing.value = false
  if (playTimer != null) window.clearInterval(playTimer)
  playTimer = null
}

function startPlaying() {
  if (!replay.value || terminalLocked.value) return
  stopPlaying()
  playing.value = true
  playTimer = window.setInterval(() => {
    if (node.value < maxNode.value) {
      advanceAnimatedStep()
    } else if (roundIndex.value < replay.value!.rounds.length - 1) {
      roundIndex.value += 1
      node.value = 0
    } else {
      stopPlaying()
    }
  }, 700)
}

function togglePlaying() {
  if (playing.value) stopPlaying()
  else startPlaying()
}

function clearResultTimers() {
  for (const timer of resultTimers) window.clearTimeout(timer)
  resultTimers = []
}

function resetTerminalPresentation() {
  clearResultTimers()
  resultPanelVisible.value = false
  terminalLocked.value = false
  activeTerminalKey = ''
}

function scheduleResult(callback: () => void, delay: number) {
  resultTimers.push(window.setTimeout(callback, delay))
}

function finishTerminalResult() {
  if (!terminalLocked.value) return
  const nextRound = roundIndex.value + 1
  resetTerminalPresentation()
  // A Changsha batch may pay several winners, or the same winner for two tiles.
  // Confirm each recorded payment before leaving the round.
  if (isChangshaRecord(detail.value) && currentRound.value?.action_ticks?.slice(node.value)
    .some(tick => String(tick?.[0]).startsWith('hu_') && !isCuoheTick(tick))) {
    advanceAnimatedStep()
    return
  }
  if (replay.value && nextRound < replay.value.rounds.length) {
    roundIndex.value = nextRound
    node.value = 0
  }
}

function revealReplayWinHand(audible: boolean) {
  if (!scene || !replay.value || !currentRound.value) return
  const ticks = currentRound.value.action_ticks || []
  let terminalIndex = Math.min(node.value - 1, ticks.length - 1)
  while (terminalIndex >= 0) {
    const terminalAction = String(ticks[terminalIndex]?.[0] ?? '')
    if (terminalAction.startsWith('hu_')) break
    terminalIndex -= 1
  }
  if (terminalIndex < 0) return
  const update = replay.value.eventForStep(
    roundIndex.value,
    terminalIndex,
    viewerOriginal.value,
    showOtherHands.value,
  )
  if (!update) return
  decorateEventRanks(update)
  update.event = { ...update.event, silent: !audible }
  scene.handleEvent(update)
}

function presentTerminalResult(action: string, handRevealApplied = false) {
  const terminalKey = `${roundIndex.value}:${node.value}:${action}`
  if (terminalLocked.value && activeTerminalKey === terminalKey) return
  activeTerminalKey = terminalKey
  terminalLocked.value = true
  stopPlaying()
  clearResultTimers()
  if (action === 'liuju' || action === 'ryuukyoku') {
    resultPanelVisible.value = true
    revealedFanCount.value = 0
    showResultTotal.value = false
    showResultConfirm.value = true
    scheduleResult(finishTerminalResult, 3500)
    return
  }
  if (!action.startsWith('hu_')) {
    resetTerminalPresentation()
    return
  }
  // The table win action is independent from the settlement-panel animation:
  // always reveal the winner's hand and play the final win sound.
  if (!handRevealApplied) revealReplayWinHand(true)
  if (!playWinAnimation.value) {
    revealedFanCount.value = resultFans.value.length
    showResultTotal.value = true
    showResultConfirm.value = true
    // Keep the same table-side "和" / voice / hand-reveal presentation time.
    // Only the fan rows themselves skip their staggered entrance.
    scheduleResult(() => {
      resultPanelVisible.value = true
    }, 1500)
    return
  }
  revealedFanCount.value = 0
  showResultTotal.value = false
  showResultConfirm.value = false
  scheduleResult(() => {
    resultPanelVisible.value = true
    resultFans.value.forEach((_, index) => {
      scheduleResult(() => {
        revealedFanCount.value = index + 1
        scene?.playUiSound('fan-reveal')
      }, index * 500)
    })
    scheduleResult(() => {
      showResultTotal.value = true
      showResultConfirm.value = true
      if (roundResult.value?.kind === 'win') scene?.playResultGong(roundResult.value.fans, roundResult.value.fan, detail.value?.rule || 'guobiao')
    }, resultFans.value.length * 500 + 350)
  }, 1500)
}

function nextVisibleNode(ticks: RecordTick[], node: number): number {
  let index = node
  while (index < ticks.length && isRecordSilentTick(ticks[index])) index += 1
  return index
}

function previousUserStepNode(ticks: RecordTick[], node: number): number {
  let lastVisible = -1
  for (let index = node - 1; index >= 0; index -= 1) {
    if (!isRecordSilentTick(ticks[index])) {
      lastVisible = index
      break
    }
  }
  if (lastVisible < 0) return 0
  let start = lastVisible
  while (start > 0 && isRecordSilentTick(ticks[start - 1])) start -= 1
  return start
}

function step(delta: number) {
  if (terminalLocked.value) return
  stopPlaying()
  resetTerminalPresentation()
  const ticks = currentRound.value?.action_ticks ?? []
  if (delta > 0 && node.value < maxNode.value) {
    advanceAnimatedStep()
    return
  }
  if (delta < 0) {
    const prev = previousUserStepNode(ticks, node.value)
    if (prev < node.value) {
      node.value = prev
      return
    }
    if (roundIndex.value > 0) {
      roundIndex.value -= 1
      node.value = roundBackTargetNode(roundIndex.value)
    }
    return
  }
  const next = node.value + delta
  if (next >= 0 && next <= maxNode.value) {
    node.value = next
  } else if (next > maxNode.value && replay.value && roundIndex.value < replay.value.rounds.length - 1) {
    roundIndex.value += 1
    node.value = 0
  }
}

function roundBackTargetNode(index: number): number {
  const ticks = replay.value?.rounds[index]?.action_ticks ?? []
  const terminalIndex = ticks.findIndex((tick) => {
    if (isCuoheTick(tick)) return false
    const action = String(tick?.[0] ?? '')
    return action.startsWith('hu_') || action === 'liuju' || action === 'ryuukyoku'
  })
  // node is the number of ticks already applied, so the terminal tick's index
  // is exactly the stable position immediately before its presentation starts.
  return terminalIndex >= 0 ? terminalIndex : ticks.length
}

function advanceAnimatedStep() {
  if (!scene || !replay.value || node.value >= maxNode.value) return
  const ticks = currentRound.value?.action_ticks ?? []
  const playNode = nextVisibleNode(ticks, node.value)
  if (playNode >= ticks.length) {
    node.value = ticks.length
    return
  }
  // Guizhou has silent physical mutations (opening-kong reveals and the
  // unique source tile of multi-ron). Apply those before the next animation.
  if (ticks.slice(node.value, playNode).some((entry) => ['fuzhou', 'malaysia', 'guizhou', 'wenzhou', 'nanchang', 'shangrao'].includes(String(entry[0])))) {
    const before = replay.value.build(roundIndex.value, playNode, viewerOriginal.value, showOtherHands.value)
    decorateSnapshotRanks(before.snapshot)
    scene.flushFromSnapshot(before.snapshot)
  }
  const tick = ticks[playNode]
  const action = String(tick?.[0] ?? '')
  const rawUpdate = replay.value.eventForStep(
    roundIndex.value,
    playNode,
    viewerOriginal.value,
    showOtherHands.value,
  )
  // 错和不是本局终点：只推进节点，不执行和牌展示，避免牌桌停留在亮牌状态。
  const update = isCuoheTick(tick) ? null : rawUpdate
  const nextNode = playNode + 1
  const nextPosition = replay.value.build(
    roundIndex.value,
    nextNode,
    viewerOriginal.value,
    showOtherHands.value,
  )
  decorateSnapshotRanks(nextPosition.snapshot)
  decorateEventRanks(update)
  node.value = nextNode
  if (update) {
    // Replay stepping uses the same board mutation path as the live game. In
    // particular, chow/pung/kong now move tiles out of the river and hand instead
    // of replacing the whole table with the post-action snapshot.
    if (update.replay_snapshot_required) {
      // A public replacement batch is a separate row, never an ordinary hand draw.
      // Rebuild its physical state before playing a win's call/reveal animation.
      scene.flushFromSnapshot(nextPosition.snapshot)
      if (action.startsWith('hu_')) scene.handleEvent(update)
    } else if (['fuzhou', 'malaysia', 'wenzhou', 'nanchang', 'shangrao'].includes(detail.value?.rule || '') && ['cl', 'cm', 'cr', 'p', 'g', 'ag', 'jg'].includes(action)) {
      let physicalNode = nextNode
      while (physicalNode < ticks.length && isRecordSilentTick(ticks[physicalNode])) physicalNode++
      const physical = replay.value.build(roundIndex.value, physicalNode, viewerOriginal.value, showOtherHands.value)
      scene.flushFromSnapshot(physical.snapshot)
      nextPosition.snapshot = physical.snapshot
      node.value = physicalNode
    } else scene.handleEvent(update)
    refreshReplayHints(nextPosition.snapshot)
    actionLabel.value = nextPosition.actionLabel
    const seatMap = replay.value.rounds[roundIndex.value].seats || [0, 1, 2, 3]
    currentScores.value = seatMap.map((seat) => nextPosition.snapshot.seats[seat]?.score ?? 0)
    skipNextPositionRender = true
  }
  if (!isCuoheTick(tick) && (action.startsWith('hu_') || action === 'liuju' || action === 'ryuukyoku')) {
    presentTerminalResult(action, Boolean(update))
  }
}

function onBoardStep(delta: number) {
  if (loading.value || errorMessage.value || !sceneReady.value) return
  step(delta)
}

function onBoardWheel(event: WheelEvent) {
  if (loading.value || errorMessage.value || !sceneReady.value || terminalLocked.value || Math.abs(event.deltaY) < 1) return
  stopPlaying()
  resetTerminalPresentation()
  const nodes = currentXunmuNodes.value
  if (event.deltaY > 0) {
    const target = nodes.find((value) => value > node.value)
    if (target != null) {
      node.value = target
    } else if (node.value < maxNode.value) {
      node.value = maxNode.value
    }
  } else {
    const target = [...nodes].reverse().find((value) => value < node.value)
    if (target != null) {
      node.value = target
    } else if (roundIndex.value > 0 && replay.value) {
      roundIndex.value -= 1
      const previousNodes = replay.value.xunmuNodes(roundIndex.value, viewerOriginal.value)
      node.value = previousNodes.at(-1) ?? 0
    }
  }
}

function scoreValuesFromTick(tick: RecordTick): number[] | null {
  const action = String(tick[0] ?? '')
  const value = action === 'ryuukyoku' ? tick[2] : action.startsWith('hu_') ? tick[4] : null
  return Array.isArray(value) && value.length >= 4 ? value.map((item) => Number(item) || 0) : null
}

function terminalTick(round: RecordRound): RecordTick | null {
  return [...(round.action_ticks || [])].reverse().find((tick) => {
    if (isCuoheTick(tick)) return false
    const action = String(tick?.[0] ?? '')
    return action.startsWith('hu_') || action === 'liuju' || action === 'ryuukyoku'
  }) || null
}

function roundOutcomeClass(index: number) {
  const round = replay.value?.rounds[index]
  const terminal = round ? terminalTick(round) : null
  if (!round || !terminal) return ''
  const action = String(terminal[0] ?? '')
  if (action === 'liuju' || action === 'ryuukyoku') return 'is-draw'
  if (!action.startsWith('hu_')) return ''
  const seat = round.seats?.[viewerOriginal.value] ?? viewerOriginal.value
  const winnerSeat = Number(terminal[1])
  if (seat === winnerSeat) return 'is-win'
  const change = scoreValuesFromTick(terminal)?.[seat] ?? 0
  if (change >= 0) return ''
  if (action === 'hu_self') return 'is-tsumo-loss'
  const terminalIndex = (round.action_ticks || []).indexOf(terminal)
  const beforeWin = replay.value?.build(index, Math.max(0, terminalIndex), viewerOriginal.value)
  const discarderSeat = Number(beforeWin?.snapshot.state.last_actor)
  return seat === discarderSeat ? 'is-ron-loss' : ''
}

function roundScoreChangeRows(index: number) {
  if (!replay.value) return []
  const changes = replay.value.roundScoreChangesByOriginal(index)
  return Array.from({length: replay.value?.playerCount || 4}, (_, original) => {
    const player = detail.value?.players.find((item) => item.original_player_index === original)
      || detail.value?.players[original]
    return {
      original,
      username: player?.username || tr(`玩家 ${original + 1}`),
      change: changes[original] ?? 0,
    }
  })
}

function moveRoundTooltip(index: number, event: MouseEvent) {
  hoveredRoundIndex.value = index
  roundTooltipPosition.value = {
    x: event.clientX - 9,
    y: event.clientY - 9,
  }
}

function showRoundTooltipFromFocus(index: number, event: FocusEvent) {
  hoveredRoundIndex.value = index
  const element = event.currentTarget as HTMLElement | null
  const rect = element?.getBoundingClientRect()
  roundTooltipPosition.value = {
    x: (rect?.left ?? 260) - 9,
    y: (rect?.top ?? 140) - 9,
  }
}

function roundSelectLabel(round: RecordRound, index: number) {
  const number = Number(round.current_round ?? index + 1)
  if (replayRoundLabeler.value) return replayRoundLabeler.value(number)
  return tr(roundLabelKey(number, appearance.value.roundLabelFormat, locale.value))
}

function selectRound(index: number) {
  if (terminalLocked.value) return
  stopPlaying()
  resetTerminalPresentation()
  roundIndex.value = index
  node.value = 0
}

function selectXunmu(value: string) {
  if (terminalLocked.value) return
  stopPlaying()
  resetTerminalPresentation()
  if (value === 'end') {
    node.value = maxNode.value
    return
  }
  const index = Math.max(0, Math.min(currentXunmuNodes.value.length - 1, Number(value) || 0))
  node.value = currentXunmuNodes.value[index] ?? 0
}

function jumpToScoreboardRound(index: number) {
  if (!replay.value || index < 0 || index >= replay.value.rounds.length) return
  scoreboardOpen.value = false
  selectRound(index)
}

function changeRound(delta: number) {
  if (!replay.value) return
  selectRound(Math.max(0, Math.min(replay.value.rounds.length - 1, roundIndex.value + delta)))
}

function renderPosition() {
  if (!scene || !replay.value) return
  const position = replay.value.build(
    roundIndex.value,
    node.value,
    viewerOriginal.value,
    showOtherHands.value,
  )
  decorateSnapshotRanks(position.snapshot)
  actionLabel.value = position.actionLabel
  const seatMap = replay.value.rounds[roundIndex.value].seats || [0, 1, 2, 3]
  currentScores.value = seatMap.map((seat) => position.snapshot.seats[seat]?.score ?? 0)
  scene.flushFromSnapshot(position.snapshot)
  refreshReplayHints(position.snapshot)
  if (roundResult.value) {
    const action = roundResult.value.action
    presentTerminalResult(action)
  } else if (resultPanelVisible.value || resultTimers.length) {
    resetTerminalPresentation()
  }
}

function meldKey(meld: MeldSnapshot): string {
  if (isChangchunRecord(detail.value) && meld.special_kind && meld.physical_tiles && meld.logical_tiles) {
    return `C${meld.special_kind}:${meld.physical_tiles.map(mmcrTileToSalasasa).join(',')}:${meld.logical_tiles.map(mmcrTileToSalasasa).join(',')}`
  }
  const tile = mmcrTileToSalasasa(meld.tile)
  if (meld.type === 'sequence') return `s${tile}`
  if (meld.type === 'triplet') return `k${tile}`
  return `${meld.concealed ? 'G' : 'g'}${tile}`
}

function waitDataForSnapshot(snapshot: ActiveSessionSnapshot) {
  const viewer = snapshot.seats.find((seat) => seat.seat_index === snapshot.viewer.seat_index)
  if (!viewer?.hand_tiles) return null
  const hand = viewer.hand_tiles.map(mmcrTileToSalasasa)
  if (viewer.drawn_tile != null) hand.push(mmcrTileToSalasasa(viewer.drawn_tile))
  if ((detail.value?.rule || detail.value?.record?.game_title?.rule) === 'changsha') {
    const known = [...hand, ...snapshot.seats.flatMap(seat => [
      ...seat.discard_pile.map(mmcrTileToSalasasa),
      ...(seat.public_kong_tiles || []).map(mmcrTileToSalasasa),
      ...seat.melds.filter(meld => !meld.concealed || seat.seat_index === viewer.seat_index || snapshot.reveal_all_hands)
        .flatMap(meld => changshaMeldTiles(meldKey(meld))),
    ])]
    return buildChangshaWaitData(hand, viewer.melds.map(meldKey), changshaInfo.value?.kamo, known, changshaInfo.value?.exchange, viewer.seat_index)
  }
  if (isChangchunRecord(detail.value)) return buildChangchunWaitData({
    hand, combinations: viewer.melds.map(meldKey), playerIndex: viewer.seat_index,
    seatDiscards: snapshot.seats.map(seat => seat.discard_pile.map(mmcrTileToSalasasa)),
    seatCombinations: snapshot.seats.map(seat => seat.melds.map(meldKey)),
    knownTiles: changchunInfo.value?.tile ? [changchunInfo.value.tile] : [],
  }, viewer.drawn_tile != null)
  if (detail.value?.rule === 'jiujiang') return jiujiangWaitDataAt(currentRound.value, node.value, viewer.seat_index, hand, viewer.melds.map(meldKey), jiujiangKnownTiles(snapshot, mmcrTileToSalasasa))
  if (detail.value?.rule === 'hongzhong') return hongzhongWaitDataAt(currentRound.value, node.value, viewer.seat_index, hand, viewer.melds.map(meldKey), hongzhongKnownTiles(snapshot, mmcrTileToSalasasa))
  if (detail.value?.rule === 'hangzhou') return hangzhouWaitDataAt(currentRound.value, node.value, viewer.seat_index, hand, viewer.melds.map(meldKey), hangzhouKnownTiles(snapshot, mmcrTileToSalasasa))
  if (detail.value?.rule === 'shanxi') {
    const publicTiles = snapshot.seats.flatMap(seat => [
      ...seat.discard_pile.map(mmcrTileToSalasasa),
      ...seat.melds.flatMap(meld => shanxiMeldTiles(meldKey(meld))),
    ])
    publicTiles.push(...shanxiKnownConcealedDiscards(currentRound.value, node.value, viewer.seat_index))
    return buildShanxiWaitData({ hand, combinations: viewer.melds.map(meldKey), publicTiles }, viewer.drawn_tile != null)
  }
  const title = detail.value?.record.game_title ?? {}
  if (['guangdong', 'wuhan', 'nanjing', 'singapore', 'shenyang', 'shangrao'].includes(detail.value?.rule ?? '')) {
    return regionalReplayWaitData(detail.value.rule, currentRound.value, node.value, viewer.seat_index,
      hand, viewer.melds.map(meldKey), [...hangzhouKnownTiles(snapshot, mmcrTileToSalasasa), ...shangraoVisibleIndicators(shangraoInfo.value)], title.detailed_config ?? {})
  }
  if (detail.value?.rule === 'malaysia') return null
  if (detail.value?.rule === 'fuzhou') return fuzhouWaitDataAt(currentRound.value,node.value,viewer.seat_index,hand,viewer.melds.map(meldKey),hangzhouKnownTiles(snapshot,mmcrTileToSalasasa))
  if (detail.value?.rule === 'wenzhou') return wenzhouWaitData(currentRound.value, node.value, viewer.seat_index, hand, snapshot, mmcrTileToSalasasa)
  if (detail.value?.rule === 'guizhou') return buildGuizhouWaitData({
    hand, combinations: viewer.melds.map(meldKey),
    ready: guizhouReadyAt(currentRound.value, node.value, viewer.seat_index),
    seatDiscards: snapshot.seats.map((seat) => seat.discard_pile.map(mmcrTileToSalasasa)),
    seatCombinations: snapshot.seats.map((seat) => seat.melds.map(meldKey)),
  }, { includeDiscards: viewer.drawn_tile != null })
  if (detail.value?.rule && detail.value.rule !== 'guobiao') return null
  return buildLocalWaitData({
    tips: true,
    hand,
    combinations: viewer.melds.map(meldKey),
    flowerCount: viewer.flower_tiles?.length ?? 0,
    playerIndex: viewer.seat_index,
    currentRound: Number(currentRound.value?.current_round ?? roundIndex.value + 1),
    hepaiLimit: Number(title.hepai_limit ?? 8),
    jokerMultiWait: !!title.joker_multi_wait,
    noJokerBonus: title.no_joker_bonus, blackFlowerProgressive: title.black_flower_progressive,
    subRule: String(detail.value?.sub_rule ?? 'guobiao'),
    seatDiscards: snapshot.seats.map((seat) => seat.discard_pile.map(mmcrTileToSalasasa)),
    seatCombinations: snapshot.seats.map((seat) => seat.melds.map(meldKey)),
  }, { includeDiscards: viewer.drawn_tile != null })
}

function waitingTilesForSnapshotSeat(seat: ActiveSessionSnapshot['seats'][number]): Set<number> {
  const hand = (seat.hand_tiles ?? []).map(mmcrTileToSalasasa)
  if (hand.length === 0) return new Set<number>()
  if ((detail.value?.rule || detail.value?.record?.game_title?.rule) === 'changsha') return new Set((changshaInfo.value?.exchange
    ? exchangeSixWaits(hand, seat.melds.map(meldKey), changshaInfo.value.exchange, seat.seat_index)
    : changshaWaits(hand, seat.melds.map(meldKey), changshaInfo.value?.kamo))
    .filter(wait => wait.canRon).map(wait => salasasaTileToMmcr(wait.tile)))
  if (detail.value?.rule === 'shanxi') return new Set(shanxiWaits(hand, seat.melds.map(meldKey)).map(salasasaTileToMmcr))
  if (isChangchunRecord(detail.value)) return new Set(changchunQualifiedWaits(hand, seat.melds.map(meldKey)).map(salasasaTileToMmcr))
  if (detail.value?.rule === 'jiujiang') {
    const hints = jiujiangHintsAt(currentRound.value,node.value,seat.seat_index,hand,seat.melds.map(meldKey))
    return new Set<number>((hints?.waiting_tiles || []).filter((tile: number)=>hints.waiting_details?.[tile]?.kind === 'dianhe').map(salasasaTileToMmcr))
  }
  if (detail.value?.rule === 'malaysia') return new Set<number>()
  if (['guangdong', 'wuhan', 'nanjing', 'singapore', 'shenyang', 'shangrao'].includes(detail.value?.rule ?? '')) {
    const hints = regionalReplayHints(detail.value.rule, currentRound.value, node.value, seat.seat_index,
      hand, seat.melds.map(meldKey), detail.value?.record.game_title?.detailed_config ?? {})
    return new Set<number>((hints?.waiting_tiles ?? []).filter((tile: number) =>
      ['ron', 'dianhe', 'win'].includes(hints.waiting_details?.[tile]?.status ?? hints.waiting_details?.[tile]?.kind)).map(salasasaTileToMmcr))
  }
  if (detail.value?.rule === 'hongzhong') return new Set<number>() // This rule never permits a discard win.
  if (detail.value?.rule === 'fuzhou') {
    const hints=fuzhouHintsAt(currentRound.value,node.value,seat.seat_index,hand,seat.melds.map(meldKey))
    return new Set<number>((hints?.waiting_tiles || []).filter((tile: number)=>hints.waiting_details?.[tile]?.kind === 'ron').map(salasasaTileToMmcr))
  }
  if (detail.value?.rule === 'wenzhou') return new Set<number>(wenzhouWaitsAt(currentRound.value, node.value, seat.seat_index, hand).map((wait: { tile: number }) => salasasaTileToMmcr(wait.tile)))
  if (detail.value?.rule === 'guizhou') return new Set(guizhouRonWaitingTiles(hand, seat.melds.map(meldKey),
    guizhouReadyAt(currentRound.value, node.value, seat.seat_index)).map(salasasaTileToMmcr))
  try {
    const waits = tingpaiCheck(hand, seat.melds.map(meldKey), false)
    return new Set(waits.map(salasasaTileToMmcr))
  } catch {
    return new Set<number>()
  }
}

function waitingForDrawAfterCut(snapshot: ActiveSessionSnapshot): boolean {
  const ticks = currentRound.value?.action_ticks ?? []
  for (let index = Math.min(node.value, ticks.length) - 1; index >= 0; index -= 1) {
    const action = String(ticks[index]?.[0] ?? '')
    if (!action || isRecordSilentTick(ticks[index])) continue
    return action === 'c'
  }
  return snapshot.state.last_event_kind === 'discard_tile'
}

function refreshReplayHints(snapshot: ActiveSessionSnapshot) {
  const waitData = waitDataForSnapshot(snapshot)
  if (waitData?.type === 'waits') {
    scene?.setReplayWaitTips({
      type: 'waits',
      details: waitData.details.map((item) => ({ ...item, tile: salasasaTileToMmcr(item.tile) })),
    })
  } else if (waitData?.type === 'waits_all') {
    scene?.setReplayWaitTips({
      type: 'waits_all',
      details: waitData.details.map((item) => ({
        discard_tile: salasasaTileToMmcr(item.discard_tile),
        adds: item.adds.map((add) => ({ ...add, tile: salasasaTileToMmcr(add.tile) })),
      })),
    })
  } else {
    scene?.setReplayWaitTips(null)
  }
  scene?.setReplayDangerTiles(replayDangerBySeat.value)
}

function decorateSnapshotRanks(snapshot: ActiveSessionSnapshot) {
  for (const seat of snapshot.seats) {
    seat.rank = seat.player_id == null ? '—' : currentRanks.value[String(seat.player_id)] || '—'
  }
}

function decorateEventRanks(update: Record<string, any> | null) {
  if (!update || !Array.isArray(update.seat_status)) return
  for (const seat of update.seat_status) {
    seat.rank = seat.user_id == null ? '—' : currentRanks.value[String(seat.user_id)] || '—'
  }
}

async function loadCurrentRanks(record: PublicGameRecord) {
  if (isExternalRecord(record.record.game_title)) {
    currentRanks.value = {}
    return
  }
  const entries = await Promise.all(record.players.map(async (player) => {
    try {
      const profile = await publicApiGet<{ rank?: { guobiao_rank?: string | null } }>(
        playerProfileUrl(player.user_id),
      )
      return [String(player.user_id), profile.rank?.guobiao_rank || '—'] as const
    } catch {
      return [String(player.user_id), '—'] as const
    }
  }))
  currentRanks.value = Object.fromEntries(entries)
}

function toggleOtherHands() {
  showOtherHands.value = !showOtherHands.value
  renderPosition()
}

function toggleChongHint() {
  chongHintEnabled.value = !chongHintEnabled.value
  if (resultPosition.value?.snapshot) {
    scene?.setReplayDangerTiles(replayDangerBySeat.value)
  }
}

function toggleMoqieHint() {
  showMoqieMode.value = !showMoqieMode.value
  scene?.setReplayMoqieHintEnabled(showMoqieMode.value)
  renderPosition()
}

function onGameInfoWheel(event: WheelEvent) {
  const element = gameInfoScrollElement.value
  if (!element) return
  const unit = event.deltaMode === WheelEvent.DOM_DELTA_LINE
    ? 16
    : event.deltaMode === WheelEvent.DOM_DELTA_PAGE
      ? element.clientHeight
      : 1
  element.scrollTop += event.deltaY * unit * 0.5
}

function mmcrTileAsset(tid: number) {
  const faceId = mmcrFaceId(tid)
  return tileFaceAssetUrl(faceId, {
    baseUrl: import.meta.env.BASE_URL,
    black: appearance.value.tileFaceTheme === 'black',
    guobiao: appearance.value.tileFaceTheme === 'guobiao',
    unityFlower: appearance.value.flowerFaceTheme === 'unity' && faceId >= 51 && faceId <= 58,
  })
}

function persistAppearance(next: typeof appearance.value) {
  appearance.value = normalizeSceneAppearanceSettings(next)
  saveStoredSceneAppearance(appearance.value)
  scene?.setAppearance(appearance.value)
}

function setAppearanceField(field: keyof typeof appearance.value, value: unknown) {
  persistAppearance({ ...appearance.value, [field]: value })
}

function setTileCoverColor(index: number, color: string) {
  const colors = [...appearance.value.tileCoverColors]
  colors[index] = color
  persistAppearance({ ...appearance.value, tileCoverColors: colors })
}

function addTileCoverColor() {
  const colors = appearance.value.tileCoverColors
  if (colors.length >= MAX_TILE_COVER_COLORS) return
  persistAppearance({
    ...appearance.value,
    tileCoverColors: [...colors, colors.at(-1) ?? '#f6bc1e'],
  })
}

function removeTileCoverColor(index: number) {
  if (appearance.value.tileCoverColors.length <= 1) return
  const currentIndex = appearance.value.lastTileCoverIndex
  const colors = appearance.value.tileCoverColors.filter((_, colorIndex) => colorIndex !== index)
  const nextIndex = index < currentIndex
    ? currentIndex - 1
    : index === currentIndex ? Math.min(index, colors.length - 1) : currentIndex
  persistAppearance({ ...appearance.value, tileCoverColors: colors, lastTileCoverIndex: nextIndex })
}

function reorderTileCoverColors(colors: string[], activeIndex: number) {
  persistAppearance({ ...appearance.value, tileCoverColors: colors, lastTileCoverIndex: activeIndex })
}

function selectTileCoverIndex(index: number) {
  persistAppearance({ ...appearance.value, lastTileCoverIndex: index })
  scene?.setActiveTileCoverIndex(index)
}

async function uploadBackgroundImage(file: File) {
  try {
    backgroundImage.value = await saveStoredSceneBackgroundImage(file)
    persistAppearance({ ...appearance.value, backgroundImageEnabled: true })
    scene?.setBackgroundImage(backgroundImage.value.dataUrl)
  } catch {
    ElMessage.error(tr('背景图片保存失败'))
  }
}

async function clearBackgroundImage() {
  try {
    await clearStoredSceneBackgroundImage()
    backgroundImage.value = null
    persistAppearance({ ...appearance.value, backgroundImageEnabled: false })
    scene?.setBackgroundImage(null)
  } catch {
    ElMessage.error(tr('背景图片移除失败'))
  }
}

async function resetAppearance() {
  resetStoredSceneAppearance()
  try { await clearStoredSceneBackgroundImage() } catch { /* IndexedDB 不可用时仍可重置 */ }
  appearance.value = {
    ...DEFAULT_SCENE_APPEARANCE,
    tileCoverColors: [...DEFAULT_SCENE_APPEARANCE.tileCoverColors],
  }
  backgroundImage.value = null
  scene?.setAppearance(appearance.value)
  scene?.setBackgroundImage(null)
}

function changeVolume(next: number) {
  volume.value = Number(next)
  saveStoredVolume(volume.value)
  scene?.setVolume(volume.value)
}

async function copyShareLink(kind: '2d' | '2d-node' | '3d' | '3d-node') {
  const gameId = String(detail.value?.game_id || route.params.gameId)
  const round = roundIndex.value + 1
  const url = new URL(replaySharePath(kind, gameId, round, node.value), window.location.origin).toString()
  try {
    await navigator.clipboard.writeText(url)
    copiedKind.value = kind
    if (kind === '2d-node' || kind === '3d-node') {
      ElMessage.success(tr('已复制{mode}当前位置（第{round}局 node {node}）', { mode: kind === '3d-node' ? '3D' : '2D', round, node: node.value }))
    }
    window.setTimeout(() => {
      if (copiedKind.value === kind) copiedKind.value = null
    }, 1800)
  } catch {
    ElMessage.error(tr('复制失败，请手动复制：{url}', { url }))
  }
}

function onKeydown(event: KeyboardEvent) {
  if (event.target instanceof HTMLInputElement) return
  if (event.key === 'ArrowLeft') {
    event.preventDefault()
    event.shiftKey ? changeRound(-1) : step(-1)
  } else if (event.key === 'ArrowRight') {
    event.preventDefault()
    event.shiftKey ? changeRound(1) : step(1)
  } else if (event.code === 'Space') {
    event.preventDefault()
    togglePlaying()
  }
}

async function mountScene() {
  if (!stageElement.value || scene) return
  const currentScene = new MahjongScene(() => {})
  currentScene.setPresentationMode('replay')
  currentScene.setRoundLabelFormatter(replayRoundLabeler.value)
  currentScene.setReplayRecordVersion(6)
  currentScene.setReplayMoqieHintEnabled(showMoqieMode.value)
  currentScene.setReplayWaitTips(null)
  currentScene.setVolume(volume.value)
  currentScene.setAppearance(appearance.value)
  for (const sound of GAME_SOUND_ASSETS) {
    const audio = new Audio(getPreloadedSoundUrl(sound.file))
    audio.preload = 'auto'
    audio.load()
    currentScene.loadSound(sound.alias, audio)
  }
  scene = currentScene
  const mounted = await currentScene.mount(stageElement.value)
  if (!mounted || scene !== currentScene) return
  try {
    backgroundImage.value = await loadStoredSceneBackgroundImage()
    currentScene.setBackgroundImage(backgroundImage.value?.dataUrl ?? null)
  } catch {
    backgroundImage.value = null
    currentScene.setBackgroundImage(null)
  } finally {
    backgroundImageLoading.value = false
  }
  sceneReady.value = true
  await nextTick()
  currentScene.forceResize()
  renderPosition()
}

async function loadRecord() {
  loading.value = true
  errorMessage.value = ''
  stopPlaying()
  resetTerminalPresentation()
  try {
    const gameId = String(route.params.gameId || '')
    let value: PublicGameRecord | null = null
    if (isLocalReplayRecord(gameId)) {
      value = loadLocalReplayRecord(gameId)
    } else {
      const cached = await getLocalGameRecord(gameId)
      value = cached?.record
        ? cached as PublicGameRecord
        : await publicApiGet<PublicGameRecord>(publicRecordUrl(gameId))
    }
    if (isLocalReplayRecord(gameId) || isLocalOnlyGameId(gameId)) currentRanks.value = {}
    else await loadCurrentRanks(value)
    if (isExternalRecord(value.record.game_title)) {
      value.players = value.players.map((player) => ({
        ...player,
        username: externalPlayerName(value.record.game_title, player.original_player_index, player.username),
      }))
    }
    // 公开牌谱路由不要求登录，直接打开/刷新时也需先恢复已有登录身份。
    if (!playerAuth.loaded) await playerAuth.fetchMe()
    replay.value = new RecordReplay(value)
    detail.value = value
    roundIndex.value = 0
    node.value = 0
    viewerOriginal.value = replay.value.defaultViewerOriginal(playerAuth.isLoggedIn ? playerAuth.userId : null)
    showOtherHands.value = true
    playWinAnimation.value = false
    chongHintEnabled.value = true
    showMoqieMode.value = true
    wallVisible.value = false
    scoreboardOpen.value = false
    settingsOpen.value = false
    applyDeepLinkPosition()
    await nextTick()
    await mountScene()
    renderPosition()
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : '牌谱读取失败'
  } finally {
    loading.value = false
  }
}

/**
 * 分享链接形如 /2d/record/{gameId}?round={第几局，从 1 开始}&node={节点下标，从 0 开始}。
 * 打开链接时直接跳转到对应局数与节点；参数缺失或越界时回退到开头。
 */
function applyDeepLinkPosition() {
  if (!replay.value) return
  const rawRound = Number(route.query.round)
  if (Number.isFinite(rawRound) && rawRound >= 1) {
    const targetRound = Math.min(replay.value.rounds.length - 1, Math.floor(rawRound) - 1)
    roundIndex.value = Math.max(0, targetRound)
  }
  const rawNode = Number(route.query.node)
  if (Number.isFinite(rawNode) && rawNode >= 0) {
    const ticks = replay.value.rounds[roundIndex.value]?.action_ticks ?? []
    node.value = Math.max(0, Math.min(ticks.length, Math.floor(rawNode)))
  }
}

watch([roundIndex, node, viewerOriginal], () => {
  if (node.value > maxNode.value) node.value = maxNode.value
  if (skipNextPositionRender) {
    skipNextPositionRender = false
    return
  }
  renderPosition()
})
watch(locale, () => {
  scene?.setAppearance(appearance.value)
})
watch(() => route.params.gameId, loadRecord)

onMounted(() => {
  window.addEventListener('four-jokers-waits-ready', refreshFourJokersHints)
  window.addEventListener('keydown', onKeydown)
  void loadRecord()
})

onBeforeUnmount(() => {
  window.removeEventListener('four-jokers-waits-ready', refreshFourJokersHints)
  window.removeEventListener('keydown', onKeydown)
  stopPlaying()
  clearResultTimers()
  scene?.destroy()
  scene = null
})

function refreshFourJokersHints() {
  if (resultPosition.value?.snapshot) refreshReplayHints(resultPosition.value.snapshot)
}
</script>

<style src="./Game.css"></style>
<style src="./Replay.css"></style>
