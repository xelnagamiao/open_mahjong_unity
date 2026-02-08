using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager : MonoBehaviour {
    /// <summary>
    /// 卡牌从删除位置移动到目标位置的动画
    /// </summary>
    private IEnumerator MoveCardFromRemovePosition(GameObject cardObj, Vector3 targetPosition, Vector3 startPosition) {
        if (startPosition == Vector3.zero) {
            yield break;
        }

        cardObj.transform.position = startPosition;
        yield return null;

        float duration = 0.3f;
        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float smoothT = 1f - Mathf.Pow(1f - t, 3f);
            cardObj.transform.position = Vector3.Lerp(startPosition, targetPosition, smoothT);
            yield return null;
        }
        cardObj.transform.position = targetPosition;
    }

    /// <summary>
    /// 卡牌移动动画：将物体移动鸣牌预备位左侧，然后线性移回原位
    /// </summary>
    private IEnumerator MoveCardAnimation(GameObject targetObj, Vector3 direction, float cardWidth) {
        if (targetObj == null) yield break;

        Vector3 originalPosition = targetObj.transform.position;
        Vector3 targetPosition = originalPosition + direction * (cardWidth * 3f);
        lastRemove3DPosition = originalPosition;

        targetObj.transform.position = targetPosition;
        yield return null;
        yield return new WaitForSeconds(0.1f);

        float duration = 0.15f;
        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            targetObj.transform.position = Vector3.Lerp(targetPosition, originalPosition, t);
            yield return null;
        }
        targetObj.transform.position = originalPosition;
    }

    /// <summary>
    /// 3D卡牌移动动画协程
    /// </summary>
    private IEnumerator Animate3DCardsToPositions(List<Transform> cards, List<Vector3> targetPositions) {
        float animationDuration = 0.3f;
        float elapsedTime = 0f;

        List<Vector3> startPositions = new List<Vector3>();
        for (int i = 0; i < cards.Count; i++) {
            startPositions.Add(cards[i] != null ? cards[i].position : Vector3.zero);
        }

        while (elapsedTime < animationDuration) {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / animationDuration;
            float smoothProgress = 1f - Mathf.Pow(1f - progress, 3f);

            for (int i = 0; i < cards.Count; i++) {
                if (cards[i] != null) {
                    Vector3 currentPos = Vector3.Lerp(startPositions[i], targetPositions[i], smoothProgress);
                    cards[i].position = currentPos;
                }
            }
            yield return null;
        }

        for (int i = 0; i < cards.Count; i++) {
            if (cards[i] != null) {
                cards[i].position = targetPositions[i];
            }
        }
    }
}


