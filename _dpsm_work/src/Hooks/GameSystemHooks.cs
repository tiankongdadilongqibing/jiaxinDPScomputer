using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DpsMeter;

// Patches on GameSystem: session lifecycle plus the per-frame driver.
//
// The battle clock is NOT GameSystem.GameTime. That field is an update/frame counter that keeps
// counting across battles and whose rate follows the frame rate (measured 45.0 units per wall second
// in combat, 27.5 then ~17 in the post-battle sequence), so it cannot be turned into seconds by a
// constant divisor. The clock is accumulated per frame in Aggregator.Tick instead.

/// <summary>GameSystem.Init: a new battle is being set up.</summary>
[HarmonyPatch(typeof(GameSystem), "Init")]
public static class GameSystemInitHook
{
	public static void Prefix()
	{
		try
		{
			Aggregator.StartSession();
		}
		catch { }
	}
}

/// <summary>
/// GameSystem.EarlyUpdateMain: the per-frame driver (Aggregator.Tick + overlay tick).
///
/// Tick is idempotent within a frame, so more than one driver is harmless; InputManager.Update below
/// is kept as a second driver because either hook can be absent depending on the scene.
/// </summary>
[HarmonyPatch(typeof(GameSystem), "EarlyUpdateMain")]
public static class GameSystemTickHook
{
	public static void Postfix()
	{
		try
		{
			// No engine delta: the battle clock is measured inside Aggregator (Time.unscaledDeltaTime
			// turned out to be the game's fixed logic step, 1/45 s, not the 1/30 s frame time).
			Aggregator.Tick();
			OverlayUGUI.Tick();
		}
		catch { }
	}
}

/// <summary>GameSystem.SetGameResult: the battle outcome is known.</summary>
[HarmonyPatch(typeof(GameSystem), "SetGameResult")]
public static class GameSystemResultHook
{
	public static void Prefix(GameResult __0)
	{
		try
		{
			if ((int)__0 != 0)
			{
				Aggregator.EndSession(__0);
			}
		}
		catch { }
	}
}

/// <summary>GameSystem.SetBattleEndFlag: the battle-end flag is raised (before SetGameResult).</summary>
[HarmonyPatch(typeof(GameSystem), "SetBattleEndFlag")]
public static class GameSystemBattleEndFlagHook
{
	public static void Prefix()
	{
		try
		{
			GameSystem val = GameSystemAccess.TryGet();
			Aggregator.EndSession((GameResult)((val != null) ? ((int)val.GameResult) : 0));
		}
		catch { }
	}
}

/// <summary>GameSystem.ActDestroy: the battle scene is being torn down.</summary>
[HarmonyPatch(typeof(GameSystem), "ActDestroy")]
public static class GameSystemDestroyHook
{
	public static void Prefix()
	{
		try
		{
			Aggregator.EndSession((GameResult)0);
		}
		catch { }
	}
}
