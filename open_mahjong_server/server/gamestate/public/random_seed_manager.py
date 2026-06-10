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
        master_seed = user_seed
        is_player_set = True
    else:
        master_seed = generate_master_seed()
        is_player_set = False
    
    salt = generate_salt()
    commitment = compute_commitment(master_seed, salt)
    
    return master_seed, salt, commitment, is_player_set
