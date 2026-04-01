using Terraria;
using Terraria.ModLoader;
using ProgressionCompass.Common.Players;
using ProgressionCompass.Common.Systems;

namespace ProgressionCompass.Common;

/// <summary>
/// Detects when a tracked item is picked up and triggers a toast notification
/// plus an immediate tracking refresh.
/// </summary>
public class TrackingGlobalItem : GlobalItem
{
	public override bool OnPickup(Item item, Player player)
	{
		if (player.whoAmI != Main.myPlayer)
			return true;

		var trackingPlayer = player.GetModPlayer<TrackingPlayer>();
		if (trackingPlayer?.Tracking == null || trackingPlayer.Tracking.Count == 0)
			return true;

		// Check if this item is relevant to any tracked goal
		bool relevant = false;
		foreach (var goal in trackingPlayer.Tracking.Goals)
		{
			if (goal.Result?.Materials == null)
				continue;
			foreach (var mat in goal.Result.Materials)
			{
				if (mat.ItemType == item.type)
				{
					relevant = true;
					break;
				}
			}
			if (relevant) break;
		}

		if (relevant)
		{
			// Queue a toast notification
			var overlay = ModContent.GetInstance<TrackingOverlaySystem>();
			overlay?.QueueToast(item.type, item.stack);

			// Force an immediate tracking refresh
			trackingPlayer.RefreshTracking();
		}

		return true;
	}
}
