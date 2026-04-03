using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;
using ProgressionCompass.Content.Progression;
using ProgressionCompass.UI.Elements;

namespace ProgressionCompass.UI.Panels;

/// <summary>
/// Left-side progression dashboard. Single scrollable column showing:
/// Progression header + Ready to Craft + Almost Ready + Building Toward.
/// Tracked items and actions live in the separate TrackingWindow.
/// </summary>
public class PlayerDashboard : UIElement
{
	private readonly UIList _list;
	private readonly UIScrollbar _scrollbar;

	private readonly Dictionary<string, bool> _collapsed = [];
	private readonly Dictionary<string, bool> _showAll = [];

	// Data
	private ProgressScanResult _progressResult;
	private List<UpgradeItem> _mergedItems;

	// Events
	public event Action<int> OnPinItem;
	public event Action<int> OnAnalyzeItem;
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
		_mergedItems = UpgradeItemBuilder.Build(
			result?.Recommendations,
			result?.Combinations,
			itemType => IsItemTracked?.Invoke(itemType) ?? false);

		// Debug: log what we got
		int recCount = result?.Recommendations?.Count ?? 0;
		int comboCount = result?.Combinations?.Count ?? 0;
		int mergedCount = _mergedItems?.Count ?? 0;
		int readyCount = _mergedItems?.FindAll(i => i.Proximity == Proximity.CraftableNow).Count ?? 0;
		int almostCount = _mergedItems?.FindAll(i => i.Proximity == Proximity.AlmostReady).Count ?? 0;
		Common.Log.Info($"Dashboard update: {recCount} recs, {comboCount} combos, {mergedCount} merged, {readyCount} ready, {almostCount} almost");

		Rebuild();
	}

	public void Rebuild()
	{
		_list.Clear();
		AddProgressionHeader();
		int countBefore = _list.Count;
		AddReadyToCraftSection();
		AddAlmostReadySection();
		AddBuildingTowardSection();

		// If no sections were added, show a helpful hint
		if (_list.Count == countBefore)
		{
			if (_mergedItems == null)
				_list.Add(new UIEmptyMessage("Scanning for upgrades..."));
			else
				_list.Add(new UIEmptyMessage("Gather materials to see craftable upgrades here."));
		}
	}

	/// <summary>
	/// For small recipes (≤2 leaf materials), checks if the player's owned materials
	/// represent meaningful progress — not just owning trivial common materials.
	/// With tree-based data, checks that at least one missing item is non-trivial.
	/// </summary>
	private static bool HasMeaningfulProgress(UpgradeItem item)
	{
		// If we have tree data: owned at least one non-trivial material
		if (item.LeafMaterialsTotal > 0 && item.LeafMaterialsOwned > 0)
		{
			// Check that what we're MISSING isn't all trivial (i.e., what we own is meaningful)
			if (item.MissingItems == null || item.MissingItems.Count == 0)
				return true; // Nothing missing = definitely meaningful
			// If all missing items are trivial, then what we own is the non-trivial part
			foreach (var (matType, _) in item.MissingItems)
				if (!UIHelper.TrivialMaterials.Contains(matType))
					return true; // At least one non-trivial item is still missing AND we have progress
			// All missing items are trivial → we only own trivial stuff → not meaningful
			return false;
		}
		return false;
	}

	private bool IsSectionCollapsed(string name) => _collapsed.GetValueOrDefault(name, false);
	private void ToggleSection(string name)
	{
		_collapsed[name] = !IsSectionCollapsed(name);
		Rebuild();
	}

	private void AddProgressionHeader()
	{
		Tier tier = _progressResult?.CurrentTier ?? ProgressionTier.Detect();
		PlayerClass detected = _progressResult?.DetectedClass ?? PlayerClass.Mixed;

		_list.Add(new UIDashboardProgressHeader(
			tier, detected,
			() => OnOpenRecommendations?.Invoke()
		));
	}

	private const int InitialShowCount = 6;

	// ── Ready to Craft: items you can build RIGHT NOW ──────
	private void AddReadyToCraftSection()
	{
		if (_mergedItems == null) return;

		var ready = _mergedItems.FindAll(i => i.Proximity == Proximity.CraftableNow);
		if (ready.Count == 0) return;

		ready.Sort((a, b) => b.Score.CompareTo(a.Score));

		const string section = "Ready to Craft";
		_list.Add(new UIDashboardSectionHeader(section, IsSectionCollapsed(section), () => ToggleSection(section)));
		if (IsSectionCollapsed(section)) return;

		bool showAll = _showAll.GetValueOrDefault(section, false);
		int limit = showAll ? ready.Count : Math.Min(ready.Count, InitialShowCount);

		for (int i = 0; i < limit; i++)
		{
			_list.Add(new UIUpgradeItemRow(ready[i],
				type => OnAnalyzeItem?.Invoke(type),
				type => OnPinItem?.Invoke(type)));
		}

		AddShowMoreLess(section, ready.Count);
	}

	// ── Almost Ready: 80%+ tree completion ──────────────────
	private void AddAlmostReadySection()
	{
		if (_mergedItems == null) return;

		var almost = _mergedItems.FindAll(i =>
			i.Proximity == Proximity.AlmostReady
			// For small recipes, require owning a non-trivial material
			&& (i.LeafMaterialsTotal > 3 || HasMeaningfulProgress(i)));
		if (almost.Count == 0) return;

		almost.Sort((a, b) => b.CompletionPercent.CompareTo(a.CompletionPercent));

		const string section = "Almost Ready";
		_list.Add(new UIDashboardSectionHeader(section, IsSectionCollapsed(section), () => ToggleSection(section)));
		if (IsSectionCollapsed(section)) return;

		bool showAll = _showAll.GetValueOrDefault(section, false);
		int limit = showAll ? almost.Count : Math.Min(almost.Count, InitialShowCount);

		for (int i = 0; i < limit; i++)
		{
			_list.Add(new UIUpgradeItemRow(almost[i],
				type => OnAnalyzeItem?.Invoke(type),
				type => OnPinItem?.Invoke(type)));
		}

		AddShowMoreLess(section, almost.Count);
	}

	// ── Building Toward: 40%+ completion ────────────────────
	private void AddBuildingTowardSection()
	{
		if (_mergedItems == null) return;

		var building = _mergedItems.FindAll(i =>
			i.Proximity is Proximity.NeedsWork or Proximity.FarAway
			&& i.CompletionPercent >= 40f
			// For small recipes, require owning a non-trivial material
			&& (i.LeafMaterialsTotal > 3 || HasMeaningfulProgress(i)));
		if (building.Count == 0) return;

		building.Sort((a, b) => b.CompletionPercent.CompareTo(a.CompletionPercent));

		const string section = "Building Toward";
		_list.Add(new UIDashboardSectionHeader(section, IsSectionCollapsed(section), () => ToggleSection(section)));
		if (IsSectionCollapsed(section)) return;

		bool showAll = _showAll.GetValueOrDefault(section, false);
		int limit = showAll ? building.Count : Math.Min(building.Count, InitialShowCount);

		for (int i = 0; i < limit; i++)
		{
			_list.Add(new UIUpgradeItemRow(building[i],
				type => OnAnalyzeItem?.Invoke(type),
				type => OnPinItem?.Invoke(type)));
		}

		AddShowMoreLess(section, building.Count);
	}

	private void AddShowMoreLess(string section, int totalCount)
	{
		bool showAll = _showAll.GetValueOrDefault(section, false);
		if (!showAll && totalCount > InitialShowCount)
		{
			int remaining = totalCount - InitialShowCount;
			_list.Add(new Common.Systems.UIShowMoreButton(
				$"Show {remaining} more...", () => { _showAll[section] = true; Rebuild(); }));
		}
		else if (showAll && totalCount > InitialShowCount)
		{
			_list.Add(new Common.Systems.UIShowMoreButton(
				"Show less", () => { _showAll[section] = false; Rebuild(); }));
		}
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

		Utils.DrawBorderString(spriteBatch, ProgressionTier.GetDisplayName(_tier),
			new Vector2(x + 4f, y + 2f), Color.Gold, 0.65f);

		Utils.DrawBorderString(spriteBatch, $"Class: {ClassDetector.GetDisplayName(_detected)}",
			new Vector2(x + 4f, y + 20f), Color.LightGray, 0.55f);

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
public class UIDashboardSectionHeader : UIElement
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

		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)dims.X, (int)dims.Y, (int)dims.Width, 1), new Color(60, 70, 130) * 0.4f);

		if (hovering)
			spriteBatch.Draw(TextureAssets.MagicPixel.Value,
				new Rectangle((int)dims.X, (int)(dims.Y + 1), (int)dims.Width, (int)dims.Height - 1),
				new Color(50, 58, 100) * 0.3f);

		string arrow = _collapsed ? ">" : "v";
		Utils.DrawBorderString(spriteBatch, arrow, new Vector2(dims.X + 4f, dims.Y + 4f),
			hovering ? Color.White : Color.Gray, 0.55f);

		Utils.DrawBorderString(spriteBatch, _title, new Vector2(dims.X + 16f, dims.Y + 4f), Color.Gold, 0.65f);
	}
}

/// <summary>Small "Clear All" button for tracked items section.</summary>
public class UIDashboardClearButton : UIPanel
{
	public UIDashboardClearButton()
	{
		Width.Set(0, 1f);
		Height.Set(22f, 0f);
		BackgroundColor = Color.Transparent;
		BorderColor = Color.Transparent;
		SetPadding(0);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		bool hover = IsMouseHovering;
		if (hover)
			spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(),
				new Color(80, 30, 30) * 0.3f);
		Utils.DrawBorderString(spriteBatch, "Clear All",
			new Vector2(dims.X + dims.Width / 2f - 25f, dims.Y + 3f),
			hover ? new Color(255, 100, 100) : Color.Gray, 0.55f);
	}
}
