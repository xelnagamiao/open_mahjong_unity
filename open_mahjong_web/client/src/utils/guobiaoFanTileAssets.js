// Unity PlayerInfoHandView overlays the hand face and backing on the same canvas.
// These original assets must not use the calibrated table pack's inset or scaling.
export function guobiaoFanTileAssets(tileId, faceDown = false, baseUrl = '/') {
  const root = `${baseUrl}game2d-assets/textures/guobiao-mahjong-tiles`
  return {
    background: `${root}/surfaces/${faceDown ? 'back' : 'front'}.png`,
    face: faceDown ? null : `${root}/hand/${tileId}.png`,
  }
}
