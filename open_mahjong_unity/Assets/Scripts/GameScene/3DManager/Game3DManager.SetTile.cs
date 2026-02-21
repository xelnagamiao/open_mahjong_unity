using System.Collections;
using UnityEngine;

public partial class Game3DManager : MonoBehaviour
{
    // 放置3D卡牌 id-位置-类型-玩家位置（协程版本）
    private IEnumerator Set3DTileCoroutine(int tileId, Transform SetPosition, string SetType, string PlayerPosition)
    {
        Debug.Log($"Set3DTileCoroutine:{tileId} {SetType}, {PlayerPosition}");

        // 获取放置位置
        Vector3 currentPosition = SetPosition.position;
        // 获取放置角度
        Quaternion rotation = Quaternion.identity;

        // 获取不同玩家的相对右侧和相对下侧
        Vector3 widthdirection = Vector3.zero;
        Vector3 heightdirection = Vector3.zero;
        if (PlayerPosition == "self")
        {
            widthdirection = RightDirection;
            heightdirection = BackDirection;
            rotation = Quaternion.Euler(90, 0, 180);
        }
        else if (PlayerPosition == "left")
        {
            widthdirection = BackDirection;
            heightdirection = LeftDirection;
            rotation = Quaternion.Euler(90, 0, 90);
        }
        else if (PlayerPosition == "top")
        {
            widthdirection = LeftDirection;
            heightdirection = FrontDirection;
            rotation = Quaternion.Euler(90, 0, 0);
        }
        else if (PlayerPosition == "right")
        {
            widthdirection = FrontDirection;
            heightdirection = RightDirection;
            rotation = Quaternion.Euler(90, 0, 270);
        }

        bool isRecordSet = SetType == "Record";
        // 添加随机的 z 轴旋转（正负3度），模拟手牌排列的自然效果
        if (!isRecordSet)
        {
            Vector3 euler = rotation.eulerAngles;
            euler.z += Random.Range(-3f, 3f);
            rotation = Quaternion.Euler(euler);
        }

        // 获取每行最多放多少张牌
        int cardsPerRow;
        // 弃牌每行最多放 6 张牌
        if (SetType == "Discard")
        {
            cardsPerRow = 6;
        }
        // 补花每行最多放 4 张牌
        else if (SetType == "Buhua")
        {
            cardsPerRow = 4;
        }
        else
        {
            Debug.LogError($"SetType {SetType} 错误");
            cardsPerRow = 10; // 默认值
        }

        // 将 discardCount 映射到从 0 开始的索引（第1张是0，第6张是5，第7张是6 → 行1列0）
        int index = SetPosition.childCount;
        // 计算当前是第几行、第几列
        int row = index / cardsPerRow;     // 整除：行号
        int col = index % cardsPerRow;     // 取余：列号

        currentPosition += widthdirection.normalized * widthSpacing * col;
        currentPosition += heightdirection.normalized * heightSpacing * row;


        Debug.Log($"创建卡片 {SetPosition.childCount}, 牌ID: {tileId}");
        // 从对象池获取麻将牌
        GameObject cardObj = MahjongObjectPool.Instance.Spawn(tileId, currentPosition, rotation);
        if (cardObj == null) {
            Debug.LogError($"无法从对象池获取牌: {tileId}");
            yield break;
        }
        if (SetType == "Discard") {
            lastCut3DObject = cardObj;
        }
        // 设置父对象
        cardObj.transform.SetParent(SetPosition, worldPositionStays: true);
        cardObj.name = $"Card_{SetPosition.childCount}";

        // 注册到悬停管理器
        if (Card3DHoverManager.Instance != null)
        {
            Card3DHoverManager.Instance.RegisterCard(cardObj, tileId);
        }

        // 启动移动动画：先移动到最后删除的位置，然后移动到目标位置
        // 如果手牌者是自己，使用selfPosPanel的cardsPosition容器本身的位置
        Vector3 startPosition = lastRemove3DPosition;
        if (PlayerPosition == "self")
        {
            startPosition = selfPosPanel.cardsPosition.position;
        }

        yield return StartCoroutine(MoveCardFromRemovePosition(cardObj, currentPosition, startPosition));
    }
    
    // 放置3D卡牌 id-位置-类型-玩家位置
    private void Set3DTile(int tileId, Transform SetPosition, string SetType, string PlayerPosition)
    {
        Debug.Log($"Set3DTile:{tileId} {SetType}, {PlayerPosition}");

        // 获取放置位置
        Vector3 currentPosition = SetPosition.position;
        // 获取放置角度
        Quaternion rotation = Quaternion.identity;

        // 获取不同玩家的相对右侧和相对下侧
        Vector3 widthdirection = Vector3.zero;
        Vector3 heightdirection = Vector3.zero;
        if (PlayerPosition == "self")
        {
            widthdirection = RightDirection;
            heightdirection = BackDirection;
            rotation = Quaternion.Euler(90, 0, 180);
        }
        else if (PlayerPosition == "left")
        {
            widthdirection = BackDirection;
            heightdirection = LeftDirection;
            rotation = Quaternion.Euler(90, 0, 90);
        }
        else if (PlayerPosition == "top")
        {
            widthdirection = LeftDirection;
            heightdirection = FrontDirection;
            rotation = Quaternion.Euler(90, 0, 0);
        }
        else if (PlayerPosition == "right")
        {
            widthdirection = FrontDirection;
            heightdirection = RightDirection;
            rotation = Quaternion.Euler(90, 0, 270);
        }

        bool isRecordSet = SetType == "Record";
        // 添加随机的 z 轴旋转（正负3度），模拟手牌排列的自然效果
        if (!isRecordSet)
        {
            Vector3 euler = rotation.eulerAngles;
            euler.z += Random.Range(-3f, 3f);
            rotation = Quaternion.Euler(euler);
        }

        // 获取每行最多放多少张牌
        int cardsPerRow;
        // 弃牌每行最多放 6 张牌
        if (SetType == "Discard" || SetType == "DiscardWithoutAnimation")
        {
            cardsPerRow = 6;
        }
        // 补花每行最多放 4 张牌
        else if (SetType == "Buhua" || SetType == "BuhuaWithoutAnimation")
        {
            cardsPerRow = 4;
        }
        else if (SetType == "Record")
        {
            cardsPerRow = 14;
        }
        else
        {
            Debug.LogError($"SetType {SetType} 错误");
            cardsPerRow = 10; // 默认值
        }

        // 将 discardCount 映射到从 0 开始的索引（第1张是0，第6张是5，第7张是6 → 行1列0）
        int index = SetPosition.childCount;
        // 计算当前是第几行、第几列
        int row = index / cardsPerRow;     // 整除：行号
        int col = index % cardsPerRow;     // 取余：列号

        currentPosition += widthdirection.normalized * widthSpacing * col;
        currentPosition += heightdirection.normalized * heightSpacing * row;

        Debug.Log($"创建卡片 {SetPosition.childCount}, 牌ID: {tileId}");
        // 从对象池获取麻将牌
        GameObject cardObj = MahjongObjectPool.Instance.Spawn(tileId, currentPosition, rotation);
        if (cardObj == null) {
            Debug.LogError($"无法从对象池获取牌: {tileId}");
            return;
        }
        if (SetType == "Discard") {
            lastCut3DObject = cardObj;
        }
        // 设置父对象
        cardObj.transform.SetParent(SetPosition, worldPositionStays: true);
        cardObj.name = $"Card_{SetPosition.childCount}";

        // 注册到悬停管理器
        if (Card3DHoverManager.Instance != null)
        {
            Card3DHoverManager.Instance.RegisterCard(cardObj, tileId);
        }

        if (isRecordSet)
        {
            cardObj.transform.position = currentPosition;
            return;
        }

        // 启动移动动画：先移动到最后删除的位置，然后移动到目标位置
        // 如果手牌者是自己，使用selfPosPanel的cardsPosition容器本身的位置
        Vector3 startPosition = lastRemove3DPosition;
        if (PlayerPosition == "self")
        {
            startPosition = selfPosPanel.cardsPosition.position;
        }
        if (SetType == "DiscardWithoutAnimation" || SetType == "BuhuaWithoutAnimation")
        {
            return;
        }
        else{
            StartCoroutine(MoveCardFromRemovePosition(cardObj, currentPosition, startPosition));
        }

    }
}
