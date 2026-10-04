using UnityEngine;

namespace EscapeRoom.UI
{
    /// <summary>
    /// Press Y (left controller) to switch between the virtual room and passthrough with the
    /// puzzle objects overlaid on the real room. Handy for checking the alignment against real
    /// walls, and as a safety view.
    /// </summary>
    public class PassthroughToggle : MonoBehaviour
    {
        public OVRPassthroughLayer passthroughLayer;

        [Tooltip("Virtual walls/floor hidden while passthrough is on.")]
        public GameObject[] hideInPassthrough;

        public bool startInPassthrough;

        Camera _eyeCamera;
        CameraClearFlags _clearFlags;
        Color _background;
        bool _on;

        void Start()
        {
            if (!passthroughLayer)
                passthroughLayer = FindFirstObjectByType<OVRPassthroughLayer>();

            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig)
                _eyeCamera = rig.centerEyeAnchor.GetComponent<Camera>();
            if (_eyeCamera)
            {
                _clearFlags = _eyeCamera.clearFlags;
                _background = _eyeCamera.backgroundColor;
            }

            Set(startInPassthrough);
        }

        void Update()
        {
            if (OVRInput.GetDown(OVRInput.Button.Four))
                Set(!_on);
        }

        public void Set(bool on)
        {
            _on = on;
            if (passthroughLayer)
                passthroughLayer.enabled = on;

            if (_eyeCamera)
            {
                _eyeCamera.clearFlags = on ? CameraClearFlags.SolidColor : _clearFlags;
                _eyeCamera.backgroundColor = on ? Color.clear : _background;
            }

            foreach (var go in hideInPassthrough)
            {
                if (go)
                    go.SetActive(!on);
            }
        }
    }
}
