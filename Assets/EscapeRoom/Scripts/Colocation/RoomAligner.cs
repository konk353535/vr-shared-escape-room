using UnityEngine;

namespace EscapeRoom.Colocation
{
    /// <summary>
    /// Moves the OVRCameraRig's tracking space so the shared room origin anchor sits at world
    /// (0, 0, 0) facing +Z. Every headset aligned to the same anchor then shares one world
    /// coordinate system, so networked positions line up with the physical room.
    ///
    /// Room convention: origin is the centre of the 6m x 5.5m floor, +Z points at the front wall
    /// (the one with the exit door), +X to the right when facing it.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class RoomAligner : MonoBehaviour
    {
        [Tooltip("How often to re-apply alignment so anchor drift corrections are picked up.")]
        public float realignIntervalSeconds = 1f;

        OVRCameraRig _rig;
        OVRSpatialAnchor _anchor;
        float _nextAlignTime;

        public bool IsAligned => _anchor != null;
        public OVRSpatialAnchor Anchor => _anchor;

        public OVRCameraRig Rig
        {
            get
            {
                if (!_rig)
                    _rig = FindFirstObjectByType<OVRCameraRig>();
                return _rig;
            }
        }

        void Start()
        {
            if (OVRManager.instance)
                OVRManager.instance.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
        }

        /// <summary>
        /// The calibration pose the host uses: on the floor under their head, facing where they look.
        /// The host should stand in the centre of the room and face the front wall.
        /// </summary>
        public Pose GetCalibrationPoseFromHead()
        {
            var head = Rig.centerEyeAnchor;
            var trackingSpace = Rig.trackingSpace;

            var position = head.position;
            position.y = trackingSpace.position.y; // floor level in the current tracking space

            var forward = Vector3.ProjectOnPlane(head.forward, trackingSpace.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = trackingSpace.forward;

            return new Pose(position, Quaternion.LookRotation(forward.normalized, trackingSpace.up));
        }

        public void AlignTo(OVRSpatialAnchor anchor)
        {
            _anchor = anchor;
            Align();
        }

        public void ClearAlignment()
        {
            if (_anchor)
                Destroy(_anchor.gameObject);
            _anchor = null;
        }

        void LateUpdate()
        {
            if (!_anchor || Time.unscaledTime < _nextAlignTime)
                return;

            _nextAlignTime = Time.unscaledTime + realignIntervalSeconds;
            Align();
        }

        void Align()
        {
            if (!_anchor || !Rig)
                return;

            var trackingSpace = Rig.trackingSpace;

            // Anchor pose expressed in tracking space. This stays stable when we move the tracking
            // space, because OVRSpatialAnchor re-derives its world pose from it every frame.
            var localPosition = trackingSpace.InverseTransformPoint(_anchor.transform.position);
            var localRotation = Quaternion.Inverse(trackingSpace.rotation) * _anchor.transform.rotation;

            // Yaw only: keep the floor level even if the anchor is slightly tilted.
            var rigRotation = Quaternion.Euler(0f, -localRotation.eulerAngles.y, 0f);
            trackingSpace.SetPositionAndRotation(-(rigRotation * localPosition), rigRotation);
        }
    }
}
