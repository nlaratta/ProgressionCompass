using Terraria;
using Terraria.ModLoader;

namespace CraftPathOptimizer.Content.Progression;

public enum PlayerClass
{
	Mixed = 0,
	Melee = 1,
	Ranged = 2,
	Mage = 3,
	Summoner = 4
}

/// <summary>
/// Auto-detects the player's class from equipped gear and weapon usage.
/// Uses DamageType.CountsAsClass() for mod compatibility.
/// </summary>
public static class ClassDetector
{
	/// <summary>
	/// Detects the dominant class from the player's equipped weapons and armor.
	/// Returns Mixed if no clear majority.
	/// </summary>
	public static PlayerClass Detect(Player player)
	{
		int melee = 0, ranged = 0, mage = 0, summon = 0;

		// Count weapons in hotbar (slots 0-9) by class
		for (int i = 0; i < 10; i++)
		{
			Item item = player.inventory[i];
			if (item.type <= Terraria.ID.ItemID.None || item.damage <= 0)
				continue;

			// Whips use SummonMeleeSpeed which doesn't CountsAsClass(Summon)
			if (item.DamageType == DamageClass.SummonMeleeSpeed)
				summon++;
			else if (item.DamageType.CountsAsClass(DamageClass.Melee))
				melee++;
			else if (item.DamageType.CountsAsClass(DamageClass.Ranged))
				ranged++;
			else if (item.DamageType.CountsAsClass(DamageClass.Magic))
				mage++;
			else if (item.DamageType.CountsAsClass(DamageClass.Summon))
				summon++;
		}

		// Armor set gives a strong signal — weight it heavily
		Item head = player.armor[0];
		Item body = player.armor[1];
		Item legs = player.armor[2];

		AddArmorClassWeight(head, ref melee, ref ranged, ref mage, ref summon);
		AddArmorClassWeight(body, ref melee, ref ranged, ref mage, ref summon);
		AddArmorClassWeight(legs, ref melee, ref ranged, ref mage, ref summon);

		// Find dominant class (needs >40% of total and at least 2 ahead of second)
		int total = melee + ranged + mage + summon;
		if (total == 0)
			return PlayerClass.Mixed;

		int max = System.Math.Max(System.Math.Max(melee, ranged), System.Math.Max(mage, summon));
		if (max < total * 0.4f)
			return PlayerClass.Mixed;

		if (melee == max) return PlayerClass.Melee;
		if (ranged == max) return PlayerClass.Ranged;
		if (mage == max) return PlayerClass.Mage;
		if (summon == max) return PlayerClass.Summoner;

		return PlayerClass.Mixed;
	}

	/// <summary>
	/// Adds class weight from an armor piece. Armor that boosts a specific class
	/// (via damage type set bonuses reflected in its tooltip) is weighted +3.
	/// </summary>
	private static void AddArmorClassWeight(Item armor, ref int melee, ref int ranged, ref int mage, ref int summon)
	{
		if (armor.type <= Terraria.ID.ItemID.None || armor.defense <= 0)
			return;

		// Check if the armor's name or tooltip hints at a class
		// This is a heuristic — works for vanilla and most mods
		string name = (armor.Name ?? "").ToLowerInvariant();

		if (ContainsAny(name, "melee", "warrior", "beetle", "solar", "hallowed mask"))
			melee += 3;
		else if (ContainsAny(name, "ranged", "ranger", "shroomite", "vortex"))
			ranged += 3;
		else if (ContainsAny(name, "magic", "mage", "wizard", "spectre", "nebula"))
			mage += 3;
		else if (ContainsAny(name, "summon", "summoner", "tiki", "spooky", "stardust"))
			summon += 3;
	}

	private static bool ContainsAny(string text, params string[] keywords)
	{
		foreach (string kw in keywords)
			if (text.Contains(kw))
				return true;
		return false;
	}

	/// <summary>
	/// Checks if an item matches a class filter. Covers weapons (via DamageType),
	/// armor (via tooltip scanning), and accessories (via tooltip scanning).
	/// </summary>
	/// <param name="strict">When true, generic/unclassified armor and accessories
	/// only match Mixed (All), not specific class filters. Use strict=true for
	/// the Upgrades window to avoid showing generic cobalt armor under Summoner.</param>
	public static bool ItemMatchesClass(Item item, PlayerClass playerClass, bool strict = false)
	{
		if (playerClass == PlayerClass.Mixed)
			return true;

		// Ammo: only relevant to Ranged
		if (item.ammo > 0)
			return playerClass == PlayerClass.Ranged;

		// Weapons: check DamageType
		if (item.damage > 0)
		{
			bool isWhip = item.DamageType == DamageClass.SummonMeleeSpeed;

			return playerClass switch
			{
				PlayerClass.Melee => !isWhip && item.DamageType.CountsAsClass(DamageClass.Melee),
				PlayerClass.Ranged => item.DamageType.CountsAsClass(DamageClass.Ranged),
				PlayerClass.Mage => item.DamageType.CountsAsClass(DamageClass.Magic),
				PlayerClass.Summoner => item.DamageType.CountsAsClass(DamageClass.Summon) || isWhip,
				_ => true
			};
		}

		// Armor and accessories: scan tooltip text for class keywords.
		if (item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0 || item.accessory)
		{
			PlayerClass? eqClass = DetectEquipmentClassFromTooltip(item);
			if (eqClass == null)
			{
				if (strict)
					return false; // Strict: unclassified equipment only shows under "All"
				return true; // Non-strict: universal matches all
			}
			return eqClass == playerClass;
		}

		// Tools, materials, etc. — always match
		return true;
	}

	/// <summary>
	/// Returns the detected class of an armor piece, or null if unclassified.
	/// Uses tooltip scanning + name-based fallbacks.
	/// </summary>
	public static PlayerClass? DetectArmorClass(Item item) => DetectEquipmentClassFromTooltip(item);

	/// <summary>
	/// Detects equipment class by scanning tooltip text for class keywords.
	/// Works for armor, accessories, and any equipment — both vanilla and modded.
	/// Tooltip text consistently uses "melee damage", "ranged", "mana", "minion" etc.
	/// </summary>
	private static PlayerClass? DetectEquipmentClassFromTooltip(Item item)
	{
		if (item.ToolTip == null || item.ToolTip == Terraria.UI.ItemTooltip.None)
			return null;

		try
		{
			for (int i = 0; i < item.ToolTip.Lines; i++)
			{
				string line = item.ToolTip.GetLine(i)?.ToLowerInvariant();
				if (string.IsNullOrEmpty(line))
					continue;

				if (ContainsAny(line, "melee damage", "melee speed", "melee crit"))
					return PlayerClass.Melee;
				if (ContainsAny(line, "ranged damage", "ranged crit", "ranged speed",
					"ammo consumption", "arrow damage", "bullet damage"))
					return PlayerClass.Ranged;
				if (ContainsAny(line, "magic damage", "magic crit", "mana cost", "maximum mana",
					"mana usage", "reduces mana", "magic speed"))
					return PlayerClass.Mage;
				if (ContainsAny(line, "summon damage", "minion damage", "minion capacity",
					"sentry capacity", "whip speed", "whip range", "summon tag",
					"minion knockback"))
					return PlayerClass.Summoner;
			}
		}
		catch { }

		// Fallback: check item name for class-specific armor variants
		string name = (item.Name ?? "").ToLowerInvariant();

		// Head slot class variants (Hallowed Mask vs Helmet vs Headgear, etc.)
		if (item.headSlot >= 0)
		{
			if (ContainsAny(name, "mask", "visage"))
				return PlayerClass.Melee;
			if (ContainsAny(name, "helmet", "visor"))
				return PlayerClass.Ranged;
			if (ContainsAny(name, "headgear", "hood", "hat", "circlet"))
				return PlayerClass.Mage;
		}

		// Armor set name prefixes — works for all slots (head/body/legs)
		if (item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0)
		{
			if (ContainsAny(name, "solar", "beetle", "turtle"))
				return PlayerClass.Melee;
			if (ContainsAny(name, "vortex", "shroomite"))
				return PlayerClass.Ranged;
			if (ContainsAny(name, "nebula", "spectre"))
				return PlayerClass.Mage;
			if (ContainsAny(name, "stardust", "tiki", "spooky"))
				return PlayerClass.Summoner;
			// Ore armor class variants (Cobalt/Mythril/Adamantite/Titanium/Hallowed/etc.)
			// These are detected via headSlot name above; body/legs are shared across classes
		}

		return null;
	}

	public static string GetDisplayName(PlayerClass pc) => pc switch
	{
		PlayerClass.Melee => "Melee",
		PlayerClass.Ranged => "Ranged",
		PlayerClass.Mage => "Mage",
		PlayerClass.Summoner => "Summoner",
		_ => "Mixed"
	};
}
