using System.Collections.Generic;
using CraftPathOptimizer.Content.Graph;

namespace CraftPathOptimizer.Content.Tracking;

/// <summary>
/// A single item the player is tracking toward crafting.
/// Stores the target, quantity, and the last resolved result for progress display.
/// </summary>
public class PinnedGoal
{
	public int ItemType { get; }
	public int Quantity { get; set; }

	/// <summary>Last resolved result for this goal. Null until first resolve.</summary>
	public ResolverResult Result { get; set; }

	/// <summary>Cached progress: how many material types are complete.</summary>
	public int OwnedMaterialCount { get; set; }
	public int TotalMaterialCount { get; set; }

	public float ProgressPercent => TotalMaterialCount > 0
		? (float)OwnedMaterialCount / TotalMaterialCount * 100f
		: 0f;

	public PinnedGoal(int itemType, int quantity = 1)
	{
		ItemType = itemType;
		Quantity = quantity;
	}

	/// <summary>Updates cached progress from the resolved result.</summary>
	public void UpdateProgress(bool includeChests)
	{
		if (Result?.Materials == null)
		{
			OwnedMaterialCount = 0;
			TotalMaterialCount = 0;
			return;
		}

		TotalMaterialCount = Result.Materials.Count;
		int owned = 0;
		foreach (var mat in Result.Materials)
		{
			if (mat.IsComplete(includeChests))
				owned++;
		}
		OwnedMaterialCount = owned;
	}
}
