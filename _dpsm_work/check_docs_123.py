# -*- coding: utf-8 -*-
"""Encoding guard for the CURRENT doc set (2026-10-03 cleanup; extended 2026-10-04 round 6).

The 1.2.3-era file list died with that cleanup: the two 1.2.x changelists it watched were deleted.
The hazard it guards against did not -- this project has corrupted CJK files through a GBK console
twice (SESSION-STATE line 19). It watches every doc that is supposed to stay readable and exits
non-zero on damage (before the 2026-10-03 fix it only printed and returned 0).

Round 6 changes:
  * FILES now covers the whole live doc set (roadmap / acceptance record / decision report / census /
    N7 record / identity + atkadd studies / the contribution table report / the current rollback).
  * A file that cannot be decoded as UTF-8, or that is MISSING, is reported as damage instead of
    raising a traceback or being skipped.
  * --selftest demonstrates the red path in the same run (a gate nobody has seen go red is not a gate).
"""
import argparse, io, os, re, sys, tempfile

FILES = [
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\SESSION-STATE.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\ARCHITECTURE.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\HANDOFF.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\DpsMeter-文档索引.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\DpsMeter-使用说明-完整版.txt',

	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REPORT-换人后总伤害为什么变低-20261003.txt',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REPORT-解包与游戏内数据获取.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\src\MasterData\MasterDataNames.cs',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\src\Composition\AbilityRoster.cs',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\src\BuildInfo.cs',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\src\DpsMeter.csproj',

	# 2026-10-03 阶段 A/B/C 交付物(CJK 必须保持可读)
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\CONTRIBUTION-DATA-DICTIONARY.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\PROJECT-DIRECTION-AND-IMPLEMENTATION-PLAN.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\contrib\report_text.py',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\src\Output\Contribution.cs',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\src\Output\ContributionSession.cs',
	r'D:\dmmplayer\rlyehshoujotaix_cl\dpsmeter_contrib.py',

	# 2026-10-04 第 6 轮:当前文档集(现状快照 / 路线图 / 验收 / 决策 / 普查 / 目视 / 研究 / 回退)
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\PROJECT-STATUS.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\CONTRIBUTION-NEXT-PHASE-ROADMAP.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\RELEASE-1.7.11-ACCEPTANCE.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\DECISION-REPORT-411001.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\BUDGET-CENSUS.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\N7-PERF-AND-VISUAL-CHECKLIST.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\CONTRIBUTION-TABLE-REPORT.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\IDENTITY-CENSUS.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\IDENTITY-METADATA-DESIGN.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\ATKADD-MODEL-AUDIT.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\atkadd_sensitivity_result.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\ROLLBACK-1.7.11.md',

	# 2026-10-04 RF0/RF1:仓库边界、基线清单、批次输入快照(都有 CJK,都必须保持可读)
	r'D:\dmmplayer\rlyehshoujotaix_cl\REPO-BOUNDARY.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\baseline-manifest.json',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\batch-inputs-rf0.json',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF0-RF2.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF3.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\STATE-LIFETIME-MATRIX.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF4.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF4B.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF4C.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\CACHE-SEMANTICS-ADR.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF5A.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF4D.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\TOOL-REGISTRY.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF7A.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF4E.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF5B.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF5C.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF5D.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF5E.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF6A.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF3C.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF4F.md',
]


def scan(paths):
	rows, bad = [], 0
	for f in paths:
		if not os.path.isfile(f):
			rows.append({"file": f, "bytes": 0, "cjk": 0, "fffd": 0, "moji": 0, "decode": "MISSING", "bad": 1})
			bad += 1
			continue
		b = io.open(f, "rb").read()
		try:
			t = b.decode("utf-8")
			dec = "OK"
		except UnicodeDecodeError:
			t, dec = b.decode("utf-8", "replace"), "DECODE-ERROR"
		cjk = len(re.findall(r"[\u4e00-\u9fff]", t))
		rep = t.count("\ufffd")
		moji = len(re.findall(r"[\u00c3\u00e3\u00e5][\u0080-\u00bf]", t))
		dmg = rep + moji + (0 if dec == "OK" else 1)
		bad += dmg
		rows.append({"file": f, "bytes": len(b), "cjk": cjk, "fffd": rep, "moji": moji,
		             "decode": dec, "bad": dmg})
	return rows, bad


def selftest():
	tmp = tempfile.mkdtemp(prefix="docs123_selftest_")
	cases = []

	def mk(name, data):
		p = os.path.join(tmp, name)
		io.open(p, "wb").write(data)
		return p

	good = mk("good.md", u"正常的中文文档 ok\n".encode("utf-8"))
	fffd = mk("fffd.md", u"损坏 \ufffd 字符\n".encode("utf-8"))
	moji = mk("moji.md", (u"mojibake " + u"\u00c3\u00a4" + u"\n").encode("utf-8"))
	raw = mk("raw.md", b"\xff\xfe not utf-8\n")
	missing = os.path.join(tmp, "does-not-exist.md")
	for label, paths, want_red in (("clean", [good], False),
	                               ("U+FFFD", [fffd], True),
	                               ("mojibake", [moji], True),
	                               ("non-utf8", [raw], True),
	                               ("missing-file", [missing], True)):
		rows, bad = scan(paths)
		red = bad > 0
		cases.append({"case": label, "want_red": want_red, "got_red": red, "pass": red == want_red})
	# the real set must not have rotted: every listed document exists
	rows, bad = scan(FILES)
	miss = [os.path.basename(r["file"]) for r in rows if r["decode"] == "MISSING"]
	cases.append({"case": "all %d listed docs exist" % len(FILES), "want_red": False,
	              "got_red": bool(miss), "pass": not miss})
	for c in cases:
		print("%-34s want_red=%-5s got_red=%-5s %s" % (c["case"], c["want_red"], c["got_red"],
		                                                "ok" if c["pass"] else "FAIL"))
	m = [os.path.basename(r["file"]) for r in rows if r["decode"] == "MISSING"]
	if m:
		print("missing: %s" % ", ".join(m))
	failed = [c for c in cases if not c["pass"]]
	print("selftest: %d case(s), %d failed" % (len(cases), len(failed)))
	try:
		import shutil
		shutil.rmtree(tmp, ignore_errors=True)
	except Exception:
		pass
	return 1 if failed else 0


def main(argv=None):
	ap = argparse.ArgumentParser(description="encoding guard for the current doc set")
	ap.add_argument("--selftest", action="store_true", help="demonstrate the red path on synthetic files")
	a = ap.parse_args(argv)
	if a.selftest:
		return selftest()
	rows, bad = scan(FILES)
	for r in rows:
		print("%-46s bytes=%-8d cjk=%-6d U+FFFD=%-3d mojibake=%-3d %s"
		      % (os.path.basename(r["file"]), r["bytes"], r["cjk"], r["fffd"], r["moji"], r["decode"]))
	print("files=%d" % len(rows))
	print("TOTAL DAMAGE MARKERS: %d" % bad)
	return 1 if bad else 0


if __name__ == "__main__":
	sys.exit(main())
