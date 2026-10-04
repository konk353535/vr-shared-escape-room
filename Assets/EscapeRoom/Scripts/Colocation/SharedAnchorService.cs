using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace EscapeRoom.Colocation
{
    /// <summary>
    /// Thin wrapper over the Meta XR group-sharing APIs: the host creates one spatial anchor at
    /// the room origin and shares it to the colocation group; every other headset loads that
    /// anchor from the group and aligns to it.
    /// </summary>
    public static class SharedAnchorService
    {
        public static async Task<OVRSpatialAnchor> CreateAnchorAsync(Pose pose)
        {
            var go = new GameObject("Room Origin Anchor (created here)");
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            var anchor = go.AddComponent<OVRSpatialAnchor>();

            bool localized = await anchor.WhenLocalizedAsync();
            if (!localized)
            {
                Debug.LogError("[EscapeRoom] Failed to create the room origin anchor.");
                UnityEngine.Object.Destroy(go);
                return null;
            }

            Debug.Log($"[EscapeRoom] Created room origin anchor {anchor.Uuid}");
            return anchor;
        }

        public static async Task<bool> ShareToGroupAsync(OVRSpatialAnchor anchor, Guid groupUuid)
        {
            var result = await OVRSpatialAnchor.ShareAsync(new List<OVRSpatialAnchor> { anchor }, groupUuid);
            if (!result.Success)
            {
                Debug.LogError($"[EscapeRoom] Sharing anchor {anchor.Uuid} to group {groupUuid} failed: {result.Status}");
                return false;
            }

            Debug.Log($"[EscapeRoom] Shared anchor {anchor.Uuid} to group {groupUuid}");
            return true;
        }

        /// <summary>
        /// Loads the first anchor shared to the group, localizes it and binds it to a new GameObject.
        /// Returns null when the host has not shared it yet (callers should retry).
        /// </summary>
        public static async Task<OVRSpatialAnchor> LoadFromGroupAsync(Guid groupUuid)
        {
            var result = await OVRSpatialAnchor.LoadUnboundSharedAnchorsAsync(
                groupUuid,
                unboundAnchors: new List<OVRSpatialAnchor.UnboundAnchor>());

            if (!result.TryGetValue(out var unboundAnchors) || unboundAnchors.Count == 0)
            {
                Debug.Log($"[EscapeRoom] No shared anchors in group {groupUuid} yet ({result.Status}).");
                return null;
            }

            var unbound = unboundAnchors[0];
            if (!await unbound.LocalizeAsync())
            {
                Debug.LogWarning($"[EscapeRoom] Found shared anchor {unbound.Uuid} but could not localize it.");
                return null;
            }

            var go = new GameObject("Room Origin Anchor (shared)");
            var anchor = go.AddComponent<OVRSpatialAnchor>();
            unbound.BindTo(anchor);

            if (!await anchor.WhenCreatedAsync())
            {
                Debug.LogError($"[EscapeRoom] Binding shared anchor {unbound.Uuid} failed.");
                UnityEngine.Object.Destroy(go);
                return null;
            }

            Debug.Log($"[EscapeRoom] Loaded shared room origin anchor {anchor.Uuid}");
            return anchor;
        }
    }
}
