using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SimpleRepaintCache
{
    /// <summary>
    /// Injects repaint modules directly into PartPrefab at runtime.
    /// This serves as a bridge so that new parts get repaint modules immediately
    /// on the first launch after adding them, without needing a restart.
    /// On subsequent launches, the cached MM .cfg handles everything.
    /// </summary>
    public static class RuntimeInjector
    {
        /// <summary>
        /// Injects repaint modules into all parts that need them.
        /// Called when cache is regenerated (new parts detected).
        /// </summary>
        public static void InjectModules(List<PartAnalyzer.PartAnalysis> analyses, List<ColorEntry> colors, RepaintSettings settings)
        {
            var usedColors = colors.FindAll(c => c.IsUsed);
            if (usedColors.Count == 0)
            {
                UnityEngine.Debug.LogError("[SimpleRepaintCache] No valid colors for runtime injection!");
                return;
            }

            int injectedCount = 0;

            foreach (var analysis in analyses)
            {
                if (!analysis.ShouldInject) continue;

                // Find the part in PartLoader
                var availPart = PartLoader.LoadedPartsList
                    .FirstOrDefault(p => p != null && p.partPrefab != null && p.name == analysis.PartName);

                if (availPart == null || availPart.partPrefab == null)
                {
                    UnityEngine.Debug.LogWarning($"[SimpleRepaintCache] Cannot find part '{analysis.PartName}' for runtime injection");
                    continue;
                }

                var partPrefab = availPart.partPrefab;

                // Determine if part is grey-listed from the analysis result
                bool isGreyListed = !analysis.UseB9PS;

                // Double-check: skip if part already has SimpleRepaint B9PS module
                bool alreadyHasSR = false;
                bool hasPartVariants = false;
                bool hasSSTURecolorGUI = false;
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
                                alreadyHasSR = true;
                                break;
                            }
                        }
                    }
                    if (module.moduleName == "ModulePartVariants")
                    {
                        hasPartVariants = true;
                    }
                    if (module.moduleName == "SSTURecolorGUI")
                    {
                        hasSSTURecolorGUI = true;
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

                if (alreadyHasSR)
                {
                    continue;
                }

                // Skip parts with existing ModulePartVariants AND grey-listed
                // (grey-listed parts can't use B9PS, and we don't want to add a second PartVariants module)
                if (hasPartVariants && isGreyListed)
                {
                    continue;
                }

                // Skip parts with SSTU/TexturesUnlimited modules (they have better repaint support)
                if (hasSSTURecolorGUI || hasSSTURecolor || hasTexturesUnlimited)
                {
                    continue;
                }

                try
                {
                    if (analysis.UseB9PS)
                    {
                        InjectB9PSModule(partPrefab, analysis, usedColors, settings);
                    }
                    else
                    {
                        InjectPartVariantsModule(partPrefab, analysis, usedColors, settings);
                    }
                    injectedCount++;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[SimpleRepaintCache] Failed to inject module for '{analysis.PartName}': {ex.Message}");
                }
            }

            UnityEngine.Debug.Log($"[SimpleRepaintCache] Runtime injection complete: {injectedCount} parts modified");
        }

        private static void InjectB9PSModule(Part partPrefab, PartAnalyzer.PartAnalysis analysis, List<ColorEntry> colors, RepaintSettings settings)
        {
            // Create a new ModuleB9PartSwitch via config node
            var configNode = new ConfigNode("MODULE");
            configNode.AddValue("name", "ModuleB9PartSwitch");
            configNode.AddValue("moduleID", "SimpleRepaint");
            configNode.AddValue("switcherDescription", "#LOC_SR_ColorVariant_title");
            configNode.AddValue("affectDragCubes", "false");
            configNode.AddValue("affectFARVoxels", "false");
            configNode.AddValue("switchInFlight", settings.RepaintInFlight ? "true" : "false");
            configNode.AddValue("uiGroupName", "SimpleRepaint");
            configNode.AddValue("uiGroupDisplayName", "#LOC_SR_Repaint_UIGroup_title");

            // Original subtype
            var originalSubtype = new ConfigNode("SUBTYPE");
            originalSubtype.AddValue("name", "SR_Original");
            originalSubtype.AddValue("title", "#LOC_SR_Color_Original");
            originalSubtype.AddValue("primaryColor", "#C7C7C7");
            var originalMaterial = new ConfigNode("MATERIAL");
            originalMaterial.AddValue("name", analysis.MaterialMask);
            var originalColor = new ConfigNode("COLOR");
            originalColor.AddValue("color", "#C7C7C7");
            originalMaterial.AddNode("COLOR", originalColor);
            originalSubtype.AddNode("MATERIAL", originalMaterial);
            configNode.AddNode("SUBTYPE", originalSubtype);

            // Color subtypes
            foreach (var color in colors)
            {
                if (!color.IsUsed) continue;

                var subtype = new ConfigNode("SUBTYPE");
                subtype.AddValue("name", color.Name);
                subtype.AddValue("title", color.Title);
                subtype.AddValue("primaryColor", color.Color);

                var material = new ConfigNode("MATERIAL");
                material.AddValue("name", analysis.MaterialMask);
                var colorNode = new ConfigNode("COLOR");
                colorNode.AddValue("color", color.Color);
                material.AddNode("COLOR", colorNode);
                subtype.AddNode("MATERIAL", material);

                configNode.AddNode("SUBTYPE", subtype);
            }

            // Add the module to the part prefab
            partPrefab.AddModule(configNode);
        }

        private static void InjectPartVariantsModule(Part partPrefab, PartAnalyzer.PartAnalysis analysis, List<ColorEntry> colors, RepaintSettings settings)
        {
            // Create a new ModulePartVariants via config node
            var configNode = new ConfigNode("MODULE");
            configNode.AddValue("name", "ModulePartVariants");
            configNode.AddValue("useMultipleDragCubes", "false");

            // Color variants
            foreach (var color in colors)
            {
                if (!color.IsUsed) continue;

                var variant = new ConfigNode("VARIANT");
                variant.AddValue("name", color.Name);
                variant.AddValue("displayName", color.Title);
                variant.AddValue("primaryColor", color.Color);
                variant.AddValue("secondaryColor", color.Color);

                var texture = new ConfigNode("TEXTURE");
                texture.AddValue("_Color", color.Color);
                variant.AddNode("TEXTURE", texture);

                configNode.AddNode("VARIANT", variant);
            }

            // Add the module to the part prefab
            partPrefab.AddModule(configNode);
        }
    }
}
