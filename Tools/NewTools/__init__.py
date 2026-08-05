if __package__:
    from .app import AA
    from .app import main
else:
    from pathlib import Path
    import sys

    sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

    from NewTools.app import AA
    from NewTools.app import main
