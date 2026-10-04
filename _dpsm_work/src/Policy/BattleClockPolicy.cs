namespace DpsMeter;

/// <summary>
/// RF3: the battle clock's DECISIONS, as pure functions.
///
/// Why this file exists. The clock rule was spread over Aggregator.FrameDelta / ClockSourceName /
/// GameUnitsPerSecond and duplicated in two of them (the "which source" rule existed twice, so the
/// reported source and the applied source could in principle disagree). Every threshold is now a named
/// constant here, every input is a primitive, and nothing reads Unity, the stopwatch, the config or the
/// disk -- so BehaviorTests executes this file and drives its boundaries without a game.
///
/// The facade keeps what a policy must not do: reading GameSystem.GameTime / Time.deltaTime /
/// Clock.Elapsed, holding the last-seen state, and writing the [CLOCK]/[TIME] log lines.
///</summary>
internal static class BattleClockPolicy
{
	/// <summary>How many game units make one game second, when neither the config nor the loaded skill
	/// data says. Measured 2026-09-27 from Skill.CoolTimeFrame / Skill.CoolTime: 750/25, 1500/50, 1050/35.
	/// </summary>
	public const double DefaultUnitsPerGameSecond = 30.0;

	/// <summary>A stalled frame (blocked main thread, OS suspend, blocked scene load) hands the whole
	/// stall to us as ONE delta. Measured 2.9 s in one frame, which jumped the battle clock (9.3 s ->
	/// 12.2 s with no event in between) and was by itself enough to trip the idle timeout in the same
	/// step. A stall must never look like progress.</summary>
	public const double MaxFrameDelta = 0.25;

	/// <summary>
	/// Effective clock source: "real" | "engine" | "game".
	///
	/// An unset or misspelled value falls back to the legacy bool and then to "game" -- it is never an
	/// error, because BepInEx keeps whatever the installed config already contains, so a typo has to
	/// degrade to the documented default and be VISIBLE in the [CLOCK] line rather than silent.
	/// </summary>
	public static string ResolveSource(string configured, bool legacyUsesGameTime)
	{
		string source = (configured != null) ? configured.Trim().ToLowerInvariant() : "";
		if (source == "real" || source == "engine" || source == "game") return source;
		return legacyUsesGameTime ? "engine" : "game";
	}

	/// <summary>Units per game second: config -> the value measured from the loaded skill data -> fallback.
	/// A non-positive config or probe is "not set", never a divisor.</summary>
	public static double ResolveUnitsPerGameSecond(double configured, double probed, double fallback)
	{
		double units = configured;
		if (units <= 0.0) units = (probed > 0.0) ? probed : fallback;
		return units;
	}

	/// <summary>Clamp ONE frame's raw delta: negative never rewinds the clock, a stall is capped at
	/// <paramref name="maxDelta"/>. Both comparisons are strict, so a delta exactly at the bound and a
	/// delta exactly 0 pass through unchanged.</summary>
	public static double ClampFrameDelta(double dt, double maxDelta)
	{
		if (dt < 0.0) dt = 0.0;
		if (dt > maxDelta) dt = maxDelta;
		return dt;
	}

	/// <summary>Real-clock delta from a monotonic second counter. <paramref name="lastTick"/> &lt; 0 is
	/// "no previous tick", which yields 0.0 and starts the clock at <paramref name="now"/> -- the first
	/// frame of a battle must not be charged the whole process uptime.</summary>
	public static double RealDelta(double now, ref double lastTick)
	{
		double delta = (lastTick < 0.0) ? 0.0 : now - lastTick;
		lastTick = now;
		return delta;
	}

	/// <summary>Game-clock delta: update steps divided by units-per-second. A first observation yields 0
	/// (there is no previous value to subtract), a non-advancing counter yields 0, and a counter that went
	/// BACKWARDS (the game restarted its own counter) also yields 0 instead of a negative delta.</summary>
	public static double GameDelta(int steps, ref int lastSteps, ref bool hasLast, double unitsPerSecond)
	{
		int dSteps = hasLast ? (steps - lastSteps) : 0;
		lastSteps = steps;
		hasLast = true;
		return (dSteps > 0) ? dSteps / unitsPerSecond : 0.0;
	}
}
