using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SimpleRepaintCache
{
    /// <summary>
    /// Main KSPAddon that manages the cache lifecycle.
    /// The cache .cfg is generated at runtime after MM has finished processing,
    /// so there is never any double injection. The original patch is renamed to .bak
    /// so MM skips it on subsequent launches.
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

                // Step 0: If cache directory is missing (e.g. user deleted it to reinstall),
                // restore the original .cfg from .bak so this launch works with the original patch.
                // The cache will be regenerated below.
                bool restored = CacheManager.RestoreOriginalPatchIfCacheMissing(_paths);
                if (restored)
                {
                    UnityEngine.Debug.Log("[SimpleRepaintCache] Original patch restored for this launch. Cache will be regenerated.");
                }

                // Step 1: Check if cache is valid
                bool cacheValid = CacheManager.IsCacheValid(_paths, out var currentManifest);

                if (cacheValid)
                {
                    UnityEngine.Debug.Log("[SimpleRepaintCache] Cache is valid, no action needed.");
                }
                else
                {
                    // Determine if this is a first-time generation or a regeneration
                    bool isFirstGeneration = !System.IO.File.Exists(System.IO.Path.Combine(_paths.CacheDir, "cache.manifest"));

                    UnityEngine.Debug.Log($"[SimpleRepaintCache] Cache {(isFirstGeneration ? "missing" : "invalid")}, regenerating...");

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
                        // Step 5: Write cache patch (MM has already finished, so no double injection)
                        CacheManager.WriteCachePatch(_paths, patchContent);

                        // Step 6: Update manifest
                        currentManifest.PartCount = analyses.Count(a => a.ShouldInject);
                        currentManifest.ColorCount = colors.Count(c => c.IsUsed);
                        currentManifest.GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        CacheManager.SaveManifest(_paths, currentManifest);

                        // Step 7: Disable original patch by renaming .cfg → .bak
                        // On next launch, MM will only see the cache .cfg
                        CacheManager.DisableOriginalPatch(_paths);

                        // Step 8: Runtime injection bridge
                        // On first generation: MM has already processed the original SimpleRepaint.cfg,
                        // so all parts already have their modules. Skip runtime injection to avoid
                        // double injection and NRE errors.
                        // On subsequent regeneration: MM processed the old cache, so only new parts
                        // (added since last generation) lack modules. RuntimeInjector will check each
                        // part and only inject those that don't already have modules.
                        if (!isFirstGeneration)
                        {
                            UnityEngine.Debug.Log("[SimpleRepaintCache] Running runtime injection bridge for new parts...");
                            RuntimeInjector.InjectModules(analyses, colors, settings);
                        }
                        else
                        {
                            UnityEngine.Debug.Log("[SimpleRepaintCache] First generation - MM already handled all parts, skipping runtime injection.");
                        }

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
