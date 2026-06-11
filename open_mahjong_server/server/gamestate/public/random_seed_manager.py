"""随机种子计算器。"""

import hashlib
import secrets
from typing import Tuple, Optional


MASTER_SEED_BITS = 256
SALT_BITS = 128


def generate_master_seed() -> int:
    """
    生成主种子。
    
    Returns:
        master_seed: int (256 bit)
    """
    return secrets.randbits(MASTER_SEED_BITS)


def generate_salt() -> str:
    """
    生成盐字符串。
    
    Returns:
        salt: str (128 bit represented as hex string)
    """
    return secrets.token_hex(SALT_BITS // 8)


def compute_commitment(master_seed: int, salt: str) -> int:
    """
    计算承诺值。
    
    Returns:
        commitment: int (256 bit)
    """
    combined = (format(master_seed, '064x') + salt).encode("utf-8")
    return int(hashlib.sha256(combined).hexdigest(), 16)


def derive_round_seed(master_seed: int, round_number: int) -> int:
    """
    从主种子和局号派生局种子。

    Returns:
        round_seed: int (256 bit)
    """
    combined = (format(master_seed, '064x') + str(round_number)).encode("utf-8")
    return int(hashlib.sha256(combined).hexdigest(), 16)


def validate_master_seed_hex(seed_hex: str) -> bool:
    """验证 hex 字符串是否为合法主种子。"""
    if not seed_hex or len(seed_hex) != MASTER_SEED_BITS // 4:
        return False
    try:
        int(seed_hex, 16)
        return True
    except ValueError:
        return False


def parse_user_master_seed(raw) -> int:
    """
    解析复式（玩家指定）主种子。
    0 表示未指定；非 0 必须为 256 位，以 64 位十六进制字符串提交。
    """
    if raw is None:
        return 0
    if isinstance(raw, bool):
        raise ValueError("随机种子类型无效")
    if isinstance(raw, int):
        if raw == 0:
            return 0
        raise ValueError(f"主种子必须为{MASTER_SEED_BITS // 4}位十六进制字符串")
    text = str(raw).strip()
    if text == "" or text == "0":
        return 0
    if text.lower().startswith("0x"):
        text = text[2:]
    text = text.lower()
    hex_len = MASTER_SEED_BITS // 4
    if len(text) != hex_len:
        raise ValueError(f"主种子必须为{hex_len}位十六进制字符串")
    if not all(c in "0123456789abcdef" for c in text):
        raise ValueError("主种子必须为十六进制字符（0-9、a-f）")
    seed = int(text, 16)
    if seed.bit_length() > MASTER_SEED_BITS:
        raise ValueError(f"主种子不能超过{MASTER_SEED_BITS}位")
    return seed


def verify_commitment(master_seed: int, salt: str, commitment: int) -> bool:
    """验证承诺值。"""
    return compute_commitment(master_seed, salt) == commitment


def verify_round_seed(master_seed: int, round_number: int, expected_round_seed: int) -> bool:
    """验证局种子。"""
    return derive_round_seed(master_seed, round_number) == expected_round_seed


def setup_random_seed_system(user_seed: Optional[int] = None) -> Tuple[int, str, int, bool]:
    """
    完整的随机种子系统初始化。
    
    Returns:
        (master_seed: int, salt: str, commitment: int, is_player_set: bool)
    """
    if user_seed is not None and user_seed != 0:
        if user_seed.bit_length() > MASTER_SEED_BITS:
            raise ValueError(f"主种子不能超过{MASTER_SEED_BITS}位")
        master_seed = user_seed
        is_player_set = True
    else:
        master_seed = generate_master_seed()
        is_player_set = False
    
    salt = generate_salt()
    commitment = compute_commitment(master_seed, salt)
    
    return master_seed, salt, commitment, is_player_set
