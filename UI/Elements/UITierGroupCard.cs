using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using ProgressionCompass.Content.Progression;

namespace ProgressionCompass.UI.Elements;

/// <summary>
/// Groups multiple items sharing the same primary bar/ore into one card.
/// Shows: tier name, item icons grid, combined progress bar, bottleneck material.
/// Replaces 5-6 individual UICompactUpgradeCards with one compact view.
/// </summary>
public class UITierGroupCard : UIElement
{
	private readonly List<UpgradeItem> _items;
	private readonly string _tierName;
	private readonly int _barItemType;
	private readonly int _totalMissing;
	private readonly int _totalOwned;
	private readonly int _totalNeeded;
	private readonly Action<int> _onAnalyze;
	private readonly Action<int> _onPin;

	/// <summary>For cross-column highlight linkage.</summary>
	public Func<int, bool> IsHighlighted { get; set; }
	public Action<int> OnHoverStart { get; set; }
	public Action OnHoverEnd { get; set; }

	private bool _wasHovering;
	private const float IconSize = 20f;
	private const float IconSpacing = 24f;

	public UITierGroupCard(
		string tierName, int barItemType,
		List<UpgradeItem> items,
		Action<int> onAnalyze, Action<int> onPin = null)
	{
		_tierName = tierName;
		_barItemType = barItemType;
		_items = items;
		_onAnalyze = onAnalyze;
		_onPin = onPin;

		// Compute group-level stats
		var allMissing = new HashSet<int>();
		var allOwned = new HashSet<int>();
		foreach (var item in items)
		{
			if (item.MissingItems != null)
				foreach (var (matType, _) in item.MissingItems)
					allMissing.Add(matType);
			_totalNeeded += item.LeafMaterialsTotal;
			_totalOwned += item.LeafMaterialsOwned;
		}
		_totalMissing = allMissing.Count;

		Width.Set(0, 1f);
		// Header + icon row(s) + progress bar + bottleneck line
		int iconRows = (items.Count + 5) / 6; // 6 icons per row
		Height.Set(26f + iconRows * 26f + 20f, 0f);
	}

	public override void LeftClick(UIMouseEvent evt)
	{
		base.LeftClick(evt);

		// Check which icon was clicked
		var dims = GetDimensions();
		float iconStartX = dims.X + 8f;
		float iconStartY = dims.Y + 24f;

		for (int i = 0; i < _items.Count; i++)
		{
			int row = i / 6;
			int col = i % 6;
			float ix = iconStartX + col * IconSpacing;
			float iy = iconStartY + row * 26f;

			if (Main.mouseX >= ix && Main.mouseX < ix + IconSize
				&& Main.mouseY >= iy && Main.mouseY < iy + IconSize)
			{
				_onAnalyze?.Invoke(_items[i].ItemType);
				return;
			}
		}

		// Click anywhere else → analyze the highest-scored item
		if (_items.Count > 0)
			_onAnalyze?.Invoke(_items[0].ItemType);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		float x = dims.X;
		float y = dims.Y;
		float w = dims.Width;

		bool hover = IsMouseHovering;
		bool highlighted = false;
		foreach (var item in _items)
			if (IsHighlighted?.Invoke(item.ItemType) == true) { highlighted = true; break; }

		if (hover && !_wasHovering)
			OnHoverStart?.Invoke(_items.Count > 0 ? _items[0].ItemType : 0);
		else if (!hover && _wasHovering)
			OnHoverEnd?.Invoke();
		_wasHovering = hover;

		// Background
		Color bg = hover ? new Color(40, 50, 80) * 0.7f
			: highlighted ? new Color(50, 60, 100) * 0.6f
			: new Color(30, 35, 60) * 0.5f;
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(), bg);
		if (highlighted)
			UIHelper.DrawBorder(spriteBatch, dims.ToRectangle(), new Color(100, 160, 255) * 0.5f);

		// Ore accent bar (left 3px)
		Color oreColor = UIHelper.GetRarityColor(
			ContentSamples.ItemsByType.GetValueOrDefault(_barItemType)?.rare ?? 0);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)x, (int)y, 3, (int)dims.Height), oreColor * 0.8f);

		// Header: tier name + item count
		Utils.DrawBorderString(spriteBatch, _tierName,
			new Vector2(x + 8f, y + 2f), Color.White, 0.55f);

		string countText = $"({_items.Count} items)";
		float nameW = FontAssets.MouseText.Value.MeasureString(_tierName).X * 0.55f;
		Utils.DrawBorderString(spriteBatch, countText,
			new Vector2(x + 12f + nameW, y + 3f), Color.Gray, 0.5f);

		// Item icons grid (6 per row)
		float iconStartX = x + 8f;
		float iconStartY = y + 24f;

		for (int i = 0; i < _items.Count; i++)
		{
			int row = i / 6;
			int col = i % 6;
			float ix = iconStartX + col * IconSpacing;
			float iy = iconStartY + row * 26f;

			UIHelper.DrawItemIcon(spriteBatch, _items[i].ItemType, new Vector2(ix, iy), IconSize);

			// Category badge (tiny text below icon)
			string badge = _items[i].Category switch
			{
				GearCategory.Weapon => "W",
				GearCategory.Armor => "A",
				GearCategory.Tool => "T",
				_ => ""
			};
			if (!string.IsNullOrEmpty(badge))
				Utils.DrawBorderString(spriteBatch, badge,
					new Vector2(ix + 6f, iy + 16f), Color.Gray * 0.7f, 0.35f);

			// Hover tooltip
			if (Main.mouseX >= ix && Main.mouseX < ix + IconSize
				&& Main.mouseY >= iy && Main.mouseY < iy + IconSize)
			{
				string suffix = _items[i].ImprovementText != null
					? $"\n{_items[i].ImprovementText}" : null;
				UIHelper.ShowItemTooltip(_items[i].ItemType, suffix);
				Main.LocalPlayer.mouseInterface = true;
			}
		}

		// Progress bar (below icons)
		int iconRows = (_items.Count + 5) / 6;
		float barY = iconStartY + iconRows * 26f;
		float barX = x + 8f;
		float barW = w - 60f;
		float barH = 6f;
		float pct = _totalNeeded > 0 ? (float)_totalOwned / _totalNeeded : 0f;

		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)barX, (int)barY, (int)barW, (int)barH),
			new Color(20, 25, 40));

		Color barColor = pct >= 0.8f ? new Color(80, 220, 80)
			: pct >= 0.4f ? new Color(220, 200, 60)
			: new Color(200, 80, 80);
		int fillW = (int)(barW * pct);
		if (fillW > 0)
			spriteBatch.Draw(TextureAssets.MagicPixel.Value,
				new Rectangle((int)barX, (int)barY, fillW, (int)barH), barColor);

		// Missing count + bottleneck
		string missingText = _totalMissing == 1 ? "1 material left" : $"{_totalMissing} materials left";
		Utils.DrawBorderString(spriteBatch, missingText,
			new Vector2(barX + barW + 4f, barY - 2f), barColor, 0.5f);
	}
}
