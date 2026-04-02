using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using ProgressionCompass.Common.Systems;

namespace ProgressionCompass.Content.Graph;

/// <summary>
/// Core algorithm: given a target item and player inventory, builds the optimal
/// crafting dependency tree via DFS with backtracking.
/// </summary>
public class DependencyResolver
{
	private const int MaxDepth = 25;
	private const int MaxRecipesPerItem = 15;

	private readonly RecipeIndexSystem _recipeIndex;
	private readonly ItemSourceSystem _sourceSystem;
	private readonly Dictionary<int, int> _recipePreferences;

	public DependencyResolver(RecipeIndexSystem recipeIndex, ItemSourceSystem sourceSystem,
		Dictionary<int, int> recipePreferences = null)
	{
		_recipeIndex = recipeIndex;
		_sourceSystem = sourceSystem;
		_recipePreferences = recipePreferences ?? [];
	}

	/// <summary>
	/// Resolves the full crafting path for a target item and produces a complete
	/// ResolverResult with tree, flat material list, and craft order.
	/// </summary>
	public ResolverResult Resolve(int itemType, int quantity, Dictionary<int, InventoryEntry> inventory, bool includeChests)
	{
		if (itemType <= 0 || quantity <= 0)
			return new ResolverResult { Tree = new AcquireNode(itemType, quantity, []) };

		// Build a mutable count dictionary from the inventory snapshot
		var available = new Dictionary<int, int>();
		foreach (var (type, entry) in inventory)
		{
			int count = entry.AvailableCount(includeChests);
			if (count > 0)
				available[type] = count;
		}

		var visited = new HashSet<int>();
		GraphNode tree;
		try
		{
			tree = ResolveItem(itemType, quantity, available, visited, 0);
		}
		catch
		{
			// If the resolver crashes on any item, fall back to AcquireNode
			tree = MakeAcquireNode(itemType, quantity);
		}

		// Flatten the tree into materials and craft order
		var materials = ExtractMaterials(tree, inventory, includeChests);
		var craftOrder = ExtractCraftOrder(tree, inventory, includeChests);

		return new ResolverResult
		{
			Tree = tree,
			Materials = materials,
			CraftOrder = craftOrder
		};
	}

	/// <summary>
	/// Recursive DFS: resolve one item at a given quantity.
	/// Returns the best GraphNode subtree for obtaining this item.
	/// </summary>
	private GraphNode ResolveItem(
		int itemType,
		int quantity,
		Dictionary<int, int> available,
		HashSet<int> visited,
		int depth)
	{
		if (depth > MaxDepth)
			return MakeAcquireNode(itemType, quantity);

		// Step 1: Check what we already have
		int have = available.GetValueOrDefault(itemType, 0);
		if (have >= quantity)
		{
			available[itemType] = have - quantity;
			return new HaveNode(itemType, quantity, InventoryEntry.Personal(quantity));
		}

		// Use what we have, still need the remainder
		int remaining = quantity;
		int usedFromInventory = 0;
		if (have > 0)
		{
			remaining -= have;
			usedFromInventory = have;
			available[itemType] = 0;
		}

		// Step 2: Look up recipes
		if (!_recipeIndex.RecipesByResult.TryGetValue(itemType, out var recipes) || recipes.Count == 0)
		{
			// Not craftable — restore what we consumed and return AcquireNode
			if (usedFromInventory > 0)
				available[itemType] = usedFromInventory;
			return MakeAcquireNode(itemType, quantity);
		}

		// Step 2b: Check if this item is a raw material that happens to have
		// reverse-decoration recipes (wall→block, platform→block, etc.).
		// If it has non-crafting sources and ALL recipes are reversals, prefer gathering.
		var sources = _sourceSystem?.GetSources(itemType);
		if (sources != null && sources.Count > 0 && AreAllRecipesReversals(itemType, recipes))
		{
			if (usedFromInventory > 0)
				available[itemType] = usedFromInventory;
			return MakeAcquireNode(itemType, quantity);
		}

		// Step 3: Try each recipe, collect all viable candidates, pick best
		var candidates = new List<(CraftNode node, Dictionary<int, int> state)>();

		int recipesToTry = Math.Min(recipes.Count, MaxRecipesPerItem);
		for (int r = 0; r < recipesToTry; r++)
		{
			Recipe recipe = recipes[r];

			// Cycle detection: check if this recipe is already on our DFS path
			int recipeIndex = GetRecipeIndex(recipe);
			if (visited.Contains(recipeIndex))
				continue;

			// Calculate how many times we must execute this recipe
			int outputPerCraft = recipe.createItem.stack;
			int craftCount = (remaining + outputPerCraft - 1) / outputPerCraft;

			// Snapshot available for backtracking
			var snapshot = new Dictionary<int, int>(available);
			visited.Add(recipeIndex);

			var ingredients = new List<GraphNode>();
			bool failed = false;

			foreach (Item ingredient in recipe.requiredItem)
			{
				if (ingredient.type <= ItemID.None)
					continue;

				int ingredientQty = ingredient.stack * craftCount;
				GraphNode childNode;

				// Check if this ingredient has an accepted recipe group
				int groupId = FindAcceptedGroup(recipe, ingredient.type);
				if (groupId >= 0)
					childNode = ResolveGroup(groupId, ingredientQty, available, visited, depth + 1);
				else
					childNode = ResolveItem(ingredient.type, ingredientQty, available, visited, depth + 1);

				if (childNode == null)
				{
					failed = true;
					break;
				}

				ingredients.Add(childNode);
			}

			visited.Remove(recipeIndex);

			if (failed)
			{
				foreach (var kv in snapshot)
					available[kv.Key] = kv.Value;
				continue;
			}

			var candidateNode = new CraftNode(itemType, quantity, recipe, craftCount, ingredients);
			candidates.Add((candidateNode, new Dictionary<int, int>(available)));

			// Rollback for next recipe attempt
			foreach (var kv in snapshot)
				available[kv.Key] = kv.Value;
		}

		if (candidates.Count > 0)
		{
			// Sort by acquisition cost ascending
			candidates.Sort((a, b) => a.node.AcquisitionCost.CompareTo(b.node.AcquisitionCost));

			// Apply user's recipe preference: find candidate matching the preferred recipe index
			int preferredIdx = 0;
			if (_recipePreferences.TryGetValue(itemType, out int prefRecipeIdx) && prefRecipeIdx >= 0)
			{
				for (int c = 0; c < candidates.Count; c++)
				{
					if (GetRecipeIndex(candidates[c].node.Recipe) == prefRecipeIdx)
					{
						preferredIdx = c;
						break;
					}
				}
			}

			var best = candidates[preferredIdx];

			// Attach all candidates as alternatives if more than one
			if (candidates.Count > 1)
			{
				var alts = new List<CraftNode>(candidates.Count);
				foreach (var (node, _) in candidates)
					alts.Add(node);
				best = (best.node with { Alternatives = alts }, best.state);
			}

			// Apply the best path's inventory state
			foreach (var kv in best.state)
				available[kv.Key] = kv.Value;
			return best.node;
		}

		// No recipe worked — treat as raw material
		if (usedFromInventory > 0)
			available[itemType] = usedFromInventory;
		return MakeAcquireNode(itemType, quantity);
	}

	/// <summary>
	/// Resolves a recipe group by trying each valid member and picking the cheapest.
	/// </summary>
	private GraphNode ResolveGroup(
		int groupId,
		int quantity,
		Dictionary<int, int> available,
		HashSet<int> visited,
		int depth)
	{
		if (!RecipeGroup.recipeGroups.TryGetValue(groupId, out var group))
			return MakeAcquireNode(0, quantity);

		GraphNode bestNode = null;
		int bestCost = int.MaxValue;
		Dictionary<int, int> bestAvailable = null;

		foreach (int memberType in group.ValidItems)
		{
			var snapshot = new Dictionary<int, int>(available);
			var node = ResolveItem(memberType, quantity, available, visited, depth);
			int cost = node.AcquisitionCost;

			if (cost < bestCost)
			{
				bestCost = cost;
				bestNode = node;
				bestAvailable = new Dictionary<int, int>(available);
			}

			// Rollback
			foreach (var kv in snapshot)
				available[kv.Key] = kv.Value;
		}

		if (bestNode != null)
		{
			foreach (var kv in bestAvailable)
				available[kv.Key] = kv.Value;
			return new GroupNode(groupId, quantity, bestNode);
		}

		return MakeAcquireNode(0, quantity);
	}

	private AcquireNode MakeAcquireNode(int itemType, int quantity)
	{
		var sources = _sourceSystem?.GetSources(itemType) ?? [];
		return new AcquireNode(itemType, quantity, sources);
	}

	// --- Tree flattening ---

	/// <summary>
	/// Extracts a flat, deduplicated material list from the tree.
	/// Aggregates all leaf nodes (HaveNode, AcquireNode) by item type.
	/// </summary>
	private List<MaterialEntry> ExtractMaterials(
		GraphNode root,
		Dictionary<int, InventoryEntry> inventory,
		bool includeChests)
	{
		var needed = new Dictionary<int, int>();
		var craftable = new HashSet<int>();
		CollectLeafMaterials(root, needed, craftable);

		var materials = new List<MaterialEntry>();
		foreach (var (itemType, qty) in needed)
		{
			var owned = inventory.GetValueOrDefault(itemType, InventoryEntry.Empty);
			var sources = _sourceSystem?.GetSources(itemType) ?? [];
			materials.Add(new MaterialEntry(itemType, qty, owned, sources, craftable.Contains(itemType)));
		}

		// Sort: missing items first, then by quantity needed descending
		materials.Sort((a, b) =>
		{
			bool aComplete = a.IsComplete(includeChests);
			bool bComplete = b.IsComplete(includeChests);
			if (aComplete != bComplete)
				return aComplete ? 1 : -1;
			return b.QuantityNeeded.CompareTo(a.QuantityNeeded);
		});

		return materials;
	}

	private static void CollectLeafMaterials(
		GraphNode node,
		Dictionary<int, int> needed,
		HashSet<int> craftable)
	{
		switch (node)
		{
			case HaveNode have:
				needed[have.ItemType] = needed.GetValueOrDefault(have.ItemType) + have.Quantity;
				break;
			case AcquireNode acquire:
				needed[acquire.ItemType] = needed.GetValueOrDefault(acquire.ItemType) + acquire.Quantity;
				break;
			case CraftNode craft:
				craftable.Add(craft.ItemType);
				foreach (var child in craft.Ingredients)
					CollectLeafMaterials(child, needed, craftable);
				break;
			case GroupNode group:
				CollectLeafMaterials(group.ChosenItem, needed, craftable);
				break;
		}
	}

	/// <summary>
	/// Post-order traversal to produce a topologically-sorted craft order.
	/// Leaf materials first, final item last.
	/// Uses the original inventory to correctly mark owned items.
	/// </summary>
	private List<CraftStep> ExtractCraftOrder(GraphNode root, Dictionary<int, InventoryEntry> inventory, bool includeChests)
	{
		var steps = new List<CraftStep>();
		var craftedItems = new Dictionary<int, int>(); // itemType → step number
		CollectCraftSteps(root, steps, craftedItems, inventory, includeChests);
		return steps;
	}

	private static void CollectCraftSteps(
		GraphNode node,
		List<CraftStep> steps,
		Dictionary<int, int> craftedItems,
		Dictionary<int, InventoryEntry> inventory,
		bool includeChests)
	{
		if (node is not CraftNode craft)
		{
			if (node is GroupNode group)
				CollectCraftSteps(group.ChosenItem, steps, craftedItems, inventory, includeChests);
			return;
		}

		// Recurse children first (post-order)
		foreach (var child in craft.Ingredients)
			CollectCraftSteps(child, steps, craftedItems, inventory, includeChests);

		int stepNumber = steps.Count + 1;

		var inputs = new List<CraftStepInput>();
		foreach (var child in craft.Ingredients)
		{
			int childType = child.ItemType;
			int childQty = child.Quantity;

			CraftStepInputSource source;
			int? fromStep = null;

			string acquireHint = null;
			List<ItemSource> acquireSources = null;

			// Determine source: check tree node type first, then fall back to inventory
			if (child is HaveNode)
			{
				source = CraftStepInputSource.Owned;
			}
			else if (craftedItems.TryGetValue(childType, out int prevStep))
			{
				source = CraftStepInputSource.FromPreviousStep;
				fromStep = prevStep;
			}
			else if (child is CraftNode)
			{
				source = CraftStepInputSource.FromPreviousStep;
			}
			else if (child is GroupNode groupChild)
			{
				if (groupChild.ChosenItem is HaveNode)
					source = CraftStepInputSource.Owned;
				else if (craftedItems.ContainsKey(groupChild.ChosenItem.ItemType) || groupChild.ChosenItem is CraftNode)
					source = CraftStepInputSource.FromPreviousStep;
				else
				{
					source = CraftStepInputSource.NeedToAcquire;
					if (groupChild.ChosenItem is AcquireNode gAcq && gAcq.Sources.Count > 0)
					{
						acquireSources = gAcq.Sources;
						acquireHint = ItemSource.PickBest(gAcq.Sources)?.Description;
					}
				}
			}
			else
			{
				source = CraftStepInputSource.NeedToAcquire;
				if (child is AcquireNode acq && acq.Sources.Count > 0)
				{
					acquireSources = acq.Sources;
					acquireHint = ItemSource.PickBest(acq.Sources)?.Description;
				}
			}

			// Override: if the tree says "need to acquire" but we actually OWN enough
			// in inventory, mark as owned. This handles cases where the resolver's
			// simulated inventory was consumed by an earlier branch.
			if (source == CraftStepInputSource.NeedToAcquire && childType > 0
				&& inventory.TryGetValue(childType, out var ownedEntry)
				&& ownedEntry.AvailableCount(includeChests) >= childQty)
			{
				source = CraftStepInputSource.Owned;
				acquireHint = null;
				acquireSources = null;
			}

			inputs.Add(new CraftStepInput(childType, childQty, source, fromStep, acquireHint, acquireSources));
		}

		var requiredTiles = craft.Recipe.requiredTile
			.Where(t => t >= 0)
			.ToArray();

		string conditions = craft.HasConditions ? craft.ConditionDescription : null;

		steps.Add(new CraftStep(
			stepNumber, craft.ItemType, craft.Quantity,
			craft.CraftCount, requiredTiles, inputs, conditions));

		craftedItems[craft.ItemType] = stepNumber;
	}

	// --- Helpers ---

	/// <summary>
	/// Checks if all recipes for an item are "reverse decoration" recipes —
	/// converting walls, platforms, or furniture back into the base material.
	/// If so, the item is better obtained by mining/gathering than crafting.
	/// </summary>
	private static bool AreAllRecipesReversals(int resultItemType, List<Recipe> recipes)
	{
		foreach (var recipe in recipes)
		{
			bool isReversal = false;
			foreach (Item ingredient in recipe.requiredItem)
			{
				if (ingredient.type <= ItemID.None)
					continue;

				// Check if the ingredient is a wall, platform, or furniture item
				// by looking at its name suffix — this catches all naming patterns:
				// "Obsidian Wall", "Smouldering Stone Wall", "Bone Block Wall",
				// "Bone Platform", "Hellstone Brick Wall", etc.
				string ingredientName = Lang.GetItemNameValue(ingredient.type);
				if (string.IsNullOrEmpty(ingredientName))
					break;

				string lower = ingredientName.ToLowerInvariant();
				if (lower.EndsWith(" wall") || lower.EndsWith(" platform")
					|| lower.EndsWith(" fence") || lower.EndsWith(" beam")
					|| lower.EndsWith(" brick") || lower.EndsWith(" block wall"))
				{
					isReversal = true;
				}

				// Also check: if the ingredient itself has a createWall property,
				// it's almost certainly a wall item being reversed
				var ingItem = ContentSamples.ItemsByType.GetValueOrDefault(ingredient.type);
				if (ingItem != null && ingItem.createWall > 0)
					isReversal = true;

				break; // Only check the first real ingredient
			}

			// If ANY recipe is NOT a reversal, there's a legitimate craft path
			if (!isReversal)
				return false;
		}
		return true;
	}

	/// <summary>Finds the recipe index in Main.recipe[] for cycle detection.</summary>
	private static int GetRecipeIndex(Recipe recipe)
	{
		// Recipe doesn't expose its index directly, but we can search by reference
		for (int i = 0; i < Recipe.numRecipes; i++)
		{
			if (ReferenceEquals(Main.recipe[i], recipe))
				return i;
		}
		return -1;
	}

	/// <summary>
	/// Checks if a recipe accepts a recipe group for the given ingredient type.
	/// Returns the group ID, or -1 if no group applies.
	/// </summary>
	private static int FindAcceptedGroup(Recipe recipe, int ingredientType)
	{
		foreach (int groupId in recipe.acceptedGroups)
		{
			if (RecipeGroup.recipeGroups.TryGetValue(groupId, out var group)
				&& group.ValidItems.Contains(ingredientType))
			{
				return groupId;
			}
		}
		return -1;
	}
}
