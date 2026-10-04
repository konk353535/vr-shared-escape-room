using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace EscapeRoom.Interaction
{
    /// <summary>
    /// A physics object any player can pick up. Grabbing asks the server for ownership; the owner
    /// then drives the transform (owner-authority NetworkTransform) and simulates the throw.
    /// The server can lock an item into a socket, after which nobody can grab it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class NetworkGrabbable : NetworkBehaviour
    {
        public const ulong NoHolder = ulong.MaxValue;

        public static readonly List<NetworkGrabbable> All = new List<NetworkGrabbable>();

        [Tooltip("Sockets match on this id, e.g. gem_red or key.")]
        public string itemId;

        [Tooltip("Seconds to wait for the server to hand over ownership before giving up on a grab.")]
        public float grabTimeout = 1.5f;

        readonly NetworkVariable<ulong> _holder = new NetworkVariable<ulong>(NoHolder);
        readonly NetworkVariable<bool> _locked = new NetworkVariable<bool>(false);

        Rigidbody _rb;
        Collider[] _colliders;
        Transform _hand;
        Vector3 _gripPosition;
        Quaternion _gripRotation;
        float _grabRequestedAt;
        bool _simulatePhysics;
        Vector3 _lastPosition;
        Vector3 _velocity;

        public bool IsHeld => _holder.Value != NoHolder;
        public bool IsLocked => _locked.Value;
        public bool CanBeGrabbed => IsSpawned && !IsHeld && !IsLocked;
        public bool IsHeldLocally => _hand != null;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>();
            _rb.isKinematic = true; // rests where it was spawned until someone throws it
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager)
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        public override void OnLostOwnership()
        {
            _simulatePhysics = false;
            _hand = null;
        }

        /// <summary>Distance from a point to this object's colliders.</summary>
        public float DistanceTo(Vector3 point)
        {
            float best = float.MaxValue;
            foreach (var c in _colliders)
            {
                if (c && c.enabled)
                    best = Mathf.Min(best, Vector3.Distance(point, c.ClosestPoint(point)));
            }
            return best == float.MaxValue ? Vector3.Distance(point, transform.position) : best;
        }

        // ------------------------------------------------------------ local grab API

        public void BeginLocalGrab(Transform hand)
        {
            _hand = hand;
            _grabRequestedAt = Time.time;
            _gripPosition = hand.InverseTransformPoint(transform.position);
            _gripRotation = Quaternion.Inverse(hand.rotation) * transform.rotation;
            RequestGrabRpc();
        }

        public void EndLocalGrab()
        {
            if (_hand == null)
                return;

            _hand = null;
            if (IsOwner && !IsLocked)
            {
                _simulatePhysics = true;
                _rb.isKinematic = false;
                _rb.linearVelocity = _velocity;
            }
            ReleaseRpc();
        }

        bool HeldByMe => IsOwner && _holder.Value == NetworkManager.LocalClientId;

        void Update()
        {
            if (!IsSpawned)
                return;

            if (_hand != null)
            {
                if (HeldByMe)
                {
                    _rb.isKinematic = true;
                    var target = _hand.TransformPoint(_gripPosition);
                    _velocity = (target - _lastPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
                    _lastPosition = target;
                    transform.SetPositionAndRotation(target, _hand.rotation * _gripRotation);
                }
                else if (IsLocked || (IsHeld && _holder.Value != NetworkManager.LocalClientId) || Time.time - _grabRequestedAt > grabTimeout)
                {
                    _hand = null; // someone else got it, it got locked, or the server never answered
                }
                else
                {
                    _lastPosition = _hand.TransformPoint(_gripPosition);
                }
                return;
            }

            // Only the owner simulates, and only after a throw. Everyone else just follows the network.
            _rb.isKinematic = !(IsOwner && _simulatePhysics && !IsLocked);
        }

        // ------------------------------------------------------------ server side

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void RequestGrabRpc(RpcParams rpcParams = default)
        {
            var sender = rpcParams.Receive.SenderClientId;
            if (IsLocked || (IsHeld && _holder.Value != sender))
                return; // the requester times out locally

            if (OwnerClientId != sender)
                NetworkObject.ChangeOwnership(sender);
            _holder.Value = sender;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void ReleaseRpc(RpcParams rpcParams = default)
        {
            if (_holder.Value == rpcParams.Receive.SenderClientId)
                _holder.Value = NoHolder;
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (_holder.Value == clientId)
                _holder.Value = NoHolder;
        }

        /// <summary>Server only: snap into a socket and stop anyone grabbing it again.</summary>
        public void ServerLockInto(Transform snapPoint)
        {
            if (!IsServer)
                return;

            _holder.Value = NoHolder;
            _locked.Value = true;
            if (OwnerClientId != NetworkManager.ServerClientId)
                NetworkObject.RemoveOwnership();

            _simulatePhysics = false;
            _rb.isKinematic = true;
            transform.SetPositionAndRotation(snapPoint.position, snapPoint.rotation);
        }
    }
}
