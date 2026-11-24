"""Render the published ApexPack benchmark CSV as GitHub-ready PNG panels."""

import argparse
import csv
from collections import defaultdict
from math import isfinite, log2
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


PANELS = {
    "Tier 1": ("basics", "基本型・文字列"),
    "Tier 2": ("models", "継承・入れ子・複合モデル"),
    "Tier 3": ("large", "大容量データ・.NET 11 固有ケース"),
    "Shape": ("shapes", "値型・参照型の単体と配列"),
    "Collection": ("collections", "配列・リスト・辞書"),
    "Graph": ("graphs", "木・共有・循環参照"),
    "Nullability": ("nullability", "参照型の null 許容性"),
}
METRICS = (
    ("直列化時間", "ApexSerializeNs", "MemoryPackSerializeNs"),
    ("逆直列化時間", "ApexDeserializeNs", "MemoryPackDeserializeNs"),
    ("出力サイズ", "ApexOutputBytes", "MemoryPackOutputBytes"),
)
RUNTIMES = (9, 10, 11)
ONLY_NET11 = {"T3_06_FullJoin", "T3_07_BFloat16Array", "T3_08_ComplexDecimal128Array"}
PAPER = "#F2F6FA"
INK = "#132940"
MUTED = "#475F78"
WIDTH = 1560
LEFT = 42
DATA_X = 516
CELL = 110


def load_cases(path):
    panels = defaultdict(dict)
    versions = {}
    with path.open(encoding="utf-8-sig", newline="") as source:
        for row in csv.DictReader(source):
            major = int(row["Runtime"].split(".")[0])
            if major not in RUNTIMES or versions.setdefault(major, row["Runtime"]) != row["Runtime"]:
                raise ValueError(f"Unexpected runtime {row['Runtime']}")
            case_id, category = row["ScenarioId"], row["Category"]
            label, entries = panels[category].setdefault(case_id, (row["Scenario"], {}))
            if major in entries or label != row["Scenario"]:
                raise ValueError(f"Duplicate running condition or inconsistent label: {case_id}")
            entries[major] = tuple(float(row[apex]) / float(row[memory]) for _, apex, memory in METRICS)
            if any(not isfinite(value) or value <= 0 for value in entries[major]):
                raise ValueError(f"Invalid ratio: {case_id}")
    if set(panels) != set(PANELS) or set(versions) != set(RUNTIMES):
        raise ValueError("The benchmark categories or .NET versions changed")
    seen = set()
    for cases in panels.values():
        for case_id, (_, entries) in cases.items():
            expected = {11} if case_id in ONLY_NET11 else set(RUNTIMES)
            if set(entries) != expected or case_id in seen:
                raise ValueError(f"Unexpected coverage: {case_id}")
            seen.add(case_id)
    return panels, versions


def wrap(text, font, max_width):
    lines = []
    while font.getlength(text) > max_width:
        end = max(index for index in range(1, len(text)) if font.getlength(text[:index]) <= max_width)
        space = text.rfind(" ", 0, end + 1)
        if space >= end // 2:
            end = space
        lines.append(text[:end].rstrip())
        text = text[end:].lstrip()
    return [*lines, text]


def blend(target, amount):
    return tuple(round(248 + (component - 248) * amount) for component in target)


def cell_style(value):
    intensity = min(abs(log2(value)) / 2, 1)
    if 0.995 <= value < 1.005:
        return "#E9EFF4", INK
    if value < 1:
        return blend((143, 209, 194), 0.34 + 0.66 * intensity), "#073F38"
    return blend((250, 182, 133), 0.34 + 0.66 * intensity), "#622709"


def format_ratio(value):
    if value < 0.01:
        return "<0.01"
    if value >= 100:
        return f"{value:,.0f}"
    return f"{value:.2f}"


def draw_panel(path, title, cases, versions, fonts):
    heading, label, number, small = fonts
    rows = [(wrap(name, label, DATA_X - LEFT - 32), values) for name, values in cases.values()]
    heights = [max(46, 26 * len(lines) + 14) for lines, _ in rows]
    image = Image.new("RGB", (WIDTH, 232 + sum(heights) + 46), PAPER)
    draw = ImageDraw.Draw(image)
    draw.text((LEFT, 27), title, font=heading, fill=INK)
    draw.text((LEFT, 83), "ApexPack ÷ MemoryPack    |    MemoryPack = 1.00", font=label, fill=MUTED)
    draw.text((LEFT, 125), "シナリオ", font=number, fill=INK)
    for group, major in enumerate(RUNTIMES):
        start = DATA_X + group * 3 * CELL
        draw.rounded_rectangle((start + 3, 119, start + 3 * CELL - 4, 166), 8, fill="#16344F")
        draw.text((start + 1.5 * CELL, 142), f".NET {versions[major]}", font=number, fill="#FFFFFF", anchor="mm")
        for column, (metric, _, _) in enumerate(METRICS):
            draw.text((start + (column + 0.5) * CELL, 191), metric, font=small, fill=MUTED, anchor="mm")

    y = 217
    for row_index, ((lines, values), height) in enumerate(zip(rows, heights, strict=True)):
        if row_index % 2 == 0:
            draw.rounded_rectangle((LEFT - 9, y, WIDTH - 40, y + height - 2), 6, fill="#FFFFFF")
        for line_index, line in enumerate(lines):
            draw.text((LEFT, y + height / 2 + (line_index - (len(lines) - 1) / 2) * 26),
                      line, font=label, fill=INK, anchor="lm")
        for group, major in enumerate(RUNTIMES):
            start = DATA_X + group * 3 * CELL
            if major not in values:
                draw.rounded_rectangle((start + 4, y + 4, start + 3 * CELL - 5, y + height - 6), 6, fill="#E5EBF1")
                draw.text((start + 1.5 * CELL, y + height / 2), "対象外", font=small, fill=MUTED, anchor="mm")
                continue
            for column, value in enumerate(values[major]):
                x = start + column * CELL
                background, foreground = cell_style(value)
                draw.rounded_rectangle((x + 4, y + 4, x + CELL - 5, y + height - 6), 6, fill=background)
                draw.text((x + CELL / 2, y + height / 2), format_ratio(value),
                          font=number, fill=foreground, anchor="mm")
        y += height

    draw.text((LEFT, y + 21), "1.00 未満は ApexPack が短時間・小容量   /   1.00 超は MemoryPack が短時間・小容量",
              font=small, fill=MUTED)
    image.save(path, optimize=True)


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--csv", type=Path, default=root / "docs/data/runtime-benchmarks.csv")
    parser.add_argument("--output", type=Path, default=root / "docs/images")
    parser.add_argument("--fonts", type=Path, default=Path("C:/Windows/Fonts"))
    args = parser.parse_args()
    regular = str(args.fonts / "BIZ-UDGothicR.ttc")
    bold = str(args.fonts / "BIZ-UDGothicB.ttc")
    fonts = tuple(ImageFont.truetype(font, points) for font, points in
                  ((bold, 42), (regular, 22), (bold, 22), (regular, 18)))
    panels, versions = load_cases(args.csv)
    args.output.mkdir(parents=True, exist_ok=True)
    for category, (slug, title) in PANELS.items():
        draw_panel(args.output / f"runtime-benchmarks-{slug}.png", title, panels[category], versions, fonts)
    print(f"Rendered {sum(len(cases) for cases in panels.values())} scenarios in {len(panels)} panels")


if __name__ == "__main__":
    main()
