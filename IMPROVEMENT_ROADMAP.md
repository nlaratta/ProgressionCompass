# Improvement Roadmap: 7 Cross-Cutting Enhancements

Based on decompilation research and codebase analysis. Each improvement is designed to benefit ALL mod features — resolver, recommendations, tracking, source priority, and UI.

---

## 1. Ore-Aware Recipe Group Resolution

### Problem
`ResolveGroup` picks the cheapest group member by `AcquisitionCost` but ignores which ore variant the world actually has. A Lead world player sees "Iron Bar" as the default because both resolve to the same cost when neither is in inventory.

### What We Have
- `WorldContext` already captures `CopperTier`, `IronTier`, `SilverTier`, `GoldTier`, `CobaltTier`, `MythrilTier`, `AdamantiteTier` (TileIDs from `SavedOreTiers`)
- `DependencyResolver` already has `_worldContext` as a field

### Approach

**New helper on WorldContext:** `IsWorldOreItem(int itemType) → bool`
Maps ItemIDs to their ore TileIDs and checks against saved tiers. Covers ore items AND bar items:
- Copper Ore (ItemID 12) → TileID 7 → check `CopperTier == 7`
- Tin Ore (ItemID 699) → TileID 166 → check `CopperTier == 166`
- Iron Bar (ItemID 22) → associated with TileID 6 → check `IronTier == 6`
- Lead Bar (ItemID 704) → associated with TileID 167 → check `IronTier == 167`
- etc. for all 14 ore items + 14 bar items + hardmode equivalents

Uses a static `Dictionary<int itemId, (int tileId, int tierField)>` mapping ~30 items.

**Modify `ResolveGroup`:** Before iterating members, sort to put world-preferred items first. When two members have equal AcquisitionCost, prefer the world's variant. This means if a player has 0 of both Iron and Lead bars, the resolver picks whichever matches their world.

**Hardmode unknown (-1):** When `CobaltTier == -1`, both Cobalt and Palladium are treated as equally valid (no preference). The sort is a no-op for unknown tiers.

### Files to Modify
- `Content/Progression/WorldContext.cs` — add `IsWorldOreItem(int)` method + static mapping
- `Content/Graph/DependencyResolver.cs` — sort group members in `ResolveGroup`

### Benefits Across Mod
- **Resolver**: Picks world-correct ore by default
- **Material list**: Shows Lead Bar (not Iron Bar) in Lead worlds
- **Craft order**: Steps reference correct ore names
- **Tracking/TODO**: Actions say "Mine Lead Ore" not "Mine Iron Ore"
- **Source Priority**: Ranks locations for the correct ore

---

## 2. Recipe Condition Awareness

### Problem
The resolver ignores `recipe.Conditions` entirely. It will suggest crafting at an Ecto Mist station (requires Graveyard biome) or post-Plantera recipes when the player hasn't beaten Plantera. Conditions are displayed in orange text but never validated.

### What We Have
- `ProgressionTier.EstimateConditionTier()` already recognizes tier-based conditions
- `CraftNode.HasConditions` / `ConditionDescription` already reads conditions for display
- `recipe.Conditions` is a `List<Condition>` with `.IsMet()` method and `.Description`

### Approach

**Do NOT filter recipes by conditions** — that would hide valid future paths. Instead:

1. **Cost penalty for unmet conditions.** In the candidate sorting, add a penalty to recipes with unmet conditions. This makes met-condition recipes preferred at equal cost, but unmet-condition recipes still appear as alternatives.

   ```
   effectiveCost = acquisitionCost + (hasUnmetConditions ? 1000 : 0)
   ```

2. **Structured condition data on CraftStep.** Replace `string ConditionDescription` with a richer structure:
   ```
   record CraftConditionInfo(string Description, bool IsMet, ConditionCategory Category)
   enum ConditionCategory { Boss, Biome, Time, Hardmode, Other }
   ```

   This lets the UI render met conditions in green, unmet in orange/red.

3. **Condition awareness in GearRecommender.** When scoring upgrade recommendations, apply a small penalty for items whose *entire* recipe chain has unmet conditions. This deprioritizes items the player literally cannot craft yet.

### Decompilation Needed
Decompile `Condition.IsMet()` to understand thread safety — if it reads game state, we need to evaluate conditions on the main thread and cache results, not call `.IsMet()` from background threads.

### Files to Modify
- `Content/Graph/DependencyResolver.cs` — add condition penalty to candidate sorting
- `Content/Graph/ResolverResult.cs` — enrich CraftStep with structured condition data
- `UI/Elements/UICraftStep.cs` — render met/unmet conditions differently
- `UI/Elements/UITreeNode.cs` — same
- `Content/Progression/GearRecommender.cs` — condition-aware scoring

### Benefits Across Mod
- **Resolver**: Prefers achievable recipes over theoretically-cheaper-but-locked ones
- **Craft order**: Players see which conditions they've met vs still need
- **Recommendations**: Items requiring unbeaten bosses ranked lower
- **Dashboard**: "Almost Ready" items that need an unbeaten boss are flagged

---

## 3. Hardmode Ore "Unknown" Handling

### Problem
Before altars are smashed, `SavedOreTiers.Cobalt/Mythril/Adamantite` are -1. The resolver treats both variants as valid, which is correct, but the UI gives no indication that the ore is undetermined. Players see "Cobalt Bar" when they might get Palladium.

### Approach

1. **Show both variants in material lists** when tier is -1. Instead of "Cobalt Bar × 3", show "Cobalt or Palladium Bar × 3" with a note icon.

2. **Add `HardmodeOresKnown` check to UI panels.** When rendering a material that's a hardmode ore and `worldContext.HardmodeOresKnown == false`, append a "(TBD)" indicator.

3. **In recommendations**, deprioritize hardmode-ore items slightly when ores are unknown — the player can't plan for them yet.

### Files to Modify
- `UI/Panels/MaterialListPanel.cs` — dual-variant display for unknown ores
- `UI/Elements/UICraftStep.cs` — "(or Palladium)" suffix
- `Content/Progression/WorldContext.cs` — add `GetOreAlternative(int itemType) → int?` helper

### Benefits Across Mod
- **Material list**: Clear "Cobalt or Palladium" display
- **Craft order**: Steps show both possibilities
- **Tracking**: TODO items show both variants until altars smashed

---

## 4. NPC Shop Condition Awareness

### Problem
Shop conditions (moon phase, time of day, post-boss, biome presence) are embedded in the description string: `"Buy: Merchant ~5g (Blood Moon)"`. This prevents programmatic filtering or validation.

### Approach

1. **Add structured condition fields to ItemSource.** Extend the record:
   ```
   record ItemSource(
       ...,
       List<string> ShopConditions = null  // NEW: structured condition texts
   )
   ```

2. **During IndexNPCShops**, store conditions separately:
   ```csharp
   var condTexts = new List<string>();
   foreach (var cond in entry.Conditions)
   {
       string desc = cond?.Description?.Value;
       if (!string.IsNullOrEmpty(desc))
           condTexts.Add(desc);
   }
   ```
   Store in both the description (for display) AND the new field (for programmatic use).

3. **Evaluate shop condition availability.** On the main thread, check `cond.IsMet()` for each shop condition. Store a `bool IsCurrentlyAvailable` on the source. UI can render unavailable shops in gray with the reason.

4. **Source Priority impact.** When scoring shop sources in `SourcePriorityAnalyzer`, apply a small penalty for shops with unmet conditions. "Buy from Merchant (always available)" ranks above "Buy from Steampunker (post-mechanical boss)".

### Decompilation Needed
Decompile shop entry condition types to understand what `.IsMet()` checks. Common patterns: `Condition.Hardmode`, `Condition.BloodMoon`, `Condition.DownedMechBoss*`, `Condition.MoonPhase*`.

### Files to Modify
- `Content/Graph/ItemSource.cs` — add `ShopConditions` field
- `Common/Systems/ItemSourceSystem.cs` — extract structured conditions during indexing
- `Content/Tracking/SourcePriorityAnalyzer.cs` — factor availability into scoring
- `UI/Elements/UILocationCard.cs` — show condition status for shop cards

### Benefits Across Mod
- **Source priority**: Shops scored by actual availability
- **Material panel**: "Buy: Merchant (Night only)" with availability indicator
- **Tracking/TODO**: "Buy" actions show when the shop is accessible
- **Recommendations**: Items behind shop locks ranked appropriately

---

## 5. Shimmer Cross-Evil Transmutation

### Problem
In a Crimson world, if a recipe needs Shadow Scales (Corruption), the resolver says "Acquire: Shadow Scale" with no path. But Tissue Samples can be shimmered into Shadow Scales. The resolver doesn't consider shimmer as a crafting alternative.

### What We Have
- `ItemID.Sets.ShimmerTransformToItem[]` already indexed by `ItemSourceSystem`
- Sources show "Shimmer: Tissue Sample" on the result item
- Shimmer is low-priority (score 25) and only shown as a source, never used as a craft path

### Approach

**Add shimmer as a "virtual recipe" in the resolver.** After trying all real recipes and finding none viable (or all too expensive), check if the item has a shimmer source. If so, treat shimmer as a one-ingredient recipe:

```
In ResolveItem, after the recipe candidate loop:
  If no candidates found OR best candidate cost is very high:
    Check _sourceSystem.GetSources(itemType) for Shimmer type
    If found:
      int sourceItemType = shimmerSource.SourceEntityId
      Resolve sourceItemType recursively
      Create a "virtual CraftNode" with shimmer as the recipe
      Add to candidates
```

This is especially powerful for cross-evil items: in a Crimson world, resolving Shadow Scale would:
1. Try real recipes → none found
2. Check shimmer sources → "Shimmer: Tissue Sample"
3. Resolve Tissue Sample → Drop: Brain of Cthulhu (available in Crimson world!)
4. Path: Kill Brain → Tissue Sample → Shimmer → Shadow Scale

**New node type or annotation.** Either create a `ShimmerNode` (subclass of GraphNode) or annotate the CraftNode with `IsShimmerTransform = true` so the UI can render it distinctively (purple shimmer icon).

**Cost adjustment.** Shimmer transforms should have a base cost penalty (shimmer pool access isn't trivial) but much less than "impossible to obtain":
```
shimmerCost = sourceResolutionCost + 5  // 5 = shimmer access penalty
```

### Files to Modify
- `Content/Graph/DependencyResolver.cs` — add shimmer fallback after recipe candidates
- `Content/Graph/GraphNode.cs` — add ShimmerNode or IsShimmerTransform flag on CraftNode
- `UI/Elements/UITreeNode.cs` — render shimmer transforms with distinctive styling
- `UI/Elements/UICraftStep.cs` — "Shimmer: Item → Result" step format

### Benefits Across Mod
- **Resolver**: Finds paths through shimmer for cross-evil items
- **Material list**: Shows shimmer intermediates as materials
- **Craft order**: Includes "Shimmer: X → Y" as a step
- **Recommendations**: Items that were "impossible" now show as obtainable via shimmer
- **Source priority**: Shimmer Pool location gets credit for enabling cross-evil paths
- **Tracking**: TODO shows "Shimmer: Tissue Sample → Shadow Scale" as an action

---

## 6. Boss Drop Difficulty Variants

### Problem
`ItemSourceSystem` tags Expert/Master drops with description suffixes like "[Expert+]" but this is embedded in text. The mod doesn't know if the player is in Expert mode, so it might show Expert-only drops as sources in a Normal world.

### Approach

1. **Capture difficulty in WorldContext:**
   ```
   WorldContext:
     + IsExpert: bool  (Main.expertMode)
     + IsMaster: bool  (Main.masterMode)
   ```

2. **Add structured difficulty field to ItemSource:**
   ```
   record ItemSource(
       ...,
       GameDifficulty MinDifficulty = GameDifficulty.Normal  // NEW
   )
   enum GameDifficulty { Normal, Expert, Master }
   ```

3. **During IndexNPCDrops**, when encountering `DropBasedOnExpertMode` or `DropBasedOnMasterMode` rules, set the appropriate difficulty on the source.

4. **Filter sources by difficulty.** In `GetSources(int, WorldContext)`, also filter out sources that require higher difficulty than the current world.

5. **Impact on recommendations.** Expert-only boss drops don't appear as upgrade sources in Normal worlds. This prevents "Acquire: Worm Scarf (Expert+)" when the player can't get it.

### Decompilation Needed
Verify `Main.expertMode` and `Main.masterMode` are the correct runtime checks. Also check for `Main.GameMode` enum values.

### Files to Modify
- `Content/Progression/WorldContext.cs` — add IsExpert, IsMaster
- `Content/Graph/ItemSource.cs` — add MinDifficulty field
- `Common/Systems/ItemSourceSystem.cs` — set difficulty during drop indexing, filter in GetSources
- `Content/Progression/GearRecommender.cs` — skip Expert-only items in Normal

### Benefits Across Mod
- **Source display**: Expert drops clearly marked and filterable
- **Recommendations**: No impossible Expert items in Normal worlds
- **Material panel**: Correct difficulty shown per source
- **Source priority**: Boss locations only credit drops the player can actually get

---

## 7. Biome-Specific Crafting Conditions

### Problem
Some recipes require specific biomes (Ecto Mist = Graveyard, certain recipes need Snow/Water/Honey). The resolver shows "Craft at: Work Bench" but doesn't mention "in a Graveyard biome." This info IS in `recipe.Conditions` but only rendered as a generic orange string.

### Approach

1. **Categorize conditions by type.** Parse `recipe.Conditions` to identify biome requirements:
   - `Condition.InGraveyard` → "Graveyard biome"
   - `Condition.NearWater` → "Near water"
   - `Condition.NearHoney` → "Near honey"
   - `Condition.NearLava` → "Near lava"
   - `Condition.InSnow` → "Snow biome"

   These are recognizable by comparing `Condition` instances to known static fields.

2. **Enrich CraftStep with biome info.** Add `string BiomeRequirement` alongside `RequiredTiles`:
   ```
   CraftStep:
     + BiomeRequirement: string?  // "Graveyard", "Near water", etc.
   ```

3. **Display biome alongside station.** In UICraftStep: "Craft at: Work Bench (Graveyard)" with biome in purple/blue.

4. **Resolver cost consideration.** Biome-specific recipes get a small cost penalty (accessing a Graveyard is harder than just having a Work Bench), but this is minor — mainly a display improvement.

### Condition Matching Strategy
Compare condition objects by reference to the known `Condition.*` static fields:
```csharp
if (cond == Condition.InGraveyard) biome = "Graveyard";
else if (cond == Condition.NearWater) biome = "Near water";
// etc.
```
Reference comparison works because Terraria's conditions are static singletons.

### Files to Modify
- `Content/Graph/DependencyResolver.cs` — extract biome from conditions during CraftStep building
- `Content/Graph/ResolverResult.cs` — add BiomeRequirement to CraftStep
- `UI/Elements/UICraftStep.cs` — display biome requirement
- `UI/Elements/UITreeNode.cs` — display biome in tree view

### Benefits Across Mod
- **Craft order**: Clear "Craft at: Work Bench (Graveyard)" instructions
- **Tree view**: Biome shown alongside station
- **Material panel**: Biome info on hover
- **Recommendations**: Biome-locked crafts visible at a glance

---

## Implementation Order (Recommended)

### Wave 1: Quick wins with existing infrastructure
1. **#1 Ore-Aware Groups** — WorldContext already threaded, just needs sorting logic + item→tile mapping
2. **#3 Hardmode Unknown** — Simple UI-level change, WorldContext.HardmodeOresKnown already exists

### Wave 2: Resolver enhancements
3. **#5 Shimmer Cross-Evil** — High impact, adds a new resolution path
4. **#2 Recipe Condition Awareness** — Cost penalty + structured condition data

### Wave 3: Data enrichment
5. **#7 Biome Crafting Conditions** — Condition parsing + display
6. **#6 Boss Drop Difficulty** — WorldContext addition + source field + filtering
7. **#4 Shop Conditions** — Structured data extraction + availability scoring

### Decompilation Tasks (can run in parallel)
- Decompile `Condition.IsMet()` for thread safety analysis
- Decompile `Main.expertMode` / `Main.masterMode` / `Main.GameMode` for difficulty detection
- Decompile common `Condition.*` fields to build reference table for biome/boss/time detection
- Decompile `ShimmerTransformToItem` initialization to verify cross-evil pairs exist

---

## Cross-Cutting Concerns

### WorldContext Expansion
Improvements #6 adds `IsExpert`/`IsMaster` to WorldContext. This needs to be captured at the same 3 capture points already established.

### ItemSource Record Expansion
Improvements #4 and #6 add new fields to ItemSource. Since ItemSource is a record, adding `ShopConditions` and `MinDifficulty` with default values is backward-compatible.

### CraftStep Record Expansion
Improvements #2 and #7 enrich CraftStep. Adding `BiomeRequirement` and structured `ConditionInfo` requires updating ExtractCraftOrder + UI renderers.

### UI Impact
All 7 improvements have UI rendering changes. The UI components are already pattern-established (UICraftStep, UITreeNode, MaterialListPanel), so changes follow existing conventions.

---

## Feature Coverage Audit

Every user-facing feature must be checked for impact. Here's the full mapping:

### Tracking Overlay (TrackingOverlaySystem / TrackingOverlayState)
Shows up to 5 tracked goals + 3 action groups on the HUD.
- **#1 Ore Groups**: Correct ore names in action item descriptions (inherits from resolver)
- **#2 Conditions**: Unmet-condition items deprioritized in action plan → different top-3 groups shown
- **#5 Shimmer**: ActionClassifier needs to handle shimmer steps — new shimmer actions ("Shimmer: Tissue Sample → Shadow Scale") should appear as `ActionType.Explore` or a new shimmer sub-type. Currently ActionClassifier reads `MaterialEntry.Sources` — if shimmer becomes a craft step, the material may no longer appear as "missing" (it's resolvable via shimmer). Need to ensure the TODO plan still shows the shimmer action.
- **#6 Difficulty**: Expert-only sources filtered → overlay won't show impossible drops in Normal worlds

### TrackingPanel "Next Actions" Section
Same data as overlay (ActionClassifier output), rendered as collapsible action group cards.
- Same impacts as overlay above
- **#4 Shop Conditions**: Action items for shops could show "(Night only)" or "(Post-Plantera)" availability tags

### UIActionGroupCard
Renders individual action groups with item rows.
- **#5 Shimmer**: If ActionClassifier produces a shimmer action, the card needs rendering for it. Options:
  - Fold shimmer into `ActionType.Explore` with a "Shimmer Pool" heading
  - Add `ActionType.Shimmer` enum value (new card style with purple accent)
  - Recommendation: Use existing `ActionType.Explore` since shimmer pool is an exploration target
- **#6 Difficulty**: Source descriptions already embed "[Expert+]" tags — no card change needed
- **#4 Shop Conditions**: Sub-line showing when the shop is accessible

### Craft Order Panel (UICraftStep)
Step-by-step crafting instructions.
- **#2 Conditions**: Structured condition info with met/unmet coloring (green vs orange)
- **#5 Shimmer**: New "Shimmer" step type — "Step 3: Shimmer Tissue Sample → Shadow Scale" with purple styling. UICraftStep needs to handle shimmer steps (no crafting station, different icon).
- **#7 Biome**: "Craft at: Work Bench (Graveyard)" biome suffix

### Material List Panel (MaterialListPanel)
Shows missing/owned materials with source tags.
- **#1 Ore Groups**: Correct ore item shown as material (Lead Bar, not Iron Bar)
- **#3 Hardmode Unknown**: "Cobalt or Palladium Bar" dual display when ores unknown
- **#5 Shimmer**: Materials obtainable via shimmer already show "Shimmer: X" source tag. But if shimmer is now a resolved craft path, the material might not appear in the "Missing" list at all (it's handled by the shimmer step). The shimmer source item (e.g., Tissue Sample) would appear instead. This is correct behavior — just verify it works.
- **#6 Difficulty**: Source tags filtered by difficulty — "[Expert+]" sources hidden in Normal worlds

### Dependency Tree Panel (UITreeNode)
Expandable recipe tree.
- **#2 Conditions**: Condition text on craft nodes — already shown in orange. Enhancement: met conditions in green.
- **#5 Shimmer**: ShimmerNode or shimmer-flagged CraftNode needs rendering. Purple "shimmer" icon/label instead of "craft" icon. Tree would show: Target → CraftNode → ... → ShimmerNode(Tissue Sample → Shadow Scale) → AcquireNode(Tissue Sample).
- **#7 Biome**: Biome requirement shown alongside station text

### Player Dashboard
Ready/Almost/Building sections from UpgradeItemBuilder.
- **#2 Conditions**: Items behind unmet conditions (e.g., post-Plantera recipe) get a small score penalty → may move from "Almost Ready" to "Building Toward" if all recipe paths are condition-locked
- **#5 Shimmer**: Items previously impossible (cross-evil) now appear as resolvable upgrades. Dashboard may gain new entries.
- **#6 Difficulty**: Expert-only items filtered out of Normal world dashboard

### Upgrade Recommendations Window (UpgradeRecommendationsPanel)
Left column: upgrade categories. Right column: source priority.
- **#1 Ore Groups**: Correct ore in material lists
- **#4 Shop Conditions**: Shop location cards show availability
- **#5 Shimmer**: "Shimmer Pool" as a location in the right column if shimmer enables cross-evil paths
- **#6 Difficulty**: Expert-only items filtered from recommendations

### Source Priority / "Where to Go" (SourcePriorityAnalyzer + UILocationCard)
Ranked biome cards in upgrade window.
- **#4 Shop Conditions**: Shop cards show when items are available
- **#5 Shimmer**: "Shimmer Pool" location card appears when shimmer enables new paths
- **#6 Difficulty**: Boss locations only credit drops the player can actually get at current difficulty

### Search / Target Selection (TargetSelectionPanel)
Item search — no impact from any improvement. Search is name-based only.

### Boss Progression Card (UIBossProgressionCard)
Shown in upgrade window header.
- **#6 Difficulty**: Could show difficulty-specific drop info, but current card is already adequate
