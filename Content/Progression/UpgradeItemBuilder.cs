using System;
using System.Collections.Generic;

namespace ProgressionCompass.Content.Progression;

/// <summary>
/// Merges GearRecommendation and CombinationResult lists into a unified
/// list of UpgradeItems. Tree-based completion drives proximity classification.
/// </summary>
public static class UpgradeItemBuilder
{
    public static List<UpgradeItem> Build(
        List<GearRecommendation> recommendations,
        List<CombinationResult> combinations,
        Func<int, bool> isTracked = null)
    {
        var result = new List<UpgradeItem>();
        var seen = new HashSet<int>();

        // Index combinations by item type for fast lookup
        var comboByType = new Dictionary<int, CombinationResult>();
        if (combinations != null)
        {
            foreach (var combo in combinations)
                comboByType.TryAdd(combo.ItemType, combo);
        }

        // 1. Process all GearRecommendations, merging with combo data when available
        if (recommendations != null)
        {
            foreach (var rec in recommendations)
            {
                seen.Add(rec.ItemType);
                comboByType.TryGetValue(rec.ItemType, out var combo);
                result.Add(MergeRecWithCombo(rec, combo, isTracked));
            }
        }

        // 2. Add combo-only items (not covered by GearRecommender)
        if (combinations != null)
        {
            foreach (var combo in combinations)
            {
                if (seen.Contains(combo.ItemType))
                    continue;
                result.Add(FromComboOnly(combo, isTracked));
            }
        }

        return result;
    }

    /// <summary>
    /// Merges a GearRecommendation with an optional matching CombinationResult.
    /// If combo data exists, tree-based completion drives proximity.
    /// Otherwise, falls back to GearRecommender's direct-ingredient proximity.
    /// </summary>
    private static UpgradeItem MergeRecWithCombo(
        GearRecommendation rec,
        CombinationResult combo,
        Func<int, bool> isTracked)
    {
        int leafOwned, leafTotal;
        List<(int, int)> missing;
        List<CombinationMilestone> milestones;
        Proximity prox;

        if (combo != null)
        {
            // Tree-based data from resolver — always preferred
            leafOwned = combo.LeafMaterialsOwned;
            leafTotal = combo.LeafMaterialsTotal;
            missing = combo.MissingItems ?? [];
            milestones = combo.ReadyMilestones ?? [];
            prox = ProximityFromCompletion(combo.CompletionPercent);
        }
        else
        {
            // No resolver data (item couldn't be resolved) — minimal fallback
            leafOwned = 0;
            leafTotal = 0;
            missing = [];
            milestones = [];
            prox = rec.HasRecipe ? Proximity.NeedsWork : Proximity.FarAway;
        }

        return new UpgradeItem
        {
            ItemType = rec.ItemType,
            Category = rec.Category,
            ImprovementText = rec.ImprovementText,
            Score = rec.Score,
            Acquisition = rec.Acquisition,
            SourceHint = rec.SourceHint,
            AllSources = rec.AllSources,
            LeafMaterialsOwned = leafOwned,
            LeafMaterialsTotal = leafTotal,
            MissingItems = missing,
            ReadyMilestones = milestones,
            HasRecipe = rec.HasRecipe,
            Proximity = prox,
            IsTracked = isTracked?.Invoke(rec.ItemType) ?? false
        };
    }

    /// <summary>
    /// Creates an UpgradeItem from a CombinationResult with no GearRecommendation match.
    /// These are combination items (accessories, weapons) the player can build toward.
    /// </summary>
    private static UpgradeItem FromComboOnly(CombinationResult combo, Func<int, bool> isTracked)
    {
        // Determine category from combo flags
        var category = combo.IsWeapon ? GearCategory.Weapon
            : combo.IsArmor ? GearCategory.Armor
            : GearCategory.Accessory;

        // Generate a simple type tag as improvement text
        string typeTag = combo.IsWeapon ? "[Weapon]"
            : combo.IsArmor ? "[Armor]"
            : "[Accessory]";

        return new UpgradeItem
        {
            ItemType = combo.ItemType,
            Category = category,
            ImprovementText = typeTag,
            Score = combo.CompletionPercent, // Sort by how close they are
            Acquisition = AcquisitionMethod.Craftable,
            LeafMaterialsOwned = combo.LeafMaterialsOwned,
            LeafMaterialsTotal = combo.LeafMaterialsTotal,
            MissingItems = combo.MissingItems ?? [],
            ReadyMilestones = combo.ReadyMilestones ?? [],
            HasRecipe = true,
            Proximity = ProximityFromCompletion(combo.CompletionPercent),
            IsTracked = isTracked?.Invoke(combo.ItemType) ?? false
        };
    }

    /// <summary>
    /// Derives Proximity from tree-based completion percentage.
    /// </summary>
    public static Proximity ProximityFromCompletion(float completionPercent)
    {
        if (completionPercent >= ScoringConstants.CraftableNowThreshold) return Proximity.CraftableNow;
        if (completionPercent >= ScoringConstants.AlmostReadyThreshold) return Proximity.AlmostReady;
        if (completionPercent >= ScoringConstants.NeedsWorkThreshold) return Proximity.NeedsWork;
        return Proximity.FarAway;
    }

}
