# Data Enrichment Research — tModLoader API Deep Dive

Research date: 2026-04-01
Assembly: tModLoader.dll (decompiled via ilspycmd 9.1.0)

---

## Topic 1: NPC Bestiary Spawn Conditions (Beyond Biome)

### What SpawnConditionBestiaryInfoElement Exposes

The class hierarchy is:

```
SpawnConditionBestiaryInfoElement
  : FilterProviderInfoElement (implements IFilterInfoProvider, IProvideSearchFilterString, IBestiaryInfoElement)
  : IBestiaryBackgroundImagePathAndColorProvider
  : IBestiaryPrioritizedElement
```

**Fields:**
- `string _key` — Localization key (e.g., `"Bestiary_Biomes.TheCorruption"`, `"Bestiary_Times.DayTime"`)
- `Point _filterIconFrame` — Icon position in the tag sprite sheet (16x5 grid)
- `string _backgroundImagePath` — Background image for bestiary portrait (nullable)
- `Color? _backgroundColor` — Tint color for bestiary portrait (nullable)
- `float OrderPriority` — Display ordering priority
- `int DisplayTextPriority` — Text display priority (lower = shown first)
- `bool HideInPortraitInfo` — Whether to hide in bestiary portrait (e.g., Blizzard overlay)

**Key method:** `GetDisplayNameKey()` returns the `_key` string, which is a **structured localization key**. The format is:
- `"Bestiary_Biomes.{BiomeName}"` — biome tags
- `"Bestiary_Times.{TimeOfDay}"` — time-of-day tags
- `"Bestiary_Events.{EventName}"` — weather/event tags
- `"Bestiary_Invasions.{InvasionName}"` — invasion tags

### Complete Spawn Condition Catalog

From `BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions`:

**Biomes (36 total):**
| Field Name | Key | Notes |
|---|---|---|
| Surface | Bestiary_Biomes.Surface | |
| Underground | Bestiary_Biomes.Underground | |
| Caverns | Bestiary_Biomes.Caverns | |
| TheUnderworld | Bestiary_Biomes.TheUnderworld | |
| Sky | Bestiary_Biomes.Sky | |
| Ocean | Bestiary_Biomes.Ocean | |
| Desert | Bestiary_Biomes.Desert | |
| UndergroundDesert | Bestiary_Biomes.UndergroundDesert | |
| Oasis | Bestiary_Biomes.Oasis | |
| Snow | Bestiary_Biomes.Snow | |
| UndergroundSnow | Bestiary_Biomes.UndergroundSnow | |
| Jungle | Bestiary_Biomes.Jungle | |
| UndergroundJungle | Bestiary_Biomes.UndergroundJungle | |
| TheHallow | Bestiary_Biomes.TheHallow | |
| UndergroundHallow | Bestiary_Biomes.UndergroundHallow | |
| TheCorruption | Bestiary_Biomes.TheCorruption | |
| UndergroundCorruption | Bestiary_Biomes.UndergroundCorruption | |
| TheCrimson | Bestiary_Biomes.Crimson | |
| UndergroundCrimson | Bestiary_Biomes.UndergroundCrimson | |
| TheDungeon | Bestiary_Biomes.TheDungeon | |
| Meteor | Bestiary_Biomes.Meteor | |
| Graveyard | Bestiary_Biomes.Graveyard | |
| UndergroundMushroom | Bestiary_Biomes.UndergroundMushroom | |
| SurfaceMushroom | Bestiary_Biomes.SurfaceMushroom | |
| SpiderNest | Bestiary_Biomes.SpiderNest | |
| TheTemple | Bestiary_Biomes.TheTemple | |
| Granite | Bestiary_Biomes.Granite | |
| Marble | Bestiary_Biomes.Marble | |
| CorruptDesert | Bestiary_Biomes.CorruptDesert | |
| CrimsonDesert | Bestiary_Biomes.CrimsonDesert | |
| HallowDesert | Bestiary_Biomes.HallowDesert | |
| CorruptUndergroundDesert | Bestiary_Biomes.CorruptUndergroundDesert | |
| CrimsonUndergroundDesert | Bestiary_Biomes.CrimsonUndergroundDesert | |
| HallowUndergroundDesert | Bestiary_Biomes.HallowUndergroundDesert | |
| CorruptIce | Bestiary_Biomes.CorruptIce | |
| CrimsonIce | Bestiary_Biomes.CrimsonIce | |
| HallowIce | Bestiary_Biomes.HallowIce | |
| NebulaPillar | Bestiary_Biomes.NebulaPillar | |
| SolarPillar | Bestiary_Biomes.SolarPillar | |
| VortexPillar | Bestiary_Biomes.VortexPillar | |
| StardustPillar | Bestiary_Biomes.StardustPillar | |

**Times (2):**
| Field Name | Key |
|---|---|
| DayTime | Bestiary_Times.DayTime |
| NightTime | Bestiary_Times.NightTime |

**Events (12):**
| Field Name | Key | Type |
|---|---|---|
| SlimeRain | Bestiary_Events.SlimeRain | SpawnConditionBestiaryInfoElement |
| WindyDay | Bestiary_Events.WindyDay | SpawnConditionBestiaryInfoElement |
| BloodMoon | Bestiary_Events.BloodMoon | SpawnConditionBestiaryInfoElement |
| Halloween | Bestiary_Events.Halloween | SpawnConditionBestiaryInfoElement |
| Rain | Bestiary_Events.Rain | SpawnConditionBestiaryOverlayInfoElement |
| Christmas | Bestiary_Events.Christmas | SpawnConditionBestiaryInfoElement |
| Eclipse | Bestiary_Events.Eclipse | SpawnConditionBestiaryInfoElement |
| Party | Bestiary_Events.Party | SpawnConditionBestiaryInfoElement |
| Blizzard | Bestiary_Events.Blizzard | SpawnConditionBestiaryOverlayInfoElement |
| Sandstorm | Bestiary_Events.Sandstorm | SpawnConditionBestiaryOverlayInfoElement |

**Invasions (7):**
| Field Name | Key |
|---|---|
| Goblins | Bestiary_Invasions.Goblins |
| Pirates | Bestiary_Invasions.Pirates |
| Martian | Bestiary_Invasions.Martian |
| OldOnesArmy | Bestiary_Invasions.OldOnesArmy |
| PumpkinMoon | Bestiary_Invasions.PumpkinMoon |
| FrostMoon | Bestiary_Invasions.FrostMoon |
| FrostLegion | Bestiary_Invasions.FrostLegion |

### How to Extract Structured Spawn Conditions at Runtime

```csharp
BestiaryEntry entry = Main.BestiaryDB.FindEntryByNPCID(npcType);
foreach (var info in entry.Info) {
    if (info is SpawnConditionBestiaryInfoElement spawnCond) {
        string key = spawnCond.GetDisplayNameKey();
        // Parse structured key: "Bestiary_Biomes.TheCorruption" -> category=Biomes, name=TheCorruption
        string[] parts = key.Split('.');
        string category = parts[0].Replace("Bestiary_", ""); // "Biomes", "Times", "Events", "Invasions"
        string conditionName = parts.Length > 1 ? parts[1] : "";
        string displayName = Language.GetTextValue(key);
    }
}
```

### Thread Safety
The bestiary database (`Main.BestiaryDB`) and entry Info lists are populated during loading and immutable at runtime. **Safe to read from background threads** after `PostSetupContent`.

### Practical Recommendations
- The key format (`Bestiary_Category.Name`) is reliable for parsing — it's been stable since 1.4.
- Use `GetDisplayNameKey()` for structured parsing, `Language.GetTextValue(key)` for display.
- There is NO depth/height data, NO hardmode flag, NO weather detail within the spawn condition itself — it's just the localization key + icon + background.
- Time-of-day tags (DayTime/NightTime) have `DisplayTextPriority = -1` and `OrderPriority = -1f`, making them sort before biome tags.
- Modded NPCs add their own `IBestiaryInfoElement` tags, which may include `ModBiomeBestiaryInfoElement` for modded biomes.

---

## Topic 2: Crafting Station Tile Mapping

### Tile Equivalence System (adjTile)

Terraria uses a `bool[] adjTile` array on `Player` (indexed by tile ID) to track which crafting stations are nearby. The system has **three layers** of equivalence:

#### Layer 1: Hardcoded Vanilla Equivalences (in Player.AdjTiles scan)

```csharp
switch (Main.tile[j, k].type) {
    case 77:   // Hellforge
    case 302:  // (Hellforge variant)
        adjTile[17] = true;  // Also counts as Furnace
        break;
    case 133:  // Adamantite/Titanium Forge
        adjTile[17] = true;  // Counts as Furnace
        adjTile[77] = true;  // Counts as Hellforge
        break;
    case 134:  // Mythril Anvil / Orichalcum Anvil
        adjTile[16] = true;  // Counts as Iron Anvil
        break;
    case 354:  // (Crystal Ball variant)
    case 469:  // (variant)
    case 487:  // (variant)
        adjTile[14] = true;  // Counts as Table
        break;
    case 355:  // Alchemy Table
        adjTile[13] = true;  // Counts as Placed Bottle
        adjTile[14] = true;  // Counts as Table
        // Also sets alchemyTable = true (33% chance to not consume ingredients)
        break;
}
```

**Key tile ID mapping:**
| Tile ID | Name | Counts As |
|---|---|---|
| 16 | Iron Anvil | (base anvil) |
| 17 | Furnace | (base furnace) |
| 77 | Hellforge | + Furnace (17) |
| 133 | Adamantite/Titanium Forge | + Furnace (17), Hellforge (77) |
| 134 | Mythril/Orichalcum Anvil | + Iron Anvil (16) |
| 355 | Alchemy Table | + Bottle (13), Table (14) |
| 354, 469, 487 | Table variants | + Table (14) |

#### Layer 2: ModTile.AdjTiles (via TileLoader.AdjTiles)

```csharp
// TileLoader.AdjTiles implementation:
public static void AdjTiles(Player player, int type) {
    ModTile tile = GetTile(type);
    if (tile != null) {
        foreach (int adjTileId in tile.AdjTiles) {
            player.adjTile[adjTileId] = true;
        }
    }
    // Also invokes GlobalTile.AdjTiles hooks
    foreach (var hook in HookAdjTiles) {
        foreach (int adjTileId in hook.Invoke(type)) {
            player.adjTile[adjTileId] = true;
        }
    }
}
```

Modded tiles declare their equivalences via `ModTile.AdjTiles` property (an `int[]`). This is the standard way modded crafting stations declare compatibility.

#### Layer 3: TileID.Sets for Room/Furniture

```csharp
TileID.Sets.RoomNeeds.CountsAsChair  // int[] — tile IDs that count as chairs for housing
TileID.Sets.RoomNeeds.CountsAsTable  // int[] — tile IDs that count as tables for housing
TileID.Sets.RoomNeeds.CountsAsTorch  // int[] — tile IDs that count as torches for housing
TileID.Sets.RoomNeeds.CountsAsDoor   // int[] — tile IDs that count as doors for housing
```

**Note:** These are for NPC housing requirements, NOT for crafting station equivalence. The crafting equivalences are handled exclusively by `adjTile`.

#### Liquid-Based Crafting

Also checked during the same scan:
```csharp
TileID.Sets.CountsAsWaterSource   // bool[] — tiles that provide water adjacency (includes tile 172)
TileID.Sets.CountsAsHoneySource   // bool[] — tiles providing honey
TileID.Sets.CountsAsLavaSource    // bool[] — tiles providing lava
TileID.Sets.CountsAsShimmerSource // bool[] — tiles providing shimmer
```

These are combined with liquid tile checks (`tile.liquid > 200`).

### Recipe Environment Requirements

Beyond tiles, recipes have boolean environment flags:
```csharp
Recipe.needWater          // Near water
Recipe.needHoney          // Near honey
Recipe.needLava           // Near lava
Recipe.needSnowBiome      // In snow biome (player.ZoneSnow)
Recipe.needGraveyardBiome // In graveyard (player.ZoneGraveyard)
Recipe.needEverythingSeed // Zenith seed world only
```

### Getting Tile Names

```csharp
// For crafting station tiles, use:
Lang.GetMapObjectName(MapHelper.TileToLookup(tileId, 0))
// This returns the display name from the map legend cache.
// Works for all vanilla tiles and modded tiles that register map entries.
```

### Thread Safety
`adjTile` is computed per-player each frame. The tile equivalence data (`ModTile.AdjTiles`, `TileID.Sets`) is set during load and **immutable at runtime — safe for background threads**. The actual `Player.adjTile[]` array changes every frame on the main thread.

### Practical Recommendations
1. To display what crafting station a recipe needs, iterate `recipe.requiredTile` and use `Lang.GetMapObjectName(MapHelper.TileToLookup(tileId, 0))` for the display name.
2. To build a complete equivalence map, combine the hardcoded switch cases above with `TileLoader.GetTile(type)?.AdjTiles` for modded tiles.
3. For our purposes, we should build a static `Dictionary<int, List<int>>` mapping tileId -> [equivalentTileIds] during PostAddRecipes to know which recipes become available when a player has access to a given crafting station.

---

## Topic 3: NPC Shop Price Calculation

### Price Pipeline (Full Computation)

The complete price calculation flows through several stages:

#### Stage 1: Base Store Value

```csharp
public int GetStoreValue() {
    if (shopCustomPrice.HasValue)
        return shopCustomPrice.Value;  // Custom price overrides everything
    return value;  // Item.value is the base (buyPrice/sellPrice sets this)
}
```

**Key fields:**
- `int Item.value` — Base value in copper coins. Buy price = value, sell = value/5.
- `int? Item.shopCustomPrice` — Override price, bypasses Item.value entirely.
- `int Item.shopSpecialCurrency` — If >= 0, uses a custom currency system (e.g., Defender Medals). Set to -1 by default.

#### Stage 2: Discount Card (20% off)

```csharp
if (discountAvailable) {
    if (!item.buyOnce)
        calcForBuying = (int)(calcForBuying * 0.8f);  // 20% discount
    if (item.isAShopItem)
        calcForSelling = calcForBuying;  // Selling price matches discounted buy
}
```

The `discountAvailable` flag is set when `discountEquipped` is true (from Discount Card, item type 854, or Greedy Ring, item type 3035).

**Items that set discount:**
| Item ID | Name | Sets |
|---|---|---|
| 854 | Discount Card | discountEquipped |
| 3035 | Greedy Ring | discountEquipped + goldRing + hasLuckyCoin |

#### Stage 3: NPC Happiness Modifier

```csharp
// For buyOnce items:
calcForBuying = (int)Math.Round(calcForBuying / PriceAdjustment);
// For normal items:
calcForBuying = (int)Math.Round(calcForBuying * PriceAdjustment);
```

**Happiness constants (from ShopHelper):**
```csharp
const float LowestPossiblePriceMultiplier = 0.75f;    // Best possible (happiest)
const float MaxHappinessAchievementPriceMultiplier = 0.82f;
const float HighestPossiblePriceMultiplier = 1.5f;     // Worst possible

// Relationship multipliers:
const float likeValue = 0.94f;     // 6% discount
const float dislikeValue = 1.06f;  // 6% markup
const float loveValue = 0.88f;     // 12% discount
const float hateValue = 1.12f;     // 12% markup
```

**Happiness factors:**
1. **Crowding**: >3 NPCs in house = `*= 1.05f` per extra NPC. >6 = "HateCrowded"
2. **Space**: <=2 in house, <4 in village = `*= 0.95f` (5% discount for liking space)
3. **NPC preferences**: love/like/dislike/hate specific NPCs nearby
4. **Biome preferences**: love/like/dislike/hate specific biomes
5. **Special NPCs**: Princess (type 663) hates being alone, Traveling Merchant/Skeleton Merchant use different formulas

**Special merchant pricing:**
- Skeleton Merchant: Moon phase multiplier (1.0 at new moon, up to 1.4 at full), +0.1 during day
- Traveling Merchant: Distance from spawn multiplier

#### Stage 4: Final Sell Value

```csharp
// buyOnce items: sell = buy / 5
// Normal items: sell = base / 5 (unaffected by happiness for selling)
```

### Accessing Price Data at Runtime

```csharp
// To get the effective buy price for an item in a shop:
int storeValue = item.GetStoreValue();
// Apply discount: storeValue * 0.8 if discount card
// Apply happiness: storeValue * player.currentShoppingSettings.PriceAdjustment
// Final = (int)Math.Round(discounted * happiness)
```

### Thread Safety
`Item.value` and `Item.shopCustomPrice` are set during `SetDefaults` and immutable for content sample items. **Safe to snapshot from background threads.** However, `Player.currentShoppingSettings` is computed per-frame on the main thread — snapshot before using.

### Practical Recommendations
1. For display, just show `Item.value` (or `shopCustomPrice`) as the base price. Don't try to compute happiness — it varies per player per NPC per location.
2. Use `Item.buyPrice()`/`Item.sellPrice()` helper methods for formatting. `value = buyPrice(0, 1, 50, 0)` means 1 gold 50 silver.
3. For special currencies (`shopSpecialCurrency >= 0`), use `CustomCurrencyManager.GetPrices()`.

---

## Topic 4: Item Stat Extraction (Structured)

### Complete Field List for Item Stats

**Combat Stats:**
| Field | Type | Description |
|---|---|---|
| `damage` | `int` | Base damage (-1 = no damage) |
| `DamageType` | `DamageClass` | Melee/Ranged/Magic/Summon/Throwing/Generic |
| `crit` | `int` | Bonus crit chance (added to base 4%) |
| `knockBack` | `float` | Knockback force (max 20) |
| `useTime` | `int` | Ticks between uses (lower = faster) |
| `useAnimation` | `int` | Animation duration in ticks |
| `useStyle` | `int` | ItemUseStyleID value |
| `shoot` | `int` | Projectile type spawned |
| `shootSpeed` | `float` | Projectile velocity |
| `mana` | `int` | Mana cost per use |
| `ArmorPenetration` | `int` | Defense points ignored |
| `autoReuse` | `bool` | Auto-swing/repeat |
| `channel` | `bool` | Held use (like Dirt Rod) |
| `useAmmo` | `int` | AmmoID consumed |
| `ammo` | `int` | AmmoID this item provides |

**Defense / Armor:**
| Field | Type | Description |
|---|---|---|
| `defense` | `int` | Defense value when equipped |
| `headSlot` | `int` | Helmet equip slot (-1 if not helmet) |
| `bodySlot` | `int` | Chestplate equip slot (-1 if not body) |
| `legSlot` | `int` | Legging equip slot (-1 if not legs) |
| `accessory` | `bool` | Is an accessory |
| `wingSlot` | `int` | Wing equip slot (-1 if not wings) |
| `shieldSlot` | `int` | Shield equip slot (-1 if not shield) |

**Tool Power:**
| Field | Type | Description |
|---|---|---|
| `pick` | `int` | Pickaxe power (%) |
| `axe` | `int` | Axe power (displayed as axe * 5 %) |
| `hammer` | `int` | Hammer power (%) |
| `tileBoost` | `int` | Extra tile range |

**Consumable / Utility:**
| Field | Type | Description |
|---|---|---|
| `healLife` | `int` | HP restored on use |
| `healMana` | `int` | Mana restored on use |
| `manaIncrease` | `int` | Max mana increase |
| `potion` | `bool` | Causes potion sickness |
| `consumable` | `bool` | Consumed on use |
| `bait` | `int` | Bait power |
| `fishingPole` | `int` | Fishing pole power |

**Placement:**
| Field | Type | Description |
|---|---|---|
| `createTile` | `int` | Tile placed (-1 = none) |
| `createWall` | `int` | Wall placed (-1 = none) |
| `placeStyle` | `int` | Tile style variant |

**Value / Rarity:**
| Field | Type | Description |
|---|---|---|
| `value` | `int` | Price in copper (buy = value, sell = value/5) |
| `rare` | `int` | Rarity tier (ItemRarityID) |
| `expert` | `bool` | Expert mode exclusive |
| `master` | `bool` | Master mode exclusive |
| `maxStack` | `int` | Max stack size |

**Important derived properties:**
```csharp
int OriginalDamage   // Damage before prefix modification
int OriginalDefense  // Defense before prefix modification
int OriginalRarity   // Rarity before prefix modification
bool IsACoin         // Whether it's a coin
bool IsAir           // Whether the item slot is empty
```

### Accessory Stat Bonuses

Accessory bonuses are **NOT stored as data fields**. They are applied imperatively in `UpdateAccessory()` hooks — each accessory modifies `Player` fields directly (e.g., `player.moveSpeed += 0.1f`). There is no structured way to extract "this accessory gives +10% movement speed" without reading the code.

**Exception:** Prefix bonuses ARE stored as tooltip data. Prefixes modify damage, speed, crit, defense, etc., and these show as tooltip lines.

### Tooltip Generation (ModifyTooltips Hook)

```csharp
public virtual void ModifyTooltips(List<TooltipLine> tooltips) { }
```

**Vanilla tooltip line names (in order):**
- `"ItemName"` — Item name
- `"Damage"` — "X melee/ranged/magic/summon damage"
- `"CritChance"` — "X% critical strike chance"
- `"Speed"` — "Insanely fast/Very fast/Fast/Average/Slow/Very slow/Extremely slow"
- `"Knockback"` — "No/Extremely weak/Very weak/Weak/Average/Strong/Very strong/Insanely strong knockback"
- `"FishingPower"` — "X% fishing power"
- `"BaitPower"` — "X% bait power"
- `"Defense"` — "+X defense"
- `"PickPower"` — "X% pickaxe power"
- `"AxePower"` — "X% axe power"
- `"HammerPower"` — "X% hammer power"
- `"HealLife"` — "Restores X life"
- `"HealMana"` — "Restores X mana"
- `"UseMana"` — "Uses X mana"
- `"Tooltip0"` through `"Tooltip#"` — Item description lines
- `"PrefixDamage"`, `"PrefixSpeed"`, `"PrefixCritChance"`, etc. — Prefix modifiers
- `"SetBonus"` — Armor set bonus description
- `"Material"` — "Material" tag
- `"Price"` — Buy/sell price

### Thread Safety
All `Item` fields are safe to read from `ContentSamples.ItemsByType[itemId]` on background threads — these are static instances populated during loading. **Never read from `Main.item[]` or player inventory on background threads.**

### Practical Recommendations
1. Use `ContentSamples.ItemsByType[type]` to read base stats for any item type.
2. For the display damage class: `item.DamageType.DisplayName` gives "melee", "ranged", etc.
3. `item.CountsAsClass<MeleeDamageClass>()` checks if an item's damage class inherits from melee.
4. Axe power display = `item.axe * 5` (stored value is 1/5 of displayed %).
5. For accessory effects, there's no data-driven extraction — you'd need to hook `ModifyTooltips` or parse tooltip text.

---

## Topic 5: Complete Condition Catalog

### All Static Condition Fields

Decompiled from `Terraria.Condition`. This is the **complete** list from the assembly.

#### Near* Conditions (4)

| Field Name | Localization Key | Predicate |
|---|---|---|
| `NearWater` | Conditions.NearWater | `player.adjWater \|\| player.adjTile[172]` |
| `NearLava` | Conditions.NearLava | `player.adjLava` |
| `NearHoney` | Conditions.NearHoney | `player.adjHoney` |
| `NearShimmer` | Conditions.NearShimmer | `player.adjShimmer` |

#### Time Conditions (2)

| Field Name | Localization Key | Predicate |
|---|---|---|
| `TimeDay` | Conditions.TimeDay | `Main.dayTime` |
| `TimeNight` | Conditions.TimeNight | `!Main.dayTime` |

#### In* Biome Conditions (31)

| Field Name | Localization Key | Predicate |
|---|---|---|
| `InDungeon` | Conditions.InDungeon | `player.ZoneDungeon` |
| `InCorrupt` | Conditions.InCorrupt | `player.ZoneCorrupt` |
| `InHallow` | Conditions.InHallow | `player.ZoneHallow` |
| `InMeteor` | Conditions.InMeteor | `player.ZoneMeteor` |
| `InJungle` | Conditions.InJungle | `player.ZoneJungle` |
| `InSnow` | Conditions.InSnow | `player.ZoneSnow` |
| `InCrimson` | Conditions.InCrimson | `player.ZoneCrimson` |
| `InWaterCandle` | Conditions.InWaterCandle | `player.ZoneWaterCandle` |
| `InPeaceCandle` | Conditions.InPeaceCandle | `player.ZonePeaceCandle` |
| `InTowerSolar` | Conditions.InTowerSolar | `player.ZoneTowerSolar` |
| `InTowerVortex` | Conditions.InTowerVortex | `player.ZoneTowerVortex` |
| `InTowerNebula` | Conditions.InTowerNebula | `player.ZoneTowerNebula` |
| `InTowerStardust` | Conditions.InTowerStardust | `player.ZoneTowerStardust` |
| `InDesert` | Conditions.InDesert | `player.ZoneDesert` |
| `InGlowshroom` | Conditions.InGlowshroom | `player.ZoneGlowshroom` |
| `InUndergroundDesert` | Conditions.InUndergroundDesert | `player.ZoneUndergroundDesert` |
| `InSkyHeight` | Conditions.InSkyHeight | `player.ZoneSkyHeight` |
| `InSpace` | (alias) | Same as `InSkyHeight` |
| `InOverworldHeight` | Conditions.InOverworldHeight | `player.ZoneOverworldHeight` |
| `InDirtLayerHeight` | Conditions.InDirtLayerHeight | `player.ZoneDirtLayerHeight` |
| `InRockLayerHeight` | Conditions.InRockLayerHeight | `player.ZoneRockLayerHeight` |
| `InUnderworldHeight` | Conditions.InUnderworldHeight | `player.ZoneUnderworldHeight` |
| `InUnderworld` | (alias) | Same as `InUnderworldHeight` |
| `InBeach` | Conditions.InBeach | `player.ZoneBeach` |
| `InRain` | Conditions.InRain | `player.ZoneRain` |
| `InSandstorm` | Conditions.InSandstorm | `player.ZoneSandstorm` |
| `InOldOneArmy` | Conditions.InOldOneArmy | `player.ZoneOldOneArmy` |
| `InGranite` | Conditions.InGranite | `player.ZoneGranite` |
| `InMarble` | Conditions.InMarble | `player.ZoneMarble` |
| `InHive` | Conditions.InHive | `player.ZoneHive` |
| `InGemCave` | Conditions.InGemCave | `player.ZoneGemCave` |
| `InLihzhardTemple` | Conditions.InLihzardTemple | `player.ZoneLihzhardTemple` |
| `InGraveyard` | Conditions.InGraveyard | `player.ZoneGraveyard` |
| `InAether` | Conditions.InAether | `player.ZoneShimmer` |
| `InShoppingZoneForest` | Conditions.InShoppingForest | `player.ShoppingZone_Forest` |
| `InBelowSurface` | Conditions.InBelowSurface | `player.ShoppingZone_BelowSurface` |

#### Composite / Negated Biome Conditions (4)

| Field Name | Predicate |
|---|---|
| `InEvilBiome` | `ZoneCrimson \|\| ZoneCorrupt` |
| `NotInEvilBiome` | `!ZoneCrimson && !ZoneCorrupt` |
| `NotInHallow` | `!ZoneHallow` |
| `NotInGraveyard` | `!ZoneGraveyard` |
| `NotInUnderworld` | `!ZoneUnderworldHeight` |

#### Game Mode Conditions (4)

| Field Name | Predicate |
|---|---|
| `InClassicMode` | `!Main.expertMode` |
| `InExpertMode` | `Main.expertMode` |
| `InMasterMode` | `Main.masterMode` |
| `InJourneyMode` | `Main.GameModeInfo.IsJourneyMode` |

#### Hardmode Conditions (2)

| Field Name | Predicate |
|---|---|
| `Hardmode` | `Main.hardMode` |
| `PreHardmode` | `!Main.hardMode` |

#### World Seed Conditions (16)

| Field Name | Predicate |
|---|---|
| `CrimsonWorld` | `WorldGen.crimson` |
| `CorruptWorld` | `!WorldGen.crimson` |
| `DrunkWorld` | `Main.drunkWorld` |
| `RemixWorld` | `Main.remixWorld` |
| `NotTheBeesWorld` | `Main.notTheBeesWorld` |
| `ForTheWorthyWorld` | `Main.getGoodWorld` |
| `TenthAnniversaryWorld` | `Main.tenthAnniversaryWorld` |
| `DontStarveWorld` | `Main.dontStarveWorld` |
| `NoTrapsWorld` | `Main.noTrapsWorld` |
| `ZenithWorld` | `Main.remixWorld && Main.getGoodWorld` |
| `NotDrunkWorld` | `!Main.drunkWorld` |
| `NotRemixWorld` | `!Main.remixWorld` |
| `NotNotTheBeesWorld` | `!Main.notTheBeesWorld` |
| `NotForTheWorthy` | `!Main.getGoodWorld` |
| `NotTenthAnniversaryWorld` | `!Main.tenthAnniversaryWorld` |
| `NotDontStarveWorld` | `!Main.dontStarveWorld` |
| `NotNoTrapsWorld` | `!Main.noTrapsWorld` |
| `NotZenithWorld` | `!ZenithWorld.IsMet()` |

#### Downed Boss Conditions (25 positive + 25 negated)

**Positive (boss defeated):**
| Field Name | Predicate |
|---|---|
| `DownedKingSlime` | `NPC.downedSlimeKing` |
| `DownedEyeOfCthulhu` | `NPC.downedBoss1` |
| `DownedEowOrBoc` | `NPC.downedBoss2` |
| `DownedEaterOfWorlds` | `NPC.downedBoss2 && !WorldGen.crimson` |
| `DownedBrainOfCthulhu` | `NPC.downedBoss2 && WorldGen.crimson` |
| `DownedQueenBee` | `NPC.downedQueenBee` |
| `DownedSkeletron` | `NPC.downedBoss3` |
| `DownedDeerclops` | `NPC.downedDeerclops` |
| `DownedQueenSlime` | `NPC.downedQueenSlime` |
| `DownedEarlygameBoss` | `Boss1 \|\| Boss2 \|\| Boss3 \|\| QueenBee \|\| hardMode` |
| `DownedMechBossAny` | `NPC.downedMechBossAny` |
| `DownedTwins` | `NPC.downedMechBoss2` |
| `DownedDestroyer` | `NPC.downedMechBoss1` |
| `DownedSkeletronPrime` | `NPC.downedMechBoss3` |
| `DownedMechBossAll` | `downedMechBoss1 && 2 && 3` |
| `DownedPlantera` | `NPC.downedPlantBoss` |
| `DownedEmpressOfLight` | `NPC.downedEmpressOfLight` |
| `DownedDukeFishron` | `NPC.downedFishron` |
| `DownedGolem` | `NPC.downedGolemBoss` |
| `DownedCultist` | `NPC.downedAncientCultist` |
| `DownedMoonLord` | `NPC.downedMoonlord` |
| `DownedMourningWood` | `NPC.downedHalloweenTree` |
| `DownedPumpking` | `NPC.downedHalloweenKing` |
| `DownedEverscream` | `NPC.downedChristmasTree` |
| `DownedSantaNK1` | `NPC.downedChristmasSantank` |
| `DownedIceQueen` | `NPC.downedChristmasIceQueen` |
| `DownedClown` | `NPC.downedClown` |
| `DownedGoblinArmy` | `NPC.downedGoblins` |
| `DownedPirates` | `NPC.downedPirates` |
| `DownedMartians` | `NPC.downedMartians` |
| `DownedFrostLegion` | `NPC.downedFrost` |
| `DownedSolarPillar` | `NPC.downedTowerSolar` |
| `DownedVortexPillar` | `NPC.downedTowerVortex` |
| `DownedNebulaPillar` | `NPC.downedTowerNebula` |
| `DownedStardustPillar` | `NPC.downedTowerStardust` |
| `DownedOldOnesArmyAny` | `DD2Event.DownedInvasionAnyDifficulty` |
| `DownedOldOnesArmyT1` | `DD2Event.DownedInvasionT1` |
| `DownedOldOnesArmyT2` | `DD2Event.DownedInvasionT2` |
| `DownedOldOnesArmyT3` | `DD2Event.DownedInvasionT3` |

**All "Not" variants exist** (e.g., `NotDownedKingSlime`, `NotDownedEyeOfCthulhu`, etc.)
**Note:** `NotDownedFrostLegin` — typo in vanilla (says "Legin" not "Legion")

#### Event / Weather Conditions (8)

| Field Name | Predicate |
|---|---|
| `Christmas` | `Main.xMas` |
| `Halloween` | `Main.halloween` |
| `BloodMoon` | `Main.bloodMoon` |
| `NotBloodMoon` | `!Main.bloodMoon` |
| `Eclipse` | `Main.eclipse` |
| `NotEclipse` | `!Main.eclipse` |
| `EclipseOrBloodMoon` | `Main.bloodMoon \|\| Main.eclipse` |
| `NotEclipseAndNotBloodMoon` | `!Main.bloodMoon && !Main.eclipse` |
| `Thunderstorm` | `Main.IsItStorming` |
| `BirthdayParty` | `BirthdayParty.PartyIsUp` |
| `LanternNight` | `LanternNight.LanternsUp` |
| `HappyWindyDay` | `Main.IsItAHappyWindyDay` |

#### Moon Phase Conditions (14)

| Field Name | Predicate |
|---|---|
| `MoonPhaseFull` | `GetMoonPhase() == MoonPhase.Full` |
| `MoonPhaseWaningGibbous` | `GetMoonPhase() == MoonPhase.ThreeQuartersAtLeft` |
| `MoonPhaseThirdQuarter` | `GetMoonPhase() == MoonPhase.HalfAtLeft` |
| `MoonPhaseWaningCrescent` | `GetMoonPhase() == MoonPhase.QuarterAtLeft` |
| `MoonPhaseNew` | `GetMoonPhase() == MoonPhase.Empty` |
| `MoonPhaseWaxingCrescent` | `GetMoonPhase() == MoonPhase.QuarterAtRight` |
| `MoonPhaseFirstQuarter` | `GetMoonPhase() == MoonPhase.HalfAtRight` |
| `MoonPhaseWaxingGibbous` | `GetMoonPhase() == MoonPhase.ThreeQuartersAtRight` |
| `MoonPhasesQuarter0-3` | `moonPhase / 2 == 0..3` |
| `MoonPhasesHalf0-1` | `moonPhase / 4 == 0..1` |
| `MoonPhasesEven` | `moonPhase % 2 == 0` |
| `MoonPhasesOdd` | `moonPhase % 2 == 1` |
| `MoonPhasesNearNew` | `moonPhase >= 3 && moonPhase <= 5` |
| `MoonPhasesEvenQuarters` | `moonPhase / 2 % 2 == 0` |
| `MoonPhasesOddQuarters` | `moonPhase / 2 % 2 == 1` |
| `MoonPhases04` | `moonPhase % 4 == 0` |
| `MoonPhases15` | `moonPhase % 4 == 1` |
| `MoonPhases26` | `moonPhase % 4 == 2` |
| `MoonPhases37` | `moonPhase % 4 == 3` |

#### Miscellaneous Conditions (7)

| Field Name | Predicate |
|---|---|
| `SmashedShadowOrb` | `WorldGen.shadowOrbSmashed` |
| `BloodMoonOrHardmode` | `Main.bloodMoon \|\| Main.hardMode` |
| `NightOrEclipse` | `!Main.dayTime \|\| Main.eclipse` |
| `Multiplayer` | `Main.netMode != 0` |
| `HappyEnough` | `PriceAdjustment <= 0.9` |
| `HappyEnoughToSellPylons` | `Main.remixWorld \|\| HappyEnough.IsMet()` |
| `AnotherTownNPCNearby` | 2+ NPCs near pylons |
| `IsNpcShimmered` | `TalkNPC?.IsShimmerVariant` |

#### Factory Methods (5)

```csharp
Condition.PlayerCarriesItem(int itemId)
Condition.GolfScoreOver(int score)
Condition.NpcIsPresent(int npcId)
Condition.AnglerQuestsFinishedOver(int quests)
Condition.BestiaryFilledPercent(int percent)
```

### Thread Safety
All `Condition` static fields reference `Func<bool>` predicates that read `Main.LocalPlayer` or game state. **NOT safe to call `.IsMet()` from background threads.** The `Description` (LocalizedText) is safe to read from any thread.

### Practical Recommendations
1. For our progression tracking, the `Downed*` conditions map directly to boss progression milestones.
2. Condition objects can be compared by reference since they're static fields: `recipe.Conditions.Contains(Condition.Hardmode)`.
3. For a condition catalog, iterate `typeof(Condition).GetFields(BindingFlags.Public | BindingFlags.Static)` to discover all conditions dynamically.
4. The `Description.Value` property gives the localized display text for any condition.

---

## Topic 6: Fishing Crate Loot and Catch Rates

### Crate Catch Rate System

From decompiled `Projectile.FishingCheck_RollDropLevels`:

```csharp
// Base formulas (fishingLevel = total fishing power):
int common    = 150 / fishingLevel;    // min 2
int uncommon  = 300 / fishingLevel;    // min 3
int rare      = 1050 / fishingLevel;   // min 4
int veryrare  = 2250 / fishingLevel;   // min 5
int legendary = 4500 / fishingLevel;   // min 6

// Crate chance is SEPARATE from rarity:
int crateChance = 10;  // 10% base
if (player.cratePotion) crateChance += 15;  // 25% with Crate Potion

// Each roll is independent:
crate     = Main.rand.Next(100) < crateChance;  // 10% or 25%
common    = Main.rand.Next(commonDenom) == 0;
uncommon  = Main.rand.Next(uncommonDenom) == 0;
// ... etc
```

**Key insight:** Crate chance (10%/25%) is a flat roll independent of fishing power. But if a crate IS rolled, what TYPE of crate depends on the rarity tiers (rare/uncommon/etc.) which ARE affected by fishing power.

### Crate Type Selection Logic

From `FishingCheck_RollItemDrop`:

```csharp
if (fisher.crate) {
    bool hardMode = Main.hardMode;
    // Biome crates require "rare" tier + biome zone:
    if (fisher.rare && flag5 /*dungeon*/)     -> Dungeon/Stockade Crate
    if (fisher.rare && ZoneBeach)              -> Ocean/Seaside Crate
    if (fisher.rare && flag /*corrupt*/)       -> Corrupt/Defiled Crate
    if (fisher.rare && flag2 /*crimson*/)      -> Crimson/Hematic Crate
    if (fisher.rare && ZoneHallow)             -> Hallowed/Divine Crate
    if (fisher.rare && flag3 /*jungle*/)       -> Jungle/Bramble Crate
    if (fisher.rare && ZoneSnow)               -> Frozen/Boreal Crate
    if (fisher.rare && ZoneDesert)             -> Oasis/Mirage Crate
    if (fisher.rare && heightLevel == 0 /*sky*/) -> Sky/Azure Crate
    // Generic crates (no biome requirement):
    if (fisher.veryrare || fisher.legendary)   -> Golden/Titanium Crate
    if (fisher.uncommon)                       -> Iron/Mythril Crate
    else                                       -> Wooden/Pearlwood Crate
}
```

**Lava fishing crates:**
```csharp
if (fisher.inLava && fisher.CanFishInLava) {
    if (fisher.crate && Main.rand.Next(6) == 0)  // 1/6 chance within crate roll
        -> Obsidian Crate (pre-HM) / Hellstone Crate (HM)
}
```

### Complete Crate-to-Biome Mapping

| Pre-Hardmode | Hardmode | Biome Required | Rarity Tier |
|---|---|---|---|
| Wooden Crate (2334) | Pearlwood Crate (3979) | Any | Default (no tier needed) |
| Iron Crate (2335) | Mythril Crate (3980) | Any | Uncommon |
| Golden Crate (2336) | Titanium Crate (3981) | Any | Very Rare or Legendary |
| Corrupt Crate (3203) | Defiled Crate (3982) | Corruption | Rare |
| Crimson Crate (3204) | Hematic Crate (3983) | Crimson | Rare |
| Dungeon Crate (3205) | Stockade Crate (3984) | Dungeon | Rare |
| Sky Crate (3206) | Azure Crate (3985) | Sky (heightLevel 0) | Rare |
| Hallowed Crate (3207) | Divine Crate (3986) | Hallow | Rare |
| Jungle Crate (3208) | Bramble Crate (3987) | Jungle | Rare |
| Frozen Crate (4405) | Boreal Crate (4406) | Snow | Rare |
| Oasis Crate (4407) | Mirage Crate (4408) | Desert | Rare |
| Ocean Crate (5002) | Seaside Crate (5003) | Beach | Rare |
| Obsidian Crate (4877) | Hellstone Crate (4878) | Lava | Crate + 1/6 |

### ItemID.Sets for Crates

```csharp
ItemID.Sets.IsFishingCrate         // bool[] — true for all crate items (26 items)
ItemID.Sets.IsFishingCrateHardmode // bool[] — true for hardmode variants only (13 items)
```

These sets are populated with the hardcoded item IDs (initialized via `RuntimeHelpers.InitializeArray` with embedded data).

### FishingAttempt Struct Fields

```csharp
struct FishingAttempt {
    PlayerFishingConditions playerFishingConditions;
    int X, Y;                    // Bobber tile coordinates
    int bobberType;              // Projectile type of bobber
    bool common, uncommon, rare, veryrare, legendary, crate; // Rarity rolls
    bool inLava, inHoney;        // Liquid type
    int waterTilesCount;         // Tiles of liquid counted
    int waterNeededToFish;       // Minimum tiles for full power
    float waterQuality;          // Penalty for small bodies
    int chumsInWater;            // Chum bucket count
    int fishingLevel;            // Final fishing power
    bool CanFishInLava;          // Has lava fishing gear
    float atmo;                  // Sky height factor
    int questFish;               // Current quest fish type (-1 if done)
    int heightLevel;             // 0=sky, 1=surface, 2=underground, 3=caverns, 4=underworld
    int rolledItemDrop;          // Result item type
    int rolledEnemySpawn;        // Result enemy type
}
```

### Thread Safety
The fishing system runs on the main thread during projectile updates. `ItemID.Sets.IsFishingCrate` is **safe to read from background threads** (static, immutable after load).

### Practical Recommendations
1. Use `ItemID.Sets.IsFishingCrate[itemType]` to identify crate items.
2. To determine which biome a crate comes from, build a lookup from the item IDs listed above — there's no built-in crate-to-biome mapping set.
3. For crate loot, look up the crate's `ItemID` and find its `OpenItem()` method or loot table in tModLoader's `ItemID.Sets` — crate loot is defined in the `Player.OpenBossBag` / `Player.OpenFishingCrate` methods.
4. The crate catch hierarchy matters for progression: biome crates require "rare" tier fishing which needs ~30+ fishing power to have a reasonable chance.

---

## Topic 7: Shimmer Decrafting System

### Full Shimmer Pipeline

When an item touches shimmer liquid, this is the complete processing order:

```
1. Item.Shimmering() — called each tick while in shimmer
   └─ shimmerTime increments by 0.01 per tick
   └─ When shimmerTime > 0.9: Item.GetShimmered()

2. Item.GetShimmered() — the transform pipeline:
   a. GetShimmerEquivalentType()       — resolve aliases
   b. Check CoinLuckValue              — coins give luck, consumed
   c. Check hardcoded special transforms — Rod of Discord, Clentaminator, etc.
   d. Check ShimmerTransformToItem     — direct item-to-item transforms
   e. Check decrafting recipe          — recipe-based decrafting (most items)
   f. Check makeNPC > 0               — NPC release/transform (critters)
```

### Step-by-Step Details

#### Step A: GetShimmerEquivalentType

```csharp
private int GetShimmerEquivalentType() {
    if (ItemID.Sets.ShimmerCountsAsItem[type] != -1)
        return ItemID.Sets.ShimmerCountsAsItem[type];
    return type;
}
```

`ShimmerCountsAsItem` maps variant items to their canonical form. For example, multiple colors of the same dye might all map to one type for shimmer purposes.

#### Step B: CoinLuckValue Check

```csharp
if (ItemID.Sets.CoinLuckValue[shimmerEquivalentType] > 0) {
    stack *= CoinLuckValue;
    player.AddCoinLuck(Center, stack);
    // Item is consumed
}
```

#### Step C: Hardcoded Special Transforms

| Source (shimmerEquivalentType) | Result | Condition |
|---|---|---|
| 1326 (Rod of Discord) | 5335 (Shimmer Rod) | `NPC.downedMoonlord` |
| 779 (Clentaminator) | 5134 (Terraformer) | `NPC.downedMoonlord` |
| 3031 (Clentaminator Solution) | 5364 (Bottomless Shimmer Bucket) | `NPC.downedMoonlord` |
| 5364 (Bottomless Shimmer Bucket) | 3031 (back to solution) | `NPC.downedMoonlord` |
| 3461 (Music Box) | Moon-phase-dependent music box | Always |
| Any item with createTile == 139 | 576 (Blank Music Box) | Always |

#### Step D: ShimmerTransformToItem

```csharp
if (ItemID.Sets.ShimmerTransformToItem[shimmerEquivalentType] > 0) {
    SetDefaults(ShimmerTransformToItem[shimmerEquivalentType]);
    // Stack is preserved (clamped to maxStack, overflow spawns new items)
}
```

This is for direct item-to-item transforms defined in `ItemID.Sets.ShimmerTransformToItem` (an `int[]` indexed by item type, defaulting to -1).

#### Step E: Recipe-Based Decrafting (Most Common Path)

```csharp
int decraftingRecipeIndex = ShimmerTransforms.GetDecraftingRecipeIndex(shimmerEquivalentType);
```

**GetDecraftingRecipeIndex logic:**
```csharp
public static int GetDecraftingRecipeIndex(int type) {
    int[] indices = ItemID.Sets.CraftingRecipeIndices[type];
    foreach (int idx in indices) {
        if (RecipeLoader.DecraftAvailable(Main.recipe[idx]))
            return idx;
    }
    return -1;  // No valid recipe found
}
```

**DecraftAvailable check:**
```csharp
public static bool DecraftAvailable(Recipe recipe) {
    if (!recipe.notDecraftable) {
        return recipe.DecraftConditions.All(c => c.IsMet());
    }
    return false;  // Disabled decrafting
}
```

**Decrafting produces ingredients:**
```csharp
// For each required ingredient in the recipe:
int amount = decraftCount * requiredItem.stack;
RecipeLoader.ConsumeIngredient(recipe, item.type, ref amount, isDecrafting: true);
// Spawns the ingredient items in the world
```

If `recipe.customShimmerResults` is not null, those items are produced instead of the recipe's `requiredItem` list.

### Key Data Structures

```csharp
// On Recipe:
Recipe.DecraftConditions       // List<Condition> — conditions for decrafting availability
Recipe.notDecraftable          // bool — if true, recipe can never be decrafted
Recipe.customShimmerResults    // List<Item>? — custom output items (overrides requiredItem)
Recipe.DecraftDisabled         // bool property => notDecraftable

// On ItemID.Sets:
ItemID.Sets.ShimmerTransformToItem[type]  // int — direct transform target (-1 = none)
ItemID.Sets.ShimmerCountsAsItem[type]     // int — type alias for shimmer (-1 = self)
ItemID.Sets.CraftingRecipeIndices[type]   // int[] — all recipe indices that create this item
ItemID.Sets.CoinLuckValue[type]           // int — coin luck value (0 = not a coin)

// On ShimmerTransforms:
ShimmerTransforms.RecipeSets.PostSkeletron  // bool[] — recipes containing item 154 (Bone)
ShimmerTransforms.RecipeSets.PostGolem      // bool[] — recipes containing item 1101 (Beetle Husk)
```

### IsItemTransformLocked

Additional gating beyond DecraftConditions:
```csharp
public static bool IsItemTransformLocked(int type) {
    int recipeIdx = GetDecraftingRecipeIndex(type);
    if (recipeIdx < 0) return false;

    // Post-Skeletron lock: recipes containing Bone (item 154)
    if (!NPC.downedBoss3 && RecipeSets.PostSkeletron[recipeIdx])
        return true;

    // Post-Golem lock: recipes containing Beetle Husk (item 1101)
    if (!NPC.downedGolemBoss && RecipeSets.PostGolem[recipeIdx])
        return true;

    // Also check mod-added DecraftConditions
    if (!RecipeLoader.DecraftAvailable(Main.recipe[recipeIdx]))
        return true;

    return false;
}
```

### Recipe API for Shimmer Control

```csharp
// Disable decrafting entirely:
recipe.DisableDecraft();

// Add conditions for when decrafting is available:
recipe.AddDecraftCondition(Condition.Hardmode);
recipe.AddDecraftCondition(Condition.DownedPlantera);

// Custom shimmer output (different from recipe ingredients):
recipe.AddCustomShimmerResult(ItemID.FragmentSolar, 5);

// Copy recipe conditions to decraft conditions:
recipe.ApplyConditionsAsDecraftConditions();
```

### CanShimmer Check (Complete)

```csharp
public bool CanShimmer() {
    int equivType = GetShimmerEquivalentType();
    if (ShimmerTransforms.IsItemTransformLocked(equivType))
        return false;

    bool specialTransform = false;
    // Hardcoded special items (Rod of Discord, Clentaminator, etc.)
    if ((type == 1326 || type == 779 || type == 3031 || type == 5364) && NPC.downedMoonlord)
        specialTransform = true;
    if (type == 4986 && !NPC.unlockedSlimeRainbowSpawn)  // Gelatin Crystal
        specialTransform = true;
    if (type == 3461 || createTile == 139)  // Music boxes
        specialTransform = true;

    if (!specialTransform
        && ShimmerTransformToItem[equivType] <= 0
        && FindDecraftAmount() <= 0
        && CoinLuckValue[type] <= 0)
        return makeNPC > 0;  // Last resort: NPC release

    return true;
}
```

### Thread Safety
`ItemID.Sets.ShimmerTransformToItem`, `ShimmerCountsAsItem`, and `CraftingRecipeIndices` are all **safe to read from background threads** (populated during load, never modified at runtime).

`RecipeLoader.DecraftAvailable()` calls `Condition.IsMet()` which reads game state — **NOT safe for background threads**. For background use, snapshot the condition results before entering the background task.

`ShimmerTransforms.RecipeSets.PostSkeletron/PostGolem` are computed during `UpdateRecipeSets()` and are safe to read.

### Practical Recommendations

1. **To check if an item can be decrafted:** Use `ItemID.Sets.CraftingRecipeIndices[type]` to get recipe indices, then check `!recipe.notDecraftable` and snapshot `DecraftConditions` results.

2. **To get decrafting ingredients:** Find the recipe via `ShimmerTransforms.GetDecraftingRecipeIndex(type)`, then read `Main.recipe[idx].requiredItem` (or `customShimmerResults` if non-null).

3. **To check direct transforms:** `ItemID.Sets.ShimmerTransformToItem[type]` — if > 0, the item transforms directly without decrafting.

4. **For our mod's resolver:** When computing "how to get item X", check if any item Y has `ShimmerTransformToItem[Y] == X` — that means shimmer transforms Y into X. Also check if X appears in some recipe's `requiredItem` list where that recipe's `createItem` can be decrafted.

5. **Priority order matters:** The shimmer system checks CoinLuck -> Special -> ShimmerTransformToItem -> Decrafting -> NPC, in that order. The first match wins.

---

## Cross-Cutting Concerns

### Thread Safety Summary

| Data | Safe for Background Threads? |
|---|---|
| `ContentSamples.ItemsByType[]` | Yes (static, immutable) |
| `ContentSamples.NpcsByNetId[]` | Yes (static, immutable) |
| `Main.BestiaryDB` entries | Yes (populated during load) |
| `ItemID.Sets.*` | Yes (static arrays, immutable) |
| `TileID.Sets.*` | Yes (static arrays, immutable) |
| `Main.recipe[]` | Yes (array immutable after load) |
| `Recipe.requiredItem`, `createItem` | Yes (set during AddRecipes) |
| `Recipe.DecraftConditions` | List is safe, but `IsMet()` is NOT |
| `Condition.IsMet()` / `Condition.Predicate` | NO — reads Main.LocalPlayer, NPC.downed*, etc. |
| `Player.adjTile[]` | NO — updated every frame |
| `Player.currentShoppingSettings` | NO — updated every frame |
| `ShimmerTransforms.RecipeSets.*` | Yes (computed once during load) |
| `Lang.GetMapObjectName()` | Yes (cache populated during load) |

### General Pattern for Background Thread Access

```csharp
// On main thread: snapshot dynamic state
var downedBosses = new Dictionary<string, bool> {
    ["Skeletron"] = NPC.downedBoss3,
    ["Golem"] = NPC.downedGolemBoss,
    ["MoonLord"] = NPC.downedMoonlord,
    // ... etc
};
bool isHardmode = Main.hardMode;
bool isCrimsonWorld = WorldGen.crimson;

// Then pass snapshot to Task.Run — never read game state directly
```
