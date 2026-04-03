using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ProgressionCompass.Common.Systems;

/// <summary>
/// Maps crafting station tile IDs to their equivalences. An Adamantite Forge
/// counts as a Furnace AND Hellforge, a Mythril Anvil counts as an Iron Anvil, etc.
/// Built once during PostAddRecipes from hardcoded vanilla rules + modded TileLoader.AdjTiles.
/// </summary>
public class CraftingStationMap : ModSystem
{
	/// <summary>
	/// Maps a tile ID to the set of tile IDs it functionally covers.
	/// E.g., 133 (Adamantite Forge) → {17 (Furnace), 77 (Hellforge)}.
	/// </summary>
	public static Dictionary<int, HashSet<int>> Equivalences { get; private set; }

	/// <summary>
	/// Reverse map: tile ID → set of higher-tier tiles that cover it.
	/// E.g., 17 (Furnace) → {77 (Hellforge), 133 (Adamantite Forge)}.
	/// </summary>
	public static Dictionary<int, HashSet<int>> CoveredBy { get; private set; }

	public override void PostAddRecipes()
	{
		Equivalences = [];
		CoveredBy = [];

		// Hardcoded vanilla equivalences (from decompiled Player.AdjTiles scan)
		AddEquivalence(77, 17);          // Hellforge → Furnace
		AddEquivalence(133, 17);         // Adamantite/Titanium Forge → Furnace
		AddEquivalence(133, 77);         // Adamantite/Titanium Forge → Hellforge
		AddEquivalence(134, 16);         // Mythril/Orichalcum Anvil → Iron Anvil
		AddEquivalence(355, 13);         // Alchemy Table → Placed Bottle
		AddEquivalence(355, 14);         // Alchemy Table → Table
		AddEquivalence(354, 14);         // Crystal Ball variant → Table
		AddEquivalence(469, 14);         // Table variant → Table
		AddEquivalence(487, 14);         // Table variant → Table

		// Modded tile equivalences via TileLoader
		for (int tileId = TileID.Count; tileId < TileLoader.TileCount; tileId++)
		{
			try
			{
				var modTile = TileLoader.GetTile(tileId);
				if (modTile?.AdjTiles != null)
				{
					foreach (int adjTile in modTile.AdjTiles)
						AddEquivalence(tileId, adjTile);
				}
			}
			catch { }
		}

		Mod.Logger.Info($"Station equivalence map: {Equivalences.Count} tiles with equivalences");
	}

	public override void Unload()
	{
		Equivalences = null;
		CoveredBy = null;
	}

	/// <summary>
	/// Given a required tile ID, returns the best name to display.
	/// If the player might have a higher-tier station, returns that name instead.
	/// Falls back to the base tile name.
	/// </summary>
	public static string GetBestStationName(int requiredTileId)
	{
		string baseName = GetTileName(requiredTileId);

		if (CoveredBy == null || !CoveredBy.TryGetValue(requiredTileId, out var coveringTiles))
			return baseName;

		// Find the highest-tier covering tile (most equivalences = most advanced)
		int bestTile = requiredTileId;
		int bestCount = 0;
		foreach (int coverTile in coveringTiles)
		{
			int count = Equivalences.GetValueOrDefault(coverTile)?.Count ?? 0;
			if (count > bestCount)
			{
				bestCount = count;
				bestTile = coverTile;
			}
		}

		if (bestTile != requiredTileId)
		{
			string advancedName = GetTileName(bestTile);
			if (advancedName != null)
				return advancedName;
		}

		return baseName;
	}

	private static string GetTileName(int tileId)
	{
		try
		{
			string name = Lang.GetMapObjectName(
				Terraria.Map.MapHelper.TileToLookup(tileId, 0));
			return string.IsNullOrEmpty(name) ? null : name;
		}
		catch { return null; }
	}

	private static void AddEquivalence(int tileId, int countsAs)
	{
		if (!Equivalences.TryGetValue(tileId, out var set))
		{
			set = [];
			Equivalences[tileId] = set;
		}
		set.Add(countsAs);

		// Reverse map
		if (!CoveredBy.TryGetValue(countsAs, out var coverSet))
		{
			coverSet = [];
			CoveredBy[countsAs] = coverSet;
		}
		coverSet.Add(tileId);
	}
}
