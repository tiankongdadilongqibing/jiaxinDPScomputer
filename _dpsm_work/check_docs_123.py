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
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF7B.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF5F.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF5G.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF4G.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF8A.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF8B.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF4H.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF7C.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\ARCHIVE-INDEX.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF7D.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF5H.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF7E.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF7F.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF7G.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF7H.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF7I.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF7J.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF7K.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF7L.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF7M.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-RF7N.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R41.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R42.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R43.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R44.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R45.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R46.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R47.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R48.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R49.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R50.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R51.md',

	# R52: 未识别规则种类的定位报告 + 证据提取流程 + 本轮批次记录(CJK 必须保持可读)
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REPORT-未识别规则种类-R52.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\EXTRACTION-FLOW.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R52.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R53.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R54.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R55.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\BATTLE-REFERENCE-IMPLEMENTATION-PLAN.md',

	# R56: 战斗编号 / 精确选场(面向用户的报告 + 逐轮记录)
	'D:\\dmmplayer\\rlyehshoujotaix_cl\\_dpsm_work\\REPORT-精确选场-R56.md',
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R56.md',

	# R57: 战斗引用绑定持久文件(实机发现)
	r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R57.md',
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R60.md',
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R61.md',
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R62.md',
    # R63: 自动技能主表转储 + 冷却单位规则
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R63.md',
    # R64: 自动技能的 Skill 侧实例 + 充能 + 发动时刻
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R64.md',
    # R65: 实机证伪 R63 的 ×30(删错数 / 删被证伪的规则 / 修探针)
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R65.md',
    # R66: 技能发动时刻做成悬浮窗的一页(F4 换岗)+ 用 9 场语料修正两条口径
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R66.md',
    # R67: 用户截图核对时间表(并N条M格 / 时间格加宽 / 奥义换通道 / gameIdx 结案)
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R67.md',
    # R69: 调用 ≠ 发动(Using 判据 / 折叠窗口改成技能自己的冷却 / 试N / 删 rec 通道)
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R69.md',
    # R70: 敌方技能混进我方时间表(三路统一团队判据)+ 一行的发动时刻放不下就往下撑
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R70.md',
    # R71: 战斗钟的原点对齐到游戏自己的战斗开始(开局补回 0.90 s;方案 B)
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R71.md',
    # R72: 让原点修正真正落地(在创建 session 处对齐 + 未定案前扣住开局事件再按到达时刻重放)
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R72.md',
    # R74: 校准仪的读数单位(初动改读 m_firstCoolTimeFrame)+ 拒因具名 + [CLOCK] calib 行 + 时间线进证据包
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R74.md',
    # R75: 「被吸收/无效化」探针(先读后命名:受击方 Life / Character.Barrier / 无敌族标志 → 分类)
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R75.md',
    # R76: 超量命中单独判定(落地 = nominal − res、溢出 = res;Life 读数优先) + 关键行独立行预算
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R76.md',
    # R77: `[ABSPROBE] sum` 一行的两个报告层缺陷(计数与金额同族群、`None` 补计数器)
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R77.md',
    # 会心逐击可观测性调查 + 折衷方案(报告,非逐轮记录)
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REPORT-CRIT-OBSERVABILITY.md',
    # R78 批次:两个词正名(文本层,不改数值) + 解除互斥把 ×1.5 会心放回同一行
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R78.md',
    # R79 批次:受击来源拆分(新导出段 takenBreakdown + F3 页面,不改已发布数值)
    r'D:\dmmplayer\rlyehshoujotaix_cl\_dpsm_work\REFACTOR-BATCH-R79.md',
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
