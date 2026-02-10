#!/usr/bin/env python3
"""
Extract minimal mapping data from Unity .spriteLib files.

Output format (colon-delimited):
  library_name:animation_name:category_name:sprite_slice_name

Animation name is inferred from the sprite slice label by stripping a trailing
"_<digits>" suffix when present.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path
from typing import Iterable, Iterator, List, Sequence, Tuple

CAT_RE = re.compile(r"^\s{2}- m_Name:\s*(.+?)\s*$")
OVERRIDE_RE = re.compile(r"^\s{4}- m_Name:\s*(.+?)\s*$")
TRAILING_FRAME_RE = re.compile(r"^(.*)_\d+$")


def find_sprite_lib_files(paths: Sequence[str]) -> List[Path]:
    files: List[Path] = []
    for raw in paths:
        p = Path(raw)
        if p.is_file() and p.suffix == ".spriteLib":
            files.append(p)
            continue
        if p.is_dir():
            files.extend(sorted(p.rglob("*.spriteLib")))
            continue
        if any(ch in raw for ch in "*?[]"):
            files.extend(sorted(Path().glob(raw)))
    unique = []
    seen = set()
    for f in files:
        key = str(f.resolve())
        if key in seen:
            continue
        seen.add(key)
        unique.append(f)
    return unique


def parse_animation_name(slice_name: str) -> str:
    m = TRAILING_FRAME_RE.match(slice_name)
    if m:
        return m.group(1)
    return slice_name


def parse_sprite_lib(path: Path) -> Iterator[Tuple[str, str, str, str]]:
    library_name = path.stem
    current_category = ""
    in_overrides = False

    with path.open("r", encoding="utf-8") as f:
        for line in f:
            cat_match = CAT_RE.match(line)
            if cat_match:
                current_category = cat_match.group(1)
                in_overrides = False
                continue

            if current_category and line.strip() == "m_OverrideEntries:":
                in_overrides = True
                continue

            if not in_overrides:
                continue

            entry_match = OVERRIDE_RE.match(line)
            if not entry_match:
                continue

            sprite_slice_name = entry_match.group(1)
            animation_name = parse_animation_name(sprite_slice_name)
            yield library_name, animation_name, current_category, sprite_slice_name


def emit_rows(rows: Iterable[Tuple[str, str, str, str]], output: Path | None) -> int:
    count = 0
    out = sys.stdout if output is None else output.open("w", encoding="utf-8", newline="\n")
    try:
        for library_name, animation_name, category_name, sprite_slice_name in rows:
            out.write(f"{library_name}:{animation_name}:{category_name}:{sprite_slice_name}\n")
            count += 1
    finally:
        if output is not None:
            out.close()
    return count


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Extract compact mapping rows from Unity .spriteLib files")
    parser.add_argument(
        "inputs",
        nargs="*",
        default=["Assets/Sprites/SpriteLibraries"],
        help="Input .spriteLib file(s), directories, or globs (default: Assets/Sprites/SpriteLibraries)",
    )
    parser.add_argument("-o", "--output", help="Write output to a file instead of stdout")
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    files = find_sprite_lib_files(args.inputs)
    if not files:
        print("No .spriteLib files found for input(s).", file=sys.stderr)
        return 1

    def all_rows() -> Iterator[Tuple[str, str, str, str]]:
        for file_path in files:
            yield from parse_sprite_lib(file_path)

    output_path = Path(args.output) if args.output else None
    count = emit_rows(all_rows(), output_path)
    print(f"Extracted {count} rows from {len(files)} sprite library file(s).", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
