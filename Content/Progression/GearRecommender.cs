using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using ProgressionCompass.Common.Systems;
using ProgressionCompass.Content.Graph;

namespace ProgressionCompass.Content.Progression;

public enum GearCategory
{
	Weapon,
	Armor,
	Accessory,
	Tool
}

public enum AcquisitionMethod
{
	Craftable,
	BossDrop,
	Shop,
	Event,
	MobDrop,
	Fishing,
	Exploration,
	Unknown
}

/// <summary>Functional category of an accessory based on equip slot.</summary>
public enum AccessorySlotCategory
{
	Wing,
	Shield,
	Balloon,
	Boots,
	Neck,
	Combat,   // handOnSlot — gloves, gauntlets
	Face,
	Back,
	Other     // no equip slot — invisible accessories
}

/// <summary>
/// A recommended gear upgrade: an item the player doesn't own that is
/// better than what they currently have and available at their tier.
/// </summary>
public enum Proximity
{
	CraftableNow = 0,
	AlmostReady = 1,  // 1-2 ingredients missing
	NeedsWork = 2,    // 3+ missing or non-craftable
	FarAway = 3       // many missing or high tier gap
}

public class GearRecommendation
{
	public int ItemType { get; init; }
	public GearCategory Category { get; init; }
	public string ImprovementText { get; init; }
	public Tier RequiredTier { get; init; }
	public float Score { get; init; }

	/// <summary>The item's actual base damage (0 for non-weapons).</summary>
	public int BaseDamage { get; init; }
	/// <summary>The item's theoretical DPS (0 for non-weapons).</summary>
	public float Dps { get; init; }

	/// <summary>How to obtain this item if not craftable.</summary>
	public string SourceHint { get; init; }
	/// <summary>All known sources for non-craftable items.</summary>
	public List<Graph.ItemSource> AllSources { get; init; }
	/// <summary>True if this item has a crafting recipe.</summary>
	public bool HasRecipe { get; init; }

	/// <summary>Primary way to obtain this item.</summary>
	public AcquisitionMethod Acquisition { get; init; }
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
		bool includeChests,
		WorldContext worldContext = default)
	{
		var stats = ComputePlayerStats(inventory, includeChests, currentArmorDefense);
		int bestMeleeDmg = stats.BestMeleeDmg;
		int bestRangedDmg = stats.BestRangedDmg;
		int bestMagicDmg = stats.BestMagicDmg;
		int bestSummonDmg = stats.BestSummonDmg;
		int currentDefense = stats.ArmorDefense;
		int bestPickPower = stats.BestPickPower;

		var ownedItems = new HashSet<int>();
		var equippedAccessories = new HashSet<int>();
		foreach (var (type, entry) in inventory)
		{
			if (entry.AvailableCount(includeChests) > 0)
			{
				ownedItems.Add(type);
				// Track owned accessories for marginal effect scoring
				var sampleItem = ContentSamples.ItemsByType.GetValueOrDefault(type);
				if (sampleItem is { accessory: true })
					equippedAccessories.Add(type);
			}
		}

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

			// World evil filter: skip items affiliated with the wrong evil
			if (!worldContext.IsAvailable(EvilAffiliationMap.Get(type)))
				continue;

			// Difficulty-gated items: skip Expert-only in Normal, Master-only in non-Master
			if (item.expertOnly && !worldContext.IsExpert)
				continue;
			if (item.masterOnly && !worldContext.IsMaster)
				continue;

			// Check if this item is available at the player's tier
			// Use rarity as a proxy (dynamic — works for modded items)
			Tier itemTier = ProgressionTier.EstimateItemTier(type);
			if (itemTier > currentTier + 1) // Allow peeking 1 tier ahead
				continue;

			// Score this item against current gear
			GearCategory category;
			float score;
			string improvement;
			int baseDamage = 0;
			float itemDps = 0;

			if (isWeapon)
			{
				category = GearCategory.Weapon;
				var result = ScoreWeapon(item, type, stats, bestMeleeDmg, bestRangedDmg, bestMagicDmg, bestSummonDmg);
				if (result == null) continue;
				(score, improvement, baseDamage, itemDps) = result.Value;
			}
			else if (isArmor)
			{
				category = GearCategory.Armor;
				var result = ScoreArmor(item, currentDefense);
				if (result == null) continue;
				(score, improvement) = result.Value;
			}
			else if (isTool)
			{
				category = GearCategory.Tool;
				var result = ScoreTool(item, bestPickPower);
				if (result == null) continue;
				(score, improvement) = result.Value;
			}
			else
			{
				category = GearCategory.Accessory;
				var result = ScoreAccessory(item, type, stats, equippedAccessories, ownedItems);
				if (result == null) continue;
				(score, improvement) = result.Value;
			}

			// Basic recipe check (for HasRecipe flag only — resolver handles the rest)
			bool hasRecipe = recipeIndex?.RecipesByResult?.ContainsKey(type) ?? false;

			// Source info for non-craftable items
			string sourceHint = null;
			List<Graph.ItemSource> allSources = null;
			AcquisitionMethod acquisition = hasRecipe ? AcquisitionMethod.Craftable : AcquisitionMethod.Unknown;

			if (!hasRecipe && sourceSystem != null)
			{
				allSources = sourceSystem.GetSources(type, worldContext);
				if (allSources.Count > 0)
				{
					var best = Graph.ItemSource.PickBest(allSources);
					sourceHint = best?.Description;
					acquisition = best?.Type switch
					{
						Graph.ItemSourceType.BossDrop => AcquisitionMethod.BossDrop,
						Graph.ItemSourceType.NPCShop => AcquisitionMethod.Shop,
						Graph.ItemSourceType.NPCDrop => AcquisitionMethod.MobDrop,
						Graph.ItemSourceType.Event => AcquisitionMethod.Event,
						Graph.ItemSourceType.Fishing => AcquisitionMethod.Fishing,
						Graph.ItemSourceType.ChestLoot => AcquisitionMethod.Exploration,
						Graph.ItemSourceType.BagDrop => AcquisitionMethod.BossDrop,
						Graph.ItemSourceType.Gathering => AcquisitionMethod.Exploration,
						_ => AcquisitionMethod.Unknown
					};
				}
			}

			results.Add(new GearRecommendation
			{
				ItemType = type,
				Category = category,
				ImprovementText = improvement,
				RequiredTier = itemTier,
				Score = score,
				BaseDamage = baseDamage,
				Dps = itemDps,
				SourceHint = sourceHint,
				AllSources = allSources,
				HasRecipe = hasRecipe,
				Acquisition = acquisition
			});
		}

		// Sort by score descending
		results.Sort((a, b) => b.Score.CompareTo(a.Score));

		return results;
	}

	// ── Category Scorers ────────────────────────────────────────

	private static (float score, string improvement, int baseDamage, float dps)? ScoreWeapon(
		Item item, int type, PlayerStats stats, int bestMelee, int bestRanged, int bestMagic, int bestSummon)
	{
		float itemDps = PlayerStats.CalculateDps(item);
		float currentBestDps = GetBestDpsForClass(item, stats);

		float slotCost = 1f;
		if (item.DamageType.CountsAsClass(DamageClass.Summon) || item.DamageType == DamageClass.SummonMeleeSpeed)
		{
			if (ItemID.Sets.StaffMinionSlotsRequired != null && type < ItemID.Sets.StaffMinionSlotsRequired.Length)
				slotCost = Math.Max(ItemID.Sets.StaffMinionSlotsRequired[type], 0.5f);
			if (item.sentry) itemDps *= 1.3f;
		}
		float effectiveDps = itemDps / slotCost;
		int bestDmg = GetBestDamageForClass(item, bestMelee, bestRanged, bestMagic, bestSummon);

		if (effectiveDps <= currentBestDps && item.damage <= bestDmg)
			return null;

		float dpsDelta = effectiveDps - currentBestDps;
		float score = Math.Max(dpsDelta, 0) + Math.Max(item.damage - bestDmg, 0) * ScoringConstants.WeaponRawDamageWeight;
		return (score, $"{item.damage} dmg, {(int)itemDps} DPS", item.damage, itemDps);
	}

	private static (float score, string improvement)? ScoreArmor(Item item, int currentDefense)
	{
		int avgPerPiece = currentDefense / 3;
		int delta = item.defense - avgPerPiece;
		bool hasClassBonus = ClassDetector.DetectArmorClass(item) != null;

		if (delta <= 0 && !hasClassBonus) return null;

		float score = Math.Max(delta, 0) * ScoringConstants.ArmorDefenseWeight;
		if (hasClassBonus) score += item.rare * ScoringConstants.ArmorClassBonusRarityWeight;
		string improvement = delta > 0 ? $"{item.defense} def (+{delta})" : $"{item.defense} def";
		return (score, improvement);
	}

	private static (float score, string improvement)? ScoreTool(Item item, int bestPickPower)
	{
		int delta = item.pick - bestPickPower;
		if (delta <= 0) return null;
		return (delta * ScoringConstants.ToolPickPowerWeight, $"{item.pick}% pick (+{delta})");
	}

	private static (float score, string improvement)? ScoreAccessory(
		Item item, int type, PlayerStats stats, HashSet<int> equippedAccessories, HashSet<int> ownedItems)
	{
		float score;
		string improvement;

		if (item.wingSlot > 0)
		{
			float wingScore = 0f;
			improvement = "Wings";
			try
			{
				if (item.wingSlot < Terraria.ID.ArmorIDs.Wing.Sets.Stats.Length)
				{
					var ws = Terraria.ID.ArmorIDs.Wing.Sets.Stats[item.wingSlot];
					float flyTimeDelta = ws.FlyTime - stats.WingFlyTime;
					float speedDelta = ws.AccRunSpeedOverride - stats.WingSpeed;
					if (flyTimeDelta <= 0 && speedDelta <= 0 && stats.WingItemType > 0)
						return null;
					wingScore = flyTimeDelta * 0.5f + speedDelta * 10f;
					if (ws.HasDownHoverStats) wingScore += ScoringConstants.WingHoverBonus;
					improvement = $"Wings: {ws.FlyTime / 60f:F1}s fly, {ws.AccRunSpeedOverride:F0} speed";
				}
			}
			catch { }
			score = wingScore + 20f;
		}
		else
		{
			var slotCategory = GetAccessoryCategory(item);
			string catPrefix = slotCategory switch
			{
				AccessorySlotCategory.Shield => "Shield: ",
				AccessorySlotCategory.Boots => "Boots: ",
				AccessorySlotCategory.Balloon => "Balloon: ",
				AccessorySlotCategory.Neck => "Neck: ",
				AccessorySlotCategory.Combat => "Combat: ",
				AccessorySlotCategory.Face => "Face: ",
				AccessorySlotCategory.Back => "Back: ",
				_ => ""
			};

			var effects = AccessoryEffectAnalyzer.Get(type);
			if (effects != null && effects.TotalScore > 0)
			{
				score = effects.TotalScore;
				string effectText = effects.GetImprovementText();
				improvement = effectText != null ? $"{catPrefix}{effectText}" : $"{catPrefix}Score: {(int)score}";
			}
			else
			{
				float directScore = 0f;
				var parts = new List<string>();
				if (item.defense > 0) { directScore += item.defense * 4f; parts.Add($"+{item.defense} def"); }
				if (item.lifeRegen > 0) { directScore += item.lifeRegen * 3f; parts.Add($"+{item.lifeRegen} regen"); }
				if (item.manaIncrease > 0) { directScore += item.manaIncrease * 0.5f; parts.Add($"+{item.manaIncrease} mana"); }

				float tooltipScore = ScoreAccessoryFromTooltip(item);
				score = directScore + tooltipScore;

				if (score > 0)
					improvement = parts.Count > 0
						? catPrefix + string.Join(", ", parts) + (tooltipScore > 0 ? $" (+{(int)tooltipScore})" : "")
						: $"{catPrefix}Score: {(int)score}";
				else
				{
					score = item.rare * 3f;
					improvement = $"{catPrefix}Tier {item.rare} accessory";
				}
			}
		}

		float marginal = AccessoryEffectAnalyzer.ComputeMarginalMultiplier(type, equippedAccessories);
		score *= marginal;
		if (marginal < 1f && HasSimilarAccessory(item, ownedItems))
			score *= 0.6f;

		return (score, improvement);
	}

	// ── Helpers ──────────────────────────────────────────────────

	private static int GetBestDamageForClass(Item item, int melee, int ranged, int magic, int summon)
	{
		if (item.DamageType == DamageClass.SummonMeleeSpeed) return summon;
		if (item.DamageType.CountsAsClass(DamageClass.Melee)) return melee;
		if (item.DamageType.CountsAsClass(DamageClass.Ranged)) return ranged;
		if (item.DamageType.CountsAsClass(DamageClass.Magic)) return magic;
		if (item.DamageType.CountsAsClass(DamageClass.Summon)) return summon;
		return Math.Max(Math.Max(melee, ranged), Math.Max(magic, summon));
	}

	private static float GetBestDpsForClass(Item item, PlayerStats stats)
	{
		if (item.DamageType == DamageClass.SummonMeleeSpeed) return stats.BestSummonDps;
		if (item.DamageType.CountsAsClass(DamageClass.Melee)) return stats.BestMeleeDps;
		if (item.DamageType.CountsAsClass(DamageClass.Ranged)) return stats.BestRangedDps;
		if (item.DamageType.CountsAsClass(DamageClass.Magic)) return stats.BestMagicDps;
		if (item.DamageType.CountsAsClass(DamageClass.Summon)) return stats.BestSummonDps;
		return Math.Max(Math.Max(stats.BestMeleeDps, stats.BestRangedDps),
			Math.Max(stats.BestMagicDps, stats.BestSummonDps));
	}

	/// <summary>
	/// Checks if the player already owns an accessory of similar type
	/// (boots vs boots, emblems vs emblems) to deprioritize duplicates.
	/// </summary>
	private static bool HasSimilarAccessory(Item candidate, HashSet<int> ownedItems)
	{
		string candName = (candidate.Name ?? "").ToLowerInvariant();

		// Extract category keywords from the candidate
		string[] typeKeys = ["boot", "wing", "shield", "emblem", "hook",
			"balloon", "horseshoe", "band", "charm", "cuff", "glove",
			"quiver", "scope", "cloak", "necklace"];

		foreach (string key in typeKeys)
		{
			if (!candName.Contains(key)) continue;

			// Check if any owned item shares this keyword
			foreach (int ownedType in ownedItems)
			{
				var owned = ContentSamples.ItemsByType.GetValueOrDefault(ownedType);
				if (owned == null || !owned.accessory) continue;
				string ownedName = (owned.Name ?? "").ToLowerInvariant();
				if (ownedName.Contains(key))
					return true;
			}
		}

		return false;
	}

	private static Tier EstimateItemTier(int itemType) =>
		ProgressionTier.EstimateItemTier(itemType);

	/// <summary>
	/// Computes a snapshot of the player's current best gear stats from inventory.
	/// Used by both the recommender (internally) and the Progression Command Center.
	/// </summary>
	public static PlayerStats ComputePlayerStats(
		Dictionary<int, InventoryEntry> inventory, bool includeChests, int armorDefense = 0)
	{
		int bestMelee = 0, bestRanged = 0, bestMagic = 0, bestSummon = 0;
		float bestMeleeDps = 0, bestRangedDps = 0, bestMagicDps = 0, bestSummonDps = 0;
		int bestPick = 0;
		int wingItem = 0;
		float wingFlyTime = 0, wingSpeed = 0;

		if (ContentSamples.ItemsByType == null)
			return new PlayerStats(0, 0, 0, 0, 0, 0, 0, 0, armorDefense, 0);

		foreach (var (type, entry) in inventory)
		{
			if (entry.AvailableCount(includeChests) <= 0)
				continue;

			var item = ContentSamples.ItemsByType.GetValueOrDefault(type);
			if (item == null) continue;

			if (item.damage > 0)
			{
				float dps = PlayerStats.CalculateDps(item);
				bool isWhip = item.DamageType == DamageClass.SummonMeleeSpeed;

				if (!isWhip && item.DamageType.CountsAsClass(DamageClass.Melee))
				{
					if (dps > bestMeleeDps) { bestMeleeDps = dps; bestMelee = item.damage; }
				}
				if (item.DamageType.CountsAsClass(DamageClass.Ranged))
				{
					if (dps > bestRangedDps) { bestRangedDps = dps; bestRanged = item.damage; }
				}
				if (item.DamageType.CountsAsClass(DamageClass.Magic))
				{
					if (dps > bestMagicDps) { bestMagicDps = dps; bestMagic = item.damage; }
				}
				if (item.DamageType.CountsAsClass(DamageClass.Summon) || isWhip)
				{
					if (dps > bestSummonDps) { bestSummonDps = dps; bestSummon = item.damage; }
				}
			}
			if (item.pick > bestPick)
				bestPick = item.pick;

			// Track best wings by fly time
			if (item.wingSlot > 0)
			{
				try
				{
					if (item.wingSlot < Terraria.ID.ArmorIDs.Wing.Sets.Stats.Length)
					{
						var ws = Terraria.ID.ArmorIDs.Wing.Sets.Stats[item.wingSlot];
						if (ws.FlyTime > wingFlyTime)
						{
							wingItem = type;
							wingFlyTime = ws.FlyTime;
							wingSpeed = ws.AccRunSpeedOverride;
						}
					}
				}
				catch { wingItem = type; }
			}
		}

		return new PlayerStats(
			bestMelee, bestRanged, bestMagic, bestSummon,
			bestMeleeDps, bestRangedDps, bestMagicDps, bestSummonDps,
			armorDefense, bestPick, wingItem, wingFlyTime, wingSpeed);
	}

	/// <summary>
	/// Parses accessory tooltip for numeric stat bonuses.
	/// Returns a composite score based on damage%, crit%, defense, mana, etc.
	/// </summary>
	public static float ScoreAccessoryFromTooltip(Item item)
	{
		if (item.ToolTip == null || item.ToolTip == Terraria.UI.ItemTooltip.None)
			return 0f;

		float score = 0f;
		try
		{
			for (int i = 0; i < item.ToolTip.Lines; i++)
			{
				string line = item.ToolTip.GetLine(i);
				if (string.IsNullOrEmpty(line)) continue;
				string lower = line.ToLowerInvariant();

				// Parse percentage bonuses: "X% increased/more damage/speed/crit"
				var pctMatch = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)%");
				if (pctMatch.Success)
				{
					int pct = int.Parse(pctMatch.Groups[1].Value);
					if (lower.Contains("damage")) score += pct * 3f;
					else if (lower.Contains("crit")) score += pct * 2.5f;
					else if (lower.Contains("speed")) score += pct * 2f;
					else if (lower.Contains("movement")) score += pct * 1f;
					else score += pct * 1f;
				}

				// Parse flat bonuses: "+X defense/mana/life"
				var flatMatch = System.Text.RegularExpressions.Regex.Match(line, @"\+?\s*(\d+)\s*(defense|mana|life|armor penetration)");
				if (flatMatch.Success)
				{
					int val = int.Parse(flatMatch.Groups[1].Value);
					string stat = flatMatch.Groups[2].Value;
					if (stat == "defense") score += val * 4f;
					else if (stat == "mana") score += val * 0.5f;
					else if (stat == "life") score += val * 2f;
					else if (stat == "armor penetration") score += val * 3f;
				}

				// Minion/sentry capacity
				if (lower.Contains("minion capacity") || lower.Contains("max minions"))
					score += 25f;
				if (lower.Contains("sentry capacity") || lower.Contains("max sentries"))
					score += 20f;

				// Immunity bonuses
				if (lower.Contains("immunity") || lower.Contains("immune"))
					score += 8f;
			}
		}
		catch { }

		return score;
	}

	/// <summary>
	/// Categorizes an accessory by its equip slot. Returns Other for accessories
	/// with no visual slot (invisible accessories like Obsidian Rose).
	/// </summary>
	public static AccessorySlotCategory GetAccessoryCategory(Item item)
	{
		if (!item.accessory) return AccessorySlotCategory.Other;
		if (item.wingSlot > 0) return AccessorySlotCategory.Wing;
		if (item.shieldSlot > 0) return AccessorySlotCategory.Shield;
		if (item.balloonSlot > 0) return AccessorySlotCategory.Balloon;
		if (item.shoeSlot > 0) return AccessorySlotCategory.Boots;
		if (item.neckSlot > 0) return AccessorySlotCategory.Neck;
		if (item.handOnSlot > 0) return AccessorySlotCategory.Combat;
		if (item.faceSlot > 0) return AccessorySlotCategory.Face;
		if (item.backSlot > 0) return AccessorySlotCategory.Back;
		return AccessorySlotCategory.Other;
	}
}
