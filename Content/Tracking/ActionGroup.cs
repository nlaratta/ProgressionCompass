using System.Collections.Generic;
using ProgressionCompass.Content.Graph;

namespace ProgressionCompass.Content.Tracking;

public enum ActionType
{
	CraftNow = 0,
	Buy = 1,
	Farm = 2,
	Event = 3,
	Boss = 4,
	Explore = 5,
	Unknown = 6
}

/// <summary>
/// A group of related actions the player should take. Farm groups cluster
/// multiple items by location so the player makes one trip for several drops.
/// </summary>
public class ActionGroup
{
	public ActionType Type { get; init; }

	/// <summary>Human-readable heading: "Underground Corruption", "Plantera", "Goblin Tinkerer"</summary>
	public string Heading { get; init; }

	/// <summary>Subheading: "All ingredients ready!", "Break Plantera's Bulb", etc.</summary>
	public string Description { get; init; }

	/// <summary>Individual action items within this group.</summary>
	public List<ActionItem> Items { get; init; } = [];

	/// <summary>Sort priority within the same ActionType. Lower = more important.</summary>
	public int SubPriority { get; init; }
}

/// <summary>
/// A single actionable item within a group: "Kill Corruptor → Vitamins (1/100), need 1"
/// </summary>
public class ActionItem
{
	/// <summary>The item the player needs.</summary>
	public int ItemType { get; init; }
	public int QuantityNeeded { get; init; }
	public int QuantityOwned { get; init; }

	/// <summary>How to get it: "Kill Corruptor", "Craft at Tinkerer's Workshop", "Buy for 10g"</summary>
	public string HowToGet { get; init; }

	/// <summary>Drop rate as a display string: "1/100", "100%", null for non-drops.</summary>
	public string DropRateText { get; init; }

	/// <summary>For CraftNow items: list of (ingredientItemType, quantity) needed.</summary>
	public List<(int itemType, int quantity)> Ingredients { get; init; }

	/// <summary>Source NPC/entity type for icon display. -1 if N/A.</summary>
	public int SourceEntityId { get; init; } = -1;

	/// <summary>The underlying item source data.</summary>
	public ItemSource Source { get; init; }

	/// <summary>All known sources for this item (for multi-source display).</summary>
	public List<ItemSource> AllSources { get; init; }

	/// <summary>True if the player has this item equipped (armor/accessory). Warns about needing to unequip.</summary>
	public bool IsEquipped { get; init; }

	/// <summary>When multiple goals need this material, shows which goals. Null if only 1 goal.</summary>
	public string GoalAttribution { get; init; }
}
