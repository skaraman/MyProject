from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable


PACKAGE_NAME = "com.skaraman.myprojectcontent"
DEFAULT_EXTERNAL_ROOT_NAME = "MyProjectContent"
CUSTOM_LIBRARY_EXTENSION = ".spriteSheetLib"
LEGACY_LIBRARY_EXTENSION = ".spriteLib"
UNITY_LOCK_EXIT_CODE = 3

KIND_FOLDERS = {
    "pack": "",
    "core": "Core",
    "form": "Forms",
    "gear": "Gears",
    "slice": "Slices",
    "episode": "Episodes",
}

PACK_TYPE_LABELS = {
    "pack": "Pack",
    "core": "Core",
    "form": "Form",
    "gear": "Gear",
    "slice": "Slice",
    "episode": "Episode",
}

PACK_LABEL_TO_KIND = {label: kind for kind, label in PACK_TYPE_LABELS.items()}

SOURCE_TYPE_LABELS = {
    "sprite_sheet": "Sprite Sheet",
    "sprite_library": "Sprite Library",
    "sprite_slice": "Sprite Slice",
}

SOURCE_LABEL_TO_TYPE = {label: key for key, label in SOURCE_TYPE_LABELS.items()}

FORM_CHARACTER_SOURCE_FOLDERS = (
    "Blast",
    "Block",
    "Breathe",
    "Dance",
    "Dodge",
    "Effects",
    "Expressions",
    "Hurt",
    "Jump",
    "JumpDouble",
    "JumpFalling",
    "JumpLanding",
    "KickLeft",
    "KickRight",
    "PunchLeft",
    "PunchRight",
    "Run",
    "Sprint",
    "Stance",
    "To",
    "Walk",
    "_Bounces",
)

@dataclass
class SourceAssetSpec:
    source_type: str
    asset_path: str
    target_folder: str
    label: str = ""
    library_name: str = ""
    category: str = ""
    label_prefix: str = ""
    normal_asset_path: str = ""

    def source_label(self) -> str:
        if self.label:
            return f"{self.asset_path}[{self.label}]"
        if self.source_type == "sprite_sheet":
            parts = [self.library_name, self.category, self.label_prefix]
            detail = "/".join(part for part in parts if part)
            if detail:
                return f"{self.asset_path} [{detail}]"
        return self.asset_path

    def row_values(self) -> tuple[str, str, str, str]:
        label = self.label
        if self.source_type == "sprite_sheet":
            label = self.label_prefix
        return (
            source_type_label(self.source_type),
            self.asset_path,
            label,
            self.target_folder,
        )


@dataclass
class PackOption:
    pack_type: str
    pack_id: str
    source_assets: list[str] = field(default_factory=list)
    authoring_sources: list[SourceAssetSpec] = field(default_factory=list)
    target_root: str = ""
    unity_asset_root: str = ""
    status: str = ""
    details: list[str] = field(default_factory=list)
    source_revision: str = ""

    def haystack(self) -> str:
        values = [
            self.pack_type,
            self.pack_id,
            self.target_root,
            self.unity_asset_root,
            self.status,
            self.source_revision,
            *self.source_assets,
            *(source.source_label() for source in self.authoring_sources),
            *self.details,
        ]
        return "\n".join(value for value in values if value).lower()

    def row_values(self) -> tuple[str, str, str, str, str]:
        return (
            self.pack_type,
            self.pack_id,
            summarize(self.source_assets),
            self.target_root,
            self.status,
        )

    def detail_text(self) -> str:
        lines = [
            f"Pack type: {self.pack_type}",
            f"Target id: {self.pack_id}",
            f"Status: {self.status or '(unknown)'}",
            f"Target root: {self.target_root or '(none)'}",
            f"Unity asset root: {self.unity_asset_root or '(none)'}",
        ]
        if self.source_revision:
            lines.append(f"Source revision: {self.source_revision}")
        lines.append("")
        lines.append("Authoring sources:")
        if self.authoring_sources:
            lines.extend(
                f"- {source_type_label(source.source_type)}: "
                f"{source.source_label()} -> {source.target_folder or '(target folder not set)'}"
                for source in self.authoring_sources
            )
        else:
            lines.append("- (none declared in manifest)")
        lines.append("")
        lines.append("Inferred source assets:")
        if self.source_assets:
            lines.extend(f"- {path}" for path in self.source_assets)
        else:
            lines.append("- (none inferred)")
        if self.details:
            lines.append("")
            lines.append("Info:")
            lines.extend(f"- {detail}" for detail in self.details)
        return "\n".join(lines)


@dataclass
class ManifestSliceSpec:
    slice_id: str
    packs: list[str] = field(default_factory=list)

    def row_values(self) -> tuple[str, str, str]:
        return (
            self.slice_id,
            summarize(self.packs, max_count=5),
            str(len(self.packs)),
        )


def main() -> int:
    parser = argparse.ArgumentParser(description="Dark UI for Content Pack Iteration options.")
    parser.add_argument("--project-root", default=str(find_project_root()), help="Unity project root.")
    parser.add_argument("--external-root", default="", help="Override the external content root.")
    parser.add_argument("--list", action="store_true", help="Print rows instead of opening the UI.")
    parser.add_argument("--pack-type", default="All", help="Filter rows for --list.")
    parser.add_argument("--search", default="", help="Search filter for --list.")
    parser.add_argument("--set-active", default="", help="Comma-separated pack ids to write into ContentPackSelection.asset.")
    parser.add_argument("--build-smart", action="store_true", help="Run Unity Smart content-pack build from this Python tool.")
    parser.add_argument("--unity-exe", default="", help="Override Unity.exe path for --build-smart.")
    parser.add_argument("--manifest-list", action="store_true", help="Print ContentManifest slices.")
    parser.add_argument("--upsert-slice", default="", help="Create or replace a ContentManifest slice id.")
    parser.add_argument("--slice-packs", default="", help="Comma-separated pack ids for --upsert-slice.")
    parser.add_argument("--add-pack-to-slice", default="", help="Append a pack id to a ContentManifest slice.")
    parser.add_argument("--remove-pack-from-slice", default="", help="Remove a pack id from a ContentManifest slice.")
    parser.add_argument("--to-slice", default="", help="Slice id used with --add-pack-to-slice or --remove-pack-from-slice.")
    parser.add_argument("--remove-slice", default="", help="Remove a ContentManifest slice id.")
    parser.add_argument("--delete-pack", default="", help="Delete an external pack folder and remove pack references.")
    parser.add_argument("--edit-sprite-sheets", action="store_true", help="Open the custom sprite sheet library editor.")
    args = parser.parse_args()

    project_root = Path(args.project_root).resolve()
    external_root = Path(args.external_root).resolve() if args.external_root else resolve_external_root(project_root)
    manifest_command = update_content_manifest_from_args(project_root, args)
    if args.manifest_list or manifest_command:
        print_content_manifest_slices(project_root)
        if not (args.list or args.set_active or args.build_smart):
            return 0

    if args.set_active:
        active_pack_ids = parse_pack_ids(args.set_active)
        selection_path = write_content_pack_selection(project_root, external_root, active_pack_ids)
        print(f"wrote_selection={selection_path}")

    if args.delete_pack:
        deleted_paths = delete_content_pack(project_root, external_root, args.delete_pack)
        for deleted_path in deleted_paths:
            print(f"deleted={deleted_path}")
        if not (args.list or args.build_smart):
            return 0

    if args.edit_sprite_sheets:
        return launch_sprite_sheet_editor(project_root, wait=True)

    if args.build_smart:
        return run_unity_smart_build(project_root, args.unity_exe)

    options = build_pack_options(project_root, external_root)

    if args.list:
        rows = filter_options(options, args.pack_type, args.search)
        try:
            for option in rows:
                print(
                    f"{option.pack_type:7} | {option.pack_id:36} | "
                    f"{option.status:18} | {option.target_root}"
                )
            print(f"rows={len(rows)} external_root={external_root}")
        except BrokenPipeError:
            return 0
        return 0

    launch_ui(project_root, external_root)
    return 0


def find_project_root() -> Path:
    current = Path(__file__).resolve()
    for parent in current.parents:
        if (parent / "Assets").exists() and (parent / "ProjectSettings").exists():
            return parent
    return Path.cwd()


def resolve_external_root(project_root: Path) -> Path:
    selection_root = read_selection_external_root(project_root)
    if selection_root:
        return selection_root

    package_root = read_package_external_root(project_root)
    if package_root:
        return package_root

    return default_external_root(project_root)


def default_external_root(project_root: Path) -> Path:
    return project_root.parent / DEFAULT_EXTERNAL_ROOT_NAME


def build_pack_options(project_root: Path, external_root: Path) -> list[PackOption]:
    selected_pack_ids = read_active_pack_ids(project_root)

    manifest_by_pack = read_external_pack_manifests(external_root)
    referenced_pack_ids = read_content_manifest_pack_ids(project_root)
    all_ids = sorted(set(referenced_pack_ids) | set(manifest_by_pack), key=pack_sort_key)
    active_ids = resolve_active_pack_ids(selected_pack_ids, all_ids)

    options = [
        build_pack_option(project_root, external_root, pack_id, manifest_by_pack.get(pack_id), active_ids)
        for pack_id in all_ids
    ]

    return [option for option in options if option is not None]


def build_pack_option(
    project_root: Path,
    external_root: Path,
    pack_id: str,
    manifest: dict[str, Any] | None,
    active_ids: set[str],
) -> PackOption | None:
    kind = infer_kind(pack_id, manifest)
    if not kind:
        return None

    pack_type = PACK_TYPE_LABELS.get(kind, kind.title())
    target_root_path = external_pack_root(external_root, kind, pack_id)
    unity_asset_root = unity_pack_root(kind, pack_id)
    owned_roots = list(manifest.get("ownedRoots") or []) if manifest else []
    authoring_sources = parse_authoring_sources(manifest)
    source_assets = [source.source_label() for source in authoring_sources]
    if not source_assets:
        source_assets = build_source_assets(project_root, external_root, pack_id, kind, owned_roots)
    details = build_details(project_root, external_root, pack_id, kind, manifest, owned_roots, source_assets)
    status = build_status(pack_id, kind, target_root_path, manifest, active_ids)

    return PackOption(
        pack_type=pack_type,
        pack_id=pack_id,
        source_assets=source_assets,
        authoring_sources=authoring_sources,
        target_root=display_path(target_root_path),
        unity_asset_root=unity_asset_root,
        status=status,
        details=details,
        source_revision=str(manifest.get("sourceRevision") or "") if manifest else "",
    )


def build_source_assets(
    project_root: Path,
    external_root: Path,
    pack_id: str,
    kind: str,
    owned_roots: Iterable[str],
) -> list[str]:
    sources: list[str] = []

    if kind == "gear":
        gear = parse_gear_pack_id(pack_id)
        if gear:
            form, code, leaf = gear
            add_unique(sources, f"Assets/Sprites/Characters/Esperanza/GroupedGearAtlases/{form}/{code}/{leaf}")

    if kind == "form":
        form_name = pack_id.removeprefix("Form_")
        add_form_source_assets(project_root, sources, form_name)

    if kind == "slice":
        slice_parts = parse_slice_pack_id(pack_id)
        if slice_parts:
            location, enemy, form = slice_parts
            add_unique(sources, f"Assets/Prefabs/Locations/{location}.prefab")
            add_unique(sources, f"Assets/Prefabs/Enemies/{enemy}.prefab")
            add_unique(sources, f"Assets/Sprites/Characters/Enemies/{enemy}")
            add_unique(sources, f"Assets/Sprites/Environments/{location}")

    for root in owned_roots:
        add_unique(sources, root)
        mapped = package_asset_to_external_path(external_root, root)
        if mapped:
            add_unique(sources, display_path(mapped))

    if kind == "core":
        add_unique(sources, "Assets/Sprites")
        add_unique(sources, display_path(external_root / "Core"))

    if kind == "episode":
        add_unique(sources, "Composition only; no sprite ownership expected")

    return sources


def add_form_source_assets(project_root: Path, sources: list[str], form_name: str) -> None:
    form_name = sanitize_identifier(form_name)
    if not form_name:
        return

    character_root = "Assets/Sprites/Characters/Esperanza"
    for folder in FORM_CHARACTER_SOURCE_FOLDERS:
        candidate = f"{character_root}/{folder}/{form_name}"
        if (project_root / candidate.replace("/", os.sep)).exists():
            add_unique(sources, candidate)

    add_unique(sources, f"Assets/Sprites/Items/Gear/{form_name}")
    add_unique(sources, f"Assets/Materials/SelectMenu/Forms/{form_name}.mat")
    add_unique(sources, "Assets/Prefabs/UI/Ability.prefab")
    add_unique(sources, "Assets/Prefabs/UI/InventoryItem.prefab")

    if form_name == "Base":
        add_unique(sources, "Assets/Prefabs/Projectiles/BlastBall.prefab")


def parse_authoring_sources(manifest: dict[str, Any] | None) -> list[SourceAssetSpec]:
    if not manifest:
        return []

    raw_sources = manifest.get("authoringSources") or manifest.get("sourceAssetMappings") or []
    if not isinstance(raw_sources, list):
        return []

    result: list[SourceAssetSpec] = []
    for raw in raw_sources:
        if not isinstance(raw, dict):
            continue
        source_type = normalize_source_type(str(raw.get("sourceType") or raw.get("type") or ""))
        asset_path = normalize_slashes(str(raw.get("assetPath") or raw.get("sourceAsset") or raw.get("source") or ""))
        target_folder = normalize_slashes(str(raw.get("targetFolder") or raw.get("target") or ""))
        label = str(raw.get("label") or raw.get("slice") or "")
        library_name = normalize_slashes(str(raw.get("libraryName") or raw.get("library") or ""))
        category = str(raw.get("category") or "")
        label_prefix = str(raw.get("labelPrefix") or raw.get("prefix") or "")
        normal_asset_path = normalize_slashes(str(raw.get("normalAssetPath") or raw.get("normalSource") or ""))
        if not source_type or not asset_path:
            continue
        result.append(SourceAssetSpec(
            source_type,
            asset_path,
            target_folder,
            label,
            library_name,
            category,
            label_prefix,
            normal_asset_path,
        ))
    return result


def write_authoring_manifest(
    project_root: Path,
    external_root: Path,
    pack_id: str,
    kind: str,
    sources: list[SourceAssetSpec],
) -> Path:
    normalize_authoring_sprite_libraries(project_root, sources)

    target_root = external_pack_root(external_root, kind, pack_id)
    target_root.mkdir(parents=True, exist_ok=True)
    manifest_path = target_root / "ContentPackManifest.json"
    manifest = read_json(manifest_path)
    manifest["packId"] = pack_id
    manifest["kind"] = kind
    manifest.pop("dependencies", None)
    if sources:
        manifest["ownedRoots"] = build_owned_roots(sources)
    else:
        manifest.setdefault("ownedRoots", [])
    manifest.setdefault("ownedLocations", [])
    manifest.setdefault("ownedEnemyTypes", [])
    manifest.setdefault("dialogIds", [])
    manifest["authoringSources"] = [serialize_authoring_source(source) for source in sources]
    manifest["exportedFromProject"] = project_root.name
    manifest["sourceRevision"] = "manual:" + datetime.now(timezone.utc).isoformat(timespec="seconds")

    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest_path


def serialize_authoring_source(source: SourceAssetSpec) -> dict[str, str]:
    data = {
        "sourceType": normalize_source_type(source.source_type),
        "assetPath": normalize_slashes(source.asset_path),
        "label": source.label.strip(),
        "targetFolder": normalize_slashes(source.target_folder),
    }
    if source.source_type == "sprite_sheet":
        data["libraryName"] = normalize_slashes(source.library_name)
        data["category"] = source.category.strip()
        data["labelPrefix"] = source.label_prefix.strip()
        data["normalAssetPath"] = normalize_slashes(source.normal_asset_path)
    return data


def build_owned_roots(sources: Iterable[SourceAssetSpec]) -> list[str]:
    roots: list[str] = []
    for source in sources:
        asset_path = normalize_slashes(source.asset_path)
        if not asset_path:
            continue
        if source.source_type == "sprite_slice" and "[" in asset_path:
            asset_path = asset_path.split("[", 1)[0]
        add_unique(roots, asset_path)
        if source.source_type == "sprite_sheet":
            add_unique(roots, normalize_slashes(source.normal_asset_path))
    return roots


def build_details(
    project_root: Path,
    external_root: Path,
    pack_id: str,
    kind: str,
    manifest: dict[str, Any] | None,
    owned_roots: list[str],
    source_assets: list[str],
) -> list[str]:
    details: list[str] = []
    target_root = external_pack_root(external_root, kind, pack_id)
    details.append(f"External pack exists: {target_root.exists()}")
    details.append(f"Unity package asset root: {unity_pack_root(kind, pack_id)}")

    if manifest:
        details.append("Manifest found: ContentPackManifest.json")
        if manifest.get("exportedFromProject"):
            details.append(f"Exported from: {manifest.get('exportedFromProject')}")
    else:
        details.append("Manifest missing; row is inferred from the pipeline plan or content manifest")

    if owned_roots:
        details.append("Owned roots: " + str(len(owned_roots)))

    missing_project_sources = [
        source for source in source_assets
        if source.startswith("Assets/") and not (project_root / source.replace("/", os.sep)).exists()
    ]
    if missing_project_sources:
        details.append(f"Project-local source paths currently missing: {len(missing_project_sources)}")

    if kind == "gear":
        gear = parse_gear_pack_id(pack_id)
        if gear:
            form, code, leaf = gear
            details.append(f"Gear id: {form}_{code}")
            details.append(f"Gear leaf: {leaf}")

    return details


def build_status(
    pack_id: str,
    kind: str,
    target_root: Path,
    manifest: dict[str, Any] | None,
    active_ids: set[str],
) -> str:
    if pack_id in active_ids:
        return "Active"
    if not target_root.exists():
        return "Planned missing"
    if not manifest:
        return "Folder only"
    return "External"


def read_external_pack_manifests(external_root: Path) -> dict[str, dict[str, Any]]:
    result: dict[str, dict[str, Any]] = {}
    known_kind_folders = {folder.lower() for folder in KIND_FOLDERS.values() if folder}
    if external_root.exists():
        for pack_dir in sorted((p for p in external_root.glob("*") if p.is_dir()), key=lambda p: p.name.lower()):
            if pack_dir.name.lower() in known_kind_folders:
                continue
            manifest = read_json(pack_dir / "ContentPackManifest.json")
            if manifest:
                pack_id = str(manifest.get("packId") or pack_dir.name)
                manifest.setdefault("kind", "pack")
                result[pack_id] = manifest
            else:
                result.setdefault(pack_dir.name, {"packId": pack_dir.name, "kind": "pack"})

    for kind in ("core", "form", "gear", "slice", "episode"):
        root = external_root / KIND_FOLDERS[kind]
        pack_dirs = [root] if kind == "core" else sorted((p for p in root.glob("*") if p.is_dir()), key=lambda p: p.name.lower())
        for pack_dir in pack_dirs:
            manifest = read_json(pack_dir / "ContentPackManifest.json")
            if not manifest:
                if kind != "core":
                    result.setdefault(pack_dir.name, {"packId": pack_dir.name, "kind": kind})
                continue
            pack_id = str(manifest.get("packId") or ("Core" if kind == "core" else pack_dir.name))
            manifest.setdefault("kind", kind)
            result[pack_id] = manifest
    return result


def update_content_manifest_from_args(project_root: Path, args: argparse.Namespace) -> bool:
    changed = False
    if args.remove_slice:
        remove_content_manifest_slice(project_root, args.remove_slice)
        changed = True
    if args.upsert_slice:
        upsert_content_manifest_slice(project_root, args.upsert_slice, parse_manifest_pack_ids(args.slice_packs))
        changed = True
    if args.add_pack_to_slice:
        slice_id = args.to_slice or args.upsert_slice
        if not slice_id:
            raise SystemExit("--add-pack-to-slice requires --to-slice or --upsert-slice.")
        add_pack_to_content_manifest_slice(project_root, slice_id, args.add_pack_to_slice)
        changed = True
    if args.remove_pack_from_slice:
        slice_id = args.to_slice or args.upsert_slice
        if not slice_id:
            raise SystemExit("--remove-pack-from-slice requires --to-slice or --upsert-slice.")
        remove_pack_from_content_manifest_slice(project_root, slice_id, args.remove_pack_from_slice)
        changed = True
    return changed


def content_manifest_path(project_root: Path) -> Path:
    return project_root / "Assets" / "ContentManifest.json"


def read_content_manifest(project_root: Path) -> dict[str, Any]:
    manifest = read_json(content_manifest_path(project_root))
    return manifest if isinstance(manifest, dict) else {}


def read_content_manifest_slices(project_root: Path) -> list[ManifestSliceSpec]:
    manifest = read_content_manifest(project_root)
    return normalize_manifest_slices(manifest.get("slices"))


def normalize_manifest_slices(raw_slices: Any) -> list[ManifestSliceSpec]:
    result: list[ManifestSliceSpec] = []
    if not isinstance(raw_slices, list):
        return result

    for raw_slice in raw_slices:
        if not isinstance(raw_slice, dict):
            continue
        slice_id = sanitize_identifier(str(raw_slice.get("id") or ""))
        if not slice_id:
            continue
        packs = parse_manifest_pack_ids(raw_slice.get("packs"))
        existing_index = find_manifest_slice_index(result, slice_id)
        if existing_index >= 0:
            for pack_id in packs:
                add_unique(result[existing_index].packs, pack_id)
            continue
        result.append(ManifestSliceSpec(slice_id=slice_id, packs=packs))
    return result


def write_content_manifest_slices(project_root: Path, slices: list[ManifestSliceSpec]) -> Path:
    manifest = read_content_manifest(project_root)
    manifest["slices"] = [serialize_manifest_slice(slice_spec) for slice_spec in normalize_manifest_slices([
        {"id": slice_spec.slice_id, "packs": slice_spec.packs}
        for slice_spec in slices
    ])]
    if not isinstance(manifest.get("episodes"), list):
        manifest["episodes"] = []
    return write_content_manifest(project_root, manifest)


def write_content_manifest(project_root: Path, manifest: dict[str, Any]) -> Path:
    path = content_manifest_path(project_root)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return path


def serialize_manifest_slice(slice_spec: ManifestSliceSpec) -> dict[str, Any]:
    return {
        "id": sanitize_identifier(slice_spec.slice_id),
        "packs": parse_manifest_pack_ids(slice_spec.packs),
    }


def upsert_content_manifest_slice(project_root: Path, slice_id: str, pack_ids: Iterable[str]) -> Path:
    normalized_slice_id = sanitize_identifier(slice_id)
    if not normalized_slice_id:
        raise SystemExit("Slice id is required.")
    normalized_packs = parse_manifest_pack_ids(list(pack_ids))
    if not normalized_packs:
        raise SystemExit("At least one pack id is required for a slice.")

    slices = read_content_manifest_slices(project_root)
    index = find_manifest_slice_index(slices, normalized_slice_id)
    slice_spec = ManifestSliceSpec(normalized_slice_id, normalized_packs)
    if index >= 0:
        slices[index] = slice_spec
    else:
        slices.append(slice_spec)
    return write_content_manifest_slices(project_root, slices)


def add_pack_to_content_manifest_slice(project_root: Path, slice_id: str, pack_id: str) -> Path:
    normalized_slice_id = sanitize_identifier(slice_id)
    normalized_pack_id = sanitize_identifier(pack_id)
    if not normalized_slice_id:
        raise SystemExit("Slice id is required.")
    if not normalized_pack_id:
        raise SystemExit("Pack id is required.")

    slices = read_content_manifest_slices(project_root)
    index = find_manifest_slice_index(slices, normalized_slice_id)
    if index < 0:
        slices.append(ManifestSliceSpec(normalized_slice_id, [normalized_pack_id]))
    else:
        add_unique(slices[index].packs, normalized_pack_id)
    return write_content_manifest_slices(project_root, slices)


def remove_pack_from_content_manifest_slice(project_root: Path, slice_id: str, pack_id: str) -> Path:
    normalized_slice_id = sanitize_identifier(slice_id)
    normalized_pack_id = sanitize_identifier(pack_id)
    if not normalized_slice_id:
        raise SystemExit("Slice id is required.")
    if not normalized_pack_id:
        raise SystemExit("Pack id is required.")

    slices = read_content_manifest_slices(project_root)
    index = find_manifest_slice_index(slices, normalized_slice_id)
    if index >= 0:
        slices[index].packs = [
            value for value in slices[index].packs
            if value.lower() != normalized_pack_id.lower()
        ]
    return write_content_manifest_slices(project_root, slices)


def remove_content_manifest_slice(project_root: Path, slice_id: str) -> Path:
    normalized_slice_id = sanitize_identifier(slice_id)
    if not normalized_slice_id:
        raise SystemExit("Slice id is required.")
    slices = [
        slice_spec for slice_spec in read_content_manifest_slices(project_root)
        if slice_spec.slice_id.lower() != normalized_slice_id.lower()
    ]
    return write_content_manifest_slices(project_root, slices)


def remove_pack_from_all_content_manifest_slices(project_root: Path, pack_id: str) -> Path:
    normalized_pack_id = sanitize_identifier(pack_id)
    if not normalized_pack_id:
        raise SystemExit("Pack id is required.")

    slices = read_content_manifest_slices(project_root)
    for slice_spec in slices:
        slice_spec.packs = [
            value for value in slice_spec.packs
            if value.lower() != normalized_pack_id.lower()
        ]
    return write_content_manifest_slices(project_root, slices)


def remove_pack_from_active_selection(project_root: Path, external_root: Path, pack_id: str) -> Path | None:
    normalized_pack_id = sanitize_identifier(pack_id)
    if not normalized_pack_id:
        raise SystemExit("Pack id is required.")

    active_pack_ids = [
        value for value in read_active_pack_ids(project_root)
        if value.lower() != normalized_pack_id.lower()
    ]
    return write_content_pack_selection(project_root, external_root, active_pack_ids)


def find_manifest_slice_index(slices: list[ManifestSliceSpec], slice_id: str) -> int:
    normalized_slice_id = sanitize_identifier(slice_id).lower()
    for index, slice_spec in enumerate(slices):
        if slice_spec.slice_id.lower() == normalized_slice_id:
            return index
    return -1


def find_first_manifest_slice_for_pack(project_root: Path, pack_id: str) -> str:
    normalized_pack_id = sanitize_identifier(pack_id)
    if not normalized_pack_id:
        return ""
    for slice_spec in read_content_manifest_slices(project_root):
        if any(value.lower() == normalized_pack_id.lower() for value in slice_spec.packs):
            return slice_spec.slice_id
    return ""


def parse_manifest_pack_ids(value: Any) -> list[str]:
    result: list[str] = []
    if isinstance(value, str):
        raw_values = re.split(r"[,;\n]+", value)
    elif isinstance(value, (list, tuple, set)):
        raw_values = value
    else:
        raw_values = []

    for raw_value in raw_values:
        pack_id = sanitize_identifier(str(raw_value or ""))
        add_unique(result, pack_id)
    return result


def print_content_manifest_slices(project_root: Path) -> None:
    slices = read_content_manifest_slices(project_root)
    for slice_spec in slices:
        print(f"{slice_spec.slice_id:36} | {', '.join(slice_spec.packs)}")
    print(f"slices={len(slices)} manifest={content_manifest_path(project_root)}")


def read_content_manifest_pack_ids(project_root: Path) -> list[str]:
    result: list[str] = []
    for slice_spec in read_content_manifest_slices(project_root):
        for pack_id in slice_spec.packs:
            add_unique(result, pack_id)
    return result


def resolve_active_pack_ids(
    selected_pack_ids: Iterable[str],
    all_pack_ids: Iterable[str],
) -> set[str]:
    all_ids = set(all_pack_ids)
    active: set[str] = set()
    for selected in selected_pack_ids:
        if selected in all_ids:
            active.add(selected)
    return active


def infer_kind(pack_id: str, manifest: dict[str, Any] | None = None) -> str:
    manifest_kind = str(manifest.get("kind") or "").lower() if manifest else ""
    if manifest_kind:
        return manifest_kind
    if not pack_id:
        return ""
    if pack_id == "Core":
        return "core"
    if pack_id.startswith("Form_"):
        return "form"
    if pack_id.startswith("Gear_"):
        return "gear"
    if pack_id.startswith("Slice_"):
        return "slice"
    if pack_id.startswith("Episode_"):
        return "episode"
    return "pack"


def external_pack_root(external_root: Path, kind: str, pack_id: str) -> Path:
    folder = KIND_FOLDERS.get(kind)
    if not folder:
        return external_root / pack_id
    if kind == "core":
        return external_root / folder
    return external_root / folder / pack_id


def delete_content_pack(project_root: Path, external_root: Path, pack_id: str) -> list[Path]:
    normalized_pack_id = sanitize_identifier(pack_id)
    if not normalized_pack_id:
        raise SystemExit("Pack id is required.")

    manifests = read_external_pack_manifests(external_root)
    manifest = manifests.get(normalized_pack_id)
    kind = infer_kind(normalized_pack_id, manifest)
    pack_root = external_pack_root(external_root, kind, normalized_pack_id)
    safe_pack_root = resolve_deletable_pack_root(external_root, pack_root)
    deleted_paths: list[Path] = []

    if safe_pack_root.exists():
        shutil.rmtree(safe_pack_root)
        deleted_paths.append(safe_pack_root)

    remove_pack_from_all_content_manifest_slices(project_root, normalized_pack_id)
    remove_pack_from_active_selection(project_root, external_root, normalized_pack_id)
    return deleted_paths


def resolve_deletable_pack_root(external_root: Path, pack_root: Path) -> Path:
    resolved_external_root = external_root.resolve()
    resolved_pack_root = pack_root.resolve()

    if resolved_pack_root == resolved_external_root:
        raise SystemExit("Refusing to delete the external content root.")

    if not is_relative_to_path(resolved_pack_root, resolved_external_root):
        raise SystemExit(f"Refusing to delete outside external root: {resolved_pack_root}")

    return resolved_pack_root


def is_relative_to_path(path: Path, root: Path) -> bool:
    try:
        path.relative_to(root)
        return True
    except ValueError:
        return False


def unity_pack_root(kind: str, pack_id: str) -> str:
    folder = KIND_FOLDERS.get(kind, "")
    if kind == "pack":
        return f"Packages/{PACKAGE_NAME}/{pack_id}"
    if kind == "core":
        return f"Packages/{PACKAGE_NAME}/Core"
    if folder:
        return f"Packages/{PACKAGE_NAME}/{folder}/{pack_id}"
    return f"Packages/{PACKAGE_NAME}/{pack_id}"


def package_asset_to_external_path(external_root: Path, asset_path: str) -> Path | None:
    prefix = f"Packages/{PACKAGE_NAME}/"
    normalized = asset_path.replace("\\", "/")
    if not normalized.startswith(prefix):
        return None
    return external_root / normalized[len(prefix):].replace("/", os.sep)


def parse_gear_pack_id(pack_id: str) -> tuple[str, str, str] | None:
    if not pack_id.startswith("Gear_"):
        return None
    parts = pack_id[len("Gear_"):].split("_")
    if len(parts) < 3:
        return None
    return "_".join(parts[:-2]), parts[-2], parts[-1]


def parse_slice_pack_id(pack_id: str) -> tuple[str, str, str] | None:
    if not pack_id.startswith("Slice_"):
        return None
    parts = pack_id[len("Slice_"):].split("_")
    if len(parts) < 3:
        return None
    return parts[0], parts[1], "_".join(parts[2:])


def read_selection_external_root(project_root: Path) -> Path | None:
    text = read_text(project_root / "Assets" / "Editor" / "ContentPackSelection.asset")
    match = re.search(r"^\s*externalRoot:\s*(.+?)\s*$", text, flags=re.MULTILINE)
    if not match:
        return None
    return Path(match.group(1).strip())


def read_active_pack_ids(project_root: Path) -> list[str]:
    text = read_text(project_root / "Assets" / "Editor" / "ContentPackSelection.asset")
    match = re.search(r"^\s*activePackIds:\s*\n((?:\s*-\s*.+\n?)*)", text, flags=re.MULTILINE)
    if not match:
        return []
    return [line.strip()[2:].strip() for line in match.group(1).splitlines() if line.strip().startswith("- ")]


def write_content_pack_selection(project_root: Path, external_root: Path, active_pack_ids: Iterable[str]) -> Path:
    selection_path = project_root / "Assets" / "Editor" / "ContentPackSelection.asset"
    selection_path.parent.mkdir(parents=True, exist_ok=True)
    script_guid = read_meta_guid(project_root / "Assets" / "Editor" / "ContentPackSelection.cs.meta")
    if not script_guid:
        raise RuntimeError("Missing ContentPackSelection.cs.meta guid.")

    normalized_root = normalize_slashes(str(external_root))
    normalized_pack_ids: list[str] = []
    for pack_id in active_pack_ids:
        add_unique(normalized_pack_ids, str(pack_id or "").strip())

    active_block = (
        "  activePackIds: []\n"
        if not normalized_pack_ids
        else "  activePackIds:\n" + "\n".join(f"  - {pack_id}" for pack_id in normalized_pack_ids) + "\n"
    )

    selection_text = (
        "%YAML 1.1\n"
        "%TAG !u! tag:unity3d.com,2011:\n"
        "--- !u!114 &11400000\n"
        "MonoBehaviour:\n"
        "  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n"
        "  m_PrefabAsset: {fileID: 0}\n"
        "  m_GameObject: {fileID: 0}\n"
        "  m_Enabled: 1\n"
        "  m_EditorHideFlags: 0\n"
        f"  m_Script: {{fileID: 11500000, guid: {script_guid}, type: 3}}\n"
        "  m_Name: ContentPackSelection\n"
        "  m_EditorClassIdentifier: \n"
        "  externalContentEnabled: 1\n"
        f"  externalRoot: {normalized_root}\n"
        f"{active_block}"
    )
    selection_path.write_text(selection_text, encoding="utf-8")

    meta_path = selection_path.with_suffix(selection_path.suffix + ".meta")
    if not meta_path.exists():
        meta_path.write_text(
            "fileFormatVersion: 2\n"
            f"guid: {uuid.uuid4().hex}\n"
            "NativeFormatImporter:\n"
            "  externalObjects: {}\n"
            "  mainObjectFileID: 11400000\n"
            "  userData: \n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n",
            encoding="utf-8",
        )

    return selection_path


def read_meta_guid(path: Path) -> str:
    match = re.search(r"^guid:\s*([0-9a-fA-F]{32})\s*$", read_text(path), flags=re.MULTILINE)
    return match.group(1) if match else ""


def run_unity_smart_build(project_root: Path, unity_exe_override: str = "") -> int:
    lock_message = get_unity_project_lock_message(project_root)
    if lock_message:
        print(lock_message, file=sys.stderr)
        return UNITY_LOCK_EXIT_CODE

    unity_exe = Path(unity_exe_override).resolve() if unity_exe_override else find_unity_exe(project_root)
    if not unity_exe.exists():
        print(f"Unity.exe not found: {unity_exe}", file=sys.stderr)
        return 2

    log_path = project_root / "Logs" / "ContentPackIterationUI-SmartBuild.log"
    log_path.parent.mkdir(parents=True, exist_ok=True)
    command = [
        str(unity_exe),
        "-batchmode",
        "-quit",
        "-projectPath",
        str(project_root),
        "-executeMethod",
        "ContentPackPipeline.BuildActiveContentSmart",
        "-logFile",
        str(log_path),
    ]
    print("running=" + " ".join(command))
    completed = subprocess.run(command, cwd=project_root)
    print(f"unity_exit_code={completed.returncode} log={log_path}")
    return completed.returncode


def get_unity_project_lock_message(project_root: Path) -> str:
    lock_path = project_root / "Temp" / "UnityLockfile"
    if not lock_path.exists():
        return ""

    lines = [
        "Build Smart is blocked because this Unity project is already open.",
        f"Lock file: {lock_path}",
        "Close Unity for this project, then run Build Smart again.",
        "If Unity is fully closed and this file remains, delete the stale lock file."
    ]
    return "\n".join(lines)


def find_unity_exe(project_root: Path) -> Path:
    version_text = read_text(project_root / "ProjectSettings" / "ProjectVersion.txt")
    match = re.search(r"^m_EditorVersion:\s*(.+?)\s*$", version_text, flags=re.MULTILINE)
    version = match.group(1).strip() if match else ""
    if version:
        candidate = Path(r"C:\Program Files\Unity\Hub\Editor") / version / "Editor" / "Unity.exe"
        if candidate.exists():
            return candidate
    return Path(r"C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor\Unity.exe")


def read_package_external_root(project_root: Path) -> Path | None:
    manifest = read_json(project_root / "Packages" / "manifest.json")
    dependency = (manifest.get("dependencies") or {}).get(PACKAGE_NAME)
    if not isinstance(dependency, str) or not dependency.startswith("file:"):
        return None
    path_text = dependency[len("file:"):]
    package_manifest_dir = project_root / "Packages"
    return (package_manifest_dir / path_text).resolve()


def read_json(path: Path) -> dict[str, Any]:
    try:
        with path.open("r", encoding="utf-8-sig") as stream:
            data = json.load(stream)
        return data if isinstance(data, dict) else {}
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return {}


def read_text(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8-sig")
    except OSError:
        return ""


def filter_options(options: list[PackOption], pack_type: str, search: str) -> list[PackOption]:
    normalized_type = (pack_type or "All").lower()
    normalized_search = (search or "").strip().lower()
    result = []
    for option in options:
        if normalized_type != "all" and option.pack_type.lower() != normalized_type:
            continue
        if normalized_search and normalized_search not in option.haystack():
            continue
        result.append(option)
    return result


def pack_sort_key(pack_id: str) -> tuple[int, str]:
    kind = infer_kind(pack_id)
    order = {"core": 0, "form": 1, "gear": 2, "slice": 3, "episode": 4}.get(kind, 9)
    return order, pack_id.lower()


def summarize(values: list[str], max_count: int = 2) -> str:
    if not values:
        return ""
    shown = values[:max_count]
    suffix = "" if len(values) <= max_count else f" (+{len(values) - max_count})"
    return "; ".join(shown) + suffix


def display_path(path: Path) -> str:
    return str(path)


def normalize_slashes(value: str) -> str:
    return (value or "").strip().replace("\\", "/")


def source_type_label(source_type: str) -> str:
    return SOURCE_TYPE_LABELS.get(source_type, source_type)


def normalize_source_type(value: str) -> str:
    normalized = (value or "").strip().lower().replace(" ", "_").replace("-", "_")
    if normalized in SOURCE_TYPE_LABELS:
        return normalized
    if normalized in ("sheet", "sprite_sheet", "spritesheet", "sprite_texture", "texture"):
        return "sprite_sheet"
    if normalized in ("library", "spritelibrary", "sprite_lib"):
        return "sprite_library"
    if normalized in ("slice", "png", "sprite_pointer", "direct_sprite"):
        return "sprite_slice"
    return ""


def migrate_sprite_library_file(project_root: Path, value: str) -> Path:
    editor_dir = Path(__file__).resolve().parent / "SpriteLibraryMultiEditor"
    if str(editor_dir) not in sys.path:
        sys.path.insert(0, str(editor_dir))

    from data import migrate_sprite_library_file as migrate_sheet_library_file  # noqa: PLC0415

    source_path = resolve_project_or_absolute_path(project_root, value)
    try:
        return migrate_sheet_library_file(source_path)
    except (FileExistsError, FileNotFoundError, ValueError) as ex:
        raise SystemExit(str(ex)) from ex


def normalize_authoring_sprite_libraries(project_root: Path, sources: list[SourceAssetSpec]) -> None:
    legacy_sources = collect_legacy_sprite_library_sources(sources)
    validate_sprite_library_migrations(project_root, legacy_sources)

    for source in legacy_sources:
        normalize_authoring_sprite_library(project_root, source)


def collect_legacy_sprite_library_sources(sources: list[SourceAssetSpec]) -> list[SourceAssetSpec]:
    legacy_sources: list[SourceAssetSpec] = []
    for source in sources:
        if should_migrate_authoring_sprite_library(source):
            legacy_sources.append(source)
    return legacy_sources


def should_migrate_authoring_sprite_library(source: SourceAssetSpec) -> bool:
    source_type = normalize_source_type(source.source_type)
    if source_type != "sprite_library":
        return False
    return is_legacy_sprite_library_path(source.asset_path)


def validate_sprite_library_migrations(project_root: Path, sources: list[SourceAssetSpec]) -> None:
    for source in sources:
        source_path = resolve_project_or_absolute_path(project_root, source.asset_path)
        target_path = source_path.with_suffix(CUSTOM_LIBRARY_EXTENSION)
        source_exists = source_path.exists()
        target_exists = target_path.exists()

        if source_exists:
            if target_exists:
                raise SystemExit(f"Target already exists: {target_path}")
            continue

        if target_exists:
            continue

        raise SystemExit(f"Missing sprite library file: {source_path}")


def normalize_authoring_sprite_library(project_root: Path, source: SourceAssetSpec) -> None:
    source_type = normalize_source_type(source.source_type)
    if source_type != "sprite_library":
        return

    asset_path = normalize_slashes(source.asset_path)
    if not is_legacy_sprite_library_path(asset_path):
        return

    source.source_type = source_type
    source.asset_path = migrate_sprite_library_reference(project_root, asset_path)


def is_legacy_sprite_library_path(asset_path: str) -> bool:
    normalized = normalize_slashes(asset_path).lower()
    return normalized.endswith(LEGACY_LIBRARY_EXTENSION.lower())


def migrate_sprite_library_reference(project_root: Path, asset_path: str) -> str:
    source_path = resolve_project_or_absolute_path(project_root, asset_path)
    target_path = source_path.with_suffix(CUSTOM_LIBRARY_EXTENSION)
    target_exists = target_path.exists()
    source_exists = source_path.exists()

    if target_exists:
        if not source_exists:
            return normalize_asset_reference(str(target_path), project_root)

    migrated_path = migrate_sprite_library_file(project_root, asset_path)
    return normalize_asset_reference(str(migrated_path), project_root)


def launch_sprite_sheet_editor(project_root: Path, wait: bool) -> int:
    editor_path = Path(__file__).resolve().parent / "SpriteLibraryMultiEditor" / "editor.py"
    if not editor_path.exists():
        raise FileNotFoundError(f"Missing sprite sheet editor: {editor_path}")

    initial_paths = find_custom_sprite_sheet_libraries(project_root)
    command = [
        sys.executable,
        str(editor_path),
    ]
    command.extend(str(path) for path in initial_paths)
    process = subprocess.Popen(command, cwd=str(project_root))
    if wait:
        return process.wait()
    return 0


def find_custom_sprite_sheet_libraries(project_root: Path) -> list[Path]:
    assets_root = project_root / "Assets"
    if not assets_root.exists():
        return []

    pattern = f"*{CUSTOM_LIBRARY_EXTENSION}"
    return sorted(
        assets_root.rglob(pattern),
        key=lambda path: str(path).lower(),
    )


def resolve_project_or_absolute_path(project_root: Path, value: str) -> Path:
    normalized = normalize_slashes(value)
    path = Path(normalized)
    if path.is_absolute():
        return path
    return project_root / normalized.replace("/", os.sep)


def sanitize_identifier(value: str) -> str:
    normalized = re.sub(r"[^A-Za-z0-9_]+", "_", (value or "").strip())
    return re.sub(r"_+", "_", normalized).strip("_")


def normalize_asset_reference(value: str, project_root: Path) -> str:
    normalized = normalize_slashes(value)
    if not normalized:
        return ""
    path = Path(normalized)
    if path.is_absolute():
        try:
            relative = path.resolve().relative_to(project_root.resolve())
            normalized = normalize_slashes(str(relative))
        except ValueError:
            normalized = normalize_slashes(str(path))
    if normalized.startswith("./"):
        normalized = normalized[2:]
    return normalized


def make_pack_id(kind: str, name_or_id: str) -> str:
    value = sanitize_identifier(name_or_id)
    if kind == "core":
        return "Core"
    prefix = {
        "form": "Form_",
        "gear": "Gear_",
        "slice": "Slice_",
        "episode": "Episode_",
    }.get(kind, "")
    if prefix and value.lower().startswith(prefix.lower()):
        return value
    return prefix + value if prefix else value


def parse_pack_ids(value: str) -> list[str]:
    result: list[str] = []
    for token in re.split(r"[,;\n]+", value or ""):
        add_unique(result, token.strip())
    return result


def default_target_folder_for_asset(asset_path: str) -> str:
    normalized = normalize_slashes(asset_path)
    if normalized.startswith("Assets/"):
        normalized = normalized[len("Assets/"):]
    if "[" in normalized:
        normalized = normalized.split("[", 1)[0]
    return normalize_slashes(str(Path(normalized).parent))


def create_authoring_source(source_type: str, asset_path: str, project_root: Path) -> SourceAssetSpec:
    normalized_path = normalize_asset_reference(asset_path, project_root)
    normalized_type = normalize_source_type(source_type)
    target_folder = default_target_folder_for_asset(normalized_path)
    stem = Path(strip_source_asset_suffix(normalized_path)).stem
    return SourceAssetSpec(
        source_type=normalized_type,
        asset_path=normalized_path,
        target_folder=target_folder,
        label="",
        label_prefix=stem if normalized_type == "sprite_sheet" else "",
    )


def strip_source_asset_suffix(source_asset: str) -> str:
    normalized = normalize_slashes(source_asset)
    if " [" in normalized:
        normalized = normalized.split(" [", 1)[0]
    if "[" in normalized and normalized.endswith("]"):
        normalized = normalized.rsplit("[", 1)[0]
    return normalized.strip()


def resolve_source_asset_path(project_root: Path, external_root: Path, source_asset: str) -> Path | None:
    normalized = strip_source_asset_suffix(source_asset)
    if not normalized or normalized.startswith("Composition only;"):
        return None
    path = Path(normalized)
    if path.is_absolute():
        return path
    if normalized.startswith("Assets/"):
        return project_root / normalized.replace("/", os.sep)
    if normalized.startswith(f"Packages/{PACKAGE_NAME}/"):
        mapped = package_asset_to_external_path(external_root, normalized)
        return mapped if mapped else project_root / normalized.replace("/", os.sep)
    return external_root / normalized.replace("/", os.sep)


def read_text_preview(path: Path, max_chars: int = 20000) -> str:
    try:
        text = path.read_text(encoding="utf-8-sig", errors="replace")
    except OSError as ex:
        return f"Unable to read file: {ex}"
    if len(text) > max_chars:
        return text[:max_chars] + "\n\n... preview truncated ..."
    return text


def collect_pack_source_rows(option: PackOption) -> list[tuple[str, str, str]]:
    rows: list[tuple[str, str, str]] = []
    for source in option.authoring_sources:
        rows.append((source_type_label(source.source_type), source.source_label(), source.target_folder))
    if rows:
        return rows
    for source_asset in option.source_assets:
        rows.append(("Inferred", source_asset, ""))
    return rows


def verify_pack_option(project_root: Path, external_root: Path, option: PackOption) -> tuple[bool, list[str]]:
    errors: list[str] = []
    info: list[str] = []
    pack_id = sanitize_identifier(option.pack_id)
    kind = infer_kind(pack_id)
    target_root = external_pack_root(external_root, kind, pack_id)

    if not pack_id:
        errors.append("Pack id is required.")
    if not external_root:
        errors.append("External root is required.")
    if not external_root.name.lower() == DEFAULT_EXTERNAL_ROOT_NAME.lower():
        info.append(f"External root is not named {DEFAULT_EXTERNAL_ROOT_NAME}: {external_root}")

    manifest_path = target_root / "ContentPackManifest.json"
    if not manifest_path.exists():
        errors.append(f"Missing pack manifest: {manifest_path}")

    if not option.authoring_sources:
        errors.append("No authoring sources declared; Smart build has no source contract to export.")

    seen_targets: dict[str, str] = {}
    for index, source in enumerate(option.authoring_sources, start=1):
        error = validate_authoring_source(source)
        if error:
            errors.append(f"Source {index}: {error}")
            continue

        source_path = resolve_source_asset_path(project_root, external_root, source.asset_path)
        if source_path is None or not source_path.exists():
            errors.append(f"Source {index}: missing asset '{source.asset_path}'.")

        if source.source_type == "sprite_sheet" and source.normal_asset_path:
            normal_path = resolve_source_asset_path(project_root, external_root, source.normal_asset_path)
            if normal_path is None or not normal_path.exists():
                errors.append(f"Source {index}: missing normal asset '{source.normal_asset_path}'.")

        asset_key = strip_source_asset_suffix(source.asset_path).lower()
        target_path = normalize_target_relative_path(source.target_folder, source.asset_path)
        previous = seen_targets.get(asset_key)
        if previous and previous.lower() != target_path.lower():
            errors.append(
                f"Source {index}: asset maps to multiple target folders: '{previous}' and '{target_path}'."
            )
        elif asset_key:
            seen_targets[asset_key] = target_path

    if not errors:
        info.append(f"Pack will export to: {target_root}")
        info.append(f"Unity package root: {unity_pack_root(kind, pack_id)}")
        info.append(f"Authoring sources: {len(option.authoring_sources)}")
    return not errors, errors or info


def normalize_target_relative_path(target_folder: str, asset_path: str) -> str:
    normalized = normalize_slashes(target_folder).strip("/")
    if normalized.lower().startswith("assets/"):
        normalized = normalized[len("Assets/"):]
    segments = [segment for segment in normalized.split("/") if segment]
    if len(segments) > 1 and segments[0].lower() == "core":
        normalized = "/".join(segments[1:])
    elif len(segments) > 2 and segments[0].lower() in {"forms", "gears", "slices", "episodes"}:
        normalized = "/".join(segments[2:])
    return normalize_slashes(str(Path(normalized) / Path(strip_source_asset_suffix(asset_path)).name))


def validate_authoring_source(source: SourceAssetSpec) -> str:
    if not source.asset_path.strip():
        return "Source asset path is required."
    if not source.target_folder.strip():
        return "Target folder is required."
    if source.source_type == "sprite_sheet":
        if not source.asset_path.lower().endswith(".png"):
            return "Sprite Sheet sources must point at a .png asset."
        if not source.library_name.strip():
            return "Sprite Sheet sources require a library name."
        if not source.category.strip():
            return "Sprite Sheet sources require a category."
        if not source.label_prefix.strip():
            return "Sprite Sheet sources require a label prefix."
        normal_asset_path = source.normal_asset_path.strip()
        if normal_asset_path:
            if not normal_asset_path.lower().endswith(".png"):
                return "Sprite Sheet normal texture must point at a .png asset."
    elif source.source_type == "sprite_library":
        asset_path = source.asset_path.lower()
        has_custom_extension = asset_path.endswith(CUSTOM_LIBRARY_EXTENSION.lower())
        has_legacy_extension = asset_path.endswith(LEGACY_LIBRARY_EXTENSION.lower())
        if has_custom_extension:
            return ""
        if has_legacy_extension:
            return ""
        return f"Sprite Library sources must point at a {CUSTOM_LIBRARY_EXTENSION} or {LEGACY_LIBRARY_EXTENSION} asset."
    elif source.source_type == "sprite_slice":
        asset_path = source.asset_path.split("[", 1)[0]
        if not asset_path.lower().endswith(".png"):
            return "Sprite Slice sources must point at a .png asset."
        if not source.label.strip() and "[" not in source.asset_path:
            return "Sprite Slice sources require a slice label."
    else:
        return "Unknown source type."
    return ""


def validate_manifest_slice(slice_spec: ManifestSliceSpec) -> str:
    if not sanitize_identifier(slice_spec.slice_id):
        return "Slice id is required."
    if not parse_manifest_pack_ids(slice_spec.packs):
        return "At least one pack id is required."
    return ""


def add_unique(values: list[str], value: str) -> None:
    if value and value not in values:
        values.append(value)


def launch_ui(project_root: Path, external_root: Path) -> None:
    import tkinter as tk
    from tkinter import filedialog, messagebox
    from tkinter import ttk

    class ContentPackIterationApp(tk.Tk):
        def __init__(self) -> None:
            super().__init__()
            self.title("Content Pack Iteration")
            self.geometry("1420x820")
            self.minsize(1100, 640)
            self.configure(bg="#101418")
            self.options: list[PackOption] = []
            self.filtered: list[PackOption] = []
            self.pack_type = tk.StringVar(value="All")
            self.search_text = tk.StringVar()
            self.status_text = tk.StringVar()
            self._build_style()
            self._build_layout()
            self.refresh()

        def _build_style(self) -> None:
            style = ttk.Style(self)
            style.theme_use("clam")
            style.configure(".", background="#101418", foreground="#d8dee9", fieldbackground="#171c22")
            style.configure("TFrame", background="#101418")
            style.configure("TLabel", background="#101418", foreground="#d8dee9")
            style.configure("TButton", background="#26313b", foreground="#edf2f7", borderwidth=0, padding=(12, 7))
            style.map("TButton", background=[("active", "#32404d")])
            style.configure("TCombobox", fieldbackground="#171c22", background="#171c22", foreground="#edf2f7")
            style.configure("Treeview", background="#151a20", fieldbackground="#151a20", foreground="#e6edf3", rowheight=27, borderwidth=0)
            style.configure("Treeview.Heading", background="#202832", foreground="#f8fafc", relief="flat")
            style.map("Treeview", background=[("selected", "#2f5f8f")], foreground=[("selected", "#ffffff")])

        def _build_layout(self) -> None:
            toolbar = ttk.Frame(self, padding=(14, 12, 14, 8))
            toolbar.pack(fill=tk.X)

            ttk.Label(toolbar, text="Pack Type").pack(side=tk.LEFT, padx=(0, 8))
            self.pack_combo = ttk.Combobox(toolbar, textvariable=self.pack_type, state="readonly", width=18)
            self.pack_combo.pack(side=tk.LEFT, padx=(0, 14))
            self.pack_combo.bind("<<ComboboxSelected>>", lambda _event: self.apply_filter())

            ttk.Label(toolbar, text="Search").pack(side=tk.LEFT, padx=(0, 8))
            search_entry = ttk.Entry(toolbar, textvariable=self.search_text, width=38)
            search_entry.pack(side=tk.LEFT, padx=(0, 14))
            search_entry.bind("<KeyRelease>", lambda _event: self.apply_filter())

            ttk.Button(toolbar, text="Refresh", command=self.refresh).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(toolbar, text="New Pack", command=self.new_pack).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(toolbar, text="Edit Pack", command=self.edit_pack).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(toolbar, text="Delete Pack", command=self.delete_pack).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(toolbar, text="Edit Manifest", command=self.edit_manifest).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(toolbar, text="Edit Sheets", command=self.edit_sheets).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(toolbar, text="Set Active", command=self.set_active).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(toolbar, text="Verify", command=self.verify_pack).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(toolbar, text="Build Smart", command=self.build_smart).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(toolbar, text="Copy Target", command=self.copy_target).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(toolbar, text="Open Target", command=self.open_target).pack(side=tk.LEFT)
            ttk.Label(toolbar, textvariable=self.status_text).pack(side=tk.RIGHT)

            body = ttk.Frame(self, padding=(14, 0, 14, 14))
            body.pack(fill=tk.BOTH, expand=True)
            body.columnconfigure(0, weight=4)
            body.columnconfigure(1, weight=2)
            body.rowconfigure(0, weight=1)

            columns = ("type", "target", "source", "root", "status")
            self.tree = ttk.Treeview(body, columns=columns, show="headings", selectmode="extended")
            headings = {
                "type": "Type",
                "target": "Target Name / ID",
                "source": "Source Assets",
                "root": "Target Root",
                "status": "Status",
            }
            widths = {
                "type": 84,
                "target": 240,
                "source": 420,
                "root": 330,
                "status": 130,
            }
            for column in columns:
                self.tree.heading(column, text=headings[column], command=lambda c=column: self.sort_by(c))
                self.tree.column(column, width=widths[column], minwidth=70, anchor=tk.W, stretch=True)

            y_scroll = ttk.Scrollbar(body, orient=tk.VERTICAL, command=self.tree.yview)
            x_scroll = ttk.Scrollbar(body, orient=tk.HORIZONTAL, command=self.tree.xview)
            self.tree.configure(yscrollcommand=y_scroll.set, xscrollcommand=x_scroll.set)
            self.tree.grid(row=0, column=0, sticky="nsew")
            y_scroll.grid(row=0, column=0, sticky="nse")
            x_scroll.grid(row=1, column=0, sticky="ew")
            self.tree.bind("<<TreeviewSelect>>", lambda _event: self.update_details())
            self.tree.bind("<Double-1>", lambda _event: self.edit_pack())
            self.tree.tag_configure("Active", foreground="#7dd3fc")
            self.tree.tag_configure("Planned missing", foreground="#fca5a5")

            detail_frame = ttk.Frame(body, padding=(12, 0, 0, 0))
            detail_frame.grid(row=0, column=1, rowspan=2, sticky="nsew")
            detail_frame.rowconfigure(1, weight=1)
            detail_frame.columnconfigure(0, weight=1)
            ttk.Label(detail_frame, text="Selection Detail").grid(row=0, column=0, sticky="w", pady=(0, 8))
            self.detail = tk.Text(
                detail_frame,
                bg="#151a20",
                fg="#e6edf3",
                insertbackground="#e6edf3",
                relief=tk.FLAT,
                wrap=tk.WORD,
                padx=12,
                pady=12,
                font=("Consolas", 10),
            )
            self.detail.grid(row=1, column=0, sticky="nsew")

        def refresh(self) -> None:
            self.options = build_pack_options(project_root, external_root)
            pack_types = ["All"] + sorted({option.pack_type for option in self.options})
            current = self.pack_type.get()
            self.pack_combo.configure(values=pack_types)
            if current not in pack_types:
                self.pack_type.set("All")
            self.apply_filter()

        def apply_filter(self) -> None:
            self.filtered = filter_options(self.options, self.pack_type.get(), self.search_text.get())
            self.tree.delete(*self.tree.get_children())
            for index, option in enumerate(self.filtered):
                self.tree.insert("", tk.END, iid=str(index), values=option.row_values(), tags=(option.status,))
            self.status_text.set(f"{len(self.filtered)} rows | external root: {external_root}")
            if self.filtered:
                self.tree.selection_set("0")
                self.tree.focus("0")
            self.update_details()

        def sort_by(self, column: str) -> None:
            column_index = {"type": 0, "target": 1, "source": 2, "root": 3, "status": 4}[column]
            self.filtered.sort(key=lambda option: option.row_values()[column_index].lower())
            self.tree.delete(*self.tree.get_children())
            for index, option in enumerate(self.filtered):
                self.tree.insert("", tk.END, iid=str(index), values=option.row_values(), tags=(option.status,))

        def selected_option(self) -> PackOption | None:
            selection = self.tree.selection()
            if not selection:
                return None
            index = int(selection[0])
            if index < 0 or index >= len(self.filtered):
                return None
            return self.filtered[index]

        def selected_options(self) -> list[PackOption]:
            result: list[PackOption] = []
            for item_id in self.tree.selection():
                index = int(item_id)
                if index < 0 or index >= len(self.filtered):
                    continue
                result.append(self.filtered[index])
            return result

        def update_details(self) -> None:
            options = self.selected_options()
            self.detail.configure(state=tk.NORMAL)
            self.detail.delete("1.0", tk.END)
            if len(options) == 1:
                self.detail.insert(tk.END, options[0].detail_text())
            elif len(options) > 1:
                lines = ["Selected packs:"]
                lines.extend(f"- {option.pack_id} ({option.status})" for option in options)
                self.detail.insert(tk.END, "\n".join(lines))
            self.detail.configure(state=tk.DISABLED)

        def copy_target(self) -> None:
            option = self.selected_option()
            if not option:
                return
            self.clipboard_clear()
            self.clipboard_append(option.target_root)
            self.status_text.set("Copied target root")

        def open_target(self) -> None:
            option = self.selected_option()
            if not option or not option.target_root:
                return
            path = Path(option.target_root)
            if path.exists():
                os.startfile(path)

        def open_sources(self) -> None:
            option = self.selected_option()
            if not option:
                return
            SourceAssetsDialog(self, project_root, external_root, option)

        def new_pack(self) -> None:
            dialog = PackEditorDialog(self, project_root, external_root, None)
            self.wait_window(dialog)
            if dialog.saved:
                self.refresh()

        def edit_pack(self) -> None:
            option = self.selected_option()
            if not option:
                return
            dialog = PackEditorDialog(self, project_root, external_root, option)
            self.wait_window(dialog)
            if dialog.saved:
                self.refresh()

        def delete_pack(self) -> None:
            option = self.selected_option()
            if not option:
                return
            if not self.confirm_delete_pack(option):
                return
            try:
                deleted_paths = delete_content_pack(project_root, external_root, option.pack_id)
            except (OSError, RuntimeError, SystemExit) as ex:
                messagebox.showerror("Delete Pack", str(ex), parent=self)
                return
            count_text = f"{len(deleted_paths)} folder(s)"
            self.status_text.set(f"Deleted {option.pack_id}: {count_text}")
            self.refresh()

        def confirm_delete_pack(self, option: PackOption) -> bool:
            lines = [
                f"Delete pack '{option.pack_id}'?",
                "",
                option.target_root,
                "",
                "This also removes it from active selection and manifest slices.",
            ]
            return messagebox.askyesno("Delete Pack", "\n".join(lines), parent=self)

        def edit_manifest(self) -> None:
            option = self.selected_option()
            seed_pack_id = option.pack_id if option else ""
            dialog = ManifestEditorDialog(self, project_root, seed_pack_id)
            self.wait_window(dialog)
            if dialog.saved:
                self.refresh()

        def edit_sheets(self) -> None:
            try:
                launch_sprite_sheet_editor(project_root, wait=False)
            except (OSError, RuntimeError) as ex:
                messagebox.showerror("Edit Sheets", str(ex), parent=self)
                return
            self.status_text.set("Opened sprite sheet editor")

        def set_active(self) -> None:
            options = self.selected_options()
            if not options:
                return
            pack_ids = read_active_pack_ids(project_root)
            for option in options:
                add_unique(pack_ids, option.pack_id)
            write_content_pack_selection(project_root, external_root, pack_ids)
            self.status_text.set(f"Active packs: {', '.join(pack_ids)}")
            self.refresh()

        def verify_pack(self) -> None:
            option = self.selected_option()
            if not option:
                return
            valid, lines = verify_pack_option(project_root, external_root, option)
            message = "\n".join(lines)
            if valid:
                self.status_text.set(f"Verified: {option.pack_id}")
                messagebox.showinfo("Verify Pack", message, parent=self)
            else:
                self.status_text.set(f"Verify failed: {option.pack_id}")
                messagebox.showerror("Verify Pack", message, parent=self)

        def build_smart(self) -> None:
            lock_message = get_unity_project_lock_message(project_root)
            if lock_message:
                self.status_text.set("Smart build blocked: Unity project is open")
                messagebox.showerror("Build Smart", lock_message, parent=self)
                return

            self.status_text.set("Running Smart build...")
            self.update_idletasks()
            exit_code = run_unity_smart_build(project_root)
            self.status_text.set(f"Smart build exit code: {exit_code}")

    class SourceAssetsDialog(tk.Toplevel):
        def __init__(self, parent: tk.Tk, project_root: Path, external_root: Path, option: PackOption) -> None:
            super().__init__(parent)
            self.project_root = project_root
            self.external_root = external_root
            self.option = option
            self.rows = collect_pack_source_rows(option)
            self.title(f"Source Assets - {option.pack_id}")
            self.geometry("1120x580")
            self.minsize(860, 420)
            self.configure(bg="#101418")
            self.transient(parent)
            self._build_layout()
            self._refresh_sources()

        def _build_layout(self) -> None:
            root = ttk.Frame(self, padding=(14, 14, 14, 14))
            root.pack(fill=tk.BOTH, expand=True)
            root.rowconfigure(1, weight=1)
            root.columnconfigure(0, weight=1)

            header = ttk.Frame(root)
            header.grid(row=0, column=0, sticky="ew", pady=(0, 8))
            ttk.Label(header, text=f"{self.option.pack_id} source assets").pack(side=tk.LEFT)
            ttk.Button(header, text="Preview", command=self.preview_selected).pack(side=tk.RIGHT)

            columns = ("type", "source", "target", "exists")
            self.source_tree = ttk.Treeview(root, columns=columns, show="headings", selectmode="browse")
            headings = {
                "type": "Type",
                "source": "Source Asset",
                "target": "Target Folder",
                "exists": "Exists",
            }
            widths = {"type": 140, "source": 560, "target": 300, "exists": 80}
            for column in columns:
                self.source_tree.heading(column, text=headings[column])
                self.source_tree.column(column, width=widths[column], minwidth=80, anchor=tk.W, stretch=True)
            self.source_tree.grid(row=1, column=0, sticky="nsew")
            self.source_tree.bind("<Double-1>", lambda _event: self.preview_selected())

            footer = ttk.Frame(root)
            footer.grid(row=2, column=0, sticky="ew", pady=(12, 0))
            footer.columnconfigure(0, weight=1)
            ttk.Label(footer, text=f"{len(self.rows)} sources").grid(row=0, column=0, sticky="w")
            ttk.Button(footer, text="Close", command=self.destroy).grid(row=0, column=1)

        def _refresh_sources(self) -> None:
            self.source_tree.delete(*self.source_tree.get_children())
            for index, row in enumerate(self.rows):
                path = resolve_source_asset_path(self.project_root, self.external_root, row[1])
                exists = "yes" if path and path.exists() else "no"
                self.source_tree.insert("", tk.END, iid=str(index), values=(row[0], row[1], row[2], exists))
            if self.rows:
                self.source_tree.selection_set("0")
                self.source_tree.focus("0")

        def _selected_source(self) -> str:
            selection = self.source_tree.selection()
            if not selection:
                return ""
            index = int(selection[0])
            if index < 0 or index >= len(self.rows):
                return ""
            return self.rows[index][1]

        def preview_selected(self) -> None:
            source_asset = self._selected_source()
            if not source_asset:
                return
            SourcePreviewDialog(self, self.project_root, self.external_root, source_asset)

    class SourcePreviewDialog(tk.Toplevel):
        def __init__(self, parent: tk.Toplevel, project_root: Path, external_root: Path, source_asset: str) -> None:
            super().__init__(parent)
            self.project_root = project_root
            self.external_root = external_root
            self.source_asset = source_asset
            self.path = resolve_source_asset_path(project_root, external_root, source_asset)
            self.image_ref: tk.PhotoImage | None = None
            self.title("Source Preview")
            self.geometry("980x700")
            self.minsize(720, 460)
            self.configure(bg="#101418")
            self.transient(parent)
            self._build_layout()

        def _build_layout(self) -> None:
            root = ttk.Frame(self, padding=(14, 14, 14, 14))
            root.pack(fill=tk.BOTH, expand=True)
            root.rowconfigure(1, weight=1)
            root.columnconfigure(0, weight=1)

            header = ttk.Frame(root)
            header.grid(row=0, column=0, sticky="ew", pady=(0, 8))
            ttk.Label(header, text=self.source_asset).pack(side=tk.LEFT)
            ttk.Button(header, text="Open File", command=self.open_file).pack(side=tk.RIGHT)

            body = ttk.Frame(root)
            body.grid(row=1, column=0, sticky="nsew")
            body.rowconfigure(0, weight=1)
            body.columnconfigure(0, weight=1)

            if self.path is None:
                self._show_text(body, "No file path for this source.")
                return
            if not self.path.exists():
                self._show_text(body, f"Missing source asset:\n{self.path}")
                return
            if self.path.is_dir():
                entries = sorted(item.name for item in self.path.iterdir())
                self._show_text(body, "Directory:\n" + str(self.path) + "\n\n" + "\n".join(entries[:400]))
                return
            if self.path.suffix.lower() in {".png", ".gif"}:
                if self._show_image(body):
                    return
            self._show_text(body, read_text_preview(self.path))

        def _show_image(self, body: ttk.Frame) -> bool:
            try:
                image = tk.PhotoImage(file=str(self.path))
            except tk.TclError:
                return False
            max_width = 900
            max_height = 560
            scale = max(
                1,
                (image.width() + max_width - 1) // max_width,
                (image.height() + max_height - 1) // max_height,
            )
            if scale > 1:
                image = image.subsample(scale, scale)
            self.image_ref = image
            canvas = tk.Canvas(body, bg="#151a20", highlightthickness=0)
            canvas.grid(row=0, column=0, sticky="nsew")
            canvas.create_image(12, 12, anchor=tk.NW, image=image)
            canvas.create_text(
                12,
                image.height() + 28,
                anchor=tk.NW,
                fill="#d8dee9",
                text=f"{self.path}\n{self.image_ref.width()}x{self.image_ref.height()} preview",
            )
            return True

        def _show_text(self, body: ttk.Frame, text: str) -> None:
            text_widget = tk.Text(
                body,
                bg="#151a20",
                fg="#e6edf3",
                insertbackground="#e6edf3",
                relief=tk.FLAT,
                wrap=tk.NONE,
                padx=12,
                pady=12,
                font=("Consolas", 10),
            )
            y_scroll = ttk.Scrollbar(body, orient=tk.VERTICAL, command=text_widget.yview)
            x_scroll = ttk.Scrollbar(body, orient=tk.HORIZONTAL, command=text_widget.xview)
            text_widget.configure(yscrollcommand=y_scroll.set, xscrollcommand=x_scroll.set)
            text_widget.grid(row=0, column=0, sticky="nsew")
            y_scroll.grid(row=0, column=1, sticky="ns")
            x_scroll.grid(row=1, column=0, sticky="ew")
            text_widget.insert(tk.END, text)
            text_widget.configure(state=tk.DISABLED)

        def open_file(self) -> None:
            if self.path is not None and self.path.exists():
                os.startfile(self.path)

    class ManifestEditorDialog(tk.Toplevel):
        def __init__(self, parent: tk.Tk, project_root: Path, seed_pack_id: str = "") -> None:
            super().__init__(parent)
            self.project_root = project_root
            self.seed_pack_id = sanitize_identifier(seed_pack_id)
            self.slices = read_content_manifest_slices(project_root)
            self.saved = False
            self.status_text = tk.StringVar()
            self.title("Content Manifest")
            self.geometry("860x560")
            self.minsize(760, 460)
            self.configure(bg="#101418")
            self.transient(parent)
            self.grab_set()
            self._build_layout()
            self._refresh_slices()

        def _build_layout(self) -> None:
            root = ttk.Frame(self, padding=(14, 14, 14, 14))
            root.pack(fill=tk.BOTH, expand=True)
            root.rowconfigure(1, weight=1)
            root.columnconfigure(0, weight=1)

            toolbar = ttk.Frame(root)
            toolbar.grid(row=0, column=0, sticky="ew", pady=(0, 8))
            ttk.Button(toolbar, text="New Slice", command=self.new_slice).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(toolbar, text="Edit Slice", command=self.edit_slice).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(toolbar, text="Delete Slice", command=self.delete_slice).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(toolbar, text="Add Selected Pack", command=self.add_seed_pack).pack(side=tk.LEFT, padx=(0, 8))

            columns = ("slice", "packs", "count")
            self.slice_tree = ttk.Treeview(root, columns=columns, show="headings", selectmode="browse")
            headings = {
                "slice": "Slice",
                "packs": "Packs",
                "count": "Count",
            }
            widths = {"slice": 240, "packs": 500, "count": 80}
            for column in columns:
                self.slice_tree.heading(column, text=headings[column])
                self.slice_tree.column(column, width=widths[column], minwidth=80, anchor=tk.W, stretch=True)
            self.slice_tree.grid(row=1, column=0, sticky="nsew")

            footer = ttk.Frame(root)
            footer.grid(row=2, column=0, sticky="ew", pady=(12, 0))
            footer.columnconfigure(0, weight=1)
            ttk.Label(footer, textvariable=self.status_text).grid(row=0, column=0, sticky="w")
            ttk.Button(footer, text="Save Manifest", command=self.save_manifest).grid(row=0, column=1, padx=(8, 0))
            ttk.Button(footer, text="Close", command=self.destroy).grid(row=0, column=2, padx=(8, 0))

        def _refresh_slices(self) -> None:
            self.slice_tree.delete(*self.slice_tree.get_children())
            for index, slice_spec in enumerate(self.slices):
                self.slice_tree.insert("", tk.END, iid=str(index), values=slice_spec.row_values())
            if self.slices:
                self.slice_tree.selection_set("0")
                self.slice_tree.focus("0")
            self.status_text.set(f"{len(self.slices)} slices | manifest: {content_manifest_path(self.project_root)}")

        def _selected_slice_index(self) -> int:
            selection = self.slice_tree.selection()
            if not selection:
                return -1
            index = int(selection[0])
            return index if 0 <= index < len(self.slices) else -1

        def new_slice(self) -> None:
            dialog = ManifestSliceEditorDialog(self, None, self.seed_pack_id)
            self.wait_window(dialog)
            if dialog.slice_spec:
                self._upsert_slice(dialog.slice_spec)

        def edit_slice(self) -> None:
            index = self._selected_slice_index()
            if index < 0:
                return
            dialog = ManifestSliceEditorDialog(self, self.slices[index], "")
            self.wait_window(dialog)
            if dialog.slice_spec:
                self.slices[index] = dialog.slice_spec
                self._dedupe_slices()
                self._refresh_slices()

        def delete_slice(self) -> None:
            index = self._selected_slice_index()
            if index < 0:
                return
            slice_id = self.slices[index].slice_id
            if not messagebox.askyesno("Content Manifest", f"Delete slice '{slice_id}'?", parent=self):
                return
            del self.slices[index]
            self._refresh_slices()

        def add_seed_pack(self) -> None:
            if not self.seed_pack_id:
                messagebox.showinfo("Content Manifest", "Select a pack row first.", parent=self)
                return
            index = self._selected_slice_index()
            if index < 0:
                dialog = ManifestSliceEditorDialog(self, None, self.seed_pack_id)
                self.wait_window(dialog)
                if dialog.slice_spec:
                    self._upsert_slice(dialog.slice_spec)
                return
            add_unique(self.slices[index].packs, self.seed_pack_id)
            self._refresh_slices()

        def _upsert_slice(self, slice_spec: ManifestSliceSpec) -> None:
            index = find_manifest_slice_index(self.slices, slice_spec.slice_id)
            if index >= 0:
                self.slices[index] = slice_spec
            else:
                self.slices.append(slice_spec)
            self._dedupe_slices()
            self._refresh_slices()

        def _dedupe_slices(self) -> None:
            self.slices = normalize_manifest_slices([
                {"id": slice_spec.slice_id, "packs": slice_spec.packs}
                for slice_spec in self.slices
            ])

        def save_manifest(self) -> None:
            for slice_spec in self.slices:
                error = validate_manifest_slice(slice_spec)
                if error:
                    messagebox.showerror("Content Manifest", error, parent=self)
                    return
            path = write_content_manifest_slices(self.project_root, self.slices)
            self.saved = True
            self.status_text.set(f"Saved {path}")
            self.destroy()

    class ManifestSliceEditorDialog(tk.Toplevel):
        def __init__(
            self,
            parent: tk.Toplevel,
            slice_spec: ManifestSliceSpec | None,
            seed_pack_id: str = "",
        ) -> None:
            super().__init__(parent)
            self.slice_spec: ManifestSliceSpec | None = None
            initial_packs = list(slice_spec.packs) if slice_spec else parse_manifest_pack_ids(seed_pack_id)
            self.slice_text = tk.StringVar(value=slice_spec.slice_id if slice_spec else "")
            self.title("Manifest Slice")
            self.geometry("680x380")
            self.minsize(560, 320)
            self.configure(bg="#101418")
            self.transient(parent)
            self.grab_set()
            self._build_layout(initial_packs)

        def _build_layout(self, initial_packs: list[str]) -> None:
            root = ttk.Frame(self, padding=(14, 14, 14, 14))
            root.pack(fill=tk.BOTH, expand=True)
            root.columnconfigure(1, weight=1)
            root.rowconfigure(1, weight=1)

            ttk.Label(root, text="Slice ID").grid(row=0, column=0, sticky="w", padx=(0, 10), pady=(0, 8))
            ttk.Entry(root, textvariable=self.slice_text).grid(row=0, column=1, sticky="ew", pady=(0, 8))

            ttk.Label(root, text="Packs").grid(row=1, column=0, sticky="nw", padx=(0, 10))
            self.packs_text = tk.Text(
                root,
                bg="#151a20",
                fg="#e6edf3",
                insertbackground="#e6edf3",
                relief=tk.FLAT,
                wrap=tk.WORD,
                padx=10,
                pady=8,
                font=("Consolas", 10),
                height=10,
            )
            self.packs_text.grid(row=1, column=1, sticky="nsew")
            self.packs_text.insert(tk.END, "\n".join(initial_packs))

            footer = ttk.Frame(root)
            footer.grid(row=2, column=0, columnspan=2, sticky="e", pady=(12, 0))
            ttk.Button(footer, text="Save Slice", command=self.save_slice).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(footer, text="Cancel", command=self.destroy).pack(side=tk.LEFT)

        def save_slice(self) -> None:
            slice_spec = ManifestSliceSpec(
                slice_id=sanitize_identifier(self.slice_text.get()),
                packs=parse_manifest_pack_ids(self.packs_text.get("1.0", tk.END)),
            )
            error = validate_manifest_slice(slice_spec)
            if error:
                messagebox.showerror("Manifest Slice", error, parent=self)
                return
            self.slice_spec = slice_spec
            self.destroy()

    class PackEditorDialog(tk.Toplevel):
        def __init__(self, parent: tk.Tk, project_root: Path, external_root: Path, option: PackOption | None) -> None:
            super().__init__(parent)
            self.project_root = project_root
            self.external_root = external_root
            self.option = option
            self.saved = False
            self.sources: list[SourceAssetSpec] = list(option.authoring_sources) if option else []
            self.title("Edit Pack" if option else "New Pack")
            self.geometry("980x620")
            self.minsize(860, 520)
            self.configure(bg="#101418")
            self.transient(parent)
            self.grab_set()

            initial_kind = infer_kind(option.pack_id) if option else "pack"
            self.kind_label = tk.StringVar(value=PACK_TYPE_LABELS.get(initial_kind, "Pack"))
            self.name_text = tk.StringVar(value=option.pack_id if option else "")
            self.slice_text = tk.StringVar(value=find_first_manifest_slice_for_pack(project_root, option.pack_id) if option else "")
            self.target_text = tk.StringVar()
            self.status_text = tk.StringVar()

            self._build_layout()
            self._refresh_target()
            self._refresh_sources()

        def _build_layout(self) -> None:
            root = ttk.Frame(self, padding=(14, 14, 14, 14))
            root.pack(fill=tk.BOTH, expand=True)
            root.columnconfigure(1, weight=1)
            root.rowconfigure(5, weight=1)

            ttk.Label(root, text="Pack Type").grid(row=0, column=0, sticky="w", padx=(0, 10), pady=(0, 8))
            type_combo = ttk.Combobox(
                root,
                textvariable=self.kind_label,
                state="readonly",
                values=[PACK_TYPE_LABELS[key] for key in KIND_FOLDERS],
                width=20,
            )
            type_combo.grid(row=0, column=1, sticky="w", pady=(0, 8))
            type_combo.bind("<<ComboboxSelected>>", lambda _event: self._refresh_target())

            ttk.Label(root, text="Name / ID").grid(row=1, column=0, sticky="w", padx=(0, 10), pady=(0, 8))
            name_entry = ttk.Entry(root, textvariable=self.name_text)
            name_entry.grid(row=1, column=1, sticky="ew", pady=(0, 8))
            name_entry.bind("<KeyRelease>", lambda _event: self._refresh_target())

            ttk.Label(root, text="Target Root").grid(row=2, column=0, sticky="w", padx=(0, 10), pady=(0, 8))
            ttk.Label(root, textvariable=self.target_text).grid(row=2, column=1, sticky="ew", pady=(0, 8))

            ttk.Label(root, text="Manifest Slice").grid(row=3, column=0, sticky="w", padx=(0, 10), pady=(0, 8))
            ttk.Entry(root, textvariable=self.slice_text).grid(row=3, column=1, sticky="ew", pady=(0, 8))

            source_toolbar = ttk.Frame(root)
            source_toolbar.grid(row=4, column=0, columnspan=2, sticky="ew", pady=(6, 8))
            ttk.Button(source_toolbar, text="Add Sheet Source", command=lambda: self.add_source("sprite_sheet")).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(source_toolbar, text="Add Library Source", command=lambda: self.add_source("sprite_library")).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(source_toolbar, text="Add Slice Source", command=lambda: self.add_source("sprite_slice")).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(source_toolbar, text="Edit Source", command=self.edit_source).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(source_toolbar, text="Remove Source", command=self.remove_source).pack(side=tk.LEFT)

            columns = ("type", "asset", "label", "target")
            self.source_tree = ttk.Treeview(root, columns=columns, show="headings", selectmode="browse", height=12)
            headings = {
                "type": "Type",
                "asset": "Asset",
                "label": "Label / Prefix",
                "target": "Target Folder",
            }
            widths = {"type": 130, "asset": 430, "label": 150, "target": 260}
            for column in columns:
                self.source_tree.heading(column, text=headings[column])
                self.source_tree.column(column, width=widths[column], minwidth=80, anchor=tk.W, stretch=True)
            self.source_tree.grid(row=5, column=0, columnspan=2, sticky="nsew")
            self.source_tree.bind("<Double-1>", lambda _event: self.preview_source())

            footer = ttk.Frame(root)
            footer.grid(row=6, column=0, columnspan=2, sticky="ew", pady=(12, 0))
            footer.columnconfigure(0, weight=1)
            ttk.Label(footer, textvariable=self.status_text).grid(row=0, column=0, sticky="w")
            ttk.Button(footer, text="Save Pack", command=self.save_pack).grid(row=0, column=1, padx=(8, 0))
            ttk.Button(footer, text="Cancel", command=self.destroy).grid(row=0, column=2, padx=(8, 0))

        def _kind(self) -> str:
            return PACK_LABEL_TO_KIND.get(self.kind_label.get(), "form")

        def _pack_id(self) -> str:
            return make_pack_id(self._kind(), self.name_text.get())

        def _refresh_target(self) -> None:
            pack_id = self._pack_id()
            target = external_pack_root(self.external_root, self._kind(), pack_id) if pack_id else self.external_root
            self.target_text.set(str(target))

        def _refresh_sources(self) -> None:
            self.source_tree.delete(*self.source_tree.get_children())
            for index, source in enumerate(self.sources):
                self.source_tree.insert("", tk.END, iid=str(index), values=source.row_values())

        def add_source(self, source_type: str) -> None:
            if source_type == "sprite_library":
                self.add_library_sources()
                return
            dialog = SourceEditorDialog(self, self.project_root, source_type, None)
            self.wait_window(dialog)
            if dialog.source:
                self.sources.append(dialog.source)
                self._refresh_sources()

        def add_library_sources(self) -> None:
            selected_paths = filedialog.askopenfilenames(
                parent=self,
                initialdir=str(self.project_root / "Assets"),
                filetypes=[
                    ("Sprite libraries", (f"*{CUSTOM_LIBRARY_EXTENSION}", f"*{LEGACY_LIBRARY_EXTENSION}")),
                    ("Custom sheet libraries", f"*{CUSTOM_LIBRARY_EXTENSION}"),
                    ("Legacy sprite libraries", f"*{LEGACY_LIBRARY_EXTENSION}"),
                    ("All files", "*.*"),
                ],
            )
            if not selected_paths:
                return

            added_count = 0
            for selected_path in selected_paths:
                source = create_authoring_source("sprite_library", selected_path, self.project_root)
                error = validate_authoring_source(source)
                if error:
                    messagebox.showerror("Add Library Source", error, parent=self)
                    continue
                if self.has_source_asset(source.asset_path):
                    continue
                self.sources.append(source)
                added_count += 1

            if added_count:
                self._refresh_sources()
            self.status_text.set(f"Added {added_count} library source(s)")

        def has_source_asset(self, asset_path: str) -> bool:
            normalized = normalize_slashes(asset_path)
            if not normalized:
                return False
            for source in self.sources:
                if normalize_slashes(source.asset_path).lower() == normalized.lower():
                    return True
            return False

        def edit_source(self) -> None:
            index = self._selected_source_index()
            if index < 0:
                return
            dialog = SourceEditorDialog(self, self.project_root, self.sources[index].source_type, self.sources[index])
            self.wait_window(dialog)
            if dialog.source:
                self.sources[index] = dialog.source
                self._refresh_sources()

        def preview_source(self) -> None:
            index = self._selected_source_index()
            if index < 0:
                return
            SourcePreviewDialog(self, self.project_root, self.external_root, self.sources[index].source_label())

        def remove_source(self) -> None:
            index = self._selected_source_index()
            if index < 0:
                return
            del self.sources[index]
            self._refresh_sources()

        def _selected_source_index(self) -> int:
            selection = self.source_tree.selection()
            if not selection:
                return -1
            index = int(selection[0])
            return index if 0 <= index < len(self.sources) else -1

        def save_pack(self) -> None:
            kind = self._kind()
            pack_id = self._pack_id()
            if not pack_id:
                messagebox.showerror("Pack Editor", "Pack name is required.", parent=self)
                return
            for source in self.sources:
                error = validate_authoring_source(source)
                if error:
                    messagebox.showerror("Pack Editor", error, parent=self)
                    return
            try:
                manifest_path = write_authoring_manifest(
                    self.project_root,
                    self.external_root,
                    pack_id,
                    kind,
                    self.sources,
                )
            except (OSError, RuntimeError, SystemExit) as ex:
                messagebox.showerror("Pack Editor", str(ex), parent=self)
                return

            slice_id = sanitize_identifier(self.slice_text.get())
            if slice_id:
                add_pack_to_content_manifest_slice(self.project_root, slice_id, pack_id)
            self.saved = True
            self.status_text.set(f"Saved {manifest_path}")
            self.destroy()

    class SourceEditorDialog(tk.Toplevel):
        def __init__(
            self,
            parent: tk.Toplevel,
            project_root: Path,
            source_type: str,
            source: SourceAssetSpec | None,
        ) -> None:
            super().__init__(parent)
            self.project_root = project_root
            self.source: SourceAssetSpec | None = None
            initial_type = source.source_type if source else source_type
            self.type_label = tk.StringVar(value=SOURCE_TYPE_LABELS.get(initial_type, "Sprite Sheet"))
            self.asset_text = tk.StringVar(value=source.asset_path if source else "")
            self.label_text = tk.StringVar(value=source.label if source else "")
            self.library_text = tk.StringVar(value=source.library_name if source else "")
            self.category_text = tk.StringVar(value=source.category if source else "")
            initial_label_prefix = ""
            if source:
                initial_label_prefix = source.label_prefix if source.source_type == "sprite_sheet" else source.label
            self.label_prefix_text = tk.StringVar(value=initial_label_prefix)
            self.normal_text = tk.StringVar(value=source.normal_asset_path if source else "")
            self.target_text = tk.StringVar(value=source.target_folder if source else "")
            self.title("Source Asset")
            self.geometry("820x420")
            self.minsize(720, 360)
            self.configure(bg="#101418")
            self.transient(parent)
            self.grab_set()
            self._build_layout()

        def _build_layout(self) -> None:
            root = ttk.Frame(self, padding=(14, 14, 14, 14))
            root.pack(fill=tk.BOTH, expand=True)
            root.columnconfigure(1, weight=1)

            ttk.Label(root, text="Source Type").grid(row=0, column=0, sticky="w", padx=(0, 10), pady=(0, 8))
            type_combo = ttk.Combobox(
                root,
                textvariable=self.type_label,
                state="readonly",
                values=list(SOURCE_TYPE_LABELS.values()),
                width=20,
            )
            type_combo.grid(row=0, column=1, sticky="w", pady=(0, 8))
            type_combo.bind("<<ComboboxSelected>>", lambda _event: self._source_type_changed())

            ttk.Label(root, text="Asset Path").grid(row=1, column=0, sticky="w", padx=(0, 10), pady=(0, 8))
            ttk.Entry(root, textvariable=self.asset_text).grid(row=1, column=1, sticky="ew", pady=(0, 8))
            ttk.Button(root, text="Browse", command=self.browse_asset).grid(row=1, column=2, padx=(8, 0), pady=(0, 8))

            ttk.Label(root, text="Library Name").grid(row=2, column=0, sticky="w", padx=(0, 10), pady=(0, 8))
            ttk.Entry(root, textvariable=self.library_text).grid(row=2, column=1, columnspan=2, sticky="ew", pady=(0, 8))

            ttk.Label(root, text="Category").grid(row=3, column=0, sticky="w", padx=(0, 10), pady=(0, 8))
            ttk.Entry(root, textvariable=self.category_text).grid(row=3, column=1, columnspan=2, sticky="ew", pady=(0, 8))

            ttk.Label(root, text="Label / Prefix").grid(row=4, column=0, sticky="w", padx=(0, 10), pady=(0, 8))
            ttk.Entry(root, textvariable=self.label_prefix_text).grid(row=4, column=1, columnspan=2, sticky="ew", pady=(0, 8))

            ttk.Label(root, text="Normal Texture").grid(row=5, column=0, sticky="w", padx=(0, 10), pady=(0, 8))
            ttk.Entry(root, textvariable=self.normal_text).grid(row=5, column=1, sticky="ew", pady=(0, 8))
            ttk.Button(root, text="Browse", command=self.browse_normal_asset).grid(row=5, column=2, padx=(8, 0), pady=(0, 8))

            ttk.Label(root, text="Target Folder").grid(row=6, column=0, sticky="w", padx=(0, 10), pady=(0, 8))
            ttk.Entry(root, textvariable=self.target_text).grid(row=6, column=1, sticky="ew", pady=(0, 8))
            ttk.Button(root, text="Default", command=self.use_default_target).grid(row=6, column=2, padx=(8, 0), pady=(0, 8))

            footer = ttk.Frame(root)
            footer.grid(row=7, column=0, columnspan=3, sticky="e", pady=(12, 0))
            ttk.Button(footer, text="Save Source", command=self.save_source).pack(side=tk.LEFT, padx=(0, 8))
            ttk.Button(footer, text="Cancel", command=self.destroy).pack(side=tk.LEFT)

        def _source_type(self) -> str:
            return SOURCE_LABEL_TO_TYPE.get(self.type_label.get(), "sprite_sheet")

        def _source_type_changed(self) -> None:
            return

        def browse_asset(self) -> None:
            source_type = self._source_type()
            filetypes = [("Sprite sheet", "*.png"), ("All files", "*.*")]
            if source_type == "sprite_library":
                filetypes = [
                    ("Sprite libraries", (f"*{CUSTOM_LIBRARY_EXTENSION}", f"*{LEGACY_LIBRARY_EXTENSION}")),
                    ("Custom sheet libraries", f"*{CUSTOM_LIBRARY_EXTENSION}"),
                    ("Legacy sprite libraries", f"*{LEGACY_LIBRARY_EXTENSION}"),
                    ("All files", "*.*"),
                ]
            selected = filedialog.askopenfilename(
                parent=self,
                initialdir=str(self.project_root / "Assets"),
                filetypes=filetypes,
            )
            if not selected:
                return
            source = create_authoring_source(source_type, selected, self.project_root)
            self.asset_text.set(source.asset_path)
            if source.source_type == "sprite_sheet" and not self.label_prefix_text.get().strip():
                self.label_prefix_text.set(source.label_prefix)
            if not self.target_text.get().strip():
                self.target_text.set(source.target_folder)

        def browse_normal_asset(self) -> None:
            selected = filedialog.askopenfilename(
                parent=self,
                initialdir=str(self.project_root / "Assets"),
                filetypes=[("Sprite sheet", "*.png"), ("All files", "*.*")],
            )
            if selected:
                self.normal_text.set(normalize_asset_reference(selected, self.project_root))

        def use_default_target(self) -> None:
            self.target_text.set(default_target_folder_for_asset(self.asset_text.get()))

        def save_source(self) -> None:
            source_type = self._source_type()
            asset_path = normalize_asset_reference(self.asset_text.get(), self.project_root)
            target_folder = normalize_slashes(self.target_text.get())
            if not target_folder:
                target_folder = default_target_folder_for_asset(asset_path)
            source = SourceAssetSpec(
                source_type=source_type,
                asset_path=asset_path,
                target_folder=target_folder,
                label=self.label_prefix_text.get().strip() if source_type == "sprite_slice" else self.label_text.get().strip(),
                library_name=normalize_slashes(self.library_text.get()),
                category=self.category_text.get().strip(),
                label_prefix=self.label_prefix_text.get().strip(),
                normal_asset_path=normalize_asset_reference(self.normal_text.get(), self.project_root),
            )
            error = validate_authoring_source(source)
            if error:
                messagebox.showerror("Source Asset", error, parent=self)
                return
            self.source = source
            self.destroy()

    ContentPackIterationApp().mainloop()


if __name__ == "__main__":
    raise SystemExit(main())
