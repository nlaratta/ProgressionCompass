using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using ProgressionCompass.Common.Systems;
using ProgressionCompass.Content.Progression;

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
	private readonly WorldContext _worldContext;
	private readonly Progression.Tier _currentTier;

	public DependencyResolver(RecipeIndexSystem recipeIndex, ItemSourceSystem sourceSystem,
		Dictionary<int, int> recipePreferences = null, WorldContext worldContext = default,
		Progression.Tier currentTier = Progression.Tier.PreBoss)
	{
		_recipeIndex = recipeIndex;
		_sourceSystem = sourceSystem;
		_recipePreferences = recipePreferences ?? [];
		_worldContext = worldContext;
		_currentTier = currentTier;
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

		// Step 2: Compute the best non-recipe acquisition cost.
		// Sources (gather, buy, drop) compete with recipes — the resolver picks
		// whichever is cheapest rather than always preferring recipes.
		var sources = _sourceSystem?.GetSources(itemType, _worldContext);
		int acquireCost = EstimateAcquireCost(itemType, sources);

		// Step 3: Look up recipes and try each one
		_recipeIndex.RecipesByResult.TryGetValue(itemType, out var recipes);

		// If no recipes exist, go straight to acquire/shimmer
		if (recipes == null || recipes.Count == 0)
		{
			// Try shimmer for cross-evil items (e.g., Shadow Scale in Crimson world)
			if (IsWrongEvilItem(itemType))
			{
				var shimmerFallback = TryShimmerFallback(itemType, quantity, available, visited, depth);
				if (shimmerFallback != null)
					return shimmerFallback;
			}

			if (usedFromInventory > 0)
				available[itemType] = usedFromInventory;
			return MakeAcquireNode(itemType, quantity);
		}

		// Skip recipes entirely for reversal-only items with real sources
		if (sources != null && sources.Count > 0 && AreAllRecipesReversals(itemType, recipes))
		{
			if (usedFromInventory > 0)
				available[itemType] = usedFromInventory;
			return MakeAcquireNode(itemType, quantity);
		}

		// Try each recipe, collect all viable candidates
		var candidates = new List<(CraftNode node, Dictionary<int, int> state)>();

		int recipesToTry = Math.Min(recipes.Count, MaxRecipesPerItem);
		for (int r = 0; r < recipesToTry; r++)
		{
			Recipe recipe = recipes[r];

			// Cycle detection: check if this recipe is already on our DFS path
			int recipeIndex = GetRecipeIndex(recipe);
			if (visited.Contains(recipeIndex))
				continue;

			// World evil filter: skip recipes affiliated with the wrong world evil
			EvilAffiliation recipeEvil = EvilAffiliationMap.GetRecipeAffiliation(recipe);
			if (!_worldContext.IsAvailable(recipeEvil))
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
			// Sort by effective cost: acquisition cost + penalty for unmet recipe conditions.
			candidates.Sort((a, b) =>
			{
				int costA = a.node.AcquisitionCost + GetConditionPenalty(a.node.Recipe);
				int costB = b.node.AcquisitionCost + GetConditionPenalty(b.node.Recipe);
				int cmp = costA.CompareTo(costB);
				if (cmp != 0) return cmp;
				// Tiebreak: prefer world-evil-matching recipe
				var evilA = EvilAffiliationMap.GetRecipeAffiliation(a.node.Recipe);
				var evilB = EvilAffiliationMap.GetRecipeAffiliation(b.node.Recipe);
				bool matchA = _worldContext.IsAvailable(evilA);
				bool matchB = _worldContext.IsAvailable(evilB);
				if (matchA != matchB) return matchA ? -1 : 1;
				return 0;
			});

			// Build unified alternatives list: all craft candidates + acquire option
			var allAlternatives = new List<GraphNode>();
			foreach (var (node, _) in candidates)
				allAlternatives.Add(node);

			// Add an AcquireNode alternative if non-recipe sources exist
			AcquireNode acquireAlt = null;
			if (sources != null && sources.Count > 0)
			{
				acquireAlt = new AcquireNode(itemType, quantity, sources);
				allAlternatives.Add(acquireAlt);
			}

			// Sort: craft candidates by effective cost, acquire at its estimated position
			// Find where acquire fits cost-wise and determine default best
			int bestRecipeCost = candidates[0].node.AcquisitionCost
				+ GetConditionPenalty(candidates[0].node.Recipe);

			int defaultIdx = 0;
			if (acquireCost < bestRecipeCost && acquireAlt != null)
				defaultIdx = allAlternatives.Count - 1; // Acquire is last in the list

			// Apply user's preference (stored as alternative index)
			int chosenIdx = defaultIdx;
			if (_recipePreferences.TryGetValue(itemType, out int prefIdx) && prefIdx >= 0 && prefIdx < allAlternatives.Count)
				chosenIdx = prefIdx;

			var chosenNode = allAlternatives[chosenIdx];

			// Attach alternatives if more than one option
			if (allAlternatives.Count > 1)
				chosenNode = chosenNode with { Alternatives = allAlternatives };
			chosenNode.SelectedAlternative = chosenIdx;

			// Apply the chosen path's inventory state (only for craft candidates)
			if (chosenIdx < candidates.Count)
			{
				foreach (var kv in candidates[chosenIdx].state)
					available[kv.Key] = kv.Value;
			}
			else if (usedFromInventory > 0)
			{
				// Acquire chosen — restore inventory
				available[itemType] = usedFromInventory;
			}

			return chosenNode;
		}

		// No recipe worked — try shimmer for cross-evil items
		if (IsWrongEvilItem(itemType))
		{
			var shimmerNode = TryShimmerFallback(itemType, quantity, available, visited, depth);
			if (shimmerNode != null)
			{
				if (usedFromInventory > 0)
					available[itemType] = 0;
				return shimmerNode;
			}
		}

		// Nothing worked — treat as raw material
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
		bool bestIsPreferred = false;
		Dictionary<int, int> bestAvailable = null;

		foreach (int memberType in group.ValidItems)
		{
			var snapshot = new Dictionary<int, int>(available);
			var node = ResolveItem(memberType, quantity, available, visited, depth);
			int cost = node.AcquisitionCost;
			bool isPreferred = _worldContext.IsPreferredOreItem(memberType);

			// Pick this member if: cheaper, OR same cost but world-preferred ore
			bool isBetter = cost < bestCost
				|| (cost == bestCost && isPreferred && !bestIsPreferred);

			if (isBetter)
			{
				bestCost = cost;
				bestNode = node;
				bestIsPreferred = isPreferred;
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
		var sources = _sourceSystem?.GetSources(itemType, _worldContext) ?? [];
		return new AcquireNode(itemType, quantity, sources);
	}

	/// <summary>
	/// Attempts to resolve an item via shimmer transformation as a fallback
	/// when no recipe is viable. Enables cross-evil paths (e.g., in a Crimson world,
	/// Shadow Scale can be obtained by shimmering Tissue Sample).
	/// </summary>
	private ShimmerNode TryShimmerFallback(
		int itemType,
		int quantity,
		Dictionary<int, int> available,
		HashSet<int> visited,
		int depth)
	{
		// Only try shimmer at shallow depths to avoid exponential blowup
		// (4000+ decrafting sources can cause combinatorial explosion)
		if (depth > MaxDepth - 5)
			return null;

		// Only use direct ShimmerTransformToItem transforms, NOT decrafting.
		// Decrafting sources are too numerous and create deep recursive paths.
		var allSources = _sourceSystem?.GetSources(itemType) ?? [];
		foreach (var source in allSources)
		{
			if (source.Type != ItemSourceType.Shimmer || source.SourceEntityId <= 0)
				continue;

			// Skip decrafting sources in resolver — they're too expensive to explore
			// recursively. They still show as sources in the material panel.
			if (source.Description.StartsWith("Decraft:"))
				continue;

			int sourceItemType = source.SourceEntityId;

			// Prevent shimmer cycles (A shimmers into B, B shimmers into A)
			if (visited.Contains(-sourceItemType))
				continue;

			// Check if the source item is obtainable in this world
			if (!_worldContext.IsAvailable(EvilAffiliationMap.Get(sourceItemType)))
				continue;

			// Try resolving the source item
			var snapshot = new Dictionary<int, int>(available);
			visited.Add(-sourceItemType);

			var sourceNode = ResolveItem(sourceItemType, quantity, available, visited, depth + 1);

			visited.Remove(-sourceItemType);

			// If the source resolved to a pure AcquireNode with no sources, it's a dead end
			if (sourceNode is AcquireNode acq && acq.Sources.Count == 0)
			{
				foreach (var kv in snapshot)
					available[kv.Key] = kv.Value;
				continue;
			}

			return new ShimmerNode(itemType, quantity, sourceItemType, sourceNode);
		}

		return null;
	}

	/// <summary>
	/// Estimates the effective cost of acquiring an item through non-recipe sources.
	/// Lower = easier to obtain. Returns int.MaxValue if no sources exist.
	/// This competes with recipe AcquisitionCost to decide craft vs acquire.
	/// </summary>
	private int EstimateAcquireCost(int itemType, List<ItemSource> sources)
	{
		if (sources == null || sources.Count == 0)
			return int.MaxValue;

		int bestCost = int.MaxValue;
		foreach (var source in sources)
		{
			int cost = source.Type switch
			{
				ItemSourceType.Gathering => Progression.ScoringConstants.AcquireCostGathering,
				ItemSourceType.NPCShop => _worldContext.IsNpcAvailable(source.SourceEntityId)
					? Progression.ScoringConstants.AcquireCostShopAvailable
					: Progression.ScoringConstants.AcquireCostShopUnavailable,
				ItemSourceType.NPCDrop => source.DropRate >= 0.1f
					? Progression.ScoringConstants.AcquireCostCommonDrop
					: source.DropRate >= 0.02f
						? Progression.ScoringConstants.AcquireCostUncommonDrop
						: Progression.ScoringConstants.AcquireCostRareDrop,
				ItemSourceType.BossDrop => Progression.ScoringConstants.AcquireCostBoss,
				ItemSourceType.Event => Progression.ScoringConstants.AcquireCostEvent,
				ItemSourceType.BagDrop => Progression.ScoringConstants.AcquireCostBoss,
				ItemSourceType.Fishing => Progression.ScoringConstants.AcquireCostFishing,
				ItemSourceType.ChestLoot => Progression.ScoringConstants.AcquireCostChest,
				ItemSourceType.Shimmer => int.MaxValue,
				_ => Progression.ScoringConstants.AcquireCostOther
			};

			if (cost < bestCost)
				bestCost = cost;
		}

		return bestCost;
	}

	/// <summary>
	/// Returns true if this item is affiliated with the opposite world evil.
	/// Shimmer fallback is ONLY used for these items (cross-evil transmutation).
	/// </summary>
	private bool IsWrongEvilItem(int itemType)
	{
		var affiliation = EvilAffiliationMap.Get(itemType);
		if (affiliation == EvilAffiliation.Neutral)
			return false;
		return !_worldContext.IsAvailable(affiliation);
	}



	/// <summary>
	/// Returns a cost penalty for recipes with conditions above the player's current tier.
	/// This deprioritizes recipes the player can't currently use without blocking them entirely.
	/// </summary>
	private int GetConditionPenalty(Recipe recipe)
	{
		if (recipe.Conditions.Count == 0)
			return 0;

		var recipeTier = Progression.ProgressionTier.EstimateRecipeTier(recipe);
		if (recipeTier <= _currentTier)
			return 0; // Conditions are at or below player's tier — likely met

		// Penalty scales with how far above the player's tier the recipe is
		int tierGap = (int)recipeTier - (int)_currentTier;
		return tierGap * Progression.ScoringConstants.ConditionTierPenalty;
	}

	/// <summary>
	/// Extracts biome/environment requirement from recipe conditions.
	/// Returns a human-readable string like "Graveyard" or "Near water", or null.
	/// Uses reference comparison against Terraria's static Condition singletons.
	/// </summary>
	private static string ExtractBiomeRequirement(Recipe recipe)
	{
		// Check recipe environment flags first (separate from Condition objects)
		if (recipe.HasCondition(Condition.NearWater)) return "Near water";
		if (recipe.HasCondition(Condition.NearHoney)) return "Near honey";
		if (recipe.HasCondition(Condition.NearLava)) return "Near lava";

		if (recipe.Conditions.Count == 0)
			return null;

		foreach (var cond in recipe.Conditions)
		{
			// Near* conditions
			if (cond == Condition.NearWater) return "Near water";
			if (cond == Condition.NearHoney) return "Near honey";
			if (cond == Condition.NearLava) return "Near lava";
			if (cond == Condition.NearShimmer) return "Near shimmer";

			// Biome conditions
			if (cond == Condition.InGraveyard) return "Graveyard";
			if (cond == Condition.InSnow) return "Snow biome";
			if (cond == Condition.InDesert) return "Desert";
			if (cond == Condition.InJungle) return "Jungle";
			if (cond == Condition.InDungeon) return "Dungeon";
			if (cond == Condition.InCorrupt) return "Corruption";
			if (cond == Condition.InCrimson) return "Crimson";
			if (cond == Condition.InHallow) return "Hallow";
			if (cond == Condition.InGlowshroom) return "Mushroom biome";
			if (cond == Condition.InSpace) return "Space";
			if (cond == Condition.InUnderworld) return "Underworld";
			if (cond == Condition.InBeach) return "Beach";
			if (cond == Condition.InMeteor) return "Meteorite";
			if (cond == Condition.InRain) return "During rain";
			if (cond == Condition.InSandstorm) return "During sandstorm";

			// Depth layers
			if (cond == Condition.InSkyHeight) return "Space";
			if (cond == Condition.InOverworldHeight) return "Surface";
			if (cond == Condition.InDirtLayerHeight) return "Underground";
			if (cond == Condition.InRockLayerHeight) return "Cavern";
			if (cond == Condition.InUnderworldHeight) return "Underworld";

			// Time
			if (cond == Condition.TimeDay) return "Daytime";
			if (cond == Condition.TimeNight) return "Nighttime";
		}

		return null;
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
			var sources = _sourceSystem?.GetSources(itemType, _worldContext) ?? [];
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
			case ShimmerNode shimmer:
				// The shimmer output is "craftable" (obtainable via shimmer)
				craftable.Add(shimmer.ItemType);
				CollectLeafMaterials(shimmer.SourceItem, needed, craftable);
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
		if (node is ShimmerNode shimmer)
		{
			// Recurse into the source item first
			CollectCraftSteps(shimmer.SourceItem, steps, craftedItems, inventory, includeChests);

			int stepNum = steps.Count + 1;
			var shimmerInput = new CraftStepInput(
				shimmer.SourceItemType, shimmer.Quantity,
				craftedItems.ContainsKey(shimmer.SourceItemType)
					? CraftStepInputSource.FromPreviousStep
					: CraftStepInputSource.NeedToAcquire,
				craftedItems.GetValueOrDefault(shimmer.SourceItemType, 0) > 0
					? craftedItems[shimmer.SourceItemType]
					: null,
				null, null);

			steps.Add(new CraftStep(
				stepNum, shimmer.ItemType, shimmer.Quantity,
				shimmer.Quantity, [], [shimmerInput], "Shimmer transformation",
				BiomeRequirement: null, IsShimmerStep: true));
			craftedItems[shimmer.ItemType] = stepNum;
			return;
		}

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
		string biome = ExtractBiomeRequirement(craft.Recipe);

		steps.Add(new CraftStep(
			stepNumber, craft.ItemType, craft.Quantity,
			craft.CraftCount, requiredTiles, inputs, conditions, biome));

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
