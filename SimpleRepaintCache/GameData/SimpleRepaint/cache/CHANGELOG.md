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
   - 遍历 `PartLoader.LoadedPartsList` 中的所有零件
   - 对每个零件应用过滤逻辑：
     - 跳过 kerbalEVA 零件
     - 检查忽略列表（IgnoreParts）
     - 检查 `SR_Ignore` 标记
     - 检查白名单（如果启用）
     - 检查是否已有重绘模块（避免重复注入）
     - 检查灰名单（B9PS 不兼容零件 → 使用 ModulePartVariants 替代）
   - 为每个符合条件的零件生成对应的 MM 补丁块
   - 将生成的缓存补丁写入 `cache/SimpleRepaintCache.cfg`
   - 计算所有配置文件和零件列表的 MD5 哈希，写入 `cache/cache.manifest`
   - 将原始 `Patches/SimpleRepaint.cfg` 重命名为 `.bak` 以禁用

2. **后续启动（使用缓存）**
   - DLL 计算当前配置文件和零件列表的 MD5 哈希
   - 与 `cache.manifest` 中保存的哈希值对比
   - **哈希一致** → 缓存有效，直接禁用原始补丁，MM 加载缓存的精简补丁
   - **哈希不一致** → 缓存失效，恢复原始补丁，重新执行生成流程

3. **配置变更时**
   - 修改 Colors.cfg / Settings.cfg / GreyList.cfg / IgnoreParts / Whitelists
   - 安装/卸载零件 mod（零件列表变化）
   - 以上任一变化都会导致哈希不匹配，缓存自动失效并重新生成

**安全机制：**
- 崩溃恢复：如果游戏异常退出导致原始补丁丢失，DLL 会自动从 `.bak` 恢复
- 异常保护：生成过程中发生异常时自动恢复原始补丁，确保游戏正常运行
- 备份保留：原始补丁始终保留 `.bak` 备份，可手动恢复

**性能提升：**
- 原始方式：MM 每次启动需处理数千条 `:HAS` 条件判断，耗时随零件数量线性增长
- 缓存方式：MM 只需读取一个精简的 `:FINAL` 补丁文件，加载时间几乎恒定
- 零件 mod 越多，性能提升越明显

**注意：**
- 首次启动需要生成缓存，加载速度与原始补丁相当（甚至略慢）
- 从第二次启动起，加载速度将显著加快
- 安装新零件 mod 后，首次启动会重新生成缓存（同样需要多等一次）

#### 修复

- **零件名含空格导致 MM 报错**：MM 的 `@PART[name]` 语法不支持空格，生成补丁时自动将零件名中的空格替换为 `?`
- **B9PS `_Color` 材质属性冲突**：移除了 `SUBTYPE` 中的 `MATERIAL { COLOR { ... } }` 块。B9PS 的 `primaryColor` 属性已能自动设置材质颜色，额外的 MATERIAL 块会导致与其他 mod 的材质控制冲突，产生 `"More than one module can't manage object ... shader property _Color"` 错误。ModulePartVariants 的 `VARIANT` 中也同步移除了 `TEXTURE { _Color = ... }` 块

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
   - Iterates through all parts in `PartLoader.LoadedPartsList`
   - Applies filtering logic to each part:
     - Skip kerbalEVA parts
     - Check ignore list (IgnoreParts)
     - Check `SR_Ignore` flag
     - Check whitelist (if enabled)
     - Check for existing repaint modules (avoid duplicates)
     - Check grey list (B9PS incompatible parts → use ModulePartVariants instead)
   - Generates MM patch blocks for each qualifying part
   - Writes cache patch to `cache/SimpleRepaintCache.cfg`
   - Computes MD5 hashes of all configs and part list, writes to `cache/cache.manifest`
   - Renames original `Patches/SimpleRepaint.cfg` to `.bak` to disable it

2. **Subsequent Launches (Cache Usage)**
   - DLL computes current MD5 hashes of configs and part list
   - Compares with saved hashes in `cache.manifest`
   - **Hashes match** → Cache valid, disable original patch, MM loads cached patch
   - **Hashes differ** → Cache invalid, restore original patch, regenerate cache

3. **Configuration Changes**
   - Modifying Colors.cfg / Settings.cfg / GreyList.cfg / IgnoreParts / Whitelists
   - Installing/uninstalling part mods (part list changes)
   - Any of the above triggers hash mismatch → cache auto-invalidates and regenerates

**Safety Features:**
- Crash recovery: auto-restores original patch from `.bak` if lost due to crash
- Exception protection: auto-restores original patch on generation errors
- Backup preservation: original patch always kept as `.bak` for manual recovery

**Performance Improvement:**
- Original: MM processes thousands of `:HAS` conditions each startup, time scales linearly with part count
- Cached: MM reads a single streamlined `:FINAL` patch file, loading time is nearly constant
- More part mods = greater performance improvement

**Note:**
- First launch generates cache, loading time comparable to (or slightly slower than) original
- From the second launch onward, loading speed improves significantly
- Installing new part mods triggers cache regeneration on next launch (one-time wait)

#### Fixes

- **Spaces in part names causing MM errors**: MM's `@PART[name]` syntax doesn't support spaces. Part names are now automatically sanitized by replacing spaces with `?` when generating patches
- **B9PS `_Color` shader property conflicts**: Removed `MATERIAL { COLOR { ... } }` blocks from `SUBTYPE` sections. B9PS's `primaryColor` property already handles material color assignment automatically. The extra MATERIAL blocks caused conflicts with other mods managing the same material properties, producing `"More than one module can't manage object ... shader property _Color"` errors. Also removed `TEXTURE { _Color = ... }` blocks from ModulePartVariants `VARIANT` sections for consistency
