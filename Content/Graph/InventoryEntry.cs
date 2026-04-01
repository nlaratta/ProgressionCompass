namespace ProgressionCompass.Content.Graph;

/// <summary>
/// Tracks how many of an item the player owns and where those items are located.
/// The breakdown lets the UI show "(4 in inventory, 8 in chest)" and flag equipped items.
/// </summary>
public record InventoryEntry(
	int PersonalCount,
	int EquippedCount,
	int ChestCount,
	int MagicStorageCount = 0)
{
	public int TotalCount => PersonalCount + EquippedCount + ChestCount + MagicStorageCount;
	public bool IsEquipped => EquippedCount > 0;
	public bool HasMagicStorage => MagicStorageCount > 0;

	/// <summary>
	/// Returns the total available count, optionally excluding chest items
	/// when chest scanning is disabled.
	/// </summary>
	public int AvailableCount(bool includeChests) =>
		PersonalCount + EquippedCount + (includeChests ? ChestCount + MagicStorageCount : 0);

	public static InventoryEntry operator +(InventoryEntry a, InventoryEntry b) =>
		new(a.PersonalCount + b.PersonalCount,
			a.EquippedCount + b.EquippedCount,
			a.ChestCount + b.ChestCount,
			a.MagicStorageCount + b.MagicStorageCount);

	public static InventoryEntry Personal(int count) => new(count, 0, 0);
	public static InventoryEntry Equipped(int count) => new(0, count, 0);
	public static InventoryEntry Chest(int count) => new(0, 0, count);
	public static InventoryEntry MagicStorage(int count) => new(0, 0, 0, count);
	public static InventoryEntry Empty => new(0, 0, 0);
}
