using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;

namespace CraftPathOptimizer.Content.Graph;

/// <summary>
/// Aggregates all items the player owns across personal storage, equipped gear,
/// and optionally nearby chests. Returns a per-item breakdown of where things are.
/// </summary>
public static class InventoryScanner
{
	/// <summary>Default chest scan radius in tiles.</summary>
	public const float DefaultChestRangeTiles = 50f;

	/// <summary>
	/// Scans all player storage and returns a dictionary of item type → InventoryEntry.
	/// </summary>
	/// <param name="includeChests">Whether to scan nearby world chests.</param>
	/// <param name="chestRangeTiles">Max distance in tiles for chest scanning.</param>
	public static Dictionary<int, InventoryEntry> Scan(
		bool includeChests = false,
		float chestRangeTiles = DefaultChestRangeTiles)
	{
		var result = new Dictionary<int, InventoryEntry>();
		Player player = Main.LocalPlayer;

		// --- Personal storage (inventory + all 4 banks) ---
		ScanContainer(result, player.inventory, InventoryEntry.Personal);
		ScanContainer(result, player.bank.item, InventoryEntry.Personal);
		ScanContainer(result, player.bank2.item, InventoryEntry.Personal);
		ScanContainer(result, player.bank3.item, InventoryEntry.Personal);
		ScanContainer(result, player.bank4.item, InventoryEntry.Personal);

		// --- Equipped gear (armor + accessories + misc) ---
		ScanContainer(result, player.armor, InventoryEntry.Equipped);
		ScanContainer(result, player.miscEquips, InventoryEntry.Equipped);

		// --- Nearby chests (when enabled) ---
		if (includeChests)
		{
			ScanNearbyChests(result, player, chestRangeTiles);
			ScanMagicStorage(result);
		}

		return result;
	}

	/// <summary>
	/// Scans an item array and merges counts into the result dictionary.
	/// The entryFactory determines which InventoryEntry field gets the count
	/// (Personal, Equipped, or Chest).
	/// </summary>
	private static void ScanContainer(
		Dictionary<int, InventoryEntry> result,
		Item[] items,
		Func<int, InventoryEntry> entryFactory)
	{
		for (int i = 0; i < items.Length; i++)
		{
			Item item = items[i];
			if (item.type <= Terraria.ID.ItemID.None || item.stack <= 0)
				continue;

			var entry = entryFactory(item.stack);
			if (result.TryGetValue(item.type, out var existing))
				result[item.type] = existing + entry;
			else
				result[item.type] = entry;
		}
	}

	/// <summary>
	/// Scans all world chests within range of the player and adds their contents
	/// as Chest-sourced inventory entries.
	/// </summary>
	private static void ScanNearbyChests(
		Dictionary<int, InventoryEntry> result,
		Player player,
		float rangeTiles)
	{
		float rangePixels = rangeTiles * 16f;
		float rangeSq = rangePixels * rangePixels;
		float playerX = player.Center.X;
		float playerY = player.Center.Y;

		for (int i = 0; i < Main.maxChests; i++)
		{
			Chest chest = Main.chest[i];
			if (chest == null)
				continue;

			// Chest position: tile coords → pixel center of the 2x2 tile chest
			float chestX = chest.x * 16f + 16f;
			float chestY = chest.y * 16f + 16f;
			float dx = chestX - playerX;
			float dy = chestY - playerY;

			// Squared distance avoids the sqrt — cheaper and we only need comparison
			if (dx * dx + dy * dy > rangeSq)
				continue;

			ScanContainer(result, chest.item, InventoryEntry.Chest);
		}
	}

	/// <summary>
	/// If Magic Storage mod is loaded, scans its storage system for items.
	/// Uses Mod.Call to avoid a hard dependency — the mod simply won't find
	/// any Magic Storage items if the mod isn't installed.
	/// </summary>
	private static void ScanMagicStorage(Dictionary<int, InventoryEntry> result)
	{
		if (!ModLoader.TryGetMod("MagicStorage", out Mod magicStorage))
			return;

		try
		{
			// Magic Storage's Mod.Call API: "GetItems" returns the items in the
			// nearest storage heart. The exact API depends on the version.
			// We try the known call signature and gracefully handle failure.
			var items = magicStorage.Call("GetItems") as IEnumerable<Item>;
			if (items == null)
				return;

			foreach (Item item in items)
			{
				if (item == null || item.type <= Terraria.ID.ItemID.None || item.stack <= 0)
					continue;

				var entry = InventoryEntry.MagicStorage(item.stack);
				if (result.TryGetValue(item.type, out var existing))
					result[item.type] = existing + entry;
				else
					result[item.type] = entry;
			}
		}
		catch
		{
			// Magic Storage API may differ by version — silently ignore failures.
			// Items from Magic Storage just won't appear in the scan.
		}
	}
}
