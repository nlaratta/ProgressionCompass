using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using ProgressionCompass.Content.Graph;
using ProgressionCompass.UI.Elements;

namespace ProgressionCompass.UI.Panels;

/// <summary>
/// Displays the crafting dependency tree as a flattened list of UITreeRow elements.
/// Each row knows its depth and draws indentation, connector lines, icons, and labels.
/// Expand/collapse hides or shows child rows.
/// </summary>
public class DependencyTreePanel : UIElement
{
	private readonly UIList _list;
	private readonly UIScrollbar _scrollbar;

	// Master list of all rows in display order; toggling expand/collapse rebuilds _list from this
	private readonly List<TreeEntry> _allEntries = [];

	// Stored result for re-flattening after recipe cycle
	private ResolverResult _currentResult;

	// Shared recipe preferences — set by parent (CraftPathState)
	private Dictionary<int, int> _recipePreferences = [];

	/// <summary>Fired when the user cycles a recipe. Args: (itemType, newAlternativeIndex).</summary>
	public event System.Action<int, int> OnRecipeCycled;

	/// <summary>Sets the shared recipe preferences dictionary (owned by CraftPathState).</summary>
	public void SetRecipePreferences(Dictionary<int, int> prefs) => _recipePreferences = prefs ?? [];

	public DependencyTreePanel()
	{
		Width.Set(0, 1f);
		Height.Set(0, 1f);

		_list = new UIList();
		_list.Width.Set(-20f, 1f);
		_list.Height.Set(0, 1f);
		_list.ListPadding = 0f;
		_list.ManualSortMethod = _ => { };
		Append(_list);

		_scrollbar = new UIScrollbar();
		_scrollbar.Left.Set(-18f, 1f);
		_scrollbar.Height.Set(0, 1f);
		_list.SetScrollbar(_scrollbar);
		Append(_scrollbar);
	}

	public void SetResult(ResolverResult result)
	{
		_currentResult = result;
		_allEntries.Clear();
		_list.Clear();

		if (result?.Tree == null)
		{
			_list.Add(new UIEmptyMessage("Search for an item to see the crafting tree."));
			return;
		}

		if (result.Tree is Content.Graph.HaveNode)
			_list.Add(new UIEmptyMessage("You already own this item."));
		else if (result.Tree is Content.Graph.AcquireNode && result.CraftOrder.Count == 0)
			_list.Add(new UIEmptyMessage("This item has no crafting recipe. See Materials tab for sources."));

		FlattenNode(result.Tree, 0, true);
		RebuildVisibleList();
	}

	/// <summary>Recursively flattens the GraphNode tree into a list of entries with depth info.</summary>
	private void FlattenNode(GraphNode node, int depth, bool isLastChild)
	{
		// Apply recipe preference: if user selected an alternative, use that subtree
		var displayNode = node;
		if (node is CraftNode craft && craft.HasAlternatives)
		{
			int selectedAlt = 0;
			if (_recipePreferences.TryGetValue(craft.ItemType, out int prefRecipeIdx) && prefRecipeIdx >= 0)
			{
				for (int i = 0; i < craft.Alternatives.Count; i++)
				{
					if (FindRecipeIndex(craft.Alternatives[i].Recipe) == prefRecipeIdx)
					{
						selectedAlt = i;
						break;
					}
				}
			}

			if (selectedAlt > 0 && selectedAlt < craft.AlternativeCount)
				displayNode = craft.Alternatives[selectedAlt];
			craft.SelectedAlternative = selectedAlt;
		}

		var children = GetChildren(displayNode);
		bool hasChildren = children.Count > 0;

		// Row shows the original node (for alternatives metadata) but children come from displayNode
		var row = new UITreeRow(node, depth, hasChildren, isLastChild);
		var config = Terraria.ModLoader.ModContent.GetInstance<Common.CraftPathConfig>();
		bool autoExpand = config?.AutoExpandTree ?? true;
		row.IsExpanded = autoExpand && depth < 3;
		row.OnToggleExpand += OnRowToggled;
		row.OnCycleRecipe += OnRowCycleRecipe;

		var entry = new TreeEntry(row, depth);
		_allEntries.Add(entry);

		for (int i = 0; i < children.Count; i++)
			FlattenNode(children[i], depth + 1, i == children.Count - 1);
	}

	private static List<GraphNode> GetChildren(GraphNode node)
	{
		return node switch
		{
			CraftNode craft => craft.Ingredients,
			GroupNode group => [group.ChosenItem],
			_ => []
		};
	}

	private void OnRowCycleRecipe(UITreeRow row)
	{
		if (row.Node is not CraftNode craft || !craft.HasAlternatives)
			return;

		// Find current alternative index by matching the stored recipe preference
		int currentAltIdx = 0;
		if (_recipePreferences.TryGetValue(craft.ItemType, out int currentRecipeIdx))
		{
			for (int i = 0; i < craft.Alternatives.Count; i++)
			{
				if (FindRecipeIndex(craft.Alternatives[i].Recipe) == currentRecipeIdx)
				{
					currentAltIdx = i;
					break;
				}
			}
		}

		// Cycle to the next alternative
		int nextAltIdx = (currentAltIdx + 1) % craft.AlternativeCount;
		int nextRecipeIdx = FindRecipeIndex(craft.Alternatives[nextAltIdx].Recipe);

		// Fire event with the stable recipe index
		OnRecipeCycled?.Invoke(craft.ItemType, nextRecipeIdx);
	}

	private static int FindRecipeIndex(Terraria.Recipe recipe)
	{
		for (int i = 0; i < Terraria.Recipe.numRecipes; i++)
			if (ReferenceEquals(Terraria.Main.recipe[i], recipe))
				return i;
		return -1;
	}

	private void OnRowToggled(UITreeRow row)
	{
		row.IsExpanded = !row.IsExpanded;
		RebuildVisibleList();
	}

	/// <summary>
	/// Rebuilds the UIList from _allEntries, skipping children of collapsed nodes.
	/// </summary>
	private void RebuildVisibleList()
	{
		_list.Clear();

		// Track which depth levels are currently collapsed
		// If a node at depth D is collapsed, skip all following nodes with depth > D
		int skipBelowDepth = int.MaxValue;

		foreach (var entry in _allEntries)
		{
			if (entry.Depth > skipBelowDepth)
				continue;

			// Reset skip threshold — we're at or above the collapsed parent
			skipBelowDepth = int.MaxValue;

			_list.Add(entry.Row);

			// If this node has children but is collapsed, skip its subtree
			if (entry.Row.HasChildren && !entry.Row.IsExpanded)
				skipBelowDepth = entry.Depth;
		}
	}

	private record TreeEntry(UITreeRow Row, int Depth);
}
