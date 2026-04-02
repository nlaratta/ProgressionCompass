using Terraria;
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

	public bool IsCorruption => !IsCrimson;
	public bool HardmodeOresKnown => CobaltTier != -1;

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
				AdamantiteTier = WorldGen.SavedOreTiers.Adamantite
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
		EvilAffiliation.Corruption => HasBothEvils || IsCorruption,
		EvilAffiliation.Crimson => HasBothEvils || IsCrimson,
		_ => true
	};
}
