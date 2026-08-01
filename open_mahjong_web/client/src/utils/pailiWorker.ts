import { calculatePaili, type PailiRequest } from "./pailiCalculator";

interface WorkerRequest {
  id: number;
  payload: PailiRequest;
}

self.onmessage = (event: MessageEvent<WorkerRequest>) => {
  const { id, payload } = event.data;
  try {
    self.postMessage({ id, result: calculatePaili(payload) });
  } catch (error) {
    self.postMessage({
      id,
      error: error instanceof Error ? error.message : String(error),
    });
  }
};
