using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using ProgressionCompass.Content.Graph;

namespace ProgressionCompass.Content.Progression;

/// <summary>
/// Captures ALL main-thread game state needed by background systems in a single call.
/// Eliminates inconsistency from capturing WorldContext, Tier, inventory, and player
/// stats at different times. Every background operation receives one of these.
/// </summary>
public class SystemSnapshot
{
	public WorldContext World { get; init; }
	public Tier PlayerTier { get; init; }
	public PlayerClass DetectedClass { get; init; }
	public Dictionary<int, InventoryEntry> Inventory { get; init; }
	public bool IncludeChests { get; init; }
	public int ArmorDefense { get; init; }
	public PlayerStats Stats { get; init; }

	/// <summary>
	/// Captures all game state on the main thread. Call ONLY from the main thread.
	/// </summary>
	public static SystemSnapshot Capture(bool includeChests, float chestRange = 50f)
	{
		var world = WorldContext.Capture();
		var tier = ProgressionTier.Detect();
		var inventory = InventoryScanner.Scan(includeChests, chestRange);

		PlayerClass detected = PlayerClass.Mixed;
		int armorDefense = 0;
		try
		{
			var player = Main.LocalPlayer;
			if (player != null)
			{
				detected = ClassDetector.Detect(player);
				for (int i = 0; i < 3; i++)
					if (player.armor[i].type > ItemID.None)
						armorDefense += player.armor[i].defense;
			}
		}
		catch { }

		var stats = GearRecommender.ComputePlayerStats(inventory, includeChests, armorDefense);

		return new SystemSnapshot
		{
			World = world,
			PlayerTier = tier,
			DetectedClass = detected,
			Inventory = inventory,
			IncludeChests = includeChests,
			ArmorDefense = armorDefense,
			Stats = stats
		};
	}
}
