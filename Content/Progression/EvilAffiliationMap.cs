using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace ProgressionCompass.Content.Progression;

/// <summary>
/// Maps items and recipes to their evil affiliation (Corruption, Crimson, or Neutral).
/// Combines a hardcoded vanilla lookup with runtime detection via recipe DecraftConditions
/// and ingredient-chain inference. Initialized once during PostAddRecipes.
/// </summary>
public static class EvilAffiliationMap
{
	private static Dictionary<int, EvilAffiliation> _cache;

	/// <summary>Number of evil-affiliated items in the cache.</summary>
	public static int Count => _cache?.Count ?? 0;

	/// <summary>
	/// Builds the evil affiliation cache. Must be called from PostAddRecipes
	/// (main thread, after recipe migration has moved corruption/crimson flags
	/// to DecraftConditions).
	/// </summary>
	public static void Initialize()
	{
		_cache = new Dictionary<int, EvilAffiliation>(128);

		// Phase 1: Seed with hardcoded vanilla items
		SeedVanillaItems();

		// Phase 2: Scan all recipes for DecraftConditions
		ScanRecipeDecraftConditions();

		// Phase 3: Ingredient-chain inference (propagate evil from known items)
		InferFromIngredients();
	}

	/// <summary>Thread-safe lookup after initialization. Returns Neutral if unknown.</summary>
	public static EvilAffiliation Get(int itemType)
	{
		if (_cache != null && _cache.TryGetValue(itemType, out var aff))
			return aff;
		return EvilAffiliation.Neutral;
	}

	/// <summary>
	/// Checks a recipe's DecraftConditions for Condition.CrimsonWorld or
	/// Condition.CorruptWorld. Returns the affiliation or Neutral if none found.
	/// </summary>
	public static EvilAffiliation GetRecipeAffiliation(Recipe recipe)
	{
		if (recipe?.DecraftConditions == null || recipe.DecraftConditions.Count == 0)
			return EvilAffiliation.Neutral;

		foreach (var cond in recipe.DecraftConditions)
		{
			if (cond == Condition.CrimsonWorld)
				return EvilAffiliation.Crimson;
			if (cond == Condition.CorruptWorld)
				return EvilAffiliation.Corruption;
		}

		return EvilAffiliation.Neutral;
	}

	public static void Unload() => _cache = null;

	// ── Phase 1: Hardcoded Vanilla Items ────────────────────────

	private static void SeedVanillaItems()
	{
		// Corruption materials
		Set(ItemID.DemoniteOre, EvilAffiliation.Corruption);
		Set(ItemID.DemoniteBar, EvilAffiliation.Corruption);
		Set(ItemID.ShadowScale, EvilAffiliation.Corruption);
		Set(ItemID.RottenChunk, EvilAffiliation.Corruption);
		Set(ItemID.WormTooth, EvilAffiliation.Corruption);
		Set(ItemID.VilePowder, EvilAffiliation.Corruption);
		Set(ItemID.VileMushroom, EvilAffiliation.Corruption);
		Set(ItemID.Ebonwood, EvilAffiliation.Corruption);
		Set(ItemID.EbonstoneBlock, EvilAffiliation.Corruption);
		Set(ItemID.CorruptSeeds, EvilAffiliation.Corruption);
		Set(ItemID.PurpleSolution, EvilAffiliation.Corruption);
		Set(ItemID.CursedFlame, EvilAffiliation.Corruption);
		Set(ItemID.EbonsandBlock, EvilAffiliation.Corruption);

		// Corruption weapons/tools
		Set(ItemID.LightsBane, EvilAffiliation.Corruption);
		Set(ItemID.Musket, EvilAffiliation.Corruption);
		Set(ItemID.BallOHurt, EvilAffiliation.Corruption);
		Set(ItemID.Vilethorn, EvilAffiliation.Corruption);
		Set(ItemID.BandofStarpower, EvilAffiliation.Corruption);
		Set(ItemID.ShadowOrb, EvilAffiliation.Corruption);

		// Corruption boss items
		Set(ItemID.WormFood, EvilAffiliation.Corruption);
		Set(ItemID.WormScarf, EvilAffiliation.Corruption);
		Set(ItemID.EatersBone, EvilAffiliation.Corruption);
		Set(ItemID.EaterOfWorldsBossBag, EvilAffiliation.Corruption);

		// Corruption armor
		Set(ItemID.ShadowHelmet, EvilAffiliation.Corruption);
		Set(ItemID.ShadowScalemail, EvilAffiliation.Corruption);
		Set(ItemID.ShadowGreaves, EvilAffiliation.Corruption);
		Set(ItemID.AncientShadowHelmet, EvilAffiliation.Corruption);
		Set(ItemID.AncientShadowScalemail, EvilAffiliation.Corruption);
		Set(ItemID.AncientShadowGreaves, EvilAffiliation.Corruption);

		// Corruption accessories/misc
		Set(ItemID.CorruptionKey, EvilAffiliation.Corruption);
		Set(ItemID.CursedFlames, EvilAffiliation.Corruption);
		Set(ItemID.ClingerStaff, EvilAffiliation.Corruption);
		Set(ItemID.PutridScent, EvilAffiliation.Corruption);
		Set(ItemID.DartPistol, EvilAffiliation.Corruption);
		Set(ItemID.WormHook, EvilAffiliation.Corruption);
		Set(ItemID.ChainGuillotines, EvilAffiliation.Corruption);

		// Crimson materials
		Set(ItemID.CrimtaneOre, EvilAffiliation.Crimson);
		Set(ItemID.CrimtaneBar, EvilAffiliation.Crimson);
		Set(ItemID.TissueSample, EvilAffiliation.Crimson);
		Set(ItemID.Vertebrae, EvilAffiliation.Crimson);
		Set(ItemID.ViciousPowder, EvilAffiliation.Crimson);
		Set(ItemID.ViciousMushroom, EvilAffiliation.Crimson);
		Set(ItemID.Shadewood, EvilAffiliation.Crimson);
		Set(ItemID.CrimstoneBlock, EvilAffiliation.Crimson);
		Set(ItemID.CrimsonSeeds, EvilAffiliation.Crimson);
		Set(ItemID.RedSolution, EvilAffiliation.Crimson);
		Set(ItemID.Ichor, EvilAffiliation.Crimson);
		Set(ItemID.CrimsandBlock, EvilAffiliation.Crimson);

		// Crimson weapons/tools
		Set(ItemID.BloodButcherer, EvilAffiliation.Crimson);
		Set(ItemID.TheUndertaker, EvilAffiliation.Crimson);
		Set(ItemID.TheRottedFork, EvilAffiliation.Crimson);
		Set(ItemID.CrimsonRod, EvilAffiliation.Crimson);
		Set(ItemID.PanicNecklace, EvilAffiliation.Crimson);
		Set(ItemID.CrimsonHeart, EvilAffiliation.Crimson);

		// Crimson boss items
		Set(ItemID.BloodySpine, EvilAffiliation.Crimson);
		Set(ItemID.BrainOfConfusion, EvilAffiliation.Crimson);
		Set(ItemID.BoneRattle, EvilAffiliation.Crimson);
		Set(ItemID.BrainOfCthulhuBossBag, EvilAffiliation.Crimson);

		// Crimson armor
		Set(ItemID.CrimsonHelmet, EvilAffiliation.Crimson);
		Set(ItemID.CrimsonScalemail, EvilAffiliation.Crimson);
		Set(ItemID.CrimsonGreaves, EvilAffiliation.Crimson);

		// Crimson accessories/misc
		Set(ItemID.CrimsonKey, EvilAffiliation.Crimson);
		Set(ItemID.FleshKnuckles, EvilAffiliation.Crimson);
		Set(ItemID.SoulDrain, EvilAffiliation.Crimson);
		Set(ItemID.DartRifle, EvilAffiliation.Crimson);
		Set(ItemID.FetidBaghnakhs, EvilAffiliation.Crimson);
		Set(ItemID.TendonHook, EvilAffiliation.Crimson);
	}

	// ── Phase 2: Recipe DecraftCondition Scan ───────────────────

	private static void ScanRecipeDecraftConditions()
	{
		for (int i = 0; i < Recipe.numRecipes; i++)
		{
			var recipe = Main.recipe[i];
			if (recipe?.createItem == null || recipe.createItem.type <= ItemID.None)
				continue;

			var aff = GetRecipeAffiliation(recipe);
			if (aff != EvilAffiliation.Neutral)
				_cache.TryAdd(recipe.createItem.type, aff);
		}
	}

	// ── Phase 3: Ingredient-Chain Inference ─────────────────────

	private static void InferFromIngredients()
	{
		// Multiple passes: items crafted from evil items inherit that affiliation.
		// Stop when no new items are classified.
		bool changed = true;
		int passes = 0;
		while (changed && passes < 5)
		{
			changed = false;
			passes++;

			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				var recipe = Main.recipe[i];
				if (recipe?.createItem == null || recipe.createItem.type <= ItemID.None)
					continue;

				int outputType = recipe.createItem.type;
				if (_cache.ContainsKey(outputType))
					continue; // Already classified

				// Check if ALL non-trivial ingredients share the same evil affiliation
				EvilAffiliation inherited = EvilAffiliation.Neutral;
				bool hasEvilIngredient = false;
				bool conflicting = false;

				foreach (var ingredient in recipe.requiredItem)
				{
					if (ingredient == null || ingredient.type <= ItemID.None)
						continue;

					var ingAff = Get(ingredient.type);
					if (ingAff == EvilAffiliation.Neutral)
						continue;

					hasEvilIngredient = true;
					if (inherited == EvilAffiliation.Neutral)
						inherited = ingAff;
					else if (inherited != ingAff)
					{
						conflicting = true;
						break;
					}
				}

				if (hasEvilIngredient && !conflicting && inherited != EvilAffiliation.Neutral)
				{
					// Only inherit if this is the SOLE recipe for the item,
					// or if ALL recipes share the same affiliation.
					// (Night's Edge has both a Corruption and Crimson recipe — should stay Neutral)
					if (IsOnlyRecipeAffiliation(outputType, inherited))
					{
						_cache[outputType] = inherited;
						changed = true;
					}
				}
			}
		}
	}

	/// <summary>
	/// Checks if all recipes for this item that have evil ingredients share the same affiliation.
	/// Returns false if different recipes have different evil affiliations (e.g., Night's Edge).
	/// </summary>
	private static bool IsOnlyRecipeAffiliation(int itemType, EvilAffiliation expected)
	{
		for (int i = 0; i < Recipe.numRecipes; i++)
		{
			var recipe = Main.recipe[i];
			if (recipe?.createItem == null || recipe.createItem.type != itemType)
				continue;

			// Check this recipe's evil ingredients
			foreach (var ingredient in recipe.requiredItem)
			{
				if (ingredient == null || ingredient.type <= ItemID.None)
					continue;

				var ingAff = Get(ingredient.type);
				if (ingAff != EvilAffiliation.Neutral && ingAff != expected)
					return false; // Different evil affiliation found
			}
		}

		return true;
	}

	private static void Set(int itemType, EvilAffiliation aff)
	{
		_cache[itemType] = aff;
	}
}
