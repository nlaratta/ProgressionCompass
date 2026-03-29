using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using CraftPathOptimizer.Common.Systems;
using CraftPathOptimizer.Content.Graph;
using CraftPathOptimizer.Content.Tracking;

namespace CraftPathOptimizer.Common.Players;

/// <summary>
/// Per-player tracking state: saves/loads pinned goals, periodically refreshes
/// progress by diffing inventory against tracked items.
/// </summary>
public class TrackingPlayer : ModPlayer
{
	public TrackingState Tracking { get; } = new();

	/// <summary>Persisted class filter for the Progress tab.</summary>
	public Content.Progression.PlayerClass SavedClassFilter { get; set; } = Content.Progression.PlayerClass.Mixed;

	private int _refreshTimer;
	private const int RefreshIntervalFrames = 180; // ~3 seconds at 60fps
	private int _lastInventoryHash;

	public override void PostUpdate()
	{
		// Periodic lightweight check: has inventory changed?
		_refreshTimer++;
		if (_refreshTimer < RefreshIntervalFrames)
			return;
		_refreshTimer = 0;

		if (Tracking.Count == 0)
			return;

		int hash = ComputeTrackedItemsHash();
		if (hash == _lastInventoryHash)
			return;
		_lastInventoryHash = hash;

		RefreshTracking();
	}

	/// <summary>
	/// Current chest toggle state. Set by the UI when the toggle changes,
	/// so that periodic refreshes and pin actions use the correct flag.
	/// </summary>
	public bool ChestsEnabled { get; set; }

	/// <summary>Force a full refresh of all tracked goals.</summary>
	public void RefreshTracking()
	{
		var recipeIndex = ModContent.GetInstance<RecipeIndexSystem>();
		var sourceSystem = ModContent.GetInstance<ItemSourceSystem>();
		if (recipeIndex?.RecipesByResult == null)
			return;

		var config = ModContent.GetInstance<global::CraftPathOptimizer.Common.CraftPathConfig>();
		float chestRange = config?.ChestScanRange ?? InventoryScanner.DefaultChestRangeTiles;
		var inventory = InventoryScanner.Scan(ChestsEnabled, chestRange);

		Tracking.Refresh(recipeIndex, sourceSystem, inventory, ChestsEnabled);
	}

	/// <summary>Quick hash of just the tracked item counts in inventory.</summary>
	private int ComputeTrackedItemsHash()
	{
		unchecked
		{
			int hash = 17;
			var player = Main.LocalPlayer;
			if (player?.inventory == null) return hash;

			foreach (var goal in Tracking.Goals)
			{
				int count = 0;
				foreach (Item item in player.inventory)
				{
					if (item.type == goal.ItemType)
						count += item.stack;
				}
				hash = hash * 31 + goal.ItemType;
				hash = hash * 31 + count;
			}
			return hash;
		}
	}

	public override void SaveData(TagCompound tag)
	{
		var goals = Tracking.GetSaveData();
		if (goals.Count == 0)
			return;

		var types = new List<int>();
		var quantities = new List<int>();
		foreach (var (itemType, quantity) in goals)
		{
			types.Add(itemType);
			quantities.Add(quantity);
		}

		tag["TrackedTypes"] = types;
		tag["TrackedQuantities"] = quantities;
		tag["ClassFilter"] = (int)SavedClassFilter;
	}

	public override void LoadData(TagCompound tag)
	{
		if (!tag.ContainsKey("TrackedTypes"))
			return;

		var types = tag.GetList<int>("TrackedTypes");
		var quantities = tag.GetList<int>("TrackedQuantities");

		var data = new List<(int, int)>();
		for (int i = 0; i < types.Count && i < quantities.Count; i++)
			data.Add((types[i], quantities[i]));

		Tracking.LoadSaveData(data);

		if (tag.ContainsKey("ClassFilter"))
			SavedClassFilter = (Content.Progression.PlayerClass)tag.GetInt("ClassFilter");
	}

	public override void OnEnterWorld()
	{
		// Resolve tracked goals on world entry
		if (Tracking.Count > 0)
			RefreshTracking();
	}
}
