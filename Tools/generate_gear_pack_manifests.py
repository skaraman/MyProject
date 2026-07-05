import argparse
import json
import re
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path


GEAR_PARTS_PATH = Path("Assets/Scripts/Data/Esperanza/GearParts.cs")
GEAR_LIBRARY_ROOT = Path("Assets/Sprites/SpriteLibraries/Esperanza/Gear")
DEFAULT_EXTERNAL_ROOT_NAME = "MyProjectContent"


@dataclass
class GearEntry:
    key: str
    parts: list[str]


def normalize_key(value: str) -> str:
    if value.startswith("Gear"):
        return value[4:]
    return value


def pascal_to_snake(value: str) -> str:
    return re.sub(r"(?<!^)([A-Z])", r"_\1", value).lower()


def project_asset_path(path: Path) -> str:
    return path.as_posix()


def read_gear_entries(project_root: Path) -> list[GearEntry]:
    source = project_root / GEAR_PARTS_PATH
    text = source.read_text(encoding="utf-8")
    pattern = re.compile(
        r'\{\s*"([^"]+)"\s*,\s*new List<string>\s*\{([^}]*)\}\s*\}'
    )
    entries: list[GearEntry] = []
    for match in pattern.finditer(text):
        key = match.group(1)
        parts = re.findall(r'"([^"]+)"', match.group(2))
        entries.append(GearEntry(key, parts))
    return entries


def slice_entries(
    entries: list[GearEntry],
    start_key: str,
    end_key: str,
) -> list[GearEntry]:
    start = normalize_key(start_key)
    end = normalize_key(end_key)
    start_index = next(index for index, entry in enumerate(entries) if entry.key == start)
    end_index = next(index for index, entry in enumerate(entries) if entry.key == end)
    return entries[start_index:end_index + 1]


def find_library_path(
    project_root: Path,
    form: str,
    code: str,
    part: str,
) -> Path | None:
    prefix = f"{form.lower()}_{code.lower()}"
    part_name = pascal_to_snake(part)
    folder_name = f"Gear{part}_split"
    base_path = project_root / GEAR_LIBRARY_ROOT / folder_name / f"{prefix}_{part_name}"

    sheet_path = base_path.with_suffix(".spriteSheetLib")
    if sheet_path.exists():
        return sheet_path

    legacy_path = base_path.with_suffix(".spriteLib")
    if legacy_path.exists():
        return legacy_path

    return None


def collect_sources(project_root: Path, entry: GearEntry) -> list[str]:
    form, code, _slot = entry.key.split("_", 2)
    sources: list[str] = []
    for part in entry.parts:
        library_path = find_library_path(project_root, form, code, part)
        if not library_path:
            continue
        relative_path = library_path.relative_to(project_root)
        sources.append(project_asset_path(relative_path))
    return sources


def target_folder_for(asset_path: str) -> str:
    parent = Path(asset_path).parent.as_posix()
    if parent.startswith("Assets/"):
        return parent[len("Assets/"):]
    return parent


def make_manifest(
    pack_id: str,
    sources: list[str],
    source_revision: str,
) -> dict:
    return {
        "packId": pack_id,
        "kind": "pack",
        "ownedRoots": sources,
        "ownedLocations": [],
        "ownedEnemyTypes": [],
        "dialogIds": [],
        "authoringSources": [
            {
                "sourceType": "sprite_library",
                "assetPath": source,
                "label": "",
                "targetFolder": target_folder_for(source),
            }
            for source in sources
        ],
        "exportedFromProject": "MyProject",
        "sourceRevision": source_revision,
    }


def folder_meta_text(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "folderAsset: yes\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def text_meta_text(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "TextScriptImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def write_meta_if_missing(path: Path, text_factory) -> None:
    if path.exists():
        return
    path.write_text(text_factory(uuid.uuid4().hex), encoding="utf-8")


def write_pack_manifest(
    external_root: Path,
    pack_id: str,
    sources: list[str],
    source_revision: str,
) -> None:
    pack_root = external_root / pack_id
    manifest_path = pack_root / "ContentPackManifest.json"
    pack_root.mkdir(parents=True, exist_ok=True)

    manifest = make_manifest(pack_id, sources, source_revision)
    manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )

    write_meta_if_missing(pack_root.with_suffix(".meta"), folder_meta_text)
    write_meta_if_missing(manifest_path.with_suffix(".json.meta"), text_meta_text)


def build_pack_manifests(
    project_root: Path,
    external_root: Path,
    start_key: str,
    end_key: str,
    write: bool,
) -> None:
    entries = read_gear_entries(project_root)
    selected_entries = slice_entries(entries, start_key, end_key)
    source_revision = "manual:" + datetime.now(timezone.utc).isoformat(timespec="seconds")

    created: list[str] = []
    existing: list[str] = []
    skipped_empty: list[str] = []

    for entry in selected_entries:
        pack_id = "Gear" + entry.key
        manifest_path = external_root / pack_id / "ContentPackManifest.json"
        if manifest_path.exists():
            existing.append(pack_id)
            continue

        sources = collect_sources(project_root, entry)
        if not sources:
            skipped_empty.append(pack_id)
            continue

        created.append(pack_id)
        if write:
            write_pack_manifest(external_root, pack_id, sources, source_revision)

    mode = "created" if write else "would_create"
    print(f"{mode}={len(created)}")
    print(f"existing={len(existing)}")
    print(f"skipped_no_libraries={len(skipped_empty)}")
    if skipped_empty:
        print("skipped:")
        for pack_id in skipped_empty:
            print(f"- {pack_id}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--project-root", default=".")
    parser.add_argument("--external-root", default="")
    parser.add_argument("--start", default="GearBolt_aa_Feet")
    parser.add_argument("--end", default="GearFire_ac_Shoulders")
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()

    project_root = Path(args.project_root).resolve()
    external_root = Path(args.external_root).resolve() if args.external_root else (
        project_root.parent / DEFAULT_EXTERNAL_ROOT_NAME
    )

    build_pack_manifests(
        project_root,
        external_root,
        args.start,
        args.end,
        args.write,
    )


if __name__ == "__main__":
    main()
