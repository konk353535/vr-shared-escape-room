using System.Collections.Generic;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace EscapeRoom.Editor.XRSetup
{
    /// <summary>
    /// Enables the OpenXR loader for Android (the headset) and Standalone (Quest Link in the
    /// editor), and turns on the Meta OpenXR features the Meta XR Core SDK needs.
    /// </summary>
    public static class XRPluginSetup
    {
        const string OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";
        const string SettingsAssetPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";

        // Matched by type name so this compiles whichever of these packages are present.
        static readonly HashSet<string> FeaturesToEnable = new HashSet<string>
        {
            "MetaXRFeature",                      // Meta XR Core SDK
            "MetaXRFoveationFeature",             // Meta XR Core SDK
            "MetaQuestFeature",                   // Unity OpenXR: Meta Quest support (Android)
            "OculusTouchControllerProfile",
            "MetaQuestTouchPlusControllerProfile", // Quest 3 controllers
            "MetaQuestTouchProControllerProfile",
        };

        [MenuItem("Escape Room/Setup/Configure XR Plug-in (OpenXR + Meta)", priority = 100)]
        public static void Configure()
        {
            var perTarget = GetOrCreatePerTargetSettings();
            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                var general = perTarget.SettingsForBuildTarget(group);
                if (!general)
                {
                    general = ScriptableObject.CreateInstance<XRGeneralSettings>();
                    general.name = $"{group} Settings";
                    perTarget.SetSettingsForBuildTarget(group, general);
                    AssetDatabase.AddObjectToAsset(general, perTarget);
                }

                if (!general.Manager)
                {
                    var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                    manager.name = $"{group} Providers";
                    general.Manager = manager;
                    AssetDatabase.AddObjectToAsset(manager, perTarget);
                }

                if (!XRPackageMetadataStore.AssignLoader(general.Manager, OpenXRLoader, group))
                    Debug.LogWarning($"[EscapeRoom] Could not assign the OpenXR loader for {group} (it may already be assigned).");

                EnableFeatures(group);
                EditorUtility.SetDirty(general);
                EditorUtility.SetDirty(general.Manager);
            }

            EditorUtility.SetDirty(perTarget);
            AssetDatabase.SaveAssets();
        }

        static XRGeneralSettingsPerBuildTarget GetOrCreatePerTargetSettings()
        {
            if (EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget existing) && existing)
                return existing;

            if (!AssetDatabase.IsValidFolder("Assets/XR"))
                AssetDatabase.CreateFolder("Assets", "XR");

            var created = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(created, SettingsAssetPath);
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, created, true);
            return created;
        }

        static void EnableFeatures(BuildTargetGroup group)
        {
            FeatureHelpers.RefreshFeatures(group);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (!settings)
            {
                Debug.LogWarning($"[EscapeRoom] No OpenXR settings for {group} yet. Open Project Settings > XR Plug-in Management > OpenXR once, then run this again.");
                return;
            }

            foreach (var feature in settings.GetFeatures())
            {
                if (FeaturesToEnable.Contains(feature.GetType().Name) && !feature.enabled)
                {
                    feature.enabled = true;
                    EditorUtility.SetDirty(feature);
                    Debug.Log($"[EscapeRoom] Enabled OpenXR feature {feature.GetType().Name} for {group}");
                }
            }
            EditorUtility.SetDirty(settings);
        }
    }
}
