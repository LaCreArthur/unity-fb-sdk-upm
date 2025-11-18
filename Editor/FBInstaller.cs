using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Facebook.Unity.Editor
{
    [InitializeOnLoad]
    public class FBInstaller
    {
        static FBInstaller()
        {
            Debug.Log("<b>[FB Installer]</b> Initializing...");
            // 1. Fix Environment (Runs immediately)
            FixBuildEnvironment();

            // 2. Check Dependencies & Settings (Runs after a short delay to allow Unity to init)
            EditorApplication.delayCall += RunDelayedChecks;
        }

        private static void RunDelayedChecks()
        {
            Debug.Log("<b>[FB Installer]</b> Running delayed checks...");
            ConfigureEDM();
            CreateFacebookSettings();
        }

        // ---------------------------------------------------------
        // 1. CROSS-PLATFORM ENVIRONMENT FIXER
        // ---------------------------------------------------------
        private static void FixBuildEnvironment()
        {
            Debug.Log("<b>[FB Installer]</b> Fixing build environment...");
            // A. Ensure Directory Structure Exists (Fixes 'DirectoryNotFoundException')
            string[] requiredDirs =
            {
                Path.Combine(Application.dataPath, "Plugins", "Android"),
                Path.Combine(Application.dataPath, "Resources")
            };

            foreach (var dir in requiredDirs)
                if (!Directory.Exists(dir))
                {
                    Debug.Log($"<b>[FB Installer]</b> Creating required directory: {dir}");
                    Directory.CreateDirectory(dir);
                }

            // B. Fix 'JAVA_HOME' for Android Builds
            // We find the JDK path using Unity's internal API or a universal fallback.
            var unityJdk = GetUnityJDKPath();

            if (!string.IsNullOrEmpty(unityJdk) && Directory.Exists(unityJdk))
            {
                // Set the Environment Variable for THIS process (Unity Editor)
                // Gradle (child process) will inherit this variable.
                var currentJavaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
                if (currentJavaHome != unityJdk)
                {
                    Environment.SetEnvironmentVariable("JAVA_HOME", unityJdk);
                    Debug.Log($"<b>[FB Installer]</b> Set JAVA_HOME to: {unityJdk}");
                }
            }
            else
            {
                Debug.LogWarning("[FB Installer] Could not find Unity's JDK path. JAVA_HOME not set.");
            }
        }

        // Helper to find JDK on both Windows and Mac
        private static string GetUnityJDKPath()
        {
            var jdkPath = "";
            Debug.Log("<b>[FB Installer]</b> Searching for Unity JDK path...");

            // 1. Try Unity API (Reflection avoids compile errors if Android Module is missing)
            try
            {
                Debug.Log("<b>[FB Installer]</b> Trying Unity API for JDK path...");
                var settingsType =
                    Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
                if (settingsType != null)
                {
                    var jdkProp = settingsType.GetProperty("jdkRootPath", BindingFlags.Static | BindingFlags.Public);
                    if (jdkProp != null)
                    {
                        jdkPath = (string)jdkProp.GetValue(null);
                        Debug.Log($"<b>[FB Installer]</b> Found JDK path via API: {jdkPath}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FB Installer] Unity API for JDK path failed: {e.Message}");
            }

            // 2. Universal Fallback (If API returns null/empty, commonly happens on fresh load)
            // EditorApplication.applicationContentsPath works on both Mac (.../Contents) and Win (.../Data)
            if (string.IsNullOrEmpty(jdkPath))
            {
                Debug.Log("<b>[FB Installer]</b> JDK path not found via API, trying fallback...");
                var contentsPath = EditorApplication.applicationContentsPath;
                var potentialPath = Path.Combine(contentsPath, "PlaybackEngines", "AndroidPlayer", "OpenJDK");

                if (Directory.Exists(potentialPath))
                {
                    jdkPath = potentialPath;
                    Debug.Log($"<b>[FB Installer]</b> Found JDK path via fallback: {jdkPath}");
                }
                else
                {
                    Debug.LogWarning($"[FB Installer] Fallback JDK path not found at: {potentialPath}");
                }
            }

            return jdkPath;
        }

        // ---------------------------------------------------------
        // 2. CONFIGURE GOOGLE EDM (REFLECTION)
        // ---------------------------------------------------------
        private static void ConfigureEDM()
        {
            Debug.Log("<b>[FB Installer]</b> Configuring Google EDM settings via reflection...");
            // Tries to find Google's settings class (works for EDM 1.2.x versions)
            var settingsType = Type.GetType("Google.Android.Resolver.AndroidResolverSettings, Google.JarResolver");
            if (settingsType == null)
            {
                Debug.Log(
                    "[FB Installer] Could not find 'Google.Android.Resolver.AndroidResolverSettings', trying fallback...");
                settingsType = Type.GetType("GooglePlayServices.SettingsDialog, Google.JarResolver");
            }

            if (settingsType == null)
            {
                Debug.LogWarning("[FB Installer] Could not find any EDM settings type. Configuration skipped.");
                return;
            }

            Debug.Log($"<b>[FB Installer]</b> Found EDM settings type: {settingsType.FullName}");

            try
            {
                // We want to set UseJavaHome = false
                var useJavaHome = settingsType.GetProperty("UseJavaHome",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);

                // Sometimes it's an instance property, sometimes static depending on version
                if (useJavaHome != null)
                {
                    Debug.Log("[FB Installer] Found 'UseJavaHome' as a static property.");
                    if ((bool)useJavaHome.GetValue(null))
                    {
                        useJavaHome.SetValue(null, false);
                        Debug.Log("<b>[FB Installer]</b> Disabled 'Use JAVA_HOME' in EDM.");
                    }
                }
                else
                {
                    Debug.Log(
                        "[FB Installer] 'UseJavaHome' not found as a static property, trying instance approach...");
                    // Try Instance approach
                    var instanceProp = settingsType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
                    if (instanceProp != null)
                    {
                        var instance = instanceProp.GetValue(null);
                        useJavaHome = instance.GetType().GetProperty("UseJavaHome");
                        if (useJavaHome != null && (bool)useJavaHome.GetValue(instance))
                        {
                            useJavaHome.SetValue(instance, false);
                            Debug.Log("<b>[FB Installer]</b> Disabled 'Use JAVA_HOME' in EDM (Instance).");
                        }
                    }
                    else
                    {
                        Debug.LogWarning(
                            "[FB Installer] Could not find 'Instance' property on EDM settings. Configuration failed.");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[FB Installer] Failed to configure EDM: {e.Message}");
            }
        }

        // ---------------------------------------------------------
        // 3. CREATE WRITABLE SETTINGS ASSET
        // ---------------------------------------------------------
        private static void CreateFacebookSettings()
        {
            var path = "Assets/Resources/FacebookSettings.asset";
            Debug.Log($"<b>[FB Installer]</b> Checking for FacebookSettings asset at: {path}");
            if (File.Exists(path))
            {
                Debug.Log("<b>[FB Installer]</b> FacebookSettings.asset already exists.");
                return;
            }

            // Use Reflection to create the asset so we don't strictly depend on the namespace being ready
            var fbSettingsType = Type.GetType("Facebook.Unity.Settings.FacebookSettings, Facebook.Unity.Settings");
            if (fbSettingsType == null)
            {
                Debug.Log(
                    "[FB Installer] Could not find 'Facebook.Unity.Settings.FacebookSettings', trying fallback...");
                fbSettingsType = Type.GetType("Facebook.Unity.Settings.FacebookSettings");
            }


            if (fbSettingsType != null)
            {
                Debug.Log($"<b>[FB Installer]</b> Found FacebookSettings type: {fbSettingsType.FullName}");
                // Ensure only one exists
                if (AssetDatabase.LoadAssetAtPath(path, fbSettingsType) == null)
                {
                    var settings = ScriptableObject.CreateInstance(fbSettingsType);
                    AssetDatabase.CreateAsset(settings, path);
                    Debug.Log("<b>[FB Installer]</b> Created writable FacebookSettings.asset in Assets/Resources/");

                    // Select it so the user sees it immediately
                    Selection.activeObject = settings;
                }
            }
            else
            {
                Debug.LogWarning("[FB Installer] Could not find FacebookSettings type. Asset creation skipped.");
            }
        }
    }
}