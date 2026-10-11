"""只读校验共享规则支持表的固定格式和运行时配表一致性；无需 Excel 依赖。"""
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[2]
WORKBOOK = ROOT / "other/麻将规则支持表.xlsx"
HEADERS = ["规则序号", "主规则", "首页显示名", "子规则名", "数据统计名", "规则标识", "是否提供匹配",
           "基础统计", "番种统计", "数据统计类型", "备注"]
NS = {"s": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}


def validate(path=WORKBOOK):
    rules = json.loads((ROOT / "open_mahjong_server/server/database/statistics_rules.json").read_text(encoding="utf-8"))["rules"]
    by_sub = {row["sub_rule"]: row for row in rules}
    with ZipFile(path) as archive:
        # 固定的一页模板，表头位于 B5:L5，左侧和顶部保留视觉留白。
        book = ET.fromstring(archive.read("xl/workbook.xml"))
        sheets = book.findall("s:sheets/s:sheet", NS)
        assert len(sheets) == 1 and sheets[0].get("name") == "子规则统计", "保留单页子规则统计模板"
        sheet = ET.fromstring(archive.read("xl/worksheets/sheet1.xml"))
        strings = []
        if "xl/sharedStrings.xml" in archive.namelist():
            strings = ["".join(item.itertext()) for item in ET.fromstring(archive.read("xl/sharedStrings.xml"))]
        cells = {}
        for cell in sheet.findall("s:sheetData/s:row/s:c", NS):
            value = cell.find("s:v", NS)
            kind = cell.get("t")
            assert kind != "e", f"单元格 {cell.get('r')} 含公式错误"
            if kind == "inlineStr":
                text = "".join(cell.find("s:is", NS).itertext())
            elif kind == "s":
                text = strings[int(value.text)]
            else:
                text = "" if value is None else value.text or ""
            cells[cell.get("r")] = text
        assert cells.get("B2") == "麻将规则支持表", "表格标题不得改变"
        assert [cells.get(f"{chr(66+c)}5", "") for c in range(11)] == HEADERS, "必须保留十一列名称和顺序"
        tables = [name for name in archive.namelist() if re.fullmatch(r"xl/tables/table\d+\.xml", name)]
        assert len(tables) == 1, "在现有原生表格中增删整行"
        table = ET.fromstring(archive.read(tables[0]))
        extent = re.fullmatch(r"B5:L(\d+)", table.get("ref", ""))
        assert extent, "保留从 B5 开始的十一列表格"
        last = int(extent[1])
        assert [c.get("name") for c in table.findall("s:tableColumns/s:tableColumn", NS)] == HEADERS
        assert table.find("s:autoFilter", NS) is not None, "保留表头筛选"
        assert [cells.get(f"B{r}") for r in range(6, last + 1)] == [str(r-5) for r in range(6,last+1)], "规则序号须从 1 逐行连续编号"
        rows = [[cells.get(f"{chr(67+c)}{r}", "") for c in range(10)] for r in range(6, last + 1)]
        assert len(rows) == len(by_sub), "表格行数必须覆盖当前全部子规则"
        assert len({r[4] for r in rows}) == len(rows), "每个子规则只占独立一行"
        assert {r[4] for r in rows} == set(by_sub), "规则标识必须与统计配表一致"
        main_names = {}
        for row in rows:
            rule = by_sub[row[4]]
            assert row[0] and row[2], f"{row[4]} 缺少主规则或子规则名"
            assert main_names.setdefault(rule["rule"], row[0]) == row[0], f"{row[4]} 主规则分组不一致"
            assert row[1] == (rule["home_label"] if rule["home_header"] else ""), f"{row[4]} 首页显示名不一致"
            assert row[3] == rule["stats_name"], f"{row[4]} 数据统计名不一致"
            assert row[5] == ("是" if rule["matchmaking"] else "否"), f"{row[4]} 匹配必须填是／否并与配表一致"
            assert row[6:8] == ["有" if rule[k] else "无" for k in ("basic_stats", "fan_stats")], f"{row[4]} 统计能力不一致"
            modes = rule["custom_modes"] + rule["ranked_modes"]
            assert row[8] == " ".join(f"[{mode}]" for mode in modes), f"{row[4]} 必须列全支持统计的局制"
        pane = sheet.find("s:sheetViews/s:sheetView/s:pane", NS)
        assert pane is not None and pane.get("ySplit") == "5" and float(pane.get("xSplit", "0")) == 0, "只冻结表头，不冻结子规则列"
        for merged in sheet.findall("s:mergeCells/s:mergeCell", NS):
            assert merged.get("ref") in ("B2:K2", "B3:L3"), "禁止合并规则数据行"
    return {"主规则": len(main_names), "子规则": len(rows), "匹配": sum(r[5] == "是" for r in rows),
            "基础统计": sum(r[6] == "有" for r in rows), "番种统计": sum(r[7] == "有" for r in rows)}


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    try:
        print("规则支持表校验通过：" + json.dumps(validate(), ensure_ascii=False))
    except (AssertionError, OSError, KeyError, ValueError) as exc:
        print(f"规则支持表校验失败：{exc}", file=sys.stderr)
        sys.exit(1)
