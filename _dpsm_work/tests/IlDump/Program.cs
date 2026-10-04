using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Writes one ASCII line per type, plus one per field (in DECLARATION order, because static field
/// initialisation order is what a partial split can silently reorder) and one per method (sorted by name,
/// because the compiler's method emission order follows file order and is not a semantic property).
///
/// Field and method SIGNATURES are hashed from their raw metadata blobs, so a change in a parameter or
/// return type is caught even when the name is unchanged.
/// </summary>
internal static class Program
{
	private static string Hex(byte[] b)
	{
		var sb = new StringBuilder(b.Length * 2);
		foreach (byte x in b) sb.Append(x.ToString("x2"));
		return sb.ToString();
	}

	private static string Sig(BlobHandle h, MetadataReader md)
	{
		if (h.IsNil) return "-";
		byte[] raw = md.GetBlobBytes(h);
		using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(raw)).Substring(0, 16);
	}

	private static int Main(string[] args)
	{
		if (args.Length < 1)
		{
			Console.WriteLine("usage: IlDump <assembly.dll> [typeNameFilter]");
			return 2;
		}
		string filter = args.Length > 1 ? args[1] : null;
		var outLines = new List<string>();
		using (var fs = File.OpenRead(args[0]))
		using (var pe = new PEReader(fs))
		{
			MetadataReader md = pe.GetMetadataReader();
			int types = 0;
			foreach (TypeDefinitionHandle th in md.TypeDefinitions)
			{
				TypeDefinition td = md.GetTypeDefinition(th);
				string tname = md.GetString(td.Namespace) + "." + md.GetString(td.Name);
				if (filter != null && tname.IndexOf(filter, StringComparison.Ordinal) < 0) continue;
				types++;
				outLines.Add("TYPE " + tname);
				var fieldNames = new List<string>();
				int fi = 0;
				foreach (FieldDefinitionHandle fh in td.GetFields())
				{
					FieldDefinition fd = md.GetFieldDefinition(fh);
					string fn = md.GetString(fd.Name);
					outLines.Add("  FIELD " + fi.ToString("D3") + " " + fn + " sig=" + Sig(fd.Signature, md));
					fieldNames.Add(fn + ":" + Sig(fd.Signature, md));
					fi++;
				}
				outLines.Add("  FIELDORDER n=" + fi + " sha=" + Hash(string.Join("|", fieldNames)));
				var methods = new List<string>();
				foreach (MethodDefinitionHandle mh in td.GetMethods())
				{
					MethodDefinition mdef = md.GetMethodDefinition(mh);
					string mn = md.GetString(mdef.Name);
					string il = "-";
					if (mdef.RelativeVirtualAddress != 0)
					{
						MethodBodyBlock body = pe.GetMethodBody(mdef.RelativeVirtualAddress);
						byte[] bytes = body.GetILBytes();
						using (var sha = SHA256.Create())
							il = Hex(sha.ComputeHash(bytes)).Substring(0, 16) + ":len=" + bytes.Length;
					}
					methods.Add("  METHOD " + mn + " sig=" + Sig(mdef.Signature, md) + " il=" + il);
				}
				methods.Sort(StringComparer.Ordinal);
				outLines.Add("  METHODCOUNT n=" + methods.Count);
				outLines.AddRange(methods);
			}
			outLines.Add("TYPES n=" + types);
		}
		foreach (string l in outLines) Console.WriteLine(l);
		return 0;
	}

	private static string Hash(string s)
	{
		using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Substring(0, 16);
	}
}
