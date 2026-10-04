using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace EscapeRoom.Puzzle
{
    /// <summary>
    /// Spawns networked objects (the puzzle controller, imported image-blaster props) when the
    /// host starts. Positions are in room space: origin at the centre of the floor, +Z to the front wall.
    /// </summary>
    public class ServerSpawner : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            public GameObject prefab;
            public Vector3 position;
            public Quaternion rotation = Quaternion.identity;
            public Vector3 scale = Vector3.one;
        }

        public NetworkManager networkManager;
        public List<Entry> entries = new List<Entry>();

        void Awake()
        {
            if (!networkManager)
                networkManager = GetComponent<NetworkManager>();
            networkManager.OnServerStarted += SpawnAll;
        }

        void OnDestroy()
        {
            if (networkManager)
                networkManager.OnServerStarted -= SpawnAll;
        }

        void SpawnAll()
        {
            foreach (var entry in entries)
                Spawn(entry.prefab, entry.position, entry.rotation, entry.scale);

            // Markers may sit under room content that is still hidden, so include inactive ones.
            foreach (var marker in FindObjectsByType<SpawnMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var t = marker.transform;
                Spawn(marker.networkPrefab, t.position, t.rotation, t.lossyScale);
            }
        }

        static void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (!prefab)
                return;

            var instance = Instantiate(prefab, position, rotation);
            instance.transform.localScale = scale;
            instance.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
        }
    }
}
