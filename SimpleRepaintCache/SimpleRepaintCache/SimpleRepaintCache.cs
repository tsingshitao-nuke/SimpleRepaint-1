using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SimpleRepaintCache
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class SimpleRepaintCacheAddon : MonoBehaviour
    {
        private CacheManager.CachePaths _paths;
        private bool _hasRun = false;

        private void Start()
        {
            _paths = new CacheManager.CachePaths
            {
                GameDataPath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData")
            };

            Debug.Log("[SimpleRepaintCache] Initializing...");
            Debug.Log($"[SimpleRepaintCache] GameData path: {_paths.GameDataPath}");

            GameEvents.OnGameDatabaseLoaded.Add(OnGameDatabaseLoaded);
        }

        private void OnGameDatabaseLoaded()
        {
            if (_hasRun) return;
            _hasRun = true;

            try
            {
                Debug.Log("[SimpleRepaintCache] GameDatabase loaded, checking cache...");

                if (CacheManager.IsCacheValid(_paths, out var currentManifest))
                {
                    Debug.Log("[SimpleRepaintCache] Cache is valid, no action needed.");
                    return;
                }

                bool isFirstGen = !File.Exists(Path.Combine(_paths.CacheDir, "cache.manifest"));
                Debug.Log($"[SimpleRepaintCache] Cache {(isFirstGen ? "missing" : "invalid")}, regenerating...");

                var colors = ColorConfigLoader.LoadColors(_paths.GameDataPath);
                var settings = ColorConfigLoader.LoadSettings(_paths.GameDataPath);
                var greyList = ColorConfigLoader.LoadGreyList(_paths.GameDataPath);
                var ignoreList = ColorConfigLoader.LoadIgnoreList(_paths.GameDataPath);
                var (whitelistMods, whitelistCategories) = ColorConfigLoader.LoadWhitelists(_paths.GameDataPath);

                var analyses = PartAnalyzer.AnalyzeAllPartsFromDatabase(
                    ignoreList, greyList, settings, whitelistMods, whitelistCategories);

                string patchContent = PatchGenerator.GeneratePatch(analyses, colors, settings);

                if (string.IsNullOrEmpty(patchContent))
                {
                    Debug.LogError("[SimpleRepaintCache] Failed to generate cache patch!");
                    return;
                }

                CacheManager.WriteCachePatch(_paths, patchContent);

                currentManifest.PartCount = analyses.Count(a => a.ShouldInject);
                currentManifest.ColorCount = colors.Count(c => c.IsUsed);
                currentManifest.GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                CacheManager.SaveManifest(_paths, currentManifest);

                CacheManager.SwitchToCacheMode(_paths);

                Debug.Log($"[SimpleRepaintCache] Cache generated! " +
                    $"{currentManifest.PartCount} parts, {currentManifest.ColorCount} colors.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SimpleRepaintCache] Error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void OnDestroy()
        {
            GameEvents.OnGameDatabaseLoaded.Remove(OnGameDatabaseLoaded);
        }
    }
}
