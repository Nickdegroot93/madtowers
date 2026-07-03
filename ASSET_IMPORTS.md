# Asset Import Layout

Imported chapter/environment packs live here:

`Assets/Art/ChapterPacks/<Pack Name>/`

Current packs:

- `Assets/Art/ChapterPacks/Jungle Landscape`
- `Assets/Art/ChapterPacks/Desert Vibe`
- `Assets/Art/ChapterPacks/Japan Landscape`

Game-authored chapter presentation stays separate:

- `Assets/Art/Chapters/<ChapterId>`: menu/chapter stills and thumbnails
- `Assets/Data/Backdrops`: gameplay backdrop presets that reference selected pack sprites by GUID
- `Assets/Resources/Chapters`: chapter definitions loaded at runtime
- `Assets/Resources/Skins/<Theme>`: runtime block/ground/laser skins loaded by `Resources.Load`

## Why Imported Packs Can Look Wrong

`.unitypackage` files store the original project paths chosen by the pack author. If a package
contains `Assets/Jungle Landscape/...`, Unity imports those files there. The import dialog can
toggle files on/off, but it does not remap them into a cleaner destination folder.

That is why Desert files landed under `Assets/Jungle Landscape`, and why the Japan package preview
also wants to write some files there. This is a package-path issue, not a runtime chapter system
requirement.

## Importing A New Chapter Pack

1. Import the package.
2. If Unity creates top-level folders such as `Assets/Jungle Landscape` or `Assets/Japan landscape`,
   immediately move the new package files into `Assets/Art/ChapterPacks/<Pack Name>/`.
3. Move assets together with their `.meta` files so GUID references stay intact.
4. Keep vendor demo scenes, pipelines, presets, and helper scripts inside that pack folder unless
   the game explicitly adopts them.
5. Curate only the sprites/backgrounds actually used by gameplay into `Assets/Data/Backdrops` or
   chapter/menu assets. Do not put raw vendor packs under `Assets/Resources`.

For portrait gameplay, do not blindly add every vendor sprite to the backdrop preset. Many
horizontal landscape packs include road strips, shadow overlays, and opaque ground plates that only
work when they are anchored as the pack's bottom stack and tiled beyond the viewport. Use
sky/cloud/mountain layers for the climb, then anchor architecture, road/base, and foreground detail
near the floor so it sinks away like the Jungle and Desert chapters.

For duplicate shared files shown in an import dialog, leave already-imported files unchecked unless
you intentionally want Unity to overwrite their contents while keeping their GUIDs.
