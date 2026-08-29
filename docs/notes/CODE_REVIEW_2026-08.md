# Code Review — August 2026

Analysis pass over the whole mod after ~5 months idle. Every finding below is
cited to `file:line` and was verified by reading the code.

**Caveat that shapes everything here:** the mod could not be compiled during
this review (see B1). Nothing was run, no timings were measured. Findings are
static-analysis grade: "this allocates on the DFS path" is defensible, "this
costs 200ms" is not claimed anywhere.

---

## Top 8, ranked by impact

| # | Finding | Lane | Severity |
|---|---------|------|----------|
| B1 | Build is unreproducible — `.csproj` hardcodes an unmounted volume | Backend | **Blocker** |
| B2 | `GetRecipeIndex` is an O(n) scan over all recipes, called inside the DFS | Backend | **High** |
| B3 | Live mutable state crosses the `Task.Run` boundary — races + a dropped-resolve path | Backend | **High** |
| B4 | `CacheVersion` is write-only — the whole invalidation mechanism is inert | Backend | **High** |
| D1 | `ChestLoot` source type is consumed in 8 files but never produced | Data | **High** |
| D2 | `SourceEntityId` holds two incompatible ID domains; already producing wrong icons/names | Data | **High** |
| D3 | Drop rates drop the numerator — a 3/4 drop is recorded as 1/4 | Data | **High** |
| U1 | Craft-step hints color-dispatch on string prefixes; ~2/3 render as "unobtainable" red | UI/UX | **Medium** |

Plus a load-order hypothesis (H1) that would outrank most of these if confirmed,
and four factual errors in the August gathering data (W3).

**On method:** findings below are mine unless marked *[codex]*, which denotes an
independent GPT‑5.6 review run against the same tree. Every codex finding
reproduced here was re-verified against the code myself; where I disagree with
its reading, I say so. Its claims that depend on external documentation are
flagged separately as unverified (H1) — it ran sandboxed without network, so its
citations are model recall, not fetched sources.

---

## B1. The build is unreproducible — **fixed in `af32d58`**

> **Resolved.** `tMLPath` now resolves from the command line, an untracked
> `Directory.Build.props`, the `TMLPATH` environment variable, or the OS
> default Steam location. All four paths verified against a stub
> `tMLMod.targets`. Still outstanding: nobody has compiled the mod against a
> real tModLoader install, so the 24 August commits remain unverified. The
> original finding is kept below for context.

`ProgressionCompass.csproj:2` imports:

```
/Volumes/DevDrive1/SteamLibrary/steamapps/common/tModLoader/tMLMod.targets
```

`DevDrive1` is the old Mac mini's drive and is not mounted. That single absolute
path is what blocks compiling, testing, and therefore verification of every
other finding in this document — including the 24 commits pushed in August,
none of which have ever been compiled.

Worth fixing first, because it gates everything else. The usual approach is an
MSBuild property with an env-var fallback, so the path is per-machine rather
than committed:

```xml
<Import Project="$(tMLPath)/tMLMod.targets" Condition="'$(tMLPath)' != ''" />
```

...with `tMLPath` set in `Directory.Build.props` (gitignored) or the environment.

Related: `CLAUDE.md` points `PLAN.md` at `/Volumes/DevDrive1/Projects/Terraria/PLAN.md`,
which also no longer exists. The real file is at the repo parent, `../PLAN.md`.

## B2. `GetRecipeIndex` — O(recipes) linear scan inside the DFS

`DependencyResolver.cs:923-932`:

```csharp
private static int GetRecipeIndex(Recipe recipe)
{
    for (int i = 0; i < Recipe.numRecipes; i++)
        if (ReferenceEquals(Main.recipe[i], recipe))
            return i;
    return -1;
}
```

This is called at `DependencyResolver.cs:236`, inside `TryRecipeCandidates`,
which is inside the recursive resolve path (`ResolveItem` → `TryRecipeCandidates`
→ `ResolveItem`, `MaxDepth = 25`, `MaxRecipesPerItem = 15`). So every candidate
recipe at every node costs a full scan of the recipe array.

Vanilla is ~5,700 recipes; Calamity and Thorium push it well past 10,000. The
work is `O(nodes × 15 × numRecipes)` reference comparisons per resolve — and
it exists only to produce a stable int key for the `visited` cycle-detection set.

The fix is small and the infrastructure is already there. `RecipeIndexSystem`
already walks `Main.recipe[]` once in `PostAddRecipes` (`RecipeIndexSystem.cs:33`).
Add a reference-keyed dictionary in that same loop:

```csharp
public Dictionary<Recipe, int> RecipeIndices { get; private set; }
// in the existing loop:
RecipeIndices[recipe] = i;
```

...built with `ReferenceEqualityComparer.Instance`, then `GetRecipeIndex` becomes
an O(1) lookup.

The same scan is duplicated verbatim at `DependencyTreePanel.cs:137-143`. It runs
per recipe-cycle click rather than per DFS node, so it is far less hot — but it
should use the same index once one exists.

**Latent correctness bug in the same function:** it returns `-1` when a recipe
isn't found, and `-1` is then added to the `visited` set (`DependencyResolver.cs:248`).
Once any recipe resolves to `-1`, every *other* unfound recipe is treated as
already-visited and skipped. A dictionary lookup makes this unreachable, but if
`-1` is kept as a sentinel it should bypass `visited` entirely.

## B3. Live mutable state crosses the async boundary

`SystemSnapshot` correctly captures main-thread game state and the background
task works on a copied inventory (`CraftPathState.cs:420-433`) — that part of
the Tier 1–3 architecture work is sound. But three mutable objects are still
shared live across the boundary rather than snapshotted:

**`_recipePreferences`** — passed by reference into `ResolveAll` on the worker
(`CraftPathState.cs:432`), read during the DFS at `DependencyResolver.cs:332`,
and written on the main thread at `CraftPathState.cs:285`. Concurrent
read/write on a `Dictionary` is unsafe: it can throw or corrupt internal state.

**`TargetQueue._targets`** — a plain `List<TargetItem>` (`TargetQueue.cs:13`),
enumerated on the worker in `ResolveAll` (`TargetQueue.cs:81`) while the main
thread can `Add`/`Remove`/`Clear` it (`TargetQueue.cs:21-52`).

This one has a concrete, reachable failure path. `OnDashboardAnalyze`
(`CraftPathState.cs:292-297`) does:

```csharp
_queue.Clear();
_queue.Add(itemType);
RunResolver();
```

`RunResolver` guards on `if (_resolving) return`, but `Clear()` and `Add()`
have already mutated the list. Clicking **Analyze** on the dashboard while a
resolve is in flight modifies the collection mid-enumeration →
`InvalidOperationException`. It is swallowed by the worker's catch
(`CraftPathState.cs:435`) and logged as "Resolver error", so the game does not
crash — the click silently does nothing. That is exactly the kind of
intermittent "sometimes the button doesn't work" bug that is miserable to
diagnose from a bug report.

**`ResolverCache._cache`** — `_cache.Store(...)` runs on the worker
(`TargetQueue.cs:93`) while the main thread can call `InvalidateCache()` →
`_cache.Clear()` from `OnRecipeCycled` (`CraftPathState.cs:286`). Same
unsynchronized-Dictionary problem.

**Also unsynchronized, lower severity:** `_pendingResult`, `_pendingProgressResult`,
`_resolving`, `_scanningProgress` (`CraftPathState.cs:40-56`) are plain fields
written on the worker and read on the main thread with no `volatile`, `lock`, or
`Interlocked`. On ARM64 — which is what this machine and every Apple Silicon
tModLoader install runs — the weak memory model permits the main thread to
observe `_resolving == false` before it observes the `_pendingResult` write. The
practical risk is a second resolve starting while the first result is still
unpublished, and the two racing at `CraftPathState.cs:554-555`.

Suggested direction: snapshot `_recipePreferences` and the target list into the
closure the same way inventory already is, and give the worker its own cache
instance (or guard the shared one). Marking the four status fields `volatile` is
a one-line hardening on top.

## D1. `ChestLoot` is scored, colored, and bucketed — but never produced

`ItemSourceType.ChestLoot` is consumed in 11 places across 8 files:

- `DependencyResolver.cs:527` — acquire cost
- `EffortAnalyzer.cs:133` — effort weight `5f`
- `ItemSource.cs:52` — recommendation score `30`
- `ScoringConstants.cs:42` — `AccessibilityChestLoot = 0.4f`
- `SourcePriorityAnalyzer.cs:245,262` — accessibility + `LocationType`
- `ActionClassifier.cs:147` — explore bucket
- `BiomeMapper.cs:120` — location string
- `GearRecommender.cs:241` — `AcquisitionMethod.Exploration`
- `UIHelper.cs:127` — source color

Nothing anywhere constructs an `ItemSource` with that type. `ItemSourceSystem`
has nine indexing entry points (`IndexNPCDrops`, `IndexNPCShops`,
`IndexSpecialShop`, `IndexItemDrops`, `IndexGatheringSources`,
`BackfillOresFromTileSets`, `IndexCritterSources`, `IndexShimmerTransforms`,
`IndexShimmerDecrafting`) and none of them emits `ChestLoot`.

So world-chest loot — Shadow Chest (Ball O' Hurt, Sunfury, Dark Lance), Dungeon
chests (Muramasa, Cobalt Shield, Handgun), Water Chests (Trident, Breathing
Reed), Sky Chests (Starfury, Lucky Horseshoe), Living Wood, Ivy Chests, and the
Hardmode biome-chest keys — has zero coverage. For a mod whose pitch is "tells
you exactly where to get it", that is the largest single data gap, and it is
pre-hardmode-heavy, which is where a progression assistant matters most.

The scoring scaffolding is already in place, so this is an indexing task, not a
design task. Vanilla chest contents are generated in `WorldGen`, not exposed as
a queryable table — the practical route is a curated static map of
chest-type → item list (the same shape as `BossDatabase`), which also sidesteps
the fact that modded chest loot is unknowable without per-mod integration.

Note the contrast with the mod description ("scans inventory, banks, equipment,
chests") — that refers to reading *nearby physical chests* for items you already
own (`InventoryScanner`), which is unrelated to knowing what loot *generates* in
world chests. Both are useful; only the first exists.

## U1. Craft-step hints color-dispatch on string prefixes

`UICraftStep.cs:152-157` picks the annotation color by matching the hint string:

```csharp
annotColor = hint.StartsWith("Drop:")  ? ...NPCDrop
           : hint.StartsWith("Buy:")   ? ...NPCShop
           : hint.StartsWith("Mine:")  ? ...Gathering
           : hint.StartsWith("Open:")  ? ...BagDrop
           : new Color(255, 150, 150);   // reddish fallback
```

The hint is `ItemSource.PickBest(sources)?.Description`
(`DependencyResolver.cs:840`) — a free-text string. The actual prefixes the
source system produces, counted across `ItemSourceSystem.cs`, `BiomeMapper.cs`,
and `BossDatabase.cs`:

```
43 Mine:      13 Drop:      5 Decraft:    2 Open:       1 Shimmer:
15 Gather:    13 Dig:       4 Buy:        2 Material:   1 Event:
12 Chop:       3 Fish:      1 Smelt:      1 Catch:      1 Boss:
```

Only `Mine:`, `Drop:`, `Buy:`, `Open:` are recognized — roughly 62 of ~120
strings. Everything else (`Gather:`, `Dig:`, `Chop:`, `Fish:`, `Catch:`,
`Shimmer:`, `Event:`, `Boss:`) falls through to the reddish fallback, which is
the same color the UI uses for "need to acquire, no idea how". So a material you
can pick up off the ground renders as if it were a dead end.

This was already wrong before August, and commit `f1249ac` roughly doubled the
affected count by adding ~30 `Dig:`/`Gather:`/`Chop:`/`Catch:` entries.

The fix is straightforward and removes the fragility entirely: `CraftStepInput`
already carries `AcquireSources` (`ResolverResult.cs:73`), so the typed source is
in hand at the call site. Replace the prefix chain with

```csharp
UIHelper.GetSourceColor(ItemSource.PickBest(input.AcquireSources)?.Type ?? ...)
```

`GetSourceColor` (`UIHelper.cs:118-130`) already covers every enum value.

---

## The August WIP commit (`f1249ac`)

~250 lines, never compiled. Audited specifically.

**W1 — `Catching` missing from two UI allowlists.** Ten files were updated for
the new enum value; two pattern-match sites were missed. Neither breaks the
build:

- `UILocationCard.cs:177-179` gates NPC-head-icon rendering on a type allowlist
  (`NPCDrop`, `BossDrop`, `NPCShop`, `Event`, `BagDrop`). `Catching` sources
  store the **critter's NPC id** in `SourceEntityId`
  (`ItemSourceSystem.cs`, `AddSource(catchItemId, new ItemSource(..., npcId))`),
  so the id is valid and usable — but the card falls through to
  `DrawItemIcon` at `:185-186` and draws the item instead of the critter.
- `UILocationCard.cs:275-280` — `GetNpcName` has the same allowlist and returns
  `null` for `Catching`, so the hover shows the item tooltip instead of the
  critter's name.

*[codex] read this exclusion as deliberate — "so they use the caught item icon
rather than interpreting its NPC ID as another source category." I don't agree,
and D2 below is why: the allowlist is wrong at both ends, not conservative.*

**W2 — `Catch:` locations resolve to the tool name, not the biome.** The new
critter descriptions are built as `Catch: {location}{rareTag} ({tool})`
(`ItemSourceSystem.cs:850-856`). `BiomeMapper.GetSourceLocations` routes
`Catching` through `InferLocationFromDescription` (`BiomeMapper.cs:114-118`),
which takes the **last** parenthetical (`LastIndexOf('(')`, `BiomeMapper.cs:167`)
— which is always the tool. So `"Catch: Surface (Bug Net)"` yields the location
`"Bug Net"`, and the UI renders **"Catching: Bug Net"** as the place to go.
`Catch: ` is also absent from the prefix-strip list at `:177`, so there is no
fallback. Every critter source is affected. *[codex]*

**W3 — four factual errors in the new gathering data.** These matter more than
display text, because `AcquireCostGathering = 0` (`ScoringConstants.cs:61`) — a
bogus gathering entry is a *free* route the resolver will prefer over the real
one. *[codex]*, verified against the code and Terraria mechanics:

- `ItemSourceSystem.cs:727` — Mana Crystal, `"Craft or gather: 3 Fallen Stars"`.
  The recipe takes **5** stars, not 3, and this registers as `Gathering`, giving
  Mana Crystal a zero-cost acquisition arm that beats its own recipe. The
  trivial-recipe override can't rescue it, because that path requires an output
  stack ≥ 2 (`DependencyResolver.cs:563-565`) and Mana Crystal makes one.
- `:725` — Rain Cloud, `"Gather: Sky during rain"`. Rain Clouds generate beneath
  Floating Islands; rain is unrelated.
- `:728` — Life Fruit, `"Hardmode, post-Plantera"`. Life Fruit begins growing
  after **any** mechanical boss, which is a materially earlier gate.
- `:745-749` — Corrupt/Crimson/Hallowed Seeds as `"shake grass"`. Their real
  sources are the Dryad's shop and Eye of Cthulhu.

The general rule this implies: a craftable item should never be encoded as a
`Gathering` source, and gathering entries need structured world-state/tier
fields rather than prose that the scorer can't read.

**W4 — ore backfill skips too broadly.** `BackfillOresFromTileSets` skips an ore
if `_sourcesByItem` contains *any* entry for it (`ItemSourceSystem.cs:781`),
rather than only if it already has a **Gathering** entry. A modded ore that also
drops from an enemy therefore never gets its mining route. *[codex]*

**W2 — new allocation on the resolve path.** `FilterDecraftWhenAlternativesExist`
(`ItemSourceSystem.cs:38-65`) allocates a new `List<ItemSource>` for any item
that has both decraft and real sources. It runs inside `GetSources(itemType)`
(`:22-28`), which `GetSources(itemType, worldContext)` calls at `:71` before
allocating a *second* list at `:76`. Two allocations per lookup.

That matters more than it used to, because the same commit added
`HasPassiveSource` (`DependencyResolver.cs:588-600`), which calls
`GetSources(itemType, worldContext)` **per ingredient** inside `IsRecipeTrivial`
— which runs inside `TryRecipeCandidates` on the DFS path. Compounding it,
`IsSourceAvailableInWorld` calls `desc.ToLowerInvariant()` per source
(`ItemSourceSystem.cs:101`), allocating a string each time.

This partially undoes the dictionary-pooling and caching work from `fe409d1`
and `99b6451`. The cheapest fix is to memoize the filtered list per item type
at index time rather than filtering per call — the decraft set is static after
load, so the filtering could happen once in `PostSetupContent` instead of on
every lookup.

**W3 — load-time cost, stated not measured.** `IndexCritterSources` constructs
`new NPC()` and calls `SetDefaults(npcId)` for every id in
`NPCID.Sets.CountsAsCritter`, and `BackfillOresFromTileSets` walks all of
`ContentSamples.ItemsByType`. Both are one-time at `PostSetupContent`. Probably
fine; worth a stopwatch log once the build works, since mod load time is a
common complaint vector.

**W4 — the commit is now the largest single contributor to two tracked debts.**
It added bare `catch { }` blocks (`ItemSourceSystem.cs`, in both new methods)
and ~30 hardcoded English strings. See the ISSUES.md corrections below.

---

## B4. `CacheVersion` is write-only — the mechanism is inert

`CacheVersion` (`CacheVersion.cs`) documents itself as the mod's global
invalidation coordinator: *"All caches across the mod check this version to
determine if their data is stale."* No cache checks it. Full usage audit:

| Call | Sites |
|---|---|
| `Bump()` | 3 — `TrackingPlayer.cs:41`, `CraftPathState.cs:117`, `:287` |
| `Reset()` | 1 — `RecipeIndexSystem.cs:85` |
| `Current` | **1** — `CraftPathState.cs:528`, only to stamp `ProgressScanResult.Version` |

And `ProgressScanResult.Version` is never read — `rg '\.Version\b'` across the
tree returns nothing. Its own doc comment says *"Stale if < CacheVersion.Current"*,
but no comparison exists anywhere.

So the entire versioning layer is write-only. `ResolverCache` never references
`CacheVersion` at all (`ResolverCache.cs`, `TargetQueue.cs`), and relies solely
on its inventory hash. This is worth knowing precisely because the architecture
*looks* like it has coordinated invalidation — a future change that assumes the
version guard is live would be assuming something untrue.

Related, and sharper than the `ResolverCache` note further down *[codex]*: the
inventory hash folds only `AvailableCount`, not the personal/equipped/chest/
Magic Storage breakdown, nor world evil, difficulty, boss state, tier, or
recipe preference. Two materially different states can hash identically.

Either wire the version into `ResolverCache.TryGet`/`Store` and bump it on world
and tier changes, or delete `CacheVersion` and stop implying a guarantee that
isn't there. The current middle state is the worst of both.

## D2. `SourceEntityId` carries two incompatible ID domains

`ItemSource.SourceEntityId` is a bare `int` that means **NPC id** for some source
types and **item id** for others:

- NPC/boss/event drops store an NPC id (`ItemSourceSystem.cs:284`, `npcId`)
- `Catching` stores a critter NPC id
- **Bag drops and fishing crates store the bag/crate *item* id** —
  `ExtractDropsFromItemRule(rule, itemType, …)` passes the container's item type
  through to `AddSource(…, sourceItemType)` (`ItemSourceSystem.cs:573-604`)

Consumers don't distinguish. `UILocationCard.cs:177-179` includes `BagDrop` in
its NPC-head-icon allowlist, and `GetNpcName` (`:275-288`) calls
`npc.SetDefaults(source.SourceEntityId)` on it — feeding a **Treasure Bag's item
id into an NPC lookup**. The result is either a wrong NPC's head and name, or a
silently-swallowed exception at `:288`. `BiomeMapper.cs:61-74` makes the same
assumption. *[codex]*

This is live today and unrelated to the August work. It also settles W1: the
allowlist is wrong at *both* ends — it includes `BagDrop`, whose id is not an
NPC, and excludes `Catching`, whose id is. That's a bug, not a conservative
choice.

The durable fix is to stop overloading the integer — separate `NpcType` /
`ContainerItemType` fields on the record, so the compiler enforces the
distinction. The narrow fix is to correct the two allowlists.

## D3. Drop rates discard the numerator

`CommonDrop` extraction records the chance as `1f / common.chanceDenominator` in
both the NPC path (`ItemSourceSystem.cs:285`) and the bag/crate path (`:604`),
ignoring `chanceNumerator` entirely. A 3/4 drop is stored and displayed as 1/4.
*[codex]*

This is not cosmetic: `DropRate` feeds `HasPassiveSource`'s `>= 0.1f` threshold
(`DependencyResolver.cs:597`), `RecommendationScore`'s `>= 0.1f` branch
(`ItemSource.cs:50`), and `EffortAnalyzer.DropEffort`. Underrating a common drop
pushes the resolver toward worse routes.

Two adjacent problems in the same code *[codex]*, both verified:

- `OneFromOptionsDropRule` and its not-scaled-with-luck variant record **no
  rate at all** (`:290-296`) — the `ItemSource` is constructed without the
  `DropRate` argument, so every option-drop defaults to 0.
- Chained rules recurse with the parent's description and source type
  (`:606-608`) but not its probability, so a 1/10-gated 1/2 sub-drop is
  presented as 1/2.

## H1. Load-order hypothesis — needs in-game verification

*[codex]* rates this its top finding: that `PostSetupContent` runs **before**
tModLoader finalizes bestiary data, NPC/item drop tables, and recipe
registration — so `IndexNPCDrops`, `IndexItemDrops`, `IndexGatheringSources`
(including the new bestiary-backed critter scan), and `IndexShimmerDecrafting`
(`ItemSourceSystem.cs:178-194`) would all read incomplete data, missing modded
`ModifyNPCLoot` / `ModifyItemLoot` contributions entirely.

**I could not verify this and it should not be treated as established.** The
tModLoader assemblies are on the unmounted volume, and codex ran sandboxed
without network — the loading-order documentation it cites is model recall, not
a fetched source.

Two things make it worth taking seriously anyway. The code already
demonstrates load-order awareness in exactly this area: `ItemSourceSystem.cs:180-183`
defers shop indexing to `PostAddRecipes` with the comment *"NPCShopDatabase
isn't populated until after AddShops() runs (which is after PostSetupContent)"*.
So the author hit one instance of this class of problem already. And the failure
mode is quiet — missing modded drops look like sparse data, not like a bug.

**Instrumentation is now wired up** (`Common/Systems/LoadOrderDiagnostics.cs`).
It samples recipe count, NPC drop tables, item drop tables, bestiary spawn
conditions, and catchable critters at `PostSetupContent`, `PostAddRecipes`,
`PostSetupRecipes`, and `OnWorldLoad`, splitting each into vanilla vs modded —
the modded half is what the hypothesis is actually about. One line per stage
into `client.log`, prefixed `[loadorder]`. `/cpo loadorder` gives the same
reading live in-game plus the current `ItemSourceSystem` index size.

**Reading the result:** if the `PostSetupContent` line already matches the
later lines, H1 is disproven and the indexing is fine where it is. If the
numbers climb after it — particularly the `m` (modded) figures — the index is
being built against incomplete data and should move to `PostSetupRecipes`.
Run once with only this mod, then again with Calamity or Thorium enabled; the
vanilla/modded split is what separates "tModLoader isn't ready yet" from
"vanilla is ready but mods haven't contributed yet".

Diagnostic only — reads game state, changes nothing. Delete the file and the
`loadorder` case in `CraftPathTestCommand` once this is settled.

## Architecture

**`ItemSourceSystem` is a god object.** 1,058 lines handling NPC drops, shops,
special shops, item drops, gathering, ore backfill, critters, shimmer
transforms, shimmer decrafting, world-context filtering, decraft filtering,
property-based inference, and a reflection fallback for unrecognized modded drop
rules. The evidence that this is a real problem rather than an aesthetic
complaint: **every wave in every roadmap doc lists "Files to Modify:
ItemSourceSystem.cs"**. It is the bottleneck for all future data work.

Splitting it into per-source indexers behind a common interface
(`INpcDropIndexer`, `IShopIndexer`, `IGatheringIndexer`, …) with the ModSystem
reduced to composition + the `_sourcesByItem` dictionary would let the planned
work proceed in parallel without collision — and would give `ChestLoot` (D1) an
obvious home.

**Three `UserInterface` instances, one mouse-blocking participant.**
`CraftPathUISystem`, `UpgradeWindowSystem`, and `TrackingOverlaySystem` each own
one, but only `UpgradeWindowSystem` exposes `IsMouseBlocked`
(`UpgradeWindowSystem.cs:30`), and `UIDraggablePanel.cs:34` checks only that one.
`CLAUDE.md` explicitly warns that a third window requires either extending the
pattern or building a shared `WindowManager` — the third window now exists and
does neither. This is also the blocker for UI_UX_IMPROVEMENTS §6
("Interactive Overlay"): making the overlay clickable without a window manager
will produce click-through bugs.

**`DetectPrimaryBar` is duplicated verbatim** in `UpgradeWindowSystem.cs:488`
and `PlayerDashboard.cs:234`. Commit `fdd3f7b` acknowledges this in its own
message ("DetectPrimaryBar duplicated from UpgradeWindowSystem (same logic)").
Belongs in `UIHelper` or `ProgressionTier`.

**`ResolverCache` keying.** The cache key is `(itemType, quantity)`
(`ResolverCache.cs:29-37`) and validity is gated solely on an inventory hash.
Tier and `WorldContext` are not part of the key, so a tier change with an
unchanged inventory hash would serve a stale tree. Recipe-preference changes
*are* handled correctly (`OnRecipeCycled` calls `InvalidateCache` at
`CraftPathState.cs:286`), so this is a latent gap rather than an active bug.
Separately, `ComputeInventoryHash` (`:44-58`) accumulates `hash * 31 + …` while
iterating a `Dictionary` — order-dependent accumulation over an unordered
collection. It works in practice because the dictionary is copy-constructed
without removals, but it is fragile by construction; summing or XOR-ing
per-entry hashes would be order-independent.

## Further findings from the independent review

All *[codex]*-sourced, listed with my verification status. I confirmed the code
reads as described in each case; the severity calls are mine.

- **Multi-target accounting destroys Magic Storage counts.** `SubtractClaimed`
  computes `used` including `MagicStorageCount` but subtracts only personal,
  equipped, and chest quantities, then rebuilds a three-field entry
  (`TargetQueue.cs:106-131`). The Magic Storage count is dropped wholesale even
  when none of it was consumed, so later queued targets are told the stock is
  gone. Verified — the replacement entry genuinely omits the field.
- **Evil-affiliation inference is registration-order dependent.**
  `EvilAffiliationMap` `TryAdd`s the first evil-affiliated recipe it sees
  (`:162-173`), then refuses to reconsider (`:189-197`), and neutral recipes
  never disprove an "evil-only" classification (`:242-262`). An item craftable
  via Corruption *or* Crimson *or* a neutral path can be filtered by whichever
  recipe registered first — the Night's Edge case the code's own comment says
  should stay neutral. Aggregating all recipes before assigning would fix it.
- **Acquire costs and recipe costs are in different units.** A nested
  `AcquireNode` costs its raw quantity (`GraphNode.cs:36-59`), while direct
  acquisition uses a flat per-source-type constant independent of quantity
  (`DependencyResolver.cs:500-538`). So one rare boss drop costs `1` inside a
  recipe but `8` standalone, and gathering 999 of something costs `0`. This is
  the root cause the W3 data errors exploit, and it's the deepest of these — a
  quantity-aware cost function is a real piece of work, not a patch.
- **`IsLavaBait` is the right classifier for lava critters.** The new code uses
  `npc.lavaImmune` (`ItemSourceSystem.cs:823-847`); `ItemID.Sets.IsLavaBait` is
  the purpose-built set. Plausible and low-risk to adopt; I could not verify the
  set's exact semantics without the assemblies.
- **`IndexItemDrops` only accepts boss bags and crates** despite a comment
  promising "other grab-bag items" (`:542-559`), and direct fishing catches are
  never indexed — `Fishing` sources come only from crate *contents* plus
  tooltip-word guessing (`:150-165`). Directly-fished gear (Reaver Shark,
  Sawtooth Shark, the Hardmode fishing rods) has no route.
- **Modded bosses are permanently "undefeated"** (`BossDatabase.cs:319-332`),
  and modded boss tier inference passes a rarity value into an item-tier API
  (`:341-355`). Both plausible from reading; both need a Calamity run to confirm
  the user-visible effect.
- **`TrackingGlobalItem.OnPickup` refreshes before the item lands.** It toasts
  and synchronously resolves at pickup time (`TrackingGlobalItem.cs:14,40-48`),
  which codex says runs before the inventory mutation and can fire repeatedly
  when inventory is full. The periodic repair hashes only tracked-goal output
  counts, not leaf materials (`TrackingPlayer.cs:70-103`), so a stale refresh
  may never self-correct. The tModLoader-behavior half is unverified (same
  no-network caveat as H1); the hash-scope half I confirmed by reading.

## Other data-sourcing notes

- **`IndexShimmerDecrafting` then filtering most of it back out.** The August
  commit added `FilterDecraftWhenAlternativesExist` to suppress the output of
  an indexer added earlier (`ItemSourceSystem.cs:894`). It works, but indexing
  data in order to hide it is a sign the decraft entries want a lower
  `RecommendationScore` (they are already at `25`, `ItemSource.cs:56`) or a
  `IsFallback` flag rather than post-hoc string matching on `"Decraft:"`.
- **String-matching on descriptions is load-bearing in three places** —
  `IsSourceAvailableInWorld` matches `"corruption"`/`"crimson"`
  (`ItemSourceSystem.cs:103-106`), the decraft filter matches `"Decraft:"`
  (`:46`), and `UICraftStep` matches four prefixes (U1). Each is a place where
  adding a source string silently changes behavior elsewhere. Structured fields
  on the `ItemSource` record would remove all three.
- `ItemSourceType.Other` is produced once (`ItemSourceSystem.cs:573`, the
  non-crate non-bag branch of `IndexItemDrops`) and has no dedicated handling in
  most consumers — it lands in `_ =>` arms. Worth checking what actually flows
  through it.

## UI/UX notes

- **`TargetSelectionPanel.FilterItems` iterates every item in the game twice per
  keystroke** (`TargetSelectionPanel.cs:165-171` to count, then `:174-184` to
  build), calling `name.ToLowerInvariant()` on each pass — two string
  allocations per item per keystroke. With Calamity loaded that is on the order
  of 10,000 allocations per typed character. Both passes can collapse into one,
  and `name.Contains(search, StringComparison.OrdinalIgnoreCase)` allocates
  nothing.
- **The UI is otherwise allocation-disciplined** — no LINQ anywhere under `UI/`,
  and only 15 collection-allocation sites in the whole directory. Worth
  preserving deliberately.
- **Localization is effectively unimplemented.** `en-US.hjson` is 111 lines and
  `Language.GetText` appears in exactly two files
  (`BiomeMapper.cs`, `TargetSelectionPanel.cs`). Everything else is hardcoded
  English, including all ~120 source-description strings. This is tracked in
  ISSUES.md but its scope is understated — it is not a handful of UI labels, it
  is the entire data layer.
- **No `icon.png`.** Confirmed absent; Steam Workshop requires 396×396. Still a
  genuine release blocker.

---

## Reconciliation: the planning docs are stale

The six planning docs read as forward-looking TODO lists, but most of what they
describe has been built. Spot-checked against the code:

| Doc item | Doc status | Actual |
|---|---|---|
| R1.1 Ore-aware recipe groups | planned | **implemented** — `FindAcceptedGroup`, `ResolveGroup` |
| R1.2 / R1.7 Recipe conditions | planned | **implemented** — 41 references in `DependencyResolver` |
| R1.4 NPC shop conditions | planned | **implemented** — `WorldContext.IsNpcAvailable` |
| R1.6 Boss drop difficulty variants | planned | **implemented** — `MinDifficulty`, `GameDifficulty` |
| R2.1 Bestiary spawn conditions | planned | **implemented** — 13 references in `BiomeMapper` |
| R2.2 Crafting station equivalence | "Files to **Create**" | **exists** — `CraftingStationMap.cs`, 130 lines |
| R2.3 Accurate shop prices | planned | **implemented** |
| R2.6 Fishing crate biome mapping | planned | **implemented** — 28 crate references |
| R2.7 Shimmer decrafting | planned | **implemented**, then partly filtered back out |
| ACC.1–4 Accessory scoring | planned | **partial** — `AccessoryEffectAnalyzer` exists; differential field analysis (ACC.3) not found |

**`CLAUDE.md` is the worst offender and actively misleads.** It describes the
mod as `CraftPathOptimizer` throughout: the project name, the directory tree,
the namespace, and — most damagingly — the instruction to write
`global::CraftPathOptimizer.X.Y` for cross-namespace references. The real root
namespace is `ProgressionCompass` (`ProgressionCompass.csproj:5`), and the
codebase uses `global::ProgressionCompass.…`. It also documents a `/cpo` chat
command and points `PLAN.md` at the unmounted DevDrive1. Anyone — human or
model — following it will write code that does not compile.

**ISSUES.md miscounts its own top item.** It claims "212 try-catch blocks with
no logging". Actual counts:

- 85 `catch` blocks total
- 33 silent (`catch { }` with an empty body)
- 18 that log

So the problem is real but ~2.5× smaller than tracked, and it is concentrated:
`ItemSourceSystem.cs` (7 silent) and `CraftPathState.cs` (6) hold nearly half.
Worth correcting, because a "212 sites" number reads as a rewrite while "33
sites, half in two files" reads as an afternoon.

---

## Suggested order

**First — unblock verification.** B1 (`.csproj` path). Nothing else can be
confirmed until the mod compiles, and there are 24 uncompiled commits on `main`.
Expect the `Catching` work to need a compile-fix pass.

**Then — settle H1,** because it is cheap once the build works and it changes
what everything else is worth. If source indexing really is reading pre-finalized
data, then modded coverage is broken at the root and that outranks every
individual data fix below it.

**Then — the WIP items,** while the August diff is still fresh: W3 (the four
wrong gathering entries — these actively mis-route, and Mana Crystal is a
one-character fix), W2 (`Catch:` → tool name), W1 + D2 (the two allowlists,
fixed together since D2 shows both ends are wrong), W4, then the filtered-list
memoization.

**Then — correctness and cost, in this order:** B3 (async boundary — reachable
silent-failure path), D3 (drop-rate numerator — wrong data feeding three
different scorers), B2 (`GetRecipeIndex` — biggest algorithmic win, ~5 lines on
infrastructure that already exists), B4 (decide whether `CacheVersion` is real
or delete it), U1 (hint colors — small fix, disproportionately visible), then
`TargetSelectionPanel` double-iteration.

**Deferred but worth scheduling:** the acquire-cost/recipe-cost unit mismatch.
It is the deepest issue in this document and the one the W3 data errors were
able to exploit, but it is a design change rather than a fix.

**Then — the doc debt:** rewrite `CLAUDE.md` for the real mod name and
namespace, correct the ISSUES.md count, and mark the implemented roadmap items
as done. Cheap, and it stops the next session from starting on false premises.

**Then — the largest genuine feature gap:** D1, chest loot indexing. Best done
after (or alongside) decomposing `ItemSourceSystem`, since that is where it
lands.
