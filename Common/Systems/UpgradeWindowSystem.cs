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
/// The upgrade recommendations panel — draggable window showing
/// gear upgrades grouped by category (left) and source priority rankings (right).
/// </summary>
internal class UpgradeRecommendationsPanel : UI.Elements.UIDraggablePanel
{
	// Left column: upgrade categories
	private Terraria.GameContent.UI.Elements.UIList _list;
	private Terraria.GameContent.UI.Elements.UIScrollbar _scrollbar;

	// Right column: source priority ("Where to Go")
	private Terraria.GameContent.UI.Elements.UIList _sourceList;
	private Terraria.GameContent.UI.Elements.UIScrollbar _sourceScroll;

	private UIClassFilterBar _classFilter;

	private PlayerClass _activeFilter = PlayerClass.Mixed;
	private List<GearRecommendation> _allRecs;
	private List<CombinationResult> _allCombos;
	private List<UpgradeItem> _mergedItems;
	private SourcePriorityResult _sourcePriority;
	private PlayerStats _stats;
	private List<BossInfo> _bosses;
	private readonly Dictionary<string, bool> _collapsed = [];

	private UI.Elements.UIPlayerStageCard _stageCard;
	private UI.Elements.UIBossProgressionCard _bossCard;

	public event Action<int> OnAnalyzeItem;
	public event Action OnCloseClicked;

	private const float PanelW = 960f;
	private const float PanelH = 680f;
	private const float LeftColumnPct = 0.55f;
	private const float DividerGap = 6f;

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
			RenderList();
			RenderSourceList();
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

		// ── Left column: Upgrades ───────────────────────────────
		_list = new Terraria.GameContent.UI.Elements.UIList();
		_list.Top.Set(listTop, 0f);
		_list.Width.Set(-16f, LeftColumnPct);
		_list.Height.Set(-(listTop + 4f), 1f);
		_list.ListPadding = 4f;
		_list.ManualSortMethod = _ => { };
		Append(_list);

		_scrollbar = new Terraria.GameContent.UI.Elements.UIScrollbar();
		_scrollbar.Top.Set(listTop, 0f);
		_scrollbar.Left.Set(-14f, LeftColumnPct);
		_scrollbar.Height.Set(-(listTop + 4f), 1f);
		_list.SetScrollbar(_scrollbar);
		Append(_scrollbar);

		// ── Right column: Where to Go ───────────────────────────
		float rightLeft = LeftColumnPct;
		float rightWidth = 1f - LeftColumnPct;

		// Column header
		var sourceHeader = new Terraria.GameContent.UI.Elements.UIText("Where to Go", 0.7f);
		sourceHeader.Top.Set(listTop - 20f, 0f);
		sourceHeader.Left.Set(DividerGap + 4f, rightLeft);
		Append(sourceHeader);

		_sourceList = new Terraria.GameContent.UI.Elements.UIList();
		_sourceList.Top.Set(listTop, 0f);
		_sourceList.Left.Set(DividerGap, rightLeft);
		_sourceList.Width.Set(-(DividerGap + 16f), rightWidth);
		_sourceList.Height.Set(-(listTop + 4f), 1f);
		_sourceList.ListPadding = 4f;
		_sourceList.ManualSortMethod = _ => { };
		Append(_sourceList);

		_sourceScroll = new Terraria.GameContent.UI.Elements.UIScrollbar();
		_sourceScroll.Top.Set(listTop, 0f);
		_sourceScroll.Left.Set(-14f, 1f);
		_sourceScroll.Height.Set(-(listTop + 4f), 1f);
		_sourceList.SetScrollbar(_sourceScroll);
		Append(_sourceScroll);

		Recalculate();
	}

	protected override void DrawChildren(SpriteBatch spriteBatch)
	{
		base.DrawChildren(spriteBatch);

		// Draw vertical divider between columns
		var dims = GetInnerDimensions();
		float divX = dims.X + dims.Width * LeftColumnPct;
		float divTop = dims.Y + 188f;
		float divH = dims.Height - 188f - 4f;
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)divX, (int)divTop, 1, (int)divH),
			new Color(60, 70, 130) * 0.4f);
	}

	private WorldContext _worldContext;

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
		RenderList();
		RenderSourceList();
	}

	private bool IsSectionCollapsed(string name) => _collapsed.GetValueOrDefault(name, false);

	// ── Left Column: Upgrade List ───────────────────────────────

	private void RenderList()
	{
		if (_list == null) return;
		_list.Clear();

		if (_mergedItems == null)
		{
			_list.Add(new UIEmptyMessage("Scanning for upgrades..."));
			return;
		}
		if (_mergedItems.Count == 0)
		{
			_list.Add(new UIEmptyMessage("No upgrades available at your current tier."));
			return;
		}

		var filtered = new List<UpgradeItem>();
		foreach (var upgrade in _mergedItems)
		{
			var item = ContentSamples.ItemsByType.GetValueOrDefault(upgrade.ItemType);
			if (item != null && !ClassDetector.ItemMatchesClass(item, _activeFilter, strict: true))
				continue;
			filtered.Add(upgrade);
		}

		// Group by acquisition method
		var craftable = filtered.FindAll(r => r.Acquisition == AcquisitionMethod.Craftable);
		var bossDrops = filtered.FindAll(r => r.Acquisition == AcquisitionMethod.BossDrop);
		var shopItems = filtered.FindAll(r => r.Acquisition == AcquisitionMethod.Shop);
		var mobDrops = filtered.FindAll(r => r.Acquisition == AcquisitionMethod.MobDrop);
		var other = filtered.FindAll(r =>
			r.Acquisition is AcquisitionMethod.Event or AcquisitionMethod.Fishing
			or AcquisitionMethod.Exploration or AcquisitionMethod.Unknown);

		if (craftable.Count > 0)
		{
			AddCategorySection("Craftable Weapons", craftable.FindAll(r => r.Category == GearCategory.Weapon));
			AddCategorySection("Craftable Armor", craftable.FindAll(r => r.Category == GearCategory.Armor));
			AddCategorySection("Craftable Accessories", craftable.FindAll(r => r.Category == GearCategory.Accessory));
			AddCategorySection("Craftable Tools", craftable.FindAll(r => r.Category == GearCategory.Tool));
		}

		AddCategorySection("Boss Drops", bossDrops);
		AddCategorySection("Shop Items", shopItems);
		AddCategorySection("Mob Drops", mobDrops);
		if (other.Count > 0)
			AddCategorySection("Events & Other", other);

		if (filtered.Count == 0)
			_list.Add(new UIEmptyMessage("No upgrades for this class."));
	}

	// ── Right Column: Source Priority List ───────────────────────

	private void RenderSourceList()
	{
		if (_sourceList == null) return;
		_sourceList.Clear();

		if (_sourcePriority == null || _sourcePriority.Rankings.Count == 0)
		{
			_sourceList.Add(new UIEmptyMessage("No farmable locations — all upgrades are craftable!"));
			return;
		}

		// Filter rankings to only show locations relevant to current class filter
		foreach (var ranking in _sourcePriority.Rankings)
		{
			// Check if any drop in this location feeds an upgrade matching the class filter
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

	private const int InitialShowCount = 8;
	private readonly Dictionary<string, bool> _showAll = [];

	private void AddCategorySection(string name, List<UpgradeItem> items)
	{
		if (items.Count == 0) return;

		bool collapsed = IsSectionCollapsed(name);
		string headerText = $"{name} ({items.Count})";
		_list.Add(new UI.Panels.UIDashboardSectionHeader(headerText, collapsed, () =>
		{
			_collapsed[name] = !collapsed;
			RenderList();
		}));

		if (collapsed) return;

		bool showAll = _showAll.GetValueOrDefault(name, false);
		int limit = showAll ? items.Count : Math.Min(items.Count, InitialShowCount);

		for (int i = 0; i < limit; i++)
			_list.Add(new UIUpgradeItemRow(items[i], type => OnAnalyzeItem?.Invoke(type)));

		if (!showAll && items.Count > InitialShowCount)
		{
			int remaining = items.Count - InitialShowCount;
			var showMoreBtn = new UIShowMoreButton($"Show {remaining} more...", () =>
			{
				_showAll[name] = true;
				RenderList();
			});
			_list.Add(showMoreBtn);
		}
		else if (showAll && items.Count > InitialShowCount)
		{
			var showLessBtn = new UIShowMoreButton("Show less", () =>
			{
				_showAll[name] = false;
				RenderList();
			});
			_list.Add(showLessBtn);
		}
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
