using System;
using System.Collections.Generic;

namespace BehaviorTests;

/// <summary>
/// Named-case runner. A case is one falsifiable statement about PRODUCTION behaviour, printed as
/// PASS/FAIL with a group-qualified name, and the process exits non-zero if any case failed.
///
/// Deliberately not xUnit/NUnit: there is no local NuGet cache and the refactor plan forbids changing
/// the plugin's dependencies to build tests (REFACTOR-PLAN-POST-1.7.11.md section 6). The plan's stated
/// fallback is exactly this: "output case names and a non-zero failure code".
///
/// The case COUNT is pinned in Program.ExpectedCases so a group that silently stops being called, or a
/// case someone deletes to make a build pass, fails the run. A count is not coverage -- it only stops
/// silent loss, which is a different failure mode.
/// </summary>
internal sealed class Runner
{
	public bool Quiet;

	private int _cases;
	private int _fail;
	private string _group = "";

	public int Cases { get { return _cases; } }
	public int Failed { get { return _fail; } }

	public void Group(string name)
	{
		_group = name;
		Console.WriteLine("== " + name);
	}

	private void Ok(string label)
	{
		_cases++;
		if (!Quiet) Console.WriteLine("PASS " + _group + "/" + label);
	}

	private void Bad(string label, string detail)
	{
		_cases++;
		_fail++;
		Console.WriteLine("FAIL " + _group + "/" + label + ": " + detail);
	}

	public void Eq(string label, long got, long want)
	{
		if (got == want) Ok(label); else Bad(label, "got=" + got + " want=" + want);
	}

	public void EqD(string label, double got, double want)
	{
		if (Math.Abs(got - want) < 1e-9) Ok(label);
		else Bad(label, "got=" + got.ToString("R") + " want=" + want.ToString("R"));
	}

	public void Str(string label, string got, string want)
	{
		if (got == want) Ok(label); else Bad(label, "got=[" + got + "] want=[" + want + "]");
	}

	public void True(string label, bool cond)
	{
		if (cond) Ok(label); else Bad(label, "condition is false");
	}

	public void Same(string label, object a, object b)
	{
		if (ReferenceEquals(a, b)) Ok(label); else Bad(label, "expected the SAME instance");
	}

	public void Diff(string label, object a, object b)
	{
		if (!ReferenceEquals(a, b)) Ok(label); else Bad(label, "expected a DIFFERENT instance (recompute)");
	}
}
