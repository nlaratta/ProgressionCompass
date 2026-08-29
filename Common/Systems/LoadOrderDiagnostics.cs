using System;
using System.Text;
using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.ID;
using Terraria.ModLoader;

namespace ProgressionCompass.Common.Systems;

/// <summary>
/// Load-order instrumentation for H1 in CODE_REVIEW_2026-08.md.
///
/// The hypothesis: <see cref="ItemSourceSystem"/> builds its drop, bestiary and
/// shimmer-decraft indexes in <c>PostSetupContent</c>, which may run before
/// tModLoader has finalized NPC/item drop tables, bestiary entries, and recipe
/// registration. If so, modded <c>ModifyNPCLoot</c> / <c>ModifyItemLoot</c>
/// contributions are silently missing from every "where do I get this" answer,
/// and the automatic modded-critter coverage added in f1249ac never worked.
///
/// This samples the same measurements at four points in the load sequence and
/// logs one line per stage. If the counts climb after PostSetupContent, the
/// hypothesis is confirmed and the indexing needs to move later.
///
/// The vanilla/modded split is the discriminator: vanilla data may well be
/// ready early while modded content is not, and it is the modded numbers the
/// hypothesis is actually about. Run once with only this mod, then again with
/// Calamity or Thorium enabled, and compare.
///
/// Diagnostic only — reads game state, changes nothing. Delete this file (and
/// the "loadorder" case in CraftPathTestCommand) once H1 is settled.
/// </summary>
public class LoadOrderDiagnostics : ModSystem
{
	public override void PostSetupContent() => LogSample("PostSetupContent");
	public override void PostAddRecipes() => LogSample("PostAddRecipes");
	public override void PostSetupRecipes() => LogSample("PostSetupRecipes");
	public override void OnWorldLoad() => LogSample("OnWorldLoad");

	private static void LogSample(string stage)
	{
		if (Main.dedServ)
			return;

		var s = Capture(stage);
		Log.Info(s.ToString());
	}

	/// <summary>
	/// Takes one reading of everything ItemSourceSystem depends on.
	/// Each measurement is isolated so a failure in one still reports the rest —
	/// and unlike the rest of the codebase, the catches here actually log.
	/// </summary>
	public static Sample Capture(string stage)
	{
		var s = new Sample { Stage = stage };

		try { s.Recipes = Recipe.numRecipes; }
		catch (Exception ex) { Log.Error($"[loadorder/{stage}] recipe count failed", ex); }

		// NPC drop tables — the primary source of "Drop: X" answers.
		try
		{
			int vanillaMax = Math.Min(NPCID.Count, NPCLoader.NPCCount);
			for (int id = 1; id < NPCLoader.NPCCount; id++)
			{
				var rules = Main.ItemDropsDB.GetRulesForNPCID(id, includeGlobalDrops: false);
				if (rules == null || rules.Count == 0)
					continue;
				if (id < vanillaMax) s.NpcsWithDropsVanilla++;
				else s.NpcsWithDropsModded++;
			}
		}
		catch (Exception ex) { Log.Error($"[loadorder/{stage}] NPC drop scan failed", ex); }

		// Item drop tables — boss bags and fishing crates.
		try
		{
			int vanillaMax = Math.Min(ItemID.Count, ItemLoader.ItemCount);
			for (int id = 1; id < ItemLoader.ItemCount; id++)
			{
				var rules = Main.ItemDropsDB.GetRulesForItemID(id);
				if (rules == null || rules.Count == 0)
					continue;
				if (id < vanillaMax) s.ItemsWithDropsVanilla++;
				else s.ItemsWithDropsModded++;
			}
		}
		catch (Exception ex) { Log.Error($"[loadorder/{stage}] item drop scan failed", ex); }

		// Bestiary spawn conditions — what IndexCritterSources derives biomes from.
		try
		{
			int vanillaMax = Math.Min(NPCID.Count, NPCLoader.NPCCount);
			for (int id = 1; id < NPCLoader.NPCCount; id++)
			{
				var entry = Main.BestiaryDB?.FindEntryByNPCID(id);
				if (entry?.Info == null)
					continue;

				bool hasSpawnCondition = false;
				foreach (var element in entry.Info)
				{
					if (element is SpawnConditionBestiaryInfoElement)
					{
						hasSpawnCondition = true;
						break;
					}
				}
				if (!hasSpawnCondition)
					continue;

				if (id < vanillaMax) s.BestiarySpawnVanilla++;
				else s.BestiarySpawnModded++;
			}
		}
		catch (Exception ex) { Log.Error($"[loadorder/{stage}] bestiary scan failed", ex); }

		// Catchable critters — the f1249ac feature. Uses ContentSamples rather
		// than constructing NPCs; if ContentSamples itself is not yet populated
		// that is equally diagnostic, so a zero here is a real signal.
		try
		{
			int vanillaMax = Math.Min(NPCID.Count, NPCLoader.NPCCount);
			int maxId = Math.Min(NPCID.Sets.CountsAsCritter.Length, NPCLoader.NPCCount);
			for (int id = 1; id < maxId; id++)
			{
				if (!NPCID.Sets.CountsAsCritter[id])
					continue;
				s.Critters++;
				if (!ContentSamples.NpcsByNetId.TryGetValue(id, out var npc) || npc == null)
					continue;
				if (npc.catchItem <= ItemID.None)
					continue;
				if (id < vanillaMax) s.CatchableVanilla++;
				else s.CatchableModded++;
			}
		}
		catch (Exception ex) { Log.Error($"[loadorder/{stage}] critter scan failed", ex); }

		return s;
	}

	/// <summary>One reading of the game's content state at a point in the load sequence.</summary>
	public class Sample
	{
		public string Stage;
		public int Recipes;
		public int NpcsWithDropsVanilla, NpcsWithDropsModded;
		public int ItemsWithDropsVanilla, ItemsWithDropsModded;
		public int BestiarySpawnVanilla, BestiarySpawnModded;
		public int Critters, CatchableVanilla, CatchableModded;

		public override string ToString()
		{
			var sb = new StringBuilder();
			sb.Append("[loadorder] ").Append(Stage.PadRight(16));
			sb.Append(" recipes=").Append(Recipes);
			sb.Append(" npcDrops=").Append(NpcsWithDropsVanilla).Append('v').Append('/').Append(NpcsWithDropsModded).Append('m');
			sb.Append(" itemDrops=").Append(ItemsWithDropsVanilla).Append('v').Append('/').Append(ItemsWithDropsModded).Append('m');
			sb.Append(" bestiarySpawn=").Append(BestiarySpawnVanilla).Append('v').Append('/').Append(BestiarySpawnModded).Append('m');
			sb.Append(" critters=").Append(Critters);
			sb.Append(" catchable=").Append(CatchableVanilla).Append('v').Append('/').Append(CatchableModded).Append('m');
			return sb.ToString();
		}
	}
}
