using System.Collections;
using UnityEngine;

public partial class Game3DManager : MonoBehaviour {
    // 摸牌3D显示（协程版本）
    private IEnumerator Get3DTileCoroutine(string playerIndex, string actionType) {
        if (playerIndex == "self") yield break;
        PosPanel3D panel = GetPosPanel(playerIndex);
        Transform cardsPosition = panel.cardsPosition;

        Quaternion rotation = Quaternion.identity;
        Vector3 direction = Vector3.zero;

        if (playerIndex == "left") {
            rotation = Quaternion.Euler(0, 90, 0); // 面朝左侧
            direction = BackDirection; // 向后
        }
        else if (playerIndex == "top") {
            rotation = Quaternion.Euler(0, 180, 0); // 面朝前侧
            direction = LeftDirection; // 向左
        }
        else if (playerIndex == "right") {
            rotation = Quaternion.Euler(0, 270, 0); // 面朝右侧
            direction = FrontDirection; // 向前
        }
        else {
            Debug.LogWarning($"未知的玩家位置: {playerIndex}");
            yield break;
        }

        // 初始化牌生成位置 = 玩家手牌起始点 + (3D卡牌数量)*宽度间距*方向
        Vector3 spawnPosition = Vector3.zero;
        if (actionType == "init") {
            spawnPosition = cardsPosition.position + (cardsPosition.childCount) * handStep * direction;
        }
        // 摸牌生成位置 = 玩家手牌起始点 + (3D卡牌数量+1)*宽度间距*方向
        else if (actionType == "get") {
            spawnPosition = cardsPosition.position + HandDrawSlotOffset(cardsPosition.childCount) * direction;
        }

        // 等待一帧，避免与其他操作在同一帧执行
        yield return null;

        spawnPosition = PlaceTileOnTable(spawnPosition, rotation);
        GameObject cardObj = MahjongObjectPool.Instance.SpawnBlankTile(spawnPosition, rotation);
        if (cardObj == null) {
            Debug.LogError("无法从对象池获取3D手牌");
            yield break;
        }
        cardObj.transform.SetParent(cardsPosition, worldPositionStays: true);
    }

    // 摸牌3D显示
    private void Get3DTile(string playerIndex, string actionType) {
        if (playerIndex == "self") return;
        PosPanel3D panel = GetPosPanel(playerIndex);
        Transform cardsPosition = panel.cardsPosition;

        Quaternion rotation = Quaternion.identity;
        Vector3 direction = Vector3.zero;

        if (playerIndex == "left") {
            rotation = Quaternion.Euler(0, 90, 0); // 面朝左侧
            direction = BackDirection; // 向后
        }
        else if (playerIndex == "top") {
            rotation = Quaternion.Euler(0, 180, 0); // 面朝前侧
            direction = LeftDirection; // 向左
        }
        else if (playerIndex == "right") {
            rotation = Quaternion.Euler(0, 270, 0); // 面朝右侧
            direction = FrontDirection; // 向前
        }
        else {
            Debug.LogWarning($"未知的玩家位置: {playerIndex}");
            return;
        }

        // 初始化牌生成位置 = 玩家手牌起始点 + (3D卡牌数量)*宽度间距*方向
        Vector3 spawnPosition = Vector3.zero;
        if (actionType == "init") {
            spawnPosition = cardsPosition.position + (cardsPosition.childCount) * handStep * direction;
        }
        // 摸牌生成位置 = 玩家手牌起始点 + (3D卡牌数量+1)*宽度间距*方向
        else if (actionType == "get") {
            spawnPosition = cardsPosition.position + HandDrawSlotOffset(cardsPosition.childCount) * direction;
        }

        spawnPosition = PlaceTileOnTable(spawnPosition, rotation);
        GameObject cardObj = MahjongObjectPool.Instance.SpawnBlankTile(spawnPosition, rotation);
        if (cardObj == null) {
            Debug.LogError("无法从对象池获取3D手牌");
            return;
        }
        cardObj.transform.SetParent(cardsPosition, worldPositionStays: true);
    }
}
