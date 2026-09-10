# Vault gallery review

Copy `VaultReview.cs.txt` temporarily to `Assets/SourceFiles/Scripts/Editor/VaultReview.cs`.
Let Unity compile, stop Play Mode, then call `VaultReview.Run()` through the local Unity bridge.
Remove the temporary script and its `.meta` after the review.

Renders both collection tabs at 780×1688, 1080×1920 and 1536×2048, both empty states,
and both detail sheets. Checks discovered-only brick visibility, locked ability input,
baked poster availability, title fit and icon/poster bounds. Screenshots go to ignored `review/`.
The fixture temporarily substitutes in-memory discovery data, disables cloud calls, and restores
state afterward. Detail entries are pre-inspected in the fixture so opening them never saves.

Phone notch/home-indicator clearance follows the existing menu safe-area container and
ModalSafeFrame. Confirm touch scrolling, tab switching, demo playback and closing in Play Mode.
