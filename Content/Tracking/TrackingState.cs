using System.Collections.Generic;
using ProgressionCompass.Common.Systems;
using ProgressionCompass.Content.Graph;

namespace ProgressionCompass.Content.Tracking;

/// <summary>
/// Central hub for the tracking system. Owns pinned goals, runs the resolver
/// and action classifier, and provides data to the sidebar, TODO tab, and HUD overlay.
/// </summary>
public class TrackingState
{
	private readonly List<PinnedGoal> _goals = [];

	public IReadOnlyList<PinnedGoal> Goals => _goals;
	public int Count => _goals.Count;

	/// <summary>Merged action groups across all pinned goals, sorted by priority.</summary>
	public List<ActionGroup> ActionPlan { get; private set; } = [];

	/// <summary>The single top-priority action for the HUD overlay.</summary>
	public ActionGroup NextAction => ActionPlan.Count > 0 ? ActionPlan[0] : null;

	// --- Goal management ---

	public bool IsTracked(int itemType)
	{
		foreach (var g in _goals)
			if (g.ItemType == itemType)
				return true;
		return false;
	}

	public void Pin(int itemType, int quantity = 1)
	{
		foreach (var g in _goals)
		{
			if (g.ItemType == itemType)
			{
				g.Quantity += quantity;
				return;
			}
		}
		_goals.Add(new PinnedGoal(itemType, quantity));
	}

	public void Unpin(int index)
	{
		if (index >= 0 && index < _goals.Count)
			_goals.RemoveAt(index);
	}

	public void UnpinByType(int itemType)
	{
		_goals.RemoveAll(g => g.ItemType == itemType);
	}

	public void Clear()
	{
		_goals.Clear();
		ActionPlan.Clear();
	}

	// --- Resolve + classify ---

	/// <summary>
	/// Re-resolves all pinned goals against current inventory and rebuilds the action plan.
	/// </summary>
	public void Refresh(
		RecipeIndexSystem recipeIndex,
		ItemSourceSystem sourceSystem,
		Dictionary<int, InventoryEntry> inventory,
		bool includeChests)
	{
		var resolver = new DependencyResolver(recipeIndex, sourceSystem);

		// Resolve each goal independently (don't share inventory — each goal
		// shows its own full requirements, the action plan merges them)
		foreach (var goal in _goals)
		{
			var inv = new Dictionary<int, InventoryEntry>(inventory);
			goal.Result = resolver.Resolve(goal.ItemType, goal.Quantity, inv, includeChests);
			goal.UpdateProgress(includeChests);
		}

		// Build unified action plan from all goals' missing materials
		ActionPlan = ActionClassifier.Classify(_goals, inventory, includeChests, sourceSystem);
	}

	// --- Serialization helpers ---

	public List<(int itemType, int quantity)> GetSaveData()
	{
		var data = new List<(int, int)>();
		foreach (var g in _goals)
			data.Add((g.ItemType, g.Quantity));
		return data;
	}

	public void LoadSaveData(List<(int itemType, int quantity)> data)
	{
		_goals.Clear();
		foreach (var (type, qty) in data)
			_goals.Add(new PinnedGoal(type, qty));
	}
}
