using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.WorldBuilding;

namespace ProgressionCompass.Content.Progression;

/// <summary>
/// Which world evil an item or recipe is affiliated with.
/// </summary>
public enum EvilAffiliation
{
	Neutral = 0,
	Corruption = 1,
	Crimson = 2
}

/// <summary>
/// Immutable snapshot of world evil and ore state, captured on the main thread
/// and passed to all background consumers. Never access WorldGen or Main
/// directly from background threads — use this instead.
/// </summary>
public readonly record struct WorldContext
{
	/// <summary>True = Crimson world, False = Corruption world.</summary>
	public bool IsCrimson { get; init; }

	/// <summary>True if both evils exist (drunk world / Zenith seed).</summary>
	public bool HasBothEvils { get; init; }

	// Pre-hardmode ore TileIDs (-1 = unset)
	public int CopperTier { get; init; }
	public int IronTier { get; init; }
	public int SilverTier { get; init; }
	public int GoldTier { get; init; }

	// Hardmode ore TileIDs (-1 = not yet smashed)
	public int CobaltTier { get; init; }
	public int MythrilTier { get; init; }
	public int AdamantiteTier { get; init; }

	// Difficulty
	public bool IsExpert { get; init; }
	public bool IsMaster { get; init; }

	// Boss progression (for shimmer decrafting locks)
	public bool DownedSkeletron { get; init; }
	public bool DownedGolem { get; init; }

	public bool IsCorruption => !IsCrimson;
	public bool HardmodeOresKnown => CobaltTier != -1;

	/// <summary>True if this context was actually captured (not default/zero).</summary>
	public bool IsValid => CopperTier != 0 || IsCrimson || HasBothEvils;

	/// <summary>Captures world state on the main thread. Call ONLY from the main thread.</summary>
	public static WorldContext Capture()
	{
		try
		{
			return new WorldContext
			{
				IsCrimson = WorldGen.crimson,
				HasBothEvils = Main.drunkWorld,
				CopperTier = WorldGen.SavedOreTiers.Copper,
				IronTier = WorldGen.SavedOreTiers.Iron,
				SilverTier = WorldGen.SavedOreTiers.Silver,
				GoldTier = WorldGen.SavedOreTiers.Gold,
				CobaltTier = WorldGen.SavedOreTiers.Cobalt,
				MythrilTier = WorldGen.SavedOreTiers.Mythril,
				AdamantiteTier = WorldGen.SavedOreTiers.Adamantite,
				IsExpert = Main.expertMode,
				IsMaster = Main.masterMode,
				DownedSkeletron = NPC.downedBoss3,
				DownedGolem = NPC.downedGolemBoss
			};
		}
		catch
		{
			// Fallback for edge cases (main menu, etc.)
			return default;
		}
	}

	/// <summary>
	/// Checks if an item with the given evil affiliation is available in this world.
	/// In drunk worlds, both are available. Otherwise, only the matching evil.
	/// </summary>
	public bool IsAvailable(EvilAffiliation affiliation) => affiliation switch
	{
		EvilAffiliation.Neutral => true,
		// If context wasn't properly captured, don't filter anything
		EvilAffiliation.Corruption => !IsValid || HasBothEvils || IsCorruption,
		EvilAffiliation.Crimson => !IsValid || HasBothEvils || IsCrimson,
		_ => true
	};

	// ── Ore Preference ──────────────────────────────────────────

	/// <summary>
	/// Returns true if the given item is the world's preferred ore/bar variant
	/// (or if the tier is unknown/-1, in which case both are valid).
	/// Items not in any ore pair always return true.
	/// </summary>
	public bool IsPreferredOreItem(int itemType)
	{
		if (!IsValid)
			return true; // Context not captured — no preference

		if (!_orePairs.TryGetValue(itemType, out var pair))
			return true; // Not an ore pair item — no preference

		int tierValue = GetTierValue(pair.tier);
		if (tierValue == -1)
			return true; // Unknown tier — both valid

		return pair.tileId == tierValue;
	}

	/// <summary>
	/// Given an item in an ore pair, returns the world's preferred variant.
	/// If not an ore item or tier is unknown, returns the input unchanged.
	/// </summary>
	public int PreferredOreItem(int itemType)
	{
		if (!_orePairs.TryGetValue(itemType, out var pair))
			return itemType;

		int tierValue = GetTierValue(pair.tier);
		if (tierValue == -1)
			return itemType; // Unknown — no preference

		// Find the partner item that matches the world's tier
		if (pair.tileId == tierValue)
			return itemType; // Already preferred

		return pair.partnerId;
	}

	/// <summary>
	/// Returns the alternative item ID for an ore pair, or null if not an ore item.
	/// Used for "Cobalt or Palladium" display when tier is unknown.
	/// </summary>
	public int? GetOreAlternative(int itemType)
	{
		if (_orePairs.TryGetValue(itemType, out var pair))
			return pair.partnerId;
		return null;
	}

	private int GetTierValue(OreTier tier) => tier switch
	{
		OreTier.Copper => CopperTier,
		OreTier.Iron => IronTier,
		OreTier.Silver => SilverTier,
		OreTier.Gold => GoldTier,
		OreTier.Cobalt => CobaltTier,
		OreTier.Mythril => MythrilTier,
		OreTier.Adamantite => AdamantiteTier,
		_ => -1
	};

	private enum OreTier { Copper, Iron, Silver, Gold, Cobalt, Mythril, Adamantite }

	private readonly record struct OrePairInfo(int tileId, OreTier tier, int partnerId);

	// Static mapping: ItemID → (TileID it corresponds to, which tier, partner ItemID)
	// TileIDs from research: Copper=7, Tin=166, Iron=6, Lead=167, Silver=9, Tungsten=168,
	// Gold=8, Platinum=169, Cobalt=107, Palladium=221, Mythril=108, Orichalcum=222,
	// Adamantite=111, Titanium=223
	private static readonly Dictionary<int, OrePairInfo> _orePairs = new()
	{
		// Copper / Tin
		[ItemID.CopperOre] = new(7, OreTier.Copper, ItemID.TinOre),
		[ItemID.TinOre] = new(166, OreTier.Copper, ItemID.CopperOre),
		[ItemID.CopperBar] = new(7, OreTier.Copper, ItemID.TinBar),
		[ItemID.TinBar] = new(166, OreTier.Copper, ItemID.CopperBar),

		// Iron / Lead
		[ItemID.IronOre] = new(6, OreTier.Iron, ItemID.LeadOre),
		[ItemID.LeadOre] = new(167, OreTier.Iron, ItemID.IronOre),
		[ItemID.IronBar] = new(6, OreTier.Iron, ItemID.LeadBar),
		[ItemID.LeadBar] = new(167, OreTier.Iron, ItemID.IronBar),

		// Silver / Tungsten
		[ItemID.SilverOre] = new(9, OreTier.Silver, ItemID.TungstenOre),
		[ItemID.TungstenOre] = new(168, OreTier.Silver, ItemID.SilverOre),
		[ItemID.SilverBar] = new(9, OreTier.Silver, ItemID.TungstenBar),
		[ItemID.TungstenBar] = new(168, OreTier.Silver, ItemID.SilverBar),

		// Gold / Platinum
		[ItemID.GoldOre] = new(8, OreTier.Gold, ItemID.PlatinumOre),
		[ItemID.PlatinumOre] = new(169, OreTier.Gold, ItemID.GoldOre),
		[ItemID.GoldBar] = new(8, OreTier.Gold, ItemID.PlatinumBar),
		[ItemID.PlatinumBar] = new(169, OreTier.Gold, ItemID.GoldBar),

		// Cobalt / Palladium
		[ItemID.CobaltOre] = new(107, OreTier.Cobalt, ItemID.PalladiumOre),
		[ItemID.PalladiumOre] = new(221, OreTier.Cobalt, ItemID.CobaltOre),
		[ItemID.CobaltBar] = new(107, OreTier.Cobalt, ItemID.PalladiumBar),
		[ItemID.PalladiumBar] = new(221, OreTier.Cobalt, ItemID.CobaltBar),

		// Mythril / Orichalcum
		[ItemID.MythrilOre] = new(108, OreTier.Mythril, ItemID.OrichalcumOre),
		[ItemID.OrichalcumOre] = new(222, OreTier.Mythril, ItemID.MythrilOre),
		[ItemID.MythrilBar] = new(108, OreTier.Mythril, ItemID.OrichalcumBar),
		[ItemID.OrichalcumBar] = new(222, OreTier.Mythril, ItemID.MythrilBar),

		// Adamantite / Titanium
		[ItemID.AdamantiteOre] = new(111, OreTier.Adamantite, ItemID.TitaniumOre),
		[ItemID.TitaniumOre] = new(223, OreTier.Adamantite, ItemID.AdamantiteOre),
		[ItemID.AdamantiteBar] = new(111, OreTier.Adamantite, ItemID.TitaniumBar),
		[ItemID.TitaniumBar] = new(223, OreTier.Adamantite, ItemID.AdamantiteBar),
	};
}
