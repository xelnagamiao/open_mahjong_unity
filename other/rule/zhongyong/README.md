# 中庸麻将与南雀

建房入口为中庸麻将，包含标准中庸（`zhongyong/standard`）和南雀（`zhongyong/nanque`）。南雀默认血战到底，保留自己的计分表，不等同于标准中庸。

- 标准中庸：[作者 v3.3 基本计分规则](https://www.zj-mahjong.info/zj33_rules_eng.html)、[44 和种表](https://www.zj-mahjong.info/zj33_patterns_eng.html)、[通用行牌规则](https://www.zj-mahjong.info/book_en/1.1%20Misc%20Rules.html)。作者 Alan Kwan；原文许可见原页面。
- 南雀：项目历史提交 `d92bece1` 的 `other/jiandan/rule_reference.md` 与已有 `game_calculation/jiandan` 计分器。新的行牌状态机位于 `server/gamestate/game_zhongyong`，计分器彼此独立。
- 玩家规则摘要位于 `open_mahjong_web/client/public/rulebooks/zhongyong.html`，网页规则书与 Unity 建房入口共同指向它。
- 南雀牌谱仅支持 `zhongyong/nanque`，携带版本与血战标记，逐次记录和牌并在终局记录统一结算。旧 `jiandan` 牌谱停止支持；旧建房接口只作为新南雀的入口别名，不再生成旧格式牌谱。
