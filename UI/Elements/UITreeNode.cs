using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using Terraria.Map;
using CraftPathOptimizer.Content.Graph;

namespace CraftPathOptimizer.UI.Elements;

/// <summary>
/// A single row in the flattened tree view. Each row represents one node
/// at a given depth. The tree panel creates one of these per visible node
/// and adds them to a UIList, so layout and scrolling work properly.
/// </summary>
public class UITreeRow : UIElement
{
	private const float RowHeight = 32f;
	private const float IndentPx = 22f;
	private const float IconSize = 24f;

	private readonly GraphNode _node;
	private readonly int _depth;
	private readonly Item _displayItem;
	private readonly bool _hasChildren;
	private readonly bool _isLastChild;

	public bool IsExpanded { get; set; } = true;
	public bool HasChildren => _hasChildren;
	public GraphNode Node => _node;
	public int Depth => _depth;

	/// <summary>Fired when the user clicks the expand/collapse arrow.</summary>
	public event Action<UITreeRow> OnToggleExpand;

	public UITreeRow(GraphNode node, int depth, bool hasChildren, bool isLastChild)
	{
		_node = node;
		_depth = depth;
		_hasChildren = hasChildren;
		_isLastChild = isLastChild;

		_displayItem = new Item();
		if (node.ItemType > ItemID.None)
			_displayItem.SetDefaults(node.ItemType);

		Width.Set(0, 1f);
		Height.Set(RowHeight, 0f);
	}

	public override void LeftClick(UIMouseEvent evt)
	{
		base.LeftClick(evt);
		if (_hasChildren)
			OnToggleExpand?.Invoke(this);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		float x = dims.X + _depth * IndentPx;
		float y = dims.Y;
		float rowWidth = dims.Width - _depth * IndentPx;

		if (IsMouseHovering)
			UIHelper.ShowItemTooltip(_node.ItemType);

		// Row background
		var rowRect = new Rectangle((int)x, (int)y, (int)rowWidth, (int)RowHeight);
		bool hovering = IsMouseHovering;
		Color bgColor = hovering ? new Color(50, 58, 110) * 0.5f : new Color(35, 40, 75) * 0.25f;
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, rowRect, bgColor);

		// Bottom separator
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)x, (int)(y + RowHeight - 1), (int)rowWidth, 1),
			new Color(50, 60, 100) * 0.3f);

		// Tree connector lines
		DrawConnectors(spriteBatch, dims.X, y);

		// Expand/collapse arrow or leaf dot
		if (_hasChildren)
		{
			string arrow = IsExpanded ? "v" : ">";
			Color arrowColor = hovering ? Color.White : new Color(150, 160, 200);
			Utils.DrawBorderString(spriteBatch, arrow, new Vector2(x + 2f, y + 7f), arrowColor, 0.7f);
		}
		else
		{
			// Leaf dot
			spriteBatch.Draw(TextureAssets.MagicPixel.Value,
				new Rectangle((int)(x + 6), (int)(y + RowHeight / 2 - 1), 4, 4),
				new Color(80, 90, 140));
		}
		x += 16f;

		// Status tag with background pill
		string tag;
		Color tagColor;
		Color tagBg;
		switch (_node)
		{
			case HaveNode:
				tag = "HAVE";
				tagColor = new Color(100, 255, 100);
				tagBg = new Color(30, 80, 30);
				break;
			case AcquireNode:
				tag = "NEED";
				tagColor = new Color(255, 120, 120);
				tagBg = new Color(80, 30, 30);
				break;
			case CraftNode:
				tag = "CRAFT";
				tagColor = new Color(255, 230, 100);
				tagBg = new Color(70, 60, 20);
				break;
			case GroupNode:
				tag = "GROUP";
				tagColor = new Color(100, 220, 255);
				tagBg = new Color(20, 50, 70);
				break;
			default:
				tag = "?";
				tagColor = Color.Gray;
				tagBg = new Color(40, 40, 40);
				break;
		}

		var tagSize = FontAssets.MouseText.Value.MeasureString(tag) * 0.6f;
		var tagRect = new Rectangle((int)x, (int)(y + 5), (int)(tagSize.X + 10), (int)(tagSize.Y + 4));
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, tagRect, tagBg * 0.8f);
		Utils.DrawBorderString(spriteBatch, tag, new Vector2(x + 5f, y + 6f), tagColor, 0.6f);
		x += tagRect.Width + 6f;

		// Item icon
		if (UIHelper.DrawItemIcon(spriteBatch, _node.ItemType, new Vector2(x, y + (RowHeight - IconSize) / 2f), IconSize))
			x += IconSize + 5f;

		// Item name + quantity
		string name = _node.ItemType > ItemID.None ? _displayItem.Name : "Unknown";
		string label = _node.Quantity > 1 ? $"{name} x{_node.Quantity}" : name;
		float textY = y + (RowHeight - FontAssets.MouseText.Value.LineSpacing * 0.8f) / 2f;
		Utils.DrawBorderString(spriteBatch, label, new Vector2(x, textY), Color.White, 0.8f);
		float afterLabel = x + FontAssets.MouseText.Value.MeasureString(label).X * 0.8f + 8f;

		// Crafting station (for CraftNodes)
		if (_node is CraftNode craftNode)
		{
			string stationText = GetStationText(craftNode);
			if (stationText != null)
			{
				Utils.DrawBorderString(spriteBatch, $"@ {stationText}",
					new Vector2(afterLabel, textY + 2f), new Color(130, 140, 180), 0.65f);
			}

			// Condition warning
			if (craftNode.HasConditions)
			{
				float condW = FontAssets.MouseText.Value.MeasureString(craftNode.ConditionDescription).X * 0.55f;
				Utils.DrawBorderString(spriteBatch, craftNode.ConditionDescription,
					new Vector2(dims.X + dims.Width - condW - 8f, textY + 2f), Color.Orange, 0.55f);
			}
		}

		// Source info (for AcquireNodes)
		if (_node is AcquireNode acquire && acquire.Sources.Count > 0)
		{
			var bestSrc = ItemSource.PickBest(acquire.Sources);

			string sourceDesc = bestSrc.Description;
			if (acquire.Sources.Count > 1)
				sourceDesc += $" (+{acquire.Sources.Count - 1} more)";

			Color sourceColor = UIHelper.GetSourceColor(bestSrc.Type);
			Utils.DrawBorderString(spriteBatch, sourceDesc,
				new Vector2(afterLabel, textY + 2f), sourceColor, 0.6f);
		}
	}

	/// <summary>Draw vertical/horizontal connector lines for tree structure.</summary>
	private void DrawConnectors(SpriteBatch spriteBatch, float baseX, float y)
	{
		Color lineColor = new Color(60, 70, 120) * 0.6f;

		for (int d = 1; d <= _depth; d++)
		{
			float lineX = baseX + (d - 1) * IndentPx + 8f;

			if (d == _depth)
			{
				// Horizontal branch to this node
				spriteBatch.Draw(TextureAssets.MagicPixel.Value,
					new Rectangle((int)lineX, (int)(y + RowHeight / 2), (int)(IndentPx - 8), 1), lineColor);

				// Vertical line from parent (half or full height)
				int vHeight = _isLastChild ? (int)(RowHeight / 2) : (int)RowHeight;
				spriteBatch.Draw(TextureAssets.MagicPixel.Value,
					new Rectangle((int)lineX, (int)y, 1, vHeight), lineColor);
			}
			else
			{
				// Continuing vertical line from ancestor
				spriteBatch.Draw(TextureAssets.MagicPixel.Value,
					new Rectangle((int)lineX, (int)y, 1, (int)RowHeight), lineColor);
			}
		}
	}

	private static string GetStationText(CraftNode craft)
	{
		var tiles = craft.Recipe.requiredTile;
		if (tiles == null || tiles.Count == 0)
			return null;

		var names = new List<string>();
		foreach (int tileId in tiles)
		{
			if (tileId < 0)
				continue;
			string tileName = Lang.GetMapObjectName(MapHelper.TileToLookup(tileId, 0));
			if (string.IsNullOrEmpty(tileName))
				tileName = $"Tile#{tileId}";
			names.Add(tileName);
		}

		return names.Count > 0 ? string.Join(" + ", names) : null;
	}
}
