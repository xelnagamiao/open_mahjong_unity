// GameCanvas 手牌处理

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class GameCanvas{
    
    // 手牌处理
    public void ChangeHandCards(string ChangeType,int tileId,int[] TilesList,int? cut_tile_index){
        // 将手牌处理任务加入队列
        changeHandCardQueue.Enqueue(() => {
            return StartCoroutine(ChangeHandCardsCoroutine(ChangeType,tileId,TilesList,cut_tile_index));
        });
        // 未启动执行队列则启动
        if (!isChangeHandCardProcessing){
            StartCoroutine(ProcessChangeHandCardQueue());
        }
    }
    
    // 处理手牌队列
    private IEnumerator ProcessChangeHandCardQueue(){
        // 手牌处理运行
        isChangeHandCardProcessing = true;
        while (changeHandCardQueue.Count > 0){
            // 拿取下一个任务
            System.Func<Coroutine> changeHandCardAction = changeHandCardQueue.Dequeue();
            // 执行任务
            Coroutine changeHandCardCoroutine = changeHandCardAction.Invoke();
            // 等待任务完成
            yield return changeHandCardCoroutine;
        }
        // 手牌处理结束
        isChangeHandCardProcessing = false;
    }
    
    // 手牌处理 
    public IEnumerator ChangeHandCardsCoroutine(string ChangeType,int tileId,int[] TilesList,int? cut_tile_index){

        Debug.Log($"手牌处理: {ChangeType}");

        // 初始化手牌 只初始化前13张
        if (ChangeType == "InitHandCards"){
            int cardCount = 0;
            foreach (int tile in TilesList){
                if (cardCount >= 13) break;
                // 创建手牌
                GameObject cardObj = Instantiate(tileCardPrefab, handCardsContainer);
                TileCard tileCard = cardObj.GetComponent<TileCard>();
                tileCard.SetTile(tile, false);
                // 设置位置：每张牌间隔一个宽度
                RectTransform cardRect = cardObj.GetComponent<RectTransform>();
                cardRect.anchoredPosition = new Vector2(cardCount * tileCardWidth, 0);
                // 计数增加
                cardCount++;
            }
        }

        // 摸牌 添加摸牌区手牌
        else if (ChangeType == "GetCard"){
            GameObject cardObj = Instantiate(tileCardPrefab, handCardsContainer);
            TileCard tileCard = cardObj.GetComponent<TileCard>();
            tileCard.SetTile(tileId, true);
            // 设置位置：手牌区右侧，间隔半个宽度
            RectTransform cardRect = cardObj.GetComponent<RectTransform>();
            int handCardCount = handCardsContainer.childCount - 1; // 减去刚添加的这张
            Vector2 targetPosition = new Vector2(handCardCount * tileCardWidth + tileCardWidth * 0.5f, 0);
            
            // 摸牌动画：先移动到上方2个宽度，然后向下滑动到原始位置，同时透明度从100到0
            yield return StartCoroutine(AnimateGetCard(cardRect, targetPosition));
        }

            // 摸牌 添加摸牌区手牌
        else if (ChangeType == "GetCardNoAnimation"){
            GameObject cardObj = Instantiate(tileCardPrefab, handCardsContainer);
            TileCard tileCard = cardObj.GetComponent<TileCard>();
            tileCard.SetTile(tileId, true);
            // 设置位置：手牌区右侧，间隔半个宽度
            RectTransform cardRect = cardObj.GetComponent<RectTransform>();
            int handCardCount = handCardsContainer.childCount - 1; // 减去刚添加的这张
            cardRect.anchoredPosition = new Vector2(handCardCount * tileCardWidth + tileCardWidth * 0.5f, 0);
        }

        // 摸切 删除摸牌区手牌
        else if (ChangeType == "RemoveGetCard"){
            // 删除最后添加的牌（摸牌区的牌）
            Transform lastCard = handCardsContainer.GetChild(handCardsContainer.childCount - 1);
            TileCard tileCard = lastCard.GetComponent<TileCard>();
            if (tileCard.tileId == tileId){
                Destroyer.Instance.AddToDestroyer(lastCard);
            }
        }

        // 手切 删除手牌区手牌
        else if (ChangeType == "RemoveHandCard"){
            bool isRemove = false;
            foreach (Transform child in handCardsContainer){
                if (child.GetSiblingIndex() == cut_tile_index.Value){
                    Destroyer.Instance.AddToDestroyer(child);
                    isRemove = true;
                    break;
                }
            }
            if (!isRemove){
                foreach (Transform child in handCardsContainer){
                TileCard needToRemoveTileCard = child.GetComponent<TileCard>();
                if (needToRemoveTileCard.tileId == tileId){
                    Destroyer.Instance.AddToDestroyer(child);
                    }
                }
            }
        }

        // 补花 删除手牌区手牌
        else if (ChangeType == "RemoveBuhuaCard"){
            foreach (Transform child in handCardsContainer){
                TileCard needToRemoveTileCard = child.GetComponent<TileCard>();
                if (needToRemoveTileCard.tileId == tileId){
                    // 如果补花牌是摸牌区手牌 设置ChangeType = "RemoveGetCard" 代表后续不用执行动画
                    if (needToRemoveTileCard.currentGetTile){
                        ChangeType = "RemoveBuhuaGetCard";
                    }
                    Destroyer.Instance.AddToDestroyer(child);
                }
            }
        }

        // 加杠 删除手牌区手牌
        else if (ChangeType == "RemoveJiagangCard"){
            foreach (Transform child in handCardsContainer){
                TileCard needToRemoveTileCard = child.GetComponent<TileCard>();
                if (needToRemoveTileCard.tileId == tileId){
                    Destroyer.Instance.AddToDestroyer(child);
                }
            }
        }

        // 删除组合牌 在手牌中删除全部组合牌
        else if (ChangeType == "RemoveCombinationCard"){
            foreach (int tileToRemove in TilesList){
                foreach (Transform child in handCardsContainer){
                    TileCard needToRemoveTileCard = child.GetComponent<TileCard>();
                    if (needToRemoveTileCard.tileId == tileToRemove){
                        Destroyer.Instance.AddToDestroyer(child);
                        break;
                    }
                }
            }
        }

        else if (ChangeType == "ReSetHandCards"){
            // 标志回合结束 只在自己其他人回合开始时调用 用以在未排序的情况下排序手牌
            if (isArranged){yield break;} // 如果手牌已经排序过 则不进行排序
        }

        // isArrangeed 用于监测玩家这次行为是否已经排序 例如补花和摸牌以后还可以执行操作,但是摸切和手切以后就不能执行操作了

        // 初始化庄家手牌、摸牌、补花以后 均执行GetCard 重置排序状态
        if (ChangeType == "GetCard" ){
            isArranged = false;
        }

        // 如果切牌是摸切或者补花摸牌张 则不需要排序
        else if (ChangeType == "RemoveGetCard" || ChangeType == "RemoveBuhuaGetCard"){
            isArranged = true;
            yield break;
        }

        // 初始化卡牌、摸切、手切、单次补花、吃碰杠以后 进行卡牌排序 
        else if (ChangeType == "RemoveHandCard" || ChangeType == "RemoveCombinationCard" || ChangeType == "RemoveBuhuaCard" || ChangeType == "RemoveJiagangCard" || ChangeType == "InitHandCards" || ChangeType == "ReSetHandCards"){
            isArranged = true;
            // 等待排序完成
            yield return StartCoroutine(RearrangeHandCardsWithAnimation());
        }
    }
      
    // 带动画的手牌重新排列
    private IEnumerator RearrangeHandCardsWithAnimation(){

        Debug.Log($"手牌重新排列");

        // 手牌恢复为非摸切状态
        for (int i = 0; i < handCardsContainer.childCount; i++){
            Transform child = handCardsContainer.GetChild(i);
            TileCard tileCard = child.GetComponent<TileCard>();
            if (tileCard != null){
                tileCard.currentGetTile = false;
            }
        }

        // 创建字典：手牌物体 -> 当前位置
        Dictionary<RectTransform, Vector2> currentPositions = new Dictionary<RectTransform, Vector2>();
        // 创建字典：手牌物体 -> 目标位置
        Dictionary<RectTransform, Vector2> targetPositions = new Dictionary<RectTransform, Vector2>();
        
        // 收集所有手牌物体和当前位置
        List<TileCard> tileCards = new List<TileCard>();
        for (int i = 0; i < handCardsContainer.childCount; i++){
            Transform child = handCardsContainer.GetChild(i);
            TileCard tileCard = child.GetComponent<TileCard>();
            if (tileCard != null) {
                RectTransform cardRect = tileCard.GetComponent<RectTransform>();
                tileCards.Add(tileCard);
                currentPositions[cardRect] = cardRect.anchoredPosition;
            }
        }

        // 如果玩家选择了自动排序手牌，按tileId排序
        if (NormalGameStateManager.Instance != null && NormalGameStateManager.Instance.isAutoArrangeHandCards){
            tileCards.Sort((a, b) => a.tileId.CompareTo(b.tileId));
        }
        // 如果不勾选自动排序，保持原有顺序

        // 根据手牌排序获取目标位置
        for (int i = 0; i < tileCards.Count; i++) {
            // 获取手牌对象
            RectTransform cardRect = tileCards[i].GetComponent<RectTransform>();
            // 根据手牌排序设置子对象层级排序
            tileCards[i].transform.SetSiblingIndex(i);
            // 根据手牌排序目标子对象位置
            Vector2 targetPos = new Vector2(i * tileCardWidth, 0);
            targetPositions[cardRect] = targetPos;
        }
        
        // 检查是否需要动画：比较每个手牌的当前位置和目标位置
        bool needsAnimation = false;
        foreach (var kvp in currentPositions){
            RectTransform cardRect = kvp.Key;
            Vector2 currentPos = kvp.Value;
            Vector2 targetPos = targetPositions[cardRect];
            
            if (Vector2.Distance(currentPos, targetPos) > 0.01f){
                needsAnimation = true;
                break;
            }
        }
        
        if (!needsAnimation){
            Debug.Log($"手牌位置无需调整，跳过动画");
            yield break;
        } else {
            // 执行动画
            List<RectTransform> cards = new List<RectTransform>(currentPositions.Keys);
            List<Vector2> targetPosList = new List<Vector2>();
            foreach (RectTransform card in cards){
                targetPosList.Add(targetPositions[card]);
            }
            yield return StartCoroutine(AnimateCardsToPositions(cards, targetPosList));
        }
    }

    // 卡牌移动动画协程
    private System.Collections.IEnumerator AnimateCardsToPositions(List<RectTransform> cards, List<Vector2> targetPositions){
        float animationDuration = 0.3f; // 动画持续时间
        float elapsedTime = 0f;
        
        // 记录起始位置
        List<Vector2> startPositions = new List<Vector2>();
        for (int i = 0; i < cards.Count; i++){
            startPositions.Add(cards[i].anchoredPosition);
        }
        
        // 动画循环
        while (elapsedTime < animationDuration){
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / animationDuration;
            
            // 使用平滑插值函数（easeOutCubic）
            float smoothProgress = 1f - Mathf.Pow(1f - progress, 3f);
            
            // 更新每张卡牌的位置
            for (int i = 0; i < cards.Count; i++){
                if (cards[i] != null){
                    Vector2 currentPos = Vector2.Lerp(startPositions[i], targetPositions[i], smoothProgress);
                    cards[i].anchoredPosition = currentPos;
                }
            }
            
            yield return null; // 等待下一帧
        }
        
        // 确保最终位置准确
        for (int i = 0; i < cards.Count; i++){
            if (cards[i] != null){
                cards[i].anchoredPosition = targetPositions[i];
            }
        }
    }

    /// <summary>
    /// 摸牌动画：从上方2个宽度向下滑动到原始位置，同时透明度从100到0
    /// </summary>
    private IEnumerator AnimateGetCard(RectTransform cardRect, Vector2 targetPosition)
    {
        // 计算初始位置（上方2个宽度）
        Vector2 startPosition = targetPosition + new Vector2(0, 1.0f * tileCardWidth);
        
        // 设置初始位置和透明度
        cardRect.anchoredPosition = startPosition;
        
        // 获取CanvasGroup组件控制透明度（如果没有则添加）
        CanvasGroup canvasGroup = cardRect.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = cardRect.gameObject.AddComponent<CanvasGroup>();
        }
        canvasGroup.alpha = 0.0f; // 初始透明度100%
        
        // 动画参数
        float animationDuration = 0.2f;
        float elapsedTime = 0f;
        
        // 动画循环
        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsedTime / animationDuration);
            
            // 位置插值：从上方滑动到目标位置
            cardRect.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, progress);
            
            // 透明度插值：从100%到0%
            canvasGroup.alpha = Mathf.Lerp(0.0f, 1.0f, progress);
            
            yield return null; // 等待下一帧
        }
        
        // 确保最终位置和透明度准确
        cardRect.anchoredPosition = targetPosition;
        canvasGroup.alpha = 1.0f;
    }
}