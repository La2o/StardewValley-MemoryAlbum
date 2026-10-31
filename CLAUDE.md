# Memory Album: notes for Claude Code

A Stardew Valley 1.6 SMAPI mod (C#, net6.0, ModBuildConfig 4.4.0), by ygsnaffy. Sibling of `../StardewValley-StardropPieces` (Scattered Stardrops) and follows the same conventions: `Framework/` for logic, `Integrations/` for mod APIs, all player-facing text in `i18n/default.json`, GMCM optional.

## Concept
A camera and an in-game photo album. Date-stamped photos, posed photos with villagers, automatic milestone photos (wedding, children, Community Center, etc.), an album browser with filters, captions, favorites and polaroid export. See README.md for the player-facing feature list.

## Layout
- `ModEntry.cs`: event wiring and key handling (camera key, album key, photo mode input).
- `Framework/PhotoCamera.cs`: renders the view into a PNG and exports polaroids.
- `Framework/PhotoMode.cs`: the viewfinder overlay, snapping, and the pose flow.
- `Framework/MilestoneWatcher.cs`: milestone detection and auto-capture, plus `MilestoneKey`.
- `Framework/AlbumStore.cs`: album folder, `album.json` index, texture cache.
- `Framework/AlbumMenu.cs` / `PhotoViewerMenu.cs`: the UI. `AlbumDrawing.cs` has shared sprite rects and helpers.

## Implementation notes (verified against the 1.6.15 decompile)
- **Capture** mirrors `Game1.takeMapScreenshot`: set `Game1.game1.takingMapScreenshot = true` (hides HUD, menus, dialogue boxes, overlays), set `options.baseZoomLevel = 1` (when taking a screenshot, `zoomLevel` returns `baseZoomLevel`), swap `Game1._lightmap` for one from `allocateLightmap(w, h)`, and call the protected `_draw(GameTime, RenderTarget2D)` via reflection into a render target the size of `Game1.viewport`. Restore everything in `finally`. Captures run from the update loop, never from inside a draw.
- SMAPI's `SGame._draw` override raises the `Rendered` events, so our overlay checks `PhotoCamera.IsCapturing`. Other mods' overlays can end up in photos; that's a documented known issue.
- Pixels are forced to alpha 255 before `SaveAsPng`, since some blend steps leave partial alpha.
- Shutter: `Game1.flashAlpha = 1f` + `cameraNoise` sound (what the vanilla screenshot option does).
- Albums are stored in `Game1.game1.GetScreenshotFolder()/MemoryAlbum/<farmName>_<uniqueIDForThisGame>/`, not in the save. JSON uses System.Text.Json (ModBuildConfig doesn't reference Newtonsoft).
- Milestone state is per player in `modData` (`ygsnaffy.MemoryAlbum/MilestonesDone`, `/MilestonesPending`, `/Initialized`), as JSON string sets. On first load every milestone already met is recorded as done, except `DayOne` on a brand new farm.
- Event IDs: Community Center ceremony `191393`, Joja `502261`. Weddings have `Event.isWedding` (id `-2`). The 1.6 wedding script (`Data/Weddings`) has no `showKissFrame`: the kiss is `showFrame 101` + `specificTemporarySprite heart`, then `pause 2000`. The photo is taken 30 ticks after `CurrentCommand` reaches the heart command (found by index, since instant commands can start and finish in one tick), with a 25 s fallback. The CC ceremony photo is taken at the first dialogue after 6 s (Lewis's speech inside the Center).
- Photographing a wedding or engagement marks the smaller pending milestones for the same person (engagement, best friends) as done, so they don't produce near-duplicate photos.
- `RandomExtensions.ChooseFrom` lives in `StardewValley.Extensions`.

## Building and testing
- `dotnet build` deploys to `Mods/MemoryAlbum`; `dotnet build -c Release` also creates the zip under `bin/Release/net6.0/`.
- `tools/publish-nexus.ps1` uploads a release as a new version of the Nexus main file (copied from the sibling project). The first release has to be uploaded by hand; then fill in `tools/nexus.json` and the `UpdateKeys` in `manifest.json`.
- `docs/screenshots/` holds the Nexus images (1920x1080, plus the polaroid export). They were made with a throwaway SMAPI harness mod that loads a save, stages scenes by warping villagers next to the player, and composites UI shots by drawing the world (via `PhotoCamera.RenderView`) and then the menu into a render target. Don't use `GraphicsDevice.GetBackBufferData` inside a SMAPI `Rendered` handler: it returns a stale, vertically flipped frame with no UI.
- Tested in-game (1.6.15, SMAPI 4.5.2, single player, 1920x1080 at 100% zoom): photo capture and date stamp, viewfinder, posing flow, album, viewer, captions, polaroid export, and the milestone photos for best friends, the wedding (at the kiss), a new child, and the Community Center ceremony (all staged in memory by a harness: married or engaged state, a `Child` added to the farmhouse, the real `191393` event). Not yet tested: Joja ceremony, house upgrades, pets, greenhouse, new year, perfection, other zoom levels, multiplayer, split-screen.
- The game font has no `·` or `★` glyphs; keep i18n text to plain ASCII punctuation.
- Decompile with `DOTNET_ROLL_FORWARD=Major ilspycmd` (the installed ilspycmd targets .NET 6, which isn't installed).
