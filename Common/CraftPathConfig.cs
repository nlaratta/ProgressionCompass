using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace CraftPathOptimizer.Common;

/// <summary>
/// Client-side mod configuration. Persisted across sessions.
/// Accessible in-game via Settings -> Mod Configuration -> Craft Path Optimizer.
/// Labels and tooltips are defined in Localization/en-US.hjson.
/// </summary>
public class CraftPathConfig : ModConfig
{
	public override ConfigScope Mode => ConfigScope.ClientSide;

	[Header("InventoryScanning")]
	[Range(10f, 200f)]
	[DefaultValue(50f)]
	[Slider]
	public float ChestScanRange { get; set; } = 50f;

	[DefaultValue(true)]
	public bool ChestScanDefault { get; set; } = true;

	[Header("Search")]
	[Range(10, 200)]
	[DefaultValue(50)]
	public int MaxSearchResults { get; set; } = 50;

	[Header("Display")]
	[DefaultValue(TabOption.Materials)]
	[DrawTicks]
	public TabOption DefaultTab { get; set; } = TabOption.Materials;

	[DefaultValue(true)]
	public bool AutoExpandTree { get; set; } = true;
}

public enum TabOption
{
	Materials,
	Tree,
	CraftOrder
}
