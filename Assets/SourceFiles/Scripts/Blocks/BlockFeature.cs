using System;

/// <summary>
/// Run-local toggles an ability can switch on the active-piece controller without the controller
/// having to know which ability owns them. Each value is one independent bit; an ability flips its
/// bit in OnAcquired/OnRemoved (e.g. <c>BlockController.SetFeature(BlockFeature.VectorGuide, true)</c>)
/// and the placement/beam code reads it via <c>BlockController.HasFeature(...)</c>.
///
/// This replaces the old one-named-static-per-ability pattern (<c>_vectorGuideEnabled</c> + a
/// setter each): a new block-level toggle ability adds one enum value, not a field + setter +
/// reset in the shared controller. (EdgePortal held 1 &lt;&lt; 1 until its removal, Nick 2026-08-01.)
/// </summary>
[Flags]
public enum BlockFeature
{
    None = 0,
    /// <summary>Vector Guide ability: a translucent landing-ghost preview on the placement beam.</summary>
    VectorGuide = 1 << 0,
}
