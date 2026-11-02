"""从规则支持表生成 Web 筛选名称和顺序；--check 用于检查同步状态。

主规则按表中首次出现的顺序，子规则按表中行序。修改规则表后运行此脚本。
仅导出显示目录，不改变统计规则标识、能力或首页统计目录。
"""
import argparse
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[2]
WORKBOOK = ROOT / 'other/麻将规则支持表.xlsx'
TARGET = ROOT / 'open_mahjong_web/shared/ruleDisplayCatalog.json'
NS = {'s': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}


def build_catalog():
    rules = json.loads((ROOT / 'open_mahjong_server/server/database/statistics_rules.json').read_text(encoding='utf-8'))['rules']
    by_sub = {r['sub_rule']: r for r in rules}
    with ZipFile(WORKBOOK) as archive:
        strings = []
        if 'xl/sharedStrings.xml' in archive.namelist():
            strings = [''.join(item.itertext()) for item in ET.fromstring(archive.read('xl/sharedStrings.xml'))]
        sheet = ET.fromstring(archive.read('xl/worksheets/sheet1.xml'))
        groups, seen = {}, set()
        for row in sheet.findall('s:sheetData/s:row', NS):
            if int(row.get('r')) < 6:
                continue
            cells = {}
            for cell in row.findall('s:c', NS):
                value = cell.find('s:v', NS)
                text = '' if value is None else value.text or ''
                if cell.get('t') == 'inlineStr':
                    text = ''.join(cell.find('s:is', NS).itertext())
                elif cell.get('t') == 's':
                    text = strings[int(text)]
                cells[re.sub(r'\d+', '', cell.get('r'))] = text
            sub = cells.get('G')
            if not sub:
                continue
            assert sub in by_sub and sub not in seen, f'未知或重复的规则标识：{sub}'
            assert cells.get('C') and cells.get('E'), f'{sub} 缺少主规则或子规则名'
            seen.add(sub)
            rule = by_sub[sub]['rule']
            group = groups.setdefault(rule, {'rule': rule, 'label': cells['C'], 'subRules': []})
            assert group['label'] == cells['C'], f'{rule} 主规则名不一致'
            group['subRules'].append({'sub_rule': sub, 'label': cells['E']})
        assert seen == set(by_sub), '规则支持表必须覆盖统计目录的全部子规则'
    return {
        'source': 'other/麻将规则支持表.xlsx',
        'generated_by': 'open_mahjong_server/scripts/sync_rule_display_catalog.py',
        'rules': list(groups.values()),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    catalog = build_catalog()
    if args.check:
        assert json.loads(TARGET.read_text(encoding='utf-8')) == catalog, 'Web 规则显示目录与规则支持表不一致，请运行本脚本同步'
        print('Rule display catalog matches the support workbook.')
    else:
        TARGET.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        print(f'Exported {len(catalog["rules"])} rule families.')


if __name__ == '__main__':
    main()
