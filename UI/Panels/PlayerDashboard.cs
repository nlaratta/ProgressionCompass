using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;
using CraftPathOptimizer.Content.Graph;
using CraftPathOptimizer.Content.Progression;
using CraftPathOptimizer.Content.Tracking;
using CraftPathOptimizer.UI.Elements;

namespace CraftPathOptimizer.UI.Panels;

/// <summary>
/// The right-side player dashboard. Single scrollable list containing:
/// 1. Progression header (tier + class filter + "Upgrades" button)
/// 2. Almost Complete combinations
/// 3. Tracked items with progress bars
/// 4. Next Actions (TODO plan)
/// </summary>
public class PlayerDashboard : UIElement
{
	private readonly UIList _list;
	private readonly UIScrollbar _scrollbar;
	private readonly Dictionary<string, bool> _collapsed = [];

	// Data
	private ProgressScanResult _progressResult;
	private TrackingState _tracking;
	private List<ActionGroup> _actionPlan;

	// Events
	public event Action<int> OnPinItem;
	public event Action<int> OnAnalyzeItem;
	public event Action<int> OnGoalClicked;
	public event Action<int> OnGoalUnpinned;
	public event Action OnClearAllGoals;
	public event Action OnOpenRecommendations;
	public Func<int, bool> IsItemTracked { get; set; }

	public PlayerDashboard()
	{
		Width.Set(0, 1f);
		Height.Set(0, 1f);

		_list = new UIList();
		_list.Width.Set(-14f, 1f);
		_list.Height.Set(0, 1f);
		_list.ListPadding = 2f;
		_list.ManualSortMethod = _ => { };
		Append(_list);

		_scrollbar = new UIScrollbar();
		_scrollbar.Left.Set(-12f, 1f);
		_scrollbar.Height.Set(0, 1f);
		_list.SetScrollbar(_scrollbar);
		Append(_scrollbar);
	}

	public void SetProgressResults(ProgressScanResult result)
	{
		_progressResult = result;
		RebuildAll();
	}

	public void RefreshTracking(TrackingState tracking)
	{
		_tracking = tracking;
		RebuildAll();
	}

	public void SetActionPlan(List<ActionGroup> plan)
	{
		_actionPlan = plan;
		RebuildAll();
	}

	public void RebuildAll()
	{
		_list.Clear();

		AddProgressionHeader();
		AddAlmostCompleteSection();
		AddTrackedItemsSection();
		AddNextActionsSection();
	}

	// ── Progression Header ──────────────────────────────────
	private void AddProgressionHeader()
	{
		Tier tier = _progressResult?.CurrentTier ?? ProgressionTier.Detect();
		PlayerClass detected = _progressResult?.DetectedClass ?? PlayerClass.Mixed;

		_list.Add(new UIDashboardProgressHeader(
			tier, detected,
			() => OnOpenRecommendations?.Invoke()
		));
	}

	private bool IsSectionCollapsed(string name) => _collapsed.GetValueOrDefault(name, false);
	private void ToggleSection(string name)
	{
		_collapsed[name] = !IsSectionCollapsed(name);
		RebuildAll();
	}

	// ── Almost Complete ─────────────────────────────────────
	private void AddAlmostCompleteSection()
	{
		if (_progressResult?.Combinations == null || _progressResult.Combinations.Count == 0)
			return;

		var filtered = _progressResult.Combinations;
		if (filtered.Count == 0) return;

		const string section = "Almost Complete";
		_list.Add(new UIDashboardSectionHeader(section, IsSectionCollapsed(section), () => ToggleSection(section)));
		if (IsSectionCollapsed(section)) return;

		foreach (var combo in filtered)
		{
			bool tracked = IsItemTracked?.Invoke(combo.ItemType) ?? false;
			var row = new UICombinationRow(combo,
				type => OnPinItem?.Invoke(type),
				type => OnAnalyzeItem?.Invoke(type),
				tracked);
			_list.Add(row);
		}
	}

	// ── Tracked Items ───────────────────────────────────────
	private void AddTrackedItemsSection()
	{
		const string section = "Tracked Items";
		_list.Add(new UIDashboardSectionHeader(section, IsSectionCollapsed(section), () => ToggleSection(section)));
		if (IsSectionCollapsed(section)) return;

		if (_tracking == null || _tracking.Count == 0)
		{
			_list.Add(new UIEmptyMessage("Pin items from search to track."));
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

	// ── Next Actions ────────────────────────────────────────
	private void AddNextActionsSection()
	{
		if (_actionPlan == null || _actionPlan.Count == 0)
			return;

		const string section = "Next Actions";
		_list.Add(new UIDashboardSectionHeader(section, IsSectionCollapsed(section), () => ToggleSection(section)));
		if (IsSectionCollapsed(section)) return;

		foreach (var group in _actionPlan)
			_list.Add(new UIActionGroupCard(group));
	}
}

// ── Dashboard-specific UI elements ──────────────────────────────

/// <summary>Progression header with tier, detected class, and "Upgrades" button.</summary>
internal class UIDashboardProgressHeader : UIElement
{
	private readonly Tier _tier;
	private readonly PlayerClass _detected;
	private readonly Action _onOpenUpgrades;

	public UIDashboardProgressHeader(Tier tier, PlayerClass detected, Action onOpenUpgrades)
	{
		_tier = tier;
		_detected = detected;
		_onOpenUpgrades = onOpenUpgrades;
		Width.Set(0, 1f);
		Height.Set(38f, 0f);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		float x = dims.X;
		float y = dims.Y;

		spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(), new Color(25, 30, 55) * 0.5f);

		// Tier
		Utils.DrawBorderString(spriteBatch, ProgressionTier.GetDisplayName(_tier),
			new Vector2(x + 4f, y + 2f), Color.Gold, 0.65f);

		// Detected class
		Utils.DrawBorderString(spriteBatch, $"Class: {ClassDetector.GetDisplayName(_detected)}",
			new Vector2(x + 4f, y + 20f), Color.LightGray, 0.55f);

		// "Upgrades >>" button (right side)
		float upgBtnX = x + dims.Width - 82f;
		bool hoverUpg = Main.mouseX >= upgBtnX && Main.mouseX < upgBtnX + 78
			&& Main.mouseY >= y + 8 && Main.mouseY < y + 28;
		Color upgBg = hoverUpg ? new Color(50, 60, 120) : new Color(35, 40, 80);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)upgBtnX, (int)(y + 9f), 78, 20), upgBg);
		Utils.DrawBorderString(spriteBatch, "Upgrades >>", new Vector2(upgBtnX + 6f, y + 10f),
			hoverUpg ? Color.White : Color.LightGray, 0.55f);
		if (hoverUpg && Main.mouseLeft && Main.mouseLeftRelease)
			_onOpenUpgrades?.Invoke();
	}
}

/// <summary>Collapsible section header. Click to toggle.</summary>
internal class UIDashboardSectionHeader : UIElement
{
	private readonly string _title;
	private readonly bool _collapsed;
	private readonly Action _onToggle;

	public UIDashboardSectionHeader(string title, bool collapsed = false, Action onToggle = null)
	{
		_title = title;
		_collapsed = collapsed;
		_onToggle = onToggle;
		Width.Set(0, 1f);
		Height.Set(22f, 0f);
	}

	public override void LeftClick(UIMouseEvent evt)
	{
		base.LeftClick(evt);
		_onToggle?.Invoke();
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		bool hovering = IsMouseHovering;

		// Divider line
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)dims.X, (int)dims.Y, (int)dims.Width, 1), new Color(60, 70, 130) * 0.4f);

		// Background on hover
		if (hovering)
			spriteBatch.Draw(TextureAssets.MagicPixel.Value,
				new Rectangle((int)dims.X, (int)(dims.Y + 1), (int)dims.Width, (int)dims.Height - 1),
				new Color(50, 58, 100) * 0.3f);

		// Collapse arrow
		string arrow = _collapsed ? ">" : "v";
		Utils.DrawBorderString(spriteBatch, arrow, new Vector2(dims.X + 4f, dims.Y + 4f),
			hovering ? Color.White : Color.Gray, 0.55f);

		// Title
		Utils.DrawBorderString(spriteBatch, _title, new Vector2(dims.X + 16f, dims.Y + 4f), Color.Gold, 0.65f);
	}
}

/// <summary>Small "Clear All" button for tracked items section.</summary>
internal class UIDashboardClearButton : UIElement
{
	public UIDashboardClearButton()
	{
		Width.Set(0, 1f);
		Height.Set(20f, 0f);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		bool hover = IsMouseHovering;
		Utils.DrawBorderString(spriteBatch, "Clear All",
			new Vector2(dims.X + dims.Width / 2f - 25f, dims.Y + 2f),
			hover ? new Color(255, 100, 100) : Color.Gray, 0.55f);
	}
}
