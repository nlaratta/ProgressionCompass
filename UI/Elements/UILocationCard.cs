using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using ProgressionCompass.Content.Graph;
using ProgressionCompass.Content.Progression;
using ProgressionCompass.Content.Tracking;

namespace ProgressionCompass.UI.Elements;

/// <summary>
/// Renders a single ranked location as a card in the "Where to Go" column.
/// Shows location name, star rating, context hint, and drop entries
/// with NPC/item icons linking to the upgrades they feed.
/// </summary>
public class UILocationCard : UIElement
{
	private readonly LocationRanking _ranking;
	private readonly Action<int> _onAnalyzeItem;
	private readonly List<DropDisplayInfo> _dropDisplay = [];

	/// <summary>Set externally: returns true if this location should be highlighted.</summary>
	public Func<string, bool> IsHighlighted { get; set; }

	/// <summary>Set externally: called on hover to highlight fed upgrade types.</summary>
	public Action<HashSet<int>> OnHoverStart { get; set; }
	public Action OnHoverEnd { get; set; }

	private bool _wasHovering;

	private const float HeaderH = 26f;
	private const float HintH = 16f;
	private const float SubtitleH = 16f;
	private const float DropEntryH = 22f;
	private const float CardPaddingBottom = 6f;

	public UILocationCard(LocationRanking ranking, Action<int> onAnalyzeItem)
	{
		_ranking = ranking;
		_onAnalyzeItem = onAnalyzeItem;
		Width.Set(0, 1f);

		// Pre-compute drop display info and height
		float h = HeaderH + SubtitleH + CardPaddingBottom;
		if (!string.IsNullOrEmpty(ranking.ContextHint))
			h += HintH;

		foreach (var drop in ranking.Drops)
		{
			var info = new DropDisplayInfo
			{
				Drop = drop,
				NpcName = GetNpcName(drop.Source),
				DropItemName = GetItemName(drop.ItemType)
			};

			// Collect distinct upgrade names this drop feeds
			var seen = new HashSet<int>();
			foreach (var c in drop.FeedsInto)
			{
				if (seen.Add(c.UpgradeItemType))
				{
					info.UpgradeEntries.Add(new UpgradeDisplayEntry
					{
						ItemType = c.UpgradeItemType,
						Name = GetItemName(c.UpgradeItemType),
						CompletionPercent = c.UpgradeCompletionPercent,
						Proximity = c.UpgradeProximity
					});
				}
			}

			_dropDisplay.Add(info);
			h += DropEntryH;
			// Sub-entries for upgrade attribution (if multiple)
			if (info.UpgradeEntries.Count > 1)
				h += (info.UpgradeEntries.Count - 1) * 14f;
		}

		Height.Set(h, 0f);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		float x = dims.X;
		float y = dims.Y;
		float w = dims.Width;

		// Cross-column hover linkage
		bool hover = IsMouseHovering;
		bool highlighted = IsHighlighted?.Invoke(_ranking.LocationName) ?? false;
		if (hover && !_wasHovering)
		{
			// Collect all upgrade types this location feeds
			var fedTypes = new HashSet<int>();
			foreach (var drop in _ranking.Drops)
				foreach (var c in drop.FeedsInto)
					fedTypes.Add(c.UpgradeItemType);
			OnHoverStart?.Invoke(fedTypes);
		}
		else if (!hover && _wasHovering)
			OnHoverEnd?.Invoke();
		_wasHovering = hover;

		// Background (with highlight)
		Color bgColor = highlighted ? new Color(35, 45, 75) * 0.7f : new Color(25, 30, 55) * 0.6f;
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(), bgColor);
		if (highlighted)
			UIHelper.DrawBorder(spriteBatch, dims.ToRectangle(), new Color(100, 160, 255) * 0.5f);

		// Left accent border (4px, colored by biome or source type)
		Color accentColor = _ranking.Type == LocationType.Biome
			? UIHelper.GetBiomeColor(_ranking.LocationName)
			: GetLocationTypeColor(_ranking.Type);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)x, (int)y, 4, (int)dims.Height), accentColor);

		float cx = x + 10f;
		float cy = y + 4f;

		// Header: location name + star rating
		Utils.DrawBorderString(spriteBatch, _ranking.LocationName,
			new Vector2(cx, cy), Color.White, 0.65f);
		float starX = x + w - 56f;
		UIHelper.DrawStarRating(spriteBatch, starX, cy + 2f, _ranking.StarRating);
		cy += HeaderH;

		// Context hint (boss summon, event trigger)
		if (!string.IsNullOrEmpty(_ranking.ContextHint))
		{
			Utils.DrawBorderString(spriteBatch, _ranking.ContextHint,
				new Vector2(cx, cy), new Color(160, 160, 190), 0.5f);
			cy += HintH;
		}

		// Subtitle: "Feeds N upgrades"
		string subtitle = _ranking.UpgradeCount == 1 ? "Feeds 1 upgrade" : $"Feeds {_ranking.UpgradeCount} upgrades";
		Utils.DrawBorderString(spriteBatch, subtitle,
			new Vector2(cx, cy), Color.Gray, 0.5f);
		cy += SubtitleH;

		// Drop entries
		foreach (var info in _dropDisplay)
		{
			DrawDropEntry(spriteBatch, info, cx, cy, w - 14f);
			cy += DropEntryH;

			// Extra upgrade attribution lines (if this drop feeds multiple upgrades)
			if (info.UpgradeEntries.Count > 1)
			{
				for (int i = 1; i < info.UpgradeEntries.Count; i++)
				{
					DrawUpgradeAttribution(spriteBatch, info.UpgradeEntries[i], cx + 22f, cy, w - 36f);
					cy += 14f;
				}
			}
		}
	}

	private void DrawDropEntry(SpriteBatch spriteBatch, DropDisplayInfo info, float x, float y, float width)
	{
		float curX = x;

		// Source type dot
		Color srcColor = UIHelper.GetSourceColor(info.Drop.Source.Type);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)curX, (int)(y + 7f), 4, 4), srcColor);
		curX += 8f;

		// NPC icon or material icon
		bool drewNpc = false;
		if (info.Drop.Source.SourceEntityId > 0 &&
			(info.Drop.Source.Type is ItemSourceType.NPCDrop or ItemSourceType.BossDrop
				or ItemSourceType.NPCShop or ItemSourceType.Event or ItemSourceType.BagDrop))
		{
			drewNpc = UIHelper.DrawNpcHeadIcon(spriteBatch, info.Drop.Source.SourceEntityId,
				new Vector2(curX, y), 18f);
		}

		if (!drewNpc)
			UIHelper.DrawItemIcon(spriteBatch, info.Drop.ItemType, new Vector2(curX, y), 18f);

		// Hover tooltip for source icon
		if (Main.mouseX >= curX && Main.mouseX < curX + 18f && Main.mouseY >= y && Main.mouseY < y + 18f)
		{
			if (info.NpcName != null)
				Main.hoverItemName = info.NpcName;
			else
				UIHelper.ShowItemTooltip(info.Drop.ItemType);
		}
		curX += 22f;

		// Drop item name (or "Buy:" prefix for shops)
		string prefix = info.Drop.Source.Type == ItemSourceType.NPCShop ? "Buy: " : "";
		string dropText = $"{prefix}{info.DropItemName}";
		Utils.DrawBorderString(spriteBatch, dropText, new Vector2(curX, y + 1f), Color.LightGray, 0.55f);
		float textW = FontAssets.MouseText.Value.MeasureString(dropText).X * 0.55f;
		curX += textW + 4f;

		// Arrow
		Utils.DrawBorderString(spriteBatch, "->", new Vector2(curX, y + 1f), new Color(80, 90, 120), 0.5f);
		curX += 18f;

		// First upgrade entry (primary)
		if (info.UpgradeEntries.Count > 0)
			DrawUpgradeRef(spriteBatch, info.UpgradeEntries[0], curX, y, width - (curX - x));
	}

	private void DrawUpgradeRef(SpriteBatch spriteBatch, UpgradeDisplayEntry entry, float x, float y, float maxW)
	{
		// Upgrade item icon (small)
		UIHelper.DrawItemIcon(spriteBatch, entry.ItemType, new Vector2(x, y), 16f);

		// Hover tooltip + click to analyze
		if (Main.mouseX >= x && Main.mouseX < x + 16f && Main.mouseY >= y && Main.mouseY < y + 16f)
		{
			UIHelper.ShowItemTooltip(entry.ItemType);
			if (Main.mouseLeft && Main.mouseLeftRelease)
				_onAnalyzeItem?.Invoke(entry.ItemType);
		}

		// Completion % with proximity color
		Color pctColor = entry.Proximity switch
		{
			Proximity.AlmostReady => new Color(80, 220, 80),
			Proximity.NeedsWork => new Color(220, 200, 60),
			_ => new Color(160, 160, 180)
		};
		string pctText = $"({entry.CompletionPercent:F0}%)";
		Utils.DrawBorderString(spriteBatch, pctText, new Vector2(x + 20f, y + 1f), pctColor, 0.48f);
	}

	private static void DrawUpgradeAttribution(SpriteBatch spriteBatch, UpgradeDisplayEntry entry, float x, float y, float width)
	{
		Utils.DrawBorderString(spriteBatch, "->", new Vector2(x, y), new Color(80, 90, 120), 0.5f);
		UIHelper.DrawItemIcon(spriteBatch, entry.ItemType, new Vector2(x + 16f, y), 14f);

		Color pctColor = entry.Proximity switch
		{
			Proximity.AlmostReady => new Color(80, 220, 80),
			Proximity.NeedsWork => new Color(220, 200, 60),
			_ => new Color(160, 160, 180)
		};
		string text = $"{entry.Name} ({entry.CompletionPercent:F0}%)";
		Utils.DrawBorderString(spriteBatch, text, new Vector2(x + 34f, y), pctColor, 0.5f);
	}

	// ── Helpers ──────────────────────────────────────────────────

	private static Color GetLocationTypeColor(LocationType type) => type switch
	{
		LocationType.Boss => new Color(255, 110, 110),
		LocationType.Shop => new Color(110, 255, 110),
		LocationType.Event => new Color(200, 110, 255),
		LocationType.Exploration => new Color(255, 220, 110),
		_ => new Color(150, 160, 180)
	};

	private static string GetItemName(int itemType)
	{
		if (itemType <= ItemID.None) return "Unknown";
		try
		{
			var item = ContentSamples.ItemsByType.GetValueOrDefault(itemType);
			return item?.Name ?? $"Item #{itemType}";
		}
		catch { return $"Item #{itemType}"; }
	}

	private static string GetNpcName(ItemSource source)
	{
		if (source.SourceEntityId <= 0) return null;
		if (source.Type is not (ItemSourceType.NPCDrop or ItemSourceType.BossDrop
			or ItemSourceType.NPCShop or ItemSourceType.Event or ItemSourceType.BagDrop))
			return null;

		try
		{
			var npc = new NPC();
			npc.SetDefaults(source.SourceEntityId);
			return npc.GivenOrTypeName ?? npc.FullName ?? $"NPC #{source.SourceEntityId}";
		}
		catch { return null; }
	}

	// ── Display Info Records ────────────────────────────────────

	private class DropDisplayInfo
	{
		public LocationDrop Drop;
		public string NpcName;
		public string DropItemName;
		public List<UpgradeDisplayEntry> UpgradeEntries = [];
	}

	private class UpgradeDisplayEntry
	{
		public int ItemType;
		public string Name;
		public float CompletionPercent;
		public Proximity Proximity;
	}
}
