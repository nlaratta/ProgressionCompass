using Terraria;

namespace ProgressionCompass.Content.Progression;

public enum Tier
{
	PreBoss = 0,
	PostEyeOrKing = 1,
	PostEvilBoss = 2,
	PostSkeletron = 3,
	PreHardmodeLate = 4,
	EarlyHardmode = 5,
	PostFirstMech = 6,
	PostAllMechs = 7,
	PostPlantera = 8,
	PostGolem = 9,
	PostCultist = 10,
	PostMoonLord = 11
}

/// <summary>
/// Detects the player's current progression stage from boss defeat flags.
/// These are static booleans on NPC that persist per-world.
/// </summary>
public static class ProgressionTier
{
	public static Tier Detect()
	{
		if (NPC.downedMoonlord)
			return Tier.PostMoonLord;
		if (NPC.downedAncientCultist)
			return Tier.PostCultist;
		if (NPC.downedGolemBoss)
			return Tier.PostGolem;
		if (NPC.downedPlantBoss)
			return Tier.PostPlantera;
		if (NPC.downedMechBoss1 && NPC.downedMechBoss2 && NPC.downedMechBoss3)
			return Tier.PostAllMechs;
		if (NPC.downedMechBossAny)
			return Tier.PostFirstMech;
		if (Main.hardMode)
			return Tier.EarlyHardmode;
		if (NPC.downedBoss3 && NPC.downedQueenBee)
			return Tier.PreHardmodeLate;
		if (NPC.downedBoss3)
			return Tier.PostSkeletron;
		if (NPC.downedBoss2)
			return Tier.PostEvilBoss;
		if (NPC.downedBoss1 || NPC.downedSlimeKing)
			return Tier.PostEyeOrKing;

		return Tier.PreBoss;
	}

	public static string GetDisplayName(Tier tier) => tier switch
	{
		Tier.PreBoss => "Pre-Boss",
		Tier.PostEyeOrKing => "Post-Eye of Cthulhu / King Slime",
		Tier.PostEvilBoss => "Post-Eater of Worlds / Brain of Cthulhu",
		Tier.PostSkeletron => "Post-Skeletron",
		Tier.PreHardmodeLate => "Late Pre-Hardmode",
		Tier.EarlyHardmode => "Early Hardmode",
		Tier.PostFirstMech => "Post-Mechanical Boss",
		Tier.PostAllMechs => "Post-All Mechanical Bosses",
		Tier.PostPlantera => "Post-Plantera",
		Tier.PostGolem => "Post-Golem",
		Tier.PostCultist => "Post-Lunatic Cultist",
		Tier.PostMoonLord => "Post-Moon Lord",
		_ => "Unknown"
	};

	/// <summary>
	/// Estimates which tier a recipe belongs to by checking its Conditions
	/// and ingredient requirements.
	/// </summary>
	public static Tier EstimateRecipeTier(Terraria.Recipe recipe)
	{
		Tier highest = Tier.PreBoss;

		// Check explicit recipe conditions
		foreach (var cond in recipe.Conditions)
		{
			Tier condTier = EstimateConditionTier(cond);
			if (condTier > highest)
				highest = condTier;
		}

		// Check ingredients for progression-gated materials
		foreach (var ingredient in recipe.requiredItem)
		{
			if (ingredient.type <= Terraria.ID.ItemID.None) continue;
			Tier ingTier = EstimateItemTier(ingredient.type);
			if (ingTier > highest)
				highest = ingTier;
		}

		return highest;
	}

	/// <summary>Estimates tier from a recipe Condition by matching known conditions.</summary>
	private static Tier EstimateConditionTier(Terraria.Condition cond)
	{
		// Compare condition description to known boss/progression conditions
		string desc = cond.Description?.Value ?? "";

		if (desc.Contains("Moon Lord", System.StringComparison.OrdinalIgnoreCase))
			return Tier.PostMoonLord;
		if (desc.Contains("Cultist", System.StringComparison.OrdinalIgnoreCase))
			return Tier.PostCultist;
		if (desc.Contains("Golem", System.StringComparison.OrdinalIgnoreCase))
			return Tier.PostGolem;
		if (desc.Contains("Plantera", System.StringComparison.OrdinalIgnoreCase))
			return Tier.PostPlantera;
		if (desc.Contains("mechanical", System.StringComparison.OrdinalIgnoreCase))
			return Tier.PostFirstMech;
		if (desc.Contains("Hardmode", System.StringComparison.OrdinalIgnoreCase)
			|| desc.Contains("Hard mode", System.StringComparison.OrdinalIgnoreCase))
			return Tier.EarlyHardmode;

		return Tier.PreBoss;
	}

	/// <summary>Estimates tier from an item type based on rarity as a progression proxy.</summary>
	public static Tier EstimateItemTier(int itemType)
	{
		// Use item value + rarity as a rough proxy for tier
		// This is the dynamic part — works for modded items too
		var item = new Item();
		item.SetDefaults(itemType);

		return item.rare switch
		{
			<= 1 => Tier.PreBoss,
			2 => Tier.PostEvilBoss,
			3 => Tier.PostSkeletron,
			4 => Tier.EarlyHardmode,
			5 => Tier.PostFirstMech,
			6 => Tier.PostAllMechs,
			7 => Tier.PostPlantera,
			8 => Tier.PostGolem,
			9 => Tier.PostCultist,
			>= 10 => Tier.PostMoonLord
		};
	}
}
