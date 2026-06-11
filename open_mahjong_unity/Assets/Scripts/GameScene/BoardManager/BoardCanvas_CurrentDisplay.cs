using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;


public partial class BoardCanvas {

    public void ResetForExit() {
        CoroutineManager.Instance?.StopNamed(CoroutineKeys.BoardCurrentFlash);
        CoroutineManager.Instance?.StopNamed(CoroutineKeys.BoardScoreDifference);
        isShowingScoreDifference = false;
        RestoreBaselineScores();

        player_self_current_image.gameObject.SetActive(false);
        player_left_current_image.gameObject.SetActive(false);
        player_top_current_image.gameObject.SetActive(false);
        player_right_current_image.gameObject.SetActive(false);
    }

    public void ShowCurrentPlayer(string currentPlayerIndex, int remainTiles){
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

        Color color = targetImage.color;
        color.a = 1f;
        targetImage.color = color;

        CoroutineManager.Ensure();
        CoroutineManager.Instance.RunNamed(
            CoroutineKeys.BoardCurrentFlash,
            FlashImage(targetImage),
            restartIfRunning: true
        );

        remiansTilesText.text = $"余:{remainTiles}";
    }

    private IEnumerator FlashImage(Image image) {
        float cycleDuration = 2.0f;
        float elapsedTime = 0f;

        while (true) {
            float progress = (elapsedTime % cycleDuration) / cycleDuration;
            float pingPongValue = Mathf.PingPong(progress * 2f, 1f);
            float alpha = 1f - pingPongValue;

            Color color = image.color;
            color.a = alpha;
            image.color = color;

            elapsedTime += Time.deltaTime;
            yield return null;
        }
    }
}
