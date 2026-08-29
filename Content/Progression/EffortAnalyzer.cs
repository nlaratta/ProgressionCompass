using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using ProgressionCompass.Common.Systems;
using ProgressionCompass.Content.Graph;
using Terraria.ModLoader;

namespace ProgressionCompass.Content.Progression;

/// <summary>
/// Unified cost model for acquiring an item. Lower = easier to obtain.
/// Factors in source type, drop rate, NPC availability, travel distance,
/// and material triviality. Replaces scattered heuristics across systems.
/// </summary>
public readonly record struct EffortCost
{
	/// <summary>Base effort units (0 = free, higher = harder).</summary>
	public float BaseEffort { get; init; }

	/// <summary>Drop rate as probability (1.0 = guaranteed, 0.01 = 1/100). Default 1.0.</summary>
	public float SuccessRate { get; init; }

	/// <summary>Travel/access multiplier (1.0 = nearby, 2.0 = far biome).</summary>
	public float TravelPenalty { get; init; }

	/// <summary>True if blocked by an unmet condition (boss, NPC, biome).</summary>
	public bool IsBlocked { get; init; }

	/// <summary>
	/// Effective effort = base * travel * expected attempts.
	/// A shop item = 1. A 1/100 drop in the Underworld = 200.
	/// </summary>
	public float Effective => BaseEffort * TravelPenalty * Math.Min(1f / Math.Max(SuccessRate, 0.001f), 100f);

	/// <summary>True if this is a trivial/free acquisition (Wood, Stone, etc.).</summary>
	public bool IsTrivial => BaseEffort <= 1f && !IsBlocked;

	public static readonly EffortCost Free = new() { BaseEffort = 0, SuccessRate = 1f, TravelPenalty = 1f };
	public static readonly EffortCost Unknown = new() { BaseEffort = 50, SuccessRate = 1f, TravelPenalty = 1f };
}

/// <summary>
/// Estimates acquisition effort for any item. Stateless utility — create per-request
/// with current world/inventory state, query via GetEffort(itemType).
/// Results are cached per instance for repeated lookups within the same cycle.
/// </summary>
public class EffortAnalyzer
{
	private readonly ItemSourceSystem _sourceSystem;
	private readonly WorldContext _worldContext;
	private readonly Dictionary<int, EffortCost> _cache = [];

	public EffortAnalyzer(ItemSourceSystem sourceSystem, WorldContext worldContext)
	{
		_sourceSystem = sourceSystem;
		_worldContext = worldContext;
	}

	/// <summary>Returns the cheapest acquisition effort for an item across all sources.</summary>
	public EffortCost GetEffort(int itemType)
	{
		if (_cache.TryGetValue(itemType, out var cached))
			return cached;

		var result = ComputeEffort(itemType);
		_cache[itemType] = result;
		return result;
	}

	private EffortCost ComputeEffort(int itemType)
	{
		var sources = _sourceSystem?.GetSources(itemType, _worldContext);

		// If no non-recipe sources exist, check if it's craftable
		if (sources == null || sources.Count == 0)
		{
			var recipeIndex = ModContent.GetInstance<RecipeIndexSystem>();
			if (recipeIndex?.RecipesByResult != null && recipeIndex.RecipesByResult.ContainsKey(itemType))
			{
				// Craftable item with no drop/shop/gather source.
				// Estimate effort from recipe complexity: simple (1-2 ingredients) = low effort
				var recipes = recipeIndex.RecipesByResult[itemType];
				int minIngredients = int.MaxValue;
				foreach (var recipe in recipes)
				{
					int count = 0;
					foreach (var ing in recipe.requiredItem)
						if (ing.type > ItemID.None) count++;
					if (count < minIngredients) minIngredients = count;
				}
				float craftEffort = minIngredients switch
				{
					<= 1 => 1f,
					2 => 2f,
					3 => 3f,
					_ => 5f
				};
				return new EffortCost { BaseEffort = craftEffort, SuccessRate = 1f, TravelPenalty = 1f };
			}
			return EffortCost.Unknown;
		}

		// Find the cheapest source
		EffortCost best = EffortCost.Unknown;
		float bestEffective = float.MaxValue;

		foreach (var source in sources)
		{
			var cost = ScoreSource(source);
			if (cost.Effective < bestEffective)
			{
				bestEffective = cost.Effective;
				best = cost;
			}
		}

		return best;
	}

	private EffortCost ScoreSource(ItemSource source)
	{
		float baseEffort = source.Type switch
		{
			ItemSourceType.Gathering => IsAbundantGathering(source) ? 0.5f : 2f,
			ItemSourceType.Catching => 1.5f,
			ItemSourceType.NPCShop => _worldContext.IsNpcAvailable(source.SourceEntityId) ? 1f : 15f,
			ItemSourceType.NPCDrop => DropEffort(source.DropRate),
			ItemSourceType.BossDrop => 10f,
			ItemSourceType.BagDrop => 10f,
			ItemSourceType.Event => 8f,
			ItemSourceType.Fishing => 6f,
			ItemSourceType.ChestLoot => 5f,
			ItemSourceType.Shimmer => 20f,
			ItemSourceType.Other => 100f, // Inferred "check wiki" sources — high effort
			_ => 15f
		};

		float successRate = source.DropRate > 0 ? source.DropRate : 1f;
		float travel = EstimateTravel(source);
		bool blocked = IsBlocked(source);

		// Blocked sources get massive penalty but aren't filtered
		if (blocked)
			baseEffort *= 5f;

		return new EffortCost
		{
			BaseEffort = baseEffort,
			SuccessRate = successRate,
			TravelPenalty = travel,
			IsBlocked = blocked
		};
	}

	private static float DropEffort(float dropRate)
	{
		if (dropRate >= 0.5f) return 2f;   // Common (50%+)
		if (dropRate >= 0.1f) return 4f;   // Uncommon (10%+)
		if (dropRate >= 0.02f) return 8f;  // Rare (2%+)
		return 15f;                         // Very rare (<2%)
	}

	private static bool IsAbundantGathering(ItemSource source)
	{
		// Check description for common gathering patterns
		string desc = source.Description;
		if (string.IsNullOrEmpty(desc)) return false;
		string lower = desc.ToLowerInvariant();
		return lower.Contains("chop:") || lower.Contains("trees")
			|| lower.Contains("dirt") || lower.Contains("stone")
			|| lower.Contains("sand");
	}

	private static float EstimateTravel(ItemSource source)
	{
		string desc = source.Description;
		if (string.IsNullOrEmpty(desc)) return 1f;
		string lower = desc.ToLowerInvariant();

		if (lower.Contains("underworld") || lower.Contains("hell")) return 2.0f;
		if (lower.Contains("jungle")) return 1.5f;
		if (lower.Contains("dungeon")) return 1.5f;
		if (lower.Contains("corruption") || lower.Contains("crimson")) return 1.3f;
		if (lower.Contains("cavern") || lower.Contains("underground")) return 1.2f;
		if (lower.Contains("ocean")) return 1.3f;
		if (lower.Contains("sky") || lower.Contains("space")) return 1.4f;

		return 1.0f;
	}

	private bool IsBlocked(ItemSource source)
	{
		// Shop from unavailable NPC
		if (source.Type == ItemSourceType.NPCShop
			&& source.SourceEntityId > 0
			&& !_worldContext.IsNpcAvailable(source.SourceEntityId))
			return true;

		return false;
	}

	// ── Static Helpers ──────────────────────────────────────────

	/// <summary>
	/// Computes total remaining effort for a list of missing materials.
	/// Used by UpgradeItemBuilder and GearRecommender to weight proximity.
	/// </summary>
	public float TotalRemainingEffort(List<(int itemType, int quantity)> missingItems)
	{
		if (missingItems == null || missingItems.Count == 0)
			return 0f;

		float total = 0f;
		foreach (var (itemType, qty) in missingItems)
		{
			var effort = GetEffort(itemType);
			total += effort.Effective * Math.Min(qty, 10); // Cap quantity impact
		}
		return total;
	}

	/// <summary>
	/// Returns true if ALL missing items are trivial (Wood, Stone, etc.).
	/// Replaces the HasMeaningfulProgress blocklist hack.
	/// </summary>
	public bool AllMissingAreTrivial(List<(int itemType, int quantity)> missingItems)
	{
		if (missingItems == null || missingItems.Count == 0)
			return true;

		foreach (var (itemType, _) in missingItems)
		{
			if (!GetEffort(itemType).IsTrivial)
				return false;
		}
		return true;
	}
}
