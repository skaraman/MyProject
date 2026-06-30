# Editor entry point for Sprite Library Multi-Editor
"""Entry point module that launches the GUI application."""

from pathlib import Path
import sys

# Import the core application class
from app import main as _main


def launch_ui(initial_paths: list[str] | None = None) -> int:
    """Launch the Sprite Library Multi-Editor GUI.

    Args:
        initial_paths: Optional list of .spriteLib files or directories to open on startup

    Returns:
        Exit code (0 for success)
    """
    return _main(initial_paths)


if __name__ == "__main__":
    exit(launch_ui(sys.argv[1:]))
