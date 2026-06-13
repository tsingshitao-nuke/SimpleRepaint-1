using System;
using System.Collections.Generic;
using System.Linq;

namespace SimpleRepaintCache
{
    public static class PartAnalyzer
    {
        public class PartAnalysis
        {
            public string PartName { get; set; }
            public bool ShouldInject { get; set; }
            public bool UseB9PS { get; set; } = true;
            public string MaterialMask { get; set; } = "*";
            public string Reason { get; set; }
        }

        public static List<PartAnalysis> AnalyzeAllPartsFromDatabase(
            HashSet<string> ignoreList,
            HashSet<string> greyList,
            RepaintSettings settings,
            HashSet<string> whitelistMods,
            HashSet<string> whitelistCategories)
        {
            var results = new List<PartAnalysis>();

            var partConfigs = GameDatabase.Instance.GetConfigs("PART");
            if (partConfigs == null || partConfigs.Length == 0)
            {
                UnityEngine.Debug.LogWarning("[SimpleRepaintCache] No PART configs found in GameDatabase!");
                return results;
            }

            var seenPartNames = new HashSet<string>();
            var distinctPartConfigs = new List<ConfigNode>();
            foreach (var pc in partConfigs)
            {
                if (pc?.config == null) continue;
                string name = pc.config.GetValue("name");
                if (!string.IsNullOrEmpty(name) && seenPartNames.Add(name))
                    distinctPartConfigs.Add(pc.config);
            }

            UnityEngine.Debug.Log($"[SimpleRepaintCache] Analyzing {distinctPartConfigs.Count} distinct parts...");

            foreach (var partConfig in distinctPartConfigs)
            {
                string partName = partConfig.GetValue("name");
                if (string.IsNullOrEmpty(partName)) continue;

                // Skip kerbalEVA
                if (partName.StartsWith("kerbalEVA", StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new PartAnalysis { PartName = partName, ShouldInject = false, Reason = "KerbalEVA" });
                    continue;
                }

                // Ignore list
                if (ignoreList.Contains(partName))
                {
                    results.Add(new PartAnalysis { PartName = partName, ShouldInject = false, Reason = "Ignored" });
                    continue;
                }

                // SR_Ignore
                string srIgnore = partConfig.GetValue("SR_Ignore");
                if (srIgnore != null && (srIgnore == "true" || srIgnore == "True"))
                {
                    results.Add(new PartAnalysis { PartName = partName, ShouldInject = false, Reason = "SR_Ignore" });
                    continue;
                }

                // Whitelist
                if (settings.RepaintWhitelistedPartsOnly)
                {
                    bool isWhitelisted = false;
                    string manufacturer = partConfig.GetValue("manufacturer") ?? "";
                    foreach (var mod in whitelistMods)
                    {
                        if (manufacturer.IndexOf(mod, StringComparison.OrdinalIgnoreCase) >= 0)
                        { isWhitelisted = true; break; }
                    }
                    if (!isWhitelisted)
                    {
                        string category = partConfig.GetValue("category") ?? "";
                        if (whitelistCategories.Contains(category)) isWhitelisted = true;
                    }
                    if (!isWhitelisted)
                    {
                        results.Add(new PartAnalysis { PartName = partName, ShouldInject = false, Reason = "Not whitelisted" });
                        continue;
                    }
                }

                // Module detection
                bool hasExistingSimpleRepaintB9PS = false;
                bool hasPartVariants = false;
                bool hasSSTURecolor = false;
                bool hasTexturesUnlimited = false;
                bool hasSSTURecolorGUI = false;
                bool hasModuleWeaponBallistic = false;

                foreach (var moduleNode in partConfig.GetNodes("MODULE"))
                {
                    string moduleName = moduleNode.GetValue("name");
                    if (string.IsNullOrEmpty(moduleName)) continue;

                    switch (moduleName)
                    {
                        case "ModuleB9PartSwitch":
                            if (moduleNode.GetValue("moduleID") == "SimpleRepaint")
                                hasExistingSimpleRepaintB9PS = true;
                            break;
                        case "ModulePartVariants": hasPartVariants = true; break;
                        case "SSTURecolor": hasSSTURecolor = true; break;
                        case "TexturesUnlimited": hasTexturesUnlimited = true; break;
                        case "SSTURecolorGUI": hasSSTURecolorGUI = true; break;
                        case "ModuleWeapon":
                            if (moduleNode.GetValue("weaponType") == "ballistic")
                                hasModuleWeaponBallistic = true;
                            break;
                    }
                }

                if (hasExistingSimpleRepaintB9PS)
                {
                    results.Add(new PartAnalysis { PartName = partName, ShouldInject = false, Reason = "Already has SR B9PS" });
                    continue;
                }

                if (hasSSTURecolor || hasTexturesUnlimited || hasSSTURecolorGUI)
                {
                    results.Add(new PartAnalysis { PartName = partName, ShouldInject = false, Reason = "Has TU/SSTU" });
                    continue;
                }

                bool isGreyListed = greyList.Contains(partName) || hasModuleWeaponBallistic;

                if (hasPartVariants && isGreyListed)
                {
                    results.Add(new PartAnalysis { PartName = partName, ShouldInject = false, Reason = "Has PartVariants + grey-listed" });
                    continue;
                }

                bool useB9PS = !isGreyListed;

                results.Add(new PartAnalysis
                {
                    PartName = partName,
                    ShouldInject = true,
                    UseB9PS = useB9PS,
                    MaterialMask = "*",
                    Reason = useB9PS ? "B9PS" : "PartVariants"
                });
            }

            int injectCount = results.Count(r => r.ShouldInject);
            UnityEngine.Debug.Log($"[SimpleRepaintCache] Analysis done: {injectCount}/{results.Count} parts will get repaint");
            return results;
        }
    }
}
