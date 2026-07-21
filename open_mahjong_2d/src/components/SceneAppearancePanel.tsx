import { useRef } from 'react'

import type { SceneAppearanceSettings } from '../lib/sceneAppearance'

type SceneAppearancePanelProps = {
  title: string
  appearance: SceneAppearanceSettings
  backgroundImageName: string | null
  backgroundImageLoading: boolean
  volume: number
  onVolumeChange: (volume: number) => void
  onBackgroundColorTableChange: (color: string) => void
  onBackgroundColorOutsideChange: (color: string) => void
  onBackgroundImageEnabledChange: (enabled: boolean) => void
  onBackgroundImageAlphaChange: (alpha: number) => void
  onBackgroundImageSelected: (file: File) => void | Promise<void>
  onBackgroundImageCleared: () => void | Promise<void>
  onTileCoverColorChange: (index: number, color: string) => void
  onAddTileCoverColor: () => void
  onRemoveTileCoverColor: (index: number) => void
  onReset: () => void | Promise<void>
}

export default function SceneAppearancePanel({
  title,
  appearance,
  backgroundImageName,
  backgroundImageLoading,
  volume,
  onVolumeChange,
  onBackgroundColorTableChange,
  onBackgroundColorOutsideChange,
  onBackgroundImageEnabledChange,
  onBackgroundImageAlphaChange,
  onBackgroundImageSelected,
  onBackgroundImageCleared,
  onTileCoverColorChange,
  onAddTileCoverColor,
  onRemoveTileCoverColor,
  onReset,
}: SceneAppearancePanelProps) {
  const fileInputRef = useRef<HTMLInputElement | null>(null)

  return (
    <div className="scene-appearance-panel">
      <div className="scene-appearance-panel__header">
        <h3 className="scene-appearance-panel__title">{title}</h3>
        <button
          type="button"
          className="scene-appearance-panel__ghost-button"
          onClick={() => { void onReset() }}
        >
          重置
        </button>
      </div>

      <label className="scene-appearance-panel__field">
        <span className="scene-appearance-panel__label">牌桌内背景</span>
        <input
          type="color"
          value={appearance.backgroundColorTable}
          onChange={(event) => onBackgroundColorTableChange(event.target.value)}
        />
      </label>

      <label className="scene-appearance-panel__field">
        <span className="scene-appearance-panel__label">牌桌外背景</span>
        <input
          type="color"
          value={appearance.backgroundColorOutside}
          onChange={(event) => onBackgroundColorOutsideChange(event.target.value)}
        />
      </label>

      <label className="scene-appearance-panel__field scene-appearance-panel__field--toggle">
        <span className="scene-appearance-panel__label">启用本地图像</span>
        <input
          type="checkbox"
          checked={appearance.backgroundImageEnabled}
          onChange={(event) => onBackgroundImageEnabledChange(event.target.checked)}
        />
      </label>

      <div className="scene-appearance-panel__field">
        <span className="scene-appearance-panel__label">图像不透明度</span>
        <div className="scene-appearance-panel__range-row">
          <input
            className="scene-appearance-panel__range"
            type="range"
            min={0}
            max={100}
            value={Math.round(appearance.backgroundImageAlpha * 100)}
            onChange={(event) => onBackgroundImageAlphaChange(Number(event.target.value) / 100)}
            disabled={!backgroundImageName}
          />
          <span className="scene-appearance-panel__value">{Math.round(appearance.backgroundImageAlpha * 100)}%</span>
        </div>
      </div>

      <div className="scene-appearance-panel__button-row">
        <button
          type="button"
          className="scene-appearance-panel__button"
          onClick={() => fileInputRef.current?.click()}
        >
          选择图片
        </button>
        <button
          type="button"
          className="scene-appearance-panel__button"
          onClick={() => { void onBackgroundImageCleared() }}
          disabled={!backgroundImageName}
        >
          移除图片
        </button>
        <input
          ref={fileInputRef}
          type="file"
          accept="image/*"
          hidden
          onChange={(event) => {
            const file = event.target.files?.[0]
            if (file) {
              void onBackgroundImageSelected(file)
            }
            event.target.value = ''
          }}
        />
      </div>

      <div className="scene-appearance-panel__hint">
        {backgroundImageLoading
          ? '正在读取已保存图片…'
          : backgroundImageName
            ? `已保存图片：${backgroundImageName}`
            : '未选择背景图片'}
      </div>

      <div className="scene-appearance-panel__section">
        <div className="scene-appearance-panel__section-header">
          <span className="scene-appearance-panel__label">牌背覆盖色</span>
          <button
            type="button"
            className="scene-appearance-panel__ghost-button"
            onClick={onAddTileCoverColor}
          >
            添加颜色
          </button>
        </div>
        <div className="scene-appearance-panel__swatch-list">
          {appearance.tileCoverColors.map((color, index) => (
            <div className="scene-appearance-panel__swatch-row" key={index}>
              <input
                type="color"
                value={color}
                onChange={(event) => onTileCoverColorChange(index, event.target.value)}
              />
              <span className="scene-appearance-panel__swatch-code">{color.toUpperCase()}</span>
              <button
                type="button"
                className="scene-appearance-panel__ghost-button"
                onClick={() => onRemoveTileCoverColor(index)}
                disabled={appearance.tileCoverColors.length <= 1}
              >
                删除
              </button>
            </div>
          ))}
        </div>
      </div>

      <div className="scene-appearance-panel__field">
        <span className="scene-appearance-panel__label">音量</span>
        <div className="scene-appearance-panel__range-row">
          <input
            className="scene-appearance-panel__range"
            type="range"
            min={0}
            max={100}
            value={Math.round(volume * 100)}
            onChange={(event) => onVolumeChange(Number(event.target.value) / 100)}
          />
          <span className="scene-appearance-panel__value">{Math.round(volume * 100)}%</span>
        </div>
      </div>

    </div>
  )
}