using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Opt-in landing surface for sloped colliders near or past the landing gate (PHYSICS.md):
/// a descending piece only STOPS on a surface `IsValidLandingSupport` accepts, so a slope
/// past ~37 deg (normal.y &lt; landingSupportNormalY 0.7) is otherwise fallen straight
/// through - there is no other stop. Colliders carrying this component (the Pyramid's
/// ~42-44 deg faces, normals ~0.71/0.75 - too close to the 0.7 gate to trust unmarked)
/// accept landings down to a 0.3 normal.y sanity floor; the piece locks, goes Dynamic,
/// and gravity/friction slide it off - which IS the intended behaviour.
///
/// Static registry instead of GetComponent per cast hit: the landing cast runs every
/// FixedUpdate on the hot path (PHYSICS.md: no per-step lookups/allocs).
/// </summary>
public sealed class LandableSlope : MonoBehaviour
{
    private static readonly HashSet<Collider2D> Surfaces = new HashSet<Collider2D>();

    public static bool Covers(Collider2D collider) =>
        collider != null && Surfaces.Contains(collider);

    private void OnEnable()
    {
        foreach (Collider2D collider in GetComponents<Collider2D>()) Surfaces.Add(collider);
    }

    private void OnDisable()
    {
        foreach (Collider2D collider in GetComponents<Collider2D>()) Surfaces.Remove(collider);
    }
}
