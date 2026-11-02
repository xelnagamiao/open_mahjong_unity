#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Salasasa.Home;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>主场景中固定界面及业务入口的序列化引用回归。</summary>
public static class MainSceneAppearanceValidation {
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields).GetValue(owner);

    public static object Run() {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/MainScene.unity");
        if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("请先打开 MainScene。");
        var components = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true)).Where(c => c).ToArray();
        var checks = new List<string>();
        void Check(bool valid, string name) { if (!valid) throw new InvalidOperationException(name); checks.Add(name); }
        T One<T>() where T : MonoBehaviour => components.OfType<T>().Single();

        var owner = One<CompleteLobbyController>();
        Check(owner.name == "HomePresentation" && owner.transform.parent == owner.productionCanvas.transform, "主界面控制器归属 MainCanvas");
        Check(owner.artwork.name == "BackgroundPanel" && owner.artwork.transform.parent == owner.productionCanvas.transform, "背景与角色归属 MainCanvas/BackgroundPanel");
        Check(owner.headerNavigation.name == "HeaderPanel" && owner.headerNavigation.transform.parent == owner.productionCanvas.transform, "实际导航归属 MainCanvas/HeaderPanel");
        var plans = One<InterfacePlansController>();
        Check(plans.owner == owner && plans.pages.Length == 2 && plans.tabs.Length == 2 && plans.pages.Select(p=>p.name).SequenceEqual(new[]{"Global","Pages"}), "界面方案移除示例标签并保留有效页面");
        Check(plans.choices.Select(c => c.option).Distinct().Count() == plans.choices.Length && plans.choices.All(c=>Enum.IsDefined(typeof(DesignOption),c.option)), "界面方案只保留有效设计配置");
        Check(plans.inventory.VariantCount==1 && plans.inventory.Variant==0 && plans.inventory.CurrentPanelSize==new Vector2(1280,704), "背包只保留紧凑居中布局");
        Check(One<InventoryPalette>().Current.name == "原版石墨", "背包固定原版石墨配色");
        Check(!plans.pages[1].transform.Find("InventoryPalette"), "背包配色选择已移除");
        Check(plans.home.target.cornerSelection && plans.home.target.cornerSelection.enabled && !plans.home.originalIndicator.enabled, "原版主菜单使用统一四角选中");
        Check(plans.iconWorkbench.bindings.Count(b=>b&&b.transform.IsChildOf(owner.headerNavigation.canvas.transform))>=9, "图标工作台绑定当前导航");
        var overlay=scene.GetRootGameObjects().Single(g=>g.name=="OverlayCanvas").transform;
        Check(!overlay.Find("LobbyOverlay/CompleteHeader"), "重复旧导航层已从主场景移除");
        var user = One<UserContainer>();
        Check(user.transform.parent == owner.productionCanvas.transform && user.gameObject.activeSelf,
            "玩家资料绑定归属常驻主画布并可在登录前初始化");
        var userVisibility = user.GetComponent<CanvasGroup>();
        Check(userVisibility && userVisibility.alpha == 0 && !userVisibility.interactable && !userVisibility.blocksRaycasts,
            "保留的玩家资料绑定不绘制重复界面或阻挡点击");
        Check(Field<TMPro.TMP_Text>(user, "usernameText") && Field<UnityEngine.UI.Image>(user, "profileImage"),
            "玩家资料保留真实姓名与头像绑定");
        Check(plans.iconWorkbench.bindings.All(b => b) && owner.headerNavigation.tabs.All(n => n && n.button),
            "当前图标绑定与可见导航无失效引用");
        Check(overlay.GetComponentsInChildren<Canvas>(true).Where(c=>c.isActiveAndEnabled).All(c=>c.rootCanvas==overlay.GetComponent<Canvas>()),"显示中的全局弹层使用OverlayCanvas作为实际根画布");
        Check(owner.bodyLayer.transform.IsChildOf(owner.sourceMenu.transform) && owner.sourceMenu.transform.parent==owner.pageRoot,"主菜单完整归属PageRoot及菜单窗口生命周期");
        Check(!owner.bodyLayer.GetComponent<Canvas>().overrideSorting,"主菜单继承页面绘制顺序");
        var matchPage=owner.pageRoot.Find("MatchPanel");
        Check(owner.statistics.transform.parent==owner.pageRoot && matchPage && matchPage.parent==owner.pageRoot,"数据与匹配页面归属PageRoot");
        Check(!owner.statistics.canvas.overrideSorting && (!matchPage.GetComponent<Canvas>() || !matchPage.GetComponent<Canvas>().overrideSorting),"一级页面不通过独立排序覆盖其他页面");
        Check(components.OfType<PanelPopupTransition>().All(p=>p.transform.IsChildOf(overlay)),"全部通用弹窗归属OverlayCanvas");
        Check(components.OfType<TipsContainer>().All(p=>p.transform.IsChildOf(overlay)) && components.OfType<ScoreHistoryFanTooltip>().All(p=>p.transform.IsChildOf(overlay)),"对局悬浮提示归属OverlayCanvas");
        Check(owner.headerNavigation.canvas.transform.IsChildOf(owner.headerNavigation.transform) && owner.leaderboard.transform.IsChildOf(overlay),"导航归属 HeaderPanel，排行榜归属全局叠层");
        Check(plans.venue.Variant==0 && !plans.pages[1].transform.Find("Venue"),"赛事基地仅保留暗底浮框");
        Check(owner.statistics.panel.playerIdInput.GetComponent<InputFieldFeedback>(),"UID输入框交互反馈已烘焙");
        Check(plans.settings.variants.Length == 3 && plans.settings.controls.Length == 24, "三套设置及全部二十四项绑定");
        Check(plans.settings.bakedPages.Where(p=>p.category==2).All(p=>p.controls.Any(f=>f.key=="MeldSpacingRow" && f.dropdown)) &&
            !plans.settings.transform.Find("NativeSettingsBindings"), "副露间距直接接配置，不保留隐藏代理设置");
        Check(One<FriendPanel>() && !components.Any(c => c.GetType().Name == "FriendLocalPreview"), "好友页面仅使用真实列表");
        Check(Field<UnityEngine.Object>(One<EventLobbyPanel>(), "presentation"), "赛事基地接入已烘焙展示层");
        Check(Field<UnityEngine.Object>(One<EventDetailPanel>(), "redesignedPanel"), "赛事详情与管理展示层完整绑定");
        Check(Field<UnityEngine.Object>(One<NoticePanel>(), "presentation"), "通知展示层完整绑定");
        Check(One<LoginPanel>() && One<PlayerInfoPanel>() && One<InventoryPanel>(), "登录、个人资料与背包存在唯一实例");
        Check(components.OfType<MatchLobbyView>().Count() == 1, "只保留已确认的匹配布局");
        var eloAdapter = One<RankedEloHelp>();
        foreach (var lobby in components.OfType<MatchLobbyView>()) {
            var entries = Field<MatchButton[]>(lobby, "entries");
            Check(entries.Length == 30 && entries.All(c => c) && entries.Select(c => c.QueueType).Distinct().Count() == 30, lobby.name + "：三十个独立队列");
            Check(entries.Any(c => c.QueueType == "qingque_elo_unlimited"), lobby.name + "：青雀不限时队列");
            var help = Field<GameObject>(lobby, "eloHelp");
            var helpButton = help ? help.GetComponent<UnityEngine.UI.Button>() : null;
            Check(help && helpButton && helpButton.targetGraphic && eloAdapter.gameObject == help && Field<RankPolicyPanel>(eloAdapter, "policyPanel") == One<RankPolicyPanel>(), lobby.name + "：头部 Elo 说明点击入口唯一绑定真实面板");
            Check(entries.All(card => !card.GetComponentInChildren<RankedEloHelp>(true)), lobby.name + "：卡片沿用 MatchButton 点击入口");
            Check(!Enumerable.Range(0, helpButton.onClick.GetPersistentEventCount()).Any(i => helpButton.onClick.GetPersistentTarget(i) is RankPolicyPanel && helpButton.onClick.GetPersistentMethodName(i) == "Show"), lobby.name + "：头部没有重复的普通段位说明监听");
        }
        var graphics = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true));
        Check(graphics.All(graphic => graphic.GetComponent<CanvasRenderer>()), "所有图形具备 CanvasRenderer");
        Check(components.All(c => c.GetType().Namespace != "Salasasa.SceneControlsLab"), "不引入独立场景设置设计");
        return new { passed = checks.Count, checks };
    }
}
#endif
