using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using ProgressionCompass.Content.Graph;
using ProgressionCompass.Content.Progression;

namespace ProgressionCompass.Common.Systems;

/// <summary>
/// Indexes all non-crafting item sources (NPC drops, shops) so the UI can show
/// "Drop: Zombie (1/50)" or "Buy: Merchant" for raw materials.
/// </summary>
public class ItemSourceSystem : ModSystem
{
	private Dictionary<int, List<ItemSource>> _sourcesByItem;

	public List<ItemSource> GetSources(int itemType)
	{
		if (_sourcesByItem != null && _sourcesByItem.TryGetValue(itemType, out var sources))
			return FilterDecraftWhenAlternativesExist(sources);

		// Fallback: try to infer source from item properties
		return InferSourceFromItem(itemType);
	}

	/// <summary>
	/// Shimmer decrafting ("Decraft: Frozen Chest" → Ice Block) is indexed as a source
	/// for every recipe-ingredient, but it's rarely the answer the player wants. When
	/// a "real" source (drop, shop, gathering, catching, fishing, chest, direct shimmer
	/// transform, event) exists, hide the decraft entries so the UI isn't cluttered
	/// with "decraft this furniture" suggestions. Returns the original list when only
	/// decrafts exist — that's the legitimate fallback case.
	/// </summary>
	private static List<ItemSource> FilterDecraftWhenAlternativesExist(List<ItemSource> sources)
	{
		if (sources == null || sources.Count == 0) return sources;

		bool hasNonDecraft = false;
		bool hasDecraft = false;
		foreach (var s in sources)
		{
			bool isDecraft = s.Type == ItemSourceType.Shimmer
				&& s.Description != null && s.Description.StartsWith("Decraft:");
			if (isDecraft) hasDecraft = true;
			else hasNonDecraft = true;
			if (hasDecraft && hasNonDecraft) break;
		}

		if (!hasDecraft || !hasNonDecraft)
			return sources; // Nothing to filter, or only decrafts — keep as-is

		var filtered = new List<ItemSource>(sources.Count);
		foreach (var s in sources)
		{
			if (s.Type == ItemSourceType.Shimmer
				&& s.Description != null && s.Description.StartsWith("Decraft:"))
				continue;
			filtered.Add(s);
		}
		return filtered;
	}

	/// <summary>
	/// Returns sources filtered for the current world context.
	/// Removes sources from wrong-evil biomes/NPCs. Falls back to unfiltered
	/// if all sources would be removed (prevents info loss).
	/// </summary>
	public List<ItemSource> GetSources(int itemType, WorldContext worldContext)
	{
		var all = GetSources(itemType);
		if (all.Count == 0 || worldContext.HasBothEvils || !worldContext.IsValid)
			return all;

		var filtered = new List<ItemSource>(all.Count);
		foreach (var source in all)
		{
			if (IsSourceAvailableInWorld(source, worldContext))
				filtered.Add(source);
		}

		return filtered.Count > 0 ? filtered : all;
	}

	private static bool IsSourceAvailableInWorld(ItemSource source, WorldContext worldContext)
	{
		// Difficulty filter: skip Expert-only drops in Normal, Master-only in non-Master
		if (source.MinDifficulty == GameDifficulty.Expert && !worldContext.IsExpert)
			return false;
		if (source.MinDifficulty == GameDifficulty.Master && !worldContext.IsMaster)
			return false;

		string desc = source.Description;
		if (string.IsNullOrEmpty(desc))
			return true;

		string lower = desc.ToLowerInvariant();

		// Filter gathering sources for wrong evil
		if (lower.Contains("corruption") && worldContext.IsCrimson)
			return false;
		if (lower.Contains("crimson") && !worldContext.IsCrimson)
			return false;

		// For NPC drops, check if the NPC is evil-affiliated by name
		if (source.SourceEntityId > 0 &&
			source.Type is ItemSourceType.NPCDrop or ItemSourceType.BossDrop or ItemSourceType.Event)
		{
			// Check NPC name for evil keywords
			if (lower.Contains("eater") && worldContext.IsCrimson) return false;
			if (lower.Contains("corruptor") && worldContext.IsCrimson) return false;
			if (lower.Contains("brain of cthulhu") && !worldContext.IsCrimson) return false;
			if (lower.Contains("creeper") && !worldContext.IsCrimson) return false;
			if (lower.Contains("ichor") && !worldContext.IsCrimson) return false;
			if (lower.Contains("cursed") && worldContext.IsCrimson) return false;
		}

		return true;
	}

	/// <summary>
	/// For items with no indexed sources, tries to infer how to obtain them
	/// from item name patterns and tooltip text.
	/// </summary>
	private static List<ItemSource> InferSourceFromItem(int itemType)
	{
		if (itemType <= ItemID.None)
			return [];

		try
		{
			var item = ContentSamples.ItemsByType.GetValueOrDefault(itemType);
			if (item == null)
				return [];

			string name = (item.Name ?? "").ToLowerInvariant();

			// Ore items — likely mined
			if (name.Contains("ore"))
				return [new ItemSource(ItemSourceType.Gathering, $"Mine: {item.Name}")];

			// Bar items — likely smelted from ore
			if (name.Contains(" bar"))
				return [new ItemSource(ItemSourceType.Gathering, $"Smelt: {item.Name} (check recipe)")];

			// Check tooltip for hints
			if (item.ToolTip != null && item.ToolTip != Terraria.UI.ItemTooltip.None)
			{
				for (int i = 0; i < item.ToolTip.Lines; i++)
				{
					string line = item.ToolTip.GetLine(i);
					if (string.IsNullOrEmpty(line)) continue;
					string lower = line.ToLowerInvariant();

					// Common tooltip patterns from mods
					if (lower.Contains("drop") || lower.Contains("defeat") || lower.Contains("boss"))
						return [new ItemSource(ItemSourceType.BossDrop, line)];
					if (lower.Contains("mine") || lower.Contains("found in") || lower.Contains("underground"))
						return [new ItemSource(ItemSourceType.Gathering, line)];
					if (lower.Contains("fish") || lower.Contains("caught"))
						return [new ItemSource(ItemSourceType.Fishing, line)];
				}
			}

			// Material items — generic hint
			if (item.material)
				return [new ItemSource(ItemSourceType.Other, $"Material: {item.Name} (check wiki)")];
		}
		catch { }

		return [];
	}

	public override void PostSetupContent()
	{
		if (Main.dedServ)
			return;

		// Index drops and gathering sources — these are available now.
		// Shop indexing is deferred to PostAddRecipes because NPCShopDatabase
		// isn't populated until after AddShops() runs (which is after PostSetupContent).
		_sourcesByItem = [];
		IndexNPCDrops();
		IndexItemDrops();
		IndexGatheringSources();
		IndexShimmerTransforms();
		IndexShimmerDecrafting();

		Mod.Logger.Info($"Indexed item sources (drops/gathering/shimmer) for {_sourcesByItem.Count} items");

		try
		{
			AccessoryEffectAnalyzer.Initialize();
			Mod.Logger.Info($"Analyzed {AccessoryEffectAnalyzer.Count} accessory effects via differential approach");
		}
		catch (Exception ex)
		{
			Mod.Logger.Warn($"Accessory effect analysis failed (non-fatal): {ex.Message}");
		}
	}

	public override void PostAddRecipes()
	{
		if (Main.dedServ || _sourcesByItem == null)
			return;

		// Precondition: source index must exist from PostSetupContent
		System.Diagnostics.Debug.Assert(_sourcesByItem != null,
			"ItemSourceSystem.PostAddRecipes called before PostSetupContent");

		int before = _sourcesByItem.Count;
		IndexNPCShops();
		Mod.Logger.Info($"Indexed NPC shop sources: {_sourcesByItem.Count - before} new items (total: {_sourcesByItem.Count})");
	}

	public override void Unload()
	{
		_sourcesByItem = null;
		AccessoryEffectAnalyzer.Unload();
	}

	private void IndexNPCDrops()
	{
		for (int npcId = NPCID.NegativeIDCount; npcId < NPCLoader.NPCCount; npcId++)
		{
			// Skip invalid IDs
			if (npcId == 0)
				continue;

			List<IItemDropRule> rules;
			try
			{
				rules = Main.ItemDropsDB.GetRulesForNPCID(npcId, includeGlobalDrops: true);
			}
			catch
			{
				continue;
			}

			if (rules == null)
				continue;

			bool isBoss = IsNPCBoss(npcId);
			string npcName;
			try { npcName = Lang.GetNPCNameValue(npcId); }
			catch { npcName = $"NPC#{npcId}"; }

			string eventName = GetNPCEventName(npcId);

			foreach (var rule in rules)
				ExtractDropsFromRule(rule, npcId, npcName, isBoss, eventName);
		}
	}

	private void ExtractDropsFromRule(IItemDropRule rule, int npcId, string npcName,
		bool isBoss, string eventName = null, string difficultyTag = null)
	{
		// Determine source type: event > boss > regular NPC
		ItemSourceType GetSourceType() =>
			eventName != null ? ItemSourceType.Event
			: isBoss ? ItemSourceType.BossDrop
			: ItemSourceType.NPCDrop;

		string eventTag = eventName != null ? $" ({eventName})" : "";
		string diffTag = difficultyTag ?? "";

		// Map difficulty tag to enum
		GameDifficulty minDiff = difficultyTag switch
		{
			" [Expert+]" => GameDifficulty.Expert,
			" [Master]" => GameDifficulty.Master,
			_ => GameDifficulty.Normal
		};

		switch (rule)
		{
			// ── Leaf rules (contain item IDs directly) ──────────────
			// Note: ItemDropWithConditionRule extends CommonDrop, so this catches both
			case CommonDrop common:
				AddSource(common.itemId, new ItemSource(
					GetSourceType(),
					$"Drop: {npcName}{eventTag}{diffTag}" + (common.chanceDenominator > 1 ? $" (1/{common.chanceDenominator})" : ""),
					npcId,
					1f / common.chanceDenominator,
					minDiff));
				break;

			case DropOneByOne dropOne:
				AddSource(dropOne.itemId, new ItemSource(
					GetSourceType(),
					$"Drop: {npcName}{eventTag}{diffTag}",
					npcId,
					MinDifficulty: minDiff));
				break;

			// ── Option rules (pick from item ID arrays) ─────────────
			case OneFromOptionsNotScaledWithLuckDropRule oneFromOptions:
				foreach (int itemId in oneFromOptions.dropIds)
					AddSource(itemId, new ItemSource(
						GetSourceType(),
						$"Drop: {npcName}{eventTag}{diffTag}",
						npcId));
				break;

			case OneFromOptionsDropRule oneFromOptions:
				foreach (int itemId in oneFromOptions.dropIds)
					AddSource(itemId, new ItemSource(
						GetSourceType(),
						$"Drop: {npcName}{eventTag}{diffTag}",
						npcId));
				break;

			case FewFromOptionsDropRule fewFromOptions:
				foreach (int itemId in fewFromOptions.dropIds)
					AddSource(itemId, new ItemSource(
						GetSourceType(),
						$"Drop: {npcName}{eventTag}{diffTag}",
						npcId));
				break;

			case FewFromOptionsNotScaledWithLuckDropRule fewFromOptionsNoLuck:
				foreach (int itemId in fewFromOptionsNoLuck.dropIds)
					AddSource(itemId, new ItemSource(
						GetSourceType(),
						$"Drop: {npcName}{eventTag}{diffTag}",
						npcId));
				break;

			case FromOptionsWithoutRepeatsDropRule fromOptionsNoRepeats:
				foreach (int itemId in fromOptionsNoRepeats.dropIds)
					AddSource(itemId, new ItemSource(
						GetSourceType(),
						$"Drop: {npcName}{eventTag}{diffTag}",
						npcId));
				break;

			// ── Difficulty branching ─────────────────────────────────
			case DropBasedOnExpertMode expertDrop:
				ExtractDropsFromRule(expertDrop.ruleForNormalMode, npcId, npcName, isBoss, eventName, difficultyTag);
				ExtractDropsFromRule(expertDrop.ruleForExpertMode, npcId, npcName, isBoss, eventName, " [Expert+]");
				break;

			case DropBasedOnMasterMode masterDrop:
				ExtractDropsFromRule(masterDrop.ruleForDefault, npcId, npcName, isBoss, eventName, difficultyTag);
				ExtractDropsFromRule(masterDrop.ruleForMasterMode, npcId, npcName, isBoss, eventName, " [Master]");
				break;

			case DropBasedOnMasterAndExpertMode masterExpert:
				ExtractDropsFromRule(masterExpert.ruleForDefault, npcId, npcName, isBoss, eventName, difficultyTag);
				ExtractDropsFromRule(masterExpert.ruleForExpertmode, npcId, npcName, isBoss, eventName, " [Expert+]");
				ExtractDropsFromRule(masterExpert.ruleForMasterMode, npcId, npcName, isBoss, eventName, " [Master]");
				break;

			// ── Container rules (contain nested IItemDropRule[]) ─────
			case OneFromRulesRule oneFromRules:
				foreach (var subRule in oneFromRules.options)
					ExtractDropsFromRule(subRule, npcId, npcName, isBoss, eventName, difficultyTag);
				break;

			case FewFromRulesRule fewFromRules:
				foreach (var subRule in fewFromRules.options)
					ExtractDropsFromRule(subRule, npcId, npcName, isBoss, eventName, difficultyTag);
				break;

			case SequentialRulesRule sequential:
				foreach (var subRule in sequential.rules)
					ExtractDropsFromRule(subRule, npcId, npcName, isBoss, eventName, difficultyTag);
				break;

			case SequentialRulesNotScalingWithLuckRule sequentialNoLuck:
				foreach (var subRule in sequentialNoLuck.rules)
					ExtractDropsFromRule(subRule, npcId, npcName, isBoss, eventName, difficultyTag);
				break;

			// ── Condition wrapper ────────────────────────────────────
			case LeadingConditionRule condRule:
				foreach (var chained in condRule.ChainedRules)
					ExtractDropsFromRule(chained.RuleToChain, npcId, npcName, isBoss, eventName, difficultyTag);
				break;

			// ── Special rules ────────────────────────────────────────
			case AlwaysAtleastOneSuccessDropRule atLeastOne:
				foreach (var subRule in atLeastOne.rules)
					ExtractDropsFromRule(subRule, npcId, npcName, isBoss, eventName, difficultyTag);
				break;

			// ── Skip rules (no item data) ────────────────────────────
			case DropNothing:
			case CoinsRule:
				break;

			// ── Fallback: reflection for modded custom rules ─────────
			default:
				TryExtractViaReflection(rule, npcId, npcName, isBoss, eventName, difficultyTag);
				break;
		}

		if (rule.ChainedRules != null)
		{
			foreach (var chained in rule.ChainedRules)
				ExtractDropsFromRule(chained.RuleToChain, npcId, npcName, isBoss, eventName, difficultyTag);
		}
	}

	public static string GetNPCEventName(int npcId)
	{
		if (npcId < 0) return null;
		try
		{
			if (npcId < NPCID.Sets.BelongsToInvasionGoblinArmy.Length && NPCID.Sets.BelongsToInvasionGoblinArmy[npcId])
				return "Goblin Army";
			if (npcId < NPCID.Sets.BelongsToInvasionFrostLegion.Length && NPCID.Sets.BelongsToInvasionFrostLegion[npcId])
				return "Frost Legion";
			if (npcId < NPCID.Sets.BelongsToInvasionPirate.Length && NPCID.Sets.BelongsToInvasionPirate[npcId])
				return "Pirate Invasion";
			if (npcId < NPCID.Sets.BelongsToInvasionMartianMadness.Length && NPCID.Sets.BelongsToInvasionMartianMadness[npcId])
				return "Martian Madness";
			if (npcId < NPCID.Sets.BelongsToInvasionOldOnesArmy.Length && NPCID.Sets.BelongsToInvasionOldOnesArmy[npcId])
				return "Old One's Army";
		}
		catch { }
		return null;
	}

	private void IndexNPCShops()
	{
		foreach (var shop in NPCShopDatabase.AllShops)
		{
			// Handle non-standard shops (Traveling Merchant, modded special shops)
			if (shop is not NPCShop npcShop)
			{
				IndexSpecialShop(shop);
				continue;
			}

			string shopOwner = Lang.GetNPCNameValue(npcShop.NpcType);

			foreach (var entry in npcShop.Entries)
			{
				if (entry.Item.type <= ItemID.None)
					continue;

				// Extract price
				string priceText = "";
				int rawPrice = -1;
				try
				{
					int price = (entry.Item.shopCustomPrice.HasValue && entry.Item.shopCustomPrice.Value > 0)
						? entry.Item.shopCustomPrice.Value
						: entry.Item.value;
					if (entry.Item.shopSpecialCurrency >= 0)
						priceText = ""; // Special currency — skip coin display
					else if (price > 0)
					{
						rawPrice = price;
						priceText = $" ~{FormatCoins(price)}";
					}
				}
				catch { }

				// Extract conditions (night only, post-boss, etc.)
				string condText = "";
				List<string> shopConds = null;
				try
				{
					var condTexts = new List<string>();
					foreach (var cond in entry.Conditions)
					{
						string desc = cond?.Description?.Value;
						if (!string.IsNullOrEmpty(desc))
							condTexts.Add(desc);
					}
					if (condTexts.Count > 0)
					{
						shopConds = condTexts;
						condText = $" ({string.Join(", ", condTexts)})";
					}
					if (condText.Length > 50)
						condText = condText[..49] + "..)";
				}
				catch { }

				AddSource(entry.Item.type, new ItemSource(
					ItemSourceType.NPCShop,
					$"Buy: {shopOwner}{priceText}{condText}",
					npcShop.NpcType,
					ShopConditions: shopConds,
					ShopPrice: rawPrice));
			}
		}
	}

	/// <summary>
	/// Indexes items from non-standard shops (TravellingMerchantShop, modded special shops)
	/// via AbstractNPCShop.ActiveEntries.
	/// </summary>
	private void IndexSpecialShop(AbstractNPCShop shop)
	{
		try
		{
			string shopOwner = Lang.GetNPCNameValue(shop.NpcType);
			string shopNote = shop is TravellingMerchantShop ? " (Traveling Merchant, random stock)" : "";

			foreach (var entry in shop.ActiveEntries)
			{
				if (entry.Item.type <= ItemID.None)
					continue;

				string priceText = "";
				try
				{
					int price = (entry.Item.shopCustomPrice.HasValue && entry.Item.shopCustomPrice.Value > 0)
						? entry.Item.shopCustomPrice.Value
						: entry.Item.value / 5;
					if (entry.Item.shopSpecialCurrency < 0 && price > 0)
						priceText = $" ~{FormatCoins(price)}";
				}
				catch { }

				AddSource(entry.Item.type, new ItemSource(
					ItemSourceType.NPCShop,
					$"Buy: {shopOwner}{priceText}{shopNote}",
					shop.NpcType));
			}
		}
		catch (Exception ex)
		{
			Common.Log.Warn($"Failed to index special shop for NPC {shop.NpcType}: {ex.Message}");
		}
	}

	private static string FormatCoins(int copper) =>
		global::ProgressionCompass.UI.UIHelper.FormatCoins(copper);


	private static string InferCrateBiome(string crateName) => global::ProgressionCompass.Content.Tracking.BiomeMapper.InferCrateBiome(crateName);

	/// <summary>
	/// Indexes drops from treasure bags, fishing crates, and other grab-bag items
	/// via Main.ItemDropsDB.GetRulesForItemID().
	/// </summary>
	private void IndexItemDrops()
	{
		foreach (var (itemType, item) in ContentSamples.ItemsByType)
		{
			if (itemType <= ItemID.None)
				continue;

			// Check boss bags and grab-bag items
			bool isBossBag = itemType < ItemID.Sets.BossBag.Length && ItemID.Sets.BossBag[itemType];
			bool isCrate = itemType < ItemID.Sets.IsFishingCrate.Length && ItemID.Sets.IsFishingCrate[itemType];
			bool isGrabBag = isBossBag || isCrate;

			if (!isGrabBag)
				continue;

			List<IItemDropRule> rules;
			try
			{
				rules = Main.ItemDropsDB.GetRulesForItemID(itemType);
			}
			catch { continue; }

			if (rules == null || rules.Count == 0)
				continue;

			string sourceName = item.Name ?? $"Item#{itemType}";
			string sourceType = isBossBag ? "Open" : isCrate ? "Fish" : "Open";
			var srcType = isBossBag ? ItemSourceType.BagDrop : isCrate ? ItemSourceType.Fishing : ItemSourceType.Other;

			// Add biome hint for fishing crates (structured lookup first, name fallback)
			string biomeHint = isCrate
				? (global::ProgressionCompass.Content.Tracking.BiomeMapper.GetCrateBiome(itemType) ?? InferCrateBiome(sourceName))
				: null;
			string desc = biomeHint != null
				? $"{sourceType}: {sourceName} ({biomeHint})"
				: $"{sourceType}: {sourceName}";

			foreach (var rule in rules)
				ExtractDropsFromItemRule(rule, itemType, desc, srcType);
		}
	}

	private void ExtractDropsFromItemRule(IItemDropRule rule, int sourceItemType, string description, ItemSourceType srcType)
	{
		switch (rule)
		{
			case CommonDrop common:
				string rateText = common.chanceDenominator > 1 ? $" (1/{common.chanceDenominator})" : "";
				AddSource(common.itemId, new ItemSource(srcType, $"{description}{rateText}", sourceItemType,
					1f / common.chanceDenominator));
				break;
			case OneFromOptionsDropRule oneFrom:
				foreach (int id in oneFrom.dropIds)
					AddSource(id, new ItemSource(srcType, description, sourceItemType));
				break;
			case OneFromOptionsNotScaledWithLuckDropRule oneFrom:
				foreach (int id in oneFrom.dropIds)
					AddSource(id, new ItemSource(srcType, description, sourceItemType));
				break;
		}

		if (rule.ChainedRules != null)
			foreach (var chained in rule.ChainedRules)
				ExtractDropsFromItemRule(chained.RuleToChain, sourceItemType, description, srcType);
	}

	private void AddSource(int itemType, ItemSource source)
	{
		if (itemType <= ItemID.None)
			return;

		if (!_sourcesByItem.TryGetValue(itemType, out var list))
		{
			list = [];
			_sourcesByItem[itemType] = list;
		}

		// Avoid duplicate sources — check entity ID and also NPC name
		// (catches NPC variants like Hornet/Big Hornet/Little Hornet)
		foreach (var existing in list)
		{
			if (existing.Type != source.Type)
				continue;

			if (existing.SourceEntityId == source.SourceEntityId)
				return;

			// Dedup by NPC display name (strip "Drop: " prefix and rate suffix)
			if (existing.Type == ItemSourceType.NPCDrop || existing.Type == ItemSourceType.BossDrop)
			{
				string existName = ExtractNPCName(existing.Description);
				string newName = ExtractNPCName(source.Description);
				if (!string.IsNullOrEmpty(existName) && existName == newName)
				{
					// Keep the one with the better (higher) drop rate
					if (source.DropRate > existing.DropRate)
					{
						list.Remove(existing);
						break; // Will add the new one below
					}
					return; // Keep existing
				}
			}
		}

		list.Add(source);
	}

	/// <summary>
	/// Adds static sources for common gathering/mining materials that aren't
	/// in drop tables or shops. These are world-gen items the player mines or finds.
	/// </summary>
	/// <summary>
	/// Only items that are purely mined/gathered from the world and have NO presence
	/// in NPC drop tables or shops. Anything that drops from mobs is already covered
	/// by IndexNPCDrops. Anything sold by NPCs is covered by IndexNPCShops.
	/// </summary>
	private void IndexGatheringSources()
	{
		// Ores — mined from the world, never in drop tables
		AddGathering(ItemID.CopperOre, "Mine: Underground");
		AddGathering(ItemID.TinOre, "Mine: Underground");
		AddGathering(ItemID.IronOre, "Mine: Underground");
		AddGathering(ItemID.LeadOre, "Mine: Underground");
		AddGathering(ItemID.SilverOre, "Mine: Underground");
		AddGathering(ItemID.TungstenOre, "Mine: Underground");
		AddGathering(ItemID.GoldOre, "Mine: Underground / Cavern");
		AddGathering(ItemID.PlatinumOre, "Mine: Underground / Cavern");
		AddGathering(ItemID.Meteorite, "Mine: Meteorite crash site");
		AddGathering(ItemID.Hellstone, "Mine: Underworld");
		AddGathering(ItemID.Obsidian, "Mine: Where water meets lava");
		AddGathering(ItemID.CobaltOre, "Mine: Underground (Hardmode)");
		AddGathering(ItemID.PalladiumOre, "Mine: Underground (Hardmode)");
		AddGathering(ItemID.MythrilOre, "Mine: Underground (Hardmode)");
		AddGathering(ItemID.OrichalcumOre, "Mine: Underground (Hardmode)");
		AddGathering(ItemID.AdamantiteOre, "Mine: Underground (Hardmode)");
		AddGathering(ItemID.TitaniumOre, "Mine: Underground (Hardmode)");
		AddGathering(ItemID.ChlorophyteOre, "Mine: Underground Jungle (Hardmode)");
		AddGathering(ItemID.LunarOre, "Mine: After Moon Lord defeat");

		// Gems — mined, not dropped
		AddGathering(ItemID.Amethyst, "Mine: Underground");
		AddGathering(ItemID.Topaz, "Mine: Underground");
		AddGathering(ItemID.Sapphire, "Mine: Underground");
		AddGathering(ItemID.Emerald, "Mine: Underground");
		AddGathering(ItemID.Ruby, "Mine: Underground");
		AddGathering(ItemID.Diamond, "Mine: Underground");
		AddGathering(ItemID.Amber, "Mine: Underground Desert");

		// Wood — chopped, not dropped
		AddGathering(ItemID.Wood, "Chop: Trees (Forest)");
		AddGathering(ItemID.BorealWood, "Chop: Trees (Snow)");
		AddGathering(ItemID.PalmWood, "Chop: Trees (Ocean/Desert)");
		AddGathering(ItemID.RichMahogany, "Chop: Trees (Jungle)");
		AddGathering(ItemID.Ebonwood, "Chop: Trees (Corruption)");
		AddGathering(ItemID.Shadewood, "Chop: Trees (Crimson)");
		AddGathering(ItemID.Pearlwood, "Chop: Trees (Hallow)");

		// Block/terrain materials — dug, not dropped
		AddGathering(ItemID.SandBlock, "Dig: Desert / Ocean");
		AddGathering(ItemID.StoneBlock, "Dig: Everywhere underground");
		AddGathering(ItemID.DirtBlock, "Dig: Everywhere");
		AddGathering(ItemID.ClayBlock, "Dig: Surface / Underground");
		AddGathering(ItemID.SiltBlock, "Dig: Underground");
		AddGathering(ItemID.IceBlock, "Mine: Ice biome");
		AddGathering(ItemID.SnowBlock, "Dig: Snow biome surface");
		AddGathering(ItemID.SlushBlock, "Dig: Ice biome (underground)");
		AddGathering(ItemID.AshBlock, "Dig: Underworld floor");
		AddGathering(ItemID.MudBlock, "Dig: Jungle / Underground Jungle");
		AddGathering(ItemID.Hive, "Mine: Jungle hive (underground)");
		AddGathering(ItemID.EbonstoneBlock, "Mine: Corruption chasms (needs Nightmare Pickaxe+)");
		AddGathering(ItemID.CrimstoneBlock, "Mine: Crimson (needs Deathbringer Pickaxe+)");
		AddGathering(ItemID.PearlstoneBlock, "Mine: Hallow (Hardmode)");
		AddGathering(ItemID.EbonsandBlock, "Dig: Corrupt desert");
		AddGathering(ItemID.CrimsandBlock, "Dig: Crimson desert");
		AddGathering(ItemID.PearlsandBlock, "Dig: Hallowed desert (Hardmode)");
		AddGathering(ItemID.Marble, "Mine: Marble biome (underground)");
		AddGathering(ItemID.Granite, "Mine: Granite biome (underground)");
		AddGathering(ItemID.Cloud, "Gather: Sky islands / floating islands");
		AddGathering(ItemID.RainCloud, "Gather: Sky during rain");
		AddGathering(ItemID.LifeCrystal, "Mine: Underground (sparkling crystal clusters)");
		AddGathering(ItemID.ManaCrystal, "Craft or gather: 3 Fallen Stars");
		AddGathering(ItemID.LifeFruit, "Chop: Jungle plants (Hardmode, post-Plantera alt source)");
		AddGathering(ItemID.Pumpkin, "Chop: Pumpkin plants (surface, Halloween / grown from seeds)");
		AddGathering(ItemID.HoneyBlock, "Gather: Jungle hive (break solidified honey)");

		// Gathered from world objects — not in drop tables
		AddGathering(ItemID.Cobweb, "Gather: Spider caves / Underground");
		AddGathering(ItemID.JungleSpores, "Gather: Underground Jungle (glowing plants)");
		AddGathering(ItemID.Coral, "Gather: Ocean floor");
		AddGathering(ItemID.Cactus, "Chop: Desert cactus");
		AddGathering(ItemID.FallenStar, "Gather: Surface at night (falls from sky)");
		AddGathering(ItemID.Seashell, "Gather: Ocean beach (surface sand)");
		AddGathering(ItemID.Starfish, "Gather: Ocean beach (surface sand)");
		AddGathering(ItemID.MushroomGrassSeeds, "Gather: Glowing Mushroom biome (shake grass)");
		AddGathering(ItemID.JungleGrassSeeds, "Gather: Jungle (shake grass)");
		AddGathering(ItemID.CorruptSeeds, "Gather: Corruption (shake grass)");
		AddGathering(ItemID.CrimsonSeeds, "Gather: Crimson (shake grass)");
		AddGathering(ItemID.HallowedSeeds, "Gather: Hallow (shake grass, Hardmode)");
		AddGathering(ItemID.BlueBrick, "Mine: Blue Dungeon");
		AddGathering(ItemID.GreenBrick, "Mine: Green Dungeon");
		AddGathering(ItemID.PinkBrick, "Mine: Pink Dungeon");

		// Critters — caught with Bug Net / Golden Bug Net / Lavaproof Bug Net.
		// These have NO drop-table entries (the critter NPC yields the item item
		// form only via net-capture), so without these entries the system would
		// have no idea how to obtain them.
		IndexCritterSources();

		// Modded ore backfill — picks up any ore that isn't in the vanilla list above.
		BackfillOresFromTileSets();
	}

	/// <summary>
	/// Any item whose <c>createTile</c> is a tile flagged in <c>TileID.Sets.Ore</c>
	/// but which has no existing gathering source indexed gets a generic
	/// "Mine: Underground" entry. This means modded ores (Calamity's Aerialite /
	/// Cryonic Ore, Thorium's Aquaite / Thorium Ore, etc.) automatically register
	/// as Gathering sources instead of falling through to the "Material: X (check
	/// wiki)" inferred-source fallback. Vanilla ores are not touched — they keep
	/// the hand-written biome/tier descriptions set in <c>IndexGatheringSources</c>.
	/// </summary>
	private void BackfillOresFromTileSets()
	{
		var oreSet = TileID.Sets.Ore;
		if (oreSet == null) return;

		int backfilled = 0;
		foreach (var (itemType, item) in ContentSamples.ItemsByType)
		{
			if (itemType <= ItemID.None || item == null) continue;
			// createTile == -1 (default) means "places no tile"; cast-to-uint folds
			// the negative check and upper bound into one range comparison.
			int tileType = item.createTile;
			if ((uint)tileType >= (uint)oreSet.Length) continue;
			if (!oreSet[tileType]) continue;
			if (_sourcesByItem.ContainsKey(itemType)) continue; // Vanilla entry wins

			AddGathering(itemType, "Mine: Underground (check wiki for biome)");
			backfilled++;
		}

		if (backfilled > 0)
			Common.Log.Info($"Backfilled {backfilled} modded ores via TileID.Sets.Ore");
	}

	/// <summary>
	/// Critters are caught in item form by using a Bug Net (or Lavaproof Bug Net for
	/// Underworld critters) on the critter NPC. Terraria stores the catch mapping
	/// directly on the NPC: <c>NPC.catchItem</c> is the item ID produced when the
	/// critter is netted. We iterate every NPC flagged as a critter in
	/// <c>NPCID.Sets.CountsAsCritter</c>, read its catchItem, and use the bestiary
	/// to derive a biome string.
	///
	/// This auto-detection has two benefits over a hand-curated list:
	/// 1. Modded critters (Calamity, Thorium, etc.) are picked up automatically as
	///    long as the mod sets CountsAsCritter and catchItem — which is the standard
	///    pattern for any critter that should work with Bug Nets.
	/// 2. Biome strings come from the bestiary's SpawnConditionBestiaryInfoElement,
	///    which is the same data the game's bestiary UI displays — it's always more
	///    accurate than hand-curation and stays in sync with vanilla updates.
	/// </summary>
	private void IndexCritterSources()
	{
		int count = 0;
		int maxNpcId = Math.Min(NPCID.Sets.CountsAsCritter.Length, NPCLoader.NPCCount);

		for (int npcId = 1; npcId < maxNpcId; npcId++)
		{
			if (!NPCID.Sets.CountsAsCritter[npcId])
				continue;

			int catchItemId;
			bool isLavaCritter;
			int rarity;
			try
			{
				var npc = new NPC();
				npc.SetDefaults(npcId);
				catchItemId = npc.catchItem;
				isLavaCritter = npc.lavaImmune;
				rarity = npc.rarity;
			}
			catch
			{
				continue;
			}

			if (catchItemId <= ItemID.None)
				continue;

			// Biome hint from bestiary — falls back to heuristic keyword matching
			// when an NPC has no bestiary entry (shouldn't happen for vanilla
			// critters, but modded ones might omit bestiary registration).
			string location = null;
			try { location = global::ProgressionCompass.Content.Tracking.BiomeMapper.GetEnrichedLocation(npcId); }
			catch { }

			// Lavaproof Bug Net is required for lava-immune critters (Lavafly,
			// MagmaSnail, Hell Butterfly in vanilla). Regular Bug Net otherwise.
			string tool = isLavaCritter ? "Lavaproof Bug Net" : "Bug Net";

			// Gold/rare variants get a "rare" annotation so the UI can communicate
			// that the player shouldn't expect to find them on their first attempt.
			string rareTag = rarity > 0 ? ", rare" : "";

			string description = !string.IsNullOrEmpty(location) && location != "Unknown"
				? $"Catch: {location}{rareTag} ({tool})"
				: $"Catch{rareTag} ({tool})";

			AddSource(catchItemId, new ItemSource(ItemSourceType.Catching, description, npcId));
			count++;
		}

		Common.Log.Info($"Indexed {count} catchable critters via NPCID.Sets.CountsAsCritter");
	}

	private void AddGathering(int itemType, string description)
	{
		AddSource(itemType, new ItemSource(ItemSourceType.Gathering, description));
	}

	private void IndexShimmerTransforms()
	{
		var shimmerMap = ItemID.Sets.ShimmerTransformToItem;
		if (shimmerMap == null) return;

		for (int sourceItem = 0; sourceItem < shimmerMap.Length; sourceItem++)
		{
			int resultItem = shimmerMap[sourceItem];
			if (resultItem <= ItemID.None) continue;

			string sourceName;
			try { sourceName = Lang.GetItemNameValue(sourceItem); }
			catch { sourceName = $"Item#{sourceItem}"; }

			AddSource(resultItem, new ItemSource(
				ItemSourceType.Shimmer,
				$"Shimmer: {sourceName}",
				sourceItem));
		}
	}

	/// <summary>
	/// Indexes shimmer decrafting: when an item can be shimmered, its recipe ingredients
	/// become obtainable via "Shimmer decraft: ItemName". This provides alternative
	/// acquisition paths for ingredients.
	/// </summary>
	private void IndexShimmerDecrafting()
	{
		var recipeIndices = ItemID.Sets.CraftingRecipeIndices;
		if (recipeIndices == null) return;

		int count = 0;
		for (int itemType = 1; itemType < recipeIndices.Length; itemType++)
		{
			var indices = recipeIndices[itemType];
			if (indices == null || indices.Length == 0)
				continue;

			// Find the first decraftable recipe for this item
			Recipe decraftRecipe = null;
			foreach (int idx in indices)
			{
				if (idx < 0 || idx >= Recipe.numRecipes) continue;
				var recipe = Main.recipe[idx];
				if (recipe == null || recipe.DecraftDisabled) continue;
				decraftRecipe = recipe;
				break;
			}

			if (decraftRecipe == null) continue;

			// Check for custom shimmer results
			if (decraftRecipe.customShimmerResults != null && decraftRecipe.customShimmerResults.Count > 0)
			{
				foreach (var result in decraftRecipe.customShimmerResults)
				{
					if (result.type <= ItemID.None) continue;
					string itemName;
					try { itemName = Lang.GetItemNameValue(itemType); }
					catch { itemName = $"Item#{itemType}"; }

					AddSource(result.type, new ItemSource(
						ItemSourceType.Shimmer,
						$"Decraft: {itemName}",
						itemType));
					count++;
				}
			}
			else
			{
				// Standard decrafting: produces the recipe's ingredients
				foreach (var ingredient in decraftRecipe.requiredItem)
				{
					if (ingredient.type <= ItemID.None) continue;

					string itemName;
					try { itemName = Lang.GetItemNameValue(itemType); }
					catch { itemName = $"Item#{itemType}"; }

					AddSource(ingredient.type, new ItemSource(
						ItemSourceType.Shimmer,
						$"Decraft: {itemName}",
						itemType));
					count++;
				}
			}
		}

		Common.Log.Info($"Indexed {count} shimmer decrafting sources");
	}

	/// <summary>Extracts NPC name from a source description like "Drop: Hornet (1/3)" → "Hornet"</summary>
	private static string ExtractNPCName(string description)
	{
		if (string.IsNullOrEmpty(description))
			return null;
		// Strip "Drop: " or "Buy: " prefix
		int colonIdx = description.IndexOf(": ");
		string name = colonIdx >= 0 ? description[(colonIdx + 2)..] : description;
		// Strip rate suffix like " (1/3)"
		int parenIdx = name.IndexOf(" (");
		if (parenIdx > 0)
			name = name[..parenIdx];
		return name.Trim();
	}

	/// <summary>
	/// Reflection fallback for unrecognized IItemDropRule implementations.
	/// Looks for fields named 'itemId', 'itemType', or 'dropId' and extracts
	/// integer values that look like item IDs.
	/// </summary>
	private void TryExtractViaReflection(IItemDropRule rule, int npcId, string npcName,
		bool isBoss, string eventName = null, string difficultyTag = null)
	{
		ItemSourceType GetSourceType() =>
			eventName != null ? ItemSourceType.Event
			: isBoss ? ItemSourceType.BossDrop
			: ItemSourceType.NPCDrop;
		string eventTag = eventName != null ? $" ({eventName})" : "";
		string diffTag = difficultyTag ?? "";

		try
		{
			var type = rule.GetType();
			string[] fieldNames = ["itemId", "itemType", "dropId", "ItemId", "ItemType"];

			foreach (string fieldName in fieldNames)
			{
				var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
				if (field != null && field.FieldType == typeof(int))
				{
					int itemId = (int)field.GetValue(rule);
					if (itemId > ItemID.None)
					{
						AddSource(itemId, new ItemSource(
							GetSourceType(),
							$"Drop: {npcName}{eventTag}{diffTag}",
							npcId));
					}
					return;
				}
			}

			foreach (string fieldName in (string[])["dropIds", "DropIds", "itemIds"])
			{
				var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
				if (field != null && field.FieldType == typeof(int[]))
				{
					var ids = (int[])field.GetValue(rule);
					if (ids != null)
					{
						foreach (int itemId in ids)
						{
							if (itemId > ItemID.None)
								AddSource(itemId, new ItemSource(
									GetSourceType(),
									$"Drop: {npcName}{eventTag}{diffTag}",
									npcId));
						}
					}
					return;
				}
			}
		}
		catch { }
	}

	private static bool IsNPCBoss(int npcId)
	{
		try
		{
			// Check vanilla boss set (only for positive IDs within array bounds)
			var bossSet = NPCID.Sets.ShouldBeCountedAsBoss;
			if (npcId > 0 && npcId < bossSet.Length)
				return bossSet[npcId];

			// Modded or variant NPCs: check via NPC defaults
			// Skip negative IDs (vanilla variants) — they're never bosses
			if (npcId <= 0)
				return false;

			var npc = new NPC();
			npc.SetDefaults(npcId);
			return npc.boss;
		}
		catch
		{
			return false;
		}
	}
}
