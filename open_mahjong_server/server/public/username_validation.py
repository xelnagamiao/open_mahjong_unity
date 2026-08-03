"""Shared Unicode-aware username normalization and length validation."""

from __future__ import annotations

import unicodedata
from typing import Optional


MAX_CODE_POINTS = 16
MIN_DISPLAY_LENGTH = 2
MAX_DISPLAY_LENGTH = 20

_WIDE_RANGES = (
    (0x1100, 0x11FF),
    (0x2E80, 0x303F),
    (0x3040, 0x30FF),
    (0x3100, 0x318F),
    (0x31A0, 0x31BF),
    (0x31F0, 0x31FF),
    (0x3400, 0x4DBF),
    (0x4E00, 0x9FFF),
    (0xA960, 0xA97F),
    (0xAC00, 0xD7AF),
    (0xD7B0, 0xD7FF),
    (0xF900, 0xFAFF),
    (0xFE10, 0xFE6F),
    (0xFF01, 0xFF60),
    (0xFF61, 0xFF9F),
    (0xFFE0, 0xFFE6),
    (0x20000, 0x323AF),
)


def normalize_username(value: object) -> str:
    """Return the canonical username representation stored by the services."""
    if value is None:
        return ""
    return unicodedata.normalize("NFC", str(value)).strip()


def _is_wide_username_character(char: str) -> bool:
    code_point = ord(char)
    return any(start <= code_point <= end for start, end in _WIDE_RANGES)


def username_display_length(username: str) -> int:
    """Measure CJK/kana/Hangul/fullwidth as 2 and combining marks as 0."""
    length = 0
    for char in username:
        if unicodedata.category(char).startswith("M"):
            continue
        length += 2 if _is_wide_username_character(char) else 1
    return length


def validate_username(username: object) -> Optional[str]:
    """Validate a username after NFC normalization, returning a user-facing error."""
    name = normalize_username(username)
    if not name:
        return "用户名不能为空"
    if len(name) > MAX_CODE_POINTS:
        return f"用户名不能超过{MAX_CODE_POINTS}个字符"
    for char in name:
        if unicodedata.category(char) in {"Cc", "Cf", "Cs", "Zl", "Zp"}:
            return "用户名不能包含控制字符或不可见格式字符"
    length = username_display_length(name)
    if length < MIN_DISPLAY_LENGTH:
        return "用户名长度至少需要2（中日韩及全角字符=2，其他字符=1）"
    if length > MAX_DISPLAY_LENGTH:
        return "用户名显示长度不能超过20"
    return None
