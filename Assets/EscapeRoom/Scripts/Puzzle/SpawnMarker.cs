using UnityEngine;

namespace EscapeRoom.Puzzle
{
    /// <summary>
    /// An editor-placeable stand-in for a networked prop. Lay the prop out in the scene like any
    /// other object; at runtime the marker hides itself and the host's ServerSpawner spawns the
    /// networked prefab at the marker's pose.
    /// </summary>
    public class SpawnMarker : MonoBehaviour
    {
        public GameObject networkPrefab;

        void Awake()
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>(true))
                c.enabled = false;
        }
    }
}
