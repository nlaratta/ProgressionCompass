using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using CraftPathOptimizer.Content.Tracking;
using CraftPathOptimizer.UI.Elements;

namespace CraftPathOptimizer.UI.Panels;

/// <summary>
/// Anchored panel showing tracked items and next actions.
/// Appended to the right edge of the main panel (not a separate window).
/// </summary>
public class TrackingPanel : UIPanel
{
	private readonly UIList _list;
	private readonly UIScrollbar _scrollbar;
	private readonly Dictionary<string, bool> _collapsed = [];

	private TrackingState _tracking;
	private List<ActionGroup> _actionPlan;

	public event Action<int> OnGoalClicked;
	public event Action<int> OnGoalUnpinned;
	public event Action OnClearAllGoals;

	public TrackingPanel()
	{
		Width.Set(0, 1f);
		Height.Set(0, 1f);
		BackgroundColor = Color.Transparent;
		BorderColor = Color.Transparent;
		SetPadding(0);

		// Title
		var title = new UIText("Tracking", 0.75f);
		title.Top.Set(2f, 0f);
		title.Left.Set(4f, 0f);
		Append(title);

		float listTop = 24f;

		_list = new UIList();
		_list.Top.Set(listTop, 0f);
		_list.Width.Set(-14f, 1f);
		_list.Height.Set(-listTop, 1f);
		_list.ListPadding = 2f;
		_list.ManualSortMethod = _ => { };
		Append(_list);

		_scrollbar = new UIScrollbar();
		_scrollbar.Top.Set(listTop, 0f);
		_scrollbar.Left.Set(-12f, 1f);
		_scrollbar.Height.Set(-listTop, 1f);
		_list.SetScrollbar(_scrollbar);
		Append(_scrollbar);
	}

	public void SetTracking(TrackingState tracking, List<ActionGroup> actionPlan)
	{
		_tracking = tracking;
		_actionPlan = actionPlan;
		Rebuild();
	}

	private void Rebuild()
	{
		_list.Clear();
		AddTrackedItemsSection();
		AddNextActionsSection();
	}

	private bool IsSectionCollapsed(string name) => _collapsed.GetValueOrDefault(name, false);

	private void AddTrackedItemsSection()
	{
		const string section = "Tracked Items";
		bool collapsed = IsSectionCollapsed(section);
		_list.Add(new UIDashboardSectionHeader(section, collapsed, () =>
		{
			_collapsed[section] = !collapsed;
			Rebuild();
		}));
		if (collapsed) return;

		if (_tracking == null || _tracking.Count == 0)
		{
			_list.Add(new UIEmptyMessage("Pin items to track."));
			return;
		}

		for (int i = 0; i < _tracking.Goals.Count; i++)
		{
			var goal = _tracking.Goals[i];
			var row = new UITrackedGoalRow(goal, i);
			row.OnClicked += index => OnGoalClicked?.Invoke(index);
			row.OnRightClicked += index => OnGoalUnpinned?.Invoke(index);
			_list.Add(row);
		}

		if (_tracking.Count > 0)
		{
			var clearBtn = new UIDashboardClearButton();
			clearBtn.OnLeftClick += (evt, el) => OnClearAllGoals?.Invoke();
			_list.Add(clearBtn);
		}
	}

	private void AddNextActionsSection()
	{
		if (_actionPlan == null || _actionPlan.Count == 0)
			return;

		const string section = "Next Actions";
		bool collapsed = IsSectionCollapsed(section);
		_list.Add(new UIDashboardSectionHeader(section, collapsed, () =>
		{
			_collapsed[section] = !collapsed;
			Rebuild();
		}));
		if (collapsed) return;

		foreach (var group in _actionPlan)
			_list.Add(new UIActionGroupCard(group));
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		// Subtle top divider
		var dims = GetDimensions();
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)dims.X, (int)dims.Y, 1, (int)dims.Height),
			new Color(60, 70, 130) * 0.4f);
	}
}
