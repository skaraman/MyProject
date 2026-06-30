# Data Operations module for Sprite Library Multi-Editor
"""Data manipulation operations on sprite library documents."""

from data import (  # noqa: F401
    SpriteCategory,
    SpriteLibraryDocument,
    find_label,
    merge_category,
    merge_labels,
)


def remove_category_from_document(
    document: SpriteLibraryDocument, category: SpriteCategory
) -> None:
    """Remove a category from its document, merging entries into the first category.

    Args:
        document: The parent document containing the category to remove
        category: The category to remove (entries will be merged if non-empty)
    """
    if not category.entries:
        # Empty category - just remove it
        document.categories.remove(category)
    else:
        # Non-empty category - merge entries into first category, then remove
        target = document.categories[0] if document.categories else category
        for entry in category.entries:
            existing_idx = find_label(target, entry.name)
            if existing_idx >= 0:
                # Label already exists - replace it (preserve original behavior with replace=True)
                target.entries[existing_idx] = entry.clone()
            else:
                # New label - append to target
                target.entries.append(entry.clone())
        document.categories.remove(category)


def remove_label_from_category(
    document: SpriteLibraryDocument, category: SpriteCategory, label_index: int
) -> None:
    """Remove a label from its category by index.

    Args:
        document: The parent document (not used but kept for API consistency)
        category: The category containing the label to remove
        label_index: The index of the label to remove
    """
    if 0 <= label_index < len(category.entries):
        category.entries.pop(label_index)


def remove_labels_without_prefix(
    document: SpriteLibraryDocument, category: SpriteCategory, prefix: str
) -> int:
    """Remove category labels whose names do not start with prefix."""
    if not prefix:
        return 0

    kept_entries = []
    removed_count = 0

    for entry in category.entries:
        if entry.name.startswith(prefix):
            kept_entries.append(entry)
        else:
            removed_count += 1

    if removed_count == 0:
        return 0

    category.entries = kept_entries
    return removed_count
