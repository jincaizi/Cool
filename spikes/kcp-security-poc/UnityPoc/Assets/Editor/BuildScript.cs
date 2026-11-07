using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KcpPocUnity.Editor
{
    public static class BuildScript
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string ApkPath = "Build/KcpSecurityPoc.apk";

        public static void BuildAndroid()
        {
            ConfigureAndroidToolchain();
            EnsureScene();
            ConfigurePlayer();

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ApkPath)));

            BuildPlayerOptions options = new BuildPlayerOptions();
            options.scenes = new[] { ScenePath };
            options.locationPathName = ApkPath;
            options.target = BuildTarget.Android;
            options.options = BuildOptions.None;

            Debug.Log("BUILD_START unity=" + Application.unityVersion +
                      " target=" + EditorUserBuildSettings.activeBuildTarget +
                      " scripting_backend=" + PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) +
                      " architectures=" + PlayerSettings.Android.targetArchitectures +
                      " abi=" + PlayerSettings.Android.targetArchitectures +
                      " output=" + Path.GetFullPath(ApkPath));

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log("BUILD_RESULT=" + summary.result +
                      " size_bytes=" + summary.totalSize +
                      " errors=" + summary.totalErrors +
                      " output=" + summary.outputPath +
                      " duration_seconds=" + summary.totalTime.TotalSeconds.ToString("F1"));

            if (summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
                return;
            }

            EditorApplication.Exit(0);
        }

        private static void ConfigureAndroidToolchain()
        {
            string data = EditorApplication.applicationContentsPath;
            SetPath("AndroidSdkRoot", Path.Combine(data, "PlaybackEngines/AndroidPlayer/SDK"));
            SetPath("AndroidNdkRoot", Path.Combine(data, "PlaybackEngines/AndroidPlayer/NDK"));
            SetPath("AndroidNdkRootR23", Path.Combine(data, "PlaybackEngines/AndroidPlayer/NDK"));
            SetPath("JdkPath", Path.Combine(data, "PlaybackEngines/AndroidPlayer/OpenJDK"));
        }

        private static void SetPath(string key, string value)
        {
            if (Directory.Exists(value))
            {
                EditorPrefs.SetString(key, value.Replace('\\', '/'));
                Debug.Log("TOOLCHAIN " + key + "=" + value.Replace('\\', '/'));
            }
            else
            {
                Debug.LogWarning("TOOLCHAIN missing " + key + "=" + value);
            }
        }

        private static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.Refresh();
                Debug.Log("SCENE_CREATED " + ScenePath);
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "KcpSecurityPoc";
            PlayerSettings.productName = "KcpSecurityPoc";
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.poc.kcpsecurity");
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(
                BuildTargetGroup.Android, ApiCompatibilityLevel.NET_Standard_2_0);
            PlayerSettings.Android.targetArchitectures =
                AndroidArchitecture.ARM64 | AndroidArchitecture.X86_64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel22;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.forceInternetPermission = true;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
        }
    }
}
