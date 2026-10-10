# UI/UX Improvement Plan

Based on comprehensive audit of all UI components. Ordered by impact.

---

## 1. Pin Button on Compact Upgrade Cards

**Problem:** Players view upgrades in the 4-column window but can't pin items to tracking without going back to the dashboard or search.

**Fix:** Add a small pin icon/button to `UICompactUpgradeCard`. On click, fire an `OnPinItem` event (same pattern as `UIUpgradeItemRow`). Show pinned state (filled vs outline icon).

**Files:**
- `UI/Elements/UICompactUpgradeCard.cs` — add pin button + event
- `Common/Systems/UpgradeWindowSystem.cs` — wire `OnPinItem` through panel → state

**Complexity:** Low

---

## 2. Cross-Column Linkage in Upgrade Window

**Problem:** "Where to Go" shows biomes feeding upgrades, but there's no visual connection to which items in the readiness columns benefit. Player must mentally cross-reference.

**Fix:** When hovering a location card, highlight the upgrade items it feeds in the readiness columns (subtle glow or border). When hovering a readiness card, highlight locations that provide its missing materials.

**Approach:** Store the `UpgradeItem.ItemType` → `LocationRanking` mapping. On hover, set a shared `_highlightedUpgradeType` or `_highlightedLocationName` field. Each card checks this during DrawSelf and applies a highlight border if matched.

**Files:**
- `Common/Systems/UpgradeWindowSystem.cs` — add shared highlight state, pass to cards
- `UI/Elements/UICompactUpgradeCard.cs` — check highlight state, draw border
- `UI/Elements/UILocationCard.cs` — check highlight state, draw border; set highlight on hover

**Complexity:** Medium

---

## 3. Recipe Cycling Discoverability

**Problem:** Right-click to cycle recipe alternatives in the tree view is completely hidden. No tooltip, no visual affordance.

**Fix:**
- Add "(Right-click to change)" tooltip text when hovering a node with alternatives
- Make the "(Recipe 2/3)" text a brighter hover color as an affordance
- Show on first encounter: a one-time hint text at the top of the tree panel when alternatives exist

**Files:**
- `UI/Elements/UITreeNode.cs` — add hover tooltip for alternative nodes
- `UI/Panels/DependencyTreePanel.cs` — optional first-time hint

**Complexity:** Low

---

## 4. Craft Order Step Context

**Problem:** Steps show "Step 3:" but not "Step 3 of 8". No visual indicator of which steps are ready (all inputs owned) vs blocked.

**Fix:**
- Pass total step count to each `UICraftStep` and display "Step X of Y"
- Add a status indicator: green dot if all inputs are Owned/FromPreviousStep, orange dot if any input is NeedToAcquire
- Optionally: dim completed steps (all inputs owned)

**Files:**
- `UI/Elements/UICraftStep.cs` — add total count param, status dot
- `UI/Panels/CraftOrderPanel.cs` — pass total count when creating steps

**Complexity:** Low

---

## 5. Minimum Text Scale Bump

**Problem:** Text at 0.4-0.45 scale is nearly illegible at 1080p. Source hints on compact cards, condition warnings, and overlay details are affected.

**Fix:** Audit all `Utils.DrawBorderString` calls across UI elements. Bump minimum scale:
- 0.4 → 0.5 (source hints, small annotations)
- 0.45 → 0.5 (condition warnings, secondary info)
- 0.42 → 0.5 (overlay source info)
Keep 0.55+ as-is (already readable).

**Files:**
- `UI/Elements/UICompactUpgradeCard.cs` — source hint 0.4→0.5
- `UI/Elements/UILocationCard.cs` — context hint 0.45→0.5, subtitle 0.5 stays
- `UI/Elements/UITreeNode.cs` — condition description 0.55 stays, alt text 0.55 stays
- `UI/Elements/UICraftStep.cs` — condition 0.65 stays
- `UI/Elements/UIActionGroupCard.cs` — alt sources 0.5 stays, attribution 0.45→0.5
- `Common/Systems/TrackingOverlaySystem.cs` — source info 0.42→0.5

**Complexity:** Low (find-and-replace)

---

## 6. Interactive Overlay

**Problem:** Tracking overlay is read-only. Can't click goals to analyze, can't unpin, can't expand.

**Fix:**
- Left-click a tracked goal row → fire event to analyze item in main panel
- Right-click a tracked goal row → unpin it
- Add a small "..." or expand button to show more goals if >5 are tracked
- Add hover effect (brighten background) to indicate interactivity

**Files:**
- `Common/Systems/TrackingOverlaySystem.cs` — add click/right-click handlers to goal rows, hover effects
- `UI/CraftPathState.cs` — handle overlay analyze/unpin events

**Complexity:** Medium

---

## 7. "Why Recommended?" Context

**Problem:** Dashboard/upgrade window shows items but never explains why they're recommended. Players see a score but not the reasoning.

**Fix:** On hover over an upgrade item (in dashboard or upgrade window), show a tooltip explaining:
- For weapons: "Higher DPS than your current {weapon} ({old} → {new})"
- For armor: "+{delta} defense over current armor"
- For accessories: the `AccessoryEffects.GetImprovementText()` output (already implemented)
- For combinations: "You have {X}/{Y} materials — {missing} items left"

The `ImprovementText` field on `UpgradeItem` already carries much of this. Just ensure it's visible on hover as a secondary tooltip line, not just inline text that gets truncated.

**Approach:** Add a `Main.hoverItemName` suffix or a custom tooltip overlay when hovering upgrade rows/cards. Use `UpgradeItem.ImprovementText` as the basis.

**Files:**
- `UI/Elements/UICompactUpgradeCard.cs` — show improvement text in hover tooltip
- `UI/Elements/UIUpgradeItemRow.cs` — ensure improvement text visible on hover

**Complexity:** Low

---

## 8. Queue Indicator

**Problem:** Multi-item analysis queue exists (`TargetQueue`) but the UI never shows it. Player doesn't know items are queued.

**Fix:** When `_queue.Count > 1`, show a badge on the analysis indicator or near the search bar: "Queue: 3 items" or a small numbered badge.

**Files:**
- `UI/CraftPathState.cs` — check `_queue.Count` in the update/draw loop
- `UI/Elements/UIAnalysisIndicator.cs` — add queue count badge

**Complexity:** Low

---

## 9. Search Improvements

**Problem:** No clear button, no result count, no indication when results are truncated.

**Fix:**
- Add "X" clear button at right edge of search bar
- Show result count below search bar: "12 results" or "50 of 127 results (showing max)"
- Add "No items matching '{query}'" empty state (may already exist)

**Files:**
- `UI/Panels/TargetSelectionPanel.cs` — add clear button, result count display

**Complexity:** Low

---

## 10. Shared Trivial Materials Constant

**Problem:** The `_trivialMaterials` HashSet is duplicated identically in `PlayerDashboard.cs` and `UpgradeWindowSystem.cs`. Bug risk if one is updated without the other.

**Fix:** Move to a shared location — either `UIHelper` or a new `ProgressionConstants` static class. Both files reference the single source.

**Files:**
- `UI/UIHelper.cs` or new `Content/Progression/ProgressionConstants.cs` — define shared set
- `UI/Panels/PlayerDashboard.cs` — reference shared set
- `Common/Systems/UpgradeWindowSystem.cs` — reference shared set

**Complexity:** Trivial

---

## Implementation Order

### Batch 1: Quick wins (30 min)
- **#10** Shared trivial materials
- **#5** Text scale bump
- **#3** Recipe cycling tooltip
- **#4** Step count + status dots

### Batch 2: Pin + context (1 hr)
- **#1** Pin button on compact cards
- **#7** "Why recommended?" hover tooltip
- **#8** Queue indicator

### Batch 3: Interactivity (1.5 hr)
- **#9** Search improvements
- **#6** Interactive overlay
- **#2** Cross-column highlight linkage
