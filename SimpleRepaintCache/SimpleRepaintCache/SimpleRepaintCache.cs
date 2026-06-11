using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SimpleRepaintCache
{
    /// <summary>
    /// Main KSPAddon that manages the cache lifecycle.
    /// Uses :NEEDS[!SimpleRepaintCache] on the original patch to prevent double injection.
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

                // Step 1: Check if cache is valid
                bool cacheValid = CacheManager.IsCacheValid(_paths, out var currentManifest);

                if (cacheValid)
                {
                    UnityEngine.Debug.Log("[SimpleRepaintCache] Cache is valid, no action needed.");
                }
                else
                {
                    UnityEngine.Debug.Log("[SimpleRepaintCache] Cache invalid or missing, regenerating...");

                    // Step 2: Load configuration
                    var colors = ColorConfigLoader.LoadColors(_paths.GameDataPath);
                    var settings = ColorConfigLoader.LoadSettings(_paths.GameDataPath);
                    var greyList = ColorConfigLoader.LoadGreyList(_paths.GameDataPath);
                    var ignoreList = ColorConfigLoader.LoadIgnoreList(_paths.GameDataPath);
                    var (whitelistMods, whitelistCategories) = ColorConfigLoader.LoadWhitelists(_paths.GameDataPath);

                    // Step 3: Analyze all parts (self-sufficient, does not depend on original patch)
                    var analyses = PartAnalyzer.AnalyzeAllParts(
                        ignoreList, greyList, settings, whitelistMods, whitelistCategories);

                    // Step 4: Generate cache patch
                    string patchContent = PatchGenerator.GeneratePatch(analyses, colors, settings);

                    if (!string.IsNullOrEmpty(patchContent))
                    {
                        // Step 5: Write cache patch
                        CacheManager.WriteCachePatch(_paths, patchContent);

                        // Step 6: Update manifest
                        currentManifest.PartCount = analyses.Count(a => a.ShouldInject);
                        currentManifest.ColorCount = colors.Count(c => c.IsUsed);
                        currentManifest.GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        CacheManager.SaveManifest(_paths, currentManifest);

                        // Step 7: Add :NEEDS[!SimpleRepaintCache] to original patch
                        // This prevents MM from processing the original patch on subsequent launches
                        CacheManager.AddNeedsCondition(_paths);

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
