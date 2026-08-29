# World Evil & Ore Detection in tModLoader -- API Research

Research conducted 2026-04-01 by decompiling `tModLoader.dll` (ILSpy) and cross-referencing
official wiki/docs. All field names, types, and code patterns are verified against the decompiled
source. This document is intended as a reference for implementing world-evil-aware filtering
in the Progression Compass mod.

---

## Table of Contents

1. [Detecting World Evil](#1-detecting-world-evil)
2. [Drunk World / Both Evils](#2-drunk-world--both-evils)
3. [All Special World Seeds](#3-all-special-world-seeds)
4. [Pre-Hardmode Ore Detection](#4-pre-hardmode-ore-detection)
5. [Hardmode Ore Detection](#5-hardmode-ore-detection)
6. [TileID Reference for All Ores](#6-tileid-reference-for-all-ores)
7. [Recipe Corruption/Crimson Flags](#7-recipe-corruptioncrimson-flags)
8. [Condition Class -- World-Evil Conditions](#8-condition-class----world-evil-conditions)
9. [Vanilla Recipe Groups](#9-vanilla-recipe-groups)
10. [ItemID.Sets -- Shimmer & Evil Items](#10-itemidsets----shimmer--evil-items)
11. [NPCID.Sets -- Evil NPCs](#11-npcidsets----evil-npcs)
12. [Demonite & Crimtane Ore Generation](#12-demonite--crimtane-ore-generation)
13. [Implementation Recommendations](#13-implementation-recommendations)
14. [Research Methodology](#14-research-methodology)

---

## 1. Detecting World Evil

### Primary API

```csharp
// Terraria.WorldGen (static bool)
WorldGen.crimson
```

- `true` = Crimson world
- `false` = Corruption world

This is the single authoritative field. It is set during world generation and saved/loaded
with the world file. The `Condition` class wraps it:

```csharp
Condition.CrimsonWorld  // predicate: () => WorldGen.crimson
Condition.CorruptWorld  // predicate: () => !WorldGen.crimson
```

### Player Zone Detection (biome presence, not world evil)

```csharp
Main.LocalPlayer.ZoneCorrupt   // player is currently in Corruption biome
Main.LocalPlayer.ZoneCrimson   // player is currently in Crimson biome
```

These are **not** the same as world evil -- a player can be in a Crimson biome in a Corruption
world (e.g., artificially created, drunk world, etc.).

### WorldGen.WorldGenParam_Evil

```csharp
public static int WorldGenParam_Evil = -1;
```

Used during world creation to override evil selection:
- `-1` = random (50/50)
- `0` = force Corruption
- `1` = force Crimson

Not useful at runtime -- only during generation.

---

## 2. Drunk World / Both Evils

### Detection

```csharp
Main.drunkWorld          // bool -- true if world uses drunk seed (05162020)
WorldGen.drunkWorldGen   // bool -- true only DURING world generation with drunk seed
```

Use `Main.drunkWorld` at runtime, not `WorldGen.drunkWorldGen`.

### Mechanics

- **Both evils generate** on opposite sides of the world.
- **Both Demonite and Crimtane ore** spawn underground naturally.
- **Both Biome Chests** appear in the Dungeon.
- **Shadow Orbs and Crimson Hearts share a single counter.** The first of either is a guaranteed
  firearm; every third total spawns its respective boss.
- **Primary evil alternates daily at dawn (4:30 AM).** The evil selected at world creation is
  "main" on day 1. This affects:
  - NPC shop inventories
  - Biome Mimic spawning
  - Eye of Cthulhu drops
  - Wall of Flesh brick generation
  - Angler quest fish
- **`WorldGen.crimson` still has a value** -- it reflects the *currently active* primary evil,
  not a permanent choice. On a drunk world, this toggles each dawn.

### Ore Generation in Drunk Worlds

- **Pre-hardmode:** Both variants of each tier spawn (Copper AND Tin, Iron AND Lead, etc.).
- **Hardmode:** Destroying the first 3 altars generates one set of hardmode ores as normal.
  Destroying the next 3 generates the missing alternates. The `SmashAltar` code explicitly
  alternates `SavedOreTiers` values in drunk worlds.

---

## 3. All Special World Seeds

All fields decompiled from `Terraria.Main`:

| Property | Type | Seed | World Evil Impact |
|---|---|---|---|
| `Main.drunkWorld` | `bool` | 05162020 | Both evils, alternating primary |
| `Main.getGoodWorld` | `bool` | 05162011 (For the Worthy) | No evil change; difficulty scaling |
| `Main.tenthAnniversaryWorld` | `bool` | 05162021 (Celebrationmk10) | No evil change |
| `Main.dontStarveWorld` | `bool` | constant / The Constant | No evil change; Iron always Iron (not Lead), Gold always Gold (not Platinum) unless also drunk |
| `Main.notTheBeesWorld` | `bool` | not the bees | No evil change |
| `Main.remixWorld` | `bool` | don't dig up | No evil change |
| `Main.noTrapsWorld` | `bool` | no traps | No evil change |

**Zenith / "Get Fixed Boi"** is NOT a separate flag. It is detected as:

```csharp
// Condition.ZenithWorld predicate:
() => Main.remixWorld && Main.getGoodWorld
```

Zenith combines all seed features, so it **does** have both evils (inherits drunk world behavior).

Corresponding `WorldGen` fields exist for generation-time checks:
`WorldGen.drunkWorldGen`, `WorldGen.getGoodWorldGen`, `WorldGen.remixWorldGen`, etc.
These are only `true` during world generation -- use the `Main.*` properties at runtime.

---

## 4. Pre-Hardmode Ore Detection

### WorldGen.SavedOreTiers (pre-hardmode)

```csharp
// Terraria.WorldGen.SavedOreTiers (nested static class)
public static int Copper = 7;    // default TileID.Copper
public static int Iron = 6;      // default TileID.Iron
public static int Silver = 9;    // default TileID.Silver
public static int Gold = 8;      // default TileID.Gold
```

These are set during world generation. **During generation, they are first reset to -1**, then
assigned based on random rolls:

```csharp
// Decompiled from WorldGen (world generation code):
SavedOreTiers.Copper = -1;  // reset
SavedOreTiers.Iron = -1;
SavedOreTiers.Silver = -1;
SavedOreTiers.Gold = -1;

// Then defaults are set:
SavedOreTiers.Copper = 7;   // TileID.Copper
SavedOreTiers.Iron = 6;     // TileID.Iron
SavedOreTiers.Silver = 9;   // TileID.Silver
SavedOreTiers.Gold = 8;     // TileID.Gold

// Then 50/50 random rolls for alternates:
if (genRand.Next(2) == 0) SavedOreTiers.Copper = 166;  // TileID.Tin
if (genRand.Next(2) == 0) SavedOreTiers.Iron = 167;    // TileID.Lead
if (genRand.Next(2) == 0) SavedOreTiers.Silver = 168;  // TileID.Tungsten
if (genRand.Next(2) == 0) SavedOreTiers.Gold = 169;    // TileID.Platinum
```

**The Constant (dontStarveWorldGen) special case:** Iron and Gold are NOT randomized unless
also a drunk world:

```csharp
if ((!dontStarveWorldGen || drunkWorldGen) && genRand.Next(2) == 0)
{
    GenVars.iron = 167; // Lead
    SavedOreTiers.Iron = 167;
}
```

### How to Read at Runtime

```csharp
int copperOrTin = WorldGen.SavedOreTiers.Copper;   // 7 (Copper) or 166 (Tin)
int ironOrLead = WorldGen.SavedOreTiers.Iron;       // 6 (Iron) or 167 (Lead)
int silverOrTungsten = WorldGen.SavedOreTiers.Silver; // 9 (Silver) or 168 (Tungsten)
int goldOrPlatinum = WorldGen.SavedOreTiers.Gold;   // 8 (Gold) or 169 (Platinum)

bool worldHasTin = WorldGen.SavedOreTiers.Copper == 166;
bool worldHasLead = WorldGen.SavedOreTiers.Iron == 167;
```

**Important:** A value of `-1` means "not yet determined" (world not fully loaded or mid-generation).

### Ore Tier Pairs Summary

| Tier | Default (TileID) | Alternate (TileID) | SavedOreTiers Field |
|------|-------------------|---------------------|---------------------|
| 1 | Copper (7) | Tin (166) | `Copper` |
| 2 | Iron (6) | Lead (167) | `Iron` |
| 3 | Silver (9) | Tungsten (168) | `Silver` |
| 4 | Gold (8) | Platinum (169) | `Gold` |

---

## 5. Hardmode Ore Detection

### WorldGen.SavedOreTiers (hardmode)

```csharp
public static int Cobalt = 107;      // default TileID.Cobalt
public static int Mythril = 108;     // default TileID.Mythril
public static int Adamantite = 111;  // default TileID.Adamantite
```

These are set to **-1 during world generation** and remain -1 until the player smashes altars.
From the decompiled `SmashAltar` method:

```csharp
// First altar smash (altarCount % 3 == 0):
if (SavedOreTiers.Cobalt == -1) {
    SavedOreTiers.Cobalt = 107;                // Cobalt
    if (genRand.Next(2) == 0)
        SavedOreTiers.Cobalt = 221;            // Palladium
}

// Second altar smash (altarCount % 3 == 1):
if (SavedOreTiers.Mythril == -1) {
    SavedOreTiers.Mythril = 108;               // Mythril
    if (genRand.Next(2) == 0)
        SavedOreTiers.Mythril = 222;           // Orichalcum
}

// Third altar smash (altarCount % 3 == 2):
if (SavedOreTiers.Adamantite == -1) {
    SavedOreTiers.Adamantite = 111;            // Adamantite
    if (genRand.Next(2) == 0)
        SavedOreTiers.Adamantite = 223;        // Titanium
}
```

### Drunk World Hardmode Alternation

In drunk worlds, `SmashAltar` toggles the alternate ore each cycle:

```csharp
// Before processing, drunk worlds alternate:
if (Main.drunkWorld) {
    if (SavedOreTiers.Adamantite == 111)
        SavedOreTiers.Adamantite = 223;
    else if (SavedOreTiers.Adamantite == 223)
        SavedOreTiers.Adamantite = 111;
}
// Similar for Cobalt and Mythril within their respective cases
```

So smashing 6 altars in a drunk world generates all 6 hardmode ore types.

### How to Read at Runtime

```csharp
int cobaltOrPalladium = WorldGen.SavedOreTiers.Cobalt;     // 107, 221, or -1
int mythrilOrOrichalcum = WorldGen.SavedOreTiers.Mythril;  // 108, 222, or -1
int adamOrTitanium = WorldGen.SavedOreTiers.Adamantite;    // 111, 223, or -1

bool hardmodeOresAssigned = WorldGen.SavedOreTiers.Cobalt != -1;
bool worldHasPalladium = WorldGen.SavedOreTiers.Cobalt == 221;
```

### Hardmode Ore Tier Pairs

| Tier | Default (TileID) | Alternate (TileID) | SavedOreTiers Field |
|------|-------------------|---------------------|---------------------|
| 5 | Cobalt (107) | Palladium (221) | `Cobalt` |
| 6 | Mythril (108) | Orichalcum (222) | `Mythril` |
| 7 | Adamantite (111) | Titanium (223) | `Adamantite` |

---

## 6. TileID Reference for All Ores

From the tModLoader Vanilla Tile IDs wiki and decompiled source:

| Ore | TileID | Alternate | Alt TileID |
|-----|--------|-----------|------------|
| Copper | 7 | Tin | 166 |
| Iron | 6 | Lead | 167 |
| Silver | 9 | Tungsten | 168 |
| Gold | 8 | Platinum | 169 |
| Demonite | 22 | Crimtane | 204 |
| Cobalt | 107 | Palladium | 221 |
| Mythril | 108 | Orichalcum | 222 |
| Adamantite | 111 | Titanium | 223 |

Demonite/Crimtane are NOT in `SavedOreTiers` -- they are determined by `WorldGen.crimson`.

---

## 7. Recipe Corruption/Crimson Flags

### Internal Flags on Recipe

```csharp
// Terraria.Recipe (internal fields)
internal bool crimson;
internal bool corruption;
```

These flags are set on vanilla recipes during `SetupRecipes()`. For example, Night's Edge has
**two separate recipes** -- one with `corruption = true` (using Light's Bane) and one with
`crimson = true` (using Blood Butcherer):

```csharp
// Night's Edge (ItemID 273) -- Corruption variant
currentRecipe.createItem.SetDefaults(273);           // Night's Edge
currentRecipe.requiredItem[0].SetDefaults(46);       // Light's Bane
currentRecipe.requiredItem[1].SetDefaults(155);      // Muramasa
currentRecipe.requiredItem[2].SetDefaults(190);      // Blade of Grass
currentRecipe.requiredItem[3].SetDefaults(121);      // Fiery Greatsword (Volcano)
currentRecipe.requiredTile[0] = 26;                  // Demon Altar
currentRecipe.corruption = true;
AddRecipe();

// Night's Edge (ItemID 273) -- Crimson variant
currentRecipe.createItem.SetDefaults(273);           // Night's Edge
currentRecipe.requiredItem[0].SetDefaults(795);      // Blood Butcherer
currentRecipe.requiredItem[1].SetDefaults(155);      // Muramasa
currentRecipe.requiredItem[2].SetDefaults(190);      // Blade of Grass
currentRecipe.requiredItem[3].SetDefaults(121);      // Fiery Greatsword (Volcano)
currentRecipe.requiredTile[0] = 26;                  // Demon Altar
currentRecipe.crimson = true;
AddRecipe();
```

### How These Flags Are Used

**Crafting:** The `corruption`/`crimson` flags are NOT checked in `PlayerMeetsEnvironmentConditions`.
Both recipes are always available at the crafting station. The flags exist purely for
**shimmer decrafting** -- they tell the shimmer system which recipe to use for decrafting based
on world evil.

**Shimmer Decrafting Migration:** During `SetupRecipes`, tModLoader converts these flags into
proper `Condition` objects for the decraft system:

```csharp
// At end of each recipe in SetupRecipes:
ReplaceDecraftCondition(ref currentRecipe.crimson, Condition.CrimsonWorld);
ReplaceDecraftCondition(ref currentRecipe.corruption, Condition.CorruptWorld);

// Implementation:
static void ReplaceDecraftCondition(ref bool field, Condition cond) {
    if (field) {
        currentRecipe.AddDecraftCondition(cond);
    }
    field = false;  // flag is cleared after migration
}
```

### Key Insight for Our Mod

**Recipes with `corruption`/`crimson` flags are NOT filtered at craft time.** Both the Light's
Bane and Blood Butcherer variants of Night's Edge are always craftable. The flags only matter for
shimmer. This means:

- We should NOT use `recipe.corruption`/`recipe.crimson` to filter recipes (they are cleared
  to `false` after `ReplaceDecraftCondition` runs).
- Instead, we should look at `recipe.DecraftConditions` for `Condition.CrimsonWorld` or
  `Condition.CorruptWorld` if we want to know which recipe is the "native" one.
- Or better: check the recipe's ingredient items to determine if they are corruption/crimson
  specific items.

---

## 8. Condition Class -- World-Evil Conditions

All conditions decompiled from `Terraria.Condition`:

### World Evil

```csharp
Condition.CrimsonWorld       // () => WorldGen.crimson
Condition.CorruptWorld       // () => !WorldGen.crimson
Condition.InCorrupt          // () => Main.LocalPlayer.ZoneCorrupt
Condition.InCrimson          // () => Main.LocalPlayer.ZoneCrimson
Condition.InEvilBiome        // () => ZoneCrimson || ZoneCorrupt
Condition.NotInEvilBiome     // () => !ZoneCrimson && !ZoneCorrupt
```

### Special Seeds

```csharp
Condition.DrunkWorld             // () => Main.drunkWorld
Condition.NotDrunkWorld          // () => !Main.drunkWorld
Condition.ForTheWorthyWorld      // () => Main.getGoodWorld
Condition.NotForTheWorthy        // () => !Main.getGoodWorld
Condition.RemixWorld             // () => Main.remixWorld
Condition.NotRemixWorld          // () => !Main.remixWorld
Condition.NotTheBeesWorld        // () => Main.notTheBeesWorld
Condition.NotNotTheBeesWorld     // () => !Main.notTheBeesWorld
Condition.TenthAnniversaryWorld  // () => Main.tenthAnniversaryWorld
Condition.NotTenthAnniversaryWorld
Condition.DontStarveWorld        // () => Main.dontStarveWorld
Condition.NotDontStarveWorld
Condition.NoTrapsWorld           // () => Main.noTrapsWorld
Condition.NotNoTrapsWorld
Condition.ZenithWorld            // () => Main.remixWorld && Main.getGoodWorld
Condition.NotZenithWorld         // () => !ZenithWorld.IsMet()
```

### Hardmode / Progression

```csharp
Condition.Hardmode               // () => Main.hardMode
Condition.PreHardmode            // () => !Main.hardMode
Condition.SmashedShadowOrb       // () => WorldGen.shadowOrbSmashed
```

### Boss Conditions (evil-relevant subset)

```csharp
Condition.DownedEowOrBoc         // () => NPC.downedBoss2
Condition.DownedEaterOfWorlds    // () => NPC.downedBoss2 && !WorldGen.crimson
Condition.DownedBrainOfCthulhu   // () => NPC.downedBoss2 && WorldGen.crimson
```

Note the evil-aware boss conditions: `DownedEaterOfWorlds` is only true in a Corruption world
where boss 2 was defeated. `DownedEowOrBoc` is evil-agnostic.

---

## 9. Vanilla Recipe Groups

All vanilla recipe groups decompiled from `Recipe.SetupRecipeGroups()`:

| String Name | RecipeGroupID Field | Items |
|---|---|---|
| `"Birds"` | `RecipeGroupID.Birds` | 3 bird critter items |
| `"Scorpions"` | `RecipeGroupID.Scorpions` | ItemID 2157, 2156 |
| `"Squirrels"` | `RecipeGroupID.Squirrels` | ItemID 2018, 3563 |
| `"Bugs"` | `RecipeGroupID.Bugs` | 3 bug critter items |
| `"Ducks"` | `RecipeGroupID.Ducks` | ItemID 2123, 2122 |
| `"Butterflies"` | `RecipeGroupID.Butterflies` | 8 butterfly items |
| `"Fireflies"` | `RecipeGroupID.Fireflies` | ItemID 1992, 2004 |
| `"Snails"` | `RecipeGroupID.Snails` | ItemID 2006, 2007 |
| `"Dragonflies"` | `RecipeGroupID.Dragonflies` | 6 dragonfly items |
| `"Turtles"` | `RecipeGroupID.Turtles` | ItemID 4464, 4465 |
| `"Macaws"` | `RecipeGroupID.Macaws` | ItemID 5212, 5300 |
| `"Cockatiels"` | `RecipeGroupID.Cockatiels` | ItemID 5312, 5313 |
| `"Cloud Balloons"` | `RecipeGroupID.CloudBalloons` | ItemID 399, 1250 |
| `"Blizzard Balloons"` | `RecipeGroupID.BlizzardBalloons` | ItemID 1163, 1251 |
| `"Sandstorm Balloons"` | `RecipeGroupID.SandstormBalloons` | ItemID 983, 1252 |
| `"Guide to Critter Companionship"` | `RecipeGroupID.CritterGuides` | ItemID 4767, 5453 |
| `"Guide to Nature Preservation"` | `RecipeGroupID.NatureGuides` | ItemID 5309, 5454 |
| `"Fruit"` | `RecipeGroupID.Fruit` | 19 fruit items |
| `"Balloons"` | `RecipeGroupID.Balloons` | 3 balloon items |
| `"Wood"` | `RecipeGroupID.Wood` | 9 wood types |
| `"Sand"` | `RecipeGroupID.Sand` | 8 sand types |
| `"IronBar"` | `RecipeGroupID.IronBar` | Iron Bar (22), Lead Bar (704) |
| `"Fragment"` | `RecipeGroupID.Fragment` | 4 pillar fragments |
| `"PressurePlate"` | `RecipeGroupID.PressurePlate` | 8 pressure plate types |

**Key finding: There are NO vanilla recipe groups for corruption/crimson equivalents.**
No group for Shadow Scale/Tissue Sample, Rotten Chunk/Vertebra, Demonite Bar/Crimtane Bar,
Light's Bane/Blood Butcherer, etc. Vanilla handles these via **duplicate recipes** with
`corruption`/`crimson` flags, not recipe groups.

The `IronBar` group (Iron Bar + Lead Bar) is the only ore-equivalence group.

### Unused Groups

`RecipeGroupID.FishForDinner` (8) and `RecipeGroupID.GoldenCritter` (9) are defined but unused.

---

## 10. ItemID.Sets -- Shimmer & Evil Items

### ShimmerTransformToItem

```csharp
public static int[] ShimmerTransformToItem;  // int[], default -1
```

Maps item types to their shimmer transmutation result. Contains 488 entries for vanilla items
including corruption-to-crimson and crimson-to-corruption conversions.

The array is initialized from a compiled data blob (not human-readable in decompilation), but
the wiki documents the pairs. Corruption/Crimson shimmer pairs include items like:
- Ebonsand Block <-> Crimsand Block
- Corrupt Seeds <-> Crimson Seeds
- (and many more biome-specific materials)

### ShimmerCountsAsItem

```csharp
public static int[] ShimmerCountsAsItem;  // int[], default -1
```

For items that should be treated as another item for shimmer purposes.

### CraftingRecipeIndices

```csharp
public static int[][] CraftingRecipeIndices;
```

Maps item type to an array of recipe indices that produce that item. Useful for finding all
recipes that craft a given item (including corruption/crimson variants).

### No IsCraftedCrimson/IsCraftedCorruption

The old `ItemID.Sets.IsCraftedCrimson` and `ItemID.Sets.IsCraftedCorruption` were **removed**
in tModLoader 1.4.4 (PR #3193). They were replaced by the `Recipe.DecraftConditions` system
using `Condition.CrimsonWorld` and `Condition.CorruptWorld`.

---

## 11. NPCID.Sets -- Evil NPCs

**There are NO NPCID.Sets fields that categorize NPCs by corruption vs crimson.**

The decompiled `NPCID.Sets` class contains sets for behaviors (Zombies, DemonEyes, Skeletons),
spawning rules, and combat properties, but nothing that tags an NPC as belonging to a specific
world evil.

To determine if an NPC is corruption or crimson specific, you must use:
- Hardcoded lookup tables (map NPCID -> evil type)
- Bestiary filtering (NPCs have biome spawn info in the bestiary)
- NPC spawn pool checks (which biome zones trigger which spawns)

---

## 12. Demonite & Crimtane Ore Generation

### World Generation

Both Demonite and Crimtane ore **do spawn naturally** as mineable ore underground:
- **Demonite Ore** (TileID 22): Spawns in Corruption worlds, in small veins underground.
  Increased amounts within Corruption cave systems.
- **Crimtane Ore** (TileID 204): Spawns in Crimson worlds, in small veins underground.

In **drunk worlds**, both Demonite and Crimtane spawn naturally.

### Not in SavedOreTiers

Demonite/Crimtane are NOT tracked in `WorldGen.SavedOreTiers`. They are determined implicitly
by `WorldGen.crimson`. To check:

```csharp
bool hasDemonite = !WorldGen.crimson || Main.drunkWorld;
bool hasCrimtane = WorldGen.crimson || Main.drunkWorld;
```

### Primary Sources

Natural ore veins are rare. The primary source of both ores is **boss drops**:
- **Eater of Worlds**: Drops 20-395 Demonite Ore (100% rate)
- **Brain of Cthulhu**: Drops Crimtane Ore
- **Eye of Cthulhu**: Drops the ore matching world evil (Demonite in Corruption, Crimtane in Crimson)

### Mining Requirements

Below 0 ft elevation: requires Gold Pickaxe or better (55% pickaxe power).
Above 0 ft: any pickaxe works.

---

## 13. Implementation Recommendations

### For the Dependency Resolver

1. **World evil detection:** Use `WorldGen.crimson` as the primary check. Additionally check
   `Main.drunkWorld` to handle both-evils scenarios.

2. **Prioritize native evil items:** When resolving crafting paths, prefer the recipe variant
   that matches the world evil. For example, in a Corruption world, prefer the Light's Bane
   variant of Night's Edge over Blood Butcherer.

3. **Detect recipe evil affiliation:** Since `recipe.corruption`/`recipe.crimson` flags are
   cleared after `SetupRecipes`, check `recipe.DecraftConditions` for `Condition.CrimsonWorld`
   or `Condition.CorruptWorld`, or check the ingredient items directly.

4. **Ore filtering:** Use `WorldGen.SavedOreTiers` to determine which ores the world has.
   When showing material requirements, prefer the world's actual ore (e.g., show Lead Bar
   instead of Iron Bar if `SavedOreTiers.Iron == 167`).

5. **Hardmode ore caveat:** `SavedOreTiers.Cobalt/Mythril/Adamantite` are `-1` until altars
   are smashed. Handle this gracefully (show both possibilities, or show "unknown").

6. **Drunk world completeness:** In drunk worlds, all ore variants and both evil items are
   available. Don't filter anything out.

### Proposed Helper

```csharp
public static class WorldEvilHelper
{
    public static bool IsCorruptionWorld => !WorldGen.crimson;
    public static bool IsCrimsonWorld => WorldGen.crimson;
    public static bool HasBothEvils => Main.drunkWorld;

    // Pre-hardmode ores
    public static bool HasCopper => WorldGen.SavedOreTiers.Copper == 7;
    public static bool HasTin => WorldGen.SavedOreTiers.Copper == 166;
    public static bool HasIron => WorldGen.SavedOreTiers.Iron == 6;
    public static bool HasLead => WorldGen.SavedOreTiers.Iron == 167;
    public static bool HasSilver => WorldGen.SavedOreTiers.Silver == 9;
    public static bool HasTungsten => WorldGen.SavedOreTiers.Silver == 168;
    public static bool HasGold => WorldGen.SavedOreTiers.Gold == 8;
    public static bool HasPlatinum => WorldGen.SavedOreTiers.Gold == 169;

    // Hardmode ores (-1 means not yet determined)
    public static bool HasCobalt => WorldGen.SavedOreTiers.Cobalt == 107;
    public static bool HasPalladium => WorldGen.SavedOreTiers.Cobalt == 221;
    public static bool HasMythril => WorldGen.SavedOreTiers.Mythril == 108;
    public static bool HasOrichalcum => WorldGen.SavedOreTiers.Mythril == 222;
    public static bool HasAdamantite => WorldGen.SavedOreTiers.Adamantite == 111;
    public static bool HasTitanium => WorldGen.SavedOreTiers.Adamantite == 223;
    public static bool HardmodeOresKnown => WorldGen.SavedOreTiers.Cobalt != -1;

    // Evil ores
    public static bool HasDemonite => !WorldGen.crimson || Main.drunkWorld;
    public static bool HasCrimtane => WorldGen.crimson || Main.drunkWorld;
}
```

---

## 14. Research Methodology

This section documents the process used to obtain the information above, so it can be
repeated for future research tasks.

### Tools

- **ILSpy CLI (`ilspycmd`)**: Decompiles .NET assemblies to C# source.
  - Install: `dotnet tool install --global ilspycmd`
  - On macOS with Homebrew dotnet, requires: `export DOTNET_ROOT=/opt/homebrew/Cellar/dotnet@8/<version>/libexec`
  - Usage: `ilspycmd /path/to/tModLoader.dll -t "Terraria.ClassName" 2>&1`

- **Web search**: For wiki documentation, community patterns, and cross-referencing decompiled findings.

- **tModLoader docs**: https://docs.tmodloader.net/docs/stable/ and https://docs.tmodloader.net/docs/preview/

### Assembly Location

```
/Volumes/DevDrive1/SteamLibrary/steamapps/common/tModLoader/tModLoader.dll
```

### Decompilation Commands Used

```bash
# Set up environment
export DOTNET_ROOT=/opt/homebrew/Cellar/dotnet@8/8.0.125/libexec
export PATH="$PATH:/Users/noahlaratta/.dotnet/tools"

# Decompile a specific type
ilspycmd /path/to/tModLoader.dll -t "Terraria.WorldGen" 2>&1 | head -200

# Search for specific fields
ilspycmd /path/to/tModLoader.dll -t "Terraria.WorldGen" 2>&1 | grep -iE "(crimson|corrupt|evil|drunk|ore)"

# Get a nested type
ilspycmd /path/to/tModLoader.dll -t "Terraria.ID.ItemID+Sets" 2>&1 | grep -iE "(shimmer|corrupt|crimson)"

# Get the full Condition class
ilspycmd /path/to/tModLoader.dll -t "Terraria.Condition" 2>&1 | head -300

# Get recipe setup internals
ilspycmd /path/to/tModLoader.dll -t "Terraria.Recipe" 2>&1 | grep -A 20 "createItem.SetDefaults(273)"

# Find SmashAltar for hardmode ore logic
ilspycmd /path/to/tModLoader.dll -t "Terraria.WorldGen" 2>&1 | grep -A 120 "static.*void SmashAltar"

# List all static fields matching a pattern
ilspycmd /path/to/tModLoader.dll -t "Terraria.Main" 2>&1 | grep -iE "(drunk|getGood|crimson)" | grep "static"
```

### Key Types to Decompile

| Type | What It Contains |
|------|------------------|
| `Terraria.WorldGen` | World evil flag, SavedOreTiers, SmashAltar, world gen params |
| `Terraria.WorldGen+SavedOreTiers` | Pre-hardmode and hardmode ore tier fields |
| `Terraria.Main` | drunkWorld, getGoodWorld, hardMode, and all special seed flags |
| `Terraria.Condition` | All condition definitions (world evil, biomes, bosses, seeds) |
| `Terraria.Recipe` | Recipe flags (crimson, corruption), SetupRecipes, SetupRecipeGroups |
| `Terraria.ID.ItemID+Sets` | ShimmerTransformToItem, CraftingRecipeIndices |
| `Terraria.ID.NPCID+Sets` | NPC behavior sets (no evil-specific ones exist) |
| `Terraria.ID.RecipeGroupID` | All vanilla recipe group ID constants |
| `Terraria.RecipeGroup` | Recipe group registration and lookup |
| `Terraria.ID.TileID` | Tile type constants for ore identification |

### Web Resources

- Official Terraria Wiki: https://terraria.wiki.gg/wiki/
- tModLoader API Docs: https://docs.tmodloader.net/docs/stable/
- tModLoader GitHub: https://github.com/tModLoader/tModLoader
- tModLoader Wiki (recipes): https://github.com/tModLoader/tModLoader/wiki/Intermediate-Recipes
- tModLoader Wiki (conditions): https://github.com/tModLoader/tModLoader/wiki/Conditions
- tModLoader Wiki (tile IDs): https://github.com/tModLoader/tModLoader/wiki/Vanilla-Tile-IDs

### Verification Workflow

1. **Start with decompilation** for exact field names, types, and default values.
2. **Cross-reference with wiki** for gameplay behavior and edge cases.
3. **Check tModLoader GitHub issues/PRs** for API changes and deprecations.
4. **Search for patterns** in existing mod source code for community-validated approaches.
5. **Test assumptions** with the in-game `/cpo` commands against actual world state.

---

## Sources

- [tModLoader WorldGen Class Reference](https://docs.tmodloader.net/docs/preview/class_world_gen.html)
- [tModLoader WorldGen.SavedOreTiers Class Reference](https://docs.tmodloader.net/docs/stable/class_world_gen_1_1_saved_ore_tiers.html)
- [tModLoader Condition.cs Source (GitHub)](https://github.com/tModLoader/tModLoader/blob/1.4.4/patches/tModLoader/Terraria/Condition.cs)
- [tModLoader Conditions Wiki](https://github.com/tModLoader/tModLoader/wiki/Conditions)
- [tModLoader Intermediate Recipes Wiki](https://github.com/tModLoader/tModLoader/wiki/Intermediate-Recipes)
- [tModLoader RecipeGroupID Class Reference](https://docs.tmodloader.net/docs/preview/class_recipe_group_i_d.html)
- [tModLoader Vanilla Tile IDs Wiki](https://github.com/tModLoader/tModLoader/wiki/Vanilla-Tile-IDs)
- [tModLoader NPCID.Sets Class Reference](https://docs.tmodloader.net/docs/preview/class_n_p_c_i_d_1_1_sets.html)
- [tModLoader ItemID.Sets Class Reference](https://docs.tmodloader.net/docs/preview/class_item_i_d_1_1_sets.html)
- [Shimmer Condition PR #3193](https://github.com/tModLoader/tModLoader/pull/3193)
- [Shimmer Condition Issue #3090](https://github.com/tModLoader/tModLoader/issues/3090)
- [Terraria Ores Wiki](https://terraria.wiki.gg/wiki/Ores)
- [Terraria Demonite Ore Wiki](https://terraria.wiki.gg/wiki/Demonite_Ore)
- [Terraria Drunk World Wiki](https://terraria.wiki.gg/wiki/Drunk_world)
- [Terraria Special World Seeds Wiki](https://terraria.wiki.gg/wiki/Special_world_seeds)
- [Terraria Night's Edge Wiki](https://terraria.wiki.gg/wiki/Night%27s_Edge)
- [Terraria Shimmer Wiki](https://terraria.wiki.gg/wiki/Shimmer)
