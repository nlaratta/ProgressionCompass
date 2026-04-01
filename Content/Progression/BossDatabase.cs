using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ProgressionCompass.Common.Systems;

namespace ProgressionCompass.Content.Progression;

/// <summary>
/// Provides boss progression data. Integrates with Boss Checklist if installed,
/// otherwise falls back to hardcoded vanilla data + modded boss discovery.
/// </summary>
public static class BossDatabase
{
	/// <summary>True if Boss Checklist mod is providing the data.</summary>
	public static bool UsingBossChecklist { get; private set; }

	private static Dictionary<int, BossInfo> _bossByNpcType;

	/// <summary>Looks up boss info by NPC type. Populated after GetNextBosses runs.</summary>
	public static BossInfo GetByNpcType(int npcType) => _bossByNpcType?.GetValueOrDefault(npcType);

	/// <summary>
	/// Returns the next undefeated bosses the player should fight,
	/// sorted by progression order.
	/// </summary>
	public static List<BossInfo> GetNextBosses(Tier currentTier, ItemSourceSystem sourceSystem, int maxCount = 3)
	{
		List<BossInfo> allBosses;

		UsingBossChecklist = false;
		if (ModLoader.TryGetMod("BossChecklist", out Mod bossChecklist))
		{
			try
			{
				allBosses = GetBossesFromBossChecklist(bossChecklist, sourceSystem);
				if (allBosses.Count > 0)
					UsingBossChecklist = true;
				else
				{
					allBosses = GetVanillaBosses();
					AddModdedBosses(allBosses, sourceSystem);
				}
			}
			catch
			{
				allBosses = GetVanillaBosses();
				AddModdedBosses(allBosses, sourceSystem);
			}
		}
		else
		{
			allBosses = GetVanillaBosses();
			AddModdedBosses(allBosses, sourceSystem);
		}

		// Cache all bosses for lookup by NPC type
		_bossByNpcType = [];
		foreach (var boss in allBosses)
			_bossByNpcType.TryAdd(boss.NpcType, boss);

		// Filter to undefeated bosses near current tier
		var next = new List<BossInfo>();
		foreach (var boss in allBosses)
		{
			if (boss.IsDefeated)
				continue;
			if (boss.RequiredTier > currentTier + 2)
				continue;
			next.Add(boss);
			if (next.Count >= maxCount)
				break;
		}

		return next;
	}

	// ── Boss Checklist Integration ──────────────────────────

	private static List<BossInfo> GetBossesFromBossChecklist(Mod bossChecklist, ItemSourceSystem sourceSystem)
	{
		var results = new List<BossInfo>();
		try
		{
			// Boss Checklist Mod.Call API requires (string, Mod, string apiVersion)
			var callerMod = ProgressionCompassMod.Instance;
			var data = bossChecklist.Call("GetBossInfoDictionary", callerMod, "1.1")
				as Dictionary<string, Dictionary<string, object>>;

			if (data == null || data.Count == 0)
				return []; // Empty = fallback to vanilla

			foreach (var (key, info) in data)
			{
				try
				{
					// Only include actual bosses, not minibosses or events
					if (info.TryGetValue("isBoss", out var isBossObj) && isBossObj is bool isBoss && !isBoss)
						continue;

					string name = info.GetValueOrDefault("displayName") as string ?? key;
					float progression = info.TryGetValue("progression", out var progObj) && progObj is float f ? f : 0f;

					// "downed" is a Func<bool>, not a bool!
					bool downed = false;
					if (info.TryGetValue("downed", out var downedObj) && downedObj is Func<bool> downedFunc)
						downed = downedFunc();

					int npcType = 0;
					if (info.TryGetValue("npcIDs", out var npcObj) && npcObj is List<int> npcIds && npcIds.Count > 0)
						npcType = npcIds[0];

					Tier tier = ProgressionFromBCValue(progression);

					// Summon info: try Boss Checklist's spawnInfo, fall back to our hardcoded hints
					string summonHint = null;
					if (info.TryGetValue("spawnInfo", out var spawnObj) && spawnObj is Func<Terraria.Localization.LocalizedText> spawnFunc)
					{
						try { summonHint = spawnFunc()?.Value; } catch { }
					}
					summonHint ??= GetVanillaSummonHint(npcType) ?? "Check wiki for summoning";

					string location = GetVanillaLocation(npcType) ?? "";
					string unlocks = GetVanillaUnlocks(npcType) ?? "";

					// Get key drops from Boss Checklist loot data
					var keyDrops = new List<int>();
					if (info.TryGetValue("loot", out var lootObj) && lootObj is List<int> loot)
					{
						for (int i = 0; i < loot.Count && keyDrops.Count < 3; i++)
							keyDrops.Add(loot[i]);
					}

					// Summon items
					var summonItems = new List<int>();
					if (info.TryGetValue("spawnItems", out var spawnItemsObj) && spawnItemsObj is List<int> spawnIds)
						summonItems.AddRange(spawnIds);

					// Treasure bag
					int treasureBag = -1;
					if (info.TryGetValue("treasureBag", out var bagObj) && bagObj is int bagId && bagId > 0)
						treasureBag = bagId;

					results.Add(new BossInfo
					{
						NpcType = npcType,
						Name = name,
						SummonHint = summonHint,
						Location = location,
						Unlocks = unlocks,
						IsDefeated = downed,
						RequiredTier = tier,
						IsOptional = IsOptionalBoss(npcType),
						ProgressionOrder = progression,
						KeyDropItemTypes = keyDrops,
						SummonItemIds = summonItems,
						TreasureBagItemType = treasureBag
					});
				}
				catch (Exception ex)
				{
					Common.Log.Warn($"Failed to parse boss entry: {ex.Message}");
				}
			}

			results.Sort((a, b) => a.ProgressionOrder.CompareTo(b.ProgressionOrder));
		}
		catch
		{
			return [];
		}

		return results;
	}

	private static Tier ProgressionFromBCValue(float progression) => progression switch
	{
		< 2f => Tier.PreBoss,
		< 4f => Tier.PostEyeOrKing,
		< 6f => Tier.PostEvilBoss,
		< 7f => Tier.PostSkeletron,
		< 8f => Tier.PreHardmodeLate,
		< 10f => Tier.EarlyHardmode,
		< 12f => Tier.PostFirstMech,
		< 13f => Tier.PostAllMechs,
		< 15f => Tier.PostPlantera,
		< 17f => Tier.PostGolem,
		< 18f => Tier.PostCultist,
		_ => Tier.PostMoonLord
	};

	// ── Vanilla Boss Data ───────────────────────────────────

	private static List<BossInfo> GetVanillaBosses()
	{
		return
		[
			MakeBoss(NPCID.KingSlime, "King Slime", "Use Slime Crown",
				"Surface", "Ninja vanity set", Tier.PreBoss, true, 1f),
			MakeBoss(NPCID.EyeofCthulhu, "Eye of Cthulhu", "Use Suspicious Looking Eye at night",
				"Surface (night)", "Demonite/Crimtane ore", Tier.PreBoss, false, 2f),
			MakeBoss(NPCID.EaterofWorldsHead, "Eater of Worlds", "Break 3 Shadow Orbs or use Worm Food",
				"Corruption", "Shadow Scale, Meteorite spawns", Tier.PostEyeOrKing, false, 3f),
			MakeBoss(NPCID.BrainofCthulhu, "Brain of Cthulhu", "Break 3 Crimson Hearts or use Bloody Spine",
				"Crimson", "Tissue Sample, Meteorite spawns", Tier.PostEyeOrKing, false, 3f),
			MakeBoss(NPCID.QueenBee, "Queen Bee", "Break Bee Larva in Jungle Hive",
				"Jungle Hive", "Bee weapons, Witch Doctor NPC", Tier.PostEvilBoss, true, 4f),
			MakeBoss(NPCID.SkeletronHead, "Skeletron", "Talk to Old Man at Dungeon at night",
				"Dungeon entrance", "Dungeon access", Tier.PostEvilBoss, false, 5f),
			MakeBoss(NPCID.Deerclops, "Deerclops", "Use Deer Thing in Snow biome",
				"Snow biome", "Unique weapons", Tier.PostSkeletron, true, 5.5f),
			MakeBoss(NPCID.WallofFlesh, "Wall of Flesh", "Throw Guide Voodoo Doll in lava",
				"Underworld", "HARDMODE, Pwnhammer", Tier.PreHardmodeLate, false, 6f),
			MakeBoss(NPCID.QueenSlimeBoss, "Queen Slime", "Use Gelatin Crystal in Hallow",
				"Hallow", "Crystal Assassin set", Tier.EarlyHardmode, true, 8f),
			MakeBoss(NPCID.TheDestroyer, "The Destroyer", "Use Mechanical Worm at night",
				"Surface (night)", "Hallowed Bars, Souls of Might", Tier.EarlyHardmode, false, 9f),
			MakeBoss(NPCID.Retinazer, "The Twins", "Use Mechanical Eye at night",
				"Surface (night)", "Hallowed Bars, Souls of Sight", Tier.EarlyHardmode, false, 9f),
			MakeBoss(NPCID.SkeletronPrime, "Skeletron Prime", "Use Mechanical Skull at night",
				"Surface (night)", "Hallowed Bars, Souls of Fright", Tier.EarlyHardmode, false, 9f),
			MakeBoss(NPCID.Plantera, "Plantera", "Break Plantera's Bulb (post-Mech)",
				"Underground Jungle", "Temple access, HM Dungeon enemies", Tier.PostAllMechs, false, 12f),
			MakeBoss(NPCID.Golem, "Golem", "Use Lihzahrd Power Cell at Lihzahrd Altar",
				"Jungle Temple", "Picksaw, Beetle Husk", Tier.PostPlantera, false, 13f),
			MakeBoss(NPCID.HallowBoss, "Empress of Light", "Use Prismatic Lacewing in Hallow at night",
				"Hallow (night)", "Terraprisma (daytime), weapons", Tier.PostPlantera, true, 14f),
			MakeBoss(NPCID.DukeFishron, "Duke Fishron", "Fish with Truffle Worm in Ocean",
				"Ocean", "Best pre-ML weapons + wings", Tier.PostPlantera, true, 14f),
			MakeBoss(NPCID.CultistBoss, "Lunatic Cultist", "Defeat Cultists at Dungeon entrance",
				"Dungeon entrance", "Lunar Events / Pillars", Tier.PostGolem, false, 16f),
			MakeBoss(NPCID.MoonLordCore, "Moon Lord", "Defeat Lunar Pillars or use Celestial Sigil",
				"After Lunar Pillars", "Luminite, endgame gear", Tier.PostCultist, false, 18f),
		];
	}

	private static BossInfo MakeBoss(int npcType, string name, string summon,
		string location, string unlocks, Tier tier, bool optional, float order)
	{
		return new BossInfo
		{
			NpcType = npcType,
			Name = name,
			SummonHint = summon,
			Location = location,
			Unlocks = unlocks,
			IsDefeated = IsVanillaBossDefeated(npcType),
			RequiredTier = tier,
			IsOptional = optional,
			ProgressionOrder = order
		};
	}

	private static bool IsVanillaBossDefeated(int npcType) => npcType switch
	{
		NPCID.KingSlime => NPC.downedSlimeKing,
		NPCID.EyeofCthulhu => NPC.downedBoss1,
		NPCID.EaterofWorldsHead => NPC.downedBoss2,
		NPCID.BrainofCthulhu => NPC.downedBoss2,
		NPCID.QueenBee => NPC.downedQueenBee,
		NPCID.SkeletronHead => NPC.downedBoss3,
		NPCID.Deerclops => NPC.downedDeerclops,
		NPCID.WallofFlesh => Main.hardMode,
		NPCID.QueenSlimeBoss => NPC.downedQueenSlime,
		NPCID.TheDestroyer => NPC.downedMechBoss1,
		NPCID.Retinazer or NPCID.Spazmatism => NPC.downedMechBoss2,
		NPCID.SkeletronPrime => NPC.downedMechBoss3,
		NPCID.Plantera => NPC.downedPlantBoss,
		NPCID.Golem => NPC.downedGolemBoss,
		NPCID.HallowBoss => NPC.downedEmpressOfLight,
		NPCID.DukeFishron => NPC.downedFishron,
		NPCID.CultistBoss => NPC.downedAncientCultist,
		NPCID.MoonLordCore => NPC.downedMoonlord,
		_ => false
	};

	// ── Modded Boss Discovery ───────────────────────────────

	private static void AddModdedBosses(List<BossInfo> bosses, ItemSourceSystem sourceSystem)
	{
		var vanillaTypes = new HashSet<int>();
		foreach (var b in bosses)
			vanillaTypes.Add(b.NpcType);

		for (int npcId = NPCID.Count; npcId < NPCLoader.NPCCount; npcId++)
		{
			try
			{
				bool isBoss = false;
				if (npcId < NPCID.Sets.ShouldBeCountedAsBoss.Length)
					isBoss = NPCID.Sets.ShouldBeCountedAsBoss[npcId];

				if (!isBoss)
				{
					var testNpc = new NPC();
					testNpc.SetDefaults(npcId);
					isBoss = testNpc.boss;
				}

				if (!isBoss || vanillaTypes.Contains(npcId))
					continue;

				string name;
				try { name = Lang.GetNPCNameValue(npcId); }
				catch { name = $"Boss #{npcId}"; }

				// Estimate tier from the boss's drops
				Tier tier = EstimateBossTier(npcId, sourceSystem);

				bosses.Add(new BossInfo
				{
					NpcType = npcId,
					Name = name,
					SummonHint = "Check wiki for summoning",
					Location = "",
					Unlocks = "",
					IsDefeated = false, // Can't reliably detect for modded bosses
					RequiredTier = tier,
					IsOptional = false,
					ProgressionOrder = (float)tier * 2f + 0.5f // Interleave with vanilla
				});
			}
			catch { }
		}

		bosses.Sort((a, b) => a.ProgressionOrder.CompareTo(b.ProgressionOrder));
	}

	private static Tier EstimateBossTier(int npcId, ItemSourceSystem sourceSystem)
	{
		// Check drop rarity to estimate tier
		var sources = sourceSystem?.GetSources(npcId) ?? [];
		float maxRarity = 0;
		foreach (var src in sources)
		{
			if (src.Type != Graph.ItemSourceType.BossDrop) continue;
			var item = Terraria.ID.ContentSamples.ItemsByType.GetValueOrDefault(src.SourceEntityId);
			if (item != null && item.rare > maxRarity)
				maxRarity = item.rare;
		}

		return ProgressionTier.EstimateItemTier((int)maxRarity > 0 ? (int)maxRarity : 0);
	}

	// ── Shared Helpers ──────────────────────────────────────

	private static bool IsOptionalBoss(int npcType) => npcType switch
	{
		NPCID.KingSlime or NPCID.QueenBee or NPCID.Deerclops
			or NPCID.QueenSlimeBoss or NPCID.HallowBoss or NPCID.DukeFishron => true,
		_ => false
	};

	private static string GetVanillaSummonHint(int npcType) => npcType switch
	{
		NPCID.KingSlime => "Use Slime Crown",
		NPCID.EyeofCthulhu => "Use Suspicious Looking Eye at night",
		NPCID.EaterofWorldsHead => "Break 3 Shadow Orbs or use Worm Food",
		NPCID.BrainofCthulhu => "Break 3 Crimson Hearts or use Bloody Spine",
		NPCID.QueenBee => "Break Bee Larva in Jungle Hive",
		NPCID.SkeletronHead => "Talk to Old Man at Dungeon at night",
		NPCID.Deerclops => "Use Deer Thing in Snow biome",
		NPCID.WallofFlesh => "Throw Guide Voodoo Doll in lava",
		NPCID.QueenSlimeBoss => "Use Gelatin Crystal in Hallow",
		NPCID.TheDestroyer => "Use Mechanical Worm at night",
		NPCID.Retinazer or NPCID.Spazmatism => "Use Mechanical Eye at night",
		NPCID.SkeletronPrime => "Use Mechanical Skull at night",
		NPCID.Plantera => "Break Plantera's Bulb (post-Mech)",
		NPCID.Golem => "Use Lihzahrd Power Cell at Altar",
		NPCID.HallowBoss => "Use Prismatic Lacewing in Hallow at night",
		NPCID.DukeFishron => "Fish with Truffle Worm in Ocean",
		NPCID.CultistBoss => "Defeat Cultists at Dungeon entrance",
		NPCID.MoonLordCore => "Defeat Lunar Pillars or use Celestial Sigil",
		_ => null
	};

	private static string GetVanillaLocation(int npcType) => npcType switch
	{
		NPCID.KingSlime => "Surface",
		NPCID.EyeofCthulhu => "Surface (night)",
		NPCID.EaterofWorldsHead => "Corruption",
		NPCID.BrainofCthulhu => "Crimson",
		NPCID.QueenBee => "Jungle Hive",
		NPCID.SkeletronHead => "Dungeon entrance",
		NPCID.Deerclops => "Snow biome",
		NPCID.WallofFlesh => "Underworld",
		NPCID.QueenSlimeBoss => "Hallow",
		NPCID.TheDestroyer or NPCID.Retinazer or NPCID.SkeletronPrime => "Surface (night)",
		NPCID.Plantera => "Underground Jungle",
		NPCID.Golem => "Jungle Temple",
		NPCID.HallowBoss => "Hallow (night)",
		NPCID.DukeFishron => "Ocean",
		NPCID.CultistBoss => "Dungeon entrance",
		NPCID.MoonLordCore => "After Lunar Pillars",
		_ => null
	};

	private static string GetVanillaUnlocks(int npcType) => npcType switch
	{
		NPCID.EyeofCthulhu => "Demonite/Crimtane ore",
		NPCID.EaterofWorldsHead or NPCID.BrainofCthulhu => "Meteorite spawns",
		NPCID.QueenBee => "Bee weapons, Witch Doctor NPC",
		NPCID.SkeletronHead => "Dungeon access",
		NPCID.WallofFlesh => "HARDMODE, Pwnhammer",
		NPCID.TheDestroyer or NPCID.Retinazer or NPCID.SkeletronPrime => "Hallowed Bars",
		NPCID.Plantera => "Temple access, HM Dungeon",
		NPCID.Golem => "Picksaw, Beetle Husk",
		NPCID.CultistBoss => "Lunar Events / Pillars",
		NPCID.MoonLordCore => "Luminite, endgame gear",
		_ => null
	};
}
