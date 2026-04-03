using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ProgressionCompass.Content.Progression;

namespace ProgressionCompass.Common.Systems;

/// <summary>
/// Builds fast lookup dictionaries from the global recipe array after all mods have
/// registered their recipes. This is the foundation — everything from the resolver
/// to the search UI queries these dictionaries.
/// </summary>
public class RecipeIndexSystem : ModSystem
{
	/// <summary>Maps item type → all recipes that produce it.</summary>
	public Dictionary<int, List<Recipe>> RecipesByResult { get; private set; }

	/// <summary>Maps item type → all recipes that consume it as an ingredient.</summary>
	public Dictionary<int, List<Recipe>> RecipesByIngredient { get; private set; }

	/// <summary>Set of all item types that have at least one recipe.</summary>
	public HashSet<int> CraftableItems { get; private set; }

	public override void PostAddRecipes()
	{
		RecipesByResult = [];
		RecipesByIngredient = [];
		CraftableItems = [];

		// Single pass over every recipe in the game (vanilla + all loaded mods).
		// Recipe.numRecipes tells us how many valid entries exist in the fixed-size array.
		for (int i = 0; i < Recipe.numRecipes; i++)
		{
			Recipe recipe = Main.recipe[i];
			int resultType = recipe.createItem.type;

			// Skip air/empty recipes
			if (resultType <= ItemID.None)
				continue;

			// Index by result
			if (!RecipesByResult.TryGetValue(resultType, out var resultList))
			{
				resultList = [];
				RecipesByResult[resultType] = resultList;
			}
			resultList.Add(recipe);
			CraftableItems.Add(resultType);

			// Index by each ingredient
			foreach (Item ingredient in recipe.requiredItem)
			{
				if (ingredient.type <= ItemID.None)
					continue;

				if (!RecipesByIngredient.TryGetValue(ingredient.type, out var ingredientList))
				{
					ingredientList = [];
					RecipesByIngredient[ingredient.type] = ingredientList;
				}
				ingredientList.Add(recipe);
			}
		}

		Mod.Logger.Info(
			$"Indexed {Recipe.numRecipes} recipes: " +
			$"{RecipesByResult.Count} craftable items, " +
			$"{RecipesByIngredient.Count} ingredient types");

		EvilAffiliationMap.Initialize();
		Mod.Logger.Info($"Built evil affiliation map: {EvilAffiliationMap.Count} evil-affiliated items");
	}

	public override void Unload()
	{
		RecipesByResult = null;
		RecipesByIngredient = null;
		CraftableItems = null;
		EvilAffiliationMap.Unload();
		CacheVersion.Reset();
	}
}
