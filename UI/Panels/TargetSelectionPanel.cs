using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;
using ProgressionCompass.UI.Elements;

namespace ProgressionCompass.UI.Panels;

/// <summary>
/// Top section of the UI: search bar + filtered item results dropdown.
/// Uses Terraria's built-in UISearchBar for reliable text input handling.
/// </summary>
public class TargetSelectionPanel : UIElement
{
	public event Action<int> OnItemSelected;
	public event Action<int> OnItemPinned;

	/// <summary>Set externally to allow the pin button to show tracked state.</summary>
	public Func<int, bool> IsItemTracked { get; set; }

	private UISearchBar _searchBar;
	private UIPanel _searchBarPanel;
	private readonly UIList _resultsList;
	private readonly UIScrollbar _scrollbar;
	private readonly int _maxResults = 50;
	private string _currentSearch = "";

	public TargetSelectionPanel()
	{
		Width.Set(0, 1f);
		Height.Set(200f, 0f);

		// Search bar background panel
		_searchBarPanel = new UIPanel();
		_searchBarPanel.Width.Set(0, 1f);
		_searchBarPanel.Height.Set(36f, 0f);
		_searchBarPanel.BackgroundColor = new Color(35, 40, 83);
		_searchBarPanel.BorderColor = new Color(60, 70, 130);
		_searchBarPanel.SetPadding(0);
		Append(_searchBarPanel);

		// Terraria's built-in search bar — handles all input, focus, IME properly
		_searchBar = new UISearchBar(Language.GetText("Mods.ProgressionCompass.UI.SearchPlaceholder"), 0.8f);
		_searchBar.Width.Set(0, 1f);
		_searchBar.Height.Set(36f, 0f);
		_searchBar.HAlign = 0f;
		_searchBar.VAlign = 0.5f;
		_searchBar.OnContentsChanged += OnSearchContentsChanged;
		_searchBar.OnStartTakingInput += OnStartSearch;
		_searchBar.OnEndTakingInput += OnEndSearch;
		_searchBarPanel.Append(_searchBar);

		_resultsList = new UIList();
		_resultsList.Top.Set(42f, 0f);
		_resultsList.Width.Set(-20f, 1f);
		_resultsList.Height.Set(-42f, 1f);
		_resultsList.ListPadding = 2f;
		_resultsList.ManualSortMethod = _ => { };
		Append(_resultsList);

		_scrollbar = new UIScrollbar();
		_scrollbar.Top.Set(42f, 0f);
		_scrollbar.Left.Set(-18f, 1f);
		_scrollbar.Height.Set(-42f, 1f);
		_resultsList.SetScrollbar(_scrollbar);
		Append(_scrollbar);
	}

	private void OnSearchContentsChanged(string contents)
	{
		_currentSearch = contents;
		FilterItems(contents);
	}

	private void OnStartSearch()
	{
		_searchBar.SetContents("", true);
		_searchBarPanel.BorderColor = new Color(100, 120, 200);
	}

	private void OnEndSearch()
	{
		_searchBarPanel.BorderColor = new Color(60, 70, 130);
	}

	public override void LeftClick(UIMouseEvent evt)
	{
		base.LeftClick(evt);
		var dims = GetDimensions();

		// Clear button (X) at right edge of search bar
		if (!string.IsNullOrEmpty(_currentSearch))
		{
			float clearX = dims.X + dims.Width - 24f;
			if (evt.MousePosition.X >= clearX && evt.MousePosition.Y < dims.Y + 36f)
			{
				ClearSearch();
				return;
			}
		}

		// If clicking on the search bar area, toggle input
		if (_searchBarPanel.ContainsPoint(evt.MousePosition))
		{
			_searchBar.ToggleTakingText();
		}
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();

		// Clear button (X) when search has text
		if (!string.IsNullOrEmpty(_currentSearch))
		{
			float clearX = dims.X + dims.Width - 24f;
			float clearY = dims.Y + 8f;
			bool hoverClear = Main.mouseX >= clearX && Main.mouseX < clearX + 20f
				&& Main.mouseY >= dims.Y && Main.mouseY < dims.Y + 36f;
			Utils.DrawBorderString(spriteBatch, "X",
				new Vector2(clearX + 4f, clearY),
				hoverClear ? new Color(255, 100, 100) : new Color(120, 120, 150), 0.65f);
		}

		// Result count (below search bar, above results)
		if (_totalMatches > 0 && !string.IsNullOrEmpty(_currentSearch))
		{
			var config = Terraria.ModLoader.ModContent.GetInstance<Common.CraftPathConfig>();
			int maxResults = config?.MaxSearchResults ?? _maxResults;

			string countText = _totalMatches <= maxResults
				? $"{_totalMatches} results"
				: $"{maxResults} of {_totalMatches} results";
			Utils.DrawBorderString(spriteBatch, countText,
				new Vector2(dims.X + 4f, dims.Y + 38f), new Color(100, 110, 150), 0.5f);
		}
	}

	private int _totalMatches;

	private void FilterItems(string searchText)
	{
		_resultsList.Clear();
		_totalMatches = 0;

		if (string.IsNullOrWhiteSpace(searchText) || searchText.Length < 2)
		{
			if (!string.IsNullOrEmpty(searchText))
				_resultsList.Add(new UIEmptyMessage("Type at least 2 characters to search."));
			return;
		}

		string search = searchText.ToLowerInvariant();
		int count = 0;

		var config = Terraria.ModLoader.ModContent.GetInstance<Common.CraftPathConfig>();
		int maxResults = config?.MaxSearchResults ?? _maxResults;

		// Count total matches first for display
		int totalFound = 0;
		foreach (var (type, _) in ContentSamples.ItemsByType)
		{
			if (type <= ItemID.None || type >= TextureAssets.Item.Length) continue;
			string name = Lang.GetItemNameValue(type);
			if (!string.IsNullOrEmpty(name) && name.ToLowerInvariant().Contains(search))
				totalFound++;
		}
		_totalMatches = totalFound;

		foreach (var (type, item) in ContentSamples.ItemsByType)
		{
			if (type <= ItemID.None || type >= TextureAssets.Item.Length)
				continue;

			string name = Lang.GetItemNameValue(type);
			if (string.IsNullOrEmpty(name))
				continue;

			if (!name.ToLowerInvariant().Contains(search))
				continue;

			try
			{
				var slot = new UICraftItemSlot(type, showName: true);
				slot.Width.Set(0, 1f);
				slot.Height.Set(36f, 0f);
				slot.ShowPinButton = true;
				slot.IsPinned = IsItemTracked?.Invoke(type) ?? false;
				slot.OnSelected += HandleItemSelected;
				slot.OnPinClicked += HandleItemPinned;
				_resultsList.Add(slot);
			}
			catch
			{
				continue;
			}

			count++;
			if (count >= maxResults)
				break;
		}

		if (count == 0)
			_resultsList.Add(new UIEmptyMessage($"No items matching \"{searchText}\"."));
	}

	/// <summary>Clears the search bar and results.</summary>
	public void ClearSearch()
	{
		_searchBar.SetContents("", true);
		if (_searchBar.IsWritingText)
			_searchBar.ToggleTakingText();
		_resultsList.Clear();
		_currentSearch = "";
		_totalMatches = 0;
	}

	private void HandleItemSelected(int itemType)
	{
		OnItemSelected?.Invoke(itemType);
		_searchBar.SetContents("", true);
		if (_searchBar.IsWritingText)
			_searchBar.ToggleTakingText();
		_resultsList.Clear();
	}

	private void HandleItemPinned(int itemType)
	{
		OnItemPinned?.Invoke(itemType);
		// Refresh pin states on visible results
		foreach (var el in _resultsList._items)
		{
			if (el is UICraftItemSlot slot)
				slot.IsPinned = IsItemTracked?.Invoke(slot.ItemType) ?? false;
		}
	}
}
