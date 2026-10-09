using System.Globalization;

namespace DpsMeter;

/// <summary>
/// R87 (方案A): the DECLARATION side of an 攻击力加算 -- "which (type, value, reference) triples does a
/// unit's loadout declare", expressed in the SAME vocabulary the per-hit runtime read uses, so the two
/// can be compared without either side guessing.
///
/// WHY THIS FILE EXISTS. `Model/AtkAddFold.cs` decides self-vs-grant by `giverKey == attackerKey`, and
/// for a team-wide "編成時、味方全員に付与" grant the game writes `ParamData.Owner` = the HOLDER, not the
/// giver. So such a grant is classified self, its fold is never emitted, and the granter gets no credit
/// at all (measured: 569 self values in one battle, `atkAdd.selfValues`). The only remaining evidence of
/// who granted it is the LOADOUT -- some unit's ability declares exactly that triple. This file is the
/// vocabulary that lets the census (Policy/AtkAddCensusPolicy.cs) match runtime evidence against it.
///
/// THE TWO MAPPINGS ARE MEASURED, NOT ASSUMED. Neither table can be read off the code -- the talent side
/// is `TalentDefine.Type` (an int) while the runtime side is `BuffParamData.ParamData.Type` (an enum that
/// prints "Rate"/"Actual"/"Fixed"), and the reference code (51..54) is an int on the talent side while the
/// runtime prints it as a NAME inside the `Ref` string. Every row below is pinned by an ability whose
/// printed NAME states the same thing, so a reader can re-derive the whole table from the corpus:
///
///   talent type 6  &lt;-&gt; "Rate"    : ネフェスティス 被动 id20076 「棺の解放」 declares type=6 p=[30,150,0]
///                                    and the battle emitted 攻击力加算 Rate+30 from it.
///   talent type 8  &lt;-&gt; "Actual"  : [闇惑]モネモネ 潜在 id88 declares type=8 p=[4,0,51] and the battle
///                                    emitted 攻击力加算 Actual+4(/refCurrentLife0) from it.
///   ref 51 &lt;-&gt; CurrentLife        : 刻印 id25 「現在耐久の2%分の値を攻撃力に加算する」 declares p=[2,0,51].
///   ref 52 &lt;-&gt; CurrentPower       : [賢導]トレイラ 潜在 id84 declares p=[100,0,52]; the emitted item is
///                                    Actual+100(/refCurrentPower0).
///   ref 53 &lt;-&gt; CurrentDefense     : 刻印 id23 「現在物理防御の50%分の値を攻撃力に加算する」 p=[50,0,53].
///   ref 54 &lt;-&gt; CurrentMagicDefense: 刻印 id24 「現在魔法防御の50%分の値を攻撃力に加算する」 p=[50,0,54].
///
/// WHAT THIS FILE REFUSES. A talent type or a reference code that is NOT in the table above returns ""
/// and the census then reports that entry as `undeclared` -- a VISIBLE "we do not know", never a guess.
/// That is deliberate: a wrong mapping must show up as a bucket count, not as a wrong attribution.
///
/// Pure on purpose (no Unity, no IL2CPP, no Plugin, no Aggregator): the whole table and both key builders
/// are compiled into the behaviour suite, so a reader can hold the mapping to the corpus instead of
/// trusting a comment.
/// </summary>
internal static class AtkAddDeclarePolicy
{
	/// <summary>No declaration found in the holder's own team.</summary>
	public const int DeclarerNone = 0;

	/// <summary>More than one unit of the holder's team declares it -- refused, never picked.</summary>
	public const int DeclarerAmbiguous = -1;

	/// <summary>TalentDefine.Type -&gt; the name <c>ParamData.Type.ToString()</c> prints. "" = not modelled.</summary>
	public static string TalentTypeName(int talentType)
	{
		// 6/7/8/9/10 are 攻击力%+ / 攻击力%- / 攻击力+ / 攻击力- / 攻击力= (Composition/TalentNames.cs
		// BuffType). Only the two that carry an ADDEND reach AtkAddFold's Rate/Actual branches; the
		// percent-minus forms are not additions and are left out rather than mapped speculatively.
		if (talentType == 6) return "Rate";
		if (talentType == 8) return "Actual";
		return "";
	}

	/// <summary>Reference code (talent p[2]) -&gt; the name the runtime prints inside the Ref string.
	/// 0 means "this addend references nothing" and is the empty name on BOTH sides.</summary>
	public static string RefTypeName(int refCode)
	{
		switch (refCode)
		{
			case 0: return "";
			case 51: return "CurrentLife";
			case 52: return "CurrentPower";
			case 53: return "CurrentDefense";
			case 54: return "CurrentMagicDefense";
			default: return "";
		}
	}

	/// <summary>
	/// The match key of a DECLARED addend, from a talent's (Type, P0, P1, P2). "" = this declaration is
	/// not matchable (unmodelled type, or an unmodelled reference code) and the census will count the
	/// runtime side as `undeclared`.
	///
	/// P1 IS ONLY the reference param when the entry actually references something. Measured counter-
	/// example: ネフェスティス 被动 id20076 declares type=6 p=[30,150,0] and the battle prints
	/// `Rate+30` with NO ref at all -- P2 == 0 means "no reference", so P1 (150) is something else and
	/// must not become the ref param, or the declaration could never match its own runtime item.
	/// </summary>
	public static string DeclKey(int talentType, int p0, int p1, int p2)
	{
		string tn = TalentTypeName(talentType);
		if (tn.Length == 0) return "";
		string rn = RefTypeName(p2);
		if (p2 != 0 && rn.Length == 0) return "";
		int rp = (p2 == 0) ? 0 : p1;
		return tn + "|" + I(p0) + "|" + I(rp) + "|" + rn;
	}

	/// <summary>
	/// The match key of a RUNTIME addend, from the fields <c>CompositionProbe.ReadAtkItem</c> read off
	/// <c>ParamData</c>. Same shape as <see cref="DeclKey"/>, so the two are comparable by string equality
	/// alone. "" = not an addition this census models.
	/// </summary>
	public static string RuntimeKey(string type, int value, int refParam, string refType)
	{
		string tn = type ?? "";
		if (tn != "Rate" && tn != "Actual") return "";
		return tn + "|" + I(value) + "|" + I(refParam) + "|" + (refType ?? "");
	}

	/// <summary>The label the export prints for one runtime addend, in the same shape the battle log and
	/// the 增益 text use ("Actual+4(/refCurrentLife0)"), so a census row can be found in the log.</summary>
	public static string ItemLabel(string type, int value, string refType, int refParam)
	{
		string s = (type ?? "") + "+" + I(value);
		if (!string.IsNullOrEmpty(refType)) s += "(/ref" + refType + I(refParam) + ")";
		return s;
	}

	private static string I(int v)
	{
		return v.ToString(CultureInfo.InvariantCulture);
	}
}
