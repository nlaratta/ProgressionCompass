using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using ProgressionCompass.Content.Graph;
using ProgressionCompass.UI.Elements;

namespace ProgressionCompass.UI.Panels;

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
			if (result?.Tree == null)
				_list.Add(new UIEmptyMessage("Search for an item to see craft steps."));
			else if (result.Tree is Content.Graph.HaveNode)
				_list.Add(new UIEmptyMessage("You already own this item!"));
			else if (result.Tree is Content.Graph.AcquireNode acq)
			{
				_list.Add(new UIEmptyMessage("This item cannot be crafted."));
				// Show sources
				if (acq.Sources.Count > 0)
				{
					_list.Add(new UISectionHeader("How to Obtain"));
					foreach (var src in acq.Sources)
						_list.Add(new UISourceRow(src));
				}
			}
			else
				_list.Add(new UIEmptyMessage("No crafting steps needed."));
			return;
		}

		int totalSteps = result.CraftOrder.Count;
		foreach (var step in result.CraftOrder)
			_list.Add(new UICraftStep(step, totalSteps));
	}
}
