<template>
  <div class="event-room-fields"><div class="event-room-fields-grid"><slot /></div></div>
</template>
<style>
/* Keep the existing rule components and their validation, while sharing one grid
   across component boundaries. Advanced expandable sections keep their own grid. */
.event-room-fields { container-type: inline-size; width: 100%; }
.event-room-fields .event-room-fields-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px 18px; align-items: start; }
.event-room-fields-grid > div:not(.el-form-item):not(.el-collapse):not(.el-alert),
.event-room-fields-grid > div > .settings,
.event-room-fields-grid > div > .common-fields,
.event-room-fields-grid > div > .shanxi-common-fields,
.event-room-fields-grid > div > .room-timers,
.event-room-fields-grid > div > .riichi-common,
.event-room-fields-grid > div > .riichi-switches,
.event-room-fields-grid > div > .gb-room-config-header,
.event-room-fields-grid > div > .gb-room-config-options,
.event-room-fields-grid > div > .gb-room-config-main,
.event-room-fields-grid > .duplicate-settings > .room-top,
.event-room-fields-grid > .duplicate-settings > .duplicate-generation,
.event-room-fields-grid .sichuan-wall-source,
.event-room-fields-grid .wall-source-parameters { display: contents; }
.event-room-fields-grid::before { content: ''; order: -1; grid-column: 1 / -1; }
.event-room-fields-grid::after { content: ''; order: 1; grid-column: 1 / -1; border-top: 1px solid var(--el-border-color-lighter); margin-top: 4px; }
.event-room-fields .el-form-item { min-width: 0; margin: 0; order: 2; }
.event-room-fields .el-form-item:where(:not(:has(.el-switch)):not(:has(.el-checkbox))) { grid-column: span 1; }
.event-room-fields .el-form-item.room-rule-field { order: -40; }
.event-room-fields .el-form-item.room-sub-rule-field { order: -30; }
.event-room-fields .el-form-item.room-name-field { order: -20; }
.event-room-fields .el-form-item.room-count-field,
.event-room-fields .el-form-item.room-preset-name-field { order: -10; }
.event-room-fields .el-form-item.wall-source-selection,
.event-room-fields .el-form-item.paired-mode-selection { grid-column: 1 / -1; order: 1; }
.event-room-fields .el-form-item.training-seed-field { grid-column: 1 / -1; order: 3; }
.event-room-fields .el-form-item:has(.el-switch) { order: 0; display: flex; flex-direction: row-reverse; align-items: flex-start; justify-content: flex-end; gap: 8px; }
.event-room-fields .el-form-item:has(.el-switch) > .el-form-item__label { flex: 1; height: auto; padding: 5px 0 0; margin: 0; line-height: 22px; white-space: normal; overflow-wrap: anywhere; }
.event-room-fields .el-form-item:has(.el-switch) > .el-form-item__content { flex: 0 0 auto; min-width: 0; margin: 0; line-height: 22px; }
.event-room-fields .el-form-item:has(.el-switch__label) > .el-form-item__content { flex: 1; }
.event-room-fields .el-switch { max-width: 100%; height: auto; min-height: 32px; align-items: flex-start; }
.event-room-fields .el-switch__core { flex-shrink: 0; margin-top: 6px; }
.event-room-fields .el-switch__label { height: auto; white-space: normal; overflow-wrap: anywhere; line-height: 22px; padding-top: 5px; }
.event-room-fields .el-form-item__label,
.event-room-fields .el-switch__label *,
.event-room-fields .el-checkbox__label,
.event-room-fields .el-radio__label,
.event-room-fields .el-radio-button__inner { font-weight: 400; }
.event-room-fields .room-segmented-options { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); width: 100%; }
.event-room-fields .room-segmented-options .el-radio-button { min-width: 0; }
.event-room-fields .room-segmented-options .el-radio-button__inner { width: 100%; padding: 9px 4px; white-space: normal; line-height: 1.4; }
.event-room-fields .el-checkbox { order: 0; margin: 0; height: auto; min-height: 32px; white-space: normal; align-items: flex-start; padding-top: 5px; }
.event-room-fields .el-checkbox__label { white-space: normal; overflow-wrap: anywhere; line-height: 22px; }
.event-room-fields .el-checkbox__input { margin-top: 4px; }
.event-room-fields .el-select,
.event-room-fields .el-input,
.event-room-fields .el-input-number { width: 100% !important; max-width: 100%; }
.event-room-fields-grid > div > p,
.event-room-fields-grid > div > a,
.event-room-fields-grid > div > .rulebook-links,
.event-room-fields-grid > div > .huahun-description,
.event-room-fields-grid > div > .el-alert,
.event-room-fields-grid > div > .el-collapse,
.event-room-fields-grid > .el-collapse,
.event-room-fields-grid .sichuan-wall-source > p,
.event-room-fields-grid .duplicate-generation > p,
.event-room-fields .seed-field,
.event-room-fields .room-top-full,
.event-room-fields .duplicate-room-field { grid-column: 1 / -1; order: 3; }
.event-room-fields .el-collapse .el-form-item { display: block; }
.event-room-fields .el-collapse .el-form-item__label { margin-bottom: 8px; }
.event-room-fields .el-collapse .el-form-item__content { display: flex; }
.event-room-fields .gb-room-section-title { display: none; }
.event-room-fields .el-form-item__content > .mode-help { display: none; }
@container (max-width: 620px) { .event-room-fields .event-room-fields-grid { grid-template-columns: repeat(3, minmax(0, 1fr)); } }
@container (max-width: 470px) { .event-room-fields .event-room-fields-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px 14px; } }
@container (max-width: 290px) { .event-room-fields .event-room-fields-grid { grid-template-columns: 1fr; } }
</style>
