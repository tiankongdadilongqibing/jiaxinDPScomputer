using CodeStage.AntiCheat.ObscuredTypes;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// Unity / IL2CPP object helpers.
///
/// Interop types are managed wrappers around native pointers, so a destroyed object is a Unity
/// "fake null": the wrapper reference is non-null while the object behind it is gone. Every access
/// therefore has to go through Unity's == operator -- and because the wrappers do not derive from
/// UnityEngine.Object, the comparison needs a cast through <c>object</c>, which is what these helpers
/// encapsulate. Writing that by hand at ~100 call sites was noisy and easy to get subtly wrong.
///
/// The parameters are declared <c>object</c> (not UnityEngine.Object) on purpose: there is no
/// conversion between an Il2CppInterop wrapper (BattleObject, GameSystem, ...) and UnityEngine.Object
/// in the C# type system.
///
/// IMPORTANT -- these helpers are ONLY valid for types that really are UnityEngine.Object
/// subclasses (BattleObject, GameSystem, Font, Texture, ... i.e. the MonoBehaviour-derived side).
/// A plain IL2CPP object such as <c>Rog.Domain.AttributeModel</c> is NOT a UnityEngine.Object and the
/// cast below throws InvalidCastException, which an outer <c>catch { }</c> turns into a silent
/// fallback value. For those use a plain <c>x == null</c>. (That exact mistake silently disabled the
/// whole 属性倍率 for several versions -- see SESSION-STATE.md 7.3.)
/// </summary>
public static class GameRef
{
	/// <summary>True when the interop wrapper is missing or the native object behind it is destroyed.</summary>
	public static bool IsNull(object o)
	{
		return (UnityEngine.Object)o == (UnityEngine.Object)null;
	}

	/// <summary>True when the object exists and its native instance is still alive.</summary>
	public static bool IsAlive(object o)
	{
		return (UnityEngine.Object)o != (UnityEngine.Object)null;
	}

	/// <summary>Unity identity comparison (same native object, not merely the same wrapper).</summary>
	public static bool Same(object a, object b)
	{
		return (UnityEngine.Object)a == (UnityEngine.Object)b;
	}

	/// <summary>Decrypt one of the game's anti-cheat obscured integers (0 when unavailable).</summary>
	public static int Dec(ObscuredInt v)
	{
		try { return v.GetDecrypted(); }
		catch { return 0; }
	}

	/// <summary>
	/// Decrypt one of the game's anti-cheat obscured strings ("" when unavailable).
	///
	/// Same rule as Dec: the raw fields must never be read directly. hiddenValue is the XORed value and
	/// fakeValue is a honeypot that returns forged data once the detector sees memory tampering, so a
	/// "fallback" read of either is not merely imprecise -- it is a wrong value that looks plausible.
	/// Only the game's own accessor decrypts.
	/// </summary>
	public static string DecStr(ObscuredString v)
	{
		try
		{
			if (v == null) return "";
			return v.GetDecrypted();
		}
		catch { return ""; }
	}
}
