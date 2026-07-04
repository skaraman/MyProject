from __future__ import annotations

import argparse
import copy
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path


CATEGORY_RE = re.compile(r"^  - m_Name:\s*(.*?)\s*$")
ENTRY_RE = re.compile(r"^    - m_Name:\s*(.*?)\s*$")
HASH_RE = re.compile(r"^\s*m_Hash:\s*(-?\d+)\s*$")
TRAILING_NUMBER_RE = re.compile(r"^(.+)_\d+$")
INVALID_FILE_NAME_CHARS = re.compile(r'[<>:"/\\|?*]')


@dataclass
class SpriteLabel:
    name: str
    hash_text: str = "0"
    raw_lines: list[str] = field(default_factory=list)

    def clone(self) -> "SpriteLabel":
        return copy.deepcopy(self)


@dataclass
class SpriteCategory:
    name: str
    hash_text: str = "0"
    entries: list[SpriteLabel] = field(default_factory=list)

    def clone_empty(self) -> "SpriteCategory":
        return SpriteCategory(
            name=self.name,
            hash_text=self.hash_text,
            entries=[],
        )


@dataclass
class SpriteLibrary:
    header_lines: list[str]
    categories: list[SpriteCategory]
    line_ending: str


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Split a Unity .spriteLib into one library per label prefix.",
    )
    parser.add_argument("source", type=Path)
    parser.add_argument("--out-dir", type=Path)
    parser.add_argument("--part-name")
    parser.add_argument("--overwrite", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--drop-empty-categories", action="store_true")
    return parser.parse_args()


def parse_sprite_library(path: Path) -> SpriteLibrary:
    text = path.read_text(encoding="utf-8-sig")
    line_ending = "\r\n" if "\r\n" in text else "\n"
    lines = text.splitlines()
    library_index = find_library_index(lines)

    if library_index < 0:
        raise ValueError(f"Missing m_Library block: {path}")

    header_lines = lines[: library_index + 1]
    categories: list[SpriteCategory] = []
    index = library_index + 1

    while index < len(lines):
        if not CATEGORY_RE.match(lines[index]):
            index += 1
            continue

        block_start = index
        index += 1

        while index < len(lines) and not CATEGORY_RE.match(lines[index]):
            index += 1

        block_lines = lines[block_start:index]
        category = parse_category_block(block_lines)
        categories.append(category)

    return SpriteLibrary(
        header_lines=header_lines,
        categories=categories,
        line_ending=line_ending,
    )


def find_library_index(lines: list[str]) -> int:
    for index, line in enumerate(lines):
        if line == "  m_Library:":
            return index

        if line == "  m_Library: []":
            return index

    return -1


def parse_category_block(lines: list[str]) -> SpriteCategory:
    name_match = CATEGORY_RE.match(lines[0])
    category_name = ""

    if name_match:
        category_name = name_match.group(1).strip()

    category = SpriteCategory(name=category_name)

    for line in lines[1:]:
        hash_match = HASH_RE.match(line)

        if hash_match:
            category.hash_text = hash_match.group(1)
            break

    index = 0

    while index < len(lines):
        if not ENTRY_RE.match(lines[index]):
            index += 1
            continue

        entry_start = index
        index += 1

        while index < len(lines):
            if ENTRY_RE.match(lines[index]):
                break

            if is_category_trailer_line(lines[index]):
                break

            index += 1

        entry_lines = lines[entry_start:index]
        entry = parse_entry_block(entry_lines)
        category.entries.append(entry)

    return category


def parse_entry_block(lines: list[str]) -> SpriteLabel:
    name_match = ENTRY_RE.match(lines[0])
    label_name = ""

    if name_match:
        label_name = name_match.group(1).strip()

    label = SpriteLabel(
        name=label_name,
        raw_lines=list(lines),
    )

    for line in lines:
        hash_match = HASH_RE.match(line)

        if hash_match:
            label.hash_text = hash_match.group(1)
            break

    return label


def is_category_trailer_line(line: str) -> bool:
    if line.startswith("    m_FromMain:"):
        return True

    if line.startswith("    m_EntryOverrideCount:"):
        return True

    return False


def label_group_name(label_name: str) -> str:
    match = TRAILING_NUMBER_RE.match(label_name)

    if match:
        return match.group(1)

    return label_name


def derive_part_name(source_path: Path) -> str:
    stem = source_path.stem

    if stem.lower().startswith("gear"):
        stem = stem[4:]

    if not stem:
        stem = source_path.stem

    return to_snake_name(stem)


def to_snake_name(value: str) -> str:
    value = value.strip()
    value = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", "_", value)
    value = re.sub(r"(?<=[A-Z])(?=[A-Z][a-z])", "_", value)
    value = re.sub(r"[^A-Za-z0-9]+", "_", value)
    value = value.strip("_")
    value = value.lower()

    if value:
        return value

    return "unnamed"


def clean_file_stem(value: str) -> str:
    value = INVALID_FILE_NAME_CHARS.sub("_", value)
    value = to_snake_name(value)
    return value


def make_output_stem(group_name: str, part_name: str) -> str:
    group_stem = clean_file_stem(group_name)
    part_stem = clean_file_stem(part_name)

    if part_stem:
        return f"{group_stem}_{part_stem}"

    return group_stem


def collect_group_names(library: SpriteLibrary) -> list[str]:
    names: list[str] = []
    seen: set[str] = set()

    for category in library.categories:
        for entry in category.entries:
            group_name = label_group_name(entry.name)
            key = group_name.lower()

            if key in seen:
                continue

            seen.add(key)
            names.append(group_name)

    return names


def split_categories(
    library: SpriteLibrary,
    group_name: str,
    keep_empty_categories: bool,
) -> list[SpriteCategory]:
    categories: list[SpriteCategory] = []

    for source_category in library.categories:
        target_category = source_category.clone_empty()

        for entry in source_category.entries:
            entry_group_name = label_group_name(entry.name)

            if entry_group_name.lower() != group_name.lower():
                continue

            target_category.entries.append(entry.clone())

        if target_category.entries:
            categories.append(target_category)
            continue

        if keep_empty_categories:
            categories.append(target_category)

    return categories


def render_library(
    header_lines: list[str],
    categories: list[SpriteCategory],
    line_ending: str,
) -> str:
    lines = list(header_lines)
    library_index = find_library_index(lines)

    if library_index >= 0:
        if categories:
            lines[library_index] = "  m_Library:"
        else:
            lines[library_index] = "  m_Library: []"

    for category in categories:
        lines.extend(render_category(category))

    return line_ending.join(lines) + line_ending


def render_category(category: SpriteCategory) -> list[str]:
    lines = [
        f"  - m_Name: {category.name}",
        f"    m_Hash: {category.hash_text or '0'}",
        "    m_CategoryList: []",
    ]

    if category.entries:
        lines.append("    m_OverrideEntries:")

        for entry in category.entries:
            lines.extend(render_entry(entry))
    else:
        lines.append("    m_OverrideEntries: []")

    lines.append("    m_FromMain: 0")
    lines.append(f"    m_EntryOverrideCount: {len(category.entries)}")
    return lines


def render_entry(entry: SpriteLabel) -> list[str]:
    if entry.raw_lines:
        return list(entry.raw_lines)

    return [
        f"    - m_Name: {entry.name}",
        f"      m_Hash: {entry.hash_text or '0'}",
        "      m_Sprite: {fileID: 0}",
        "      m_FromMain: 0",
        "      m_SpriteOverride: {fileID: 0}",
    ]


def write_split_libraries(
    source_path: Path,
    out_dir: Path,
    part_name: str,
    overwrite: bool,
    dry_run: bool,
    keep_empty_categories: bool,
) -> tuple[int, int]:
    library = parse_sprite_library(source_path)
    group_names = collect_group_names(library)
    reserved_stems: dict[str, str] = {}
    written = 0
    skipped = 0

    for group_name in group_names:
        output_stem = make_output_stem(group_name, part_name)
        output_key = output_stem.lower()

        if output_key in reserved_stems:
            other_group = reserved_stems[output_key]
            raise ValueError(f"Output name collision: {other_group} and {group_name}")

        reserved_stems[output_key] = group_name

    if not dry_run:
        out_dir.mkdir(parents=True, exist_ok=True)

    for group_name in group_names:
        output_stem = make_output_stem(group_name, part_name)
        output_path = out_dir / f"{output_stem}{source_path.suffix}"

        if output_path.resolve() == source_path.resolve():
            raise ValueError(f"Refusing to overwrite source: {output_path}")

        if output_path.exists() and not overwrite:
            skipped += 1
            continue

        categories = split_categories(
            library=library,
            group_name=group_name,
            keep_empty_categories=keep_empty_categories,
        )
        text = render_library(
            header_lines=library.header_lines,
            categories=categories,
            line_ending=library.line_ending,
        )
        written += 1

        if dry_run:
            entry_count = count_entries(categories)
            print(f"{output_path} entries={entry_count}")
            continue

        output_path.write_text(text, encoding="utf-8")

    return written, skipped


def collect_source_paths(source_path: Path) -> list[Path]:
    if source_path.is_file():
        if source_path.suffix.lower() != ".spritelib":
            raise ValueError(f"Expected a .spriteLib file: {source_path}")

        return [source_path]

    if not source_path.is_dir():
        raise FileNotFoundError(f"Missing source: {source_path}")

    paths: list[Path] = []

    for candidate in sorted(source_path.rglob("*.spriteLib")):
        if is_in_split_folder(candidate, source_path):
            continue

        paths.append(candidate.resolve())

    return paths


def is_in_split_folder(path: Path, root_path: Path) -> bool:
    relative_path = path.relative_to(root_path)
    folder_parts = relative_path.parts[:-1]

    for part in folder_parts:
        if part.lower().endswith("_split"):
            return True

    return False


def make_source_out_dir(
    source_path: Path,
    requested_out_dir: Path | None,
    source_root: Path | None,
) -> Path:
    if requested_out_dir is None:
        return source_path.parent / f"{source_path.stem}_split"

    if source_root is None:
        return requested_out_dir

    relative_parent = source_path.parent.relative_to(source_root)
    return requested_out_dir / relative_parent / f"{source_path.stem}_split"


def count_entries(categories: list[SpriteCategory]) -> int:
    total = 0

    for category in categories:
        total += len(category.entries)

    return total


def main() -> int:
    args = parse_args()
    source_path = args.source.resolve()
    requested_out_dir = args.out_dir

    if requested_out_dir is not None:
        requested_out_dir = requested_out_dir.resolve()

    if source_path.is_dir():
        source_root: Path | None = source_path
    else:
        source_root = None

    keep_empty_categories = not args.drop_empty_categories

    try:
        source_paths = collect_source_paths(source_path)
    except Exception as exc:
        print(str(exc), file=sys.stderr)
        return 1

    if not source_paths:
        print(f"No .spriteLib files found: {source_path}", file=sys.stderr)
        return 1

    total_written = 0
    total_skipped = 0

    for sprite_lib_path in source_paths:
        out_dir = make_source_out_dir(
            source_path=sprite_lib_path,
            requested_out_dir=requested_out_dir,
            source_root=source_root,
        )
        out_dir = out_dir.resolve()
        part_name = args.part_name or derive_part_name(sprite_lib_path)

        try:
            written, skipped = write_split_libraries(
                source_path=sprite_lib_path,
                out_dir=out_dir,
                part_name=part_name,
                overwrite=args.overwrite,
                dry_run=args.dry_run,
                keep_empty_categories=keep_empty_categories,
            )
        except Exception as exc:
            print(f"{sprite_lib_path}: {exc}", file=sys.stderr)
            return 1

        total_written += written
        total_skipped += skipped

    print(f"processed={len(source_paths)} written={total_written} skipped={total_skipped}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
