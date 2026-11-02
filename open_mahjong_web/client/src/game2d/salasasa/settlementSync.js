/** Server-owned result identity, acknowledgements and monotonic countdown. */
export class SettlementSync {
  constructor(now = () => performance.now()) {
    this.now = now
    this.current = null
    this.deadline = null
    this.presented = new Set()
  }

  accept(sync, snapshot = false, receivedAt = this.now()) {
    if (!sync) return true // Legacy servers and local replay.
    const previous = this.current
    const sameGame = previous?.gamestate_id === sync.gamestate_id
    if (!snapshot && previous && (!sameGame || sync.sequence < previous.sequence
      || (sync.round_id !== previous.round_id && sync.sequence <= previous.sequence)
      || (sync.sequence === previous.sequence && sync.revision < previous.revision))) return false
    if (!sameGame || previous?.round_id !== sync.round_id) this.presented.clear()
    const sameResult = sameGame && previous?.result_id === sync.result_id
    const deadline = sync.remaining_ms == null ? null : receivedAt + Math.max(0, sync.remaining_ms)
    // Another ready acknowledgement must never restart or lengthen this clock.
    this.deadline = sameResult && this.deadline != null && deadline != null
      ? Math.min(this.deadline, deadline) : deadline
    this.current = sync
    return true
  }

  present(sync, restoring = false) {
    if (!sync?.result_id) return true
    if (!this.accept(sync)) return false
    if (!restoring && this.presented.has(sync.result_id)) return false
    this.presented.add(sync.result_id)
    return true
  }

  remainingSeconds() {
    return this.deadline == null ? null : Math.max(0, Math.ceil((this.deadline - this.now()) / 1000))
  }
}
