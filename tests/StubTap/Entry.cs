using System.Runtime.Loader;

namespace StubTap;

/// <summary>
/// An entry type the endpoint accepts. Start appends a line to the file its startup data names, saying
/// which load context it runs in; Stop appends another to the same file.
/// </summary>
public static class Entry
{
	private static string? _log;

	public static void Start(string startupData)
	{
		_log = startupData;
		File.AppendAllText(startupData, $"start {AssemblyLoadContext.GetLoadContext(typeof(Entry).Assembly)?.Name}\n");
	}

	public static void Stop()
	{
		if (_log is not null) File.AppendAllText(_log, "stop\n");
	}
}

/// <summary>A type with neither entry point, which the endpoint refuses.</summary>
public static class NoEntry
{
}

/// <summary>An entry type whose Start fails, which the endpoint reports rather than leaving a session that never greets.</summary>
public static class Throwing
{
	public static void Start(string startupData) => throw new InvalidOperationException($"no session for {startupData}");

	public static void Stop()
	{
	}
}
