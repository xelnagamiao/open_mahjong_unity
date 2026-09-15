# 卡牌贴图

统一在此管理手牌、3D 牌面及牌体贴图；模型和材质仍在 Resources/3D、Resources/Materials/Tiles。

- `Faces/{official,fluffy,hkmahjong}/{hand,table}`：标准麻将牌面花纹。
- `Faces/hongque/{hand,table}`：虹雀手牌 / 3D 牌面，各 126 张。
- `Faces/official/TableAtlas.spriteatlasv2`：官方 3D 牌面图集。
- `Surfaces/backgrounds`：手牌竖向、横向底图。
- `Surfaces/backs`：手牌牌背。

标准包 `2.png` 是纯白白板使用的空白前景，叠在牌体底色上；`46.png` 保留该包原有的白板花纹。香港麻将保留原来的框式白板，同时提供 hand/table 两份空白 `2.png`。纯白模式不会强制覆盖用户自定义的牌面底色。

上传的 3D 牌背、背景和自定义牌面属于用户存档，不写入 Resources。
运行时资源路径集中在 `TilePackIds`，生成与导出工具为仓库 `tools/tilepack/rebuild_table_faces.py`。
移动资产时必须一起保留同名 `.meta`，以维持场景和图集的 GUID 引用。
