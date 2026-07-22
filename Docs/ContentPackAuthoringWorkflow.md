# Content Pack Authoring Workflow

1. Create or update images under `Assets/Sprites`.
2. If the image needs trimmed offsets, run the whitespace removal or trim/offset authoring tool before packing.
3. If the image belongs to a Sprite Library, update the `.spriteSheetLib` category and label (`.spriteLib` remains supported during migration).
4. If categories or labels need to move between Sprite Libraries, open `Tools\SpriteLibraryMultiEditor.py`.
5. If the image is a direct sliced `.png`, confirm the Unity sprite slice label is stable.
6. Open `Tools\ContentPackIterationUI.py`.
7. Create or edit the target pack.
8. Add each source asset with its own target folder.
9. Save the pack manifest.
10. Hand off to `Docs/ContentPackIterationPlan.md` to run Addressables packing.

## Whitespace And Offset Tools

Use whitespace removal when transparent padding is authoring noise rather than intentional layout.

Use trim/offset export when runtime placement must preserve the visual origin after trimming:

- `Tools > Authoring > Trim Atlas + Export Offsets`

Use grouped gear atlas authoring when changing Esperanza gear source atlases:

- `Tools > Authoring > Group Atlases`
- add the exact source atlas PNG assets that belong in one batch
- set the target output folder
- set the grouped output base name
- analyze the selection, then export

Rules:

- runtime `atlas.json` is optional
- runtime `atlas.json` contains only `SpriteWithNormals` offset data
- do not regenerate packed-rect slice-definition metadata into runtime `atlas.json`
- zero-offset grouped or trimmed atlas exports should not emit runtime `atlas.json`

## Pack Source Examples

Sprite Library source:

- source type: `Sprite Library`
- asset path: `Assets/Sprites/SpriteLibraries/UI/Fonts.spriteSheetLib`
- category: `Plate`
- label: `A`
- target folder: `Core/Sprites/SpriteLibraries/UI`

Direct Sprite Slice source:

- source type: `Sprite Slice`
- asset path: `Assets/Sprites/Characters/Enemies/Imp/Run/atlas.png`
- label: `run_0`
- target folder: `Slices/Slice_DomeCity_Imp_Base/Sprites/Characters/Enemies/Imp/Run`

## Sprite Library Multi Editor

Run:

```powershell
python .\Tools\SpriteLibraryMultiEditor.py Assets\Sprites\SpriteLibraries
```

Use this when rebuilding form libraries or moving labels between existing `.spriteSheetLib` or legacy `.spriteLib` files:

- open multiple `.spriteSheetLib`/`.spriteLib` files or folders at once
- drag a category onto a library to copy or merge that category
- drag a category onto another category to copy or merge labels
- drag a label onto a category to copy that label and sprite reference
- drag a label onto a library to copy it into a matching category, creating the category if needed
- enable `Move` before dropping when the source category or label should be removed after copy

## Ownership Guidance

Core source images are shared runtime content:

- global UI that is not tied to a form
- fonts
- main menu and select menu art
- shared player bootstrap art that is not tied to a form

Form source images are form-specific combat content:

- form UI and form menu/icon art
- form item and gear icon art
- form-specific Esperanza movement and expression payloads
- form-specific character attack animation payloads
- form effects
- form projectile visuals
- form-specific prefabs and materials

Gear source images are equipped visual payload only:

- one gear pack per `Gear_<Form>_<GearCode>_<Leaf>`
- grouped gear atlas data comes from Unity importer data and offset metadata

Slice source images are one stageable gameplay unit:

- location art
- slice-local enemy art
- slice-local encounter art
- slice-local dialog or portrait art

Episode packs do not own image source payloads. They compose slices.

## Handoff Checklist

Before packing:

- source image exists under `Assets/Sprites`
- whitespace cleanup or offset export has already run if needed
- Sprite Library category and label are correct, or direct `.png` slice label is correct
- `Tools\ContentPackIterationUI.py` has a saved pack manifest
- every source row has a target folder

Next step:

- run the Python Smart packing workflow in `Docs/ContentPackIterationPlan.md`
