using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EscapeRoom.Puzzle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EscapeRoom.Editor
{
    /// <summary>
    /// Brings an image-blaster world (https://github.com/neilsonnn/image-blaster) into the open scene.
    ///
    /// Reads worlds/&lt;slug&gt;/scene.json (the placements saved by image-blaster's viewer) and
    /// worlds/&lt;slug&gt;/output/&lt;object&gt;/N-*.glb (Hunyuan 3D meshes), copies the meshes and SFX into
    /// Assets/EscapeRoom/Imported/&lt;slug&gt;, and lays them out under [Room]/ImageBlaster:
    ///  - physics "rigidbody": becomes a networked, grabbable prefab, placed via a SpawnMarker
    ///  - physics "static": a plain mesh with a MeshCollider
    ///  - physics "ghost": a plain mesh, no collider
    /// The World Labs collider mesh (output/world/N-world.glb) is added as an EditorOnly layout
    /// reference. Gaussian splats (.spz) are not imported: Unity has no built-in splat renderer
    /// and a colocated room is better served by real geometry.
    ///
    /// image-blaster's viewer is three.js (right-handed). glTFast converts meshes by mirroring X,
    /// so placements are mirrored the same way here.
    /// </summary>
    public static class ImageBlasterImporter
    {
        public const string ImportRootName = "ImageBlaster";
        const string ImportDir = "Assets/EscapeRoom/Imported";
        const string LastFolderPref = "EscapeRoom.ImageBlaster.LastFolder";

        static readonly string[] AudioExtensions = { ".mp3", ".wav", ".ogg", ".m4a" };
        static readonly Regex IndexedName = new Regex(@"^(\d+)-(.+)$");

        [MenuItem("Escape Room/3. Import image-blaster World...", priority = 30)]
        static void ImportFromMenu()
        {
            var start = EditorPrefs.GetString(LastFolderPref, "");
            var folder = EditorUtility.OpenFolderPanel("Choose an image-blaster world folder (image-blaster/worlds/<slug>)", start, "");
            if (string.IsNullOrEmpty(folder))
                return;

            EditorPrefs.SetString(LastFolderPref, folder);
            Import(folder);
        }

        public static void Import(string worldDir)
        {
            var slug = Path.GetFileName(worldDir.TrimEnd('/', '\\'));
            var outputDir = Path.Combine(worldDir, "output");
            if (!Directory.Exists(outputDir))
            {
                EditorUtility.DisplayDialog("image-blaster import", $"{worldDir} has no output/ folder. Pick a worlds/<slug> folder that image-blaster has generated.", "OK");
                return;
            }

            var objects = DiscoverObjects(outputDir);
            var scene = ReadSceneFile(Path.Combine(worldDir, "scene.json"));
            var placements = scene?.instances != null && scene.instances.Length > 0
                ? scene.instances.ToList()
                : DefaultPlacements(objects.Keys.ToList());

            var assetRoot = $"{ImportDir}/{Sanitize(slug)}";
            StarterRoomBuilder.EnsureFolder($"{assetRoot}/Models");
            StarterRoomBuilder.EnsureFolder($"{assetRoot}/Audio");
            StarterRoomBuilder.EnsureFolder($"{assetRoot}/Prefabs");

            // Copy source files into the project; glTFast's importer turns each .glb into a model asset.
            var modelPaths = new Dictionary<string, string>();
            var audioPaths = new Dictionary<string, List<string>>();
            foreach (var placement in placements)
            {
                if (modelPaths.ContainsKey(placement.objectId))
                    continue;

                if (!objects.TryGetValue(placement.objectId, out var source))
                {
                    Debug.LogWarning($"[EscapeRoom] scene.json references '{placement.objectId}' but no matching .glb was found under {outputDir}.");
                    continue;
                }

                var id = Sanitize(placement.objectId);
                modelPaths[placement.objectId] = CopyIntoProject(source.glbPath, $"{assetRoot}/Models/{id}.glb");
                audioPaths[placement.objectId] = source.sfxPaths
                    .Select(p => CopyIntoProject(p, $"{assetRoot}/Audio/{id}/{Path.GetFileName(p)}"))
                    .ToList();
            }

            var worldInfo = FindWorldCollider(Path.Combine(outputDir, "world"));
            string worldModelPath = worldInfo.glbPath != null ? CopyIntoProject(worldInfo.glbPath, $"{assetRoot}/Models/world-collider.glb") : null;

            AssetDatabase.Refresh();

            // Lay everything out in the open scene.
            var root = FindOrCreateImportRoot();
            var existing = root.Find(slug);
            if (existing)
                Object.DestroyImmediate(existing.gameObject);
            var worldRoot = new GameObject(slug).transform;
            worldRoot.SetParent(root, false);

            int networked = 0, placed = 0;
            foreach (var placement in placements)
            {
                if (!modelPaths.TryGetValue(placement.objectId, out var modelPath))
                    continue;

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (!model)
                {
                    Debug.LogError($"[EscapeRoom] {modelPath} did not import as a model. Is the glTFast package (com.unity.cloud.gltfast) installed?");
                    continue;
                }

                var physics = string.IsNullOrEmpty(placement.physics) ? "rigidbody" : placement.physics;
                var clips = audioPaths[placement.objectId].Select(AssetDatabase.LoadAssetAtPath<AudioClip>).Where(c => c).ToArray();
                var instanceName = string.IsNullOrEmpty(placement.instanceId) ? placement.objectId : placement.instanceId;

                GameObject instance;
                if (physics == "rigidbody")
                {
                    var prefab = BuildNetworkedPrefab(model, placement.objectId, clips, $"{assetRoot}/Prefabs/{Sanitize(placement.objectId)}.prefab");
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    instance.AddComponent<SpawnMarker>().networkPrefab = prefab;
                    networked++;
                }
                else
                {
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    if (physics == "static")
                        AddMeshColliders(instance, convex: false);
                    placed++;
                }

                instance.name = instanceName;
                instance.transform.SetParent(worldRoot, false);
                instance.transform.localPosition = ToUnityPosition(placement.position);
                instance.transform.localRotation = ToUnityRotation(placement.rotation);
                instance.transform.localScale = ToUnityScale(placement.scale);
            }

            if (worldModelPath != null)
            {
                var worldModel = AssetDatabase.LoadAssetAtPath<GameObject>(worldModelPath);
                if (worldModel)
                {
                    float metricScale = scene != null && scene.metricScaleFactor > 0f ? scene.metricScaleFactor : worldInfo.metricScaleFactor;
                    float groundOffset = scene != null && scene.groundPlaneOffset > SceneFile.Unset ? scene.groundPlaneOffset : worldInfo.groundPlaneOffset;

                    var reference = (GameObject)PrefabUtility.InstantiatePrefab(worldModel);
                    reference.name = "WorldColliderReference (editor only)";
                    reference.tag = "EditorOnly";
                    reference.transform.SetParent(worldRoot, false);
                    reference.transform.localPosition = new Vector3(0f, groundOffset, 0f);
                    reference.transform.localRotation = worldInfo.flipY ? Quaternion.Euler(180f, 0f, 0f) : Quaternion.identity;
                    reference.transform.localScale = Vector3.one * metricScale;
                }
            }

            EditorSceneManager.MarkSceneDirty(worldRoot.gameObject.scene);
            Selection.activeTransform = worldRoot;
            Debug.Log($"[EscapeRoom] Imported image-blaster world '{slug}': {networked} grabbable props, {placed} static props. " +
                      "Move them into the 6m x 5.5m room (origin = floor centre, +Z = front wall) and save the scene.");
        }

        // ------------------------------------------------------------------ prefab building

        static GameObject BuildNetworkedPrefab(GameObject model, string objectId, AudioClip[] clips, string prefabPath)
        {
            var root = new GameObject(objectId);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.transform.SetParent(root.transform, false);
            AddMeshColliders(visual, convex: true);

            StarterRoomBuilder.MakeGrabbable(root, objectId, mass: 1f);

            if (clips.Length > 0)
            {
                root.AddComponent<AudioSource>();
                root.AddComponent<ImpactSound>().clips = clips;
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            StarterRoomBuilder.EnsureNetworkPrefabsList(prefab);
            return prefab;
        }

        static void AddMeshColliders(GameObject go, bool convex)
        {
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.sharedMesh || filter.GetComponent<Collider>())
                    continue;
                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = convex;
            }
        }

        static Transform FindOrCreateImportRoot()
        {
            foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                var found = go.name == ImportRootName ? go.transform : go.transform.Find(ImportRootName);
                if (found)
                    return found;
            }

            var room = GameObject.Find("[Room]");
            var root = new GameObject(ImportRootName).transform;
            if (room)
                root.SetParent(room.transform, false);
            return root;
        }

        // ------------------------------------------------------------------ coordinate conversion

        /// <summary>three.js (right-handed, Y up) to Unity (left-handed, Y up), mirroring X like glTFast.</summary>
        public static Vector3 ToUnityPosition(float[] p) =>
            p == null || p.Length < 3 ? Vector3.zero : new Vector3(-p[0], p[1], p[2]);

        public static Vector3 ToUnityScale(float[] s) =>
            s == null || s.Length < 3 ? Vector3.one : new Vector3(s[0], s[1], s[2]);

        /// <summary>three.js Euler (radians, XYZ order) to a Unity rotation.</summary>
        public static Quaternion ToUnityRotation(float[] r)
        {
            if (r == null || r.Length < 3)
                return Quaternion.identity;

            // Same as THREE.Quaternion.setFromEuler with order 'XYZ'.
            double c1 = Math.Cos(r[0] / 2), c2 = Math.Cos(r[1] / 2), c3 = Math.Cos(r[2] / 2);
            double s1 = Math.Sin(r[0] / 2), s2 = Math.Sin(r[1] / 2), s3 = Math.Sin(r[2] / 2);
            double x = s1 * c2 * c3 + c1 * s2 * s3;
            double y = c1 * s2 * c3 - s1 * c2 * s3;
            double z = c1 * c2 * s3 + s1 * s2 * c3;
            double w = c1 * c2 * c3 - s1 * s2 * s3;

            // Mirroring X flips the handedness: negate the Y and Z components.
            return new Quaternion((float)x, (float)-y, (float)-z, (float)w);
        }

        // ------------------------------------------------------------------ reading image-blaster output

        [Serializable]
        class SceneFile
        {
            public const float Unset = -99999f;
            public int version;
            public Placement[] instances;
            public float metricScaleFactor = Unset;
            public float groundPlaneOffset = Unset;
        }

        [Serializable]
        class Placement
        {
            public string instanceId;
            public string objectId;
            public string assetId;
            public string physics;
            public float[] position;
            public float[] rotation;
            public float[] scale;
        }

        [Serializable] class WorldFile { public WorldAssets assets; }
        [Serializable] class WorldFileWrapper { public WorldFile world; }
        [Serializable] class WorldAssets { public WorldSplats splats; }
        [Serializable] class WorldSplats { public WorldSemantics semantics_metadata; }
        [Serializable] class WorldSemantics { public float metric_scale_factor = 1f; public float ground_plane_offset; public bool flip_y; }

        class ObjectSource
        {
            public string glbPath;
            public List<string> sfxPaths = new List<string>();
        }

        struct WorldColliderInfo
        {
            public string glbPath;
            public float metricScaleFactor;
            public float groundPlaneOffset;
            public bool flipY;
        }

        static SceneFile ReadSceneFile(string path)
        {
            if (!File.Exists(path))
                return null;
            try
            {
                return JsonUtility.FromJson<SceneFile>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EscapeRoom] Could not read {path}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Mirrors image-blaster's viewer: every folder under output/ except world and sfx is an
        /// object; each N-*.glb inside it is variant N with id "&lt;folder&gt;-N" (or just "&lt;folder&gt;"
        /// when the file isn't indexed).
        /// </summary>
        static Dictionary<string, ObjectSource> DiscoverObjects(string outputDir)
        {
            var result = new Dictionary<string, ObjectSource>();
            foreach (var dir in Directory.GetDirectories(outputDir).OrderBy(d => d))
            {
                var folder = Path.GetFileName(dir);
                if (folder == "world" || folder == "sfx")
                    continue;

                var sfxDir = Path.Combine(dir, "sfx");
                var sfx = Directory.Exists(sfxDir)
                    ? Directory.GetFiles(sfxDir).Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()) && !Path.GetFileName(f).StartsWith(".")).OrderBy(f => f).ToList()
                    : new List<string>();

                foreach (var glb in Directory.GetFiles(dir, "*.glb").OrderBy(f => f))
                {
                    var fileName = Path.GetFileNameWithoutExtension(glb);
                    if (fileName.StartsWith("."))
                        continue;

                    var match = IndexedName.Match(fileName);
                    var id = match.Success ? $"{folder}-{int.Parse(match.Groups[1].Value)}" : folder;
                    result[id] = new ObjectSource { glbPath = glb, sfxPaths = sfx };
                }
            }
            return result;
        }

        /// <summary>Same default grid image-blaster's viewer uses when scene.json has no placements.</summary>
        static List<Placement> DefaultPlacements(List<string> objectIds)
        {
            int columns = Mathf.Min(3, Mathf.Max(1, objectIds.Count));
            return objectIds.Select((id, i) => new Placement
            {
                instanceId = id,
                objectId = id,
                physics = "rigidbody",
                position = new[] { (i % columns - (columns - 1) / 2f) * 1f, 0f, -2f - (i / columns) },
                rotation = new[] { 0f, 0f, 0f },
                scale = new[] { 1f, 1f, 1f },
            }).ToList();
        }

        static WorldColliderInfo FindWorldCollider(string worldDir)
        {
            var info = new WorldColliderInfo { metricScaleFactor = 1f };
            if (!Directory.Exists(worldDir))
                return info;

            // Newest generation wins: N-world.glb with the highest N.
            var latest = Directory.GetFiles(worldDir, "*-world.glb")
                .Select(f => (path: f, match: IndexedName.Match(Path.GetFileNameWithoutExtension(f))))
                .Where(x => x.match.Success)
                .OrderByDescending(x => int.Parse(x.match.Groups[1].Value))
                .FirstOrDefault();
            if (latest.path == null)
                return info;

            info.glbPath = latest.path;
            var jsonPath = Path.Combine(worldDir, $"{latest.match.Groups[1].Value}-world.json");
            if (File.Exists(jsonPath))
            {
                try
                {
                    // N-world.json is either the World Labs world object or wrapped in { "world": ... }.
                    // JsonUtility fills missing nested objects with defaults, so prefer whichever parse found values.
                    var json = File.ReadAllText(jsonPath);
                    var direct = JsonUtility.FromJson<WorldFile>(json)?.assets?.splats?.semantics_metadata;
                    var wrapped = JsonUtility.FromJson<WorldFileWrapper>(json)?.world?.assets?.splats?.semantics_metadata;
                    var semantics = HasValues(direct) ? direct : wrapped;
                    if (semantics != null)
                    {
                        info.metricScaleFactor = semantics.metric_scale_factor > 0f ? semantics.metric_scale_factor : 1f;
                        info.groundPlaneOffset = semantics.ground_plane_offset;
                        info.flipY = semantics.flip_y;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EscapeRoom] Could not read {jsonPath}: {e.Message}");
                }
            }
            return info;
        }

        static bool HasValues(WorldSemantics s) =>
            s != null && (!Mathf.Approximately(s.metric_scale_factor, 1f) || !Mathf.Approximately(s.ground_plane_offset, 0f) || s.flip_y);

        // ------------------------------------------------------------------ file helpers

        static string CopyIntoProject(string sourcePath, string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var destination = Path.Combine(projectRoot, assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(sourcePath, destination, overwrite: true);
            return assetPath;
        }

        static string Sanitize(string name) => Regex.Replace(name, @"[^A-Za-z0-9_\-]", "_");
    }
}
