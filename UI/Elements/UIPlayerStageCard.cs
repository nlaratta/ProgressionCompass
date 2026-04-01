using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using CraftPathOptimizer.Content.Progression;

namespace CraftPathOptimizer.UI.Elements;

/// <summary>
/// Displays the player's current progression stats: tier, class,
/// best weapon damage, armor defense, pick power, wing status.
/// </summary>
public class UIPlayerStageCard : UIElement
{
	private readonly Tier _tier;
	private readonly PlayerClass _detectedClass;
	private readonly PlayerStats _stats;
	private readonly PlayerClass _activeFilter;

	public UIPlayerStageCard(Tier tier, PlayerClass detectedClass, PlayerStats stats, PlayerClass activeFilter)
	{
		_tier = tier;
		_detectedClass = detectedClass;
		_stats = stats;
		_activeFilter = activeFilter;
		Width.Set(200f, 0f);
		Height.Set(130f, 0f);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		float x = dims.X;
		float y = dims.Y;

		// Background
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(), new Color(25, 30, 55) * 0.6f);
		UIHelper.DrawBorder(spriteBatch, dims.ToRectangle(), new Color(50, 60, 110) * 0.5f);

		// Header
		Utils.DrawBorderString(spriteBatch, "Your Stage", new Vector2(x + 6f, y + 4f), new Color(180, 190, 220), 0.6f);
		y += 20f;

		// Tier
		Utils.DrawBorderString(spriteBatch, ProgressionTier.GetDisplayName(_tier),
			new Vector2(x + 6f, y), Color.Gold, 0.55f);
		y += 16f;

		// Class
		Utils.DrawBorderString(spriteBatch, $"Class: {ClassDetector.GetDisplayName(_detectedClass)}",
			new Vector2(x + 6f, y), Color.LightGray, 0.5f);
		y += 16f;

		if (_stats == null) return;

		// Best weapon (DPS)
		int bestDmg = _stats.BestDamageForClass(_activeFilter);
		float bestDps = _stats.BestDpsForClass(_activeFilter);
		string dmgLabel = bestDmg > 0 ? $"Weapon: {bestDmg} dmg, {(int)bestDps} DPS" : "Weapon: —";
		Utils.DrawBorderString(spriteBatch, dmgLabel, new Vector2(x + 6f, y), Color.White, 0.5f);
		y += 16f;

		// Defense
		Utils.DrawBorderString(spriteBatch, $"Armor def: {_stats.ArmorDefense}",
			new Vector2(x + 6f, y), Color.White, 0.5f);
		y += 16f;

		// Pick power
		if (_stats.BestPickPower > 0)
		{
			Utils.DrawBorderString(spriteBatch, $"Pick power: {_stats.BestPickPower}%",
				new Vector2(x + 6f, y), Color.White, 0.5f);
			y += 16f;
		}

		// Wings
		if (_stats.WingItemType > 0)
		{
			var wingItem = new Item();
			wingItem.SetDefaults(_stats.WingItemType);
			UIHelper.DrawItemIcon(spriteBatch, _stats.WingItemType, new Vector2(x + 6f, y), 14f);
			Utils.DrawBorderString(spriteBatch, wingItem.Name ?? "Wings",
				new Vector2(x + 24f, y + 1f), new Color(150, 200, 255), 0.45f);
		}
		else
		{
			Utils.DrawBorderString(spriteBatch, "No wings",
				new Vector2(x + 6f, y), Color.Gray, 0.45f);
		}
	}
}
