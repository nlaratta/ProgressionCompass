using System;

namespace ProgressionCompass.Common;

/// <summary>
/// Shorthand for mod logging. Use Log.Warn("msg") instead of
/// ProgressionCompassMod.Instance?.Logger?.Warn("msg").
/// </summary>
internal static class Log
{
	private static Terraria.ModLoader.Mod Mod => ProgressionCompassMod.Instance;

	public static void Info(string msg) => Mod?.Logger?.Info(msg);
	public static void Warn(string msg) => Mod?.Logger?.Warn(msg);
	public static void Error(string msg, Exception ex = null) =>
		Mod?.Logger?.Error(ex != null ? $"{msg}: {ex.Message}" : msg);
}
