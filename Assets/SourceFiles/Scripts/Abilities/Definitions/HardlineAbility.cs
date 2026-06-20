using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Hardline", menuName = "Stacking/Abilities/Hardline")]
public class HardlineAbility : PassiveAbility
{
    [Header("Presentation")]
    [SerializeField] private Color laserColor = new Color(0.78f, 0.28f, 1f, 1f);
    [SerializeField] private float laserYOffset = 0.34f;
    [SerializeField] private int laserSortingOrder = -7;

    [Header("Catch")]
    [SerializeField] private float settleSeconds = 0.2f;
    [SerializeField] private float platformLiftAboveLaser = 0.05f;
    [SerializeField] private float overlapClearance = 0.04f;
    [SerializeField] private float maxOverlapNudgeColumns = 3f;

    [Header("Juice")]
    [Tooltip("Authored CFXR prefab burst from every cell of the caught block (base prefabs only - see ABILITIES.md). Null-safe.")]
    [SerializeField] private GameObject catchEffect;
    [SerializeField] private float catchEffectScale = 1f;

    private const int HardlinePriority = 50;
    private const float AngleStep = 90f;

    private SacrificeLaserLine _laserLine;
    private static readonly Color LaserAccent = new Color(0.98f, 0.42f, 1f, 1f);

    public override int LossInterceptPriority => HardlinePriority;
    public override float LossInterceptLineOffset => Mathf.Max(0f, laserYOffset);

    public override void OnAcquired(AbilityContext context, int stacks)
    {
        EnsureLaserLine();
    }

    public override void OnRemoved(AbilityContext context)
    {
        if (_laserLine != null)
        {
            Object.Destroy(_laserLine.gameObject);
            _laserLine = null;
        }
    }

    public override bool TryInterceptLoss(AbilityContext context, BlockController block)
    {
        if (block == null) return false;

        Camera cam = Camera.main;
        float catchY = LossZone.CurrentLossLineY(cam) + laserYOffset;
        SacrificeLaserLine.FlashAtLossLine(laserColor, laserYOffset);

        if (TryPlanPlatform(block, catchY, out Vector3 targetPosition, out float targetAngle))
        {
            HardlinePlatformFx.Begin(block, targetPosition, targetAngle, settleSeconds);
        }
        else
        {
            block.FreezeInPlace();
        }

        AbilityEffects.BurstFromEveryCell(block, catchEffect, catchEffectScale);
        SfxPlayer.Play("swoosh_01", 0.75f, 0.05f);
        AbilityEffects.ImpactPunch(0.035f, 0.11f, 0.12f);
        return true;
    }

    private void EnsureLaserLine()
    {
        if (_laserLine != null) return;

        GameObject go = new GameObject("HardlineLaserLine");
        _laserLine = go.AddComponent<SacrificeLaserLine>();
        _laserLine.Configure(laserColor, laserYOffset, laserSortingOrder, LaserAccent);
    }

    private bool TryPlanPlatform(BlockController block, float catchY, out Vector3 targetPosition, out float targetAngle)
    {
        targetPosition = block.transform.position;
        targetAngle = SnapAngle(block.transform.eulerAngles.z);

        if (!TrySnapshotCells(block, out CellSnapshot[] cells)) return false;

        targetAngle = ChooseBestPlatformAngle(block, cells);
        Bounds angledBounds = CalculateBounds(cells, targetPosition, targetAngle);
        targetPosition.y += catchY + Mathf.Max(0f, platformLiftAboveLaser) - angledBounds.min.y;
        targetPosition = ResolveOverlapNudge(block, cells, targetPosition, targetAngle);
        return true;
    }

    private float ChooseBestPlatformAngle(BlockController block, CellSnapshot[] cells)
    {
        float currentAngle = block.transform.eulerAngles.z;
        Candidate best = default;
        bool hasBest = false;

        for (int i = 0; i < 4; i++)
        {
            float angle = i * AngleStep;
            Candidate candidate = EvaluateCandidate(cells, angle, currentAngle, block.GridSpacing);
            if (!hasBest || IsBetter(candidate, best))
            {
                best = candidate;
                hasBest = true;
            }
        }

        return hasBest ? best.Angle : SnapAngle(currentAngle);
    }

    private Candidate EvaluateCandidate(CellSnapshot[] cells, float angle, float currentAngle, float gridSpacing)
    {
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        Vector2[] rotated = new Vector2[cells.Length];

        for (int i = 0; i < cells.Length; i++)
        {
            Vector2 point = Rotate(cells[i].LocalCenter, angle);
            rotated[i] = point;
            float half = cells[i].HalfSize;
            minX = Mathf.Min(minX, point.x - half);
            maxX = Mathf.Max(maxX, point.x + half);
            minY = Mathf.Min(minY, point.y - half);
            maxY = Mathf.Max(maxY, point.y + half);
        }

        int topCells = 0;
        float topTolerance = Mathf.Max(0.02f, gridSpacing * 0.12f);
        for (int i = 0; i < rotated.Length; i++)
        {
            if (rotated[i].y + cells[i].HalfSize >= maxY - topTolerance) topCells++;
        }

        return new Candidate
        {
            Angle = angle,
            Width = maxX - minX,
            Height = maxY - minY,
            TopCellCount = topCells,
            RotationDelta = Mathf.Abs(Mathf.DeltaAngle(currentAngle, angle))
        };
    }

    private bool IsBetter(Candidate candidate, Candidate best)
    {
        if (candidate.Width > best.Width + 0.01f) return true;
        if (candidate.Width < best.Width - 0.01f) return false;

        if (candidate.TopCellCount != best.TopCellCount)
        {
            return candidate.TopCellCount > best.TopCellCount;
        }

        if (candidate.Height < best.Height - 0.01f) return true;
        if (candidate.Height > best.Height + 0.01f) return false;

        return candidate.RotationDelta < best.RotationDelta;
    }

    private Vector3 ResolveOverlapNudge(BlockController block, CellSnapshot[] cells, Vector3 targetPosition, float targetAngle)
    {
        Bounds targetBounds = CalculateBounds(cells, targetPosition, targetAngle);
        float bestScore = GetOverlapScore(block, targetBounds);
        if (bestScore <= 0f) return targetPosition;

        Vector3 bestPosition = targetPosition;
        float step = Mathf.Max(0.05f, block.GridSpacing * 0.25f);
        int maxSteps = Mathf.CeilToInt(Mathf.Max(0f, maxOverlapNudgeColumns) * 4f);
        int awayDirection = targetPosition.x >= EstimateTowerCenterX(block) ? 1 : -1;

        for (int i = 1; i <= maxSteps; i++)
        {
            for (int side = 0; side < 2; side++)
            {
                int direction = side == 0 ? awayDirection : -awayDirection;
                Vector3 candidatePosition = targetPosition + Vector3.right * (direction * step * i);
                Bounds candidateBounds = CalculateBounds(cells, candidatePosition, targetAngle);
                float score = GetOverlapScore(block, candidateBounds);
                if (score >= bestScore - 0.0001f) continue;

                bestScore = score;
                bestPosition = candidatePosition;
                if (bestScore <= 0f) return bestPosition;
            }
        }

        return bestPosition;
    }

    private float GetOverlapScore(BlockController rescued, Bounds targetBounds)
    {
        Bounds padded = targetBounds;
        float clearance = Mathf.Max(0f, overlapClearance);
        padded.Expand(new Vector3(clearance, clearance, 0f));

        float score = 0f;
        IReadOnlyList<BlockController> blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController other = blocks[i];
            if (other == null || other == rescued || !other.HasLanded) continue;
            if (!other.TryGetWorldBounds(out Bounds otherBounds)) continue;

            float overlapX = Mathf.Min(padded.max.x, otherBounds.max.x) - Mathf.Max(padded.min.x, otherBounds.min.x);
            float overlapY = Mathf.Min(padded.max.y, otherBounds.max.y) - Mathf.Max(padded.min.y, otherBounds.min.y);
            if (overlapX <= 0f || overlapY <= 0f) continue;

            score += overlapX * overlapY;
        }

        return score;
    }

    private float EstimateTowerCenterX(BlockController excluded)
    {
        float total = 0f;
        int count = 0;
        IReadOnlyList<BlockController> blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || block == excluded || !block.HasLanded) continue;
            if (!block.TryGetWorldBounds(out Bounds bounds)) continue;

            total += bounds.center.x;
            count++;
        }

        return count > 0 ? total / count : 0f;
    }

    private bool TrySnapshotCells(BlockController block, out CellSnapshot[] cells)
    {
        cells = null;
        BoxCollider2D[] colliders = block.GetComponentsInChildren<BoxCollider2D>();
        if (colliders == null || colliders.Length == 0) return false;

        List<CellSnapshot> snapshots = new List<CellSnapshot>(colliders.Length);
        float halfSize = Mathf.Max(0.05f, block.GridSpacing * 0.5f);
        for (int i = 0; i < colliders.Length; i++)
        {
            BoxCollider2D box = colliders[i];
            if (box == null || box.isTrigger) continue;

            Vector3 worldCenter = box.transform.TransformPoint(box.offset);
            snapshots.Add(new CellSnapshot
            {
                LocalCenter = block.transform.InverseTransformPoint(worldCenter),
                HalfSize = halfSize
            });
        }

        cells = snapshots.ToArray();
        return cells.Length > 0;
    }

    private Bounds CalculateBounds(CellSnapshot[] cells, Vector3 position, float angle)
    {
        Vector2 first = Rotate(cells[0].LocalCenter, angle);
        Bounds bounds = new Bounds(position + (Vector3)first, Vector3.one * cells[0].HalfSize * 2f);
        for (int i = 1; i < cells.Length; i++)
        {
            Vector2 point = Rotate(cells[i].LocalCenter, angle);
            bounds.Encapsulate(new Bounds(position + (Vector3)point, Vector3.one * cells[i].HalfSize * 2f));
        }

        return bounds;
    }

    private static Vector2 Rotate(Vector2 point, float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector2(point.x * cos - point.y * sin, point.x * sin + point.y * cos);
    }

    private static float SnapAngle(float angle)
    {
        return Mathf.Round(angle / AngleStep) * AngleStep;
    }

    private struct CellSnapshot
    {
        public Vector2 LocalCenter;
        public float HalfSize;
    }

    private struct Candidate
    {
        public float Angle;
        public float Width;
        public float Height;
        public int TopCellCount;
        public float RotationDelta;
    }
}
