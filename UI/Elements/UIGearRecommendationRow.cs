using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using CraftPathOptimizer.Content.Graph;
using CraftPathOptimizer.Content.Progression;

namespace CraftPathOptimizer.UI.Elements;

/// <summary>
/// Row showing a gear upgrade recommendation with improvement text,
/// ingredient details / source info, and a progress bar.
/// </summary>
public class UIGearRecommendationRow : UIElement
{
	private readonly GearRecommendation _rec;
	private readonly Action<int> _onAnalyze;

	public UIGearRecommendationRow(GearRecommendation rec, Action<int> onAnalyze)
	{
		_rec = rec;
		_onAnalyze = onAnalyze;
		Width.Set(0, 1f);

		float h = 26f;
		if (rec.HasRecipe && rec.Ingredients != null)
		{
			h += 18f;
			h += 14f;
		}
		else if (rec.AllSources != null && rec.AllSources.Count > 0)
		{
			h += Math.Min(rec.AllSources.Count, 3) * 14f;
		}
		else if (!string.IsNullOrEmpty(rec.SourceHint))
		{
			h += 14f;
		}
		Height.Set(h, 0f);
	}

	public override void LeftClick(UIMouseEvent evt)
	{
		base.LeftClick(evt);
		_onAnalyze?.Invoke(_rec.ItemType);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		float x = dims.X;
		float y = dims.Y;
		bool hovering = IsMouseHovering;

		if (hovering)
			UIHelper.ShowItemTooltip(_rec.ItemType);

		Color bg = hovering ? new Color(45, 52, 100) * 0.4f : new Color(30, 35, 70) * 0.25f;
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(), bg);

		var item = new Item();
		if (_rec.ItemType > ItemID.None) item.SetDefaults(_rec.ItemType);
		Color rarityColor = UIHelper.GetRarityColor(item.rare);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)x, (int)y + 2, 3, (int)dims.Height - 4), rarityColor * 0.8f);

		UIHelper.DrawItemIcon(spriteBatch, _rec.ItemType, new Vector2(x + 8f, y + 2f), 20f);

		string name = item.Name ?? "?";
		Utils.DrawBorderString(spriteBatch, name, new Vector2(x + 32f, y + 2f), Color.White, 0.65f);

		float nameW = FontAssets.MouseText.Value.MeasureString(name).X * 0.65f;
		Utils.DrawBorderString(spriteBatch, _rec.ImprovementText,
			new Vector2(x + 36f + nameW, y + 3f), new Color(150, 220, 150), 0.5f);

		string badge;
		Color badgeColor;
		if (_rec.CraftableNow)
		{ badge = "CRAFTABLE"; badgeColor = new Color(80, 220, 80); }
		else if (_rec.HasRecipe && _rec.MissingIngredientCount > 0)
		{ badge = $"{_rec.TotalIngredientCount - _rec.MissingIngredientCount}/{_rec.TotalIngredientCount}"; badgeColor = new Color(220, 180, 80); }
		else if (!_rec.HasRecipe)
		{ badge = ""; badgeColor = Color.Gray; }
		else
		{ badge = "Available"; badgeColor = Color.Gray; }

		if (!string.IsNullOrEmpty(badge))
		{
			float badgeW = FontAssets.MouseText.Value.MeasureString(badge).X * 0.5f;
			Utils.DrawBorderString(spriteBatch, badge,
				new Vector2(dims.X + dims.Width - badgeW - 6f, y + 3f), badgeColor, 0.5f);
		}

		float lineY = y + 24f;

		if (_rec.HasRecipe && _rec.Ingredients != null)
		{
			float ingX = x + 14f;
			foreach (var (ingType, needed, owned) in _rec.Ingredients)
			{
				UIHelper.DrawItemIcon(spriteBatch, ingType, new Vector2(ingX, lineY), 14f);
				var ingItem = new Item();
				if (ingType > ItemID.None) ingItem.SetDefaults(ingType);

				bool hasEnough = owned >= needed;
				string ingText = needed > 1 ? $"{ingItem.Name} x{needed}" : ingItem.Name ?? "?";
				if (ingText.Length > 12) ingText = ingText[..11] + "..";

				Color ingColor = hasEnough ? new Color(100, 200, 100) : new Color(200, 130, 130);
				Utils.DrawBorderString(spriteBatch, ingText, new Vector2(ingX + 16f, lineY + 1f), ingColor, 0.42f);

				float textW = FontAssets.MouseText.Value.MeasureString(ingText).X * 0.42f;
				ingX += 18f + textW + 6f;

				if (ingX > dims.X + dims.Width - 40f)
				{
					Utils.DrawBorderString(spriteBatch, "...", new Vector2(ingX, lineY + 1f), Color.Gray, 0.42f);
					break;
				}
			}

			lineY += 16f;

			float barX = x + 14f;
			float barW = dims.Width - 28f;
			float pct = _rec.TotalIngredientCount > 0
				? (float)(_rec.TotalIngredientCount - _rec.MissingIngredientCount) / _rec.TotalIngredientCount
				: 0f;
			spriteBatch.Draw(TextureAssets.MagicPixel.Value,
				new Rectangle((int)barX, (int)lineY, (int)barW, 6), new Color(15, 18, 35));
			Color barC = pct >= 1f ? new Color(50, 180, 50) : pct > 0.5f ? new Color(200, 180, 50) : new Color(180, 80, 50);
			int fillW = (int)(barW * pct);
			if (fillW > 0)
				spriteBatch.Draw(TextureAssets.MagicPixel.Value,
					new Rectangle((int)barX, (int)lineY, fillW, 6), barC);
		}
		else if (_rec.AllSources != null && _rec.AllSources.Count > 0)
		{
			int shown = 0;
			foreach (var src in _rec.AllSources)
			{
				if (shown >= 3) break;
				Color srcColor = UIHelper.GetSourceColor(src.Type);
				Utils.DrawBorderString(spriteBatch, src.Description,
					new Vector2(x + 14f, lineY), srcColor, 0.48f);
				lineY += 14f;
				shown++;
			}
		}
		else if (!string.IsNullOrEmpty(_rec.SourceHint))
		{
			Utils.DrawBorderString(spriteBatch, _rec.SourceHint,
				new Vector2(x + 14f, lineY), Color.Gray, 0.48f);
		}
	}
}
