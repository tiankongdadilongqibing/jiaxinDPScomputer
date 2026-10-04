# DMM 客户端解包与游戏内数据获取 — 实机勘查报告

> 目标:把「战斗日志反推机制」升级为「用官方主数据核对机制」。
> 本文所有结论都在本机 `D:\dmmplayer\rlyehshoujotaix_cl` 上**实测验证**过,不是推测。

---

## 0. 一句话结论

- 缓存里 **7,937 个 AssetBundle 全部可读且未加密**(UnityPy 1.25.3 全量扫描 **0 失败**)——但里面**只有 UI / 立绘 / 骨骼动画 / 剧情**,**没有主数据**。
- 主数据是**服务器下发 JSON**,在内存里反序列化成 `Rog.MasterData.*MasterTable` 对象,**从不落盘**。
- 因此「解包」对主数据无效;正确路径是**运行时从内存把主数据 dump 出来**——而这条路已经被打通到字段级。

---

## 1. 实测到的客户端形态

| 项 | 值 | 来源 |
|---|---|---|
| 引擎 | Unity **6000.3.5f2** | `UnityFS` 头里的 `unityRevision`;`unity-libs\6000.3.5.zip` |
| 脚本后端 | **IL2CPP** | `GameAssembly.dll` 83 MB + `global-metadata.dat` 16.2 MB |
| 公司 / 包名 | `jp.co.fanzagames` | `*_Data\app.info` |
| 启动器 | DMM GAME PLAYER 5.3.1(本体是 Electron) | `D:\dmmplayer\DMMGamePlayer-Setup-5.3.1.exe` |
| 资源缓存 | `*_Data\Caches`,**35,572 文件 / 2.87 GB** | 实测 |
| 其中 bundle | **7,937 个 `__data`**(+7,938 个 `__info`) | 实测 |

### 缓存目录结构(Unity 内建 AssetBundle 缓存)

```
Caches\<64 hex>\<32 hex>\__data      ← 真正的 UnityFS 包
Caches\<64 hex>\<32 hex>\__info      ← 4 行文本:过期时间 / 时间戳 / 计数 / __data
```

- `<64 hex>` = 前 32 位(每包唯一)+ 后 32 位(同一版本批共享)。
- **`__info` 不含 URL** → 无法从元数据直接还原包名。
- **包名在包内部**:每个包的 `AssetBundle.m_Name` = `"<32 hex>.bytes"`,而这段 32 hex **正好等于缓存目录名的前 32 位**。

```
缓存目录 .../46c67dc2297f292f6c875aef506051c4/46c67dc2297f292f6c875aef506051c4/__data
  → AssetBundle.m_Name = "46c67dc2297f292f6c875aef506051c4.bytes"
  → 容器路径           = "assets/assetbundleresources/battle/btl_setting/preloadassetpathlist.asset"
```

**这就是「缓存哈希 → 真实包名」的还原公式。**

---

## 2. 解包工具链实测结果

### 2.1 UnityPy 1.25.3 —— 可用,推荐

```powershell
python -m pip install UnityPy        # 实测:网络可用,依赖 lz4/brotli/etcpak 等一并装上
```

```python
import UnityPy
env = UnityPy.load(r"...\Caches\<64hex>\<32hex>\__data")
print(len(env.objects), len(env.container))
for o in env.objects:
    if o.type.name == 'AssetBundle':
        d = o.read()
        print(d.m_Name, list(d.m_Container)[:5])
```

- 对 Unity **6000.3.5** 的 `UnityFS` 包**完全兼容**。
- 全量 7,937 包扫描:**178 秒,0 错误**(约 45 包/秒;大包会拖慢)。
- 头部字段解读:`55 6E 69 74 79 46 53 00` = `UnityFS\0`,版本 8,`5.x.x`,**`6000.3.5f2`**;`flags = 0x243`(LZ4HC + 压缩块信息)。**没有自定义头、没有加密、没有偏移扰动。**

### 2.2 缓存里到底有什么(全量统计)

| 对象类型 | 数量 |
|---|---|
| MonoBehaviour | 419,682 |
| GameObject | 343,237 |
| RectTransform | 326,114 |
| CanvasRenderer | 251,192 |
| MonoScript | 32,687 |
| Sprite / Texture2D | 23,172 / 21,221 |
| AudioClip | 3,869 |
| **TextAsset** | **1,480** |

容器路径前缀几乎全是:
`icon/shop` `icon/wallet` `icon/skill` `icon/artifact` `icon/equipment` `ui/*` `sprite/*` `spriteatlas/*` `mapground/*` `font/*` `advscene/*` `minigame/*`

- **1,480 个 TextAsset 全部是 Spine 骨骼动画的 JSON/atlas**,不是主数据。
- 剧情文本在 `assets/advscene/scenarioexcel/**/*.book.asset`(约 660 个场景)。

### 2.3 主数据不在缓存里的**决定性证据**

主数据表是 `ScriptableObject`,**没有 GameObject / Transform**。按这个特征筛全量索引:

> `MonoBehaviour >= 20` 且 `GameObject == 0` 且 `Transform+RectTransform == 0` → **只有 6 个包**

这 6 个是 `sortfilter/filter/condition/*.asset`(筛选条件)和 `motion/easing/*.asset`(缓动曲线),**都不是主数据**。
`container` 里也**没有任何** `master*` / `onmemorydata*` 路径。

**结论:主数据确实不在本地资源缓存中。**

---

## 3. 主数据从哪来:三条独立证据链

### 证据 1 — 域名(从 `global-metadata.dat` 的字符串字面量挖出)

```
https://api.cthulhu-rog.net                          ← 游戏 API 根
https://api.cthulhu-rog.net/gha/payment/{procedure,confirmation}-gp
https://assets.cthulhu-rog.net/assetbundles/         ← AssetBundle CDN 根
https://assets.cthulhu-rog.net/res/production/information/news/1/news.json
https://sdk-gameplayer.dmm.com/api/sdk               ← DMM 平台 SDK
https://point.dmm.co.jp/choice/pay?...
```

实测:`news.json` 返回 **200**(140 KB JSON);`assetbundles/<hash>.bytes` 返回 **403**(需要签名/会话,不是公开静态资源)。字符串字面量在 metadata 里被**连成长块**(`.NET` 字符串堆合并),所以必须用「全字节正则」而不是「按行读字符串」才能挖出来。

### 证据 2 — 序列化器与 API 客户端程序集

- `global-metadata.dat` 含 `Newtonsoft.Json`;interop 目录含 **`Utf8Json.dll`** 与 **`ServerApiInterface.dll`(8.95 MB / 5,843 个类)**。
- `ServerApiInterface.dll` 是**自动生成的 API 客户端**:每个端点都有 `app.api.XxxRequest` / `app.api.XxxResponse200`,以及配套的 `Utf8Json.Formatters.app.api.XxxFormatter`。
  → **主数据是 Utf8Json 解析的 JSON,来自 API。**

### 证据 3 — 加载器就在类型系统里

`Rog.MasterData.MasterDataManager`:

```csharp
public static Il2CppStringArray LoadDataTableNamesBeforTitle;   // 表名清单(启动前)
public static Il2CppStringArray LoadDataTableNames;             // 表名清单(全量)
public Dictionary<string, MasterTableBase> m_cache;             // ← 表名 → 已加载的表
public Dictionary<string, MasterTableBase> m_builtinCache;      // ← 内置表
public IEnumerator Load();                 // 协程
public IEnumerator LoadBeforeTitle();
public void LoadBuiltin();
public void LoadBuiltinTables();
public T    LoadFromCache<T>(string dataAssetName)          where T : MasterTableBase;   // CallerCount(0)
public bool TryLoadFromCache<T>(string dataAssetName, out T dest) where T: MasterTableBase; // CallerCount(205)
```

- **205 处**调用 `TryLoadFromCache<T>` → 游戏到处按「表资源名」取表。
- 另有 `<MasterData>k__BackingField` / `<MasterDataUpdated>` 事件。
- `globalgamemanagers` 里有 `onmemorydata/builtinmastertablelist` → **内置表清单**资源。

---

## 4. 主数据的结构(这是「深入分析」的关键)

全量类名扫描(从 `globalgamemanagers.assets` + metadata)得到 **216 个 `*MasterTable`** 类。与战斗分析直接相关的:

```
AbilityMasterTable              AbilityData / 技能主档
AttachAbilityMasterTable        神器·附属能力
ArtifactAbilityMasterTable      神器能力
EngravingMasterTable            刻印          ← 官方刻印名
EngravingAbilityMasterTable     刻印效果
EngravingMutateMasterTable      刻印变异
EngravingReinforceMasterTable   刻印强化
EquipmentMasterTable            装备          ← 官方装备名+数值
EquipmentRankPatternMasterTable 装备品阶
EquipmentSynthesisMasterTable   装备合成
PotentialMasterTable            潜在(素质)
AwakePotentialMasterTable       觉醒潜在
PossessionAbilityMasterTable    持有能力
JobMasterTable / JobRankMasterTable / AttributeMasterTable / PropertyMasterTable
TalentDefine (enum)             素质定义
RogueTalentMasterTable / RogueOracleTalentMasterTable / RoguePassiveTalentMasterTable
ChasingConfigMasterTable        追击配置      ← 之前的「追击触发条件」
DotDamageMasterTable            DoT 伤害
DamageFieldMasterTable          伤害场
MissileMasterTable / SpecialAttackMasterTable / SubAttackMasterTable
BattleDefineMasterTable         ← 战斗系数定义!
DetailTextMasterTable / DescriptionMasterTable   文本
```

### 表 → 行的通用基类

```csharp
public class MasterTableBase<T1, T2> : MasterTableBase<T1> where T1 : class {
    public Dictionary<T2, T1> m_cache { get; }   // ← 全部行,可直接枚举
    public T1   Get(T2 key);
    public bool TryGet(T2 key, out T1 result);
    public bool ContainsKey(T2 key);
}
```

例如 `EngravingMasterTable : MasterTableBase<EngravingMasterData, int>` → `m_cache` 就是 `Dictionary<int, EngravingMasterData>`。

### 行类型实例(已反编译确认的字段)

```csharp
class EngravingMasterData        { int id; string name; ResourceMasterList resourceMasterList; int abilityId; }
class EngravingAbilityMasterData { int id; List<AbilityData> abilityDataList; }
class AttachAbilityMasterData    { int seriesId; int grade; int restrictionForClass/Tribe/Element;
                                   ArtifactAbilityCategory category; string flavorText;
                                   string acquireLocationText; bool canBeUnknown; bool isUnique; int needQuestId; }
class EquipmentMasterData        { ObscuredInt id; ObscuredString name; ObscuredString text;
                                   int rarity; string item_id; ObscuredInt life/power/defense/magic_resist/
                                   range/attack_speed/attack_interval; long sale_price; bool salable;
                                   int val_cap; int sort_priority; }
class AbilityMasterData          { ObscuredInt id; int rarity; ObscuredString name/kana/text;
                                   ObscuredInt maxLevel; int recipeId; bool skipend;
                                   List<AbilityTalent> talentList; Il2CppReferenceArray<AbilityTalent> m_talents; }
```

→ `EngravingMasterData.name` 就是**官方刻印名**;`EquipmentMasterData.name/text` 就是**官方装备名与说明**。这正是设计阶段 4 缺的「母表」。

---

## 5. ⚠️ 两个必须知道的陷阱

### 陷阱 1 — 游戏带 **Anti-Cheat Toolkit (ACTk)**

`global-metadata.dat` 里:`CodeStage` ×16、`Obscured` ×159、`ObscuredString` ×5、`ObscuredPrefs` ×21、`InjectionDetector` ×2、`ACTk` ×102、`AesManaged`/`RijndaelManaged`。

- interop 里有 **`ACTk.Runtime.dll`(531 KB)**。
- `*_Data\Save\*`(34 个数字命名的文件)**全是高熵密文,无明文** → `ObscuredPrefs` 加密存档,不是主数据。

**`ObscuredInt` 的真实结构:**

```csharp
public int hiddenValue;       // XOR 过的值 —— 裸读它是错的!
public int currentCryptoKey;  // 密钥
public int fakeValue;         // ← 蜜罐:内存被改时用它骗你
public static implicit operator int(ObscuredInt value);   // ★ 正确读法(内含解密)
```

**必须走隐式转换 / `GetDecryptedValue`,绝不能裸读 `hiddenValue`,更不能信 `fakeValue`。**
这是本项目反复踩过的同一类坑:「兜底读法给出看起来合理但错误的值」。

> 注意:`EngravingMasterData` 用的是**明文** `int`/`string`,而 `AbilityMasterData`/`EquipmentMasterData` 是 **`ObscuredInt`/`ObscuredString`**。同一个游戏里两种混用 —— **必须逐字段确认**,不能假定。

### 陷阱 2 — DMM 平台层与游戏层是两套

`Caches` 是**游戏**的 Unity 缓存;DMM GAME PLAYER 本体是 Electron,自己的数据在
`%APPDATA%\dmmgameplayer5`(含 Chromium 的 `Local Storage` leveldb、`logs\app.log`)与 `%LOCALAPPDATA%\dmmgameplayer5-updater`。
**排查问题时别把两边搞混**(实测在 DMM 目录里翻半天只会得到 Chromium 的噪声)。

---

## 6. 推荐路径

### ✅ 路线 A(推荐):运行时 dump 主数据

理由:主数据不落盘,API 需会话鉴权 + ACTk 混淆,而**内存里已经是解好的对象**。
成本:复用本插件已有的 BepInEx + Il2CppInterop 基建,**不需要新的解包工具**。

```
1. 拿表实例
   a) Resources.FindObjectsOfTypeAll<T>()                    ← 不用知道谁持有 manager(1.2.0 采用这条)
   b) 或 hook MasterDataManager.Initialize / 构造函数 捕获实例,读 m_cache(表名 → 表)
2. 读行:table.m_cache  →  Il2CppSystem.Collections.Generic.Dictionary<TKey, TRow>
3. 一次批量复制出来(不要用枚举器,也不要读键 —— 见第 10 节的编译级验证结论):
        var arr = new Il2CppReferenceArray<TRow>(dict.Count);
        dict.Values.CopyTo(arr, 0);        // ValueCollection.CopyTo(Il2CppArrayBase<TRow>, int)
4. 逐字段读值:遇到 ObscuredInt/ObscuredString 必须走 GetDecrypted / GameRef.Dec·DecStr
5. 序列化成 JSON,作为「官方母表」落盘,供战斗对账使用
```

**先做最有价值的 8 张表**:`Engraving*`(4)、`Equipment*`、`ArtifactAbility`、`Ability`、`Potential`、`ChasingConfig`、`BattleDefine`、`DetailText`。

### ⛔ 路线 B:离线解包 —— 对主数据无效

已实测排除了:
- 资源缓存 7,937 包全扫,**0 个主数据表**;
- `_Data` 内嵌资源(`resources.assets` 1.8 MB、`.resS` 29 MB、`globalgamemanagers.assets` 868 KB)**没有主数据**;里面的日文串是 SDF 字体的字形表;
- `Save\*` 是高熵密文。

另外离线读主数据即使拿到包也要**类型树**(IL2CPP 包内不含 typetree),而主数据根本不在包里 —— 这条路不必再走。

### ⚠️ 路线 C:API 重放(可行但要鉴权)

`ServerApiInterface.dll` 里 5,843 个类完整描述了每个端点的请求/响应。
障碍:需要 DMM 会话令牌 + ACTk 校验 + UA/Referer。收益不足以优先做。

---

## 7. 附带收获(与当前插件直接相关)

1. **翻译包不是数据源**:`BepInEx\plugins\JashinsenkiTranslationMod\translation\zh-CN\pack.json` 13 MB / 660 场景 / 47,560 条,但**只有剧情文本**(且只存中文,日文原文只有 581 条含假名)。系统/技能/刻印文本**不在里面** —— 别指望它。
2. **`bypass` 了「不知道刻印/神器叫什么」这个死结**:`EngravingMasterData.name` + `EquipmentMasterData.name/text` 直接给官方名。之前靠 id 聚类反推槽位语义(职业特性/潜在/神器/刻印)的做法,可以用母表**直接验证**。
3. **`BattleDefineMasterTable` 值得优先 dump**:战斗系数定义可能一次性解释掉「对账率只有 35.8%」里的一大块。
4. `Newtonsoft.Json` + `Utf8Json` 双序列化器在内存里 → 反序列化后的对象结构就是最干净的接口。

---

## 8. 工具链与社区资料(含出处)

### 8.1 本机实测可用的工具

| 工具 | 版本 | 本机状态 | 备注 |
|---|---|---|---|
| **UnityPy** | **1.25.3** | ✅ 已装,7,937 包全扫 0 失败 | `pip install UnityPy`(网络可用) |
| ilspycmd | 8.2.0 | ✅ 已装 | 反编译 interop 程序集 |
| BepInEx | 6.0.0-be.785 | ✅ 已注入 | 插件基建现成 |

### 8.2 离线读 IL2CPP `MonoBehaviour` 的正规做法

主数据虽然不在缓存里,但如果以后需要离线读**任何 IL2CPP 的 MonoBehaviour**(UI 预制体、筛选条件资产等),
UnityPy 提供 `TypeTreeGenerator`,可直接吃 IL2CPP 元数据:

```python
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

generator = TypeTreeGenerator("6000.3.5f2")
generator.load_il2cpp(il2cpp=open("GameAssembly.dll","rb").read(),
                      metadata=open("global-metadata.dat","rb").read())
env = UnityPy.load(path)
env.typetree_generator = generator
obj.parse_as_object()          # MonoBehaviour 现在能读出字段
```

出处:[UnityPy README — TypeTreeGenerator](https://raw.githubusercontent.com/K0lb3/UnityPy/master/README.md)
(需额外 `pip install TypeTreeGeneratorAPI`)。

UnityPy 还提供两个逃生口(本游戏用不到,但值得记):
- `UnityPy.set_assetbundle_decrypt_key(key)` —— 对应 Unity 中国版的 `AssetBundle.SetAssetBundleDecryptKey`;本游戏**未使用**(包头是明文 `UnityFS`)。
- `CompressionHelper.DECOMPRESSION_MAP[flag] = fn` —— 自定义块解压,应对非标准压缩。
- ⚠️ 已知风险:typetree 的 C 实现**可能直接让 Python 崩溃**,必要时 `TypeTreeHelper.read_typetree_boost = False`。

### 8.3 图形化工具(AssetStudio / AssetRipper)对 Unity 6 的支持现状

[AssetStudio(Razviar fork)CURRENT_STATE.md](https://github.com/Razviar/assetstudio/blob/main/CURRENT_STATE.md)(2025-11-27,v2.4.0)明确记载:

- **Unity 6000 支持**:Texture2D / Sprite 预览、Mesh、Material、GameObject 层级、AnimationClip ✅
- **Shader 解析不完整**:在 Unity `6000.0.58f2` 上仍有 1,082 个 shader 失败(有优雅降级,不崩)
- SkinnedMeshRenderer 有解析问题
- 测试环境是 Marvel Snap(Unity 6000.0.58f2);Genshin(2020.3,含 MiHoYo 加密)正常

对本项目的影响:**可以忽略** —— 我们要的是数据表,不是 shader。但要知道「Unity 6 上 AssetStudio 不是 100% 干净」。
另外本机包版本是 **6000.3.5f2**,比它验证过的 6000.0.58f2 更新,属于未验证区间。

### 8.4 ACTk 的 `fakeValue` 蜜罐

[Anti-Cheat Toolkit 官方文档 — ObscuredCheatingDetector](https://docs.codestage.net/actk/api/CodeStage.AntiCheat.Detectors.ObscuredCheatingDetector.html)
确认 `fakeValue` 机制:正常情况下 `fakeValue` 保存一份真实值副本用于比对,
**一旦检测到内存被篡改就返回伪造值**。所以:
- 裸读 `hiddenValue` → 得到 XOR 后的错值;
- 相信 `fakeValue` → 可能拿到**故意骗你的值**;
- **唯一正确做法:走 `implicit operator int` / `GetDecryptedValue`。**

### 8.5 本游戏已有的社区资料

- [`lafayas/JashinsenkiTranslation`](https://github.com/lafayas/JashinsenkiTranslation) —— 本游戏的**中文汉化 Mod**,已安装在本机(`BepInEx\plugins\JashinsenkiTranslationMod`)。
  其 `translation\zh-CN\pack.json`(13 MB / 660 场景 / 47,560 条 / 生成于 2026-10-02)是**剧情文本**翻译,
  **不含系统/技能/刻印/装备文本** → 对主数据目标**无用**。(实测确认)
- 检索本作解包资料时,社区讨论主要集中在 **Live2DHub** 等论坛的 DMM 系作品解包帖;
  本作没有公开的 datamine 库,所以本文的结论基本靠本机实测得出。

---

## 9. 诚实边界

- ~~本文没有真的 dump 出主数据~~ → **已于 1.2.0 实现**(见第 10 节)。本文的证据链本身不变:*包全部可读*、*主数据不在包里*、*加载器与字段结构已定位到字段级*、*读取时的混淆陷阱已识别*。
- `Resources.FindObjectsOfTypeAll` 在 Il2CppInterop 下的**实机行为仍未验证**(本项目有过 `TryCast` 失败的先例)。1.2.0 的 `[MASTER]` 自检行就是为这个写的:跑一场即可判定。
- `assetbundles/` 的 403 **尚未确定**是「需要签名」还是「路径含子目录」;没继续试,因为对主数据目标无关。
- `MasterDataManager` 的**持有者未知**(现有 dump 里没有别的类引用它)→ 1.2.0 因此改走 `Resources.FindObjectsOfTypeAll<T>()`,完全绕开 manager。
- `ACTk` 的 `fakeValue` 蜜罐在**读主数据时会不会被触发**未知;保守做法是一律走 `GetDecrypted()`(1.2.0 已如此实现,并新增 `GameRef.DecStr`)。

---

## 10. 实施前的编译级验证(1.2.0,结论已落地)

把整条路线写成一份**会编译、永不执行**的代码(`_dpsm_work\routeA_probe\`,引用同一批 interop 程序集),
让编译器当裁判。**它推翻了我 6 个假设** —— 每一条如果靠猜就会在运行时变成静默错值或崩溃:

| 假设 | 编译器的裁决 |
|---|---|
| `Dictionary` 有 `CopyTo(Il2CppArrayBase<T>, int)` | ❌ 那两个属于**嵌套的** `KeyCollection`/`ValueCollection`(第 771 / 1272 行,缩进 8);`Dictionary` 自身的 `CopyTo`(第 2264 行)只收 `KeyValuePair[]` |
| `new Il2CppReferenceArray<string>(n)` | ❌ `Il2CppReferenceArray<T>` 要求 `T : Il2CppObjectBase` → 字符串数组必须用 `Il2CppStringArray`(它继承 `Il2CppArrayBase<string>`) |
| `Il2CppSystem.Type.Of<T>()` | ❌ 真名是 `Il2CppInterop.Runtime.Il2CppType.Of<T>()` |
| `EquipmentMasterTable` 按 `int` 键 | ❌ 实际是 `Dictionary<string, EquipmentMasterData>`(按 `item_id`) |
| `BattleDefineMasterTable` 按 `int` 键 | ❌ 实际是 `Dictionary<BattleDefine.Id, BattleDefineMasterData>`(枚举键) |
| `EngravingAbilityMasterData.abilityDataList` 用顶层 `AbilityData` | ❌ 用的是**嵌套类型** `EngravingAbilityMasterData.AbilityData` |

通过项(未报错即成立):`table.m_cache` 可达、`rows.Values.CopyTo(...)`、`MasterDataManager` 的静态字段与两个泛型方法、`ObscuredInt/ObscuredString.GetDecrypted()`、17 张目标表类型全部可达。探针最终 **0 警告 0 错误**。

### ⚠️ 编译级验证**看不见**的第 7 个坑(读 interop 实现才抓到)

`Resources.FindObjectsOfTypeAll<T>()` **编译通过**,但如果去读它的 interop 本体:

```csharp
IL2CPP.il2cpp_runtime_invoke(MethodInfoStoreGeneric_FindObjectsOfTypeAll_..._<T>.Pointer, ...)
```

那个 `Pointer` 是 `il2cpp_method_get_from_reflection(...).MakeGenericMethod(...)` 建出来的。
**IL2CPP 是 AOT 的 —— 镜像里不存在 `FindObjectsOfTypeAll<EngravingMasterTable>` 这个实例化**,
所以它会在**运行时**失败,而编译器完全看不见。`Resources.ConvertObjects<T>()` 同病,不能用来兜底。

正确姿势(1.2.0 已采用):**非泛型**重载 `FindObjectsOfTypeAll(Il2CppType.Of<TTable>())`
—— 它是直接的原生方法指针 —— 再对元素 `TryCast<TTable>()`(`Il2CppType.Of<T>` 只用每类型生成的类指针,
`TryCast` 走 `il2cpp_class_is_assignable_from`,两者都安全)。

**判据(两边差别是本质的)**:

| | 是否安全 | 理由 |
|---|---|---|
| 泛型**类型**,如 `Dictionary<int, EngravingMasterData>` | ✅ | 它就是 `EngravingMasterTable.m_cache` 的字段类型 → **游戏自己用了它** → 镜像里必然存在 |
| 泛型**方法**套游戏类型,如 `FindObjectsOfTypeAll<EngravingMasterTable>()` | ❌ 不可假设 | 只有游戏恰好调用过那个实例化才存在 |

**这条是这个项目里第一个"编译级验证通过、只有读 interop 实现才能发现"的坑** —— 值得记住:
编译级验证能抓 API 形状,抓不了 AOT 实例化。

**补充:主数据的键类型并不统一** —— `int`(刻印/能力/潜在/职业/…)、`string`(装备,按 `item_id`)、
`BattleDefine.Id`(战斗定义)、`ValueTuple<int,int>`(刻印强化)。这正是 `Table<TKey,TTable,TRow>`
把 TKey 做成真泛型参数的原因(**键从头到尾不被读取**,只有 `Values` 被复制)。

### 1.2.0 实现与验证记录

- 新模块 `src/MasterData/MasterDataDump.cs`;17 张表 + `_table_registry.json`;输出到
  `BepInEx\plugins\DpsMeter\masterdata\*.json`;开关 `General/MasterDataDump`(新键,不继承旧值)。
- 只导出标量/字符串/枚举 + 嵌套集合**长度**;嵌套内容留待第二遍,并以 `嵌套跳过=` **计数而非静默丢弃**。
- 自检行 `[DpsMeter][MASTER] 表成功=… 表缺失=… 表异常=… 行=… 空行=… 行异常=… 嵌套跳过=… 明细[…] 备注[…] 目录=…`。
- 构建 0 警告 0 错误;反编译核对**构建产物**含 `MasterDataDump.RunOnce/Diag`、泛型 `Table`、
  `Values.CopyTo(...)`;`GameRef.DecStr`、`Plugin.CfgMasterDataDump` 均在;守卫全通过;
  部署 SHA256 `02CF64915C559BD2DD2A3724787D976C1E06233C3F6DA95DCF07C5A36E297549`(与构建产物一致),
  备份 `DpsMeter-1.1.3.bak`。
- **仍未验证**:`Resources.FindObjectsOfTypeAll` 的实机行为、`Obscured*` 解密在**主数据行上**的表现。
  需要一场真实战斗的 `[MASTER]` 行来判定。
