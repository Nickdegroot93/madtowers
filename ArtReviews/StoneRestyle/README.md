The ordinary bricks now share carved stone relief across all 17 existing theme presets. Chapter palettes are unchanged. The generator adds angular weathering, shallow pitting, worn bevel facets and top-lit bevels along the internal cell joints. Individual cells no longer receive a separate brightness roll.

[Gameplay before/after](gameplay-comparison.png) · [Enlarged detail](stone-detail.png) · [Landing guide and next preview](ghost-comparison.png) · [Vault Vine demo](vault-vine-demo.png) · [All chapter colours](chapters.png)

The gameplay images are Unity `ScreenCapture.CaptureScreenshot` captures at the normal orthographic size of 15. Both use the same 587 × 972 Game View, camera, HUD and frozen layout of actual piece prefabs. The run started through the editor-only Custom Game screen with `GameMode_JungleClassic`; a temporary cloned chapter associated that custom level with Jungle presentation. No level or chapter assets were saved. The Vine capture uses the Vault's actual `BlockDemoStage.Open` scenario in its documented temporary RawImage review overlay, run for ten seconds. Play mode was stopped and keyboard/mouse input restored afterward.

Changes are limited to `Tools/generate_piece_sprites.py`, 153 existing ordinary-piece PNGs, and the four existing Vault posters that expose ordinary chapter art (Normal, Vine, Ice and Locked). Fixed-look variant posters, Pyramid art, shaders, ground, backdrops, HUD, abilities, runtime scripts, prefabs and all existing metadata retain their original bytes. The three unrelated edits already present when work began were preserved: `Assets/csc.rsp`, `Screen buff.prefab`, and the LiberationSans fallback font asset. Nothing was committed.

Physics and geometry verification:

- `BlockController.Setup.cs` places the sprite on a collider-free `PieceSkin` child. Collider forgiveness starts with the prefab's cell `BoxCollider2D.size` and `gridSpacing`; it does not use sprite bounds. `BlockCellGeometry` reads the colliders' centers and bounds.
- Before/after runtime records match exactly for I, O, T, S, Z, J, L, Domino and Pip: each cell has a **0.82 × 0.88** collider core and **0.06** edge radius at spawn orientation. Expanded footprints remain **0.94 × 1.00**. Collider counts, offsets, cell positions, sprite pivots and bounds also match.
- All **153** sprites retain **256 PPU**, centered pivots, full-rectangle meshes, readable textures and disabled mipmaps in Unity.
- Every regenerated PNG has the **exact original alpha channel and dimensions**. The 22 px silhouette radius, 17 px outline, 26 px bevel band, cell layouts and bleed constants remain unchanged.
- All chapter palette presets match the original generator. A second render of every sprite produced identical PNG bytes.
- `ChapterSkins.LoadPiece` returned the actual Classic sprite for every one of the nine shapes when `Folder` was temporarily set to a nonexistent theme, then the original folder was restored.
- Hash comparison of 2,333 baseline files found only the 157 intended PNG changes. `git diff --check` passed.

Evidence: [before collider records](colliders-before.json), [after collider records](colliders-after.json), [image/pipeline verification](verification.json), [Unity importer verification](importers.json), [Classic fallback results](fallback.json).

The console-clean requirement remains qualified. No gameplay C# exceptions appeared, but Unity's console is not empty: the existing Google Play packages have orphaned LICENSE/README metas in immutable folders, the MCP connection had reported a bridge error, and the renderer reported memoryless depth load/store errors. Asset imports repeated the Google Play errors. The final capture contains 45 error/exception entries from those categories; the Vault/check phase added eight repeated package errors and no new error category. These unrelated systems were left untouched. [Full console evidence](console-errors.json).
