using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DpsMeter;

/// <summary>
/// Fine chart renderer (SSAA 2x + bitmap digits inside the picture).
/// Three cumulative charts: 0 = party DPS, 1 = party net taken, 2 = enemy output.
/// Drawing happens on a 2x work canvas and the RawImage shows it bilinear-downscaled,
/// which smooths diagonal lines. Numeric x ticks (seconds) and the y max value are
/// stamped inside the chart with a built-in 3x5 bitmap font so labels align with pixels.
/// Pixel row 0 is the BOTTOM-LEFT; bigger value -> bigger row index (higher on screen).
/// </summary>
public static class OverlayChart
{
	// display slot geometry (used by OverlayUGUI for layout)
	public const int W = 448;
	public const int H = 140;

	// supersample factor
	private const int S = 2;
	// work-canvas size
	private const int CW = W * S;
	private const int CH = H * S;

	// work-space plot consts
	private const int PL = 8 * S;            // left margin
	private const int PR = 8 * S;            // right margin
	private const int PLOTL = PL;
	private const int PLOTR = CW - PR;
	private const int PLOTB = 24 * S;        // row of the bottom axis (work): leaves room for tick digits
	private const int PLOTT = CH - 8;        // row of the top border pad (work)

	public static Texture2D PartyTexture;   // mode 0
	public static Texture2D TakenTexture;   // mode 1
	public static Texture2D EnemyTexture;   // mode 2

	private static Color32 _bg = new Color32(15, 18, 24, 255);
	private static Color32 _border = new Color32(90, 98, 110, 255);
	private static Color32 _grid = new Color32(44, 50, 60, 255);
	private static Color32 _axis = new Color32(165, 175, 190, 255);
	private static Color32 _tick = new Color32(230, 235, 240, 255);

	/// <summary>Chart mode: true = per-second (instant) damage; false = cumulative.</summary>
	public static bool UsePerSecond;

	/// <summary>Last battle second that has actual data (X axis / grid window).</summary>
	public static int DataEnd(BattleSession session)
	{
		if (session == null) return 1;
		int end = 0;
		foreach (var a in session.OrderedActors)
		{
			int m = a.MaxSecond();
			if (m - 1 > end) end = m - 1;
		}
		int tm = session.TeamMaxSecond();
		if (tm - 1 > end) end = tm - 1;
		if (end < 1) end = 1;
		return end;
	}

	public static void Draw(BattleSession session)
	{
		try
		{
			EnsureTexture(ref PartyTexture);
			EnsureTexture(ref TakenTexture);
			EnsureTexture(ref EnemyTexture);
			if (session == null)
			{
				FillEmpty(PartyTexture);
				FillEmpty(TakenTexture);
				FillEmpty(EnemyTexture);
				return;
			}
			int tMax = DataEnd(session);
			RenderChart(PartyTexture, tMax, session, 0);
			RenderChart(TakenTexture, tMax, session, 1);
			RenderChart(EnemyTexture, tMax, session, 2);
		}
		catch { }
	}

	private static void RenderChart(Texture2D tex, int tMax, BattleSession session, int mode)
	{
		var px = new Il2CppStructArray<Color32>(CW * CH);
		for (int i = 0; i < CW * CH; i++) px[i] = _bg;

		// ---------- series (collect, then sort like the legend so colors match) ----------
		var items = new System.Collections.Generic.List<(ActorStats a, long[] s)>();
		long yMax = 1;
		foreach (var a in session.OrderedActors)
		{
			bool ally = CharacterInfo.IsAllyTeam(a.Team);
			if (mode == 0 && !ally) continue;
			if (mode == 1 && !ally) continue;
			if (mode == 2 && ally) continue;

			bool has = mode == 1 ? HasTakenOrHeal(a, tMax) : HasDealt(a, tMax);
			if (mode == 1 && a.DamageTaken <= 0) continue; // only characters that actually took damage
			if (!has) continue;

			long[] arr = new long[tMax + 1];
			long acc = 0;
			for (int s = 0; s <= tMax; s++)
			{
				if (mode == 1)
				{
					// remaining HP percentage, stored as 0..100000 (0.1% units), starts at 100%
					arr[s] = (long)(a.GetHpPct(s) * 1000f);
					continue;
				}
				long raw = a.GetSecondDamage(s);
				if (UsePerSecond)
				{
					arr[s] = raw;               // instant per-second damage
				}
				else
				{
					acc += raw;                 // cumulative
					arr[s] = acc;
				}
			}
			items.Add((a, arr));
		}
		// sort exactly like AppendChartLegend does, so colors stay matched between line and legend
		if (mode == 0 || mode == 2) items.Sort((x, y) => y.a.DamageDealt.CompareTo(x.a.DamageDealt));
		else items.Sort((x, y) => y.a.DamageTaken.CompareTo(x.a.DamageTaken));
		foreach (var it in items)
		{
			long mx = 0;
			foreach (var v in it.s) if (v > mx) mx = v;
			if (mx > yMax) yMax = mx;
		}

		long yTopForScale = yMax;
		if (mode == 1) yTopForScale = 100000; // HP% chart is fixed to 0..100%
		double yScale = yTopForScale * 1.12;
		if (yScale <= 0) yScale = 1;

		// ---------- grid ----------
		if (tMax > 0)
		{
			for (int t = 0; t <= tMax; t += 5)
			{
				int x = PLOTL + (int)((double)t / tMax * (PLOTR - PLOTL));
				for (int r = PLOTB; r <= PLOTT; r++) px[r * CW + x] = _grid;
			}
		}
		for (int k = 1; k <= 3; k++)
		{
			int r = PLOTB + (int)((PLOTT - PLOTB) * k / 4.0);
			for (int x = PLOTL; x <= PLOTR; x++) px[r * CW + x] = _grid;
		}

		// ---------- series lines (in the same order as the legend) ----------
		int palIdx = 0;
		foreach (var it in items)
		{
			Color c = OverlayUGUI.PaletteColor(mode < 2, palIdx++);
			DrawSeries(px, it.s, tMax, yScale, c, thick: 2 * S);
		}

		// ---------- axes ----------
		for (int x = PLOTL; x <= PLOTR; x++) px[PLOTB * CW + x] = _axis;
		for (int r = PLOTB; r <= PLOTT; r++) px[r * CW + PLOTL] = _axis;

		// ---------- x tick numbers (seconds) ----------
		if (tMax > 0)
		{
			int tickEvery = 5;
			if (tMax > 60) tickEvery = 10;
			if (tMax > 180) tickEvery = 15;
			for (int t = 0; t <= tMax; t += tickEvery)
			{
				int x = PLOTL + (int)((double)t / tMax * (PLOTR - PLOTL));
				int w = DigitWidth(t) * (3 * 8) - 8;
				int dx = x - w / 2;
				// keep the label inside the plot area (no overflow past the frame)
				if (dx < PL + 2 * S) dx = PL + 2 * S;
				int maxDx = PLOTR - w - 2 * S;
				if (dx > maxDx) dx = maxDx;
				// note: 'row' is the TOP of the glyph; digits extend downward toward the bottom axis
				DrawNumber(px, dx, 38, t, 8, _tick);
			}
		}

		// ---------- y max label (top-left, inside plot) ----------
		if (yTopForScale > 0)
		{
			DrawText(px, PL + 3 * S, CH - 8, mode == 1 ? "HP%" : Abbrev(yTopForScale), 8, _tick);
		}

		// ---------- border ----------
		for (int x = 0; x < CW; x++) { px[x] = _border; px[(CH - 1) * CW + x] = _border; }
		for (int y = 0; y < CH; y++) { px[y * CW] = _border; px[y * CW + (CW - 1)] = _border; }

		tex.SetPixels32(px);
		tex.Apply(false);
	}

	private static string Abbrev(long v)
	{
		if (v >= 100000000) return (v / 1000000) + "M";
		if (v >= 100000) return (v / 1000) + "K";
		if (v >= 10000) return (v / 1000) + "K";
		return v.ToString();
	}

	private static void DrawSeries(Il2CppStructArray<Color32> px, long[] vals, int tMax, double yScale, Color c, int thick)
	{
		var col = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), 255);
		int prevX = -1, prevR = -1;
		for (int s = 0; s <= tMax; s++)
		{
			long v = vals[s];
			if (v < 0) v = 0;
			int x = PLOTL + (int)((double)s / tMax * (PLOTR - PLOTL));
			int r = PLOTB + (int)((double)v / yScale * (PLOTT - PLOTB));
			if (r < PLOTB) r = PLOTB;
			if (r > PLOTT) r = PLOTT;
			if (s > 0) DrawLine(px, prevX, prevR, x, r, col, thick);
			Plot(px, x, r, col, thick);
			prevX = x; prevR = r;
		}
		if (prevX >= 0) Plot(px, prevX, prevR, new Color32(255, 255, 255, 255), thick + 1);
	}

	private static void DrawLine(Il2CppStructArray<Color32> px, int x0, int r0, int x1, int r1, Color32 col, int thickness)
	{
		int dx = System.Math.Abs(x1 - x0), dr = System.Math.Abs(r1 - r0);
		int sx = x0 < x1 ? 1 : -1, sr = r0 < r1 ? 1 : -1;
		int err = dx - dr;
		int x = x0, r = r0;
		while (true)
		{
			Plot(px, x, r, col, thickness);
			if (x == x1 && r == r1) break;
			int e2 = 2 * err;
			if (e2 > -dr) { err -= dr; x += sx; }
			if (e2 < dx) { err += dx; r += sr; }
		}
	}

	private static void Plot(Il2CppStructArray<Color32> px, int x, int r, Color32 col, int thickness)
	{
		if (x < 4 || x >= CW - 4 || r < 4 || r >= CH - 4) return;
		int t = thickness / 2;
		for (int or = -t; or <= t; or++)
			for (int ox = -t; ox <= t; ox++)
			{
				int xx = x + ox, rr = r + or;
				if (xx < 4 || xx >= CW - 4 || rr < 4 || rr >= CH - 4) continue;
				px[rr * CW + xx] = col;
			}
	}

	// ---------- tiny 3x5 bitmap text ----------
	private static readonly string[] DIG = new string[10]
	{
		"111101101101111", // 0
		"010110010010111", // 1
		"111001111100111", // 2
		"111001111001111", // 3
		"101101111001001", // 4
		"111100111001111", // 5
		"111100111101111", // 6
		"111001010010010", // 7
		"111101111101111", // 8
		"111101111001111", // 9
	};

	private static readonly string[] ABBR = new string[1] { "K" };

	private static int DigitWidth(int v)
	{
		int n = 1;
		if (v >= 100) n = 3; else if (v >= 10) n = 2;
		return n;
	}

	private static void DrawText(Il2CppStructArray<Color32> px, int x, int row, string text, int cell, Color32 col)
	{
		int cur = x;
		for (int i = 0; i < text.Length; i++)
		{
			char ch = text[i];
			if (ch >= '0' && ch <= '9')
			{
				DrawDigitAt(px, cur, row, ch - '0', cell, col);
				cur += 3 * cell + cell; // 3px wide + gap
			}
			else if (ch == 'K' || ch == 'M')
			{
				DrawLetterK(px, cur, row, cell, col);
				cur += 3 * cell + cell;
			}
			else if (ch == 's' || ch == 'm')
			{
				cur += 2 * cell;
			}
		}
	}

	private static void DrawNumber(Il2CppStructArray<Color32> px, int x, int row, int value, int cell, Color32 col)
	{
		DrawText(px, x, row, value.ToString(), cell, col);
	}

	private static void DrawDigitAt(Il2CppStructArray<Color32> px, int x, int row, int d, int cell, Color32 col)
	{
		string pat = DIG[d];
		for (int yy = 0; yy < 5; yy++)
		{
			for (int xx = 0; xx < 3; xx++)
			{
				if (pat[yy * 3 + xx] == '1')
					FillRect(px, x + xx * cell, row - yy * cell, cell, cell, col);
			}
		}
	}

	private static void DrawLetterK(Il2CppStructArray<Color32> px, int x, int row, int cell, Color32 col)
	{
		// crude 3x5 K
		string pat = "101101110101101";
		for (int yy = 0; yy < 5; yy++)
			for (int xx = 0; xx < 3; xx++)
				if (pat[yy * 3 + xx] == '1')
					FillRect(px, x + xx * cell, row - yy * cell, cell, cell, col);
	}

	private static void FillRect(Il2CppStructArray<Color32> px, int x, int top, int w, int h, Color32 col)
	{
		int x0 = x, x1 = x + w;
		int r0 = top - h + 1, r1 = top;
		if (r0 < 0) r0 = 0;
		if (r1 >= CH) r1 = CH - 1;
		if (x0 < 0) x0 = 0;
		if (x1 > CW) x1 = CW;
		for (int r = r0; r <= r1; r++)
			for (int xx = x0; xx < x1; xx++)
				px[r * CW + xx] = col;
	}

	// ---------- helpers ----------
	private static long[] Cumulative(System.Func<int, long> f, int tMax)
	{
		long[] cum = new long[tMax + 1];
		long acc = 0;
		for (int s = 0; s <= tMax; s++) { acc += f(s); cum[s] = acc; }
		return cum;
	}

	private static bool HasDealt(ActorStats a, int tMax)
	{
		for (int s = 0; s <= tMax; s++) if (a.GetSecondDamage(s) != 0L) return true;
		return false;
	}

	private static bool HasTakenOrHeal(ActorStats a, int tMax)
	{
		for (int s = 0; s <= tMax; s++) if (a.GetSecondTaken(s) != 0L || a.GetSecondHeal(s) != 0L) return true;
		return false;
	}

	private static void FillEmpty(Texture2D tex)
	{
		var px = new Il2CppStructArray<Color32>(CW * CH);
		for (int i = 0; i < CW * CH; i++) px[i] = _bg;
		tex.SetPixels32(px);
		tex.Apply(false);
	}

	private static void EnsureTexture(ref Texture2D tex)
	{
		if (!GameRef.IsNull(tex)) return;
		tex = new Texture2D(CW, CH, TextureFormat.RGBA32, false);
		tex.filterMode = FilterMode.Bilinear; // 2x work canvas smoothed down to the RawImage
		tex.wrapMode = TextureWrapMode.Clamp;
	}
}
