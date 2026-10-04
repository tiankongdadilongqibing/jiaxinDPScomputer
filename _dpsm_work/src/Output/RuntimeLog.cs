using System;
using System.IO;
using System.Text;
using BepInEx;

namespace DpsMeter;

public static class RuntimeLog
{
	private static readonly object Lock = new object();

	private static readonly StringBuilder Buf = new StringBuilder();

	private static string _path;

	private static DateTime _lastFlush = DateTime.Now;

	public static void Init()
	{
		try
		{
			_path = Path.Combine(Paths.ConfigPath, "dpsmeter_runtime.log");
			if (File.Exists(_path))
			{
				File.Delete(_path);
			}
			Write("=== DpsMeter runtime log started ===");
			Flush();
		}
		catch
		{
		}
	}

	public static void Write(string line)
	{
		if (string.IsNullOrEmpty(_path))
		{
			return;
		}
		try
		{
			lock (Lock)
			{
				Buf.Append('[').Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append("] ")
					.Append(line)
					.Append('\n');
				if ((DateTime.Now - _lastFlush).TotalMilliseconds >= 500.0)
				{
					FlushLocked();
				}
			}
		}
		catch
		{
		}
	}

	public static void Flush()
	{
		if (string.IsNullOrEmpty(_path))
		{
			return;
		}
		try
		{
			lock (Lock)
			{
				FlushLocked();
			}
		}
		catch
		{
		}
	}

	private static void FlushLocked()
	{
		if (Buf.Length > 0)
		{
			File.AppendAllText(_path, Buf.ToString());
			Buf.Clear();
		}
		_lastFlush = DateTime.Now;
	}
}
