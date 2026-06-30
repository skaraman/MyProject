from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

from data import parse_sprite_library

# python .\Tools\SpriteLibraryMultiEditor\create_empty_label_libraries.py .\Assets\Sprites\SpriteLibraries\Items\Items.spriteLib

EMPTY_LIBRARY_TEXT = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &1
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: a5e6fedc2472449cead18ef23b5cb30d, type: 3}
  m_Name: 
  m_EditorClassIdentifier: Unity.2D.Animation.Runtime::UnityEngine.U2D.Animation.SpriteLibrarySourceAsset
  m_Library: []
"""


INVALID_FILE_NAME_CHARS = re.compile(r'[<>:"/\\|?*]')


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("--out-dir", type=Path)
    parser.add_argument("--overwrite", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    return parser.parse_args()


def clean_name(value: str) -> str:
    value = value.strip()
    value = INVALID_FILE_NAME_CHARS.sub("_", value)
    return value


def make_library_name(category_name: str, label_name: str) -> str:
    category_name = clean_name(category_name)
    label_name = clean_name(label_name)
    return f"{label_name}_{category_name}.spriteLib"


def make_library_path(out_dir: Path, library_name: str) -> Path:
    library_folder = Path(library_name).stem
    return out_dir / library_folder / library_name


def collect_output_paths(source_path: Path, out_dir: Path) -> list[Path]:
    document = parse_sprite_library(source_path)
    paths: list[Path] = []
    seen: set[str] = set()

    for category in document.categories:
        for label in category.entries:
            name = make_library_name(category.name, label.name)
            key = name.lower()

            if key in seen:
                continue

            seen.add(key)
            paths.append(make_library_path(out_dir, name))

    return paths


def write_empty_library(path: Path) -> None:
    path.write_text(EMPTY_LIBRARY_TEXT, encoding="utf-8")


def create_libraries(
    source_path: Path,
    out_dir: Path,
    overwrite: bool,
    dry_run: bool,
) -> tuple[int, int]:
    created = 0
    skipped = 0
    paths = collect_output_paths(source_path, out_dir)

    if not dry_run:
        out_dir.mkdir(parents=True, exist_ok=True)

    for path in paths:
        if path.exists() and not overwrite:
            skipped += 1
            continue

        created += 1

        if dry_run:
            print(path)
            continue

        path.parent.mkdir(parents=True, exist_ok=True)
        write_empty_library(path)

    return created, skipped


def main() -> int:
    args = parse_args()
    source_path = args.source.resolve()
    out_dir = args.out_dir or source_path.parent
    out_dir = out_dir.resolve()

    if not source_path.exists():
        print(f"Missing source: {source_path}", file=sys.stderr)
        return 1

    created, skipped = create_libraries(
        source_path,
        out_dir,
        args.overwrite,
        args.dry_run,
    )

    print(f"created={created} skipped={skipped}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
