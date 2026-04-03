using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using ProgressionCompass.Content.Progression;

namespace ProgressionCompass.UI.Elements;

/// <summary>
/// Compact upgrade card for the narrow readiness columns in the upgrade window.
/// Shows: item icon, name, progress bar (or CRAFT! badge), source hint.
/// ~48px tall, designed for ~185px column width.
/// </summary>
public class UICompactUpgradeCard : UIElement
{
	private readonly UpgradeItem _item;
	private readonly Action<int> _onAnalyze;
	private readonly Item _displayItem;
	private readonly string _sourceHint;

	private const float CardHeight = 48f;

	public UICompactUpgradeCard(UpgradeItem item, Action<int> onAnalyze)
	{
		_item = item;
		_onAnalyze = onAnalyze;

		_displayItem = new Item();
		_displayItem.SetDefaults(item.ItemType);

		// Build a concise source hint
		_sourceHint = item.ImprovementText ?? item.SourceHint;

		Width.Set(0, 1f);
		Height.Set(CardHeight, 0f);
	}

	public override void LeftClick(UIMouseEvent evt)
	{
		base.LeftClick(evt);
		_onAnalyze?.Invoke(_item.ItemType);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		float x = dims.X;
		float y = dims.Y;
		float w = dims.Width;

		bool hover = IsMouseHovering;
		if (hover)
		{
			UIHelper.ShowItemTooltip(_item.ItemType);
			Main.LocalPlayer.mouseInterface = true;
		}

		// Background
		Color bg = hover ? new Color(40, 50, 80) * 0.7f : new Color(30, 35, 60) * 0.5f;
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(), bg);

		// Rarity accent (left 3px bar)
		Color rarityColor = UIHelper.GetRarityColor(_displayItem.rare);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)x, (int)y, 3, (int)CardHeight), rarityColor * 0.8f);

		// Item icon
		float iconX = x + 6f;
		float iconY = y + 4f;
		UIHelper.DrawItemIcon(spriteBatch, _item.ItemType, new Vector2(iconX, iconY), 20f);

		// Item name (truncated to fit)
		float textX = iconX + 24f;
		string name = _displayItem.Name ?? "Unknown";
		float maxNameWidth = w - 34f;
		string displayName = TruncateName(name, maxNameWidth, 0.55f);
		Utils.DrawBorderString(spriteBatch, displayName,
			new Vector2(textX, y + 3f), Color.White, 0.55f);

		// Row 2: Progress bar or CRAFT! badge
		float row2Y = y + 20f;
		if (_item.Proximity == Proximity.CraftableNow)
		{
			// Green CRAFT! badge
			Utils.DrawBorderString(spriteBatch, "CRAFT!",
				new Vector2(textX, row2Y), new Color(80, 255, 80), 0.5f);
		}
		else
		{
			// Progress bar
			float barX = textX;
			float barW = w - 60f;
			float barH = 6f;
			float pct = _item.CompletionPercent / 100f;

			// Bar background
			spriteBatch.Draw(TextureAssets.MagicPixel.Value,
				new Rectangle((int)barX, (int)(row2Y + 2f), (int)barW, (int)barH),
				new Color(20, 25, 40));

			// Bar fill
			Color barColor = pct >= 0.8f ? new Color(80, 220, 80)
				: pct >= 0.4f ? new Color(220, 200, 60)
				: new Color(200, 80, 80);
			int fillW = (int)(barW * pct);
			if (fillW > 0)
				spriteBatch.Draw(TextureAssets.MagicPixel.Value,
					new Rectangle((int)barX, (int)(row2Y + 2f), fillW, (int)barH),
					barColor);

			// Percentage text (right of bar)
			string pctText = $"{_item.CompletionPercent:F0}%";
			Utils.DrawBorderString(spriteBatch, pctText,
				new Vector2(barX + barW + 4f, row2Y - 1f), barColor, 0.5f);
		}

		// Row 3: Source hint (small, gray)
		if (_sourceHint != null)
		{
			float row3Y = y + 34f;
			string hint = _sourceHint.Length > 28 ? _sourceHint[..27] + ".." : _sourceHint;
			Utils.DrawBorderString(spriteBatch, hint,
				new Vector2(textX, row3Y), Color.Gray, 0.5f);
		}
	}

	private static string TruncateName(string name, float maxWidth, float scale)
	{
		var font = FontAssets.MouseText.Value;
		if (font.MeasureString(name).X * scale <= maxWidth)
			return name;

		// Binary search for truncation point
		for (int len = name.Length - 1; len > 3; len--)
		{
			string truncated = name[..len] + "..";
			if (font.MeasureString(truncated).X * scale <= maxWidth)
				return truncated;
		}
		return name[..3] + "..";
	}
}
