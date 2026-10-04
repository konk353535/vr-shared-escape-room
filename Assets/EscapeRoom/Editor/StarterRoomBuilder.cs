using System.Collections.Generic;
using System.IO;
using EscapeRoom.Colocation;
using EscapeRoom.Interaction;
using EscapeRoom.Players;
using EscapeRoom.Puzzle;
using EscapeRoom.Session;
using EscapeRoom.UI;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EscapeRoom.Editor
{
    /// <summary>
    /// Generates the starter scene, prefabs and materials from code, so the repo doesn't carry
    /// hand-edited YAML and the room can be regenerated after changing dimensions.
    ///
    /// Room space: origin at the centre of the floor, +Z towards the front wall (exit door),
    /// +X to the right when facing it. The physical room is 6m (X) by 5.5m (Z).
    /// </summary>
    public static class StarterRoomBuilder
    {
        public const float RoomWidth = 6f;    // X
        public const float RoomDepth = 5.5f;  // Z
        public const float WallHeight = 2.7f;
        const float WallThickness = 0.1f;
        const float DoorWidth = 1f;
        const float DoorHeight = 2.1f;

        public const string GeneratedRoot = "Assets/EscapeRoom/Generated";
        const string PrefabDir = GeneratedRoot + "/Prefabs";
        const string MaterialDir = GeneratedRoot + "/Materials";
        public const string ScenePath = "Assets/EscapeRoom/Scenes/EscapeRoom.unity";
        public const string NetworkPrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";

        static readonly Color Red = new Color(0.9f, 0.12f, 0.1f);
        static readonly Color Green = new Color(0.15f, 0.85f, 0.25f);
        static readonly Color Blue = new Color(0.15f, 0.4f, 0.95f);

        [MenuItem("Escape Room/2. Build Starter Room Scene", priority = 20)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureFolder(PrefabDir);
            EnsureFolder(MaterialDir);
            EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));

            var gemRed = BuildGemPrefab("Gem_Red", "gem_red", Red);
            var gemGreen = BuildGemPrefab("Gem_Green", "gem_green", Green);
            var gemBlue = BuildGemPrefab("Gem_Blue", "gem_blue", Blue);
            var key = BuildKeyPrefab();
            var player = BuildPlayerPrefab();
            var puzzle = BuildPuzzlePrefab(gemRed, gemGreen, gemBlue, key);

            var prefabsList = EnsureNetworkPrefabsList(gemRed, gemGreen, gemBlue, key, player, puzzle);
            BuildScene(player, puzzle, prefabsList);

            AssetDatabase.SaveAssets();
            Debug.Log($"[EscapeRoom] Built {ScenePath}. Next: Escape Room > 1. Configure Project for Quest 3 (if you haven't), then Build And Run.");
        }

        // ------------------------------------------------------------------ prefabs

        static NetworkObject BuildGemPrefab(string name, string itemId, Color color)
        {
            var root = new GameObject(name);
            var collider = root.AddComponent<SphereCollider>();
            collider.radius = 0.06f;

            var visual = Primitive(PrimitiveType.Sphere, "Visual", root.transform, Vector3.zero, Vector3.one * 0.12f, Mat(name, color, emissive: true), collider: false);
            visual.transform.localRotation = Quaternion.Euler(0f, 45f, 35f);

            MakeGrabbable(root, itemId, mass: 0.3f);
            return SavePrefab(root, name).GetComponent<NetworkObject>();
        }

        static NetworkObject BuildKeyPrefab()
        {
            var root = new GameObject("Key");
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.22f, 0.05f, 0.07f);

            var gold = Mat("Gold", new Color(1f, 0.78f, 0.25f), emissive: true);
            Primitive(PrimitiveType.Cube, "Shaft", root.transform, new Vector3(0.03f, 0f, 0f), new Vector3(0.14f, 0.022f, 0.022f), gold, collider: false);
            var bow = Primitive(PrimitiveType.Cylinder, "Bow", root.transform, new Vector3(-0.07f, 0f, 0f), new Vector3(0.065f, 0.008f, 0.065f), gold, collider: false);
            bow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "Bit", root.transform, new Vector3(0.085f, -0.022f, 0f), new Vector3(0.03f, 0.03f, 0.018f), gold, collider: false);

            MakeGrabbable(root, "key", mass: 0.2f);
            return SavePrefab(root, "Key").GetComponent<NetworkObject>();
        }

        internal static void MakeGrabbable(GameObject root, string itemId, float mass)
        {
            var networkObject = root.AddComponent<NetworkObject>();
            networkObject.DontDestroyWithOwner = true; // a dropped item stays if its holder leaves

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.isKinematic = true;

            var networkTransform = root.AddComponent<NetworkTransform>();
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;

            var grabbable = root.AddComponent<NetworkGrabbable>();
            grabbable.itemId = itemId;
        }

        static GameObject BuildPlayerPrefab()
        {
            var root = new GameObject("NetworkPlayer");
            root.AddComponent<NetworkObject>();
            var player = root.AddComponent<NetworkPlayer>();

            var skin = Mat("Avatar", Color.white);
            var visorMat = Mat("Visor", new Color(0.05f, 0.05f, 0.07f));

            var head = NetworkedChild("Head", root.transform);
            var headVisual = Primitive(PrimitiveType.Sphere, "HeadVisual", head, Vector3.zero, new Vector3(0.18f, 0.22f, 0.2f), skin, collider: false);
            Primitive(PrimitiveType.Cube, "Visor", headVisual.transform, new Vector3(0f, 0.08f, 0.42f), new Vector3(0.85f, 0.3f, 0.25f), visorMat, collider: false);
            var label = Text("NameLabel", head, new Vector3(0f, 0.22f, 0f), "Player", 0.006f, Color.white);

            var left = NetworkedChild("LeftHand", root.transform);
            var leftVisual = Primitive(PrimitiveType.Cube, "LeftHandVisual", left, Vector3.zero, new Vector3(0.06f, 0.04f, 0.12f), skin, collider: false);
            var right = NetworkedChild("RightHand", root.transform);
            var rightVisual = Primitive(PrimitiveType.Cube, "RightHandVisual", right, Vector3.zero, new Vector3(0.06f, 0.04f, 0.12f), skin, collider: false);

            player.head = head;
            player.leftHand = left;
            player.rightHand = right;
            player.nameLabel = label;
            player.tintRenderers = new[] { headVisual.GetComponent<Renderer>(), leftVisual.GetComponent<Renderer>(), rightVisual.GetComponent<Renderer>() };
            player.hiddenForOwner = new[] { headVisual, label.gameObject };

            return SavePrefab(root, "NetworkPlayer");
        }

        static Transform NetworkedChild(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var networkTransform = go.AddComponent<NetworkTransform>();
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            networkTransform.SyncScaleX = networkTransform.SyncScaleY = networkTransform.SyncScaleZ = false;
            return go.transform;
        }

        static GameObject BuildPuzzlePrefab(NetworkObject gemRed, NetworkObject gemGreen, NetworkObject gemBlue, NetworkObject key)
        {
            var root = new GameObject("GemVaultPuzzle");
            root.AddComponent<NetworkObject>();
            var puzzle = root.AddComponent<GemVaultPuzzle>();

            var stone = Mat("Stone", new Color(0.55f, 0.55f, 0.58f));
            var wood = Mat("Wood", new Color(0.45f, 0.3f, 0.18f));
            var darkWood = Mat("DarkWood", new Color(0.3f, 0.19f, 0.1f));
            var metal = Mat("Metal", new Color(0.35f, 0.36f, 0.4f));

            // Stage 1: three pedestals along the left (west) wall, one per gem colour.
            var gems = new[] { ("gem_red", Red), ("gem_green", Green), ("gem_blue", Blue) };
            var sockets = new List<ItemSocket>();
            for (int i = 0; i < gems.Length; i++)
            {
                var (itemId, color) = gems[i];
                var basePos = new Vector3(-2.3f, 0f, -1.2f + 1.2f * i);
                var pedestal = new GameObject($"Pedestal_{itemId}").transform;
                pedestal.SetParent(root.transform, false);
                pedestal.localPosition = basePos;

                Primitive(PrimitiveType.Cylinder, "Column", pedestal, new Vector3(0f, 0.5f, 0f), new Vector3(0.3f, 0.5f, 0.3f), stone);
                var ring = Primitive(PrimitiveType.Cylinder, "Ring", pedestal, new Vector3(0f, 1.005f, 0f), new Vector3(0.24f, 0.01f, 0.24f), Mat($"Ring_{itemId}", color * 0.3f, emissive: true), collider: false);

                var socketGo = new GameObject("Socket");
                socketGo.transform.SetParent(pedestal, false);
                socketGo.transform.localPosition = new Vector3(0f, 1.07f, 0f);
                var socket = socketGo.AddComponent<ItemSocket>();
                socket.acceptsItemId = itemId;
                socket.snapRadius = 0.2f;
                socket.indicator = ring.GetComponent<Renderer>();
                socket.emptyColor = color * 0.3f;
                socket.filledColor = color;
                sockets.Add(socket);
            }

            Text("Hint", root.transform, new Vector3(-2.93f, 1.75f, 0f), "Return each gem\nto its pedestal", 0.012f, new Color(0.9f, 0.85f, 0.7f))
                .transform.localRotation = Quaternion.Euler(0f, -90f, 0f);

            // Stage 2: a chest against the back (south) wall.
            var chest = new GameObject("Chest").transform;
            chest.SetParent(root.transform, false);
            chest.localPosition = new Vector3(0f, 0f, -2.3f);
            Primitive(PrimitiveType.Cube, "Base", chest, new Vector3(0f, 0.225f, 0f), new Vector3(0.8f, 0.45f, 0.5f), wood);
            var lidPivot = new GameObject("LidPivot").transform;
            lidPivot.SetParent(chest, false);
            lidPivot.localPosition = new Vector3(0f, 0.45f, -0.25f); // hinge on the back edge
            Primitive(PrimitiveType.Cube, "Lid", lidPivot, new Vector3(0f, 0.05f, 0.25f), new Vector3(0.82f, 0.1f, 0.52f), darkWood);
            var keySpawn = new GameObject("KeySpawnPoint").transform;
            keySpawn.SetParent(chest, false);
            keySpawn.localPosition = new Vector3(0f, 0.5f, 0.05f);

            // Stage 3: the exit door in the front (north) wall and a lock plate beside it.
            float frontZ = RoomDepth / 2f;
            var doorPivot = new GameObject("DoorPivot").transform;
            doorPivot.SetParent(root.transform, false);
            doorPivot.localPosition = new Vector3(-DoorWidth / 2f, 0f, frontZ);
            Primitive(PrimitiveType.Cube, "DoorPanel", doorPivot, new Vector3(DoorWidth / 2f, DoorHeight / 2f, 0f), new Vector3(DoorWidth - 0.02f, DoorHeight - 0.01f, 0.06f), darkWood);
            Primitive(PrimitiveType.Sphere, "Handle", doorPivot, new Vector3(DoorWidth - 0.12f, 1.0f, -0.05f), Vector3.one * 0.06f, metal, collider: false);

            var lockPos = new Vector3(0.8f, 1.2f, frontZ);
            Primitive(PrimitiveType.Cube, "LockPlate", root.transform, lockPos + new Vector3(0f, 0f, -0.06f), new Vector3(0.2f, 0.3f, 0.04f), metal);
            var keyhole = Primitive(PrimitiveType.Cube, "Keyhole", root.transform, lockPos + new Vector3(0f, 0f, -0.085f), new Vector3(0.04f, 0.09f, 0.01f), Mat("Keyhole", new Color(0.1f, 0.1f, 0.1f), emissive: true), collider: false);
            var keySocketGo = new GameObject("KeySocket");
            keySocketGo.transform.SetParent(root.transform, false);
            keySocketGo.transform.localPosition = lockPos + new Vector3(0f, 0f, -0.2f);
            keySocketGo.transform.localRotation = Quaternion.Euler(0f, -90f, 0f); // key's +X (bit end) into the lock
            var keySocket = keySocketGo.AddComponent<ItemSocket>();
            keySocket.acceptsItemId = "key";
            keySocket.snapRadius = 0.18f;
            keySocket.indicator = keyhole.GetComponent<Renderer>();
            keySocket.emptyColor = new Color(0.1f, 0.1f, 0.1f);
            keySocket.filledColor = new Color(1f, 0.78f, 0.25f);

            var lamp = Primitive(PrimitiveType.Sphere, "StatusLamp", root.transform, new Vector3(1.4f, 2.3f, frontZ - 0.1f), Vector3.one * 0.15f, Mat("Lamp", Red, emissive: true), collider: false);
            var lightGo = new GameObject("LampLight");
            lightGo.transform.SetParent(lamp.transform, false);
            var lampLight = lightGo.AddComponent<Light>();
            lampLight.type = LightType.Point;
            lampLight.range = 4f;
            lampLight.intensity = 1.5f;

            var banner = Text("EscapedBanner", root.transform, new Vector3(0f, 2.35f, frontZ - 0.15f), "YOU ESCAPED!", 0.03f, new Color(0.3f, 1f, 0.4f));
            banner.gameObject.SetActive(false);

            puzzle.gemSockets = sockets.ToArray();
            puzzle.startingItems = new[]
            {
                new GemVaultPuzzle.ItemSpawn { prefab = gemRed, localPosition = new Vector3(-2.4f, 0.57f, -2.2f) },    // on the back-left crate
                new GemVaultPuzzle.ItemSpawn { prefab = gemGreen, localPosition = new Vector3(2.4f, 0.57f, 2.2f) },    // on the front-right crate
                new GemVaultPuzzle.ItemSpawn { prefab = gemBlue, localPosition = new Vector3(0f, 0.61f, -2.3f) },      // on top of the locked chest
            };
            puzzle.chestLidPivot = lidPivot;
            puzzle.keyPrefab = key;
            puzzle.keySpawnPoint = keySpawn;
            puzzle.keySocket = keySocket;
            puzzle.doorPivot = doorPivot;
            puzzle.escapedBanner = banner.gameObject;
            puzzle.lamp = lamp.GetComponent<Renderer>();
            puzzle.lampLight = lampLight;

            return SavePrefab(root, "GemVaultPuzzle");
        }

        internal static NetworkPrefabsList EnsureNetworkPrefabsList(params Object[] prefabs)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);
            if (!list)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, NetworkPrefabsListPath);
            }

            foreach (var prefab in prefabs)
            {
                var go = prefab is Component c ? c.gameObject : (GameObject)prefab;
                if (!list.Contains(go))
                    list.Add(new NetworkPrefab { Prefab = go });
            }

            EditorUtility.SetDirty(list);
            return list;
        }

        // ------------------------------------------------------------------ scene

        static void BuildScene(GameObject playerPrefab, GameObject puzzlePrefab, NetworkPrefabsList prefabsList)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.GetComponent<Camera>())
                    Object.DestroyImmediate(go);
            }

            // XR rig
            var rig = CreateCameraRig();
            var passthrough = rig.AddComponent<OVRPassthroughLayer>();
            passthrough.overlayType = OVROverlay.OverlayType.Underlay;
            passthrough.enabled = false;
            var ovrManager = rig.GetComponent<OVRManager>();
            if (ovrManager)
                ovrManager.isInsightPassthroughEnabled = true;

            // Networking
            var networkGo = new GameObject("[Network]");
            var networkManager = networkGo.AddComponent<NetworkManager>();
            var transport = networkGo.AddComponent<UnityTransport>();
            if (networkManager.NetworkConfig == null)
                networkManager.NetworkConfig = new NetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
            if (!networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists.Contains(prefabsList))
                networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabsList);
            var spawner = networkGo.AddComponent<ServerSpawner>();
            spawner.networkManager = networkManager;
            spawner.entries.Add(new ServerSpawner.Entry { prefab = puzzlePrefab, position = Vector3.zero });

            // Room content (hidden until this headset is aligned to the shared origin)
            var room = new GameObject("[Room]");
            var shell = BuildRoomShell(room.transform);
            var props = new GameObject("Props").transform;
            props.SetParent(room.transform, false);
            var crate = Mat("Crate", new Color(0.6f, 0.45f, 0.25f));
            Primitive(PrimitiveType.Cube, "Crate_BackLeft", props, new Vector3(-2.4f, 0.25f, -2.2f), Vector3.one * 0.5f, crate);
            Primitive(PrimitiveType.Cube, "Crate_FrontRight", props, new Vector3(2.4f, 0.25f, 2.2f), Vector3.one * 0.5f, crate);
            new GameObject(ImageBlasterImporter.ImportRootName).transform.SetParent(room.transform, false);

            // Session flow
            var sessionGo = new GameObject("[Session]");
            var aligner = sessionGo.AddComponent<RoomAligner>();
            sessionGo.AddComponent<LocalRigBinder>();
            var toggle = sessionGo.AddComponent<PassthroughToggle>();
            toggle.passthroughLayer = passthrough;
            toggle.hideInPassthrough = new[] { shell };

            var hudGo = new GameObject("StatusHud");
            hudGo.transform.SetParent(sessionGo.transform, false);
            var hud = hudGo.AddComponent<StatusHud>();
            hud.text = Text("Text", hudGo.transform, Vector3.zero, "Starting...", 0.004f, Color.white);

            var bootstrapper = sessionGo.AddComponent<SessionBootstrapper>();
            bootstrapper.networkManager = networkManager;
            bootstrapper.transport = transport;
            bootstrapper.aligner = aligner;
            bootstrapper.hud = hud;
            bootstrapper.roomContent = room;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static GameObject CreateCameraRig()
        {
            foreach (var guid in AssetDatabase.FindAssets("OVRCameraRig t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("/OVRCameraRig.prefab"))
                    continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.name = "[XR] OVRCameraRig";
                return instance;
            }

            Debug.LogWarning("[EscapeRoom] OVRCameraRig prefab not found in the Meta XR Core SDK; building a bare rig instead.");
            var rig = new GameObject("[XR] OVRCameraRig");
            rig.AddComponent<OVRCameraRig>();
            rig.AddComponent<OVRManager>();
            return rig;
        }

        static GameObject BuildRoomShell(Transform parent)
        {
            var shell = new GameObject("RoomShell").transform;
            shell.SetParent(parent, false);

            var floorMat = Mat("Floor", new Color(0.32f, 0.27f, 0.22f));
            var wallMat = Mat("Wall", new Color(0.72f, 0.68f, 0.6f));

            float halfW = RoomWidth / 2f, halfD = RoomDepth / 2f, t = WallThickness;
            Primitive(PrimitiveType.Cube, "Floor", shell, new Vector3(0f, -0.01f, 0f), new Vector3(RoomWidth, 0.02f, RoomDepth), floorMat);

            // Back, left and right walls sit just outside the 6 x 5.5 play area.
            Primitive(PrimitiveType.Cube, "Wall_Back", shell, new Vector3(0f, WallHeight / 2f, -halfD - t / 2f), new Vector3(RoomWidth + 2 * t, WallHeight, t), wallMat);
            Primitive(PrimitiveType.Cube, "Wall_Left", shell, new Vector3(-halfW - t / 2f, WallHeight / 2f, 0f), new Vector3(t, WallHeight, RoomDepth), wallMat);
            Primitive(PrimitiveType.Cube, "Wall_Right", shell, new Vector3(halfW + t / 2f, WallHeight / 2f, 0f), new Vector3(t, WallHeight, RoomDepth), wallMat);

            // Front wall with a doorway in the middle.
            float sideWidth = (RoomWidth + 2 * t - DoorWidth) / 2f;
            float frontZ = halfD + t / 2f;
            Primitive(PrimitiveType.Cube, "Wall_Front_Left", shell, new Vector3(-(DoorWidth + sideWidth) / 2f, WallHeight / 2f, frontZ), new Vector3(sideWidth, WallHeight, t), wallMat);
            Primitive(PrimitiveType.Cube, "Wall_Front_Right", shell, new Vector3((DoorWidth + sideWidth) / 2f, WallHeight / 2f, frontZ), new Vector3(sideWidth, WallHeight, t), wallMat);
            Primitive(PrimitiveType.Cube, "Wall_Front_Header", shell, new Vector3(0f, (WallHeight + DoorHeight) / 2f, frontZ), new Vector3(DoorWidth, WallHeight - DoorHeight, t), wallMat);
            Primitive(PrimitiveType.Cube, "ExitGlow", shell, new Vector3(0f, DoorHeight / 2f, halfD + 0.25f), new Vector3(DoorWidth, DoorHeight, 0.02f), Mat("ExitGlow", new Color(1f, 0.97f, 0.85f), emissive: true), collider: false);

            return shell.gameObject;
        }

        // ------------------------------------------------------------------ helpers

        static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static TextMesh Text(string name, Transform parent, Vector3 localPosition, string content, float characterSize, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var text = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font;
            text.fontSize = 64;
            text.characterSize = characterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            text.text = content;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return text;
        }

        static Material Mat(string name, Color color, bool emissive = false)
        {
            var path = $"{MaterialDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            if (emissive)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetColor("_EmissionColor", color * 0.8f);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        static GameObject SavePrefab(GameObject root, string name)
        {
            var path = $"{PrefabDir}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
