namespace ProgressionCompass.Content.Progression;

/// <summary>
/// Snapshot of the player's current gear stats, used by the
/// Progression Command Center to show "Your Stage" summary.
/// </summary>
public record PlayerStats(
	int BestMeleeDmg,
	int BestRangedDmg,
	int BestMagicDmg,
	int BestSummonDmg,
	float BestMeleeDps,
	float BestRangedDps,
	float BestMagicDps,
	float BestSummonDps,
	int ArmorDefense,
	int BestPickPower,
	int WingItemType = 0,
	float WingFlyTime = 0f,
	float WingSpeed = 0f)
{
	/// <summary>Best weapon damage for a specific class filter.</summary>
	public int BestDamageForClass(PlayerClass pc) => pc switch
	{
		PlayerClass.Melee => BestMeleeDmg,
		PlayerClass.Ranged => BestRangedDmg,
		PlayerClass.Mage => BestMagicDmg,
		PlayerClass.Summoner => BestSummonDmg,
		_ => System.Math.Max(System.Math.Max(BestMeleeDmg, BestRangedDmg),
			System.Math.Max(BestMagicDmg, BestSummonDmg))
	};

	/// <summary>Best weapon DPS for a specific class filter.</summary>
	public float BestDpsForClass(PlayerClass pc) => pc switch
	{
		PlayerClass.Melee => BestMeleeDps,
		PlayerClass.Ranged => BestRangedDps,
		PlayerClass.Mage => BestMagicDps,
		PlayerClass.Summoner => BestSummonDps,
		_ => System.Math.Max(System.Math.Max(BestMeleeDps, BestRangedDps),
			System.Math.Max(BestMagicDps, BestSummonDps))
	};

	/// <summary>Calculates theoretical DPS for an item.</summary>
	public static float CalculateDps(Terraria.Item item)
	{
		if (item.damage <= 0) return 0;
		float useTime = System.Math.Max(item.useTime, 1);
		float attacksPerSec = 60f / useTime;
		float critMult = 1f + (4f + item.crit) / 100f;
		return item.damage * critMult * attacksPerSec;
	}
}
