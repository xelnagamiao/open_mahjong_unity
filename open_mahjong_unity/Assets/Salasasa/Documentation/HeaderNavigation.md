# MainScene 导航与背景维护

## 场景中的位置

当前显示内容直接保存在 `Assets/Scenes/MainScene.unity`，不再由旧 header 的隐藏按钮转发。

`MainCanvas` 下的主要分组：

- `BackgroundPanel`：首页背景和角色。`BackgroundImage` 是实际显示背景纹理的 RawImage，`CharacterStage` 是角色容器；`ContinuousBackground` 与 `ArtworkShade` 是现有衬底。
- `HeaderPanel/Navigation`：当前实际显示的导航、主菜单幕布、工具按钮和展开菜单。直接在这里查看图形与布局。
- `PageRoot`：首页正文、房间、牌谱、赛事、社群等页面。
- `HomePresentation`：页面与美术配置的协调组件，不是另一套可见 UI，也不再有独立的根 Canvas。
- `UserContainer`：保留原登录昵称、头像和段位的数据绑定，供登录回调和个人资料使用；当前可见头像入口不在此编辑。

确认框、提示、设置与其他弹窗仍由 `OverlayCanvas` 管理，以保持它们在页面和牌桌之上。不要将弹窗与背景混到同一排序层。

背景和导航采用现有 `CanvasReferenceLayout`，把原 1600×900 的设计坐标换算到 MainCanvas。`BackgroundPanel` 上禁用的 CanvasScaler 只保存设计参考参数；实际根 Canvas 和屏幕相机由 MainCanvas 统一管理。不要重新启用该 CanvasScaler 或建立另一个根 Canvas。

## 控制关系

| 组件 | 位置与职责 |
|---|---|
| `FourSideHeader` | 位于 MainCanvas/HeaderPanel；布局、选中状态、幕布与展开菜单 |
| `HeaderPresentation` | 位于 MainCanvas/HeaderPanel；应用导航文字、图标和语言配置 |
| `CompleteLobbyController` | 位于 MainCanvas/HomePresentation；直接调用 WindowsManager 和各页面控制器，控制背景/正文的显示状态 |
| `LobbyShowcase` | 位于 MainCanvas/BackgroundPanel；应用当前角色、背景及场景美术设置 |
| `WindowsManager` | 切换页面，并维护 CanReturnToGame；headerPanel 字段引用实际 HeaderPanel |
| `HeaderReturnGame` | 位于 Navigation/NavigationTabs 的返回入口；直接返回挂起的对局/牌谱，并根据真实待操作状态闪烁 |
| `LobbyTableTransition` | 位于 WindowsManager；淡出 PageRoot、BackgroundPanel、HeaderPanel、LobbyOverlay 后显示牌桌 |
| `LobbyIconWorkbench / LobbyIconBinding` | 保留已选定图标的资源绑定与界面方案入口；不依赖旧 HeaderButton |
| `HomeUiVisibility` | 仅隐藏 UI，保留明确的背景和角色对象；恢复时还原原显示状态 |

旧 `HeaderPanel.cs`、`HeaderButton.cs`、旧隐藏导航子节点、隐藏匹配入口及 `HeaderDataIconChoices.cs` 已退役。不要恢复 productionNavigation、HeaderReturnGame.source 或按旧按钮的 onClick 转发。新增页面路由在 CompleteLobbyController 中实现，保持重复点击回主页的既有行为。

数据手册图标已经固定，主图形由 HeaderDataGraphic 直接绘制。其他当前图标的资源绑定必须保留，不能在清理时统一调用 Apply(0) 覆盖所选样式。

## 修改文字、图标与背景

1. 打开 MainScene，使用 **Tools → Salasasa → Header → 设计配置**。
2. 权威配置为 `Assets/Salasasa/HeaderNavigation/Editable/HeaderPresentation.asset`。文字的简繁英文、字号、位置、颜色与图标的尺寸、位置分别配置。
3. 使用 **保存并应用到 MainScene** 保存设计；这个工具只更新绑定，不重建页面。直接修改 Caption 是修改烘焙结果，下次应用配置会覆盖。
4. 背景在 BackgroundPanel 的 LobbyShowcase 上配置；显示结果在 BackgroundImage 和 CharacterStage 中查看。

各入口保留 Artwork/Icon/Caption/TextOutline/TextShadow 的命名与层级。语言改变时三个 TMP 文字层同步，中文保持正常字重。主菜单调色仍由 HomeHeaderAppearance 和 HomeHeaderPalette 控制。

社群、邮件、资料等内容沿用现有功能。未明确合并的独立设计项目不属于此层级清理范围。不要执行历史 CompleteLobby 或 FourSideHeader 示例迁移脚本重建主场景。

## 检查与构建

- **Tools → Mahjong → Main Scene → Validate Presentation Hierarchy** 检查新的层级、当前引用、缩放和返回牌桌动画绑定。
- **Validate Fixed UI** 和正式构建前检查会同时调用上述检查，防止再次绑定旧 header。
- 调整层级后用 `LobbyTableTransitionBaker.Bake` 更新动画分组并保存场景，不要把控制器的父节点包含进自身淡出组。
- 验证登录、重复点击导航、展开菜单、返回牌桌、背景切换、隐藏 UI，以及宽屏/手机相机模式。构建使用真实 MainScene。
- 本机临时验证、备份、日志和 Windows 示例均放在 `.om_workspace/`，不向玩家默认注入测试登录、开发模式或本地服务地址。

共享 Editor 的场景修改、编译和构建仍须遵守工作区互斥规则。
