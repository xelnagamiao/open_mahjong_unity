# -*- coding: utf-8 -*-
from pathlib import Path
import re

for f in Path(r"d:\open_mahjong_unity\open_mahjong_server\server\gamestate").rglob("wait_action.py"):
    t = f.read_text(encoding="utf-8")
    # Fix broken escaped quotes from bad regex replacement
    t2 = t.replace(r'getattr(self, \"_cp_active\", False)', 'getattr(self, "_cp_active", False)')
    # Remove unused had_claim_protection assignment before finalize
    t2 = re.sub(
        r"\n([ \t]*)had_claim_protection = getattr\(self, \"_cp_active\", False\)\n"
        r"([ \t]*)await finalize_claim_protection",
        r"\n\1await finalize_claim_protection",
        t2,
    )
    # Update obsolete comment
    t2 = t2.replace(
        "荣和：先把暂存出牌发给受保护观众，再按 cut 揭示时刻 + gap 等待后进入结算。",
        "荣和：先把暂存出牌发给受保护观众；gap 由 outbound_pipe 处理，主循环不阻塞。",
    )
    f.write_text(t2, encoding="utf-8")
    print(f, "ok" if t2 != t else "unchanged")

# jiandan unused vars
jf = Path(r"d:\open_mahjong_unity\open_mahjong_server\server\gamestate\game_jiandan\JiandanGameState.py")
jt = jf.read_text(encoding="utf-8")
jt2 = re.sub(
    r"\n([ \t]*)had_claim_protection = bool\(getattr\(self, \"_cp_active\", False\)\)\n"
    r"([ \t]*)await self\.finish_claim_protection\(\)\n"
    r"([ \t]*)has_claim = any\(\n"
    r"([\s\S]*?)"
    r"([ \t]*)\)\n"
    r"([ \t]*)# 受保护观众后续帧走 outbound_pipe，此处不再全局 sleep\n",
    r"\n\1await self.finish_claim_protection()\n"
    r"\1# 受保护观众后续帧走 outbound_pipe，此处不再全局 sleep\n",
    jt,
)
if jt2 != jt:
    jf.write_text(jt2, encoding="utf-8")
    print("jiandan cleaned")
else:
    print("jiandan unchanged")
