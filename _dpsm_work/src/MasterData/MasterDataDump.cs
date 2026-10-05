using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using CodeStage.AntiCheat.ObscuredTypes;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Rog.MasterData;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// FEATURE (master data): dump the game's own master tables -- the authoritative names and numbers
/// -- out of the running game, as JSON, so battle analysis can quote the game instead of inferring.
///
/// WHY FROM MEMORY AND NOT FROM DISK
/// The master tables are NOT in the install directory. Measured 2026-10-03: the 7,937 cached
/// AssetBundles in *_Data\Caches are plaintext UnityFS and all parse cleanly (UnityPy, 0 failures),
/// but they contain only UI/sprites/Spine/scenario; a scan for ScriptableObject-only bundles
/// (MonoBehaviour>=20 with zero GameObject/Transform) returned 6 bundles, none of them master data,
/// and the 1,480 TextAssets are all Spine skeletons. The data arrives as JSON from
/// api.cthulhu-rog.net and is deserialised straight into Rog.MasterData.*MasterTable. So the only
/// place it exists in usable form is the live process -- which is where this reads it.
///
/// HOW THE ROWS ARE READ (verified at compile level before implementation; see the report)
/// Every table is a ScriptableObject deriving from MasterTableBase&lt;TRow, TKey&gt;, which stores the
/// rows in a Dictionary&lt;TKey, TRow&gt; named m_cache. Rows are pulled out with the nested
/// ValueCollection.CopyTo(Il2CppArrayBase&lt;TRow&gt;, int) -- a single bulk copy into a native array.
/// That deliberately avoids the three interop landmines this project has already been bitten by:
///   * the Dictionary enumerator, whose Current is a boxed KeyValuePair (value-type field reads are
///     the exact shape that returned -1247486736 for a ValueTuple enum in 1.1.2);
///   * reading the dictionary KEY at all -- every row exposes its own key via the Key property, so
///     no value-type key is ever touched;
///   * TryCast&lt;List&lt;T&gt;&gt; on generic natives, which always fails here.
///
/// The table instances are found with the NON-GENERIC Resources.FindObjectsOfTypeAll(Type), so this
/// needs neither a reference to MasterDataManager (which has no singleton and is not reachable from
/// any type dumped so far) nor a generic method instantiation (see the note at the call site -- the
/// generic overload is an AOT hazard on IL2CPP).
///
/// ANTI-CHEAT
/// Several row fields are CodeStage ACTk types (ObscuredInt/ObscuredString). Those are read ONLY
/// through GameRef.Dec/DecStr, i.e. the game's own decryption, never by reading hiddenValue (XORed)
/// or fakeValue (a honeypot that returns forged data once tampering is detected).
///
/// READ-ONLY with respect to game state: nothing here writes to a table or calls a mutator.
/// Gated by General/MasterDataDump. Produces BepInEx\plugins\DpsMeter\masterdata\*.json.
/// </summary>
public static class MasterDataDump
{
	private const int MaxNotes = 40;

	private static bool _done;

	private static string _dir;

	private static int _tablesOk;

	private static int _tablesMissing;

	private static int _tablesError;

	private static int _rowsWritten;

	private static int _rowsNull;

	private static int _rowErrors;

	private static int _nestedSkipped;

	/// <summary>Tables that had more than one loaded instance (the same class can be loaded repeatedly).</summary>
	private static int _duplicateInstances;

	/// <summary>Instances that could not be read at all while picking the best one.</summary>
	private static int _instErrors;

	private static readonly List<string> Notes = new List<string>();

	private static int _notesSuppressed;

	private static readonly List<string> PerTable = new List<string>();

	/// <summary>
	/// Dump every known master table once per process. Safe to call repeatedly: it is a no-op after a
	/// fully successful run, and it retries on a later battle when no table could be found (which is
	/// what a battle that ended before the tables finished loading looks like).
	/// </summary>
	public static void RunOnce()
	{
		if (_done) return;
		try
		{
			_tablesOk = 0;
			_tablesMissing = 0;
			_tablesError = 0;
			_rowsWritten = 0;
			_rowsNull = 0;
			_rowErrors = 0;
			_nestedSkipped = 0;
			_duplicateInstances = 0;
			_instErrors = 0;
			Notes.Clear();
			_notesSuppressed = 0;
			PerTable.Clear();

			_dir = ResolveDir();
			if (_dir == null)
			{
				Note("输出目录不可用");
				return;
			}

			DumpTableRegistry();

			// 刻印 -- id/name/abilityId are plain (not obscured) on this row.
			Table<int, EngravingMasterTable, EngravingMasterData>("engraving", "刻印", (EngravingMasterTable t) => t.m_cache, delegate (EngravingMasterData r, RowJson o)
			{
				o.N("id", r.id);
				o.S("name", r.name);
				o.N("abilityId", r.abilityId);
			});
			// 刻印效果 -- each engraving carries 5 entries (one per level) and those entries ARE the
			// official 刻印 effect text, so 1.2.1 expands them instead of just counting them.
			Table<int, EngravingAbilityMasterTable, EngravingAbilityMasterData>("engraving_ability", "刻印效果", (EngravingAbilityMasterTable t) => t.m_cache, delegate (EngravingAbilityMasterData r, RowJson o)
			{
				o.N("id", r.id);
				o.N("entryCount", CountOf(r.abilityDataList));
				o.Arr("entries", AbilityEntries(r.abilityDataList));
			});
			// 刻印变异 -- inherits the AbilityMasterData shape and adds a condition.
			Table<int, EngravingMutateMasterTable, EngravingMutateMasterData>("engraving_mutate", "刻印变异", (EngravingMutateMasterTable t) => t.m_cache, delegate (EngravingMutateMasterData r, RowJson o)
			{
				WriteAbilityShape(o, r.id, r.name, r.kana, r.text, r.maxLevel, r.rarity, r.recipeId, r.skipend, r.talentList);
				o.N("conditionCategory", (int)r.conditionCategory);
				o.N("conditionParam", r.conditionParam);
				o.N("disp_category", r.disp_category);
				o.N("sort_priority", r.sort_priority);
			});
			// 刻印强化 -- keyed by (engravingId, level), a ValueTuple, hence the explicit TKey.
			Table<Il2CppSystem.ValueTuple<int, int>, EngravingReinforceMasterTable, EngravingReinforceMasterData>("engraving_reinforce", "刻印强化", (EngravingReinforceMasterTable t) => t.m_cache, delegate (EngravingReinforceMasterData r, RowJson o)
			{
				o.N("engravingId", r.engravingId);
				o.N("level", r.level);
			});
			// 神器 -- AttachAbilityMasterTable's rows are ArtifactAbilityMasterData.
			Table<int, AttachAbilityMasterTable, ArtifactAbilityMasterData>("artifact", "神器", (AttachAbilityMasterTable t) => t.m_cache, delegate (ArtifactAbilityMasterData r, RowJson o)
			{
				WriteAbilityShape(o, r.id, r.name, r.kana, r.text, r.maxLevel, r.rarity, r.recipeId, r.skipend, r.talentList);
				o.N("seriesId", r.seriesId);
				o.N("grade", r.grade);
				o.Code("rarityCode", r.rarity);
				o.N("category", (int)r.category);
				o.N("restrictionForClass", r.restrictionForClass);
				o.N("restrictionForTribe", r.restrictionForTribe);
				o.N("restrictionForElement", r.restrictionForElement);
				o.B("canBeUnknown", r.canBeUnknown);
				o.B("isUnique", r.isUnique);
				o.N("needQuestId", r.needQuestId);
				o.S("flavorText", r.flavorText);
				o.S("acquireLocationText", r.acquireLocationText);
			});
			// 装备 -- the stat block that a damage reconstruction actually needs.
			// NOTE: this table is keyed by string (the row's item_id), not by the numeric id; the
			// compiler is what established that, and it is why TKey is a real type parameter here.
			Table<string, EquipmentMasterTable, EquipmentMasterData>("equipment", "装备", (EquipmentMasterTable t) => t.m_cache, delegate (EquipmentMasterData r, RowJson o)
			{
				o.OI("id", r.id);
				o.OS("name", r.name);
				o.OS("text", r.text);
				o.N("rarity", r.rarity);
				o.S("item_id", r.item_id);
				o.OI("life", r.life);
				o.OI("power", r.power);
				o.OI("defense", r.defense);
				o.OI("magic_resist", r.magic_resist);
				o.OI("range", r.range);
				o.OI("attack_speed", r.attack_speed);
				o.OI("attack_interval", r.attack_interval);
				o.L("sale_price", r.sale_price);
				o.B("salable", r.salable);
				o.N("val_cap", r.val_cap);
				o.N("sort_priority", r.sort_priority);
			});
			// 能力主档 -- the skill/ability master the roster and talent code already reference by id.
			Table<int, AbilityMasterTable, AbilityMasterData>("ability", "能力", (AbilityMasterTable t) => t.m_cache, delegate (AbilityMasterData r, RowJson o)
			{
				WriteAbilityShape(o, r.id, r.name, r.kana, r.text, r.maxLevel, r.rarity, r.recipeId, r.skipend, r.talentList);
			});
			// 潜在(素质) -- maps a素质 id to the unit and rank that grant it.
			Table<int, PotentialMasterTable, PotentialMasterData>("potential", "潜在", (PotentialMasterTable t) => t.m_cache, delegate (PotentialMasterData r, RowJson o)
			{
				o.N("id", r.id);
				o.N("unitId", r.unitId);
				o.N("rank", r.rank);
				o.OI("skillId", r.skillId);
			});
			Table<int, AwakePotentialMasterTable, AwakePotentialMasterData>("awake_potential", "觉醒潜在", (AwakePotentialMasterTable t) => t.m_cache, delegate (AwakePotentialMasterData r, RowJson o)
			{
				o.N("id", r.id);
				o.N("unit_id", r.unit_id);
				o.N("category", (int)r.category);
				o.N("acquire_id", r.acquire_id);
				o.N("auto_skill_id", r.auto_skill_id);
				o.N("cost", r.cost);
			});
			// R63: 自动技能 -- the skill a unit fires on its OWN clock, which is what `awake_potential`
			// points at: measured on the shipped tables, 潜在 category 3 / acquire_id 403 carries
			// `auto_skill_id` = the UNIT id (unit 84 = [賢導]トレイラ -> 84), and category 4 is the empty
			// second slot. Until this table was dumped the auto skill's cycle could only be INFERRED from
			// battle data (effect-channel cadence); these are the game's own numbers.
			//
			// UNITS -- CORRECTED IN R65. R63 published two extra columns (`*CoolTimeFrames`) computed as
			// `seconds * 30` from the claim "the master stores SECONDS". That claim is FALSIFIED for this
			// table: measured 2026-10-06 on a live battle, `Skill.CoolTimeFrame` equals the master's
			// `maxCoolTime` (or `minCoolTime` for a unit below max level) **VERBATIM** for 9 of 9 auto
			// skills -- 150, 210, 240, 300, 420, 2970 all matched exactly and NOT ONE matched x30. So the
			// numbers here ARE the game's own charge unit and the x30 columns were wrong by a factor of 30.
			// They are gone. What a reader needs instead is the RATE, so the envelope carries the
			// process-measured `unitsPerGameSecond` (30.0 measured: the charge counter drains 30 units per
			// game second) -- divide, do not multiply.
			Table<int, AutoSkillMasterTable, AutoSkillMasterData>("auto_skill", "自动技能", (AutoSkillMasterTable t) => t.m_cache, delegate (AutoSkillMasterData r, RowJson o)
			{
				o.OI("id", r.id);
				o.N("iconId", r.iconId);
				o.OS("name", r.name);
				o.OS("text", r.text);
				o.OI("autoActivate", r.autoActivate);
				o.OI("maxLevel", r.maxLevel);
				o.OI("minFirstCoolTime", r.minFirstCoolTime);
				o.OI("maxFirstCoolTime", r.maxFirstCoolTime);
				o.OI("minCoolTime", r.minCoolTime);
				o.OI("maxCoolTime", r.maxCoolTime);
				o.OI("minDurationTime", r.minDurationTime);
				o.OI("maxDurationTime", r.maxDurationTime);
				o.OI("skillRange", r.skillRange);
				o.OI("stock", r.stock);
				o.N("activationType", r.activationType);
				o.N("activationTypeParam", r.activationTypeParam);
				o.N("activationPositionSortId", r.activationPositionSortId);
				o.B("isTargetUnnecessary", r.isTargetUnnecessary);
				o.N("talentCount", CountOf(r.talentList));
				o.Arr("talents", TalentsJson(r.talentList));
				o.N("triggerTimingCount", CountOf(r.triggerTimings));
				o.Arr("triggerTimings", TriggerTimingsJson(r.triggerTimings));
			}, "unitsPerGameSecond", LiveUnitsPerGameSecond());
			// 战斗定义 -- a BattleDefine.Id -> string table (21 rows, one per enum member).
			// CORRECTED 2026-10-03 (1.4.0): the earlier "this is where the battle's coefficients live"
			// note was WRONG and the data falsifies it -- 18 of the 21 values are ids in an id space no
			// other dumped table uses (the few apparent matches, e.g. 10080 = 時蝕者の塵, are
			// coincidences) and only 2000/2001 resolve as numbers (50 / 100 = the 会心率 and 貫通率
			// caps). So the VALUE is exported verbatim rather than interpreted, and the enum NAME is
			// emitted alongside the raw id: the 21 ids are exactly the 21 members of BattleDefine.Id,
			// so naming them is free and turns 21 opaque rows into a labelled table.
			// Keyed by the BattleDefine.Id ENUM, not by int.
			Table<BattleDefine.Id, BattleDefineMasterTable, BattleDefineMasterData>("battle_define", "战斗定义", (BattleDefineMasterTable t) => t.m_cache, delegate (BattleDefineMasterData r, RowJson o)
			{
				o.N("defineId", (int)r.defineId);
				o.S("define", r.defineId.ToString());
				o.S("value", r.value);
			});
			Table<int, AttributeMasterTable, AttributeMasterData>("attribute", "属性", (AttributeMasterTable t) => t.m_cache, delegate (AttributeMasterData r, RowJson o)
			{
				o.N("id", r.m_id);
				o.S("name", r.m_attr_name);
				o.Cnt("attackAdvantage", CountOf(r.m_attackAdvantageData));
				o.Cnt("healAdvantage", CountOf(r.m_healAdvantageData));
			});
			Table<int, JobMasterTable, JobMasterData>("job", "职业", (JobMasterTable t) => t.m_cache, delegate (JobMasterData r, RowJson o)
			{
				o.OI("id", r.id);
				o.OS("name", r.name);
				o.OS("text", r.text);
				o.OI("ability", r.ability);
				o.N("summonType", (int)r.summonType);
				o.N("hitType", (int)r.hitType);
				o.OI("attackRange", r.attackRange);
				o.OI("targetNum", r.targetNum);
				o.OI("attackInterval", r.attackInterval);
				o.OI("attackCount", r.attackCount);
				o.N("targetType", (int)r.targetType);
				o.OI("missile", r.missile);
				o.N("effect", r.effect);
				o.OI("moveSpeed", r.moveSpeed);
				o.OI("sortConfig", r.sortConfig);
				o.OI("filterConfig", r.filterConfig);
				o.OI("chasingConfig1", r.chasingConfig1);
				o.OI("chasingConfig2", r.chasingConfig2);
				o.OI("systemId", r.systemId);
				o.OI("comebackInterval", r.comebackInterval);
				o.OI("subAttack", r.subAttack);
				o.N("extensionType", (int)r.extensionType);
			});
			Table<int, RarityMasterTable, RarityMasterData>("rarity", "稀有度", (RarityMasterTable t) => t.m_cache, delegate (RarityMasterData r, RowJson o)
			{
				o.N("id", r.id);
				o.Code("idCode", r.id);
				o.S("idName", r.idName);
				o.S("name", r.name);
				o.N("orderVal", r.orderVal);
				o.L("maxLevel", (long)r.maxLevel);
			});
			// 细则文本 -- keyed by STRING (DetailTextMasterData.id), which is exactly what battle_define's
			// values point at. Without this table battle_define is a list of bare numbers.
			Table<string, DetailTextMasterTable, DetailTextMasterData>("detail_text", "细则文本", (DetailTextMasterTable t) => t.m_cache, delegate (DetailTextMasterData r, RowJson o)
			{
				o.S("id", r.id);
				o.N("type", r.type);
				o.S("text", r.text);
				o.S("general_text", r.general_text);
			});
			Table<int, DescriptionMasterTable, DescriptionMasterData>("description", "说明", (DescriptionMasterTable t) => t.m_cache, delegate (DescriptionMasterData r, RowJson o)
			{
				o.N("id", r.id);
				o.S("category", r.category);
				o.S("title", r.title);
				o.S("message", r.message);
			});
			// DoT -- previously only observable as 蓄積値 behaviour in the combat log.
			Table<int, DotDamageMasterTable, DotDamageMasterData>("dot_damage", "持续伤害", (DotDamageMasterTable t) => t.m_cache, delegate (DotDamageMasterData r, RowJson o)
			{
				o.OI("dotDamageId", r.dotDamageId);
				o.OI("value", r.value);
				o.OI("activeTime", r.activeTime);
				o.OI("damageDuration", r.damageDuration);
				o.N("referenceType", r.referenceType);
				o.N("attackType", r.attackType);
				o.OI("playEffectId", r.playEffectId);
			});
			// 追击配置 -- the config behind the 1.0.57 追击/貫通 investigation.
			Table<int, ChasingConfigMasterTable, ChasingConfigMasterData>("chasing_config", "追击配置", (ChasingConfigMasterTable t) => t.m_cache, delegate (ChasingConfigMasterData r, RowJson o)
			{
				o.N("id", r.id);
				o.Cnt("items", CountOf(r.data));
			});
			Table<int, TraitTagMasterTable, TraitTagMasterData>("trait_tag", "特性标签", (TraitTagMasterTable t) => t.m_cache, delegate (TraitTagMasterData r, RowJson o)
			{
				o.N("id", r.id);
				o.S("name", r.name);
			});
			Table<int, TribeMasterTable, TribeMasterData>("tribe", "种族", (TribeMasterTable t) => t.m_cache, delegate (TribeMasterData r, RowJson o)
			{
				o.N("id", r.id);
				o.S("tribe", r.tribe);
			});

			// A run that found nothing is almost certainly "the tables were not loaded yet", not
			// "the route is wrong" -- leave _done false so the next battle retries.
			_done = _tablesOk > 0;
		}
		catch (Exception ex)
		{
			Note("整体异常 " + ex.GetType().Name + ": " + ex.Message);
		}
		finally
		{
			string d = Diag();
			Plugin.LogSource.LogInfo(d);
			RuntimeLog.Write(d);
			RuntimeLog.Flush();
		}
	}

	/// <summary>One-line self-report: what was read, and every failure counted rather than swallowed.</summary>
	public static string Diag()
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("[DpsMeter][MASTER] 主数据 表成功=").Append(_tablesOk)
		  .Append(" 表缺失=").Append(_tablesMissing)
		  .Append(" 表异常=").Append(_tablesError)
		  .Append(" 行=").Append(_rowsWritten)
		  .Append(" 空行=").Append(_rowsNull)
		  .Append(" 行异常=").Append(_rowErrors)
		  .Append(" 嵌套跳过=").Append(_nestedSkipped)
		  .Append(" 多实例表=").Append(_duplicateInstances)
		  .Append(" 实例异常=").Append(_instErrors);
		if (PerTable.Count > 0)
		{
			sb.Append(" 明细[");
			sb.Append(string.Join(" ", PerTable.ToArray()));
			sb.Append(']');
		}
		if (Notes.Count > 0)
		{
			sb.Append(" 备注[");
			sb.Append(string.Join(" | ", Notes.ToArray()));
			if (_notesSuppressed > 0) sb.Append(" +").Append(_notesSuppressed);
			sb.Append(']');
		}
		if (_dir != null) sb.Append(" 目录=").Append(_dir);
		return sb.ToString();
	}

	/// <summary>The table-name registry the game itself loads from (string[] of asset names).</summary>
	private static void DumpTableRegistry()
	{
		try
		{
			Il2CppStringArray names = MasterDataManager.LoadDataTableNames;
			int n = names == null ? 0 : names.Length;
			StringBuilder sb = new StringBuilder();
			sb.Append("{\"source\":\"MasterDataManager.LoadDataTableNames\",\"count\":").Append(n).Append(",\"names\":[");
			for (int i = 0; i < n; i++)
			{
				if (i > 0) sb.Append(',');
				sb.Append('"').Append(Esc(names[i])).Append('"');
			}
			sb.Append("]}");
			WriteFile("_table_registry", sb.ToString());
			PerTable.Add("registry=" + n);
		}
		catch (Exception ex)
		{
			Note("表名清单读取异常 " + ex.GetType().Name);
		}
	}

	/// <summary>
	/// Shared registration for every table keyed by int. TKey is a real generic parameter (not fixed to
	/// int) so the ValueTuple-keyed 刻印强化 table can use the same path; TKey is never read, only the
	/// ValueCollection is, which is why a non-int key costs nothing.
	/// </summary>
	/// <summary>
	/// Dump one game table. `envelopeKey`/`envelopeValue` (R65, optional) add ONE measured scalar to the
	/// envelope beside `table`/`key`/`count`, for a table whose numbers are expressed in a unit the reader
	/// cannot see. Only `auto_skill` uses it today: its cooldowns are counts of game updates, and the
	/// update rate is a measurement, not a constant of the file (see the R65 note at the call site).
	/// </summary>
	private static void Table<TKey, TTable, TRow>(string key, string label,
		Func<TTable, Il2CppSystem.Collections.Generic.Dictionary<TKey, TRow>> getRows,
		Action<TRow, RowJson> write,
		string envelopeKey = null, double envelopeValue = 0.0)
		where TTable : MasterTableBase
		where TRow : Il2CppObjectBase
	{
		try
		{
			// The GENERIC overload Resources.FindObjectsOfTypeAll<T>() is deliberately NOT used, even
			// though it exists and compiles. Read its interop body: it invokes
			// MethodInfoStoreGeneric_FindObjectsOfTypeAll_...<T>.Pointer, which is built with
			// MakeGenericMethod. IL2CPP is AOT, so a game type such as EngravingMasterTable has no
			// FindObjectsOfTypeAll<EngravingMasterTable> instantiation in the image and that call
			// would fail at runtime -- a compile-level check cannot see this, which is exactly why the
			// interop implementation was read. The non-generic overload is a plain native call that
			// takes a Type, and Il2CppType.Of<T>() only needs the per-type class pointer that the
			// interop assembly always provides.
			Il2CppReferenceArray<UnityEngine.Object> found =
				Resources.FindObjectsOfTypeAll(Il2CppType.Of<TTable>());
			if (found == null || found.Length == 0)
			{
				_tablesMissing++;
				Note(label + " 未找到已加载实例");
				return;
			}

			// The same table class can have SEVERAL loaded instances (measured 2026-10-03: 能力 had 9,
			// 稀有度 had 2). Taking [0] blindly is how a partial table gets published as the whole table,
			// so the instance with the most rows wins and every count is reported. Ties are harmless --
			// equal counts mean the instances are equivalent for our purposes.
			TTable table = null;
			Il2CppSystem.Collections.Generic.Dictionary<TKey, TRow> rows = null;
			int best = -1;
			StringBuilder counts = new StringBuilder();
			for (int i = 0; i < found.Length; i++)
			{
				try
				{
					TTable cand = found[i].TryCast<TTable>();
					if (cand == null) continue;
					Il2CppSystem.Collections.Generic.Dictionary<TKey, TRow> d = getRows(cand);
					int c = d == null ? -1 : d.Count;
					if (counts.Length > 0) counts.Append('/');
					counts.Append(c);
					if (c > best)
					{
						best = c;
						table = cand;
						rows = d;
					}
					if (found.Length > 1) _duplicateInstances++;
				}
				catch (Exception ex)
				{
					_instErrors++;
					Note(label + " 实例读取异常 " + ex.GetType().Name);
				}
			}

			if (table == null || rows == null)
			{
				_tablesMissing++;
				Note(label + " 实例为空或 m_cache 为空");
				return;
			}
			if (found.Length > 1) Note(label + " 实例=" + found.Length + " 行数[" + counts + "] 取最多");

			int count = rows.Count;
			Il2CppReferenceArray<TRow> arr = new Il2CppReferenceArray<TRow>(count);
			// ValueCollection.CopyTo(Il2CppArrayBase<TRow>, int): one bulk native copy, no enumerator.
			rows.Values.CopyTo(arr, 0);

			StringBuilder sb = new StringBuilder();
			sb.Append("{\"table\":\"").Append(Esc(label)).Append("\",\"key\":\"").Append(Esc(key))
			  .Append("\",\"count\":").Append(count);
			if (!string.IsNullOrEmpty(envelopeKey))
			{
				sb.Append(",\"").Append(Esc(envelopeKey)).Append("\":")
				  .Append(envelopeValue.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
			}
			sb.Append(",\"rows\":[");
			int written = 0;
			for (int i = 0; i < arr.Length; i++)
			{
				TRow row = arr[i];
				if (row == null)
				{
					_rowsNull++;
					continue;
				}
				try
				{
					RowJson o = new RowJson();
					write(row, o);
					if (written > 0) sb.Append(',');
					sb.Append('{').Append(o.Body).Append('}');
					written++;
				}
				catch (Exception ex)
				{
					_rowErrors++;
					Note(label + " 行异常 " + ex.GetType().Name);
				}
			}
			sb.Append("]}");
			WriteFile(key, sb.ToString());

			_tablesOk++;
			_rowsWritten += written;
			PerTable.Add(key + "=" + written);
		}
		catch (Exception ex)
		{
			_tablesError++;
			Note(label + " 表异常 " + ex.GetType().Name + ": " + ex.Message);
		}
	}

	/// <summary>The field set shared by AbilityMasterData and every row type deriving from it.
	/// 1.2.1 expands the talent list rather than only counting it, because the talents carry the actual
	/// numeric parameters (AbilityTalent.Param.num) that a damage reconstruction needs.</summary>
	private static void WriteAbilityShape(RowJson o, ObscuredInt id, ObscuredString name, ObscuredString kana,
		ObscuredString text, ObscuredInt maxLevel, int rarity, int recipeId, bool skipend,
		Il2CppSystem.Collections.Generic.List<AbilityTalent> talents)
	{
		o.OI("id", id);
		o.OS("name", name);
		o.OS("kana", kana);
		o.OS("text", text);
		o.OI("maxLevel", maxLevel);
		o.N("rarity", rarity);
		o.N("recipeId", recipeId);
		o.B("skipend", skipend);
		o.N("talentCount", CountOf(talents));
		o.Arr("talents", TalentsJson(talents));
	}

	/// <summary>Expands one engraving's per-level ability entries (EngravingAbilityMasterData.AbilityData,
	/// which derives from AbilityMasterData and adds only `level`).</summary>
	private static string AbilityEntries(Il2CppSystem.Collections.Generic.List<EngravingAbilityMasterData.AbilityData> list)
	{
		if (list == null) return "";
		StringBuilder sb = new StringBuilder();
		int n;
		try { n = list.Count; } catch { return ""; }
		for (int i = 0; i < n; i++)
		{
			try
			{
				EngravingAbilityMasterData.AbilityData a = list[i];
				if (a == null) continue;
				if (sb.Length > 0) sb.Append(',');
				RowJson o = new RowJson();
				o.OI("id", a.id);
				o.N("level", a.level);
				o.OS("name", a.name);
				o.OS("text", a.text);
				o.OI("maxLevel", a.maxLevel);
				o.N("talentCount", CountOf(a.talentList));
				o.Arr("talents", TalentsJson(a.talentList));
				sb.Append('{').Append(o.Body).Append('}');
			}
			catch { _rowErrors++; }
		}
		return sb.ToString();
	}

	/// <summary>AbilityTalent entries: the id/timing/range plus the numeric parameter arrays.</summary>
	private static string TalentsJson(Il2CppSystem.Collections.Generic.List<AbilityTalent> list)
	{
		if (list == null) return "";
		StringBuilder sb = new StringBuilder();
		int n;
		try { n = list.Count; } catch { return ""; }
		for (int i = 0; i < n; i++)
		{
			try
			{
				AbilityTalent t = list[i];
				if (t == null) continue;
				if (sb.Length > 0) sb.Append(',');
				RowJson o = new RowJson();
				o.OI("talentId", t.talentId);
				o.N("timing", (int)t.timing);
				o.N("range", (int)t.range);
				o.Arr("param", ParamsJson(t.param));
				o.Arr("maxParam", ParamsJson(t.maxParam));
				o.N("triggerCount", CountOf(t.triggerData));
				o.N("activeCount", CountOf(t.activeData));
				sb.Append('{').Append(o.Body).Append('}');
			}
			catch { _rowErrors++; }
		}
		return sb.ToString();
	}

	/// <summary>
	/// R63: SkillMasterDataBase.TriggerTimingData entries (the timings a skill may fire on, e.g. the
	/// 自动技能 start/finish triggers). Nested in SkillMasterDataBase, not top-level -- established by the
	/// compiler, like the AbilityData note below.
	/// </summary>
	private static string TriggerTimingsJson(Il2CppSystem.Collections.Generic.List<SkillMasterDataBase.TriggerTimingData> list)
	{
		if (list == null) return "";
		StringBuilder sb = new StringBuilder();
		int n;
		try { n = list.Count; } catch { return ""; }
		for (int i = 0; i < n; i++)
		{
			try
			{
				SkillMasterDataBase.TriggerTimingData t = list[i];
				if (t == null) continue;
				if (sb.Length > 0) sb.Append(',');
				RowJson o = new RowJson();
				o.N("index", t.index);
				o.N("timing", (int)t.timing);
				sb.Append('{').Append(o.Body).Append('}');
			}
			catch { _rowErrors++; }
		}
		return sb.ToString();
	}

	/// <summary>
	/// R63/R65: the game units per game second as THE LIVE GAME reported them (`Skill.CoolTimeFrame /
	/// Skill.CoolTime`, read by TimeProbe), falling back to the clock policy's default while the probe has
	/// not seen a usable ratio yet. The fallback is a documented constant, not a guess: 30.0 was measured
	/// for every loaded skill (750/25, 1500/50, 1050/35), and R65 confirmed it independently from the AUTO
	/// skill's own charge counter (it drains exactly 30 units per game second).
	/// </summary>
	private static double LiveUnitsPerGameSecond()
	{
		try
		{
			double u = TimeProbe.UnitsPerGameSecond;
			if (u > 0.0) return u;
		}
		catch { }
		return BattleClockPolicy.DefaultUnitsPerGameSecond;
	}

	/// <summary>AbilityTalent.Param is just an array of anti-cheat ints; each element is decrypted
	/// through the game's own accessor, never read raw.</summary>
	private static string ParamsJson(Il2CppSystem.Collections.Generic.List<AbilityTalent.Param> list)
	{
		if (list == null) return "";
		StringBuilder sb = new StringBuilder();
		int n;
		try { n = list.Count; } catch { return ""; }
		for (int i = 0; i < n; i++)
		{
			try
			{
				AbilityTalent.Param p = list[i];
				if (p == null) continue;
				if (sb.Length > 0) sb.Append(',');
				sb.Append('[');
				Il2CppStructArray<ObscuredInt> nums = p.num;
				int m = nums == null ? 0 : nums.Length;
				for (int j = 0; j < m; j++)
				{
					if (j > 0) sb.Append(',');
					sb.Append(GameRef.Dec(nums[j]).ToString(CultureInfo.InvariantCulture));
				}
				sb.Append(']');
			}
			catch { _rowErrors++; }
		}
		return sb.ToString();
	}

	/// <summary>R63: the nested trigger-timing entries of a skill master row.</summary>
	private static int CountOf(Il2CppSystem.Collections.Generic.List<SkillMasterDataBase.TriggerTimingData> list)
	{
		try
		{
			if (list == null) return 0;
			return list.Count;
		}
		catch { return -1; }
	}

	private static int CountOf(Il2CppReferenceArray<AbilityTrigger> arr)
	{
		try
		{
			if (arr == null) return 0;
			return arr.Length;
		}
		catch { return -1; }
	}

	/// <summary>
	/// Some int fields are not numbers at all but a big-endian packed ASCII code. Measured 2026-10-03 on
	/// the dumped table: equipment/artifact `rarity` reads 17201 = 0x4331 = "C1", 21041 = "R1",
	/// 5460529 = "SR1", 1397969457 = "SSR1"; RarityMasterData.id reads 82 = "R", 18514 = "HR".
	/// Publishing those as plain ints is technically correct and practically useless, so the decoded code
	/// is emitted next to the raw value. Returns "" when the bytes are not all printable, i.e. when the
	/// field really is a number.
	/// </summary>
	private static string PackedCode(int v)
	{
		if (v <= 0) return "";
		StringBuilder sb = new StringBuilder(4);
		for (int shift = 24; shift >= 0; shift -= 8)
		{
			int b = (v >> shift) & 0xFF;
			if (b == 0) continue;
			if (b < 0x30 || b > 0x7A) return "";
			sb.Append((char)b);
		}
		return sb.ToString();
	}

	/// <summary>Length of a nested native collection, or -1 when unavailable. Nested contents are not
	/// exported in this phase; the count is what tells us whether we need a second pass.</summary>
	private static int CountOf(Il2CppSystem.Collections.Generic.List<AbilityTalent> list)
	{
		try
		{
			if (list == null) return 0;
			return list.Count;
		}
		catch { return -1; }
	}

	private static int CountOf(Il2CppSystem.Collections.Generic.List<AbilityData> list)
	{
		try
		{
			if (list == null) return 0;
			return list.Count;
		}
		catch { return -1; }
	}

	/// <summary>EngravingAbilityMasterData.abilityDataList uses a NESTED AbilityData type, not the
	/// top-level Rog.MasterData.AbilityData -- established by the compiler, not by reading the dump.</summary>
	private static int CountOf(Il2CppSystem.Collections.Generic.List<EngravingAbilityMasterData.AbilityData> list)
	{
		try
		{
			if (list == null) return 0;
			return list.Count;
		}
		catch { return -1; }
	}

	private static int CountOf(Il2CppReferenceArray<AttributeAdvantageMasterData> arr)
	{
		try
		{
			if (arr == null) return 0;
			return arr.Length;
		}
		catch { return -1; }
	}

	private static int CountOf(Il2CppSystem.Collections.Generic.List<ChasingConfigMasterDataItem> list)
	{
		try
		{
			if (list == null) return 0;
			return list.Count;
		}
		catch { return -1; }
	}

	private static string ResolveDir()
	{
		try
		{
			string d = Path.Combine(Paths.PluginPath, "DpsMeter", "masterdata");
			Directory.CreateDirectory(d);
			return d;
		}
		catch
		{
			try
			{
				string d = Path.Combine(AppContext.BaseDirectory, "masterdata");
				Directory.CreateDirectory(d);
				return d;
			}
			catch { return null; }
		}
	}

	private static void WriteFile(string key, string json)
	{
		try
		{
			File.WriteAllText(Path.Combine(_dir, key + ".json"), json, new UTF8Encoding(false));
		}
		catch (Exception ex)
		{
			Note(key + " 写入失败 " + ex.GetType().Name);
		}
	}

	private static void Note(string s)
	{
		if (Notes.Count < MaxNotes) Notes.Add(s);
		else _notesSuppressed++;
	}

	/// <summary>Minimal JSON string escaping; the project's export uses the same rule.</summary>
	private static string Esc(string t)
	{
		if (string.IsNullOrEmpty(t)) return "";
		StringBuilder sb = new StringBuilder(t.Length + 8);
		foreach (char c in t)
		{
			switch (c)
			{
				case '"': sb.Append("\\\""); break;
				case '\\': sb.Append("\\\\"); break;
				case '\n': sb.Append("\\n"); break;
				case '\r': sb.Append("\\r"); break;
				case '\t': sb.Append("\\t"); break;
				default:
					if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
					else sb.Append(c);
					break;
			}
		}
		return sb.ToString();
	}

	/// <summary>Accumulates one row's fields. Every setter is exception-free by construction; a field
	/// that throws (obscured value unavailable) is caught by the caller's per-row try.</summary>
	private sealed class RowJson
	{
		private readonly StringBuilder _sb = new StringBuilder(256);

		private bool _first = true;

		public string Body => _sb.ToString();

		private void Sep()
		{
			if (!_first) _sb.Append(',');
			_first = false;
		}

		public void N(string k, int v)
		{
			Sep();
			_sb.Append('"').Append(k).Append("\":").Append(v.ToString(CultureInfo.InvariantCulture));
		}

		public void L(string k, long v)
		{
			Sep();
			_sb.Append('"').Append(k).Append("\":").Append(v.ToString(CultureInfo.InvariantCulture));
		}

		public void B(string k, bool v)
		{
			Sep();
			_sb.Append('"').Append(k).Append("\":").Append(v ? "true" : "false");
		}

		public void S(string k, string v)
		{
			Sep();
			_sb.Append('"').Append(k).Append("\":\"").Append(Esc(v)).Append('"');
		}

		/// <summary>Anti-cheat obscured int -- decrypted through the game's own accessor.</summary>
		public void OI(string k, ObscuredInt v)
		{
			N(k, GameRef.Dec(v));
		}

		/// <summary>Anti-cheat obscured string -- decrypted through the game's own accessor.</summary>
		public void OS(string k, ObscuredString v)
		{
			S(k, GameRef.DecStr(v));
		}

		/// <summary>Length of a nested collection. The contents are deliberately NOT exported in this
		/// phase -- only the count, which is what says whether a second pass is needed. Bumping
		/// 嵌套跳过 here keeps that omission counted instead of silent.</summary>
		public void Cnt(string k, int count)
		{
			N(k + "Count", count);
			_nestedSkipped++;
		}

		/// <summary>Appends an already-built JSON array body (see the *Json helpers).</summary>
		public void Arr(string k, string rawArrayBody)
		{
			Sep();
			_sb.Append('"').Append(k).Append("\":[").Append(rawArrayBody).Append(']');
		}

		/// <summary>Packed-ASCII code fields (equipment/artifact rarity): the decoded string, or "".</summary>
		public void Code(string k, int packed)
		{
			string s = PackedCode(packed);
			if (s.Length > 0) S(k, s);
		}
	}
}
