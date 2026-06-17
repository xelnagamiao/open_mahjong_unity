// GameCanvas 手牌处理

using System.Collections;
using System.Collections.Generic;
using System;
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
            _processChangeHandCardQueueCoroutine = StartCoroutine(ProcessChangeHandCardQueue());
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
        _processChangeHandCardQueueCoroutine = null;
        isChangeHandCardProcessing = false;
    }
    
    // 手牌处理 
    public IEnumerator ChangeHandCardsCoroutine(string ChangeType,int tileId,int[] TilesList,int? cut_tile_index){

        Debug.Log($"手牌处理: {ChangeType}");

        // 初始化手牌（全部直接创建）
        if (ChangeType == "InitHandCards"){
            if (TilesList == null){
                yield break;
            }
            for (int i = 0; i < TilesList.Length; i++){
                GameObject cardObj = Instantiate(tileCardPrefab, handCardsContainer);
                TileCard tileCard = cardObj.GetComponent<TileCard>();
                tileCard.SetTile(TilesList[i], false);
                tileCard.handSortIndex = i;
            }
            LayoutHandCardsFromCurrentOrder();
        }

        else if (ChangeType == "InitHandCardsFromRecord"){
            if (TilesList == null){
                Debug.LogWarning("ChangeHandCards InitHandCardsFromRecord: TilesList 为空，跳过初始化。");
                yield break;
            }
            int[] sortedTiles = (int[])TilesList.Clone();
            Array.Sort(sortedTiles, TileIdOrder.Comparer);
            for (int i = handCardsContainer.childCount - 1; i >= 0; i--){
                Transform child = handCardsContainer.GetChild(i);
                Destroyer.Instance.AddToDestroyer(child);
            }
            for (int i = 0; i < sortedTiles.Length; i++){
                GameObject cardObj = Instantiate(tileCardPrefab, handCardsContainer);
                TileCard tileCard = cardObj.GetComponent<TileCard>();
                tileCard.SetTile(sortedTiles[i], false);
                tileCard.handSortIndex = i;
            }
            LayoutHandCardsFromCurrentOrder();
        }

        // 摸牌 添加摸牌区手牌
        else if (ChangeType == "GetCard"){
            GameObject cardObj = Instantiate(tileCardPrefab, handCardsContainer);
            TileCard tileCard = cardObj.GetComponent<TileCard>();
            tileCard.SetTile(tileId, true);
            int handCardCount = handCardsContainer.childCount - 1;
            tileCard.handSortIndex = handCardCount;
            List<TileCard> main = GetMainHandCardsOrdered(tileCard);
            RectTransform cardRect = cardObj.GetComponent<RectTransform>();
            Vector2 targetPosition = GetDrawTileTargetPosition(main);
            yield return StartCoroutine(AnimateGetCard(cardRect, targetPosition));
            if (NormalGameStateManager.Instance != null && NormalGameStateManager.Instance.IsSelfActionRequired) {
                RefreshHandTileSelectability();
            }
        }

            // 摸牌 添加摸牌区手牌
        else if (ChangeType == "GetCardNoAnimation"){
            GameObject cardObj = Instantiate(tileCardPrefab, handCardsContainer);
            TileCard tileCard = cardObj.GetComponent<TileCard>();
            tileCard.SetTile(tileId, true);
            int handCardCount = handCardsContainer.childCount - 1;
            tileCard.handSortIndex = handCardCount;
            LayoutHandCardsFromCurrentOrder();
            if (NormalGameStateManager.Instance != null && NormalGameStateManager.Instance.IsSelfActionRequired) {
                RefreshHandTileSelectability();
            }
        }

        // 摸切 删除摸牌区手牌
        else if (ChangeType == "RemoveGetCard"){
            TryRemoveCutHandCard(tileId, isMoqie: true, cutTileIndex: null);
        }

        // 四川血战自摸和牌：仅移除和牌张，不触发全手重排（避免 2D 手牌区闪烁）
        else if (ChangeType == "RemoveHuWinTile"){
            TryRemoveCutHandCard(tileId, isMoqie: true, cutTileIndex: null);
            isArranged = true;
            yield break;
        }

        // 牌谱模式手切：仅按 tileId 删除一张，逻辑独立无 dataIndex/重排
        else if (ChangeType == "RemoveHandCardRecord"){
            foreach (Transform child in handCardsContainer){
                TileCard tc = child.GetComponent<TileCard>();
                if (tc != null && tc.tileId == tileId){
                    Destroyer.Instance.AddToDestroyer(child);
                    break;
                }
            }
        }

        // 手切 删除手牌区手牌（对局模式用 cut_tile_index）
        else if (ChangeType == "RemoveHandCard"){
            TryRemoveCutHandCard(tileId, isMoqie: false, cutTileIndex: cut_tile_index);
        }

        // 补花 删除手牌区手牌（只删一张：判空 + 命中即停，避免理牌期间非 TileCard 子物体触发空引用中断、或边遍历边 SetParent 改集合导致补花牌残留手牌）
        else if (ChangeType == "RemoveBuhuaCard"){
            for (int i = 0; i < handCardsContainer.childCount; i++){
                TileCard needToRemoveTileCard = handCardsContainer.GetChild(i).GetComponent<TileCard>();
                if (needToRemoveTileCard != null && needToRemoveTileCard.tileId == tileId){
                    // 如果补花牌是摸牌区手牌 设置ChangeType = "RemoveBuhuaGetCard" 代表后续不用执行动画
                    if (needToRemoveTileCard.currentGetTile){
                        ChangeType = "RemoveBuhuaGetCard";
                    }
                    Destroyer.Instance.AddToDestroyer(needToRemoveTileCard.transform);
                    break;
                }
            }
        }

        // 加杠 删除手牌区手牌（只删一张：判空 + 命中即停，避免理牌期间非 TileCard 子物体触发空引用中断、或边遍历边 SetParent 改集合漏删）
        else if (ChangeType == "RemoveJiagangCard"){
            RemoveOneHandCardByTileId(tileId);
        }

        // 删除组合牌 在手牌中删除全部组合牌
        else if (ChangeType == "RemoveCombinationCard"){
            if (TilesList == null){
                Debug.LogWarning("ChangeHandCards RemoveCombinationCard: TilesList 为空，跳过处理。");
                yield break;
            }
            foreach (int tileToRemove in TilesList){
                for (int i = 0; i < handCardsContainer.childCount; i++){
                    TileCard needToRemoveTileCard = handCardsContainer.GetChild(i).GetComponent<TileCard>();
                    if (needToRemoveTileCard != null && needToRemoveTileCard.tileId == tileToRemove){
                        Destroyer.Instance.AddToDestroyer(needToRemoveTileCard.transform);
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
        else if (ChangeType == "RemoveBuhuaGetCard" || ChangeType == "InitHandCardsFromRecord"){
            isArranged = true;
            yield break;
        }

        // 初始化卡牌、摸切、手切、单次补花、吃碰杠以后 进行卡牌排序 
        else if (ChangeType == "RemoveHandCard" || ChangeType == "RemoveHandCardRecord" || ChangeType == "RemoveCombinationCard" || ChangeType == "RemoveBuhuaCard" ||
         ChangeType == "RemoveJiagangCard" || ChangeType == "InitHandCards" || ChangeType == "InitHandCardsFromRecord" || ChangeType == "ReSetHandCards" || ChangeType == "RemoveGetCard"){
            isArranged = true;
            // 等待排序完成
            yield return StartCoroutine(RearrangeHandCardsWithAnimation());
        }
    }

    // 带动画的手牌重新排列
    private IEnumerator RearrangeHandCardsWithAnimation(){
        CancelCompetingHandReflowAnimations("出牌重排");
        yield return RunHandReflowAnim(RearrangeHandCardsWithAnimationCore());
    }

    private IEnumerator RearrangeHandCardsWithAnimationCore(){

        Debug.Log($"[HandLayout] 手牌重新排列");

        // 手牌恢复为非摸切状态（同时清除摸牌区固定标记）
        for (int i = 0; i < handCardsContainer.childCount; i++){
            Transform child = handCardsContainer.GetChild(i);
            TileCard tileCard = child.GetComponent<TileCard>();
            if (tileCard != null){
                tileCard.currentGetTile = false;
                tileCard.isDrawSlotPinned = false;
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
        // 牌谱回放/观战回放依赖 cut_tile_index，不能在客户端额外重排顺序
        bool isRecordPlayback = GameRecordManager.Instance != null && GameRecordManager.Instance.gameObject.activeSelf;
        // 牌谱模式始终按牌值排序；对局模式在勾选自动排序时排序
        if (isRecordPlayback || (AutoAction.Instance != null && AutoAction.Instance.IsAutoArrangeHandCards)){
            tileCards.Sort((a, b) => TileIdOrder.Compare(a.tileId, b.tileId));
        }
        // 对局且不勾选自动排序时保持原有顺序

        Dictionary<RectTransform, Vector2> layoutPositions = BuildHandLayoutPositions(tileCards, null, null);
        for (int i = 0; i < tileCards.Count; i++) {
            RectTransform cardRect = tileCards[i].GetComponent<RectTransform>();
            tileCards[i].handSortIndex = i;
            tileCards[i].transform.SetSiblingIndex(i);
            targetPositions[cardRect] = layoutPositions[cardRect];
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
    public System.Collections.IEnumerator AnimateCardsToPositions(List<RectTransform> cards, List<Vector2> targetPositions, float animationDuration = 0.3f){
        float elapsedTime = 0f;
        
        // 记录起始位置
        List<Vector2> startPositions = new List<Vector2>();
        for (int i = 0; i < cards.Count; i++){
            if (cards[i] == null) yield break; // 手牌已被清空（重连/下一局）
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
    /// 若 cardRect 在动画过程中被销毁（如重连/下一局清空手牌），会安全退出，避免 MissingReferenceException。
    /// </summary>
    private IEnumerator AnimateGetCard(RectTransform cardRect, Vector2 targetPosition)
    {
        if (cardRect == null) yield break;

        // 计算初始位置（上方2个宽度）
        Vector2 startPosition = targetPosition + new Vector2(0, 1.0f * tileCardWidth);
        
        // 设置初始位置和透明度
        cardRect.anchoredPosition = startPosition;
        if (cardRect == null) yield break;
        
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
            if (cardRect == null) yield break; // 手牌容器被清空（重连/下一局）时安全退出
            elapsedTime += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsedTime / animationDuration);
            
            // 位置插值：从上方滑动到目标位置
            cardRect.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, progress);
            
            // 透明度插值：从100%到0%
            canvasGroup.alpha = Mathf.Lerp(0.0f, 1.0f, progress);
            
            yield return null; // 等待下一帧
        }
        
        // 确保最终位置和透明度准确
        if (cardRect == null) yield break;
        cardRect.anchoredPosition = targetPosition;
        canvasGroup.alpha = 1.0f;
    }

    /// <summary>
    /// 切牌删牌：固定位置（须 tileId 一致）→ 同切型 tileId → 跨切型 tileId → 任意 tileId。
    /// 用于对局摸切/手切；实时观战或本地理牌后 cut_tile_index 可能与服务器不一致时仍按 tileId 纠正。
    /// </summary>
    private bool TryRemoveCutHandCard(int tileId, bool isMoqie, int? cutTileIndex) {
        // 1. 固定位置（须 tileId 一致才删）
        if (isMoqie) {
            // 优先检查摸牌区标记牌，再检查最后一张牌（摸牌区的牌）
            TileCard drawTile = GetDrawTile();
            if (TryRemoveCutHandCardIfMatch(drawTile, tileId)) {
                return true;
            }
            if (handCardsContainer.childCount > 0) {
                Transform lastCard = handCardsContainer.GetChild(handCardsContainer.childCount - 1);
                if (TryRemoveCutHandCardIfMatch(lastCard.GetComponent<TileCard>(), tileId)) {
                    return true;
                }
            }
        }
        else if (cutTileIndex.HasValue
                 && cutTileIndex.Value >= 0
                 && cutTileIndex.Value < handCardsContainer.childCount) {
            Transform indexedChild = handCardsContainer.GetChild(cutTileIndex.Value);
            if (TryRemoveCutHandCardIfMatch(indexedChild.GetComponent<TileCard>(), tileId)) {
                return true;
            }
        }

        // 2. 同切型按 tileId 查找（多张同牌值时优先靠近 cut_tile_index）
        if (TryRemoveCutHandCardByTileId(tileId, isMoqie, cutTileIndex)) {
            return true;
        }

        // 3. 跨切型兜底（手切可从摸切区找，摸切也可从手切区找）
        if (TryRemoveCutHandCardByTileId(tileId, !isMoqie, cutTileIndex)) {
            Debug.LogWarning($"切牌跨类型兜底: tileId={tileId}, 请求={(isMoqie ? "摸切" : "手切")}");
            return true;
        }

        // 4. 如果摸牌区/手牌区都没找到，从全部手牌按 tileId 寻找(如果吃碰杠以后超时等)
        if (TryRemoveCutHandCardByTileId(tileId, null, cutTileIndex)) {
            Debug.LogWarning($"切牌最终兜底: tileId={tileId}");
            return true;
        }

        Debug.LogWarning($"切牌删牌失败: tileId={tileId}, moqie={isMoqie}, index={cutTileIndex}");
        return false;
    }

    /// <summary>
    /// 从手牌中按 tileId 删除一张（命中即停）。用于加杠等「必然只删一张」的场景：
    /// 判空跳过非 TileCard 子物体（理牌拖拽期间可能短暂存在），命中后立即返回，
    /// 避免原 foreach 边遍历边 SetParent 改集合、或空引用抛异常中断协程导致加杠牌残留手牌。
    /// </summary>
    private bool RemoveOneHandCardByTileId(int tileId) {
        for (int i = 0; i < handCardsContainer.childCount; i++) {
            Transform child = handCardsContainer.GetChild(i);
            TileCard tc = child.GetComponent<TileCard>();
            if (tc != null && tc.tileId == tileId) {
                Destroyer.Instance.AddToDestroyer(child);
                return true;
            }
        }
        Debug.LogWarning($"RemoveOneHandCardByTileId 未找到要删除的牌 tileId={tileId}");
        return false;
    }

    private bool TryRemoveCutHandCardIfMatch(TileCard card, int tileId) {
        if (card != null && card.tileId == tileId) {
            Destroyer.Instance.AddToDestroyer(card.transform);
            return true;
        }
        return false;
    }

    /// <param name="requireMoqie">true=摸切区(currentGetTile), false=手切区, null=不限</param>
    private bool TryRemoveCutHandCardByTileId(int tileId, bool? requireMoqie, int? preferIndex) {
        Transform bestChild = null;
        int bestDist = int.MaxValue;

        for (int i = 0; i < handCardsContainer.childCount; i++) {
            Transform child = handCardsContainer.GetChild(i);
            TileCard tc = child.GetComponent<TileCard>();
            if (tc == null || tc.tileId != tileId) {
                continue;
            }
            if (requireMoqie.HasValue && tc.currentGetTile != requireMoqie.Value) {
                continue;
            }

            if (!preferIndex.HasValue) {
                Destroyer.Instance.AddToDestroyer(child);
                return true;
            }

            int dist = Mathf.Abs(i - preferIndex.Value);
            if (dist < bestDist) {
                bestDist = dist;
                bestChild = child;
            }
        }

        if (bestChild != null) {
            Destroyer.Instance.AddToDestroyer(bestChild);
            return true;
        }
        return false;
    }
}