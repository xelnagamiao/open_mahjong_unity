import { useUnifiedCalculator as useCalculator } from '../../composables/useUnifiedCalculator.js'
export { initialState, validateState, encodeState, decodeState } from '../../composables/useUnifiedCalculator.js'

export const useUnifiedCalculator = () => useCalculator({
  storageKey: 'salasasa.unified-calculator.prototype.v1',
  fallbackStorageKeys: [],
})
