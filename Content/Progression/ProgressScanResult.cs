using System.Collections.Generic;

namespace CraftPathOptimizer.Content.Progression;

/// <summary>
/// Data from a background progress scan, ready to display.
/// </summary>
public class ProgressScanResult
{
	public List<CombinationResult> Combinations;
	public List<GearRecommendation> Recommendations;
	public Tier CurrentTier;
	public PlayerClass DetectedClass;
}
