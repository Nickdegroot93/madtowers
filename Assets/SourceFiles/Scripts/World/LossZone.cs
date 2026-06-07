using UnityEngine;

public class LossZone : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;

        // If the collider belongs to a block, the Rigidbody2D may live on its parent.
        Rigidbody2D rb = other.attachedRigidbody;
        if (rb != null)
        {
            GameManager.Instance.GameOver();
            Destroy(rb.gameObject);
        }
    }
}
