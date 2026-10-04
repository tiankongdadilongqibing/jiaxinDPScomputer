// Export-JSON adapter: export file -> the plugin core's inputs (DpsMeter.ContributionHit/_Actor).
// It compiles the REAL ../src/Output/Contribution.cs, so this offline run exercises the shipping code
// (review R1 mitigation: "pre-deploy offline parity run on the existing export").
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using DpsMeter;

namespace ContribCs
{
    public static class ExportJsonAdapter
    {
        public static void Load(string path, out List<ContributionHit> hits,
            out List<ContributionActor> roster, out string version, out int quest)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var root = doc.RootElement;
            version = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
            quest = root.TryGetProperty("quest", out var q) && q.ValueKind == JsonValueKind.Number ? q.GetInt32() : 0;
            roster = new List<ContributionActor>();
            hits = new List<ContributionHit>();
            foreach (var row in root.GetProperty("actors").EnumerateArray())
            {
                var a = new ContributionActor
                {
                    Key = row.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.Number ? k.GetInt32() : -1,
                    Name = row.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : "",
                    Team = row.TryGetProperty("team", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0,
                    Kind = row.TryGetProperty("kind", out var kd) && kd.ValueKind == JsonValueKind.String ? kd.GetString() : "",
                    Summon = row.TryGetProperty("summon", out var su) && su.ValueKind == JsonValueKind.True,
                };
                if (row.TryGetProperty("abilities", out var abs) && abs.ValueKind == JsonValueKind.Array)
                    foreach (var ab in abs.EnumerateArray())
                    {
                        if (ab.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number) a.AbilityIds.Add(id.GetInt32());
                        if (ab.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String) a.AbilityNames.Add(nm.GetString());
                    }
                if (row.TryGetProperty("talents", out var tls) && tls.ValueKind == JsonValueKind.Array)
                    foreach (var tl in tls.EnumerateArray())
                    {
                        if (tl.TryGetProperty("abilityId", out var id) && id.ValueKind == JsonValueKind.Number) a.AbilityIds.Add(id.GetInt32());
                        if (tl.TryGetProperty("ability", out var nm) && nm.ValueKind == JsonValueKind.String) a.AbilityNames.Add(nm.GetString());
                    }
                roster.Add(a);
            }
            foreach (var e in root.GetProperty("events").EnumerateArray())
            {
                if (!e.TryGetProperty("type", out var ty) || ty.ValueKind != JsonValueKind.String || ty.GetString() != "dmg") continue;
                var hit = new ContributionHit
                {
                    Damage = e.TryGetProperty("amount", out var am) && am.ValueKind == JsonValueKind.Number ? am.GetDouble() : 0.0,
                    AttackerKey = e.TryGetProperty("atkKey", out var ak) && ak.ValueKind == JsonValueKind.Number ? ak.GetInt32() : 0,
                };
                if (e.TryGetProperty("calc", out var calc) && calc.ValueKind == JsonValueKind.Object)
                {
                    hit.HasCalc = true;
                    if (calc.TryGetProperty("foldDropped", out var fd) && fd.ValueKind == JsonValueKind.Number) hit.FoldDropped = fd.GetInt32();
                    if (calc.TryGetProperty("fold", out var fl) && fl.ValueKind == JsonValueKind.Array)
                        foreach (var f in fl.EnumerateArray())
                            hit.Folds.Add(new ContributionFold
                            {
                                Kind = f.TryGetProperty("kind", out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : "",
                                Side = f.TryGetProperty("side", out x) && x.ValueKind == JsonValueKind.String ? x.GetString() : "",
                                Origin = f.TryGetProperty("origin", out x) && x.ValueKind == JsonValueKind.String ? x.GetString() : "",
                                Label = f.TryGetProperty("label", out x) && x.ValueKind == JsonValueKind.String ? x.GetString() : "",
                                ByUnit = f.TryGetProperty("byUnit", out x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null,
                                Factor = f.TryGetProperty("factor", out x) && x.ValueKind == JsonValueKind.Number ? x.GetDouble() : 0.0,
                            });
                }
                hits.Add(hit);
            }
        }
    }
}
