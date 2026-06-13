# SimpleRepaintCache

**SimpleRepaint 的缓存附属 Mod — 大幅缩短游戏加载时间**

## 概述

### 问题

SimpleRepaint 的原始 MM 补丁（`SimpleRepaint.cfg`）在每次游戏启动时，都会遍历所有已加载零件 × 24 种颜色，通过复杂的 `:HAS` 条件判断为每个零件生成 B9PartSwitch 或 ModulePartVariants 模块。当安装了大量零件 mod 时，MM 需要处理成千上万条补丁规则，严重拖慢加载速度。

### 解决方案

SimpleRepaintCache 是一个 KSPAddon DLL，它在 GameDatabase 加载完成后（MM 处理完所有补丁后，主菜单出现前）运行，**一次性**完成所有分析工作，直接生成一个只包含必要信息的精简 `.cfg` 补丁文件，然后将原始补丁重命名为 `.bak` 并用 stub 文件占位。后续启动时 MM 只需读取这个精简文件，无需再执行复杂的条件判断。

**零件 mod 越多，性能提升越明显。**

---

## 工作流程

### 首次启动（生成缓存）

```
游戏启动 → MM 处理原始 SimpleRepaint.cfg（正常上色）
         → GameDatabase 加载完成（MM 处理完所有补丁）
         → SimpleRepaintCache DLL 启动
         → 读取配置文件（Colors.cfg, Settings.cfg, GreyList.cfg 等）
         → 从 GameDatabase 读取所有零件配置，应用过滤逻辑
         → 生成缓存补丁 SimpleRepaintCache.cfg
         → 计算 MD5 哈希，写入 cache.manifest
         → 将原始 SimpleRepaint.cfg 重命名为 .bak
         → 写入 stub 文件到 SimpleRepaint.cfg（含恢复说明）
         → 主菜单出现（无需进入任何场景）
```

### 后续启动（使用缓存）

```
游戏启动 → MM 读取 SimpleRepaint.cfg（stub 文件，仅含注释）
         → MM 读取 SimpleRepaintCache.cfg（精简补丁，快速加载）
         → PartLoader 加载所有零件
         → SimpleRepaintCache DLL 启动
         → 计算当前 MD5 哈希
         → 与 cache.manifest 对比
         → 哈希一致 → 缓存有效，不做任何操作
         → 游戏继续，上色正常
```

### 配置变更时

```
修改 Colors.cfg / 安装新零件 mod / 修改 GreyList.cfg 等
         → 下次启动时 MD5 哈希不匹配
         → 缓存失效，重新执行生成流程
         → 生成新缓存 → 改名 .bak → 写入 stub
         → 本次启动使用原始补丁上色（.bak 已被覆盖，但 stub 确保 MM 跳过）
         → 再下一次启动使用新缓存
```

---

## 零件过滤逻辑

SimpleRepaintCache 对每个零件按以下顺序判断是否注入重涂模块：

```
1. 是 kerbalEVA 零件？                    → 跳过
2. 在忽略列表（IgnoreParts）中？            → 跳过
3. 有 SR_Ignore = true 标记？              → 跳过
4. 白名单模式启用且不在白名单中？            → 跳过
5. 已有 SimpleRepaint B9PS 模块
   （moduleID=SimpleRepaint）？             → 跳过（防重复注入）
   （其他 moduleID 的 B9PS 不冲突，正常注入）
6. 有 SSTURecolor / SSTURecolorGUI /
   TexturesUnlimited？                     → 跳过（它们有更好的重涂支持）
7. 在灰名单（GreyList）中？
   ├─ 是 → 使用 ModulePartVariants 注入
   │      └─ 如果已有 ModulePartVariants → 跳过（不重复添加）
   └─ 否 → 使用 ModuleB9PartSwitch 注入
```

---

## 文件结构

```
GameData/SimpleRepaint/
├── cache/                          ← SimpleRepaintCache 目录
│   ├── SimpleRepaintCache.dll      ← KSPAddon DLL
│   ├── SimpleRepaintCache.cfg      ← 生成的缓存补丁（MM 加载）
│   ├── cache.manifest              ← MD5 哈希清单
│   └── README.md                   ← 本文件
├── Patches/
│   ├── SimpleRepaint.cfg           ← stub 文件（仅含注释，MM 无操作）
│   └── SimpleRepaint.cfg.bak       ← 被禁用的原始补丁
├── Colors.cfg                      ← 颜色配置
├── Settings.cfg                    ← 设置
├── GreyList.cfg                    ← B9PS 不兼容零件列表
├── IgnoreParts.cfg                 ← 忽略列表
├── IgnoreParts/                    ← 按 mod 分类的忽略列表
├── Whitelists/                     ← 白名单
└── Localization/                   ← 本地化文件
```

---

## 安装方法

1. 将 `SimpleRepaintCache/GameData/SimpleRepaint/cache/` 整个目录复制到你的 KSP `GameData/SimpleRepaint/` 目录下
2. 确保 `SimpleRepaintCache.dll` 位于 `GameData/SimpleRepaint/cache/SimpleRepaintCache.dll`
3. 启动游戏，首次启动会生成缓存（加载速度与原始补丁相当）
4. 从第二次启动起，加载速度将显著加快

### 卸载方法

1. 删除 `GameData/SimpleRepaint/cache/` 目录
2. 删除 `GameData/SimpleRepaint/Patches/SimpleRepaint.cfg`（stub 文件）
3. 将 `GameData/SimpleRepaint/Patches/SimpleRepaint.cfg.bak` 改名为 `SimpleRepaint.cfg`
4. 游戏恢复原始 SimpleRepaint 行为

---

## 性能对比

| 场景 | 原始 SimpleRepaint | 使用 SimpleRepaintCache |
|------|-------------------|------------------------|
| 首次启动 | 慢（MM 处理所有条件） | 慢（生成缓存） |
| 后续启动 | 慢（MM 处理所有条件） | **快**（MM 读取精简补丁） |
| 新增零件 mod | 慢（MM 处理所有条件） | 慢一次（重新生成缓存），之后快 |
| 零件 mod 越多 | 越慢 | 几乎不受影响 |

---

## 技术细节

### 哈希校验

缓存有效性通过 MD5 哈希校验判断。以下内容的 MD5 被计算并保存在 `cache.manifest` 中：

- `Colors.cfg` 的内容
- `Settings.cfg` 的内容
- `GreyList.cfg` 的内容
- `IgnoreParts.cfg` 的内容
- `IgnoreParts/` 目录下所有 `.cfg` 文件的内容
- `Whitelists/` 目录下所有文件的内容
- 当前所有已加载零件的名称列表（排序后）

任一变化都会导致哈希不匹配，缓存自动失效。

### .bak + stub 切换机制

缓存生成后，原始 `SimpleRepaint.cfg` 被重命名为 `SimpleRepaint.cfg.bak`，然后在原位置写入一个 stub 文件（仅含注释，指导如何恢复）。MM 读取 stub 文件时不会执行任何操作。缓存补丁 `SimpleRepaintCache.cfg` 使用 `:FINAL` 确保在 MM 处理顺序的最后应用。

### 补丁格式

缓存补丁中的每条 `@PART` 块都包含完整的 `:HAS` 条件：

```
@PART[partName]:HAS[
  ~SR_Ignore[],
  !MODULE[TexturesUnlimited],
  !MODULE[SSTURecolor],
  !MODULE[SSTURecolorGUI],
  !MODULE[ModuleB9PartSwitch]:HAS[#moduleID[SimpleRepaint]]
]:FINAL
```

这确保 MM 在每次启动时都重新评估零件状态，即使其他 mod 改变了零件的模块列表也能正确跳过。注意只排除 `moduleID=SimpleRepaint` 的 B9PS 模块，零件可以有其他用途的 B9PS 模块共存。

---

## 注意事项

- **首次启动**需要生成缓存，加载速度与原始补丁相当（甚至略慢）
- **新增零件 mod 后**，首次启动会重新生成缓存，**需要重启一次才能生效**（本次启动使用原始补丁上色）
- **移除缓存 mod**：删除 `cache/` 目录 + 删除 stub 文件 + 将 `.bak` 改回 `.cfg` 即可恢复
- **兼容性**：与所有 SimpleRepaint 支持的 mod 兼容，因为过滤逻辑完全复制自原始补丁
- **触发时机**：缓存生成在 GameDatabase 加载完成后立即执行，无需进入任何场景

---

## 更新日志

### 1.0.2 — 代码精简与触发时机优化

- **迁移到 GameDatabase 触发**：从 `OnPartLoaderLoaded` 改为 `OnGameDatabaseLoaded`，缓存生成在 MM 处理完所有补丁后立即执行，无需进入任何场景
- **移除 RuntimeInjector**：完全移除运行时注入桥接，缓存系统为纯 MM 补丁方案
- **代码精简**：删除遗留的旧 `AnalyzeAllParts` 方法（~120 行死代码），清理未使用的 using 引用，模块检测改用 switch 语句
- **修复 B9PS 冲突检测**：只排除 `moduleID=SimpleRepaint` 的 B9PS 模块，零件可以有其他用途的 B9PS 模块共存

### 1.0.1 — 修复运行时注入崩溃

- **移除运行时注入桥接**：`RuntimeInjector.InjectModules` 在 PartLoader 完成后调用 `partPrefab.AddModule()` 动态添加模块，导致零件初始化状态不一致，在 VAB 中添加新部件时触发崩溃。已完全移除运行时注入，缓存系统改为纯 MM 补丁方案。
- **简化 SwitchToCacheMode**：总是将原始 `SimpleRepaint.cfg` 重命名为 `.bak`（即使已存在 `.bak` 也覆盖），不再保留 stub 文件。

### 1.0.0 — 初始版本

- 实现完整的缓存生成系统
- 支持 B9PartSwitch 和 ModulePartVariants 两种注入方式
- 支持灰名单、白名单、忽略列表
- 支持 SSTURecolor / TexturesUnlimited 检测跳过
- 支持零件名空格和点号/下划线兼容
- MD5 哈希校验自动失效缓存
