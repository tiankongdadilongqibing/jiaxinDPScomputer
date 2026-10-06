using DpsMeter;

namespace BehaviorTests;

internal static partial class Cases
{
	/// <summary>
	/// R74: the calibration sampler's own evidence (`Policy/ClockLagDiagnostics.cs`).
	///
	/// Why this group exists. R73 proved that `[CLOCK] samples=0` cannot tell "no slot answered" from "the
	/// window closed first", because the deciding frame always fails the window clause -- so "why was the
	/// shift never applied" stayed open for two rounds while the only signal was a boolean. The replacement
	/// is one line with one bucket per value, and a line whose whole job is to be read must not be able to
	/// lose a bucket or file a reason under the wrong name: a mutation that does either has to go red HERE,
	/// not in a battle.
	///
	/// The exact text is PINNED, not merely "contains": the point of the line is that two battles can be
	/// compared by eye, which only works if the shape is fixed.
	/// </summary>
	internal static void ClockLagDiagnosticsCases(Runner r)
	{
		r.Group("policy/clock-lag-diagnostics");

		var empty = new ClockLagDiagnostics();
		r.Str("an-untouched-report-pins-every-bucket", empty.Describe(),
			"attempts=0 party=0 slots=0 usable=0 via(field/fallback)=0/0 first(sec/frame/wait)=0/0/0"
			+ " rejected(noFirst/wait/units/window/range)=0/0/0/0/0");

		var d = new ClockLagDiagnostics();
		d.Attempts = 7;
		d.Party = 7;
		d.SlotsRead = 14;
		d.NoteReading(6, 180, 150, false);   // the frame-denominated field answered
		d.NoteReading(6, 180, 120, true);    // ... and the fallback answered for another slot
		d.Note(BattleClockCalibrationPolicy.LagReason.Usable);
		d.Note(BattleClockCalibrationPolicy.LagReason.Usable);
		d.Note(BattleClockCalibrationPolicy.LagReason.NoFirstCool);
		d.Note(BattleClockCalibrationPolicy.LagReason.WaitOutOfRange);
		d.Note(BattleClockCalibrationPolicy.LagReason.WaitOutOfRange);
		d.Note(BattleClockCalibrationPolicy.LagReason.WaitOutOfRange);
		d.Note(BattleClockCalibrationPolicy.LagReason.BadUnits);
		d.Note(BattleClockCalibrationPolicy.LagReason.OutOfWindow);
		d.Note(BattleClockCalibrationPolicy.LagReason.LagOutOfRange);
		r.Str("a-filled-report-pins-every-bucket", d.Describe(),
			"attempts=7 party=7 slots=14 usable=2 via(field/fallback)=1/1 first(sec/frame/wait)=6/180/150"
			+ " rejected(noFirst/wait/units/window/range)=1/3/1/1/1");

		// The triple kept is the FIRST reading (the only one certainly taken while the slot is still on its
		// first charge), and it is what settles "seconds or units" from a single battle's log.
		r.True("the-first-triple-is-the-one-kept",
			d.FirstSeconds == 6 && d.FirstFrame == 180 && d.FirstWait == 150 && d.HasFirstTriple);

		// An unreadable field prints as `?`, never as 0: "could not read it" and "it read zero" are different
		// facts, and the whole R71/R72 defect was a unit mix-up that a printed 0 would have hidden.
		var q = new ClockLagDiagnostics();
		q.NoteReading(int.MinValue, int.MinValue, int.MinValue, false);
		r.True("an-unreadable-reading-prints-as-a-question-mark",
			q.Describe().Contains("first(sec/frame/wait)=?/?/?"));

		// One battle, one set of counters: Clear is what the per-battle reset relies on.
		d.Clear();
		r.Str("clear-empties-every-bucket", d.Describe(),
			"attempts=0 party=0 slots=0 usable=0 via(field/fallback)=0/0 first(sec/frame/wait)=0/0/0"
			+ " rejected(noFirst/wait/units/window/range)=0/0/0/0/0");
		r.True("clear-drops-the-stored-triple", !d.HasFirstTriple);
	}
}
