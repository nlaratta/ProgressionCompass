using System;

namespace CraftPathOptimizer.Common;

/// <summary>
/// Shorthand for mod logging. Avoids repeating
/// global::CraftPathOptimizer.CraftPathOptimizer.Instance?.Logger?.Warn(...) everywhere.
/// </summary>
internal static class Log
{
	private static Terraria.ModLoader.Mod Mod =>
		global::CraftPathOptimizer.CraftPathOptimizer.Instance;

	public static void Info(string msg) => Mod?.Logger?.Info(msg);
	public static void Warn(string msg) => Mod?.Logger?.Warn(msg);
	public static void Error(string msg, Exception ex = null) =>
		Mod?.Logger?.Error(ex != null ? $"{msg}: {ex.Message}" : msg);
}
