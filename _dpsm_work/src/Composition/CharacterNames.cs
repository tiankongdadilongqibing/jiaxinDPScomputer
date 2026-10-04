using System;
using System.Collections.Generic;

namespace DpsMeter
{
	/// <summary>
	/// Character / token names from the wiki roster (names, plus the same names with a
	/// [prefix] removed).
	///
	/// Why this exists: a 素質 (talent) ability row is named after its OWNER character, so the
	/// row that レヴナント carries is labelled "シゼル＝メ" in the game master data (the row was
	/// copied/reused and the old name was left in place). Printing that name as the "source" of
	/// a damage modifier is misleading, so a name that is a known unit name is never used as a
	/// provenance label. Artifact / skill names (夢のクリスタライザー, 究極の選択, ...) are not
	/// unit names and are still shown.
	/// </summary>
	internal static class CharacterNames
	{
		private static readonly HashSet<string> _names = new HashSet<string>(StringComparer.Ordinal)
		{
			"T.O.W.E.R.typeR",
			"【バイコーン娘】ヴァリナ",
			"アスタリーゼ",
			"アリスノリス",
			"アーフナ",
			"イグナ",
			"イリム",
			"イヴ・メサイア",
			"ウェンディ",
			"ウロロス",
			"エカ・インティ",
			"エルディ",
			"エレノワール",
			"エヴァラス・フラウ",
			"カルヴァレーナ",
			"ガーネシオン",
			"キャピノラ",
			"クアローラ",
			"ク＝ルヴィ",
			"グラン・ヴェルノ",
			"サイオン",
			"シェメラーヌ",
			"シェルン",
			"シゼル＝メ",
			"シャーコット",
			"シャーリー",
			"シュアン",
			"シリウス３",
			"ゼラニュート",
			"ゼルトナ",
			"ソフィー",
			"タトゥメ",
			"チェイシィ",
			"テトラ",
			"ディオネ",
			"ディシア",
			"トレイラ",
			"ネア・ウルム",
			"ネオン",
			"ネフェスティス",
			"ネーフェ＝ジアー",
			"ノイシス",
			"ノーティア・ゾーノ",
			"ビーネリンデ",
			"ピオニー",
			"フィロス",
			"フラーナ＝フリーニ",
			"フーシャ",
			"プリゾナ",
			"ホルテウス",
			"ポポロット",
			"ポーラシェンテ",
			"マグナラ",
			"マッドシーカー",
			"マリア・シュピール",
			"ミャウラ",
			"ミューゼ",
			"ムスクーマ",
			"ムルミィ・ゾォム",
			"メアジェリー",
			"メアリー",
			"メイ＝ユル",
			"メルティエル",
			"メルファ",
			"モネモネ",
			"ユラナ・ゾッタ",
			"ラツィーネ",
			"ララヴァ",
			"リフル",
			"リヴァシー",
			"ルゥ=ルルサ",
			"ルセ=ルルカ",
			"ルナリス",
			"ルーチェルト",
			"レアリティ",
			"レベルキャップ",
			"レヴナント",
			"ロフィ",
			"ローレン",
			"ヴァリナ",
			"ヴィヴィア",
			"亀の目",
			"塵埃の手",
			"大海の妖精",
			"招来",
			"死のカラス",
			"水辺の親玉招来",
			"氷鳥",
			"炎の天馬",
			"石造都市ルルイエ",
			"蛇瓶",
			"霊魂",
			"鬼火、鬼青火",
			"魔の食虫植物",
			"Ｇｅｎ．Ｃ．バーミリオン",
			"［渚風］エレノワール",
			"［炎波］イグナ",
			"［燦閃］ネオン",
			"［痺夏］シゼル＝メ",
			"［眩愛］チェイシィ",
			"［賢導］トレイラ",
		};

		internal static bool IsUnitName(string s)
		{
			try
			{
				if (string.IsNullOrEmpty(s)) return false;
				string t = s.Trim();
				if (_names.Contains(t)) return true;
				t = StripBrackets(t);
				return t.Length > 0 && _names.Contains(t);
			}
			catch { return false; }
		}

		/// <summary>"[痺夏]シゼル＝メ" -> "シゼル＝メ"; decorations are not part of the name.</summary>
		private static string StripBrackets(string s)
		{
			s = s.Trim();
			while (s.Length > 2)
			{
				int e = -1;
				char c = s[0];
				if (c == '[') e = s.IndexOf(']');
				else if (c == '［') e = s.IndexOf('］');
				else if (c == '【') e = s.IndexOf('】');
				if (e <= 0) break;
				s = s.Substring(e + 1).Trim();
			}
			return s;
		}
	}
}
