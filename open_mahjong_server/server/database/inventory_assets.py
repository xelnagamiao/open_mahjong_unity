"""Available store templates. Retired demonstration content is not reseeded."""

DEFAULT_AVATAR_ID = 2301
DEFAULT_AVATAR_FRAME_ID = 0
A1_AVATAR_FRAME_ID = 2201
B1_AVATAR_FRAME_ID = 2202

LEGACY_FRAME_DESCRIPTIONS = {
    'frame.a1': ('淡香槟纯色填充，小圆角。默认使用的头像框。',
                 '淡香槟细边，柔和光泽，小圆角。默认使用的头像框。'),
    'frame.b1': ('冰青纯色填充，圆角更柔和。可随时切换使用。',
                 '冰青细边，柔和光泽，圆角更柔和。可随时切换使用。'),
}

LEGACY_FRAME_NAMES = {
    'frame.a1': 'A1 · 淡香槟',
    'frame.b1': 'B1 · 冰青',
}

ASSETS = {
    'avatar.default': dict(label='默认头像', category='cosmetic', slot='avatar', icon_path='image/Profiles/1'),
    'avatar.reimu': dict(label='博丽灵梦', category='cosmetic', slot='avatar', icon_path='image/Profiles/2302'),
    'avatar.cirno': dict(label='琪露诺', category='cosmetic', slot='avatar', icon_path='image/Profiles/2303'),
    'item.rename_credit': dict(label='改名卡', category='item', slot='', effect='rename_credit'),
    'item.grass_tile': dict(label='草牌', category='item', slot='', effect='collectible',
                            icon_path='image/Inventory/grass_tile'),
    'item.good_tile': dict(label='好牌', category='item', slot='', effect='collectible',
                           icon_path='image/Inventory/good_tile'),
    'frame.a1': dict(label='金橙色', category='cosmetic', slot='avatar_frame',
                     icon_path='image/Profiles/1', frame_color='#EFC23D'),
    'frame.b1': dict(label='青色', category='cosmetic', slot='avatar_frame',
                     icon_path='image/Profiles/1', frame_color='#7ACDE4'),
}

SEEDS = [
    (DEFAULT_AVATAR_ID, 'avatar_default', '默认头像', '', 'avatar.default', True, {}),
    (2302, 'avatar_reimu', '博丽灵梦', 'afensorm / Taisei Project · CC BY 4.0', 'avatar.reimu', True, {}),
    (2303, 'avatar_cirno', '琪露诺', 'afensorm / Taisei Project · CC BY 4.0', 'avatar.cirno', True, {}),
    (3001, 'rename_coupon', '改名卡', '在网页账号中心修改用户名，改名成功时消耗 1 张；无需提前使用或兑换。', 'item.rename_credit', False, {}),
    (3011, 'grass_tile', '草牌', '表达牛魔的小礼物，没有实际用途，当你打牌遇到了大牛魔事件时随机掉落～', 'item.grass_tile', False, {}),
    (3012, 'good_tile', '好牌', '表达心意的小礼物，没有实际用途，在玩家qq群互动或者发送Xe来点好牌、码龙来点好牌 随机掉落～', 'item.good_tile', False, {}),
    # is_default denotes permanent availability; item 0 retains the original unadorned frame.
    (A1_AVATAR_FRAME_ID, 'avatar_frame_a1', '金橙色', '金橙色细边，柔和光泽', 'frame.a1', True, {}),
    (B1_AVATAR_FRAME_ID, 'avatar_frame_b1', '青色', '青色细边，柔和光泽', 'frame.b1', True, {}),
]
