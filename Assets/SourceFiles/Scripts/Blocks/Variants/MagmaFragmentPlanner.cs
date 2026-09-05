using System.Collections.Generic;
using UnityEngine;

/// <summary>Connected cells translating by the same clearance remain one rigid piece.</summary>
public static class MagmaFragmentPlanner
{
    public static List<List<Vector3>> Capture(BlockController magma)
    {
        var cells = new List<BoxCollider2D>();
        foreach (BoxCollider2D cell in magma.GetComponentsInChildren<BoxCollider2D>())
            if (cell != null && !cell.isTrigger) cells.Add(cell);
        cells.Sort((a, b) => Compare(a.bounds.center, b.bounds.center));
        float spacing = magma.GridSpacing;
        var positions = new List<Vector3>();
        var drops = new List<float>();
        for (int i = 0; i < cells.Count; i++)
        {
            Vector3 centre = cells[i].bounds.center;
            int bottom = i;
            for (int j = 0; j < i; j++)
                if (Mathf.Abs(cells[j].bounds.center.x - centre.x) < spacing * .01f) { bottom = j; break; }
            positions.Add(centre);
            // The upper cell rides the bottom cell's translation, not its own distance to the floor.
            drops.Add(bottom < i ? drops[bottom] : magma.MeasureMagmaCellDrop(cells[i]));
        }
        return Group(positions, drops, spacing);
    }

    // Pure partition, also exercised by the editor regression cases. Disconnected equal-height
    // stones stay separate; joining them across empty space would invent physical material.
    public static List<List<Vector3>> Group(IReadOnlyList<Vector3> positions, IReadOnlyList<float> drops, float spacing)
    {
        var groups = new List<List<Vector3>>();
        var visited = new bool[positions.Count];
        var queue = new Queue<int>();
        float tolerance = Mathf.Max(.001f, spacing * .005f);
        for (int start = 0; start < positions.Count; start++)
        {
            if (visited[start]) continue;
            var group = new List<Vector3>();
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                group.Add(positions[at]);
                for (int j = 0; j < positions.Count; j++)
                {
                    if (visited[j]) continue;
                    bool sameDrop = drops[j] == drops[start] || Mathf.Abs(drops[j] - drops[start]) <= tolerance;
                    Vector3 delta = positions[j] - positions[at];
                    bool adjacent = (Mathf.Abs(delta.x) <= tolerance && Mathf.Abs(Mathf.Abs(delta.y) - spacing) <= tolerance)
                        || (Mathf.Abs(delta.y) <= tolerance && Mathf.Abs(Mathf.Abs(delta.x) - spacing) <= tolerance);
                    if (!sameDrop || !adjacent) continue;
                    visited[j] = true;
                    queue.Enqueue(j);
                }
            }
            group.Sort(Compare);
            groups.Add(group);
        }
        groups.Sort((a, b) => Compare(a[0], b[0]));
        return groups;
    }

    private static int Compare(Vector3 a, Vector3 b)
    {
        int y = a.y.CompareTo(b.y);
        return y != 0 ? y : a.x.CompareTo(b.x);
    }
}
