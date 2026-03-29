using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using CraftPathOptimizer.Content.Graph;
using CraftPathOptimizer.UI.Elements;

namespace CraftPathOptimizer.UI.Panels;

/// <summary>
/// Displays the topologically-sorted craft order as a step-by-step list.
/// Each step shows what to craft, at which station, and where inputs come from.
/// </summary>
public class CraftOrderPanel : UIElement
{
	private readonly UIList _list;
	private readonly UIScrollbar _scrollbar;

	public CraftOrderPanel()
	{
		Width.Set(0, 1f);
		Height.Set(0, 1f);

		_list = new UIList();
		_list.Width.Set(-20f, 1f);
		_list.Height.Set(0, 1f);
		_list.ListPadding = 4f;
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
		_list.Clear();

		if (result?.CraftOrder == null || result.CraftOrder.Count == 0)
		{
			_list.Add(new UIEmptyMessage(
				result?.Tree is Content.Graph.HaveNode ? "You already own this item!" : "Search for an item to see craft steps."));
			return;
		}

		foreach (var step in result.CraftOrder)
			_list.Add(new UICraftStep(step));
	}
}
