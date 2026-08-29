using System;
using System.Collections.Generic;
using System.Linq;
using ProgressionCompass.Common.Systems;
using ProgressionCompass.Content.Graph;
using ProgressionCompass.Content.Progression;

namespace ProgressionCompass.Content.Tracking;

/// <summary>
/// Analyzes upgrade recommendations to produce a ranked list of locations/biomes,
/// scored by how many upgrades their drops contribute to. Inverts the "what do I need?"
/// axis into "where should I go?"
/// </summary>
public static class SourcePriorityAnalyzer
{
	/// <summary>
	/// Analyzes all upgrade items and ranks locations by aggregate upgrade contribution.
	/// Runs synchronously — all inputs are pre-computed by the async progress scan.
	/// </summary>
	public static SourcePriorityResult Analyze(List<UpgradeItem> upgrades, ItemSourceSystem sourceSystem,
		WorldContext worldContext = default, EffortAnalyzer effortAnalyzer = null)
	{
		if (upgrades == null || upgrades.Count == 0 || sourceSystem == null)
			return new SourcePriorityResult { TotalUpgradesAnalyzed = 0 };

		// Phase A: Build material → upgrade mapping
		// For each non-CraftableNow upgrade with missing items, record what it needs.
		var materialToUpgrades = new Dictionary<int, List<(UpgradeItem upgrade, int missingCount)>>();

		int analyzedCount = 0;
		foreach (var upgrade in upgrades)
		{
			if (upgrade.Proximity == Proximity.CraftableNow)
				continue;
			if (upgrade.MissingItems == null || upgrade.MissingItems.Count == 0)
				continue;
			// Skip upgrades with no progress — don't point players toward things
			// they haven't started working on at all
			if (upgrade.CompletionPercent <= 0f)
				continue;

			analyzedCount++;
			int missingCount = upgrade.MissingItems.Count;
			foreach (var (matType, _) in upgrade.MissingItems)
			{
				if (!materialToUpgrades.TryGetValue(matType, out var list))
				{
					list = [];
					materialToUpgrades[matType] = list;
				}
				list.Add((upgrade, missingCount));
			}
		}

		if (materialToUpgrades.Count == 0)
			return new SourcePriorityResult { TotalUpgradesAnalyzed = upgrades.Count };

		// Phase B: Look up sources for each material and map to locations
		var locationDrops = new Dictionary<string, List<LocationDrop>>();
		var locationTypes = new Dictionary<string, LocationType>();
		var locationEntityIds = new Dictionary<string, HashSet<int>>();
		var locationContextHints = new Dictionary<string, string>();

		foreach (var (matType, upgradeList) in materialToUpgrades)
		{
			var sources = sourceSystem.GetSources(matType, worldContext);
			if (sources == null || sources.Count == 0)
				continue;

			// Build contributions (shared across all sources for this material)
			var contributions = new List<UpgradeContribution>();
			foreach (var (upgrade, missingCount) in upgradeList)
			{
				contributions.Add(new UpgradeContribution
				{
					UpgradeItemType = upgrade.ItemType,
					UpgradeProximity = upgrade.Proximity,
					UpgradeCompletionPercent = upgrade.CompletionPercent,
					MissingCountForUpgrade = missingCount,
					IsLastPiece = missingCount <= 2
				});
			}

			// Total quantity needed across all upgrades
			int totalQty = 0;
			foreach (var (upgrade, _) in upgradeList)
				foreach (var (mType, qty) in upgrade.MissingItems)
					if (mType == matType)
						totalQty += qty;

			// Consider all sources for location coverage, use best for scoring
			foreach (var source in sources)
			{
				float dropScore = ComputeDropScore(source, contributions, effortAnalyzer);
				if (dropScore <= 0f)
					continue;

				var locations = BiomeMapper.GetSourceLocations(source, worldContext);
				var locType = ClassifyLocationType(source);

				foreach (string loc in locations)
				{
					if (!locationDrops.TryGetValue(loc, out var drops))
					{
						drops = [];
						locationDrops[loc] = drops;
					}

					// Avoid adding duplicate materials to the same location
					if (drops.Exists(d => d.ItemType == matType))
						continue;

					drops.Add(new LocationDrop
					{
						ItemType = matType,
						QuantityNeeded = totalQty,
						Source = source,
						FeedsInto = contributions,
						DropScore = dropScore
					});

					locationTypes.TryAdd(loc, locType);

					// Track NPC entity IDs for icon rendering
					if (source.SourceEntityId > 0)
					{
						if (!locationEntityIds.TryGetValue(loc, out var ids))
						{
							ids = [];
							locationEntityIds[loc] = ids;
						}
						ids.Add(source.SourceEntityId);
					}

					// Attach context hints
					if (!locationContextHints.ContainsKey(loc))
					{
						string hint = GetContextHint(source, loc);
						if (hint != null)
							locationContextHints[loc] = hint;
					}
				}
			}
		}

		// Phase C: Aggregate into LocationRankings, sort
		var rankings = new List<LocationRanking>();
		float maxScore = 0f;

		foreach (var (locName, drops) in locationDrops)
		{
			float totalScore = 0f;
			foreach (var drop in drops)
				totalScore += drop.DropScore;

			var allUpgradeTypes = new HashSet<int>();
			foreach (var drop in drops)
				foreach (var c in drop.FeedsInto)
					allUpgradeTypes.Add(c.UpgradeItemType);

			drops.Sort((a, b) => b.DropScore.CompareTo(a.DropScore));

			if (totalScore > maxScore)
				maxScore = totalScore;

			rankings.Add(new LocationRanking
			{
				LocationName = locName,
				Type = locationTypes.GetValueOrDefault(locName, LocationType.Biome),
				TotalScore = totalScore,
				UpgradeCount = allUpgradeTypes.Count,
				Drops = drops,
				ContextHint = locationContextHints.GetValueOrDefault(locName),
				SourceEntityIds = locationEntityIds.TryGetValue(locName, out var entityIds)
					? [.. entityIds]
					: []
			});
		}

		rankings.Sort((a, b) => b.TotalScore.CompareTo(a.TotalScore));

		// Derive star ratings: top location = 5 stars, scale proportionally
		if (maxScore > 0f)
		{
			foreach (var ranking in rankings)
				ranking.StarRating = Math.Max(1, (int)Math.Ceiling(ranking.TotalScore / maxScore * 5f));
		}

		return new SourcePriorityResult
		{
			Rankings = rankings,
			TotalUpgradesAnalyzed = upgrades.Count
		};
	}

	// ── Scoring ─────────────────────────────────────────────────

	private static float ComputeDropScore(ItemSource source, List<UpgradeContribution> contributions,
		EffortAnalyzer effortAnalyzer = null)
	{
		float accessibility = GetAccessibilityMultiplier(source);

		// If we have effort data, apply travel/block penalty from the source
		if (effortAnalyzer != null && source.SourceEntityId > 0)
		{
			var effort = effortAnalyzer.GetEffort(source.SourceEntityId);
			if (effort.IsBlocked) accessibility *= 0.2f;
			accessibility /= effort.TravelPenalty;
		}

		float total = 0f;

		foreach (var c in contributions)
		{
			float proximity = c.UpgradeProximity switch
			{
				Proximity.CraftableNow => 0f,
				Proximity.AlmostReady => ScoringConstants.ProximityWeightAlmostReady,
				Proximity.NeedsWork => ScoringConstants.ProximityWeightNeedsWork,
				Proximity.FarAway => ScoringConstants.ProximityWeightFarAway,
				_ => ScoringConstants.ProximityWeightFarAway
			};

			float lastPiece = c.MissingCountForUpgrade switch
			{
				1 => ScoringConstants.LastPieceBonus1,
				2 => ScoringConstants.LastPieceBonus2,
				3 => ScoringConstants.LastPieceBonus3,
				_ => 1.0f
			};

			total += proximity * accessibility * lastPiece;
		}

		return total;
	}

	private static float GetAccessibilityMultiplier(ItemSource source)
	{
		if (source.Type == ItemSourceType.NPCShop) return ScoringConstants.AccessibilityShop;
		if (source.Type == ItemSourceType.Gathering) return ScoringConstants.AccessibilityGathering;
		if (source.Type == ItemSourceType.Catching) return ScoringConstants.AccessibilityGathering;
		if (source.Type == ItemSourceType.Fishing) return ScoringConstants.AccessibilityFishing;
		if (source.Type == ItemSourceType.ChestLoot) return ScoringConstants.AccessibilityChestLoot;
		if (source.Type == ItemSourceType.Shimmer) return ScoringConstants.AccessibilityShimmer;

		// Drop-rate based for NPC/Boss/Event drops
		if (source.DropRate >= 1.0f) return ScoringConstants.AccessibilityGuaranteedDrop;
		if (source.DropRate >= 0.10f) return ScoringConstants.AccessibilityCommonDrop;
		if (source.DropRate >= 0.02f) return ScoringConstants.AccessibilityUncommonDrop;
		return ScoringConstants.AccessibilityRareDrop;
	}

	// ── Classification ──────────────────────────────────────────

	private static LocationType ClassifyLocationType(ItemSource source) => source.Type switch
	{
		ItemSourceType.BossDrop or ItemSourceType.BagDrop => LocationType.Boss,
		ItemSourceType.NPCShop => LocationType.Shop,
		ItemSourceType.Event => LocationType.Event,
		ItemSourceType.ChestLoot or ItemSourceType.Gathering or ItemSourceType.Catching
			or ItemSourceType.Shimmer or ItemSourceType.Fishing => LocationType.Exploration,
		_ => LocationType.Biome
	};

	// ── Context Hints ───────────────────────────────────────────

	private static string GetContextHint(ItemSource source, string locationName)
	{
		switch (source.Type)
		{
			case ItemSourceType.BossDrop:
			case ItemSourceType.BagDrop:
			{
				if (source.SourceEntityId > 0)
				{
					var boss = BossDatabase.GetByNpcType(source.SourceEntityId);
					if (boss != null && !string.IsNullOrEmpty(boss.SummonHint))
						return $"Summon: {boss.SummonHint}";
				}
				return null;
			}

			case ItemSourceType.Event:
			{
				// Extract event name from location string "Event: Goblin Army"
				string eventName = locationName.StartsWith("Event: ")
					? locationName["Event: ".Length..]
					: locationName;
				string hint = BiomeMapper.GetEventTriggerHint(eventName);
				return hint != null ? $"Trigger: {hint}" : null;
			}

			default:
				return null;
		}
	}
}
