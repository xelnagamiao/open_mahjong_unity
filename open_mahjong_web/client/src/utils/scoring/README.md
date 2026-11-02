# 浏览器本地算番

`handShapes.ts` 枚举一般型、七对和十三幺；`qingque.ts`、`riichi.ts` 在浏览器或 Web Worker 内计分。运行时不使用 Node/Python 服务或 Unity Editor。

- 青雀判定和精算表来源：仓库 `open_mahjong_unity/Assets/Scripts/GameScene/Calculation/CalculationScript/Qingque13/Qingque13Calc/`。`qingqueData.json` 的 `names` 保持 `QingqueFan.cs` 枚举顺序，`covers` 对应 `QingqueDerepellenise.cs` 的覆盖表，`cache` 将 `fan_cache.json` 中的 96 位二进制键转换为不含前导零的十六进制键，精算值保持原值。偶然番在查表后相加，覆盖关系仅用于显示，底分为精算番的平方取偶数舍入。
- 日麻参照同目录下 `Riichi/RiichiCalc/` 的四人默认规则：食断、累计役满、复合役满开启；双倍役满、切上满贯关闭；连风雀头 4 符。按具体和牌面子分别计算待牌、暗刻和符数，再选最高点数。本场、供托不计入展示点数。宝牌、里宝牌、赤宝牌输入实际数量，宝牌不能代替役。初巡天和／地和条件和三麻规则不在当前页面设置范围内。
- 字牌使用 Web 的牌号：45 中、46 白、47 发；输入 `5z/6z/7z` 对应白／发／中。手牌里的 `0m/0p/0s` 自动填入赤宝牌数量，副露赤宝牌可在数量框中补充。

`tests/fixtures/calculatorScoring.json` 保存离线运行仓库原有 C# 引擎得到的 137 组参考结果，包含一般型、七对、十三幺、副露、杠和不同和牌条件。`npm run test:hand-analysis` 验证参考结果、规则预设、跨页面状态、向听推进及异步失效处理。
