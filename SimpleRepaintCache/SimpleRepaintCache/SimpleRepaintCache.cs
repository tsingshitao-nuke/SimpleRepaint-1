using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SimpleRepaintCache
{
    /// <summary>
    /// Main KSPAddon that manages the cache lifecycle.
    /// Runs after PartLoader has loaded all parts.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class SimpleRepaintCacheAddon : MonoBehaviour
    {
        private CacheManager.CachePaths _paths;
        private bool _initialized = false;

        private void Start()
        {
            // Initialize paths
            _paths = new CacheManager.CachePaths
            {
                GameDataPath = System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "GameData")
            };

            UnityEngine.Debug.Log("[SimpleRepaintCache] Initializing...");
            UnityEngine.Debug.Log($"[SimpleRepaintCache] GameData path: {_paths.GameDataPath}");
            UnityEngine.Debug.Log($"[SimpleRepaintCache] Cache directory: {_paths.CacheDir}");

            // Subscribe to PartLoader loaded event
            GameEvents.OnPartLoaderLoaded.Add(OnPartLoaderLoaded);
        }

        private void OnPartLoaderLoaded()
        {
            try
            {
                UnityEngine.Debug.Log("[SimpleRepaintCache] PartLoader loaded, checking cache...");

                // Step 1: Crash recovery - check if original patch needs restoring
                CacheManager.PerformCrashRecovery(_paths);

                // Step 2: Check if cache is valid
                bool cacheValid = CacheManager.IsCacheValid(_paths, out var currentManifest);

                if (cacheValid)
                {
                    UnityEngine.Debug.Log("[SimpleRepaintCache] Cache is valid, disabling original patch...");
                    // Cache is good - just disable the original patch
                    CacheManager.DisableOriginalPatch(_paths);
                }
                else
                {
                    UnityEngine.Debug.Log("[SimpleRepaintCache] Cache invalid or missing, regenerating...");

                    // Step 3: Restore original patch first (in case it was disabled)
                    CacheManager.RestoreOriginalPatch(_paths);

                    // Step 4: Load configuration
                    var colors = ColorConfigLoader.LoadColors(_paths.GameDataPath);
                    var settings = ColorConfigLoader.LoadSettings(_paths.GameDataPath);
                    var greyList = ColorConfigLoader.LoadGreyList(_paths.GameDataPath);
                    var ignoreList = ColorConfigLoader.LoadIgnoreList(_paths.GameDataPath);
                    var (whitelistMods, whitelistCategories) = ColorConfigLoader.LoadWhitelists(_paths.GameDataPath);

                    // Step 5: Analyze all parts
                    var analyses = PartAnalyzer.AnalyzeAllParts(
                        ignoreList, greyList, settings, whitelistMods, whitelistCategories);

                    // Step 6: Generate cache patch
                    string patchContent = PatchGenerator.GeneratePatch(analyses, colors, settings);

                    if (!string.IsNullOrEmpty(patchContent))
                    {
                        // Step 7: Write cache patch
                        CacheManager.WriteCachePatch(_paths, patchContent);

                        // Step 8: Update manifest
                        currentManifest.PartCount = analyses.Count(a => a.ShouldInject);
                        currentManifest.ColorCount = colors.Count(c => c.IsUsed);
                        currentManifest.GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        CacheManager.SaveManifest(_paths, currentManifest);

                        // Step 9: Disable original patch
                        CacheManager.DisableOriginalPatch(_paths);

                        UnityEngine.Debug.Log($"[SimpleRepaintCache] Cache generated successfully! " +
                            $"{currentManifest.PartCount} parts with {currentManifest.ColorCount} colors.");
                    }
                    else
                    {
                        UnityEngine.Debug.LogError("[SimpleRepaintCache] Failed to generate cache patch!");
                    }
                }

                _initialized = true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[SimpleRepaintCache] Error during cache processing: {ex.Message}");
                UnityEngine.Debug.LogError($"[SimpleRepaintCache] Stack trace: {ex.StackTrace}");

                // On error, try to restore original patch to ensure game works
                try { CacheManager.RestoreOriginalPatch(_paths); } catch { }
            }
        }

        private void OnDestroy()
        {
            if (_initialized)
            {
                // Clean up event subscription
                GameEvents.OnPartLoaderLoaded.Remove(OnPartLoaderLoaded);
            }
        }
    }
}
