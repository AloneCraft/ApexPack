"""Render the published ApexPack benchmark CSV as GitHub-ready PNG panels."""

import argparse
import csv
from collections import defaultdict
from html import escape
from math import isfinite
import os
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


PANELS = {
    "Tier 1": ("basics", "Basic types and strings"),
    "Tier 2": ("models", "Inheritance and nested objects"),
    "Tier 3": ("large", "Large payloads and .NET 11 features"),
    "Shape": ("shapes", "Value types and reference types"),
    "Collection": ("collections", "Arrays, lists, and dictionaries"),
    "Graph": ("graphs", "Trees, shared references, and cycles"),
    "Nullability": ("nullability", "Nullable references"),
}
METRICS = (
    ("Write time", "ApexSerializeNs", "MemoryPackSerializeNs"),
    ("Read time", "ApexDeserializeNs", "MemoryPackDeserializeNs"),
    ("Output size", "ApexOutputBytes", "MemoryPackOutputBytes"),
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


def scenario_name(case_id, original):
    if not case_id.startswith("Compare_"):
        return original
    _, category, kind, *detail = case_id.split("_")
    if category == "Shape":
        name = {
            "Struct16": "16-byte struct", "Struct128": "128-byte struct",
            "Class16": "16-byte class", "Class128": "128-byte class",
            "StructShortText": "Struct with a short string", "StructLongText": "Struct with a long string",
            "ClassShortText": "Class with a short string", "ClassLongText": "Class with a long string",
        }[kind]
        return name if detail[0] == "Single" else f"{name} array ({int(detail[0][5:]):,})"
    if category == "Collection":
        value = {"Int32": "int", "Class16": "16-byte class"}[detail[0]]
        if kind == "Array":
            name = f"{value}[]"
        elif kind in {"List", "LinkedList"}:
            name = f"{kind}<{value}>"
        else:
            name = f"{kind}<int, {value}>"
        return f"{name} ({int(detail[1]):,})"
    if category == "Graph":
        name = {"Chain": "Chain", "Tree": "Binary tree", "Shared": "Shared references", "Cycle": "Cycle"}[kind]
        return f"{name} ({int(detail[0]):,} nodes)"
    return {
        "RequiredText": "Required string, all values present",
        "OptionalText": "Nullable string, all values present",
        "OptionalText33": "Nullable string, 33% null",
        "OptionalText100": "Nullable string, 100% null",
        "RequiredChild": "Required child object, all values present",
        "OptionalChild": "Nullable child object, all values present",
    }[kind]


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
            ratios = tuple(float(row[apex]) / float(row[memory]) for _, apex, memory in METRICS)
            if any(not isfinite(ratio) or ratio <= 0 for ratio in ratios):
                raise ValueError(f"Invalid ratio: {case_id}")
            entries[major] = tuple(100 * (1 - ratio) for ratio in ratios)
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


def format_change(value):
    return "0%" if abs(value) < 0.05 else f"{value:+.1f}%"


def draw_change(draw, x, y, height, value, font):
    mid = x + CELL / 2
    baseline_y = y + height / 2 + 10
    color = INK
    if abs(value) >= 0.05:
        color = "#067063" if value > 0 else "#AC430A"
    draw.rounded_rectangle((x + 4, y + 3, x + CELL - 5, y + height - 5), 6, fill="#E6EDF4")
    draw.text((mid, baseline_y - 11), format_change(value), font=font, fill=color, anchor="ms")
    endpoint = mid + max(-1, min(1, value / 100)) * (CELL / 2 - 11)
    draw.line((x + 11, baseline_y, x + CELL - 11, baseline_y), fill="#C4D0DD", width=2)
    if round(mid) != round(endpoint):
        draw.line((mid, baseline_y, endpoint, baseline_y), fill=color, width=6)
    draw.line((mid, baseline_y - 5, mid, baseline_y + 5), fill="#52697F", width=1)


def draw_panel(path, title, cases, versions, fonts):
    heading, label, number, small = fonts
    rows = [(wrap(scenario_name(case_id, name), label, DATA_X - LEFT - 32), values)
            for case_id, (name, values) in cases.items()]
    heights = [max(46, 26 * len(lines) + 14) for lines, _ in rows]
    image = Image.new("RGB", (WIDTH, 232 + sum(heights) + 46), PAPER)
    draw = ImageDraw.Draw(image)
    draw.text((LEFT, 27), title, font=heading, fill=INK)
    draw.text((LEFT, 83), "Improvement over MemoryPack (%)", font=label, fill=MUTED)
    draw.text((LEFT, 125), "Scenario", font=number, fill=INK)
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
                draw.text((start + 1.5 * CELL, y + height / 2), "Not applicable", font=small, fill=MUTED, anchor="mm")
                continue
            for column, value in enumerate(values[major]):
                draw_change(draw, start + column * CELL, y, height, value, small)
        y += height

    draw.text((LEFT, y + 21), "0% = no change.   Right = less time or fewer bytes.   Left = more time or bytes.   Bars stop at 100%.",
              font=small, fill=MUTED)
    image.save(path, optimize=True)


def write_text_results(path, panels, versions):
    lines = [
        "# Benchmark results by scenario", "",
        "Positive percentages mean ApexPack takes less time or produces fewer bytes than MemoryPack.",
        "Negative percentages mean ApexPack takes more time or produces more bytes. 0% means no change at the displayed precision.",
        "Write time means serialization. Read time means deserialization.", "",
        "[Raw measurements and memory allocations (CSV)](data/runtime-benchmarks.csv)", "",
    ]
    columns = ["Scenario", *(f".NET {versions[major]} {metric.lower()}"
                              for major in RUNTIMES for metric, _, _ in METRICS)]
    for category, (_, title) in PANELS.items():
        lines.extend((f"## {title}", "", "| " + " | ".join(columns) + " |",
                      "| " + " | ".join(["---", *["---:"] * (len(columns) - 1)]) + " |"))
        for case_id, (name, values) in panels[category].items():
            row = [escape(scenario_name(case_id, name)).replace("|", r"\|")]
            for major in RUNTIMES:
                row.extend(map(format_change, values[major]) if major in values else ["N/A"] * len(METRICS))
            lines.append("| " + " | ".join(row) + " |")
        lines.append("")
    path.write_text("\n".join(lines), encoding="utf-8")


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--csv", type=Path, default=root / "docs/data/runtime-benchmarks.csv")
    parser.add_argument("--output", type=Path, default=root / "docs/images")
    parser.add_argument("--fonts", type=Path, default=Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts")
    parser.add_argument("--text-output", type=Path, default=root / "docs/benchmark-results.md")
    args = parser.parse_args()
    regular = str(args.fonts / "BIZ-UDGothicR.ttc")
    bold = str(args.fonts / "BIZ-UDGothicB.ttc")
    fonts = tuple(ImageFont.truetype(font, points) for font, points in
                  ((bold, 42), (regular, 22), (bold, 22), (regular, 18)))
    panels, versions = load_cases(args.csv)
    args.output.mkdir(parents=True, exist_ok=True)
    for category, (slug, title) in PANELS.items():
        draw_panel(args.output / f"runtime-benchmarks-{slug}.png", title, panels[category], versions, fonts)
    write_text_results(args.text_output, panels, versions)
    print(f"Rendered {sum(len(cases) for cases in panels.values())} scenarios in {len(panels)} panels")


if __name__ == "__main__":
    main()
