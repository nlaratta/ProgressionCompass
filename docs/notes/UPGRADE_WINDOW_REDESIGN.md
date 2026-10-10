# Upgrade Window Redesign: 4-Column Layout

## Current State

The upgrade window (`UpgradeRecommendationsPanel`) uses a 2-column layout:
- **Left (55%)**: Upgrade list grouped by acquisition method (Craftable Weapons/Armor/Accessories/Tools, Boss Drops, Shop Items, Mob Drops, Events)
- **Right (45%)**: "Where to Go" source priority rankings

## Proposed Change

Replace the single upgrade list with **3 readiness columns**, keeping the source priority column wider:

```
+====================================================================+
| [drag]  Progression & Upgrades                                [X]  |
|--------------------------------------------------------------------|
| [All] [Melee] [Ranged] [Mage] [Summon]                            |
|--------------------------------------------------------------------|
| [ Stage Card ] [ Boss Progression Card              ]              |
|====================================================================|
|              |              |              |                        |
| READY (3)    | NEARLY (5)   | BUILDING (8) | WHERE TO GO           |
| ~17%         | ~17%         | ~17%         | ~49%                  |
|              |              |              |                        |
| [i] Night's  | [i] Ankh     | [i] Terra    | ┌─ Ug Jungle ★★★★★ ┐ |
|   Edge       |   Shield     |   Blade      | │ Feeds 3 upgrades   │ |
|   CRAFT!     |   92%        |   55%        | │ Hornet → Bezoar    │ |
|              |   ████████░░ |   █████░░░░░ | │  → Ankh Charm(92%) │ |
| [i] Blade    |              |              | │ ...                 │ |
|   of Grass   | [i] Megashark| [i] Zenith   | └─────────────────────┘ |
|   CRAFT!     |   85%        |   42%        |                        |
|              |   ████████░  |   ████░░░░░░ | ┌─ Dungeon ─── ★★★★ ┐ |
| [scroll]     | [scroll]     | [scroll]     | │ ...                │ |
|              |              |              | │ [scroll]            │ |
+====================================================================+
```

## Column Specifications

| Column | Width | Content | Scroll |
|--------|-------|---------|--------|
| Ready to Craft | ~17% | Items at 100% completion | Independent |
| Nearly Ready | ~17% | Items at 80-99% completion | Independent |
| Building Toward | ~17% | Items at 40-79% completion | Independent |
| Where to Go | ~49% | Source priority biome cards | Independent |

**Default window size**: 1080w x 680h (up from 960x680)

## Compact Item Card Design

Each card in the 3 readiness columns (~185px usable width):

```
┌──────────────────┐
│ [icon] Item Name  │  ← 0.55f scale, truncate with ellipsis
│ ████████░░ 85%   │  ← progress bar + percentage
│ Drop: Zombie     │  ← source hint (0.45f scale, gray)
└──────────────────┘
```

- Height: ~48px per card
- "Ready" column cards get green "CRAFT!" badge instead of progress bar
- Hover shows full vanilla tooltip via UIHelper.ShowItemTooltip()
- Click fires OnAnalyzeItem to open craft path in main panel

## Column Headers

Each column has a header showing count and colored indicator:
- **Ready (3)** — green accent
- **Nearly (5)** — yellow accent
- **Building (8)** — gray accent
- **Where to Go** — existing header

## Implementation Changes

### Files to modify
- `Common/Systems/UpgradeWindowSystem.cs` — replace single `_list` with `_readyList`, `_nearlyList`, `_buildingList`; adjust column widths; new `RenderReadyColumn()`, `RenderNearlyColumn()`, `RenderBuildingColumn()` methods
- Potentially new `UI/Elements/UICompactUpgradeCard.cs` — smaller card for narrow columns

### What stays the same
- `SourcePriorityAnalyzer`, `BiomeMapper`, `SourcePriorityResult` — no changes
- `UILocationCard` — no changes (right column unchanged)
- `UIHelper` additions — no changes
- Class filter logic — still filters all 4 columns
- Info cards (stage + boss) — still span full width above columns

### What gets removed/replaced
- `RenderList()` method (grouped by acquisition) → 3 separate column render methods
- `AddCategorySection()` — no longer needed (no acquisition grouping)
- `UIUpgradeItemRow` usage in this panel — replaced by compact cards (UIUpgradeItemRow still used elsewhere)

## Dividers

3 vertical 1px dividers between the 4 columns, same style: `Color(60, 70, 130) * 0.4f`
