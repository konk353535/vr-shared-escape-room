using UnityEngine;

namespace EscapeRoom.Interaction
{
    /// <summary>
    /// Local-only grab input on one OVRCameraRig hand anchor. Squeeze the grip on a Touch
    /// controller (or pinch with hand tracking) near a NetworkGrabbable to pick it up.
    ///
    /// This is deliberately minimal so the networking is easy to follow. The Meta Interaction SDK
    /// (already in the manifest) is the natural upgrade for hand-grab poses and distance grabs.
    /// </summary>
    public class HandGrabber : MonoBehaviour
    {
        public OVRInput.Controller touchController = OVRInput.Controller.RTouch;
        public OVRInput.Controller trackedHand = OVRInput.Controller.RHand;
        public float grabRadius = 0.12f;
        public float pressThreshold = 0.55f;
        public float releaseThreshold = 0.35f;

        NetworkGrabbable _held;
        bool _pressed;

        void Update()
        {
            float squeeze = Mathf.Max(
                OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, touchController),
                OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, trackedHand));

            if (!_pressed && squeeze >= pressThreshold)
            {
                _pressed = true;
                TryGrab();
            }
            else if (_pressed && squeeze <= releaseThreshold)
            {
                _pressed = false;
                Release();
            }

            if (_held && !_held.IsHeldLocally)
                _held = null; // the grab was refused or the item was locked into a socket
        }

        void TryGrab()
        {
            NetworkGrabbable best = null;
            float bestDistance = grabRadius;
            foreach (var grabbable in NetworkGrabbable.All)
            {
                if (!grabbable.CanBeGrabbed)
                    continue;

                float d = grabbable.DistanceTo(transform.position);
                if (d <= bestDistance)
                {
                    best = grabbable;
                    bestDistance = d;
                }
            }

            if (!best)
                return;

            _held = best;
            _held.BeginLocalGrab(transform);
            OVRInput.SetControllerVibration(0.3f, 0.2f, touchController);
            Invoke(nameof(StopVibration), 0.08f);
        }

        void Release()
        {
            if (_held)
                _held.EndLocalGrab();
            _held = null;
        }

        void StopVibration() => OVRInput.SetControllerVibration(0f, 0f, touchController);

        void OnDisable() => Release();
    }
}
