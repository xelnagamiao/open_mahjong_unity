# 自定义标准麻将牌面包

仅 **Unity 客户端**（桌面 / WebGL）支持上传。虹雀牌面锁定官方 HQv3.1，不可自定义。对局仍按规则自动选族：虹雀局用虹雀图，其余规则用标准套。

2D 手牌为 **固定宽度、高度按原图像素比自适应**，避免拉伸。3D 桌牌 mesh 不变，自定义图按 contain 贴进牌面 UV。

## 压缩包

- 扩展名：`.zip`
- 可选 `manifest.json`：

```json
{
  "format": "om-tilepack",
  "version": 1,
  "family": "standard"
}
```

`family` 必须是 `standard`。没有 manifest 也可以，只要 PNG 文件名能对上牌 ID。

## 目录与文件名

```text
my-tiles.zip
├── manifest.json          // 可选
├── 11.png … 19.png        // 万
├── 21.png … 29.png        // 条
├── 31.png … 39.png        // 筒
├── 41.png … 47.png        // 东南西北中发白
├── 51.png … 58.png        // 花（春夏秋冬梅兰竹菊）
├── 105.png 205.png 305.png // 赤5万 / 赤5饼 / 赤5条
└── 2.png                  // 可选，纯白白板
```

文件可放在 zip 根目录或 `hand/`。`table/` 下同名 PNG 仅用于 3D 桌牌；没有 `table/` 时 3D 会用 `hand/` 图 contain 贴面。

## 限制

| 项 | 要求 |
|---|---|
| 格式 | 仅 PNG |
| 单边 | ≤ 1024 像素 |
| 单张 | ≤ 500KB |
| 整包解压后 | ≤ 20MB |

缺图回退官方雪风牌面，不会整包作废。设置页展示全部标准牌 / 全部虹雀牌；自定义包缺图的格子半透明。

## 存储

- 桌面 / Android / iOS：`persistentDataPath/TilePacks/standard/`
- Unity WebGL：浏览器 IndexedDB（库名 `omu.unityAssets`，store `tileBlobs`），**不走 PlayerPrefs 存文件体**

同一套 IndexedDB 也存放自定义牌背、桌布、边框：

| 资源 | key |
|---|---|
| 标准牌面包 zip | `standardZip` |
| 牌背图 | `cardBack` |
| 桌布 | `tablecloth/{id}` |
| 边框 | `tableedge/{id}` |

选中哪一张仍用 PlayerPrefs 存短 key。首次启动会把旧版 PlayerPrefs 里的 base64 图迁进 IndexedDB。单张自定义图上限 8MB。

在场景设置 → 牌面 → 标准麻将牌 中上传或恢复默认。
