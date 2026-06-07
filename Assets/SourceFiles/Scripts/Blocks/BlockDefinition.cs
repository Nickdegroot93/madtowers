using UnityEngine;

[CreateAssetMenu(fileName = "BlockDefinition", menuName = "Stacking/Block Definition")]
public class BlockDefinition : ScriptableObject
{
    [SerializeField] private string displayName = "Block";
    [SerializeField] private GameObject prefab;
    [SerializeField] private BlockData defaultData;
    [Min(1)]
    [SerializeField] private int bagCopies = 1;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName)
        ? (prefab != null ? prefab.name : name)
        : displayName;

    public GameObject Prefab => prefab;
    public BlockData DefaultData => defaultData;
    public int BagCopies => Mathf.Max(1, bagCopies);
}
