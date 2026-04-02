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
	public bool CraftableNow { get; init; }
	public int MissingIngredientCount { get; init; }
	public int TotalIngredientCount { get; init; }
	public Tier RequiredTier { get; init; }
	public float Score { get; init; }
	public Proximity Proximity { get; init; }

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
	/// <summary>Recipe ingredients: (itemType, needed, owned). Null if not craftable.</summary>
	public List<(int itemType, int needed, int owned)> Ingredients { get; init; }

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

			// World evil filter: skip items affiliated with the wrong evil
			if (!worldContext.IsAvailable(EvilAffiliationMap.Get(type)))
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

			int baseDamage = 0;
			float itemDps = 0;

			if (isWeapon)
			{
				category = GearCategory.Weapon;

				// DPS-based comparison instead of raw damage
				itemDps = PlayerStats.CalculateDps(item);
				float currentBestDps = GetBestDpsForClass(item, stats);

				// For summon weapons: use damage-per-slot
				float slotCost = 1f;
				if (item.DamageType.CountsAsClass(DamageClass.Summon) || item.DamageType == DamageClass.SummonMeleeSpeed)
				{
					if (ItemID.Sets.StaffMinionSlotsRequired != null && type < ItemID.Sets.StaffMinionSlotsRequired.Length)
						slotCost = Math.Max(ItemID.Sets.StaffMinionSlotsRequired[type], 0.5f);

					// Sentries get a bonus — they use separate slots
					if (item.sentry)
						itemDps *= 1.3f;
				}
				float effectiveDps = itemDps / slotCost;

				if (effectiveDps <= currentBestDps && item.damage <= GetBestDamageForClass(item, bestMeleeDmg, bestRangedDmg, bestMagicDmg, bestSummonDmg))
					continue; // Not an upgrade by DPS or raw damage

				baseDamage = item.damage;
				float dpsDelta = effectiveDps - currentBestDps;
				score = Math.Max(dpsDelta, 0) + Math.Max(item.damage - GetBestDamageForClass(item, bestMeleeDmg, bestRangedDmg, bestMagicDmg, bestSummonDmg), 0) * 0.5f;
				improvement = $"{item.damage} dmg, {(int)itemDps} DPS";
			}
			else if (isArmor)
			{
				category = GearCategory.Armor;
				int avgCurrentPerPiece = currentDefense / 3;
				int delta = item.defense - avgCurrentPerPiece;

				// Class-variant head armor (e.g., Cobalt Hat has 1 def but +40 mana)
				// may have low defense but valuable class bonuses — don't skip them
				bool hasClassBonus = ClassDetector.DetectArmorClass(item) != null;
				if (delta <= 0 && !hasClassBonus) continue;

				score = Math.Max(delta, 0) * 2f;
				if (hasClassBonus)
					score += item.rare * 3f; // Class-specific armor is inherently valuable
				improvement = delta > 0 ? $"{item.defense} def (+{delta})" : $"{item.defense} def";
			}
			else if (isTool)
			{
				category = GearCategory.Tool;
				int delta = item.pick - bestPickPower;
				if (delta <= 0) continue;
				score = delta * 0.5f;
				improvement = $"{item.pick}% pick (+{delta})";
			}
			else
			{
				category = GearCategory.Accessory;

				// Wings: compare by flight stats
				if (item.wingSlot > 0)
				{
					float wingScore = 0f;
					try
					{
						if (item.wingSlot < Terraria.ID.ArmorIDs.Wing.Sets.Stats.Length)
						{
							var ws = Terraria.ID.ArmorIDs.Wing.Sets.Stats[item.wingSlot];
							float flyTimeDelta = ws.FlyTime - stats.WingFlyTime;
							float speedDelta = ws.AccRunSpeedOverride - stats.WingSpeed;
							if (flyTimeDelta <= 0 && speedDelta <= 0 && stats.WingItemType > 0)
								continue; // Not better than current wings
							wingScore = flyTimeDelta * 0.5f + speedDelta * 10f;
							if (ws.HasDownHoverStats) wingScore += 15f;
							float flySecs = ws.FlyTime / 60f;
							improvement = $"Wings: {flySecs:F1}s fly, {ws.AccRunSpeedOverride:F0} speed";
						}
					}
					catch { }
					score = wingScore + 20f;
				}
				else
				{
					// Parse tooltip for actual stat bonuses
					float tooltipScore = ScoreAccessoryFromTooltip(item);
					if (tooltipScore > 0)
					{
						score = tooltipScore;
						improvement = $"Score: {(int)tooltipScore}";
					}
					else
					{
						score = item.rare * 3f;
						improvement = $"Tier {item.rare} accessory";
					}
				}

				if (HasSimilarAccessory(item, ownedItems))
					score *= 0.4f;
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
			}

			// Classify proximity (no score boost — sections handle grouping)
			Proximity proximity;
			if (hasRecipe && missingCount == 0)
				proximity = Proximity.CraftableNow;
			else if (hasRecipe && missingCount <= 2)
				proximity = Proximity.AlmostReady;
			else if (hasRecipe)
				proximity = Proximity.NeedsWork;
			else
				proximity = Proximity.FarAway;

			// Source info + acquisition method
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
				CraftableNow = hasRecipe && missingCount == 0,
				MissingIngredientCount = missingCount,
				TotalIngredientCount = totalIngCount,
				RequiredTier = itemTier,
				Score = score,
				Proximity = proximity,
				BaseDamage = baseDamage,
				Dps = itemDps,
				SourceHint = sourceHint,
				AllSources = allSources,
				HasRecipe = hasRecipe,
				Ingredients = bestIngredients,
				Acquisition = acquisition
			});
		}

		// Sort by score descending
		results.Sort((a, b) => b.Score.CompareTo(a.Score));

		return results;
	}

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
}
