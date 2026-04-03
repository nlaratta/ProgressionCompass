using System.Collections.Generic;

namespace ProgressionCompass.Content.Progression;

/// <summary>
/// Data from a background progress scan, ready to display.
/// </summary>
public class ProgressScanResult
{
	public List<CombinationResult> Combinations;
	public List<GearRecommendation> Recommendations;
	public Tier CurrentTier;
	public PlayerClass DetectedClass;
	public PlayerStats Stats;
	public List<BossInfo> NextBosses;
	public WorldContext WorldContext;

	/// <summary>Effort analyzer for scoring acquisition difficulty.</summary>
	public EffortAnalyzer Effort;

	/// <summary>Version at which this result was computed. Stale if < CacheVersion.Current.</summary>
	public long Version;
}
