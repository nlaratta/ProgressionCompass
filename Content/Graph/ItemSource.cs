using System.Collections.Generic;

namespace ProgressionCompass.Content.Graph;

public enum ItemSourceType
{
	NPCDrop,
	BossDrop,
	NPCShop,
	BagDrop,
	Fishing,
	ChestLoot,
	Gathering,
	Event,
	Shimmer,
	Other
}

/// <summary>
/// Describes one way to obtain an item outside of crafting.
/// A single item can have multiple sources (e.g., Iron Ore drops from multiple NPCs
/// and can be mined).
/// </summary>
/// <summary>Minimum game difficulty required for this source.</summary>
public enum GameDifficulty { Normal = 0, Expert = 1, Master = 2 }

public record ItemSource(
	ItemSourceType Type,
	string Description,
	int SourceEntityId = -1,
	float DropRate = -1f,
	GameDifficulty MinDifficulty = GameDifficulty.Normal,
	List<string> ShopConditions = null,
	int ShopPrice = -1)
{
	/// <summary>
	/// Scores this source for ranking. Higher = more desirable to show as primary.
	/// Used by ActionClassifier and tree view to pick the best source to display.
	/// </summary>
	public int RecommendationScore => Type switch
	{
		ItemSourceType.BossDrop => DropRate >= 1f ? 110 : 60,
		ItemSourceType.NPCShop => 100,
		ItemSourceType.BagDrop => DropRate >= 1f ? 90 : 55,
		ItemSourceType.NPCDrop => DropRate >= 0.1f ? 80 : 50,
		ItemSourceType.Gathering => 70,
		ItemSourceType.Event => 45,
		ItemSourceType.Shimmer => 25,
		ItemSourceType.Fishing => 40,
		ItemSourceType.ChestLoot => 30,
		_ => 0
	};

	/// <summary>Formats the shop price as a readable coin string. Returns null if no price.</summary>
	public string FormattedPrice
	{
		get
		{
			if (ShopPrice <= 0) return null;
			int copper = ShopPrice;
			int platinum = copper / 1000000; copper %= 1000000;
			int gold = copper / 10000; copper %= 10000;
			int silver = copper / 100; copper %= 100;
			if (platinum > 0) return gold > 0 ? $"{platinum}p {gold}g" : $"{platinum}p";
			if (gold > 0) return silver > 0 ? $"{gold}g {silver}s" : $"{gold}g";
			if (silver > 0) return $"{silver}s";
			return $"{copper}c";
		}
	}

	/// <summary>Picks the best source from a list by recommendation score.</summary>
	public static ItemSource PickBest(System.Collections.Generic.List<ItemSource> sources)
	{
		if (sources == null || sources.Count == 0) return null;
		if (sources.Count == 1) return sources[0];

		var best = sources[0];
		int bestScore = best.RecommendationScore;
		for (int i = 1; i < sources.Count; i++)
		{
			int score = sources[i].RecommendationScore;
			if (score > bestScore || (score == bestScore && sources[i].DropRate > best.DropRate))
			{
				best = sources[i];
				bestScore = score;
			}
		}
		return best;
	}
}
