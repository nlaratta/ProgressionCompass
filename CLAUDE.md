# Craft Path Optimizer — tModLoader Mod

A client-side tModLoader mod that computes optimal crafting paths for any item, with automatic support for all loaded content mods (Calamity, Thorium, etc.).

## Project Structure

```
CraftPathOptimizer/
  CraftPathOptimizer.cs              — Mod entry point, keybind registration
  CraftPathOptimizer.csproj          — References tMLMod.targets (handles everything)
  build.txt                          — side = Client, mod metadata
  Common/Systems/
    RecipeIndexSystem.cs             — Indexes Main.recipe[] into fast dictionaries (PostAddRecipes)
    ItemSourceSystem.cs              — Indexes NPC drops + shops for "where to get it" info
    CraftPathUISystem.cs             — UI lifecycle: create, update, draw, toggle
  Common/Players/
    CraftPathPlayer.cs               — Keybind handler (ProcessTriggers)
  Content/Graph/
    GraphNode.cs                     — Node hierarchy: HaveNode, AcquireNode, CraftNode, GroupNode
    DependencyResolver.cs            — Core DFS algorithm with backtracking + cycle detection
    InventoryScanner.cs              — Scans player storage + equipped + nearby chests
    InventoryEntry.cs                — Per-item breakdown: personal/equipped/chest counts
    ItemSource.cs                    — ItemSource record + ItemSourceType enum
    ResolverResult.cs                — Result container: tree, materials, craft order
  UI/
    CraftPathState.cs                — Root UIState, orchestrates resolve flow
    Elements/                        — Reusable UI components (search bar, item slot, tabs, etc.)
    Panels/                          — Tab panels (TargetSelection, MaterialList)
  Localization/en-US.hjson           — All user-facing strings
```

## Build

```bash
cd ProgressionCompass
dotnet build
```

Requires .NET 8.0 SDK and a tModLoader install.

The `.csproj` locates tModLoader via the `tMLPath` property and imports its
`tMLMod.targets`, which supplies the target framework (net8.0), C# 12, all
assembly references, and `.tmod` packaging. Do not add NuGet packages or manual
assembly references.

`tMLPath` resolves in this order — the first hit wins:

1. `dotnet build -p:tMLPath="/path/to/tModLoader"`
2. `Directory.Build.props` next to the `.csproj` (untracked; copy
   `Directory.Build.props.example` and edit the path)
3. The `tMLPath` / `TMLPATH` environment variable
4. The default Steam install location for the OS — on macOS,
   `~/Library/Application Support/Steam/steamapps/common/tModLoader`

If tModLoader is installed in the Steam default location, `dotnet build` works
with no configuration. Otherwise the build fails with an error naming all three
ways to point it at your install.

**Do not commit an absolute path here.** The `.csproj` previously hardcoded one
developer's Steam library, which meant it built on exactly one machine.

## Architecture

**Data flow:** PostAddRecipes indexes recipes → keybind opens UI → InventoryScanner snapshots all storage → DependencyResolver builds optimal tree via DFS → UI panels render results.

**Key design:** The resolver uses snapshot-and-rollback for backtracking. Before trying each recipe branch, it copies the `available` dictionary. If that branch loses, it restores the snapshot. Cycle detection uses a `HashSet<int>` of recipe indices on the current DFS path (not global). Depth limit is 20.

## tModLoader API Patterns

### Text Rendering
Always use `Utils.DrawBorderString(spriteBatch, text, position, color)` for drawing text. **Never use `spriteBatch.DrawString(font, ...)`** — Terraria's fonts are `DynamicSpriteFont` (ReLogic), not XNA's `SpriteFont`, so the standard XNA DrawString overloads won't compile.

For measuring text, `FontAssets.MouseText.Value.MeasureString(text)` works fine — `DynamicSpriteFont` implements MeasureString correctly.

### UI Class Naming
`UIList`, `UIScrollbar`, `UIItemSlot`, and `UISearchBar` already exist in `Terraria.GameContent.UI.Elements`. Our custom versions must use different names (we prefix with `UICraft`). Always add `using Terraria.GameContent.UI.Elements;` when using lists/scrollbars.

### Namespace vs Class Name Collision
The mod class `CraftPathOptimizer` shares its name with the root namespace `CraftPathOptimizer`. When referencing namespaces from within **any** file in the project, use `global::CraftPathOptimizer.X.Y.Z` to disambiguate. This applies to referencing config, systems, or elements from the UI namespace. This is a recurring issue — always use `global::` when the compiler says "type name does not exist in type".

### Magic Number Warnings
tModLoader's code analyzer flags `<= 0` checks on item/NPC types. Use `ItemID.None` and `NPCID.None` instead of `0` for readability.

### Lifecycle Hooks
- `PostAddRecipes()` — index recipes here, NOT in Load(). This fires after all mods register recipes.
- `PostSetupContent()` — index NPC drops/shops here. Content is fully loaded.
- `Load()` — UI creation, keybind registration. Guard with `if (Main.dedServ) return;` for client-only code.
- `Unload()` — null out all static references to avoid memory leaks across mod reloads.

### UI Framework
- `ModSystem.ModifyInterfaceLayers()` — insert draw layer before "Vanilla: Mouse Text"
- `Main.LocalPlayer.mouseInterface = true` — set this when hovering interactive UI to block game input
- `Main.HoverItem = item.Clone()` — triggers vanilla tooltip rendering on hover

### ModConfig Localization
`[Label]` and `[Tooltip]` attributes are **deprecated** in current tModLoader. Config labels and tooltips must be defined in `Localization/en-US.hjson` under `Mods.CraftPathOptimizer.Configs.ClassName.PropertyName.Label` / `.Tooltip`. Use `[Header("SectionName")]` with corresponding `Headers.SectionName` localization.

### Async + Game State
Game state (`Main.LocalPlayer`, `Main.chest[]`, etc.) must be read on the main thread. Snapshot data before passing to `Task.Run`. The resolver is CPU-only and safe to run in the background.

### Magic Storage Integration
Uses `ModLoader.TryGetMod("MagicStorage", out Mod)` + `mod.Call("GetItems")` to avoid a hard dependency. Wrapped in try-catch since the API may differ by version. Items from Magic Storage are tagged separately in `InventoryEntry.MagicStorageCount`.

### IItemDropRule Reflection Fallback
For unrecognized modded drop rule types, `ItemSourceSystem.TryExtractViaReflection` looks for fields named `itemId`, `itemType`, `dropId`, or `dropIds` via reflection. This catches most convention-following modded rules without requiring per-mod integration code.

### Texture Bounds Checks
Always check `ItemType >= TextureAssets.Item.Length` before accessing textures. Some modded items may have IDs beyond the texture array bounds, especially during mod loading/unloading.

### UIHelper (UI/UIHelper.cs)
All shared UI rendering logic is consolidated here. **Always use these instead of inline code:**
- `UIHelper.DrawItemIcon(spriteBatch, itemType, position, maxSize)` — item icon with bounds check, animation, scaling
- `UIHelper.DrawItemIconCentered(...)` — icon centered within a box
- `UIHelper.ShowItemTooltip(itemType, suffix?)` — vanilla tooltip on hover
- `UIHelper.GetSourceColor(ItemSourceType)` — canonical color for drop/shop/gather source tags
- `UIHelper.GetActionAccentColor(ActionType)` — text color for TODO/overlay action groups
- `UIHelper.GetActionBackgroundColor(ActionType)` — card background for TODO tab
- `UIHelper.DrawBorder(spriteBatch, rect, color)` — 1px rectangular border
- `ItemSource.PickBest(sources)` / `ItemSource.RecommendationScore` — source ranking (in ItemSource.cs)

Never duplicate these patterns inline. If a new UI element needs to render an item icon, show a tooltip, or color a source tag, use UIHelper.

### Multi-Window Click-Through Prevention
tModLoader's `UserInterface` instances process mouse events independently — they don't know about each other's z-order. When a popup window (like Upgrade Recommendations) overlaps the main panel, clicks pass through to both.

**Pattern for any new window:**
1. The window's `ModSystem` must track `IsMouseBlocked` — set `true` when the mouse is over the window panel each frame (check in `UpdateUI` via `panel.ContainsPoint(Main.MouseScreen)`)
2. The window's `UIState` must expose `GetPanel()` so the system can check containment
3. `UIDraggablePanel.LeftMouseDown` checks `UpgradeWindowSystem.IsMouseBlocked` before processing — if any overlay window is consuming the mouse, the main panel ignores the click

If adding another popup window, either extend this pattern (check all window systems) or create a shared `WindowManager` that tracks which window has mouse focus.

## Conventions

- File-scoped namespaces throughout (`namespace X;` not `namespace X { }`)
- C# 12 features: records for data types, collection expressions `[]`, pattern matching
- `side = Client` in build.txt — mod is purely informational, no server effects
- All user-facing strings in `Localization/en-US.hjson`, not hardcoded
- Use `global::CraftPathOptimizer.X.Y` for cross-namespace references (class/namespace collision)

## Testing

In-game chat command `/cpo` with subcommands:
- `/cpo stats` — recipe index + inventory scan counts
- `/cpo zenith` / `terra` / `ankh` / `cell` — resolve known items, log timing + tree stats
- `/cpo cycle` — verify Wood cycle detection
- `/cpo all` — run all tests

## Plan

Full implementation plan with architecture, algorithm pseudocode, UI mockups, phase breakdown, risk register, and testing strategy is in `/Volumes/DevDrive1/Projects/Terraria/PLAN.md`.
