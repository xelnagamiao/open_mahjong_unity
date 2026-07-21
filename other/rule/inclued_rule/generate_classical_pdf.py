"""生成「古典麻将规则书」PDF。

在原有的「古典麻将规则.pdf」内容基础上，补充
- 庄家幺二（庄家收支双倍，副值不变）
- 连庄（庄家本局获胜则保留庄家位置）
两节内容。

执行：
    python other/rule/generate_classical_pdf.py
输出：
    other/rule/古典麻将规则.pdf
"""

from __future__ import annotations

import os

from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import cm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    Paragraph,
    SimpleDocTemplate,
    Spacer,
    Table,
    TableStyle,
)


def _register_chinese_font() -> tuple[str, str]:
    candidates = [
        ("MSYaHei", r"C:\Windows\Fonts\msyh.ttc", 0),
        ("MSYaHeiBold", r"C:\Windows\Fonts\msyhbd.ttc", 0),
    ]
    fallback = ("KongyuanSans", "open_mahjong_unity/Assets/font/空源黑体/Kongyuan Sans R.otf", None)

    regular = bold = None
    for name, path, idx in candidates:
        if os.path.exists(path):
            try:
                if idx is None:
                    pdfmetrics.registerFont(TTFont(name, path))
                else:
                    pdfmetrics.registerFont(TTFont(name, path, subfontIndex=idx))
            except Exception:
                continue
            if regular is None:
                regular = name
            else:
                bold = name
        if regular and bold:
            break

    if regular is None and os.path.exists(fallback[1]):
        pdfmetrics.registerFont(TTFont(fallback[0], fallback[1]))
        regular = fallback[0]
    if bold is None:
        bold = regular
    return regular, bold


def build_pdf(output_path: str) -> None:
    regular, bold = _register_chinese_font()

    doc = SimpleDocTemplate(
        output_path,
        pagesize=A4,
        leftMargin=2.0 * cm,
        rightMargin=2.0 * cm,
        topMargin=2.0 * cm,
        bottomMargin=2.0 * cm,
        title="古典麻将规则",
        author="open mahjong unity",
    )

    title_style = ParagraphStyle(
        "Title", fontName=bold, fontSize=22, leading=30, alignment=1, spaceAfter=4, textColor=colors.HexColor("#222831")
    )
    subtitle_style = ParagraphStyle(
        "Subtitle", fontName=regular, fontSize=11, leading=18, alignment=1, spaceAfter=14, textColor=colors.HexColor("#5d6d7e")
    )
    h1_style = ParagraphStyle(
        "H1", fontName=bold, fontSize=14, leading=22, spaceBefore=10, spaceAfter=4, textColor=colors.HexColor("#1f3a5f")
    )
    h2_style = ParagraphStyle(
        "H2", fontName=bold, fontSize=12, leading=18, spaceBefore=6, spaceAfter=2, textColor=colors.HexColor("#34495e")
    )
    body_style = ParagraphStyle(
        "Body", fontName=regular, fontSize=10.5, leading=17, spaceAfter=2, textColor=colors.HexColor("#1c1c1c")
    )
    bullet_style = ParagraphStyle(
        "Bullet", fontName=regular, fontSize=10.5, leading=17, leftIndent=12, bulletIndent=2, spaceAfter=2,
        textColor=colors.HexColor("#1c1c1c")
    )
    note_style = ParagraphStyle(
        "Note", fontName=regular, fontSize=9.5, leading=15, textColor=colors.HexColor("#7d8a99"), leftIndent=10
    )

    def bullet(text: str) -> Paragraph:
        return Paragraph(f"• {text}", bullet_style)

    def std_table(data, col_widths, header_color="#e6eef8", header_fg="#1f3a5f"):
        t = Table(data, colWidths=col_widths)
        t.setStyle(TableStyle([
            ("FONTNAME", (0, 0), (-1, -1), regular),
            ("FONTNAME", (0, 0), (-1, 0), bold),
            ("FONTSIZE", (0, 0), (-1, -1), 10.5),
            ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor(header_color)),
            ("TEXTCOLOR", (0, 0), (-1, 0), colors.HexColor(header_fg)),
            ("ALIGN", (0, 0), (-1, -1), "CENTER"),
            ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
            ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#f5f7fa")]),
            ("GRID", (0, 0), (-1, -1), 0.4, colors.HexColor("#c3cfe2")),
            ("LEFTPADDING", (0, 0), (-1, -1), 6),
            ("RIGHTPADDING", (0, 0), (-1, -1), 6),
            ("TOPPADDING", (0, 0), (-1, -1), 4),
            ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
        ]))
        return t

    story = []
    story.append(Paragraph("古典麻将规则", title_style))
    story.append(Paragraph("Open Mahjong · Classical Rule", subtitle_style))

    # 一、基础规则
    story.append(Paragraph("一、基础规则", h1_style))
    story.append(bullet("全副 136 张：万、筒、索（每色 1–9 各 4 张，共 108 张）；字牌东、南、西、北、中、发、白 各 4 张，共 28 张。"))
    story.append(bullet("四人对战，按东南西北门风入座；庄家起手 13 张 + 第一摸 1 张，闲家各 13 张。"))
    story.append(bullet("允许吃、碰、明杠、暗杠、加杠等副露动作。"))
    story.append(bullet("不设起和限制：任意副数（含 0 番）均可和牌。"))

    # 二、王张
    story.append(Paragraph("二、王张", h1_style))
    story.append(bullet("王张区固定 7 墩，共 14 张。"))
    story.append(bullet("牌墙摸至仅剩 14 张王张时禁止再开杠。"))
    story.append(bullet("牌摸尽后进入普通流局，仍照常进行数和尾结算。"))

    # 三、副
    story.append(Paragraph("三、副（基础分）", h1_style))
    story.append(Paragraph(
        "副是基础分。最终单家的「总副」由所有副值累加，再乘以番数倍率。同名副重复出现时按出现次数累计计算。",
        body_style,
    ))

    story.append(Paragraph("3.1 和牌方式副", h2_style))
    story.append(std_table([
        ["副名", "副值", "说明"],
        ["和牌", "10", "成功和牌的固定副值"],
        ["自摸", "2", "自己摸到和牌张"],
        ["边嵌吊", "2", "边张 / 嵌张 / 单钓将和牌"],
    ], [4 * cm, 3 * cm, 8 * cm]))
    story.append(Spacer(1, 4))

    story.append(Paragraph("3.2 刻子 / 杠子 / 番牌对副", h2_style))
    story.append(std_table([
        ["类别", "刻子", "暗刻", "明杠", "暗杠"],
        ["普通", "2", "4", "8", "16"],
        ["幺九", "4", "8", "16", "32"],
        ["番牌", "8", "16", "32", "64"],
    ], [3 * cm] * 5))
    story.append(bullet("番牌对（雀头为番牌）：2 副 / 对。"))
    story.append(bullet("幺九牌：1 万 / 9 万、1 筒 / 9 筒、1 索 / 9 索，以及全部字牌（字牌同时是番牌时按番牌计算，不再重复计为幺九）。"))
    story.append(bullet("番牌：中、发、白三元牌，以及该玩家自身门风（东 / 南 / 西 / 北 座对应的风牌）。"))
    story.append(bullet("点和（荣和）时，含和牌张的暗刻 / 暗杠会被自动降级为明刻 / 明杠（暗转明），副值改按对应明面值计算。"))

    # 四、番
    story.append(Paragraph("四、番（倍率）", h1_style))

    story.append(Paragraph("4.1 普通番（按 2 的幂次累乘）", h2_style))
    story.append(std_table([
        ["番名", "番数", "条件简述"],
        ["混一色", "1 番", "数牌仅一色，且包含字牌"],
        ["鸾凤和鸣（对对和）", "1 番", "全为刻子 / 杠子 + 一对雀头"],
        ["小三元", "2 番", "中发白其中两组刻 / 杠 + 另一组为雀头"],
        ["清一色", "3 番", "全部数牌且仅一色，无字牌"],
        ["字一色", "3 番", "全部由字牌组成"],
        ["岭上开花", "1 番", "杠后摸岭上牌即和（含杠上开花）"],
        ["海底捞月", "1 番", "牌墙最后一张和牌"],
        ["金鸡夺食", "1 番", "抢杠和（玩家加杠时被截胡）"],
    ], [4.5 * cm, 2.5 * cm, 8 * cm]))
    story.append(Spacer(1, 4))

    story.append(Paragraph("4.2 满贯番（一旦命中直接 300 副）", h2_style))
    story.append(std_table([
        ["番名", "条件简述"],
        ["大三元", "中、发、白 三组皆刻子或杠子"],
        ["大四喜", "东、南、西、北 四组皆刻子或杠子"],
        ["小四喜", "东南西北 三组刻 / 杠 + 一组为雀头"],
        ["天和", "庄家起手 14 张直接和牌"],
        ["地和", "闲家第一巡（庄家首张弃牌前）即和牌"],
        ["九莲宝灯", "清一色「1112345678999」型，听 9 面"],
        ["国士无双", "13 种幺九牌齐全且任一为对（开局即可宣告）"],
    ], [4.5 * cm, 10.5 * cm]))
    story.append(Paragraph(
        "命中任一满贯番时，结算总副直接判定为 300，其它普通番不再相乘叠加，最终番种栏仅显示满贯番名。",
        note_style,
    ))

    # 五、计分公式
    story.append(Paragraph("五、计分公式", h1_style))
    story.append(bullet("基础副 = 所有副值之和（和牌、自摸、边嵌吊、各刻杠、番牌对…全部累加）。"))
    story.append(bullet("总副 = min(基础副, 300) × 2<sup>普通番总数</sup>，并再次以 300 作为上限。"))
    story.append(bullet("命中任意满贯番时，总副直接判定为 300。"))
    story.append(bullet("自摸：其他三家各支付「总副」分给和牌者，和牌者实得「总副 × 3」。"))
    story.append(bullet("荣和：放铳者向和牌者支付「总副 × 3」分。"))

    # 六、流局
    story.append(Paragraph("六、流局方式", h1_style))
    story.append(Paragraph("6.1 普通流局（牌摸尽）", h2_style))
    story.append(Paragraph(
        "牌山摸至王张 14 张为止仍未有人和牌，本局结束。普通流局后仍然进行数和尾结算。",
        body_style,
    ))
    story.append(Paragraph("6.2 九老峰回（九种九牌）", h2_style))
    story.append(bullet("开局起手或第一巡，玩家手牌中存在 9 种或以上不同的幺九牌时，可宣告九老峰回。"))
    story.append(bullet("宣告成立后本局立即结束，不进行任何结算（包括数和尾），直接进入下一局。"))

    # 七、数和尾
    story.append(Paragraph("七、数和尾结算（每局必算 · 九老峰回除外）", h1_style))
    story.append(Paragraph(
        "无论本局以和牌还是普通流局结束，都对四家分别独立计副，再两两比较扣除分数：",
        body_style,
    ))
    story.append(bullet("未和牌玩家：仅按其副露 + 手牌中可成立的暗刻 / 番牌对计副。"))
    story.append(bullet("和牌玩家：另三家各向其支付和牌总副（涉及庄家翻倍）。"))
    story.append(bullet("未和牌者之间：除去和牌家后，余下三家两两比对；副更低者向副更高者支付副差。"))
    story.append(bullet("流局时：四家之间仅进行上述副差两两比对（无和牌收取环节）。"))
    story.append(bullet("九老峰回（九种九牌）发生时跳过本结算。"))

    # 八、连庄（新增）
    story.append(Paragraph("八、连庄", h1_style))
    story.append(Paragraph(
        "连庄即「庄家本局获胜则保留庄家位置，下一局继续坐庄」。当庄家自摸或荣和时触发连庄，"
        "圈内座位与门风全部保持不变，「当前圈局数 current_round」也不推进，仅以 round_index 自增记录连庄次数。",
        body_style,
    ))
    story.append(bullet("触发条件：庄家自摸 / 庄家荣和（任一种和牌方式且和牌者为庄家）。"))
    story.append(bullet("圈位不变：连庄期间不轮换座位，门风、自风保持不变；东家继续是东家。"))
    story.append(bullet("局数处理：current_round 保持不变，round_index 自增以记录连庄次数。"))
    story.append(bullet("非连庄局：庄家未和或闲家和牌时，正常进入下一局，由下家继位为庄家（座位轮转）。"))
    story.append(bullet("与「庄家幺二」的关系：连庄期间庄家继续享受收支 ×2 效果，对庄家而言是高风险高回报的对冲点。"))

    # 九、庄家幺二（新增）
    story.append(Paragraph("九、庄家幺二（庄家收支双倍）", h1_style))
    story.append(Paragraph(
        "古典麻将沿用传统「庄家幺二」规则：所有涉及庄家的收支均按 2 倍结算，而副数本身保持不变。"
        "即番倍计算、副值合计逻辑全部沿用第三/五章定义；仅在最终结算转账时，对涉及庄家的支付乘以 2。",
        body_style,
    ))
    story.append(bullet("庄家自摸：三家各按「2 × 总副」支付，庄家共得「6 × 总副」。"))
    story.append(bullet("庄家荣和：放铳者按「2 × 总副」支付（其他两家不参与本笔结算）。"))
    story.append(bullet("闲家自摸：庄家以「2 × 总副」支付，其余两位闲家按「1 × 总副」支付。"))
    story.append(bullet("闲家荣和：仅放铳者支付「1 × 总副」（与庄家无关）。"))
    story.append(bullet("数和尾结算：和牌家收取另三家各付和牌总副；余下三家按副差互结；涉及庄家时该笔转账翻倍。"))
    story.append(Paragraph(
        "实现细节请参见 ClassicalGameState.py 中的 _settle_shuhewei 与 actual_hu_score 计算分支。",
        note_style,
    ))

    story.append(Spacer(1, 6))
    story.append(Paragraph(
        "—— 文档自先行实现代码（classical_hepai_check.py / ClassicalGameState.py）整理生成 ——",
        note_style,
    ))

    doc.build(story)
    print(f"已生成: {output_path}")


if __name__ == "__main__":
    out_dir = os.path.dirname(os.path.abspath(__file__))
    out = os.path.join(out_dir, "古典麻将规则.pdf")
    build_pdf(out)
