using UnityEngine;

/// <summary>Three counted placements, then unconditional departure. A falling piece,
/// generated debris and the pyramid's own placement do not advance the engine.</summary>
public sealed class PyramidBlockBehaviour : MonoBehaviour
{
    public const int PlacementsBeforeLaunch = 3;
    public const float IgnitionSeconds = .45f;
    public int Charge { get; private set; }

    private BlockController _block;
    private PyramidBlockSkin _skin;
    private int _lastPlacement;
    private float _ignition;
    private bool _departed;

    public void Begin(BlockController block)
    {
        _block = block;
        _skin = block.GetComponent<PyramidBlockSkin>();
        _lastPlacement = GameManager.Instance != null ? GameManager.Instance.CurrentRunResult.TotalPlacedBlocks : 0;
        GameEvents.BlockPlaced += OnPlacement;
    }

    private bool Inert => _departed || _block == null || !_block.enabled
        || _block.GetComponent<VoidSuckFx>() != null
        || (GameManager.Instance != null && GameManager.Instance.isGameOver);

    private void OnPlacement(int total)
    {
        if (Inert || total <= _lastPlacement) return;
        _lastPlacement = total;
        // The ledger sets LastPlacedBlock before publishing BlockPlaced, including our
        // own lock. Unlike a frame latch, this still counts another placement that frame.
        if (GameManager.Instance != null && GameManager.Instance.LastPlacedBlock == _block) return;
        if (Charge >= PlacementsBeforeLaunch) return;
        Charge++;
        _skin?.SetCharge(Charge);
    }

    private void Update()
    {
        if (Inert || Charge < PlacementsBeforeLaunch) return;
        _ignition += Time.deltaTime;
        _skin?.SetIgnition(Mathf.Clamp01(_ignition / IgnitionSeconds));
        if (_ignition >= IgnitionSeconds) Depart();
    }

    private void Depart()
    {
        _departed = true;
        GameEvents.BlockPlaced -= OnPlacement;
        // Only a collider-free visual copy flies. The original body is never moved by
        // the effect, and leaves height/targeting/standing accounting at takeoff.
        _skin?.Launch();
        _block.enabled = false;
        foreach (var collider in _block.GetComponentsInChildren<Collider2D>()) collider.enabled = false;
        var body = _block.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.simulated = false;
            // A welded neighbour must be freed before the source body is destroyed.
            foreach (var other in BlockController.AllBlocks)
            {
                if (other == null) continue;
                foreach (var joint in other.GetComponents<Joint2D>())
                    if (other == _block || joint.connectedBody == body) Destroy(joint);
            }
        }
        _block.DetachFromTracking();
        GameEvents.RaiseBlockDestroyed(_block); // exactly one standing -1; no loss/life path
        BlockController.WakeDynamicLandedBlocks(_block);
        Destroy(_block.gameObject);
    }

    private void OnDestroy() => GameEvents.BlockPlaced -= OnPlacement;
}
