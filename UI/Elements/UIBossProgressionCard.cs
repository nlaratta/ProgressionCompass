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
/// Displays 2-3 upcoming bosses the player should fight, with
/// summoning info, location, and what defeating them unlocks.
/// </summary>
public class UIBossProgressionCard : UIElement
{
	private readonly List<BossInfo> _bosses;
	private readonly bool _usingBossChecklist;

	public UIBossProgressionCard(List<BossInfo> bosses)
	{
		_bosses = bosses ?? [];
		_usingBossChecklist = BossDatabase.UsingBossChecklist;
		Width.Set(0, 1f);
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
		Utils.DrawBorderString(spriteBatch, "Next Bosses", new Vector2(x + 6f, y + 4f), new Color(180, 190, 220), 0.6f);

		// Data source note (right-aligned)
		string sourceNote = _usingBossChecklist ? "via Boss Checklist" : "Install Boss Checklist for modded bosses";
		Utils.DrawBorderString(spriteBatch, sourceNote,
			new Vector2(x + dims.Width - FontAssets.MouseText.Value.MeasureString(sourceNote).X * 0.35f - 6f, y + 6f),
			Color.Gray, 0.35f);

		y += 22f;

		if (_bosses.Count == 0)
		{
			Utils.DrawBorderString(spriteBatch, "All bosses defeated!",
				new Vector2(x + 6f, y + 20f), new Color(80, 220, 80), 0.6f);
			return;
		}

		// Show up to 3 bosses
		int shown = 0;
		foreach (var boss in _bosses)
		{
			if (shown >= 3) break;
			DrawBossEntry(spriteBatch, boss, x + 4f, y, dims.Width - 8f);
			y += 34f;
			shown++;
		}
	}

	private static void DrawBossEntry(SpriteBatch spriteBatch, BossInfo boss, float x, float y, float width)
	{
		// Try to draw boss head icon
		bool drewIcon = false;
		if (boss.NpcType > 0)
		{
			try
			{
				// Load NPC texture if needed
				Main.instance.LoadNPC(boss.NpcType);

				// Try boss head texture
				var npc = new NPC();
				npc.SetDefaults(boss.NpcType);
				int headIdx = npc.GetBossHeadTextureIndex();
				if (headIdx >= 0 && headIdx < TextureAssets.NpcHeadBoss.Length)
				{
					var headAsset = TextureAssets.NpcHeadBoss[headIdx];
					if (!headAsset.IsLoaded)
						Main.instance.LoadNPC(boss.NpcType);
					var headTex = headAsset.Value;
					if (headTex != null)
					{
						float scale = 24f / Math.Max(headTex.Width, headTex.Height);
						spriteBatch.Draw(headTex, new Vector2(x + 2f, y + 2f), null, Color.White,
							0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
						drewIcon = true;
					}
				}
			}
			catch { }
		}

		// Fallback: draw first key drop icon
		if (!drewIcon && boss.KeyDropItemTypes.Count > 0)
			UIHelper.DrawItemIcon(spriteBatch, boss.KeyDropItemTypes[0], new Vector2(x + 2f, y + 2f), 22f);

		float textX = x + 30f;

		// Boss name + optional tag
		string nameText = boss.Name;
		Color nameColor = Color.White;
		if (boss.IsOptional)
		{
			nameText += " (optional)";
			nameColor = Color.LightGray;
		}
		Utils.DrawBorderString(spriteBatch, nameText, new Vector2(textX, y), nameColor, 0.55f);

		// Summon hint + location
		string infoLine = boss.SummonHint ?? "";
		if (!string.IsNullOrEmpty(boss.Location))
			infoLine += $" — {boss.Location}";
		Utils.DrawBorderString(spriteBatch, infoLine, new Vector2(textX, y + 14f), Color.Gray, 0.5f);

		// Unlocks (if any)
		if (!string.IsNullOrEmpty(boss.Unlocks))
		{
			float unlockX = textX + FontAssets.MouseText.Value.MeasureString(infoLine).X * 0.5f + 8f;
			if (unlockX < x + width - 50f)
				Utils.DrawBorderString(spriteBatch, $"Unlocks: {boss.Unlocks}",
					new Vector2(textX, y + 24f), new Color(100, 200, 100), 0.5f);
		}
	}
}
