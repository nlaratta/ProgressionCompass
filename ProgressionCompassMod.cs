using Terraria.ModLoader;

namespace ProgressionCompass;

public class ProgressionCompassMod : Mod
{
	public static ProgressionCompassMod Instance { get; private set; }
	public static ModKeybind ToggleUIKeybind { get; private set; }
	public static ModKeybind InspectItemKeybind { get; private set; }

	public override void Load()
	{
		Instance = this;
		ToggleUIKeybind = KeybindLoader.RegisterKeybind(this, "Toggle Craft Path", "P");
		InspectItemKeybind = KeybindLoader.RegisterKeybind(this, "Inspect Hovered Item", "I");
	}

	public override void Unload()
	{
		ToggleUIKeybind = null;
		InspectItemKeybind = null;
		Instance = null;
	}
}
