using System.IO;
using System.Linq;
using System.Xml.Linq;
using Meta.XR;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEditor.Build.Reporting;

namespace QRLens.Editor
{
    public static class QRLensProjectSetup
    {
        private const string OpenXRLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
        private const string XRSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";

        [MenuItem("Tools/QR Lens/Configure Quest Project")]
        public static void ConfigureQuest()
        {
            ConfigureOpenXR();
            ConfigureMetaProject();
            AssetDatabase.SaveAssets();
            Debug.Log("QR Lens Quest project configuration complete.");
        }

        private static void ConfigureOpenXR()
        {
            var perBuildTarget = GetOrCreateGeneralSettings();
            if (!perBuildTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            }

            var managerSettings = perBuildTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!XRPackageMetadataStore.AssignLoader(managerSettings, OpenXRLoaderType, BuildTargetGroup.Android))
            {
                throw new System.InvalidOperationException("Could not assign the OpenXR loader for Android.");
            }

            var openXRSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (!openXRSettings)
            {
                FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
                openXRSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
                if (!openXRSettings)
                {
                    throw new System.InvalidOperationException("OpenXR Android settings were not created.");
                }
            }

            if (openXRSettings.GetFeatures().All(feature => feature.GetType().FullName != "Meta.XR.MetaXRFeature"))
            {
                FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            }

            openXRSettings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            EnableFeature(openXRSettings, "Meta.XR.MetaXRFeature");
            EnableFeature(
                openXRSettings,
                "UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile");
            EnableFeature(openXRSettings, "UnityEngine.XR.OpenXR.Features.Meta.OpenXRLifeCycleFeature");
            EnableFeature(
                openXRSettings,
                "UnityEngine.XR.OpenXR.Features.CompositionLayers.OpenXRCompositionLayersFeature");
            EditorUtility.SetDirty(openXRSettings);
        }

        [MenuItem("Tools/QR Lens/Build Quest APK")]
        public static void BuildQuestApk()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                throw new System.InvalidOperationException("Could not switch the Unity project to Android.");
            }

            ConfigureQuest();
            Directory.CreateDirectory("Builds/Android");
            var outputPath = $"Builds/Android/QR-Lens-v{PlayerSettings.bundleVersion}.apk";

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/QRLens/Scenes/Main.unity" },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                // Meta XR Core includes its editor-only Operator API layer in Development
                // builds. That layer is not part of QR Lens and can abort OpenXR startup on
                // device, so local Quest APKs intentionally use the normal player profile.
                options = BuildOptions.None
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new System.InvalidOperationException(
                    $"Quest APK build failed: {report.summary.result} ({report.summary.totalErrors} errors)");
            }

            ValidateGeneratedAndroidManifest();
            Debug.Log($"QR Lens APK built: {report.summary.outputPath}");
        }

        private static void ValidateGeneratedAndroidManifest()
        {
            const string manifestPath =
                "Library/Bee/Android/Prj/IL2CPP/Gradle/unityLibrary/src/main/AndroidManifest.xml";
            if (!File.Exists(manifestPath))
            {
                throw new System.InvalidOperationException(
                    $"Could not validate the generated Android manifest at {manifestPath}.");
            }

            var document = XDocument.Load(manifestPath);
            var root = document.Root ??
                       throw new System.InvalidOperationException("The generated Android manifest is empty.");
            XNamespace android = "http://schemas.android.com/apk/res/android";
            XNamespace horizonOs = "http://schemas.horizonos/sdk";

            RequireManifestEntry(root, "uses-permission", android, "horizonos.permission.HEADSET_CAMERA");
            RequireManifestEntry(root, "uses-feature", android, "com.oculus.feature.PASSTHROUGH");
            RejectManifestEntry(root, "uses-permission", android, "com.oculus.permission.USE_SCENE");
            RejectManifestEntry(root, "uses-permission", android, "com.oculus.permission.USE_ANCHOR_API");

            if (root.Elements("uses-feature").Any(element =>
                    (string)element.Attribute(android + "name") == "com.oculus.experimental.enabled"))
            {
                throw new System.InvalidOperationException(
                    "The generated manifest contains the prohibited com.oculus.experimental.enabled feature.");
            }

            var horizonSdk = root.Element(horizonOs + "uses-horizonos-sdk");
            if (horizonSdk == null ||
                !int.TryParse((string)horizonSdk.Attribute(horizonOs + "minSdkVersion"), out var minSdkVersion) ||
                minSdkVersion < 74)
            {
                throw new System.InvalidOperationException(
                    "The generated manifest must require Horizon OS SDK 74 or newer.");
            }
        }

        private static void RequireManifestEntry(
            XElement root,
            string elementName,
            XNamespace android,
            string requiredName)
        {
            if (root.Elements(elementName).Any(element =>
                    (string)element.Attribute(android + "name") == requiredName))
            {
                return;
            }

            throw new System.InvalidOperationException(
                $"The generated Android manifest is missing required entry {requiredName}.");
        }

        private static void RejectManifestEntry(
            XElement root,
            string elementName,
            XNamespace android,
            string rejectedName)
        {
            if (root.Elements(elementName).Any(element =>
                    (string)element.Attribute(android + "name") == rejectedName))
            {
                throw new System.InvalidOperationException(
                    $"The generated Android manifest contains unnecessary entry {rejectedName}.");
            }
        }

        private static XRGeneralSettingsPerBuildTarget GetOrCreateGeneralSettings()
        {
            if (EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(
                    XRGeneralSettings.k_SettingsKey,
                    out var existing) && existing)
            {
                return existing;
            }

            var existingGuid = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget").FirstOrDefault();
            if (!string.IsNullOrEmpty(existingGuid))
            {
                var path = AssetDatabase.GUIDToAssetPath(existingGuid);
                existing = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
            }

            if (!existing)
            {
                Directory.CreateDirectory("Assets/XR");
                existing = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(existing, XRSettingsPath);
            }

            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, existing, true);
            return existing;
        }

        private static void EnableFeature(OpenXRSettings settings, string fullTypeName)
        {
            var feature = settings.GetFeatures().FirstOrDefault(value => value.GetType().FullName == fullTypeName);
            if (!feature)
            {
                throw new System.InvalidOperationException($"Required OpenXR feature not found: {fullTypeName}");
            }

            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }

        private static void ConfigureMetaProject()
        {
            PlayerSettings.preserveFramebufferAlpha = true;
            var config = OVRProjectConfig.CachedProjectConfig;
            config.targetDeviceTypes = new System.Collections.Generic.List<OVRProjectConfig.DeviceType>
            {
                OVRProjectConfig.DeviceType.Quest3,
                OVRProjectConfig.DeviceType.Quest3S
            };
            config.anchorSupport = OVRProjectConfig.AnchorSupport.Disabled;
            config.sceneSupport = OVRProjectConfig.FeatureSupport.None;
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
            config.isPassthroughCameraAccessEnabled = true;
            config.experimentalFeaturesEnabled = false;
            config.minHorizonOsSdkVersion = 74;
            config.targetHorizonOsSdkVersion = OVRProjectConfig.currentSdkVersion;
            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            OVRProjectConfig.CommitProjectConfig(config);
        }
    }
}
