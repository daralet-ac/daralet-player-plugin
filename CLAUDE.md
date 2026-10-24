# Working on this plugin

Daralet Toolkit (the player plugin: `DaraletToolkit.dll`, `DaraletPlayer` as its namespace in code, plain "Daralet
Toolkit" wherever players see a name) is a Decal plugin for Asheron's Call, for players on the Daralet server (daralet-ACE). Its sibling, Daralet Admin
(`../daralet-admin-plugin`), is the staff plugin this one was started from: when something here is unclear, see how it's
done there. Until v0.7.0 it was called Daralet, its dll `Daralet.dll` (`DaraletPlayer.dll` until v0.1.0),
and its repo `daralet-player-plugin`; the assembly guid (what Decal knows it by) never changed, and the installer deletes the old dlls. This file is the knowledge that building them took: copy it into any new Daralet Decal plugin and keep it
up to date.

## The stack

- SDK-style project, `net48`, `PlatformTarget` x86, `LangVersion` 12. No C# records: net48 has no `IsExternalInit`.
- UI: [UtilityBelt.Service](https://gitlab.com/utilitybelt/utilitybelt.service) 2.2.12 from NuGet, with
  `ExcludeAssets="runtime; build; contentFiles"`, because it's installed on the client. It's a separate install from
  UtilityBelt (its own installer, a Decal service under Services, in `C:\Games\DecalPlugins\UtilityBelt.Service`);
  players who only ran UtilityBelt's installer get "Could not load file or assembly 'UtilityBelt.Service'", so the
  README links the 2.2.12 installer. 3.x is out but untested here. Windows are
  `UBService.Huds.CreateHud(name)` with `OnRender`, drawn with ImGui (`ImGuiNET`). Images use `ManagedTexture`, made
  lazily the first time it's drawn, once the game's graphics device exists.
- `deps/` holds `Decal.Adapter.dll` and `Decal.Interop.Core.dll` from Decal 2.9.8.3, and `UtilityBelt.Common.dll` from
  UtilityBelt.Service 2.2.12 (installed beside it, with ACE's dat readers in it, `ACE.DatLoader`), all referenced with
  `Private=False`. The dungeon map uses `UtilityBelt.Common` directly; `ItemIcons.EnsurePortalDat` and `Spells` still reach
  it by reflection, from before it was referenced. It's a .NET Standard dll, so a throwaway console app (net48, x86) can
  reference it and open a game folder's dats to try dat code outside the game.
- `Microsoft.DirectX.Direct3D` is referenced from `$(WINDIR)\Microsoft.NET\DirectX for Managed Code\1.0.2902.0\` only so the
  compiler can tell `ManagedTexture`'s constructors apart. UtilityBelt installs it.
- MSB3277 is suppressed: UtilityBelt.Service was built against Decal.Adapter 2.9.8.0 and Decal installs 2.9.7.5 (assembly
  version). Only the installed one is ever loaded.
- Based on UtilityBelt's [HotDecalPluginTemplate](https://gitlab.com/utilitybelt/HotDecalPluginTemplate). The loader
  (`src/DaraletPlayer.Loader`, built as `DaraletToolkit.Loader.dll`) is a Decal network filter that loads `DaraletToolkit.dll` from bytes
  and reloads it on every rebuild.

Build and test:

```bash
dotnet build DaraletPlayer.sln -c Release
dotnet test tests/DaraletPlayer.Tests/DaraletPlayer.Tests.csproj
```

## Gotchas, each found the hard way

- **Decal must be 2.9.8.x.** Decal 3.0 (2.9.7.5) runs plugins on .NET 2.0 and says "Failed to register plugin" for a net48
  dll. The agent's title bar shows the version.
- **Decal registers a .NET plugin by the assembly's `[Guid]`**, under `HKLM\SOFTWARE\WOW6432Node\Decal\Plugins\{guid}`, with
  `Object` = the plugin class's full name. Give every plugin (and loader) its own guid in `Properties/AssemblyInfo.cs`:
  never reuse Daralet Admin's, or the two can't be installed side by side.
- **A hot-reloaded plugin isn't registered with Decal**, so `PluginBase.Host`, `Core` and the base events
  (`ServerDispatch`...) never work for it. Use `CoreManager.Current` everywhere, and
  `CoreManager.Current.EchoFilter.ServerDispatch` for raw network messages. `CharacterFilter.LoginComplete` has already
  fired when a reload starts, so check `CharacterFilter.LoginStatus == 3` instead.
- **A plugin running in the game locks `bin\*.dll`** (acclient and DenAgent). To check a build while it runs, build to
  another folder (`-p:OutputPath=...`), or develop with the loader.
- **A local ACE server locks daralet-ACE's `dist\apps\server`**, so `dotnet test` there fails to copy `ACE.Server.exe`.
  Build the tests somewhere else instead: `-p:BaseOutputPath=<dir>/ -p:OutputPath=<dir>/`. Built that way, the
  `StartupTests` and physics tests fail for want of `Config.js` and the dat files; the rest are meaningful.
- **Piping `dotnet build`/`dotnet test` into `grep` can hang forever**: MSBuild's reused worker nodes keep the pipe open
  after the build ends. Pass `-nodeReuse:false` (and set `MSBUILDDISABLENODEREUSE=1`), or write to a log file and grep that.
- **Unit tests (net48 MSTest) can't load Decal.Adapter.** Keep Decal types out of anything tested: for example, pass
  logging in as `Action<string>` instead of calling `PluginCore.Log`.
- **`ImGui.GetColorU32` crashes before an ImGui context exists**, so colours in static fields are packed by hand
  (`(a << 24) | (b << 16) | (g << 8) | r`).
- **Modal dialogs opened from a right-click menu** must be opened (`OpenPopup`) at the window level on the next frame, not
  inside the menu: popups are found by ID stack. See `PlayerActions.RenderDialog` / `CommandConfirm` in Daralet Admin.
- **A hot reload must leave the old copy inert.** The loader shuts the running plugin down and starts the new build in the
  same game. If any old window kept drawing, it kept asking the server for data while its chat filter was gone, and the
  `DAP1|` replies flooded the chat box. So shutdown disposes each piece on its own (`PluginCore.ShutdownStep`,
  `PlayerHud.Dispose`), and a disposed feed or window does nothing (`PlayerFeed.Request`, `FloatingWindow.Hud_OnRender`).
- **UtilityBelt can start without the portal dat.** It opens `client_cell_1.dat`, then `client_portal.dat`, by relative path
  from the game's working folder, and stops at the first that fails. ShadowLauncher starts each game in its own
  `AppData\Local\ShadowLauncher\Instances\<id>` folder of that server's dats (hard links into `DatSets`, or another dat
  folder), and a fresh cell dat there left `UBService.PortalDat` null: every `GetIconTexture` threw a
  NullReferenceException. `ItemIcons.EnsurePortalDat` opens `client_portal.dat` from the same working folder (so it's still
  that server's dats, custom icons and all) and gives it to UtilityBelt, by reflection, since `PortalDatDatabase` is in
  `UtilityBelt.Common`, which the plugin doesn't reference.
- **Decal's WorldFilter.GetByContainer and GetInventory go stale.** An item moved into a bank pack after the bank was
  opened (a `/bank deposit`) didn't show in GetByContainer until the bank was reopened, though the client and the item's
  own `LongValueKey.Container` had it. The bank tab groups `WorldFilter.GetAll()` by each object's Container instead
  (`InventoryBrowser.Snapshot`, twice a second, and the frame after Decal hears any object change, `InventoryChanges`, so a move shows the moment the server answers).
- **Decal throws away a vendor's unique item once you buy it.** A unique item (one sold back to a vendor, a market
  listing) keeps its guid when bought, and Decal knows that guid as one of the vendor's items. The server answers a
  purchase with the item's CreateObject (your pack as its container) and then the vendor's list again
  (`vendor.ApproachVendor` at the end of `Player.FinalizeBuyTransaction`), and Decal releases the vendor items missing
  from the new list, guid and all: the bought item, which then stays out of every list until the next login. Moving it
  between packs doesn't help (the server only updates its container, which Decal no longer has); dropping it and
  picking it up does (a fresh CreateObject). The plugin can't make the server send one, so the server does: a tenth
  of a second after the purchase it sends each bought unique item again (`Player.ResendBoughtUniques` in daralet-ACE). For five seconds after a buy or a wield every item message (what the client sent, the server's answers,
  Decal's changes and releases) goes in log.txt as `[inventory]` lines (`InventoryChanges.Watch.cs`).
- **The game drops an item dragged from its pack and let go over a plugin window.** ImGui windows aren't game panels, so
  to the game the button came up over the world. `GameDrag` eats that WM_LBUTTONUP (Decal's `WindowMessage`, `e.Eat`)
  when it's over a window the drag was held over last frame (`GameDrag.Cover`, from `FloatingWindow` and `PlayerHud`) and
  the selection is something you carry, and posts the game one where the drag began, over the item's own place. ImGui
  may not hear the eaten release either, so a hotbar also takes it from `GameDrag.CaughtOverLastItem`. If the game drops
  the item anyway within three seconds, it's moved back to the container and slot it came from (`WatchDropped`).
  `[drag-in]` lines in log.txt.
- **Decal's hooks crash the game before login is over.** `Actions.Heading`, `Landcell`, `LocationX`, `CurrentSelection`,
  `ChatState` and the like read the player straight from the game's memory, which isn't there at character select or
  while logging in: an AccessViolationException, which no catch stops, and acclient dies (seen from the Map window
  drawing during login, `ACHooksClass.get_HeadingDegrees` in the Windows event log). Nothing reads one until
  `CharacterFilter.LoginStatus == 3`: `FloatingWindow` stays hidden until then, `SelfPosition` gives nothing, and
  `Hotkeys.ForTheGame` and `GameDrag` check first. The main window draws at character select, so a tab mustn't read them
  before the feed is connected (it isn't until login).
- **Every Decal event handler and render callback is wrapped in try/catch.** An exception escaping into Decal can crash the
  client.
- **JSON:** System.Text.Json writes dictionaries as objects; `DataContractJsonSerializer` only reads them with
  `UseSimpleDictionaryFormat = true` (see `Feed/Json.cs`).

## What the client already gets, and what it doesn't

Read from daralet-ACE; check before relying on it.

- **Fellowship vitals only reach a client whose fellowship panel is open** (`Fellowship.OnVitalUpdate` checks
  `FellowshipPanelOpen`), so the plugin gets them from `@playerui fellowship` instead, once a second.
- **A target's health** comes back when the client asks for it (`QueryHealth`), as a percentage. Its stamina, mana, buffs
  and debuffs are never sent to the client: those need the server.
- **Threat** lives on the monster (`ThreatLevel`, `PositiveThreat`, `NegativeThreat` in `Monster_Awareness.cs`). Players can
  already see it in chat by appraising a monster with the "attempt to perceive threats" option on
  (`Player_Awareness.GetMonsterThreatTable`): a Perception roll against half the monster's Deception, at most once every
  10 seconds, and only as tiers ("Major threat"...), never numbers. The plugin deliberately doesn't follow those rules: it
  shows the exact `ThreatLevel` numbers, with no Perception roll, updated every second (the user's call, October 2026).
- **The bank** is a `Storage` container the player opens in the world. `/bank` (`Commands/PlayerCommands/BankCommand.cs`)
  does the rest: deposit, withdraw, sort, search, combine, balance, packs, inscribe, icon, autosort, log.
- **The market** is run by Market Broker NPCs (`apps/server/Market/MarketBroker.cs`): give one an item to list it, and talk
  to it (`listings`, `cancel <id>`, `payouts`, `claim expired`). Listings live in the database.
- **The Market API** (`apps/market-api`) is the public, read-only HTTP service the website uses for market search
  (`/api/market/listings`) and leaderboards (`/api/leaderboards/...`), at `https://play.daralet.ac/api/...` (nginx only
  passes `/api/` through, so its `/health` isn't reachable from outside). The plugin calls it directly over HTTPS for market
  search and leaderboards: off the game thread, with TLS 1.2 switched on (`ServicePointManager.SecurityProtocol`), since
  net48 doesn't always offer it by default. A player's own listings, sales and expired items aren't public, so those come
  from `@playerui`.
- **The arena** is `/arena` (`Commands/PlayerCommands/ArenaCommand.cs`): queue, challenge, leave, stats, top, maps, watch.
- **Map locations** come from GoArrow's `locations.xml`, in Phenyl's Daralet edition (its "About" entry says so): about 1,700
  `<loc id name type NS EW>` entries, in game coordinates (south and west negative). Types are Town, Dungeon, Landmark,
  Lifestone, Outpost, Bindstone, Vendor, NPC, AllegianceHall, TownPortal / WildernessPortal / UndergroundPortal (with
  `exitNS`/`exitEW`, where the portal goes) and Custom (notes, like the About entries in the corners). Dungeons have a
  `dungeonId`. The text inside an entry is a free note: "Level 30 Capstone" on capstone dungeons, a number on towns, a tier
  ("T2") on others. The website's `mapdata.js` is a smaller set taken from the same data, as image percentages
  (EW = x% × 2.038 − 101.9, NS = 101.9 − y% × 2.038). Daralet Admin's `Map/MapProjection.cs` and `Map/MapView.cs` draw the
  map, in the same game coordinates.

## Talking to the server

There's no custom network message: the client and Decal don't know new opcodes. Instead:

1. The plugin sends a chat command with `CoreManager.Current.Actions.InvokeChatParser("@playerui hello")`.
2. The server replies with system chat lines `DAP2|kind|id|index|count|chunk`: camelCase JSON in base64, split into
   200-character chunks (`PlayerUiProtocol`, which uses `AdminUiProtocol`'s encoding and chunking with its own prefix).
3. The plugin reads them from the raw `0xF7E0` message (`e.Message.Value<string>("text")`), joins and decodes the chunks
   (`Feed/ReplyAssembler.cs`), and hides them from chat with `CoreManager.Current.ChatBoxMessage` + `e.Eat = true`.

**Never put readable text in a chat line meant for a plugin.** Other chat plugins act on it before yours can hide it:
Virindi Chat System posts its own copy of any line with coordinates in it ("Hebian-to (39.0S, 83.0E)", a fellow's
location), and `e.Eat` doesn't stop that copy. That's why the JSON goes out in base64. Before it did, replies showed in
chat, sometimes cut short and starting mid-reply (`lse,"members":...`), which was first taken for the client splitting
long lines. `DAP1` lines (plain JSON) are still read from older servers, and a piece of one that reaches chat is hidden and
logged once to `log.txt` (`ReplyAssembler.LooksLikeReplyPiece`).

Server side, in daralet-ACE: `@playerui` is a Player-level command (`apps/server/Commands/PlayerCommands/PlayerUi.cs`, with
its code in `apps/server/Entity/PlayerUi/`). `PlayerUiProtocol` uses `AdminUiProtocol`'s chunking with its own prefix.
Don't reuse `@adminui`: it needs Sentinel, and it sends data about everyone. The JSON classes in `Feed/Models.cs` mirror the
records in `PlayerUiSnapshots.cs`; tests on both sides pin the names (`PlayerUiProtocolTests`, `ReplyAssemblerTests`).

The `hello` reply lists every request the server answers (`kinds`, from `PlayerUi.Kinds`). Add each new request there, and
have its tab check `PlayerFeed.Supports(kind)`, so a newer plugin on an older server says what's missing instead of waiting.

`/dap` (or `@dap`) shows and hides the window.

Requests so far: `hello`; `fellowship` (your fellowship with vitals and map places, or `null`); `position` (where you
are on the map, which the plugin only asks indoors); `targets <guid> [guid]` (up to four
creatures in your landblock or the ones around it: vitals, the spell in effect in each category, attack target and
threat table, or `"status":"gone"`). Another player as a target gets only `"status":"player"` and their name: the plugin
shows the health percentage the client already has (game event `0x01C0`), never their spells or exact vitals. The server
ignores a request of the same kind sooner than 400ms after the last (hello: 2 seconds), and more than 10 a second of every
kind together, in bursts of up to 20 (`PlayerUiLimits`, a token bucket per player; each a `/modifylong playerui_...`
server property, 0 for no limit). The plugin never asks sooner than 500ms (`PlayerFeed.MinInterval`) and sends about 6 a
second at most, with every window open: keep it inside those limits, or its windows wait 3 seconds for a reply that never
comes (`PlayerFeed.RequestTimeout`). Polling goes through `PlayerFeed.Refresh`. `bankwindow` and `lootwindow`
(`on|off`, answering `{"on":...}`) are described below. `rollranges` sends the ranges loot rolls each filter stat in (`{"ranges":[{"category","stat","tier","min","max"}]}`), for
the loot rules' tooltips. `character` sends your character stats (`{"sections":[{"title",
"lines"}]}`), for the Character tab. `lfg` sends looking for group, for Find a group (see the Fellowship tab). The server can also send a reply unasked (`PlayerUiProtocol.Send`),
which the plugin reads the same as one it asked for.

**Map places indoors:** the game's `GetMapCoords` gives nothing for any indoor cell (0x0100 and up), buildings included.
But a building's rooms share their landblock's coordinates, so the server places them anyway (`PlayerUiData.GetMapCoords`,
`inBuilding: true`); only a true dungeon (`Town.HasReliableNearestTown`, the check listplayers uses) has no place on the
map. The client can do the same sums (`Map/Navigation.MapCoords`) but can't tell a building from a dungeon, which is why
it asks `position` when you're indoors.

**The bank:** opening a bank chest sends the client every pack and item in it (`Storage.SendBankVaultInventory`), so
Decal's WorldFilter has them all, and moving one is the game's own move (`Actions.MoveItem`), which the server checks.
What the client can't tell is which open container is a bank: `@playerui bank` says (`open` means open and near enough to
use, like every `/bank` command but balance, log and intro needs) and numbers its packs the way `/bank packs` does.

**The plugin's windows instead of the game's:** a Decal plugin can't stop the client drawing a container's window, but the
server can leave out what makes it: `GameEventViewContents`. `@playerui bankwindow on|off` and `@playerui lootwindow
on|off` set `Player.PlayerUiBankWindow` / `PlayerUiLootWindow` (in memory only: the plugin says which it wants each time it
starts, `PlayerFeed.WantWindow`, every frame until the server agrees). With one on, opening a bank
(`Storage.SendBankVaultInventory`), or a corpse or chest (`Container.SendInventory`), still sends every item's
CreateObject, so Decal has them, but no ViewContents, and pushes the `bank` or `corpse` reply unasked instead; closing
pushes it again (the bank closed, or a corpse with status `closed`). Without ViewContents Decal's `ContainerOpened` doesn't
fire, which is why the server says. Two things the client normally does go missing with the window: it never sends
NoLongerViewingContents when you walk away, so the server closes such a container from the player's heartbeat
(`Player.ClosePlayerUiContainerOutOfReach`); and closing is a Use of the open container (`Actions.UseItem`), which the
plugin's windows send when they're closed. If the plugin is removed mid-session with these on, the game's windows stay away
until the next login.

**The market:** a Market Broker is driven by talking to it (`MarketBroker.HandleTalkDirect`: `listings`, `cancel <id>`,
`claim payouts`, `claim expired`, a price after you give it an item). `@playerui market` sends your listings, sales (last 30
days, from `GetSoldListingsForAccount`) and expired items, and whether a broker is within 30 meters
(`PlayerUiMarketData.BrokerRange`, found from what the player can see). `@playerui broker list|cancel|claim|expired` hands
the same words to that broker, so its checks and Yes/No confirmations apply; with no broker near it says so. Working out a
listing's item name loads the item from the database (`MarketBroker.TryGetListingItemName`), so names are cached by
listing id. The four queries behind `@playerui market` run off the world thread (`PlayerUiMarketData.RequestMarket`, the
reply sent from the world thread once they're in), and their rows are kept per account for 4 seconds (`RowsFor`), or until
a listing or payout of the account's changes (`MarketListingEvents.AccountChanged`, raised by `DbPlayerMarketRepository`'s
writes). The broker answers in tells ("Market Broker tells you, ..."), which `SendCommand` keeps as replies too.

**The arena:** `@playerui arena` (`Arena/ArenaManager.PlayerUi.cs`, a partial like the admin one) sends your state in one
word (off, idle, queued, watching, defeated, accepting, starting, fighting) and in words (`ArenaManager.Status`), your
ratings (`ArenaBoards.All`), duels to watch and the latest results. The buttons send the `/arena` commands themselves.

**Loot:** `@playerui corpse <guid>` describes a corpse's items (you have to be allowed to loot it, `Corpse.HasPermission`,
and near enough to use it), or a chest's or other world container's while you have it open (`Container.Viewer`,
`PlayerUiCorpseData.IsLootableContainer`: a Chest or a plain Container, the static dungeon corpses being the latter; the
bank, house hooks and anything in an inventory are `notcorpse`, which
the looter drops quietly). The looter asks about any corpse, and any container in the world (not in an inventory) but the
open bank; `@playerui describe <guid>...` does the same for up to 20 items you carry (the Loot tab's test);
`@playerui salvage <guid>...` salvages up to 20 carried items with the Ust in your packs, through `Player.HandleSalvaging`
(so pack space, retained and unstable items are checked as ever; `Player.IsSalvageable` is the per-item check both use).
Each item is described by the Market API's own code, shared as linked source: `apps/market-api/MarketRepository.Items.cs`
(`MarketRepository.Describe(IItemFacts)`), `ItemFacts.cs` and `MarketListing.cs` are compiled into the server too. The API
reads the facts from its query's row (`RowFacts`); the server from an item snapshot (`SnapshotItemFacts`, made in memory by
`MarketListingSnapshotSerializer.TryCreateLiveSnapshotJson`, since loot isn't in the database yet). `ItemColumns.Paths` says
where each column is in a snapshot, copied from the SQL; `SharedItemDescriptionTests` checks the two agree, so a column
added to the query needs a row there too. Tiers come from `MarketBroker.GetItemTier`, the market's own. Corpse replies
leave nulls out (`PlayerUiProtocol.BuildMessages(..., omitNulls: true)`): each item is a whole listing.

## The plugin's own pieces

- **The map image** is `Resources/dereth-map.png`, 2048x2048: 2040 pixels of Dereth (10 pixels a map unit, -102 to +102 both
  ways, north up) with 4 pixels of sea round it (`Map/MapProjection.cs`). It came from Daralet Admin's `tools/MapGen`, made
  from the server's dat files, then had some islands and artifacts painted out by hand; `Resources/dereth-map.PSD` is the
  Photoshop source for that edit (not built into the plugin). To change it, edit that file and rebuild. Or, to try an edit first, put a `dereth-map.png`
  next to the plugin dll: it's drawn instead (`MapView.OpenImage`), once the plugin next starts. It has to keep the same
  layout, or places land in the wrong spots, and be square (one that isn't is skipped, with a line in chat).
- **World data** (`Map/WorldData.cs`, `.github/workflows/world-data.yml`): the map image and `locations.xml` update
  without a new plugin. On every push to `main` that changes either file, the workflow uploads them, then a
  `manifest.json` (`format`, `published`, `commit`, and each file's `name`, `sha256` and `size`), to the
  `world-data` pre-release (fixed tag, files replaced with `--clobber`). When the plugin starts, `WorldData` reads the
  manifest from `releases/download/world-data/`, downloads any file whose hash differs from what it has, checks the hash
  and the contents (`Locations.Check`, `WorldData.CheckSquarePng`), and saves it in
  `Documents\Decal Plugins\Daralet Toolkit\world-data`. A file that matches the built-in one isn't downloaded. This all runs off
  the game's thread; `WorldData.Version` going up is the World tab's cue to reload the places and the maps
  (`MapView.Reload`), on the game's thread. Lookup order for both files: next to the dll, then downloaded, then built in.
  Daralet Admin has copies of `WorldData.cs` and `Locations.cs` and reads the same release: keep them the same. Raise
  `WorldData.Format` (and the workflow's `format`) only when old plugins couldn't read the files; they then keep what
  they have.
- **Item tooltips** (`Feed/ItemLines.cs`, `Ui/ItemTooltip.cs`): every stat an item's market description has, as lines in
  sections; the bank, the Loot tab, the Corpse window and market search all draw them, so the layout changes in one place.
  An item you hold or loot is titled with its own name, which the server sends with its material in front
  (`NameWithMaterial`; `ItemLines.WithMaterial` adds it if missing), and its value, burden and stack go on the workmanship
  line.
  The bank asks for descriptions as the mouse passes over items (`PlayerFeed.Describe`, 20 a request; `@playerui describe`
  covers your packs and your open bank). The server sends nothing for an item it won't
  describe, nor for a request its rate limit drops, so DescribeRequests keeps track: a request with no answer in 3
  seconds is sent again (3 tries), an item a reply left out is not yours (DescribeState.NotYours), and either is asked
  about again after 5 seconds if something still wants it; PlayerFeed.DescribeWaitText says which in place of the
  details. Tiers show as T0-T7 everywhere, the way the Search tab's filter has them.
  Speed is the weapon's WeaponTime (`MarketListing.WeaponTime`) with attacks a second after it, as "atk/s": 60 / WeaponTime,
  so 60 is one a second (`WeaponSpeed.GetSecondsPerHit` in daralet-ACE, at 100 Quickness). The Market API's Dps uses
  the same rule, so DPS and atk/s agree. Casters show no speed. Text is always drawn with `Widgets.Text*`
  (`TextUnformatted`): ImGui.NET's `Text*` read a "%" as a printf format. Items `@playerui` describes carry `rollQuality`: appraisal's Roll Quality section
  (`LootGenerationFactory.GetItemRollRanges`) by stat name, which `ItemLines` matches to the line showing the stat
  (`ItemLine.RollStat`) and `ItemTooltip` draws at the right. Market search has none: the Market API has no live item.
- **Loot tab** (`Ui/LootTab.cs`, `Ui/Looter.cs`, `Feed/Loot.cs`): named lists (players see them as "loot profiles"; the code
  still says `LootList`) in `SettingsData.Loot`, each character
  picking one (`LootData.ActiveFor`). A rule is a category, tiers, name words and any of `MarketSearch.Filters`, plus an
  outcome (Keep, Salvage, Notify, Ignore); the first enabled rule that matches decides. The rule list is widened by
  dragging the bar between it and the editor (`SettingsData.LootRuleListWidth`, 0 for the default); a long rule name
  is cut short of its mark, which stays at the row's right end. **Folders** (`LootRule.Folder`, `Feed/LootFolders.cs`,
  `Ui/LootTab.RuleList.cs`) are only a name on rules, and a folder's rules are always kept together where its first one
  is (`LootFolders.Gather`), so the list's order is still the order rules are checked in. Ctrl/Shift+click picks several
  rules (`chosenRules`) for a folder or a drag; rules and whole folders drag to a new place (dropped on a rule: above it,
  in its folder, so on a folder's first rule at its top; on a folder itself: at its end, framed while held there; below the list: the end, out of any folder). Folded folders are
  `LootList.ClosedFolders`. Held near the list's top or bottom
  edge, a drag scrolls the list (`ScrollWhileDragging`). Right-click Duplicate copies a rule (or every picked one) just below itself, or a folder and its rules
  just below it as "Name copy" (`LootFolders.Duplicate`, `DuplicateFolder`). **Add folder** makes an empty one at the top
  (`LootList.EmptyFolders`, since a folder is otherwise only a name on its rules); a rule dropped in goes to the top of the
  list, and the folder leaves `EmptyFolders` once it has one (`LootFolders.Tidy`, after every change to the list). The list's changes wait until it's drawn (`pendingRuleMove`). `Loot.Matches` compares each filter
  with the same field the API's SQL does, per armor slot where the API is (ward level, armor and clothing mods), with
  "price" as the item's vendor value. A filter it doesn't know leaves the rule alone. Some filters are the loot rules' alone
  (`MarketFilter.LootOnly`, which the market search leaves out: the Market API has no such parameter): burden and value
  per burden for every category (burden 0 counting as 1), a weapon's
  **Subtype** (Melee / Thrown, Missile, Caster, from the class, `MarketSearch.SubtypeOf`), the damage stat for each
  (base damage, a launcher's `DamageMod`, a caster's `ElementalDamageMod` and `RestorationMod`) and **Damage element**,
  a multi-choice (`FilterKind.Multi`, comma-separated, any one matches). With a subtype picked only its filters show
  (`MarketFilter.Subtypes`), the Class list only its classes, and a hidden box doesn't count. Under it a weapon
  **Type** (`MarketSearch.WeaponTypes`: Sword, Atlatl, Orb...), which decides the subtype and shows its special
  property's filter (`MarketSearch.SpecialProperties`: Biting Strike, Crushing Blow, Armor and Ward Cleaving, Stamina Cost
  Reduction, `MarketFilter.WeaponTypes`), matched against `MarketListing.PropertyValues`. The type comes only from the
  server (`weaponType`, `PlayerUiWeaponType` in daralet-ACE, by the weapon's skill and its loot tables' caster and thrown
  rows, as `TryMutateWeaponSubtypeBonuses` picks the property), so a rule with a type matches nothing on an older one.
  Note the server's Sceptres roll Biting Strike and its Wands and Batons Crushing Blow. Armor's **Weight** shows only the
  mods that weight class rolls, and jewelry's **Slot** only the two ratings that slot rolls (rings crit damage and its
  resist, bracelets damage and its resist, amulets healing boost and max health; ward on all), both by `MarketFilter.Kinds`
  (from `TryMutateGearRating` and `LootGenerationFactory_Jewelry`; `Loot.KindOf` says which filter picks the kind). `Loot.Shows` decides what's
  shown, and so what counts. The editor shows the filters under headings, Types, Damage, Protection, Mods, Magic and Misc
  (`MarketFilter.Group`, set by `MarketSearch.GroupOf` from the parameter), leaving out a heading with nothing shown.
  **Spell** and **Scroll spell** take several names, comma-separated, any one matching (part of a name, case and spaces
  ignored, `Loot.WordsHold`); the Market API takes one, so the search sends the first (`MarketSearch.FirstSpell`).
  The Mods heading has an **OR** box (`LootRule.ModsAny`): on, any one Mods filter filled in that holds is enough, while
  every other filter still has to.
  **Max speed** (`maxWeaponTime`) is the weapon's own Speed, its WeaponTime, lower being faster; the Market API only filters
  its Speed (2 - WeaponTime / 100, held between 1 and 2.5), so the search sends it as that (`MarketSearch.AddSpeed`). Each filter's tooltip says
  the range loot rolls it in at the rule's tiers (`RollRangesInfo.Describe`), from `@playerui rollranges`
  (`LootGenerationFactory.GetLootFilterRanges` in daralet-ACE, worked out once from the loot formulas: by category and
  Market API parameter without min/max, in the filter's own units, the lowest to the highest any item of the kind rolls).
  Stats whose range depends on the piece itself (armor level, an armor's ward) have none. A Salvage rule only takes items with a
  workmanship (`Loot.Decide`); anything else falls to the rules after it. The looter moves one item at a time
  (`Actions.MoveItem` into the main pack, the next once the last has left the corpse), and salvages after. The server
  refuses a pickup that comes while the last is still starting ("You're too busy", `GameEventInventoryServerSaveFailed`,
  event 0x00A0), which back-to-back looting hits, and a move sent while the last is still going can get no answer at all;
  the looter sends the same item again 150ms after a refusal, or after silence for 1.5 times the last clean pickup (at
  least 900ms; a fixed 3 seconds made Loot all take 4 an item), and waits 250ms after each landing before the next, since
  a move sent the moment the last landed was always dropped unanswered,
  giving up after 8 seconds (a full pack). The Corpse window shows "Looting..." with Cancel while it works, in every mode
  but chat. It starts off; there's no way to tell from a plugin whether VirindiTank's looter is on, so the tab just warns.
  **Loot window** (`SettingsData.LootWindow`, switched under the Config tab's Client UI Override) hides the game's corpse
  and chest window (see above): the Corpse window then
  lists everything in it (`Looter.Everything`), matched or not, with Loot all, and works with the looter off too. **Grid**
  (`SettingsData.LootGrid`) shows it as 32-pixel icons, 8 a row (the window sizes itself), in a section per outcome (Keep,
  Salvage, Notify, Ignore, no rule), with the rule marks and the stack bottom right; Ctrl+click ticks and unticks for
  Confirm loot. The
  server's word that one opened can reach the client before its items do, so an item only counts as looted once it's been
  seen in the corpse and then isn't (`Looter.Left`); before that fix, killed corpses showed empty. `GetCorpse` skips its
  range check for a container the player has open: a corpse's use radius is small, and the game opens it from where
  `WithinUseRadius` said "gone". With Loot window on the looter ignores the client's ContainerOpened and follows the server
  alone, and the window opens for any status, saying why it can't show the items. It logs what it hears to log.txt
  (`[loot]` lines, `Looter.LogCorpse`).
  Each "min" filter for a stat that loot rolls (`Loot.RollStats`, by the names appraisal's Roll Quality section and
  `ItemLines` use) can be switched to a roll quality on its own (`LootRule.RollFilters`, by filter key): its box is then the
  lowest roll quality, 0 to 100, rounded as the tooltip shows it, and an item without that roll doesn't match. Switching
  empties the box. A rule can also mark what it decides (`LootMarks`: `LootRule.Highlight`, a frame colour, and `Icon`, a
  symbol in the top left corner, in `IconColor`, else the frame's colour, else gold). Colours are picked freely (ImGui's picker) or from
  the ready-made swatches beside it (`LootMarks.Highlights`: gold, silver, bronze and more), and saved as `#RRGGBB`; the
  swatches' names, which rules were saved with before the picker, are still read. Shapes are drawn with ImGui shapes; the grade
  letters S to F (`LetterS`...) with `DrawLabel`'s GDI+ text, through the one `ItemIcons` (`ItemIcons.current`), since
  the marks are drawn from static code. The editor's icon picker draws each symbol. Marks are drawn by `ItemIcons.DrawMark` in
  the Corpse window, the Test tab and, with **Loot highlights** (`SettingsData.InventoryLootMarks`), the Inventory tab, which has
  every listed item described to match it. Item tooltips (bank, packs, Loot tab, Corpse window) end with the rule that decides the item, in its outcome's colour (`ItemTooltip.Show(..., rule)`). The first rule that matches decides the mark too, so an earlier rule without
  one hides a later one's.
  **Unlearned spells only** (`LootRule.UnlearnedSpellsOnly`, Misc) takes scrolls whose spell the character doesn't know:
  `Loot.KnowsScrollSpell` is set by `PluginCore` from Decal (`LongValueKey.AssociatedSpell`,
  `CharacterFilter.IsSpellKnown`), and a scroll whose spell can't be told counts as unlearned.
  **Sellable only** (LootRule.SellableOnly, beside Salvageable only) takes only what a Market Broker would list: the
  server sends sellable with each item it describes (MarketBroker.IsSellable: not attuned, character-bound or
  unstable, and of a kind the market takes). An older server sends none, and then every item counts as sellable. Misc's
  **Type** Gems takes only a gem with a material (Loot.MiscTypeOf): the description calls anything of the game's Gem
  kind a gem, and those without one count as Other. The market search still goes by the Market API's word. New
  settings get the
  **Example Loot Profile** (`Feed/ExampleLoot.cs`, read from `Resources/example-loot-profile.json`, a profile as
  To file saves one: to change it, save one and copy it over) after "My loot", then **Example Loot Profile (colorless)**
  (`example-loot-profile-colorless.json`, `ExampleLoot.MakeColorless`), the same rules marked white, 50% gray and black
  instead of gold, silver and bronze: change both together. The Example buttons add them again, and tests check their
  filter keys exist and that the colorless one still matches the other. **To file / From file** save and load profiles as `.json` in `Documents\Decal Plugins\Daralet Toolkit\Loot
  Profiles` (`LootFiles`).

- **Fellowship tab** (`Ui/FellowshipTab.cs`): sub tabs Members (the members and their vitals, and the fellowship window)
  and **Find a group** (`Ui/FindGroupTab.cs`, `Feed/LfgChoice.cs`, `Feed/LfgCommands.cs`, `Feed/LfgInfo.cs`), looking for
  group, whose server side came first so it works without Decal (daralet-ACE `docs/lfg.md`, `apps/server/Lfg/`):
  `/lfgroup <activity>` (join the first fellowship looking for more for it), `/lfgroup join <name>`, `/lfmore
  <activity> [manual]` (leaders: players looking for it join, or with manual the leader's asked about each), `/lfmore
  invite <name>` (the same as `/invite`), and `list` and `stop` on each. Not `/lfg`, `/group` or `/fellow`: the client
  keeps `/lfg` and `/clfg` for the LFG channel, and `/party`, `/g`, `/group`, `/fellow`, `/fellows` and `/fellowship`
  for fellowship chat (the command words in `acclient.exe`), so a server command by those names is never reached. The server posts each new lfg or lfm in the LFG channel under the player's
  name. The tab is a form that builds the words a player would type (`LfgChoice.Words`, which `LfgParser` in daralet-ACE reads) and shows the
  commands, then the fellowships and players looking (`@playerui lfg`, every 2 seconds while you're looking, 4
  otherwise), green where they fit you, with Join (Ask for a manual lfm) and Invite. Whether they fit, and why a button
  can't be used (`cantSeek`, `cantList`), is the server's say, from its own matching. Members' "You aren't in a
  fellowship" has a Find a group button, which opens that tab (`Widgets.TabItem(..., select)`: ImGui.NET only takes
  tab flags with a close button's bool, so it calls `ImGuiNative.igBeginTabItem` with none).
  **LFG chat links** (`Ui/LfgChatLinks.cs`, `Feed/LfgChatLine.cs`, `SettingsData.LfgChatLinks`, on to start, under Find a
  group's Settings): each line the server posts ends with what to type to answer it (`- to join: /lfgroup join Arwen`,
  `- to invite: /lfmore invite Holmez`), so the plugin finds those in `ChatBoxMessage`, keeps the line from the chat window
  (`e.Eat`) and adds it again to the same window in the same colour (`AddChatText(text, e.Color, e.Target)`) with a
  `[Join Arwen]` or `[Invite Holmez]` link on the end. Only players with the plugin see the links. A re-added line comes
  back through `ChatBoxMessage`, so a line already carrying one of the plugin's links is left alone, and your own lines get
  none. Links use `PluginCore.ChatLinkId` like the update notice's, but carry only a number the plugin handed out (the last
  100 are kept), never the command: anyone can put that id in a tell, and a made-up link must send nothing. The first line
  linked each session is logged (`[lfg-chat]` in log.txt), with its colour and window.

- **Combat tab** (`Ui/CombatTab.cs`): the Target tab it used to be (`Ui/TargetTab.cs`, still its own sub tab) and
  **Hotbars** (`Ui/Hotbars.cs`, `Ui/HotbarWindow.cs`, `Ui/HotbarItems.cs`, `Ui/Hotkeys.cs`, `Feed/Hotbar.cs`,
  `Feed/Keys.cs`). Each bar (`HotbarBar`, in `SettingsData.Hotbars`) is a `FloatingWindow` (`hotbar-<id>`, titled
  `Hotbar <id>`, since UtilityBelt knows a window by its title) of columns by rows on up to ten pages, with its layout,
  looks and hotkeys shared by every character and its items per character (`Slots`, by name). The first bar is there from
  the start, hidden. Colours are packed as ImGui's u32 by hand (`PackedColor`). A slot keeps the item's guid and what it
  is (weenie class, `WorldObject.Type`, and name, and its icon layers): when that one's gone (a stack used up) it takes
  the fullest one like it you carry (`HotbarItems.Resolve`), and meanwhile shows the saved icon, shaded. Items go on by
  dropping them from the inventory's lists (`InventoryBrowser.DragType`, the item in `InventoryBrowser.DraggedItem`) or from the
  game's own pack (`GameDrag`); only what you carry or wear. While one's held over a slot, the slot shows its icon at half
  opacity (`HotbarWindow.DrawDropPreview`, `ItemIcons.DrawAt`'s tint), drawn after the slot itself, or the slot's
  background covers it; the red circle where it can't go. Slots only move in **Edit mode** (dragged, onto another
  slot on any bar to swap, or off every window to empty it) or from their right-click menu (Move..., which picks it up
  for the next slot clicked, and Move to); outside Edit mode a click uses the slot, on the press. Using is
  `ItemUser.UseFromBar`: `ItemUser.Use`, except a tool used on something goes straight onto yourself or your selection
  (`HotbarSlot.UseOn`, `HotbarUseOn`: Auto is yourself if it works on you, else the selection, else the pick cursor).
  A kit's use flags name only a target that isn't you (in the game you select or pick yourself), so Auto never puts one
  on you; **One-click Heal** (`SettingsData.OneClickHeal`, on by default) sends any kit (`ItemUser.IsKit`: Decal's
  `HealingKit`, the server's Healer, whichever vital) to you at once, unless its slot says Your selection or Pick.
  `ItemUser.RenderCursor`, from every bar, keeps a wield's steps and the reticle going with the inventory closed. A
  slot's right-click menu also carries the window's Lock, size, background and Hide (`FloatingWindow.RenderMenuItems`),
  since a bar is all items and the window's own menu doesn't open over items. Bars use less window padding
  (`FloatingWindow`'s `padding`, set and put back like the border).
  **Hotkeys** are a key per place (row and column, whichever page shows), page keys, and Bind keys (hover and press).
  They're read from Windows (`GetAsyncKeyState`, every bindable key, once a frame from `Hotbars.Frame`), not ImGui, which
  only hears keys while it wants them; they fire only with the game's window in front, the chat bar not active
  (`Actions.ChatState`) and no ImGui text box typed in, and a hidden bar's keys do nothing. A plugin can't keep a key from
  the game, so the tooltips say to pick keys it doesn't use. One key does one thing (`HotbarData.Forget`).
  **Cooldowns:** an item's cooldown group and length are `SharedCooldown` (PropertyInt 280) and `CooldownDuration`
  (PropertyFloat 167), which only an appraisal sends (the first look asks, `Actions.RequestId`); Decal keeps them by
  number. A running cooldown is an enchantment on the player whose spell id is `0x8000 | group`
  (`EnchantmentManager.StartCooldown` in daralet-ACE). Decal's `CharacterFilter.Enchantments` doesn't pass those on, so
  `HotbarItems` reads them from the server's own messages (`EchoFilter.ServerDispatch`, game events 0x02C2 to 0x02C8,
  parsed from `Message.RawData` by `Feed/CooldownMessages.cs` in `Enchantment.Write`'s order; time left is the
  duration less the start time's size, the server's own sum), and those come first. Everything is by cooldown group,
  so one use shows on every slot sharing it (the combat ability gems, potions of a kind). `[cooldown]` lines in
  log.txt. A cooldown already running at login isn't seen until it's next started. Decal's list is still read after. In case Decal doesn't
  list those, a use from a bar starts one of its own (`HotbarItems.Used`), but only once the use shows it worked (`Confirm`: a
  use gone, fewer of its kind, or the item gone, within 3 seconds), as the server's does: a kit at full health starts none. Shown as a clock sweep
  (`CooldownShape.Sweep`, drawn as triangles with anti-aliased fill off, or the seams show), fill, dim or bar.
  **Structure** is Decal's `UsesRemaining` / `UsesTotal`.
  **Spells** (`HotbarSlot.SpellId`, `Ui/Spells.cs`, `Ui/SpellDrag.cs`): a slot can hold a spell instead of an item, put
  there by dragging it from the game's spellbook or spell bar, or from a slot's right-click menu (Put a spell here, the
  spells you know by part of their name). A plugin can't see the game's drags, and `GameDrag` takes the selection for what's
  dragged, which put the last selected item on the bar; so `SpellDrag` listens where the game says a drag began,
  `CM_Item::SendNotice_BeginDrag(itemId, spellId, flag)` (static, cdecl, entrypoint 0x6A9640): its three calls in the client
  are pointed at the plugin with UtilityBelt.Service's `AcClient.Hook(entrypoint, callLocation)`, the plugin notes a known
  spell's id (under 0x10000, `IsSpellKnown`) and calls the game's function on unchanged, and `Stop` puts the calls back
  (a hook left in after the plugin's gone would crash the game). `[spell-drag]` lines in log.txt. UtilityBelt.Service
  ships the client's own types and addresses as `AcClient` (`UIElementManager.s_pInstance`, `ClientMagicSystem`,
  `Entrypoint`, `Call`), which is where to look for anything else of the client's. A spell let go over a plugin window is
  kept from the game like an item (`GameDrag`), or the game takes it off its spell bar. What a spell is (name,
  description, school, icon, flags, target type, mana) is the portal dat's spell table, `UBService.PortalDat.SpellTable.
  Spells`, by reflection; Decal's `Decal.FileService.dll` has a spell table too, but it's built against Decal.Adapter
  2.9.8.3 and won't link against the 2.9.7.5 the plugin uses. Casting is Decal's `Actions.CastSpell`, which needs
  spellcasting mode; the bar doesn't switch modes for you (the user's call): out of it, it says "You must be in
  spellcasting mode to cast spells." and does nothing, before any word about the target. The target: none for an untargeted spell, you for a self-only one (SpellFlags.SelfTargeted
  0x8), your selection for a harmful one (not Beneficial 0x4), and your selection or you for the rest. A spell you don't
  know shows shaded.
  **The settings page** (`Ui/Hotbars.Settings.cs`, with its Keys tab in `.Settings.Keys.cs` and the form pieces in
  `.Settings.Controls.cs`): the bars down the left (shown or not,
  New hotbar) with Move slots (Edit mode) and Bind keys as lit buttons; the chosen bar on the right, under a preview of
  made-up slots (a count, a cooldown with uses left, a worn item, empty ones), in tabs Layout, Look, Cooldowns and uses,
  Keys and Items. Every slot anywhere (the bar, the preview, the style and cooldown pickers) is drawn by `SlotPainter`
  from a `SlotLook`, so the preview is what the bar looks like. Look has ready-made styles (`HotbarStyles`: every colour
  and the border at once, `HotbarBar.Style`), and the colours one by one under Custom colours, which makes the style
  Custom. Keys is a grid of the bar's places to click and press a key in, a Key size of its own (`KeyTextScale`, 0 following
  the Text size, `KeyScale`; the cooldown's seconds have a Number size under Cooldowns and uses, `CooldownTextScale`,
  150% to start, and Show Decimals, off to start), with quick sets for a row (1 to 0, Shift + 1
  to 0, F1 to F12). All slot text (hotkeys, counts, cooldown seconds, uses left) is
  `ItemIcons.DrawLabel`: ImGui stretches one small font texture, which blurs and dims bigger text, so each label is drawn
  by GDI+ (Segoe UI bold, white, black outline, cut close round its letters) into a texture at exactly its pixel size,
  tinted to its colour, and kept (400 at most); ImGui's text if one can't be made. Forms are two-column tables (`Form.BeginForm`, `Form.Label`), and rows of checkboxes and pickers wrap to the
  width (`Flow`) rather than running off the side.

- **Character tab** (`Ui/CharacterTab.cs`), and the **Character window** (`character`, hidden to start) that's the same in
  a `FloatingWindow`: first what the game's Character Information panel says, then everything the Combat Focus appraisal
  shows under Character Stats (gear mods, ratings, ward, armor level, resistances, jewel effects), from `@playerui
  character`, every two seconds while either is open. The Character Information part is the client's own, worked out in
  the plugin from Decal's `CharacterFilter` (`Birth`, `Age`, `Deaths`, `Attributes[...].Creation` and `.Base`,
  `EffectiveAttribute`, `BurdenUnits`, and `GetCharProperty` for any int property the player description carries:
  augmentations, chess rank, fishing, luminance augmentations) by `Feed/RetailCharacterInfo.cs`, in the panel's own
  sentences, taken from `client_local_English.dat`'s string table `0x23000001` (played time's units from `0x23000006`,
  365-day years and 30-day months as the server's `/age`). The resistance and regeneration words are the client's steps,
  which `Player.GetNaturalResistanceString` and `GetRegenBonusString` in daralet-ACE copy (unused there); the
  overburdened line is `EncumbranceSystem`'s sums. The panel's code isn't in the data, so the augmentations' order is the
  plugin's, and the Seer's auras (Destruction, Invulnerability, Retribution, Hardening), which share their properties with
  Nalicana's, aren't told apart: each luminance augmentation shows once, as Nalicana's. Server side, `Player.GetStatsSheetSections` (`WorldObjects/Player_StatsSheet.cs`) builds the sections and both the
  appraisal's text (`GetStatsSheetText`) and the reply (`PlayerUiCharacter`) come from it, so they can't disagree. Each
  line is split at its first ": " into stat and value (`StatsSectionInfo.Split`); in a wide window the sections go in two
  columns.

- **Quests tab** (`Ui/QuestsTab.cs`): your contracts, labelled as quests, from `@playerui quests` every five seconds while
  open. The client is only sent each contract's id, stage and timers (`GameEventSendClientContractTracker`); the server
  sends the rest of its portal.dat ContractTable entry too (`Entity/PlayerUi/PlayerUiQuestData.cs`): descriptions, the
  start and end NPCs, and their places and the quest area's, placed like buildings (`PlayerUiData.BuildingMapCoords`,
  `Town.IsOnMap` leaving out dungeons). Each place's Go makes it the World tab's destination (`WorldTab.GoTo`). Abandon
  (asked twice) is `@playerui abandon <contract id>`, the Contracts panel's own `HandleActionAbandonContract`, which
  keeps the starter contracts until they're done (`Player.CanAbandonContract`). Stages are available, started (a
  progress counter too) and done. The **Quests window** (`quests`, hidden to start) is the same list in a
  `FloatingWindow`, sharing the tab's search and picked quest.

- **Toolbar buttons** (`Ui/ToolbarButtons.cs`, `Ui/ToolbarButton.cs`, `Ui/ClientElements.cs`): the game toolbar's
  backpack, Contracts (quill) and Map (compass) buttons, and the Character Information button in the indicators beside
  it (the burden pack), drawn again over the game's own, opening the Inventory, Quests, Map and Character windows instead
  of the game's panels; each on or off by itself, together in the Config tab's **Client UI Override** section
  (`ClientUiOverride`, with Bank window and Loot window; `ToolbarButtons.RenderToggles`; `SettingsData.ToolbarInventory`,
  `ToolbarQuests`, `ToolbarMap`, `ToolbarCharacter`, off to start), all six in one row under **Enable all** and
  **Disable all**. Enable all asks first when it would turn Bank window or Loot window on (`Windows.ConfirmAllOn`), as
  their own checkboxes do, and leaves them off on a server too old for them; Disable all says what follows when either
  was on (`Windows.NoticeAllOff`). The rest is the game's.
  The burden pack is element `0x100000F7` in the indicators' layout `0x21000071` (INDI `0x10000611`, the panel it's
  checked against instead of TBAR), its pictures by burden `060074A2` / `060074A3` / `060074A4` (states E, F, 0x10), with
  `0600749B` framed over it while its panel's open (its child `0x100000F2`'s state 3). The game's cut-offs aren't in the
  data: the plugin goes yellow over 100% and red at 200%, where `GetBurdenMod` is 0 (`RetailCharacterInfo.BurdenLevel`,
  from Decal's `Burden`, a percent). A button's pictures are drawn in layers, chosen each frame (`ToolbarButton.Pictures`).
  Each is a hud of its own with no padding, border or background, placed every frame exactly on the game's button,
  which `ClientElements.TryGetScreenBox` finds by element id (`UIElementManager::GetElement`), with its screen box from
  `UIRegion::GetScreenX0` and friends, and shown only while it and the toolbar (TBAR) are visible. Those are the client's
  own functions, called at the addresses AcClient gives them with plain `void*`s, never through AcClient's types: they
  aren't blittable, so .NET throws on a call taking or returning one ("Pointers cannot reference marshaled structures",
  from `UIElement.GetChildRecursive`, every frame), and their fields' places in memory aren't promised. A call that fails
  is logged once and the lookups stop until the next start. `UIElementManager::s_pInstance` is read from its address
  each time: UtilityBelt.Service's copy is read once, when its type is first used. A click on an ImGui window doesn't reach the game, so the game's button
  underneath does nothing. The pictures (`toolbar-*.png` in `Resources/ui`, normal and `-open`, the game's state 1 and 6)
  are lit while the window's open, as the game lights a panel's button. An item dragged from the plugin's lists onto the
  backpack goes in the main pack; one dragged from the game's pack isn't covered (`GameDrag.Cover`), so the game hears it
  let go over its own backpack underneath. The game draws its drag icon under every ImGui window, so the buttons draw it
  again over themselves while it's held there (`GameDrag.DrawIcon`, as the hotbars do). The ids and pictures came from the toolbar's LayoutDesc, `0x21000016` in
  `client_local_English.dat` (inside `0x21000070`, the frame, inside `0x21000005`, the main UI, as TBAR `0x10000603`):
  backpack `0x100001B1` (`06004CF7`, open `06004CF8`), Contracts `0x1000055A` (`060069AE` / `060069AF`), Map `0x1000019A`
  (`06001116` / `06001118`). daralet-ACE's `libs/dat-loader` reads LayoutDescs (`LayoutDesc`, `ElementDesc`, `MediaDesc`)
  and exports textures (`Texture.GetBitmap`); it needs `ConfigManager.Initialize(new MasterConfiguration())`, code page
  1252 registered, and `DatManager.PortalDat` set (for `BaseProperty`), without `DatManager.Initialize`, which fails on
  Daralet's dats reading their iteration.

- **Arena and Leaderboards tabs** (`Ui/ArenaTab.cs`, `Ui/LeaderboardsTab.cs`, `Feed/ArenaCommands.cs`,
  `Feed/Leaderboards.cs`): the leaderboards are the website's (`leaderboards.js`), read from `/api/leaderboards/...` with
  one `LeaderboardRow` for every board. A new board is a row in `Leaderboards.All`. One `MarketApi` (one `HttpClient`) is
  shared by the Market, Arena and Leaderboards tabs and made by `PlayerHud`.

- **Market tab** (`Ui/MarketTab.cs`, `Feed/MarketSearch.cs`, `Feed/MarketApi.cs`): My market, and Search over HTTP. The
  search filters are a table (`MarketSearch.Filters`) copied from the website's `market.js`: when the website gains a filter,
  add a row. Percent filters send a fraction (15 → 0.15), "percent of one" ones a multiplier (15 → 1.15), tiers go up by one
  (the API counts from 1), and Salvage has no tier. `ApiRequest<T>` runs a request off the game thread; the newest one wins.
- **The website's icons** are at `https://play.daralet.ac/images/<iconHex>.png` (13,000 of them, `daralet-website/images`),
  named by the portal.dat icon id, which is what the market listings' `iconHex` and Decal's `LongValueKey.Icon` give.

- **Inventory tab** (`Ui/InventoryTab.cs`, `Ui/InventoryBrowser*.cs`, `Ui/BankCommandBar.cs`, `Feed/BankCommands.cs`): sub tabs Carried
  (your packs, anywhere) and Bank (the bank's packs, while it's open). The `/bank` buttons are a `BankCommandBar` above the
  items in each, and in the Bank window: one bar, shared, so its "what" box carries over. Deposit with the box empty
  is Deposit packs (your "deposit"-tagged packs), with words in it a deposit of those. A bank pack has a **Tags**
  drop-down of every `/bank` word, ticked when the pack has it (`BankCommands.HasTag` / `ToggleTag`, comma-separated
  groups), each tick sending `/bank inscribe` at once (the plugin keeps what it sent until the next bank reply). Groups are
  OR (a comma) and the words in one AND (a space): the list's **OR** makes each tick its own group (`ToggleTag`), its
  **AND** adds the word to a picked group, or a new one (`ToggleInGroup`), and ticks then show that group. And an
  **Icon** drop-down of every style and color `/bank icon` takes (`BankCommands.PackIcons`, copied from daralet-ACE's
  `Entity/PackIcons.cs`: pouches have no white), plus the pack's own icon back; the bank's views add Withdraw, Sort
  and Search, and Carried has its own Sort, the server's `/sort` of your packs (`Commands/PlayerCommands/Sort.cs`, no bank
  needed, no words); the rest are under More. The last command's first reply
  line shows under the buttons, and hovering it shows the whole reply. Carried and Bank are each a `InventoryBrowser`
  (`carried: true` for yours), with their own selected pack and search. **Show my packs** on the Bank view
  (`SettingsData.BankShowCarried`) lists your packs there too, above the bank's, to drag between them. **Bank window**
  (`SettingsData.BankWindow`, switched under the Config tab's Client UI Override) hides the game's bank window (see
  above) and opens the plugin's own, a `FloatingWindow` you
  size yourself (`defaultSize`, its size saved like its position), with its own `InventoryBrowser`. It opens and closes with the
  bank, which `InventoryTab` follows every frame, so it works with the main window closed. The **Inventory window** (controls
  on the Carried tab, window id `carried`, hidden by default) is Carried in a `FloatingWindow` with its own `InventoryBrowser`
  (`retailAllowed: true`). Its **Retail** box (`SettingsData.CarriedRetail`, on by default) lays it out like the game's inventory
  (`InventoryBrowser.RenderRetail`): the paperdoll at the top left (shown whatever Worn says) and the packs beside it on the
  right, the two as tall as each other so their bottoms line up: the row is the pack column's height (from last frame, `packsHeight`), and the paperdoll closes up the space under its trinket and sigils' row to fit (`RenderPaperdoll(sigilGap)`, the empty row 1 and its gaps in the game's layout), and
  the chosen pack's items as the grid across the whole width below. The packs are icons only, each with a fill bar down its right side, over the icon, filling from the bottom (`LongValueKey.ItemSlots`; 102 for the main
  pack when the client isn't told) and its name on hover, then an empty square for each side pack slot left (`LongValueKey.PackSlots`, 7 when the client isn't told; drop a pack on one to put it there), with a burden bar down its right (`CharacterFilter.Burden`; hover it for the percent and `BurdenUnits`, full at 300%, the most you can carry, and yellow past 100%, with faint lines at 100% and 200%). The pop-out's **Reset width** makes it just wide enough, in Retail, for the paperdoll (with a little room) and the packs beside it, leaving the grid a little over seven columns (`RetailWidthShort`); in the plain view, for six columns of items (`InventoryBrowser.RetailColumns`, `GridWidthShort`). `FloatingWindow.SetWidth` sizes the window. No `/bank` bar in Retail: its top line has Loot highlights and Sort (`/sort`) instead, and the chosen pack is framed with the selection frame and a light brown arrow on its left edge, pointing in (`DrawChosenPack`). Packs (not the main pack) are dragged onto another pack, in any pack list, to take its place (`MoveItem` to that pack's container and slot, `Holder.Parent`/`Slot`): packs can't go inside packs, so that's how they're reordered, or moved between yours and the bank's at a place.
  `PlayerFeed.SendCommand` keeps the System and Help chat (types 5 and 15, Decal's `e.Color`) that comes back within three
  seconds as the command's reply. The items show as a list or, with **Grid** (`SettingsData.InventoryGrid`), as icons only, as
  many to a row as fit, each with the list's hover, drag and right-click; a stack shows its size in the corner. A drag shows
  only the item's icon, centred on the mouse and drawn on the foreground draw list (no preview tooltip, which ImGui fades).
  Dropped on another item, it goes to that item's container and slot (`InventoryBrowser.MoveTo`, `MoveItem` without stacking,
  unless it's a stack of the same thing), which is how items are reordered within a pack. **Worn**
  (`SettingsData.InventoryShowWorn`) slides out a paperdoll of what you're wearing on the right (`InventoryBrowser.RenderWorn`), laid
  out as the game's equipment panel by `EquipSlots` (the game's EquipMask bits, one square per slot; an item covering
  several shows in each; Pants leave out AbdomenWear, which shirts have too; an empty one shows the game's own picture of it, `EquipSlot.EmptyIcon`, found in portal.dat by the
  bevelled blue frame they share and drawn as it is, `ItemIcons.DrawPlain`). The window grows by the panel's width when
  Worn is turned on and shrinks when it's off, once per window (`wornShownAs`, by host), and the Worn checkbox is above the
  packs, where it can't be squeezed out. Its items have the list's hover and right-click; dragging one onto a pack or onto
  the items takes it off into that pack, and dropping an item on a square wears it (`Actions.UseItem`, the game's own
  use).

- **Use** (`Ui/ItemUser.cs`, `Feed/UseRules.cs`): double-clicking an item you carry or wear in the Inventory tab's lists,
  or Use in its right-click menu, does what a double-click in the game's inventory does. Gear goes on with the game's own
  auto-wield (`Actions.AutoWield`), or comes off into the main pack. The server won't put anything over a taken slot
  ("You must remove your..."), and a plugin's AutoWield doesn't take anything off first the way the client's double-click
  does, so `ItemUser.Wield` takes off what's in the way (`UseRules.ToTakeOff`: armor and clothing sharing a layer of its
  coverage, Decal's `Coverage`, the server's ClothingPriority, since a shirt and pants share AbdomenWear without
  clashing; the left ring or bracelet when both are full, the main hand, the off hand for a two-hander, caster or bow, after the server's
  `CheckWeaponCollision`) one item at a time, 250ms apart, then wields; dropping onto a Worn square does the same. A ring
  or bracelet is wielded into one named place (`UseRules.PlaceFor`: the square dropped on, else a free one, left
  first), sent as the client's own `AcClient.CM_Inventory.Event_GetAndWieldItem(item, place)`: neither Decal's plain
  `AutoWield(id)` nor its explicit `AutoWield(id, place, 1, 0)` ever put one on the left wrist. A tool
  whose use flags have target bits (ACE's `Usable`, high 16 bits) and whose source bits (low 16) allow where it is (a
  caster's `SourceWieldedTargetRemote` only once wielded, so a caster in a pack is wielded) turns the mouse into a reticle (drawn on the foreground draw list: a plugin can't change the
  game's cursor), and the next item clicked in any list gets it (`Actions.ApplyItem`). Anything else is `UseItem`. A target
  must be in a place the flags allow (worn = Wielded, packs = Contained, the open bank = Viewed, the tool itself = ObjSelf)
  and of a kind its TargetType has (`TargetType & ItemType`, the server's own check in `HandleActionUseWithTarget`); the
  bar above the list adds "On yourself" (Self) and "On <selection>" (Remote or Viewed). Escape or right-click puts the
  tool down. Decal's names for these keys come from the network header flags: `LongValueKey.Unknown10` is the use flags
  (flag 0x10), `UsageMask` the TargetType, `Category` the ItemType. Each use logs them to log.txt (`[use]` lines).

- **Dragging into the world** (`Ui/WorldDrop.cs`): an item (or pack) let go outside every ImGui window is given to what it was let go over, or dropped on the ground, as the game's own pack does. A plugin can't ask what's under the mouse, so the selection is cleared and the game is posted a left click there (`PostMessage` to `Decal.Hwnd`): whatever it selects within 300ms gets the item (`Actions.GiveItem`); nothing, and it's dropped (`Actions.DropItem`). Yourself does nothing; the bank's items are left alone. `[drop]` lines in log.txt. The game's own panels aren't ImGui windows, so letting go over one counts as the world, except the vendor window: while a vendor is open (`Actions.VendorId`), an item let go over the panel along the bottom it's shown in (ENVP, `ClientElements.Environment`, which also shows chests and corpses) goes on the sell list (`Actions.VendorAddSellList`) instead of on the ground. While it's held there (`WorldDrop.OverVendor` decides both), `VendorDrop` does what the game does for one from its own pack: the window turns to its Selling tab and the sell list's slot under the mouse has the green ring. The game's own `gmVendorUI::UpdateDragOver` opens that tab only while it's dragging something of its own (`UIElementManager`'s drag element at `+0x31C`), with `gmVendorUI::OpenTab(0x100000BB)`, which hands the tab's id to the window's panel of tabs (`UIElement_Panel::OpenTab`, `0x46C110`); the plugin calls that on the panel itself (`ClientElements.OpenTab`), found by id, rather than reach the vendor window through an unchecked offset. A click posted on the tab instead reached ImGui too, which took its button-up for the item being let go, selling it the moment the tab switched. The vendor window's parts are its panel of tabs `0x100000B8` (element type 8, `UIElement_Panel`), the tabs Items, Buying and Selling (`0x100000B9`, `0x100000BA`, `0x100000BB`), their pages (`0x100000BC`, `0x100000C4`, `0x100000CD`: Selling is `0x100000CD`, which `UpdateDragOver` checks for in the panel's current page at `+0x6D8`), and each page's single row of 32-pixel slots (`0x100000BD`, `0x100000C5`, `0x100000CE`), all three rows in the same place. The client's functions were read with a throwaway disassembler (the Iced NuGet package) over `acclient.exe`, at the addresses AcClient's `Entrypoint` names. The vendor window's own layout is `0x21000012` (found by searching the layouts for gmVendorUI's element type `0x10000017`, which `gmVendorUI::GetUIElementType` returns), but the game never makes its root element, `0x100000B7`: `GetElement` finds nothing by that id.
- **Dragging in from the game** (`Ui/GameDrag.cs`, first version): a plugin can't see the game's drag, so it's inferred: the left button went down outside every ImGui window (ImGui counts no window hovered while a button held from outside is down), moved, and came up over one of ours, and the dragged thing is the game's selection. What can be dragged in is anything on the ground (`GameDraggedGroundItem`): let go over a spot (`InventoryBrowser.GameDropTarget`), a pack goes in a pack slot (or takes a pack's place), anything else in a pack or at a grid slot, by `MoveItem`; elsewhere it shows red. While it's held over our windows ImGui hovers nothing, so the drag is drawn by hand: the pack's icon under the mouse over an inventory window (`GameDrag.DrawIcon`; the game's own drag icon isn't drawn over ImGui windows), and the green ring or red circle over the spot it's over (`GameDrag.OverLastItem`, the mouse against the spot's square). Each drop spot's verdict is logged when it changes (`[drop]` lines, `LogDropMark`). `[drag-in]` lines in log.txt say what was seen.
- **Item icons** (`Ui/ItemIcons.cs`, `Feed/IconLayers.cs`): every item list (bank, Loot tab, Corpse window, Market) draws
  an item's underlay, icon and overlay before its name. UtilityBelt.Service's only icon loader is meant for its scripts
  (`ScriptHudManager.GetIconTexture`, which reads portal.dat), but a `ScriptHudManager` made with `new` works: its
  constructor only hooks the UB bar, and its `Dispose` frees every texture it made. At most 8 new textures a frame, so
  opening a full bank doesn't stall the game. Items the client has come from Decal (`LongValueKey.Icon`, `IconOverlay`,
  `IconUnderlay`); listings from the Market API's hex ids, and your own listings, sales and expired items from the
  `icon` `@playerui market` sends (read from each listing's snapshot by the Market API's `Describe`). Underlay tiles by
  kind, and the outline colour by effect (white outline pixels recoloured, black for none), are the website's
  (`IconLayers.Underlays`, `OutlineColors`, from market.js). Hovering an icon shows the item's tooltip. **Compact** on the
  window's top line (`SettingsData.CompactLists`) hides them; a loot mark still shows, on its own.

- **World tab** (`Ui/WorldTab.cs`, `Map/`): the map (Daralet Admin's `dereth-map.png`), the places in
  `Resources/locations.xml` (a `locations.xml` next to the dll wins, then a downloaded one: see World data), you, your
  fellows, and a destination arrow window. The map image is one `MapImage` both maps share, loaded from login
  (`WorldTab.Core_RenderFrame`) whether or not a map is open: decoded and cut into 16 tiles off the game's thread, one
  tile a frame made a texture on it, since one 2048-pixel texture stalled the game.
  The file also has 43 `_Unknown` entries (shops, taverns, halls): every type needs a row in `Locations.Types`, which a
  test checks.

- **Appraisal** (`Ui/Appraisal.cs`, `Ui/AppraisalWindow.cs`, `Feed/ItemCompare.cs`): Appraise and Appraise in new window
  in an item's right-click menu in the Corpse window or the bank (`Appraisal.MenuItems`; nothing happens until one is
  picked). Appraise reuses the first window, a new window opens another (up to 6, titles numbered, since UtilityBelt knows
  a window by its title). Each compares the item (only one with `EquipableSlots`: the section is left out for anything
  else) with what you're wearing where it goes (`AppraisalWindow.Equipped`:
  `EquipableSlots` against worn `EquippedSlots`, any weapon for a weapon), with `ItemCompare` saying which way each stat is
  better; ` describe` covers equipped items for this. A
  plugin can't open the game's appraisal panel: Decal's `Actions.RequestId` only sends the request, and the client shows
  only appraisals it started itself. So with **Plugin appraisal** (`SettingsData.AppraiseInPlugin`, on by default, on the
  window's top line) the plugin's Appraisal window shows it: the icon a size up with the loot rule's mark, the name, kind
  and tier, what your loot rules make of it, and `ItemLines` with a bar for each roll's quality. A corpse item uses the
  corpse's description; a carried or banked one is described on request. An item you can't wear or wield, or a trinket
  or sigil, also shows its Use text and long description, here and in the bank's and the Loot tab's tooltips
  (`Ui/ItemUse.cs`: `StringValueKey.UsageInstructions`, `FullDescription`). The server's description doesn't carry them:
  they reach Decal only with an appraisal, so the first look asks for one (`Actions.RequestId`, once an item, unless
  `HasIdData`), and the game's panel stays shut. Market search tooltips have none: those items aren't in Decal. Off, right-click just selects the item, for the
  game's appraise key.

- **Dungeon window** (`Ui/DungeonWindow.cs`, `Map/Dungeon*.cs`, `dungeon`, hidden to start, its switch beside the
  World tab's Show... and its settings under the tab's Settings): a map of the landblock you're in while indoors, drawn
  from the dats, with no help from the server. A landblock's `LandblockInfo` (`0xXXYYFFFE` in the cell dat) says how many
  cells it has; each `EnvCell` (`0xXXYY0100` on) names its piece (an `Environment`, `0x0D000000 |` its id, in the portal
  dat, and which `CellStruct`) and where it goes (`Position`, in landblock meters, the same frame as Decal's
  `LocationX/Y/Z`); the piece has the polygons (`PhysicsPolygons`, or the drawn ones when it has none). `DungeonDat`
  reads them; `DungeonLayout` keeps the walkable ones (normal Z at least 0.66, the game's FloorZ; under 0.97 a ramp), and
  the walls are the floors' outline, edges no other floor shares (matched to 5cm, height included), so doorways stay
  open: the game's wall polygons were never needed. `DungeonLoader` reads 60 cells a frame on the game's thread (the dat
  readers aren't safe on another, and UtilityBelt uses them on this one; 750 cells take about 0.1 seconds) and keeps the
  last six landblocks. It uses `UBService.CellDat` and `PortalDat`, opening the cell dat itself off the game's thread if
  UtilityBelt hasn't (about a second and 55 MB) and handing it over, as `EnsurePortalDat` does. `DungeonView` draws each
  floor from yours (`DungeonGeometry.FloorFrom`, 6 meters a floor, from your `LocationZ`): four below darker the deeper,
  one above as a faint fill and outline, yours last and brightest, with anti-aliased fill off for the floors (seams).
  **Turn with me** turns it by your heading, with a compass needle; **Follow me**, dragging turning it off, as the maps.
  The window's Only when needed shows it only indoors. Inside a building it maps every building's rooms in the landblock.
  **Rooms you've been in** are grey (`DungeonVisits`, in settings.json as `dungeonVisits`: by character name, then
  landblock as four hex digits, then cell numbers), noted from every frame's `Landcell` whether the window's open or not,
  in a colour of your own under Show... (`DungeonVisitedColor`; ramps lean 30% towards the ramp colour, lower floors
  darker).
  **Markers** (`DungeonMarker`, `DungeonView.MarkerKinds`, each kind shown or hidden by name in `DungeonMarkers`) are
  Decal's `WorldFilter.GetLandscape()`, four times a second, by `ObjectClass` (your corpse by its name, "Corpse of" you):
  where each is is `RawCoordinates()`, landblock meters like `LocationX/Y/Z`, and its cell `LongValueKey.Landblock`, or
  the floor under it (`DungeonLayout.CellAt`) when Decal doesn't say. Only those on your floor, in your room or one
  you've been in, are kept (`DungeonMarker.MayShow`): the server sends what's in every room in sight of yours, through
  walls and floors, and the map is meant to show what you'd have seen, not be a radar (the user's call, October 2026).
  Way out and the route only use those too. Anything truly secret needs the server's own hiding, `PropertyBool.Visibility`
  with `ResistPerception` and a `Tier`: such an object is sent only to a player who has passed a Perception roll for it
  (`WorldObject_Awareness.cs` in daralet-ACE), so no plugin sees it before then. The first five of each dungeon go in
  log.txt. Clicking one selects it. **The way** (`DungeonRoute`, Dijkstra over the rooms, each entered at the doorway it was reached
  by) goes room to room through the doorways, which are each `EnvCell`'s `CellPortals` (the portal polygon's middle, at
  its foot; `OtherCellId` 0xFFFF leads outside and is left out). Way out is the nearest portal by the route: the client
  can't tell which portal leads out. Right-click for the way to a marker (followed as it moves) or a spot. It's worked
  out again in each new room, or twice a second. While the game runs, a dat it has open can't be opened by anyone else
  (a sharing violation), which is likely what leaves UtilityBelt without a dat in ShadowLauncher's folders: the
  plugin's own try then fails the same way, and says so.
  UtilityBelt's own dungeon maps paste one of 614 hand-drawn 10-meter tiles per piece instead, so pieces without a tile
  (Daralet's own) and cells off its grid go missing; this draws whatever the dats have.
- **Floating windows** (`Ui/FloatingWindow.cs`): small auto-sized UB huds that stay on screen (fellowship vitals, target,
  tracked target). Locked means no title bar and no dragging; right-click one to lock, unlock or hide it. Two things decide
  whether one is drawn, worked out every frame (`Core_RenderFrame`): `Visible`, the saved setting its tab's checkbox and
  Hide change, and `inUse`, what it's showing now (null for nothing). A window with **Only when needed**
  (`WindowPlacement.OnlyInUse`) is drawn only while in use; the Corpse and Bank windows always are (`alwaysOnlyInUse`).
  Those two also have `onClose`: their close button (X), Close in the right-click menu (a locked window has no X) and the Bank window's Close bank close
  the corpse or bank (a Use of it), and the window stays away until it shows something else (`closedFor`), without touching
  the setting. Nothing sets `Visible` behind the player's back any more, so Hide holds; Track and a new destination still
  switch their windows on, as asked for. **Background** (`WindowPlacement.Background`, null for the old 0.55 locked / 0.85
  unlocked) fades each window's box; at 0% it's `NoBackground`, and the style's `WindowBorderSize` is zeroed for that
  window's Begin and put back in `Hud_OnRender` (or the next `Core_RenderFrame`), since UB begins the window between the two.
  Locking takes the title bar away, so `Locked` moves the window down by the title bar's height (measured while unlocked,
  `titleBarHeight`) and back up when unlocked: what's in it stays exactly where it was. `RememberPosition` skips a frame
  whose new position is still on its way (`positioned` false), or it would save the old one back.
  These windows have `NoFocusOnAppearing`; `BringToFront` focuses one once (Appraisal uses it, so a new appraisal isn't
  hidden behind the main window). `menuItems` adds to a window's right-click menu: the fellowship window's is the vitals of
  the fellow right-clicked (`SettingsData.FellowVitals`, by name, each vital null to follow the Stamina/Mana checkboxes).
  Vital and threat bars are never shorter than the font (`Widgets.Bar`), nor narrower than their numbers
  (`Widgets.VitalBarWidth`): a big font wrote each bar's numbers over the next. Clicking a threat row selects that
  creature or player. The **Map window** is the World tab's map in a `FloatingWindow` with its own `MapView` (its own pan,
  zoom and Follow me); the arrow window's **Details** box leaves the arrow alone.
- **The game's UI pictures** (`Resources/ui`, embedded as `DaraletPlayer.Resources.ui.<name>.png`, drawn by `ItemIcons.DrawArt`): taken from the client by hand, since they aren't item icons: the main pack (`main-pack`), the green behind each pack in the Retail column (`pack-background`, with the pack's icon on it without its usual tile), the ring over the slot an item is held over in a drag (`drop-ring`, 17x16, scaled to the slot's width; a frame over a list line), and the red crossed-out circle (`drop-invalid`, not the reticle's) where it can't go, letting go there doing nothing (`Accepted(allowed)`: a pack only in a pack slot, on a pack, the main pack or the bank's own slots, never among a pack's items; an item not in a full pack; a paperdoll square only for what its `EquipableSlots` include), the empty slot (`empty-slot`: a pack's room left in the grids, which show a pack as its every slot, `InventoryBrowser.Slots`, and the side pack slots left in the Retail column), the selection frame (`selected`: the selected item, as `Actions.CurrentSelection` says, in the grids, the paperdoll and the Retail pack column, a click in a list selecting it as the game's pack does), and the target reticles (`reticle`, `reticle-invalid`). The Retail chosen pack's arrow is still drawn with shapes: the game's own hasn't been found. The grids have no gap between squares, as the game's pack has none.
- **The installer** (`installer/DaraletToolkit.iss`, Inno Setup 6, built and released by `.github/workflows/release.yml` on a
  `v*` tag that must match the csproj's `<Version>`): copies `DaraletToolkit.dll` and writes Decal's entry itself, the values
  UtilityBelt's plugin template installer writes (`(Default)` name, `Enabled`, `Object`, `Assembly`, `Path`, and
  `Surrogate` = `{71A69713-6593-47EC-0002-0000000DECA1}`, Decal's .NET plugin host), as 32-bit `HKLM\SOFTWARE\Decal\...`
  (WOW6432Node). Its default folder is the `Path` of an entry already there, so it takes over a dll added by hand. It
  stops while `acclient.exe` or `DenAgent.exe` runs, warns (Yes goes on anyway) when .NET 4.8 or Decal 2.9.8 isn't found
  (Decal's version from `DenAgent.exe` in the `Agent` key's `AgentPath`), and runs `UtilityBelt.Service.Installer.exe`,
  taken from the UtilityBelt.Service NuGet package's `content/`, when Decal has no Services entry for its guid
  (`{8adc5729-db1a-4e28-9475-c4eafae1e6e7}`). Uninstalling removes the entry and offers to delete the data folder. Keep
  its `AppId`: it's how a new installer upgrades the old install. The build references Managed DirectX from `%WINDIR%`,
  which runners lack, so the workflow installs the DirectX June 2010 runtime first.
- **New versions** (`Feed/UpdateCheck.cs`): when the plugin starts it asks GitHub's API, once and off the game's thread,
  for `daralet-ac/daralet-toolkit`'s latest release (`releases/latest`, which never means a draft or a pre-release,
  so not `world-data`), and compares its tag with the assembly's version (both made four-part first: `Version` puts 0.5.0
  before 0.5.0.0). A newer one gets a line in chat once the character is logged in (`PluginCore.AnnounceUpdate`; a line
  added before login never shows), with a link to the release page, and an **Update** button on the window's top line that
  opens the same page. It only tells: the player runs the new installer. Unauthenticated, GitHub allows 60 requests an
  hour from one address. Chat links are UtilityBelt's kind: `<Tell:IIDString:id:openurl|url>text</Tell>` in
  `AddChatText`, whose click comes back as `ChatNameClicked` with that id (`PluginCore.ChatLinkId`, a fixed number).
  Anyone can put a link with that id in a tell, so a click only opens this plugin's release pages
  (`UpdateCheck.IsReleasePage`, which takes the repo's old name, `daralet-player-plugin`, too). The repo was renamed
  with the plugin, after v0.6.0: older plugins still ask the old address, which GitHub redirects (the API too), and
  their Update link falls back to their own `LatestPageUrl`, since a release page under the new name isn't one of theirs. No Decal plugin known (October 2026) downloads and installs its own updates; UtilityBelt's
  is a notice like this one, and the loader updating the plugin itself was left for later.
- **Everything the plugin writes** goes in `Documents\Decal Plugins\Daralet Toolkit` (`DataFolder`): `settings.json`,
  `profiles.json`, `Settings Profiles`, `Settings Exports`, `log.txt`, `Loot Profiles`, `world-data`. It was `Decal Plugins\Daralet` before the rename: `PluginCore.Startup` moves
  that folder over first thing (`DataFolder.MoveOldFolder`, `FolderMove.Once`, copying instead if a file's held open),
  unless the new one is already there. Only startup does it, so a test that asks `DataFolder` for a path doesn't move
  the developer's real folder. Only files a player drops in by hand (`dereth-map.png`, `locations.xml`) are looked for
  next to the dll (`PluginCore.AssemblyDirectory`).
- **Settings** (`Settings.cs`, what the file holds in `SettingsData.cs`, each window's in `WindowPlacement.cs`) live in
  `settings.json` there: window positions and visibility, and display options. A DataContract reader skips field initialisers, so new settings
  get their defaults in `SetDefaults`, which runs before a file is read as well. Saved two seconds after the last change.
  A renamed setting keeps its old `DataMember` name, or players lose it: the Inventory tab was the Bank tab, so
  `InventoryGrid`, `InventoryShowWorn` and `InventoryLootMarks` are still `bankGrid`, `bankShowWorn` and `bankLootMarks`
  in the file.
- **Settings profiles** (`Settings.Profiles.cs`, `ProfileChoices.cs`, `Ui/ConfigTab.cs`, the **Config** tab, first):
  a profile is a whole `SettingsData`. Default is `settings.json` itself (so a plugin from before profiles still finds
  its settings), the rest `Settings Profiles\<name>.json`, named by file (`Settings.CleanName`), and which character uses
  which is `profiles.json` (`characters`, case-blind; one not there uses Default). The plugin starts on Default and
  swaps to the character's profile once logged in (`PluginCore.UseCharacterProfile`, also at a hot reload's start).
  A swap saves the old profile, replaces `Settings.Data` and raises `Settings.Swapped`: everything reads
  `settings.Data` afresh each frame, so only what keeps something from it listens: `FloatingWindow` (placed again from
  the new profile's placement, made hidden if it starts so), `Hotbars` (a window per bar, made again), `PlayerHud`
  (`InventoryBrowser.ForgetWornSizes`, `LootTab.SettingsSwapped`). Anything new that holds a piece of `Data` across
  frames must do the same. The rooms you've been in (`DungeonVisits`) aren't a setting: a swap adds them to the new
  profile's (`DungeonVisits.Merge`), and To file (`Settings.Export`, into `Settings Exports`) leaves them out. From
  file (`Settings.Import`) adds any settings file as a new profile named after the file.

`@playerui` only ever sends a player data about themselves, their fellowship and what they're fighting (for a monster
they've selected: its vitals, every buff and debuff on it, and its threat table). The one exception is `lfg`, whose lists
name the players and fellowships looking for group, with only what their own lines already told the LFG channel (the
user's call, October 2026). The server is the
only thing that can be trusted: never let the plugin's request decide what it's allowed to see. Anything that changes the
game (banking, listing, queueing) goes through the existing player commands, which check everything again; the plugin's own
checks (greying out market buttons away from a broker, say) are only there to be helpful.

## Conventions

- **License:** GPL-3.0 or later (`LICENSE`), with `NOTICE` adding a section 7 permission to combine it with Decal,
  UtilityBelt.Service, Managed DirectX and the client, and listing what isn't ours. Code copied in from elsewhere needs a
  GPL-compatible license (MIT, BSD, Apache 2.0, (A)GPL; daralet-ACE is AGPL-3.0, like ACE); anything else not written
  here goes on NOTICE's list. The installer installs `LICENSE`, `NOTICE` and UtilityBelt.Service's MIT notice.
- Braces on every `if`, even one-line bodies.
- One type per file. A big class is split into partial files by topic, named `Class.Topic.cs` (`InventoryBrowser.Retail.cs`,
  `LootTab.Corpse.cs`), so a file names what's in it; look for a class's other parts before adding to its main file.
  Fields go at the top of the class, and lines stay under about 125 characters: long tooltips are joined with `+`.
- Small shared pieces live in one place: `Widgets` (text, tips, `Button`, `OutlineIfHovered`, bars), `Colors`,
  `PluginCore.Chat` for a line in chat, `Words.Count` for "3 rules".
- **Settings pages** are built like the Hotbars page, from `Form` (`Ui/Form.cs`): a `Section` heading per group, forms
  of labels and lined-up controls (`BeginForm`, `Label`, `ControlWidth`), and rows of checkboxes and buttons that wrap
  (`Flow`, `FlowCheck`, `FlowSmallButton`). A tab whose content comes first keeps its settings folded under a
  **Settings** header (`Form.SettingsHeader`, closed to start), with a section per floating window
  (`FloatingWindow.RenderControls`: Enabled, Only when needed, Locked, Reset position, then Size and Background). A popup
  opened from inside a form must be drawn inside it too: a table adds to the ID stack.
  Sections go two abreast with `Form.Columns` (the World, Target and Inventory tabs' Settings) when the tab is wide enough, one under another
  when not; `Flow` and `ControlWidth` keep to the column they're in.
- Code comments explain why, in plain words; tooltips (`Widgets.Tip`) on every control say what it does and which command it
  sends.
- The user commits and pushes with GitHub Desktop: leave changes staged (`git add`), don't commit. New branches with
  `git switch -c <name> --no-track origin/main`.
