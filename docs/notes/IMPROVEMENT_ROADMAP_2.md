# Improvement Roadmap 2: Data Enrichment

Based on decompilation of tModLoader.dll. Full research in DATA_ENRICHMENT_RESEARCH.md.

---

## 1. Rich Bestiary Spawn Conditions

### Problem
BiomeMapper only extracts the localized display name from bestiary entries. The raw key is structured (`Bestiary_Biomes.TheCorruption`, `Bestiary_Times.NightTime`, `Bestiary_Events.BloodMoon`) and could provide time-of-day, event, and invasion data.

### What We Learned
- Keys follow format: `Bestiary_{Category}.{Name}` where Category is Biomes/Times/Events/Invasions
- 36 biome tags, 2 time tags, 12 event tags, 7 invasion tags — all catalogued
- Data is immutable after load — safe for background threads
- No depth/hardmode data embedded in the element itself

### Approach
**Enhance BiomeMapper to parse structured keys** instead of just getting display text.

1. In `GetNPCLocationFromBestiary`, parse the key to extract category + condition name:
   ```
   "Bestiary_Biomes.TheCorruption" → (Biomes, TheCorruption)
   "Bestiary_Times.NightTime" → (Times, NightTime)
   "Bestiary_Events.BloodMoon" → (Events, BloodMoon)
   ```

2. New `NpcSpawnInfo` record returned from BiomeMapper:
   ```
   record NpcSpawnInfo(
       string Location,          // "The Corruption" (existing)
       string TimeOfDay,         // "Night" or null
       string Event,             // "Blood Moon" or null
       string Invasion,          // "Goblins" or null
       List<string> AllBiomes    // all biome tags
   )
   ```

3. Use in ActionClassifier/SourcePriorityAnalyzer to show "Zombie (Night, Surface)" instead of just "Surface".

### Files to Modify
- `Content/Tracking/BiomeMapper.cs` — parse structured keys, return NpcSpawnInfo
- `Content/Tracking/ActionClassifier.cs` — use time/event data in action group descriptions
- `UI/Elements/UILocationCard.cs` — show time/event badges on drop entries
- `UI/Elements/UIActionGroupCard.cs` — show spawn conditions

### Benefits
- TODO overlay: "Farm: Surface (Night)" instead of "Farm: Surface"
- Source priority: Time-locked mobs get slight score penalty (can't farm during day)
- Material panel: Source tags include "(Night)" suffix

---

## 2. Crafting Station Equivalence Map

### Problem
Recipes require tile IDs for crafting stations. We show station names but don't know that an Adamantite Forge covers Furnace AND Hellforge recipes. Players might think they need 3 separate stations.

### What We Learned
- Hardcoded vanilla equivalences: Adamantite Forge → Furnace + Hellforge, Mythril Anvil → Iron Anvil, Alchemy Table → Bottle + Table
- ModTile.AdjTiles provides modded equivalences
- Recipe also uses `needWater`, `needSnowBiome`, etc. flags — these are SEPARATE from Condition objects

### Approach
**Build a static station equivalence map during PostAddRecipes.**

1. New `CraftingStationMap` class in `Common/Systems/`:
   ```
   static Dictionary<int, HashSet<int>> _equivalences  // tileId → set of tile IDs it covers
   ```

2. Populate from:
   - Hardcoded vanilla switch cases (6 equivalences from research)
   - `TileLoader.GetTile(type)?.AdjTiles` for all modded tiles

3. **Use in craft order display.** When showing "Station: Furnace", check if player's known stations (from inventory scan) include an equivalent. Show "Furnace (you have: Adamantite Forge)" or just "Adamantite Forge" since it covers Furnace.

4. **Use in resolver cost.** Could add a small cost reduction for recipes whose stations the player has access to, but this is low priority — display improvement is the main win.

### Files to Create
- `Common/Systems/CraftingStationMap.cs` — equivalence map built during PostAddRecipes

### Files to Modify
- `UI/Elements/UICraftStep.cs` — show equivalent stations
- `UI/Elements/UITreeNode.cs` — same

### Benefits
- Craft order: "Adamantite Forge" instead of "Furnace" when player has the forge
- Tree view: Station info reflects actual available stations
- Reduced player confusion about which stations they need

---

## 3. Accurate Shop Prices

### Problem
Shop prices are captured as formatted strings ("~5g") but don't account for Discount Card (20% off) or NPC happiness (±25%). Players see one price but pay another.

### What We Learned
- `Item.GetStoreValue()` returns base price (or `shopCustomPrice` override)
- Discount Card: `* 0.8` applied when `player.discountEquipped`
- NPC Happiness: 0.75x to 1.5x multiplier via `currentShoppingSettings.PriceAdjustment`
- Special currencies: `shopSpecialCurrency >= 0` uses custom system

### Approach
**Show base price + discount indicator.** Don't try to compute exact happiness (too dynamic).

1. During `IndexNPCShops`, store the raw `Item.value` or `shopCustomPrice` as a structured field:
   ```
   ItemSource: + int? ShopPrice  // raw copper value, null if special currency
   ```

2. In UI, show formatted price. If player has Discount Card equipped (detectable in WorldContext or inventory scan), show discounted price.

3. For special currencies, show the currency name + amount via `CustomCurrencyManager`.

### Files to Modify
- `Content/Graph/ItemSource.cs` — add `ShopPrice` field
- `Common/Systems/ItemSourceSystem.cs` — store raw price during indexing
- `UI/Elements/UIActionGroupCard.cs` — show price on shop action items
- `UI/Elements/UILocationCard.cs` — show price on shop entries

### Benefits
- Material panel: "Buy: Merchant (5g)" with accurate base price
- TODO/overlay: Shop actions show price so player knows cost upfront
- Source priority: Could factor price into scoring (cheaper = more accessible)

---

## 4. Structured Item Stats for Recommendations

### Problem
GearRecommender parses tooltip text with string matching to extract +damage%, +crit, +defense. This is fragile and misses modded items that format tooltips differently.

### What We Learned
- `Item.damage`, `Item.crit`, `Item.knockBack`, `Item.useTime`, `Item.defense` are all readable fields on `ContentSamples.ItemsByType[type]`
- `Item.DamageType` gives the damage class
- Tool power: `item.pick`, `item.axe * 5`, `item.hammer`
- Accessory bonuses are NOT stored as data — they're applied imperatively in `UpdateAccessory()`. No data-driven extraction possible.

### Approach
**Use structured Item fields instead of tooltip parsing for weapons/armor/tools.** Keep tooltip parsing only for accessories (no alternative).

1. In GearRecommender, replace tooltip string parsing for:
   - **Weapons**: Use `item.damage`, `item.DamageType`, `item.useTime`, `item.crit` directly
   - **Armor**: Use `item.defense` directly
   - **Tools**: Use `item.pick`, `item.axe * 5`, `item.hammer` directly
   - **Accessories**: Keep tooltip parsing (no structured data available)

2. This makes weapon/armor/tool scoring more reliable, especially for modded content where tooltip formatting varies.

### Files to Modify
- `Content/Progression/GearRecommender.cs` — use item fields instead of tooltip parsing where possible

### Benefits
- More reliable scoring for modded weapons/armor/tools
- Eliminates tooltip parsing bugs for non-accessories
- Faster recommendation computation (no string parsing)

---

## 5. Exhaustive Recipe Condition Matching

### Problem
Our `ExtractBiomeRequirement` in DependencyResolver checks 5 conditions. The full catalog has 31 biome conditions, 4 Near* conditions, 2 time conditions, and more. We're missing conditions that exist on real recipes.

### What We Learned
Complete catalog of 70+ conditions. Key ones we're missing:
- `NearShimmer` — near shimmer liquid
- `InUnderworld` — checks `player.ZoneUnderworldHeight`
- `InSkyHeight` / `InSpace` — space layer
- `InDirtLayerHeight`, `InRockLayerHeight` — underground depth layers
- `InBeach`, `InRain`, `InSandstorm` — weather/location
- `InGraveyard` already handled; but `InGlowshroom`, `InDesert` partially handled

### Approach
**Expand the condition catalog to cover all gameplay-relevant conditions.**

1. In `DependencyResolver.ExtractBiomeRequirement`, add all conditions that affect where a player must physically be:
   ```
   NearShimmer → "Near shimmer"
   InSkyHeight → "Space"
   InUnderworldHeight → "Underworld" (addition to existing InUnderworld)
   InBeach → "Beach/Ocean"
   InRain → "During rain"
   InGraveyard, InSnow, InDesert, InJungle, InDungeon → already handled
   InCorrupt, InCrimson, InHallow → add these
   InGlowshroom → "Mushroom biome" (already handled)
   ```

2. In `UITreeNode.GetBiomeRequirement`, same expansion.

3. **Also handle `recipe.needWater`, `recipe.needSnowBiome`, `recipe.needLava`, `recipe.needHoney`** — these are separate bool fields on Recipe, not Condition objects. They should be extracted alongside conditions.

### Files to Modify
- `Content/Graph/DependencyResolver.cs` — expand ExtractBiomeRequirement
- `UI/Elements/UITreeNode.cs` — expand GetBiomeRequirement

### Benefits
- Complete biome/environment coverage for all vanilla + modded recipes
- No missing condition display for edge-case recipes
- Recipe environment flags (needWater etc.) properly shown

---

## 6. Fishing Crate Biome Mapping

### Problem
We index crate drops via `ItemID.Sets.IsFishingCrate` and infer biome from crate names. But there's no structured crate→biome mapping, and we don't show catch requirements.

### What We Learned
- Crate chance is flat 10% (25% with potion), but crate TYPE depends on fishing power rarity tiers
- 13 biome crate pairs (pre-HM/HM) with exact item IDs catalogued
- Biome crates require being in the correct zone + rolling "rare" tier
- No built-in crate-to-biome Set — must hardcode

### Approach
**Build a structured crate→biome lookup table.**

1. New static mapping in ItemSourceSystem or a helper class:
   ```
   Dictionary<int, string> _crateBiomes  // crateItemId → biome name
   ```
   Covers all 26 crate items (13 pairs) from the research.

2. Use during `IndexItemDrops` to add accurate biome tags to fishing sources instead of name inference.

3. Show fishing power hint: "Fish: Jungle Crate (30+ power)" based on rarity tier requirements.

### Files to Modify
- `Common/Systems/ItemSourceSystem.cs` — structured crate lookup replacing name inference
- Potentially `Content/Tracking/BiomeMapper.cs` — use structured data for crate biomes

### Benefits
- Accurate biome for all crates (no name-parsing failures for modded crates with unusual names)
- Fishing power hints help players know what gear they need
- Source priority: Fishing locations scored more accurately

---

## 7. Shimmer Decrafting as Acquisition Path

### Problem
Our resolver uses `ShimmerTransformToItem` for direct shimmer transforms but doesn't know about shimmer decrafting. A player can shimmer a crafted item to get its ingredients back — this is a valid acquisition path.

### What We Learned
- `ShimmerTransforms.GetDecraftingRecipeIndex(type)` finds the recipe used for decrafting
- `Recipe.notDecraftable` and `Recipe.DecraftConditions` gate availability
- `Recipe.customShimmerResults` overrides output if set
- Post-Skeletron/Post-Golem locks via `RecipeSets.PostSkeletron/PostGolem`
- `ItemID.Sets.CraftingRecipeIndices[type]` maps item→recipe indices (for decrafting lookup)
- `ShimmerCountsAsItem` maps variant items to canonical shimmer IDs

### Approach
**Add shimmer decrafting as an alternative source for materials.** This is complex — implement in phases.

**Phase 1: Index decrafting sources.** During `PostAddRecipes`, for each item that can be decrafted, add an `ItemSourceType.Shimmer` source for each ingredient the decrafting produces:
```
If item X decrafts into [A, B, C]:
  Add source to A: "Shimmer decraft: X" (type=Shimmer)
  Add source to B: "Shimmer decraft: X" (type=Shimmer)
  Add source to C: "Shimmer decraft: X" (type=Shimmer)
```

**Phase 2: Resolver awareness.** In `TryShimmerFallback`, check both direct transforms AND decrafting. If the player owns item X and needs ingredient A, and X decrafts into A, that's a valid path.

**Phase 3: Condition gating.** Snapshot `NPC.downedBoss3` (Skeletron) and `NPC.downedGolemBoss` for PostSkeletron/PostGolem locks. Add to WorldContext and check during shimmer resolution.

### Files to Modify
- `Common/Systems/ItemSourceSystem.cs` — index decrafting sources in PostAddRecipes
- `Content/Graph/DependencyResolver.cs` — expand TryShimmerFallback with decrafting
- `Content/Progression/WorldContext.cs` — add DownedSkeletron, DownedGolem for shimmer locks

### Benefits
- Resolver finds more paths (decrafting opens up ingredient acquisition)
- Material panel shows "Shimmer decraft: X" as a source for ingredients
- Cross-evil paths expand further (decraft a Crimson item to get universal ingredients)

---

## Implementation Order

### Wave 4: Quick catalog expansions
1. **#5 Exhaustive Conditions** — Expand 2 methods with more condition checks. Minimal risk.
2. **#6 Fishing Crate Mapping** — Replace name inference with structured lookup. Clean improvement.

### Wave 5: Data enrichment
3. **#1 Rich Bestiary Conditions** — Parse structured keys, enhance spawn info display
4. **#2 Station Equivalence** — New system, moderate scope, high display value
5. **#4 Structured Item Stats** — Refactor GearRecommender scoring, improves reliability

### Wave 6: Advanced features
6. **#3 Accurate Shop Prices** — Store raw prices, show formatted amounts
7. **#7 Shimmer Decrafting** — Complex multi-phase, highest new-path-discovery value

### Decompilation Verification Needed
- Verify `recipe.needWater` / `needSnowBiome` / `needLava` / `needHoney` field names (for #5)
- Verify `CustomCurrencyManager` API for special shop currencies (for #3)
- Verify `ShimmerCountsAsItem` behavior for edge cases (for #7)
