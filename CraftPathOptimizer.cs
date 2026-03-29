using Terraria.ModLoader;

namespace CraftPathOptimizer;

public class CraftPathOptimizer : Mod
{
	public static CraftPathOptimizer Instance { get; private set; }
	public static ModKeybind ToggleUIKeybind { get; private set; }

	public override void Load()
	{
		Instance = this;
		ToggleUIKeybind = KeybindLoader.RegisterKeybind(this, "Toggle Craft Path", "P");
	}

	public override void Unload()
	{
		ToggleUIKeybind = null;
		Instance = null;
	}
}
