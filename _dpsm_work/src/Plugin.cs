using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace DpsMeter;

[BepInPlugin(BuildInfo.Guid, BuildInfo.Name, BuildInfo.Version)]
public class Plugin : BasePlugin
{
	public static ManualLogSource LogSource;

	public static ConfigEntry<string> CfgOverlayMode;

	public static ConfigEntry<bool> CfgVerbose;

	public static ConfigEntry<bool> CfgShowEnemies;

	public static ConfigEntry<bool> CfgShowSkills;

	public static ConfigEntry<bool> CfgTraceCandidates;

	public static ConfigEntry<bool> CfgChartBothSides;

	public static ConfigEntry<bool> CfgTimerUsesGameTime;

	public static ConfigEntry<bool> CfgChartPerSecond;

	public static ConfigEntry<bool> CfgDamageComposition;

	public static ConfigEntry<bool> CfgWheelScrolls;

	public static ConfigEntry<bool> CfgFilterFriendlyFire;

	/// <summary>Diagnostic: dump every unit's RAW ability texts and the per-clause verdict ([ABIL]).</summary>
	public static ConfigEntry<bool> CfgAbilityDump;

	/// <summary>Battle-clock source: real (stopwatch seconds) | engine (Time.deltaTime) |
	/// game (the GAME's own clock: update steps / units-per-second, see TimeProbe).</summary>
	public static ConfigEntry<string> CfgClockSource;

	/// <summary>Units per game second for CfgClockSource=game. 0 = auto (read from Skill.CoolTimeFrame
	/// / Skill.CoolTime, falling back to 60).</summary>
	public static ConfigEntry<int> CfgGameUnitsPerSecond;

	/// <summary>PROBE: read the attacker's 攻击力 in the prefix of DamageCalculater's attack constructor
	/// (i.e. before the power is computed) and report every candidate attack as [POWER] lines. Used to
	/// identify which reading is the one the game built 计算威力 from.</summary>
	public static ConfigEntry<bool> CfgPowerProbe;

	/// <summary>PROBE (feasibility of the 1.1 "read the game's own bookkeeping" design): at the end of a
	/// battle dump each unit's ability roster with its SOURCE SLOT (eAbilitySlotType: Job / Awaking /
	/// Engraving / Artifact / Skill ...), every talent's activation counters, its structured trigger
	/// condition (eTalentCondType, e.g. IsFrozen / IsIgnoreAttack) and the periodic-talent timers, plus a
	/// micro-benchmark of the read cost. Off by default; see Diagnostics/SlotProbe.cs.</summary>
	public static ConfigEntry<bool> CfgSlotProbe;

	/// <summary>FEATURE (1.1.0): build each unit's ability roster with its SOURCE SLOT
	/// (eAbilitySlotType: 职业特性/觉醒/潜在/专用武器/刻印/神器/皮肤/技能) and expose it in the export and the
	/// detail view, so 刻印/装备/素质 stop being anonymous text lines. Read-only.</summary>
	public static ConfigEntry<bool> CfgAbilityRoster;

	/// <summary>FEATURE (1.1.0): per-hit diff of the game's own talent activation counters, so "did this
	/// hit / this add-on trigger that 素質" is answered by the game's counter instead of by inference.
	/// Read-only, but it reads one int per talent per damage event -- switch off if it ever costs too much.</summary>
	public static ConfigEntry<bool> CfgTalentTriggers;

	/// <summary>Escape hatch for the ONE game method the roster calls
	/// (CharacterDataBase.GetAbilityDetailDataList). It is a read-only call, but it is a game method that
	/// builds a list, so if it ever turns out to be unsafe this can be switched off WITHOUT a rebuild; the
	/// roster then falls back to joining CharacterStatistics.m_abilityStatistics, and any unit it cannot
	/// label is reported as 未分类 instead of being guessed.</summary>
	public static ConfigEntry<bool> CfgRosterRouteA;

	/// <summary>FEATURE (1.2.0): dump the game's own master tables (刻印/神器/装备/能力/潜在/战斗定义/...)
	/// out of the running process as JSON, so battle analysis can quote the game's names and numbers
	/// instead of inferring them. The tables are NOT on disk -- measured 2026-10-03: all 7,937 cached
	/// AssetBundles parse, but they hold only UI/sprites/Spine/scenario, and the master data arrives as
	/// JSON from api.cthulhu-rog.net straight into memory. Read-only; runs once per process at battle end
	/// and reports itself as a [MASTER] line. See MasterData/MasterDataDump.cs.</summary>
	public static ConfigEntry<bool> CfgMasterDataDump;

	/// <summary>FEATURE (1.3.0): emit the structured `calc` object for every hit in the export (the same
	/// numbers the 中文 composition line mentions, plus `pairTrusted` / `valueMatches` as separate
	/// booleans). ON by default -- this is the data the reconciliation KPI is about -- but it is roughly
	/// a fifth of the file size, so it can be switched off without losing the root `reconcile` summary
	/// (that block is derived from the same in-memory records either way).</summary>
	public static ConfigEntry<bool> CfgReconcileCalc;

	/// <summary>FEATURE (1.6.0 阶段 E): write the `contribution` section -- per character, how much of the
	/// team's dealt damage is their own base damage, their own rules, and the rules they provided to
	/// others, with every number traceable to the per-hit fold list (see
	/// _dpsm_work/CONTRIBUTION-DATA-DICTIONARY.md). Derived at export time from the event list, never
	/// accumulated during the battle. It reads calc.fold, so it follows ReconcileCalc: with that off the
	/// section reports the base part only, exactly like the file.</summary>
	public static ConfigEntry<bool> CfgContribution;

	/// <summary>FEATURE (1.7.0 阶段 F): show the 总贡献 dashboard in the overlay -- per character the
	/// base / own-rule / assist / total credit and the share, plus the top rules. It reuses the SAME
	/// computation the export writes (ContributionSession -> Contribution.Compute), so the panel and the
	/// file can never disagree; it is cached and refreshed at most once a second. Off = the panel is
	/// hidden and the export is unaffected.</summary>
	public static ConfigEntry<bool> CfgShowContribution;

	/// <summary>1.3.5: fold 被伤害 modifiers granted to the victim (`m_giveTalentData`).</summary>
	public static ConfigEntry<bool> CfgGivenTalent;

	/// <summary>1.3.7: log each unit's 18 status-resistance slots once (`CharacterDataBase.StatusResistance`).</summary>
	public static ConfigEntry<bool> CfgStatusResist;

	/// <summary>1.4.0: keep a bounded specimen set of the hits the composition could NOT explain, each
	/// with the LIVE status state (resistance, 蓄积 counters, active statuses) of both sides captured at
	/// hit time. This exists so one battle answers every remaining "what is this residual?" question
	/// instead of one battle per hypothesis. See Diagnostics/Forensics.cs.</summary>
	public static ConfigEntry<bool> CfgForensics;

	/// <summary>1.4.1: fold 狂気 (Madness) as an OUTGOING damage multiplier (`最狂比/100`).</summary>
	public static ConfigEntry<bool> CfgMadness;

	/// <summary>1.5.3: fold 狂気 (Madness) on the VICTIM as an INCOMING multiplier (被ダメージ+50%).</summary>
	public static ConfigEntry<bool> CfgMadnessVictim;

	/// <summary>1.5.0 (B4): full-resolution status / status-resistance CHANGE timeline for the victim of
	/// every damage event. See Diagnostics/StateTimelineProbe.cs.</summary>
	public static ConfigEntry<bool> CfgStateTimeline;

	/// <summary>1.5.4 (贡献归因 C): record every 狂気 application with its applier
	/// (TalentActionAddMadness.ActExecute hook). See Diagnostics/StatusApplierProbe.cs.</summary>
	public static ConfigEntry<bool> CfgMadnessApplier;

	/// <summary>1.5.5 (贡献归因 A): record every give-type action application (who granted to whom)
	/// so granted modifiers can be attributed. See Diagnostics/GiveApplierProbe.cs.</summary>
	public static ConfigEntry<bool> CfgGivenGiverHook;

	/// <summary>1.7.2 (阶段 G, step 1): read the OWNER of every entry in a unit's applied-parameter
	/// dictionary (BuffParam.m_owner / ParamData.Owner) into a per-(unit,target,type,value,owner)
	/// UNION, so 攻击力 additions granted by a TEAMMATE stop being anonymous. See
	/// Diagnostics/ParamOwnerProbe.cs.</summary>
	public static ConfigEntry<bool> CfgParamOwners;

	/// <summary>1.5.0 (B1): a deduplicated FACT RECORD for every damage hit, referenced from the event by
	/// `factId`. See Diagnostics/FactStore.cs.</summary>
	public static ConfigEntry<bool> CfgFactStore;

	/// <summary>R52 (证据提取流程): write a self-contained evidence bundle when a battle ends.
	/// R55 (user request, 2026-10-05): ON by default -- writing the bundle must not depend on the user
	/// knowing about a key. A bundle is a ~25 MB copy plus checksums, and ExtractKeep (default 5) bounds
	/// the disk cost; set the config to false to stop writing them. See Diagnostics/EvidenceExtractor.cs.</summary>
	public static ConfigEntry<bool> CfgExtractOnBattleEnd;

	/// <summary>R52: the key that extracts a bundle on demand (default F4; the overlay owns F5-F12).
	/// "NONE"/empty disables the key route and leaves the battle-end route alone.</summary>
	public static ConfigEntry<string> CfgExtractKey;

	/// <summary>R52: how many bundle directories survive (oldest deleted first). Bounded so a long
	/// session cannot fill the disk with 25 MB bundles.</summary>
	public static ConfigEntry<int> CfgExtractKeep;

	/// <summary>PROBE (R64): read each party unit's AUTO SKILL from the live `Skill` side
	/// (`Player.AutoSkill1/2`, `Skill.Type = AutoSkill1ForPassiveSkill/...`) and log the activation
	/// instant plus the charge counter, as [AUTOSK] lines. WHY: R63 published the auto-skill master row
	/// (240-300 s cooldown) but the cadence previously reverse-inferred from a damage channel (~13.5 s)
	/// contradicts it, and the master cooldown cannot be checked without reading the live skill. One
	/// Harmony patch on the game's own command entry point plus a read-only 2 s sampler; see
	/// Diagnostics/AutoSkillProbe.cs.</summary>
	public static ConfigEntry<bool> CfgAutoSkillProbe;

	private Harmony _harmony;

	/// <summary>
	/// Patch the crit/attribute-rate probe separately from PatchAll: PatchAll aborts the entire
	/// patch set when one target fails to resolve, so a diagnostic probe must never be able to
	/// disable every other hook.
	/// </summary>
	private void TryPatchCritProbe()
	{
		try
		{
			var m = AccessTools.Method(typeof(DamageCalculater), "GetFlyTextNumberSizeForAttack");
			if (m == null)
			{
				LogSource.LogInfo("[DpsMeter] GetFlyTextNumberSizeForAttack not found; crit probe skipped.");
				return;
			}
			_harmony.Patch(m, postfix: new HarmonyMethod(typeof(CalcFlyTextSizeHook), nameof(CalcFlyTextSizeHook.Postfix)));
			LogSource.LogInfo("[DpsMeter] crit/attribute-rate probe patch applied.");
		}
		catch (Exception ex)
		{
			LogSource.LogInfo("[DpsMeter] crit probe patch failed (meter unaffected): " + ex.Message);
		}
	}

	/// <summary>
	/// 1.5.4 (贡献归因 C): patch TalentActionAddMadness.ActExecute SEPARATELY from PatchAll --
	/// the same isolation rule as the crit/power probes: a target that fails to resolve must not
	/// kill the patch set, and a combat-only talent-action hook must never touch startup.
	/// </summary>
	private void TryPatchMadnessApplier()
	{
		try
		{
			var m = AccessTools.Method(typeof(TalentActionAddMadness), "ActExecute");
			if (m == null)
			{
				LogSource.LogInfo("[DpsMeter] TalentActionAddMadness.ActExecute not found; madness-applier hook skipped.");
				return;
			}
			_harmony.Patch(m, postfix: new HarmonyMethod(typeof(MadnessApplyHook), nameof(MadnessApplyHook.Postfix)));
			LogSource.LogInfo("[DpsMeter] madness-applier hook applied (TalentActionAddMadness.ActExecute).");
		}
		catch (Exception ex)
		{
			LogSource.LogInfo("[DpsMeter] madness-applier hook failed (meter unaffected): " + ex.Message);
		}
	}

	/// <summary>
	/// 1.5.5 (贡献归因 A, attempt 2): the give-type actions' ActExecute. Same isolation rule as the
	/// other probes -- one patch per class, each guarded, so a class that does not resolve cannot
	/// affect the rest. Which of them the game actually runs is answered by the [GIVAPP] counters.
	/// </summary>
	private void TryPatchGiveApplier()
	{
		TryPatchGiveOne("addTalent", typeof(TalentActionAddTalent), nameof(GiveTalentApplyHooks.PostfixAddTalent));
		TryPatchGiveOne("lottery", typeof(TalentActionAddTalentLottery), nameof(GiveTalentApplyHooks.PostfixAddTalentLottery));
	}

	private void TryPatchGiveOne(string label, Type t, string postfix)
	{
		try
		{
			var m = AccessTools.Method(t, "ActExecute", new Type[] { typeof(BattleObject), typeof(BattleObject), typeof(TalentOption) });
			if (m == null)
			{
				LogSource.LogInfo("[DpsMeter] give-applier hook: " + label + ".ActExecute not found; skipped.");
				return;
			}
			_harmony.Patch(m, postfix: new HarmonyMethod(typeof(GiveTalentApplyHooks), postfix));
			LogSource.LogInfo("[DpsMeter] give-applier hook applied (" + label + ").");
		}
		catch (Exception ex)
		{
			LogSource.LogInfo("[DpsMeter] give-applier hook failed for " + label + " (meter unaffected): " + ex.Message);
		}
	}

	/// <summary>
	/// R64: patch the game's own auto-skill command entry point
	/// (`GameCmdExecuter.ActExecutePlayerAutoSkillForPassive`) SEPARATELY from PatchAll -- same isolation
	/// rule as the other probes. If the signature does not resolve, the probe still reports the charge
	/// and still derives the activation moments from the sampler's rising edge, so the round produces
	/// evidence either way; the [AUTOSK] SUM line states which channel supplied each row.
	/// </summary>
	private void TryPatchAutoSkillActivation()
	{
		try
		{
			var m = AccessTools.Method(typeof(GameCmdExecuter), "ActExecutePlayerAutoSkillForPassive",
				new Type[] { typeof(Player), typeof(int), typeof(UnityEngine.Vector3) });
			if (m == null)
			{
				LogSource.LogInfo("[DpsMeter] GameCmdExecuter.ActExecutePlayerAutoSkillForPassive not found; auto-skill activation postfix skipped (the sampler still records the charge and the poll edge).");
				return;
			}
			_harmony.Patch(m, postfix: new HarmonyMethod(typeof(AutoSkillHooks), nameof(AutoSkillHooks.PostfixAutoSkillForPassive)));
			LogSource.LogInfo("[DpsMeter] auto-skill activation postfix applied (GameCmdExecuter.ActExecutePlayerAutoSkillForPassive).");
		}
		catch (Exception ex)
		{
			LogSource.LogInfo("[DpsMeter] auto-skill activation postfix failed (meter unaffected): " + ex.Message);
		}
	}

	/// <summary>
	/// Patch the attack constructor of DamageCalculater (attacker, blocker, abilityList, draw) to read the
	/// attacker's 攻击力 BEFORE the body computes 计算威力. Manual patch for the same reason as the crit
	/// probe: a constructor whose signature resolves differently must not be able to kill the patch set.
	/// Measured 2026-09-27: this overload is NEVER called during combat (heartbeat ctor1=0), so the other
	/// constructors are probed too -- the heartbeat reports which one the game actually uses.
	/// </summary>
	private void TryPatchPowerProbe()
	{
		var hb = new HarmonyMethod(typeof(CalcCtorProbeHook), nameof(CalcCtorProbeHook.Prefix));
		var hb2 = new HarmonyMethod(typeof(CalcCtor2ProbeHook), nameof(CalcCtor2ProbeHook.Prefix));
		var hb3 = new HarmonyMethod(typeof(CalcCtor3ProbeHook), nameof(CalcCtor3ProbeHook.Prefix));
		var hb4 = new HarmonyMethod(typeof(CalcAddBlockerProbeHook), nameof(CalcAddBlockerProbeHook.Prefix));
		try
		{
			TryPatchCtor("ctor1", new Type[]
			{
				typeof(BattleObject),
				typeof(BattleObject),
				typeof(Il2CppSystem.Collections.Generic.IEnumerable<Ability>),
				typeof(bool)
			}, hb);
		}
		catch (Exception ex) { LogSource.LogInfo("[DpsMeter] ctor1 probe failed: " + ex.Message); }
		try
		{
			TryPatchCtor("ctor2", new Type[]
			{
				typeof(DamageSource),
				typeof(int),
				typeof(Rog.Domain.AttributeModel),
				typeof(Rog.Domain.AttributeModel),
				typeof(BattleObject),
				typeof(eDamageCalcType),
				typeof(bool),
				typeof(int),
				typeof(BattleObject),
				typeof(BattleObject)
			}, hb2);
		}
		catch (Exception ex) { LogSource.LogInfo("[DpsMeter] ctor2 probe failed: " + ex.Message); }
		try
		{
			TryPatchCtor("ctor3", new Type[]
			{
				typeof(int),
				typeof(Il2CppSystem.Nullable<int>),
				typeof(Il2CppSystem.Nullable<int>),
				typeof(int),
				typeof(bool),
				typeof(TalentExecutor)
			}, hb3);
		}
		catch (Exception ex) { LogSource.LogInfo("[DpsMeter] ctor3 probe failed: " + ex.Message); }
		try
		{
			var m = AccessTools.Method(typeof(DamageCalculater), "AddBlocker");
			if (m != null) _harmony.Patch(m, prefix: hb4);
			else LogSource.LogInfo("[DpsMeter] AddBlocker not found (probe skipped).");
		}
		catch (Exception ex) { LogSource.LogInfo("[DpsMeter] AddBlocker probe failed: " + ex.Message); }
	}

	private void TryPatchCtor(string label, Type[] args, HarmonyMethod prefix)
	{
		var m = AccessTools.Constructor(typeof(DamageCalculater), args);
		if (m == null)
		{
			LogSource.LogInfo("[DpsMeter] DamageCalculater " + label + " not found; probe skipped.");
			return;
		}
		_harmony.Patch(m, prefix: prefix);
		LogSource.LogInfo("[DpsMeter] damage-calc " + label + " probe patch applied.");
	}

	// BattleObject.ActDamage is NEVER patched. Do not add a probe here.
	//
	// 1.0.48 patched it (tally only) and the game crashed on startup: the postfix body read
	// BattleObject.ObjectType on a half-constructed object -> uncatchable AccessViolationException.
	// 1.0.49 reduced the postfix to pure managed counters gated on an active battle session -- and the
	// game STILL crashed on startup, this time inside Il2CppInterop's own parameter marshalling
	// (Il2CppObjectPool.Get -> il2cpp_object_get_class), i.e. before the body could run. So for this
	// method even an inert detour is fatal; the only safe detour would take IntPtr parameters only,
	// and the question it was meant to answer is answered without any patch by the [CROSS] line
	// (nominal_taken == game_taken per unit). See Hooks/BattleObjectHooks.cs.

	public override void Load()
	{
		LogSource = Log;
		RuntimeLog.Init();
		CfgOverlayMode = Config.Bind<string>("General", "OverlayMode", "ugui", "Overlay renderer: ugui (default, safe) | imgui (experimental, ClassInjector) | none (hooks only).");
		CfgVerbose = Config.Bind<bool>("Debug", "VerboseEvents", false, "Log every damage/heal event to LogOutput.log (only for verification; keep false in normal play).");
		CfgTraceCandidates = Config.Bind<bool>("Debug", "TraceCandidates", true, "Probe: tally candidate damage code paths (DamageCalculater.*, CharacterStatistics.Add*) and log attribution gaps. Temporary diagnostics. NOTE: BattleObject.ActDamage is deliberately NOT patched -- its detour crashes the game (see Hooks/BattleObjectHooks.cs), and the [CROSS] line answers the same question.");
		CfgShowEnemies = Config.Bind<bool>("Overlay", "ShowEnemies", true, "Show enemy rows in the overlay.");
		CfgShowSkills = Config.Bind<bool>("Overlay", "ShowSkills", false, "Show per-skill damage breakdown under each ally row.");
		CfgChartBothSides = Config.Bind<bool>("Overlay", "ChartBothSides", true, "Damage-time chart includes both teams (party and enemies).");
		CfgWheelScrolls = Config.Bind<bool>("Overlay", "WheelScrolls", true, "Allow mouse wheel / PageUp-PageDown to scroll overlay content (clamped to panel). Disable to fully ignore the wheel.");
		CfgFilterFriendlyFire = Config.Bind<bool>("Overlay", "FilterFriendlyFire", false, "Exclude same-team damage (回復反転 /heal reversal, self-damage) from dealt totals and DPS. Default false: it is kept so totals match the game's own damage report, and it is always labelled as 自伤/回复反噬 in the UI.");
		CfgAbilityDump = Config.Bind<bool>("Debug", "AbilityDump", true, "PROBE: write every unit's raw ability texts plus each damage clause's verdict to the runtime log as [ABIL] lines (once per unit per battle). Needed to see why a modifier was excluded.");
		CfgTimerUsesGameTime = Config.Bind<bool>("General", "TimerUsesGameTime", false, "DEPRECATED alias for ClockSource=engine. Battle-timer clock mode: false = REAL seconds, measured by an internal stopwatch (default; a 17.9 s battle reports 17.9 s, pauses included); true = game-logic seconds via Time.deltaTime (freezes while the game is paused). Do NOT switch this hoping to 'fix' the rate: Time.unscaledDeltaTime in this game is the fixed logic step (1/45 s), not the 1/30 s frame time.");
		CfgClockSource = Config.Bind<string>("General", "ClockSource", "game", "Battle-clock source. game = THE GAME'S OWN CLOCK (default): GameSystem.GameTime update steps divided by the game's units-per-second, so the meter ticks exactly like skill cooldowns do. real = real seconds from an internal stopwatch. engine = Time.deltaTime. MEASURED on 2026-09-27 (timeScale 1.5): GameTime, the on-screen counter GameTimeLimitCounter.NowTime and a skill's wait counter all advance 45.0 units per real second, and Skill.CoolTimeFrame / Skill.CoolTime = 30.0 for every skill (750/25, 1500/50, 1050/35) => 1 game second = 30 units and the game clock runs 45/30 = 1.5x real time. The cooldown itself is unit based: Skill.m_coolTimeFrame / m_waitCountFrame, decremented once per game update.");
		CfgGameUnitsPerSecond = Config.Bind<int>("General", "GameUnitsPerSecond", 0, "ClockSource=game: how many game units make one game second. 0 = auto: use Skill.CoolTimeFrame / Skill.CoolTime from the loaded skill data (measured 30.0), falling back to 30.");
		CfgPowerProbe = Config.Bind<bool>("Debug", "PowerProbe", true, "PROBE: read the attacker's 攻击力 in the prefix of DamageCalculater's attack constructor (before 计算威力 is computed) and log [POWER] lines whenever the candidate attack readings disagree. Used to identify which reading built the power; costs one dictionary write per damage calc.");
		CfgChartPerSecond = Config.Bind<bool>("Overlay", "ChartPerSecond", false, "Chart shows per-second (instant) damage instead of cumulative. F12 toggles in-game.");
		CfgDamageComposition = Config.Bind<bool>("Debug", "DamageComposition", false, "PROBE: log per-hit damage composition (calc power/attribute/hit type + attacker buffs/params) as [COMP] lines for formula analysis.");
		CfgSlotProbe = Config.Bind<bool>("Debug", "SlotProbe", false, "PROBE: once per battle, dump each unit's ability roster with its source slot, every talent's activation counters, its structured trigger condition (eTalentCondType) and the periodic-talent timers, as [SLOT] lines, plus a micro-benchmark of the counter read cost. Superseded by the General/AbilityRoster + General/TalentTriggers features (1.1.0) but kept as the raw evidence dump. Read-only.");
		CfgAbilityRoster = Config.Bind<bool>("General", "AbilityRoster", true, "FEATURE: build each unit's ability roster with its source slot (eAbilitySlotType: 职业特性/觉醒/潜在/专用武器/刻印/神器/皮肤/技能) and show it in the damage-detail view and the export. The slot is recovered from CharacterDataBase.GetAbilityDetailDataList(), falling back to a join against CharacterStatistics.m_abilityStatistics; anything unmatched is reported as 未分类 rather than guessed. Read-only, no new Harmony patches.");
		CfgTalentTriggers = Config.Bind<bool>("General", "TalentTriggers", true, "FEATURE: attribute each hit to the 素质/词条 that fired just before it, using the game's own activation counters (TalentData.ActivateCount), and report the battle-end talent table. This is what answers 'did the 貫通 add-on trigger this character's 素質?' with the game's counter instead of arithmetic. Read-only; costs one counter read per talent per damage event.");
		CfgRosterRouteA = Config.Bind<bool>("Debug", "RosterRouteA", true, "Escape hatch: use CharacterDataBase.GetAbilityDetailDataList() to learn which SLOT each ability came from (刻印/神器/觉醒/职业...). Set to false if that call ever misbehaves -- the roster then falls back to the CharacterStatistics.m_abilityStatistics join and labels whatever it cannot resolve as 未分类.");
		CfgMasterDataDump = Config.Bind<bool>("General", "MasterDataDump", true, "FEATURE: once per process, at battle end, dump the game's own master tables (刻印/神器/装备/能力/潜在/觉醒潜在/战斗定义/属性/职业/稀有度/持续伤害/追击配置/特性标签/种族/刻印变异/刻印强化/刻印效果) to BepInEx\\plugins\\DpsMeter\\masterdata\\*.json, plus the game's own table-name registry. These are the OFFICIAL names and numbers the battle log can then be checked against. The data exists only in memory (it is downloaded as JSON and never written to disk), so it cannot be extracted from the install directory. Read-only, no Harmony patches beyond the existing ones; self-reports as a [MASTER] line. Set false to skip.");
		CfgReconcileCalc = Config.Bind<bool>("General", "ReconcileCalc", true, "FEATURE (1.3.0): write the structured `calc` object for every hit into the export -- the same numbers the Chinese composition line mentions (attackPower/power/ratio/base/attrMult/dealtMult/takenMult/knownMult/theory/applied/residual/defense/penetration/...), plus `pairTrusted` and `valueMatches` as SEPARATE booleans. The old single sentence '本次伤害与该次计算值不符' conflated pairing with arithmetic (measured 2026-10-03: 98.6% of the rows whose arithmetic DID match still carried it) and has been reworded to 配对未获结算对象佐证. The root `reconcile` block (exact/approx/unexplained/theoryExceeds/byPair/byTenth/topResidual) is always written and is derived from the same records, so switching this off only removes the per-hit detail, not the KPI.");
		CfgGivenTalent = Config.Bind<bool>("General", "GivenTalent", true, "FEATURE (1.3.5): fold the 被伤害 modifiers that other units GRANT to the victim, read per hit from the victim's `m_giveTalentData`. This is the cross-unit channel: 刻印 id=26 「ブロックしている敵の被ダメージ+10%（前衛のみ）」 is defined as `type=1004 AddTalent` + payload `type=1006 被伤害- p=[-10]` (timing=Block, cond=GiveTalent(1)), so the carrier hands the modifier to the enemy it blocks and that enemy then takes +10% from EVERY attacker. Measured 2026-10-03 on battle_411001_...125554: the ×1.210 window was identical across all four attackers (per-instant, ON 10.7-19.3 / 22.9-31.4 / 39.2-42.2 / ... s) and 1791 of 5490 hits carried it, while the victim's own ability list and m_buffList had no trace of it. The same measurement showed the attacker-side text scan folding ×1.10 per copy into the CARRIER's own hits was wrong (53/53 hits at residual 0.826 = 1/1.21), so that branch now defers to this channel. 1.3.6 added the cancellation: a granted modifier whose factor this hit's global-rule path already accounted for is dropped instead of added again (measured 2026-10-03: without it 63.9% of a battle landed on 1/1.15^k). Set false to measure without folding: the counts and text stay in the export, the factor just stays in the residual.");
		CfgStatusResist = Config.Bind<bool>("General", "StatusResist", true, "FEATURE (1.3.7): log each unit's 18 status-resistance slots once per battle as a [RESIST] line, read from `BattleObject.Data.m_statusResistance` (class `CharacterStatusResistance`: 眩晕/眩晕蓄积/石化/石化蓄积/毒/毒伤害/击退/火傷/凍結/暗闇/狂気/恐怖/即死/时停/基础状态/移速/攻速/攻隔). These are NOT talents -- the boss's own ability #40009 carries only 1002/0 and 6(攻击力)/150/-1 -- and they are not in any table the master dump can reach, so this is the only way to see them. A compact copy is written to the export as rosterAudit.resistSample. Read-only; failures counted in rosterAudit.resistErrors/resistNull. NOTE (1.4.0, MEASURED): this field is a TEMPLATE and never moves; the value the game actually consults is read by the same switch through `Character.Status.Resistance` and exported as `liveResistFirst`/`liveResistLast`.");
		CfgMadnessVictim = Config.Bind<bool>("General", "MadnessVictim", true, "FEATURE (1.5.3): fold 狂気 (Madness) on the VICTIM as an INCOMING damage multiplier. The 1.4.1 fold only covers the OUTGOING side (the holder DEALS more); 狂気 also raises the damage its holder TAKES (被ダメージ+50%), and the composition never modelled that side, so every hit whose victim carried 狂気 was short by exactly that factor. MEASURED 2026-10-03 on battle_411001_20261003_183213 vs _191159: two clean テトラ cells with a baseline residual of 1.0 go 0.9996 -> 1.4954 and 0.9965 -> 1.5011 (x1.4961 / x1.5063), and the pooled controlled factor is 1.4886 (58.7M damage without 狂気 vs 45.2M with). The factor is the measured 1.5 and NOT ratio/100: MadnessAllyBuffRatio is the ALLY buff (it reads 250 on an enemy, which is not this factor), so a victim-side ratio would over-count. The flag is exported per hit as victimMadnessOn either way, so the rule stays falsifiable from data. Set false to measure without folding: the reading stays in the export and the factor stays in the residual.");
		CfgMadness = Config.Bind<bool>("General", "Madness", true, "FEATURE (1.4.1): fold 狂気 (Madness) as an OUTGOING damage multiplier, read from the ATTACKER's `Character.Status.MadnessAllyBuffRatio` (`CharaStatus`). MEASURED 2026-10-03 on battle_411001_20261003_150140: 924/924 of メアリー's damage records satisfy `residual == 1.15^n x (狂気 ? 2.5 : 1.0)` with ZERO exceptions, where 狂気 is read independently from the per-hit `comp4.自身状态` (834 with 狂気 all reduce to exactly 2.5; the 90 without reduce to 1.0), and `MadnessAllyBuffRatio` reads 250 while 狂気 is on versus 100 otherwise -- so 100 is neutral and the factor is ratio/100. Before this the plugin read 狂気 ONLY as one of the 18 RESISTANCE slots, never as a damage modifier. Both conditions are required (IsMadness AND ratio>100) so a unit holding the status with a neutral ratio cannot be over-counted, and a failed read falls back to x1.0. The raw ratio and the flag are exported per hit (`madnessRatio`/`madnessOn`) either way, so the rule stays falsifiable from data. Set false to measure without folding: the reading stays in the export and the factor stays in the residual.");
		CfgForensics = Config.Bind<bool>("Debug", "Forensics", true, "FEATURE (1.4.0): for every damage record the chain could NOT explain (same predicate as reconcile.exactWithCrit), keep the first 2 hits of each (attacker, residual-to-3-decimals) class -- at most 80 classes -- with the FOUR composition lines plus the LIVE status state of both sides (resistance slots, 蓄积 counters, active statuses, granted-talent text) captured at hit time. Purpose: a residual such as メアリー's 3.306 (= 2.5 x 1.15^2) is by definition a factor the composition never reads, so it cannot be found by re-parsing data that is already exported -- the specimen has to be taken while the game objects are alive. Bounded and read-only; exported as the root `forensics` object and self-reported as a [DpsMeter][FORENSIC] line.");
		CfgStateTimeline = Config.Bind<bool>("General", "StateTimeline", true, "FEATURE (1.5.0 B4): emit a FULL-RESOLUTION change timeline for the VICTIM of every damage event -- which status flags turned on/off (the game's own `CharaStatus.IsPoison`/`IsBurn`/... flags, bit order exported as `timeline.statusFlags`) and which of the 18 status-resistance slots changed value (`timeline.resistSlots` gives the slot order). WHY: the 1.4.1 resistance curve for the boss could only be drawn from 64 decoded value points out of 5,464 hits, because values were read only when the forensics channel kept a specimen, so the chart had to warn that its connecting line was a guide and not a measurement. This channel reads the victim's 18 slots on every damage event and emits a row only when something CHANGED, which makes the curve exact instead of sampled. Victim only on purpose: the receiver's resistance is what moves a damage number, and the attacker's statuses are already in comp4. Read-only; bounded by StateTimeline.MaxRows with the drop count exported; self-reported as a [DpsMeter][TIMELINE] line. Set false if the extra 18 reads per hit ever cost anything measurable.");
		CfgMadnessApplier = Config.Bind<bool>("General", "MadnessApplier", true, "FEATURE (1.5.4 贡献归因 C): record every 狂気 application with its applier (TalentActionAddMadness.ActExecute hook) and stamp the latest applier onto per-hit madness folds (byUnit). Off = the recording stays dark; the patch itself is isolated and cannot affect anything else.");
		CfgGivenGiverHook = Config.Bind<bool>("General", "GivenGiverHook", true, "FEATURE (1.5.5 贡献归因 A): hook the give-type actions (TalentActionAddTalent / AddTalentLottery) and record who granted to whom, so granted modifiers carry byUnit. Off = the hooks stay dark.");
		CfgParamOwners = Config.Bind<bool>("General", "ParamOwners", true, "FEATURE (1.7.2 阶段 G): record WHO owns each entry of a unit's applied-parameter dictionary (BuffParam.m_owner and ParamData.Owner) as a per-unit UNION of (target,type,value,owner). WHY: the contribution model only credits multiplicative folds, so an 攻击力 addition granted by a TEAMMATE sits inside the beneficiary's 基础 and its provider gets 0. MEASURED (master data, 2026-10-04): ability 12060 エンチャンター is the only talent in the whole ability table whose scope is TalentDefine.RangeType 29 (FriendTeamAllExcludeToken), and it grants 攻击力+300 (reference ExistenceTime 3000); ルナリスの artifacts grant +7%/+10% at RangeType 3 (FriendTeamAll). The existing 参数表 read truncates at 14 entries, keeps only the last throttled sample per unit, and prints no owner -- so it can prove the addend exists but never who applied it. This read is additive (new export section paramOwners, the existing params section is untouched), runs at the same 500 ms-per-unit throttle, is bounded at 64 entries/read and 1024 union rows with every drop counted, and records BOTH owner sides plus their disagreement count because which side the game fills is measured, not assumed. Off = section absent, nothing else changes.");
		CfgContribution = Config.Bind<bool>("General", "Contribution", true, "FEATURE (1.6.0 阶段 E): write the `contribution` section of the export (per character: base damage / own-rule credit / assist credit / total credit, per-rule damage equivalents and provider->beneficiary links). Derived from the same per-hit fold list the composition emitted, at export time only -- no battle-time accumulation, no new hooks. It is about 0.04%-0.12% of the file. Off = the export keeps the 1.5.5 structure and offline analysis (which has its own verified core) is unaffected. NOTE: with General/ReconcileCalc=false there are no folds in the file, so the section reports base damage only -- by design, so the file and the section can never disagree.");
		CfgShowContribution = Config.Bind<bool>("General", "ShowContribution", true, "FEATURE (1.7.0 阶段 F): show the 总贡献 dashboard in the overlay (per character: base / own rules / assist / total credit + share, plus the top rules by damage equivalent). It reuses the exact computation the export writes, cached and refreshed at most once a second, so the panel and the exported contribution section are always the same numbers. Requires ReconcileCalc (no folds = no attribution -> the panel says 不可用 instead of showing zeros). Off = panel hidden, export unchanged.");
		CfgFactStore = Config.Bind<bool>("General", "FactStore", true, "FEATURE (1.5.0 B1): give EVERY damage hit a reference (`event.factId`) into a deduplicated fact table (`facts.items`), where each class carries the four composition lines plus the VICTIM's LIVE state (resistance slots, 蓄积 counters, active statuses) read at that class's first sight. WHY: live state can only be read while the game objects are alive (`FinalizeLocked` clears `ActorStats.Source` immediately after the export), so anything not captured during the battle is unrecoverable; before 1.5.0 that capture happened for at most 160 hits of 5,501 (3%), which is why every new question cost another battle. MEASURED: those 5,501 hits collapse to 331-463 distinct classes across four exports, and 2,838 distinct composition quadruples out of 5,501 events, so a bounded deduped table covers 100% of hits inside the byte budget that used to buy 3%. The expensive live read runs only for the first `MaxLiveClasses` (420) classes; both overflow counters are exported. Bounded and read-only; self-reported as a [DpsMeter][FACT] line. Set false to skip it entirely.");
		CfgExtractOnBattleEnd = Config.Bind<bool>("General", "ExtractOnBattleEnd", true, "FEATURE (R52 证据提取流程): at battle end, write a SELF-CONTAINED evidence bundle to BepInEx\\plugins\\DpsMeter\\extract\\<stamp>\\ containing (1) battle.json from the same serializer as the normal export, (2) contrib_census.json -- every UNRESOLVED fold grouped by reason/kind/origin/label/factor with its victim and, for the granted channel, the loadout-side carrier verdict (who HOLDS a rule that grants that modifier), (3) masterdata/ as a copy of the game's own table dump, (4) manifest.json hashing every file plus the deployed assembly. WHY: 'what is unknown_kind made of' and 'why is the giver always null' used to cost a one-off script over a 24 MB file, and the answer was not reproducible. ON by default (user request, 2026-10-05): writing the bundle must not depend on the user knowing about a key. A bundle is a ~25 MB copy, so ExtractKeep (default 5) bounds the disk cost; set this false to stop writing them, and press the key below for a one-off on demand. Never throws into the finalisation.");
		CfgExtractKey = Config.Bind<string>("General", "ExtractKey", ExtractPolicy.DefaultKey, "FEATURE (R52): press this key for an evidence bundle on demand (works in and out of a battle). F1-F12, A-Z or 0-9; NONE disables it. F4 by default because the overlay already owns F5-F12 and F8/F9 must keep their meanings. Every press writes a bundle, so ExtractKeep bounds how many stay on disk.");
		CfgExtractKeep = Config.Bind<int>("General", "ExtractKeep", ExtractPolicy.DefaultKeep, "FEATURE (R52): how many evidence bundles to keep under BepInEx\\plugins\\DpsMeter\\extract (oldest deleted first, decided by a pure string sort of the timestamped directory names). 1..50.");
		CfgAutoSkillProbe = Config.Bind<bool>("Debug", "AutoSkillProbe", true, "PROBE (R64): read each party unit's AUTO SKILL from the live Skill side and log (a) the instant it fires as an [AUTOSK] act row and (b) its charge counter every 2 s as an [AUTOSK] chg row, plus a per-slot median interval at battle end. WHY: R63 published the auto-skill master row (暗沌への導き: minCoolTime/maxCoolTime = 300/240 s = 9000/7200 frames) but the ~13.5 s cadence earlier reverse-inferred from a damage channel contradicts it, and the master number cannot be checked without the live skill -- the auto skill is NOT in the standby list the [CLOCKP] line walks (verified: that list holds 地下からの完全顕現/電脳掌都/狂気の眼球, and only 暗沌への導き of those four names is in auto_skill.json). One isolated Harmony postfix on GameCmdExecuter.ActExecutePlayerAutoSkillForPassive + a read-only sampler (Player.AutoSkill1/2, Skill.Type/GetStatus/WaitCountFrame/CoolTimeFrame); if the patch does not resolve, the sampler's rising edge still times the activations and the SUM line says so. Set false to stop both.");
		try
		{
			_harmony = new Harmony("dev.dpsmeter");
			_harmony.PatchAll();
			LogSource.LogInfo("[DpsMeter] Harmony patches applied.");
			TryPatchCritProbe();
			TryPatchPowerProbe();
			TryPatchMadnessApplier();
			TryPatchGiveApplier();
			TryPatchAutoSkillActivation();
		}
		catch (Exception ex)
		{
			LogSource.LogError($"[DpsMeter] Harmony init failed: {ex}");
		}
		string text = CfgOverlayMode.Value.Trim().ToLowerInvariant();
		if (!(text == "ugui"))
		{
			if (text == "imgui")
			{
				InitImGuiOverlay();
			}
			else
			{
				LogSource.LogInfo("[DpsMeter] Overlay disabled (OverlayMode=none). Hooks only.");
			}
		}
		else
		{
			OverlayUGUI.Create();
		}
		LogSource.LogInfo("[DpsMeter] DpsMeter " + BuildInfo.Version + " loaded. F8 show/hide, F9 reset, F10 roster/chart, F11 both-side chart, "
				+ (ExtractPolicy.DescribeKey(ExtractPolicy.ParseVirtualKey(CfgExtractKey == null ? null : CfgExtractKey.Value)))
				+ " = evidence bundle (ExtractOnBattleEnd=" + (CfgExtractOnBattleEnd != null && CfgExtractOnBattleEnd.Value ? "on" : "off") + ").");
		RuntimeLog.Write("[DpsMeter] Load complete.");
		RuntimeLog.Flush();
		DumpEnums();
	}

	/// <summary>One-off dump of game enum names (for mapping buff status types to readable labels).</summary>
	private static void DumpEnums()
	{
		try
		{
			var sb = new System.Text.StringBuilder("[DpsMeter][ENUM] ");
			try { sb.Append("CharacterStatus.Type=").Append(string.Join(",", Enum.GetNames(typeof(CharacterStatus.Type)))).Append(" | "); } catch (Exception e1) { sb.Append("CharacterStatus.Type=(").Append(e1.GetType().Name).Append(") | "); }
			try { sb.Append("BuffTarget=").Append(string.Join(",", Enum.GetNames(typeof(BuffParamData.BuffTarget)))).Append(" | "); } catch (Exception e2) { sb.Append("BuffTarget=(").Append(e2.GetType().Name).Append(") | "); }
			try { sb.Append("BuffType=").Append(string.Join(",", Enum.GetNames(typeof(BuffParamData.BuffType)))).Append(" | "); } catch (Exception e3) { sb.Append("BuffType=(").Append(e3.GetType().Name).Append(") | "); }
			try { sb.Append("CalcType=").Append(string.Join(",", Enum.GetNames(typeof(eDamageCalcType)))).Append(" | "); } catch (Exception e4) { sb.Append("CalcType=(").Append(e4.GetType().Name).Append(") | "); }
			try { sb.Append("DamageSource=").Append(string.Join(",", Enum.GetNames(typeof(DamageSource)))); } catch (Exception e5) { sb.Append("DamageSource=(").Append(e5.GetType().Name).Append(")"); }
			RuntimeLog.Write(sb.ToString());
			RuntimeLog.Flush();
		}
		catch { }
	}

	private void InitImGuiOverlay()
	{
		try
		{
			LogSource.LogInfo("[DpsMeter] Registering overlay type...");
			ClassInjector.RegisterTypeInIl2Cpp<OverlayUI>();
			LogSource.LogInfo("[DpsMeter] Creating overlay GameObject...");
			GameObject val = new GameObject("DpsMeterOverlay");
			UnityEngine.Object.DontDestroyOnLoad(val);
			val.hideFlags = HideFlags.HideAndDontSave;
			LogSource.LogInfo("[DpsMeter] Adding overlay component...");
			val.AddComponent<OverlayUI>();
			LogSource.LogInfo("[DpsMeter] Overlay component created.");
		}
		catch (Exception ex)
		{
			LogSource.LogError($"[DpsMeter] IMGUI overlay init failed: {ex}");
		}
	}

	public override bool Unload()
	{
		if (_harmony != null) _harmony.UnpatchSelf();
		return true;
	}
}
