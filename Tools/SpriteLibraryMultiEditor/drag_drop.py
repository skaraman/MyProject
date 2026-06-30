# Drag and Drop / Reordering functionality for Sprite Library Multi Editor
from __future__ import annotations

import sys
import tkinter as tk
from typing import Any

from tree_operations import (
    category_iid,
    label_iid,
    parse_iid_ref,
    select_item,
    is_tree_indicator_hit,
)


def on_tree_button_press(app: Any, event: Any) -> str | None:
    """Handle initial click on the tree view to determine if drag or collapse is starting."""
    clear_tree_drag(app)

    if is_tree_indicator_hit(app.category_tree, event.x, event.y):
        return None

    on_tree_drag_start(app, event)
    return None


def on_tree_drag_start(app: Any, event: Any) -> None:
    """Record initial drag start information."""
    item = app.category_tree.identify_row(event.y)
    app._drag_source_iid = item or None
    app._drag_selection_iids = app.category_tree.selection()
    app._drag_start_x = event.x
    app._drag_start_y = event.y


def on_tree_drag_drop(app: Any, event: Any) -> None:
    """Handle mouse release / drag drop event on the tree view."""
    if not app._drag_source_iid:
        return

    if not is_tree_drag(app, event):
        clear_tree_drag(app)
        return

    target_iid = app.category_tree.identify_row(event.y)
    source_iid = app._drag_source_iid
    selected_iids = get_tree_drag_selection(app, source_iid)
    clear_tree_drag(app)
    if not target_iid or target_iid == source_iid:
        return

    source = parse_iid_ref(source_iid)
    target = parse_iid_ref(target_iid)
    if not app._is_valid_ref(source) or not app._is_valid_ref(target):
        return

    # Handle document drag and drop (reordering loaded libraries)
    if source.kind == "document":
        if target.doc_index is not None and source.doc_index != target.doc_index:
            move_document_to_index(app, source.doc_index, target.doc_index)
            app._refresh_doc_list(override_selected_iid=f"doc:{target.doc_index}")
        return

    # Handle category drag and drop
    if source.kind == "category" and source.cat_index is not None:
        if source.doc_index == target.doc_index:
            if target.cat_index is not None:
                move_category_to_index(app, source.doc_index, source.cat_index, target.cat_index)
                app._refresh_doc_list(override_selected_iid=category_iid(source.doc_index, target.cat_index))
        else:
            if source_iid in selected_iids:
                source_cat_indices = []
                for iid in selected_iids:
                    ref = parse_iid_ref(iid)
                    if ref.kind == "category" and ref.doc_index == source.doc_index and ref.cat_index is not None:
                        if ref.cat_index not in source_cat_indices:
                            source_cat_indices.append(ref.cat_index)
                if source_cat_indices:
                    copy_categories_to_document(app, source.doc_index, source_cat_indices, target.doc_index)
                else:
                    copy_categories_to_document(app, source.doc_index, [source.cat_index], target.doc_index)
            else:
                copy_categories_to_document(app, source.doc_index, [source.cat_index], target.doc_index)
            app.refresh_tree()
        return

    # Handle label drag and drop
    if source.kind == "label" and source.cat_index is not None and source.label_index is not None:
        if source.doc_index == target.doc_index and source.cat_index == target.cat_index:
            if target.label_index is not None:
                move_label_to_index(app, source.doc_index, source.cat_index, source.label_index, target.label_index)
                app._refresh_doc_list(override_selected_iid=label_iid(source.doc_index, source.cat_index, target.label_index))
        elif target.cat_index is not None:
            if source_iid in selected_iids:
                source_labels_refs = []
                for iid in selected_iids:
                    ref = parse_iid_ref(iid)
                    if ref.kind == "label" and ref.doc_index == source.doc_index and ref.cat_index is not None and ref.label_index is not None:
                        source_labels_refs.append((ref.cat_index, ref.label_index))
                if source_labels_refs:
                    changed = copy_labels_to_category(
                        app,
                        source.doc_index,
                        source_labels_refs,
                        target.doc_index,
                        target.cat_index,
                    )
                else:
                    changed = copy_labels_to_category(
                        app,
                        source.doc_index,
                        [(source.cat_index, source.label_index)],
                        target.doc_index,
                        target.cat_index,
                    )
            else:
                changed = copy_labels_to_category(
                    app,
                    source.doc_index,
                    [(source.cat_index, source.label_index)],
                    target.doc_index,
                    target.cat_index,
                )
            if changed:
                app.refresh_tree()
                select_item(app.category_tree, target.doc_index, target.cat_index)


def is_tree_drag(app: Any, event: Any) -> bool:
    """Check if the drag distance exceeded the minimum threshold."""
    if app._drag_start_x is None or app._drag_start_y is None:
        return False
    dx = abs(event.x - app._drag_start_x)
    dy = abs(event.y - app._drag_start_y)
    return dx >= app._drag_min_distance or dy >= app._drag_min_distance


def clear_tree_drag(app: Any) -> None:
    """Clear drag-and-drop state variables."""
    app._drag_source_iid = None
    app._drag_selection_iids = ()
    app._drag_start_x = None
    app._drag_start_y = None


def get_tree_drag_selection(app: Any, source_iid: str) -> tuple[str, ...]:
    """Retrieve the selection items during drag start."""
    if source_iid in app._drag_selection_iids:
        return app._drag_selection_iids
    return app.category_tree.selection()


def move_document_to_index(app: Any, source_doc_index: int, target_doc_index: int) -> None:
    """Shift a document to another index in the documents list."""
    if source_doc_index == target_doc_index:
        return
    app._save_undo_state()
    doc = app.documents.pop(source_doc_index)
    app.documents.insert(target_doc_index, doc)
    app.selected_document_index = target_doc_index
    app.status_text.set(f"Moved library '{doc.path.name}'")


def move_category_to_index(app: Any, doc_index: int, source_cat_index: int, target_cat_index: int) -> None:
    """Shift a category to another index in the document."""
    if source_cat_index == target_cat_index:
        return
    app._save_undo_state()
    doc = app.documents[doc_index]
    category = doc.categories.pop(source_cat_index)
    doc.categories.insert(target_cat_index, category)
    doc.dirty = True
    app.selected_category_index = target_cat_index
    app.status_text.set(f"Moved category '{category.name}'")


def move_label_to_index(app: Any, doc_index: int, cat_index: int, source_label_index: int, target_label_index: int) -> None:
    """Shift a label to another index in the category."""
    if source_label_index == target_label_index:
        return
    app._save_undo_state()
    doc = app.documents[doc_index]
    cat = doc.categories[cat_index]
    label = cat.entries.pop(source_label_index)
    cat.entries.insert(target_label_index, label)
    doc.dirty = True
    app.selected_label_index = target_label_index
    app.status_text.set(f"Moved label '{label.name}'")


def move_label_up(app: Any, doc_index: int, cat_index: int, label_index: int) -> None:
    """Move a label up by one slot."""
    if label_index > 0:
        new_iid = label_iid(doc_index, cat_index, label_index - 1)
        move_label_to_index(app, doc_index, cat_index, label_index, label_index - 1)
        app._refresh_doc_list(override_selected_iid=new_iid)


def move_label_down(app: Any, doc_index: int, cat_index: int, label_index: int) -> None:
    """Move a label down by one slot."""
    doc = app.documents[doc_index]
    cat = doc.categories[cat_index]
    if label_index < len(cat.entries) - 1:
        new_iid = label_iid(doc_index, cat_index, label_index + 1)
        move_label_to_index(app, doc_index, cat_index, label_index, label_index + 1)
        app._refresh_doc_list(override_selected_iid=new_iid)


def move_category_up(app: Any, doc_index: int, cat_index: int) -> None:
    """Move a category up by one slot."""
    if cat_index > 0:
        new_iid = category_iid(doc_index, cat_index - 1)
        move_category_to_index(app, doc_index, cat_index, cat_index - 1)
        app._refresh_doc_list(override_selected_iid=new_iid)


def move_category_down(app: Any, doc_index: int, cat_index: int) -> None:
    """Move a category down by one slot."""
    doc = app.documents[doc_index]
    if cat_index < len(doc.categories) - 1:
        new_iid = category_iid(doc_index, cat_index + 1)
        move_category_to_index(app, doc_index, cat_index, cat_index + 1)
        app._refresh_doc_list(override_selected_iid=new_iid)


def move_document_up(app: Any, doc_index: int) -> None:
    """Move a document up by one slot in the documents list."""
    if doc_index > 0:
        new_iid = f"doc:{doc_index - 1}"
        move_document_to_index(app, doc_index, doc_index - 1)
        app._refresh_doc_list(override_selected_iid=new_iid)


def move_document_down(app: Any, doc_index: int) -> None:
    """Move a document down by one slot in the documents list."""
    if doc_index < len(app.documents) - 1:
        new_iid = f"doc:{doc_index + 1}"
        move_document_to_index(app, doc_index, doc_index + 1)
        app._refresh_doc_list(override_selected_iid=new_iid)


def on_tree_move_up_shortcut(app: Any, event: Any = None) -> str:
    """Shortcut handler for Alt+Up arrow to move selected tree nodes."""
    selection = app.category_tree.selection()
    if not selection:
        return "break"
    ref = parse_iid_ref(selection[0])
    if not app._is_valid_ref(ref):
        return "break"

    if ref.kind == "document":
        move_document_up(app, ref.doc_index)
    elif ref.kind == "category" and ref.cat_index is not None:
        move_category_up(app, ref.doc_index, ref.cat_index)
    elif ref.kind == "label" and ref.cat_index is not None and ref.label_index is not None:
        move_label_up(app, ref.doc_index, ref.cat_index, ref.label_index)
    return "break"


def on_tree_move_down_shortcut(app: Any, event: Any = None) -> str:
    """Shortcut handler for Alt+Down arrow to move selected tree nodes."""
    selection = app.category_tree.selection()
    if not selection:
        return "break"
    ref = parse_iid_ref(selection[0])
    if not app._is_valid_ref(ref):
        return "break"

    if ref.kind == "document":
        move_document_down(app, ref.doc_index)
    elif ref.kind == "category" and ref.cat_index is not None:
        move_category_down(app, ref.doc_index, ref.cat_index)
    elif ref.kind == "label" and ref.cat_index is not None and ref.label_index is not None:
        move_label_down(app, ref.doc_index, ref.cat_index, ref.label_index)
    return "break"


def on_doc_move_up_shortcut(app: Any, event: Any = None) -> str:
    """Shortcut handler for Alt+Up on the document listbox."""
    selection = app.doc_listbox.curselection()
    if not selection:
        return "break"
    index = selection[0]
    move_document_up(app, index)
    return "break"


def on_doc_move_down_shortcut(app: Any, event: Any = None) -> str:
    """Shortcut handler for Alt+Down on the document listbox."""
    selection = app.doc_listbox.curselection()
    if not selection:
        return "break"
    index = selection[0]
    move_document_down(app, index)
    return "break"


def copy_categories_to_document(app: Any, source_doc_index: int, source_cat_indices: list[int], target_doc_index: int) -> None:
    """Copy selected category objects to another document."""
    from data import merge_category

    if source_doc_index == target_doc_index:
        return
    app._save_undo_state()
    target_doc = app.documents[target_doc_index]
    copied_any = False
    copied_names = []

    selected_iids = app.category_tree.selection()

    for source_cat_index in source_cat_indices:
        source_category = app.documents[source_doc_index].categories[source_cat_index]

        # Filter category labels based on selection if category is selected and some labels are selected
        cat_iid = category_iid(source_doc_index, source_cat_index)

        # Clone to avoid modifying the original data
        source_category_copy = source_category.clone()

        if cat_iid in selected_iids:
            selected_label_indices = [
                i for i in range(len(source_category.entries))
                if label_iid(source_doc_index, source_cat_index, i) in selected_iids
            ]
            if selected_label_indices:
                source_category_copy.entries = [source_category_copy.entries[i] for i in selected_label_indices]

        if merge_category(target_doc, source_category_copy, replace=True):
            copied_any = True
            copied_names.append(source_category_copy.name)

    if copied_any:
        target_doc.dirty = True
        names_str = ", ".join(f"'{name}'" for name in copied_names)
        app.status_text.set(f"Copied category(ies) {names_str} to {target_doc.path.name}")
        print(
            f"[SpriteLibEditor] Copied categories source={source_doc_index}:{source_cat_indices} target_doc={target_doc_index}",
            file=sys.stderr,
        )
    else:
        if app.undo_stack:
            app.undo_stack.pop()


def copy_labels_to_category(
    app: Any,
    source_doc_index: int,
    source_labels_refs: list[tuple[int, int]],
    target_doc_index: int,
    target_cat_index: int,
) -> bool:
    """Copy selected labels to a specific category in another document."""
    from data import merge_labels

    app._save_undo_state()
    target_doc = app.documents[target_doc_index]
    target_category = target_doc.categories[target_cat_index]

    labels_to_copy = []
    for cat_index, label_index in source_labels_refs:
        label = app.documents[source_doc_index].categories[cat_index].entries[label_index]
        labels_to_copy.append(label)

    changed = merge_labels(target_category, labels_to_copy, replace=True)
    if changed:
        target_doc.dirty = True
        names_str = ", ".join(f"'{label.name}'" for name in labels_to_copy)
        app.status_text.set(f"Copied label(s) {names_str} to {target_category.name}")
        print(
            f"[SpriteLibEditor] Copied labels source_refs={source_labels_refs} target={target_doc_index}:{target_cat_index}",
            file=sys.stderr,
        )
    else:
        if app.undo_stack:
            app.undo_stack.pop()
    return changed
