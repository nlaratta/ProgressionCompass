using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using CraftPathOptimizer.Common.Systems;
using CraftPathOptimizer.Content.Graph;

namespace CraftPathOptimizer.Content.Progression;

/// <summary>
/// A near-complete combination item: an accessory or weapon made by combining
/// other equipment pieces together, where the player owns some of the components.
/// </summary>
public class CombinationResult
{
	public int ItemType { get; init; }
	public int LeafMaterialsOwned { get; init; }
	public int LeafMaterialsTotal { get; init; }
	public float CompletionPercent => LeafMaterialsTotal > 0
		? (float)LeafMaterialsOwned / LeafMaterialsTotal * 100f
		: 0f;
	public List<(int itemType, int quantity)> MissingItems { get; init; } = [];
	public bool IsAccessory { get; init; }
	public bool IsWeapon { get; init; }
	public bool IsArmor { get; init; }

	/// <summary>
	/// Sub-combinations that are buildable now (100% complete) and would
	/// make progress toward this item. e.g., "Build Obsidian Shield to
	/// progress toward Ankh Shield."
	/// </summary>
	public List<CombinationMilestone> ReadyMilestones { get; set; } = [];
}

/// <summary>A sub-combination that can be built right now as a step toward a larger goal.</summary>
public class CombinationMilestone
{
	public int ItemType { get; init; }
	public bool IsReady { get; init; }
	public float CompletionPercent { get; init; }
}

/// <summary>
/// Scans all recipes to find equipment combination recipes (accessories or weapons
/// made by combining 2+ other equipment pieces). Computes completion % using the
/// resolver and filters out items the player already owns or has upgraded beyond.
/// </summary>
public static class CombinationScanner
{
	private const float MinCompletionPercent = 40f;

	/// <summary>
	/// Scans for near-complete combination items.
	/// Returns results sorted by completion % descending.
	/// </summary>
	public static List<CombinationResult> Scan(
		RecipeIndexSystem recipeIndex,
		ItemSourceSystem sourceSystem,
		Dictionary<int, InventoryEntry> inventory,
		bool includeChests,
		Tier currentTier)
	{
		var results = new List<CombinationResult>();
		var ownedItemTypes = new HashSet<int>();

		// Build set of all item types the player owns
		foreach (var (type, entry) in inventory)
		{
			if (entry.AvailableCount(includeChests) > 0)
				ownedItemTypes.Add(type);
		}

		// Build a set of all item types that are ingredients of other recipes
		// (to detect "upgraded" items — if you own Ankh Shield, skip Obsidian Shield)
		var upgradeTargets = BuildUpgradeMap(recipeIndex);

		// Pass 1: Identify ALL combination recipe candidates (cheap — no resolver calls)
		var candidates = new List<(int outputType, bool isAccessory, bool isWeapon, bool isArmor)>();
		var seenOutputs = new HashSet<int>();

		for (int i = 0; i < Recipe.numRecipes; i++)
		{
			Recipe recipe = Main.recipe[i];
			int outputType = recipe.createItem.type;

			if (outputType <= ItemID.None || seenOutputs.Contains(outputType))
				continue;
			seenOutputs.Add(outputType);

			if (!IsCombinationRecipe(recipe))
				continue;

			var output = ContentSamples.ItemsByType.GetValueOrDefault(outputType);
			if (output == null) continue;

			bool isAccessory = output.accessory;
			bool isWeapon = output.damage > 0 && output.ammo == 0;
			bool isArmor = output.headSlot >= 0 || output.bodySlot >= 0 || output.legSlot >= 0;
			if (!isAccessory && !isWeapon && !isArmor) continue;

			if (ownedItemTypes.Contains(outputType)) continue;
			if (PlayerOwnsUpgrade(outputType, ownedItemTypes, upgradeTargets)) continue;

			Tier recipeTier = ProgressionTier.EstimateRecipeTier(recipe);
			if (recipeTier > currentTier + 2) continue;

			candidates.Add((outputType, isAccessory, isWeapon, isArmor));
		}

		// Pass 2: Resolve each candidate to get completion % (expensive but targeted)
		var resolver = new DependencyResolver(recipeIndex, sourceSystem);

		foreach (var (outputType, isAccessory, isWeapon, isArmor) in candidates)
		{
			var inv = new Dictionary<int, InventoryEntry>(inventory);
			ResolverResult result;
			try
			{
				result = resolver.Resolve(outputType, 1, inv, includeChests);
			}
			catch (Exception ex)
			{
				global::CraftPathOptimizer.Common.Log.Warn($"Combination resolve failed for item {outputType}: {ex.Message}");
				continue;
			}

			if (result?.Materials == null || result.Materials.Count < 2)
				continue;

			// Count owned vs total leaf materials
			int owned = 0;
			int total = result.Materials.Count;
			var missing = new List<(int, int)>();

			foreach (var mat in result.Materials)
			{
				if (mat.IsComplete(includeChests))
					owned++;
				else
					missing.Add((mat.ItemType, mat.Deficit(includeChests)));
			}

			float pct = (float)owned / total * 100f;
			// Lower threshold for complex items (5+ materials) — even partial progress is meaningful
			float threshold = total >= 5 ? 20f : MinCompletionPercent;
			if (pct < threshold)
				continue;

			results.Add(new CombinationResult
			{
				ItemType = outputType,
				LeafMaterialsOwned = owned,
				LeafMaterialsTotal = total,
				MissingItems = missing,
				IsAccessory = isAccessory,
				IsWeapon = isWeapon,
				IsArmor = isArmor
			});
		}

		// Build a set of all combination item types for sub-component detection
		var combinationTypes = new HashSet<int>();
		foreach (var r in results)
			combinationTypes.Add(r.ItemType);

		// For each result, find sub-combinations that are components of it
		// Build ingredient graph: which combination items are ingredients of which?
		var subComponentOf = new Dictionary<int, List<int>>(); // childType → list of parentTypes

		foreach (var r in results)
		{
			if (!recipeIndex.RecipesByResult.TryGetValue(r.ItemType, out var recipes))
				continue;

			foreach (var recipe in recipes)
				CollectSubCombinations(recipe, r.ItemType, combinationTypes, recipeIndex, subComponentOf, 0);
		}

		// Remove items that are sub-components of other results
		var topLevel = new List<CombinationResult>();
		foreach (var r in results)
		{
			if (subComponentOf.ContainsKey(r.ItemType))
			{
				// This item is a sub-component of something else in our results.
				// Only keep it if the parent isn't in our results (filtered out by tier/completion).
				bool parentInResults = false;
				foreach (int parentType in subComponentOf[r.ItemType])
				{
					if (combinationTypes.Contains(parentType) && results.Exists(x => x.ItemType == parentType))
					{
						parentInResults = true;
						break;
					}
				}
				if (parentInResults)
					continue; // Skip — it'll appear as a milestone under the parent
			}
			topLevel.Add(r);
		}

		// Attach milestones: for each top-level item, find sub-combinations
		// that are ready to build (or close) and attach them
		foreach (var r in topLevel)
		{
			var milestones = new List<CombinationMilestone>();
			foreach (var sub in results)
			{
				if (sub.ItemType == r.ItemType) continue;
				// Check if sub is a recursive ingredient of r
				if (subComponentOf.TryGetValue(sub.ItemType, out var parents)
					&& parents.Contains(r.ItemType))
				{
					milestones.Add(new CombinationMilestone
					{
						ItemType = sub.ItemType,
						IsReady = sub.CompletionPercent >= 100f,
						CompletionPercent = sub.CompletionPercent
					});
				}
			}
			// Also check combinations that didn't meet the 40% threshold
			// but ARE 100% ready to build — these are worth showing
			try
			{
				FindReadySubCombinations(r.ItemType, recipeIndex, inventory, includeChests,
					resolver, combinationTypes, ownedItemTypes, milestones);
			}
			catch (Exception ex)
			{
				global::CraftPathOptimizer.Common.Log.Warn($"Sub-combination scan failed for {r.ItemType}: {ex.Message}");
			}

			milestones.Sort((a, b) => b.CompletionPercent.CompareTo(a.CompletionPercent));
			r.ReadyMilestones = milestones;
		}

		topLevel.Sort((a, b) => b.CompletionPercent.CompareTo(a.CompletionPercent));
		return topLevel;
	}

	/// <summary>
	/// Checks if a recipe is a combination/upgrade recipe worth tracking.
	/// Returns true if:
	/// - 2+ equipment ingredients (classic combination: Ankh Shield, Terra Blade)
	/// - OR 1 equipment ingredient + 2 other ingredients (upgrade: Minishark → Megashark)
	/// The output must also be equipment (checked by the caller).
	/// </summary>
	private static bool IsCombinationRecipe(Recipe recipe)
	{
		int equipmentIngredients = 0;
		int totalIngredients = 0;

		foreach (var ingredient in recipe.requiredItem)
		{
			if (ingredient.type <= ItemID.None)
				continue;

			totalIngredients++;
			var item = ContentSamples.ItemsByType.GetValueOrDefault(ingredient.type);
			if (item == null)
				continue;

			bool isEquipment = item.accessory || item.damage > 0
				|| item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0;
			if (isEquipment)
				equipmentIngredients++;
		}

		// 2+ equipment = classic combination (Ankh Shield, Night's Edge)
		if (equipmentIngredients >= 2)
			return true;

		// 1 equipment + other materials = upgrade recipe (Minishark → Megashark, Turtle → Beetle)
		if (equipmentIngredients >= 1 && totalIngredients >= 2)
			return true;

		return false;
	}

	/// <summary>
	/// Builds a map: itemType → list of items that USE it as a recipe ingredient.
	/// Used to detect "this is a component of something the player already owns."
	/// </summary>
	private static Dictionary<int, List<int>> BuildUpgradeMap(RecipeIndexSystem recipeIndex)
	{
		var map = new Dictionary<int, List<int>>();

		if (recipeIndex?.RecipesByIngredient == null)
			return map;

		foreach (var (ingredientType, recipes) in recipeIndex.RecipesByIngredient)
		{
			foreach (var recipe in recipes)
			{
				int outputType = recipe.createItem.type;
				var output = ContentSamples.ItemsByType.GetValueOrDefault(outputType);
				if (output == null)
					continue;

				// Only track upgrades where output is equipment
				if (!output.accessory && output.damage <= 0 && output.defense <= 0)
					continue;

				if (!map.ContainsKey(ingredientType))
					map[ingredientType] = [];
				map[ingredientType].Add(outputType);
			}
		}

		return map;
	}

	/// <summary>
	/// Checks if the player owns an upgrade that uses this item as a component.
	/// e.g., if player owns Ankh Shield, they don't need to see Obsidian Shield.
	/// Checks recursively: Ankh Shield uses Ankh Charm uses Armor Bracing, etc.
	/// </summary>
	private static bool PlayerOwnsUpgrade(
		int itemType,
		HashSet<int> ownedItems,
		Dictionary<int, List<int>> upgradeMap,
		int depth = 0)
	{
		if (depth > 5) return false; // Prevent infinite recursion

		if (!upgradeMap.TryGetValue(itemType, out var upgrades))
			return false;

		foreach (int upgradeType in upgrades)
		{
			if (ownedItems.Contains(upgradeType))
				return true;
			// Recursively check if the player owns an upgrade of the upgrade
			if (PlayerOwnsUpgrade(upgradeType, ownedItems, upgradeMap, depth + 1))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Recursively collects which combination items are sub-components of a parent item
	/// by walking its recipe tree.
	/// </summary>
	private static void CollectSubCombinations(
		Recipe recipe, int parentType, HashSet<int> combinationTypes,
		RecipeIndexSystem recipeIndex, Dictionary<int, List<int>> subComponentOf,
		int depth)
	{
		if (depth > 8) return;

		foreach (var ingredient in recipe.requiredItem)
		{
			if (ingredient.type <= ItemID.None) continue;

			if (combinationTypes.Contains(ingredient.type))
			{
				if (!subComponentOf.ContainsKey(ingredient.type))
					subComponentOf[ingredient.type] = [];
				if (!subComponentOf[ingredient.type].Contains(parentType))
					subComponentOf[ingredient.type].Add(parentType);
			}

			// Recurse into sub-recipes
			if (recipeIndex.RecipesByResult.TryGetValue(ingredient.type, out var subRecipes))
			{
				foreach (var sub in subRecipes)
					CollectSubCombinations(sub, parentType, combinationTypes, recipeIndex, subComponentOf, depth + 1);
			}
		}
	}

	/// <summary>
	/// Finds sub-combination items of a parent that are 100% ready to build,
	/// even if they didn't meet the overall completion threshold.
	/// </summary>
	private static void FindReadySubCombinations(
		int parentType, RecipeIndexSystem recipeIndex,
		Dictionary<int, InventoryEntry> inventory, bool includeChests,
		DependencyResolver resolver, HashSet<int> alreadyFound,
		HashSet<int> ownedItems, List<CombinationMilestone> milestones)
	{
		if (!recipeIndex.RecipesByResult.TryGetValue(parentType, out var recipes))
			return;

		foreach (var recipe in recipes)
		{
			foreach (var ingredient in recipe.requiredItem)
			{
				if (ingredient.type <= ItemID.None) continue;
				if (ownedItems.Contains(ingredient.type)) continue;
				if (alreadyFound.Contains(ingredient.type)) continue;
				if (milestones.Exists(m => m.ItemType == ingredient.type)) continue;

				// Check if this ingredient is itself a combination recipe
				if (!recipeIndex.RecipesByResult.TryGetValue(ingredient.type, out var subRecipes))
					continue;

				bool isCombination = false;
				foreach (var sub in subRecipes)
				{
					if (IsCombinationRecipe(sub))
					{
						isCombination = true;
						break;
					}
				}
				if (!isCombination) continue;

				// Resolve to check if ready
				try
				{
					var inv = new Dictionary<int, InventoryEntry>(inventory);
					var result = resolver.Resolve(ingredient.type, 1, inv, includeChests);
					if (result?.Materials == null) continue;

					int owned = 0;
					foreach (var mat in result.Materials)
						if (mat.IsComplete(includeChests)) owned++;

					float pct = (float)owned / result.Materials.Count * 100f;
					if (pct >= 80f) // Show milestones that are ≥80% complete
					{
						milestones.Add(new CombinationMilestone
						{
							ItemType = ingredient.type,
							IsReady = pct >= 100f,
							CompletionPercent = pct
						});
					}
				}
				catch { }
			}
		}
	}
}
