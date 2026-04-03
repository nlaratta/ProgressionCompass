using System.Threading;

namespace ProgressionCompass.Content.Progression;

/// <summary>
/// Global version counter for cache coordination. All caches across the mod
/// check this version to determine if their data is stale. Bumped when:
/// - Player inventory changes
/// - Recipe preferences change
/// - World state changes (boss defeated, altars smashed)
/// - Chest toggle changes
///
/// Thread-safe: uses Interlocked for atomic increment.
/// </summary>
public static class CacheVersion
{
	private static long _version;

	/// <summary>Current version. Caches compare their stored version against this.</summary>
	public static long Current => Interlocked.Read(ref _version);

	/// <summary>Bumps the version, invalidating all caches. Returns the new version.</summary>
	public static long Bump() => Interlocked.Increment(ref _version);

	/// <summary>Resets to 0. Called during Unload.</summary>
	public static void Reset() => Interlocked.Exchange(ref _version, 0);
}
