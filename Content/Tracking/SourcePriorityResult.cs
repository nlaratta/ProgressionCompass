using System.Collections.Generic;
using ProgressionCompass.Content.Graph;
using ProgressionCompass.Content.Progression;

namespace ProgressionCompass.Content.Tracking;

/// <summary>
/// Categorizes locations into distinct types for grouping and display.
/// </summary>
public enum LocationType
{
	Biome,
	Boss,
	Shop,
	Event,
	Exploration
}

/// <summary>
/// Links a material drop to a specific upgrade it contributes to.
/// </summary>
public class UpgradeContribution
{
	public int UpgradeItemType { get; init; }
	public Proximity UpgradeProximity { get; init; }
	public float UpgradeCompletionPercent { get; init; }
	public int MissingCountForUpgrade { get; init; }
	public bool IsLastPiece { get; init; }
}

/// <summary>
/// A single material drop available at a location, linking back to
/// which upgrade(s) it contributes to.
/// </summary>
public class LocationDrop
{
	public int ItemType { get; init; }
	public int QuantityNeeded { get; init; }
	public ItemSource Source { get; init; }
	public List<UpgradeContribution> FeedsInto { get; init; } = [];
	public float DropScore { get; init; }
}

/// <summary>
/// A ranked location with its aggregate score and all drops found there.
/// </summary>
public class LocationRanking
{
	public string LocationName { get; init; }
	public LocationType Type { get; init; }
	public float TotalScore { get; init; }
	public int StarRating { get; set; }
	public int UpgradeCount { get; init; }
	public List<LocationDrop> Drops { get; init; } = [];

	/// <summary>
	/// Contextual hint for display — boss summon hint, event trigger hint, or shop conditions.
	/// </summary>
	public string ContextHint { get; init; }

	/// <summary>
	/// Distinct NPC types present at this location, for icon rendering.
	/// </summary>
	public List<int> SourceEntityIds { get; init; } = [];
}

/// <summary>
/// Complete result of the Source Priority Analyzer.
/// </summary>
public class SourcePriorityResult
{
	public List<LocationRanking> Rankings { get; init; } = [];
	public int TotalUpgradesAnalyzed { get; init; }
}
