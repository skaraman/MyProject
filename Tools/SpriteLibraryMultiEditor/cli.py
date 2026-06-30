# CLI module for Sprite Library Multi Editor
"""Command-line interface functionality for the Sprite Library Multi-Editor.

This module provides command-line operations that build on top of the shared
dataclasses and utilities in data, with UI-specific helpers from ui_utils.
"""

import argparse
import sys
from pathlib import Path

# Import all shared definitions from data module
from data import (  # noqa: F401
    SpriteLabel,
    SpriteCategory,
    SpriteLibraryDocument,
    parse_sprite_library,
    write_sprite_library,
    find_category,
    find_label,
    merge_category,
    expand_input_paths,
    scan_all_previews,
)

# Import regex patterns from data module
from data import (  # noqa: F401
    CATEGORY_RE,
    ENTRY_RE,
    HASH_RE,
    SPRITE_REF_RE,
)


def parse_arguments() -> argparse.Namespace:
    """Parse command-line arguments for CLI operations."""
    parser = argparse.ArgumentParser(
        description="Sprite Library Multi Editor - CLI module"
    )
    subparsers = parser.add_subparsers(dest="command", help="Available commands")

    # Parse command
    parse_parser = subparsers.add_parser("parse", help="Parse a .spriteLib file")
    parse_parser.add_argument("path", type=Path, help="Path to the .spriteLib file")

    # Write command
    write_parser = subparsers.add_parser("write", help="Write changes back to a .spriteLib file")
    write_parser.add_argument("path", type=Path, help="Path to the .spriteLib file")

    # Scan previews command
    scan_parser = subparsers.add_parser(
        "scan-previews", help="Scan and pre-load all sprite previews"
    )
    scan_parser.add_argument(
        "--paths", nargs="+", default=["."], help="Paths to search for .spriteLib files"
    )

    return parser.parse_args()


def main() -> int:
    """Main entry point for CLI command-line operations."""
    args = parse_arguments()

    if args.command == "parse":
        document = parse_sprite_library(args.path)
        print(f"Parsed {args.path}:")
        print(f"  Categories: {len(document.categories)}")
        total_entries = sum(len(cat.entries) for cat in document.categories)
        print(f"  Total entries: {total_entries}")

    elif args.command == "write":
        if not hasattr(args, "document"):
            print("Error: No document provided. Use --help for usage.")
            return 1
        write_sprite_library(args.document)
        print(f"Wrote changes to {args.path}")

    elif args.command == "scan-previews":
        paths = expand_input_paths(args.paths)
        documents = [parse_sprite_library(path) for path in paths]
        loaded = scan_all_previews(documents)
        print(f"Loaded {loaded} sprite previews from {len(paths)} files")

    else:
        sys.exit(1)

    return 0


if __name__ == "__main__":
    sys.exit(main())
