using System.Collections.Generic;
using System.Text;

namespace DpsMeter;

/// <summary>
/// R87 (方案A): the IMPURE half of the self-addend census -- it gathers the roster and its declared addends,
/// then hands them to the pure policy, which does the matching and writes the JSON.
///
/// The split is the same one every other section in this repo uses: the part that touches game objects
/// lives here (and can therefore not be compiled into the behaviour suite), the part that decides lives in
/// Policy/ and is executed by it. This file is deliberately three loops and a call.
///
/// The declarations come from <c>ActorStats.Roster[].Talents[]</c> -- the very list ExportService already
/// writes as <c>actors[].abilities[].talents[]</c>, cached on ActorStats during the battle for exactly this
/// kind of after-the-fact question. No new game read, no new hook, no new config key.
///
/// EVERY actor is emitted, not only the ones that declare something. The census needs the HOLDER's team to
/// scope its match, and a holder usually declares no attack addend of its own -- so an index built only
/// from declarers would report every such holder as `noHolder` and the section would look empty for the
/// wrong reason.
/// </summary>
internal static class AtkAddCensusWriter
{
	public static void AppendJson(StringBuilder sb, BattleSession s)
	{
		var roster = new List<AtkAddRosterUnit>(32);
		if (s != null && s.OrderedActors != null)
		{
			for (int i = 0; i < s.OrderedActors.Count; i++)
			{
				ActorStats a = s.OrderedActors[i];
				if (a == null) continue;
				var u = new AtkAddRosterUnit { Key = a.Key, Name = a.Name ?? "", Team = (int)a.Team };
				if (a.Roster != null)
				{
					for (int j = 0; j < a.Roster.Count; j++)
					{
						RosterAbility r = a.Roster[j];
						if (r == null) continue;
						for (int k = 0; k < r.Talents.Count; k++)
						{
							TalentRef t = r.Talents[k];
							if (t == null) continue;
							string key = AtkAddDeclarePolicy.DeclKey(t.Type, t.P0, t.P1, t.P2);
							if (key.Length == 0) continue;
							if (!u.DeclKeys.Contains(key)) u.DeclKeys.Add(key);
						}
					}
				}
				roster.Add(u);
			}
		}
		AtkAddCensusPolicy.AppendJson(sb,
			AtkAddCensusPolicy.Build(AtkAddFold.SelfEntries, AtkAddFold.SelfValues, roster));
	}
}
