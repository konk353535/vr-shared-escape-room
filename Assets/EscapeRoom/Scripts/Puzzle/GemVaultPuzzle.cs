using System;
using EscapeRoom.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace EscapeRoom.Puzzle
{
    /// <summary>
    /// The starter puzzle chain, run by the server and mirrored to every headset:
    ///   1. Three gems are scattered around the room.
    ///   2. Put each gem on its matching pedestal. When all three are in, the chest opens and a key appears.
    ///   3. Put the key in the lock beside the door. The door swings open: escaped.
    /// </summary>
    public class GemVaultPuzzle : NetworkBehaviour
    {
        public enum Stage
        {
            ReturnTheGems = 0,
            FindTheKey = 1,
            Escaped = 2,
        }

        [Serializable]
        public struct ItemSpawn
        {
            public NetworkObject prefab;
            [Tooltip("Position relative to the puzzle root (the room origin).")]
            public Vector3 localPosition;
        }

        [Header("Stage 1: gems onto pedestals")]
        public ItemSocket[] gemSockets;
        public ItemSpawn[] startingItems;

        [Header("Stage 2: chest opens, key appears")]
        public Transform chestLidPivot;
        public float chestLidOpenAngle = -110f;
        public NetworkObject keyPrefab;
        public Transform keySpawnPoint;

        [Header("Stage 3: key in lock, door opens")]
        public ItemSocket keySocket;
        public Transform doorPivot;
        public float doorOpenAngle = -100f;
        public GameObject escapedBanner;

        [Header("Feedback")]
        public Renderer lamp;
        public Light lampLight;
        public Color[] stageColors = { new Color(0.9f, 0.15f, 0.1f), new Color(0.95f, 0.75f, 0.1f), new Color(0.2f, 0.9f, 0.3f) };
        public AudioSource stageCompleteSound;

        public float animationSpeed = 2f;

        readonly NetworkVariable<int> _stage = new NetworkVariable<int>((int)Stage.ReturnTheGems);
        readonly NetworkVariable<int> _gemMask = new NetworkVariable<int>(0);

        Quaternion _lidClosed;
        Quaternion _doorClosed;

        public Stage CurrentStage => (Stage)_stage.Value;

        void Awake()
        {
            if (chestLidPivot)
                _lidClosed = chestLidPivot.localRotation;
            if (doorPivot)
                _doorClosed = doorPivot.localRotation;
        }

        public override void OnNetworkSpawn()
        {
            _stage.OnValueChanged += OnStageChanged;
            _gemMask.OnValueChanged += OnGemMaskChanged;
            ApplyVisuals();

            if (IsServer)
            {
                foreach (var spawn in startingItems)
                    SpawnItem(spawn.prefab, transform.TransformPoint(spawn.localPosition), transform.rotation);
            }
        }

        public override void OnNetworkDespawn()
        {
            _stage.OnValueChanged -= OnStageChanged;
            _gemMask.OnValueChanged -= OnGemMaskChanged;
        }

        void Update()
        {
            AnimateHinges();

            if (IsSpawned && IsServer)
                ServerTick();
        }

        void ServerTick()
        {
            switch (CurrentStage)
            {
                case Stage.ReturnTheGems:
                    for (int i = 0; i < gemSockets.Length; i++)
                    {
                        if ((_gemMask.Value & (1 << i)) != 0)
                            continue;

                        var gem = gemSockets[i].FindCandidate();
                        if (!gem)
                            continue;

                        gem.ServerLockInto(gemSockets[i].SnapPoint);
                        _gemMask.Value |= 1 << i;
                    }

                    if (_gemMask.Value == (1 << gemSockets.Length) - 1)
                    {
                        _stage.Value = (int)Stage.FindTheKey;
                        if (keyPrefab && keySpawnPoint)
                            SpawnItem(keyPrefab, keySpawnPoint.position, keySpawnPoint.rotation);
                    }
                    break;

                case Stage.FindTheKey:
                    var key = keySocket ? keySocket.FindCandidate() : null;
                    if (key)
                    {
                        key.ServerLockInto(keySocket.SnapPoint);
                        _stage.Value = (int)Stage.Escaped;
                    }
                    break;
            }
        }

        static void SpawnItem(NetworkObject prefab, Vector3 position, Quaternion rotation)
        {
            if (!prefab)
                return;

            var instance = Instantiate(prefab, position, rotation);
            instance.Spawn(destroyWithScene: true);
        }

        void OnStageChanged(int previous, int current)
        {
            ApplyVisuals();
            if (current > previous && stageCompleteSound)
                stageCompleteSound.Play();
        }

        void OnGemMaskChanged(int previous, int current) => ApplyVisuals();

        void ApplyVisuals()
        {
            for (int i = 0; i < gemSockets.Length; i++)
                gemSockets[i].SetFilledVisual((_gemMask.Value & (1 << i)) != 0);

            if (keySocket)
                keySocket.SetFilledVisual(CurrentStage == Stage.Escaped);

            var color = stageColors[Mathf.Clamp(_stage.Value, 0, stageColors.Length - 1)];
            if (lamp)
            {
                lamp.material.color = color;
                lamp.material.SetColor("_EmissionColor", color * 2f);
            }
            if (lampLight)
                lampLight.color = color;

            if (escapedBanner)
                escapedBanner.SetActive(CurrentStage == Stage.Escaped);
        }

        void AnimateHinges()
        {
            float t = 1f - Mathf.Exp(-animationSpeed * Time.deltaTime);

            if (chestLidPivot)
            {
                var target = CurrentStage >= Stage.FindTheKey ? _lidClosed * Quaternion.Euler(chestLidOpenAngle, 0f, 0f) : _lidClosed;
                chestLidPivot.localRotation = Quaternion.Slerp(chestLidPivot.localRotation, target, t);
            }

            if (doorPivot)
            {
                var target = CurrentStage >= Stage.Escaped ? _doorClosed * Quaternion.Euler(0f, doorOpenAngle, 0f) : _doorClosed;
                doorPivot.localRotation = Quaternion.Slerp(doorPivot.localRotation, target, t);
            }
        }
    }
}
