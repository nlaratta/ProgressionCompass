using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;
using ProgressionCompass.Content.Graph;
using ProgressionCompass.Content.Progression;
using ProgressionCompass.UI.Elements;

namespace ProgressionCompass.UI.Panels;

/// <summary>
/// Flat material checklist showing what the player owns vs. what they need.
/// Groups by owned/missing, shows source tags and equipped badges.
/// </summary>
public class MaterialListPanel : UIElement
{
	private readonly UIList _list;
	private readonly UIScrollbar _scrollbar;
	private bool _includeChests;

	/// <summary>Fired when user clicks an item in the "Used In" section.</summary>
	public event Action<int> OnAnalyzeItem;

	public MaterialListPanel()
	{
		Width.Set(0, 1f);
		Height.Set(0, 1f);

		_list = new UIList();
		_list.Width.Set(-20f, 1f);
		_list.Height.Set(0, 1f);
		_list.ListPadding = 2f;
		_list.ManualSortMethod = _ => { }; // Preserve our insertion order (Missing → Owned)
		Append(_list);

		_scrollbar = new UIScrollbar();
		_scrollbar.Left.Set(-18f, 1f);
		_scrollbar.Height.Set(0, 1f);
		_list.SetScrollbar(_scrollbar);
		Append(_scrollbar);
	}

	private WorldContext _worldContext;

	public void SetResults(ResolverResult result, bool includeChests, WorldContext worldContext = default)
	{
		_includeChests = includeChests;
		_worldContext = worldContext;
		_list.Clear();

		if (result?.Tree == null)
		{
			_list.Add(new UIEmptyMessage("Search for an item to see materials needed."));
			return;
		}

		// For non-craftable items (no materials), show item info + sources
		if (result.Materials == null || result.Materials.Count == 0)
		{
			AddItemInfoSection(result);
			AddAlsoObtainableSection(result.Tree.ItemType);
			AddShimmersIntoSection(result.Tree.ItemType);
			AddUsedInSection(result);
			return;
		}

		// Section header: Owned
		int ownedCount = 0;
		int missingCount = 0;
		foreach (var mat in result.Materials)
		{
			if (mat.IsComplete(includeChests))
				ownedCount++;
			else
				missingCount++;
		}

		// Missing items first
		if (missingCount > 0)
		{
			_list.Add(new UISectionHeader($"Missing ({missingCount})"));
			foreach (var mat in result.Materials)
			{
				if (!mat.IsComplete(includeChests))
				{
					var row = new UIMaterialRow(mat, includeChests, _worldContext);
					row.OnClicked += type => OnAnalyzeItem?.Invoke(type);
					_list.Add(row);
				}
			}
		}

		// Then owned items
		if (ownedCount > 0)
		{
			_list.Add(new UISectionHeader($"Owned ({ownedCount})"));
			foreach (var mat in result.Materials)
			{
				if (mat.IsComplete(includeChests))
				{
					var row = new UIMaterialRow(mat, includeChests, _worldContext);
					row.OnClicked += type => OnAnalyzeItem?.Invoke(type);
					_list.Add(row);
				}
			}
		}

		// Non-recipe sources for the target item (drops, shops, shimmer, etc.)
		AddAlsoObtainableSection(result.Tree.ItemType);
		AddShimmersIntoSection(result.Tree.ItemType);

		// "Used In" section: what recipes use this item as an ingredient?
		AddUsedInSection(result);
	}

	/// <summary>Shows info about a non-craftable item: what it is, where to get it, how many you own.</summary>
	private void AddItemInfoSection(ResolverResult result)
	{
		int itemType = result.Tree.ItemType;
		if (itemType <= Terraria.ID.ItemID.None) return;

		var item = new Item();
		item.SetDefaults(itemType);

		// Item header with icon
		_list.Add(new UIItemInfoHeader(itemType, item.Name ?? "?"));

		// Sources
		var sourceSystem = Terraria.ModLoader.ModContent.GetInstance<Common.Systems.ItemSourceSystem>();
		var sources = sourceSystem?.GetSources(itemType) ?? [];

		if (sources.Count > 0)
		{
			_list.Add(new UISectionHeader($"How to Obtain ({sources.Count})"));
			int shown = 0;
			foreach (var src in sources)
			{
				if (shown >= 5) break;
				_list.Add(new UISourceRow(src));
				shown++;
			}
			if (sources.Count > 5)
				_list.Add(new UIEmptyMessage($"+{sources.Count - 5} more sources..."));
		}
		else
		{
			_list.Add(new UIEmptyMessage("No known sources for this item."));
		}
	}

	/// <summary>Shows non-recipe sources for the item (drops, shops, shimmer, fishing, chests).</summary>
	private void AddAlsoObtainableSection(int itemType)
	{
		if (itemType <= Terraria.ID.ItemID.None) return;

		var sourceSystem = Terraria.ModLoader.ModContent.GetInstance<Common.Systems.ItemSourceSystem>();
		var sources = sourceSystem?.GetSources(itemType);
		if (sources == null || sources.Count == 0) return;

		_list.Add(new UISectionHeader($"Also Obtainable ({sources.Count})"));
		int shown = 0;
		foreach (var src in sources)
		{
			if (shown >= 8) break;
			_list.Add(new UISourceRow(src));
			shown++;
		}
		if (sources.Count > 8)
			_list.Add(new UIEmptyMessage($"+{sources.Count - 8} more sources..."));
	}

	/// <summary>Shows what this item transforms into when dropped in shimmer.</summary>
	private void AddShimmersIntoSection(int itemType)
	{
		if (itemType <= Terraria.ID.ItemID.None) return;

		try
		{
			var shimmerMap = Terraria.ID.ItemID.Sets.ShimmerTransformToItem;
			if (shimmerMap == null || itemType >= shimmerMap.Length) return;

			int resultType = shimmerMap[itemType];
			if (resultType <= Terraria.ID.ItemID.None) return;

			_list.Add(new UISectionHeader("Shimmers Into"));

			var row = new UIUsedInRow(resultType);
			row.OnClicked += type => OnAnalyzeItem?.Invoke(type);
			_list.Add(row);
		}
		catch { }
	}

	private void AddUsedInSection(ResolverResult result)
	{
		if (result?.Tree == null) return;
		int selectedType = result.Tree.ItemType;
		if (selectedType <= Terraria.ID.ItemID.None) return;

		var recipeIndex = Terraria.ModLoader.ModContent.GetInstance<Common.Systems.RecipeIndexSystem>();
		if (recipeIndex?.RecipesByIngredient == null) return;

		if (!recipeIndex.RecipesByIngredient.TryGetValue(selectedType, out var recipes) || recipes.Count == 0)
			return;

		// Collect unique output items
		var usedIn = new List<int>();
		var seen = new HashSet<int>();
		foreach (var recipe in recipes)
		{
			int outputType = recipe.createItem.type;
			if (outputType <= Terraria.ID.ItemID.None || outputType == selectedType || seen.Contains(outputType))
				continue;
			seen.Add(outputType);
			usedIn.Add(outputType);
			if (usedIn.Count >= 20) break; // Cap at 20 to avoid huge lists
		}

		if (usedIn.Count == 0) return;

		_list.Add(new UISectionHeader($"Used In ({usedIn.Count})"));
		foreach (int itemType in usedIn)
		{
			var row = new UIUsedInRow(itemType);
			row.OnClicked += type => OnAnalyzeItem?.Invoke(type);
			_list.Add(row);
		}
	}
}

/// <summary>Compact clickable row showing an item this ingredient is used in.</summary>
internal class UIUsedInRow : UIElement
{
	private readonly int _itemType;
	public event Action<int> OnClicked;

	public UIUsedInRow(int itemType)
	{
		_itemType = itemType;
		Width.Set(0, 1f);
		Height.Set(28f, 0f);
	}

	public override void LeftClick(UIMouseEvent evt)
	{
		base.LeftClick(evt);
		OnClicked?.Invoke(_itemType);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		bool hover = IsMouseHovering;

		if (hover)
			UIHelper.ShowItemTooltip(_itemType);

		Color bg = hover ? new Color(45, 55, 110) * 0.4f : new Color(30, 35, 70) * 0.15f;
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(), bg);

		// Cyan accent for "used in" items
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)dims.X, (int)dims.Y + 2, 3, (int)dims.Height - 4),
			new Color(80, 180, 220) * 0.7f);

		UIHelper.DrawItemIcon(spriteBatch, _itemType, new Vector2(dims.X + 8f, dims.Y + 2f), 20f);

		var item = new Item();
		if (_itemType > Terraria.ID.ItemID.None) item.SetDefaults(_itemType);
		Utils.DrawBorderString(spriteBatch, item.Name ?? "?",
			new Vector2(dims.X + 32f, dims.Y + 4f), Color.White, 0.6f);

		// Rarity color indicator
		Color rarityColor = UIHelper.GetRarityColor(item.rare);
		float nameW = FontAssets.MouseText.Value.MeasureString(item.Name ?? "?").X * 0.6f;
		Utils.DrawBorderString(spriteBatch, $"(Tier {item.rare})",
			new Vector2(dims.X + 36f + nameW, dims.Y + 5f), rarityColor * 0.7f, 0.45f);

		// Arrow indicator (clickable)
		Utils.DrawBorderString(spriteBatch, ">>",
			new Vector2(dims.X + dims.Width - 22f, dims.Y + 5f),
			hover ? Color.White : Color.Gray, 0.5f);
	}
}

/// <summary>A section header label in the material list.</summary>
public class UISectionHeader : UIElement
{
	private readonly string _text;

	public UISectionHeader(string text)
	{
		_text = text;
		Width.Set(0, 1f);
		Height.Set(24f, 0f);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		var font = FontAssets.MouseText.Value;
		var textPos = new Vector2(dims.X + 4f, dims.Y + 2f);
		Utils.DrawBorderString(spriteBatch, _text, textPos, Color.Gold);
	}
}

/// <summary>A single material row: icon, name, count, source breakdown, source tags.</summary>
internal class UIMaterialRow : UIElement
{
	private readonly MaterialEntry _material;
	private readonly bool _includeChests;
	private readonly Item _displayItem;
	private readonly WorldContext _worldContext;
	private readonly string _altName; // "or Palladium Bar" when hardmode ore tier unknown

	public event Action<int> OnClicked;

	public override void LeftClick(UIMouseEvent evt)
	{
		base.LeftClick(evt);
		OnClicked?.Invoke(_material.ItemType);
	}

	public UIMaterialRow(MaterialEntry material, bool includeChests, WorldContext worldContext = default)
	{
		_material = material;
		_includeChests = includeChests;
		_worldContext = worldContext;

		_displayItem = new Item();
		_displayItem.SetDefaults(material.ItemType);

		// Check for hardmode ore alternative when tier is unknown
		int? altId = worldContext.GetOreAlternative(material.ItemType);
		if (altId.HasValue && !worldContext.IsPreferredOreItem(material.ItemType)
			&& !worldContext.IsPreferredOreItem(altId.Value))
		{
			// Both variants are equally valid (tier unknown) — show alternative name
			var altItem = new Item();
			altItem.SetDefaults(altId.Value);
			_altName = $" or {altItem.Name}";
		}

		Width.Set(0, 1f);

		// Base height + source tags
		float height = 36f;
		if (!material.IsComplete(includeChests))
		{
			int sourcesToShow = System.Math.Min(material.Sources.Count, 3);
			height += sourcesToShow * 16f;
		}
		if (material.Owned.IsEquipped)
			height += 16f;
		if (includeChests && material.Owned.ChestCount > 0)
			height += 16f;
		Height.Set(height, 0f);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		bool complete = _material.IsComplete(_includeChests);

		if (IsMouseHovering)
			UIHelper.ShowItemTooltip(_material.ItemType);

		// Background
		Color bgColor = complete ? new Color(30, 60, 30) * 0.5f : new Color(60, 30, 30) * 0.5f;
		if (IsMouseHovering)
			bgColor *= 1.5f;
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(), bgColor);

		// Item icon (left side)
		UIHelper.DrawItemIconCentered(spriteBatch, _material.ItemType, dims.X + 4f, dims.Y + 2f, 32f, 32f, 28f);

		var font = FontAssets.MouseText.Value;
		var smallFont = FontAssets.ItemStack.Value;

		// Item name — vertically centered with the icon
		string name = _displayItem.Name;
		float nameY = dims.Y + (36f - font.LineSpacing) / 2f;
		var namePos = new Vector2(dims.X + 40f, nameY);
		Utils.DrawBorderString(spriteBatch, name, namePos, Color.White);

		// Show ore alternative when hardmode tier unknown (e.g., "or Palladium Bar")
		if (_altName != null)
		{
			float nameW = font.MeasureString(name).X;
			Utils.DrawBorderString(spriteBatch, _altName,
				new Vector2(namePos.X + nameW + 2f, nameY + 2f), new Color(160, 160, 190), 0.7f);
		}

		// Count: "8/12" or "1/1 DONE"
		int owned = _material.Owned.AvailableCount(_includeChests);
		string countText = $"{owned}/{_material.QuantityNeeded}";
		Color countColor;

		if (complete)
		{
			countText += " DONE";
			countColor = Color.Green;
		}
		else if (owned > 0)
		{
			countColor = Color.Yellow;
		}
		else
		{
			countColor = Color.Red;
		}

		var countPos = new Vector2(dims.X + dims.Width - font.MeasureString(countText).X - 8f, nameY);
		Utils.DrawBorderString(spriteBatch, countText, countPos, countColor);

		// [E] badge next to count
		if (_material.Owned.IsEquipped)
		{
			var ePos = new Vector2(countPos.X - 20f, dims.Y + 4f);
			Utils.DrawBorderString(spriteBatch, "[E]", ePos, Color.Yellow);
		}

		// Sub-lines
		float subY = dims.Y + 22f;

		// Source breakdown (e.g., "5 in inventory, 3 in chest")
		if (owned > 0 && (_material.Owned.ChestCount > 0 || _material.Owned.IsEquipped))
		{
			var parts = new System.Collections.Generic.List<string>();
			if (_material.Owned.PersonalCount > 0)
				parts.Add($"{_material.Owned.PersonalCount} in inventory");
			if (_material.Owned.EquippedCount > 0)
				parts.Add($"{_material.Owned.EquippedCount} equipped");
			if (_includeChests && _material.Owned.ChestCount > 0)
				parts.Add($"{_material.Owned.ChestCount} in chest");

			string breakdown = string.Join(", ", parts);
			var breakdownPos = new Vector2(dims.X + 52f, subY);
			Utils.DrawBorderString(spriteBatch, breakdown, breakdownPos, Color.LightGray);
			subY += 16f;
		}

		// Source tags (up to 3, for missing/partial items)
		if (!complete && _material.Sources.Count > 0)
		{
			int maxSources = System.Math.Min(_material.Sources.Count, 3);
			for (int s = 0; s < maxSources; s++)
			{
				var src = _material.Sources[s];
				Color sourceColor = UIHelper.GetSourceColor(src.Type);

				var sourcePos = new Vector2(dims.X + 52f, subY);
				Utils.DrawBorderString(spriteBatch, src.Description, sourcePos, sourceColor, 0.6f);
				subY += 16f;
			}
			if (_material.Sources.Count > 3)
			{
				Utils.DrawBorderString(spriteBatch, $"  +{_material.Sources.Count - 3} more sources...",
					new Vector2(dims.X + 52f, subY), Color.Gray, 0.55f);
			}
		}
	}
}

/// <summary>A centered gray message for empty panel states. Used by all content panels.</summary>
public class UIEmptyMessage : UIElement
{
	private readonly string _text;

	public UIEmptyMessage(string text)
	{
		_text = text;
		Width.Set(0, 1f);
		Height.Set(40f, 0f);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		var font = FontAssets.MouseText.Value;
		var size = font.MeasureString(_text) * 0.75f;
		var pos = new Vector2(dims.X + (dims.Width - size.X) / 2f, dims.Y + 10f);
		Utils.DrawBorderString(spriteBatch, _text, pos, Color.Gray, 0.75f);
	}
}

/// <summary>Header showing an item's icon, name, and rarity for non-craftable item info.</summary>
internal class UIItemInfoHeader : UIElement
{
	private readonly int _itemType;
	private readonly string _name;

	public UIItemInfoHeader(int itemType, string name)
	{
		_itemType = itemType;
		_name = name;
		Width.Set(0, 1f);
		Height.Set(36f, 0f);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();

		if (IsMouseHovering)
			UIHelper.ShowItemTooltip(_itemType);

		spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(), new Color(30, 35, 60) * 0.4f);

		UIHelper.DrawItemIcon(spriteBatch, _itemType, new Vector2(dims.X + 6f, dims.Y + 4f), 26f);

		var item = new Item();
		if (_itemType > Terraria.ID.ItemID.None) item.SetDefaults(_itemType);
		Color rarityColor = UIHelper.GetRarityColor(item.rare);

		Utils.DrawBorderString(spriteBatch, _name, new Vector2(dims.X + 38f, dims.Y + 4f), Color.White, 0.75f);
		Utils.DrawBorderString(spriteBatch, "Not craftable",
			new Vector2(dims.X + 38f, dims.Y + 20f), Color.Gray, 0.5f);

		// Rarity bar
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)dims.X, (int)dims.Y + 2, 3, (int)dims.Height - 4), rarityColor * 0.8f);
	}
}

/// <summary>Row showing a single item source (Drop: X, Buy: Y, Mine: Z).</summary>
public class UISourceRow : UIElement
{
	private readonly Content.Graph.ItemSource _source;

	public UISourceRow(Content.Graph.ItemSource source)
	{
		_source = source;
		Width.Set(0, 1f);
		Height.Set(20f, 0f);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		Color color = UIHelper.GetSourceColor(_source.Type);
		Utils.DrawBorderString(spriteBatch, _source.Description,
			new Vector2(dims.X + 12f, dims.Y + 2f), color, 0.55f);
	}
}
