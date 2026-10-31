# Memory Album - Photos, Posed Pictures and Milestone Memories

Take out a camera, snap date-stamped photos, ask villagers to pose with you, and let the album capture the big moments of your farm life by itself. Then flip through the years: your farm, your family, your pets and your friendships, and how they changed.

## Features
- **A camera.** Press `O` to open the viewfinder. The HUD hides so you can frame the shot; click to take a photo. Photos never include the HUD or menus, so cutscenes and festivals make great pictures too.
- **Date stamps.** Every photo gets the in-game date printed in the corner, like an old film camera (you can turn this off).
- **Pose with villagers.** Stand next to someone and right-click to ask them to pose. Friends say yes, best friends get excited, your spouse always says yes, and people you barely know politely decline. Children and pets pose too.
- **Milestones, captured for you.** The album photographs your wedding (at the kiss), your engagement, each new child, the Community Center ceremony, house upgrades, new pets, new best friends, the greenhouse, each new year, perfection, and your very first day.
- **An in-game album.** Press `L` to flip through polaroids. Filter by milestones, favorites, posed photos, year, or the person in the picture. Hover a photo for its date, time, place, weather, festival and who's in it.
- **Captions and favorites.** Open any photo to write a caption, mark it as a favorite, or delete it.
- **Share-ready polaroids.** Click **Save as polaroid** to export a framed image with your caption and date written underneath. They're ready for Reddit, Discord or TikTok.
- **"On this day."** Some mornings you'll be reminded of a photo you took on the same day in an earlier year.
- **Existing saves welcome.** Milestones you already reached before installing are skipped quietly, so you don't get a flood of photos.

## Install
1. Install [SMAPI](https://smapi.io) 4.0 or later.
2. Unzip this mod into your `Mods` folder.
3. Optional: install [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) to change settings in-game.

## Controls
| Action | Mouse and keyboard | Controller |
|---|---|---|
| Take out or put away the camera | `O` | set a button in config |
| Take a photo | left-click | `X` |
| Ask a nearby villager to pose | right-click | `A` |
| Put the camera away | `Esc` or `O` | `B` |
| Open the album | `L` | set a button in config |
| Album: change page | arrows, scroll wheel | shoulder buttons |
| Album: change filter | filter arrows | triggers |

## Where are my photos?
In the game's screenshot folder, under `MemoryAlbum/<farm name>_<id>/`. On Windows that's `%AppData%\StardewValley\Screenshots\MemoryAlbum\`. Polaroid exports go in the `Polaroids` subfolder. The album has an **Open folder** button too.

Each farm has its own album. Photos aren't stored in your save, so they don't bloat save backups and you can browse them outside the game. `album.json` in the same folder holds the captions and details; don't delete it if you want to keep them.

## Config
Edit `config.json` or use Generic Mod Config Menu.

| Option | Default | Description |
|---|---|---|
| `CameraKey` | `O` | Takes out or puts away the camera. |
| `AlbumKey` | `L` | Opens the album. |
| `StampDate` | `true` | Print the in-game date on photos. |
| `ShutterEffects` | `true` | Play the shutter sound and flash. |
| `HideHudInPhotoMode` | `true` | Hide the HUD while the camera is out. |
| `MinHeartsToPose` | `2` | Hearts a villager needs before they'll pose. Spouses, children and pets always pose. |
| `AutoCaptureMilestones` | `true` | Photograph milestones automatically. |
| `ShowHudOnMemory` | `true` | Show a message when a milestone photo is taken. |
| `OnThisDayReminders` | `true` | Remind you of photos from the same day in earlier years. |

## Milestones
| Milestone | When it's photographed |
|---|---|
| Day one | Your first morning on a new farm |
| A new year | The first time you step onto your farm in a new year |
| Engagement | When your partner is next on screen after saying yes |
| Wedding | During the ceremony, at the kiss |
| New child | When the child is next on screen |
| New pet | When the pet is next on screen |
| House upgrade | The next time you're home |
| Best friends (8 hearts) | When they're next on screen |
| Greenhouse | The first time you visit the repaired greenhouse |
| Community Center | During the restoration ceremony |
| Joja warehouse | During the completion ceremony |
| Perfection | The morning after you reach it |

## Multiplayer and split-screen
Each player has their own album and milestones. Photos are saved on the computer of the player who took them.

## Console commands
- `album_snap [caption]`: take a photo now
- `album_list`: list your photos
- `album_folder`: open the album folder
- `album_milestones`: list photographed and pending milestones
- `album_milestone <key>`: queue a milestone photo, e.g. `album_milestone BestFriends:Abigail`
- `album_reset_milestones`: forget which milestones were photographed

## Compatibility
- Stardew Valley 1.6+, SMAPI 4.0+. Windows, Mac and Linux.
- Doesn't change game data or use Harmony, so it should work with most mods, including Stardew Valley Expanded villagers (they can pose and appear in photo tags).
- Mods that draw their own HUD overlays may show up in photos.
- Safe to remove: the only things in your save are a few milestone flags.

## Translations
All text is in `i18n/default.json`. Translations are welcome!

## Source
MIT licensed.
