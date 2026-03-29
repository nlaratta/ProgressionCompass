using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.UI;
using CraftPathOptimizer.Content.Progression;
using CraftPathOptimizer.UI.Elements;
using CraftPathOptimizer.UI.Panels;

namespace CraftPathOptimizer.Common.Systems;

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
				"CraftPathOptimizer: Upgrade Recommendations",
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

	public void SetResults(List<GearRecommendation> recs, PlayerClass filter, HashSet<int> excludeComboTypes = null)
	{
		if (_panel != null)
		{
			_panel.CombinationItemTypes = excludeComboTypes;
			_panel.SetResults(recs, filter);
		}
	}
}

/// <summary>
/// The upgrade recommendations panel — draggable window showing
/// gear upgrades grouped by category.
/// </summary>
internal class UpgradeRecommendationsPanel : global::CraftPathOptimizer.UI.Elements.UIDraggablePanel
{
	private readonly Terraria.GameContent.UI.Elements.UIList _list;
	private readonly Terraria.GameContent.UI.Elements.UIScrollbar _scrollbar;
	private readonly UIClassFilterBar _classFilter;

	private PlayerClass _activeFilter = PlayerClass.Mixed;
	private List<GearRecommendation> _allRecs;

	public event Action<int> OnAnalyzeItem;
	public event Action OnCloseClicked;

	public UpgradeRecommendationsPanel()
	{
		Width.Set(520f, 0f);
		Height.Set(550f, 0f);
		SetPadding(10f);
		BackgroundColor = new Color(33, 37, 68) * 0.95f;
		BorderColor = new Color(60, 70, 130);

		// Title
		var title = new Terraria.GameContent.UI.Elements.UIText("Upgrade Recommendations", 0.8f);
		title.Top.Set(2f, 0f);
		Append(title);

		// Close button
		var closeBtn = new Terraria.GameContent.UI.Elements.UIPanel();
		closeBtn.Width.Set(24f, 0f);
		closeBtn.Height.Set(24f, 0f);
		closeBtn.Top.Set(0f, 0f);
		closeBtn.Left.Set(-26f, 1f);
		closeBtn.BackgroundColor = new Color(80, 30, 30);
		closeBtn.BorderColor = new Color(140, 50, 50);
		closeBtn.SetPadding(0);
		var closeText = new Terraria.GameContent.UI.Elements.UIText("X", 0.7f);
		closeText.HAlign = 0.5f;
		closeText.VAlign = 0.5f;
		closeBtn.Append(closeText);
		closeBtn.OnLeftClick += (evt, el) => OnCloseClicked?.Invoke();
		Append(closeBtn);

		// Class filter bar
		_classFilter = new UIClassFilterBar();
		_classFilter.Top.Set(28f, 0f);
		_classFilter.Width.Set(0, 1f);
		_classFilter.OnFilterChanged += newFilter =>
		{
			_activeFilter = newFilter;
			RenderList();
		};
		Append(_classFilter);

		// List
		_list = new Terraria.GameContent.UI.Elements.UIList();
		_list.Top.Set(54f, 0f);
		_list.Width.Set(-16f, 1f);
		_list.Height.Set(-58f, 1f);
		_list.ListPadding = 4f;
		_list.ManualSortMethod = _ => { };
		Append(_list);

		_scrollbar = new Terraria.GameContent.UI.Elements.UIScrollbar();
		_scrollbar.Top.Set(54f, 0f);
		_scrollbar.Left.Set(-14f, 1f);
		_scrollbar.Height.Set(-58f, 1f);
		_list.SetScrollbar(_scrollbar);
		Append(_scrollbar);
	}

	/// <summary>Set of item types already shown in "Almost Complete" — exclude from recommendations.</summary>
	public HashSet<int> CombinationItemTypes { get; set; }

	public void SetResults(List<GearRecommendation> recs, PlayerClass filter)
	{
		_allRecs = recs;
		_activeFilter = filter;
		_classFilter.SelectedClass = filter;
		RenderList();
	}

	private void RenderList()
	{
		_list.Clear();

		if (_allRecs == null || _allRecs.Count == 0)
		{
			_list.Add(new UIEmptyMessage("No upgrades available at your current tier."));
			return;
		}

		var filtered = new List<GearRecommendation>();
		foreach (var rec in _allRecs)
		{
			if (CombinationItemTypes?.Contains(rec.ItemType) == true)
				continue;
			var item = Terraria.ID.ContentSamples.ItemsByType.GetValueOrDefault(rec.ItemType);
			if (item != null && !ClassDetector.ItemMatchesClass(item, _activeFilter))
				continue;
			filtered.Add(rec);
		}

		var weapons = filtered.FindAll(r => r.Category == GearCategory.Weapon);
		var armor = filtered.FindAll(r => r.Category == GearCategory.Armor);
		var accessories = filtered.FindAll(r => r.Category == GearCategory.Accessory);
		var tools = filtered.FindAll(r => r.Category == GearCategory.Tool);

		if (weapons.Count > 0)
		{
			_list.Add(new UISectionHeader("Weapons"));
			foreach (var r in weapons)
				_list.Add(new UIGearRecommendationRow(r, type => OnAnalyzeItem?.Invoke(type)));
		}
		if (armor.Count > 0)
		{
			_list.Add(new UISectionHeader("Armor"));
			foreach (var r in armor)
				_list.Add(new UIGearRecommendationRow(r, type => OnAnalyzeItem?.Invoke(type)));
		}
		if (accessories.Count > 0)
		{
			_list.Add(new UISectionHeader("Accessories"));
			foreach (var r in accessories)
				_list.Add(new UIGearRecommendationRow(r, type => OnAnalyzeItem?.Invoke(type)));
		}
		if (tools.Count > 0)
		{
			_list.Add(new UISectionHeader("Tools"));
			foreach (var r in tools)
				_list.Add(new UIGearRecommendationRow(r, type => OnAnalyzeItem?.Invoke(type)));
		}

		if (filtered.Count == 0)
			_list.Add(new UIEmptyMessage("No upgrades for this class."));
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
