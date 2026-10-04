#if UNITY_EDITOR
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    private void BakeRoomFooterAndHelp() {
        var footer = transform.Find("FooterPanel");
        var reset = RoomRect("RestoreDefaults", footer);
        restoreDefaultsButton = RoomGet<Button>(reset.gameObject);
        restoreDefaultsButton.targetGraphic = RoomImage(reset, Color.white);
        restoreDefaultsButton.targetGraphic.raycastTarget = true;
        var resetBorder = RoomGet<UnityEngine.UI.Outline>(reset.gameObject);
        resetBorder.effectColor = RoomLine;
        resetBorder.effectDistance = new Vector2(1, -1);
        var text = RoomLabel("Label", reset, "恢复默认设置", 30);
        text.color = Color.black; text.alignment = TextAlignmentOptions.Center;
        RoomFill(text.rectTransform);
        if (!roomHelpPanel) roomHelpPanel = footer.Find("ContextHelpPanel") as RectTransform;
        if (roomHelpPanel) roomHelpPanel.SetParent(transform, false);
        else roomHelpPanel = RoomRect("ContextHelpPanel", transform);
        roomHelpPanel.SetSiblingIndex(footer.GetSiblingIndex() + 1);
        RoomImage(roomHelpPanel, new Color32(34, 46, 94, 255)).raycastTarget = true;
        var helpBorder = RoomGet<UnityEngine.UI.Outline>(roomHelpPanel.gameObject);
        helpBorder.effectColor = new Color32(104, 120, 175, 255);
        helpBorder.effectDistance = new Vector2(1, -1);
        var existingText = footer.Find("ContextHelp");
        if (existingText) existingText.SetParent(roomHelpPanel, false);
        roomHelpText = RoomLabel("ContextHelp", roomHelpPanel, "", 22);
        roomHelpText.color = new Color(1, 1, 1, .92f);
        roomHelpText.margin = Vector4.zero;
        roomHelpText.richText = true;
        roomHelpText.raycastTarget = true;
        roomHelpText.textWrappingMode = TextWrappingModes.Normal;
        roomHelpText.overflowMode = TextOverflowModes.Overflow;
        RoomGet<CreateRoomHelpLink>(roomHelpText.gameObject).Configure(this, roomHelpText);
        var progress = roomHelpPanel.Find("Progress");
        if (progress) UnityEditor.Undo.DestroyObjectImmediate(progress.gameObject);
        roomHelpLifetime = RoomGet<CreateRoomHelpLifetime>(roomHelpPanel.gameObject);
        roomHelpLifetime.Configure(this);
        roomHelpPanel.gameObject.SetActive(false);

        void Help(Component control, string message) {
            if (!control) return;
            RoomGet<CreateRoomHelpTrigger>(control.gameObject).Configure(this, message);
        }
        void InputHelp(TMP_InputField input, string message) {
            Help(input, message);
            Help(input.transform.parent, message);
            var title = input.transform.parent.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(t => !t.GetComponentInParent<TMP_InputField>(true) && t.gameObject.activeSelf);
            if (title) title.raycastTarget = true;
        }
        const string seedHelp = "场景复现：输入曾经对局生成的随机种子，复现过去的对局。";
        const string duplicateHelp = "复式：复式麻将是指在比赛开始之前为每位玩家设置独立的手牌和牌山，并且提前规定好手牌和牌山的顺序，让多名玩家同时游玩同一副牌的同一个位置，最后比对这些相同位置玩家的最终数据，从而判断名次的规则。开启后须输入复式密钥，<link=\"duplicate\"><color=#FFC574><u>点击此处</u></color></link>查看规则详情或生成复式密钥。";
        const string limitHelp = "修改起和番：开启后将弃用规则的默认起和设置，输入1-64的整数设置起和番";
        const string cuoheHelp = "错和计分：-30/+10 为错和者扣30分、其余玩家各加10分；-40/0 为错和者扣40分，其余玩家维持原点";
        Help(chooseRule, "选择房间所用的麻将规则。");
        Help(SubRuleDropdown, "选择麻将子规则。");
        Help(tipsToggle, "番数提示：显示听牌的具体番数");
        Help(countTipsToggle, "枚数提示：显示听牌的剩余枚数。");
        Help(pointerTipsToggle, "指针提示：鼠标指向卡牌时，在牌河中使用蓝色叠加高亮所有与指向卡牌相同的卡牌");
        Help(CuoHeheToggle, "错和：允许申报不满足和牌条件的和牌，和牌后按错和计分处理。");
        Help(CuoheTypeDropdown, cuoheHelp);
        Help(CuoheTypePanel.transform, cuoheHelp);
        Help(passwordToggle, "设置密码：开启后须设置房间密码");
        Help(TouristLimitToggle, "限制游客：开启后不允许游客加入房间。");
        Help(AllowSpectatorToggle, "允许观战：其他玩家可以旁观本房间的对局，延迟180秒。");
        Help(ClaimProtectionToggle, "鸣牌保护：可吃、碰、杠的玩家立即看到弃牌，其他玩家稍后看到；无人可鸣牌或有人可和牌时不保护。房间中有机器人时自动停用，移除全部机器人后恢复所选设置。关闭后所有玩家同步看到弃牌。");
        Help(TacticalCallToggle, "战术鸣牌：战术鸣牌是一种以更高优先级操作碰断别人吃牌的战术行为，战术鸣牌的具体规则在不同规则中有不同的定义，<link=\"tactical-call\"><color=#FFC574><u>点击此处</u></color></link>查看战术鸣牌的详细说明");
        Help(SetRandomSeedToggle, seedHelp); InputHelp(randomSeedInput, seedHelp);
        Help(DuplicateWallToggle, duplicateHelp); InputHelp(DuplicateKeyInput, duplicateHelp);
        Help(InputHepaiLimitToggle, limitHelp); InputHelp(HepaiLimitInput, limitHelp);
        InputHelp(roomNameInput, "输入房间名称：房间名称不能为空。");
        InputHelp(passwordInput, "输入密码：设置房间密码");
        InputHelp(RiichiStartingScoreInput, "起始点数：输入 1000–1000000 的整数，并且必须是 100 的倍数");
        Help(GuobiaoFlowersToggle, "花牌：选择牌山是否包含花牌");
        Help(TianDiRenHeToggle, "天地人和：仅标准国标与血战到底可开启，各加计8番。天和：庄家起手直接自摸；地和：闲家和庄家打出的第一张牌；人和：闲家首次摸牌自摸。全桌吃、碰、杠（含暗杠）均打断，补花不打断。");
        Help(stepTimer, "步时：每次操作的基础思考时间。");
        Help(roundTimer, "局时：每局可用的额外思考时间，步时耗尽后消耗。");
        Help(RedDoraToggle, "赤宝牌：启用赤五万、赤五筒与赤五索。");
        Help(KuikaeToggle, "禁止食替：副露后禁止立即打出同种牌或筋食替牌。");
        Help(XiruToggle, "西入：预定末局无人达到终局目标分时进入延长战。");
        Help(TobiToggle, "击飞：有玩家点数小于 0 时结束对局。");
        Help(HepaiWayDropdown, "和牌方式：选择多家和牌、三家和了流局或头跳。");
        Help(DetailedConfigButton, "高级设置：打开当前规则的详细配置");
        Help(restoreDefaultsButton, "恢复默认设置：恢复当前麻将规则的默认设置");
        foreach (var round in RoomRoundToggles) Help(round, "选择对局长度");
    }
}
#endif
