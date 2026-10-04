using System;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;

namespace DpsMeter;

/// <summary>
/// PROBE: which clock is the GAME's clock?
///
/// The question came from the overlay battle time not matching the game's own pacing. Reading the game
/// assembly answers the mechanism part:
///
///   * a character skill counts its cooldown in UNITS, not seconds:
///       Skill.m_coolTimeFrame / m_waitCountFrame / m_durationCountFrame / m_overChargeWaitCountFrame
///       (all ObscuredInt "…CountFrame"), decremented by Skill.Update / ActUpdate, i.e. once per game
///       update -- exactly like BattleObject.LifeTimePerFrame and GameSystem.GameTime;
///       the master value is Skill.CoolTime / FirstCoolTime (int), and talents shorten it by *count*
///       (TalentActiveSkillWaitTimeShortening, Skill.ShortenWaitCount / ShorteningWaitCountByPercent);
///   * the on-screen battle timer is GameTimeLimitCounter.NowTime (int) with m_time / isEndless.
///
/// What is still unknown is the CONVERSION: how many of those units make one game second. This probe
/// prints every candidate raw value plus its measured rate, so ONE battle answers it:
///
///   [CLOCKP] wall=4.0s active=4.0s steps=180 stepRate=45.0/s
///            | limitNow=.. raw=.. endless=0 doing=1 dNowPerSec=..
///            | standby=3 | sk0:名前 CT=30 CTF=1800 wait=900 dur=0 stock=1 units/s=60.0 | …
///
///   stepRate        = game update steps per REAL second (45.0 measured at timeScale 1.5)
///   dNowPerSec      = the game's own displayed timer per real second  <- the target to match
///   units/s (CTF/CT)= game units per game second, straight from the loaded skill data
///
/// The last number is what the "game" clock source uses (see Aggregator.Tick + Plugin.CfgClockSource).
/// </summary>
internal static class TimeProbe
{
	/// <summary>Game units per game second, derived from the loaded skill data
	/// (Skill.CoolTimeFrame / Skill.CoolTime). 0 = not known yet, outside [5,300] = rejected.</summary>
	internal static double UnitsPerGameSecond;

	private static double _lastWall = -1.0;
	private static int _lastSteps;
	private static int _lastNow;
	private static bool _hasLast;

	/// <summary>One diagnostic line. Cheap enough to call on the [TIME] cadence (every 2 s).</summary>
	internal static string Line(GameSystem val, double wall, BattleSession s)
	{
		StringBuilder sb = new StringBuilder(240);
		try
		{
			int steps = 0;
			try { steps = val.GameTime; } catch { }
			sb.Append("[CLOCKP] wall=").Append(BattleTime.Log(wall))
				.Append(" active=").Append(BattleTime.Log(s.ActiveSeconds))
				.Append(" steps=").Append(steps);
			bool rateOk = _hasLast && wall - _lastWall > 0.2;
			if (rateOk)
				sb.Append(" stepRate=").Append(((steps - _lastSteps) / (wall - _lastWall)).ToString("F1")).Append("/s");

			// ---- the game's own on-screen battle timer ----
			try
			{
				GameTimeLimitCounter limit = val.m_gameTimeLimitCounter;
				if (!GameRef.IsNull(limit))
				{
					int now = limit.NowTime;
					sb.Append(" | limit now=").Append(now)
						.Append(" raw=").Append(limit.m_time)
						.Append(" doing=").Append(limit.IsDoing ? 1 : 0);
					if (rateOk)
						sb.Append(" dNow/s=").Append(((now - _lastNow) / (wall - _lastWall)).ToString("F2"));
					_lastNow = now;
				}
			}
			catch { }

			// ---- the cooldown counters the question is about ----
			try
			{
				StandbyManager mgr = val.StandbyManager;
				StandbyController ctl = (mgr != null) ? mgr.m_standbyController : null;
				Il2CppSystem.Collections.Generic.List<StandbyDataBase> list = (ctl != null) ? ctl.m_standbyDataList : null;
				if (list != null)
				{
					sb.Append(" | standby=").Append(list.Count);
					int shown = 0;
					for (int i = 0; i < list.Count && shown < 3; i++)
					{
						PlayerSkillStandbyData ps = null;
						try { ps = list[i].TryCast<PlayerSkillStandbyData>(); } catch { }
						if (ps == null) continue;
						Skill sk = null;
						try { sk = ps.m_skill; } catch { }
						if (sk == null) continue;
						int ct = 0, ctf = 0, wait = 0, dur = 0, stock = 0;
						string nm = "";
						try { nm = sk.Name; } catch { }
						try { ct = sk.CoolTime; } catch { }
						try { ctf = sk.CoolTimeFrame; } catch { }
						try { wait = sk.WaitCountFrame; } catch { }
						try { dur = sk.DurationCountFrame; } catch { }
						try { stock = sk.Stock; } catch { }
						sb.Append(" | sk").Append(shown).Append(':').Append(nm)
							.Append(" CT=").Append(ct).Append(" CTF=").Append(ctf)
							.Append(" wait=").Append(wait).Append(" dur=").Append(dur)
							.Append(" stock=").Append(stock);
						if (ct > 0 && ctf > 0)
						{
							double ups = (double)ctf / ct;
							sb.Append(" units/s=").Append(ups.ToString("F1"));
							if (ups >= 5.0 && ups <= 300.0) UnitsPerGameSecond = ups;
						}
						shown++;
					}
				}
			}
			catch { }

			_lastWall = wall;
			_lastSteps = steps;
			_hasLast = true;
		}
		catch { }
		return sb.ToString();
	}
}
