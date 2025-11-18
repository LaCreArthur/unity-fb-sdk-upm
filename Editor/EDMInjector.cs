using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public class EDMInjector
{
    private const string EDM_PACKAGE_ID = "com.google.external-dependency-manager";
    private const string EDM_GIT_URL = "https://github.com/googlesamples/unity-jar-resolver.git?path=upm";

    [InitializeOnLoadMethod]
    private static void InjectEDM()
    {
        Debug.Log("<b>[FB Installer]</b> Checking for Google EDM dependency...");
        var manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            Debug.LogWarning($"[FB Installer] manifest.json not found at: {manifestPath}");
            return;
        }

        try
        {
            var jsonText = File.ReadAllText(manifestPath);
            // Simple string check is faster and dependency-free
            if (!jsonText.Contains(EDM_PACKAGE_ID))
            {
                Debug.Log("<b>[FB Installer]</b> Injecting Google EDM to manifest.json...");

                // Find the "dependencies" block start
                var depIndex = jsonText.IndexOf("\"dependencies\": {", StringComparison.Ordinal);
                if (depIndex != -1)
                {
                    // Insert our package at the top of the dependencies list
                    var insertion = $"\n    \"{EDM_PACKAGE_ID}\": \"{EDM_GIT_URL}\",";
                    var newJson = jsonText.Insert(depIndex + 17, insertion);

                    File.WriteAllText(manifestPath, newJson);
                    Debug.Log("<b>[FB Installer]</b> Successfully injected EDM. Forcing AssetDatabase refresh.");
                    AssetDatabase.Refresh(); // Force Unity to resolve
                }
                else
                {
                    Debug.LogWarning("[FB Installer] Could not find 'dependencies' block in manifest.json.");
                }
            }
            else
            {
                Debug.Log("<b>[FB Installer]</b> Google EDM dependency already exists.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[FB Installer] Manifest inject failed: {e.Message}");
        }
    }
}