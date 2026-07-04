using System.Collections;
using UnityEngine;

/// <summary>
/// The "saved!" animation for the Rebound ability. Takes over a block that was about to be
/// lost off the bottom (already removed from the board + accounting), neutralises it so it
/// stops falling and can't touch the tower, then beams it up: a brief hover, a soft rising
/// lift on a light beam, a dissolve into a per-cell magic burst, and gone. Purely cosmetic -
/// the block is no longer a gameplay object, so moving its transform here is fine (PHYSICS.md
/// I1 only protects LIVE landed blocks). Self-destroys when the lift finishes.
/// </summary>
public sealed class RescueLift : MonoBehaviour
{
    private const float HoldTime = 0.08f;     // a beat of "caught" before the lift
    private const float RiseTime = 0.42f;
    private const float RiseDistance = 2.6f;  // brings a block lost just below the screen back into view
    private const float BurstAtFraction = 0.72f;

    private static readonly Color BeamColor = new Color(0.45f, 0.85f, 1f, 1f); // cool rescue cyan

    private GameObject _burstEffect;
    private float _burstScale;
    private Vector3[] _cellLocalCenters;
    private float _cellSize = 1f;
    private SpriteRenderer[] _renderers;
    private SpriteRenderer _beam;

    /// <summary>Begin the rescue beam-up on a block. The block must already be removed from
    /// the board's accounting by the caller; this only handles the visuals + final destroy.</summary>
    public static void Begin(BlockController block, GameObject burstEffect, float burstScale)
    {
        if (block == null) return;

        RescueLift lift = block.gameObject.AddComponent<RescueLift>();
        lift._burstEffect = burstEffect;
        lift._burstScale = Mathf.Max(0.1f, burstScale);

        // Snapshot cell centres (LOCAL) for the per-cell burst before colliders go away, so
        // the burst lands on each cell at wherever the block has risen to.
        BoxCollider2D[] cells = block.GetComponentsInChildren<BoxCollider2D>();
        lift._cellLocalCenters = new Vector3[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            lift._cellLocalCenters[i] = block.transform.InverseTransformPoint(cells[i].bounds.center);
            lift._cellSize = Mathf.Max(lift._cellSize, Mathf.Max(cells[i].bounds.size.x, cells[i].bounds.size.y));
        }

        // Neutralise: stop physics so it can't fall or shove the tower, and stop the
        // controller's own logic. Transform is then ours to animate.
        if (block.TryGetComponent(out Rigidbody2D rb))
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = false;
        }
        block.enabled = false;
        // Stop counting it as a tower block NOW (not at Destroy), so the ~0.5s rise can't be
        // seen by AllBlocks sweeps - win-height verification, camera focus, Sacrifice's
        // topmost pick - as a live landed block that's already leaving the board.
        block.DetachFromTracking();

        lift._renderers = block.GetComponentsInChildren<SpriteRenderer>();
        lift._beam = lift.CreateBeam(block.transform);
        lift.StartCoroutine(lift.Run());
    }

    private SpriteRenderer CreateBeam(Transform parent)
    {
        GameObject go = new GameObject("RescueBeam");
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // horizontal soft bar -> vertical beam
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = RuntimeSprites.SoftHorizontalBar(0.5f);
        sr.color = new Color(BeamColor.r, BeamColor.g, BeamColor.b, 0f);
        sr.sortingOrder = -1; // just behind the block's cells
        // Stretch into a tall, narrow beam (bar's local X becomes vertical after the 90 rotation).
        sr.transform.localScale = new Vector3((RiseDistance + 1.5f) / sr.sprite.bounds.size.x, 0.6f, 1f);
        return sr;
    }

    private IEnumerator Run()
    {
        yield return new WaitForSeconds(HoldTime);

        Vector3 start = transform.position;
        bool bursted = false;
        for (float t = 0f; t < RiseTime; t += Time.deltaTime)
        {
            float u = Mathf.Clamp01(t / RiseTime);
            float eased = 1f - (1f - u) * (1f - u); // ease-out: quick lift, gentle settle
            transform.position = start + Vector3.up * (RiseDistance * eased);

            float bodyAlpha = 1f - Mathf.Clamp01((u - 0.45f) / 0.55f); // fade out over the back half
            SetBodyAlpha(bodyAlpha);
            if (_beam != null)
            {
                // beam blooms in, then fades with the body
                float beamAlpha = 0.55f * Mathf.Sin(u * Mathf.PI);
                _beam.color = new Color(BeamColor.r, BeamColor.g, BeamColor.b, beamAlpha);
            }

            if (!bursted && u >= BurstAtFraction)
            {
                bursted = true;
                Poof();
            }
            yield return null;
        }

        Destroy(gameObject);
    }

    private void Poof()
    {
        if (_cellLocalCenters != null && _cellLocalCenters.Length > 0)
        {
            foreach (Vector3 local in _cellLocalCenters)
            {
                Vfx.Spawn(_burstEffect, transform.TransformPoint(local), _cellSize * _burstScale);
            }
        }
        else
        {
            Vfx.Spawn(_burstEffect, transform.position, _cellSize * _burstScale);
        }
        SfxPlayer.Play("rescue_beam", 0.75f, 0.04f);
    }

    private void SetBodyAlpha(float a)
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            SpriteRenderer sr = _renderers[i];
            if (sr == null || sr == _beam) continue;
            Color c = sr.color;
            c.a = a;
            sr.color = c;
        }
    }
}
