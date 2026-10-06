using System.Globalization;
using System.Text;

namespace DpsMeter;

/// <summary>
/// R74: what the clock-origin CALIBRATION SAMPLER actually saw, so "no samples" can never again be a
/// silent outcome.
///
/// WHY IT EXISTS. R71 and R72 both shipped a shift that never happened once. The only clue either battle
/// left was `[CLOCK] origin=none ... samples=0`, and that number is structurally useless on its own: the
/// decision is only reached once `ActiveSeconds > WindowSeconds`, and `TryLag` refuses every sample at that
/// very moment because of the window, so `samples=0` looks identical whether nothing answered, everything
/// was refused by a unit mix-up, or the sampler never ran at all.
///
/// This container is filled by `Diagnostics/AutoSkillProbe.TryMeasureClockLag` on EVERY attempt inside the
/// window (not only the last one) and printed as one `[CLOCK] calib` line when the origin is decided. It is
/// pure -- no Unity, no IL2CPP, no Plugin -- so the counting and the text are executed by the behaviour
/// suite, and a mutation that hides a bucket or misfiles a reason has to go red there.
/// </summary>
internal sealed class ClockLagDiagnostics
{
	/// <summary>Sampler invocations. A per-frame caller makes this tens; a caller that only ran at session
	/// creation makes it 1, which is itself the answer to "was it even tried?".</summary>
	internal int Attempts;

	/// <summary>Units walked by the widest party seen (0 = the standby list never resolved).</summary>
	internal int Party;

	/// <summary>Slots whose two readings were both obtained, summed over every attempt.</summary>
	internal int SlotsRead;

	/// <summary>Slots that produced a usable lag, summed over every attempt. This is the number that
	/// distinguishes "no slot could answer" from "the window closed first"; `[CLOCK] samples=` can only ever
	/// be a MAX over attempts, so its 0 cannot.</summary>
	internal int Usable;

	/// <summary>Readings whose unit value came from the frame-denominated field.</summary>
	internal int ViaField;

	/// <summary>Readings whose unit value came from `FirstCoolTime x unitsPerSecond` (the field did not
	/// answer). Kept apart from <see cref="ViaField"/> so a fallback value can never be read as a measured
	/// one.</summary>
	internal int ViaFallback;

	internal int RejNoFirstCool;
	internal int RejWaitOutOfRange;
	internal int RejBadUnits;
	internal int RejOutOfWindow;
	internal int RejLagOutOfRange;

	/// <summary>The FIRST slot read of the FIRST attempt: the single triple that settles, from the log
	/// alone, whether the frame-denominated field carries a unit count and whether the seconds property
	/// carries seconds.</summary>
	internal bool HasFirstTriple;
	internal int FirstSeconds;
	internal int FirstFrame;
	internal int FirstWait;

	internal void Clear()
	{
		Attempts = 0;
		Party = 0;
		SlotsRead = 0;
		Usable = 0;
		ViaField = 0;
		ViaFallback = 0;
		RejNoFirstCool = 0;
		RejWaitOutOfRange = 0;
		RejBadUnits = 0;
		RejOutOfWindow = 0;
		RejLagOutOfRange = 0;
		HasFirstTriple = false;
		FirstSeconds = 0;
		FirstFrame = 0;
		FirstWait = 0;
	}

	/// <summary>One slot's first-charge reading as it arrived: which route produced the unit value, and both
	/// raw readings next to the counter they are compared against (only the first one is kept, because that
	/// is the one taken while the slot is certainly still on its first charge).</summary>
	internal void NoteReading(int firstSeconds, int firstFrame, int wait, bool usedFallback)
	{
		if (usedFallback) ViaFallback++;
		else ViaField++;
		if (HasFirstTriple) return;
		HasFirstTriple = true;
		FirstSeconds = firstSeconds;
		FirstFrame = firstFrame;
		FirstWait = wait;
	}

	internal void Note(BattleClockCalibrationPolicy.LagReason why)
	{
		switch (why)
		{
			case BattleClockCalibrationPolicy.LagReason.Usable: Usable++; break;
			case BattleClockCalibrationPolicy.LagReason.NoFirstCool: RejNoFirstCool++; break;
			case BattleClockCalibrationPolicy.LagReason.WaitOutOfRange: RejWaitOutOfRange++; break;
			case BattleClockCalibrationPolicy.LagReason.BadUnits: RejBadUnits++; break;
			case BattleClockCalibrationPolicy.LagReason.OutOfWindow: RejOutOfWindow++; break;
			case BattleClockCalibrationPolicy.LagReason.LagOutOfRange: RejLagOutOfRange++; break;
		}
	}

	/// <summary>One ASCII line, one bucket per value, no label omitted: an unreadable field prints `?`, never
	/// a 0, because "could not read it" and "it read zero" have to stay different answers.</summary>
	internal string Describe()
	{
		var sb = new StringBuilder(240);
		sb.Append("attempts=").Append(Attempts.ToString(CultureInfo.InvariantCulture));
		sb.Append(" party=").Append(Party.ToString(CultureInfo.InvariantCulture));
		sb.Append(" slots=").Append(SlotsRead.ToString(CultureInfo.InvariantCulture));
		sb.Append(" usable=").Append(Usable.ToString(CultureInfo.InvariantCulture));
		sb.Append(" via(field/fallback)=").Append(ViaField.ToString(CultureInfo.InvariantCulture))
		  .Append('/').Append(ViaFallback.ToString(CultureInfo.InvariantCulture));
		sb.Append(" first(sec/frame/wait)=").Append(Num(FirstSeconds)).Append('/').Append(Num(FirstFrame))
		  .Append('/').Append(Num(FirstWait));
		sb.Append(" rejected(noFirst/wait/units/window/range)=")
		  .Append(RejNoFirstCool.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(RejWaitOutOfRange.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(RejBadUnits.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(RejOutOfWindow.ToString(CultureInfo.InvariantCulture)).Append('/')
		  .Append(RejLagOutOfRange.ToString(CultureInfo.InvariantCulture));
		return sb.ToString();
	}

	private static string Num(int v)
	{
		return (v == int.MinValue) ? "?" : v.ToString(CultureInfo.InvariantCulture);
	}
}
