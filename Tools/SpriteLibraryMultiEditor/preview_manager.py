import re
from pathlib import Path
import tkinter as tk  # noqa: PLC0415
from data import SpriteLibraryDocument, resolve_sprite_path


def _get_sprite_rect(meta_path: Path, file_id: str) -> tuple[int, int, int, int] | None:
    """Find the rect (x, y, w, h) for a sprite with the given file_id in the meta file."""
    if not meta_path.exists():
        return None
    try:
        content = meta_path.read_text(encoding="utf-8", errors="ignore")
        if "spriteSheet:" not in content:
            return None
        sprites_part = content.split("spriteSheet:")[1]
        if "sprites:" not in sprites_part:
            return None
        # Split by individual sprite blocks starting with "    - "
        blocks = sprites_part.split("    - ")
        for block in blocks:
            if re.search(rf"internalID:\s*{file_id}\b", block):
                x_match = re.search(r"\bx:\s*(\d+)", block)
                y_match = re.search(r"\by:\s*(\d+)", block)
                w_match = re.search(r"\bwidth:\s*(\d+)", block)
                h_match = re.search(r"\bheight:\s*(\d+)", block)
                if x_match and y_match and w_match and h_match:
                    return (
                        int(x_match.group(1)),
                        int(y_match.group(1)),
                        int(w_match.group(1)),
                        int(h_match.group(1)),
                    )
    except Exception as exc:
        print(f"[SpriteLibEditor] Error parsing meta file {meta_path}: {exc}")
    return None


def update_preview_display(
    sprite_ref: str | None,
    preview_label: tk.Label,
    documents: list[SpriteLibraryDocument],
    document_index: int = 0,
) -> None:
    """Update the sprite preview display with a cropped sprite image or thumbnail."""
    if not sprite_ref or not documents:
        preview_label.config(image="")
        preview_label.config(text="No sprite selected")
        return

    if document_index < 0 or document_index >= len(documents):
        preview_label.config(image="")
        preview_label.config(text="No sprite selected")
        return

    document = documents[document_index]
    if not document.path:
        preview_label.config(image="")
        preview_label.config(text="No sprite selected")
        return

    guid = None
    file_id = None
    if ":" in sprite_ref:
        guid, file_id = sprite_ref.split(":", 1)
    else:
        guid = sprite_ref

    img_path = resolve_sprite_path(guid, document.path) if guid else None

    if not img_path or not img_path.exists():
        preview_label.config(image="")
        preview_label.config(text=f"Sprite not found: {guid}")
        return

    max_size = (400, 300)
    try:
        from PIL import Image

        img = Image.open(img_path)

        # Try to extract the specific slice from the meta file
        if file_id:
            meta_path = Path(str(img_path) + ".meta")
            rect = _get_sprite_rect(meta_path, file_id)
            if rect:
                x, y, w, h = rect
                # Convert Unity's bottom-left origin to PIL's top-left origin
                left = x
                upper = img.height - (y + h)
                right = x + w
                lower = img.height - y

                # Make sure coordinates are within image bounds
                left = max(0, min(left, img.width))
                upper = max(0, min(upper, img.height))
                right = max(0, min(right, img.width))
                lower = max(0, min(lower, img.height))

                if right > left and lower > upper:
                    img = img.crop((left, upper, right, lower))

        img.thumbnail(max_size, Image.Resampling.LANCZOS)
        from PIL import ImageTk

        photo = ImageTk.PhotoImage(img)
        preview_label.image = photo
        preview_label.config(image=photo, text="")
    except Exception as exc:
        preview_label.config(image="")
        preview_label.config(text=f"Error loading sprite: {exc}")


def scan_all_previews(
    documents: list[SpriteLibraryDocument], status_text_var: tk.StringVar
) -> tuple[int, bool]:
    """Scan and load all sprite previews from open documents."""
    from tkinter.messagebox import showwarning  # noqa: PLC0415
    from data import scan_all_previews as scan_document_previews  # noqa: PLC0415

    try:
        loaded = scan_document_previews(documents)
        status_text_var.set(f"Scanned {loaded} previews")
        return loaded, True
    except ImportError:
        showwarning("Missing Dependency", "Pillow (PIL) is required for preview scanning.", parent=None)
        return 0, False


# Type stubs for external references (removed - not needed)
