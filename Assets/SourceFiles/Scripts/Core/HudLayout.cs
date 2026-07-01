using System;
using UnityEngine;

/// <summary>
/// Player-customizable HUD layout (the Controls tab), persisted by SettingsService as JSON. The
/// in-game HUD (UIManager nudge guides, AbilityHud consumable slots) and the layout editor all
/// read/write this one object. Slot positions are normalized within the safe area so a layout
/// authored on one device maps onto any other; sizes are in 1080x1920 reference px (the canvas
/// scales them to the device). See SETTINGS.md / GRAPHICS.md siblings.
/// </summary>
[Serializable]
public class HudLayout
{
    // Consumable slot count is fixed at 2 (AbilityRuntime.ConsumableSlotCount); kept literal here
    // so the data model stays free of an Abilities dependency.
    public const int SlotCount = 2;

    // Defensive clamps for persisted data (the editor authors within a tighter range).
    private const float MinSlotSize = 60f;
    private const float MaxSlotSize = 260f;

    public float nudgeGuideOpacity;   // 0..1, visual-only opacity of the nudge corner guides
    public bool slotsLinked;          // editor mode: move/resize the two slots together
    public SlotLayout[] slots;        // one per consumable slot

    [Serializable]
    public class SlotLayout
    {
        public float x;     // normalized position within the safe area [0..1]
        public float y;     // normalized position within the safe area [0..1]
        public float size;  // reference px (canvas-scaled)
    }

    public static HudLayout CreateDefault()
    {
        return new HudLayout
        {
            nudgeGuideOpacity = 1f,
            slotsLinked = true,
            // Calibrated to AbilityHud's prior constants on a no-notch 1080x1920 screen: right
            // edge (x = (1080-92)/1080), stacked +-71px around 58% height ((0.58*1920 +- 71)/1920).
            slots = new[]
            {
                new SlotLayout { x = 0.915f, y = 0.617f, size = 124f },
                new SlotLayout { x = 0.915f, y = 0.543f, size = 124f },
            },
        };
    }

    /// <summary>Deep copy (the editor edits a draft, then commits via SettingsService).</summary>
    public HudLayout Clone() => FromJsonOrDefault(JsonUtility.ToJson(this));

    /// <summary>Parse stored JSON, falling back to defaults on empty/garbled/short data so
    /// consumers never null-deref or read a stale schema.</summary>
    public static HudLayout FromJsonOrDefault(string json)
    {
        if (string.IsNullOrEmpty(json)) return CreateDefault();

        HudLayout layout = null;
        try { layout = JsonUtility.FromJson<HudLayout>(json); }
        catch { /* malformed - fall through to default */ }

        if (layout == null || layout.slots == null || layout.slots.Length < SlotCount) return CreateDefault();
        layout.nudgeGuideOpacity = Mathf.Clamp01(layout.nudgeGuideOpacity);
        for (int i = 0; i < layout.slots.Length; i++)
        {
            SlotLayout s = layout.slots[i];
            if (s == null) return CreateDefault(); // malformed element
            s.x = Mathf.Clamp01(s.x);
            s.y = Mathf.Clamp01(s.y);
            s.size = Mathf.Clamp(s.size, MinSlotSize, MaxSlotSize);
        }
        return layout;
    }
}
