using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using CraftPathOptimizer.Common.Systems;
using CraftPathOptimizer.Content.Graph;

namespace CraftPathOptimizer.Content.Progression;

public enum GearCategory
{
	Weapon,
	Armor,
	Accessory,
	Tool
}

/// <summary>
/// A recommended gear upgrade: an item the player doesn't own that is
/// better than what they currently have and available at their tier.
/// </summary>
public class GearRecommendation
{
	public int ItemType { get; init; }
	public GearCategory Category { get; init; }
	public string ImprovementText { get; init; }
	public bool CraftableNow { get; init; }
	public int MissingIngredientCount { get; init; }
	public int TotalIngredientCount { get; init; }
	public Tier RequiredTier { get; init; }
	public float Score { get; init; }

	/// <summary>How to obtain this item if not craftable.</summary>
	public string SourceHint { get; init; }
	/// <summary>All known sources for non-craftable items.</summary>
	public List<Graph.ItemSource> AllSources { get; init; }
	/// <summary>True if this item has a crafting recipe.</summary>
	public bool HasRecipe { get; init; }
	/// <summary>Recipe ingredients: (itemType, needed, owned). Null if not craftable.</summary>
	public List<(int itemType, int needed, int owned)> Ingredients { get; init; }
}

/// <summary>
/// Scans all items in the game, filters by tier and class, scores against
/// the player's current gear, and returns ranked upgrade recommendations.
/// Works with modded items via ContentSamples + DamageType.CountsAsClass().
/// </summary>
public static class GearRecommender
{
	// No cap — tier filter + class filter at display time handles limiting

	public static List<GearRecommendation> Recommend(
		int currentArmorDefense,
		PlayerClass classFilter,
		Tier currentTier,
		RecipeIndexSystem recipeIndex,
		ItemSourceSystem sourceSystem,
		Dictionary<int, InventoryEntry> inventory,
		bool includeChests)
	{
		// Analyze player's current best gear
		int bestMeleeDmg = 0, bestRangedDmg = 0, bestMagicDmg = 0, bestSummonDmg = 0;
		int currentDefense = 0;
		int bestPickPower = 0;

		foreach (var (type, entry) in inventory)
		{
			if (entry.AvailableCount(includeChests) <= 0)
				continue;

			var item = ContentSamples.ItemsByType.GetValueOrDefault(type);
			if (item == null) continue;

			if (item.damage > 0)
			{
				bool isWhip = item.DamageType == DamageClass.SummonMeleeSpeed;
				if (!isWhip && item.DamageType.CountsAsClass(DamageClass.Melee) && item.damage > bestMeleeDmg)
					bestMeleeDmg = item.damage;
				if (item.DamageType.CountsAsClass(DamageClass.Ranged) && item.damage > bestRangedDmg)
					bestRangedDmg = item.damage;
				if (item.DamageType.CountsAsClass(DamageClass.Magic) && item.damage > bestMagicDmg)
					bestMagicDmg = item.damage;
				if ((item.DamageType.CountsAsClass(DamageClass.Summon) || isWhip) && item.damage > bestSummonDmg)
					bestSummonDmg = item.damage;
			}
			if (item.pick > bestPickPower)
				bestPickPower = item.pick;
		}

		currentDefense = currentArmorDefense;

		var ownedItems = new HashSet<int>();
		foreach (var (type, entry) in inventory)
			if (entry.AvailableCount(includeChests) > 0)
				ownedItems.Add(type);

		var results = new List<GearRecommendation>();

		if (ContentSamples.ItemsByType == null)
			return results;

		foreach (var (type, item) in ContentSamples.ItemsByType)
		{
			if (type <= ItemID.None || item == null)
				continue;

			// Skip items the player already owns
			if (ownedItems.Contains(type))
				continue;

			// Skip deprecated/unobtainable
			if (type < ItemID.Sets.Deprecated.Length && ItemID.Sets.Deprecated[type])
				continue;

			// Must be equipment
			bool isWeapon = item.damage > 0 && item.ammo == 0; // Skip ammo
			bool isArmor = item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0;
			bool isAccessory = item.accessory;
			bool isTool = item.pick > 0 || item.axe > 0;

			if (!isWeapon && !isArmor && !isAccessory && !isTool)
				continue;

			// Class filter — applies to weapons, armor, and class-specific accessories
			if (!ClassDetector.ItemMatchesClass(item, classFilter))
				continue;

			// Check if this item is available at the player's tier
			// Use rarity as a proxy (dynamic — works for modded items)
			Tier itemTier = ProgressionTier.EstimateItemTier(type);
			if (itemTier > currentTier + 1) // Allow peeking 1 tier ahead
				continue;

			// Score: how much better is this than current gear?
			float score = 0f;
			string improvement = "";
			GearCategory category;

			if (isWeapon)
			{
				category = GearCategory.Weapon;
				int bestCurrentDmg = GetBestDamageForClass(item, bestMeleeDmg, bestRangedDmg, bestMagicDmg, bestSummonDmg);
				int delta = item.damage - bestCurrentDmg;
				if (delta <= 0) continue; // Not an upgrade
				score = delta;
				improvement = $"+{delta} damage over current";
			}
			else if (isArmor)
			{
				category = GearCategory.Armor;
				int avgCurrentPerPiece = currentDefense / 3;
				int delta = item.defense - avgCurrentPerPiece;
				if (delta <= 0) continue;
				score = delta * 2f; // Weight armor upgrades
				improvement = $"+{delta} defense over current";
			}
			else if (isTool)
			{
				category = GearCategory.Tool;
				int delta = item.pick - bestPickPower;
				if (delta <= 0) continue;
				score = delta * 0.5f;
				improvement = $"+{delta}% pick power";
			}
			else
			{
				category = GearCategory.Accessory;
				// Accessories are harder to score — use rarity as proxy
				score = item.rare * 5f;
				if (item.wingSlot > 0)
					score += 20f; // Wings are always high priority
				improvement = $"Rarity: {item.rare}";
			}

			// Check craftability + gather ingredient details
			bool hasRecipe = recipeIndex?.RecipesByResult?.ContainsKey(type) ?? false;
			int missingCount = 0;
			int totalIngCount = 0;
			List<(int itemType, int needed, int owned)> bestIngredients = null;

			if (hasRecipe)
			{
				var recipes = recipeIndex.RecipesByResult[type];
				int bestMissing = int.MaxValue;

				foreach (var recipe in recipes)
				{
					int missing = 0;
					var ings = new List<(int, int, int)>();
					foreach (var ing in recipe.requiredItem)
					{
						if (ing.type <= ItemID.None) continue;
						int have = 0;
						if (inventory.TryGetValue(ing.type, out var entry))
							have = entry.AvailableCount(includeChests);
						ings.Add((ing.type, ing.stack, Math.Min(have, ing.stack)));
						if (have < ing.stack)
							missing++;
					}
					if (missing < bestMissing)
					{
						bestMissing = missing;
						bestIngredients = ings;
					}
				}
				missingCount = bestMissing;
				totalIngCount = bestIngredients?.Count ?? 0;
				if (missingCount == 0)
					score += 50f;
			}

			// Source info for non-craftable items
			string sourceHint = null;
			List<Graph.ItemSource> allSources = null;
			if (!hasRecipe && sourceSystem != null)
			{
				allSources = sourceSystem.GetSources(type);
				if (allSources.Count > 0)
				{
					var best = Graph.ItemSource.PickBest(allSources);
					sourceHint = best?.Description;
				}
			}

			results.Add(new GearRecommendation
			{
				ItemType = type,
				Category = category,
				ImprovementText = improvement,
				CraftableNow = hasRecipe && missingCount == 0,
				MissingIngredientCount = missingCount,
				TotalIngredientCount = totalIngCount,
				RequiredTier = itemTier,
				Score = score,
				SourceHint = sourceHint,
				AllSources = allSources,
				HasRecipe = hasRecipe,
				Ingredients = bestIngredients
			});
		}

		// Sort by score descending
		results.Sort((a, b) => b.Score.CompareTo(a.Score));

		return results;
	}

	private static int GetBestDamageForClass(Item item, int melee, int ranged, int magic, int summon)
	{
		if (item.DamageType == DamageClass.SummonMeleeSpeed) return summon; // Whips compare against summon
		if (item.DamageType.CountsAsClass(DamageClass.Melee)) return melee;
		if (item.DamageType.CountsAsClass(DamageClass.Ranged)) return ranged;
		if (item.DamageType.CountsAsClass(DamageClass.Magic)) return magic;
		if (item.DamageType.CountsAsClass(DamageClass.Summon)) return summon;
		return Math.Max(Math.Max(melee, ranged), Math.Max(magic, summon));
	}

	private static Tier EstimateItemTier(int itemType) =>
		ProgressionTier.EstimateItemTier(itemType);
}
