using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using ProgressionCompass.Common.Systems;

namespace ProgressionCompass.Common.Players;

public class CraftPathPlayer : ModPlayer
{
	public override void ProcessTriggers(TriggersSet triggersSet)
	{
		if (ProgressionCompassMod.ToggleUIKeybind.JustPressed)
		{
			ModContent.GetInstance<CraftPathUISystem>().ToggleUI();
		}

		// Inspect hovered item: opens panel and analyzes whatever item the cursor is on
		if (ProgressionCompassMod.InspectItemKeybind.JustPressed)
		{
			// Main.HoverItem is set when the mouse is over any item slot in the UI
			if (Main.HoverItem != null && Main.HoverItem.type > Terraria.ID.ItemID.None)
			{
				var uiSystem = ModContent.GetInstance<CraftPathUISystem>();

				// Open panel if not already open
				if (!uiSystem.IsVisible)
					uiSystem.ToggleUI();

				// Set the hovered item as the analysis target
				uiSystem.InspectItem(Main.HoverItem.type);
			}
		}

		// Escape closes the UI when it's open
		if (Main.keyState.IsKeyDown(Keys.Escape) && !Main.oldKeyState.IsKeyDown(Keys.Escape))
		{
			var uiSystem = ModContent.GetInstance<CraftPathUISystem>();
			if (uiSystem.IsVisible)
			{
				uiSystem.ToggleUI();
				// Prevent escape from also opening the game menu
				Main.blockKey = "Escape";
			}
		}
	}
}
