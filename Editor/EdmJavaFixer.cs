using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class EdmJavaFixer
{
    private static bool _applied;

    [InitializeOnLoadMethod]
    private static void StartWatching()
    {
        if (_applied) return;
        Debug.Log("<b>[FB SDK JavaFixer]</b> Started watching for EDM...");
        EditorApplication.delayCall += CheckAndFixLoop;
    }

    private static void CheckAndFixLoop()
    {
        if (_applied) return;

        // Search for ANY type that has the properties we need (UseJavaHome + JavaPath)
        var settingsType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(asm => asm.GetTypes())
            .FirstOrDefault(t =>
            {
                if (!t.Name.Contains("Resolver") && !t.Name.Contains("Settings")) return false;

                var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                var hasUseJavaHome = props.Any(p =>
                    string.Equals(p.Name, "UseJavaHome", StringComparison.OrdinalIgnoreCase));
                var hasJavaPath = props.Any(p =>
                    string.Equals(p.Name, "JavaPath", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Name, "javaPath", StringComparison.OrdinalIgnoreCase));
                return hasUseJavaHome && hasJavaPath;
            });

        if (settingsType == null)
        {
            Debug.Log("<b>[FB SDK JavaFixer]</b> EDM settings type not discovered yet — retrying...");
            EditorApplication.delayCall += CheckAndFixLoop;
            return;
        }

        Debug.Log(
            $"<b>[FB SDK JavaFixer]</b> EDM settings type discovered via brute-force: {settingsType.FullName} in {settingsType.Assembly.GetName().Name}");
        _applied = true;
        ApplyJavaFixAndResolve(settingsType);
    }

    private static void ApplyJavaFixAndResolve(Type settingsType)
    {
        // Get Instance (static property, common in all versions)
        var instanceProp = settingsType.GetProperty("Instance",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        var instance = instanceProp?.GetValue(null);
        if (instance == null)
        {
            Debug.LogError("[FB SDK JavaFixer] Could not get EDM Instance (property missing or null)");
            return;
        }

        // Flexible property access (case-insensitive + camelCase fallback)
        var useJavaHomeProp = settingsType.GetProperty("UseJavaHome",
                                  BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) ??
                              settingsType.GetProperty("useJavaHome", BindingFlags.Public | BindingFlags.Instance);

        var javaPathProp =
            settingsType.GetProperty("JavaPath",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) ??
            settingsType.GetProperty("javaPath", BindingFlags.Public | BindingFlags.Instance);

        if (useJavaHomeProp == null || javaPathProp == null)
        {
            Debug.LogError("[FB SDK JavaFixer] Could not find Java properties — EDM version incompatible?");
            return;
        }

        // Unity embedded OpenJDK path
        string internalJdkPath;
#if UNITY_EDITOR_OSX
        internalJdkPath =
 Path.GetFullPath(Path.Combine(EditorApplication.applicationContentsPath, "../../PlaybackEngines/AndroidPlayer/OpenJDK"));
#else
        internalJdkPath = Path.Combine(EditorApplication.applicationContentsPath,
            "PlaybackEngines/AndroidPlayer/OpenJDK");
#endif

        if (!Directory.Exists(internalJdkPath))
        {
            Debug.LogWarning(
                $"[FB SDK JavaFixer] Unity's embedded OpenJDK missing at:\n{internalJdkPath}\nInstall Android Build Support + OpenJDK via Unity Hub.");
            return;
        }

        var useJavaHome = (bool)useJavaHomeProp.GetValue(instance);
        var currentPath = javaPathProp.GetValue(instance)?.ToString() ?? "";

        if (useJavaHome || string.IsNullOrEmpty(currentPath) || !currentPath.Replace("\\", "/").Contains("OpenJDK"))
        {
            useJavaHomeProp.SetValue(instance, false);
            javaPathProp.SetValue(instance, internalJdkPath);

            Debug.Log(
                $"<b>[FB SDK Auto-Fix]</b> EDM4U Java path FIXED → now using Unity's embedded OpenJDK:\n{internalJdkPath}");
        }
        else
        {
            Debug.Log("<b>[FB SDK JavaFixer]</b> EDM Java path already correct — nothing to do.");
        }

        // Trigger resolve (resolver class also varies, but ForceResolve is stable)
        var resolverType = Type.GetType("Google.JarResolver.PlayServicesResolver, Google.JarResolver") ??
                           Type.GetType("Google.PlayServicesResolver, Google.JarResolver") ??
                           Type.GetType("Google.AndroidDependencyResolver, Google.ExternalDependencyManager") ??
                           AppDomain.CurrentDomain.GetAssemblies()
                               .SelectMany(a => a.GetTypes())
                               .FirstOrDefault(t =>
                                   t.GetMethod("ForceResolve", BindingFlags.Static | BindingFlags.Public) != null);

        resolverType?.GetMethod("ForceResolve", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);

        Debug.Log("<b>[FB SDK Auto-Fix]</b> Java fix applied successfully + dependencies resolved!");
    }
}