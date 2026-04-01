# Craft Path Optimizer — Issue Tracker

## Critical (Release Blockers)

- [ ] **No mod icon** — Need 396x396 PNG `icon.png` for Steam Workshop display
- [ ] **Silent error handling** — 212 try-catch blocks with no logging. Add `Mod.Logger.Warn()` in catch blocks across all systems
- [ ] **Hardcoded UI strings** — Move to `en-US.hjson`: "Type at least 2 characters", "No items matching...", tree empty messages, tier names, action group headings
- [ ] **Localization typo** — Line 5 of `en-US.hjson` has malformed keybind entry (double spaces)

## High Priority (Pre-Release Polish)

- [ ] **Overlay position not saved** — Resets to default on each load. Save `_posX`/`_posY` in `TrackingPlayer.SaveData`
- [ ] **Private field access** — `TargetSelectionPanel` accesses `_resultsList._items` (private). Replace with public API or reflection
- [ ] **No confirm on Clear All** — Accidentally wiping all tracked items with no undo. Add confirmation prompt
- [ ] **Escape doesn't close sub-windows** — Closing main panel via Escape leaves upgrade window floating. Wire Escape to close all windows
- [ ] **User-facing error messages** — When resolver crashes: "Crafting path too complex." When tier detection fails: "Unable to detect progression tier."
- [ ] **Mod reload safety** — Test `/reload mods` with UI open. Verify tracked items persist, overlay position saves, no null refs

## Medium Priority (UX Improvements)

- [ ] **Copy craft plan to clipboard** — "Copy list" button on materials tab, formatted as item + quantity
- [ ] **Search filters** — Filter by source type: craftable, drops, shop, all
- [ ] **Help screen / keybind display** — "?" button showing all keybinds and usage tips
- [ ] **Collapse/expand all** — Buttons for tree view to collapse or expand entire tree
- [ ] **Drag-to-reorder goals** — Let users prioritize tracked items by dragging
- [ ] **Search result rarity** — Show item rarity color in search results for quick tier identification
- [ ] **Quantity targeting** — Allow tracking "I need 5 of this item" not just "I need 1"

## Low Priority (0.2.0+)

- [ ] **Multi-language support** — Add localization files beyond en-US
- [ ] **Font size config** — Add option in CraftPathConfig for UI font scaling
- [ ] **Changelog** — Create CHANGELOG.md documenting features per version
- [ ] **Steam Workshop metadata** — Description, screenshots, tags (Quality of Life, Crafting, Utilities)
- [ ] **Color palette system** — Extract hardcoded Color(...) constants into a shared palette
- [ ] **Performance profiler** — Log resolver time + node count, warn if tree exceeds 500 nodes
- [ ] **Undo for tracking** — Keep history of last 5 pin/unpin actions, support undo
- [ ] **Modded content warnings** — Detect orphaned tracked items from disabled mods
- [ ] **Config migration** — Version config schema so future changes don't break existing saves
