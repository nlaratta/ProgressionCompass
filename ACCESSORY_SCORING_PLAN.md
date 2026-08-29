# Accessory Scoring Improvement Plan

Current state: GearRecommender scores accessories via `ScoreAccessoryFromTooltip` — regex parsing of tooltip text for percentage/flat bonuses. Works for vanilla but fragile for modded content with non-standard tooltip formats. Wings already use structured `ArmorIDs.Wing.Sets.Stats`.

---

## Improvement 1: Slot-Based Accessory Classification

### Problem
All non-wing accessories are scored through a single tooltip parser with no awareness of *what kind* of accessory it is. A shield and a pair of boots get the same scoring logic even though their value propositions are completely different.

### What We Learned
Every `Item` has equip slot fields that identify accessory type:

| Field | Category | Examples |
|---|---|---|
| `wingSlot > 0` | Flight | All wings (already handled) |
| `shieldSlot > 0` | Shield/Defense | Cobalt Shield, Ankh Shield, Shield of Cthulhu |
| `balloonSlot > 0` | Jump boost | Shiny Red Balloon, Bundle of Balloons |
| `shoeSlot > 0` | Movement/Boots | Hermes Boots, Lightning Boots, Terraspark |
| `neckSlot > 0` | Neck/Defensive | Cross Necklace, Worm Scarf, Star Veil |
| `handOnSlot > 0` | Melee/Combat | Fire Gauntlet, Power Glove, Celestial Cuffs |
| `faceSlot > 0` | Face/Vision | Diving Helmet, Obsidian Skull |
| `backSlot > 0` | Back/Utility | Magic Quiver, Hive Pack |
| `waistSlot > 0` | Waist | — |
| All slots = -1 | Unclassified | ~30% of accessories (Obsidian Rose, Mana Flower, etc.) |

### Approach
1. Add `AccessorySlotCategory` enum: `Wing, Shield, Balloon, Boots, Neck, HandOn, Face, Back, Other`
2. Add static `GetAccessoryCategory(Item item) → AccessorySlotCategory` helper
3. In GearRecommender, use category for smarter scoring:
   - **Shields**: High base value if player has no knockback immunity. Compare against existing shield.
   - **Boots**: Compare movement speed tier (Hermes < Lightning < Terraspark). Only recommend if faster than equipped.
   - **Balloons**: Compare jump types. Bundle > single balloon.
   - **Neck**: High value for defensive accessories (Cross Necklace → extended invincibility).
   - **HandOn**: Combat gloves — bonus for class matching (Fire Gauntlet for melee players).
   - **Other**: Fall back to tooltip parsing (existing behavior).
4. Use category in dashboard/upgrade display for grouping: "Accessory: Shield", "Accessory: Boots"

### Files to Modify
- `Content/Progression/GearRecommender.cs` — category-aware scoring branch before tooltip fallback
- `Content/Progression/UpgradeItem.cs` or `GearCategory` — could add sub-categories

### Impact
- 60-70% of accessories get category-aware scoring
- Better recommendations: won't suggest a second shield when player has one
- Better display: "Shield upgrade" vs generic "Accessory"

---

## Improvement 2: Direct Numeric Field Scoring

### Problem
Some accessories provide `defense`, `lifeRegen`, or `manaIncrease` directly on the Item, but `ScoreAccessoryFromTooltip` might miss these if the tooltip doesn't match the expected regex patterns.

### What We Learned
Fields readable directly from `ContentSamples.ItemsByType[type]`:
- `item.defense` — direct defense bonus (e.g., Shackle gives 1 defense)
- `item.lifeRegen` — direct regen bonus
- `item.manaIncrease` — direct max mana bonus
- `item.expertOnly` / `item.masterOnly` — availability gates

### Approach
1. Before tooltip parsing, check these direct fields:
   ```csharp
   float directScore = 0;
   directScore += item.defense * 4f;
   directScore += item.lifeRegen * 3f;
   directScore += item.manaIncrease * 0.5f;
   ```
2. Combine with tooltip score (don't replace — tooltip catches percentage bonuses that aren't in fields)
3. Filter `expertOnly`/`masterOnly` items using existing WorldContext difficulty check

### Files to Modify
- `Content/Progression/GearRecommender.cs` — add direct field scoring in accessory block

### Impact
- Catches accessories where tooltip parsing fails but direct fields are set
- More accurate for modded accessories that set defense/regen/mana fields
- Tiny change, very safe

---

## Improvement 3: Differential Player Field Analysis (The Big One)

### Problem
~85% of accessory effects are applied imperatively via `Player.ApplyEquipFunctional()` and `ItemLoader.UpdateAccessory()`. The effects are set on Player fields (`accRunSpeed`, `noKnockback`, `dashType`, `endurance`, `jumpBoost`, etc.) but there's no data-driven way to read them. Tooltip parsing catches maybe 60% of these effects.

### What We Learned
The `Player.ResetEffects()` method resets ~100+ accessory-related fields each tick. By snapshotting a Player before and after applying an accessory, we can measure exactly what it does. This works for both vanilla AND modded accessories.

Key fields to diff (~25 high-value fields):

**Movement:**
- `accRunSpeed` (float) — run speed boost value
- `rocketBoots` (int) — 0/1/2 rocket boot tier
- `dashType` (int) — 0/1/2+ dash type
- `jumpBoost` (bool) — extended jump
- `jumpSpeedBoost` (float) — jump speed
- `noFallDmg` (bool) — fall immunity
- `moveSpeed` (float) — movement multiplier
- `iceSkate` (bool)
- `waterWalk` / `waterWalk2` (bool)
- `accFlipper` (bool) — swimming

**Combat:**
- `noKnockback` (bool) — knockback immunity
- `endurance` (float) — damage reduction (Worm Scarf = 0.17)
- `armorPenetration` (float) — armor pen bonus
- `allDamage` (float) — general damage multiplier
- `allCrit` (float) — general crit bonus
- `aggro` (int) — threat level
- `blackBelt` (bool) — dodge
- `kbGlove` (bool) — knockback boost
- `autoReuseGlove` (bool) — auto-reuse
- `magicQuiver` (bool) — arrow bonus

**Survivability:**
- `lavaImmune` (bool) — lava immunity
- `longInvince` (bool) — extended iframes
- `statManaMax2` (int) — bonus max mana
- `statLifeMax2` (int) — bonus max life

### Approach

**New class: `AccessoryEffectAnalyzer`** in `Content/Progression/`

```
static class AccessoryEffectAnalyzer
{
    // Computed once during PostSetupContent, cached permanently
    static Dictionary<int, AccessoryEffects> _cache;

    static void Initialize()   // called from PostSetupContent
    static AccessoryEffects Get(int itemType)
    static void Unload()
}

record AccessoryEffects(
    // Movement
    float RunSpeedBoost,
    int RocketBootsTier,
    int DashType,
    bool JumpBoost,
    float JumpSpeedBoost,
    bool NoFallDamage,
    float MoveSpeedBoost,
    bool Swimming,
    bool WaterWalk,
    bool IceSkate,

    // Combat
    bool KnockbackImmunity,
    float DamageReduction,
    float ArmorPenetration,
    float AllDamageBoost,
    float AllCritBoost,
    int AggroChange,
    bool DodgeChance,
    bool KnockbackBoost,
    bool AutoReuse,
    bool MagicQuiver,

    // Survivability
    bool LavaImmune,
    bool ExtendedIFrames,
    int BonusMana,
    int BonusLife,

    // Computed
    float CombatScore,
    float MobilityScore,
    float SurvivalScore,
    float TotalScore
)
```

**Initialization algorithm:**
1. Create a scratch `Player` instance
2. For each accessory item in `ContentSamples.ItemsByType`:
   a. Call `player.ResetEffects()` to clear all fields
   b. Snapshot relevant fields into "before" values
   c. Call `player.ApplyEquipFunctional(item, false)` (vanilla accessories)
   d. Call `ItemLoader.UpdateAccessory(item, player, false)` (modded accessories)
   e. Diff relevant fields against "before" → build `AccessoryEffects`
   f. Compute category scores from the diffs

**Scoring formulas:**
```
CombatScore =
    (KnockbackImmunity ? 15 : 0) +
    DamageReduction * 100 +           // 0.17 → 17 points (Worm Scarf)
    ArmorPenetration * 3 +
    AllDamageBoost * 200 +            // 0.04 → 8 points
    AllCritBoost * 150 +
    (DodgeChance ? 20 : 0) +
    (KnockbackBoost ? 5 : 0) +
    (AutoReuse ? 8 : 0) +
    (MagicQuiver ? 12 : 0)

MobilityScore =
    RunSpeedBoost * 3 +
    RocketBootsTier * 15 +
    DashType * 20 +
    (JumpBoost ? 10 : 0) +
    JumpSpeedBoost * 5 +
    (NoFallDamage ? 8 : 0) +
    MoveSpeedBoost * 10 +
    (Swimming ? 5 : 0) +
    (WaterWalk ? 5 : 0)

SurvivalScore =
    (LavaImmune ? 15 : 0) +
    (ExtendedIFrames ? 12 : 0) +
    BonusMana * 0.3 +
    BonusLife * 2

TotalScore = CombatScore + MobilityScore + SurvivalScore
```

**Integration with GearRecommender:**
```csharp
// In the accessory scoring block (after wing check):
var effects = AccessoryEffectAnalyzer.Get(type);
if (effects != null && effects.TotalScore > 0)
{
    score = effects.TotalScore;
    improvement = BuildImprovementText(effects);  // "Knockback immunity, 17% DR, Dash"
}
else
{
    // Fall back to tooltip parsing for unanalyzed accessories
    score = ScoreAccessoryFromTooltip(item);
}
```

**Improvement text generation:**
Build a concise string from the highest-impact effects:
- "Knockback immunity, 17% DR" (Worm Scarf)
- "Dash, +6 run speed" (Shield of Cthulhu)
- "Swim, Water walk, No fall damage" (Terraspark Boots)
- "+20 mana, Auto mana potion" (Mana Flower)

### Thread Safety
`Player.ApplyEquipFunctional` and `ItemLoader.UpdateAccessory` modify Player fields but don't read game state (they operate on the passed Player instance). Safe to run during initialization on the main thread. The cached `_cache` dictionary is immutable after init — safe for background reads.

**Risk: Some modded accessories read game state in UpdateAccessory** (checking Main.hardMode, NPC.downedBoss, etc.). These might produce different results based on world state. Mitigation: run the analysis on a "blank" Player and accept that state-dependent effects may not be captured. These are rare edge cases.

### Files to Create
- `Content/Progression/AccessoryEffectAnalyzer.cs` — analysis + cache
- `Content/Progression/AccessoryEffects.cs` — data record (or nested in analyzer)

### Files to Modify
- `Content/Progression/GearRecommender.cs` — use AccessoryEffects for scoring
- `Common/Systems/RecipeIndexSystem.cs` or `ItemSourceSystem.cs` — call Initialize during PostSetupContent

### Impact
- ~85% of accessories scored by actual effects, not tooltip guessing
- Modded accessories scored correctly with no per-mod integration
- Improvement text shows actual effects: "Knockback immunity, Dash" instead of "Score: 42"
- Better comparative scoring: can detect duplicate effects (player already has knockback immunity → shield value drops)
- Enables smart filtering: don't recommend a second source of the same effect

---

## Improvement 4: Duplicate Effect Detection

### Problem
The recommender might suggest Obsidian Shield when the player already has Ankh Shield — both give knockback immunity. Currently there's a `HasSimilarAccessory` check but it's based on item similarity, not effect overlap.

### What We Learned
With the differential approach, we can compare the `AccessoryEffects` of equipped accessories against candidates. If a candidate provides `noKnockback = true` but the player already has an equipped accessory that provides it, the benefit is zero for that effect.

### Approach
1. During recommendation, compute the player's current "effect set" from all equipped accessories
2. For each candidate, compute its "marginal value" — only effects the player doesn't already have
3. Score based on marginal value, not total value

```csharp
// Compute combined effects of all equipped accessories
var equippedEffects = MergeEffects(equippedAccessories);

// For each candidate:
var candidateEffects = AccessoryEffectAnalyzer.Get(candidateType);
float marginalScore = ComputeMarginalScore(candidateEffects, equippedEffects);
```

**Marginal scoring example:**
- Player has Cobalt Shield (knockback immunity)
- Candidate: Obsidian Shield (knockback immunity + fire walk)
- Marginal value: fire walk only (knockback immunity already covered)
- vs. Candidate: Worm Scarf (17% damage reduction)
- Marginal value: full 17% DR (new effect)
- Result: Worm Scarf ranked higher than Obsidian Shield

### Depends On
Improvement 3 (differential approach) must be implemented first.

### Files to Modify
- `Content/Progression/GearRecommender.cs` — marginal scoring logic
- `Content/Progression/AccessoryEffectAnalyzer.cs` — `MergeEffects` and `ComputeMarginalScore` methods

### Impact
- Dramatically better accessory recommendations for mid/late game
- No more "get Cobalt Shield" when you already have Ankh Shield
- Understands upgrade paths: Shield of Cthulhu > Cobalt Shield because it adds Dash

---

## Implementation Order

1. **#2 Direct Numeric Fields** — 15 minutes, zero risk, immediate value
2. **#1 Slot-Based Classification** — 1 hour, categorizes 70% of accessories, improves display
3. **#3 Differential Analysis** — the big build. Core analyzer + cache + integration with recommender
4. **#4 Duplicate Effect Detection** — builds on #3, adds marginal scoring

#3 is the most exciting because it solves the fundamental problem: modded accessories are a black box with tooltip parsing, but the differential approach reads their actual effects regardless of how their tooltips are formatted.
