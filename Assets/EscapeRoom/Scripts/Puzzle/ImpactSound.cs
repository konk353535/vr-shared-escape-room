using UnityEngine;

namespace EscapeRoom.Puzzle
{
    /// <summary>
    /// Plays one of a set of clips when the object hits something. Used for the per-object SFX
    /// that image-blaster generates.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ImpactSound : MonoBehaviour
    {
        public AudioClip[] clips;
        public float minImpactSpeed = 0.4f;
        public float fullVolumeSpeed = 3f;
        public float cooldown = 0.1f;

        AudioSource _source;
        float _nextAllowed;

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 1f;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (clips == null || clips.Length == 0 || Time.time < _nextAllowed)
                return;

            float speed = collision.relativeVelocity.magnitude;
            if (speed < minImpactSpeed)
                return;

            _nextAllowed = Time.time + cooldown;
            var clip = clips[Random.Range(0, clips.Length)];
            _source.PlayOneShot(clip, Mathf.Clamp01(speed / fullVolumeSpeed));
        }
    }
}
