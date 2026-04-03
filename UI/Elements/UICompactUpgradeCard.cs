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
	private readonly Action<int> _onPin;
	private readonly Item _displayItem;
	private readonly string _sourceHint;

	/// <summary>Set externally: returns true if this item type should be highlighted.</summary>
	public Func<int, bool> IsHighlighted { get; set; }

	/// <summary>Set externally: called when hovering to highlight related locations.</summary>
	public Action<int> OnHoverStart { get; set; }
	public Action OnHoverEnd { get; set; }

	private const float CardHeight = 48f;
	private const float PinSize = 14f;
	private bool _wasHovering;

	public UICompactUpgradeCard(UpgradeItem item, Action<int> onAnalyze, Action<int> onPin = null)
	{
		_item = item;
		_onAnalyze = onAnalyze;
		_onPin = onPin;

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

		// Check if click is on pin button (top-right corner)
		if (_onPin != null)
		{
			var dims = GetDimensions();
			float pinX = dims.X + dims.Width - PinSize - 4f;
			float pinY = dims.Y + 2f;
			if (Main.mouseX >= pinX && Main.mouseX < pinX + PinSize + 4f
				&& Main.mouseY >= pinY && Main.mouseY < pinY + PinSize + 4f)
			{
				_onPin.Invoke(_item.ItemType);
				return;
			}
		}

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
			// Show item tooltip with improvement text as suffix
			string suffix = _item.ImprovementText != null ? $"\n{_item.ImprovementText}" : null;
			UIHelper.ShowItemTooltip(_item.ItemType, suffix);
			Main.LocalPlayer.mouseInterface = true;
		}

		// Cross-column hover linkage
		bool highlighted = IsHighlighted?.Invoke(_item.ItemType) ?? false;
		if (hover)
			OnHoverStart?.Invoke(_item.ItemType);
		else if (!hover && _wasHovering)
			OnHoverEnd?.Invoke();
		_wasHovering = hover;

		// Rarity-driven visual intensity
		int rarity = _displayItem.rare;
		Color rarityColor = UIHelper.GetRarityColor(rarity);
		float intensity = Math.Clamp(rarity / 9f, 0f, 1f);

		// Background: tinted with rarity color at higher rarities
		Color baseBg = hover ? new Color(40, 50, 80) * 0.7f
			: highlighted ? new Color(50, 60, 100) * 0.6f
			: new Color(30, 35, 60) * 0.5f;
		Color tintBg = new(
			(int)(rarityColor.R * 0.12f),
			(int)(rarityColor.G * 0.12f),
			(int)(rarityColor.B * 0.12f));
		Color bg = Color.Lerp(baseBg, tintBg, intensity * 0.5f);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(), bg);

		// Rarity-colored border: opacity scales with tier
		float borderOpacity = 0.15f + intensity * 0.45f;
		UIHelper.DrawBorder(spriteBatch, dims.ToRectangle(), rarityColor * borderOpacity);
		if (highlighted)
			UIHelper.DrawBorder(spriteBatch,
				new Rectangle((int)x + 1, (int)y + 1, (int)w - 2, (int)CardHeight - 2),
				new Color(100, 160, 255) * 0.5f);

		// Pin button (top-right corner)
		if (_onPin != null)
		{
			float pinX = x + w - PinSize - 4f;
			float pinY = y + 2f;
			bool pinHover = Main.mouseX >= pinX && Main.mouseX < pinX + PinSize + 4f
				&& Main.mouseY >= pinY && Main.mouseY < pinY + PinSize + 4f;
			string pinText = _item.IsTracked ? "*" : "+";
			Color pinColor = _item.IsTracked ? new Color(80, 220, 80)
				: pinHover ? Color.White : new Color(100, 110, 140);
			Utils.DrawBorderString(spriteBatch, pinText, new Vector2(pinX + 2f, pinY), pinColor, 0.6f);
		}

		// Left accent bar: thicker for higher rarity
		int accentWidth = 3 + (int)(intensity * 2f);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)x, (int)y, accentWidth, (int)CardHeight), rarityColor * 0.9f);

		// Item icon
		float iconX = x + 6f;
		float iconY = y + 4f;
		UIHelper.DrawItemIcon(spriteBatch, _item.ItemType, new Vector2(iconX, iconY), 20f);

		// Item name (truncated, tinted by rarity at higher tiers)
		float textX = iconX + 24f;
		string name = _displayItem.Name ?? "Unknown";
		float maxNameWidth = w - 34f;
		string displayName = TruncateName(name, maxNameWidth, 0.55f);
		Color nameColor = Color.Lerp(Color.White, rarityColor, intensity * 0.4f);
		Utils.DrawBorderString(spriteBatch, displayName,
			new Vector2(textX, y + 3f), nameColor, 0.55f);

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
