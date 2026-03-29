using System;
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
/// Renders a single step in the craft order: step number, output item,
/// crafting station, and list of inputs with their source annotations.
/// </summary>
public class UICraftStep : UIElement
{
	private readonly CraftStep _step;
	private readonly Item _outputItem;

	public UICraftStep(CraftStep step)
	{
		_step = step;
		_outputItem = new Item();
		if (step.ItemType > ItemID.None)
			_outputItem.SetDefaults(step.ItemType);

		Width.Set(0, 1f);
		// Height: header line + one line per input + optional condition line + padding
		float h = 28f + _step.Inputs.Count * 18f + 8f;
		if (_step.ConditionDescription != null)
			h += 18f;
		Height.Set(h, 0f);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		float x = dims.X;
		float y = dims.Y;

		if (IsMouseHovering)
			UIHelper.ShowItemTooltip(_step.ItemType);

		// Background with subtle border
		Color bgColor = IsMouseHovering ? new Color(50, 55, 100) * 0.5f : new Color(35, 40, 80) * 0.35f;
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(), bgColor);

		// Top divider line
		var divider = new Rectangle((int)dims.X + 4, (int)dims.Y, (int)dims.Width - 8, 1);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, divider, new Color(60, 70, 120) * 0.5f);

		// Step number
		string stepLabel = $"Step {_step.StepNumber}:";
		Utils.DrawBorderString(spriteBatch, stepLabel, new Vector2(x + 4f, y + 4f), Color.Gold, 0.8f);
		float stepWidth = FontAssets.MouseText.Value.MeasureString(stepLabel).X * 0.8f;

		// Output item icon
		float iconX = x + stepWidth + 12f;
		if (UIHelper.DrawItemIcon(spriteBatch, _step.ItemType, new Vector2(iconX, y + 2f), 22f))
			iconX += 26f;

		// "Craft Hallowed Bar x12"
		string outputName = _step.ItemType > ItemID.None ? _outputItem.Name : "Unknown";
		string craftText = _step.Quantity > 1 ? $"Craft {outputName} x{_step.Quantity}" : $"Craft {outputName}";
		Utils.DrawBorderString(spriteBatch, craftText, new Vector2(iconX, y + 4f), Color.White, 0.8f);

		// Station text (right-aligned)
		string stationText = GetStationText();
		if (stationText != null)
		{
			float stationWidth = FontAssets.MouseText.Value.MeasureString(stationText).X * 0.65f;
			Utils.DrawBorderString(spriteBatch, stationText, new Vector2(x + dims.Width - stationWidth - 8f, y + 6f), Color.Gray, 0.65f);
		}

		// Inputs
		float inputY = y + 28f;
		foreach (var input in _step.Inputs)
		{
			var inputItem = new Item();
			if (input.ItemType > ItemID.None)
				inputItem.SetDefaults(input.ItemType);

			string inputName = input.ItemType > ItemID.None ? inputItem.Name : "Unknown";
			string inputText = input.Quantity > 1 ? $"{inputName} x{input.Quantity}" : inputName;

			// Source annotation
			string annotation;
			Color annotColor;
			switch (input.Source)
			{
				case CraftStepInputSource.Owned:
					annotation = " (owned)";
					annotColor = Color.Green;
					break;
				case CraftStepInputSource.FromPreviousStep:
					annotation = $" (from Step {input.FromStepNumber})";
					annotColor = Color.Cyan;
					break;
				default:
					annotation = input.AcquireHint != null
						? $" ({input.AcquireHint})"
						: " (need to acquire)";
					annotColor = new Color(255, 150, 150);
					break;
			}

			Utils.DrawBorderString(spriteBatch, $"  {inputText}", new Vector2(x + 20f, inputY), Color.LightGray, 0.7f);
			float inputWidth = FontAssets.MouseText.Value.MeasureString($"  {inputText}").X * 0.7f;
			Utils.DrawBorderString(spriteBatch, annotation, new Vector2(x + 20f + inputWidth, inputY), annotColor, 0.65f);

			inputY += 18f;
		}

		// Condition warning
		if (_step.ConditionDescription != null)
		{
			Utils.DrawBorderString(spriteBatch, $"  Requires: {_step.ConditionDescription}",
				new Vector2(x + 20f, inputY), Color.Orange, 0.65f);
		}
	}

	private string GetStationText()
	{
		if (_step.RequiredTiles == null || _step.RequiredTiles.Length == 0)
			return null;

		var names = new System.Collections.Generic.List<string>();
		foreach (int tileId in _step.RequiredTiles)
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
