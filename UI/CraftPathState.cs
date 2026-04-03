using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using ProgressionCompass.Common;
using ProgressionCompass.Common.Systems;
using ProgressionCompass.Content.Graph;
using ProgressionCompass.Common.Players;
using ProgressionCompass.Content.Progression;
using ProgressionCompass.Content.Tracking;
using ProgressionCompass.UI.Elements;
using ProgressionCompass.UI.Panels;

namespace ProgressionCompass.UI;

public class CraftPathState : UIState
{
	// Layout
	private UIDraggablePanel _mainPanel;
	private TargetSelectionPanel _targetPanel;
	private UIAnalysisIndicator _analysisIndicator;
	private UITabBar _tabBar;
	private MaterialListPanel _materialPanel;
	private DependencyTreePanel _treePanel;
	private CraftOrderPanel _craftOrderPanel;
	private PlayerDashboard _dashboard;
	private TrackingPanel _trackingPanel;
	private UIText _chestToggleText;
	private UIPanel _chestToggleBtn;
	private UIStatusBar _statusBar;

	private bool _chestsEnabled;
	private bool _resolving;
	private global::ProgressionCompass.Content.Progression.WorldContext _lastWorldContext;
	private float _contentLeft; // Left offset for center column panels

	// Recipe preferences: itemType → preferred recipe index in Main.recipe[]
	private Dictionary<int, int> _recipePreferences = [];

	// Analysis (single item view for Materials/Tree/Craft Order tabs)
	private readonly TargetQueue _queue = new();
	private MultiTargetResult _multiResult;
	private MultiTargetResult _pendingResult;
	private Dictionary<int, InventoryEntry> _lastInventory;

	// Progress tab (async scan + cache)
	private ProgressScanResult _pendingProgressResult;
	private ProgressScanResult _cachedProgressResult;
	private bool _scanningProgress;

	// Tracking
	private TrackingState Tracking
	{
		get
		{
			try { return Main.LocalPlayer?.GetModPlayer<TrackingPlayer>()?.Tracking; }
			catch { return null; }
		}
	}

	private const float DashboardWidth = 260f;
	private const float TrackingWidth = 230f;

	public override void OnInitialize()
	{
		_mainPanel = new UIDraggablePanel();
		_mainPanel.Width.Set(980f, 0f);
		_mainPanel.Height.Set(620f, 0f);
		_mainPanel.HAlign = 0.5f;
		_mainPanel.VAlign = 0.5f;
		_mainPanel.SetPadding(12f);
		_mainPanel.BackgroundColor = new Color(33, 37, 68) * 0.95f;
		_mainPanel.BorderColor = new Color(60, 70, 130);
		Append(_mainPanel);

		// Three-column layout: [Dashboard | Content | Tracking]
		_contentLeft = DashboardWidth + 8f;
		float contentLeft = _contentLeft;
		float contentRightMargin = TrackingWidth + 8f;
		float contentW = -(contentLeft + contentRightMargin); // 100% minus both sides
		float yPos = 0f;
		float statusBarH = 30f;
		float columnH = -34f - statusBarH - 16f; // Height for side columns

		// ── Header row (spans content area) ─────────────────────
		var title = new UIText("Progression Compass", 0.9f);
		title.Top.Set(yPos + 2f, 0f);
		title.Left.Set(contentLeft, 0f);
		_mainPanel.Append(title);

		_chestToggleBtn = new UIPanel();
		_chestToggleBtn.Width.Set(108f, 0f);
		_chestToggleBtn.Height.Set(28f, 0f);
		_chestToggleBtn.Top.Set(yPos, 0f);
		_chestToggleBtn.Left.Set(-(contentRightMargin + 148f), 1f);
		_chestToggleBtn.BackgroundColor = new Color(44, 50, 88);
		_chestToggleBtn.BorderColor = new Color(70, 80, 140);
		_chestToggleBtn.SetPadding(0);
		_chestToggleText = new UIText("Chests: OFF", 0.75f);
		_chestToggleText.HAlign = 0.5f;
		_chestToggleText.VAlign = 0.5f;
		_chestToggleBtn.Append(_chestToggleText);
		_chestToggleBtn.OnLeftClick += (evt, el) =>
		{
			_chestsEnabled = !_chestsEnabled;
			SyncChestToggle();
			_chestToggleText.SetText(_chestsEnabled ? "Chests: ON" : "Chests: OFF");
			_chestToggleBtn.BackgroundColor = _chestsEnabled ? new Color(40, 70, 120) : new Color(44, 50, 88);
			_queue.InvalidateCache();
			global::ProgressionCompass.Content.Progression.CacheVersion.Bump();
			if (_queue.Count > 0) RunResolver();
			try { Main.LocalPlayer?.GetModPlayer<TrackingPlayer>()?.RefreshTracking(); } catch { }
			_cachedProgressResult = null;
			_lastInventory = null;
			RunProgressScan();
			RefreshDashboard();
		};
		_mainPanel.Append(_chestToggleBtn);

		var closeBtn = new UIPanel();
		closeBtn.Width.Set(28f, 0f);
		closeBtn.Height.Set(28f, 0f);
		closeBtn.Top.Set(yPos, 0f);
		closeBtn.Left.Set(-30f, 1f);
		closeBtn.BackgroundColor = new Color(80, 30, 30);
		closeBtn.BorderColor = new Color(140, 50, 50);
		closeBtn.SetPadding(0);
		var closeText = new UIText("X", 0.8f);
		closeText.HAlign = 0.5f;
		closeText.VAlign = 0.5f;
		closeBtn.Append(closeText);
		closeBtn.OnLeftClick += (evt, el) => ModContent.GetInstance<CraftPathUISystem>().ToggleUI();
		_mainPanel.Append(closeBtn);

		yPos += 34f;

		// ── Dashboard (LEFT column) ─────────────────────────────
		var dashPanel = new UIPanel();
		dashPanel.Top.Set(34f, 0f);
		dashPanel.Left.Set(0f, 0f);
		dashPanel.Width.Set(DashboardWidth, 0f);
		dashPanel.Height.Set(columnH, 1f);
		dashPanel.BackgroundColor = new Color(28, 32, 58) * 0.9f;
		dashPanel.BorderColor = new Color(50, 60, 110);
		dashPanel.SetPadding(4);
		_mainPanel.Append(dashPanel);

		_dashboard = new PlayerDashboard();
		_dashboard.Width.Set(0, 1f);
		_dashboard.Height.Set(0, 1f);
		_dashboard.OnPinItem += OnItemPinned;
		_dashboard.OnAnalyzeItem += OnDashboardAnalyze;
		_dashboard.OnOpenRecommendations += OnOpenRecommendations;
		_dashboard.IsItemTracked = itemType => Tracking?.IsTracked(itemType) ?? false;
		dashPanel.Append(_dashboard);

		// ── Tracking (RIGHT column) ─────────────────────────────
		var trackPanel = new UIPanel();
		trackPanel.Top.Set(34f, 0f);
		trackPanel.Left.Set(-TrackingWidth, 1f);
		trackPanel.Width.Set(TrackingWidth, 0f);
		trackPanel.Height.Set(columnH, 1f);
		trackPanel.BackgroundColor = new Color(28, 32, 58) * 0.9f;
		trackPanel.BorderColor = new Color(50, 60, 110);
		trackPanel.SetPadding(4);
		_mainPanel.Append(trackPanel);

		_trackingPanel = new TrackingPanel();
		_trackingPanel.OnGoalClicked += OnDashboardGoalClicked;
		_trackingPanel.OnGoalUnpinned += OnDashboardGoalUnpinned;
		_trackingPanel.OnClearAllGoals += OnDashboardClearAll;
		trackPanel.Append(_trackingPanel);

		// ── Search (CENTER column) ──────────────────────────────
		_targetPanel = new TargetSelectionPanel();
		_targetPanel.Top.Set(yPos, 0f);
		_targetPanel.Left.Set(contentLeft, 0f);
		_targetPanel.Width.Set(contentW, 1f);
		_targetPanel.Height.Set(150f, 0f);
		_targetPanel.OnItemSelected += OnItemSelected;
		_targetPanel.OnItemPinned += OnItemPinned;
		_targetPanel.IsItemTracked = itemType => Tracking?.IsTracked(itemType) ?? false;
		_mainPanel.Append(_targetPanel);

		yPos += 156f;

		// ── Analysis indicator ──────────────────────────────────
		_analysisIndicator = new UIAnalysisIndicator();
		_analysisIndicator.Top.Set(yPos, 0f);
		_analysisIndicator.Left.Set(contentLeft, 0f);
		_analysisIndicator.Width.Set(contentW, 1f);
		_analysisIndicator.IsItemTracked = itemType => Tracking?.IsTracked(itemType) ?? false;
		_analysisIndicator.OnClearClicked += () =>
		{
			_queue.Clear();
			_multiResult = null;
			_analysisIndicator.Clear();
			UpdateActivePanel();
		};
		_analysisIndicator.OnPinClicked += OnItemPinned;
		_mainPanel.Append(_analysisIndicator);

		yPos += 32f;

		// ── Tab bar ─────────────────────────────────────────────
		_tabBar = new UITabBar();
		_tabBar.Top.Set(yPos, 0f);
		_tabBar.Left.Set(contentLeft, 0f);
		_tabBar.Width.Set(contentW, 1f);
		_tabBar.AddTab("Materials");
		_tabBar.AddTab("Tree");
		_tabBar.AddTab("Craft Order");
		_tabBar.OnTabSelected += _ => UpdateActivePanel();
		_mainPanel.Append(_tabBar);

		yPos += 34f;

		// ── Content panels (CENTER column, stacked) ─────────────
		float contentH = -(yPos + statusBarH + 16f);

		_materialPanel = new MaterialListPanel();
		_materialPanel.Top.Set(yPos, 0f);
		_materialPanel.Left.Set(contentLeft, 0f);
		_materialPanel.Width.Set(contentW, 1f);
		_materialPanel.Height.Set(contentH, 1f);
		_materialPanel.OnAnalyzeItem += OnDashboardAnalyze;
		_mainPanel.Append(_materialPanel);

		_treePanel = new DependencyTreePanel();
		_treePanel.Top.Set(yPos, 0f);
		_treePanel.Left.Set(contentLeft, 0f);
		_treePanel.Width.Set(contentW, 1f);
		_treePanel.Height.Set(contentH, 1f);
		_treePanel.SetRecipePreferences(_recipePreferences);
		_treePanel.OnRecipeCycled += OnRecipeCycled;
		_mainPanel.Append(_treePanel);

		_craftOrderPanel = new CraftOrderPanel();
		_craftOrderPanel.Top.Set(yPos, 0f);
		_craftOrderPanel.Left.Set(contentLeft, 0f);
		_craftOrderPanel.Width.Set(contentW, 1f);
		_craftOrderPanel.Height.Set(contentH, 1f);
		_mainPanel.Append(_craftOrderPanel);

		// ── Status bar (bottom, full width) ─────────────────────
		_statusBar = new UIStatusBar();
		_statusBar.Top.Set(-statusBarH - 2f, 1f);
		_statusBar.Width.Set(0, 1f);
		_statusBar.Height.Set(statusBarH, 0f);
		_mainPanel.Append(_statusBar);

		UpdateActivePanel();
	}

	// ── Item Analysis ───────────────────────────────────────────

	/// <summary>Called from keybind: inspect a specific item (from hovering in game UI).</summary>
	public void InspectItem(int itemType)
	{
		if (itemType <= 0) return;
		_queue.Clear();
		_queue.Add(itemType);
		_analysisIndicator.SetItem(itemType);
		_tabBar.SelectTab(0); // Switch to Materials tab
		RunResolver();
	}

	private void OnItemSelected(int itemType)
	{
		_queue.Clear();
		_queue.Add(itemType);
		_analysisIndicator.SetItem(itemType);
		RunResolver();
	}

	private void OnRecipeCycled(int itemType, int alternativeIndex)
	{
		_recipePreferences[itemType] = alternativeIndex;
		_queue.InvalidateCache();
		global::ProgressionCompass.Content.Progression.CacheVersion.Bump();
		if (_queue.Count > 0) RunResolver();
	}

	private void OnDashboardAnalyze(int itemType)
	{
		_queue.Clear();
		_queue.Add(itemType);
		_analysisIndicator.SetItem(itemType);
		RunResolver();
		_tabBar.SelectTab(0);
	}

	private void OnDashboardGoalClicked(int index)
	{
		var tracking = Tracking;
		if (tracking == null || index >= tracking.Goals.Count) return;
		var goal = tracking.Goals[index];
		_queue.Clear();
		_queue.Add(goal.ItemType, goal.Quantity);
		_analysisIndicator.SetItem(goal.ItemType);
		RunResolver();
	}

	// ── Pinning / Tracking ──────────────────────────────────────

	private void OnItemPinned(int itemType)
	{
		var tracking = Tracking;
		if (tracking == null) return;

		if (tracking.IsTracked(itemType))
			tracking.UnpinByType(itemType);
		else
			tracking.Pin(itemType);

		try
		{
			Main.LocalPlayer?.GetModPlayer<TrackingPlayer>()?.RefreshTracking();
		}
		catch
		{
			var recipeIndex = ModContent.GetInstance<RecipeIndexSystem>();
			var sourceSystem = ModContent.GetInstance<ItemSourceSystem>();
			if (recipeIndex?.RecipesByResult != null)
			{
				var inv = InventoryScanner.Scan(_chestsEnabled);
				tracking.Refresh(recipeIndex, sourceSystem, inv, _chestsEnabled);
			}
		}
		RefreshDashboard();
	}

	private void OnDashboardGoalUnpinned(int index)
	{
		Tracking?.Unpin(index);
		try { Main.LocalPlayer?.GetModPlayer<TrackingPlayer>()?.RefreshTracking(); } catch { }
		RefreshDashboard();
	}

	private void OnDashboardClearAll()
	{
		Tracking?.Clear();
		RefreshDashboard();
	}

	private void OnOpenRecommendations()
	{
		var system = ModContent.GetInstance<UpgradeWindowSystem>();
		if (system == null) return;
		system.Toggle();
		if (system.IsVisible)
		{
			PlayerClass savedFilter = PlayerClass.Mixed;
			try
			{
				var tp = Main.LocalPlayer?.GetModPlayer<TrackingPlayer>();
				if (tp != null) savedFilter = tp.SavedClassFilter;
			}
			catch { }

			system.WindowState.SetResults(
				_cachedProgressResult?.Recommendations,
				savedFilter,
				_cachedProgressResult?.Stats,
				_cachedProgressResult?.NextBosses,
				_cachedProgressResult?.Combinations,
				_cachedProgressResult?.WorldContext ?? default,
				_cachedProgressResult?.Effort);

			system.WindowState.OnAnalyzeItem -= OnDashboardAnalyze;
			system.WindowState.OnAnalyzeItem += OnDashboardAnalyze;
			system.WindowState.OnPinItem -= OnItemPinned;
			system.WindowState.OnPinItem += OnItemPinned;
		}
	}

	// ── Dashboard Refresh ───────────────────────────────────────

	private void RefreshDashboard()
	{
		if (_cachedProgressResult != null)
			_dashboard.SetProgressResults(_cachedProgressResult);

		var tracking = Tracking;
		_trackingPanel?.SetTracking(tracking, tracking?.ActionPlan);
	}

	private void SyncChestToggle()
	{
		try
		{
			var tp = Main.LocalPlayer?.GetModPlayer<TrackingPlayer>();
			if (tp != null)
			{
				tp.ChestsEnabled = _chestsEnabled;
				tp.RecipePreferences = _recipePreferences;
			}
		}
		catch { }
	}

	// ── Resolver (async) ────────────────────────────────────────

	private void RunResolver()
	{
		if (_queue.Count == 0 || _resolving) return;

		var recipeIndex = ModContent.GetInstance<RecipeIndexSystem>();
		var sourceSystem = ModContent.GetInstance<ItemSourceSystem>();
		if (recipeIndex?.RecipesByResult == null) return;
		var config = ModContent.GetInstance<CraftPathConfig>();
		float chestRange = config?.ChestScanRange ?? InventoryScanner.DefaultChestRangeTiles;

		var snap = global::ProgressionCompass.Content.Progression.SystemSnapshot.Capture(_chestsEnabled, chestRange);
		_lastInventory = snap.Inventory;
		_lastWorldContext = snap.World;

		_resolving = true;

		Task.Run(() =>
		{
			try
			{
				var inventoryCopy = new Dictionary<int, InventoryEntry>(snap.Inventory);
				var result = _queue.ResolveAll(recipeIndex, sourceSystem, inventoryCopy, snap.IncludeChests, _recipePreferences, snap.World, snap.PlayerTier);
				_pendingResult = result;
			}
			catch (Exception ex)
			{
				Common.Log.Warn($"Resolver error: {ex.Message}");
			}
			finally { _resolving = false; }
		});
	}

	// ── Progress Scan (async) ───────────────────────────────────

	private void RunProgressScan()
	{
		if (_scanningProgress) return;

		var recipeIndex = ModContent.GetInstance<RecipeIndexSystem>();
		var sourceSystem = ModContent.GetInstance<ItemSourceSystem>();
		if (recipeIndex?.RecipesByResult == null) return;

		var config = ModContent.GetInstance<CraftPathConfig>();
		float chestRange = config?.ChestScanRange ?? InventoryScanner.DefaultChestRangeTiles;

		// Single snapshot captures ALL main-thread state at one consistent point
		var snap = global::ProgressionCompass.Content.Progression.SystemSnapshot.Capture(_chestsEnabled, chestRange);
		_lastInventory = snap.Inventory;

		var nextBosses = BossDatabase.GetNextBosses(snap.PlayerTier, sourceSystem, snap.World);

		_scanningProgress = true;

		Task.Run(() =>
		{
			try
			{
				var invCopy = new Dictionary<int, InventoryEntry>(snap.Inventory);
				var effort = new global::ProgressionCompass.Content.Progression.EffortAnalyzer(sourceSystem, snap.World);
				var combos = CombinationScanner.Scan(recipeIndex, sourceSystem, invCopy, snap.IncludeChests, snap.PlayerTier, snap.World);
				var recs = GearRecommender.Recommend(snap.ArmorDefense, PlayerClass.Mixed, snap.PlayerTier, recipeIndex, sourceSystem, invCopy, snap.IncludeChests, snap.World, effort);

				// Resolve each recommended item not already covered by CombinationScanner
				var comboTypes = new HashSet<int>();
				foreach (var c in combos)
					comboTypes.Add(c.ItemType);

				var resolver = new global::ProgressionCompass.Content.Graph.DependencyResolver(
					recipeIndex, sourceSystem, worldContext: snap.World, currentTier: snap.PlayerTier);

				foreach (var rec in recs)
				{
					if (comboTypes.Contains(rec.ItemType))
						continue;

					try
					{
						var inv = new Dictionary<int, InventoryEntry>(snap.Inventory);
						var result = resolver.Resolve(rec.ItemType, 1, inv, snap.IncludeChests);
						if (result?.Materials == null || result.Materials.Count == 0)
							continue;

						int owned = 0;
						int total = result.Materials.Count;
						var missing = new List<(int, int)>();
						foreach (var mat in result.Materials)
						{
							if (mat.IsComplete(snap.IncludeChests))
								owned++;
							else
								missing.Add((mat.ItemType, mat.Deficit(snap.IncludeChests)));
						}

						combos.Add(new global::ProgressionCompass.Content.Progression.CombinationResult
						{
							ItemType = rec.ItemType,
							LeafMaterialsOwned = owned,
							LeafMaterialsTotal = total,
							MissingItems = missing,
							IsAccessory = rec.Category == global::ProgressionCompass.Content.Progression.GearCategory.Accessory,
							IsWeapon = rec.Category == global::ProgressionCompass.Content.Progression.GearCategory.Weapon,
							IsArmor = rec.Category == global::ProgressionCompass.Content.Progression.GearCategory.Armor
						});
					}
					catch { }
				}

				_pendingProgressResult = new ProgressScanResult
				{
					Combinations = combos,
					Recommendations = recs,
					CurrentTier = snap.PlayerTier,
					DetectedClass = snap.DetectedClass,
					Stats = snap.Stats,
					NextBosses = nextBosses,
					WorldContext = snap.World,
					Effort = effort,
					Version = global::ProgressionCompass.Content.Progression.CacheVersion.Current
				};
			}
			catch (Exception ex)
			{
				Common.Log.Warn($"Progress scan error: {ex.Message}");
			}
			finally { _scanningProgress = false; }
		});
	}

	// ── Update Loop ─────────────────────────────────────────────

	private int _progressRefreshTimer;
	private const int ProgressRefreshInterval = 300;

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);

		// Keep queue indicator current
		if (_analysisIndicator != null)
			_analysisIndicator.QueueCount = _queue.Count;

		if (_pendingResult != null)
		{
			_multiResult = _pendingResult;
			_pendingResult = null;
			UpdateActivePanel();
		}

		if (_pendingProgressResult != null)
		{
			_cachedProgressResult = _pendingProgressResult;
			_pendingProgressResult = null;
			RefreshDashboard();
		}

		_progressRefreshTimer++;
		if (_progressRefreshTimer >= ProgressRefreshInterval && !_scanningProgress)
		{
			_progressRefreshTimer = 0;
			_cachedProgressResult = null;
			_lastInventory = null;
			RunProgressScan();
		}
	}

	// ── Tab Switching ───────────────────────────────────────────

	private void UpdateActivePanel()
	{
		_materialPanel.Left.Set(_tabBar.SelectedIndex == 0 ? _contentLeft : 9999f, 0f);
		_treePanel.Left.Set(_tabBar.SelectedIndex == 1 ? _contentLeft : 9999f, 0f);
		_craftOrderPanel.Left.Set(_tabBar.SelectedIndex == 2 ? _contentLeft : 9999f, 0f);

		if (_resolving)
		{
			_statusBar.SetComputing();
			return;
		}

		if (_multiResult == null || _multiResult.PerTarget.Count == 0)
		{
			_statusBar.Clear();
			_materialPanel.SetResults(null, _chestsEnabled, _lastWorldContext);
			_treePanel.SetResult(null);
			_craftOrderPanel.SetResult(null);
			return;
		}

		int focused = Math.Clamp(_queue.FocusedIndex, 0, _multiResult.PerTarget.Count - 1);
		var focusedResult = _multiResult.PerTarget[focused];

		_statusBar.Update(_multiResult.MergedMaterials, _chestsEnabled);

		var materialsToShow = _multiResult.PerTarget.Count > 1
			? _multiResult.MergedMaterials
			: focusedResult.Materials;
		var displayResult = new ResolverResult
		{
			Tree = focusedResult.Tree,
			Materials = materialsToShow,
			CraftOrder = focusedResult.CraftOrder
		};
		_materialPanel.SetResults(displayResult, _chestsEnabled, _lastWorldContext);
		_treePanel.SetResult(focusedResult);
		_craftOrderPanel.SetResult(focusedResult);
	}

	// ── Activate ────────────────────────────────────────────────

	private bool _configApplied;

	public override void OnActivate()
	{
		if (!_configApplied)
		{
			var config = ModContent.GetInstance<CraftPathConfig>();
			if (config != null)
			{
				_chestsEnabled = config.ChestScanDefault;

				// Apply default tab from config
				int defaultTab = config.DefaultTab switch
				{
					TabOption.Tree => 1,
					TabOption.CraftOrder => 2,
					_ => 0
				};
				_tabBar.SelectTab(defaultTab);
			}
			_configApplied = true;
		}

		// Restore recipe preferences from TrackingPlayer (persists across UI toggles)
		try
		{
			var tp = Main.LocalPlayer?.GetModPlayer<TrackingPlayer>();
			if (tp?.RecipePreferences != null && tp.RecipePreferences.Count > 0)
			{
				_recipePreferences = tp.RecipePreferences;
				_treePanel?.SetRecipePreferences(_recipePreferences);
			}
		}
		catch { }

		SyncChestToggle();

		// Sync chest toggle button visual with actual state
		_chestToggleText?.SetText(_chestsEnabled ? "Chests: ON" : "Chests: OFF");
		if (_chestToggleBtn != null)
			_chestToggleBtn.BackgroundColor = _chestsEnabled ? new Color(40, 70, 120) : new Color(44, 50, 88);

		if (_queue.Count > 0)
			RunResolver();

		_cachedProgressResult = null;
		RunProgressScan();
		RefreshDashboard();
	}
}

// ── Helper UI elements ────────────────────────────────────────────

internal class UIStatusBar : UIElement
{
	private string _text = "";

	public void SetComputing() => _text = "Computing craft path...";
	public void Clear() => _text = "";

	public void Update(List<MaterialEntry> materials, bool includeChests)
	{
		if (materials == null || materials.Count == 0)
		{
			_text = "";
			return;
		}

		int total = materials.Count;
		int totalItems = 0;
		int owned = 0;
		foreach (var m in materials)
		{
			totalItems += m.QuantityNeeded;
			if (m.IsComplete(includeChests))
				owned++;
		}

		int missing = total - owned;
		int pct = total > 0 ? (owned * 100 / total) : 0;
		_text = $"Total: {total} types, {totalItems} items  |  Owned: {owned} ({pct}%)  |  Missing: {missing}";
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(), new Color(22, 26, 50) * 0.9f);

		if (!string.IsNullOrEmpty(_text))
			Utils.DrawBorderString(spriteBatch, _text, new Vector2(dims.X + 8f, dims.Y + 5f), Color.LightGray, 0.7f);
	}
}
