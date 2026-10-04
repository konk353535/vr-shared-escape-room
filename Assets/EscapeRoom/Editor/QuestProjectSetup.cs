using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace EscapeRoom.Editor
{
    /// <summary>
    /// One-click Player Settings for Quest 3. Done in code rather than a checked-in
    /// ProjectSettings.asset so it stays readable and survives editor upgrades.
    /// Afterwards, run Meta > Tools > Project Setup Tool and "Fix All" for anything Meta adds.
    /// </summary>
    public static class QuestProjectSetup
    {
        public const string ApplicationId = "com.konk353535.vrsharedescaperoom";
        const string XRSetupMenu = "Escape Room/Setup/Configure XR Plug-in (OpenXR + Meta)";

        [MenuItem("Escape Room/0. Switch Build Target to Android (Quest)", priority = 0)]
        public static void SwitchToAndroid()
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                Debug.LogError("[EscapeRoom] Could not switch to Android. Install the Android Build Support module (with OpenJDK and Android SDK & NDK) from Unity Hub.");
        }

        [MenuItem("Escape Room/1. Configure Project for Quest 3", priority = 10)]
        public static void Configure()
        {
            PlayerSettings.companyName = "konk353535";
            PlayerSettings.productName = "VR Shared Escape Room";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);

            // Quest 3 requirements and Meta's recommended settings.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.colorSpace = ColorSpace.Linear;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

            // Netcode talks to the host headset over the LAN.
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.runInBackground = true;

            if (File.Exists(StarterRoomBuilder.ScenePath))
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(StarterRoomBuilder.ScenePath, true) };

            // XR loader setup lives in its own assembly so a package API change there can't
            // break the rest of the editor tooling.
            if (!EditorApplication.ExecuteMenuItem(XRSetupMenu))
                Debug.LogWarning("[EscapeRoom] XR setup menu unavailable. Enable OpenXR (Android + Standalone) with the Meta XR and Meta Quest features in Project Settings > XR Plug-in Management.");

            AssetDatabase.SaveAssets();
            Debug.Log("[EscapeRoom] Player settings configured for Quest 3. Now run Meta > Tools > Project Setup Tool and press Fix All.");
        }
    }
}
