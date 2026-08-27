import { calculateHongquePaili } from '../game2d/calc/hongque'

interface WorkerRequest {
  id: number
  hand: string[]
}

self.onmessage = (event: MessageEvent<WorkerRequest>) => {
  const { id, hand } = event.data
  try {
    self.postMessage({ id, result: calculateHongquePaili(hand) })
  } catch (error) {
    self.postMessage({
      id,
      error: error instanceof Error ? error.message : String(error),
    })
  }
}
