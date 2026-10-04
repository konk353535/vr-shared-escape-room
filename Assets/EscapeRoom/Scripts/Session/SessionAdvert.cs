using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace EscapeRoom.Session
{
    /// <summary>
    /// What the host headset puts in its colocation advertisement so other headsets in the
    /// room can find the Netcode host on the local network. Colocation metadata is limited
    /// to 1024 bytes, so keep this small.
    /// </summary>
    [Serializable]
    public class SessionAdvert
    {
        public const int CurrentVersion = 1;

        public int v = CurrentVersion;
        public string ip;
        public int port;
        public string name;

        public byte[] ToBytes() => Encoding.UTF8.GetBytes(JsonUtility.ToJson(this));

        public static bool TryRead(byte[] bytes, out SessionAdvert advert)
        {
            advert = null;
            if (bytes == null || bytes.Length == 0)
                return false;

            try
            {
                advert = JsonUtility.FromJson<SessionAdvert>(Encoding.UTF8.GetString(bytes));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EscapeRoom] Could not parse session advert: {e.Message}");
                return false;
            }

            return advert != null && advert.v == CurrentVersion && !string.IsNullOrEmpty(advert.ip) && advert.port > 0;
        }
    }

    public static class LocalNetwork
    {
        /// <summary>
        /// Best guess at this device's LAN IPv4 address (the Wi-Fi address on a Quest).
        /// </summary>
        public static string GetLocalIPv4()
        {
            // Connecting a UDP socket sends no packets but makes the OS pick the outbound interface.
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect("8.8.8.8", 65530);
                    if (socket.LocalEndPoint is IPEndPoint endPoint && !IPAddress.IsLoopback(endPoint.Address))
                        return endPoint.Address.ToString();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EscapeRoom] UDP route lookup failed, falling back to interface scan: {e.Message}");
            }

            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    foreach (var address in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                            return address.Address.ToString();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EscapeRoom] Interface scan failed: {e.Message}");
            }

            return "127.0.0.1";
        }
    }
}
