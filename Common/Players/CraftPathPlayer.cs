using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using CraftPathOptimizer.Common.Systems;

namespace CraftPathOptimizer.Common.Players;

public class CraftPathPlayer : ModPlayer
{
	public override void ProcessTriggers(TriggersSet triggersSet)
	{
		if (CraftPathOptimizer.ToggleUIKeybind.JustPressed)
		{
			ModContent.GetInstance<CraftPathUISystem>().ToggleUI();
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
