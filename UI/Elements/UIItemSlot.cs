using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;

namespace CraftPathOptimizer.UI.Elements;

/// <summary>
/// Displays an item icon with optional name, quantity, and equipped badge.
/// Two modes: compact (icon only, for queue) and list row (icon + name, for search results).
/// </summary>
public class UICraftItemSlot : UIElement
{
	public int ItemType { get; }
	public int Quantity { get; set; }
	public bool ShowEquippedBadge { get; set; }
	public bool ShowName { get; set; }
	public bool ShowPinButton { get; set; }
	public bool IsPinned { get; set; }
	public event Action<int> OnSelected;
	public event Action<int> OnPinClicked;

	private readonly Item _displayItem;

	public UICraftItemSlot(int itemType, int quantity = 1, bool showName = false)
	{
		ItemType = itemType;
		Quantity = quantity;
		ShowName = showName;

		_displayItem = new Item();
		if (itemType > ItemID.None)
			_displayItem.SetDefaults(itemType);

		Width.Set(40f, 0f);
		Height.Set(40f, 0f);
	}

	public override void LeftClick(UIMouseEvent evt)
	{
		base.LeftClick(evt);
		OnSelected?.Invoke(ItemType);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		bool hovering = IsMouseHovering;

		if (hovering)
			UIHelper.ShowItemTooltip(ItemType);

		// Background
		Color bgColor = hovering ? new Color(55, 65, 120) : new Color(38, 42, 82);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(), bgColor * 0.85f);

		// Hover border
		if (hovering)
		{
			var r = dims.ToRectangle();
			spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(r.X, r.Y, r.Width, 1), new Color(80, 100, 180));
			spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), new Color(80, 100, 180));
		}

		float iconSize = ShowName ? 28f : 32f;

		if (ShowName)
		{
			// List row mode: icon on left, name text next to it
			UIHelper.DrawItemIconCentered(spriteBatch, ItemType, dims.X + 6f, dims.Y, 32f, dims.Height, iconSize);

			// Item name
			string name = _displayItem.Name;
			float textY = dims.Y + (dims.Height - FontAssets.MouseText.Value.LineSpacing * 0.85f) / 2f;
			Utils.DrawBorderString(spriteBatch, name, new Vector2(dims.X + 42f, textY), Color.White, 0.85f);

			// Rarity color bar on left edge
			Color rarityColor = UIHelper.GetRarityColor(_displayItem.rare);
			spriteBatch.Draw(TextureAssets.MagicPixel.Value,
				new Rectangle((int)dims.X, (int)dims.Y + 2, 3, (int)dims.Height - 4), rarityColor * 0.8f);

			// Pin button (right side of row)
			if (ShowPinButton)
			{
				float pinX = dims.X + dims.Width - 28f;
				float pinY = dims.Y + (dims.Height - 20f) / 2f;
				var pinRect = new Rectangle((int)pinX, (int)pinY, 24, 20);

				bool hoverPin = Main.mouseX >= pinRect.X && Main.mouseX < pinRect.Right
					&& Main.mouseY >= pinRect.Y && Main.mouseY < pinRect.Bottom;

				Color pinBg = IsPinned
					? new Color(35, 70, 35)
					: hoverPin ? new Color(50, 60, 110) : new Color(35, 40, 75);
				spriteBatch.Draw(TextureAssets.MagicPixel.Value, pinRect, pinBg);

				if (IsPinned)
					UIHelper.DrawBorder(spriteBatch, pinRect, new Color(60, 140, 60) * 0.7f);

				string pinIcon = IsPinned ? "OK" : "+";
				Color pinColor = IsPinned ? new Color(80, 220, 80) : hoverPin ? Color.White : Color.Gray;
				Utils.DrawBorderString(spriteBatch, pinIcon, new Vector2(pinX + 4f, pinY + 1f), pinColor, 0.6f);

				if (hoverPin && Main.mouseLeft && Main.mouseLeftRelease)
					OnPinClicked?.Invoke(ItemType);
			}
		}
		else
		{
			// Compact mode: icon centered
			UIHelper.DrawItemIconCentered(spriteBatch, ItemType, dims.X, dims.Y, dims.Width, dims.Height, iconSize);
		}

		// Quantity text (bottom-right)
		if (Quantity > 1)
		{
			string qtyText = Quantity.ToString();
			var font = FontAssets.ItemStack.Value;
			var textPos = new Vector2(dims.X + dims.Width - font.MeasureString(qtyText).X - 2f,
				dims.Y + dims.Height - font.LineSpacing);
			Utils.DrawBorderString(spriteBatch, qtyText, textPos, Color.White);
		}

		// [E] equipped badge (top-right)
		if (ShowEquippedBadge)
		{
			var font = FontAssets.ItemStack.Value;
			var badgePos = new Vector2(dims.X + dims.Width - font.MeasureString("E").X - 4f, dims.Y + 2f);
			Utils.DrawBorderString(spriteBatch, "E", badgePos, Color.Yellow);
		}
	}

}
