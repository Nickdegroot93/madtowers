using System.Collections.Generic;
using UnityEngine;

/// <summary>Owns a transient Pip-based prefab assembled BEFORE the controller caches its cell grid.</summary>
public sealed class MagmaFragment : MonoBehaviour
{
    private GameObject _templateHost;
    private BlockDefinition _definition;
    private Sprite _plainCell;

    public static BlockController Spawn(Spawner spawner, BlockDefinition stone, List<Vector3> positions)
    {
        if (positions.Count == 1)
            return spawner.SpawnControlledPieceAt(stone, positions[0], suspended: false);

        GameObject host = new GameObject("MagmaFragmentTemplate") { hideFlags = HideFlags.HideAndDontSave };
        host.SetActive(false); // prevents Awake until the complete layout is instantiated by the spawner
        GameObject template = Instantiate(stone.Prefab, host.transform);
        // No whole-shape sprite exists for arbitrary triomino fragments. An empty token skips that
        // optional art path; ordinary chapter Pip faces remain underneath the fixed magma skin.
        template.name = "";
        BoxCollider2D source = template.GetComponentInChildren<BoxCollider2D>(true);
        Sprite plain = ChapterSkins.LoadPiece("Pip");
        Sprite cropped = null;
        if (plain != null)
        {
            Rect r = plain.rect;
            cropped = Sprite.Create(plain.texture, new Rect(r.x + 32, r.y + 32, 256, 256),
                Vector2.one * .5f, 256, 0, SpriteMeshType.FullRect);
            cropped.name = "MagmaFragmentPlainCell";
            cropped.hideFlags = HideFlags.HideAndDontSave;
        }
        for (int i = 0; i < positions.Count; i++)
        {
            BoxCollider2D cell = i == 0 ? source : Instantiate(source.gameObject, template.transform).GetComponent<BoxCollider2D>();
            cell.transform.localPosition = positions[i] - positions[0];
            cell.offset = Vector2.zero;
            if (cropped != null && cell.TryGetComponent(out SpriteRenderer sr)) sr.sprite = cropped;
        }
        BlockDefinition definition = stone.CloneWithPrefab(template);
        BlockController block = spawner.SpawnControlledPieceAt(definition, positions[0], suspended: false);
        if (block == null)
        {
            Destroy(host); Destroy(definition); if (cropped != null) Destroy(cropped);
            return null;
        }
        block.name = "MagmaFragment_" + positions.Count;
        // Combo predicates match definitions by reference. Keep the original stone identity;
        // the transient definition exists solely to feed the complete geometry through Awake.
        block.GetComponent<BlockIdentity>().Assign(stone, stone.DefaultData);
        // The old output was one mass-bearing Pip per cell. A joined fragment keeps their total mass.
        block.GetComponent<Rigidbody2D>().mass *= positions.Count;
        MagmaFragment owner = block.gameObject.AddComponent<MagmaFragment>();
        owner._templateHost = host; owner._definition = definition; owner._plainCell = cropped;
        return block;
    }

    private void OnDestroy()
    {
        if (_templateHost != null) Destroy(_templateHost);
        if (_definition != null) Destroy(_definition);
        if (_plainCell != null) Destroy(_plainCell);
    }
}
