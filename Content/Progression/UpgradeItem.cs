using System.Collections.Generic;
using ProgressionCompass.Content.Graph;

namespace ProgressionCompass.Content.Progression;

/// <summary>
/// Unified data model that merges GearRecommendation and CombinationResult
/// into a single type for consistent display across dashboard and upgrade window.
/// </summary>
public class UpgradeItem
{
    // ── Identity ────────────────────────────────────────────────
    public int ItemType { get; init; }
    public GearCategory Category { get; init; }

    // ── Improvement info (from GearRecommender) ─────────────────
    public string ImprovementText { get; init; }
    public float Score { get; init; }

    // ── Acquisition (from GearRecommender) ──────────────────────
    public AcquisitionMethod Acquisition { get; init; }
    public string SourceHint { get; init; }
    public List<ItemSource> AllSources { get; init; }

    // ── Tree-based completion (from CombinationScanner/Resolver) ─
    public int LeafMaterialsOwned { get; init; }
    public int LeafMaterialsTotal { get; init; }
    public float CompletionPercent => LeafMaterialsTotal > 0
        ? (float)LeafMaterialsOwned / LeafMaterialsTotal * 100f
        : 0f;
    public List<(int itemType, int quantity)> MissingItems { get; init; } = [];

    // ── Milestones (from CombinationScanner) ────────────────────
    public List<CombinationMilestone> ReadyMilestones { get; init; } = [];

    // ── Recipe info ───────────────────────────────────────────────
    public bool HasRecipe { get; init; }

    // ── Proximity (computed from tree completion) ────────────────
    public Proximity Proximity { get; init; }

    // ── Flags ───────────────────────────────────────────────────
    public bool IsTracked { get; set; }
}
