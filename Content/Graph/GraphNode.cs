using System.Collections.Generic;
using Terraria;

namespace ProgressionCompass.Content.Graph;

/// <summary>
/// A node in the crafting dependency tree. Each variant represents a different
/// resolution state for an item the player needs.
/// </summary>
public abstract record GraphNode(int ItemType, int Quantity)
{
	/// <summary>Recursively counts how many raw items must be acquired (not already owned).</summary>
	public abstract int AcquisitionCost { get; }

	/// <summary>
	/// All viable resolution paths for this item, including the chosen one.
	/// Index 0 is the default best. May contain CraftNodes, AcquireNodes, ShimmerNodes.
	/// Empty if only one path exists.
	/// </summary>
	public List<GraphNode> Alternatives { get; init; } = [];

	/// <summary>Index of the currently active alternative (0 = best/default).</summary>
	public int SelectedAlternative { get; set; }

	public bool HasAlternatives => Alternatives.Count > 1;
	public int AlternativeCount => Alternatives.Count;
}

/// <summary>Player already owns enough of this item — no action needed.</summary>
public record HaveNode(int ItemType, int Quantity, InventoryEntry Source)
	: GraphNode(ItemType, Quantity)
{
	public override int AcquisitionCost => 0;
}

/// <summary>Item must be obtained from a non-crafting source (drop, shop, mine, etc.).</summary>
public record AcquireNode(int ItemType, int Quantity, List<ItemSource> Sources)
	: GraphNode(ItemType, Quantity)
{
	public override int AcquisitionCost => Quantity;
}

/// <summary>Item is produced by executing a recipe. Children are the ingredients.</summary>
public record CraftNode(
	int ItemType,
	int Quantity,
	Recipe Recipe,
	int CraftCount,
	List<GraphNode> Ingredients) : GraphNode(ItemType, Quantity)
{
	public override int AcquisitionCost
	{
		get
		{
			int cost = 0;
			foreach (var child in Ingredients)
				cost += child.AcquisitionCost;
			return cost;
		}
	}

	public bool HasConditions => Recipe.Conditions.Count > 0;

	public string ConditionDescription => string.Join(", ",
		Recipe.Conditions.ConvertAll(c => c.Description.Value));
}

/// <summary>
/// Represents a recipe group choice (e.g., "Any Iron Bar").
/// The resolver picks the best member; ChosenItem is the resolved subtree.
/// </summary>
public record GroupNode(int GroupId, int Quantity, GraphNode ChosenItem)
	: GraphNode(ChosenItem.ItemType, Quantity)
{
	public override int AcquisitionCost => ChosenItem.AcquisitionCost;
}

/// <summary>
/// Item is obtained by shimmering another item. SourceItem is the input to shimmer.
/// Used for cross-evil transmutations (e.g., Tissue Sample → Shadow Scale in Crimson worlds).
/// </summary>
public record ShimmerNode(int ItemType, int Quantity, int SourceItemType, GraphNode SourceItem)
	: GraphNode(ItemType, Quantity)
{
	/// <summary>Shimmer cost = source item cost + small penalty for shimmer pool access.</summary>
	public override int AcquisitionCost => SourceItem.AcquisitionCost + Progression.ScoringConstants.ShimmerAccessCost;
}
