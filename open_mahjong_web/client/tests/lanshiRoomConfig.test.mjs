import test from 'node:test'
import assert from 'node:assert/strict'
import { buildGuobiaoRoomPayload, createDefaultGuobiaoRoomConfig } from '../src/utils/guobiaoRoomConfig.js'

test('蓝十固定无花、5分起和及错和罚40，覆盖旧表单的所有输入分支', () => {
  for (const limit of [1, 5, 8, 64]) {
    for (const open of [false, true]) {
      for (const penalty of [0, 1]) {
        for (const flowers of [false, true]) {
          const form = {
            ...createDefaultGuobiaoRoomConfig(), sub_rule: 'guobiao/lanshi',
            hepai_limit: limit, use_flowers: flowers, open_cuohe: open, cuohe_type: penalty,
          }
          const before = structuredClone(form)
          const { room_config: config } = buildGuobiaoRoomPayload(form)
          assert.deepEqual([config.hepai_limit, config.use_flowers, config.open_cuohe, config.cuohe_type], [5, false, true, 1])
          assert.deepEqual(form, before)
        }
      }
    }
  }
})

test('标准、小林、K神仍保留自定义起和及错和选项', () => {
  for (const sub_rule of ['guobiao/standard', 'guobiao/xiaolin', 'guobiao/kshen']) {
    for (const open_cuohe of [false, true]) {
      const { room_config: config } = buildGuobiaoRoomPayload({
        ...createDefaultGuobiaoRoomConfig(), sub_rule, hepai_limit: 7,
        open_cuohe, cuohe_type: 0, use_flowers: true,
      })
      assert.deepEqual([config.hepai_limit, config.use_flowers, config.open_cuohe, config.cuohe_type], [7, true, open_cuohe, 0])
    }
  }
})
