using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ModLoader;
using Terraria.UI;
using CraftPathOptimizer.Common.Systems;

namespace CraftPathOptimizer.UI.Elements;

/// <summary>
/// A UIPanel that can be dragged by its header and resized from the bottom-right corner.
/// </summary>
public class UIDraggablePanel : UIPanel
{
	private const float DragZoneHeight = 24f;
	private const float ResizeZone = 16f;
	private const float PanelMinW = 450f;
	private const float PanelMinH = 400f;
	private const float PanelMaxW = 1200f;
	private const float PanelMaxH = 900f;

	private bool _dragging;
	private bool _resizing;
	private Vector2 _dragOffset;
	private Vector2 _resizeStart;
	private Vector2 _sizeAtResizeStart;

	public override void LeftMouseDown(UIMouseEvent evt)
	{
		// Don't process drag/resize if the upgrade window is on top
		var upgradeSystem = ModContent.GetInstance<UpgradeWindowSystem>();
		if (upgradeSystem?.IsMouseBlocked == true)
			return;

		base.LeftMouseDown(evt);
		var dims = GetDimensions();

		// Check resize zone (bottom-right corner)
		float relX = evt.MousePosition.X - dims.X;
		float relY = evt.MousePosition.Y - dims.Y;
		if (relX >= dims.Width - ResizeZone && relY >= dims.Height - ResizeZone)
		{
			_resizing = true;
			_resizeStart = evt.MousePosition;
			_sizeAtResizeStart = new Vector2(Width.Pixels, Height.Pixels);
			return;
		}

		// Check drag zone (header area)
		if (evt.MousePosition.Y - dims.Y < DragZoneHeight)
		{
			_dragging = true;
			_dragOffset = new Vector2(evt.MousePosition.X - Left.Pixels, evt.MousePosition.Y - Top.Pixels);
		}
	}

	public override void LeftMouseUp(UIMouseEvent evt)
	{
		base.LeftMouseUp(evt);
		_dragging = false;
		_resizing = false;
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);

		if (_dragging)
		{
			Left.Set(Main.mouseX - _dragOffset.X, 0f);
			Top.Set(Main.mouseY - _dragOffset.Y, 0f);
			Recalculate();
		}

		if (_resizing)
		{
			float dx = Main.mouseX - _resizeStart.X;
			float dy = Main.mouseY - _resizeStart.Y;

			float newW = MathHelper.Clamp(_sizeAtResizeStart.X + dx, PanelMinW, PanelMaxW);
			float newH = MathHelper.Clamp(_sizeAtResizeStart.Y + dy, PanelMinH, PanelMaxH);

			Width.Set(newW, 0f);
			Height.Set(newH, 0f);

			// Switch from centered (HAlign/VAlign) to absolute positioning on first resize
			if (HAlign != 0f || VAlign != 0f)
			{
				var dims = GetDimensions();
				Left.Set(dims.X, 0f);
				Top.Set(dims.Y, 0f);
				HAlign = 0f;
				VAlign = 0f;
			}

			Recalculate();
		}

		if (ContainsPoint(Main.MouseScreen))
			Main.LocalPlayer.mouseInterface = true;
	}

	protected override void DrawChildren(SpriteBatch spriteBatch)
	{
		base.DrawChildren(spriteBatch);

		// Draw resize grip in bottom-right corner
		var dims = GetDimensions();
		float gx = dims.X + dims.Width - 14f;
		float gy = dims.Y + dims.Height - 14f;

		Color gripColor = new Color(80, 90, 150) * 0.7f;

		// Three diagonal lines (classic resize grip pattern)
		for (int i = 0; i < 3; i++)
		{
			float offset = i * 4f;
			spriteBatch.Draw(TextureAssets.MagicPixel.Value,
				new Rectangle((int)(gx + offset), (int)(gy + 10 - offset), 1, (int)(offset + 2)),
				gripColor);
			spriteBatch.Draw(TextureAssets.MagicPixel.Value,
				new Rectangle((int)(gx + offset + 1), (int)(gy + 10 - offset), 1, (int)(offset + 2)),
				gripColor * 0.5f);
		}
	}
}
