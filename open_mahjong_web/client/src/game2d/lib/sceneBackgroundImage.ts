export type StoredSceneBackgroundImage = {
  dataUrl: string
  name: string
  type: string
  updatedAt: number
}

const DB_NAME = 'mmcr14.sceneAssets'
const STORE_NAME = 'sceneAppearance'
const BACKGROUND_IMAGE_KEY = 'backgroundImage'

function openSceneAssetsDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    if (typeof indexedDB === 'undefined') {
      reject(new Error('IndexedDB is unavailable'))
      return
    }

    const request = indexedDB.open(DB_NAME, 1)
    request.onupgradeneeded = () => {
      const db = request.result
      if (!db.objectStoreNames.contains(STORE_NAME)) {
        db.createObjectStore(STORE_NAME)
      }
    }
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error ?? new Error('Failed to open IndexedDB'))
  })
}

function readFileAsDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => {
      if (typeof reader.result === 'string') {
        resolve(reader.result)
        return
      }
      reject(new Error('Failed to read image file'))
    }
    reader.onerror = () => reject(reader.error ?? new Error('Failed to read image file'))
    reader.readAsDataURL(file)
  })
}

export async function loadStoredSceneBackgroundImage(): Promise<StoredSceneBackgroundImage | null> {
  const db = await openSceneAssetsDb()
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, 'readonly')
    const request = tx.objectStore(STORE_NAME).get(BACKGROUND_IMAGE_KEY)

    request.onsuccess = () => {
      resolve((request.result as StoredSceneBackgroundImage | undefined) ?? null)
    }
    request.onerror = () => reject(request.error ?? new Error('Failed to load background image'))
    tx.oncomplete = () => db.close()
    tx.onerror = () => {
      db.close()
      reject(tx.error ?? new Error('Failed to load background image'))
    }
    tx.onabort = () => {
      db.close()
      reject(tx.error ?? new Error('Failed to load background image'))
    }
  })
}

export async function saveStoredSceneBackgroundImage(file: File): Promise<StoredSceneBackgroundImage> {
  const dataUrl = await readFileAsDataUrl(file)
  const entry: StoredSceneBackgroundImage = {
    dataUrl,
    name: file.name,
    type: file.type,
    updatedAt: Date.now(),
  }

  const db = await openSceneAssetsDb()
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, 'readwrite')
    const request = tx.objectStore(STORE_NAME).put(entry, BACKGROUND_IMAGE_KEY)

    request.onsuccess = () => resolve(entry)
    request.onerror = () => reject(request.error ?? new Error('Failed to save background image'))
    tx.oncomplete = () => db.close()
    tx.onerror = () => {
      db.close()
      reject(tx.error ?? new Error('Failed to save background image'))
    }
    tx.onabort = () => {
      db.close()
      reject(tx.error ?? new Error('Failed to save background image'))
    }
  })
}

export async function clearStoredSceneBackgroundImage(): Promise<void> {
  const db = await openSceneAssetsDb()
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, 'readwrite')
    const request = tx.objectStore(STORE_NAME).delete(BACKGROUND_IMAGE_KEY)

    request.onsuccess = () => resolve()
    request.onerror = () => reject(request.error ?? new Error('Failed to clear background image'))
    tx.oncomplete = () => db.close()
    tx.onerror = () => {
      db.close()
      reject(tx.error ?? new Error('Failed to clear background image'))
    }
    tx.onabort = () => {
      db.close()
      reject(tx.error ?? new Error('Failed to clear background image'))
    }
  })
}