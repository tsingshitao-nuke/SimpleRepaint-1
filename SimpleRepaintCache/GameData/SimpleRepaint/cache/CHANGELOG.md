# SimpleRepaintCache 更新日志 / Changelog

## 1.0.0 — 缓存系统 / Cache System

### 中文

#### 新功能：SimpleRepaintCache 缓存附属 Mod

**原理：**
SimpleRepaint 的原始 MM 补丁（`SimpleRepaint.cfg`）通过复杂的 `:HAS` 条件在每次游戏启动时遍历所有已加载零件 × 24 种颜色，为每个符合条件的零件生成 B9PartSwitch 或 ModulePartVariants 模块。当安装了大量零件 mod 时，MM 需要处理成千上万条补丁规则，严重拖慢加载速度。

SimpleRepaintCache 是一个 KSPAddon DLL，它在 PartLoader 完成零件加载后运行，**一次性**完成所有分析工作，直接生成一个只包含必要信息的精简 `.cfg` 补丁文件，然后禁用原始补丁。后续启动时 MM 只需读取这个精简文件，无需再执行复杂的条件判断。

**工作流程：**

1. **首次启动（生成缓存）**
   - DLL 初始化，订阅 `GameEvents.OnPartLoaderLoaded` 事件
   - PartLoader 加载完所有零件后，DLL 开始工作
   - 读取 SimpleRepaint 配置文件：`Colors.cfg`、`Settings.cfg`、`GreyList.cfg`、`IgnoreParts/*.cfg`、`Whitelists/*.*`
   - 遍历 `PartLoader.LoadedPartsList` 中的所有零件（已去重）
   - 对每个零件应用过滤逻辑：
     - 跳过 kerbalEVA 零件
     - 检查忽略列表（IgnoreParts）
     - 检查 `SR_Ignore` 标记
     - 检查白名单（如果启用）
     - 检查是否已有 SimpleRepaint B9PS 模块（避免重复注入）
     - 检查 SSTURecolor / TexturesUnlimited 模块（跳过，它们有更好的重涂支持）
     - 检查灰名单（B9PS 不兼容零件 → 使用 ModulePartVariants 替代）
     - 检查已有其他 B9PS 模块 → 使用 PartVariants 避免材质冲突
   - 为每个符合条件的零件生成对应的 MM 补丁块（每条补丁包含完整的 `:HAS` 条件，确保 MM 每次启动都重新评估零件状态）
   - 将生成的缓存补丁写入 `cache/SimpleRepaintCache.cfg`
   - 计算所有配置文件和零件列表的 MD5 哈希，写入 `cache/cache.manifest`
   - 将原始 `Patches/SimpleRepaint.cfg` 重命名为 `.bak` 以禁用
   - **运行时注入桥接**：将模块直接注入到 PartPrefab，**本次启动立即生效**

2. **后续启动（使用缓存）**
   - DLL 计算当前配置文件和零件列表的 MD5 哈希
   - 与 `cache.manifest` 中保存的哈希值对比
   - **哈希一致** → 缓存有效，MM 加载缓存的精简补丁（`:HAS` 条件动态评估）
   - **哈希不一致** → 缓存失效，重新执行生成流程 + 运行时注入

3. **配置变更时**
   - 修改 Colors.cfg / Settings.cfg / GreyList.cfg / IgnoreParts / Whitelists
   - 安装/卸载零件 mod（零件列表变化）
   - 以上任一变化都会导致哈希不匹配，缓存自动失效并重新生成

**性能提升：**
- 原始方式：MM 每次启动需处理数千条 `:HAS` 条件判断，耗时随零件数量线性增长
- 缓存方式：MM 只需读取一个精简的 `:FINAL` 补丁文件，加载时间几乎恒定
- 零件 mod 越多，性能提升越明显

**注意：**
- 首次启动需要生成缓存，加载速度与原始补丁相当（甚至略慢）
- 从第二次启动起，加载速度将显著加快
- 安装新零件 mod 后，首次启动会重新生成缓存 + 运行时注入，**一次启动即可生效**
- 移除缓存 mod 后，需手动将 `Patches/SimpleRepaint.cfg.bak` 重命名为 `SimpleRepaint.cfg` 以恢复

#### 修复

- **零件名含空格导致 MM 报错**：MM 的 `@PART[name]` 语法不支持空格，生成补丁时自动将零件名中的空格替换为 `?`
- **双重注入导致 `_Color` 材质属性冲突**：`"More than one module can't manage object ... shader property _Color"` 错误的根本原因是原始 SimpleRepaint.cfg 和缓存补丁同时生效导致两个 B9PS 模块争夺材质控制权。已通过以下方式彻底解决：
  - 缓存 .cfg 的每条补丁包含 `:HAS[!MODULE[ModuleB9PartSwitch]:HAS[#moduleID[SimpleRepaint]]]` 条件
  - 原始 .cfg 在缓存生成后被改名 `.bak`，MM 不会同时处理两个补丁
  - 运行时注入有 `alreadyHasSR` 双重检查
- **自带 ModulePartVariants 的零件被错误跳过**：原始 SimpleRepaint 支持给自带 PartVariants 的零件添加独立的颜色切换模块，但 PartAnalyzer 错误地将这些零件全部跳过。已修复：只检查 `ModuleB9PartSwitch` 的 `moduleID = SimpleRepaint`，不再检查 `ModulePartVariants`
- **缓存 .cfg 包含完整 `:HAS` 条件**：每条补丁都包含 `~SR_Ignore[]`、`!MODULE[TexturesUnlimited]`、`!MODULE[SSTURecolor]`、`!MODULE[ModuleB9PartSwitch]:HAS[#moduleID[SimpleRepaint]]` 等条件，B9PS 路径额外包含 `!MODULE[ModuleB9PartSwitch]`，PartVariants 路径额外包含 `!MODULE[ModulePartVariants]`。确保 MM 每次启动都重新评估零件状态，即使其他 mod 改变了零件的模块列表也能正确跳过

---

### English

#### New Feature: SimpleRepaintCache Cache Addon

**How it works:**
SimpleRepaint's original MM patch (`SimpleRepaint.cfg`) uses complex `:HAS` conditions to iterate through all loaded parts × 24 colors on every game startup, generating massive temporary patches that significantly slow down loading times, especially with many part mods installed.

SimpleRepaintCache is a KSPAddon DLL that runs after PartLoader finishes loading all parts. It performs all analysis **once**, generates a streamlined `.cfg` patch file containing only the necessary information, then disables the original patch. On subsequent startups, MM only needs to read this optimized file without executing complex conditional checks.

**Workflow:**

1. **First Launch (Cache Generation)**
   - DLL initializes and subscribes to `GameEvents.OnPartLoaderLoaded`
   - After PartLoader loads all parts, DLL begins processing
   - Reads SimpleRepaint configs: `Colors.cfg`, `Settings.cfg`, `GreyList.cfg`, `IgnoreParts/*.cfg`, `Whitelists/*.*`
   - Iterates through all parts in `PartLoader.LoadedPartsList` (deduplicated)
   - Applies filtering logic to each part:
     - Skip kerbalEVA parts
     - Check ignore list (IgnoreParts)
     - Check `SR_Ignore` flag
     - Check whitelist (if enabled)
     - Check for existing SimpleRepaint B9PS module (avoid duplicates)
     - Check SSTURecolor / TexturesUnlimited modules (skip, they have better repaint support)
     - Check grey list (B9PS incompatible parts → use ModulePartVariants instead)
     - Check for other B9PS modules → use PartVariants to avoid material conflicts
   - Generates MM patch blocks for each qualifying part (each block includes full `:HAS` conditions for dynamic re-evaluation)
   - Writes cache patch to `cache/SimpleRepaintCache.cfg`
   - Computes MD5 hashes of all configs and part list, writes to `cache/cache.manifest`
   - Renames original `Patches/SimpleRepaint.cfg` to `.bak` to disable it
   - **Runtime injection bridge**: injects modules directly into PartPrefab for **immediate effect on this launch**

2. **Subsequent Launches (Cache Usage)**
   - DLL computes current MD5 hashes of configs and part list
   - Compares with saved hashes in `cache.manifest`
   - **Hashes match** → Cache valid, MM loads cached patch (with `:HAS` conditions)
   - **Hashes differ** → Cache invalid, regenerate + runtime injection

3. **Configuration Changes**
   - Modifying Colors.cfg / Settings.cfg / GreyList.cfg / IgnoreParts / Whitelists
   - Installing/uninstalling part mods (part list changes)
   - Any of the above triggers hash mismatch → cache auto-invalidates and regenerates

**Performance Improvement:**
- Original: MM processes thousands of `:HAS` conditions each startup, time scales linearly with part count
- Cached: MM reads a single streamlined `:FINAL` patch file, loading time is nearly constant
- More part mods = greater performance improvement

**Note:**
- First launch generates cache, loading time comparable to (or slightly slower than) original
- From the second launch onward, loading speed improves significantly
- Installing new part mods triggers cache regeneration + runtime injection, **works in one launch**
- To remove the cache mod, manually rename `Patches/SimpleRepaint.cfg.bak` back to `SimpleRepaint.cfg`

#### Fixes

- **Spaces in part names causing MM errors**: MM's `@PART[name]` syntax doesn't support spaces. Part names are now automatically sanitized by replacing spaces with `?` when generating patches
- **`_Color` shader property conflicts from double injection**: The `"More than one module can't manage object ... shader property _Color"` error was caused by the original SimpleRepaint.cfg and cache patch both being active simultaneously. Fixed by:
  - Each cache patch block includes `:HAS[!MODULE[ModuleB9PartSwitch]:HAS[#moduleID[SimpleRepaint]]]` condition
  - Original .cfg renamed to `.bak` after cache generation
  - Runtime injection has `alreadyHasSR` double-check
- **Parts with existing ModulePartVariants incorrectly skipped**: Original SimpleRepaint supports adding independent color switching modules to parts that already have PartVariants, but PartAnalyzer was incorrectly skipping all such parts. Fixed: only check for `ModuleB9PartSwitch` with `moduleID = SimpleRepaint`, no longer check for `ModulePartVariants`
- **Cache .cfg includes full `:HAS` conditions**: Each patch block includes `~SR_Ignore[]`, `!MODULE[TexturesUnlimited]`, `!MODULE[SSTURecolor]`, `!MODULE[ModuleB9PartSwitch]:HAS[#moduleID[SimpleRepaint]]`, plus `!MODULE[ModuleB9PartSwitch]` for B9PS path or `!MODULE[ModulePartVariants]` for PartVariants path. Ensures MM re-evaluates part state on every launch
