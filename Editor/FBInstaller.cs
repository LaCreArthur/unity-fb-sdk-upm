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
        private const string EDM_PACKAGE_ID = "com.google.external-dependency-manager";
        private const string EDM_GIT_URL = "https://github.com/googlesamples/unity-jar-resolver.git?path=upm";

        static FBInstaller()
        {
            // 1. Fix Environment (Runs immediately)
            FixBuildEnvironment();

            // 2. Check Dependencies & Settings (Runs after a short delay to allow Unity to init)
            EditorApplication.delayCall += RunDelayedChecks;
        }

        private static void RunDelayedChecks()
        {
            InjectEDM();
            ConfigureEDM();
            CreateFacebookSettings();
        }

        // ---------------------------------------------------------
        // 1. CROSS-PLATFORM ENVIRONMENT FIXER
        // ---------------------------------------------------------
        private static void FixBuildEnvironment()
        {
            // A. Ensure Directory Structure Exists (Fixes 'DirectoryNotFoundException')
            string[] requiredDirs =
            {
                Path.Combine(Application.dataPath, "Plugins", "Android"),
                Path.Combine(Application.dataPath, "Resources")
            };

            foreach (var dir in requiredDirs)
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

            // B. Fix 'JAVA_HOME' for Android Builds
            // We find the JDK path using Unity's internal API or a universal fallback.
            var unityJdk = GetUnityJDKPath();

            if (!string.IsNullOrEmpty(unityJdk) && Directory.Exists(unityJdk))
            {
                // Set the Environment Variable for THIS process (Unity Editor)
                // Gradle (child process) will inherit this variable.
                var currentJavaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
                if (currentJavaHome != unityJdk) Environment.SetEnvironmentVariable("JAVA_HOME", unityJdk);
                // Only log if we actually changed it to avoid spam
                // Debug.Log($"[FB Installer] Set JAVA_HOME to: {unityJdk}"); 
            }
        }

        // Helper to find JDK on both Windows and Mac
        private static string GetUnityJDKPath()
        {
            var jdkPath = "";

            // 1. Try Unity API (Reflection avoids compile errors if Android Module is missing)
            try
            {
                var settingsType =
                    Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
                if (settingsType != null)
                {
                    var jdkProp = settingsType.GetProperty("jdkRootPath", BindingFlags.Static | BindingFlags.Public);
                    if (jdkProp != null) jdkPath = (string)jdkProp.GetValue(null);
                }
            }
            catch
            {
            }

            // 2. Universal Fallback (If API returns null/empty, commonly happens on fresh load)
            // EditorApplication.applicationContentsPath works on both Mac (.../Contents) and Win (.../Data)
            if (string.IsNullOrEmpty(jdkPath))
            {
                var contentsPath = EditorApplication.applicationContentsPath;
                var potentialPath = Path.Combine(contentsPath, "PlaybackEngines", "AndroidPlayer", "OpenJDK");

                if (Directory.Exists(potentialPath)) jdkPath = potentialPath;
            }

            return jdkPath;
        }

        // ---------------------------------------------------------
        // 2. DEPENDENCY INJECTION (NO JSON DEPENDENCY)
        // ---------------------------------------------------------
        private static void InjectEDM()
        {
            var manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            if (!File.Exists(manifestPath)) return;

            try
            {
                var jsonText = File.ReadAllText(manifestPath);
                // Simple string check is faster and dependency-free
                if (!jsonText.Contains(EDM_PACKAGE_ID))
                {
                    Debug.Log("<b>[FB Installer]</b> Injecting Google EDM to manifest.json...");

                    // Find the "dependencies" block start
                    var depIndex = jsonText.IndexOf("\"dependencies\": {");
                    if (depIndex != -1)
                    {
                        // Insert our package at the top of the dependencies list
                        var insertion = $"\n    \"{EDM_PACKAGE_ID}\": \"{EDM_GIT_URL}\",";
                        var newJson = jsonText.Insert(depIndex + 17, insertion);

                        File.WriteAllText(manifestPath, newJson);
                        AssetDatabase.Refresh(); // Force Unity to resolve
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[FB Installer] Manifest inject failed: {e.Message}");
            }
        }

        // ---------------------------------------------------------
        // 3. CONFIGURE GOOGLE EDM (REFLECTION)
        // ---------------------------------------------------------
        private static void ConfigureEDM()
        {
            // Tries to find Google's settings class (works for EDM 1.2.x versions)
            var settingsType = Type.GetType("Google.Android.Resolver.AndroidResolverSettings, Google.JarResolver");
            if (settingsType == null)
                settingsType = Type.GetType("GooglePlayServices.SettingsDialog, Google.JarResolver");

            if (settingsType == null) return;

            try
            {
                // We want to set UseJavaHome = false
                var useJavaHome = settingsType.GetProperty("UseJavaHome",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);

                // Sometimes it's an instance property, sometimes static depending on version
                if (useJavaHome != null)
                {
                    if ((bool)useJavaHome.GetValue(null)) useJavaHome.SetValue(null, false);
                    // Debug.Log("[FB Installer] Disabled 'Use JAVA_HOME' in EDM.");
                }
                else
                {
                    // Try Instance approach
                    var instanceProp = settingsType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
                    if (instanceProp != null)
                    {
                        var instance = instanceProp.GetValue(null);
                        useJavaHome = instance.GetType().GetProperty("UseJavaHome");
                        if (useJavaHome != null && (bool)useJavaHome.GetValue(instance))
                            useJavaHome.SetValue(instance, false);
                        // Debug.Log("[FB Installer] Disabled 'Use JAVA_HOME' in EDM (Instance).");
                    }
                }
            }
            catch
            {
            }
        }

        // ---------------------------------------------------------
        // 4. CREATE WRITABLE SETTINGS ASSET
        // ---------------------------------------------------------
        private static void CreateFacebookSettings()
        {
            var path = "Assets/Resources/FacebookSettings.asset";
            if (File.Exists(path)) return;

            // Use Reflection to create the asset so we don't strictly depend on the namespace being ready
            var fbSettingsType = Type.GetType("Facebook.Unity.Settings.FacebookSettings, Facebook.Unity.Settings");
            if (fbSettingsType == null) fbSettingsType = Type.GetType("Facebook.Unity.Settings.FacebookSettings");

            if (fbSettingsType != null)
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
    }
}