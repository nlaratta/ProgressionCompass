using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.ID;
using ProgressionCompass.Common.Systems;
using ProgressionCompass.Content.Graph;
using ProgressionCompass.Content.Progression;

namespace ProgressionCompass.Content.Tracking;

/// <summary>
/// Shared utility for mapping NPCs and item sources to biome/location strings.
/// Extracted from ActionClassifier and ItemSourceSystem so both the action plan
/// and the source priority advisor can reuse the same logic.
/// </summary>
public static class BiomeMapper
{
	// ── NPC Location (primary API) ──────────────────────────────

	/// <summary>
	/// Returns the primary biome/location string for an NPC.
	/// Tries bestiary first, falls back to keyword heuristic.
	/// </summary>
	public static string GetNPCLocation(int npcId)
	{
		string bestiary = GetNPCLocationFromBestiary(npcId);
		if (bestiary != null) return bestiary;
		return GetNPCLocationHeuristic(npcId);
	}

	/// <summary>
	/// Returns all biomes an NPC spawns in. Splits multi-biome bestiary
	/// results (e.g. "Forest / Underground") into separate entries.
	/// </summary>
	public static List<string> GetNPCLocations(int npcId)
	{
		string location = GetNPCLocation(npcId);
		if (string.IsNullOrEmpty(location))
			return ["Unknown Location"];

		if (location.Contains(" / "))
		{
			var parts = location.Split(" / ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
			return parts.Length > 0 ? [.. parts] : [location];
		}

		return [location];
	}

	// ── Source-to-Location Dispatch ─────────────────────────────

	/// <summary>
	/// Returns the location(s) for any ItemSource, dispatching by source type.
	/// Used by SourcePriorityAnalyzer to group sources into location buckets.
	/// </summary>
	public static List<string> GetSourceLocations(ItemSource source)
	{
		switch (source.Type)
		{
			case ItemSourceType.NPCDrop:
				return source.SourceEntityId > 0
					? GetNPCLocations(source.SourceEntityId)
					: ["Unknown Location"];

			case ItemSourceType.BossDrop:
			case ItemSourceType.BagDrop:
			{
				// Try BossDatabase for accurate location
				if (source.SourceEntityId > 0)
				{
					var boss = BossDatabase.GetByNpcType(source.SourceEntityId);
					if (boss != null && !string.IsNullOrEmpty(boss.Location))
						return [$"Boss: {boss.Name}"];
				}
				// Fallback for bag drops — use NPC location
				return source.SourceEntityId > 0
					? GetNPCLocations(source.SourceEntityId)
					: ["Unknown Location"];
			}

			case ItemSourceType.NPCShop:
			{
				// Group per vendor NPC for actionable "visit this NPC" cards
				if (source.SourceEntityId > 0)
				{
					try
					{
						var npc = new NPC();
						npc.SetDefaults(source.SourceEntityId);
						string npcName = npc.GivenOrTypeName ?? npc.FullName ?? $"NPC #{source.SourceEntityId}";
						return [$"Shop: {npcName}"];
					}
					catch { }
				}
				return ["NPC Shops"];
			}

			case ItemSourceType.Event:
			{
				string eventName = source.SourceEntityId > 0
					? ItemSourceSystem.GetNPCEventName(source.SourceEntityId)
					: null;
				return [eventName != null ? $"Event: {eventName}" : "Event"];
			}

			case ItemSourceType.Fishing:
			{
				string biome = InferLocationFromDescription(source.Description);
				return [biome != null ? $"Fishing: {biome}" : "Fishing"];
			}

			case ItemSourceType.Gathering:
			{
				string loc = InferLocationFromDescription(source.Description);
				return [loc ?? "World Gathering"];
			}

			case ItemSourceType.ChestLoot:
			{
				string loc = InferLocationFromDescription(source.Description);
				return [loc != null ? $"Exploration: {loc}" : "Chest Exploration"];
			}

			case ItemSourceType.Shimmer:
				return ["Shimmer Pool"];

			default:
				return ["Other"];
		}
	}

	/// <summary>
	/// Returns locations filtered by world context. Removes wrong-evil biome locations.
	/// </summary>
	public static List<string> GetSourceLocations(ItemSource source, WorldContext worldContext)
	{
		var locations = GetSourceLocations(source);
		if (worldContext.HasBothEvils)
			return locations;

		var filtered = new List<string>(locations.Count);
		foreach (var loc in locations)
		{
			string lower = loc.ToLowerInvariant();
			if (lower.Contains("corruption") && worldContext.IsCrimson) continue;
			if (lower.Contains("crimson") && !worldContext.IsCrimson) continue;
			filtered.Add(loc);
		}

		return filtered.Count > 0 ? filtered : locations;
	}

	// ── Description Parsing ─────────────────────────────────────

	/// <summary>
	/// Parses structured source descriptions to extract location keywords.
	/// Handles formats like "Mine: Underground", "Chop: Trees (Forest)",
	/// "Fish: Jungle Crate (Jungle)", "Open: Ocean Crate (Ocean)".
	/// </summary>
	public static string InferLocationFromDescription(string desc)
	{
		if (string.IsNullOrEmpty(desc)) return null;

		// Try parenthetical biome hint first: "Fish: Ocean Crate (Ocean)"
		int parenStart = desc.LastIndexOf('(');
		int parenEnd = desc.LastIndexOf(')');
		if (parenStart >= 0 && parenEnd > parenStart)
		{
			string hint = desc[(parenStart + 1)..parenEnd].Trim();
			if (!string.IsNullOrEmpty(hint) && !hint.StartsWith("1/") && hint.Length < 40)
				return hint;
		}

		// Strip known prefixes: "Mine: Underground" → "Underground"
		string[] prefixes = ["Mine: ", "Chop: ", "Dig: ", "Gather: ", "Fish: ", "Open: "];
		foreach (string prefix in prefixes)
		{
			if (desc.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				string remainder = desc[prefix.Length..].Trim();
				// Strip drop rate suffix if present
				int rateIdx = remainder.IndexOf(" (1/");
				if (rateIdx > 0)
					remainder = remainder[..rateIdx].Trim();
				if (!string.IsNullOrEmpty(remainder))
					return remainder;
			}
		}

		return null;
	}

	// ── Crate Biome Inference ───────────────────────────────────

	/// <summary>
	/// Returns the biome for a fishing crate. Uses structured ItemID lookup first,
	/// falls back to name-based inference for modded crates.
	/// </summary>
	public static string InferCrateBiome(string crateName) =>
		InferCrateBiomeFromName(crateName);

	/// <summary>
	/// Returns the biome for a crate by item ID. Covers all 26 vanilla crates.
	/// Returns null for non-crate items or generic crates (Wooden/Iron/Golden).
	/// </summary>
	public static string GetCrateBiome(int crateItemId) =>
		_crateBiomeMap.GetValueOrDefault(crateItemId);

	private static string InferCrateBiomeFromName(string crateName)
	{
		string lower = (crateName ?? "").ToLowerInvariant();
		if (lower.Contains("jungle") || lower.Contains("bramble")) return "Jungle";
		if (lower.Contains("frozen") || lower.Contains("boreal")) return "Snow biome";
		if (lower.Contains("ocean") || lower.Contains("seaside")) return "Ocean";
		if (lower.Contains("sky") || lower.Contains("azure")) return "Sky islands";
		if (lower.Contains("corrupt") || lower.Contains("defiled")) return "Corruption";
		if (lower.Contains("crimson") || lower.Contains("hematic")) return "Crimson";
		if (lower.Contains("hallow") || lower.Contains("divine")) return "Hallow";
		if (lower.Contains("oasis") || lower.Contains("desert") || lower.Contains("mirage")) return "Desert";
		if (lower.Contains("obsidian") || lower.Contains("hellstone")) return "Underworld";
		if (lower.Contains("dungeon") || lower.Contains("stockade")) return "Dungeon";
		return null;
	}

	// Structured crate ItemID → biome mapping (from decompiled FishingCheck_RollItemDrop)
	private static readonly Dictionary<int, string> _crateBiomeMap = new()
	{
		// Pre-Hardmode → Biome
		[3203] = "Corruption",   // Corrupt Crate
		[3204] = "Crimson",      // Crimson Crate
		[3205] = "Dungeon",      // Dungeon Crate
		[3206] = "Sky islands",  // Sky Crate
		[3207] = "Hallow",       // Hallowed Crate
		[3208] = "Jungle",       // Jungle Crate
		[4405] = "Snow biome",   // Frozen Crate
		[4407] = "Desert",       // Oasis Crate
		[5002] = "Ocean",        // Ocean Crate
		[4877] = "Underworld",   // Obsidian Crate

		// Hardmode → same biomes
		[3982] = "Corruption",   // Defiled Crate
		[3983] = "Crimson",      // Hematic Crate
		[3984] = "Dungeon",      // Stockade Crate
		[3985] = "Sky islands",  // Azure Crate
		[3986] = "Hallow",       // Divine Crate
		[3987] = "Jungle",       // Bramble Crate
		[4406] = "Snow biome",   // Boreal Crate
		[4408] = "Desert",       // Mirage Crate
		[5003] = "Ocean",        // Seaside Crate
		[4878] = "Underworld",   // Hellstone Crate
	};

	// ── Event Trigger Hints ─────────────────────────────────────

	/// <summary>
	/// Returns a human-readable hint about how to trigger an event.
	/// </summary>
	public static string GetEventTriggerHint(string eventDescription)
	{
		if (string.IsNullOrEmpty(eventDescription)) return null;
		string lower = eventDescription.ToLowerInvariant();

		if (lower.Contains("goblin")) return "Use Goblin Battle Standard, or random at 65% HP";
		if (lower.Contains("pirate")) return "Use Pirate Map, or random in Hardmode Ocean";
		if (lower.Contains("solar eclipse")) return "Use Solar Tablet, or random post-Mech Hardmode";
		if (lower.Contains("blood moon")) return "Random event at night (1/9 chance)";
		if (lower.Contains("frost moon")) return "Use Naughty Present at night";
		if (lower.Contains("pumpkin moon")) return "Use Pumpkin Moon Medallion at night";
		if (lower.Contains("martian")) return "Lure Martian Probe in Space layer";
		if (lower.Contains("frost legion")) return "Use Snow Globe";
		if (lower.Contains("old one")) return "Use Eternia Crystal at Eternia Crystal Stand";
		return null;
	}

	// ── Bestiary + Heuristic (internal) ─────────────────────────

	internal static string GetNPCLocationFromBestiary(int npcId)
	{
		if (npcId <= 0) return null;
		try
		{
			var entry = Main.BestiaryDB?.FindEntryByNPCID(npcId);
			if (entry?.Info == null) return null;

			var biomes = new List<string>();
			foreach (var element in entry.Info)
			{
				if (element is SpawnConditionBestiaryInfoElement spawnCond)
				{
					string name = null;
					try
					{
						string key = spawnCond.GetDisplayNameKey();
						if (!string.IsNullOrEmpty(key))
							name = Terraria.Localization.Language.GetTextValue(key);
					}
					catch { }

					if (!string.IsNullOrEmpty(name) && name != "Unknown")
					{
						biomes.Add(name);
						if (biomes.Count >= 2) break;
					}
				}
			}

			return biomes.Count > 0 ? string.Join(" / ", biomes) : null;
		}
		catch { return null; }
	}

	internal static string GetNPCLocationHeuristic(int npcId)
	{
		if (npcId <= 0)
			return "Surface";

		try
		{
			var npc = new NPC();
			npc.SetDefaults(npcId);

			if (npc.boss)
				return "Boss Fight";

			string name = npc.FullName ?? npc.GivenOrTypeName ?? "";

			if (ContainsAny(name, "Corrupt", "Eater", "Devourer", "Clinger"))
				return "Underground Corruption";
			if (ContainsAny(name, "Crimson", "Creeper", "Ichor", "Floaty"))
				return "Underground Crimson";
			if (ContainsAny(name, "Hallow", "Pixie", "Unicorn", "Gastropod", "Enchanted"))
				return "Underground Hallow";
			if (ContainsAny(name, "Jungle", "Hornet", "Moss", "Tortoise", "Arapaima"))
				return "Underground Jungle";
			if (ContainsAny(name, "Dungeon", "Skeleton", "Paladin", "Necromancer", "Ragged"))
				return "Dungeon";
			if (ContainsAny(name, "Ice", "Frost", "Viking", "Undead"))
				return "Ice Biome";
			if (ContainsAny(name, "Desert", "Mummy", "Lamia", "Sand"))
				return "Desert";
			if (ContainsAny(name, "Mushroom", "Fungi"))
				return "Mushroom Biome";
			if (ContainsAny(name, "Ocean", "Shark", "Crab"))
				return "Ocean";
			if (ContainsAny(name, "Pirate", "Goblin", "Martian", "Solar", "Nebula", "Vortex", "Stardust"))
				return "Event";
			if (ContainsAny(name, "Lava", "Hell", "Demon", "Bone Serpent", "Fire Imp"))
				return "Underworld";

			if (npc.defense > 30 || npc.lifeMax > 400)
				return "Underground / Cavern";

			return "Surface / Underground";
		}
		catch
		{
			return "Unknown Location";
		}
	}

	internal static bool ContainsAny(string text, params string[] keywords)
	{
		foreach (string kw in keywords)
			if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
				return true;
		return false;
	}
}
