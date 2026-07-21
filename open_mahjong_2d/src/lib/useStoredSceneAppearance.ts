import { useEffect, useState } from 'react'

import {
  DEFAULT_SCENE_APPEARANCE,
  normalizeSceneAppearanceSettings,
  type SceneAppearanceSettings,
} from './sceneAppearance'
import {
  clearStoredSceneBackgroundImage,
  loadStoredSceneBackgroundImage,
  saveStoredSceneBackgroundImage,
  type StoredSceneBackgroundImage,
} from './sceneBackgroundImage'
import {
  loadStoredSceneAppearance,
  resetStoredSceneAppearance,
  saveStoredSceneAppearance,
} from './storage'

type SceneAppearanceUpdater = (current: SceneAppearanceSettings) => SceneAppearanceSettings

export function useStoredSceneAppearance() {
  const [appearance, setAppearance] = useState<SceneAppearanceSettings>(() => loadStoredSceneAppearance())
  const [backgroundImage, setBackgroundImage] = useState<StoredSceneBackgroundImage | null>(null)
  const [backgroundImageLoading, setBackgroundImageLoading] = useState(true)

  useEffect(() => {
    let cancelled = false

    loadStoredSceneBackgroundImage()
      .then((image) => {
        if (!cancelled) {
          setBackgroundImage(image)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setBackgroundImage(null)
        }
      })
      .finally(() => {
        if (!cancelled) {
          setBackgroundImageLoading(false)
        }
      })

    return () => {
      cancelled = true
    }
  }, [])

  const updateAppearance = (updater: SceneAppearanceUpdater) => {
    setAppearance((current) => {
      const next = normalizeSceneAppearanceSettings(updater(current))
      saveStoredSceneAppearance(next)
      return next
    })
  }

  const setBackgroundColorTable = (backgroundColorTable: string) => {
    updateAppearance((current) => ({ ...current, backgroundColorTable }))
  }

  const setBackgroundColorOutside = (backgroundColorOutside: string) => {
    updateAppearance((current) => ({ ...current, backgroundColorOutside }))
  }

  const setBackgroundImageEnabled = (backgroundImageEnabled: boolean) => {
    updateAppearance((current) => ({ ...current, backgroundImageEnabled }))
  }

  const setBackgroundImageAlpha = (backgroundImageAlpha: number) => {
    updateAppearance((current) => ({ ...current, backgroundImageAlpha }))
  }

  const setTileCoverColor = (index: number, color: string) => {
    updateAppearance((current) => {
      const nextColors = [...current.tileCoverColors]
      nextColors[index] = color
      return { ...current, tileCoverColors: nextColors }
    })
  }

  const addTileCoverColor = () => {
    updateAppearance((current) => ({
      ...current,
      tileCoverColors: [...current.tileCoverColors, current.tileCoverColors[current.tileCoverColors.length - 1] ?? '#f6bc1e'],
    }))
  }

  const removeTileCoverColor = (index: number) => {
    updateAppearance((current) => {
      if (current.tileCoverColors.length <= 1) {
        return current
      }
      return {
        ...current,
        tileCoverColors: current.tileCoverColors.filter((_, colorIndex) => colorIndex !== index),
      }
    })
  }

  const uploadBackgroundImage = async (file: File) => {
    const stored = await saveStoredSceneBackgroundImage(file)
    setBackgroundImage(stored)
    updateAppearance((current) => ({ ...current, backgroundImageEnabled: true }))
  }

  const clearBackgroundImage = async () => {
    await clearStoredSceneBackgroundImage()
    setBackgroundImage(null)
    updateAppearance((current) => ({ ...current, backgroundImageEnabled: false }))
  }

  const resetAppearance = async () => {
    resetStoredSceneAppearance()
    await clearStoredSceneBackgroundImage()
    setAppearance(DEFAULT_SCENE_APPEARANCE)
    setBackgroundImage(null)
  }

  return {
    appearance,
    backgroundImage,
    backgroundImageLoading,
    setBackgroundColorTable,
    setBackgroundColorOutside,
    setBackgroundImageEnabled,
    setBackgroundImageAlpha,
    setTileCoverColor,
    addTileCoverColor,
    removeTileCoverColor,
    uploadBackgroundImage,
    clearBackgroundImage,
    resetAppearance,
  }
}