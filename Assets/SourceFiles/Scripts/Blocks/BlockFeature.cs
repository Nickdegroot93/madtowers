using System;

/// <summary>
/// Run-local toggles an ability can switch on the active-piece controller without the controller
/// having to know which ability owns them. Each value is one independent bit; an ability flips its
/// bit in OnAcquired/OnRemoved (e.g. <c>BlockController.SetFeature(BlockFeature.EdgePortal, true)</c>)
/// and the placement/beam code reads it via <c>BlockController.HasFeature(...)</c>.
///
/// This replaces the old one-named-static-per-ability pattern (<c>_vectorGuideEnabled</c> /
/// <c>_edgePortalEnabled</c> + a setter each): a new block-level toggle ability adds one enum value,
/// not a field + setter + reset in the shared controller.
/// </summary>
[Flags]
public enum BlockFeature
{
    None = 0,
    /// <summary>Vector Guide ability: a translucent landing-ghost preview on the placement beam.</summary>
    VectorGuide = 1 << 0,
    /// <summary>Edge Portal ability: active pieces wrap across the camera's horizontal edges.</summary>
    EdgePortal = 1 << 1,
}
