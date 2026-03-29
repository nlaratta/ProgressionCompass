using System;
using System.Collections.Generic;

namespace CraftPathOptimizer.Content.Graph;

/// <summary>
/// The complete output of the dependency resolver for one or more target items.
/// Contains everything the UI needs to render all three tabs.
/// </summary>
public class ResolverResult
{
	/// <summary>The root node of the crafting dependency tree.</summary>
	public GraphNode Tree { get; init; }

	/// <summary>
	/// Flat list of all leaf materials needed, aggregated by item type.
	/// Each entry contains the total needed, how much is owned, and sources.
	/// </summary>
	public List<MaterialEntry> Materials { get; init; } = [];

	/// <summary>Step-by-step crafting order (topologically sorted, leaf-first).</summary>
	public List<CraftStep> CraftOrder { get; init; } = [];

	/// <summary>Total distinct material types needed.</summary>
	public int TotalMaterialTypes => Materials.Count;

	/// <summary>Total individual items needed across all types.</summary>
	public int TotalItemCount
	{
		get
		{
			int count = 0;
			foreach (var m in Materials)
				count += m.QuantityNeeded;
			return count;
		}
	}
}

/// <summary>A single row in the flat materials list.</summary>
public record MaterialEntry(
	int ItemType,
	int QuantityNeeded,
	InventoryEntry Owned,
	List<ItemSource> Sources,
	bool IsCraftable)
{
	public int Deficit(bool includeChests) =>
		Math.Max(0, QuantityNeeded - Owned.AvailableCount(includeChests));

	public bool IsComplete(bool includeChests) =>
		Owned.AvailableCount(includeChests) >= QuantityNeeded;
}

/// <summary>A single step in the craft order sequence.</summary>
public record CraftStep(
	int StepNumber,
	int ItemType,
	int Quantity,
	int CraftCount,
	int[] RequiredTiles,
	List<CraftStepInput> Inputs,
	string ConditionDescription);

/// <summary>One ingredient of a craft step, showing its source.</summary>
public record CraftStepInput(
	int ItemType,
	int Quantity,
	CraftStepInputSource Source,
	int? FromStepNumber,
	string AcquireHint = null);

public enum CraftStepInputSource
{
	Owned,
	FromPreviousStep,
	NeedToAcquire
}
