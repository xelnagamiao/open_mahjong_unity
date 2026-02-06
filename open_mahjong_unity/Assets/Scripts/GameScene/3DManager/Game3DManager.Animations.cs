using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager : MonoBehaviour
{
    // 卡牌从删除位置移动到目标位置的动画
    private IEnumerator MoveCardFromRemovePosition(GameObject cardObj, Vector3 targetPosition, Vector3 startPosition)
    {
        // 如果起始位置未设置，直接跳过动画
        if (startPosition == Vector3.zero)
        {
            yield break;
        }

        // 先将卡牌移动到起始位置
        cardObj.transform.position = startPosition;

        // 等待一帧
        yield return null;

        // 从起始位置移动到目标位置的动画
        float duration = 0.3f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            // 使用平滑插值
            float smoothT = 1f - Mathf.Pow(1f - t, 3f);
            cardObj.transform.position = Vector3.Lerp(startPosition, targetPosition, smoothT);
            yield return null;
        }

        // 确保最终位置准确
        cardObj.transform.position = targetPosition;
    }

    // 卡牌移动动画：将物体移动鸣牌预备位左侧，然后线性移回原位
    private IEnumerator MoveCardAnimation(GameObject targetObj, Vector3 direction, float cardWidth)
    {
        if (targetObj == null) yield break;

        // 左方向
        Vector3 leftDirection = direction;

        // 计算目标位置（3个卡牌宽度以左）
        Vector3 originalPosition = targetObj.transform.position;
        Vector3 targetPosition = originalPosition + leftDirection * (cardWidth * 3f);

        // 保存最后一张删牌的位置
        lastRemove3DPosition = originalPosition;

        // 先移动到左侧位置
        targetObj.transform.position = targetPosition;

        // 等待一帧，确保卡牌已创建
        yield return null;

        // 在原地停顿0.1秒
        yield return new WaitForSeconds(0.1f);

        // 在0.15秒内线性移回原位
        float duration = 0.15f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            targetObj.transform.position = Vector3.Lerp(targetPosition, originalPosition, t);
            yield return null;
        }

        // 确保最终位置准确
        targetObj.transform.position = originalPosition;
    }

    // 3D卡牌移动动画协程
    private IEnumerator Animate3DCardsToPositions(List<Transform> cards, List<Vector3> targetPositions)
    {
        float animationDuration = 0.3f; // 动画持续时间
        float elapsedTime = 0f;

        // 记录起始位置
        List<Vector3> startPositions = new List<Vector3>();
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
            {
                startPositions.Add(cards[i].position);
            }
            else
            {
                startPositions.Add(Vector3.zero);
            }
        }

        // 动画循环
        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / animationDuration;

            // 使用平滑插值函数（easeOutCubic）
            float smoothProgress = 1f - Mathf.Pow(1f - progress, 3f);

            // 更新每张卡牌的位置
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] != null)
                {
                    Vector3 currentPos = Vector3.Lerp(startPositions[i], targetPositions[i], smoothProgress);
                    cards[i].position = currentPos;
                }
            }

            yield return null; // 等待下一帧
        }

        // 确保最终位置准确
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
            {
                cards[i].position = targetPositions[i];
            }
        }
    }
}


