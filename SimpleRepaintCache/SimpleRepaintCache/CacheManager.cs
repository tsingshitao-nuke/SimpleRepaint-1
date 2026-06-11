using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SimpleRepaintCache
{
    /// <summary>
    /// Manages cache validation, file hashing, and disabling the original patch via .bak rename.
    /// The cache .cfg is generated at runtime after MM has finished processing, so there is
    /// never any double injection. On subsequent launches, the original .cfg.bak is skipped by MM.
    /// </summary>
    public static class CacheManager
    {
        private const string MANIFEST_FILENAME = "cache.manifest";
        private const string CACHE_PATCH_FILENAME = "SimpleRepaintCache.cfg";
        private const string ORIGINAL_PATCH_FILENAME = "SimpleRepaint.cfg";

        /// <summary>
        /// Manifest data stored in cache.manifest
        /// </summary>
        public class CacheManifest
        {
            public string PartListHash { get; set; } = "";
            public string ColorsHash { get; set; } = "";
            public string SettingsHash { get; set; } = "";
            public string GreyListHash { get; set; } = "";
            public string IgnorePartsHash { get; set; } = "";
            public string WhitelistsHash { get; set; } = "";
            public int PartCount { get; set; } = 0;
            public int ColorCount { get; set; } = 0;
            public string GeneratedAt { get; set; } = "";
        }

        /// <summary>
        /// Paths used by the cache system
        /// </summary>
        public class CachePaths
        {
            public string GameDataPath { get; set; }
            public string CacheDir => Path.Combine(GameDataPath, "SimpleRepaint", "cache");
            public string ManifestPath => Path.Combine(CacheDir, MANIFEST_FILENAME);
            public string CachePatchPath => Path.Combine(CacheDir, CACHE_PATCH_FILENAME);
            public string OriginalPatchPath => Path.Combine(GameDataPath, "SimpleRepaint", "Patches", ORIGINAL_PATCH_FILENAME);
            public string OriginalPatchBakPath => Path.Combine(GameDataPath, "SimpleRepaint", "Patches", ORIGINAL_PATCH_FILENAME + ".bak");
        }

        /// <summary>
        /// Computes MD5 hash of a file
        /// </summary>
        public static string ComputeFileHash(string filePath)
        {
            if (!File.Exists(filePath)) return "";
            try
            {
                using (var md5 = MD5.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[SimpleRepaintCache] Error hashing file {filePath}: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Computes a combined hash for all files in a directory matching a pattern
        /// </summary>
        public static string ComputeDirectoryHash(string directoryPath, string pattern)
        {
            if (!Directory.Exists(directoryPath)) return "";

            var files = Directory.GetFiles(directoryPath, pattern).OrderBy(f => f).ToList();
            if (files.Count == 0) return "";

            var combined = new StringBuilder();
            foreach (var file in files)
            {
                combined.Append(ComputeFileHash(file));
            }

            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(combined.ToString()));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Computes a hash of all part names (sorted, comma-separated)
        /// </summary>
        public static string ComputePartListHash()
        {
            var allParts = PartLoader.LoadedPartsList;
            if (allParts == null || allParts.Count == 0) return "";

            var partNames = allParts
                .Where(p => p != null && p.partPrefab != null)
                .Select(p => p.name)
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            string joined = string.Join(",", partNames);

            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(joined));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Loads the cache manifest from disk
        /// </summary>
        public static CacheManifest LoadManifest(CachePaths paths)
        {
            var manifest = new CacheManifest();

            if (!File.Exists(paths.ManifestPath))
            {
                return manifest;
            }

            try
            {
                string[] lines = File.ReadAllLines(paths.ManifestPath);
                foreach (string line in lines)
                {
                    int idx = line.IndexOf('=');
                    if (idx < 0) continue;

                    string key = line.Substring(0, idx).Trim();
                    string value = line.Substring(idx + 1).Trim();

                    switch (key)
                    {
                        case "PartListHash": manifest.PartListHash = value; break;
                        case "ColorsHash": manifest.ColorsHash = value; break;
                        case "SettingsHash": manifest.SettingsHash = value; break;
                        case "GreyListHash": manifest.GreyListHash = value; break;
                        case "IgnorePartsHash": manifest.IgnorePartsHash = value; break;
                        case "WhitelistsHash": manifest.WhitelistsHash = value; break;
                        case "PartCount": int.TryParse(value, out int pc); manifest.PartCount = pc; break;
                        case "ColorCount": int.TryParse(value, out int cc); manifest.ColorCount = cc; break;
                        case "GeneratedAt": manifest.GeneratedAt = value; break;
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[SimpleRepaintCache] Error loading manifest: {ex.Message}");
            }

            return manifest;
        }

        /// <summary>
        /// Saves the cache manifest to disk
        /// </summary>
        public static void SaveManifest(CachePaths paths, CacheManifest manifest)
        {
            try
            {
                if (!Directory.Exists(paths.CacheDir))
                {
                    Directory.CreateDirectory(paths.CacheDir);
                }

                var sb = new StringBuilder();
                sb.AppendLine($"PartListHash={manifest.PartListHash}");
                sb.AppendLine($"ColorsHash={manifest.ColorsHash}");
                sb.AppendLine($"SettingsHash={manifest.SettingsHash}");
                sb.AppendLine($"GreyListHash={manifest.GreyListHash}");
                sb.AppendLine($"IgnorePartsHash={manifest.IgnorePartsHash}");
                sb.AppendLine($"WhitelistsHash={manifest.WhitelistsHash}");
                sb.AppendLine($"PartCount={manifest.PartCount}");
                sb.AppendLine($"ColorCount={manifest.ColorCount}");
                sb.AppendLine($"GeneratedAt={manifest.GeneratedAt}");

                File.WriteAllText(paths.ManifestPath, sb.ToString());
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[SimpleRepaintCache] Error saving manifest: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if the cache is still valid by comparing hashes
        /// </summary>
        public static bool IsCacheValid(CachePaths paths, out CacheManifest currentManifest)
        {
            currentManifest = new CacheManifest();

            // Compute current hashes
            currentManifest.PartListHash = ComputePartListHash();
            currentManifest.ColorsHash = ComputeFileHash(Path.Combine(paths.GameDataPath, "SimpleRepaint", "Colors.cfg"));
            currentManifest.SettingsHash = ComputeFileHash(Path.Combine(paths.GameDataPath, "SimpleRepaint", "Settings.cfg"));
            currentManifest.GreyListHash = ComputeFileHash(Path.Combine(paths.GameDataPath, "SimpleRepaint", "GreyList.cfg"));
            currentManifest.IgnorePartsHash = ComputeDirectoryHash(Path.Combine(paths.GameDataPath, "SimpleRepaint", "IgnoreParts"), "*.cfg");
            currentManifest.WhitelistsHash = ComputeDirectoryHash(Path.Combine(paths.GameDataPath, "SimpleRepaint", "Whitelists"), "*.*");

            // Load saved manifest
            var savedManifest = LoadManifest(paths);

            // Check if cache patch file exists
            if (!File.Exists(paths.CachePatchPath))
            {
                UnityEngine.Debug.Log("[SimpleRepaintCache] Cache patch file not found, needs regeneration");
                return false;
            }

            // Compare hashes
            bool valid = true;

            if (currentManifest.PartListHash != savedManifest.PartListHash)
            {
                UnityEngine.Debug.Log("[SimpleRepaintCache] Part list changed, cache invalid");
                valid = false;
            }
            if (currentManifest.ColorsHash != savedManifest.ColorsHash)
            {
                UnityEngine.Debug.Log("[SimpleRepaintCache] Colors.cfg changed, cache invalid");
                valid = false;
            }
            if (currentManifest.SettingsHash != savedManifest.SettingsHash)
            {
                UnityEngine.Debug.Log("[SimpleRepaintCache] Settings.cfg changed, cache invalid");
                valid = false;
            }
            if (currentManifest.GreyListHash != savedManifest.GreyListHash)
            {
                UnityEngine.Debug.Log("[SimpleRepaintCache] GreyList.cfg changed, cache invalid");
                valid = false;
            }
            if (currentManifest.IgnorePartsHash != savedManifest.IgnorePartsHash)
            {
                UnityEngine.Debug.Log("[SimpleRepaintCache] IgnoreParts changed, cache invalid");
                valid = false;
            }
            if (currentManifest.WhitelistsHash != savedManifest.WhitelistsHash)
            {
                UnityEngine.Debug.Log("[SimpleRepaintCache] Whitelists changed, cache invalid");
                valid = false;
            }

            if (valid)
            {
                UnityEngine.Debug.Log("[SimpleRepaintCache] Cache is valid!");
            }

            return valid;
        }

        /// <summary>
        /// Writes the cache patch file to disk
        /// </summary>
        public static bool WriteCachePatch(CachePaths paths, string content)
        {
            try
            {
                if (!Directory.Exists(paths.CacheDir))
                {
                    Directory.CreateDirectory(paths.CacheDir);
                }

                File.WriteAllText(paths.CachePatchPath, content);
                UnityEngine.Debug.Log($"[SimpleRepaintCache] Cache patch written to: {paths.CachePatchPath}");
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[SimpleRepaintCache] Error writing cache patch: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Renames the original SimpleRepaint.cfg to .bak so MM skips it on subsequent launches.
        /// The cache .cfg is generated at runtime after MM has finished, so there is no double injection.
        /// </summary>
        public static bool DisableOriginalPatch(CachePaths paths)
        {
            try
            {
                if (File.Exists(paths.OriginalPatchBakPath))
                {
                    // Already disabled
                    return true;
                }

                if (!File.Exists(paths.OriginalPatchPath))
                {
                    UnityEngine.Debug.LogWarning("[SimpleRepaintCache] Original patch not found, cannot disable");
                    return false;
                }

                File.Move(paths.OriginalPatchPath, paths.OriginalPatchBakPath);
                UnityEngine.Debug.Log("[SimpleRepaintCache] Disabled original patch: .cfg → .bak");
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[SimpleRepaintCache] Error disabling original patch: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Restores the original patch from .bak to .cfg.
        /// Used when the cache mod is removed - player manually renames .bak back to .cfg.
        /// </summary>
        public static bool RestoreOriginalPatch(CachePaths paths)
        {
            try
            {
                if (!File.Exists(paths.OriginalPatchBakPath))
                {
                    // Already restored or never disabled
                    return true;
                }

                if (File.Exists(paths.OriginalPatchPath))
                {
                    // Both exist, remove the .cfg (it's the cache-generated one)
                    File.Delete(paths.OriginalPatchPath);
                }

                File.Move(paths.OriginalPatchBakPath, paths.OriginalPatchPath);
                UnityEngine.Debug.Log("[SimpleRepaintCache] Restored original patch: .bak → .cfg");
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[SimpleRepaintCache] Error restoring original patch: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if the cache directory exists. If not (e.g. user deleted cache to reinstall),
        /// restores the original .cfg from .bak so this launch works with the original patch.
        /// Returns true if restoration was performed.
        /// </summary>
        public static bool RestoreOriginalPatchIfCacheMissing(CachePaths paths)
        {
            try
            {
                // Check if cache directory or manifest is missing
                bool cacheMissing = !Directory.Exists(paths.CacheDir) || !File.Exists(paths.ManifestPath);

                if (!cacheMissing)
                {
                    return false;
                }

                // Cache is missing, check if we have a .bak to restore
                if (!File.Exists(paths.OriginalPatchBakPath))
                {
                    // No .bak, nothing to restore
                    return false;
                }

                UnityEngine.Debug.Log("[SimpleRepaintCache] Cache directory missing, restoring original patch for this launch...");

                // Remove any existing .cfg (might be stale cache)
                if (File.Exists(paths.OriginalPatchPath))
                {
                    File.Delete(paths.OriginalPatchPath);
                }

                // Restore .bak → .cfg
                File.Move(paths.OriginalPatchBakPath, paths.OriginalPatchPath);
                UnityEngine.Debug.Log("[SimpleRepaintCache] Original patch restored: .bak → .cfg");

                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[SimpleRepaintCache] Error restoring original patch: {ex.Message}");
                return false;
            }
        }
    }
}
