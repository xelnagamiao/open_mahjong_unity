"""生成「中国麻将（小林改版）」规则书 PDF。

读取同目录下的 .doc（OLE 二进制格式），抽取核心条目并润色后用 reportlab 排版输出。
若需重新生成，直接执行：
    python other/rule/generate_kobayashi_pdf.py
输出文件：other/rule/中国麻将（小林改版）规则书.pdf
"""

from __future__ import annotations

import os
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import cm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    PageBreak,
    Paragraph,
    SimpleDocTemplate,
    Spacer,
    Table,
    TableStyle,
)


def _register_chinese_font() -> tuple[str, str]:
    """注册中文字体。优先使用微软雅黑 .ttc，其次回退到 Kongyuan."""
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
        bold = regular  # 没有粗体时退回常规
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
        title="中国麻将（小林改版）规则书",
        author="open mahjong unity",
    )

    title_style = ParagraphStyle(
        "Title", fontName=bold, fontSize=22, leading=30, alignment=1, spaceAfter=14, textColor=colors.HexColor("#222831")
    )
    subtitle_style = ParagraphStyle(
        "Subtitle", fontName=regular, fontSize=11, leading=18, alignment=1, spaceAfter=14, textColor=colors.HexColor("#5d6d7e")
    )
    h1_style = ParagraphStyle(
        "H1", fontName=bold, fontSize=15, leading=22, spaceBefore=14, spaceAfter=6, textColor=colors.HexColor("#1f3a5f")
    )
    body_style = ParagraphStyle(
        "Body", fontName=regular, fontSize=11, leading=18, spaceAfter=4, textColor=colors.HexColor("#1c1c1c")
    )
    note_style = ParagraphStyle(
        "Note", fontName=regular, fontSize=10, leading=16, textColor=colors.HexColor("#7d8a99"), leftIndent=10
    )

    story = []
    story.append(Paragraph("中国麻将（小林改版）规则书", title_style))
    story.append(Paragraph("Open Mahjong Unity · 内部公测版", subtitle_style))
    story.append(Paragraph(
        "本规则书面向「小林改版」中国麻将的修订条款。整体仍沿用国标麻将（MCR）的番种与计分体系，"
        "仅对若干强弱失衡或规则不清的条目进行平衡。文档内容仍处于公测阶段，欢迎提出意见与建议。",
        body_style,
    ))

    # 1. 总则
    story.append(Paragraph("一、设计目标", h1_style))
    story.append(Paragraph(
        "以 8 分为一档进行小幅度调整：削弱过强或过大的番种、加强过小或几乎不出现的番种，使整套番表的赋分更接近赔率与稀有度的对应关系。"
        "目前已在 Xe 麻将平台上线，部分番种的判定仍沿用标准 MCR 定义。",
        body_style,
    ))

    # 2. 番值调整
    story.append(Paragraph("二、番值与番种调整", h1_style))

    adjust_data = [
        ["条目", "改动方向", "改后取值"],
        ["大三元", "下调", "88 → 64"],
        ["小三元", "下调", "64 → 32"],
        ["混一色", "上调", "6 → 12"],
        ["全带幺", "上调", "4 → 6"],
        ["一般高", "上调", "1 → 2"],
        ["三色双龙会", "上调", "16 → 24"],
        ["双箭刻", "上调", "6 → 12"],
        ["三风刻", "上调", "12 → 24"],
    ]
    table = Table(adjust_data, colWidths=[5 * cm, 4 * cm, 6 * cm])
    table.setStyle(TableStyle([
        ("FONTNAME", (0, 0), (-1, -1), regular),
        ("FONTNAME", (0, 0), (-1, 0), bold),
        ("FONTSIZE", (0, 0), (-1, -1), 11),
        ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#e6eef8")),
        ("TEXTCOLOR", (0, 0), (-1, 0), colors.HexColor("#1f3a5f")),
        ("ALIGN", (0, 0), (-1, -1), "CENTER"),
        ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
        ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#f5f7fa")]),
        ("GRID", (0, 0), (-1, -1), 0.4, colors.HexColor("#c3cfe2")),
        ("LEFTPADDING", (0, 0), (-1, -1), 8),
        ("RIGHTPADDING", (0, 0), (-1, -1), 8),
        ("TOPPADDING", (0, 0), (-1, -1), 6),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
    ]))
    story.append(table)
    story.append(Spacer(1, 6))

    # 3. 移除番种
    story.append(Paragraph("三、移除的番种", h1_style))
    story.append(Paragraph(
        "下述番种因与其他番种存在过度重合或区分度不足，统一从番表中移除，不再计入和牌番数：",
        body_style,
    ))
    for line in (
        "・自摸（已并入主胡牌的结算流程，自摸不额外计番）",
        "・不求人（与门清/自摸语义重复）",
        "・幺九刻（与全带幺等番种联动后过强）",
        "・和绝张（判定边界含糊，难以稳定判定）",
        "・全双刻（数据上几乎不出现且与七对/碰碰胡等已有番种重合，未来版本删除）",
    ):
        story.append(Paragraph(line, body_style))

    # 4. 平和与边坎吊
    story.append(Paragraph("四、平和与边坎吊", h1_style))
    story.append(Paragraph(
        "平和的判定允许叠加无字（未实装）；同时支持多面听上的边坎吊单独识别——只胡实际上可以胡的一张时，记 2 分（未实装）。",
        body_style,
    ))

    # 5. 杠系
    story.append(Paragraph("五、杠系番种", h1_style))
    story.append(Paragraph(
        "杠系按「杠数 × 杠类 × 暗刻」三维度进行重写（未实装），确保不同组合下都拥有清晰且稳定的赋分：",
        body_style,
    ))
    for line in (
        "・一杠：明杠 1 分 / 暗杠 2 分（按杠类计）",
        "・两杠：先按杠数取 4 分，再叠加杠类与暗刻数",
        "・两杠（双明杠）：4 分；双明杠 + 暗杠 6 分；双暗杠 8 分",
        "・三杠 / 四杠：先按杠数计基础分，再依据杠类与暗刻数叠加",
    ):
        story.append(Paragraph(line, body_style))
    story.append(Paragraph(
        "在公测期间，杠系的具体赋分仍以服务器实际计算结果为准。",
        note_style,
    ))

    # 6. 天地人和
    story.append(Paragraph("六、新增「天地人和」", h1_style))
    story.append(Paragraph(
        "天地人和：在某些版本的小林改中作为对开局极端运气状态的补偿赋分，统一计 8 分（未实装）。",
        body_style,
    ))

    # 7. 公测说明
    story.append(Paragraph("七、公测与反馈", h1_style))
    story.append(Paragraph(
        "本规则书所列条款大多已在 Xe 平台公测，但仍有部分项目标注「未实装」，会在后续版本逐步上线。"
        "如发现规则与实际行为不一致，或对某条调整有更合适的赋分意见，欢迎在 Open Mahjong Unity 项目交流群中反馈。",
        body_style,
    ))

    doc.build(story)
    print(f"已生成: {output_path}")


if __name__ == "__main__":
    out_dir = os.path.dirname(os.path.abspath(__file__))
    out = os.path.join(out_dir, "中国麻将（小林改版）规则书.pdf")
    build_pdf(out)
