using System;
using System.Collections.Generic;
using System.Linq;

namespace SimpleRepaintCache
{
    /// <summary>
    /// Analyzes parts and determines whether they should receive repaint modules.
    /// Replicates the filtering logic from SimpleRepaint.cfg
    /// </summary>
    public static class PartAnalyzer
    {
        /// <summary>
        /// Result of analyzing a part
        /// </summary>
        public class PartAnalysis
        {
            public string PartName { get; set; }
            public bool ShouldInject { get; set; }
            public bool UseB9PS { get; set; } = true;
            public string MaterialMask { get; set; } = "*";
            public string Reason { get; set; }
        }

        /// <summary>
        /// Analyzes all loaded parts and determines which should get repaint modules
        /// </summary>
        public static List<PartAnalysis> AnalyzeAllParts(
            HashSet<string> ignoreList,
            HashSet<string> greyList,
            RepaintSettings settings,
            HashSet<string> whitelistMods,
            HashSet<string> whitelistCategories)
        {
            var results = new List<PartAnalysis>();

            var allParts = PartLoader.LoadedPartsList;
            if (allParts == null || allParts.Count == 0)
            {
                UnityEngine.Debug.LogWarning("[SimpleRepaintCache] PartLoader.LoadedPartsList is empty!");
                return results;
            }

            UnityEngine.Debug.Log($"[SimpleRepaintCache] Analyzing {allParts.Count} loaded parts...");

            foreach (var availPart in allParts)
            {
                if (availPart == null || availPart.partPrefab == null) continue;

                string partName = availPart.name;
                var partPrefab = availPart.partPrefab;

                // Skip kerbalEVA parts
                if (partName.StartsWith("kerbalEVA", StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new PartAnalysis
                    {
                        PartName = partName,
                        ShouldInject = false,
                        Reason = "KerbalEVA part"
                    });
                    continue;
                }

                // Check ignore list
                if (ignoreList.Contains(partName))
                {
                    results.Add(new PartAnalysis
                    {
                        PartName = partName,
                        ShouldInject = false,
                        Reason = "In ignore list"
                    });
                    continue;
                }

                // Check if part already has SR_Ignore field set by other patches
                // SR_Ignore is set via MM patches as a Part field, check partPrefab's module fields
                bool hasSRIgnore = false;
                try
                {
                    // Check all modules for SR_Ignore field
                    foreach (var module in partPrefab.Modules)
                    {
                        var ignoreField = module.Fields["SR_Ignore"];
                        if (ignoreField != null)
                        {
                            string val = ignoreField.GetValue<string>(module);
                            if (val == "true" || val == "True")
                            {
                                hasSRIgnore = true;
                                break;
                            }
                        }
                    }
                }
                catch { }

                if (hasSRIgnore)
                {
                    results.Add(new PartAnalysis
                    {
                        PartName = partName,
                        ShouldInject = false,
                        Reason = "SR_Ignore = true"
                    });
                    continue;
                }

                // Check whitelist if enabled
                if (settings.RepaintWhitelistedPartsOnly)
                {
                    bool isWhitelisted = false;

                    // Check by mod (manufacturer) - use AvailablePart info
                    string manufacturer = availPart.manufacturer ?? "";
                    if (whitelistMods.Count > 0)
                    {
                        foreach (var mod in whitelistMods)
                        {
                            if (manufacturer.IndexOf(mod, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                isWhitelisted = true;
                                break;
                            }
                        }
                    }

                    // Check by category
                    if (!isWhitelisted && whitelistCategories.Count > 0)
                    {
                        string category = availPart.category.ToString();
                        if (whitelistCategories.Contains(category))
                        {
                            isWhitelisted = true;
                        }
                    }

                    if (!isWhitelisted)
                    {
                        results.Add(new PartAnalysis
                        {
                            PartName = partName,
                            ShouldInject = false,
                            Reason = "Not whitelisted"
                        });
                        continue;
                    }
                }

                // Check if part already has a ModuleB9PartSwitch with moduleID = SimpleRepaint
                bool hasB9PSModule = false;
                bool hasPartVariantsModule = false;
                foreach (var module in partPrefab.Modules)
                {
                    if (module.moduleName == "ModuleB9PartSwitch")
                    {
                        var moduleIDField = module.Fields["moduleID"];
                        if (moduleIDField != null)
                        {
                            string mid = moduleIDField.GetValue<string>(module);
                            if (mid == "SimpleRepaint")
                            {
                                hasB9PSModule = true;
                                break;
                            }
                        }
                    }
                    if (module.moduleName == "ModulePartVariants")
                    {
                        hasPartVariantsModule = true;
                    }
                }

                if (hasB9PSModule || hasPartVariantsModule)
                {
                    results.Add(new PartAnalysis
                    {
                        PartName = partName,
                        ShouldInject = false,
                        Reason = "Already has repaint module"
                    });
                    continue;
                }

                // Check if part is in grey list (B9PS incompatible)
                bool isGreyListed = greyList.Contains(partName);

                // Check for ModuleWeapon (dynamic grey list from GreyList.cfg)
                if (!isGreyListed)
                {
                    foreach (var module in partPrefab.Modules)
                    {
                        if (module.moduleName == "ModuleWeapon")
                        {
                            var weaponTypeField = module.Fields["weaponType"];
                            if (weaponTypeField != null)
                            {
                                string wt = weaponTypeField.GetValue<string>(module);
                                if (wt == "ballistic")
                                {
                                    isGreyListed = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                bool useB9PS = !isGreyListed;

                results.Add(new PartAnalysis
                {
                    PartName = partName,
                    ShouldInject = true,
                    UseB9PS = useB9PS,
                    MaterialMask = "*",
                    Reason = useB9PS ? "B9PS" : "PartVariants (grey-listed)"
                });
            }

            int injectCount = results.Count(r => r.ShouldInject);
            UnityEngine.Debug.Log($"[SimpleRepaintCache] Analysis complete: {injectCount}/{results.Count} parts will get repaint modules");
            return results;
        }
    }
}
