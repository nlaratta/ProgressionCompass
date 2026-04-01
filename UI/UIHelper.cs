using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using ProgressionCompass.Content.Graph;
using ProgressionCompass.Content.Tracking;

namespace ProgressionCompass.UI;

/// <summary>
/// Shared rendering helpers used across all UI elements.
/// Consolidates duplicated patterns for icon rendering, tooltips,
/// color mapping, and border drawing.
/// </summary>
public static class UIHelper
{
	// ── Item Icon Rendering ─────────────────────────────────────

	/// <summary>
	/// Draws an item icon with bounds checking, animation frame support,
	/// and automatic scaling to fit within maxSize.
	/// Returns true if the icon was drawn, false if itemType was invalid.
	/// </summary>
	public static bool DrawItemIcon(
		SpriteBatch spriteBatch, int itemType, Vector2 position, float maxSize,
		float opacity = 1f)
	{
		if (itemType <= ItemID.None || itemType >= TextureAssets.Item.Length)
			return false;

		// Force-load the texture if it hasn't been loaded yet.
		// tModLoader lazy-loads item textures — without this, .Value returns null
		// on first access and the icon appears blank until a future frame.
		var asset = TextureAssets.Item[itemType];
		if (!asset.IsLoaded)
			Main.instance.LoadItem(itemType);

		var texture = asset.Value;
		if (texture == null)
			return false;

		var sourceRect = itemType < Main.itemAnimations.Length && Main.itemAnimations[itemType] != null
			? Main.itemAnimations[itemType].GetFrame(texture)
			: texture.Frame();

		float scale = 1f;
		float maxDim = Math.Max(sourceRect.Width, sourceRect.Height);
		if (maxDim > maxSize)
			scale = maxSize / maxDim;

		spriteBatch.Draw(texture, position, sourceRect, Color.White * opacity,
			0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
		return true;
	}

	/// <summary>
	/// Draws an item icon centered within a box of the given size.
	/// </summary>
	public static bool DrawItemIconCentered(
		SpriteBatch spriteBatch, int itemType, float x, float y,
		float boxW, float boxH, float maxSize, float opacity = 1f)
	{
		if (itemType <= ItemID.None || itemType >= TextureAssets.Item.Length)
			return false;

		var asset = TextureAssets.Item[itemType];
		if (!asset.IsLoaded)
			Main.instance.LoadItem(itemType);

		var texture = asset.Value;
		if (texture == null)
			return false;

		var sourceRect = itemType < Main.itemAnimations.Length && Main.itemAnimations[itemType] != null
			? Main.itemAnimations[itemType].GetFrame(texture)
			: texture.Frame();

		float scale = 1f;
		float maxDim = Math.Max(sourceRect.Width, sourceRect.Height);
		if (maxDim > maxSize)
			scale = maxSize / maxDim;

		var pos = new Vector2(
			x + (boxW - sourceRect.Width * scale) / 2f,
			y + (boxH - sourceRect.Height * scale) / 2f);

		spriteBatch.Draw(texture, pos, sourceRect, Color.White * opacity,
			0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
		return true;
	}

	// ── Tooltip ─────────────────────────────────────────────────

	/// <summary>
	/// Shows the vanilla item tooltip for the given item type.
	/// Call from MouseOver or DrawSelf when hovering.
	/// </summary>
	public static void ShowItemTooltip(int itemType, string suffix = null)
	{
		if (itemType <= ItemID.None)
			return;
		var item = new Item();
		item.SetDefaults(itemType);
		Main.hoverItemName = suffix != null ? item.Name + suffix : item.Name;
		Main.HoverItem = item.Clone();
	}

	// ── Source Colors ───────────────────────────────────────────

	/// <summary>
	/// Canonical color for an ItemSourceType. Used in Materials tab,
	/// Tree tab, and TODO tab for consistent source coloring.
	/// </summary>
	public static Color GetSourceColor(ItemSourceType type) => type switch
	{
		ItemSourceType.BossDrop => new Color(255, 110, 110),
		ItemSourceType.NPCDrop => new Color(255, 185, 110),
		ItemSourceType.NPCShop => new Color(110, 255, 110),
		ItemSourceType.Fishing => new Color(110, 185, 255),
		ItemSourceType.Event => new Color(200, 110, 255),
		ItemSourceType.Shimmer => new Color(160, 120, 255),
		ItemSourceType.Gathering => new Color(180, 220, 130),
		ItemSourceType.ChestLoot => new Color(255, 220, 110),
		ItemSourceType.BagDrop => new Color(255, 160, 200),
		_ => new Color(150, 150, 150)
	};

	// ── Action Colors ───────────────────────────────────────────

	/// <summary>Accent/text color for an ActionType.</summary>
	public static Color GetActionAccentColor(ActionType type) => type switch
	{
		ActionType.CraftNow => new Color(80, 220, 80),
		ActionType.Buy => new Color(80, 180, 220),
		ActionType.Farm => new Color(220, 180, 80),
		ActionType.Event => new Color(200, 80, 200),
		ActionType.Boss => new Color(220, 80, 80),
		ActionType.Explore => new Color(180, 180, 100),
		_ => new Color(120, 120, 120)
	};

	/// <summary>Background color for an ActionType card.</summary>
	public static Color GetActionBackgroundColor(ActionType type) => type switch
	{
		ActionType.CraftNow => new Color(30, 60, 30) * 0.5f,
		ActionType.Buy => new Color(30, 50, 60) * 0.5f,
		ActionType.Farm => new Color(50, 40, 25) * 0.5f,
		ActionType.Event => new Color(50, 25, 50) * 0.5f,
		ActionType.Boss => new Color(60, 25, 25) * 0.5f,
		ActionType.Explore => new Color(40, 40, 25) * 0.5f,
		_ => new Color(35, 40, 60) * 0.5f
	};

	// ── Rarity Colors ───────────────────────────────────────────

	/// <summary>Canonical rarity-to-color mapping for item display.</summary>
	public static Color GetRarityColor(int rare) => rare switch
	{
		-1 => new Color(130, 130, 130),
		0 => Color.White,
		1 => new Color(150, 150, 255),
		2 => new Color(150, 255, 150),
		3 => new Color(255, 200, 150),
		4 => new Color(255, 150, 150),
		5 => new Color(255, 150, 200),
		6 => new Color(210, 160, 255),
		7 => new Color(150, 255, 10),
		8 => new Color(255, 255, 10),
		9 => new Color(5, 200, 255),
		10 => new Color(255, 40, 100),
		11 => new Color(180, 40, 255),
		_ => Color.White
	};

	// ── Border Drawing ──────────────────────────────────────────

	/// <summary>Draws a 1px rectangular border.</summary>
	public static void DrawBorder(SpriteBatch spriteBatch, Rectangle rect, Color color)
	{
		var pixel = TextureAssets.MagicPixel.Value;
		spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
		spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
		spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
		spriteBatch.Draw(pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), color);
	}
}
