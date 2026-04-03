using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using ProgressionCompass.Content.Progression;

namespace ProgressionCompass.UI.Elements;

/// <summary>
/// Unified row for displaying an upgrade item across dashboard and upgrade window.
/// Combines rarity bar, icon, name, improvement text, progress bar, milestones,
/// missing items, and pin button into one consistent element.
/// </summary>
public class UIUpgradeItemRow : UIElement
{
    private readonly UpgradeItem _item;
    private readonly Action<int> _onAnalyze;
    private readonly Action<int> _onPin;

    private readonly int _milestoneCount;
    private readonly int _missingCount;

    public UIUpgradeItemRow(UpgradeItem item, Action<int> onAnalyze, Action<int> onPin = null)
    {
        _item = item;
        _onAnalyze = onAnalyze;
        _onPin = onPin;

        Width.Set(0, 1f);

        // Dynamic height: base + progress bar + milestones + missing items
        _milestoneCount = Math.Min(item.ReadyMilestones?.Count ?? 0, 3);
        _missingCount = Math.Min(item.MissingItems?.Count ?? 0, 4);

        float h = 28f;  // Name line
        h += 12f;       // Progress bar (always shown)
        h += _milestoneCount * 16f;
        h += _missingCount * 14f;
        Height.Set(h, 0f);
    }

    public override void LeftClick(UIMouseEvent evt)
    {
        base.LeftClick(evt);
        var dims = GetDimensions();

        // Check if pin button was clicked (right-aligned area)
        if (_onPin != null)
        {
            float pinX = dims.X + dims.Width - 44f;
            if (evt.MousePosition.X >= pinX)
            {
                _onPin.Invoke(_item.ItemType);
                return;
            }
        }

        _onAnalyze?.Invoke(_item.ItemType);
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        var dims = GetDimensions();
        float x = dims.X;
        float y = dims.Y;
        bool hovering = IsMouseHovering;

        if (hovering)
        {
            string suffix = _item.ImprovementText != null ? $"\n{_item.ImprovementText}" : null;
            UIHelper.ShowItemTooltip(_item.ItemType, suffix);
        }

        // ── Background with proximity tint ───────────────────────
        Color bg;
        if (_item.Proximity == Proximity.CraftableNow)
            bg = hovering ? new Color(35, 65, 35) * 0.5f : new Color(25, 50, 25) * 0.35f;
        else if (_item.Proximity == Proximity.AlmostReady)
            bg = hovering ? new Color(55, 55, 30) * 0.5f : new Color(40, 40, 20) * 0.35f;
        else
            bg = hovering ? new Color(45, 52, 100) * 0.4f : new Color(30, 35, 70) * 0.25f;
        spriteBatch.Draw(TextureAssets.MagicPixel.Value, dims.ToRectangle(), bg);

        // ── Rarity bar (3px left edge) ──────────────────────────
        var tempItem = new Item();
        if (_item.ItemType > ItemID.None) tempItem.SetDefaults(_item.ItemType);
        Color rarityColor = UIHelper.GetRarityColor(tempItem.rare);
        spriteBatch.Draw(TextureAssets.MagicPixel.Value,
            new Rectangle((int)x, (int)y + 2, 3, (int)dims.Height - 4), rarityColor * 0.8f);

        // ── Item icon ───────────────────────────────────────────
        UIHelper.DrawItemIcon(spriteBatch, _item.ItemType, new Vector2(x + 8f, y + 2f), 22f);

        // ── Item name ───────────────────────────────────────────
        string name = tempItem.Name ?? "?";
        Utils.DrawBorderString(spriteBatch, name, new Vector2(x + 34f, y + 2f), Color.White, 0.65f);
        float nameW = FontAssets.MouseText.Value.MeasureString(name).X * 0.65f;

        // ── Improvement text ────────────────────────────────────
        if (!string.IsNullOrEmpty(_item.ImprovementText))
        {
            Color impColor = _item.Proximity == Proximity.CraftableNow
                ? new Color(100, 220, 100) : new Color(150, 220, 150);
            Utils.DrawBorderString(spriteBatch, _item.ImprovementText,
                new Vector2(x + 38f + nameW, y + 3f), impColor, 0.5f);
        }

        // ── Pin button (right-aligned) ──────────────────────────
        if (_onPin != null)
            DrawPinButton(spriteBatch, dims, y);

        // ── Badge (CRAFT or fraction, left of pin) ──────────────
        DrawBadge(spriteBatch, dims, y);

        // ── Progress bar ────────────────────────────────────────
        float barY = y + 26f;
        DrawProgressBar(spriteBatch, x, barY, dims.Width);

        // ── Milestones ──────────────────────────────────────────
        float contentY = barY + 12f;
        if (_milestoneCount > 0)
            contentY = DrawMilestones(spriteBatch, x, contentY);

        // ── Missing items ───────────────────────────────────────
        if (_missingCount > 0)
            DrawMissingItems(spriteBatch, x, contentY, dims.Width);
    }

    private void DrawPinButton(SpriteBatch spriteBatch, CalculatedStyle dims, float y)
    {
        float pinX = dims.X + dims.Width - 44f;

        if (_item.IsTracked)
        {
            spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                new Rectangle((int)pinX, (int)(y + 3f), 40, 18), new Color(35, 70, 35));
            Utils.DrawBorderString(spriteBatch, "Pinned",
                new Vector2(pinX + 3f, y + 4f), new Color(80, 220, 80), 0.45f);
        }
        else
        {
            bool hoverPin = Main.mouseX >= pinX && Main.mouseX < pinX + 40
                && Main.mouseY >= y && Main.mouseY < y + 26;
            Utils.DrawBorderString(spriteBatch, "Pin",
                new Vector2(pinX + 8f, y + 5f),
                hoverPin ? Color.Green : Color.Gray, 0.55f);
        }
    }

    private void DrawBadge(SpriteBatch spriteBatch, CalculatedStyle dims, float y)
    {
        float rightEdge = _onPin != null ? dims.X + dims.Width - 48f : dims.X + dims.Width - 6f;

        string badge;
        Color badgeColor;

        if (_item.Proximity == Proximity.CraftableNow)
        {
            badge = "CRAFT";
            badgeColor = new Color(80, 220, 80);
        }
        else if (_item.LeafMaterialsTotal > 0)
        {
            badge = $"{_item.LeafMaterialsOwned}/{_item.LeafMaterialsTotal}";
            badgeColor = _item.Proximity == Proximity.AlmostReady
                ? new Color(220, 200, 80) : Color.LightGray;
        }
        else
            return;

        float badgeW = FontAssets.MouseText.Value.MeasureString(badge).X * 0.5f;
        Utils.DrawBorderString(spriteBatch, badge,
            new Vector2(rightEdge - badgeW, y + 4f), badgeColor, 0.5f);
    }

    private void DrawProgressBar(SpriteBatch spriteBatch, float x, float barY, float totalWidth)
    {
        float barX = x + 10f;
        float barW = totalWidth - 20f;
        float pct = _item.LeafMaterialsTotal > 0
            ? (float)_item.LeafMaterialsOwned / _item.LeafMaterialsTotal
            : 0f;

        // Background
        spriteBatch.Draw(TextureAssets.MagicPixel.Value,
            new Rectangle((int)barX, (int)barY, (int)barW, 6), new Color(15, 18, 35));

        // Fill
        Color barColor = pct >= 1f ? new Color(50, 180, 50)
            : pct >= 0.8f ? new Color(100, 180, 50)
            : pct > 0.5f ? new Color(200, 180, 50)
            : new Color(180, 80, 50);
        int fillW = (int)(barW * pct);
        if (fillW > 0)
            spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                new Rectangle((int)barX, (int)barY, fillW, 6), barColor);
    }

    private float DrawMilestones(SpriteBatch spriteBatch, float x, float startY)
    {
        float msY = startY;
        int shown = 0;
        foreach (var ms in _item.ReadyMilestones)
        {
            if (shown >= 3) break;

            UIHelper.DrawItemIcon(spriteBatch, ms.ItemType, new Vector2(x + 30f, msY), 14f);

            var msItem = new Item();
            if (ms.ItemType > ItemID.None) msItem.SetDefaults(ms.ItemType);

            string msLabel = ms.IsReady ? "BUILD NOW" : $"{(int)ms.CompletionPercent}%";
            Color msColor = ms.IsReady ? new Color(80, 255, 80) : new Color(220, 200, 80);
            Utils.DrawBorderString(spriteBatch, $"{msItem.Name} \u2014 {msLabel}",
                new Vector2(x + 48f, msY), msColor, 0.5f);

            msY += 16f;
            shown++;
        }
        return msY;
    }

    private void DrawMissingItems(SpriteBatch spriteBatch, float x, float startY, float totalWidth)
    {
        float missingY = startY;
        int shown = 0;
        foreach (var (missingType, missingQty) in _item.MissingItems)
        {
            if (shown >= 4) break;

            UIHelper.DrawItemIcon(spriteBatch, missingType, new Vector2(x + 30f, missingY), 12f);

            var missingItem = new Item();
            if (missingType > ItemID.None) missingItem.SetDefaults(missingType);

            string text = missingQty > 1 ? $"{missingItem.Name} x{missingQty}" : missingItem.Name ?? "?";
            if (text.Length > 20) text = text[..19] + "..";
            Utils.DrawBorderString(spriteBatch, text,
                new Vector2(x + 46f, missingY), new Color(255, 150, 150), 0.5f);

            // Show best source for this missing item
            var sourceSystem = Terraria.ModLoader.ModContent.GetInstance<Common.Systems.ItemSourceSystem>();
            var sources = sourceSystem?.GetSources(missingType);
            if (sources != null && sources.Count > 0)
            {
                var best = Content.Graph.ItemSource.PickBest(sources);
                float textW = FontAssets.MouseText.Value.MeasureString(text).X * 0.5f;
                string srcDesc = best.Description;
                if (srcDesc.Length > 22) srcDesc = srcDesc[..21] + "..";
                Color srcColor = UIHelper.GetSourceColor(best.Type);
                Utils.DrawBorderString(spriteBatch, srcDesc,
                    new Vector2(x + 50f + textW, missingY), srcColor, 0.5f);
            }

            missingY += 14f;
            shown++;
        }
    }
}
