using Unity.Netcode;
using UnityEngine;

namespace EscapeRoom.Players
{
    /// <summary>
    /// Networked stand-in for a player: a head and two hands. The owning headset copies its
    /// OVRCameraRig poses onto the children every frame; owner-authority NetworkTransforms on
    /// those children replicate them to everyone else. Because every headset is aligned to the
    /// same room origin, the avatar appears where the real person is standing.
    /// </summary>
    public class NetworkPlayer : NetworkBehaviour
    {
        public Transform head;
        public Transform leftHand;
        public Transform rightHand;

        [Tooltip("Renderers tinted with this player's colour.")]
        public Renderer[] tintRenderers;

        [Tooltip("Objects hidden on the owner's own headset (e.g. the head mesh, so it doesn't block your view).")]
        public GameObject[] hiddenForOwner;

        public TextMesh nameLabel;

        static readonly Color[] Palette =
        {
            new Color(0.95f, 0.45f, 0.2f),
            new Color(0.25f, 0.6f, 0.95f),
            new Color(0.4f, 0.85f, 0.35f),
            new Color(0.85f, 0.35f, 0.85f),
        };

        OVRCameraRig _rig;

        public override void OnNetworkSpawn()
        {
            var color = Palette[(int)(OwnerClientId % (ulong)Palette.Length)];
            foreach (var r in tintRenderers)
            {
                if (r)
                    r.material.color = color; // instantiates a per-player material
            }

            if (nameLabel)
            {
                nameLabel.text = $"Player {OwnerClientId + 1}";
                nameLabel.color = color;
            }

            if (IsOwner)
            {
                foreach (var go in hiddenForOwner)
                {
                    if (go)
                        go.SetActive(false);
                }
            }

            name = $"Player {OwnerClientId + 1}{(IsOwner ? " (local)" : "")}";
        }

        void LateUpdate()
        {
            if (IsSpawned && IsOwner)
                CopyRigPoses();

            if (nameLabel && nameLabel.gameObject.activeInHierarchy && Camera.main)
            {
                // Billboard the label towards whoever is looking.
                var toCamera = nameLabel.transform.position - Camera.main.transform.position;
                if (toCamera.sqrMagnitude > 0.0001f)
                    nameLabel.transform.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
            }
        }

        void CopyRigPoses()
        {
            if (!_rig)
                _rig = FindFirstObjectByType<OVRCameraRig>();
            if (!_rig)
                return;

            Copy(_rig.centerEyeAnchor, head);
            Copy(_rig.leftHandAnchor, leftHand);
            Copy(_rig.rightHandAnchor, rightHand);
        }

        static void Copy(Transform from, Transform to)
        {
            if (from && to)
                to.SetPositionAndRotation(from.position, from.rotation);
        }
    }
}
