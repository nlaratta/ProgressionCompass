using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using CraftPathOptimizer.Content.Progression;

namespace CraftPathOptimizer.UI.Elements;

/// <summary>
/// Row showing a near-complete combination item with progress bar,
/// milestones, missing items, and pin button.
/// </summary>
public class UICombinationRow : UIElement
{
	private readonly CombinationResult _combo;
	private readonly Action<int> _onPin;
	private readonly Action<int> _onAnalyze;
	private readonly bool _isTracked;

	public UICombinationRow(CombinationResult combo, Action<int> onPin, Action<int> onAnalyze, bool isTracked)
	{
		_combo = combo;
		_onPin = onPin;
		_onAnalyze = onAnalyze;
		_isTracked = isTracked;
		Width.Set(0, 1f);
		int milestoneCount = Math.Min(combo.ReadyMilestones?.Count ?? 0, 3);
		int missingShown = Math.Min(combo.MissingItems.Count, 4);
		Height.Set(32f + milestoneCount * 18f + missingShown * 14f, 0f);
	}

	public override void LeftClick(UIMouseEvent evt)
	{
		base.LeftClick(evt);
		var dims = GetDimensions();
		float pinX = dims.X + dims.Width - 40f;
		if (evt.MousePosition.X >= pinX)
			_onPin?.Invoke(_combo.ItemType);
		else
			_onAnalyze?.Invoke(_combo.ItemType);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		float x = dims.X;
		float y = dims.Y;
		bool hovering = IsMouseHovering;

		if (hovering)
			UIHelper.ShowItemTooltip(_combo.ItemType);

		Color bg = hovering ? new Color(45, 52, 100) * 0.5f : new Color(30, 35, 70) * 0.3f;
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(), bg);

		UIHelper.DrawItemIcon(spriteBatch, _combo.ItemType, new Vector2(x + 4f, y + 4f), 22f);

		var item = new Item();
		if (_combo.ItemType > ItemID.None) item.SetDefaults(_combo.ItemType);
		string name = item.Name ?? "?";
		string typeTag = _combo.IsAccessory ? "[Acc]" : "[Wep]";
		Utils.DrawBorderString(spriteBatch, $"{name} {typeTag}", new Vector2(x + 30f, y + 4f), Color.White, 0.7f);

		float barX = dims.X + dims.Width - 130f;
		float barW = 60f;
		float pct = _combo.CompletionPercent / 100f;
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)barX, (int)(y + 6f), (int)barW, 10), new Color(15, 18, 35));
		Color barC = pct > 0.8f ? new Color(50, 180, 50) : pct > 0.5f ? new Color(200, 180, 50) : new Color(180, 100, 50);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)barX, (int)(y + 6f), (int)(barW * pct), 10), barC);

		string fracText = $"{_combo.LeafMaterialsOwned}/{_combo.LeafMaterialsTotal}";
		Utils.DrawBorderString(spriteBatch, fracText, new Vector2(barX + barW + 4f, y + 4f), Color.LightGray, 0.55f);

		float pinX = dims.X + dims.Width - 40f;
		if (_isTracked)
		{
			spriteBatch.Draw(TextureAssets.MagicPixel.Value,
				new Rectangle((int)pinX, (int)(y + 3f), 36, 18), new Color(35, 70, 35));
			Utils.DrawBorderString(spriteBatch, "Pinned", new Vector2(pinX + 2f, y + 4f), new Color(80, 220, 80), 0.45f);
		}
		else
		{
			bool hoverPin = Main.mouseX >= pinX && Main.mouseX < pinX + 36
				&& Main.mouseY >= y && Main.mouseY < y + 24;
			Utils.DrawBorderString(spriteBatch, "Pin", new Vector2(pinX + 4f, y + 5f),
				hoverPin ? Color.Green : Color.Gray, 0.55f);
		}

		float milestoneY = y + 30f;
		if (_combo.ReadyMilestones != null)
		{
			int mShown = 0;
			foreach (var ms in _combo.ReadyMilestones)
			{
				if (mShown >= 3) break;
				UIHelper.DrawItemIcon(spriteBatch, ms.ItemType, new Vector2(x + 30f, milestoneY), 14f);
				var msItem = new Item();
				if (ms.ItemType > ItemID.None) msItem.SetDefaults(ms.ItemType);
				string msLabel = ms.IsReady ? "BUILD NOW" : $"{(int)ms.CompletionPercent}%";
				Color msColor = ms.IsReady ? new Color(80, 255, 80) : new Color(220, 200, 80);
				Utils.DrawBorderString(spriteBatch, $"{msItem.Name} — {msLabel}",
					new Vector2(x + 48f, milestoneY), msColor, 0.5f);
				milestoneY += 18f;
				mShown++;
			}
		}

		float missingY = milestoneY;
		int shown = 0;
		foreach (var (missingType, missingQty) in _combo.MissingItems)
		{
			if (shown >= 4) break;
			UIHelper.DrawItemIcon(spriteBatch, missingType, new Vector2(x + 30f, missingY), 12f);
			var missingItem = new Item();
			if (missingType > ItemID.None) missingItem.SetDefaults(missingType);
			string missingText = missingQty > 1 ? $"{missingItem.Name} x{missingQty}" : missingItem.Name ?? "?";
			Utils.DrawBorderString(spriteBatch, missingText, new Vector2(x + 46f, missingY), new Color(255, 150, 150), 0.5f);
			missingY += 14f;
			shown++;
		}
	}
}
