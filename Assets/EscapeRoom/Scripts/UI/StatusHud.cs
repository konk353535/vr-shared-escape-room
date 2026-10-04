using UnityEngine;

namespace EscapeRoom.UI
{
    /// <summary>
    /// A small world-space text panel that lazily follows the player's view. Uses the legacy
    /// TextMesh so it needs no extra packages or fonts.
    /// </summary>
    public class StatusHud : MonoBehaviour
    {
        public TextMesh text;
        public float distance = 1.3f;
        public float heightOffset = -0.25f;
        public float followSpeed = 3f;
        public float hideAfterSeconds = 12f;

        Transform _head;
        float _shownAt;

        public void SetStatus(string message)
        {
            if (text)
            {
                text.text = message;
                text.gameObject.SetActive(true);
            }
            _shownAt = Time.time;
        }

        void LateUpdate()
        {
            if (!_head)
            {
                var rig = FindFirstObjectByType<OVRCameraRig>();
                if (rig)
                    _head = rig.centerEyeAnchor;
                else if (Camera.main)
                    _head = Camera.main.transform;
                if (!_head)
                    return;
                SnapToView(1f);
            }

            // Pressing either thumbstick brings the panel back after it fades.
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick) || OVRInput.GetDown(OVRInput.Button.SecondaryThumbstick))
                SetStatus(text ? text.text : "");

            if (text && hideAfterSeconds > 0f && Time.time - _shownAt > hideAfterSeconds)
                text.gameObject.SetActive(false);

            SnapToView(1f - Mathf.Exp(-followSpeed * Time.deltaTime));
        }

        void SnapToView(float t)
        {
            var forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = _head.up;
            forward.Normalize();

            var targetPosition = _head.position + forward * distance + Vector3.up * heightOffset;
            var targetRotation = Quaternion.LookRotation(forward, Vector3.up);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, targetPosition, t),
                Quaternion.Slerp(transform.rotation, targetRotation, t));
        }
    }
}
