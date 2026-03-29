using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;

namespace CraftPathOptimizer.UI.Elements;

/// <summary>
/// A bar showing the currently analyzed item: [icon] Analyzing: ItemName [X]
/// Sits between the search panel and tab bar so the user always knows
/// which item the Materials/Tree/Craft Order tabs are showing.
/// </summary>
public class UIAnalysisIndicator : UIElement
{
	private int _itemType;
	private Item _displayItem;

	public event Action OnClearClicked;
	public event Action<int> OnPinClicked;
	public Func<int, bool> IsItemTracked { get; set; }
	public bool HasItem => _itemType > ItemID.None;

	public UIAnalysisIndicator()
	{
		Width.Set(0, 1f);
		Height.Set(28f, 0f);
	}

	public void SetItem(int itemType)
	{
		_itemType = itemType;
		if (itemType > ItemID.None)
		{
			_displayItem = new Item();
			_displayItem.SetDefaults(itemType);
		}
		else
		{
			_displayItem = null;
		}
	}

	public void Clear()
	{
		_itemType = 0;
		_displayItem = null;
	}

	public override void LeftClick(UIMouseEvent evt)
	{
		base.LeftClick(evt);
		if (!HasItem) return;
		var dims = GetDimensions();

		// X button (rightmost)
		if (evt.MousePosition.X >= dims.X + dims.Width - 24f)
		{
			OnClearClicked?.Invoke();
			return;
		}

		// Pin button (next to X)
		if (evt.MousePosition.X >= dims.X + dims.Width - 60f)
		{
			OnPinClicked?.Invoke(_itemType);
			return;
		}
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();

		// Background
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(),
			new Color(28, 32, 58) * 0.8f);

		if (!HasItem)
		{
			Utils.DrawBorderString(spriteBatch, "Select an item to analyze",
				new Vector2(dims.X + 8f, dims.Y + 5f), Color.Gray, 0.65f);
			return;
		}

		// Show tooltip on hover
		if (IsMouseHovering)
			UIHelper.ShowItemTooltip(_itemType);

		// Item icon
		UIHelper.DrawItemIcon(spriteBatch, _itemType, new Vector2(dims.X + 6f, dims.Y + 2f), 22f);

		// "Analyzing: ItemName"
		string text = $"Analyzing: {_displayItem?.Name ?? "?"}";
		Utils.DrawBorderString(spriteBatch, text, new Vector2(dims.X + 32f, dims.Y + 5f), Color.White, 0.65f);

		// Pin button
		bool tracked = IsItemTracked?.Invoke(_itemType) ?? false;
		float pinBtnX = dims.X + dims.Width - 60f;
		bool hoverPin = Main.mouseX >= pinBtnX && Main.mouseX < pinBtnX + 32
			&& Main.mouseY >= dims.Y && Main.mouseY < dims.Y + dims.Height;

		Color pinBg = tracked ? new Color(35, 70, 35) : hoverPin ? new Color(50, 60, 110) : new Color(35, 40, 75);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)pinBtnX, (int)(dims.Y + 3f), 32, 22), pinBg);
		if (tracked)
			UIHelper.DrawBorder(spriteBatch, new Rectangle((int)pinBtnX, (int)(dims.Y + 3f), 32, 22),
				new Color(60, 140, 60) * 0.7f);

		string pinText = tracked ? "OK" : "Pin";
		Color pinColor = tracked ? new Color(80, 220, 80) : hoverPin ? Color.White : Color.Gray;
		Utils.DrawBorderString(spriteBatch, pinText, new Vector2(pinBtnX + 6f, dims.Y + 5f), pinColor, 0.55f);

		// X button (rightmost)
		float xBtnX = dims.X + dims.Width - 24f;
		bool hoverX = Main.mouseX >= xBtnX && Main.mouseX < xBtnX + 20
			&& Main.mouseY >= dims.Y && Main.mouseY < dims.Y + dims.Height;
		Utils.DrawBorderString(spriteBatch, "X", new Vector2(xBtnX + 4f, dims.Y + 5f),
			hoverX ? new Color(255, 100, 100) : Color.Gray, 0.6f);
	}
}
