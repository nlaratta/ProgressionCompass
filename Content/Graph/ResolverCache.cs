using System.Collections.Generic;

namespace CraftPathOptimizer.Content.Graph;

/// <summary>
/// Caches resolved trees keyed by (itemType, quantity, inventoryHash).
/// Invalidated when inventory changes (detected via hash of available counts).
/// </summary>
public class ResolverCache
{
	private readonly Dictionary<long, ResolverResult> _cache = [];
	private int _lastInventoryHash;

	/// <summary>
	/// Checks if the inventory has changed since last resolve.
	/// If so, clears the cache and updates the hash.
	/// </summary>
	public void CheckInventory(Dictionary<int, InventoryEntry> inventory, bool includeChests)
	{
		int hash = ComputeInventoryHash(inventory, includeChests);
		if (hash != _lastInventoryHash)
		{
			_cache.Clear();
			_lastInventoryHash = hash;
		}
	}

	public bool TryGet(int itemType, int quantity, out ResolverResult result)
	{
		long key = ((long)itemType << 32) | (uint)quantity;
		return _cache.TryGetValue(key, out result);
	}

	public void Store(int itemType, int quantity, ResolverResult result)
	{
		long key = ((long)itemType << 32) | (uint)quantity;
		_cache[key] = result;
	}

	public void Clear() => _cache.Clear();

	private static int ComputeInventoryHash(Dictionary<int, InventoryEntry> inventory, bool includeChests)
	{
		// Simple hash combining item types and counts.
		// Not cryptographic — just needs to detect changes reliably.
		unchecked
		{
			int hash = 17;
			foreach (var (type, entry) in inventory)
			{
				hash = hash * 31 + type;
				hash = hash * 31 + entry.AvailableCount(includeChests);
			}
			return hash;
		}
	}
}
