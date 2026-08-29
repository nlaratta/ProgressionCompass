using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using ProgressionCompass.Common.Systems;
using ProgressionCompass.Content.Graph;
using ProgressionCompass.Content.Progression;

namespace ProgressionCompass.Content.Tracking;

/// <summary>
/// Analyzes missing materials across all pinned goals and produces a prioritized,
/// location-grouped action plan. The key innovation: farm targets are clustered
/// by WHERE the player should go, not by individual item.
/// </summary>
public static class ActionClassifier
{
	public static List<ActionGroup> Classify(
		IReadOnlyList<PinnedGoal> goals,
		Dictionary<int, InventoryEntry> inventory,
		bool includeChests,
		ItemSourceSystem sourceSystem,
		EffortAnalyzer effortAnalyzer = null)
	{
		// Merge all missing materials across all goals
		var allMissing = new Dictionary<int, MergedNeed>();

		foreach (var goal in goals)
		{
			if (goal.Result?.Materials == null)
				continue;

			foreach (var mat in goal.Result.Materials)
			{
				if (mat.IsComplete(includeChests))
					continue;

				int deficit = mat.Deficit(includeChests);
				if (deficit <= 0)
					continue;

				if (allMissing.TryGetValue(mat.ItemType, out var existing))
				{
					existing.TotalNeeded += deficit;
					if (!existing.ForGoals.Contains(goal.ItemType))
						existing.ForGoals.Add(goal.ItemType);
				}
				else
				{
					bool equipped = inventory.TryGetValue(mat.ItemType, out var invEntry)
						&& invEntry.IsEquipped;
					allMissing[mat.ItemType] = new MergedNeed
					{
						ItemType = mat.ItemType,
						TotalNeeded = deficit,
						Owned = mat.Owned.AvailableCount(includeChests),
						Sources = mat.Sources,
						IsCraftable = mat.IsCraftable,
						IsEquipped = equipped,
						ForGoals = [goal.ItemType]
					};
				}
			}
		}

		if (allMissing.Count == 0)
			return [];

		// Classify each missing item and build groups
		var craftNowItems = new List<ActionItem>();
		var buyItems = new List<ActionItem>();
		var farmByLocation = new Dictionary<string, List<ActionItem>>();
		var eventItems = new Dictionary<string, List<ActionItem>>();
		var bossItems = new Dictionary<string, (List<ActionItem> items, ItemSource source)>();
		var exploreItems = new List<ActionItem>();
		var unknownItems = new List<ActionItem>();

		// Build craft step cache once — O(1) lookups instead of O(n×m) per item
		var craftCache = BuildCraftStepCache(goals);

		foreach (var (itemType, missing) in allMissing)
		{
			// Check if craftable with current inventory (all sub-ingredients owned)
			if (missing.IsCraftable && IsSubCraftable(itemType, craftCache))
			{
				string stationName = GetCraftingStation(itemType, craftCache);
				var ingredients = GetCraftIngredients(itemType, craftCache);
				craftNowItems.Add(new ActionItem
				{
					ItemType = itemType,
					QuantityNeeded = missing.TotalNeeded,
					QuantityOwned = missing.Owned,
					HowToGet = $"Craft at {stationName ?? "crafting station"}",
					Source = null,
					Ingredients = ingredients,
					IsEquipped = missing.IsEquipped,
					GoalAttribution = FormatGoalAttribution(missing.ForGoals)
				});
				continue;
			}

			if (missing.Sources.Count == 0)
			{
				// Fix #3: Don't show "Unknown source" for craftable intermediates
				if (missing.IsCraftable)
					unknownItems.Add(MakeActionItem(missing, "Craft (see Tree tab for recipe)"));
				else
					unknownItems.Add(MakeActionItem(missing, "Unknown source"));
				continue;
			}

			// Pick the best source: prefer regular drops over boss drops,
			// prefer higher drop rates, prefer shops over rare drops
			var bestSource = PickBestSource(missing.Sources);
			switch (bestSource.Type)
			{
				case ItemSourceType.NPCShop:
					buyItems.Add(MakeActionItem(missing, bestSource));
					break;

				case ItemSourceType.BossDrop:
					string bossKey = bestSource.SourceEntityId > 0
						? Lang.GetNPCNameValue(bestSource.SourceEntityId)
						: "Unknown Boss";
					if (!bossItems.ContainsKey(bossKey))
						bossItems[bossKey] = ([], bestSource);
					bossItems[bossKey].items.Add(MakeActionItem(missing, bestSource));
					break;

				case ItemSourceType.Event:
					string eventKey = Common.Systems.ItemSourceSystem.GetNPCEventName(bestSource.SourceEntityId)
						?? bestSource.Description;
					if (!eventItems.ContainsKey(eventKey))
						eventItems[eventKey] = [];
					eventItems[eventKey].Add(MakeActionItem(missing, bestSource));
					break;

				case ItemSourceType.NPCDrop:
					// Group by the NPC's biome/location (enriched with time/event from bestiary)
					string location = BiomeMapper.GetEnrichedLocation(bestSource.SourceEntityId);
					if (!farmByLocation.ContainsKey(location))
						farmByLocation[location] = [];
					farmByLocation[location].Add(MakeActionItem(missing, bestSource));
					break;

				case ItemSourceType.ChestLoot:
				case ItemSourceType.Gathering:
				case ItemSourceType.Catching:
				case ItemSourceType.Shimmer:
					exploreItems.Add(MakeActionItem(missing, bestSource));
					break;

				case ItemSourceType.Fishing:
					string fishLocation = bestSource.Description;
					if (!farmByLocation.ContainsKey(fishLocation))
						farmByLocation[fishLocation] = [];
					farmByLocation[fishLocation].Add(MakeActionItem(missing, bestSource));
					break;

				default:
					unknownItems.Add(MakeActionItem(missing, bestSource));
					break;
			}
		}

		// Build sorted action groups
		var result = new List<ActionGroup>();

		if (craftNowItems.Count > 0)
		{
			result.Add(new ActionGroup
			{
				Type = ActionType.CraftNow,
				Heading = "Craft Now",
				Description = $"{craftNowItems.Count} item(s) ready to craft!",
				Items = craftNowItems,
				SubPriority = 0
			});
		}

		if (buyItems.Count > 0)
		{
			result.Add(new ActionGroup
			{
				Type = ActionType.Buy,
				Heading = "Buy from NPCs",
				Description = $"{buyItems.Count} item(s) available in shops",
				Items = buyItems,
				SubPriority = 0
			});
		}

		// Farm groups sorted by lowest total effort (easiest locations first)
		foreach (var (location, items) in farmByLocation.OrderBy(kv =>
		{
			if (effortAnalyzer == null) return -kv.Value.Count; // fallback: most items
			float totalEffort = 0f;
			foreach (var item in kv.Value)
				totalEffort += effortAnalyzer.GetEffort(item.ItemType).Effective;
			return totalEffort;
		}))
		{
			float locEffort = 0f;
			if (effortAnalyzer != null)
				foreach (var item in items)
					locEffort += effortAnalyzer.GetEffort(item.ItemType).Effective;

			result.Add(new ActionGroup
			{
				Type = ActionType.Farm,
				Heading = $"Farm: {location}",
				Description = effortAnalyzer != null
					? $"{items.Count} item(s), ~{locEffort:F0} effort"
					: $"{items.Count} item(s) obtainable here",
				Items = items,
				SubPriority = -(int)(locEffort > 0 ? 1000f / locEffort : items.Count)
			});
		}

		foreach (var (eventName, items) in eventItems)
		{
			string triggerHint = BiomeMapper.GetEventTriggerHint(eventName);
			result.Add(new ActionGroup
			{
				Type = ActionType.Event,
				Heading = $"Event: {eventName}",
				Description = triggerHint ?? $"{items.Count} item(s) from this event",
				Items = items,
				SubPriority = 0
			});
		}

		foreach (var (bossName, (items, source)) in bossItems)
		{
			result.Add(new ActionGroup
			{
				Type = ActionType.Boss,
				Heading = $"Defeat: {bossName}",
				Description = GetBossSummonHint(source.SourceEntityId),
				Items = items,
				SubPriority = 0
			});
		}

		if (exploreItems.Count > 0)
		{
			result.Add(new ActionGroup
			{
				Type = ActionType.Explore,
				Heading = "Explore & Find",
				Description = $"{exploreItems.Count} item(s) from chests/world",
				Items = exploreItems,
				SubPriority = 0
			});
		}

		if (unknownItems.Count > 0)
		{
			result.Add(new ActionGroup
			{
				Type = ActionType.Unknown,
				Heading = "Other",
				Description = $"{unknownItems.Count} item(s) with unknown source",
				Items = unknownItems,
				SubPriority = 0
			});
		}

		// Sort: by ActionType (enum order = priority), then SubPriority
		result.Sort((a, b) =>
		{
			int typeComp = a.Type.CompareTo(b.Type);
			return typeComp != 0 ? typeComp : a.SubPriority.CompareTo(b.SubPriority);
		});

		return result;
	}

	private static ActionItem MakeActionItem(MergedNeed missing, ItemSource source)
	{
		string dropRate = source.DropRate > 0 && source.DropRate < 1f
			? $"1/{(int)(1f / source.DropRate)}"
			: source.DropRate >= 1f ? "100%" : null;

		return new ActionItem
		{
			ItemType = missing.ItemType,
			QuantityNeeded = missing.TotalNeeded,
			QuantityOwned = missing.Owned,
			HowToGet = source.Description,
			DropRateText = dropRate,
			SourceEntityId = source.SourceEntityId,
			Source = source,
			AllSources = missing.Sources,
			IsEquipped = missing.IsEquipped,
			GoalAttribution = FormatGoalAttribution(missing.ForGoals)
		};
	}

	private static ActionItem MakeActionItem(MergedNeed missing, string description)
	{
		return new ActionItem
		{
			ItemType = missing.ItemType,
			QuantityNeeded = missing.TotalNeeded,
			QuantityOwned = missing.Owned,
			HowToGet = description,
			AllSources = missing.Sources,
			IsEquipped = missing.IsEquipped,
			GoalAttribution = FormatGoalAttribution(missing.ForGoals)
		};
	}

	private static string FormatGoalAttribution(List<int> forGoals)
	{
		if (forGoals == null || forGoals.Count <= 1)
			return null;
		var names = new List<string>();
		foreach (int goalType in forGoals)
		{
			string name = Lang.GetItemNameValue(goalType);
			if (!string.IsNullOrEmpty(name))
				names.Add(name);
		}
		return names.Count > 1 ? $"For: {string.Join(", ", names)}" : null;
	}

	private static ItemSource PickBestSource(List<ItemSource> sources) =>
		ItemSource.PickBest(sources);

	/// <summary>
	/// Checks if an item can be crafted RIGHT NOW — meaning the resolver found it as
	/// a CraftNode and all its leaf materials are owned.
	/// </summary>
	/// <summary>
	/// Builds a cache of craft steps by item type from all goals' craft orders.
	/// Replaces 3 separate O(n×m) lookups with O(1) dictionary access.
	/// </summary>
	private static Dictionary<int, CraftStep> BuildCraftStepCache(IReadOnlyList<PinnedGoal> goals)
	{
		var cache = new Dictionary<int, CraftStep>();
		foreach (var goal in goals)
		{
			if (goal.Result?.CraftOrder == null) continue;
			foreach (var step in goal.Result.CraftOrder)
				cache.TryAdd(step.ItemType, step);
		}
		return cache;
	}

	private static bool IsSubCraftable(int itemType, Dictionary<int, CraftStep> craftCache)
	{
		if (!craftCache.TryGetValue(itemType, out var step))
			return false;
		foreach (var input in step.Inputs)
			if (input.Source == CraftStepInputSource.NeedToAcquire)
				return false;
		return true;
	}

	private static string GetCraftingStation(int itemType, Dictionary<int, CraftStep> craftCache)
	{
		if (!craftCache.TryGetValue(itemType, out var step) || step.RequiredTiles == null)
			return null;
		var names = new List<string>();
		foreach (int tileId in step.RequiredTiles)
		{
			if (tileId < 0) continue;
			string name = Lang.GetMapObjectName(Terraria.Map.MapHelper.TileToLookup(tileId, 0));
			if (!string.IsNullOrEmpty(name))
				names.Add(name);
		}
		return names.Count > 0 ? string.Join(" + ", names) : null;
	}

	private static List<(int itemType, int quantity)> GetCraftIngredients(int itemType, Dictionary<int, CraftStep> craftCache)
	{
		if (!craftCache.TryGetValue(itemType, out var step))
			return null;
		var result = new List<(int, int)>();
		foreach (var input in step.Inputs)
			result.Add((input.ItemType, input.Quantity));
		return result;
	}

	private static string GetBossSummonHint(int npcId)
	{
		// Try Boss Checklist data first (covers modded bosses)
		var boss = Content.Progression.BossDatabase.GetByNpcType(npcId);
		if (boss != null)
		{
			// Prefer Boss Checklist's own spawnInfo hint if detailed
			if (!string.IsNullOrEmpty(boss.SummonHint) && boss.SummonHint != "Check wiki for summoning")
				return boss.SummonHint;

			// Generate from spawnItems if available
			if (boss.SummonItemIds.Count > 0)
			{
				var names = boss.SummonItemIds
					.Select(id => Lang.GetItemNameValue(id))
					.Where(n => !string.IsNullOrEmpty(n))
					.Take(2);
				string joined = string.Join(" or ", names);
				if (!string.IsNullOrEmpty(joined))
					return $"Use {joined}";
			}
		}

		// Hardcoded fallback for vanilla bosses
		return npcId switch
		{
			NPCID.EyeofCthulhu => "Use Suspicious Looking Eye at night",
			NPCID.EaterofWorldsHead => "Break 3 Shadow Orbs or use Worm Food",
			NPCID.BrainofCthulhu => "Break 3 Crimson Hearts or use Bloody Spine",
			NPCID.KingSlime => "Use Slime Crown",
			NPCID.QueenBee => "Break Bee Larva in Jungle Hive",
			NPCID.SkeletronHead => "Talk to Old Man at Dungeon entrance at night",
			NPCID.WallofFlesh => "Throw Guide Voodoo Doll in lava (Underworld)",
			NPCID.TheDestroyer => "Use Mechanical Worm at night (Hardmode)",
			NPCID.SkeletronPrime => "Use Mechanical Skull at night (Hardmode)",
			NPCID.Retinazer or NPCID.Spazmatism => "Use Mechanical Eye at night (Hardmode)",
			NPCID.Plantera => "Break Plantera's Bulb (Underground Jungle, post-Mech)",
			NPCID.Golem => "Use Lihzahrd Power Cell at Lihzahrd Altar",
			NPCID.DukeFishron => "Fish with Truffle Worm in Ocean",
			NPCID.CultistBoss => "Defeat Cultists at Dungeon entrance",
			NPCID.MoonLordCore => "Defeat Lunar Pillars or use Celestial Sigil",
			NPCID.QueenSlimeBoss => "Use Gelatin Crystal in Hallow",
			NPCID.HallowBoss => "Use Prismatic Lacewing in Hallow at night",
			NPCID.Deerclops => "Use Deer Thing in Snow biome",
			_ => "Check wiki for summoning requirements"
		};
	}

	/// <summary>
	/// Merged view of a material need across multiple goals.
	/// Holds the core material data plus goal attribution.
	/// </summary>
	private class MergedNeed
	{
		public int ItemType;
		public int TotalNeeded;
		public int Owned;
		public List<ItemSource> Sources;
		public bool IsCraftable;
		public bool IsEquipped;
		public List<int> ForGoals = [];
	}
}
