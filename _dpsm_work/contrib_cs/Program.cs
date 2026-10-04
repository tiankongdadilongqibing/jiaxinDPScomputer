using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DpsMeter;

namespace ContribCs
{
    internal static class Program
    {
        // usage: ContribCs <export.json> [out-section.json]
        // Runs the SHIPPING contribution core (../src/Output/Contribution.cs) against one export.
        private static int Main(string[] args)
        {
            if (args.Length < 1) { Console.WriteLine("usage: ContribCs <export.json> [out.json]"); return 2; }
            string outPath = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "contribution_cs.json");
            List<ContributionHit> hits;
            List<ContributionActor> roster;
            string version;
            int quest;
            ExportJsonAdapter.Load(args[0], out hits, out roster, out version, out quest);
            var sb = new StringBuilder(1 << 16);
            ContributionStats st;
            using (var _ = new MemoryStream())
            {
                st = Contribution.AppendJson(sb, hits, roster, 1, version, quest);
            }
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine("CS(plugin src) analyzable={0:F0} attributed={1:F0} credited={2:F6} unattr={3:F0} hits={4} folds={5} zero={6} noop={7} subUnity={8} negative={9} calcMissing={10}",
                st.Analyzable, st.Attributed, st.CreditedShare, st.Unattributed, st.Hits, st.Folds,
                st.ZeroFactor, st.NoopFactor, st.SubUnity, st.Negative, st.CalcMissing);
            Console.WriteLine("CS wrote " + outPath);
            return 0;
        }
    }
}
