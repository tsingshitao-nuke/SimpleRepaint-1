using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SimpleRepaintCache
{
    public class ColorEntry
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string Color { get; set; }
        public bool IsUsed => Name != "NOT_USED" && !string.IsNullOrEmpty(Name);
    }

    public class RepaintSettings
    {
        public bool RepaintInFlight { get; set; } = true;
        public bool UseStockVariantSwitcherForB9PSIncompatibleParts { get; set; } = true;
        public bool RepaintWhitelistedPartsOnly { get; set; } = false;
    }

    public static class ColorConfigLoader
    {
        public static List<ColorEntry> LoadColors(string gameDataPath)
        {
            var colors = new List<ColorEntry>();
            string colorsPath = Path.Combine(gameDataPath, "SimpleRepaint", "Colors.cfg");

            if (!File.Exists(colorsPath))
            {
                UnityEngine.Debug.LogError("[SimpleRepaintCache] Colors.cfg not found at: " + colorsPath);
                return colors;
            }

            string content = File.ReadAllText(colorsPath);

            for (int i = 1; i <= 24; i++)
            {
                string block = ExtractBlock(content, $"COLOR_{i}");
                if (block == null) continue;

                var entry = new ColorEntry
                {
                    Name = ExtractValue(block, "name"),
                    Title = ExtractValue(block, "title"),
                    Color = ExtractValue(block, "color")
                };
                colors.Add(entry);
            }

            UnityEngine.Debug.Log($"[SimpleRepaintCache] Loaded {colors.Count} colors from Colors.cfg");
            return colors;
        }

        public static RepaintSettings LoadSettings(string gameDataPath)
        {
            var settings = new RepaintSettings();
            string settingsPath = Path.Combine(gameDataPath, "SimpleRepaint", "Settings.cfg");

            if (!File.Exists(settingsPath))
            {
                UnityEngine.Debug.LogWarning("[SimpleRepaintCache] Settings.cfg not found, using defaults");
                return settings;
            }

            string content = File.ReadAllText(settingsPath);

            settings.RepaintInFlight = ParseBool(ExtractValue(content, "RepaintInFlight"), true);
            settings.UseStockVariantSwitcherForB9PSIncompatibleParts = ParseBool(ExtractValue(content, "UseStockVariantSwitcherForB9PSIncompatibleParts"), true);
            settings.RepaintWhitelistedPartsOnly = ParseBool(ExtractValue(content, "RepaintWhitelistedPartsOnly"), false);

            UnityEngine.Debug.Log("[SimpleRepaintCache] Loaded settings from Settings.cfg");
            return settings;
        }

        public static HashSet<string> LoadGreyList(string gameDataPath)
        {
            var greyList = new HashSet<string>();
            string greyListPath = Path.Combine(gameDataPath, "SimpleRepaint", "GreyList.cfg");

            if (!File.Exists(greyListPath))
            {
                UnityEngine.Debug.LogWarning("[SimpleRepaintCache] GreyList.cfg not found");
                return greyList;
            }

            string content = File.ReadAllText(greyListPath);

            var partMatch = Regex.Matches(content, @"@PART\[([^\]]+)\]");
            foreach (Match match in partMatch)
            {
                string partList = match.Groups[1].Value;
                foreach (string part in partList.Split(','))
                {
                    string trimmed = part.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        greyList.Add(trimmed);
                    }
                }
            }

            UnityEngine.Debug.Log($"[SimpleRepaintCache] Loaded {greyList.Count} grey-listed parts from GreyList.cfg");
            return greyList;
        }

        public static HashSet<string> LoadIgnoreList(string gameDataPath)
        {
            var ignoreList = new HashSet<string>();
            string ignoreDir = Path.Combine(gameDataPath, "SimpleRepaint", "IgnoreParts");

            if (!Directory.Exists(ignoreDir))
            {
                UnityEngine.Debug.LogWarning("[SimpleRepaintCache] IgnoreParts directory not found");
                return ignoreList;
            }

            foreach (string file in Directory.GetFiles(ignoreDir, "*.cfg"))
            {
                string content = File.ReadAllText(file);
                var partMatch = Regex.Matches(content, @"@PART\[([^\]]+)\]");
                foreach (Match match in partMatch)
                {
                    string partList = match.Groups[1].Value;
                    foreach (string part in partList.Split(','))
                    {
                        string trimmed = part.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            ignoreList.Add(trimmed);
                        }
                    }
                }
            }

            UnityEngine.Debug.Log($"[SimpleRepaintCache] Loaded {ignoreList.Count} ignored parts from IgnoreParts");
            return ignoreList;
        }

        public static (HashSet<string> mods, HashSet<string> categories) LoadWhitelists(string gameDataPath)
        {
            var mods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string whitelistDir = Path.Combine(gameDataPath, "SimpleRepaint", "Whitelists");

            if (!Directory.Exists(whitelistDir))
            {
                return (mods, categories);
            }

            string modsPath = Path.Combine(whitelistDir, "Mods.txt");
            if (File.Exists(modsPath))
            {
                foreach (string line in File.ReadAllLines(modsPath))
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("//"))
                    {
                        mods.Add(trimmed);
                    }
                }
            }

            string categoriesPath = Path.Combine(whitelistDir, "Categories.txt");
            if (File.Exists(categoriesPath))
            {
                foreach (string line in File.ReadAllLines(categoriesPath))
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("//"))
                    {
                        categories.Add(trimmed);
                    }
                }
            }

            return (mods, categories);
        }

        private static string ExtractBlock(string content, string blockName)
        {
            var match = Regex.Match(content, $@"{blockName}\s*\{{([^}}]*)\}}", RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string ExtractValue(string blockContent, string key)
        {
            var match = Regex.Match(blockContent, $@"{key}\s*=\s*(.+)", RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }

        private static bool ParseBool(string value, bool defaultValue)
        {
            if (string.IsNullOrEmpty(value)) return defaultValue;
            value = value.Trim().ToLowerInvariant();
            if (value == "true" || value == "1" || value == "yes") return true;
            if (value == "false" || value == "0" || value == "no") return false;
            return defaultValue;
        }
    }
}
