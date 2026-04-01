using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using ProgressionCompass.Common.Players;
using ProgressionCompass.Content.Tracking;
using ProgressionCompass.UI;

namespace ProgressionCompass.Common.Systems;

public class TrackingOverlaySystem : ModSystem
{
	private UserInterface _overlayInterface;
	private TrackingOverlayState _overlayState;
	private GameTime _lastGameTime;
	private readonly List<ToastEntry> _toasts = [];

	public override void Load()
	{
		if (Main.dedServ) return;
		_overlayInterface = new UserInterface();
		_overlayState = new TrackingOverlayState(this);
		_overlayState.Activate();
		_overlayInterface.SetState(_overlayState);
	}

	public override void Unload()
	{
		_overlayState = null;
		_overlayInterface = null;
	}

	public override void UpdateUI(GameTime gameTime)
	{
		_lastGameTime = gameTime;
		_overlayInterface?.Update(gameTime);

		for (int i = _toasts.Count - 1; i >= 0; i--)
		{
			_toasts[i].TimeLeft--;
			if (_toasts[i].TimeLeft <= 0)
				_toasts.RemoveAt(i);
		}
	}

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		var uiSystem = ModContent.GetInstance<CraftPathUISystem>();
		bool mainPanelOpen = uiSystem?.IsVisible == true;

		int index = layers.FindIndex(l => l.Name.Equals("Vanilla: Resource Bars"));
		if (index != -1)
		{
			layers.Insert(index + 1, new LegacyGameInterfaceLayer(
				"ProgressionCompass: Tracking Overlay",
				delegate
				{
					if (!mainPanelOpen)
						_overlayInterface?.Draw(Main.spriteBatch, _lastGameTime);
					DrawToasts(Main.spriteBatch);
					return true;
				},
				InterfaceScaleType.UI));
		}
	}

	public void QueueToast(int itemType, int count)
	{
		_toasts.Add(new ToastEntry { ItemType = itemType, Count = count, TimeLeft = 120 });
	}

	private void DrawToasts(SpriteBatch spriteBatch)
	{
		if (_toasts.Count == 0) return;
		float y = Main.screenHeight / 2f - _toasts.Count * 12f;
		foreach (var toast in _toasts)
		{
			float alpha = Math.Min(1f, toast.TimeLeft / 30f);
			var item = new Item();
			if (toast.ItemType > ItemID.None) item.SetDefaults(toast.ItemType);
			string text = $"+{toast.Count} {item.Name}";
			Utils.DrawBorderString(spriteBatch, text, new Vector2(Main.screenWidth - 260f, y), Color.White * alpha, 0.75f);
			y += 24f;
		}
	}

	public class ToastEntry
	{
		public int ItemType;
		public int Count;
		public int TimeLeft;
	}
}

/// <summary>
/// Compact HUD overlay with dragging support and detailed next-action info.
/// </summary>
public class TrackingOverlayState : UIState
{
	private readonly TrackingOverlaySystem _system;
	private const float PanelW = 240f;

	// Dragging
	private static float _posX = -1;
	private static float _posY = -1;
	private bool _dragging;
	private Vector2 _dragOffset;

	// Flash effect on progress change
	private readonly Dictionary<int, float> _lastProgress = [];
	private readonly Dictionary<int, int> _flashTimers = [];
	private const int FlashDuration = 30;

	public TrackingOverlayState(TrackingOverlaySystem system)
	{
		_system = system;
	}

	public override void LeftMouseDown(UIMouseEvent evt)
	{
		base.LeftMouseDown(evt);
		var pos = GetPanelPos();
		var rect = new Rectangle((int)pos.X, (int)pos.Y, (int)PanelW, 22);
		if (rect.Contains(evt.MousePosition.ToPoint()))
		{
			_dragging = true;
			_dragOffset = evt.MousePosition - pos;
		}
	}

	public override void LeftMouseUp(UIMouseEvent evt)
	{
		base.LeftMouseUp(evt);
		_dragging = false;
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		if (_dragging)
		{
			_posX = Main.mouseX - _dragOffset.X;
			_posY = Main.mouseY - _dragOffset.Y;
		}

		// Block game input when hovering the overlay
		var pos = GetPanelPos();
		if (Main.mouseX >= pos.X && Main.mouseX < pos.X + PanelW
			&& Main.mouseY >= pos.Y && Main.mouseY < pos.Y + 300)
			Main.LocalPlayer.mouseInterface = true;

		// Detect progress changes for flash effect
		TrackingPlayer tp;
		try { tp = Main.LocalPlayer?.GetModPlayer<TrackingPlayer>(); } catch { tp = null; }
		if (tp?.Tracking != null)
		{
			foreach (var goal in tp.Tracking.Goals)
			{
				float pct = goal.ProgressPercent;
				if (_lastProgress.TryGetValue(goal.ItemType, out float last) && Math.Abs(pct - last) > 0.1f)
					_flashTimers[goal.ItemType] = FlashDuration;
				_lastProgress[goal.ItemType] = pct;
			}
		}

		// Decay flash timers
		var keys = new List<int>(_flashTimers.Keys);
		foreach (int key in keys)
		{
			_flashTimers[key]--;
			if (_flashTimers[key] <= 0)
				_flashTimers.Remove(key);
		}
	}

	private Vector2 GetPanelPos()
	{
		if (_posX < 0)
		{
			// Default: center-right edge
			_posX = Main.screenWidth - PanelW - 16f;
			_posY = Main.screenHeight / 2f - 100f;
		}
		return new Vector2(_posX, _posY);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		TrackingPlayer player;
		try { player = Main.LocalPlayer?.GetModPlayer<TrackingPlayer>(); }
		catch { return; }
		if (player?.Tracking == null || player.Tracking.Count == 0)
			return;

		var tracking = player.Tracking;
		var pos = GetPanelPos();
		float px = pos.X;
		float y = pos.Y;

		// Pre-calculate total height for background
		int goalCount = Math.Min(tracking.Goals.Count, 5);
		float totalH = 26f + goalCount * 26f + 6f;

		// Action sections height (show up to 3 action groups)
		int maxActions = Math.Min(tracking.ActionPlan.Count, 3);
		for (int a = 0; a < maxActions; a++)
		{
			var action = tracking.ActionPlan[a];
			totalH += 24f; // header
			totalH += Math.Min(action.Items.Count, 4) * 18f;
			totalH += 8f; // spacing
		}

		// Background
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)px, (int)y, (int)PanelW, (int)totalH),
			new Color(18, 22, 44) * 0.88f);

		// Border
		UIHelper.DrawBorder(spriteBatch, new Rectangle((int)px, (int)y, (int)PanelW, (int)totalH),
			new Color(55, 65, 120) * 0.7f);

		// Header (drag handle)
		spriteBatch.Draw(TextureAssets.MagicPixel.Value,
			new Rectangle((int)px, (int)y, (int)PanelW, 22), new Color(30, 35, 65));
		Utils.DrawBorderString(spriteBatch, "Craft Goals", new Vector2(px + 6f, y + 2f), new Color(180, 190, 220), 0.65f);

		// Drag hint dots
		for (int d = 0; d < 3; d++)
			spriteBatch.Draw(TextureAssets.MagicPixel.Value,
				new Rectangle((int)(px + PanelW - 16 + d * 4), (int)(y + 9), 2, 2), Color.Gray * 0.5f);

		y += 24f;

		// Goal rows
		for (int i = 0; i < goalCount; i++)
		{
			var goal = tracking.Goals[i];
			float pct = goal.ProgressPercent / 100f;
			bool isReady = pct >= 1f;

			// Ready-to-craft highlight
			if (isReady)
				spriteBatch.Draw(TextureAssets.MagicPixel.Value,
					new Rectangle((int)px + 2, (int)y, (int)PanelW - 4, 22),
					new Color(30, 70, 30) * 0.5f);

			UIHelper.DrawItemIcon(spriteBatch, goal.ItemType, new Vector2(px + 6f, y + 2f), 16f);

			// Name
			var item = new Item();
			if (goal.ItemType > ItemID.None) item.SetDefaults(goal.ItemType);
			string name = item.Name ?? "?";

			if (isReady)
			{
				// Show "READY!" badge instead of progress bar
				if (name.Length > 12) name = name[..11] + "..";
				Utils.DrawBorderString(spriteBatch, name, new Vector2(px + 24f, y + 1f), Color.White, 0.58f);
				Utils.DrawBorderString(spriteBatch, "CRAFT!",
					new Vector2(px + PanelW - 52f, y + 1f), new Color(80, 255, 80), 0.6f);
			}
			else
			{
				if (name.Length > 16) name = name[..15] + "..";
				Utils.DrawBorderString(spriteBatch, name, new Vector2(px + 24f, y + 1f), Color.White, 0.58f);

				// Progress bar (inline, right side)
				float barX = px + PanelW - 70f;
				float barW = 44f;
				spriteBatch.Draw(TextureAssets.MagicPixel.Value,
					new Rectangle((int)barX, (int)(y + 4f), (int)barW, 8), new Color(15, 18, 35));
				Color barC = pct > 0.8f ? new Color(50, 180, 50) : pct > 0.4f ? new Color(200, 180, 50) : new Color(180, 50, 50);
				int fillW = (int)(barW * pct);
				if (fillW > 0)
					spriteBatch.Draw(TextureAssets.MagicPixel.Value,
						new Rectangle((int)barX, (int)(y + 4f), fillW, 8), barC);

				// Flash overlay on progress change
				if (_flashTimers.TryGetValue(goal.ItemType, out int flashTime) && flashTime > 0 && fillW > 0)
				{
					float flashAlpha = (float)flashTime / FlashDuration * 0.6f;
					spriteBatch.Draw(TextureAssets.MagicPixel.Value,
						new Rectangle((int)barX, (int)(y + 4f), fillW, 8), Color.White * flashAlpha);
				}

				// Fraction on bar
				string frac = $"{goal.OwnedMaterialCount}/{goal.TotalMaterialCount}";
				Utils.DrawBorderString(spriteBatch, frac, new Vector2(barX + 2f, y + 1f), Color.White, 0.45f);
			}

			if (Main.mouseX >= px && Main.mouseX < px + PanelW && Main.mouseY >= y && Main.mouseY < y + 24f)
				UIHelper.ShowItemTooltip(goal.ItemType);

			y += 24f;
		}

		// ── Action sections (show up to 3 groups) ────────────
		for (int a = 0; a < maxActions; a++)
		{
			var action = tracking.ActionPlan[a];

			y += 2f;
			spriteBatch.Draw(TextureAssets.MagicPixel.Value,
				new Rectangle((int)px + 4, (int)y, (int)PanelW - 8, 1), new Color(60, 70, 130) * 0.5f);
			y += 4f;

			Color actionColor = UIHelper.GetActionAccentColor(action.Type);
			Utils.DrawBorderString(spriteBatch, action.Heading, new Vector2(px + 6f, y), actionColor, 0.6f);
			y += 18f;

			int shown = 0;
			foreach (var actionItem in action.Items)
			{
				if (shown >= 4) break;

				UIHelper.DrawItemIcon(spriteBatch, actionItem.ItemType, new Vector2(px + 10f, y + 1f), 14f);

				var aItem = new Item();
				if (actionItem.ItemType > ItemID.None) aItem.SetDefaults(actionItem.ItemType);
				string aName = aItem.Name ?? "?";
				if (aName.Length > 13) aName = aName[..12] + "..";
				Utils.DrawBorderString(spriteBatch, aName, new Vector2(px + 28f, y + 1f), Color.White, 0.55f);

				// Equipped warning badge
				if (actionItem.IsEquipped)
				{
					float nW = FontAssets.MouseText.Value.MeasureString(aName).X * 0.55f;
					Utils.DrawBorderString(spriteBatch, "[E]", new Vector2(px + 28f + nW + 2f, y + 1f),
						new Color(255, 200, 60), 0.45f);
				}

				string detail = "";
				if (actionItem.DropRateText != null)
					detail = actionItem.DropRateText;
				else if (actionItem.QuantityNeeded > 1)
					detail = $"x{actionItem.QuantityNeeded}";

				if (!string.IsNullOrEmpty(detail))
				{
					float nameW = FontAssets.MouseText.Value.MeasureString(aName).X * 0.55f;
					Utils.DrawBorderString(spriteBatch, detail, new Vector2(px + 28f + nameW + 4f, y + 2f),
						Color.Gray, 0.48f);
				}

				string qty = $"{actionItem.QuantityOwned}/{actionItem.QuantityNeeded}";
				Color qC = actionItem.QuantityOwned >= actionItem.QuantityNeeded ? Color.Green : new Color(255, 150, 150);
				float qW = FontAssets.MouseText.Value.MeasureString(qty).X * 0.5f;
				Utils.DrawBorderString(spriteBatch, qty, new Vector2(px + PanelW - qW - 8f, y + 1f), qC, 0.5f);

				y += 18f;
				shown++;
			}
		}
	}
}
