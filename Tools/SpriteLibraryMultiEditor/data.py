# Shared utilities, dataclasses, parsing, and rendering functions
from __future__ import annotations

import re
import copy
from pathlib import Path
from typing import Any
from dataclasses import dataclass, field

# Regex patterns for parsing Unity-style sprite sheet library files.
CUSTOM_LIBRARY_EXTENSION = ".spriteSheetLib"
LEGACY_LIBRARY_EXTENSION = ".spriteLib"
LEGACY_LIBRARY_MARKER = "Unity.2D.Animation.Runtime::UnityEngine.U2D.Animation.SpriteLibrarySourceAsset"
CUSTOM_LIBRARY_MARKER = "Esperanza.SpriteSheetLibrary"
LIBRARY_EXTENSIONS = (
    CUSTOM_LIBRARY_EXTENSION,
    LEGACY_LIBRARY_EXTENSION,
)
LIBRARY_SUFFIXES = {extension.lower() for extension in LIBRARY_EXTENSIONS}

CATEGORY_RE = re.compile(r"^  - m_Name:\s*(.*?)\s*$")
ENTRY_RE = re.compile(r"^    - m_Name:\s*(.*?)\s*$")
HASH_RE = re.compile(r"^\s*m_Hash:\s*(-?\d+)\s*$")
SPRITE_REF_RE = re.compile(r"fileID:\s*([^,\s}]+).*?guid:\s*([0-9a-fA-F]{32})")

_GUID_CACHE: dict[str, str] = {}
_PROJECT_ROOT: Path | None = None


def _find_project_root(path: Path) -> Path | None:
    """Find the project root by walking up from a given path until Assets/ is found."""
    current = path.resolve()
    while True:
        if (current / "Assets").is_dir():
            return current
        parent = current.parent
        if parent == current:
            break
        current = parent
    return None


# Public alias for use by other modules
find_project_root = _find_project_root


def _build_guid_cache(project_root: Path) -> dict[str, str]:
    """Build a cache mapping sprite GUIDs to their relative file paths."""
    global _PROJECT_ROOT  # noqa: PLW0603
    resolved = project_root.resolve()
    if _GUID_CACHE and _PROJECT_ROOT == resolved:
        return _GUID_CACHE
    _PROJECT_ROOT = resolved
    _GUID_CACHE.clear()
    assets = project_root / "Assets"
    scanned = 0
    for meta in assets.rglob("*.meta"):
        scanned += 1
        text = meta.read_text(encoding="utf-8", errors="ignore")
        m = re.search(r"^guid:\s*([0-9a-fA-F]{32})", text, re.M)
        if m:
            guid = m.group(1).lower()
            rel = meta.with_suffix("").relative_to(resolved)
            _GUID_CACHE[guid] = str(rel)
    print(f"[SpriteLibEditor] GUID cache project={resolved} metas={scanned} sprites={len(_GUID_CACHE)}")
    return _GUID_CACHE


def resolve_sprite_path(guid: str, sprite_lib_path: Path) -> Path | None:
    """Resolve a sprite GUID to its file path using the project's guid cache."""
    project_root = _find_project_root(sprite_lib_path)
    if not project_root:
        return None
    cache = _build_guid_cache(project_root)
    rel = cache.get(guid.lower())
    if rel:
        full = (project_root / rel).resolve()
        if full.exists():
            return full
    return None


# Dataclasses
@dataclass
class SpriteLabel:
    name: str
    hash_text: str = "0"
    raw_lines: list[str] = field(default_factory=list)
    sprite_ref: str = ""

    def clone(self) -> "SpriteLabel":
        return copy.deepcopy(self)


@dataclass
class SpriteCategory:
    name: str
    hash_text: str = "0"
    entries: list[SpriteLabel] = field(default_factory=list)

    def clone(self) -> "SpriteCategory":
        return copy.deepcopy(self)


@dataclass
class SpriteLibraryDocument:
    path: Path
    header_lines: list[str]
    categories: list[SpriteCategory]
    line_ending: str = "\n"
    dirty: bool = False

    @property
    def title(self) -> str:
        suffix = " *" if self.dirty else ""
        return self.path.name + suffix


# Parsing functions
def parse_sprite_library(path: Path) -> SpriteLibraryDocument:
    """Parse a .spriteLib file into a SpriteLibraryDocument."""
    text = path.read_text(encoding="utf-8-sig")
    line_ending = "\r\n" if "\r\n" in text else "\n"
    lines = text.splitlines()
    library_index = find_library_index(lines)
    if library_index < 0:
        raise ValueError(f"Missing m_Library block: {path}")

    header = lines[:library_index + 1]
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
        categories.append(parse_category_block(lines[block_start:index]))

    return SpriteLibraryDocument(path=path, header_lines=header, categories=categories, line_ending=line_ending)


def find_library_index(lines: list[str]) -> int:
    """Find the index of the m_Library marker in lines."""
    for index, line in enumerate(lines):
        if line == "  m_Library:" or line == "  m_Library: []":
            return index
    return -1


def parse_category_block(lines: list[str]) -> SpriteCategory:
    """Parse a category block from lines into a SpriteCategory."""
    first = CATEGORY_RE.match(lines[0])
    category = SpriteCategory(name=first.group(1).strip() if first else "")
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
        while index < len(lines) and not ENTRY_RE.match(lines[index]) and not is_category_trailer_line(lines[index]):
            index += 1
        category.entries.append(parse_entry_block(lines[entry_start:index]))
    return category


def parse_entry_block(lines: list[str]) -> SpriteLabel:
    """Parse an entry block from lines into a SpriteLabel."""
    first = ENTRY_RE.match(lines[0])
    label = SpriteLabel(name=first.group(1).strip() if first else "", raw_lines=list(lines))
    for index, line in enumerate(lines):
        hash_match = HASH_RE.match(line)
        if hash_match:
            label.hash_text = hash_match.group(1)
        if "m_SpriteOverride:" in line or "m_Sprite:" in line:
            combined = line
            if index + 1 < len(lines) and lines[index + 1].startswith("        "):
                combined += " " + lines[index + 1].strip()
            ref_match = SPRITE_REF_RE.search(combined)
            if ref_match:
                label.sprite_ref = f"{ref_match.group(2)}:{ref_match.group(1)}"
    return label


# Rendering functions
def write_sprite_library(document: SpriteLibraryDocument) -> None:
    """Write a SpriteLibraryDocument back to a .spriteLib file."""
    lines = list(document.header_lines)
    library_index = find_library_index(lines)
    if library_index >= 0:
        if document.categories:
            lines[library_index] = "  m_Library:"
        else:
            lines[library_index] = "  m_Library: []"

    for category in document.categories:
        lines.extend(render_category(category))
    text = document.line_ending.join(lines) + document.line_ending
    document.path.write_text(text, encoding="utf-8")
    document.dirty = False


def render_category(category: SpriteCategory) -> list[str]:
    """Render a SpriteCategory as lines for writing to .spriteLib file."""
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
    """Render a SpriteLabel as lines for writing to .spriteLib file."""
    if entry.raw_lines:
        return list(entry.raw_lines)
    sprite_value = "{fileID: 0}"
    if entry.sprite_ref and ":" in entry.sprite_ref:
        guid, file_id = entry.sprite_ref.split(":", 1)
        sprite_value = f"{{fileID: {file_id}, guid: {guid}, type: 3}}"
    return [
        f"    - m_Name: {entry.name}",
        f"      m_Hash: {entry.hash_text or '0'}",
        f"      m_Sprite: {sprite_value}",
        "      m_FromMain: 0",
        f"      m_SpriteOverride: {sprite_value}",
    ]


# Utility functions
def is_category_trailer_line(line: str) -> bool:
    """Check if a line marks the end of a category block."""
    return line.startswith("    m_FromMain:") or line.startswith("    m_EntryOverrideCount:")


def expand_input_paths(values: list[str]) -> list[Path]:
    """Expand input paths to find custom sheet libraries and legacy sprite libraries."""
    result: list[Path] = []
    for value in values:
        path = Path(value).resolve()
        if path.is_dir():
            for extension in LIBRARY_EXTENSIONS:
                pattern = f"*{extension}"
                candidates = sorted(path.rglob(pattern), key=lambda item: str(item).lower())
                for candidate in candidates:
                    add_unique_path(result, candidate)
        elif path.exists() and path.suffix.lower() in LIBRARY_SUFFIXES:
            add_unique_path(result, path)
    return result


def migrate_sprite_library_file(source_path: Path) -> Path:
    """Rename one legacy .spriteLib into the custom sheet library format."""
    source_path = source_path.resolve()
    if source_path.suffix.lower() != LEGACY_LIBRARY_EXTENSION.lower():
        raise ValueError(f"Expected a {LEGACY_LIBRARY_EXTENSION} file: {source_path}")
    if not source_path.exists():
        raise FileNotFoundError(f"Missing sprite library file: {source_path}")

    target_path = source_path.with_suffix(CUSTOM_LIBRARY_EXTENSION)
    if target_path.exists():
        raise FileExistsError(f"Target already exists: {target_path}")

    text = source_path.read_text(encoding="utf-8-sig", errors="replace")
    text = text.replace(LEGACY_LIBRARY_MARKER, CUSTOM_LIBRARY_MARKER)
    target_path.write_text(text, encoding="utf-8")

    source_meta = Path(str(source_path) + ".meta")
    if source_meta.exists():
        target_meta = Path(str(target_path) + ".meta")
        if target_meta.exists():
            raise FileExistsError(f"Target meta already exists: {target_meta}")
        source_meta.rename(target_meta)

    source_path.unlink()
    return target_path


def add_unique_path(paths: list[Path], path: Path) -> None:
    """Add a path to the list if it's not already present (by resolved path)."""
    resolved = path.resolve()
    if not any(existing.resolve() == resolved for existing in paths):
        paths.append(resolved)


def find_category(document: SpriteLibraryDocument, name: str) -> SpriteCategory | None:
    """Find a category by name (case-insensitive)."""
    for category in document.categories:
        if category.name.lower() == name.lower():
            return category
    return None


def find_label(category: SpriteCategory, name: str) -> int:
    """Find the index of a label by name (case-insensitive), or -1 if not found."""
    for index, label in enumerate(category.entries):
        if label.name.lower() == name.lower():
            return index
    return -1


def merge_category(target_doc: SpriteLibraryDocument, category: SpriteCategory, replace: bool) -> bool:
    """Merge a category into a target document. Returns True if any changes were made."""
    existing = find_category(target_doc, category.name)
    if existing is None:
        target_doc.categories.append(category.clone())
        return True
    return merge_labels(existing, category.entries, replace)


def merge_labels(target_category: SpriteCategory, labels: list[SpriteLabel], replace: bool) -> bool:
    """Merge a list of labels into a target category. Returns True if any changes were made."""
    changed = False
    for label in labels:
        existing_index = find_label(target_category, label.name)
        if existing_index >= 0:
            if not replace:
                continue
            target_category.entries[existing_index] = label.clone()
            changed = True
            continue
        target_category.entries.append(label.clone())
        changed = True
    return changed


def scan_all_previews(documents: list[SpriteLibraryDocument]) -> int:
    """Pre-load all sprite previews from currently open libraries.
    Returns count of successfully loaded images."""
    try:
        from PIL import Image  # noqa: N812
    except ImportError:
        return 0

    loaded = 0
    for document in documents:
        project_root = _find_project_root(document.path)
        if not project_root:
            continue
        cache = _build_guid_cache(project_root)
        for category in document.categories:
            for label in category.entries:
                guid = label.sprite_ref.split(":")[0] if label.sprite_ref else None
                if not guid:
                    continue
                img_path = resolve_sprite_path(guid, document.path)
                if not img_path or not img_path.exists():
                    continue
                try:
                    with Image.open(img_path):
                        pass
                    loaded += 1
                except Exception:
                    pass
    return loaded
