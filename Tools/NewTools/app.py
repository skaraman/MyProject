if __package__:
    from .common import CommonMixin
    from .common import tk
    from .image_tools import ImageToolsMixin
    from .spritelib_overwrite import SpriteLibraryOverwriteMixin
    from .spritelib_replace import SpriteLibraryReplaceMixin
    from .spritelib_ui import SpriteLibraryUiMixin
    from .spritelib_yaml import SpriteLibraryYamlMixin
else:
    from pathlib import Path
    import sys

    sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

    from NewTools.common import CommonMixin
    from NewTools.common import tk
    from NewTools.image_tools import ImageToolsMixin
    from NewTools.spritelib_overwrite import SpriteLibraryOverwriteMixin
    from NewTools.spritelib_replace import SpriteLibraryReplaceMixin
    from NewTools.spritelib_ui import SpriteLibraryUiMixin
    from NewTools.spritelib_yaml import SpriteLibraryYamlMixin


class AA(
    CommonMixin,
    ImageToolsMixin,
    SpriteLibraryUiMixin,
    SpriteLibraryOverwriteMixin,
    SpriteLibraryReplaceMixin,
    SpriteLibraryYamlMixin,
):
    pass


def main():
    root = tk.Tk()
    AA(root)
    root.mainloop()
