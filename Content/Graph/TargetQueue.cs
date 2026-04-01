using System.Collections.Generic;
using ProgressionCompass.Common.Systems;

namespace ProgressionCompass.Content.Graph;

/// <summary>
/// Manages a queue of target items and resolves them sequentially,
/// consuming from a shared inventory so materials aren't double-counted.
/// </summary>
public class TargetQueue
{
	private readonly List<TargetItem> _targets = [];

	public IReadOnlyList<TargetItem> Targets => _targets;
	public int Count => _targets.Count;

	/// <summary>Currently focused target (shown in tree/craft order tabs).</summary>
	public int FocusedIndex { get; set; }

	public void Add(int itemType, int quantity = 1)
	{
		// Don't add duplicates
		foreach (var t in _targets)
		{
			if (t.ItemType == itemType)
			{
				t.Quantity += quantity;
				InvalidateCache();
				return;
			}
		}
		_targets.Add(new TargetItem(itemType, quantity));
		InvalidateCache();
	}

	public void Remove(int index)
	{
		if (index >= 0 && index < _targets.Count)
		{
			_targets.RemoveAt(index);
			if (FocusedIndex >= _targets.Count)
				FocusedIndex = _targets.Count - 1;
			InvalidateCache();
		}
	}

	public void Clear()
	{
		_targets.Clear();
		FocusedIndex = 0;
		InvalidateCache();
	}

	/// <summary>
	/// Resolves all targets sequentially against a shared inventory.
	/// Each target consumes from what's left after previous targets.
	/// Returns per-target results plus a merged material summary.
	/// </summary>
	private readonly ResolverCache _cache = new();

	/// <summary>
	/// Resolves all targets sequentially against a shared inventory.
	/// Uses cache to skip re-resolving unchanged targets.
	/// </summary>
	public MultiTargetResult ResolveAll(
		RecipeIndexSystem recipeIndex,
		ItemSourceSystem sourceSystem,
		Dictionary<int, InventoryEntry> inventory,
		bool includeChests)
	{
		_cache.CheckInventory(inventory, includeChests);

		var resolver = new DependencyResolver(recipeIndex, sourceSystem);
		var perTarget = new List<ResolverResult>();

		// Shared mutable inventory — each resolve consumes from what's left
		var sharedInventory = new Dictionary<int, InventoryEntry>(inventory);

		foreach (var target in _targets)
		{
			ResolverResult result;
			if (_cache.TryGet(target.ItemType, target.Quantity, out var cached))
			{
				result = cached;
			}
			else
			{
				result = resolver.Resolve(target.ItemType, target.Quantity, sharedInventory, includeChests);
				_cache.Store(target.ItemType, target.Quantity, result);
			}

			perTarget.Add(result);
			SubtractClaimed(sharedInventory, result, includeChests);
		}

		return new MultiTargetResult(perTarget, MergeMaterials(perTarget, inventory, includeChests));
	}

	/// <summary>Invalidates the cache (call when targets change).</summary>
	public void InvalidateCache() => _cache.Clear();

	private static void SubtractClaimed(
		Dictionary<int, InventoryEntry> inventory,
		ResolverResult result,
		bool includeChests)
	{
		foreach (var mat in result.Materials)
		{
			if (!inventory.TryGetValue(mat.ItemType, out var entry))
				continue;

			int used = System.Math.Min(mat.QuantityNeeded, entry.AvailableCount(includeChests));
			if (used <= 0)
				continue;

			// Subtract proportionally from personal first, then chest
			int fromPersonal = System.Math.Min(used, entry.PersonalCount);
			int remaining = used - fromPersonal;
			int fromEquipped = System.Math.Min(remaining, entry.EquippedCount);
			remaining -= fromEquipped;
			int fromChest = System.Math.Min(remaining, entry.ChestCount);

			inventory[mat.ItemType] = new InventoryEntry(
				entry.PersonalCount - fromPersonal,
				entry.EquippedCount - fromEquipped,
				entry.ChestCount - fromChest);
		}
	}

	private static List<MaterialEntry> MergeMaterials(
		List<ResolverResult> results,
		Dictionary<int, InventoryEntry> originalInventory,
		bool includeChests)
	{
		// Sum all material needs across targets
		var totals = new Dictionary<int, int>();
		var craftable = new System.Collections.Generic.HashSet<int>();

		foreach (var result in results)
		{
			foreach (var mat in result.Materials)
			{
				totals[mat.ItemType] = totals.GetValueOrDefault(mat.ItemType) + mat.QuantityNeeded;
				if (mat.IsCraftable)
					craftable.Add(mat.ItemType);
			}
		}

		var merged = new List<MaterialEntry>();
		foreach (var (itemType, qty) in totals)
		{
			var owned = originalInventory.GetValueOrDefault(itemType, InventoryEntry.Empty);
			// Search all results for sources (not just the first)
			List<Content.Graph.ItemSource> sources = [];
			foreach (var r in results)
			{
				var mat = r.Materials.Find(m => m.ItemType == itemType);
				if (mat?.Sources != null && mat.Sources.Count > 0)
				{
					sources = mat.Sources;
					break;
				}
			}
			merged.Add(new MaterialEntry(itemType, qty, owned, sources, craftable.Contains(itemType)));
		}

		// Sort: missing first, then by quantity
		merged.Sort((a, b) =>
		{
			bool aComplete = a.IsComplete(includeChests);
			bool bComplete = b.IsComplete(includeChests);
			if (aComplete != bComplete)
				return aComplete ? 1 : -1;
			return b.QuantityNeeded.CompareTo(a.QuantityNeeded);
		});

		return merged;
	}
}

public class TargetItem
{
	public int ItemType { get; }
	public int Quantity { get; set; }

	public TargetItem(int itemType, int quantity)
	{
		ItemType = itemType;
		Quantity = quantity;
	}
}

/// <summary>Results for a multi-target resolve: per-target trees + merged materials.</summary>
public record MultiTargetResult(
	List<ResolverResult> PerTarget,
	List<MaterialEntry> MergedMaterials);
