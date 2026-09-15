<template>
  <div v-loading="loading" class="emp">
    <header class="emp-head">
      <div class="emp-head-main">
        <h2 class="emp-name">{{ detail?.name || '加载中…' }}</h2>
        <div class="emp-meta" v-if="detail">
          <el-tag :type="eventStatusTagType(detail.status)" size="small" effect="dark">
            {{ eventStatusLabel(detail.status) }}
          </el-tag>
          <el-tag :type="venueKindTagType(detail.kind)" size="small" effect="plain">
            {{ venueKindLabel(detail.kind) }}
          </el-tag>
          <el-tag v-if="detail.reopen_requested" type="warning" size="small">待审核再开</el-tag>
          <el-tag type="info" size="small" effect="plain">{{ eventRoleLabel(detail.my_role, detail.kind) }}</el-tag>
          <span class="emp-id">ID {{ detail.event_id }}</span>
          <span class="emp-stat">牌谱 {{ detail.record_count ?? 0 }}</span>
        </div>
      </div>
      <div class="emp-head-actions">
        <router-link
          v-if="detail"
          class="emp-public-link"
          :to="`/events/${detail.event_id}`"
          target="_blank"
        >公开详情</router-link>
        <el-button text type="primary" @click="closeManagePanel">收起</el-button>
      </div>
    </header>

    <template v-if="detail">
      <section class="emp-lifecycle" :class="detail.status">
        <p class="emp-lifecycle-text">{{ lifecycleHint }}</p>
        <div v-if="isOwner" class="emp-lifecycle-actions">
          <el-button
            v-if="detail.status === 'registered'"
            type="primary"
            @click="openEvent"
          >{{ isBase ? '开启基地' : '开始赛事' }}</el-button>
          <el-button
            v-if="detail.status === 'active'"
            type="warning"
            @click="closeEvent"
          >{{ isBase ? '关闭基地' : '关闭赛事' }}</el-button>
          <el-button
            v-if="detail.status === 'closed' && !detail.reopen_requested"
            type="primary"
            plain
            @click="requestReopen"
          >申请重新开启</el-button>
          <el-tag v-if="detail.status === 'closed' && detail.reopen_requested" type="warning">
            已提交申请，等待平台管理员审核
          </el-tag>
        </div>
      </section>

      <section class="emp-desc">
        <h3 class="emp-sec-title">{{ isBase ? '基地介绍' : '赛事介绍' }}</h3>
        <p class="emp-desc-body">{{ detail.description?.trim() || '暂无介绍' }}</p>
        <dl class="emp-desc-meta">
          <div><dt>创建时间</dt><dd>{{ formatDate(detail.created_at) }}</dd></div>
          <div v-if="detail.closed_at"><dt>关闭时间</dt><dd>{{ formatDate(detail.closed_at) }}</dd></div>
        </dl>
      </section>

      <el-tabs v-model="activeTab" class="emp-tabs">
        <el-tab-pane v-if="isOwner" :label="isBase ? '基地资料' : '赛事资料'" name="profile">
          <el-alert
            :title="isBase ? '修改基地名或简介需提交平台管理员审核，通过后才会在公开页生效。' : '修改赛事名或赛事简介需提交平台管理员审核，通过后才会在公开页生效。'"
            type="info"
            :closable="false"
            show-icon
            class="emp-alert"
          />
          <el-alert
            v-if="pendingProfile"
            :title="`待审核：拟改为「${pendingProfile.proposed_name}」`"
            type="warning"
            :closable="false"
            show-icon
            class="emp-alert"
          />
          <el-form label-position="top" class="emp-profile-form" @submit.prevent="submitProfileChange">
            <el-form-item :label="isBase ? '基地名称' : '赛事名称'">
              <el-input v-model="profileForm.name" maxlength="128" show-word-limit />
            </el-form-item>
            <el-form-item :label="isBase ? '基地简介' : '赛事简介'">
              <el-input
                v-model="profileForm.description"
                type="textarea"
                :rows="5"
                maxlength="2000"
                show-word-limit
              />
            </el-form-item>
            <el-form-item label="修改说明">
              <el-input v-model="profileForm.reason" placeholder="可选，供审核参考" />
            </el-form-item>
            <el-form-item>
              <el-button
                type="primary"
                :loading="savingProfile"
                :disabled="!!pendingProfile"
                @click="submitProfileChange"
              >提交审核</el-button>
              <el-button
                v-if="pendingProfile"
                :loading="cancellingProfile"
                @click="cancelProfileChange"
              >撤销申请</el-button>
            </el-form-item>
          </el-form>
        </el-tab-pane>

        <el-tab-pane :label="isBase ? '基地公告' : '比赛公告'" name="announcements">
          <el-alert type="info" :closable="false" show-icon class="emp-alert emp-announce-tip">
            <template #title>{{ isBase ? '发布基地公告说明' : '发布比赛公告说明' }}</template>
            <p class="tip-lead">
              {{ isBase
                ? '可用于活动通知、规则变更、精彩片段、排名或违规公示。平台对此不做严格规定。'
                : '您在包括且不限于以下情形时都可以发布比赛公告，并且您应该在1、4条所属情形发生时发布比赛公告，本平台对此不做严格规定。' }}
            </p>
            <ol v-if="!isBase" class="tip-list">
              <li>赛事规则的最新变更或者活动通知</li>
              <li>传达赛事中出现的精彩片段</li>
              <li>发布比赛的中途对阵情况，帮助玩家更好的观看享受比赛</li>
              <li>公布比赛中途或最终的排名以及奖励获得者</li>
              <li>对比赛中的违规行为进行公布</li>
            </ol>
          </el-alert>
          <el-form label-position="top" class="emp-profile-form" @submit.prevent="publishAnnouncement">
            <el-form-item label="标题">
              <el-input v-model="announceForm.title" maxlength="200" show-word-limit />
            </el-form-item>
            <el-form-item label="内容">
              <el-input
                v-model="announceForm.body"
                type="textarea"
                :rows="5"
                maxlength="10000"
                show-word-limit
              />
            </el-form-item>
            <el-form-item>
              <el-button type="primary" :loading="publishingAnnounce" @click="publishAnnouncement">
                发布公告
              </el-button>
              <el-button text type="primary" :loading="loadingAnnouncements" @click="loadAnnouncements">
                刷新
              </el-button>
            </el-form-item>
          </el-form>
          <el-table
            :data="announcements"
            size="small"
            v-loading="loadingAnnouncements"
            empty-text="暂无公告"
          >
            <el-table-column prop="title" label="标题" min-width="140" />
            <el-table-column label="内容" min-width="200" show-overflow-tooltip>
              <template #default="{ row }">{{ row.body }}</template>
            </el-table-column>
            <el-table-column label="作者" width="110">
              <template #default="{ row }">{{ row.author_username || row.created_by }}</template>
            </el-table-column>
            <el-table-column label="时间" min-width="150">
              <template #default="{ row }">{{ formatDate(row.created_at) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="90">
              <template #default="{ row }">
                <el-button
                  v-if="canDeleteAnnouncement(row)"
                  link
                  type="danger"
                  @click="deleteAnnouncement(row)"
                >删除</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>

        <el-tab-pane v-if="isOwner" label="子管理员" name="admins">
          <div class="emp-tab-bar">
            <el-form inline class="emp-form" @submit.prevent="addAdmin">
              <el-form-item label="用户 ID">
                <el-input v-model="adminForm.user_id" clearable style="width: 140px" />
              </el-form-item>
              <el-form-item>
                <el-button type="primary" :loading="savingAdmin" @click="addAdmin">添加子管理员</el-button>
              </el-form-item>
            </el-form>
            <span class="emp-count">{{ adminList.length }} / 10</span>
          </div>
          <el-table :data="adminList" size="small" :empty-text="`暂无${venueNoun}子管理员`">
            <el-table-column prop="username" label="用户名" min-width="120" />
            <el-table-column prop="user_id" label="用户 ID" width="120" />
            <el-table-column label="添加时间" min-width="150">
              <template #default="{ row }">{{ formatDate(row.created_at) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="90">
              <template #default="{ row }">
                <el-button link type="danger" @click="removeAdmin(row)">移除</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>

        <el-tab-pane label="房间" name="rooms">
          <el-alert
            v-if="detail.status !== 'active'"
            :title="detail.status === 'registered' ? `${venueNoun}尚未开启，无法创建房间` : `${venueNoun}已关闭，无法创建房间`"
            type="info"
            :closable="false"
            show-icon
            class="emp-alert"
          />
          <div class="emp-tab-bar">
            <el-button
              type="primary"
              :disabled="detail.status !== 'active'"
              @click="openRoomDialog('create')"
            >创建房间</el-button>
            <el-button text type="primary" :loading="loadingRooms" @click="loadRooms">刷新</el-button>
          </div>
          <el-table :data="rooms" size="small" v-loading="loadingRooms" empty-text="暂无房间">
            <el-table-column prop="room_name" label="名称" min-width="100" />
            <el-table-column prop="room_id" label="房间 ID" min-width="100" />
            <el-table-column prop="room_rule" label="规则" width="90" />
            <el-table-column label="人数" width="80">
              <template #default="{ row }">
                {{ (row.player_list || []).length }} / {{ row.max_player || 4 }}
              </template>
            </el-table-column>
            <el-table-column label="对局中" width="80">
              <template #default="{ row }">
                <el-tag :type="row.is_game_running ? 'warning' : 'info'" size="small">
                  {{ row.is_game_running ? '是' : '否' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="90">
              <template #default="{ row }">
                <el-button
                  link
                  type="danger"
                  :disabled="!!row.is_game_running"
                  @click="deleteRoom(row)"
                >删除</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>

        <el-tab-pane :label="isBase ? '加入审核' : '报名审核'" name="registrations">
          <div class="emp-tab-bar">
            <el-radio-group v-model="registrationFilter" size="small" @change="loadRegistrations">
              <el-radio-button label="">全部</el-radio-button>
              <el-radio-button label="pending">待审</el-radio-button>
              <el-radio-button label="approved">已通过</el-radio-button>
              <el-radio-button label="rejected">已拒绝</el-radio-button>
            </el-radio-group>
            <el-form inline class="emp-form" @submit.prevent="addPlayerByUid">
              <el-form-item :label="isBase ? '直接加入 UID' : '直接参赛 UID'">
                <el-input v-model="addPlayerForm.user_id" clearable style="width: 180px" placeholder="玩家 UID" />
              </el-form-item>
              <el-form-item>
                <el-button type="primary" :loading="addingPlayer" @click="addPlayerByUid">添加</el-button>
              </el-form-item>
            </el-form>
            <el-button text type="primary" :loading="loadingRegistrations" @click="loadRegistrations">刷新</el-button>
          </div>
          <el-table
            :data="registrations"
            size="small"
            class="emp-reg-table"
            scrollbar-always-on
            v-loading="loadingRegistrations"
            :empty-text="isBase ? '暂无加入申请' : '暂无报名'"
          >
            <el-table-column label="用户名" min-width="180">
              <template #default="{ row }">{{ row.username || '—' }}</template>
            </el-table-column>
            <el-table-column label="UID" min-width="140">
              <template #default="{ row }">{{ row.user_id }}</template>
            </el-table-column>
            <el-table-column label="段位" min-width="110">
              <template #default="{ row }">{{ row.guobiao_rank || '—' }}</template>
            </el-table-column>
            <el-table-column label="状态" width="90">
              <template #default="{ row }">
                <el-tag :type="registrationStatusTagType(row.status)" size="small">
                  {{ registrationStatusLabel(row.status) }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="contact" label="联系方式" min-width="140" show-overflow-tooltip />
            <el-table-column prop="remark" label="备注" min-width="140" show-overflow-tooltip />
            <el-table-column label="提交时间" min-width="150">
              <template #default="{ row }">{{ formatDate(row.created_at) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="140">
              <template #default="{ row }">
                <template v-if="row.status === 'pending'">
                  <el-button link type="success" @click="reviewRegistration(row, 'approved')">通过</el-button>
                  <el-button link type="danger" @click="reviewRegistration(row, 'rejected')">拒绝</el-button>
                </template>
                <span v-else class="muted">{{ row.review_note || '—' }}</span>
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>

        <el-tab-pane label="准备组桌" name="ready">
          <section class="emp-default-settings" data-testid="default-settings">
            <div class="emp-ready-section-head">
              <div class="emp-ready-summary"><h4>默认配置</h4><span>{{ roomSettingsSummary }}</span></div>
              <el-button link type="primary" :disabled="!roomSettingsLoaded || savingRoomSettings" @click="togglePresetEditor">{{ settingsEditorExpanded ? '收起对局设置' : '修改对局设置' }}</el-button>
            </div>
            <EventRoomPresetEditor
              v-show="settingsEditorExpanded"
              ref="presetEditorRef"
              :key="eventId"
              :settings-doc="roomSettingsDoc"
              :loaded="roomSettingsLoaded"
              :busy="savingRoomSettings || savingMatchmaking"
              :rule-options="roomRuleOptions"
              :save-change="savePresetEditorChange"
              :remove-preset="removeRoomPreset"
            />
          </section>
          <el-alert v-if="!roomSettingsLoaded && !loadingRoomSettings" title="对局设置未加载，请点击刷新后再操作。" type="warning" :closable="false" show-icon class="emp-alert" />

          <section class="emp-manual-seating" data-testid="manual-seating">
            <div class="emp-ready-section-head">
              <div><h4>手动组桌</h4><p class="emp-settings-description">勾选 4 名玩家后，可在确认组桌时选择默认配置或预设。组桌后进入房间，不自动开局。</p></div>
              <div class="emp-ready-actions">
                <el-button type="primary" :disabled="detail.status !== 'active' || selectedReadyIds.length !== 4 || autoMatching.enabled || !roomSettingsLoaded" @click="openSeatDialog">组桌（已选 {{ selectedReadyIds.length }}/4）</el-button>
                <el-button text type="primary" :loading="loadingReady" @click="refreshReadyTab">刷新</el-button>
              </div>
            </div>
            <el-table ref="readyTableRef" :data="readyPlayers" row-key="user_id" size="small" v-loading="loadingReady" empty-text="暂无准备中的玩家" @selection-change="onReadySelectionChange">
              <el-table-column type="selection" width="42" reserve-selection :selectable="() => !autoMatching.enabled" />
              <el-table-column prop="username" label="用户名" min-width="120" />
              <el-table-column prop="user_id" label="用户 ID" width="120" />
              <el-table-column label="准备时间" min-width="160"><template #default="{ row }">{{ formatDate(row.ready_at) }}</template></el-table-column>
            </el-table>
          </section>

          <section class="emp-auto-match" data-testid="auto-matching" v-loading="loadingRoomSettings">
            <div class="emp-auto-match-head">
              <h4>自动匹配</h4>
              <el-switch :model-value="autoMatching.enabled" :loading="savingMatchmaking" :disabled="!roomSettingsLoaded || (detail.status !== 'active' && !autoMatching.enabled) || savingRoomSettings || seatingTable || !matchingChoiceExists" active-text="已开启" inactive-text="未开启" aria-label="自动匹配" @change="toggleAutoMatching" />
            </div>
            <p class="emp-settings-description">在线玩家按准备时间依次匹配，凑够 4 人自动组桌并开局。使用所选配置的最新设置，正在进行的对局不受修改影响。</p>
            <div class="emp-room-presets emp-match-settings">
              <span class="emp-control-label">对局配置</span>
              <el-select v-model="matchingPresetChoice" class="emp-preset-select" aria-label="自动匹配对局配置" :disabled="!roomSettingsLoaded || savingMatchmaking || savingRoomSettings" @change="onMatchingChoiceChange">
                <el-option value="default" label="默认配置" />
                <el-option v-for="preset in roomPresets" :key="preset.preset_id" :value="preset.preset_id" :label="preset.name" />
              </el-select>
              <span class="emp-ready-summary">{{ matchingSettingsSummary }}</span>
            </div>
            <p class="emp-settings-hint emp-match-status">当前等待 {{ readyPlayers.length }} 人<span v-if="autoMatching.enabled && matchingRuntime?.eligible_count != null">（在线可匹配 {{ matchingRuntime.eligible_count }} 人）</span> · {{ matchingStatusLabel }}</p>
            <el-alert v-if="autoMatching.enabled && matchmakingError" :title="matchmakingError" type="warning" :closable="false" show-icon />
          </section>
        </el-tab-pane>

        <el-tab-pane label="准入配置" name="entry">
          <el-alert
            title="可配置自动通过、建房权限、等待队列与加入口令。"
            description="创建房间权限为「所有」时，未报名玩家可以创建并加入该场馆房间。「进入玩家队列」只控制详情页的「加入等待」，不会自动允许加入房间。"
            type="info"
            :closable="false"
            show-icon
            class="emp-alert"
          />
          <el-form label-position="top" class="emp-profile-form" @submit.prevent="saveEntryConfig">
            <el-form-item label="禁止游客报名">
              <el-switch v-model="entryForm.forbid_tourist" />
            </el-form-item>
            <el-form-item label="自动通过报名">
              <el-switch v-model="entryForm.auto_approve" />
            </el-form-item>
            <el-form-item label="创建房间权限">
              <el-radio-group v-model="entryForm.create_room_permission">
                <el-radio-button label="all">所有</el-radio-button>
                <el-radio-button label="registered">已报名</el-radio-button>
                <el-radio-button label="admin">管理员</el-radio-button>
              </el-radio-group>
            </el-form-item>
            <el-form-item label="允许未报名玩家进入玩家队列">
              <el-switch v-model="entryForm.unregistered_can_ready" />
            </el-form-item>
            <el-form-item label="加入口令（可选）">
              <el-input
                v-model="entryForm.join_code"
                maxlength="32"
                show-word-limit
                placeholder="留空则不校验口令"
                show-password
              />
            </el-form-item>
            <el-form-item>
              <el-button type="primary" :loading="savingEntry" @click="saveEntryConfig">保存准入配置</el-button>
            </el-form-item>
          </el-form>
        </el-tab-pane>

        <el-tab-pane label="进行中对局" name="games">
          <div class="emp-tab-bar emp-tab-bar--end">
            <el-button text type="primary" :loading="loadingGames" @click="loadGames">刷新</el-button>
          </div>
          <el-table :data="games" size="small" scrollbar-always-on v-loading="loadingGames" :empty-text="`当前没有本${venueNoun}进行中的对局`">
            <el-table-column prop="gamestate_id" label="对局 ID" min-width="150" />
            <el-table-column prop="room_rule" label="规则" width="90" />
            <el-table-column prop="game_status" label="状态机" width="110" />
            <el-table-column label="投票/暂停" width="110">
              <template #default="{ row }">
                <el-tag size="small">{{ phaseLabel(row.vote_phase) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="玩家" min-width="160">
              <template #default="{ row }">
                <span v-for="p in row.players" :key="p.user_id" class="player-chip">
                  {{ p.username }}
                </span>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="220">
              <template #default="{ row }">
                <el-button size="small" :disabled="isPauseDisabled(row)" @click="onPause(row)">暂停</el-button>
                <el-button
                  size="small"
                  type="success"
                  :disabled="isResumeDisabled(row)"
                  @click="onResume(row)"
                >解除</el-button>
                <el-button size="small" type="danger" @click="onEnd(row)">结束对局</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>

        <el-tab-pane label="统计" name="stats">
          <div class="emp-stats-totals">
            <div class="emp-stats-totals-head">
              <h4 class="emp-stats-heading">
                本{{ venueNoun }}总计
                <span v-if="statsDateRangeLabel" class="emp-stats-range-hint">{{ statsDateRangeLabel }}</span>
              </h4>
              <el-button text type="primary" size="small" :loading="loadingStats" @click="refreshStatsTab">
                刷新
              </el-button>
            </div>
            <div class="emp-stats-grid" v-loading="loadingStats">
              <div v-for="item in totalsStatsDisplay" :key="item.label" class="emp-stats-cell">
                <span class="emp-stats-label">{{ item.label }}</span>
                <span class="emp-stats-value">{{ item.value }}</span>
              </div>
              <div class="emp-stats-cell">
                <span class="emp-stats-label">参赛人数</span>
                <span class="emp-stats-value">{{ statsTotals.player_count ?? 0 }}</span>
              </div>
            </div>
          </div>

          <div class="emp-stats-search">
            <el-date-picker
              popper-class="compact-date-range-popper"
              :popper-options="{ modifiers: [{ name: 'preventOverflow', options: { altAxis: true, padding: 12 } }] }"
              v-model="statsDateRange"
              type="daterange"
              size="small"
              unlink-panels
              clearable
              range-separator="—"
              start-placeholder="开始日期"
              end-placeholder="结束日期"
              value-format="YYYY-MM-DD"
              :shortcuts="STATS_DATE_SHORTCUTS"
              class="emp-stats-daterange"
              @change="onStatsScopeChange"
            />
            <div class="emp-stats-scope">
              <el-select
                v-model="statsFilter.rule"
                clearable
                placeholder="全部规则"
                size="small"
                @change="onStatsScopeChange"
              >
                <el-option
                  v-for="r in statsRuleOptions"
                  :key="r"
                  :label="ruleLabel(r)"
                  :value="r"
                />
              </el-select>
              <el-select
                v-model="statsFilter.game_type"
                clearable
                placeholder="全部局制"
                size="small"
                @change="onStatsScopeChange"
              >
                <el-option
                  v-for="opt in GAME_TYPE_OPTIONS"
                  :key="opt.value"
                  :label="opt.label"
                  :value="opt.value"
                />
              </el-select>
            </div>
            <div class="emp-stats-player-search">
              <el-input
                v-model="statsFilter.q"
                clearable
                size="small"
                placeholder="搜索玩家 ID / 用户名"
                @keyup.enter="searchPlayer"
                @clear="clearPlayerSearch"
              />
              <div class="emp-stats-search-actions">
                <el-button type="primary" size="small" :loading="searchingPlayer" @click="searchPlayer">
                  查询玩家
                </el-button>
                <el-button size="small" @click="resetStatsFilter">重置</el-button>
              </div>
            </div>
          </div>

          <div v-if="focusPlayer" class="emp-stats-player-detail">
            <div class="emp-stats-player-head">
              <h4 class="emp-stats-heading">
                {{ focusPlayer.username }}
                <span class="emp-stats-uid">ID {{ focusPlayer.user_id }}</span>
              </h4>
              <el-button text type="primary" size="small" @click="clearPlayerSearch">清除</el-button>
            </div>
            <div class="emp-stats-grid">
              <div v-for="item in focusPlayerStatsDisplay" :key="item.label" class="emp-stats-cell">
                <span class="emp-stats-label">{{ item.label }}</span>
                <span class="emp-stats-value">{{ item.value }}</span>
              </div>
            </div>
          </div>

          <div class="emp-records-section">
            <div class="emp-records-head">
              <span class="emp-records-title">对局记录</span>
              <span class="emp-records-total">共 {{ recordsTotal }} 局</span>
            </div>
            <el-table
              :data="records"
              size="small"
              class="emp-records-table"
              scrollbar-always-on
              v-loading="loadingRecords"
              empty-text="暂无对局记录"
              @selection-change="onRecordsSelectionChange"
            >
              <el-table-column type="selection" width="36" />
              <el-table-column label="牌谱 ID" min-width="140">
                <template #default="{ row }">
                  <span class="cell-game-id" :title="row.game_id">{{ row.game_id }}</span>
                </template>
              </el-table-column>
              <el-table-column label="时间" width="150">
                <template #default="{ row }">
                  <span class="cell-time">{{ formatRecordDate(row.created_at) }}</span>
                </template>
              </el-table-column>
              <el-table-column label="场次" min-width="120">
                <template #default="{ row }">
                  <span class="cell-scene">{{ sceneLabel(row) }}</span>
                </template>
              </el-table-column>
              <el-table-column label="局制" width="80">
                <template #default="{ row }">
                  <span class="cell-mode">{{ gameTypeLabel(row.match_type) }}</span>
                </template>
              </el-table-column>
              <el-table-column label="顺位" width="64">
                <template #default="{ row }">
                  <span
                    v-if="focusRank(row)"
                    class="rank-badge"
                    :class="`rank-${focusRank(row)}`"
                  >{{ focusRank(row) }}</span>
                  <span v-else class="cell-dash">-</span>
                </template>
              </el-table-column>
              <el-table-column label="得分" width="80">
                <template #default="{ row }">
                  <span class="cell-score" :class="scoreClass(focusScore(row))">
                    {{ formatScore(focusScore(row)) }}
                  </span>
                </template>
              </el-table-column>
              <el-table-column label="同桌" min-width="180">
                <template #default="{ row }">
                  <el-tooltip effect="dark" placement="top">
                    <template #content>
                      <div v-for="p in row.players" :key="p.user_id" class="tip-player">
                        <span class="rank-badge" :class="`rank-${p.rank}`">{{ p.rank }}</span>
                        {{ p.username }}
                        <span :class="scoreClass(p.score)">{{ formatScore(p.score) }}</span>
                      </div>
                    </template>
                    <span class="cell-players">{{ playersSummary(row) }}</span>
                  </el-tooltip>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="168">
                <template #default="{ row }">
                  <el-button
                    v-if="row.rule === 'guobiao'"
                    link
                    type="warning"
                    size="small"
                    tag="a"
                    :href="`/2d/record/${encodeURIComponent(row.game_id)}`"
                    target="_blank"
                    rel="noopener noreferrer"
                  >2D</el-button>
                  <el-button
                    link
                    type="success"
                    size="small"
                    tag="a"
                    :href="`/game-unity?recordId=${encodeURIComponent(row.game_id)}`"
                    target="_blank"
                    rel="noopener noreferrer"
                  >3D</el-button>
                  <el-button link type="primary" size="small" @click="downloadOne(row.game_id)">
                    JSON
                  </el-button>
                </template>
              </el-table-column>
            </el-table>
            <div class="emp-records-foot">
              <el-pagination
                v-model:current-page="recordsPage.current"
                v-model:page-size="recordsPage.size"
                :total="recordsTotal"
                :page-sizes="[20, 50]"
                :pager-count="5"
                layout="prev, pager, next, sizes, total"
                small
                background
                @current-change="loadRecords"
                @size-change="onRecordsSizeChange"
              />
              <div class="emp-records-actions">
                <el-button
                  size="small"
                  :disabled="selectedRecordIds.length === 0"
                  :loading="downloadingRecords"
                  @click="downloadSelected"
                >{{ tr('下载选中({count})', { count: selectedRecordIds.length }) }}</el-button>
                <el-button
                  size="small"
                  type="primary"
                  :disabled="recordsTotal === 0"
                  :loading="downloadingRecords"
                  @click="downloadFiltered"
                >{{ tr('下载筛选结果(ZIP)') }}</el-button>
              </div>
            </div>
          </div>
        </el-tab-pane>
      </el-tabs>
    </template>

    <el-dialog v-model="seatDialogVisible" title="确认组桌" width="min(680px, calc(100vw - 24px))" class="emp-seat-dialog" :close-on-click-modal="!seatingTable" :close-on-press-escape="!seatingTable" :show-close="!seatingTable">
      <p class="emp-settings-description">以下 4 名玩家将进入同一房间。本次配置只用于这一桌，不会修改默认配置。</p>
      <ul class="emp-seat-players"><li v-for="player in seatingPlayers" :key="player.user_id"><strong>{{ player.username || player.user_id }}</strong><span>UID {{ player.user_id }}</span></li></ul>
      <el-form label-position="top">
        <el-form-item label="本桌对局配置"><el-select v-model="seatPresetChoice" aria-label="本桌对局配置" :disabled="seatingTable" class="emp-seat-preset-select"><el-option value="default" label="默认配置" /><el-option v-for="preset in roomPresets" :key="preset.preset_id" :value="preset.preset_id" :label="preset.name" /></el-select></el-form-item>
      </el-form>
      <h4 class="emp-seat-settings-title">{{ seatSettingsName }}</h4>
      <dl class="emp-seat-settings"><div v-for="item in seatSettingsRows" :key="item.label"><dt>{{ item.label }}</dt><dd>{{ item.value }}</dd></div></dl>
      <el-alert v-if="seatDialogRevision !== roomSettingsDoc.revision" title="配置已更新，以下展示最新设置。请核对后再次确认组桌。" type="warning" :closable="false" show-icon class="emp-alert" />
      <el-alert v-if="!seatingPlayersReady" title="有玩家已离开准备池，请取消后重新选择 4 人。" type="warning" :closable="false" show-icon class="emp-alert" />
      <el-alert v-if="autoMatching.enabled" title="自动匹配正在使用准备池，手动组桌已暂停。" type="info" :closable="false" class="emp-alert" />
      <template #footer><el-button :disabled="seatingTable" @click="seatDialogVisible = false">取消</el-button><el-button type="primary" :loading="seatingTable" :disabled="!seatingPlayersReady || autoMatching.enabled || !seatSettingsSource || detail?.status !== 'active'" @click="seatTable">确认组桌</el-button></template>
    </el-dialog>

    <VenueRoomDialog
      v-model="roomDialogVisible"
      :form="roomForm"
      :title="roomDialogTitle"
      confirm-text="创建房间"
      :loading="creatingRoom"
      :room-rule-options="roomRuleOptions"
      @confirm="createRoom"
    />
  </div>
</template>

<script setup>
import { computed, onUnmounted, reactive, ref, watch } from 'vue'
import { tr } from '@/i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import eventAdminApi, { getEventAdminToken } from '@/api/eventAdminClient'
import { useEventAdminAuthStore } from '@/stores/eventAdminAuth'
import VenueRoomDialog from '@/components/VenueRoomDialog.vue'
import EventRoomPresetEditor from '@/components/EventRoomPresetEditor.vue'
import { buildEventRoomSettings, createEventRoomForm, eventRoomSettingsSummary, eventRoomSettingsRows } from '@/utils/eventRoomSettings'
import {
  eventRoleLabel,
  eventStatusLabel,
  eventStatusTagType,
  venueKindLabel,
  venueKindTagType,
  registrationStatusLabel,
  registrationStatusTagType,
} from '@/utils/eventMeta'
import { buildPlayerStatsRows, dateRangeToQueryParams, STATS_DATE_SHORTCUTS } from '@/utils/statsDisplay'

const RULE_LABELS = {
  guobiao: '国标',
  riichi: '立直',
  qingque: '青雀',
  classical: '古典',
  sichuan: '四川',
  changsha: '长沙',
  taiwan: '台湾',
  jiandan: '南雀',
}

const GAME_TYPE_OPTIONS = [
  { value: 'quanzhuang', label: '全庄战' },
  { value: 'xifeng', label: '东西战' },
  { value: 'banzhuang', label: '半庄战' },
  { value: 'dongfeng', label: '东风战' },
]

const MODE_LABELS = { '4/4': '全庄战', '3/4': '东西战', '2/4': '半庄战', '1/4': '东风战' }

const RANK_STAT_LABELS = new Set([
  '总对局',
  '平均顺位',
  '一位率',
  '二位率',
  '三位率',
  '四位率',
])

function ruleLabel(rule) {
  return RULE_LABELS[rule] || rule
}

function rankOnlyStatsRows(stats) {
  return buildPlayerStatsRows(stats || {}).filter((row) => RANK_STAT_LABELS.has(row.label))
}

function gameTypeLabel(matchType) {
  if (!matchType) return '-'
  const base = String(matchType).replace(/_rank$/, '')
  return MODE_LABELS[base] || matchType
}

const props = defineProps({
  eventId: { type: String, required: true },
})
const emit = defineEmits(['close', 'updated'])
const eventAuth = useEventAdminAuthStore()

const loading = ref(false)
const detail = ref(null)
const activeTab = ref('rooms')
const savingAdmin = ref(false)
const adminForm = reactive({ user_id: '' })

const rooms = ref([])
const loadingRooms = ref(false)
const creatingRoom = ref(false)
const roomDialogVisible = ref(false)
const presetEditorRef = ref(null)
const settingsEditorExpanded = ref(true)
const loadingRoomSettings = ref(false)
const roomSettingsLoaded = ref(false)
const savingRoomSettings = ref(false)
const savingMatchmaking = ref(false)
const roomSettingsDoc = ref({ revision: 0, manual: {}, auto_match: { enabled: false }, presets: [] })
const roomPresets = computed(() => roomSettingsDoc.value.presets || [])
const manualSettings = computed(() => roomSettingsDoc.value.manual || {})
const autoMatching = computed(() => roomSettingsDoc.value.auto_match || { enabled: false })
const matchingPresetChoice = ref('default')
const matchingChoiceExists = computed(() => Boolean(settingsForChoice(matchingPresetChoice.value)))
const matchingRuntime = ref(null)
const matchingServiceError = ref('')
const matchmakingError = computed(() => matchingServiceError.value || matchingRuntime.value?.last_error || '')
const matchingStatusLabel = computed(() => {
  if (!autoMatching.value.enabled) return '自动匹配未开启'
  if (detail.value?.status !== 'active') return `${venueNoun.value}未开启，自动匹配已暂停`
  if (matchingServiceError.value || !matchingRuntime.value?.worker_running || !matchingRuntime.value?.enabled) return '已开启，等待匹配服务响应；手动组桌已暂停'
  return '自动匹配运行中，手动组桌已暂停'
})
const roomDialogTitle = computed(() => isBase.value ? '创建基地房间' : '创建赛事房间')
const roomSettingsSummary = computed(() => eventRoomSettingsSummary(manualSettings.value, RULE_LABELS))
const matchingSettingsSummary = computed(() => eventRoomSettingsSummary(settingsForChoice(matchingPresetChoice.value) || {}, RULE_LABELS))
const roomRuleOptions = [
  { value: 'guobiao', label: '国标' },
  { value: 'riichi', label: '立直' },
  { value: 'qingque', label: '青雀' },
  { value: 'classical', label: '古典' },
  { value: 'sichuan', label: '四川' },
  { value: 'changsha', label: '长沙' },
  { value: 'taiwan', label: '台湾' },
]
const roomForm = reactive(createEventRoomForm())

const games = ref([])
const loadingGames = ref(false)
const records = ref([])
const loadingRecords = ref(false)
const recordsTotal = ref(0)
const recordsPage = reactive({ current: 1, size: 20 })
const selectedRecordIds = ref([])
const downloadingRecords = ref(false)

const loadingStats = ref(false)
const searchingPlayer = ref(false)
const statsTotals = ref({
  total_games: 0,
  first_place_count: 0,
  second_place_count: 0,
  third_place_count: 0,
  fourth_place_count: 0,
  player_count: 0,
})
const statsRuleOptions = ref([])
const statsFilter = reactive({
  rule: '',
  game_type: '',
  q: '',
})
const statsDateRange = ref(null)
const focusPlayer = ref(null)

const profileForm = reactive({ name: '', description: '', reason: '' })
const pendingProfile = ref(null)
const savingProfile = ref(false)
const cancellingProfile = ref(false)

const announcements = ref([])
const loadingAnnouncements = ref(false)
const publishingAnnounce = ref(false)
const announceForm = reactive({ title: '', body: '' })

const registrations = ref([])
const loadingRegistrations = ref(false)
const registrationFilter = ref('pending')
const addingPlayer = ref(false)
const addPlayerForm = reactive({ user_id: '' })
const readyPlayers = ref([])
const readyTableRef = ref(null)
const loadingReady = ref(false)
const selectedReadyIds = ref([])
const seatingTable = ref(false)
const seatDialogVisible = ref(false)
const seatingPlayers = ref([])
const seatPresetChoice = ref('default')
const seatDialogRevision = ref(0)
const seatSettingsSource = computed(() => settingsForChoice(seatPresetChoice.value))
const seatSettingsName = computed(() => seatPresetChoice.value === 'default' ? '默认配置' : seatSettingsSource.value?.name || '预设已删除')
const seatSettingsRows = computed(() => seatSettingsSource.value ? eventRoomSettingsRows(seatSettingsSource.value, RULE_LABELS) : [])
const seatingPlayersReady = computed(() => seatingPlayers.value.length === 4 && seatingPlayers.value.every(player => readyPlayers.value.some(ready => Number(ready.user_id) === Number(player.user_id))))
const savingEntry = ref(false)
const entryForm = reactive({
  forbid_tourist: false,
  auto_approve: false,
  create_room_permission: 'admin',
  unregistered_can_ready: false,
  join_code: '',
})

const isOwner = computed(() => detail.value?.my_role === 'owner')
const isBase = computed(() => detail.value?.kind === 'base')
const venueNoun = computed(() => (isBase.value ? '基地' : '赛事'))
const adminList = computed(() => (detail.value?.admins || []).filter((a) => a.role === 'admin'))

const totalsStatsDisplay = computed(() => rankOnlyStatsRows(statsTotals.value))
const focusPlayerStatsDisplay = computed(() =>
  focusPlayer.value ? rankOnlyStatsRows(focusPlayer.value) : []
)
const statsDateRangeLabel = computed(() => {
  const r = statsDateRange.value
  if (!r || r.length < 2 || !r[0] || !r[1]) return ''
  return `${r[0]} — ${r[1]}`
})

function canDeleteAnnouncement(row) {
  if (isOwner.value) return true
  return Number(row.created_by) === Number(eventAuth.userId)
}

const lifecycleHint = computed(() => {
  const s = detail.value?.status
  const noun = venueNoun.value
  if (s === 'registered') {
    return `${noun}注册成功，开启后可创建房间、审核报名并组桌。`
  }
  if (s === 'active') {
    return `${noun}已开启。全程结束后可关闭；关闭后仍可查看数据。`
  }
  if (s === 'closed') {
    return detail.value?.reopen_requested
      ? `${noun}已关闭，重新开启申请审核中。`
      : `${noun}已关闭，无法创建房间。如需再次开启，请提交申请由平台管理员审核。`
  }
  return ''
})

const PHASE_LABELS = {
  idle: '无',
  voting: '投票中',
  pause_pending: '待暂停',
  paused: '已暂停',
  resume_voting: '解除投票',
  resume_countdown: '解除倒计时',
  end_countdown: '结束倒计时',
}

function formatDate(v) {
  return v ? new Date(v).toLocaleString('zh-CN') : '—'
}
function formatRecordDate(dateString) {
  if (!dateString) return ''
  const date = new Date(dateString)
  return date.toLocaleString('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}
function sceneLabel(row) {
  const name = row.event_name || detail.value?.name
  if (name) return `比赛场 · ${name}`
  if (row.event_id) return `比赛场 · ${row.event_id}`
  return '比赛场'
}
function focusSeat(rec) {
  const uid = focusPlayer.value?.user_id
  if (uid == null) return null
  return (rec.players || []).find((p) => Number(p.user_id) === Number(uid)) || null
}
function focusRank(rec) {
  return focusSeat(rec)?.rank
}
function focusScore(rec) {
  return focusSeat(rec)?.score
}
function scoreClass(s) {
  if (s === undefined || s === null) return ''
  return s > 0 ? 'pos' : s < 0 ? 'neg' : ''
}
function formatScore(s) {
  if (s === undefined || s === null) return '-'
  return (s > 0 ? '+' : '') + s
}
function playersSummary(rec) {
  return (rec.players || []).map((p) => p.username || '?').join(' / ')
}
function phaseLabel(p) {
  return PHASE_LABELS[p] || p || '无'
}
function isPauseDisabled(row) {
  return row.vote_phase === 'paused' || row.vote_phase === 'pause_pending'
}
function isResumeDisabled(row) {
  return row.vote_phase !== 'paused' && row.vote_phase !== 'pause_pending'
}

async function loadRooms() {
  loadingRooms.value = true
  try {
    const res = await eventAdminApi.get(`/events/${props.eventId}/rooms`)
    rooms.value = res.data.data?.items || []
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载房间失败')
    rooms.value = []
  } finally {
    loadingRooms.value = false
  }
}

async function loadGames() {
  loadingGames.value = true
  try {
    const res = await eventAdminApi.get(`/events/${props.eventId}/games`)
    games.value = res.data.data?.items || []
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载对局失败')
    games.value = []
  } finally {
    loadingGames.value = false
  }
}

function assignStatsScopeParams(target) {
  if (statsFilter.rule) target.rule = statsFilter.rule
  if (statsFilter.game_type) target.game_type = statsFilter.game_type
  Object.assign(target, dateRangeToQueryParams(statsDateRange.value))
  return target
}

function buildRecordsParams() {
  const params = assignStatsScopeParams({
    page: recordsPage.current,
    limit: recordsPage.size,
  })
  if (focusPlayer.value?.user_id) params.user_id = focusPlayer.value.user_id
  return params
}

async function loadRecords() {
  loadingRecords.value = true
  selectedRecordIds.value = []
  try {
    const res = await eventAdminApi.get(`/events/${props.eventId}/records`, {
      params: buildRecordsParams(),
    })
    const data = res.data.data || {}
    records.value = data.items || []
    recordsTotal.value = data.total || 0
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载对局记录失败')
    records.value = []
    recordsTotal.value = 0
  } finally {
    loadingRecords.value = false
  }
}

function onRecordsSizeChange() {
  recordsPage.current = 1
  loadRecords()
}

function onRecordsSelectionChange(rows) {
  selectedRecordIds.value = rows.map((r) => r.game_id)
}

function triggerBlob(blob, filename) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

async function handleDownloadResponse(resp) {
  if (resp.status === 400) {
    try {
      const j = await resp.json()
      ElMessage.error(j.message || '下载失败')
    } catch (_) {
      ElMessage.error('下载失败')
    }
    return false
  }
  if (resp.status === 404) {
    ElMessage.warning('没有匹配的牌谱')
    return false
  }
  if (resp.status === 401) {
    ElMessage.error('登录已失效，请重新登录')
    return false
  }
  if (!resp.ok) {
    ElMessage.error('下载失败')
    return false
  }
  const blob = await resp.blob()
  const cd = resp.headers.get('content-disposition') || ''
  const m = /filename="?([^";]+)"?/.exec(cd)
  triggerBlob(blob, m ? m[1] : 'records.zip')
  return true
}

function downloadOne(gameId) {
  const token = getEventAdminToken()
  downloadingRecords.value = true
  fetch(`/api/event-admin/events/${props.eventId}/record/${encodeURIComponent(gameId)}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  })
    .then(async (resp) => {
      if (!resp.ok) {
        ElMessage.error('下载失败')
        return
      }
      const blob = await resp.blob()
      triggerBlob(blob, `${gameId}.json`)
    })
    .catch(() => ElMessage.error('下载失败'))
    .finally(() => {
      downloadingRecords.value = false
    })
}

async function downloadSelected() {
  if (selectedRecordIds.value.length === 0) return
  downloadingRecords.value = true
  try {
    const token = getEventAdminToken()
    const resp = await fetch(`/api/event-admin/events/${props.eventId}/records/download`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify({ game_ids: selectedRecordIds.value }),
    })
    await handleDownloadResponse(resp)
  } catch (_) {
    ElMessage.error('下载失败')
  } finally {
    downloadingRecords.value = false
  }
}

async function downloadFiltered() {
  downloadingRecords.value = true
  try {
    const token = getEventAdminToken()
    const body = assignStatsScopeParams({})
    if (focusPlayer.value?.user_id) body.user_id = focusPlayer.value.user_id
    const resp = await fetch(`/api/event-admin/events/${props.eventId}/records/download`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify(body),
    })
    await handleDownloadResponse(resp)
  } catch (_) {
    ElMessage.error('下载失败')
  } finally {
    downloadingRecords.value = false
  }
}

/** 本赛事总计：不受玩家搜索影响 */
async function loadEventTotals() {
  loadingStats.value = true
  try {
    const params = assignStatsScopeParams({})
    const res = await eventAdminApi.get(`/events/${props.eventId}/player-stats`, { params })
    const data = res.data.data || {}
    statsTotals.value = data.totals || {
      total_games: 0,
      first_place_count: 0,
      second_place_count: 0,
      third_place_count: 0,
      fourth_place_count: 0,
      player_count: 0,
    }
    statsRuleOptions.value = data.filters?.rules || []
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载统计失败')
  } finally {
    loadingStats.value = false
  }
}

async function searchPlayer() {
  const q = statsFilter.q.trim()
  if (!q) {
    clearPlayerSearch()
    return
  }
  searchingPlayer.value = true
  try {
    const params = assignStatsScopeParams({ q })
    const res = await eventAdminApi.get(`/events/${props.eventId}/player-stats`, { params })
    const players = res.data.data?.players || []
    if (!players.length) {
      focusPlayer.value = null
      ElMessage.warning(`未找到该玩家在本${venueNoun.value}的对局`)
    } else {
      const exactId = /^\d+$/.test(q)
        ? players.find((p) => String(p.user_id) === q)
        : null
      const exactName = players.find(
        (p) => String(p.username || '').toLowerCase() === q.toLowerCase()
      )
      focusPlayer.value = exactId || exactName || players[0]
      if (players.length > 1 && !exactId && !exactName) {
        ElMessage.info(`匹配到 ${players.length} 名玩家，已显示「${focusPlayer.value.username}」`)
      }
    }
    recordsPage.current = 1
    await loadRecords()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '查询玩家失败')
  } finally {
    searchingPlayer.value = false
  }
}

function clearPlayerSearch() {
  statsFilter.q = ''
  focusPlayer.value = null
  recordsPage.current = 1
  loadRecords()
}

async function onStatsScopeChange() {
  recordsPage.current = 1
  await loadEventTotals()
  if (focusPlayer.value) {
    await reloadFocusPlayerStats()
  }
  await loadRecords()
}

async function reloadFocusPlayerStats() {
  if (!focusPlayer.value) return
  searchingPlayer.value = true
  try {
    const params = assignStatsScopeParams({ q: String(focusPlayer.value.user_id) })
    const res = await eventAdminApi.get(`/events/${props.eventId}/player-stats`, { params })
    const players = res.data.data?.players || []
    const hit = players.find((p) => Number(p.user_id) === Number(focusPlayer.value.user_id))
    if (hit) {
      focusPlayer.value = hit
    } else {
      focusPlayer.value = null
      statsFilter.q = ''
      ElMessage.warning('该玩家在当前筛选下无对局')
    }
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '查询玩家失败')
  } finally {
    searchingPlayer.value = false
  }
}

function resetStatsFilter() {
  statsFilter.rule = ''
  statsFilter.game_type = ''
  statsFilter.q = ''
  statsDateRange.value = null
  focusPlayer.value = null
  recordsPage.current = 1
  loadEventTotals()
  loadRecords()
}

function refreshStatsTab() {
  loadEventTotals()
  if (focusPlayer.value) {
    reloadFocusPlayerStats().then(() => loadRecords())
  } else {
    loadRecords()
  }
}

async function loadProfileChange() {
  try {
    const res = await eventAdminApi.get(`/events/${props.eventId}/profile-change`)
    const data = res.data.data || {}
    profileForm.name = data.current_name || detail.value?.name || ''
    profileForm.description = data.current_description || detail.value?.description || ''
    pendingProfile.value = data.pending || null
  } catch {
    profileForm.name = detail.value?.name || ''
    profileForm.description = detail.value?.description || ''
    pendingProfile.value = null
  }
}

async function loadAnnouncements() {
  loadingAnnouncements.value = true
  try {
    const res = await eventAdminApi.get(`/events/${props.eventId}/announcements`)
    announcements.value = res.data.data?.items || []
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载公告失败')
    announcements.value = []
  } finally {
    loadingAnnouncements.value = false
  }
}

async function submitProfileChange() {
  if (!profileForm.name.trim()) {
    ElMessage.warning(`请填写${venueNoun.value}名称`)
    return
  }
  savingProfile.value = true
  try {
    const res = await eventAdminApi.post(`/events/${props.eventId}/profile-change`, {
      name: profileForm.name.trim(),
      description: profileForm.description,
      reason: profileForm.reason.trim(),
    })
    pendingProfile.value = res.data.data?.pending || null
    profileForm.reason = ''
    ElMessage.success(res.data.message || '已提交审核')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '提交失败')
  } finally {
    savingProfile.value = false
  }
}

async function cancelProfileChange() {
  cancellingProfile.value = true
  try {
    await eventAdminApi.post(`/events/${props.eventId}/profile-change/cancel`)
    pendingProfile.value = null
    ElMessage.success('已撤销申请')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '撤销失败')
  } finally {
    cancellingProfile.value = false
  }
}

async function publishAnnouncement() {
  if (!announceForm.title.trim() || !announceForm.body.trim()) {
    ElMessage.warning('请填写公告标题与内容')
    return
  }
  publishingAnnounce.value = true
  try {
    const res = await eventAdminApi.post(`/events/${props.eventId}/announcements`, {
      title: announceForm.title.trim(),
      body: announceForm.body.trim(),
    })
    announcements.value = res.data.data?.items || []
    announceForm.title = ''
    announceForm.body = ''
    ElMessage.success(res.data.message || '公告已发布')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '发布失败')
  } finally {
    publishingAnnounce.value = false
  }
}

async function deleteAnnouncement(row) {
  try {
    await ElMessageBox.confirm(`确认删除公告「${row.title}」？`, '删除公告', { type: 'warning' })
    const res = await eventAdminApi.delete(
      `/events/${props.eventId}/announcements/${row.announcement_id}`
    )
    announcements.value = res.data.data?.items || []
    ElMessage.success('公告已删除')
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '删除失败')
  }
}

function resolveCreateRoomPermission(src) {
  const raw = String(src.create_room_permission || '').trim().toLowerCase()
  if (raw === 'all' || raw === 'registered' || raw === 'admin') return raw
  if (src.unregistered_can_create_room) return 'all'
  if (src.member_can_create_room) return 'registered'
  return 'admin'
}

function applyEntryForm(cfg) {
  const src = cfg && typeof cfg === 'object' ? cfg : {}
  entryForm.forbid_tourist = Boolean(src.forbid_tourist)
  entryForm.auto_approve = Boolean(src.auto_approve)
  entryForm.create_room_permission = resolveCreateRoomPermission(src)
  entryForm.unregistered_can_ready = Boolean(src.unregistered_can_ready)
  entryForm.join_code = String(src.join_code || '')
}

async function loadRegistrations() {
  loadingRegistrations.value = true
  try {
    const res = await eventAdminApi.get(`/events/${props.eventId}/registrations`, {
      params: { status: registrationFilter.value || undefined },
    })
    registrations.value = res.data.data?.items || []
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载报名失败')
    registrations.value = []
  } finally {
    loadingRegistrations.value = false
  }
}

async function addPlayerByUid() {
  const uid = addPlayerForm.user_id.trim()
  if (!uid) {
    ElMessage.warning('请填写玩家 UID')
    return
  }
  addingPlayer.value = true
  try {
    await eventAdminApi.post(`/events/${props.eventId}/registrations`, { user_id: uid })
    ElMessage.success('已将该玩家加入')
    addPlayerForm.user_id = ''
    if (registrationFilter.value && registrationFilter.value !== 'approved') {
      registrationFilter.value = 'approved'
    }
    await loadRegistrations()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '添加失败')
  } finally {
    addingPlayer.value = false
  }
}

async function reviewRegistration(row, status) {
  const action = status === 'approved' ? '通过' : '拒绝'
  try {
    await ElMessageBox.confirm(
      `确认${action}「${row.username || row.user_id}」的报名？`,
      `${action}报名`,
      { type: status === 'approved' ? 'info' : 'warning' }
    )
    await eventAdminApi.post(`/events/${props.eventId}/registrations/${row.user_id}/review`, {
      status,
    })
    ElMessage.success(`已${action}`)
    await loadRegistrations()
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '操作失败')
  }
}

async function loadReady() {
  const eventId = props.eventId
  loadingReady.value = true
  try {
    const res = await eventAdminApi.get(`/events/${eventId}/ready`)
    if (eventId !== props.eventId) return
    readyPlayers.value = res.data.data?.items || []
    const readyIds = new Set(readyPlayers.value.map((player) => Number(player.user_id)))
    for (const row of readyTableRef.value?.getSelectionRows?.() || []) {
      if (!readyIds.has(Number(row.user_id))) readyTableRef.value.toggleRowSelection(row, false)
    }
  } catch (e) {
    if (eventId !== props.eventId) return
    ElMessage.error(e.response?.data?.message || '加载准备池失败')
    readyPlayers.value = []
    selectedReadyIds.value = []
    readyTableRef.value?.clearSelection()
  } finally {
    if (eventId === props.eventId) loadingReady.value = false
  }
}

function applyRoomSettingsDoc(data) {
  if (!data || !Array.isArray(data.presets) || !data.manual || !data.auto_match) return
  if (Number(data.revision) < Number(roomSettingsDoc.value.revision)) return
  roomSettingsDoc.value = data
  roomSettingsLoaded.value = true
  matchingPresetChoice.value = data.auto_match.preset_id || 'default'
  if (data.runtime) {
    matchingRuntime.value = data.runtime
    matchingServiceError.value = ''
  }
  if (data.auto_match.enabled) {
    selectedReadyIds.value = []
    readyTableRef.value?.clearSelection()
  }
}

async function loadRoomSettings(silent = false) {
  const eventId = props.eventId
  if (!silent) loadingRoomSettings.value = true
  try {
    const res = await eventAdminApi.get(`/events/${eventId}/room-settings`)
    if (eventId === props.eventId) applyRoomSettingsDoc(res.data.data)
  } catch (e) {
    if (!silent && eventId === props.eventId) {
      ElMessage.error(e.response?.data?.message || '加载对局设置失败，请刷新后重试')
    }
  } finally {
    if (eventId === props.eventId && !silent) loadingRoomSettings.value = false
  }
}

async function refreshReadyTab() {
  await Promise.all([loadReady(), loadRoomSettings(), loadMatchingRuntime()])
}

async function loadMatchingRuntime() {
  const eventId = props.eventId
  try {
    const res = await eventAdminApi.get(`/events/${eventId}/auto-match`)
    if (eventId !== props.eventId) return
    matchingRuntime.value = res.data.data
    matchingServiceError.value = ''
  } catch (e) {
    if (eventId === props.eventId) {
      matchingRuntime.value = null
      matchingServiceError.value = '无法连接自动匹配服务，已保存的设置会在服务恢复后生效。'
    }
  }
}

function settingsForChoice(choice) {
  return choice === 'default' ? manualSettings.value : roomPresets.value.find(item => item.preset_id === choice)
}

async function togglePresetEditor() {
  if (settingsEditorExpanded.value) {
    if (!await presetEditorRef.value?.canDiscard()) return
    presetEditorRef.value?.resetDraft()
  }
  settingsEditorExpanded.value = !settingsEditorExpanded.value
}

async function closeManagePanel() {
  if (presetEditorRef.value && !await presetEditorRef.value.canDiscard()) return
  emit('close')
}

async function saveRoomSettingsRequest(method, path, payload, options = {}) {
  if (!roomSettingsLoaded.value || savingRoomSettings.value || savingMatchmaking.value) return false
  const eventId = props.eventId
  const busy = options.matching ? savingMatchmaking : savingRoomSettings
  busy.value = true
  try {
    const res = await eventAdminApi.request({ method, url: `/events/${eventId}/${path}`, data: { revision: options.revision ?? roomSettingsDoc.value.revision, ...payload } })
    if (eventId !== props.eventId) return false
    if (res.data?.success === false) throw new Error(res.data.message || '保存失败')
    applyRoomSettingsDoc(res.data.data)
    if (res.data.message?.includes('暂未响应')) matchingServiceError.value = res.data.message
    return true
  } catch (e) {
    if (eventId !== props.eventId) return false
    if (e.response?.status === 409) {
      applyRoomSettingsDoc(e.response.data?.data)
      ElMessage.warning('已保存的设置发生更新，当前操作尚未保存。请核对后重试。')
    } else ElMessage.error(e.response?.data?.message || e.message || '保存对局设置失败')
    return false
  } finally {
    if (eventId === props.eventId) busy.value = false
  }
}

async function savePresetEditorChange(change) {
  const { kind, presetId, revision, name, room_rule, room_config } = change
  const path = kind === 'default' ? 'room-settings' : kind === 'create' ? 'room-presets' : `room-presets/${encodeURIComponent(presetId)}`
  const body = kind === 'default' ? { room_rule, room_config, preset_id: null } : { name, room_rule, room_config }
  const saved = await saveRoomSettingsRequest(kind === 'create' ? 'post' : 'put', path, body, { revision })
  if (saved) ElMessage.success(kind === 'default' ? '默认配置已保存' : '对局预设已保存')
  return { saved, presetId: kind === 'create' ? roomSettingsDoc.value.saved_preset_id : presetId }
}

async function removeRoomPreset(presetId, revision) {
  if (autoMatching.value.preset_id === presetId) { ElMessage.warning('请先切换自动匹配使用的预设'); return false }
  const saved = await saveRoomSettingsRequest('delete', `room-presets/${encodeURIComponent(presetId)}`, {}, { revision })
  if (saved) ElMessage.success('预设已删除')
  return saved
}

async function onMatchingChoiceChange() {
  await persistMatchingChoice(Boolean(autoMatching.value.enabled))
}

async function persistMatchingChoice(enabled) {
  if (!matchingChoiceExists.value) { ElMessage.warning('所选预设已删除，请重新选择'); return false }
  const saved = await saveRoomSettingsRequest('put', 'auto-match', { enabled, preset_id: matchingPresetChoice.value === 'default' ? null : matchingPresetChoice.value }, { matching: true })
  if (saved) {
    matchingPresetChoice.value = autoMatching.value.preset_id || 'default'
    if (matchingServiceError.value && enabled) ElMessage.warning(matchingServiceError.value)
    else ElMessage.success(enabled ? '自动匹配已保存，将使用所选配置自动开局' : '自动匹配设置已保存')
  }
  if (!saved) matchingPresetChoice.value = autoMatching.value.preset_id || 'default'
  return saved
}

async function toggleAutoMatching(enabled) {
  matchingPresetChoice.value = autoMatching.value.preset_id || 'default'
  if (await persistMatchingChoice(enabled)) await Promise.all([loadReady(), loadRooms(), loadGames(), loadMatchingRuntime()])
}

function onReadySelectionChange(rows) {
  selectedReadyIds.value = (rows || []).map((r) => Number(r.user_id)).filter((id) => id > 0)
}

function openSeatDialog() {
  if (autoMatching.value.enabled || !roomSettingsLoaded.value || selectedReadyIds.value.length !== 4) return
  seatingPlayers.value = selectedReadyIds.value.map(id => readyPlayers.value.find(player => Number(player.user_id) === id)).filter(Boolean).map(player => ({ ...player }))
  if (seatingPlayers.value.length !== 4) { ElMessage.warning('准备池已更新，请重新选择 4 人'); return }
  seatPresetChoice.value = 'default'
  seatDialogRevision.value = roomSettingsDoc.value.revision
  seatDialogVisible.value = true
}

async function seatTable() {
  if (autoMatching.value.enabled || !roomSettingsLoaded.value || seatingTable.value) return
  if (!seatingPlayersReady.value || !seatSettingsSource.value) {
    ElMessage.warning('玩家或对局配置已变化，请重新确认')
    return
  }
  const eventId = props.eventId
  seatingTable.value = true
  try {
    const res = await eventAdminApi.post(`/events/${eventId}/seat`, {
      user_ids: seatingPlayers.value.map(player => Number(player.user_id)),
      revision: seatDialogRevision.value,
      preset_id: seatPresetChoice.value === 'default' ? null : seatPresetChoice.value,
    })
    if (eventId !== props.eventId) return
    if (res.data?.success === false) throw new Error(res.data.message || '组桌失败')
    if (res.data.audit_warning) ElMessage.warning(res.data.message || '已组桌，操作记录写入失败，请勿重复组桌')
    else ElMessage.success('已组桌，玩家将进入新房间')
    seatDialogVisible.value = false
    selectedReadyIds.value = []
    readyTableRef.value?.clearSelection()
    await Promise.all([loadReady(), loadRooms()])
  } catch (e) {
    if (eventId !== props.eventId) return
    if (e.response?.status === 409) {
      applyRoomSettingsDoc(e.response.data?.data)
      seatDialogRevision.value = roomSettingsDoc.value.revision
      ElMessage.warning('对局配置已更新，请核对当前设置后再次确认组桌')
    } else ElMessage.error(e.response?.data?.message || e.message || '组桌失败')
  } finally {
    if (eventId === props.eventId) seatingTable.value = false
  }
}

async function saveEntryConfig() {
  savingEntry.value = true
  try {
    const res = await eventAdminApi.put(`/events/${props.eventId}/entry-config`, {
      entry_config: {
        forbid_tourist: entryForm.forbid_tourist,
        auto_approve: entryForm.auto_approve,
        create_room_permission: entryForm.create_room_permission,
        unregistered_can_ready: entryForm.unregistered_can_ready,
        join_code: entryForm.join_code,
      },
    })
    applyEntryForm(res.data.data)
    if (detail.value) detail.value.entry_config = { ...res.data.data }
    ElMessage.success('准入配置已保存')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '保存失败')
  } finally {
    savingEntry.value = false
  }
}

async function load() {
  if (!props.eventId) return
  loading.value = true
  try {
    const res = await eventAdminApi.get(`/events/${props.eventId}`)
    detail.value = res.data.data
    activeTab.value = isOwner.value ? 'profile' : 'announcements'
    if (detail.value?.status === 'active') activeTab.value = 'rooms'
    await Promise.all([
      loadRooms(),
      loadGames(),
      loadEventTotals(),
      loadRecords(),
      loadAnnouncements(),
      loadRegistrations(),
      loadReady(),
      loadRoomSettings(),
      isOwner.value ? loadProfileChange() : Promise.resolve(),
    ])
    applyEntryForm(detail.value?.entry_config)
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载失败')
    detail.value = null
  } finally {
    loading.value = false
  }
}

function applyEventPatch(data) {
  Object.assign(detail.value, data)
  emit('updated')
}

async function openEvent() {
  try {
    await ElMessageBox.confirm(
      `确认开启${venueNoun.value}「${detail.value.name}」？`,
      `开启${venueNoun.value}`,
      { type: 'info' }
    )
    const res = await eventAdminApi.post(`/events/${props.eventId}/open`, {})
    applyEventPatch(res.data.data)
    ElMessage.success(`${venueNoun.value}已开启`)
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '开启失败')
  }
}

async function closeEvent() {
  try {
    await ElMessageBox.confirm(
      `确认关闭${venueNoun.value}「${detail.value.name}」？关闭后将被封存，再次开启需平台管理员审核`,
      `关闭${venueNoun.value}`,
      { type: 'warning' }
    )
    const res = await eventAdminApi.post(`/events/${props.eventId}/close`, {})
    applyEventPatch(res.data.data)
    ElMessage.success(`${venueNoun.value}已关闭`)
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '关闭失败')
  }
}

async function requestReopen() {
  try {
    await ElMessageBox.confirm(
      `确认申请重新开启${venueNoun.value}「${detail.value.name}」？`,
      '申请重新开启',
      { type: 'info' }
    )
    const res = await eventAdminApi.post(`/events/${props.eventId}/request-reopen`, {})
    applyEventPatch(res.data.data)
    ElMessage.success(res.data.message || '已提交重新开启申请')
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '申请失败')
  }
}

async function addAdmin() {
  if (!adminForm.user_id.trim()) {
    ElMessage.warning('请填写用户 ID')
    return
  }
  savingAdmin.value = true
  try {
    const res = await eventAdminApi.post(`/events/${props.eventId}/admins`, {
      user_id: adminForm.user_id.trim(),
    })
    detail.value.admins = res.data.data.admins
    adminForm.user_id = ''
    ElMessage.success(`${venueNoun.value}子管理员已添加`)
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '添加失败')
  } finally {
    savingAdmin.value = false
  }
}

async function removeAdmin(row) {
  try {
    await ElMessageBox.confirm(
      `确认移除${venueNoun.value}子管理员「${row.username || row.user_id}」？`,
      `移除${venueNoun.value}子管理员`,
      { type: 'warning' }
    )
    const res = await eventAdminApi.delete(`/events/${props.eventId}/admins/${row.user_id}`)
    detail.value.admins = res.data.data.admins
    ElMessage.success('已移除')
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '移除失败')
  }
}

async function createRoom() {
  if (creatingRoom.value) return
  creatingRoom.value = true
  try {
    await eventAdminApi.post(`/events/${props.eventId}/rooms`, buildEventRoomSettings(roomForm))
    roomForm.room_name = ''
    roomForm.password = ''
    roomDialogVisible.value = false
    ElMessage.success('房间已创建')
    await loadRooms()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '创建失败')
  } finally {
    creatingRoom.value = false
  }
}

function openRoomDialog() {
  Object.assign(roomForm, createEventRoomForm(manualSettings.value))
  roomDialogVisible.value = true
}

async function deleteRoom(row) {
  try {
    await ElMessageBox.confirm(
      `确认删除房间 ${row.room_id}？`,
      '删除房间',
      { type: 'warning' }
    )
    await eventAdminApi.delete(`/events/${props.eventId}/rooms/${row.room_id}`)
    ElMessage.success('房间已删除')
    await loadRooms()
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '删除失败')
  }
}

async function callControl(action, row, confirmText) {
  try {
    await ElMessageBox.confirm(confirmText, '确认操作', { type: 'warning' })
  } catch (_) {
    return
  }
  try {
    const res = await eventAdminApi.post(
      `/events/${props.eventId}/games/${row.gamestate_id}/${action}`
    )
    ElMessage.success(res.data.message || '操作成功')
    await loadGames()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '操作失败')
  }
}

function onPause(row) {
  callControl('pause', row, '确认强制暂停该对局？')
}
function onResume(row) {
  callControl('resume', row, '确认强制解除暂停？')
}
async function onEnd(row) {
  try {
    await ElMessageBox.confirm('确认强制结束该对局？玩家将被踢回大厅。', '危险操作', {
      type: 'error',
      confirmButtonText: '结束对局',
    })
  } catch (_) {
    return
  }
  try {
    const res = await eventAdminApi.post(
      `/events/${props.eventId}/games/${row.gamestate_id}/end`
    )
    ElMessage.success(res.data.message || '已结束')
    await loadGames()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '操作失败')
  }
}

watch(
  () => props.eventId,
  () => {
    roomSettingsDoc.value = { revision: 0, manual: {}, auto_match: { enabled: false }, presets: [] }
    roomSettingsLoaded.value = false
    roomDialogVisible.value = false
    settingsEditorExpanded.value = true
    seatDialogVisible.value = false
    seatingPlayers.value = []
    matchingPresetChoice.value = 'default'
    seatingTable.value = false
    matchingRuntime.value = null
    matchingServiceError.value = ''
    selectedReadyIds.value = []
    readyTableRef.value?.clearSelection()
    savingRoomSettings.value = false
    savingMatchmaking.value = false
    load()
  },
  { immediate: true }
)

watch(activeTab, (tab) => {
  if (tab === 'ready') refreshReadyTab()
})

let pollingReady = false
const readyPollTimer = setInterval(async () => {
  if (activeTab.value !== 'ready' || document.hidden || loading.value || pollingReady || loadingReady.value || seatingTable.value || savingRoomSettings.value || savingMatchmaking.value) return
  pollingReady = true
  try {
    await Promise.all([loadReady(), loadRoomSettings(true), loadMatchingRuntime()])
  } finally {
    pollingReady = false
  }
}, 10000)
onUnmounted(() => clearInterval(readyPollTimer))
</script>

<style scoped>
.emp {
  margin-top: 20px;
  padding: 20px 20px 8px;
  border: 1px solid #e4e7ed;
  border-radius: 10px;
  background: #fafbfc;
}
.emp-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 16px;
}
.emp-head-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
}
.emp-public-link {
  font-size: 13px;
  color: #409eff;
  text-decoration: none;
  padding: 0 8px;
  white-space: nowrap;
}
.emp-public-link:hover {
  text-decoration: underline;
}
.emp-name {
  margin: 0 0 8px;
  font-size: 22px;
  font-weight: 700;
  color: #1f2329;
  line-height: 1.3;
}
.emp-meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.emp-id,
.emp-stat {
  font-size: 12px;
  color: #909399;
  font-family: ui-monospace, Consolas, monospace;
}
.emp-lifecycle {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 14px;
  border-radius: 8px;
  margin-bottom: 16px;
}
.emp-lifecycle.registered {
  background: #f4f4f5;
  border: 1px solid #e9e9eb;
}
.emp-lifecycle.active {
  background: #f0f9eb;
  border: 1px solid #e1f3d8;
}
.emp-lifecycle.closed {
  background: #fef0f0;
  border: 1px solid #fde2e2;
}
.emp-lifecycle-text {
  margin: 0;
  flex: 1;
  min-width: 0;
  font-size: 13px;
  color: #606266;
  line-height: 1.5;
}
.emp-lifecycle-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}
.emp-desc {
  margin-bottom: 16px;
  padding: 16px;
  background: #fff;
  border-radius: 8px;
  border: 1px solid #ebeef5;
}
.emp-sec-title {
  margin: 0 0 10px;
  font-size: 13px;
  font-weight: 600;
  color: #909399;
  letter-spacing: 0.04em;
}
.emp-desc-body {
  margin: 0;
  white-space: pre-wrap;
  line-height: 1.7;
  font-size: 14px;
  color: #303133;
  min-height: 2.4em;
}
.emp-desc-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 16px 28px;
  margin: 14px 0 0;
  padding-top: 12px;
  border-top: 1px dashed #ebeef5;
}
.emp-desc-meta div {
  display: flex;
  gap: 8px;
  font-size: 12px;
}
.emp-desc-meta dt {
  color: #909399;
  margin: 0;
}
.emp-desc-meta dd {
  margin: 0;
  color: #606266;
}
.emp-tabs {
  background: #fff;
  border-radius: 8px;
  border: 1px solid #ebeef5;
  padding: 0 12px 12px;
}
.emp-tab-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 8px;
}
.emp-ready-bar {
  align-items: center;
}
.emp-ready-summary {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  color: #606266;
  font-size: 13px;
}
.emp-ready-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.emp-ready-section-head { display: flex; flex-wrap: wrap; align-items: flex-start; justify-content: space-between; gap: 10px 16px; margin: 12px 0; }
.emp-ready-section-head h4 { margin: 0; color: #303133; font-size: 14px; font-weight: 600; }
.emp-default-settings { margin-bottom: 16px; }
.emp-default-settings > .emp-ready-section-head { align-items: center; }
.emp-default-settings > .emp-ready-section-head .emp-ready-summary { display: block; min-width: 0; }
.emp-default-settings > .emp-ready-section-head .emp-ready-summary > span { display: block; margin-top: 5px; color: #909399; line-height: 1.6; }
.emp-default-settings > .emp-ready-section-head > .el-button { flex-shrink: 0; margin-left: 0; }
.emp-manual-seating { padding-top: 2px; border-top: 1px solid #ebeef5; }
.emp-manual-seating .emp-settings-description { margin: 5px 0 0; }
.emp-seat-players { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px 16px; margin: 0 0 16px; padding: 0; list-style: none; }
.emp-seat-players li { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 4px 8px; padding: 8px 10px; background: #f5f7fa; border-radius: 4px; font-size: 13px; }
.emp-seat-players span { color: #909399; font-size: 12px; }
.emp-seat-preset-select { width: 100%; }
.emp-seat-settings-title { margin: 12px 0 8px; font-size: 14px; color: #303133; }
.emp-seat-settings { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 16px; margin: 0 0 16px; }
.emp-seat-settings > div { display: flex; justify-content: space-between; gap: 8px; padding: 7px 0; border-bottom: 1px solid #ebeef5; font-size: 13px; }
.emp-seat-settings dt { flex-shrink: 0; color: #909399; }
.emp-seat-settings dd { margin: 0; color: #303133; text-align: right; overflow-wrap: anywhere; }
.emp-form {
  flex-wrap: wrap;
}
.emp-reg-table :deep(.cell) {
  white-space: normal;
  word-break: break-all;
  line-height: 1.45;
}
.emp-count {
  color: #909399;
  font-size: 13px;
  line-height: 32px;
}
.emp-room-form :deep(.el-form-item) {
  margin-bottom: 10px;
}
.emp-alert {
  margin-bottom: 12px;
}
.emp-profile-form {
  max-width: 640px;
}
.emp-announce-tip :deep(.el-alert__content) {
  width: 100%;
}
.tip-lead {
  margin: 0 0 8px;
  font-size: 13px;
  line-height: 1.6;
  color: #606266;
}
.tip-list {
  margin: 0;
  padding-left: 1.2em;
  font-size: 13px;
  line-height: 1.7;
  color: #606266;
}
.player-chip {
  display: inline-block;
  margin-right: 8px;
  font-size: 12px;
}
.muted {
  color: #909399;
  font-size: 12px;
}
.emp-stats-totals,
.emp-stats-player-detail {
  margin-bottom: 14px;
  padding: 12px 14px;
  border: 1px solid #ebeef5;
  border-radius: 8px;
  background: #fafbfc;
}
.emp-stats-totals-head,
.emp-stats-player-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.emp-stats-heading {
  margin: 0 0 10px;
  font-size: 13px;
  font-weight: 600;
  color: #606266;
}
.emp-stats-totals-head .emp-stats-heading,
.emp-stats-player-head .emp-stats-heading {
  margin-bottom: 0;
}
.emp-stats-totals-head + .emp-stats-grid,
.emp-stats-player-head + .emp-stats-grid {
  margin-top: 10px;
}
.emp-stats-uid {
  margin-left: 8px;
  font-weight: 400;
  color: #909399;
  font-family: ui-monospace, Consolas, monospace;
  font-size: 12px;
}
.emp-stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
  gap: 8px 12px;
}
.emp-stats-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.emp-stats-label {
  font-size: 12px;
  color: #909399;
}
.emp-stats-value {
  font-size: 16px;
  font-weight: 600;
  color: #303133;
  font-variant-numeric: tabular-nums;
}
.emp-stats-search {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  margin-bottom: 14px;
}
.emp-stats-search :deep(.emp-stats-daterange.el-date-editor) {
  flex: 0 1 260px;
  width: 260px;
  min-width: 0;
  max-width: 100%;
}
.emp-stats-search :deep(.emp-stats-daterange .el-range-input) {
  font-size: 12px;
}
.emp-stats-scope,
.emp-stats-player-search,
.emp-stats-search-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  min-width: 0;
  max-width: 100%;
}
.emp-stats-scope {
  flex: 0 1 268px;
}
.emp-stats-scope :deep(.el-select) {
  flex: 1 1 110px;
  width: 130px;
  min-width: 0;
}
.emp-stats-player-search {
  flex: 0 1 auto;
}
.emp-stats-player-search > .el-input {
  flex: 1 1 180px;
  width: 200px;
  min-width: 0;
}
.emp-stats-search-actions :deep(.el-button + .el-button),
.emp-records-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}
.emp-stats-range-hint {
  margin-left: 8px;
  font-weight: 400;
  color: #909399;
}
.emp-records-section {
  margin-top: 4px;
  border-top: 1px solid #eef0f3;
  padding-top: 10px;
}
.emp-records-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin-bottom: 8px;
}
.emp-records-title {
  font-size: 14px;
  font-weight: 700;
  color: #1e293b;
}
.emp-records-total {
  font-size: 12px;
  color: #94a3b8;
}
.emp-records-table {
  width: 100%;
}
:deep(.emp-records-table .el-table__cell) {
  padding: 4px 0;
}
:deep(.emp-records-table th.el-table__cell) {
  background: #f5f7fa;
  color: #475569;
  font-weight: 600;
  border-bottom: 1px solid #dcdfe6;
}
:deep(.emp-records-table td.el-table__cell) {
  border-color: #eef0f3;
}
.cell-game-id {
  font-size: 12px;
  font-family: Consolas, Menlo, monospace;
  color: #303133;
}
.cell-time {
  font-size: 12px;
  color: #64748b;
  font-family: Consolas, Menlo, monospace;
}
.cell-scene {
  font-size: 12px;
  color: #303133;
}
.cell-mode {
  font-size: 12px;
  color: #64748b;
  font-family: Consolas, Menlo, monospace;
}
.cell-score {
  font-weight: 700;
  font-family: Consolas, Menlo, monospace;
}
.cell-score.pos {
  color: #c0392b;
}
.cell-score.neg {
  color: #2c7a2c;
}
.cell-players {
  font-size: 12px;
  color: #64748b;
  cursor: help;
}
.cell-dash {
  color: #c0c4cc;
}
.tip-player {
  font-size: 12px;
  line-height: 1.7;
}
.tip-player .pos {
  color: #ff7a7a;
  font-weight: 700;
}
.tip-player .neg {
  color: #6ee06e;
  font-weight: 700;
}
.rank-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 18px;
  height: 18px;
  padding: 0 4px;
  font-weight: 700;
  font-size: 11px;
  font-family: Consolas, Menlo, monospace;
}
.rank-1 {
  background: #6fd86f;
  color: #1f5e1f;
}
.rank-2 {
  background: #5dadff;
  color: #fff;
}
.rank-3 {
  background: #aab4c2;
  color: #2c3848;
}
.rank-4 {
  background: #ff7a7a;
  color: #fff;
}
.emp-records-foot {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px 12px;
  margin-top: 10px;
}
.emp-records-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.emp-room-presets {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 12px;
  margin: 10px 0 14px;
}
.emp-control-label {
  color: #606266;
  font-size: 13px;
  white-space: nowrap;
}
.emp-room-presets :deep(.emp-preset-select) {
  width: 220px;
  max-width: 100%;
}
.emp-preset-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.emp-preset-actions :deep(.el-button),
.emp-ready-actions :deep(.el-button) {
  margin-left: 0;
}
.emp-settings-hint {
  color: #909399;
  font-size: 12px;
  line-height: 1.6;
}
.emp-auto-match {
  margin-top: 18px;
  padding-top: 14px;
  border-top: 1px solid #ebeef5;
}
.emp-auto-match-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
}
.emp-auto-match-head h4 {
  margin: 0;
  color: #303133;
  font-size: 14px;
  font-weight: 600;
}
.emp-settings-description {
  margin: 6px 0 12px;
  color: #606266;
  font-size: 13px;
  line-height: 1.7;
}
.emp-match-settings {
  margin-bottom: 8px;
}
.emp-match-status,
.emp-dialog-hint {
  margin: 0 0 10px;
}
.emp-records-foot :deep(.el-pagination) {
  flex-wrap: wrap;
  gap: 8px;
  max-width: 100%;
}
.emp-records-foot :deep(.el-pagination > *) {
  margin: 0;
}
@media (max-width: 640px) {
  .emp-seat-players, .emp-seat-settings { grid-template-columns: minmax(0, 1fr); }
  .emp-room-presets :deep(.emp-preset-select) { flex: 1; min-width: 140px; }
  .emp-room-presets > .emp-settings-hint,
  .emp-match-settings > .emp-ready-summary { flex-basis: 100%; }
  .emp { padding: 12px; }
  .emp-head { flex-wrap: wrap; }
  .emp-head-main { min-width: 0; }
  .emp-name { overflow-wrap: anywhere; }
  .emp-desc { padding: 12px; }
  .emp-lifecycle-text { flex-basis: 100%; }
  .emp-stats-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .emp-stats-search :deep(.emp-stats-daterange.el-date-editor),
  .emp-stats-scope,
  .emp-stats-player-search {
    flex-basis: 100%;
    width: 100%;
  }
}
</style>
