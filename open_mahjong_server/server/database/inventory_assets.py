"""Available store templates. Retired demonstration content is not reseeded."""

DEFAULT_AVATAR_FRAME_ID = 2201
B1_AVATAR_FRAME_ID = 2202

LEGACY_FRAME_DESCRIPTIONS = {
    'frame.a1': '淡香槟纯色填充，小圆角。默认使用的头像框。',
    'frame.b1': '冰青纯色填充，圆角更柔和。可随时切换使用。',
}

ASSETS = {
    'item.rename_credit': dict(label='改名卡', category='item', slot='', effect='rename_credit'),
    'frame.a1': dict(label='A1 · 淡香槟', category='cosmetic', slot='avatar_frame',
                     icon_path='image/Profiles/1', frame_color='#DCC7A0'),
    'frame.b1': dict(label='B1 · 冰青', category='cosmetic', slot='avatar_frame',
                     icon_path='image/Profiles/1', frame_color='#7ACDE4'),
}

SEEDS = [
    (3001, 'rename_coupon', '改名卡', '使用后增加 1 次账号改名机会，可在网页账号中心改名。', 'item.rename_credit', False, {}),
    # is_default denotes permanent availability; only A1 is the fallback equipment.
    (DEFAULT_AVATAR_FRAME_ID, 'avatar_frame_a1', 'A1 · 淡香槟', '淡香槟细边，柔和光泽，小圆角。默认使用的头像框。', 'frame.a1', True, {}),
    (B1_AVATAR_FRAME_ID, 'avatar_frame_b1', 'B1 · 冰青', '冰青细边，柔和光泽，圆角更柔和。可随时切换使用。', 'frame.b1', True, {}),
]
