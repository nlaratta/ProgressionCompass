namespace ProgressionCompass.Content.Progression;

/// <summary>
/// Central location for all scoring weights and thresholds.
/// Tuning any recommendation, priority, or penalty across the mod
/// should start here rather than hunting through individual files.
/// </summary>
public static class ScoringConstants
{
	// ── Proximity Thresholds (completion % → tier) ──────────────

	/// <summary>Completion % at or above which an item is CraftableNow.</summary>
	public const float CraftableNowThreshold = 100f;

	/// <summary>Completion % at or above which an item is AlmostReady.</summary>
	public const float AlmostReadyThreshold = 80f;

	/// <summary>Completion % at or above which an item is NeedsWork (Building Toward).</summary>
	public const float NeedsWorkThreshold = 40f;

	// ── Proximity Weights (for source priority scoring) ─────────

	/// <summary>Weight for upgrades at AlmostReady proximity.</summary>
	public const float ProximityWeightAlmostReady = 10f;

	/// <summary>Weight for upgrades at NeedsWork proximity.</summary>
	public const float ProximityWeightNeedsWork = 4f;

	/// <summary>Weight for upgrades at FarAway proximity.</summary>
	public const float ProximityWeightFarAway = 1f;

	// ── Source Accessibility Multipliers ─────────────────────────

	public const float AccessibilityShop = 2.0f;
	public const float AccessibilityGuaranteedDrop = 1.8f;
	public const float AccessibilityGathering = 1.5f;
	public const float AccessibilityCommonDrop = 1.2f;
	public const float AccessibilityUncommonDrop = 0.8f;
	public const float AccessibilityFishing = 0.7f;
	public const float AccessibilityShimmer = 0.6f;
	public const float AccessibilityRareDrop = 0.5f;
	public const float AccessibilityChestLoot = 0.4f;

	// ── Last-Piece Bonus (materials close to completing an upgrade) ─

	public const float LastPieceBonus1 = 3.0f;
	public const float LastPieceBonus2 = 2.0f;
	public const float LastPieceBonus3 = 1.5f;

	// ── Resolver Penalties ───────────────────────────────────────

	/// <summary>Cost added per tier gap between recipe conditions and player tier.</summary>
	public const int ConditionTierPenalty = 50;

	/// <summary>Base cost added for shimmer pool access (on ShimmerNode).</summary>
	public const int ShimmerAccessCost = 2;

	// ── Acquire Cost Estimates (for resolver craft-vs-acquire) ───

	public const int AcquireCostGathering = 0;
	public const int AcquireCostShopAvailable = 1;
	public const int AcquireCostCatching = 1;
	public const int AcquireCostCommonDrop = 3;
	public const int AcquireCostUncommonDrop = 6;
	public const int AcquireCostBoss = 8;
	public const int AcquireCostFishing = 8;
	public const int AcquireCostRareDrop = 10;
	public const int AcquireCostEvent = 10;
	public const int AcquireCostChest = 12;
	public const int AcquireCostOther = 15;
	public const int AcquireCostShopUnavailable = 20;

	// ── Accessory Effect Weights ────────────────────────────────

	public const float AccKnockbackImmunity = 15f;
	public const float AccDamageReductionWeight = 100f;
	public const float AccArmorPenWeight = 3f;
	public const float AccAllDamageWeight = 200f;
	public const float AccAllCritWeight = 150f;
	public const float AccDodgeChance = 20f;
	public const float AccAutoReuse = 8f;
	public const float AccMagicQuiver = 12f;
	public const float AccKnockbackBoost = 5f;

	public const float AccRunSpeedWeight = 3f;
	public const float AccRocketBootsTier = 15f;
	public const float AccDashType = 20f;
	public const float AccJumpBoost = 10f;
	public const float AccJumpSpeedWeight = 5f;
	public const float AccNoFallDamage = 8f;
	public const float AccMoveSpeedWeight = 10f;
	public const float AccSwimming = 5f;
	public const float AccWaterWalk = 5f;
	public const float AccIceSkate = 3f;

	public const float AccLavaImmune = 15f;
	public const float AccExtendedIFrames = 12f;
	public const float AccBonusManaWeight = 0.3f;
	public const float AccBonusLifeWeight = 2f;

	// ── Gear Recommender Weights ────────────────────────────────

	public const float WeaponRawDamageWeight = 0.5f;
	public const float ArmorDefenseWeight = 2f;
	public const float ArmorClassBonusRarityWeight = 3f;
	public const float ToolPickPowerWeight = 0.5f;
	public const float WingHoverBonus = 15f;
}
