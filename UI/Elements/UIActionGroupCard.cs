using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using ProgressionCompass.Content.Tracking;

namespace ProgressionCompass.UI.Elements;

/// <summary>
/// Renders a single action group as a card: header with icon + title,
/// followed by individual action items with sources and ingredients.
/// </summary>
public class UIActionGroupCard : UIElement
{
	private readonly ActionGroup _group;
	private const int MaxExtraSourcesShown = 3;

	public UIActionGroupCard(ActionGroup group)
	{
		_group = group;
		Width.Set(0, 1f);
		float h = 48f;
		foreach (var item in group.Items)
		{
			h += 26f;
			if (item.IsEquipped)
				h += 14f;
			if (item.GoalAttribution != null)
				h += 14f;
			if (item.Ingredients != null)
				h += item.Ingredients.Count * 16f;
			if (item.AllSources != null && item.AllSources.Count > 1)
				h += Math.Min(item.AllSources.Count - 1, MaxExtraSourcesShown) * 14f;
		}
		Height.Set(h, 0f);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		float x = dims.X;
		float y = dims.Y;

		Color cardBg = UIHelper.GetActionBackgroundColor(_group.Type);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(), cardBg);
		Color accentColor = UIHelper.GetActionAccentColor(_group.Type);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)x, (int)y, 4, (int)dims.Height), accentColor);

		string typeIcon = _group.Type switch
		{
			ActionType.CraftNow => ">>",
			ActionType.Buy => "$",
			ActionType.Farm => "!",
			ActionType.Event => "*",
			ActionType.Boss => "X",
			ActionType.Explore => "?",
			_ => "-"
		};
		Utils.DrawBorderString(spriteBatch, typeIcon, new Vector2(x + 10f, y + 4f), accentColor, 0.8f);
		Utils.DrawBorderString(spriteBatch, _group.Heading, new Vector2(x + 30f, y + 4f), Color.White, 0.8f);

		if (!string.IsNullOrEmpty(_group.Description))
			Utils.DrawBorderString(spriteBatch, _group.Description, new Vector2(x + 30f, y + 26f), Color.LightGray, 0.6f);

		float itemY = y + 48f;
		foreach (var item in _group.Items)
		{
			DrawActionItem(spriteBatch, item, x + 14f, itemY, dims.Width - 18f);
			itemY += 26f;

			// Equipped warning
			if (item.IsEquipped)
			{
				Utils.DrawBorderString(spriteBatch, "[E] Currently equipped — unequip to use",
					new Vector2(x + 36f, itemY), new Color(255, 200, 60), 0.48f);
				itemY += 14f;
			}

			// Goal attribution
			if (item.GoalAttribution != null)
			{
				Utils.DrawBorderString(spriteBatch, item.GoalAttribution,
					new Vector2(x + 36f, itemY), new Color(140, 140, 170), 0.5f);
				itemY += 14f;
			}

			if (item.Ingredients != null)
			{
				foreach (var (ingType, ingQty) in item.Ingredients)
				{
					DrawIngredientLine(spriteBatch, ingType, ingQty, x + 36f, itemY, dims.Width - 40f);
					itemY += 16f;
				}
			}

			if (item.AllSources != null && item.AllSources.Count > 1)
			{
				int shown = 0;
				for (int s = 0; s < item.AllSources.Count && shown < MaxExtraSourcesShown; s++)
				{
					var src = item.AllSources[s];
					if (src == item.Source) continue;
					string prefix = shown == 0 ? "Also: " : "      ";
					Utils.DrawBorderString(spriteBatch, $"{prefix}{src.Description}",
						new Vector2(x + 36f, itemY), new Color(140, 140, 170), 0.5f);
					itemY += 14f;
					shown++;
				}
			}
		}
	}

	private static void DrawActionItem(SpriteBatch spriteBatch, ActionItem item, float x, float y, float width)
	{
		UIHelper.DrawItemIcon(spriteBatch, item.ItemType, new Vector2(x, y + 2f), 18f);

		var displayItem = new Item();
		if (item.ItemType > ItemID.None)
			displayItem.SetDefaults(item.ItemType);
		string name = displayItem.Name ?? "Unknown";
		Utils.DrawBorderString(spriteBatch, name, new Vector2(x + 22f, y + 2f), Color.White, 0.7f);

		string info = item.HowToGet ?? "";
		if (item.DropRateText != null)
			info += $" ({item.DropRateText})";
		float nameW = FontAssets.MouseText.Value.MeasureString(name).X * 0.7f;
		Utils.DrawBorderString(spriteBatch, info, new Vector2(x + 22f + nameW + 8f, y + 3f), Color.Gray, 0.6f);

		string qtyText = $"{item.QuantityOwned}/{item.QuantityNeeded}";
		Color qtyColor = item.QuantityOwned >= item.QuantityNeeded ? Color.Green : Color.Red;
		float qtyW = FontAssets.MouseText.Value.MeasureString(qtyText).X * 0.65f;
		Utils.DrawBorderString(spriteBatch, qtyText, new Vector2(x + width - qtyW - 4f, y + 2f), qtyColor, 0.65f);

		if (Main.mouseX >= x && Main.mouseX < x + width && Main.mouseY >= y && Main.mouseY < y + 24f)
			UIHelper.ShowItemTooltip(item.ItemType);
	}

	private static void DrawIngredientLine(SpriteBatch spriteBatch, int itemType, int qty, float x, float y, float width)
	{
		Utils.DrawBorderString(spriteBatch, "-", new Vector2(x - 10f, y - 1f), new Color(80, 90, 130), 0.5f);
		UIHelper.DrawItemIcon(spriteBatch, itemType, new Vector2(x, y), 14f, 0.8f);

		var ingItem = new Item();
		if (itemType > ItemID.None) ingItem.SetDefaults(itemType);
		string text = qty > 1 ? $"{ingItem.Name} x{qty}" : ingItem.Name ?? "?";
		Utils.DrawBorderString(spriteBatch, text, new Vector2(x + 18f, y), new Color(180, 180, 200), 0.55f);
	}
}
