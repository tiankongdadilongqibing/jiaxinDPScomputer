using HarmonyLib;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// InputManager.Update: the second per-frame driver (see GameSystemTickHook). Aggregator.Tick is
/// idempotent within one frame, so having both drivers costs nothing and covers scenes where only one
/// of the two hooks fires.
/// </summary>
[HarmonyPatch(typeof(InputManager), "Update")]
public static class InputManagerTickHook
{
	public static void Postfix()
	{
		try
		{
			Aggregator.Tick();
			OverlayUGUI.Tick();
		}
		catch { }
	}
}
