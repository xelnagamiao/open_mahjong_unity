Taisei 角色头像裁图
==================

本目录是包体外的备用素材，不属于 Unity 的 Assets、Resources 或构建场景依赖。
512/ 和 1024/ 中的 PNG 仅保留在本机，由 .gitignore 排除，不随仓库下载。
本目录的清单、裁切记录、来源和许可证继续提交；换电脑时可从本地开发素材包恢复 PNG。
正式使用的灵梦和琪露诺头像仍在 Assets/Resources/image/Profiles/ 中随项目提交。
共 11 位当前角色，每位均提供保留透明通道的正方形 PNG：

512/   游戏内头像尺寸，512 × 512。
1024/  高清备用尺寸，1024 × 1024。
avatar-catalog.json  原图地址、作者、许可、原图哈希、裁切位置及输出哈希。
Licenses/           上游许可声明和 CC BY 4.0 正文。
LICENSE.txt         本目录头像的独立许可及适用范围。

角色：博丽灵梦、雾雨魔理沙、魂魄妖梦、永江衣玖、艾丽、胡桃、
琪露诺、键山雏、莉格露·奈特巴格、冈崎梦美、Scuttle。

当前游戏只接入两张新头像；原有默认头像仍保留：
2302 博丽灵梦：512/reimu.png → Assets/Resources/image/Profiles/2302.png
2303 琪露诺：512/cirno.png → Assets/Resources/image/Profiles/2303.png
其余九位仅备用，未新增游戏物品、背包选项或 Resources 资源。

裁图方式
--------
从项目现有角色原画截取头肩正方形区域，再等比例缩小，不重绘、不换色，
保留原画的透明通道。灵梦、琪露诺使用与现有 Sprite 完全一致的取景区域，
只将原本依赖完整立绘的头像换成独立小图。所有原始立绘保持不变。
裁切坐标以原始 PNG 像素为准；清单同时记录左上角与 Unity 左下角坐标。
各角色的头饰、帽子及姿势不同，取景留白随原画调整，避免强行拉伸人物。

来源与署名
----------
画师：afensorm，受 Taisei Project 委托创作。
画师主页：https://afens.surge.sh / https://pixiv.me/afens
Taisei Project：https://github.com/taisei-project/taisei
源文件仓库：https://github.com/taisei-project/taisei-rawmedia
源版本：d3dd520f3f1a18ee958cd60d4bf2c88ea93530c6
源目录：gfx/dialog/afens/；每张原图的精确链接见 avatar-catalog.json。
许可：Creative Commons Attribution 4.0 International（CC BY 4.0）。
许可地址：https://creativecommons.org/licenses/by/4.0/
上游明确的角色立绘许可声明见 Licenses/Taisei-COPYING.txt。

分发本目录的图像或其衍生物时，应保留作者、原始来源、许可链接和修改说明。
本套的修改为头像裁切与等比例缩小；进一步修改时请补充说明。
东方 Project 相关角色归 ZUN / 上海爱丽丝幻乐团所有，Taisei 中另有原创角色。
本套不表示原作者背书；美术许可不转让角色本身的权利。
东方二次创作指引：https://touhou-project.news/guideline/
