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
	/// <summary>
	/// Alternative complete subtrees for this item, sorted by cost ascending.
	/// Index 0 is the currently selected alternative. Empty if only one recipe exists.
	/// Set by the resolver when multiple viable recipes are found.
	/// </summary>
	public List<CraftNode> Alternatives { get; init; } = [];

	/// <summary>Index of the currently active alternative (0 = best/default).</summary>
	public int SelectedAlternative { get; set; }

	public bool HasAlternatives => Alternatives.Count > 1;
	public int AlternativeCount => Alternatives.Count;

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
