using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using CraftPathOptimizer.Content.Tracking;

namespace CraftPathOptimizer.UI.Elements;

/// <summary>
/// Single row for a tracked/pinned goal: icon, name, progress bar, fraction.
/// Click to analyze, right-click to unpin.
/// </summary>
public class UITrackedGoalRow : UIElement
{
	private readonly PinnedGoal _goal;
	private readonly int _index;
	private readonly Item _displayItem;

	public event Action<int> OnClicked;
	public event Action<int> OnRightClicked;

	public UITrackedGoalRow(PinnedGoal goal, int index)
	{
		_goal = goal;
		_index = index;
		_displayItem = new Item();
		if (goal.ItemType > ItemID.None)
			_displayItem.SetDefaults(goal.ItemType);

		Width.Set(0, 1f);
		Height.Set(46f, 0f);
	}

	public override void LeftClick(UIMouseEvent evt)
	{
		base.LeftClick(evt);
		OnClicked?.Invoke(_index);
	}

	public override void RightClick(UIMouseEvent evt)
	{
		base.RightClick(evt);
		OnRightClicked?.Invoke(_index);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		bool hovering = IsMouseHovering;
		float pct = _goal.ProgressPercent / 100f;
		bool isReady = pct >= 1f;

		if (hovering)
			UIHelper.ShowItemTooltip(_goal.ItemType, " (right-click to unpin)");

		Color bg = isReady
			? (hovering ? new Color(40, 80, 40) * 0.7f : new Color(30, 65, 30) * 0.5f)
			: (hovering ? new Color(50, 58, 110) * 0.6f : new Color(35, 40, 75) * 0.4f);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(), bg);

		float x = dims.X + 4f;
		float y = dims.Y + 2f;

		UIHelper.DrawItemIcon(spriteBatch, _goal.ItemType, new Vector2(x, y), 20f);
		x += 24f;

		string name = _displayItem.Name ?? "Unknown";
		if (name.Length > 20) name = name[..19] + "..";
		Utils.DrawBorderString(spriteBatch, name, new Vector2(x, y), Color.White, 0.65f);

		if (isReady)
		{
			// Ready to craft — show prominent badge instead of fraction
			Utils.DrawBorderString(spriteBatch, "CRAFT!",
				new Vector2(dims.X + dims.Width - 50f, y), new Color(80, 255, 80), 0.65f);

			// Full green bar
			float barX = dims.X + 4f;
			float barY = y + 22f;
			float barW = dims.Width - 8f;
			spriteBatch.Draw(TextureAssets.MagicPixel.Value,
				new Rectangle((int)barX, (int)barY, (int)barW, 10), new Color(50, 180, 50));
			Utils.DrawBorderString(spriteBatch, "Ready to craft!",
				new Vector2(barX + (barW - FontAssets.MouseText.Value.MeasureString("Ready to craft!").X * 0.5f) / 2f, barY - 1f),
				Color.White, 0.5f);
		}
		else
		{
			string frac = $"{_goal.OwnedMaterialCount}/{_goal.TotalMaterialCount}";
			float fracW = FontAssets.MouseText.Value.MeasureString(frac).X * 0.6f;
			Utils.DrawBorderString(spriteBatch, frac, new Vector2(dims.X + dims.Width - fracW - 4f, y), Color.LightGray, 0.6f);

			float barX = dims.X + 4f;
			float barY = y + 22f;
			float barW = dims.Width - 8f;
			float barH = 10f;

			spriteBatch.Draw(TextureAssets.MagicPixel.Value,
				new Rectangle((int)barX, (int)barY, (int)barW, (int)barH), new Color(20, 22, 40));

			Color barColor = pct > 0.8f ? new Color(50, 180, 50)
				: pct > 0.4f ? new Color(200, 180, 50)
				: new Color(180, 50, 50);
			int fillW = (int)(barW * pct);
			if (fillW > 0)
				spriteBatch.Draw(TextureAssets.MagicPixel.Value,
					new Rectangle((int)barX, (int)barY, fillW, (int)barH), barColor);

			string pctText = $"{(int)_goal.ProgressPercent}%";
			Utils.DrawBorderString(spriteBatch, pctText,
				new Vector2(barX + (barW - FontAssets.MouseText.Value.MeasureString(pctText).X * 0.5f) / 2f, barY - 1f),
				Color.White, 0.5f);
		}
	}
}
