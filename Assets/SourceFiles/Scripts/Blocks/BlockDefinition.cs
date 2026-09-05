using UnityEngine;

[CreateAssetMenu(fileName = "BlockDefinition", menuName = "Stacking/Block Definition")]
public class BlockDefinition : ScriptableObject
{
    [SerializeField] private string displayName = "Block";
    [SerializeField] private GameObject prefab;
    [SerializeField] private BlockData defaultData;
    [Min(1)]
    [SerializeField] private int bagCopies = 1;
    [Tooltip("The default data IS this shape's identity (Pyramid: non-rotatable sandstone). " +
             "Ambient variant rolls and variant overrides never replace it.")]
    [SerializeField] private bool lockDefaultData;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName)
        ? (prefab != null ? prefab.name : name)
        : displayName;

    public GameObject Prefab => prefab;
    public BlockData DefaultData => defaultData;
    public int BagCopies => Mathf.Max(1, bagCopies);
    public bool LockDefaultData => lockDefaultData && defaultData != null;

    // A melt fragment keeps the source stone's data/identity but owns a transient cell layout.
    // The fragment destroys this clone when it leaves the scene; authored definitions never change.
    internal BlockDefinition CloneWithPrefab(GameObject runtimePrefab)
    {
        BlockDefinition clone = Instantiate(this);
        clone.name = name;
        clone.hideFlags = HideFlags.HideAndDontSave;
        clone.prefab = runtimePrefab;
        return clone;
    }
}
