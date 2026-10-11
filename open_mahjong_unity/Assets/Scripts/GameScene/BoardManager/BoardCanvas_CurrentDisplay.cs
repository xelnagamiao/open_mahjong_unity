using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public partial class BoardCanvas {

    string shownCurrentPlayer;
    private bool showingExchangePlayers;

    public bool TryGetExchangePoints(RectTransform target, string seat, out Vector2 center, out Vector2 edge) {
        center = edge = Vector2.zero;
        UnityEngine.UI.Image marker = seat == "self" ? player_self_current_image : seat == "top" ? player_top_current_image
            : seat == "left" ? player_left_current_image : seat == "right" ? player_right_current_image : null;
        if (!target || !marker || !player_self_current_image || !player_top_current_image
            || !player_left_current_image || !player_right_current_image) return false;
        Canvas board = GetComponent<Canvas>();
        Canvas overlay = target.GetComponentInParent<Canvas>()?.rootCanvas;
        if (!board || !overlay) return false;
        Camera fromCamera = board.renderMode == RenderMode.ScreenSpaceOverlay ? null : board.worldCamera ?? Camera.main;
        Camera toCamera = overlay.renderMode == RenderMode.ScreenSpaceOverlay ? null : overlay.worldCamera;
        // 取四边标记的世界中心，避免把整张桌面的 Canvas 边界误当作中心盘边界。
        Vector3 worldCenter = (player_self_current_image.transform.position + player_top_current_image.transform.position
            + player_left_current_image.transform.position + player_right_current_image.transform.position) * .25f;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(target,
                RectTransformUtility.WorldToScreenPoint(fromCamera, worldCenter), toCamera, out center)
            && RectTransformUtility.ScreenPointToLocalPointInRectangle(target,
                RectTransformUtility.WorldToScreenPoint(fromCamera, marker.transform.position), toCamera, out edge);
    }

    public void ShowExchangePlayers(int[] seats, System.Collections.Generic.Dictionary<int, string> positions) {
        ClearExchangePlayers();
        ShowCurrentPlayer(null, TableMirror.Current.RemainTiles);
        CoroutineManager.Instance?.StopNamed(CoroutineKeys.BoardCurrentFlash);
        showingExchangePlayers = true;
        var images = new System.Collections.Generic.List<Image>();
        foreach (int seat in seats ?? System.Array.Empty<int>()) {
            if (positions == null || !positions.TryGetValue(seat, out string position)) continue;
            Image target = position == "self" ? player_self_current_image : position == "left" ? player_left_current_image
                : position == "top" ? player_top_current_image : position == "right" ? player_right_current_image : null;
            if (target == null || images.Contains(target)) continue;
            target.gameObject.SetActive(true);
            images.Add(target);
        }
        CoroutineManager.Ensure();
        CoroutineManager.Instance.RunNamed(CoroutineKeys.BoardCurrentFlash, FlashExchangeImages(images), restartIfRunning: true);
    }

    private IEnumerator FlashExchangeImages(System.Collections.Generic.List<Image> images) {
        float elapsed = 0;
        while (showingExchangePlayers) {
            foreach (Image image in images) if (image) {
                Color color = image.color;
                color.a = CurrentPlayerAlpha(elapsed);
                image.color = color;
            }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    public void ClearExchangePlayers() {
        if (!showingExchangePlayers) return;
        showingExchangePlayers = false;
        shownCurrentPlayer = null;
        CoroutineManager.Instance?.StopNamed(CoroutineKeys.BoardCurrentFlash);
        player_self_current_image.gameObject.SetActive(false);
        player_left_current_image.gameObject.SetActive(false);
        player_top_current_image.gameObject.SetActive(false);
        player_right_current_image.gameObject.SetActive(false);
    }

    public void ResetForExit() {
        ClearExchangePlayers();
        CoroutineManager.Instance?.StopNamed(CoroutineKeys.BoardCurrentFlash);
        CoroutineManager.Instance?.StopNamed(CoroutineKeys.BoardScoreDifference);
        isShowingScoreDifference = false;
        RestoreBaselineScores();
        shownCurrentPlayer = null;

        player_self_current_image.gameObject.SetActive(false);
        player_left_current_image.gameObject.SetActive(false);
        player_top_current_image.gameObject.SetActive(false);
        player_right_current_image.gameObject.SetActive(false);
        ApplyOccupiedSeatClusters(null);
    }

    public void ShowCurrentPlayer(string currentPlayerIndex, int remainTiles){
        ClearExchangePlayers();
        remiansTilesText.text = $"余:{remainTiles}";
        if (currentPlayerIndex == shownCurrentPlayer) return;
        shownCurrentPlayer = currentPlayerIndex;

        CoroutineManager.Instance?.StopNamed(CoroutineKeys.BoardCurrentFlash);
        player_self_current_image.gameObject.SetActive(false);
        player_left_current_image.gameObject.SetActive(false);
        player_top_current_image.gameObject.SetActive(false);
        player_right_current_image.gameObject.SetActive(false);

        Image targetImage = null;
        if (currentPlayerIndex == "self"){
            player_self_current_image.gameObject.SetActive(true);
            targetImage = player_self_current_image;
        } else if (currentPlayerIndex == "left"){
            player_left_current_image.gameObject.SetActive(true);
            targetImage = player_left_current_image;
        } else if (currentPlayerIndex == "top"){
            player_top_current_image.gameObject.SetActive(true);
            targetImage = player_top_current_image;
        } else if (currentPlayerIndex == "right"){
            player_right_current_image.gameObject.SetActive(true);
            targetImage = player_right_current_image;
        }

        if (targetImage == null) return;

        Color color = targetImage.color;
        color.a = 1f;
        targetImage.color = color;

        CoroutineManager.Ensure();
        CoroutineManager.Instance?.RunNamed(
            CoroutineKeys.BoardCurrentFlash,
            FlashImage(targetImage),
            restartIfRunning: true
        );
    }

    private static float CurrentPlayerAlpha(float elapsedTime) {
        // A two-second breath with a visible minimum and smooth turning points.
        return Mathf.Lerp(.35f, 1f, .5f + .5f * Mathf.Cos(elapsedTime * Mathf.PI));
    }

    private IEnumerator FlashImage(Image image) {
        float elapsedTime = 0f;

        while (image && image.gameObject.activeSelf) {
            Color color = image.color;
            color.a = CurrentPlayerAlpha(elapsedTime);
            image.color = color;

            elapsedTime += Time.deltaTime;
            yield return null;
        }
    }
}
