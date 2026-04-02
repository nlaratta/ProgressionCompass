using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using ProgressionCompass.Content.Graph;

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
			return sources;

		// Fallback: try to infer source from item properties
		return InferSourceFromItem(itemType);
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

		Mod.Logger.Info($"Indexed item sources (drops/gathering/shimmer) for {_sourcesByItem.Count} items");
	}

	public override void PostAddRecipes()
	{
		if (Main.dedServ || _sourcesByItem == null)
			return;

		int before = _sourcesByItem.Count;
		IndexNPCShops();
		Mod.Logger.Info($"Indexed NPC shop sources: {_sourcesByItem.Count - before} new items (total: {_sourcesByItem.Count})");
	}

	public override void Unload()
	{
		_sourcesByItem = null;
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

		switch (rule)
		{
			// ── Leaf rules (contain item IDs directly) ──────────────
			// Note: ItemDropWithConditionRule extends CommonDrop, so this catches both
			case CommonDrop common:
				AddSource(common.itemId, new ItemSource(
					GetSourceType(),
					$"Drop: {npcName}{eventTag}{diffTag}" + (common.chanceDenominator > 1 ? $" (1/{common.chanceDenominator})" : ""),
					npcId,
					1f / common.chanceDenominator));
				break;

			case DropOneByOne dropOne:
				AddSource(dropOne.itemId, new ItemSource(
					GetSourceType(),
					$"Drop: {npcName}{eventTag}{diffTag}",
					npcId));
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

	internal static string GetNPCEventName(int npcId)
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
				try
				{
					int price = (entry.Item.shopCustomPrice.HasValue && entry.Item.shopCustomPrice.Value > 0)
						? entry.Item.shopCustomPrice.Value
						: entry.Item.value / 5;
					if (entry.Item.shopSpecialCurrency >= 0)
						priceText = ""; // Special currency — skip coin display
					else if (price > 0)
						priceText = $" ~{FormatCoins(price)}";
				}
				catch { }

				// Extract conditions (night only, post-boss, etc.)
				string condText = "";
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
						condText = $" ({string.Join(", ", condTexts)})";
					if (condText.Length > 50)
						condText = condText[..49] + "..)";
				}
				catch { }

				AddSource(entry.Item.type, new ItemSource(
					ItemSourceType.NPCShop,
					$"Buy: {shopOwner}{priceText}{condText}",
					npcShop.NpcType));
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

	private static string FormatCoins(int copper)
	{
		if (copper <= 0) return "";
		int platinum = copper / 1000000;
		copper %= 1000000;
		int gold = copper / 10000;
		copper %= 10000;
		int silver = copper / 100;
		copper %= 100;

		if (platinum > 0) return gold > 0 ? $"{platinum}p {gold}g" : $"{platinum}p";
		if (gold > 0) return silver > 0 ? $"{gold}g {silver}s" : $"{gold}g";
		if (silver > 0) return $"{silver}s";
		return $"{copper}c";
	}

	private static string InferCrateBiome(string crateName)
	{
		string lower = (crateName ?? "").ToLowerInvariant();
		if (lower.Contains("jungle")) return "Jungle";
		if (lower.Contains("frozen") || lower.Contains("boreal")) return "Snow biome";
		if (lower.Contains("ocean") || lower.Contains("seaside")) return "Ocean";
		if (lower.Contains("sky") || lower.Contains("azure")) return "Sky islands";
		if (lower.Contains("corrupt")) return "Corruption";
		if (lower.Contains("crimson") || lower.Contains("hematic")) return "Crimson";
		if (lower.Contains("hallow") || lower.Contains("divine")) return "Hallow";
		if (lower.Contains("oasis") || lower.Contains("desert") || lower.Contains("mirage")) return "Desert";
		if (lower.Contains("obsidian") || lower.Contains("hellstone")) return "Underworld";
		return null;
	}

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

			// Add biome hint for fishing crates
			string biomeHint = isCrate ? InferCrateBiome(sourceName) : null;
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

		// Gathered from world objects — not in drop tables
		AddGathering(ItemID.Cobweb, "Gather: Spider caves / Underground");
		AddGathering(ItemID.JungleSpores, "Gather: Underground Jungle (glowing plants)");
		AddGathering(ItemID.Coral, "Gather: Ocean floor");
		AddGathering(ItemID.Cactus, "Chop: Desert cactus");
		AddGathering(ItemID.FallenStar, "Gather: Surface at night (falls from sky)");
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
