using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using CraftPathOptimizer.Content.Graph;
using CraftPathOptimizer.UI.Elements;

namespace CraftPathOptimizer.UI.Panels;

/// <summary>
/// Flat material checklist showing what the player owns vs. what they need.
/// Groups by owned/missing, shows source tags and equipped badges.
/// </summary>
public class MaterialListPanel : UIElement
{
	private readonly UIList _list;
	private readonly UIScrollbar _scrollbar;
	private bool _includeChests;

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

	public void SetResults(ResolverResult result, bool includeChests)
	{
		_includeChests = includeChests;
		_list.Clear();

		if (result?.Materials == null || result.Materials.Count == 0)
		{
			_list.Add(new UIEmptyMessage("Search for an item to see materials needed."));
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
					_list.Add(new UIMaterialRow(mat, includeChests));
			}
		}

		// Then owned items
		if (ownedCount > 0)
		{
			_list.Add(new UISectionHeader($"Owned ({ownedCount})"));
			foreach (var mat in result.Materials)
			{
				if (mat.IsComplete(includeChests))
					_list.Add(new UIMaterialRow(mat, includeChests));
			}
		}
	}
}

/// <summary>A section header label in the material list.</summary>
internal class UISectionHeader : UIElement
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

	public UIMaterialRow(MaterialEntry material, bool includeChests)
	{
		_material = material;
		_includeChests = includeChests;

		_displayItem = new Item();
		_displayItem.SetDefaults(material.ItemType);

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
