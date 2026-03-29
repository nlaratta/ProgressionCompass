using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace CraftPathOptimizer.UI.Elements;

/// <summary>
/// A horizontal row of tabs with proper panel-style rendering.
/// </summary>
public class UITabBar : UIElement
{
	public int SelectedIndex { get; private set; }
	public event Action<int> OnTabSelected;

	private readonly List<string> _tabLabels = [];

	public UITabBar()
	{
		Width.Set(0, 1f);
		Height.Set(30f, 0f);
	}

	public void AddTab(string label)
	{
		_tabLabels.Add(label);
	}

	/// <summary>Programmatically select a tab (fires OnTabSelected).</summary>
	public void SelectTab(int index)
	{
		if (index >= 0 && index < _tabLabels.Count && index != SelectedIndex)
		{
			SelectedIndex = index;
			OnTabSelected?.Invoke(SelectedIndex);
		}
	}

	public override void LeftClick(UIMouseEvent evt)
	{
		base.LeftClick(evt);

		var dims = GetDimensions();
		float tabWidth = dims.Width / _tabLabels.Count;
		int clickedIndex = (int)((evt.MousePosition.X - dims.X) / tabWidth);
		clickedIndex = Math.Clamp(clickedIndex, 0, _tabLabels.Count - 1);

		if (clickedIndex != SelectedIndex)
		{
			SelectedIndex = clickedIndex;
			OnTabSelected?.Invoke(SelectedIndex);
		}
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		float tabWidth = dims.Width / _tabLabels.Count;
		var font = FontAssets.MouseText.Value;

		for (int i = 0; i < _tabLabels.Count; i++)
		{
			float x = dims.X + i * tabWidth;
			var rect = new Rectangle((int)x + 1, (int)dims.Y, (int)tabWidth - 2, (int)dims.Height);

			bool selected = i == SelectedIndex;
			bool hovering = Main.mouseX >= rect.X && Main.mouseX < rect.Right
				&& Main.mouseY >= rect.Y && Main.mouseY < rect.Bottom;

			// Tab background
			Color bgColor = selected
				? new Color(50, 58, 120)
				: hovering
					? new Color(40, 48, 95)
					: new Color(30, 35, 68);
			spriteBatch.Draw(TextureAssets.MagicPixel.Value, rect, bgColor);

			// Bottom border (selected = bright, others = dim)
			var borderRect = new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2);
			Color borderColor = selected ? new Color(100, 120, 220) : new Color(50, 60, 100);
			spriteBatch.Draw(TextureAssets.MagicPixel.Value, borderRect, borderColor);

			// Tab label
			string label = _tabLabels[i];
			var textSize = font.MeasureString(label);
			var textPos = new Vector2(
				x + (tabWidth - textSize.X * 0.8f) / 2f,
				dims.Y + (dims.Height - textSize.Y * 0.8f) / 2f);

			Color textColor = selected ? Color.White : hovering ? Color.LightGray : Color.Gray;
			Utils.DrawBorderString(spriteBatch, label, textPos, textColor, 0.8f);
		}
	}
}
