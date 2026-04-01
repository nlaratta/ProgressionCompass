using System.Collections.Generic;

namespace CraftPathOptimizer.Content.Progression;

/// <summary>
/// Information about a boss the player can fight, including
/// how to summon it, what it unlocks, and defeat status.
/// </summary>
public class BossInfo
{
	public int NpcType { get; init; }
	public string Name { get; init; }
	public string SummonHint { get; init; }
	public string Location { get; init; }
	public string Unlocks { get; init; }
	public bool IsDefeated { get; init; }
	public Tier RequiredTier { get; init; }
	public bool IsOptional { get; init; }
	public List<int> KeyDropItemTypes { get; init; } = [];
	/// <summary>Items used to summon this boss (from Boss Checklist spawnItems).</summary>
	public List<int> SummonItemIds { get; init; } = [];
	/// <summary>Treasure bag item type (Expert+). -1 if none.</summary>
	public int TreasureBagItemType { get; init; } = -1;
	/// <summary>Progression value from Boss Checklist (if available). Lower = earlier.</summary>
	public float ProgressionOrder { get; init; }
}
