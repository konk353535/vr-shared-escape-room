using EscapeRoom.Interaction;
using UnityEngine;

namespace EscapeRoom.Puzzle
{
    /// <summary>
    /// A place an item belongs: a gem pedestal, a keyhole. The server asks each socket whether a
    /// matching, released item is close enough, then locks it in.
    /// </summary>
    public class ItemSocket : MonoBehaviour
    {
        public string acceptsItemId;
        public float snapRadius = 0.2f;

        [Tooltip("Where the item sits once locked in. Defaults to this transform.")]
        public Transform snapPoint;

        [Tooltip("Lights up when the socket is filled.")]
        public Renderer indicator;
        public Color emptyColor = new Color(0.15f, 0.15f, 0.15f);
        public Color filledColor = Color.white;

        public Transform SnapPoint => snapPoint ? snapPoint : transform;

        public NetworkGrabbable FindCandidate()
        {
            foreach (var item in NetworkGrabbable.All)
            {
                if (item.itemId != acceptsItemId || item.IsHeld || item.IsLocked)
                    continue;

                if (Vector3.Distance(item.transform.position, SnapPoint.position) <= snapRadius)
                    return item;
            }
            return null;
        }

        public void SetFilledVisual(bool filled)
        {
            if (!indicator)
                return;

            var color = filled ? filledColor : emptyColor;
            indicator.material.color = color;
            indicator.material.SetColor("_EmissionColor", filled ? color * 1.5f : Color.black);
        }
    }
}
