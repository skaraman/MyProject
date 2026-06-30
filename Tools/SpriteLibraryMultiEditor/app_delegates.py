from __future__ import annotations

import functools
import sys
from typing import Any

import dialogs
import document_operations
import drag_drop


def _on_context_menu(self: Any, event: Any) -> None:
    from tree_operations import parse_iid_ref
    from ui_builder import create_context_menu

    item = self.category_tree.identify_row(event.y)
    if not item:
        menu = create_context_menu(self.root)
        _add_global_menu_items(self, menu)
        menu.post(event.x_root, event.y_root)
        return

    current_selection = self.category_tree.selection()
    if item not in current_selection:
        self.category_tree.selection_set(item)
        current_selection = (item,)

    menu = create_context_menu(self.root)
    if len(current_selection) > 1:
        _add_multi_selection_menu_items(self, menu)
        menu.post(event.x_root, event.y_root)
        return

    ref = parse_iid_ref(current_selection[0])
    if not self._is_valid_ref(ref):
        return

    if ref.kind == "label":
        _add_label_menu_items(self, menu, ref)
    elif ref.kind == "category":
        _add_category_menu_items(self, menu, ref, item)
    elif ref.kind == "document":
        _add_document_menu_items(self, menu, ref.doc_index)

    menu.add_separator()
    _add_global_menu_items(self, menu)
    menu.post(event.x_root, event.y_root)


def _add_global_menu_items(self: Any, menu: Any) -> None:
    menu.add_command(label="Open Library", command=self.open_library)
    menu.add_separator()
    menu.add_command(label="Unload All Libraries", command=self.unload_all)
    menu.add_command(
        label="Delete Labels Without Prefix In All Libraries...",
        command=self.delete_labels_without_prefix_in_all_libraries,
    )
    menu.add_separator()
    menu.add_command(label="Expand All", command=self._expand_all)
    menu.add_command(label="Collapse All", command=self._collapse_all)


def _add_multi_selection_menu_items(self: Any, menu: Any) -> None:
    if self._has_multiple_selected_categories():
        menu.add_command(label="Add Suffix", command=self._add_suffix_to_selected_categories)
    menu.add_command(label="Delete Selected Items", command=self._on_delete_shortcut)
    menu.add_separator()
    _add_global_menu_items(self, menu)


def _add_label_menu_items(self: Any, menu: Any, ref: Any) -> None:
    if ref.cat_index is None or ref.label_index is None:
        return

    menu.add_command(
        label="Edit Label Name",
        command=functools.partial(self._edit_label_name, ref.doc_index, ref.cat_index, ref.label_index),
    )
    menu.add_command(
        label="Edit Sprite Reference",
        command=functools.partial(self._edit_sprite_ref, ref.doc_index, ref.cat_index, ref.label_index),
    )
    menu.add_separator()
    menu.add_command(
        label="Delete Label",
        command=functools.partial(self._delete_label, ref.doc_index, ref.cat_index, ref.label_index),
    )
    menu.add_separator()
    menu.add_command(
        label="Move Up",
        command=functools.partial(self._move_label_up, ref.doc_index, ref.cat_index, ref.label_index),
        state="normal" if ref.label_index > 0 else "disabled",
    )

    cat = self.documents[ref.doc_index].categories[ref.cat_index]
    menu.add_command(
        label="Move Down",
        command=functools.partial(self._move_label_down, ref.doc_index, ref.cat_index, ref.label_index),
        state="normal" if ref.label_index < len(cat.entries) - 1 else "disabled",
    )


def _add_category_menu_items(self: Any, menu: Any, ref: Any, item: str) -> None:
    if ref.cat_index is None:
        return

    menu.add_command(
        label="Rename Category",
        command=functools.partial(self._edit_category_name, ref.doc_index, ref.cat_index),
    )
    menu.add_command(
        label="Add Category",
        command=functools.partial(self._add_category, ref.doc_index),
    )
    menu.add_command(
        label="Move and Rename",
        command=functools.partial(self._move_and_rename_into_category, ref.doc_index, ref.cat_index),
    )
    menu.add_separator()
    menu.add_command(
        label="Delete Category",
        command=functools.partial(self._delete_category, ref.doc_index, ref.cat_index),
    )
    menu.add_command(
        label="Delete Labels Without Prefix...",
        command=functools.partial(self._delete_labels_without_prefix, ref.doc_index, ref.cat_index),
    )
    menu.add_separator()
    _add_category_move_items(self, menu, ref)
    menu.add_separator()
    _add_category_open_item(self, menu, ref, item)


def _add_category_move_items(self: Any, menu: Any, ref: Any) -> None:
    doc = self.documents[ref.doc_index]
    menu.add_command(
        label="Move Up",
        command=functools.partial(self._move_category_up, ref.doc_index, ref.cat_index),
        state="normal" if ref.cat_index > 0 else "disabled",
    )
    menu.add_command(
        label="Move Down",
        command=functools.partial(self._move_category_down, ref.doc_index, ref.cat_index),
        state="normal" if ref.cat_index < len(doc.categories) - 1 else "disabled",
    )


def _add_category_open_item(self: Any, menu: Any, ref: Any, item: str) -> None:
    is_expanded = self.category_tree.item(item)["open"]
    if is_expanded:
        menu.add_command(
            label="Collapse Category",
            command=functools.partial(self._collapse_category, ref.doc_index, ref.cat_index),
        )
        return

    menu.add_command(
        label="Expand Category",
        command=functools.partial(self._expand_category, ref.doc_index, ref.cat_index),
    )


def _add_document_menu_items(self: Any, menu: Any, doc_index: int) -> None:
    menu.add_command(label="Add Category", command=functools.partial(self._add_category, doc_index))
    menu.add_separator()
    menu.add_command(label="Unload Library", command=functools.partial(self._unload_document, doc_index))
    menu.add_command(
        label="Move and Suffix",
        command=functools.partial(self._move_and_suffix_into_document, doc_index),
    )
    menu.add_separator()
    menu.add_command(
        label="Delete Labels Without Prefix...",
        command=functools.partial(self._delete_labels_without_prefix_in_document, doc_index),
    )
    menu.add_separator()
    _add_document_move_items(self, menu, doc_index)


def _add_document_move_items(self: Any, menu: Any, doc_index: int) -> None:
    menu.add_command(
        label="Move Up",
        command=functools.partial(self._move_document_up, doc_index),
        state="normal" if doc_index > 0 else "disabled",
    )
    menu.add_command(
        label="Move Down",
        command=functools.partial(self._move_document_down, doc_index),
        state="normal" if doc_index < len(self.documents) - 1 else "disabled",
    )


def _on_doc_listbox_context_menu(self: Any, event: Any) -> None:
    if not self.documents:
        return

    index = self.doc_listbox.nearest(event.y)
    if index < 0 or index >= len(self.documents):
        return

    self.doc_listbox.selection_clear(0, "end")
    self.doc_listbox.selection_set(index)
    self.selected_document_index = index
    self._update_doc_info(self.documents[index])

    from ui_builder import create_context_menu

    menu = create_context_menu(self.root)
    _add_document_menu_items(self, menu, index)
    menu.add_separator()
    menu.add_command(label="Open Library", command=self.open_library)
    menu.add_separator()
    menu.add_command(label="Unload All Libraries", command=self.unload_all)
    menu.add_separator()
    menu.add_command(
        label="Delete Labels Without Prefix In All Libraries...",
        command=self.delete_labels_without_prefix_in_all_libraries,
    )
    menu.post(event.x_root, event.y_root)


def _on_tree_button_press(self: Any, event: Any) -> str | None:
    return drag_drop.on_tree_button_press(self, event)


def _on_tree_drag_start(self: Any, event: Any) -> None:
    drag_drop.on_tree_drag_start(self, event)


def _on_tree_drag_drop(self: Any, event: Any) -> None:
    drag_drop.on_tree_drag_drop(self, event)


def _is_tree_drag(self: Any, event: Any) -> bool:
    return drag_drop.is_tree_drag(self, event)


def _clear_tree_drag(self: Any) -> None:
    drag_drop.clear_tree_drag(self)


def _get_tree_drag_selection(self: Any, source_iid: str) -> tuple[str, ...]:
    return drag_drop.get_tree_drag_selection(self, source_iid)


def _move_document_to_index(self: Any, source_doc_index: int, target_doc_index: int) -> None:
    drag_drop.move_document_to_index(self, source_doc_index, target_doc_index)


def _move_category_to_index(self: Any, doc_index: int, source_cat_index: int, target_cat_index: int) -> None:
    drag_drop.move_category_to_index(self, doc_index, source_cat_index, target_cat_index)


def _move_label_to_index(self: Any, doc_index: int, cat_index: int, source_label_index: int, target_label_index: int) -> None:
    drag_drop.move_label_to_index(self, doc_index, cat_index, source_label_index, target_label_index)


def _move_label_up(self: Any, doc_index: int, cat_index: int, label_index: int) -> None:
    drag_drop.move_label_up(self, doc_index, cat_index, label_index)


def _move_label_down(self: Any, doc_index: int, cat_index: int, label_index: int) -> None:
    drag_drop.move_label_down(self, doc_index, cat_index, label_index)


def _move_category_up(self: Any, doc_index: int, cat_index: int) -> None:
    drag_drop.move_category_up(self, doc_index, cat_index)


def _move_category_down(self: Any, doc_index: int, cat_index: int) -> None:
    drag_drop.move_category_down(self, doc_index, cat_index)


def _move_document_up(self: Any, doc_index: int) -> None:
    drag_drop.move_document_up(self, doc_index)


def _move_document_down(self: Any, doc_index: int) -> None:
    drag_drop.move_document_down(self, doc_index)


def _on_tree_move_up_shortcut(self: Any, event: Any = None) -> str:
    return drag_drop.on_tree_move_up_shortcut(self, event)


def _on_tree_move_down_shortcut(self: Any, event: Any = None) -> str:
    return drag_drop.on_tree_move_down_shortcut(self, event)


def _on_doc_move_up_shortcut(self: Any, event: Any = None) -> str:
    return drag_drop.on_doc_move_up_shortcut(self, event)


def _on_doc_move_down_shortcut(self: Any, event: Any = None) -> str:
    return drag_drop.on_doc_move_down_shortcut(self, event)


def _copy_categories_to_document(self: Any, source_doc_index: int, source_cat_indices: list[int], target_doc_index: int) -> None:
    drag_drop.copy_categories_to_document(self, source_doc_index, source_cat_indices, target_doc_index)


def _copy_labels_to_category(
    self: Any,
    source_doc_index: int,
    source_labels_refs: list[tuple[int, int]],
    target_doc_index: int,
    target_cat_index: int,
) -> bool:
    return drag_drop.copy_labels_to_category(
        self,
        source_doc_index,
        source_labels_refs,
        target_doc_index,
        target_cat_index,
    )


def _edit_label_name(self: Any, doc_index: int, cat_index: int, label_index: int) -> None:
    dialogs.edit_label_name(self, doc_index, cat_index, label_index)


def _rename_label(self: Any, document: Any, category: Any, label_index: int, new_name: str) -> None:
    dialogs.rename_label(document, category, label_index, new_name)


def _get_selected_label_refs(self: Any) -> list[tuple[int, int, int]]:
    return dialogs.get_selected_label_refs(self)


def _get_suffix_rename_refs(
    self: Any,
    selected_refs: list[tuple[int, int, int]],
    doc_index: int,
    cat_index: int,
    label_index: int,
    old_name: str,
    new_name: str,
) -> list[tuple[int, int, int, str, str]]:
    return dialogs.get_suffix_rename_refs(
        self,
        selected_refs,
        doc_index,
        cat_index,
        label_index,
        old_name,
        new_name,
    )


def _get_suffix_change(self: Any, old_name: str, new_name: str) -> tuple[str, str]:
    return dialogs.get_suffix_change(old_name, new_name)


def _rename_labels_with_suffix(self: Any, refs: list[tuple[int, int, int, str, str]]) -> None:
    dialogs.rename_labels_with_suffix(self, refs)


def _replace_label_suffix(self: Any, name: str, old_suffix: str, new_suffix: str) -> str:
    return dialogs.replace_label_suffix(name, old_suffix, new_suffix)


def _edit_sprite_ref(self: Any, doc_index: int, cat_index: int, label_index: int) -> None:
    dialogs.edit_sprite_ref(self, doc_index, cat_index, label_index)


def _edit_category_name(self: Any, doc_index: int, cat_index: int) -> None:
    dialogs.edit_category_name(self, doc_index, cat_index)


def _add_category(self: Any, doc_index: int) -> None:
    dialogs.add_category(self, doc_index)


def _has_multiple_selected_categories(self: Any) -> bool:
    return dialogs.has_multiple_selected_categories(self)


def _get_selected_category_refs(self: Any) -> list[tuple[int, int]]:
    return dialogs.get_selected_category_refs(self)


def _add_suffix_to_selected_categories(self: Any) -> None:
    dialogs.add_suffix_to_selected_categories(self)


def unload_all(self: Any) -> None:
    document_operations.unload_all(self)


def delete_labels_without_prefix_in_all_libraries(self: Any) -> None:
    document_operations.delete_labels_without_prefix_in_all_libraries(self)


def _unload_document(self: Any, index: int) -> bool:
    return document_operations.unload_document(self, index)


def _on_doc_listbox_delete(self: Any, event: Any = None) -> None:
    document_operations.on_doc_listbox_delete(self, event)


def _move_and_suffix_into_document(self: Any, target_doc_index: int) -> None:
    document_operations.move_and_suffix_into_document(self, target_doc_index)


def _move_and_rename_into_category(self: Any, target_doc_index: int, target_cat_index: int) -> None:
    document_operations.move_and_rename_into_category(self, target_doc_index, target_cat_index)


def _get_library_category_suffix(self: Any, document: Any) -> str:
    return document_operations.get_library_category_suffix(document)


def _delete_label(self: Any, doc_index: int, cat_index: int, label_index: int) -> None:
    document_operations.delete_label(self, doc_index, cat_index, label_index)


def _delete_category(self: Any, doc_index: int, cat_index: int) -> None:
    document_operations.delete_category(self, doc_index, cat_index)


def _delete_labels_without_prefix(self: Any, doc_index: int, cat_index: int) -> None:
    document_operations.delete_labels_without_prefix(self, doc_index, cat_index)


def _delete_labels_without_prefix_in_document(self: Any, doc_index: int) -> None:
    document_operations.delete_labels_without_prefix_in_document(self, doc_index)


def _on_delete_shortcut(self: Any, event: Any = None) -> None:
    document_operations.on_delete_shortcut(self, event)


def _on_rename_shortcut(self: Any, event: Any = None) -> None:
    selection = self.category_tree.selection()
    if not selection:
        return

    from tree_operations import parse_iid_ref

    ref = parse_iid_ref(selection[0])
    if not self._is_valid_ref(ref):
        return

    if ref.kind == "category" and ref.cat_index is not None:
        self._edit_category_name(ref.doc_index, ref.cat_index)
    elif ref.kind == "label" and ref.cat_index is not None and ref.label_index is not None:
        self._edit_label_name(ref.doc_index, ref.cat_index, ref.label_index)


def _expand_category(self: Any, doc_index: int, cat_index: int) -> None:
    from tree_operations import category_iid

    iid = category_iid(doc_index, cat_index)
    if self.category_tree.exists(iid):
        self.category_tree.item(iid, open=True)


def _collapse_category(self: Any, doc_index: int, cat_index: int) -> None:
    from tree_operations import category_iid

    iid = category_iid(doc_index, cat_index)
    if self.category_tree.exists(iid):
        self.category_tree.item(iid, open=False)


def _expand_all(self: Any) -> None:
    for doc_item in self.category_tree.get_children(""):
        self.category_tree.item(doc_item, open=True)
        for cat_item in self.category_tree.get_children(doc_item):
            self.category_tree.item(cat_item, open=True)


def _collapse_all(self: Any) -> None:
    for doc_item in self.category_tree.get_children(""):
        self.category_tree.item(doc_item, open=False)
        for cat_item in self.category_tree.get_children(doc_item):
            self.category_tree.item(cat_item, open=False)


def refresh_tree(self: Any) -> None:
    from tree_operations import build_tree

    build_tree(self.category_tree, self.documents)


def _save_undo_state(self: Any) -> None:
    import copy

    if len(self.undo_stack) >= 100:
        self.undo_stack.pop(0)
    self.undo_stack.append(copy.deepcopy(self.documents))
    self.redo_stack.clear()
    print(f"[SpriteLibEditor] Saved undo state. Stack size: {len(self.undo_stack)}", file=sys.stderr)


def _on_undo(self: Any, event: Any = None) -> None:
    if not self.undo_stack:
        return

    import copy

    self.redo_stack.append(copy.deepcopy(self.documents))
    if len(self.redo_stack) > 100:
        self.redo_stack.pop(0)
    self.documents = self.undo_stack.pop()
    self._refresh_doc_list()
    self.status_text.set("Undo")
    print(f"[SpriteLibEditor] Restored state from undo stack. Remaining: {len(self.undo_stack)}", file=sys.stderr)


def _on_redo(self: Any, event: Any = None) -> None:
    if not self.redo_stack:
        return

    import copy

    self.undo_stack.append(copy.deepcopy(self.documents))
    if len(self.undo_stack) > 100:
        self.undo_stack.pop(0)
    self.documents = self.redo_stack.pop()
    self._refresh_doc_list()
    self.status_text.set("Redo")
    print(f"[SpriteLibEditor] Restored state from redo stack. Remaining: {len(self.redo_stack)}", file=sys.stderr)


def install_app_delegates(cls: type) -> None:
    method_names = (
        "_on_context_menu",
        "_on_doc_listbox_context_menu",
        "_on_tree_button_press",
        "_on_tree_drag_start",
        "_on_tree_drag_drop",
        "_is_tree_drag",
        "_clear_tree_drag",
        "_get_tree_drag_selection",
        "_move_document_to_index",
        "_move_category_to_index",
        "_move_label_to_index",
        "_move_label_up",
        "_move_label_down",
        "_move_category_up",
        "_move_category_down",
        "_move_document_up",
        "_move_document_down",
        "_on_tree_move_up_shortcut",
        "_on_tree_move_down_shortcut",
        "_on_doc_move_up_shortcut",
        "_on_doc_move_down_shortcut",
        "_copy_categories_to_document",
        "_copy_labels_to_category",
        "_edit_label_name",
        "_rename_label",
        "_get_selected_label_refs",
        "_get_suffix_rename_refs",
        "_get_suffix_change",
        "_rename_labels_with_suffix",
        "_replace_label_suffix",
        "_edit_sprite_ref",
        "_edit_category_name",
        "_add_category",
        "_has_multiple_selected_categories",
        "_get_selected_category_refs",
        "_add_suffix_to_selected_categories",
        "unload_all",
        "delete_labels_without_prefix_in_all_libraries",
        "_unload_document",
        "_on_doc_listbox_delete",
        "_move_and_suffix_into_document",
        "_move_and_rename_into_category",
        "_get_library_category_suffix",
        "_delete_label",
        "_delete_category",
        "_delete_labels_without_prefix",
        "_delete_labels_without_prefix_in_document",
        "_on_delete_shortcut",
        "_on_rename_shortcut",
        "_expand_category",
        "_collapse_category",
        "_expand_all",
        "_collapse_all",
        "refresh_tree",
        "_save_undo_state",
        "_on_undo",
        "_on_redo",
    )

    for name in method_names:
        setattr(cls, name, globals()[name])
