using UnityEngine;

namespace EscapeRoom.Interaction
{
    /// <summary>
    /// Adds a HandGrabber to each hand anchor of the local OVRCameraRig at runtime, so the scene
    /// doesn't depend on the rig's hierarchy existing at edit time.
    /// </summary>
    public class LocalRigBinder : MonoBehaviour
    {
        void Start()
        {
            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (!rig)
            {
                Debug.LogError("[EscapeRoom] No OVRCameraRig in the scene.");
                return;
            }

            Bind(rig.leftHandAnchor, OVRInput.Controller.LTouch, OVRInput.Controller.LHand);
            Bind(rig.rightHandAnchor, OVRInput.Controller.RTouch, OVRInput.Controller.RHand);
        }

        static void Bind(Transform anchor, OVRInput.Controller touch, OVRInput.Controller hand)
        {
            if (!anchor || anchor.GetComponent<HandGrabber>())
                return;

            var grabber = anchor.gameObject.AddComponent<HandGrabber>();
            grabber.touchController = touch;
            grabber.trackedHand = hand;
        }
    }
}
