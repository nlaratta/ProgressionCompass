using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using ProgressionCompass.Content.Progression;
using ProgressionCompass.Content.Tracking;
using ProgressionCompass.UI.Elements;
using ProgressionCompass.UI.Panels;

namespace ProgressionCompass.Common.Systems;

/// <summary>
/// Manages the separate Upgrade Recommendations window.
/// Follows the same pattern as TrackingOverlaySystem.
/// </summary>
public class UpgradeWindowSystem : ModSystem
{
	internal UserInterface WindowInterface;
	internal UpgradeRecommendationsState WindowState;
	private GameTime _lastGameTime;

	public bool IsVisible => WindowInterface?.CurrentState != null;

	/// <summary>True if the upgrade window is consuming the mouse this frame.</summary>
	public bool IsMouseBlocked { get; private set; }

	public override void Load()
	{
		if (Main.dedServ) return;
		WindowInterface = new UserInterface();
		WindowState = new UpgradeRecommendationsState();
		WindowState.Activate();
	}

	public override void Unload()
	{
		WindowState = null;
		WindowInterface = null;
	}

	public void Toggle()
	{
		if (IsVisible)
			WindowInterface.SetState(null);
		else
			WindowInterface.SetState(WindowState);
	}

	public void Close() => WindowInterface?.SetState(null);

	public override void UpdateUI(GameTime gameTime)
	{
		_lastGameTime = gameTime;
		IsMouseBlocked = false;
		if (WindowInterface?.CurrentState != null)
		{
			WindowInterface.Update(gameTime);
			// Check if the upgrade window is under the mouse — block main panel
			if (WindowState != null)
			{
				var panel = WindowState.GetPanel();
				if (panel != null && panel.ContainsPoint(Main.MouseScreen))
					IsMouseBlocked = true;
			}
		}
	}

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		if (WindowInterface?.CurrentState == null)
			return;

		int index = layers.FindIndex(l => l.Name.Equals("Vanilla: Mouse Text"));
		if (index != -1)
		{
			layers.Insert(index, new LegacyGameInterfaceLayer(
				"ProgressionCompass: Upgrade Recommendations",
				delegate
				{
					WindowInterface.Draw(Main.spriteBatch, _lastGameTime);
					return true;
				},
				InterfaceScaleType.UI));
		}
	}
}

/// <summary>
/// UIState wrapper for the upgrade recommendations window.
/// </summary>
public class UpgradeRecommendationsState : UIState
{
	private UpgradeRecommendationsPanel _panel;

	public event Action<int> OnAnalyzeItem;

	public UIElement GetPanel() => _panel;

	public override void OnInitialize()
	{
		_panel = new UpgradeRecommendationsPanel();
		_panel.HAlign = 0.3f;
		_panel.VAlign = 0.4f;
		_panel.OnAnalyzeItem += type => OnAnalyzeItem?.Invoke(type);
		_panel.OnCloseClicked += () => ModContent.GetInstance<UpgradeWindowSystem>()?.Close();
		Append(_panel);
	}

	public void SetResults(List<GearRecommendation> recs, PlayerClass filter,
		PlayerStats stats = null, List<BossInfo> nextBosses = null,
		List<CombinationResult> combos = null,
		WorldContext worldContext = default)
	{
		if (_panel != null)
			_panel.SetResults(recs, filter, stats, nextBosses, combos, worldContext);
	}
}

/// <summary>
/// The upgrade recommendations panel — 4-column layout:
/// Ready to Craft | Nearly Ready | Building Toward | Where to Go
/// </summary>
internal class UpgradeRecommendationsPanel : UI.Elements.UIDraggablePanel
{
	// 3 readiness columns + 1 source priority column
	private Terraria.GameContent.UI.Elements.UIList _readyList;
	private Terraria.GameContent.UI.Elements.UIList _nearlyList;
	private Terraria.GameContent.UI.Elements.UIList _buildingList;
	private Terraria.GameContent.UI.Elements.UIList _sourceList;
	private Terraria.GameContent.UI.Elements.UIScrollbar _readyScroll;
	private Terraria.GameContent.UI.Elements.UIScrollbar _nearlyScroll;
	private Terraria.GameContent.UI.Elements.UIScrollbar _buildingScroll;
	private Terraria.GameContent.UI.Elements.UIScrollbar _sourceScroll;

	private UIClassFilterBar _classFilter;

	private PlayerClass _activeFilter = PlayerClass.Mixed;
	private List<GearRecommendation> _allRecs;
	private List<CombinationResult> _allCombos;
	private List<UpgradeItem> _mergedItems;
	private SourcePriorityResult _sourcePriority;
	private PlayerStats _stats;
	private List<BossInfo> _bosses;
	private WorldContext _worldContext;

	private UI.Elements.UIPlayerStageCard _stageCard;
	private UI.Elements.UIBossProgressionCard _bossCard;

	public event Action<int> OnAnalyzeItem;
	public event Action OnCloseClicked;

	private const float PanelW = 1080f;
	private const float PanelH = 680f;

	// Column layout: 3 narrow + 1 wide
	private const float Col1Pct = 0.17f;   // Ready
	private const float Col2Pct = 0.17f;   // Nearly
	private const float Col3Pct = 0.17f;   // Building
	// Col4 = 1 - Col1 - Col2 - Col3 = 0.49  (Where to Go)
	private const float ColGap = 4f;

	public UpgradeRecommendationsPanel()
	{
		Width.Set(PanelW, 0f);
		Height.Set(PanelH, 0f);
		SetPadding(10f);
		BackgroundColor = new Color(33, 37, 68) * 0.95f;
		BorderColor = new Color(60, 70, 130);
		BuildLayout();
	}

	private void BuildLayout()
	{
		RemoveAllChildren();

		Width.Set(PanelW, 0f);
		Height.Set(PanelH, 0f);

		// Title
		var title = new Terraria.GameContent.UI.Elements.UIText("Progression & Upgrades", 0.8f);
		title.Top.Set(2f, 0f);
		Append(title);

		// Close button
		var closeBtn = new Terraria.GameContent.UI.Elements.UIPanel();
		closeBtn.Width.Set(24f, 0f);
		closeBtn.Height.Set(22f, 0f);
		closeBtn.Top.Set(1f, 0f);
		closeBtn.Left.Set(-28f, 1f);
		closeBtn.BackgroundColor = new Color(80, 30, 30);
		closeBtn.BorderColor = new Color(140, 50, 50);
		closeBtn.SetPadding(0);
		var closeText = new Terraria.GameContent.UI.Elements.UIText("X", 0.7f);
		closeText.HAlign = 0.5f;
		closeText.VAlign = 0.5f;
		closeBtn.Append(closeText);
		closeBtn.OnLeftClick += (evt, el) => OnCloseClicked?.Invoke();
		Append(closeBtn);

		// Class filter
		_classFilter = new UIClassFilterBar();
		_classFilter.Top.Set(26f, 0f);
		_classFilter.Width.Set(0, 1f);
		_classFilter.SelectedClass = _activeFilter;
		_classFilter.OnFilterChanged += newFilter =>
		{
			_activeFilter = newFilter;
			RenderAllColumns();
		};
		Append(_classFilter);

		float listTop = 52f;

		// Info cards
		Tier tier = Tier.PreBoss;
		try { tier = ProgressionTier.Detect(); } catch { }
		PlayerClass detected = PlayerClass.Mixed;
		try { detected = ClassDetector.Detect(Main.LocalPlayer); } catch { }

		if (_stats != null)
		{
			_stageCard = new UI.Elements.UIPlayerStageCard(tier, detected, _stats, _activeFilter);
			_stageCard.Top.Set(52f, 0f);
			_stageCard.Left.Set(0f, 0f);
			Append(_stageCard);
		}

		_bossCard = new UI.Elements.UIBossProgressionCard(_bosses);
		_bossCard.Top.Set(52f, 0f);
		_bossCard.Left.Set(210f, 0f);
		_bossCard.Width.Set(-214f, 1f);
		Append(_bossCard);

		listTop = 188f;
		float headerY = listTop - 18f;

		// ── Column 1: Ready to Craft ────────────────────────────
		float c1Left = 0f;
		(_readyList, _readyScroll) = MakeColumn(c1Left, Col1Pct, listTop);

		// ── Column 2: Nearly Ready ──────────────────────────────
		float c2Left = Col1Pct;
		(_nearlyList, _nearlyScroll) = MakeColumn(c2Left, Col2Pct, listTop);

		// ── Column 3: Building Toward ───────────────────────────
		float c3Left = Col1Pct + Col2Pct;
		(_buildingList, _buildingScroll) = MakeColumn(c3Left, Col3Pct, listTop);

		// ── Column 4: Where to Go ───────────────────────────────
		float c4Left = Col1Pct + Col2Pct + Col3Pct;
		float c4Width = 1f - c4Left;
		(_sourceList, _sourceScroll) = MakeColumn(c4Left, c4Width, listTop);

		// Right-align source scrollbar to panel edge
		_sourceScroll.Left.Set(-14f, 1f);

		Recalculate();
	}

	private (Terraria.GameContent.UI.Elements.UIList list, Terraria.GameContent.UI.Elements.UIScrollbar scroll) MakeColumn(
		float leftPct, float widthPct, float top)
	{
		var list = new Terraria.GameContent.UI.Elements.UIList();
		list.Top.Set(top, 0f);
		list.Left.Set(ColGap, leftPct);
		list.Width.Set(-(ColGap + 14f), widthPct);
		list.Height.Set(-(top + 4f), 1f);
		list.ListPadding = 3f;
		list.ManualSortMethod = _ => { };
		Append(list);

		var scroll = new Terraria.GameContent.UI.Elements.UIScrollbar();
		scroll.Top.Set(top, 0f);
		scroll.Left.Set(-(ColGap + 10f), leftPct + widthPct);
		scroll.Height.Set(-(top + 4f), 1f);
		list.SetScrollbar(scroll);
		Append(scroll);

		return (list, scroll);
	}

	protected override void DrawChildren(SpriteBatch spriteBatch)
	{
		base.DrawChildren(spriteBatch);

		// Draw 3 vertical dividers between the 4 columns
		var dims = GetInnerDimensions();
		float divTop = dims.Y + 188f;
		float divH = dims.Height - 188f - 4f;
		Color divColor = new Color(60, 70, 130) * 0.4f;

		float div1X = dims.X + dims.Width * Col1Pct;
		float div2X = dims.X + dims.Width * (Col1Pct + Col2Pct);
		float div3X = dims.X + dims.Width * (Col1Pct + Col2Pct + Col3Pct);

		spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle((int)div1X, (int)divTop, 1, (int)divH), divColor);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle((int)div2X, (int)divTop, 1, (int)divH), divColor);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle((int)div3X, (int)divTop, 1, (int)divH), divColor);

		// Column headers (drawn over dividers for clean look)
		float headerY = dims.Y + 188f - 18f;
		DrawColumnHeader(spriteBatch, "Ready", dims.X, Col1Pct * dims.Width, headerY, new Color(80, 220, 80), _readyCount);
		DrawColumnHeader(spriteBatch, "Nearly", dims.X + Col1Pct * dims.Width, Col2Pct * dims.Width, headerY, new Color(220, 200, 60), _nearlyCount);
		DrawColumnHeader(spriteBatch, "Building", dims.X + (Col1Pct + Col2Pct) * dims.Width, Col3Pct * dims.Width, headerY, new Color(160, 160, 180), _buildingCount);
		DrawColumnHeader(spriteBatch, "Where to Go", dims.X + (Col1Pct + Col2Pct + Col3Pct) * dims.Width, (1f - Col1Pct - Col2Pct - Col3Pct) * dims.Width, headerY, new Color(100, 160, 255), -1);
	}

	private static void DrawColumnHeader(SpriteBatch spriteBatch, string label, float x, float colW, float y, Color accentColor, int count)
	{
		// Accent underline
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)(x + ColGap), (int)(y + 15f), (int)(colW - ColGap * 2), 2), accentColor * 0.6f);

		string text = count >= 0 ? $"{label} ({count})" : label;
		Utils.DrawBorderString(spriteBatch, text, new Vector2(x + ColGap + 2f, y), accentColor, 0.55f);
	}

	private int _readyCount, _nearlyCount, _buildingCount;

	public void SetResults(List<GearRecommendation> recs, PlayerClass filter,
		PlayerStats stats = null, List<BossInfo> nextBosses = null,
		List<CombinationResult> combos = null,
		WorldContext worldContext = default)
	{
		_allRecs = recs;
		_allCombos = combos;
		_mergedItems = UpgradeItemBuilder.Build(recs, combos);
		_activeFilter = filter;
		_stats = stats;
		_bosses = nextBosses;
		_worldContext = worldContext;

		// Compute source priorities from merged upgrades
		var sourceSystem = ModContent.GetInstance<ItemSourceSystem>();
		_sourcePriority = _mergedItems != null && sourceSystem != null
			? SourcePriorityAnalyzer.Analyze(_mergedItems, sourceSystem, worldContext)
			: null;

		if (_classFilter != null)
			_classFilter.SelectedClass = filter;
		BuildLayout();
		RenderAllColumns();
	}

	private void RenderAllColumns()
	{
		RenderReadinessColumns();
		RenderSourceList();
	}

	// ── Readiness Columns ───────────────────────────────────────

	private void RenderReadinessColumns()
	{
		_readyList?.Clear();
		_nearlyList?.Clear();
		_buildingList?.Clear();
		_readyCount = 0;
		_nearlyCount = 0;
		_buildingCount = 0;

		if (_mergedItems == null || _mergedItems.Count == 0)
		{
			_readyList?.Add(new UIEmptyMessage("Scanning..."));
			return;
		}

		// Filter by class
		var filtered = new List<UpgradeItem>();
		foreach (var upgrade in _mergedItems)
		{
			var item = ContentSamples.ItemsByType.GetValueOrDefault(upgrade.ItemType);
			if (item != null && !ClassDetector.ItemMatchesClass(item, _activeFilter, strict: true))
				continue;
			filtered.Add(upgrade);
		}

		// Distribute into readiness columns
		foreach (var upgrade in filtered)
		{
			switch (upgrade.Proximity)
			{
				case Proximity.CraftableNow:
					_readyList?.Add(new UICompactUpgradeCard(upgrade, type => OnAnalyzeItem?.Invoke(type)));
					_readyCount++;
					break;
				case Proximity.AlmostReady:
					// Same filter as dashboard: for small recipes (≤2 leaf materials),
					// only show if player owns an equipment ingredient — owning just
					// a common material like Wood isn't meaningful progress
					if (upgrade.LeafMaterialsTotal < 3 && !OwnsEquipmentIngredient(upgrade))
						break;
					_nearlyList?.Add(new UICompactUpgradeCard(upgrade, type => OnAnalyzeItem?.Invoke(type)));
					_nearlyCount++;
					break;
				case Proximity.NeedsWork:
					_buildingList?.Add(new UICompactUpgradeCard(upgrade, type => OnAnalyzeItem?.Invoke(type)));
					_buildingCount++;
					break;
				// FarAway items not shown in columns
			}
		}

		if (_readyCount == 0)
			_readyList?.Add(new UIEmptyMessage("None yet"));
		if (_nearlyCount == 0)
			_nearlyList?.Add(new UIEmptyMessage("None yet"));
		if (_buildingCount == 0)
			_buildingList?.Add(new UIEmptyMessage("None yet"));
	}

	/// <summary>
	/// For small recipes (≤2 ingredients), checks if the player owns at least one
	/// equipment ingredient — not just a common material like Wood or Gel.
	/// </summary>
	private static bool OwnsEquipmentIngredient(UpgradeItem item)
	{
		if (item.Ingredients == null) return false;
		foreach (var (ingType, needed, owned) in item.Ingredients)
		{
			if (owned <= 0) continue;
			var ing = ContentSamples.ItemsByType.GetValueOrDefault(ingType);
			if (ing == null) continue;
			if (ing.accessory || ing.damage > 0 || ing.headSlot >= 0 || ing.bodySlot >= 0 || ing.legSlot >= 0)
				return true;
		}
		return false;
	}

	// ── Source Priority Column ───────────────────────────────────

	private void RenderSourceList()
	{
		if (_sourceList == null) return;
		_sourceList.Clear();

		if (_sourcePriority == null || _sourcePriority.Rankings.Count == 0)
		{
			_sourceList.Add(new UIEmptyMessage("No farmable locations — all upgrades are craftable!"));
			return;
		}

		foreach (var ranking in _sourcePriority.Rankings)
		{
			if (_activeFilter != PlayerClass.Mixed && _mergedItems != null)
			{
				bool hasRelevant = false;
				foreach (var drop in ranking.Drops)
				{
					foreach (var c in drop.FeedsInto)
					{
						var item = ContentSamples.ItemsByType.GetValueOrDefault(c.UpgradeItemType);
						if (item == null || ClassDetector.ItemMatchesClass(item, _activeFilter, strict: true))
						{
							hasRelevant = true;
							break;
						}
					}
					if (hasRelevant) break;
				}
				if (!hasRelevant) continue;
			}

			_sourceList.Add(new UILocationCard(ranking, type => OnAnalyzeItem?.Invoke(type)));
		}

		if (_sourceList.Count == 0)
			_sourceList.Add(new UIEmptyMessage("No locations for this class."));
	}
}

/// <summary>Clickable "Show X more..." / "Show less" button.</summary>
internal class UIShowMoreButton : UIElement
{
	private readonly string _text;
	private readonly Action _onClick;

	public UIShowMoreButton(string text, Action onClick)
	{
		_text = text;
		_onClick = onClick;
		Width.Set(0, 1f);
		Height.Set(22f, 0f);
	}

	public override void LeftClick(UIMouseEvent evt)
	{
		base.LeftClick(evt);
		_onClick?.Invoke();
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		bool hover = IsMouseHovering;
		Color color = hover ? new Color(120, 180, 255) : new Color(80, 130, 200);
		Utils.DrawBorderString(spriteBatch, _text,
			new Vector2(dims.X + 14f, dims.Y + 3f), color, 0.55f);
	}
}

/// <summary>Horizontal class filter bar with All/Melee/Ranged/Mage/Summoner buttons.</summary>
internal class UIClassFilterBar : UIElement
{
	public PlayerClass SelectedClass { get; set; } = PlayerClass.Mixed;
	public event Action<PlayerClass> OnFilterChanged;

	private static readonly PlayerClass[] Classes =
		[PlayerClass.Mixed, PlayerClass.Melee, PlayerClass.Ranged, PlayerClass.Mage, PlayerClass.Summoner];

	public UIClassFilterBar()
	{
		Height.Set(22f, 0f);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		float btnW = Math.Min(60f, (dims.Width - 8f) / Classes.Length);
		float x = dims.X + 4f;

		foreach (var pc in Classes)
		{
			string label = pc switch
			{
				PlayerClass.Mixed => "All",
				PlayerClass.Melee => "Melee",
				PlayerClass.Ranged => "Ranged",
				PlayerClass.Mage => "Mage",
				PlayerClass.Summoner => "Summon",
				_ => "?"
			};

			bool selected = pc == SelectedClass;
			var rect = new Rectangle((int)x, (int)dims.Y, (int)btnW - 2, (int)dims.Height);
			bool hover = Main.mouseX >= rect.X && Main.mouseX < rect.Right
				&& Main.mouseY >= rect.Y && Main.mouseY < rect.Bottom;

			Color bg = selected ? new Color(50, 65, 130) : hover ? new Color(40, 48, 95) : new Color(30, 35, 65);
			spriteBatch.Draw(TextureAssets.MagicPixel.Value, rect, bg);

			if (selected)
				spriteBatch.Draw(TextureAssets.MagicPixel.Value,
					new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), new Color(100, 120, 220));

			Color tc = selected ? Color.White : hover ? Color.LightGray : Color.Gray;
			Utils.DrawBorderString(spriteBatch, label, new Vector2(x + 4f, dims.Y + 2f), tc, 0.5f);

			if (hover && Main.mouseLeft && Main.mouseLeftRelease)
			{
				SelectedClass = pc;
				OnFilterChanged?.Invoke(pc);
			}

			x += btnW;
		}
	}
}
