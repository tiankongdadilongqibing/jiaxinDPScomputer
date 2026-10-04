using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DpsMeter
{
	/// <summary>
	/// Recognises "tiered" damage-modifier descriptions whose NUMBERS do not live in the same clause as
	/// the damage keyword. This is how every 耐久が減少するほど ability is written in game data:
	///
	///   [痺夏]シゼル＝メ 素質
	///     耐久が減少するほど物理/魔法被ダメージが減少  (現在耐久が9/6/3割以下の場合、それぞれ0.1/0.3/0.5倍)
	///   レヴナント 素質
	///     耐久が減少するほど魔法被ダメージが軽減      （現在耐久が9/6/3割以下の場合、それぞれ-10/-25/-40%）
	///   シゼル＝メ 素質
	///     耐久が減少するほど与ダメージが上昇          （耐久が9/6/3割以下の場合、それぞれ1.1/1.3/1.5倍上昇）
	///
	/// The first clause carries the keyword but no number, the second carries the numbers but no keyword,
	/// so a clause-by-clause scan can only say 条件性,未计入. This class pairs the two: it parses the
	/// threshold list + value list from the whole ability text and returns the value of the tier that is
	/// active for the unit's CURRENT 耐久%.
	///
	/// Reading rules taken from the wiki (［痺夏］シゼル＝メ page, 素質 section):
	///   "90%～：被ダメ1割減少　60%～：被ダメ3割減少　30%～：被ダメ5割減少"
	/// i.e. the parenthetical "0.1/0.3/0.5倍" is NOT a damage multiplier but "10/30/50% 減少", and the
	/// tier that applies is the one with the SMALLEST threshold that is still satisfied (the deepest
	/// bracket the unit has fallen into). For 以上 lists the largest satisfied threshold wins.
	/// </summary>
	internal static class TieredModifier
	{
		internal struct Tier
		{
			public int Threshold;   // percent of max HP ("9割" -> 90)
			public double Value;    // exactly as written in the text
			public bool Percent;    // written with % / ％
			public bool Times;      // written with 倍
			public bool Negative;   // explicit minus sign in front of the value
		}

		internal sealed class Spec
		{
			public List<Tier> Tiers = new List<Tier>();
			public bool Below;      // 以下/未満 (vs 以上/超)
			public string Source = "";
			public int Start;       // index of the threshold list inside Source
			public int End;         // index just past the value list
		}

		// "9/6/3割" or "20/50/80%" : a run of at least two numbers separated by '/'
		// (no RegexOptions.Compiled: it needs dynamic codegen, which is not worth the risk inside BepInEx)
		private static readonly Regex ThresholdList = new Regex(
			@"(?<nums>[0-9]+(?:\s*/\s*[0-9]+)+)\s*(?<unit>割|%|％)");

		// "0.1/0.3/0.5倍" or "-10/-25/-40%" or "1.1/1.3/1.5"
		private static readonly Regex ValueList = new Regex(
			@"(?<nums>-?[0-9]+(?:\.[0-9]+)?(?:\s*/\s*-?[0-9]+(?:\.[0-9]+)?)+)\s*(?<unit>%|％|倍)?");

		private static readonly Dictionary<string, Spec> _cache = new Dictionary<string, Spec>();

		/// <summary>Cached parse. Returns null when the text carries no tiered list.</summary>
		internal static Spec Get(string text)
		{
			if (string.IsNullOrEmpty(text)) return null;
			Spec s;
			if (_cache.TryGetValue(text, out s)) return s;
			TryParse(text, out s);
			if (_cache.Count < 4000) _cache[text] = s;
			return s;
		}

		internal static void ClearCache()
		{
			try { _cache.Clear(); } catch { }
		}

		/// <summary>
		/// Parse "&lt;thresholds&gt;(割|%) (以下|未満|以上|超) ... それぞれ &lt;values&gt;(%|倍)".
		/// Requires the それぞれ separator, so ordinary single-value conditions are left alone.
		/// </summary>
		internal static bool TryParse(string text, out Spec spec)
		{
			spec = null;
			try
			{
				if (string.IsNullOrEmpty(text)) return false;
				Match tm = ThresholdList.Match(text);
				while (tm.Success)
				{
					int after = tm.Index + tm.Length;
					string tail = text.Substring(after, System.Math.Min(8, text.Length - after));
					bool below;
					if (tail.StartsWith("以下", StringComparison.Ordinal) || tail.StartsWith("未満", StringComparison.Ordinal)) below = true;
					else if (tail.StartsWith("以上", StringComparison.Ordinal) || tail.StartsWith("超", StringComparison.Ordinal)) below = false;
					else { tm = tm.NextMatch(); continue; }

					// the value list is introduced by それぞれ, close behind the condition
					int sep = text.IndexOf("それぞれ", after, StringComparison.Ordinal);
					if (sep < 0 || sep - after > 48) { tm = tm.NextMatch(); continue; }

					Match vm = ValueList.Match(text, sep);
					if (!vm.Success || vm.Index - sep > 10) { tm = tm.NextMatch(); continue; }

					string[] th = tm.Groups["nums"].Value.Split('/');
					string[] vals = vm.Groups["nums"].Value.Split('/');
					if (th.Length < 2 || th.Length != vals.Length) { tm = tm.NextMatch(); continue; }

					bool thPercent = tm.Groups["unit"].Value != "割";
					var tiers = new List<Tier>();
					bool ok = true;
					for (int i = 0; i < th.Length; i++)
					{
						int t;
						if (!int.TryParse(th[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out t)) { ok = false; break; }
						string vs = vals[i].Trim();
						bool neg = vs.StartsWith("-", StringComparison.Ordinal) || vs.StartsWith("−", StringComparison.Ordinal);
						if (neg) vs = vs.Substring(1);
						double v;
						if (!double.TryParse(vs, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { ok = false; break; }
						string unit = vm.Groups["unit"].Value;
						var tier = new Tier();
						tier.Threshold = thPercent ? t : t * 10;
						tier.Value = neg ? -v : v;
						tier.Negative = neg;
						tier.Percent = unit == "%" || unit == "％";
						tier.Times = unit == "倍";
						if (unit.Length == 0) tier.Percent = true;   // bare number: this game writes percents bare
						tiers.Add(tier);
					}
					if (!ok || tiers.Count < 2) { tm = tm.NextMatch(); continue; }

					var s = new Spec();
					s.Tiers = tiers;
					s.Below = below;
					s.Source = text;
					s.Start = tm.Index;
					s.End = vm.Index + vm.Length;
					spec = s;
					return true;
				}
				return false;
			}
			catch { spec = null; return false; }
		}

		/// <summary>
		/// Value of the tier that is active at <paramref name="hpPct"/>. Returns false when the unit is
		/// outside every bracket (the modifier is then simply inactive).
		/// </summary>
		internal static bool TryEvaluate(Spec spec, int hpPct, string keywordClause, out double factor, out string note)
		{
			factor = 1.0;
			note = null;
			try
			{
				if (spec == null || spec.Tiers.Count == 0) return false;

				int chosen = -1;
				int best = spec.Below ? int.MaxValue : int.MinValue;
				for (int i = 0; i < spec.Tiers.Count; i++)
				{
					int t = spec.Tiers[i].Threshold;
					if (spec.Below)
					{
						// deepest bracket the unit has fallen into = smallest satisfied threshold
						if (t >= hpPct && t < best) { best = t; chosen = i; }
					}
					else
					{
						if (t <= hpPct && t > best) { best = t; chosen = i; }
					}
				}
				if (chosen < 0) return false;

				Tier tier = spec.Tiers[chosen];
				// direction words may sit in the keyword clause ("…被ダメージが減少") or in the tier
				// segment ("…0.1/0.3/0.5倍"); an explicit minus sign always means a reduction.
				string seg = spec.Source.Substring(spec.Start, spec.End - spec.Start);
				string all = (keywordClause ?? "") + seg;
				bool minus = all.IndexOf("軽減", StringComparison.Ordinal) >= 0
					|| all.IndexOf("減少", StringComparison.Ordinal) >= 0
					|| all.IndexOf("カット", StringComparison.Ordinal) >= 0
					|| all.IndexOf("ダウン", StringComparison.Ordinal) >= 0
					|| all.IndexOf("低下", StringComparison.Ordinal) >= 0
					|| all.IndexOf("抑制", StringComparison.Ordinal) >= 0;
				bool plus = all.IndexOf("上昇", StringComparison.Ordinal) >= 0
					|| all.IndexOf("増加", StringComparison.Ordinal) >= 0
					|| all.IndexOf("アップ", StringComparison.Ordinal) >= 0;
				bool reduce = tier.Negative || (minus && !plus);

				double f;
				double v = System.Math.Abs(tier.Value);
				if (tier.Percent) f = reduce ? (1.0 - v / 100.0) : (1.0 + v / 100.0);
				else if (tier.Times) f = reduce ? (1.0 - v) : v;
				else f = reduce ? (1.0 - v / 100.0) : (1.0 + v / 100.0);
				if (f < 0.0) f = 0.0;
				if (f > 5.0) f = 5.0;
				if (f == 1.0) return false;

				factor = f;
				note = "当前耐久" + hpPct + "%" + (spec.Below ? "≤" : "≥") + tier.Threshold + "%档";
				return true;
			}
			catch { factor = 1.0; note = null; return false; }
		}

		/// <summary>Number of non-overlapping occurrences of <paramref name="needle"/>.</summary>
		internal static int CountOccurrences(string text, string needle)
		{
			try
			{
				if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(needle)) return 0;
				int n = 0, i = 0;
				while (true)
				{
					int k = text.IndexOf(needle, i, StringComparison.Ordinal);
					if (k < 0) break;
					n++;
					i = k + needle.Length;
				}
				return n;
			}
			catch { return 0; }
		}
	}
}
