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

            // Deduplicate by part name to prevent double injection
            var seenPartNames = new HashSet<string>();
            var distinctParts = new List<AvailablePart>();
            foreach (var p in allParts)
            {
                if (p == null || p.partPrefab == null) continue;
                if (seenPartNames.Add(p.name))
                {
                    distinctParts.Add(p);
                }
            }

            UnityEngine.Debug.Log($"[SimpleRepaintCache] Analyzing {distinctParts.Count} distinct parts (from {allParts.Count} total entries)...");

            foreach (var availPart in distinctParts)
            {
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

                // Check for existing modules that affect repaint compatibility
                bool hasExistingSimpleRepaintB9PS = false;
                bool hasOtherB9PS = false;
                bool hasPartVariantsModule = false;
                bool hasSSTURecolor = false;
                bool hasTexturesUnlimited = false;
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
                                hasExistingSimpleRepaintB9PS = true;
                            }
                            else
                            {
                                hasOtherB9PS = true;
                            }
                        }
                        else
                        {
                            hasOtherB9PS = true;
                        }
                    }
                    if (module.moduleName == "ModulePartVariants")
                    {
                        hasPartVariantsModule = true;
                    }
                    if (module.moduleName == "SSTURecolor")
                    {
                        hasSSTURecolor = true;
                    }
                    if (module.moduleName == "TexturesUnlimited")
                    {
                        hasTexturesUnlimited = true;
                    }
                }

                // Skip if already has SimpleRepaint module
                if (hasExistingSimpleRepaintB9PS || hasPartVariantsModule)
                {
                    results.Add(new PartAnalysis
                    {
                        PartName = partName,
                        ShouldInject = false,
                        Reason = "Already has SimpleRepaint module"
                    });
                    continue;
                }

                // Skip parts with SSTURecolor or TexturesUnlimited (they have better repaint support)
                if (hasSSTURecolor || hasTexturesUnlimited)
                {
                    results.Add(new PartAnalysis
                    {
                        PartName = partName,
                        ShouldInject = false,
                        Reason = hasSSTURecolor ? "Has SSTURecolor module" : "Has TexturesUnlimited module"
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

                // If part has other B9PS modules, use PartVariants instead to avoid material conflicts
                bool useB9PS = !isGreyListed && !hasOtherB9PS;

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
