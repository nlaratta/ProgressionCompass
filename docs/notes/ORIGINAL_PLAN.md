# Craft Path Optimizer — Comprehensive Implementation Plan

> A tModLoader mod that computes optimal crafting paths for any item, shows what you
> own vs. what you need, tells you *where* to get missing materials, and works
> seamlessly with Calamity and other content mods.

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Technical Foundation](#2-technical-foundation)
3. [Architecture Overview](#3-architecture-overview)
4. [Core Systems — Detailed Design](#4-core-systems--detailed-design)
5. [UI Design](#5-ui-design)
6. [Algorithm Deep-Dive: The Dependency Resolver](#6-algorithm-deep-dive-the-dependency-resolver)
7. [File-by-File Specification](#7-file-by-file-specification)
8. [Phase Breakdown](#8-phase-breakdown)
9. [Mod Compatibility Strategy](#9-mod-compatibility-strategy)
10. [Testing Strategy](#10-testing-strategy)
11. [Risk Register](#11-risk-register)
12. [Publishing Checklist](#12-publishing-checklist)
13. [Learning Resources](#13-learning-resources)

---

## 1. Executive Summary

### What It Does

The player presses a keybind (default: `P`) and gets a full-screen overlay panel.
They search for any item — vanilla or modded — and the mod instantly shows:

- A **dependency tree** of every crafting step from raw materials to final product
- A **material checklist** showing what they already own (across all storage) vs. what's missing
- **Source annotations** for raw materials: "Drop: Eye of Cthulhu", "Buy: Merchant (5g)", "Fish: Ocean"
- A **step-by-step crafting order** (topologically sorted) they can follow sequentially
- **Multi-target** support: queue up Zenith + Ankh Shield and see a unified material list

### Why It Doesn't Exist Yet

Recipe Browser shows recipes. The wiki has drop tables. But nothing *reads your
actual inventory across all storage containers, understands every loaded mod's
recipes, and produces a personalized action plan*. That's the gap.

### Why a tModLoader Mod (Not a Web App)

| Factor | tModLoader Mod | Web App |
|--------|---------------|---------|
| Recipe data (vanilla + mods) | `Main.recipe[]` — automatic, zero maintenance | Manual data extraction per mod per version |
| Player inventory | `Main.LocalPlayer.inventory` — real-time | Parse `.plr` files, no bank access without bridge mod |
| Mod compatibility | Inherent — any mod's recipes just appear | Each mod needs a separate data pipeline |
| User experience | In-game, no alt-tabbing | Separate browser window |
| Distribution | Steam Workshop one-click install | URL + file upload workflow |

The mod approach wins on every axis that matters.

---

## 2. Technical Foundation

### Environment

| Property | Value |
|----------|-------|
| **tModLoader version** | 1.4.4-stable (TML_2026_01 build) |
| **Target framework** | `net8.0` |
| **C# language version** | 12.0 (records, pattern matching, collection expressions) |
| **tModLoader install** | `/Volumes/DevDrive1/SteamLibrary/steamapps/common/tModLoader/` |
| **Build targets file** | `tMLMod.targets` (auto-references tModLoader.dll + all Libraries/) |
| **Mod side** | `Client` — purely informational, no server-side effects |
| **Platform** | macOS (Darwin), cross-platform via .NET 8 + FNA |

### Key tModLoader APIs We Depend On

#### Recipe System
```
Main.recipe[]                          — Array of ALL recipes (vanilla + every loaded mod)
Recipe.numRecipes                      — Count of valid entries
recipe.createItem                      — Item output (has .type and .stack)
recipe.requiredItem                    — List<Item> of ingredients
recipe.requiredTile                    — List<int> of crafting station tile IDs
recipe.Conditions                      — List<Condition> for conditional recipes
recipe.acceptedGroups                  — List<int> of RecipeGroup IDs this recipe accepts
RecipeGroup.recipeGroups               — Dictionary<int, RecipeGroup> of all groups
RecipeGroup.ValidItems                 — HashSet<int> of interchangeable items per group
```

#### Player Inventory
```
Main.LocalPlayer.inventory             — 58 slots (hotbar + main + coins + ammo)
Main.LocalPlayer.bank.item             — Piggy Bank (40 slots)
Main.LocalPlayer.bank2.item            — Safe (40 slots)
Main.LocalPlayer.bank3.item            — Defender's Forge (40 slots)
Main.LocalPlayer.bank4.item            — Void Vault (40 slots)
```

#### Item/NPC Info
```
ContentSamples.ItemsByType[id]         — Get Item instance by type for display
Lang.GetItemNameValue(type)            — Localized item name string
Lang.GetNPCNameValue(type)             — Localized NPC name string
TextureAssets.Item[type]               — Item sprite for rendering
ItemID.Sets.*                          — Various item metadata sets
```

#### Drop Tables & Shops
```
Main.ItemDropsDB.GetRulesForNPCID(id, includeGlobalDrops: true)
    — Returns List<IItemDropRule> for an NPC
    — Rule types: CommonDrop, OneFromOptionsDropRule, DropBasedOnExpertMode, etc.
    — Each rule may have ChainedRules for nested drops

NPCShopDatabase.AllShops               — IEnumerable<AbstractNPCShop>
    — Cast to NPCShop, access .Entries for List<NPCShop.Entry>
    — Each entry has .Item.type for the item ID
```

#### UI Framework
```
UserInterface                          — Manages a stack of UIStates, handles input routing
UIState                                — Full-screen container, the root of a UI tree
UIElement                              — Base class: Append(), Draw(), Update(), events
UIPanel                                — UIElement with a background texture
UIList + UIScrollbar                   — Scrollable list of elements
UIText                                 — Text rendering element
UIImage                                — Image display element
UIInputTextField                       — Text input field (from tModLoader internals)
```

#### Mod Lifecycle
```
Mod.Load()                             — Register keybinds, one-time setup
ModSystem.PostAddRecipes()             — ALL recipes from ALL mods are registered; build index here
ModSystem.UpdateUI(GameTime)           — Per-frame UI update
ModSystem.ModifyInterfaceLayers(List)  — Insert our UI layer into the rendering stack
ModPlayer.ProcessTriggers(TriggersSet) — Handle keybind presses
```

### NuGet / External Dependencies

**None.** We depend only on what tModLoader already ships:
- tModLoader.dll (mod API + Terraria game)
- FNA (graphics/input)
- Newtonsoft.Json (if we need config serialization — already in Libraries/)

This is intentional: zero external deps = zero version conflicts with other mods.

---

## 3. Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                        UI Layer                             │
│  ┌──────────┐ ┌──────────────┐ ┌────────┐ ┌────────────┐   │
│  │ Target   │ │  Dependency  │ │Material│ │  Craft     │   │
│  │ Search   │ │  Tree View   │ │  List  │ │  Order     │   │
│  └────┬─────┘ └──────┬───────┘ └───┬────┘ └─────┬──────┘   │
│       │              │             │             │           │
│  ┌────┴──────────────┴─────────────┴─────────────┴──────┐   │
│  │              CraftPathState (UIState)                 │   │
│  └──────────────────────┬───────────────────────────────┘   │
│                         │                                   │
│  ┌──────────────────────┴───────────────────────────────┐   │
│  │           CraftPathUISystem (ModSystem)               │   │
│  └──────────────────────┬───────────────────────────────┘   │
└─────────────────────────┼───────────────────────────────────┘
                          │
┌─────────────────────────┼───────────────────────────────────┐
│                    Core Layer                                │
│                         │                                    │
│  ┌──────────────────────┴───────────────────────────────┐   │
│  │             DependencyResolver                        │   │
│  │  (DFS + backtracking + scoring + topo sort)           │   │
│  └───────┬──────────────┬──────────────────┬────────────┘   │
│          │              │                  │                 │
│  ┌───────┴───────┐ ┌───┴────────┐ ┌───────┴───────────┐    │
│  │ RecipeIndex   │ │ Inventory  │ │  ItemSource       │    │
│  │ System        │ │ Scanner    │ │  System           │    │
│  │               │ │            │ │                   │    │
│  │ Dict<int,     │ │ Dict<int,  │ │ Dict<int,         │    │
│  │  List<Recipe>>│ │  int>      │ │  List<ItemSource>>│    │
│  └───────────────┘ └────────────┘ └───────────────────┘    │
│                                                             │
│  Data source:       Data source:     Data source:           │
│  Main.recipe[]      Player.inventory Main.ItemDropsDB       │
│  (PostAddRecipes)   Player.bank*     NPCShopDatabase        │
└─────────────────────────────────────────────────────────────┘
```

### Data Flow

1. **On mod load** (`PostAddRecipes`): `RecipeIndexSystem` iterates `Main.recipe[]` and builds a `Dictionary<int, List<Recipe>>` keyed by output item type. `ItemSourceSystem` indexes NPC drops and shops.

2. **On UI open** (keybind press): `InventoryScanner` aggregates all player storage into `Dictionary<int, int>` (itemType → totalCount).

3. **On item selected**: `DependencyResolver` takes (targetItemType, quantity, inventorySnapshot) and runs DFS to produce a `GraphNode` tree.

4. **On tree computed**: UI panels render the tree as an interactive visual, a flat material checklist, and a topologically-sorted craft order.

---

## 4. Core Systems — Detailed Design

### 4.1 RecipeIndexSystem

**Responsibility:** Build and maintain a fast recipe lookup from the global recipe array.

**Lifecycle hook:** `ModSystem.PostAddRecipes()` — fires *after* all mods have registered their recipes. This guarantees Calamity's 1000+ recipes are included.

**Data structures:**
```csharp
// Primary lookup: "what recipes produce this item?"
Dictionary<int, List<Recipe>> RecipesByResult

// Secondary lookup: "what recipes consume this item?" (for reverse lookups)
Dictionary<int, List<Recipe>> RecipesByIngredient

// Quick membership test
HashSet<int> CraftableItems
```

**Performance note:** `Main.recipe[]` can contain 5000+ entries with large mod packs. Building three dictionaries in a single O(n) pass at load time means all subsequent lookups are O(1). This happens once, not per-query.

**Recipe group handling:** For each recipe, we also check `recipe.acceptedGroups` and index the group member items as potential ingredients. This lets the resolver know that "Any Iron Bar" means Iron Bar OR Lead Bar.

---

### 4.2 InventoryScanner

**Responsibility:** Snapshot all items the player currently owns across every storage container.

**Storage locations scanned:**

| Container | Access Path | Slots | Source Tag |
|-----------|-------------|-------|------------|
| Main inventory | `Main.LocalPlayer.inventory` | 58 | `Personal` |
| Piggy Bank | `Main.LocalPlayer.bank.item` | 40 | `Personal` |
| Safe | `Main.LocalPlayer.bank2.item` | 40 | `Personal` |
| Defender's Forge | `Main.LocalPlayer.bank3.item` | 40 | `Personal` |
| Void Vault | `Main.LocalPlayer.bank4.item` | 40 | `Personal` |
| Equipment slots | `Main.LocalPlayer.armor` | 20 | `Equipped` |
| Misc equips | `Main.LocalPlayer.miscEquips` | 5 | `Equipped` |
| Nearby chests | `Main.chest[]` (distance-filtered) | Variable | `Chest` |

**Output:** `Dictionary<int, InventoryEntry>` where:
```csharp
public record InventoryEntry(
    int TotalCount,
    int PersonalCount,     // inventory + banks
    int EquippedCount,     // armor + misc equips
    int ChestCount,        // nearby chests (when enabled)
    bool IsEquipped        // true if any count comes from equipped slots
);
```

This lets the UI show markers like "(4 equipped)" or "(12 in chests)" next to quantities, so
the player knows *where* their items are and can make informed decisions about whether to
unequip or retrieve from chests.

**Equipped item handling:** Items in armor/equip slots ARE counted toward "owned" totals,
but the UI marks them with an `[E]` badge. The resolver treats them as available — the
player can decide whether they actually want to unequip. This avoids the mod silently
ignoring items the player owns.

**Refresh strategy:** Recompute when:
- UI is opened (keybind press)
- User clicks a "Refresh" button
- User selects a new target item

We intentionally do NOT auto-refresh every frame — inventory scanning is cheap, but
triggering a full graph recompute on every frame would be wasteful.

#### Nearby Chest Scanning

**Toggle:** Controlled via a `[Scan Chests: ON/OFF]` toggle button in the UI header
AND persisted in `ModConfig`. Default: **OFF** (personal storage only).

**How `Main.chest[]` works:**
- `Main.chest` is a fixed array of 8000 `Chest` objects (the world maximum)
- Non-null entries are placed chests. Each has `.x`, `.y` (tile coords) and `.item[40]`
- `Chest.x` and `Chest.y` are in *tile* coordinates (multiply by 16 for pixel position)
- Player position: `Main.LocalPlayer.position` is in pixel coordinates

**Distance calculation:**
```csharp
// Convert chest tile position to pixel center, measure against player
float chestPixelX = chest.x * 16f + 16f;  // center of 2-tile-wide chest
float chestPixelY = chest.y * 16f + 16f;
float dx = chestPixelX - Main.LocalPlayer.Center.X;
float dy = chestPixelY - Main.LocalPlayer.Center.Y;
float distanceTiles = MathF.Sqrt(dx * dx + dy * dy) / 16f;
```

**Range threshold:** Default **50 tiles** (~3 screen widths). This covers a typical
storage room without pulling in chests from across the map. Configurable in ModConfig
with a slider (10–200 tiles). For reference:
- Terraria crafting station range: ~4 tiles
- Typical NPC housing width: ~10 tiles
- Screen width at 1080p: ~60 tiles
- 50 tiles = comfortable "I'm in my base" radius

**Performance considerations:**
- Iterating 8000 chest slots and checking distance: negligible (< 1ms)
- Only non-null chests with items are processed
- Only runs on refresh, not per-frame
- Typical world has 200-500 chests; large builds maybe 1000

**What about dresser, barrel, and other container types?**
In Terraria, dressers and barrels are also stored in `Main.chest[]` — they're all
`Chest` objects. So our scan naturally includes them. No special handling needed.

**What about Magic Storage (mod)?**
Magic Storage uses a custom storage system not in `Main.chest[]`. Integration would
require checking if the Magic Storage mod is loaded and calling its API. This is a
future enhancement, not MVP. For now, Magic Storage items won't appear in chest scans.

**Multiplayer note:** In multiplayer, `Main.chest[]` is synced from the server. Chest
contents should be accurate for loaded chunks. Chests in unloaded chunks may be null
or stale — our distance filter naturally excludes these since they'd be far away.

---

### 4.3 ItemSourceSystem

**Responsibility:** For every item in the game, know all non-crafting ways to obtain it.

**Source types:**
```csharp
public enum ItemSourceType
{
    NPCDrop,       // "Drop: Zombie (1/50)"
    NPCShop,       // "Buy: Merchant (5g)"
    BossDrop,      // "Drop: Eye of Cthulhu"
    BagDrop,       // "Open: Treasure Bag (EoC)"
    Fishing,       // "Fish: Ocean"
    ChestLoot,     // "Found in: Gold Chest"
    Gathering,     // "Mine: Underground" (ores, gems)
    Event,         // "Event: Blood Moon"
    Other          // Fallback
}

public record ItemSource(
    ItemSourceType Type,
    string Description,     // Human-readable: "Drop: Zombie (1/50)"
    int SourceEntityId,     // NPC type ID, or -1 for non-NPC sources
    float DropRate           // 0.0-1.0, or -1 for non-drop sources
);
```

**Indexing strategy:**

Built once during `PostAddRecipes()` / `PostSetupContent()`:

1. **NPC Drops:** Iterate all NPC types (`-65` to `NPCLoader.NPCCount`). For each, call `Main.ItemDropsDB.GetRulesForNPCID(npcId, true)`. Recursively traverse `IItemDropRule` types:
   - `CommonDrop` → `.itemId`, `.chanceDenominator`
   - `OneFromOptionsDropRule` → `.dropIds` array
   - `DropBasedOnExpertMode` → unwrap inner rules
   - Follow `.ChainedRules` recursively for nested drops

2. **NPC Shops:** Iterate `NPCShopDatabase.AllShops`. Cast to `NPCShop`, read `.Entries`, extract `.Item.type` per entry.

3. **Static sources:** Some items have known sources that aren't in drop tables (ores from world gen, plants, fishing crates). We'll maintain a small static lookup for common material categories using `ItemID.Sets` flags where available.

**Output:** `Dictionary<int, List<ItemSource>>` — for any item, get all known ways to obtain it.

---

### 4.4 DependencyResolver

> This is the algorithmic heart of the mod. It gets its own deep-dive in [Section 6](#6-algorithm-deep-dive-the-dependency-resolver).

**Responsibility:** Given a target item (or multiple targets) and the player's inventory snapshot, produce:
1. An optimal `GraphNode` tree (the crafting dependency graph)
2. A flat material list (leaf nodes aggregated)
3. A topologically-sorted craft order (sequence of steps)

---

### 4.5 CraftPathUISystem

**Responsibility:** Manage the entire UI lifecycle — creation, rendering, input, and teardown.

**Pattern:** This follows the standard tModLoader UI pattern established by mods like Magic Storage and Recipe Browser:

```
ModSystem.Load()                    → Create UserInterface + UIState instances
ModSystem.UpdateUI(GameTime)        → Tick the UserInterface
ModSystem.ModifyInterfaceLayers()   → Insert our draw layer before "Vanilla: Mouse Text"
ModSystem.Unload()                  → Null out references
```

**Toggle behavior:** The keybind toggles the UI on/off by calling `UserInterface.SetState(state)` or `UserInterface.SetState(null)`. When toggled on, we also trigger an inventory scan and (if a target is selected) a graph recompute.

**Input blocking:** When our UI is visible, we set `Main.LocalPlayer.mouseInterface = true` on hover to prevent clicks from passing through to the game world.

---

### 4.6 Multi-Target Queue

**Responsibility:** Let the player queue multiple target items and see a unified view.

**Design:**
```csharp
public class TargetQueue
{
    public List<(int itemType, int quantity)> Targets { get; }

    // Returns merged results across all targets
    public ResolverResult ComputeAll(
        DependencyResolver resolver,
        Dictionary<int, int> inventory);
}
```

**Material merging:** When computing paths for multiple targets, shared sub-materials should be counted once, not duplicated. Example: if both Zenith and Ankh Shield need Iron Bars, the total iron needed is the *sum* across both paths, but intermediate crafts that overlap (like the Iron Bars themselves) should be computed once.

**Implementation approach:**
1. Resolve each target independently to get its full tree
2. Merge the flat material lists by summing quantities per item
3. In the craft order, deduplicate intermediate crafts and adjust quantities

This is a Phase 2 enhancement. MVP supports single-target; the data structures are designed to extend naturally.

---

## 5. UI Design

### Layout — Full Overlay Panel

```
┌──────────────────────────────────────────────────────────────────┐
│  Craft Path Optimizer                              [_] [□] [X]  │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌─ Target Queue ──────────────────────────────────────────────┐ │
│  │ [Search: _______________]  [+ Add Target]                   │ │
│  │                                                             │ │
│  │ ┌────┐ ┌────┐ ┌────┐                                       │ │
│  │ │Zen │ │Ankh│ │Cell│   ← queued target item icons           │ │
│  │ │ith │ │Shld│ │Phon│     (click to focus, X to remove)     │ │
│  │ └────┘ └────┘ └────┘                                       │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                                                                  │
│  ┌─ Tabs ──────────────────────────────────────────────────────┐ │
│  │ [ Tree ] [ Materials ] [ Craft Order ]                      │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                                                                  │
│  ┌─ Tab Content ───────────────────────────────────────────────┐ │
│  │                                                             │ │
│  │  (content depends on selected tab — see below)              │ │
│  │                                                             │ │
│  │                                                             │ │
│  │                                                             │ │
│  │                                                             │ │
│  │                                                             │ │
│  │                                                             │ │
│  │                                                             │ │
│  │                                                             │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                                                                  │
│  ┌─ Status Bar ────────────────────────────────────────────────┐ │
│  │ Total materials needed: 47 types, 283 items                 │ │
│  │ Already owned: 31 types (65%)  |  Missing: 16 types         │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

### Tab: Tree View

```
  Terra Blade (1)
  ├─ [CRAFT] True Night's Edge (1) @ Mythril Anvil
  │  ├─ [CRAFT] Night's Edge (1) @ Demon Altar
  │  │  ├─ [HAVE ✓] Blade of Grass (1)        ← green: in inventory
  │  │  ├─ [NEED] Muramasa (1)                 ← red: not owned
  │  │  │  └─ [SOURCE] Found in: Dungeon Gold Chest
  │  │  ├─ [HAVE ✓] Blood Butcherer (1)
  │  │  └─ [HAVE ✓] Volcano (1)
  │  └─ [CRAFT] Soul of Night (20)
  │     └─ [SOURCE] Drop: Any enemy in Underground Corruption/Crimson
  ├─ [CRAFT] True Excalibur (1) @ Mythril Anvil
  │  ├─ [CRAFT] Excalibur (1) @ Mythril Anvil
  │  │  └─ [CRAFT] Hallowed Bar (12) ...
  │  └─ [NEED] Soul of Light (20)
  │     └─ [SOURCE] Drop: Any enemy in Underground Hallow
  └─ ...
```

**Color coding:**
- **Green** = owned, no action needed
- **Yellow** = craftable from owned materials (intermediate step)
- **Red** = must be acquired (drop, buy, mine, fish)
- **Gray** = conditional recipe (e.g., only during Blood Moon)

**Interactions:**
- Click a node to expand/collapse its subtree
- Hover for full item tooltip (vanilla tooltip system via `Main.HoverItem`)
- Right-click to "pin" a sub-item as a separate target

### Tab: Materials List

```
  ┌──────────────────────────────────────────────────────────────┐
  │  Filter: [All ▼]  Sort: [By Source ▼]  [Chests: ON] [🔄]   │
  ├──────────────────────────────────────────────────────────────┤
  │                                                              │
  │  ── Owned (31/47) ──────────────────────────────             │
  │  [icon] Blade of Grass      1/1    ✓ DONE                   │
  │  [icon] Blood Butcherer     1/1    ✓ DONE  [E]              │
  │         └─ ⚠ Currently equipped                              │
  │  [icon] Hallowed Bar        8/12   ⚠ Need 4 more            │
  │         └─ 5 in inventory, 3 in chest                       │
  │         └─ Drop: Any Mech Boss                              │
  │  [icon] Iron Bar           12/12   ✓ DONE                   │
  │         └─ 4 in inventory, 8 in chest                       │
  │  ...                                                        │
  │                                                              │
  │  ── Missing (16/47) ────────────────────────────             │
  │  [icon] Muramasa            0/1    ✗                        │
  │         └─ Found in: Dungeon Gold Chest                     │
  │  [icon] Soul of Night       0/20   ✗                        │
  │         └─ Drop: Underground Corruption enemies              │
  │  [icon] Soul of Light       0/20   ✗                        │
  │         └─ Drop: Underground Hallow enemies                  │
  │  ...                                                        │
  └──────────────────────────────────────────────────────────────┘
```

Note the `[Chests: ON]` toggle in the header and the source breakdown annotations.
When chest scanning is OFF, chest counts are excluded and the "in chest" lines disappear.
The `[E]` badge on Blood Butcherer indicates it's currently equipped — the count is
included in the total but the player is warned they'd need to unequip it.

### Tab: Craft Order

```
  ┌─────────────────────────────────────────────────────────┐
  │  Step-by-step crafting order (post-order traversal)     │
  ├─────────────────────────────────────────────────────────┤
  │                                                         │
  │  ✓ Step 1: Craft Hallowed Bar ×12                       │
  │     Station: Adamantite/Titanium Forge                  │
  │     Input: Hallowed Ore ×48 (need to acquire)           │
  │                                                         │
  │  ○ Step 2: Craft Excalibur ×1                           │
  │     Station: Mythril/Orichalcum Anvil                   │
  │     Input: Hallowed Bar ×12 (from Step 1)               │
  │                                                         │
  │  ○ Step 3: Craft Night's Edge ×1                        │
  │     Station: Demon/Crimson Altar                        │
  │     Input: Blade of Grass ×1 ✓                          │
  │            Muramasa ×1 (need to acquire)                 │
  │            Blood Butcherer ×1 ✓                          │
  │            Volcano ×1 ✓                                  │
  │  ...                                                    │
  │                                                         │
  │  ○ Step 9: Craft Terra Blade ×1                         │
  │     Station: Mythril/Orichalcum Anvil                   │
  │     Input: True Night's Edge ×1 (from Step 7)           │
  │            True Excalibur ×1 (from Step 8)              │
  │            Broken Hero Sword ×1 (need to acquire)       │
  │                                                         │
  └─────────────────────────────────────────────────────────┘
```

### Search Panel (Dropdown)

When the user types in the search bar, a dropdown appears showing matching items:

```
  [Search: "terra bl___"]
  ┌──────────────────────────┐
  │ [icon] Terra Blade       │  ← vanilla
  │ [icon] Terra Edge        │  ← vanilla
  │ [icon] Terra Flare       │  ← Calamity
  │ [icon] Terratomere       │  ← Calamity
  └──────────────────────────┘
```

**Search implementation:** Filter `ContentSamples.ItemsByType` by checking if `Lang.GetItemNameValue(type)` contains the search string (case-insensitive). Debounce by only filtering when text hasn't changed for 5 frames.

---

## 6. Algorithm Deep-Dive: The Dependency Resolver

This is the most complex and interesting part of the mod. It deserves careful design.

### Problem Statement

Given:
- A target item type and desired quantity
- A dictionary of all recipes indexed by output item
- A snapshot of the player's inventory

Produce:
- The lowest-cost crafting tree from raw materials to target
- Where "cost" = total items that must be acquired (not already owned)

### Node Types

```csharp
public abstract record GraphNode(int ItemType, int Quantity);

// Player already owns enough of this item
public record HaveNode(int ItemType, int Quantity)
    : GraphNode(ItemType, Quantity);

// Item must be acquired from a non-crafting source
public record AcquireNode(int ItemType, int Quantity, List<ItemSource> Sources)
    : GraphNode(ItemType, Quantity);

// Item is crafted via a specific recipe
public record CraftNode(int ItemType, int Quantity, Recipe Recipe,
    int CraftCount, List<GraphNode> Ingredients)
    : GraphNode(ItemType, Quantity);

// Represents a recipe group choice (any valid item satisfies it)
public record GroupNode(int GroupId, int Quantity, GraphNode ChosenItem)
    : GraphNode(ChosenItem.ItemType, Quantity);
```

### The Algorithm: DFS with Backtracking and Scoring

```
FUNCTION Resolve(itemType, quantity, inventory, visited) → GraphNode:

  1. CHECK INVENTORY
     available = inventory[itemType]
     if available >= quantity:
         inventory[itemType] -= quantity        // "consume" from simulation
         return HaveNode(itemType, quantity)
     else if available > 0:
         quantity -= available                   // use what we have
         inventory[itemType] = 0

  2. CHECK CRAFTABILITY
     recipes = RecipesByResult[itemType]
     if recipes is empty:
         sources = ItemSourceSystem.GetSources(itemType)
         return AcquireNode(itemType, quantity, sources)

  3. TRY EACH RECIPE (score and pick best)
     bestTree = null
     bestCost = ∞

     for each recipe in recipes:
         if recipe is in visited → SKIP (cycle detection)

         // How many times must we execute this recipe?
         craftCount = ceil(quantity / recipe.createItem.stack)

         // Snapshot inventory before this branch
         inventorySnapshot = copy(inventory)
         visited.add(recipe)

         ingredientNodes = []
         branchCost = 0
         failed = false

         for each ingredient in recipe.requiredItem:
             // Handle recipe groups
             if ingredient belongs to a RecipeGroup:
                 node = ResolveGroup(groupId, ingredient.stack * craftCount,
                                     inventory, visited)
             else:
                 node = Resolve(ingredient.type, ingredient.stack * craftCount,
                                inventory, visited)

             if node is null → failed = true; break
             ingredientNodes.add(node)
             branchCost += Cost(node)

         visited.remove(recipe)

         if failed:
             inventory = inventorySnapshot    // rollback
             continue

         if branchCost < bestCost:
             bestCost = branchCost
             bestTree = CraftNode(itemType, quantity, recipe,
                                  craftCount, ingredientNodes)
             bestInventory = copy(inventory)
         else:
             inventory = inventorySnapshot    // rollback non-winning branch

     if bestTree is not null:
         inventory = bestInventory
         return bestTree

  4. FALLBACK: No recipe worked (all cycled or failed)
     return AcquireNode(itemType, quantity, sources)


FUNCTION Cost(node) → int:
    match node:
        HaveNode     → 0
        AcquireNode  → node.Quantity
        GroupNode    → Cost(node.ChosenItem)
        CraftNode   → sum(Cost(child) for child in node.Ingredients)
```

### Critical Edge Cases

#### Recipe Cycles
Terraria has real cycles: Wood → Platform → Wood. Without protection, the resolver
would recurse infinitely.

**Solution:** Maintain a `HashSet<int>` of recipe *indices* on the current DFS branch
(not globally — a recipe can appear in different branches). Check before recursing.
This matches Recipe Browser's `CheckParentsForRecipeLoopViaIngredients()` approach.

**Depth limit:** Hard cap of 20 levels (matches Recipe Browser). Any path deeper than
this is almost certainly a cycle or an impractical recipe chain.

#### Recipe Groups ("Any Iron Bar")
When a recipe accepts a RecipeGroup, we need to choose the *best* member.

```
FUNCTION ResolveGroup(groupId, quantity, inventory, visited) → GroupNode:
    members = RecipeGroup.recipeGroups[groupId].ValidItems
    bestNode = null
    bestCost = ∞

    for each memberType in members:
        inventorySnapshot = copy(inventory)
        node = Resolve(memberType, quantity, inventory, visited)
        cost = Cost(node)

        if cost < bestCost:
            bestCost = cost
            bestNode = node
            bestInventory = inventory
        inventory = inventorySnapshot

    return GroupNode(groupId, quantity, bestNode)
```

#### Conditional Recipes
Some recipes have `Conditions` (e.g., near water, during Blood Moon). We include these
recipes in the graph but annotate them:

```csharp
// In CraftNode, expose condition info for UI display
public bool HasConditions => Recipe.Conditions.Count > 0;
public string ConditionDescription => string.Join(", ",
    Recipe.Conditions.Select(c => c.Description.Value));
```

The UI renders conditional steps in gray with the condition text shown.

#### Multi-Target Merging
When resolving multiple targets:

1. Resolve Target 1 → Tree 1, consuming from inventory
2. Resolve Target 2 → Tree 2, consuming from *remaining* inventory
3. (Repeat for all targets)

This naturally handles shared materials: if Target 1 consumes 10 Iron Bars and
Target 2 needs 5, the resolver sees only what's left after Target 1.

The order matters — targets should be resolved in the order the user added them.
If the user re-orders their queue, we recompute from scratch.

### Topological Sort for Craft Order

Once we have the tree, flatten it to a step-by-step order:

```
FUNCTION TopoSort(root) → List<CraftStep>:
    steps = []
    PostOrderTraversal(root, steps)
    return steps

FUNCTION PostOrderTraversal(node, steps):
    if node is CraftNode:
        for each child in node.Ingredients:
            PostOrderTraversal(child, steps)
        steps.add(CraftStep(node))
    // HaveNode and AcquireNode are not "steps" — they're prerequisites
```

This guarantees you never encounter a step whose inputs haven't been crafted yet.

---

## 7. File-by-File Specification

### Root Files

| File | Purpose |
|------|---------|
| `CraftPathOptimizer.cs` | Mod entry point. Registers keybind in `Load()`. ~15 lines. |
| `CraftPathOptimizer.csproj` | Project file referencing `tMLMod.targets`. ~10 lines. |
| `build.txt` | Mod metadata: name, author, version, `side = Client`. |
| `description.txt` | Steam Workshop description. |
| `icon.png` | 80x80 mod icon. |

### Common/Systems/

| File | Purpose | Key APIs | Est. Lines |
|------|---------|----------|------------|
| `RecipeIndexSystem.cs` | Builds recipe lookup dictionaries from `Main.recipe[]` | `PostAddRecipes()`, `Main.recipe[]`, `RecipeGroup.recipeGroups` | ~80 |
| `ItemSourceSystem.cs` | Indexes all non-crafting item sources (drops, shops) | `Main.ItemDropsDB`, `NPCShopDatabase.AllShops`, `IItemDropRule` types | ~200 |
| `CraftPathUISystem.cs` | UI lifecycle: create, update, draw, toggle | `UserInterface`, `UIState`, `ModifyInterfaceLayers()` | ~100 |

### Common/Players/

| File | Purpose | Key APIs | Est. Lines |
|------|---------|----------|------------|
| `CraftPathPlayer.cs` | Keybind handler, triggers UI toggle | `ProcessTriggers()`, `ModKeybind.JustPressed` | ~20 |

### Content/Graph/

| File | Purpose | Est. Lines |
|------|---------|------------|
| `GraphNode.cs` | Node type hierarchy: `HaveNode`, `AcquireNode`, `CraftNode`, `GroupNode` | ~40 |
| `DependencyResolver.cs` | DFS algorithm with backtracking, cycle detection, scoring, topo sort | ~250 |
| `InventoryScanner.cs` | Aggregates player storage + nearby chests into `Dict<int,InventoryEntry>` | ~120 |
| `ItemSource.cs` | `ItemSource` record + `ItemSourceType` enum | ~25 |
| `ResolverResult.cs` | Result container: tree, flat material list, craft order, stats | ~50 |
| `TargetQueue.cs` | Multi-target management + merged resolution | ~80 |

### UI/

| File | Purpose | Est. Lines |
|------|---------|------------|
| `CraftPathState.cs` | Root UIState. Holds all panels, manages tab switching. | ~100 |
| `Panels/TargetSelectionPanel.cs` | Search bar + item results + target queue display | ~150 |
| `Panels/DependencyTreePanel.cs` | Interactive tree visualization with expand/collapse | ~200 |
| `Panels/MaterialListPanel.cs` | Flat checklist: have vs. need, with source tags | ~150 |
| `Panels/CraftOrderPanel.cs` | Step-by-step crafting instructions | ~120 |
| `Elements/UISearchBar.cs` | Text input with debounced filtering | ~60 |
| `Elements/UIItemSlot.cs` | Item icon + count display element | ~50 |
| `Elements/UITreeNode.cs` | Single node in the tree view (expandable) | ~80 |
| `Elements/UICraftStep.cs` | Single step in the craft order | ~50 |
| `Elements/UIDraggablePanel.cs` | Draggable + resizable container panel | ~70 |
| `Elements/UITabBar.cs` | Tab switching element | ~40 |
| `Elements/UIItemSourceTag.cs` | "Drop: Zombie" tag with icon | ~40 |

### Localization/

| File | Purpose |
|------|---------|
| `en-US.hjson` | All user-facing strings (search placeholder, tab labels, status formats, etc.) |

### Estimated Totals (updated)

| Category | Files | Est. Lines |
|----------|-------|------------|
| Root / config | 5 | ~50 |
| Systems | 3 | ~380 |
| Player | 1 | ~20 |
| Graph / Core | 6 | ~565 |
| UI | 12 | ~1110 |
| Localization | 1 | ~50 |
| **Total** | **28** | **~2175** |

---

## 8. Phase Breakdown

### Phase 1: Foundation + MVP (Single Target, Material List)

**Goal:** Player can search for any item, select it, and see a flat material list
showing what they have vs. what they need. This alone is useful enough to ship as
a beta.

**Deliverables:**

| # | File | What to build |
|---|------|---------------|
| 1 | `CraftPathOptimizer.cs` | Mod entry + keybind registration |
| 2 | `CraftPathOptimizer.csproj` | Project file |
| 3 | `build.txt` | Mod metadata |
| 4 | `Localization/en-US.hjson` | Initial strings |
| 5 | `Common/Systems/RecipeIndexSystem.cs` | Recipe dictionary builder |
| 6 | `Content/Graph/InventoryScanner.cs` | Inventory aggregation (personal + equipped + nearby chests with toggle) |
| 7 | `Content/Graph/GraphNode.cs` | Node type hierarchy |
| 8 | `Content/Graph/DependencyResolver.cs` | Core DFS algorithm (single-target) |
| 9 | `Content/Graph/ResolverResult.cs` | Result container |
| 10 | `Content/Graph/InventoryEntry.cs` | Per-item breakdown: personal / equipped / chest counts |
| 11 | `Common/Systems/CraftPathUISystem.cs` | UI lifecycle boilerplate |
| 12 | `Common/Players/CraftPathPlayer.cs` | Keybind handler |
| 13 | `UI/CraftPathState.cs` | Root UIState with panels |
| 14 | `UI/Elements/UISearchBar.cs` | Search input |
| 15 | `UI/Elements/UIItemSlot.cs` | Item display element (with `[E]` equipped badge) |
| 16 | `UI/Panels/TargetSelectionPanel.cs` | Search + item picker |
| 17 | `UI/Panels/MaterialListPanel.cs` | Have/need checklist with source breakdown (personal/equipped/chest) |

**Milestone tests:**
- Load mod with Calamity. Search "Zenith". See correct material list with inventory counts.
  Verify Calamity items that are ingredients in vanilla recipes show up correctly.
- Place Iron Bars in a nearby chest. Toggle chest scanning ON. Verify they appear in
  material list with "(in chest)" annotation. Toggle OFF. Verify they disappear.
- Equip a weapon that's a crafting ingredient. Verify it shows with `[E]` badge.

**Resolved design decisions:**
- Scoring: fewest total acquisitions (simple and correct)
- Search: cap at 50 results, debounce 5 frames
- Inventory display: "8/12" with source tags like `[E]` for equipped, chest count annotation
- Chest scanning: included in Phase 1 with toggle (default OFF, 50-tile range)

---

### Phase 2: Tree View + Craft Order + Multi-Target

**Goal:** Full visual tree and step-by-step crafting order. Multi-target queue.

**Deliverables:**

| # | File | What to build |
|---|------|---------------|
| 1 | `UI/Elements/UITreeNode.cs` | Tree node element with expand/collapse |
| 2 | `UI/Panels/DependencyTreePanel.cs` | Full tree visualization |
| 3 | `UI/Elements/UICraftStep.cs` | Craft step element |
| 4 | `UI/Panels/CraftOrderPanel.cs` | Step-by-step panel |
| 5 | `UI/Elements/UITabBar.cs` | Tab switching |
| 6 | `UI/Elements/UIDraggablePanel.cs` | Panel dragging/resizing |
| 7 | `Content/Graph/TargetQueue.cs` | Multi-target + merge logic |
| 8 | Update `CraftPathState.cs` | Tab navigation + queue display |
| 9 | Update `TargetSelectionPanel.cs` | Queue management UI |
| 10 | Update `DependencyResolver.cs` | Topo sort output |

**Milestone test:** Queue Zenith + Cell Phone. See unified material list. See correct
tree for each. Verify craft order puts leaf crafts before parent crafts.

---

### Phase 3: Item Sources + Polish

**Goal:** "Where to get it" annotations. UI polish. Config options.

**Deliverables:**

| # | File | What to build |
|---|------|---------------|
| 1 | `Content/Graph/ItemSource.cs` | Source data model |
| 2 | `Common/Systems/ItemSourceSystem.cs` | Drop table + shop indexer |
| 3 | `UI/Elements/UIItemSourceTag.cs` | Source tag display |
| 4 | Update `MaterialListPanel.cs` | Show sources under each material |
| 5 | Update `DependencyTreePanel.cs` | Show sources on AcquireNodes |
| 6 | Update `DependencyResolver.cs` | Attach sources to AcquireNodes |
| 7 | Add `ModConfig` class | User settings (keybind, panel position, etc.) |

**Milestone test:** Search for Terra Blade. See "Drop: Any Mech Boss" under Hallowed
Bars. See "Found in: Dungeon Gold Chest" under Muramasa. Verify Calamity drop
sources appear correctly.

---

### Phase 4: Optimization + Edge Cases + Release Polish

**Goal:** Performance tuning, broad mod compatibility, release readiness.

**Work items:**

- [ ] **Caching:** Cache resolved trees per target item; invalidate when inventory changes
- [ ] **Async resolution:** For deeply nested trees (modded endgame items), run resolver on a background thread and show "Computing..." indicator
- [ ] **Alternative paths:** When multiple recipes exist, show a "Try alternative recipe" button
- [ ] **Conditional recipe display:** Gray out steps with conditions, show condition text
- [ ] **ModConfig:** Panel opacity, default tab, auto-refresh interval, search result limit, chest scan range slider
- [ ] **Stress test:** Calamity + Thorium + Fargo's loaded simultaneously
- [ ] **Edge cases:** Items with no source (e.g., developer items), recursive recipe groups, zero-stack items
- [ ] **Magic Storage integration:** Detect Magic Storage mod and scan its storage system via mod.Call API
- [ ] **Custom IItemDropRule reflection fallback:** For unrecognized modded drop rule types, use reflection to find `itemId`-like fields as a best-effort source extraction
- [ ] **Accessibility:** Keyboard navigation within the UI panel
- [ ] **Icon:** Create 80x80 `icon.png`
- [ ] **Workshop description:** Write compelling `description.txt` with screenshots
- [ ] **Changelog / version tagging**

---

### Phase 5: Tracking System + Action Advisor + HUD Overlay

**Goal:** Persistent goal tracking with intelligent action guidance and a compact gameplay HUD.

#### 5a. Tracking Data Layer

New files:
- `Content/Tracking/TrackingState.cs` — owns pinned goals, progress snapshots, action recommendations
- `Content/Tracking/PinnedGoal.cs` — single tracked item: itemType, quantity, resolved materials, progress
- `Content/Tracking/ActionClassifier.cs` — classifies missing materials into action groups
- `Content/Tracking/ActionGroup.cs` — data model for grouped actions (farm location, boss, shop, craft-now)
- `Common/Players/TrackingPlayer.cs` — save/load pinned goals via TagCompound, periodic inventory diff
- `Common/TrackingGlobalItem.cs` — GlobalItem.OnPickup() for instant tracked-item detection + toasts

**TrackingState** is the central hub:
- Owns `List<PinnedGoal>` — the user's tracked items
- Runs `ActionClassifier` on all pinned goals' merged missing materials
- Provides `List<ActionGroup>` sorted by priority for the TODO tab and HUD overlay
- Refreshes on: item pickup, manual refresh, periodic timer (every 3s)

**ActionClassifier** groups missing materials:
1. CRAFT NOW — all sub-ingredients owned, just need to visit a station
2. BUY — available from an NPC shop
3. FARM — mob drops, grouped by biome/location (key innovation: one trip = multiple items)
4. EVENT — drops only during Blood Moon, Solar Eclipse, etc.
5. BOSS — boss-gated drops with summoning info
6. EXPLORE — chest loot, world-gen items

Farm groups use NPC spawn biome data to cluster items by WHERE the player should go.

**Persistence:** `TrackingPlayer.SaveData()` / `LoadData()` stores pinned goals as a TagCompound
list of (itemType, quantity) pairs. Resolved data is recomputed on world load.

#### 5b. Main Panel — Tracked Sidebar + TODO Tab

Layout change: main panel splits into left content (70%) + right sidebar (30%).

**Right sidebar (always visible):**
- List of pinned items with icon, name, progress fraction, progress bar
- Color-coded: green (>80%), yellow (40-80%), red (<40%)
- Click to make it the active analysis target (populates Materials/Tree/Craft Order)
- Right-click to unpin
- "Clear All" button at bottom

**Pin flow:**
- Search results get a pin [📌] button on the right side of each row
- Clicking the pin adds to tracked sidebar (does NOT change the active analysis target)
- Clicking the item name/icon sets it as active analysis target (existing behavior)

**New "TODO" tab (4th tab alongside Materials/Tree/Craft Order):**
- Renders `ActionClassifier` output as grouped, prioritized action cards
- Each group is a collapsible section:
  - CRAFT NOW: item icon, station name, "all ingredients ready!"
  - FARM: biome name, list of (mob → item, drop rate, needed qty)
  - BOSS: boss name, summon method, location, drops with rates
  - BUY: NPC name, item, cost
  - EXPLORE: location hint
- Groups sorted by: Craft Now (0) → Buy (1) → Farm (2) → Event (3) → Boss (4) → Explore (5)
- Farm groups sorted by: most items obtainable in one trip (descending)

#### 5c. HUD Overlay

Separate `UserInterface`/`UIState` pair, rendered at "Vanilla: Resource Bars" layer.

**Compact always-on widget (during gameplay, panel closed):**
```
┌─ Goals ────────────────────┐
│ [icon] Ankh Shield    7/11 │
│ [icon] Cell Phone     4/13 │
│                             │
│ Next: Farm Undergr. Corrupt│
│  Corruptor → Vitamins      │
│  Dark Mummy → Blindfold    │
└─────────────────────────────┘
```

- Draggable position (saved in ModConfig)
- Configurable: show/hide, max goals shown, opacity
- Auto-hides when main panel is open (avoids redundancy)
- Flashes/highlights when a tracked item is picked up

**Pickup toasts:**
- Small floating "+1 Vitamins (1/1 DONE!)" near the overlay
- Fade over 2 seconds
- Triggered by `TrackingGlobalItem.OnPickup()`

#### Implementation Order

| Step | What | Files |
|------|------|-------|
| 1 | Data model + persistence | PinnedGoal.cs, TrackingState.cs, TrackingPlayer.cs |
| 2 | Tracked sidebar in main panel | Update CraftPathState.cs layout, new UITrackedSidebar |
| 3 | Pin button in search results | Update UICraftItemSlot + TargetSelectionPanel |
| 4 | ActionClassifier engine | ActionClassifier.cs, ActionGroup.cs |
| 5 | TODO tab | New TodoPanel.cs |
| 6 | Pickup detection + toasts | TrackingGlobalItem.cs, toast rendering |
| 7 | HUD overlay | TrackingOverlaySystem.cs, TrackingOverlayState.cs |
| 8 | Config + persistence polish | Save overlay position, update ModConfig |

---

### Phase 6: Progression Helper — Class-Aware Gear Advisor

**Goal:** A "Progress" tab that analyzes the player's current inventory, detects their
progression stage and class, recommends gear upgrades, and highlights combined accessories
they're close to completing.

#### Architecture: Hybrid Static + Dynamic (Option C)

**Static:** Progression tier definitions (boss flags → tier name + available stations).
**Dynamic:** Item scanning via `ContentSamples.ItemsByType` + `item.DamageType.CountsAsClass()` +
`item.damage`/`item.defense`/`item.accessory` to discover what's available at each tier.
This gives automatic Calamity/mod support without hardcoded item lists.

#### 6a. Progression Tier Detection

New file: `Content/Progression/ProgressionTier.cs`

Tiers detected from boss flags (all static bools on `NPC`):

```
Tier 0:  Pre-Boss           — none downed
Tier 1:  Post-Eye/King      — downedBoss1 || downedSlimeKing
Tier 2:  Post-Evil Boss     — downedBoss2
Tier 3:  Post-Skeletron     — downedBoss3
Tier 4:  Pre-Hardmode Late  — downedQueenBee && downedBoss3
Tier 5:  Early Hardmode     — Main.hardMode
Tier 6:  Post-First Mech    — downedMechBossAny
Tier 7:  Post-All Mechs     — downedMechBoss1 && downedMechBoss2 && downedMechBoss3
Tier 8:  Post-Plantera      — downedPlantBoss
Tier 9:  Post-Golem         — downedGolemBoss
Tier 10: Post-Cultist       — downedAncientCultist
Tier 11: Post-Moon Lord     — downedMoonlord
```

Each tier knows:
- Display name ("Early Hardmode")
- What crafting stations become available (Hardmode Anvil at Tier 5, etc.)
- What ores/materials become available

#### 6b. Class Detection

New file: `Content/Progression/ClassDetector.cs`

Auto-detect from:
1. Equipped armor set bonus (check `player.armor[0..2]` item types)
2. Weapon types in hotbar (count melee vs ranged vs magic vs summon by `DamageType.CountsAsClass()`)
3. Highest-damage weapon by class

Returns a `PlayerClass` enum: `Melee`, `Ranged`, `Mage`, `Summoner`, `Mixed`

User can override via a toggle row: `[All] [Melee] [Ranged] [Mage] [Summoner]`

#### 6c. Gear Recommender

New file: `Content/Progression/GearRecommender.cs`

The core algorithm:

```
For each item in ContentSamples.ItemsByType:
  Skip if: deprecated, quest item, unobtainable, zero damage/defense
  Determine item category: Weapon, Armor (head/body/legs), Accessory, Tool
  Determine class: item.DamageType.CountsAsClass(...)
  Determine tier: find which tier this item's recipe becomes available
    (by checking recipe.Conditions for boss conditions, or recipe ingredient
     availability based on tier)
  If tier <= player's current tier AND player doesn't own it:
    Score it: damage/defense relative to what player currently has
    → "Upgrade: +15 damage over your current weapon"
```

**Scoring:**
- Weapons: compare `item.damage` to player's best weapon in that class
- Armor: compare `item.defense` to player's current armor defense
- Accessories: check for movement (wings, boots), combat buffs, utility
- Tools: compare `item.pick`/`item.axe` power to current

**Recipe tier detection heuristic:**
```
For a recipe's required ingredients:
  If any ingredient requires Hardmode ores → Tier 5+
  If any ingredient is a Mech Boss drop → Tier 6+
  If any ingredient is a Plantera drop → Tier 8+
  If recipe.Conditions includes Condition.Hardmode → Tier 5+
  If recipe.Conditions includes Condition.DownedMechBossAll → Tier 7+
  etc.
```

#### 6d. Near-Complete Accessory Scanner

New file: `Content/Progression/AccessoryScanner.cs`

Algorithm:
```
For each accessory recipe with depth >= 3 (multi-step crafting):
  Run the existing DependencyResolver
  Count leaf materials: owned vs total
  If completion % >= 50%:
    Add to "Almost Complete" list with missing items
```

Curated "notable" accessories to always check (even if < 50%):
- Ankh Shield, Cell Phone/Shellphone, Terraspark Boots, The Grand Design
- Any accessory whose recipe has 5+ unique ingredients

Results sorted by completion % descending.

**Integration with tracking:** Each "almost complete" row has a "Pin to track" button
that adds it to the TrackedSidebar with all missing materials.

#### 6e. Progress Tab UI

New file: `UI/Panels/ProgressPanel.cs`

Layout:
```
┌─ Progress ────────────────────────────────────────────┐
│  Stage: Early Hardmode (Post-Mech Bosses)             │
│  Detected Class: Melee                                │
│  [All] [Melee] [Ranged] [Mage] [Summoner]            │
│                                                        │
│  ── Recommended Upgrades ─────────────────────────    │
│  [icon] Excalibur (+22 dmg)              CRAFTABLE    │
│     Hallowed Bar x12 — you have 8                     │
│  [icon] Hallowed Plate Mail (+15 def)    4 items away │
│     Missing: Hallowed Bar x20 more                    │
│                                                        │
│  ── Almost Complete Accessories ──────────────────    │
│  [icon] Ankh Shield                      9/11 (82%)   │
│     Missing: Vitamins, Blindfold         [Pin]        │
│  [icon] Terraspark Boots                 6/8  (75%)   │
│     Missing: Lava Charm, Ice Skates      [Pin]        │
│                                                        │
│  ── Available This Tier ──────────────────────────    │
│  Weapons: (scrollable list of tier-appropriate items) │
│  Armor: (scrollable list)                             │
│  Accessories: (scrollable list)                       │
└───────────────────────────────────────────────────────┘
```

Key tModLoader APIs used:
- `item.DamageType.CountsAsClass(DamageClass.Melee)` — class filtering
- `NPC.downedBoss1/2/3`, `Main.hardMode`, etc. — tier detection
- `ContentSamples.ItemsByType` — enumerate all items (vanilla + modded)
- `Condition.IsMet()` on recipe conditions — check tier availability
- `item.damage`, `item.defense`, `item.pick` — power comparison
- `player.armor[0..2]` — equipped armor detection
- `item.wingSlot` — wing detection + `ArmorIDs.Wing.Sets.Stats` for tier

#### Implementation Order

| Step | What | Files |
|------|------|-------|
| 1 | ProgressionTier detection | ProgressionTier.cs |
| 2 | ClassDetector | ClassDetector.cs |
| 3 | GearRecommender (scan + score) | GearRecommender.cs |
| 4 | AccessoryScanner (near-complete) | AccessoryScanner.cs |
| 5 | ProgressPanel UI | ProgressPanel.cs |
| 6 | Wire into CraftPathState as 5th tab | Update CraftPathState.cs |
| 7 | Pin-to-track integration | Wire to TrackingState |
| 8 | Build + test | Verify with vanilla + Calamity |

---

## 9. Mod Compatibility Strategy

### Why It Works With Any Mod Automatically

The core insight is that tModLoader's recipe system is centralized. When Calamity
registers a recipe, it goes into the same `Main.recipe[]` array as vanilla recipes.
Our `RecipeIndexSystem` iterates this array *after* all mods have loaded
(`PostAddRecipes`), so we see everything.

This means:
- **Calamity's 1000+ recipes** → automatically indexed
- **Thorium's recipes** → automatically indexed
- **Any future mod's recipes** → automatically indexed

No per-mod integration code needed.

### Where Compatibility Could Break

| Risk | Likelihood | Mitigation |
|------|-----------|------------|
| Mod uses non-standard recipe system (bypasses `Main.recipe[]`) | Very low | No known mods do this. tModLoader enforces the standard system. |
| Mod adds items with IDs that conflict | Impossible | tModLoader assigns unique IDs per mod. |
| NPC drop rules use custom `IItemDropRule` implementations | Medium | We handle known rule types and log unknown ones. Phase 4 adds extensibility for custom rule types. |
| Mod adds 10,000+ recipes, causing perf issues | Low | Our O(1) dictionary lookups handle this. The DFS depth limit prevents pathological trees. |
| Recipe groups span mod boundaries | Expected | `RecipeGroup.recipeGroups` is global. Our resolver handles groups natively. |

### Testing Matrix

| Mod | Why Test It | Key Items to Verify |
|-----|-------------|---------------------|
| **Calamity** | Largest content mod. 1500+ items. Complex recipe chains. | Exo weapons, Auric Tesla armor, Draedon's items |
| **Thorium** | Second-largest. Different design philosophy. | Dream Weaver, Maestro set |
| **Fargo's Mutant** | Adds QoL recipes for vanilla items. May create recipe alternatives. | Enchantments, Souls |
| **Vanilla only** | Baseline correctness. | Zenith, Ankh Shield, Cell Phone, Terra Blade |

---

## 10. Testing Strategy

### Unit Testing Approach

tModLoader mods don't have a traditional unit test framework, but we can build
testability into the design:

1. **Pure functions:** The `DependencyResolver` takes explicit inputs (recipe index,
   inventory dict, target) and returns explicit outputs (GraphNode tree). No global
   state dependency. This is testable by constructing mock inputs.

2. **In-game test command:** Register a `ModCommand` that runs known test cases:
   ```
   /cpo-test zenith     → Verify Zenith tree has correct depth and leaf count
   /cpo-test cycle      → Verify Wood→Platform→Wood cycle is handled
   /cpo-test group      → Verify Iron/Lead Bar group resolution
   /cpo-test calamity   → Verify Calamity items if Calamity is loaded
   ```

3. **Logging:** Use `Mod.Logger.Info()` / `.Warn()` to log resolver decisions.
   Enable verbose mode via ModConfig for debugging.

### Manual Testing Checklist

**Phase 1:**
- [ ] Search finds vanilla items by name
- [ ] Search finds Calamity items by name
- [ ] Inventory scanner counts items across all 6 containers
- [ ] Resolver produces correct tree for Zenith (known structure)
- [ ] Resolver handles recipes with conditions (shows them but marks as conditional)
- [ ] Material list shows correct have/need counts
- [ ] UI opens and closes with keybind
- [ ] UI doesn't intercept game input when closed
- [ ] No errors in log with Calamity loaded
- [ ] No errors in log with vanilla only

**Phase 2:**
- [ ] Tree expands and collapses correctly
- [ ] Craft order is correct (leaf-first)
- [ ] Multi-target merges materials correctly
- [ ] Tab switching works

**Phase 3:**
- [ ] NPC drop sources show correct NPC names
- [ ] Shop sources show correct NPC + price
- [ ] Calamity boss drops appear as sources
- [ ] Items with no known source show "Unknown source"

---

## 11. Risk Register

| # | Risk | Impact | Probability | Mitigation |
|---|------|--------|-------------|------------|
| 1 | **Recipe cycles cause infinite recursion** | Crash | Medium | Visited-set per DFS branch + depth limit of 20. Matches Recipe Browser's proven approach. |
| 2 | **Large mod packs cause frame drops during resolution** | Bad UX | Medium | Run resolver off main thread (Phase 4). Show loading indicator. Cache results. |
| 3 | **tModLoader API changes in future updates** | Maintenance | Low | Pin to 1.4.4 stable. `PostAddRecipes()` and `Main.recipe[]` are stable APIs unlikely to change. |
| 4 | **Custom IItemDropRule types from mods are unrecognized** | Incomplete source data | Medium | Handle known types, log unknowns. Add extensibility for custom handlers in Phase 4. |
| 5 | **UI performance with 10,000+ search results** | Laggy search | Medium | Limit search results to 50. Debounce input. Use `UIList` virtualization. |
| 6 | **Resolver picks suboptimal path** | Wrong advice | Low | Scoring function prioritizes fewest acquisitions, then fewest crafts. Allow user to view and switch alternatives. |
| 7 | **Inventory changes during resolution** | Stale data | Low | Snapshot inventory at resolution start. "Refresh" button for manual resync. |
| 8 | **Multiplayer desync** | N/A | N/A | Mod is `side = Client`. Uses only `Main.LocalPlayer`. No multiplayer concerns. |

---

## 12. Publishing Checklist

### Pre-Release

- [ ] Mod loads without errors (vanilla only)
- [ ] Mod loads without errors (with Calamity)
- [ ] All Phase 1-3 manual tests pass
- [ ] `description.txt` written with feature list + screenshots
- [ ] `icon.png` created (80x80, represents the mod visually)
- [ ] `build.txt` has correct metadata (displayName, author, version, homepage)
- [ ] `en-US.hjson` has all strings (no hardcoded English in code)
- [ ] ModConfig works (keybind, settings persist across sessions)
- [ ] README.md in repo with setup instructions

### Steam Workshop Publishing

1. In tModLoader: Workshop → Develop Mods → Publish
2. Set visibility to Public
3. Add tags: QoL, Informational, Client-side
4. Add screenshots showing: search panel, tree view, material list, craft order
5. Write changelog for initial release

### Post-Release

- [ ] Monitor tModLoader mod browser for crash reports
- [ ] Check compatibility with tModLoader updates
- [ ] Respond to feature requests in Steam Workshop comments
- [ ] Tag releases in git (v0.1.0, v0.2.0, etc.)

---

## 13. Learning Resources

### tModLoader Documentation

- [Official Docs Portal](https://docs.tmodloader.net/) — API reference for all classes
- [tModLoader Wiki](https://github.com/tModLoader/tModLoader/wiki) — guides and tutorials
- [Advanced UI Guide](https://github.com/tModLoader/tModLoader/wiki/Advanced-guide-to-custom-UI) — the definitive UI pattern reference
- [ExampleMod](https://github.com/tModLoader/tModLoader/tree/master/ExampleMod) — official example code for every feature
- [Intermediate Recipes Wiki](https://github.com/tModLoader/tModLoader/wiki/Intermediate-Recipes) — recipe system deep-dive

### Reference Mods (Source Code)

- [Recipe Browser](https://github.com/JavidPack/RecipeBrowser) — closest existing mod; study RecipePath.cs for cycle detection patterns
- [Magic Storage](https://github.com/blushiemagic/MagicStorage) — best reference for complex tModLoader UI
- [Boss Checklist](https://github.com/JavidPack/BossChecklist) — reference for cross-mod data aggregation

### C# / .NET 8

- [C# 12 What's New](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-12) — collection expressions, primary constructors
- [Records](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record) — we use these for GraphNode types
- [Pattern Matching](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/functional/pattern-matching) — heavily used in the resolver

### Algorithms

- [Topological Sort](https://en.wikipedia.org/wiki/Topological_sorting) — used for craft order
- [DFS with Backtracking](https://en.wikipedia.org/wiki/Backtracking) — the resolver's core algorithm
- [DAG (Directed Acyclic Graph)](https://en.wikipedia.org/wiki/Directed_acyclic_graph) — the recipe graph structure (with cycle detection to enforce acyclicity)

---

## Appendix A: Glossary

| Term | Definition |
|------|-----------|
| **Recipe** | A Terraria crafting recipe: inputs + crafting station → output item |
| **Recipe Group** | A set of interchangeable items in recipes (e.g., "Any Iron Bar" = Iron Bar or Lead Bar) |
| **Graph Node** | A node in our dependency tree (HaveNode, AcquireNode, CraftNode, GroupNode) |
| **DFS** | Depth-First Search — our traversal strategy for building the crafting tree |
| **Topological Sort** | Ordering nodes so that dependencies come before dependents (leaf-first) |
| **Craft Count** | Number of times a recipe must be executed: `ceil(needed / recipe.stack)` |
| **Item Source** | A non-crafting way to obtain an item (drop, shop, fishing, etc.) |
| **ModSystem** | tModLoader class for per-game/per-world logic (our systems extend this) |
| **ModPlayer** | tModLoader class for per-player logic (our keybind handler extends this) |
| **UIState** | tModLoader's root container for a custom UI screen |
| **UserInterface** | tModLoader's manager that routes input/drawing to the active UIState |

## Appendix B: Quick Reference — tModLoader Project Setup

```bash
# The .csproj should contain only this (tMLMod.targets handles everything else):
```

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="<tModLoaderPath>/tMLMod.targets" />
  <PropertyGroup>
    <AssemblyName>CraftPathOptimizer</AssemblyName>
    <RootNamespace>CraftPathOptimizer</RootNamespace>
  </PropertyGroup>
</Project>
```

```ini
# build.txt
displayName = Craft Path Optimizer
author = YourName
version = 0.1.0
side = Client
buildIgnore = *.csproj, *.user, *.sln, .vs/*, bin/*, obj/*, .git/*, .gitignore
```

```csharp
// Minimal Mod entry point
using Terraria.ModLoader;

namespace CraftPathOptimizer;

public class CraftPathOptimizer : Mod
{
    public static ModKeybind ToggleUIKeybind { get; private set; }

    public override void Load()
    {
        ToggleUIKeybind = KeybindLoader.RegisterKeybind(this, "Toggle Craft Path", "P");
    }

    public override void Unload()
    {
        ToggleUIKeybind = null;
    }
}
```

## Appendix C: Key Decision Log

This section tracks important design decisions and their rationale. Update as we go.

| # | Decision | Rationale | Date |
|---|----------|-----------|------|
| 1 | tModLoader mod, not web app | Auto-access to all mod recipes + inventory; no data pipeline needed | 2026-03-27 |
| 2 | `side = Client` | Mod is purely informational UI; no server-side effects | 2026-03-27 |
| 3 | DFS with backtracking (not BFS) | Recipes form deep chains, not wide; DFS naturally maps to the tree structure we render | 2026-03-27 |
| 4 | Depth limit of 20 | Matches Recipe Browser's proven limit; prevents pathological recursion | 2026-03-27 |
| 5 | Snapshot inventory (not live) | Avoid mid-resolution inconsistencies; explicit refresh is simpler and predictable | 2026-03-27 |
| 6 | C# records for GraphNode types | Immutable, pattern-matchable, value equality — perfect for tree nodes | 2026-03-27 |
| 7 | No external NuGet dependencies | Zero version conflicts with other mods; tModLoader ships everything we need | 2026-03-27 |
| 8 | net8.0 + C# 12 | Matches current tModLoader targets; gives us modern language features | 2026-03-27 |
| 9 | Nearby chest scanning in Phase 1 (with toggle) | High QoL value; `Main.chest[]` iteration is cheap; toggle defaults OFF so no UX surprise | 2026-03-27 |
| 10 | Scoring = fewest total acquisitions | Simple, correct, and the metric players care about most; revisit if users request more nuance | 2026-03-27 |
| 11 | Multi-target resolve order = queue insertion order | Predictable for the user; re-resolves on reorder; no need for optimization heuristics | 2026-03-27 |
| 12 | Equipped items count as owned with `[E]` marker | Player should know they own it; UI badge lets them decide whether to unequip | 2026-03-27 |
| 13 | Search result cap = 50 items | Enough to find anything; avoids lag with 10k+ modded items | 2026-03-27 |
| 14 | Non-drop/non-shop items show "Unknown source" for MVP | Static source tables are maintenance burden; ship without and add later based on demand | 2026-03-27 |
| 15 | Custom IItemDropRule: defer to Phase 4 | Most mods use vanilla rule types; reflection fallback is low-risk/low-priority | 2026-03-27 |
