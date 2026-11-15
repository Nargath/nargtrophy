# NargTrophy

**AI disclosure:** the code was written by Claude, Anthropic's AI. I'm not a programmer. The design, decisions, testing and balance are mine.

Inspired by Terraria's banner system.

Give your trophies a use. Collect trophies from one creature and craft its **trophy banner** or **war standard**. Players fighting near one deal **more damage to that creature** and **take less damage from it**.

## Features

- **A banner for every regular creature.** All 59 non-boss vanilla creatures with a trophy, from Boars to Ashlands and Deep North creatures. Bosses are left out because their trophies already have a job.
- **One creature, one banner.** A Greydwarf banner affects Greydwarfs only, not Brutes or Shamans. Starred creatures count as the same creature.
- **Two kinds of banner:**
  - **Trophy banner.** A building piece in its own **Trophy banners** hammer tab. It is always on, so it's good for base defence against raids.
  - **War standard.** A reusable item you plant at your feet, even mid-fight. It is stronger but lasts a limited time, then needs a cooldown before you can plant it again. Good for clearing villages, nests and dungeons.
- **Trophies are spent once.** You pay trophies when you build or craft a banner. War standards are never used up, and trophy banners need no upkeep.
- **Fair costs.** Costs are based on how easy a creature is to find and on its real trophy drop chance, so rare drops don't mean an endless grind. Drop-rate mods are taken into account.
- **Upgrades.** Both kinds have 3 upgrade levels that improve damage, damage reduction and radius. Upgrade a war standard at its crafting station like gear. Upgrade a trophy banner by hovering it and pressing **E**. Deconstructing refunds everything you spent.
- **No stacking.** If several banners for the same creature overlap, you get the strongest one, not the sum. Banners for different creatures each work as normal.
- **Clear feedback.** Each banner shows the creature's trophy, uses the biome's colours, and has a ward-style circle for its area. Your status effects show **Guarded** (trophy banner), **Rallied** (war standard) and **Replant** (cooldown).
- **Supports modded creatures.** Any creature from any mod can get a banner with one line of config. See [Modded creatures](#modded-creatures).
- **Multiplayer ready.** Banner values and gameplay settings come from the server.

## Default values

| | Trophy banner | War standard |
|---|---|---|
| Radius | 30 m | 12 m |
| Damage dealt | +15% | +30% |
| Damage taken | -15% | -25% |
| Duration | Always on | 5 minutes |
| Cooldown | - | 15 minutes, starting when you plant it |
| Per upgrade level | +5% dealt, -3% taken, +3 m | +5% dealt, -3% taken, +2 m |

Everything can be changed in `banners.yml`.

## Installation

Use a mod manager (r2modman, Thunderstore Mod Manager, Gale), or install by hand:

1. Install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/), [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/) and [YamlDotNet](https://thunderstore.io/c/valheim/p/ValheimModding/YamlDotNet/).
2. Put `NargTrophy.dll` in `BepInEx/plugins/NargTrophy/`.

**Multiplayer:** the server and every player need the mod.

## Configuration

The mod uses two config files, both in `BepInEx/config/`:

- **`NargTrophy/banners.yml`** holds every banner: costs, radius, bonuses, duration, cooldown, colours, upgrades. It is created the first time you start the game. When you save it, the new values load straight away. Adding or removing banners needs a restart.
- **`com.nargath.nargtrophy.cfg`** holds general settings:

| Setting | Default | |
|---|---|---|
| `Portable.MaxActivePerPlayer` | 0 | How many war standards one player can have planted at once. 0 = no limit. Server-synced. |
| `Portable.RespectWards` | true | War standards can't be planted inside a ward you don't have access to. Server-synced. |
| `Portable.PlantedBannerProtected` | true | Planted war standards can't be damaged or removed. Server-synced. |
| `Visuals.ShowRadius` | true | Show a banner's area while you hover it or place it. |
| `Debug.LogDamage` | false | Log every hit a banner changes. Useful for balancing. |

On a server, the server's banner files and settings are sent to every player when they join, and again whenever the files are saved.

## Modded creatures

Nothing is added for you automatically. You choose which modded creatures get a banner:

1. Install the creature mod and load into a world.
2. Open the console (F5) and run `nt_trophies`. It writes `suggested-banners.yml` to `BepInEx/config/NargTrophy/`, with a ready-to-paste line for every creature that drops a trophy but has no banner yet.
3. Create `banners.<ModName>.yml` next to `banners.yml`, for example:

   ```yaml
   requiresMod: <the mod's plugin GUID>   # optional: skip these banners when the mod isn't installed
   banners:
     - { id: Werewolf, creature: SomeMod_Werewolf, trophy: TrophyWerewolf, biome: BlackForest, availability: scarce }
   ```

4. Change `biome` and `availability` (`abundant`, `common`, `scarce`, `rare`) to suit the creature, then restart.
5. Give the same file to the server and every player.

You can find a mod's GUID in the BepInEx log, in the line `Loading [ModName 1.2.3] (<GUID>)`.

## Console commands

| Command | What it does |
|---|---|
| `nt_trophies` | Lists every creature that drops a trophy, its real drop chance and its banner, and writes the suggested lines for creatures without one. |
| `nt_standards` | Lists planted war standards and the time they have left. |
| `nt_remount` | Rebuilds the trophy models on banners from `banners.yml`. Useful for adjusting how a modded trophy sits on its banner. |

## Compatibility

- **Starred creatures** (including StarLevelSystem) use the same banner as the normal creature.
- **Drop-rate mods** such as DropNSpawn change trophy costs automatically, because costs come from the real drop chance.
- **Recipe mods** such as RecipeManager can override costs. The war standard recipes are called `Recipe_NT_Banner_<Id>` and the trophy banner pieces are called `piece_nt_banner_<id>`.
- **Damage mods** such as EpicLoot work alongside the banner bonus.

## Known limitations

- All values are a first balance pass and may change.
- Deep North banners cost trophies only for now, with no extra materials.
- Multiplayer is supported, but it hasn't been tested on a dedicated server yet. Please report any problems.

## License

[MIT](LICENSE)
