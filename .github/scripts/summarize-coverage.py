#!/usr/bin/env python3
"""Print Coverlet / cobertura line coverage and optional GitHub outputs."""

from __future__ import annotations

import argparse
import os
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def write_output(name: str, value: str) -> None:
    print(f"{name}={value}")
    output = os.environ.get("GITHUB_OUTPUT")
    if not output:
        return
    with open(output, "a", encoding="utf-8") as handle:
        handle.write(f"{name}={value}\n")


def reports(root: Path) -> list[Path]:
    names = ("coverage.cobertura.xml", "Cobertura.xml", "coverage.xml")
    found: list[Path] = []
    for path in root.rglob("*"):
        if path.is_file() and path.name in names:
            found.append(path)
    return sorted(found)


def line_rate(path: Path) -> float | None:
    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError) as exc:
        print(f"Could not parse {path}: {exc}", file=sys.stderr)
        return None
    raw = root.attrib.get("line-rate") or root.attrib.get("line-rate".replace("-", "_"))
    if raw:
        return float(raw)
    lines = 0
    covered = 0
    for element in root.iter():
        if element.tag.endswith("line") or element.tag == "line":
            lines += 1
            hits = element.attrib.get("hits") or element.attrib.get("count") or "0"
            if float(hits) > 0:
                covered += 1
    if lines == 0:
        return None
    return covered / lines


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default="coverage")
    args = parser.parse_args()
    root = Path(args.root)
    files = reports(root) if root.exists() else []
    if not files:
        print(f"No coverage reports under {root}")
        write_output("line_rate", "n/a")
        return 0

    rates: list[float] = []
    for path in files:
        rate = line_rate(path)
        if rate is None:
            print(f"{path}: no line-rate")
            continue
        rates.append(rate)
        print(f"{path}: {rate * 100:.1f}% line coverage")

    if not rates:
        write_output("line_rate", "n/a")
        return 0

    average = sum(rates) / len(rates)
    write_output("line_rate", f"{average * 100:.1f}%")
    print(f"Average line coverage: {average * 100:.1f}%")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
