# Daralet

Daralet is the official [Decal](https://www.decaldev.com/) plugin for players on the Daralet server. It opens a window in
the game with:

- **World**: a map of Dereth with Daralet's towns, dungeons, lifestones, portals and more. It shows where you are, where
  your fellowship is (unless they're in a dungeon), and an arrow window that points you anywhere on the map, or follows a
  fellow. Search for a place by name, or right-click the map.
- **Fellowship**: your fellowship's health, stamina and mana, with a small window that stays on screen while you play.
  Click a name to select that member.
- **Target**: what you're fighting: its health, stamina and mana, every buff and debuff on it, and its threat table (who
  it wants to attack). Has a window that stays on screen too.
- **Loot**: a looter with loot profiles (sets of rules) you write yourself, using the same filters as the market search. Each character picks
  a loot profile, and you can swap them at any time. When you open a corpse or a chest, it can loot automatically, ask first, or just point
  out what matches. It can also salvage what you tell it to. A rule can ask for how well a stat rolled rather than the stat
  itself, and can mark what it finds with a coloured frame and a symbol. Loot window shows corpses and chests in the
  plugin's window instead of the game's.
- **Inventory**: the packs you carry, anywhere, and your bank while it's open, as a list or a grid of icons. Move items
  between packs, and deposit, withdraw, sort and search without typing `/bank` commands. Bank window opens your bank in
  the plugin's own window instead of the game's. Loot marks shows which of your items your loot rules would mark.
- **Market**: your listings, sales and expired items, with buttons for the Market Broker (list, cancel, claim), plus a
  search of the whole market like the website's.
- **Arena**: queue, challenge, leave and watch duels, with your ratings and the latest results.
- **Leaderboards**: the website's leaderboards: level, arena, capstones, deaths and more.

Type `/dap` to open and close the window. Hover over almost anything for a tooltip saying what it does.

## What you need

- **Decal 2.9.8.3.** Older versions, including "Decal 3.0" (which reports itself as 2.9.7.5), can't run this plugin.
  The Decal agent's title bar shows which version you have.
- **[UtilityBelt](https://utilitybelt.gitlab.io/) and
  [UtilityBelt.Service 2.2.12](https://gitlab.com/utilitybelt/utilitybelt.service/-/releases/v2.2.12).** Daralet draws
  its window with UtilityBelt.Service, which is a **separate install** from UtilityBelt itself. You need both installed,
  but you don't have to use UtilityBelt's own features.
- **A Daralet account.** The plugin only works on the Daralet server.

## Installing

1. **Update Decal if you need to.** Open the Decal agent (the icon in your system tray) and look at the version in its
   title bar. If it isn't 2.9.8.x, install [Decal 2.9.8.3](https://www.decaldev.com/) over it. Install the Visual C++ 2017
   (x86) runtime it links to as well. Your other plugins keep working.
2. **Install UtilityBelt** from [utilitybelt.gitlab.io](https://utilitybelt.gitlab.io/) if you don't have it.
3. **Close the game and the Decal agent** (right-click its tray icon, **Exit**).
4. **Download `Daralet-Setup-x.y.z.exe`** from the
   [latest release](https://github.com/daralet-ac/daralet-player-plugin/releases/latest) (under **Assets**) and run it.
   It checks your Decal version, installs UtilityBelt.Service if you don't have it (tick the box it shows), puts
   `Daralet.dll` in `C:\Games\Decal Plugins\Daralet\` and adds it to Decal for you, ticked.
5. **Start the game** through Decal and log in. Type `/dap` to open the window.

Afterwards the Decal agent should show **Daralet** under **Plugins** and **UtilityBelt.Service** under **Services**, both
ticked. (UtilityBelt itself shows under **Network Filters**.)

### Installing by hand

If you'd rather not run the installer:

1. Do steps 1 and 2 above.
2. **Install UtilityBelt.Service**, even if you already have UtilityBelt: UtilityBelt's installer doesn't include it.
   Close the game and the Decal agent, then run
   [UtilityBelt.Service-Installer-2.2.12.exe](https://gitlab.com/utilitybelt/utilitybelt.service/-/releases/v2.2.12/downloads/packages/UtilityBelt.Service-Installer-2.2.12.exe).
3. **Download `Daralet.dll`** from the
   [latest release](https://github.com/daralet-ac/daralet-player-plugin/releases/latest) (under **Assets**).
4. **Put it somewhere it can stay**, for example `C:\Games\Decal Plugins\Daralet\`. Decal loads the file from wherever you
   put it, so don't leave it in your Downloads folder.
5. **Add it to Decal.** In the Decal agent, click **Add**, then **Browse**, and pick `Daralet.dll`.
6. **Make sure Daralet is ticked** under **Plugins** in the Decal agent, then start the game.

## Updating

Close the game and the Decal agent, and run the new `Daralet-Setup-x.y.z.exe` from the
[latest release](https://github.com/daralet-ac/daralet-player-plugin/releases/latest). It replaces the old version
wherever it is, including a `Daralet.dll` you added to Decal by hand. Your settings are kept.

By hand instead: download the new `Daralet.dll`, close the game, replace the old `Daralet.dll` with the new one (same
name, same folder), then start the game again. You don't need to add it to Decal again.

**Coming from v0.1.0?** That version's file was called `DaraletPlayer.dll`. The installer takes care of it. By hand: put
`Daralet.dll` in the same folder, then in the Decal agent click **Add**, **Browse**, and pick `Daralet.dll`: it replaces
the old entry, since Decal knows them as the same plugin. Then delete `DaraletPlayer.dll`. Your settings are kept.

## Your settings

Everything the plugin keeps is in

    Documents\Decal Plugins\Daralet

and shared by every character on your computer: window positions, display options and loot profiles in
`settings.json`, loot profile files in `Loot Profiles`, the downloaded map and places in `world-data`, and
`log.txt`. To start fresh, close the game and delete `settings.json`.

To move your loot profiles to another computer, or share them with a friend, use **Export** on the Loot tab and **Import**
on the other end.

## Things to know

- **Loot:** the looter starts off. If you also use VirindiTank's looter, only turn one of them on, or they'll fight over
  the same corpse. Salvaging needs an Ust in your packs.
- **Market:** listing, cancelling and claiming only work when you're near a Market Broker (the market stand in
  Hebian-To). Searching works from anywhere. Market search and leaderboards come from the Daralet website, so they need
  an internet connection, the same as the website.
- **Map:** the map and its places update themselves: each time the plugin starts it checks GitHub for a newer map
  image and `locations.xml`, and downloads them if they've changed (into `Documents\Decal Plugins\Daralet\world-data`).
  **Show...** on the World tab says what it found and has a **Check for map updates** button. Without an internet
  connection the plugin uses the ones it came with. To use your own copy of the map image instead, put a square
  `dereth-map.png` next to `Daralet.dll`. It has to keep the original's layout. To use your own places, do the same
  with a GoArrow-style `locations.xml`. Files next to the dll always win over downloaded ones.

## Problems

- **"Failed to register plugin" when adding the dll:** Decal is older than 2.9.8. See step 1 of Installing.
- **Daralet isn't in Decal's list, or isn't ticked:** run the installer again, or add it by hand (Installing by hand,
  step 5) and tick it.
- **"Could not load file or assembly 'UtilityBelt.Service'" in chat or `log.txt`, or no UtilityBelt bar in game:**
  UtilityBelt.Service isn't installed. Run the installer again and tick the UtilityBelt.Service box, or see Installing
  by hand, step 2. Having UtilityBelt alone isn't enough. (Decal's
  **Add/Remove Plugins** window saying UtilityBelt is "Not Installed" doesn't matter: it only knows about things
  installed from that window.)
- **Nothing happens when you type `/dap`:** check that UtilityBelt.Service is under **Services** in the Decal agent, and
  that Daralet is ticked under **Plugins**. Then restart the game.
- **A tab says the server is too old:** the server hasn't been updated for that part of the plugin yet. The rest still
  works.
- **Lines starting with `DAP2|` (or `DAP1|`) show up in chat:** those are messages from the server to the plugin, and the plugin
  normally hides them. Report it, and include `log.txt` from `Documents\Decal Plugins\Daralet`.
- **Anything else:** errors are written to `log.txt` in `Documents\Decal Plugins\Daralet`. Include that file when you
  report a problem.

## Uninstalling

If you used the installer, uninstall **Daralet (Decal plugin)** from Windows' **Installed apps** (Add or remove
programs). It takes Daralet out of Decal and asks whether to delete your settings and loot profiles too.

If you installed by hand: in the Decal agent, select **Daralet** and click **Remove** (or just untick it). Then delete
`Daralet.dll`, and `Documents\Decal Plugins\Daralet\` if you don't want your settings any more.

## For developers

See [CLAUDE.md](CLAUDE.md) for how the plugin is built and how it talks to the server. In short:

```bash
dotnet build DaraletPlayer.sln -c Release
```

```bash
dotnet test tests/DaraletPlayer.Tests/DaraletPlayer.Tests.csproj
```

Both dlls end up in `bin/`. `Daralet.Loader.dll` is for development only: add it as a Decal network filter (with
Daralet unticked) and it reloads the plugin every time you rebuild. Players only need `Daralet.dll`.

### Releasing a new version

1. Raise `<Version>` in `src/DaraletPlayer/DaraletPlayer.csproj` and merge it into `main`.
2. Tag that commit `v` and the version (`v0.5.0`) and push the tag.
3. The **Release** workflow (`.github/workflows/release.yml`) builds and tests the plugin, builds the installer from
   `installer/Daralet.iss`, and publishes the release with `Daralet-Setup-<version>.exe` and `Daralet.dll`. It stops if
   the tag and `<Version>` disagree. Run it by hand from the Actions tab to get the installer as a run artifact without
   releasing anything, to try it first.

To build the installer yourself, install [Inno Setup 6](https://jrsoftware.org/isinfo.php), do a Release build, then run
`iscc /DAppVersion=0.5.0 installer\Daralet.iss`. It's written to `out\`.

### Publishing a new map or places

Both Daralet plugins download the map image and `locations.xml` from this repository's
[`world-data` release](https://github.com/daralet-ac/daralet-player-plugin/releases/tag/world-data). To update them for
everyone, without a new plugin:

1. Change `src/DaraletPlayer/Resources/locations.xml` or `src/DaraletPlayer/Resources/dereth-map.png` (keep the map
   2048x2048 and laid out the same) and merge it into `main`.
2. The **World data** workflow (`.github/workflows/world-data.yml`) uploads both files and a `manifest.json` of their
   hashes to the release. It can also be run by hand from the Actions tab.
3. Players get them the next time the plugin starts, or straight away with **Check for map updates**.

The release is a pre-release with a fixed tag, so it never shows up as the plugin's latest release. Don't edit it by
hand: the workflow replaces its files each time. Daralet Admin builds in the same files, so copy them over there too when
you next release it (it downloads them anyway).
