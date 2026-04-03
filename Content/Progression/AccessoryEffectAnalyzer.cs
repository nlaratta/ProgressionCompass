using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ProgressionCompass.Content.Progression;

/// <summary>
/// Structured accessory effects captured by the differential approach:
/// equip an accessory on a scratch Player, diff ~25 fields against baseline.
/// </summary>
public class AccessoryEffects
{
	// Movement
	public float RunSpeedBoost { get; init; }
	public int RocketBootsTier { get; init; }
	public int DashType { get; init; }
	public bool JumpBoost { get; init; }
	public float JumpSpeedBoost { get; init; }
	public bool NoFallDamage { get; init; }
	public float MoveSpeedBoost { get; init; }
	public bool Swimming { get; init; }
	public bool WaterWalk { get; init; }
	public bool IceSkate { get; init; }

	// Combat
	public bool KnockbackImmunity { get; init; }
	public float DamageReduction { get; init; }
	public float ArmorPenetration { get; init; }
	public float AllDamageBoost { get; init; }
	public float AllCritBoost { get; init; }
	public int AggroChange { get; init; }
	public bool DodgeChance { get; init; }
	public bool KnockbackBoost { get; init; }
	public bool AutoReuse { get; init; }
	public bool MagicQuiver { get; init; }

	// Survivability
	public bool LavaImmune { get; init; }
	public bool ExtendedIFrames { get; init; }
	public int BonusMana { get; init; }
	public int BonusLife { get; init; }

	// Computed scores
	public float CombatScore { get; init; }
	public float MobilityScore { get; init; }
	public float SurvivalScore { get; init; }
	public float TotalScore { get; init; }

	/// <summary>Builds a concise description of the most impactful effects.</summary>
	public string GetImprovementText()
	{
		var parts = new List<string>(4);

		// Combat effects
		if (KnockbackImmunity) parts.Add("KB immunity");
		if (DamageReduction > 0) parts.Add($"{DamageReduction * 100:F0}% DR");
		if (DodgeChance) parts.Add("Dodge");
		if (AllDamageBoost > 0) parts.Add($"+{AllDamageBoost * 100:F0}% dmg");
		if (AllCritBoost > 0) parts.Add($"+{AllCritBoost * 100:F0}% crit");
		if (ArmorPenetration > 0) parts.Add($"+{ArmorPenetration:F0} pen");
		if (AutoReuse) parts.Add("Auto-reuse");
		if (MagicQuiver) parts.Add("Magic Quiver");

		// Mobility effects
		if (DashType > 0) parts.Add("Dash");
		if (RunSpeedBoost > 0) parts.Add($"+{RunSpeedBoost:F0} speed");
		if (RocketBootsTier > 0) parts.Add("Rocket boots");
		if (JumpBoost) parts.Add("Jump boost");
		if (NoFallDamage) parts.Add("No fall dmg");
		if (Swimming) parts.Add("Swim");
		if (WaterWalk) parts.Add("Water walk");

		// Survival effects
		if (LavaImmune) parts.Add("Lava immune");
		if (ExtendedIFrames) parts.Add("Extended iframes");
		if (BonusMana > 0) parts.Add($"+{BonusMana} mana");
		if (BonusLife > 0) parts.Add($"+{BonusLife} life");

		if (parts.Count == 0)
			return null;

		// Show up to 3 most impactful effects
		return string.Join(", ", parts.Count > 3 ? parts.GetRange(0, 3) : parts);
	}
}

/// <summary>
/// Analyzes accessory effects by equipping each on a scratch Player and measuring
/// which fields change. Works for both vanilla and modded accessories.
/// Initialized once during PostSetupContent, results cached permanently.
/// </summary>
public static class AccessoryEffectAnalyzer
{
	private static Dictionary<int, AccessoryEffects> _cache;

	public static int Count => _cache?.Count ?? 0;

	/// <summary>
	/// Analyzes all accessory items. Must be called from the main thread
	/// during PostSetupContent (after all items are registered).
	/// </summary>
	public static void Initialize()
	{
		_cache = new Dictionary<int, AccessoryEffects>(256);

		Player baseline = new();
		Player test = new();

		foreach (var (type, item) in ContentSamples.ItemsByType)
		{
			if (type <= ItemID.None || item == null || !item.accessory)
				continue;
			if (item.vanity)
				continue;

			try
			{
				var effects = AnalyzeItem(item, baseline, test);
				if (effects != null && effects.TotalScore > 0)
					_cache[type] = effects;
			}
			catch { }
		}
	}

	/// <summary>Thread-safe lookup after initialization.</summary>
	public static AccessoryEffects Get(int itemType) =>
		_cache != null && _cache.TryGetValue(itemType, out var eff) ? eff : null;

	public static void Unload() => _cache = null;

	/// <summary>
	/// Computes the marginal value of a candidate accessory vs what the player already has.
	/// Effects the player already has from equipped accessories are worth zero.
	/// Returns a multiplier 0.0-1.0 to apply to the candidate's score.
	/// </summary>
	public static float ComputeMarginalMultiplier(int candidateType, HashSet<int> equippedAccessoryTypes)
	{
		var candidate = Get(candidateType);
		if (candidate == null || candidate.TotalScore <= 0)
			return 1f; // No data — don't penalize

		// Merge effects of all equipped accessories
		float equippedCombat = 0f;
		float equippedMobility = 0f;
		float equippedSurvival = 0f;
		bool hasKBImmunity = false;
		bool hasDash = false;
		bool hasNoFall = false;
		bool hasLavaImmune = false;

		foreach (int eqType in equippedAccessoryTypes)
		{
			var eqEffects = Get(eqType);
			if (eqEffects == null) continue;
			equippedCombat += eqEffects.CombatScore;
			equippedMobility += eqEffects.MobilityScore;
			equippedSurvival += eqEffects.SurvivalScore;
			if (eqEffects.KnockbackImmunity) hasKBImmunity = true;
			if (eqEffects.DashType > 0) hasDash = true;
			if (eqEffects.NoFallDamage) hasNoFall = true;
			if (eqEffects.LavaImmune) hasLavaImmune = true;
		}

		// Count how many of the candidate's key effects are already covered
		int totalEffects = 0;
		int duplicateEffects = 0;

		if (candidate.KnockbackImmunity) { totalEffects++; if (hasKBImmunity) duplicateEffects++; }
		if (candidate.DashType > 0) { totalEffects++; if (hasDash) duplicateEffects++; }
		if (candidate.NoFallDamage) { totalEffects++; if (hasNoFall) duplicateEffects++; }
		if (candidate.LavaImmune) { totalEffects++; if (hasLavaImmune) duplicateEffects++; }

		// Count numeric effects that are already well-covered
		if (candidate.DamageReduction > 0) totalEffects++;
		if (candidate.RunSpeedBoost > 0) totalEffects++;
		if (candidate.RocketBootsTier > 0) totalEffects++;
		if (candidate.AllDamageBoost > 0) totalEffects++;

		if (totalEffects == 0)
			return 1f;

		// Reduce score proportionally to duplicate boolean effects
		float dupeRatio = (float)duplicateEffects / totalEffects;
		return Math.Max(0.2f, 1f - dupeRatio * 0.7f);
	}

	private static AccessoryEffects AnalyzeItem(Item item, Player baseline, Player test)
	{
		// Reset both players to clean state
		baseline.ResetEffects();
		test.ResetEffects();

		// Apply the accessory to the test player
		try
		{
			// Vanilla + modded accessory application via the player instance
			test.ApplyEquipFunctional(item, false);
		}
		catch { }

		try
		{
			// Modded hook — may add additional effects beyond vanilla
			ItemLoader.UpdateAccessory(item, test, false);
		}
		catch { }

		// Diff fields
		float runSpeed = test.accRunSpeed - baseline.accRunSpeed;
		int rocketBoots = test.rocketBoots - baseline.rocketBoots;
		int dash = test.dashType - baseline.dashType;
		bool jumpBoost = test.jumpBoost && !baseline.jumpBoost;
		float jumpSpeed = test.jumpSpeedBoost - baseline.jumpSpeedBoost;
		bool noFall = test.noFallDmg && !baseline.noFallDmg;
		float moveSpeed = test.moveSpeed - baseline.moveSpeed;
		bool swim = test.accFlipper && !baseline.accFlipper;
		bool waterWalk = (test.waterWalk || test.waterWalk2) && !(baseline.waterWalk || baseline.waterWalk2);
		bool iceSkate = test.iceSkate && !baseline.iceSkate;

		bool noKB = test.noKnockback && !baseline.noKnockback;
		float endurance = test.endurance - baseline.endurance;
		float armorPen = test.GetTotalArmorPenetration(DamageClass.Generic) - baseline.GetTotalArmorPenetration(DamageClass.Generic);
		float dmgBoost = test.GetTotalDamage(DamageClass.Generic).Additive - baseline.GetTotalDamage(DamageClass.Generic).Additive;
		float critBoost = test.GetTotalCritChance(DamageClass.Generic) - baseline.GetTotalCritChance(DamageClass.Generic);
		int aggro = test.aggro - baseline.aggro;
		bool dodge = test.blackBelt && !baseline.blackBelt;
		bool kbBoost = test.kbGlove && !baseline.kbGlove;
		bool autoReuse = test.autoReuseGlove && !baseline.autoReuseGlove;
		bool quiver = test.magicQuiver && !baseline.magicQuiver;

		bool lavaImmune = test.lavaImmune && !baseline.lavaImmune;
		bool longInv = test.longInvince && !baseline.longInvince;
		int bonusMana = test.statManaMax2 - baseline.statManaMax2;
		int bonusLife = test.statLifeMax2 - baseline.statLifeMax2;

		// Compute scores using centralized weights
		float combat =
			(noKB ? ScoringConstants.AccKnockbackImmunity : 0f) +
			endurance * ScoringConstants.AccDamageReductionWeight +
			armorPen * ScoringConstants.AccArmorPenWeight +
			dmgBoost * ScoringConstants.AccAllDamageWeight +
			critBoost * ScoringConstants.AccAllCritWeight +
			(dodge ? ScoringConstants.AccDodgeChance : 0f) +
			(kbBoost ? ScoringConstants.AccKnockbackBoost : 0f) +
			(autoReuse ? ScoringConstants.AccAutoReuse : 0f) +
			(quiver ? ScoringConstants.AccMagicQuiver : 0f) +
			Math.Abs(aggro) * 0.05f;

		float mobility =
			runSpeed * ScoringConstants.AccRunSpeedWeight +
			rocketBoots * ScoringConstants.AccRocketBootsTier +
			dash * ScoringConstants.AccDashType +
			(jumpBoost ? ScoringConstants.AccJumpBoost : 0f) +
			jumpSpeed * ScoringConstants.AccJumpSpeedWeight +
			(noFall ? ScoringConstants.AccNoFallDamage : 0f) +
			moveSpeed * ScoringConstants.AccMoveSpeedWeight +
			(swim ? ScoringConstants.AccSwimming : 0f) +
			(waterWalk ? ScoringConstants.AccWaterWalk : 0f) +
			(iceSkate ? ScoringConstants.AccIceSkate : 0f);

		float survival =
			(lavaImmune ? ScoringConstants.AccLavaImmune : 0f) +
			(longInv ? ScoringConstants.AccExtendedIFrames : 0f) +
			bonusMana * ScoringConstants.AccBonusManaWeight +
			bonusLife * ScoringConstants.AccBonusLifeWeight;

		float total = combat + mobility + survival;

		if (total <= 0)
			return null;

		return new AccessoryEffects
		{
			RunSpeedBoost = runSpeed,
			RocketBootsTier = rocketBoots,
			DashType = dash,
			JumpBoost = jumpBoost,
			JumpSpeedBoost = jumpSpeed,
			NoFallDamage = noFall,
			MoveSpeedBoost = moveSpeed,
			Swimming = swim,
			WaterWalk = waterWalk,
			IceSkate = iceSkate,

			KnockbackImmunity = noKB,
			DamageReduction = endurance,
			ArmorPenetration = armorPen,
			AllDamageBoost = dmgBoost,
			AllCritBoost = critBoost,
			AggroChange = aggro,
			DodgeChance = dodge,
			KnockbackBoost = kbBoost,
			AutoReuse = autoReuse,
			MagicQuiver = quiver,

			LavaImmune = lavaImmune,
			ExtendedIFrames = longInv,
			BonusMana = bonusMana,
			BonusLife = bonusLife,

			CombatScore = combat,
			MobilityScore = mobility,
			SurvivalScore = survival,
			TotalScore = total
		};
	}
}
