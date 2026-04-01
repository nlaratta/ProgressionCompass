using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using CraftPathOptimizer.Common.Systems;
using CraftPathOptimizer.Content.Graph;

namespace CraftPathOptimizer.Content.Tracking;

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
		ItemSourceSystem sourceSystem)
	{
		// Merge all missing materials across all goals
		var allMissing = new Dictionary<int, MissingItem>();

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
					allMissing[mat.ItemType] = new MissingItem
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

		foreach (var (itemType, missing) in allMissing)
		{
			// Check if craftable with current inventory (all sub-ingredients owned)
			if (missing.IsCraftable && IsSubCraftable(itemType, goals))
			{
				string stationName = GetCraftingStation(itemType, goals);
				var ingredients = GetCraftIngredients(itemType, goals);
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
					string eventKey = bestSource.Description;
					if (!eventItems.ContainsKey(eventKey))
						eventItems[eventKey] = [];
					eventItems[eventKey].Add(MakeActionItem(missing, bestSource));
					break;

				case ItemSourceType.NPCDrop:
					// Group by the NPC's biome/location
					string location = GetNPCLocation(bestSource.SourceEntityId);
					if (!farmByLocation.ContainsKey(location))
						farmByLocation[location] = [];
					farmByLocation[location].Add(MakeActionItem(missing, bestSource));
					break;

				case ItemSourceType.ChestLoot:
				case ItemSourceType.Gathering:
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

		// Farm groups sorted by most items obtainable (best use of time)
		foreach (var (location, items) in farmByLocation.OrderByDescending(kv => kv.Value.Count))
		{
			result.Add(new ActionGroup
			{
				Type = ActionType.Farm,
				Heading = $"Farm: {location}",
				Description = $"{items.Count} item(s) obtainable here",
				Items = items,
				SubPriority = -items.Count // more items = higher priority
			});
		}

		foreach (var (eventName, items) in eventItems)
		{
			string triggerHint = GetEventTriggerHint(eventName);
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

	private static ActionItem MakeActionItem(MissingItem missing, ItemSource source)
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

	private static ActionItem MakeActionItem(MissingItem missing, string description)
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
	private static bool IsSubCraftable(int itemType, IReadOnlyList<PinnedGoal> goals)
	{
		foreach (var goal in goals)
		{
			if (goal.Result?.CraftOrder == null)
				continue;
			foreach (var step in goal.Result.CraftOrder)
			{
				if (step.ItemType != itemType)
					continue;
				// Check if all inputs are owned or from previous (completed) steps
				bool allReady = true;
				foreach (var input in step.Inputs)
				{
					if (input.Source == CraftStepInputSource.NeedToAcquire)
					{
						allReady = false;
						break;
					}
				}
				if (allReady)
					return true;
			}
		}
		return false;
	}

	/// <summary>Gets the crafting station name for an item from its resolved craft order.</summary>
	private static string GetCraftingStation(int itemType, IReadOnlyList<PinnedGoal> goals)
	{
		foreach (var goal in goals)
		{
			if (goal.Result?.CraftOrder == null)
				continue;
			foreach (var step in goal.Result.CraftOrder)
			{
				if (step.ItemType != itemType || step.RequiredTiles == null)
					continue;
				var names = new List<string>();
				foreach (int tileId in step.RequiredTiles)
				{
					if (tileId < 0) continue;
					string name = Lang.GetMapObjectName(
						Terraria.Map.MapHelper.TileToLookup(tileId, 0));
					if (!string.IsNullOrEmpty(name))
						names.Add(name);
				}
				if (names.Count > 0)
					return string.Join(" + ", names);
			}
		}
		return null;
	}

	/// <summary>Gets the ingredients for a craftable item from its resolved craft order.</summary>
	private static List<(int itemType, int quantity)> GetCraftIngredients(int itemType, IReadOnlyList<PinnedGoal> goals)
	{
		foreach (var goal in goals)
		{
			if (goal.Result?.CraftOrder == null)
				continue;
			foreach (var step in goal.Result.CraftOrder)
			{
				if (step.ItemType != itemType)
					continue;
				var result = new List<(int, int)>();
				foreach (var input in step.Inputs)
					result.Add((input.ItemType, input.Quantity));
				return result;
			}
		}
		return null;
	}

	/// <summary>
	/// Rough biome classification for an NPC based on its type.
	/// This is a simplified heuristic — a full implementation would check
	/// NPC spawn conditions, but that data isn't easily accessible.
	/// </summary>
	private static string GetNPCLocation(int npcId)
	{
		if (npcId <= 0)
			return "Surface";

		// Use NPC bestiary info or known spawn conditions
		// For now, use a heuristic based on NPC name patterns and known IDs
		try
		{
			var npc = new NPC();
			npc.SetDefaults(npcId);

			// Check basic spawn info from NPC stats
			if (npc.boss)
				return "Boss Fight";

			string name = npc.FullName ?? npc.GivenOrTypeName ?? "";

			// Known biome mob patterns (covers most vanilla + common modded)
			if (ContainsAny(name, "Corrupt", "Eater", "Devourer", "Clinger"))
				return "Underground Corruption";
			if (ContainsAny(name, "Crimson", "Creeper", "Ichor", "Floaty"))
				return "Underground Crimson";
			if (ContainsAny(name, "Hallow", "Pixie", "Unicorn", "Gastropod", "Enchanted"))
				return "Underground Hallow";
			if (ContainsAny(name, "Jungle", "Hornet", "Moss", "Tortoise", "Arapaima"))
				return "Underground Jungle";
			if (ContainsAny(name, "Dungeon", "Skeleton", "Paladin", "Necromancer", "Ragged"))
				return "Dungeon";
			if (ContainsAny(name, "Ice", "Frost", "Viking", "Undead"))
				return "Ice Biome";
			if (ContainsAny(name, "Desert", "Mummy", "Lamia", "Sand"))
				return "Desert";
			if (ContainsAny(name, "Mushroom", "Fungi"))
				return "Mushroom Biome";
			if (ContainsAny(name, "Ocean", "Shark", "Crab"))
				return "Ocean";
			if (ContainsAny(name, "Pirate", "Goblin", "Martian", "Solar", "Nebula", "Vortex", "Stardust"))
				return "Event";
			if (ContainsAny(name, "Lava", "Hell", "Demon", "Bone Serpent", "Fire Imp"))
				return "Underworld";

			// Fallback: classify by defense/life as rough depth indicator
			if (npc.defense > 30 || npc.lifeMax > 400)
				return "Underground / Cavern";

			return "Surface / Underground";
		}
		catch
		{
			return "Unknown Location";
		}
	}

	private static bool ContainsAny(string text, params string[] keywords)
	{
		foreach (string kw in keywords)
			if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
				return true;
		return false;
	}

	private static string GetBossSummonHint(int npcId)
	{
		// Known boss summoning info for common bosses
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

	private static string GetEventTriggerHint(string eventDescription)
	{
		if (string.IsNullOrEmpty(eventDescription)) return null;
		string lower = eventDescription.ToLowerInvariant();

		if (lower.Contains("goblin")) return "Use Goblin Battle Standard, or random at 65% HP";
		if (lower.Contains("pirate")) return "Use Pirate Map, or random in Hardmode Ocean";
		if (lower.Contains("solar eclipse")) return "Use Solar Tablet, or random post-Mech Hardmode";
		if (lower.Contains("blood moon")) return "Random event at night (1/9 chance)";
		if (lower.Contains("frost moon")) return "Use Naughty Present at night";
		if (lower.Contains("pumpkin moon")) return "Use Pumpkin Moon Medallion at night";
		if (lower.Contains("martian")) return "Lure Martian Probe in Space layer";
		if (lower.Contains("frost legion")) return "Use Snow Globe";
		if (lower.Contains("old one")) return "Use Eternia Crystal at Eternia Crystal Stand";
		return null;
	}

	private class MissingItem
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
