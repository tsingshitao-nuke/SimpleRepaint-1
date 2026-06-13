using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SimpleRepaintCache
{
    public static class CacheManager
    {
        private const string MANIFEST_FILENAME = "cache.manifest";
        private const string CACHE_PATCH_FILENAME = "SimpleRepaintCache.cfg";
        private const string ORIGINAL_PATCH_FILENAME = "SimpleRepaint.cfg";

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

        public class CachePaths
        {
            public string GameDataPath { get; set; }
            public string CacheDir => Path.Combine(GameDataPath, "SimpleRepaint", "cache");
            public string ManifestPath => Path.Combine(CacheDir, MANIFEST_FILENAME);
            public string CachePatchPath => Path.Combine(CacheDir, CACHE_PATCH_FILENAME);
            public string OriginalPatchPath => Path.Combine(GameDataPath, "SimpleRepaint", "Patches", ORIGINAL_PATCH_FILENAME);
            public string OriginalPatchBakPath => Path.Combine(GameDataPath, "SimpleRepaint", "Patches", ORIGINAL_PATCH_FILENAME + ".bak");
        }

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
                UnityEngine.Debug.LogError($"[SimpleRepaintCache] Error hashing {filePath}: {ex.Message}");
                return "";
            }
        }

        public static string ComputeDirectoryHash(string directoryPath, string pattern)
        {
            if (!Directory.Exists(directoryPath)) return "";

            var files = Directory.GetFiles(directoryPath, pattern).OrderBy(f => f).ToList();
            if (files.Count == 0) return "";

            var combined = new StringBuilder();
            foreach (var file in files)
                combined.Append(ComputeFileHash(file));

            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(combined.ToString()));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        public static string ComputePartListHash()
        {
            var partConfigs = GameDatabase.Instance.GetConfigs("PART");
            if (partConfigs == null || partConfigs.Length == 0) return "";

            var partNames = partConfigs
                .Where(pc => pc?.config != null)
                .Select(pc => pc.config.GetValue("name"))
                .Where(name => !string.IsNullOrEmpty(name))
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

        public static CacheManifest LoadManifest(CachePaths paths)
        {
            var manifest = new CacheManifest();
            if (!File.Exists(paths.ManifestPath)) return manifest;

            try
            {
                foreach (string line in File.ReadAllLines(paths.ManifestPath))
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

        public static void SaveManifest(CachePaths paths, CacheManifest manifest)
        {
            try
            {
                if (!Directory.Exists(paths.CacheDir))
                    Directory.CreateDirectory(paths.CacheDir);

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

        public static bool IsCacheValid(CachePaths paths, out CacheManifest currentManifest)
        {
            currentManifest = new CacheManifest();

            currentManifest.PartListHash = ComputePartListHash();
            currentManifest.ColorsHash = ComputeFileHash(Path.Combine(paths.GameDataPath, "SimpleRepaint", "Colors.cfg"));
            currentManifest.SettingsHash = ComputeFileHash(Path.Combine(paths.GameDataPath, "SimpleRepaint", "Settings.cfg"));
            currentManifest.GreyListHash = ComputeFileHash(Path.Combine(paths.GameDataPath, "SimpleRepaint", "GreyList.cfg"));
            currentManifest.IgnorePartsHash = ComputeDirectoryHash(Path.Combine(paths.GameDataPath, "SimpleRepaint", "IgnoreParts"), "*.cfg");
            currentManifest.WhitelistsHash = ComputeDirectoryHash(Path.Combine(paths.GameDataPath, "SimpleRepaint", "Whitelists"), "*.*");

            var savedManifest = LoadManifest(paths);

            if (!File.Exists(paths.CachePatchPath))
            {
                UnityEngine.Debug.Log("[SimpleRepaintCache] Cache patch file not found");
                return false;
            }

            bool valid = currentManifest.PartListHash == savedManifest.PartListHash
                      && currentManifest.ColorsHash == savedManifest.ColorsHash
                      && currentManifest.SettingsHash == savedManifest.SettingsHash
                      && currentManifest.GreyListHash == savedManifest.GreyListHash
                      && currentManifest.IgnorePartsHash == savedManifest.IgnorePartsHash
                      && currentManifest.WhitelistsHash == savedManifest.WhitelistsHash;

            if (!valid)
                UnityEngine.Debug.Log("[SimpleRepaintCache] Cache invalid (config or parts changed)");
            else
                UnityEngine.Debug.Log("[SimpleRepaintCache] Cache is valid");

            return valid;
        }

        public static bool WriteCachePatch(CachePaths paths, string content)
        {
            try
            {
                if (!Directory.Exists(paths.CacheDir))
                    Directory.CreateDirectory(paths.CacheDir);

                File.WriteAllText(paths.CachePatchPath, content);
                UnityEngine.Debug.Log($"[SimpleRepaintCache] Cache patch written: {paths.CachePatchPath}");
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[SimpleRepaintCache] Error writing cache patch: {ex.Message}");
                return false;
            }
        }

        public static string ComputeStubContent()
        {
            return "// SimpleRepaint is disabled by SimpleRepaintCache." + Environment.NewLine +
                   "// To restore the original patch, remove SimpleRepaintCache and" + Environment.NewLine +
                   "// rename SimpleRepaint/Patches/SimpleRepaint.cfg.bak to SimpleRepaint.cfg" + Environment.NewLine;
        }

        public static bool SwitchToCacheMode(CachePaths paths)
        {
            try
            {
                if (!File.Exists(paths.OriginalPatchBakPath))
                {
                    if (!File.Exists(paths.OriginalPatchPath))
                    {
                        UnityEngine.Debug.LogWarning("[SimpleRepaintCache] Original patch not found");
                        return false;
                    }

                    File.Move(paths.OriginalPatchPath, paths.OriginalPatchBakPath);
                    UnityEngine.Debug.Log("[SimpleRepaintCache] Original patch renamed to .bak");
                }

                File.WriteAllText(paths.OriginalPatchPath, ComputeStubContent());
                UnityEngine.Debug.Log("[SimpleRepaintCache] Stub patch created");
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[SimpleRepaintCache] Error switching to cache mode: {ex.Message}");
                return false;
            }
        }
    }
}
