using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using ProgressionCompass.UI;

namespace ProgressionCompass.Common.Systems;

/// <summary>
/// Manages the UI lifecycle: creates the UserInterface and UIState,
/// handles per-frame updates, and inserts our draw layer into
/// Terraria's rendering pipeline.
/// </summary>
public class CraftPathUISystem : ModSystem
{
	internal UserInterface CraftPathInterface;
	internal CraftPathState CraftPathUI;
	private GameTime _lastGameTime;

	public bool IsVisible => CraftPathInterface?.CurrentState != null;

	public override void Load()
	{
		if (Main.dedServ)
			return;

		CraftPathInterface = new UserInterface();
		CraftPathUI = new CraftPathState();
		CraftPathUI.Activate();
	}

	public override void Unload()
	{
		CraftPathUI = null;
		CraftPathInterface = null;
	}

	public void ToggleUI()
	{
		if (IsVisible)
		{
			CraftPathInterface.SetState(null);
			// Close upgrade recommendations window when main panel closes
			ModContent.GetInstance<UpgradeWindowSystem>()?.Close();
		}
		else
		{
			CraftPathInterface.SetState(CraftPathUI);
		}
	}

	/// <summary>Opens the panel (if needed) and sets the given item as the analysis target.</summary>
	public void InspectItem(int itemType)
	{
		CraftPathUI?.InspectItem(itemType);
	}

	/// <summary>Ensures the main panel is visible and analyzes the given item.</summary>
	public void ShowAndAnalyze(int itemType)
	{
		if (!IsVisible)
			CraftPathInterface?.SetState(CraftPathUI);
		CraftPathUI?.InspectItem(itemType);
	}

	public override void UpdateUI(GameTime gameTime)
	{
		_lastGameTime = gameTime;
		if (CraftPathInterface?.CurrentState != null)
			CraftPathInterface.Update(gameTime);
	}

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		// Insert our UI just before the vanilla mouse text layer
		int mouseTextIndex = layers.FindIndex(
			layer => layer.Name.Equals("Vanilla: Mouse Text"));

		if (mouseTextIndex != -1)
		{
			layers.Insert(mouseTextIndex, new LegacyGameInterfaceLayer(
				"ProgressionCompass: Craft Path UI",
				delegate
				{
					if (CraftPathInterface?.CurrentState != null)
						CraftPathInterface.Draw(Main.spriteBatch, _lastGameTime);
					return true;
				},
				InterfaceScaleType.UI));
		}
	}
}
