using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ProgressionCompass.Common.Systems;
using ProgressionCompass.Content.Graph;
using ProgressionCompass.Content.Progression;

namespace ProgressionCompass.Common;

/// <summary>
/// In-game chat command for testing the resolver.
/// Usage: /cpo [itemname|zenith|cycle|all]
/// </summary>
public class CraftPathTestCommand : ModCommand
{
	public override string Command => "cpo";
	public override string Usage => "/cpo [zenith|terra|ankh|cell|cycle|stats|all]";
	public override string Description => "Test the Progression Compass resolver";
	public override CommandType Type => CommandType.Chat;

	public override void Action(CommandCaller caller, string input, string[] args)
	{
		var recipeIndex = ModContent.GetInstance<RecipeIndexSystem>();
		var sourceSystem = ModContent.GetInstance<ItemSourceSystem>();

		if (recipeIndex?.RecipesByResult == null)
		{
			caller.Reply("Recipe index not initialized yet.", Color.Red);
			return;
		}

		string testName = args.Length > 0 ? args[0].ToLowerInvariant() : "stats";

		switch (testName)
		{
			case "stats":
				RunStats(caller, recipeIndex, sourceSystem);
				break;
			case "zenith":
				RunItemTest(caller, recipeIndex, sourceSystem, ItemID.Zenith, "Zenith");
				break;
			case "terra":
				RunItemTest(caller, recipeIndex, sourceSystem, ItemID.TerraBlade, "Terra Blade");
				break;
			case "ankh":
				RunItemTest(caller, recipeIndex, sourceSystem, ItemID.AnkhShield, "Ankh Shield");
				break;
			case "cell":
				RunItemTest(caller, recipeIndex, sourceSystem, ItemID.CellPhone, "Cell Phone");
				break;
			case "cycle":
				RunCycleTest(caller, recipeIndex, sourceSystem);
				break;
			case "all":
				RunStats(caller, recipeIndex, sourceSystem);
				RunItemTest(caller, recipeIndex, sourceSystem, ItemID.Zenith, "Zenith");
				RunItemTest(caller, recipeIndex, sourceSystem, ItemID.TerraBlade, "Terra Blade");
				RunItemTest(caller, recipeIndex, sourceSystem, ItemID.AnkhShield, "Ankh Shield");
				RunCycleTest(caller, recipeIndex, sourceSystem);
				break;
			default:
				caller.Reply($"Unknown test: {testName}. Use: {Usage}", Color.Yellow);
				break;
		}
	}

	private static void RunStats(CommandCaller caller, RecipeIndexSystem recipeIndex, ItemSourceSystem sourceSystem)
	{
		caller.Reply($"[CPO] Recipe Index: {recipeIndex.RecipesByResult.Count} craftable items, " +
			$"{recipeIndex.RecipesByIngredient.Count} ingredient types", Color.Cyan);
		caller.Reply($"[CPO] Total recipes in Main.recipe[]: {Terraria.Recipe.numRecipes}", Color.Cyan);

		// Inventory scan test
		var inv = InventoryScanner.Scan(false);
		caller.Reply($"[CPO] Inventory scan: {inv.Count} distinct item types in personal storage", Color.Cyan);

		var invWithChests = InventoryScanner.Scan(true);
		caller.Reply($"[CPO] Inventory scan (with chests): {invWithChests.Count} distinct item types", Color.Cyan);
	}

	private static void RunItemTest(
		CommandCaller caller,
		RecipeIndexSystem recipeIndex,
		ItemSourceSystem sourceSystem,
		int itemId,
		string itemName)
	{
		var inventory = InventoryScanner.Scan(false);
		var worldContext = WorldContext.Capture();
		var resolver = new DependencyResolver(recipeIndex, sourceSystem, worldContext: worldContext);

		var sw = System.Diagnostics.Stopwatch.StartNew();
		var result = resolver.Resolve(itemId, 1, inventory, false);
		sw.Stop();

		caller.Reply($"[CPO] {itemName}: resolved in {sw.ElapsedMilliseconds}ms", Color.Green);
		caller.Reply($"  Tree: {GetNodeSummary(result.Tree)}", Color.White);
		caller.Reply($"  Materials: {result.Materials.Count} types", Color.White);
		caller.Reply($"  Craft steps: {result.CraftOrder.Count}", Color.White);
		caller.Reply($"  Acquisition cost: {result.Tree.AcquisitionCost} items to acquire", Color.White);

		// Count what we already have
		int owned = 0;
		foreach (var m in result.Materials)
		{
			if (m.IsComplete(false))
				owned++;
		}
		caller.Reply($"  Already owned: {owned}/{result.Materials.Count} material types", Color.Yellow);
	}

	private static void RunCycleTest(CommandCaller caller, RecipeIndexSystem recipeIndex, ItemSourceSystem sourceSystem)
	{
		// Wood and Platform have a cycle: Wood -> Platform (at Work Bench) -> Wood (at Work Bench)
		var inventory = new Dictionary<int, InventoryEntry>();
		var resolver = new DependencyResolver(recipeIndex, sourceSystem, worldContext: WorldContext.Capture());

		var sw = System.Diagnostics.Stopwatch.StartNew();
		var result = resolver.Resolve(ItemID.Wood, 100, inventory, false);
		sw.Stop();

		bool hasCycle = result.Tree is AcquireNode;
		caller.Reply($"[CPO] Cycle test (Wood x100, empty inventory): " +
			$"resolved in {sw.ElapsedMilliseconds}ms, " +
			$"result type: {result.Tree.GetType().Name} " +
			(hasCycle ? "(correctly fell through to AcquireNode)" : "(resolved as craft)"),
			hasCycle ? Color.Green : Color.Yellow);
	}

	private static string GetNodeSummary(GraphNode node)
	{
		int have = 0, acquire = 0, craft = 0, group = 0;
		CountNodes(node, ref have, ref acquire, ref craft, ref group);
		return $"{have} owned, {acquire} to acquire, {craft} crafts, {group} groups";
	}

	private static void CountNodes(GraphNode node, ref int have, ref int acquire, ref int craft, ref int group)
	{
		switch (node)
		{
			case HaveNode:
				have++;
				break;
			case AcquireNode:
				acquire++;
				break;
			case CraftNode c:
				craft++;
				foreach (var child in c.Ingredients)
					CountNodes(child, ref have, ref acquire, ref craft, ref group);
				break;
			case GroupNode g:
				group++;
				CountNodes(g.ChosenItem, ref have, ref acquire, ref craft, ref group);
				break;
		}
	}
}
