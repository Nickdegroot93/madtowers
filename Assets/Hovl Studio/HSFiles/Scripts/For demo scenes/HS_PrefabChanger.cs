using UnityEngine;

namespace Hovl
{
    public class HS_PrefabChanger : MonoBehaviour
    {
        public GameObject[] prefabs;

        int currentIndex = 0;

        void Start()
        {
            if (prefabs == null || prefabs.Length == 0) return;

            // Ensure all prefabs are disabled, then enable the first one
            for (int i = 0; i < prefabs.Length; i++)
            {
                if (prefabs[i] != null)
                    prefabs[i].SetActive(false);
            }

            currentIndex = 0;
            if (prefabs[0] != null)
                prefabs[0].SetActive(true);
        }

        void Update()
        {
            // Change with left/right arrow keys
            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                NextPrefab();
            }
            else if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                PrevPrefab();
            }
        }

        // Call from UI buttons if needed
        public void NextPrefab()
        {
            if (prefabs == null || prefabs.Length == 0) return;

            // Disable current
            if (prefabs[currentIndex] != null)
                prefabs[currentIndex].SetActive(false);

            // Advance and wrap
            currentIndex = (currentIndex + 1) % prefabs.Length;

            // Enable new
            if (prefabs[currentIndex] != null)
                prefabs[currentIndex].SetActive(true);
        }

        public void PrevPrefab()
        {
            if (prefabs == null || prefabs.Length == 0) return;

            // Disable current
            if (prefabs[currentIndex] != null)
                prefabs[currentIndex].SetActive(false);

            // Step back and wrap
            currentIndex = (currentIndex - 1 + prefabs.Length) % prefabs.Length;

            // Enable new
            if (prefabs[currentIndex] != null)
                prefabs[currentIndex].SetActive(true);
        }
    }
}