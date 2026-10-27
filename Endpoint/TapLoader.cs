using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;

namespace RoseMcp.Tap.Endpoint;

/// <summary>
/// Loads one tap into a context of its own and starts it, and replaces it when asked for a different
/// file. What a <see cref="TapEndpoint"/> does with a load request.
/// <para>
/// The same file again calls <c>Start</c> again with the new startup data, which is how a host
/// reconnects: the tap stays loaded between sessions. A different file -- another path, or the same path
/// rebuilt -- loads the new one first, and only once its entry type checks out stops the old tap and
/// unloads its context, so a refused replacement leaves the tap that was working still working.
/// </para>
/// <para>
/// The tap is read into memory rather than loaded from its path, so the file is never held open and a
/// rebuild can overwrite it while the app runs.
/// </para>
/// </summary>
internal sealed class TapLoader
{
	private const string LoadsCode = "A tap is an assembly loaded at run time and called by name.";

	private static readonly StringComparison PathComparison =
		OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

	private Loaded? _current;

	/// <summary>
	/// Loads the tap at <paramref name="assemblyPath"/>, or keeps the one already loaded from it, and calls
	/// <paramref name="entryType"/>'s <c>Start</c> with <paramref name="startupData"/>. Returns null when
	/// the tap was started, or the reason nothing was.
	/// </summary>
	[RequiresUnreferencedCode(LoadsCode)]
	[RequiresDynamicCode(LoadsCode)]
	public string? Load(string assemblyPath, string entryType, string startupData)
	{
		FileInfo file;
		try
		{
			file = new FileInfo(assemblyPath);
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or UnauthorizedAccessException)
		{
			return $"'{assemblyPath}' is not a path this endpoint can read: {exception.Message}";
		}

		if (!file.Exists) return $"There is no tap at {file.FullName}.";

		var isCurrent = _current is { } current
			&& string.Equals(current.Path, file.FullName, PathComparison)
			&& current.Written == file.LastWriteTimeUtc
			&& current.Length == file.Length
			&& string.Equals(current.EntryType, entryType, StringComparison.Ordinal);

		if (!isCurrent)
		{
			var (loaded, refusal) = LoadNew(file, entryType);
			if (loaded is null) return refusal;

			Stop();
			_current = loaded;
		}

		try
		{
			_current!.Start.Invoke(null, [startupData]);
		}
		catch (TargetInvocationException exception)
		{
			var inner = exception.InnerException ?? exception;
			return $"{entryType}.Start threw {inner.GetType().Name}: {inner.Message}";
		}

		return null;
	}

	/// <summary>Stops the loaded tap and unloads its context. Nothing to do where none is loaded.</summary>
	public void Stop()
	{
		if (_current is not { } current) return;

		_current = null;

		try
		{
			current.Stop.Invoke(null, null);
		}
		catch (TargetInvocationException)
		{
			// A tap that fails to stop is still unloaded: what it held is unreachable once its context goes,
			// unless a handler it left on the app's objects keeps the context alive, which nothing here can
			// see or fix.
		}

		current.Context.Unload();
	}

	/// <summary>The tap in <paramref name="file"/>, loaded and checked, or the reason it cannot be.</summary>
	[RequiresUnreferencedCode(LoadsCode)]
	[RequiresDynamicCode(LoadsCode)]
	private static (Loaded? Loaded, string? Refusal) LoadNew(FileInfo file, string entryType)
	{
		var context = new TapLoadContext(file.DirectoryName ?? ".");
		try
		{
			var assembly = context.LoadFromBytes(file.FullName);

			var type = assembly.GetType(entryType, throwOnError: false);
			if (type is null) return Refuse(context, $"{file.Name} has no type {entryType}.");

			var start = type.GetMethod("Start", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
			var stop = type.GetMethod("Stop", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);
			if (start is null || stop is null)
			{
				return Refuse(context, $"{entryType} needs a public static Start(string) and a public static Stop(), and has "
					+ (start is null && stop is null ? "neither." : start is null ? "no Start(string)." : "no Stop()."));
			}

			return (new Loaded(file.FullName, file.LastWriteTimeUtc, file.Length, entryType, context, start, stop), null);
		}
		catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or FileNotFoundException or TypeLoadException or IOException)
		{
			return Refuse(context, $"{file.Name} could not be loaded: {exception.Message}");
		}
	}

	private static (Loaded?, string?) Refuse(TapLoadContext context, string reason)
	{
		context.Unload();
		return (null, reason);
	}

	/// <summary>A tap that is loaded, with what it was loaded from so a request for the same file can be told from one for a rebuilt one.</summary>
	private sealed record Loaded(string Path, DateTime Written, long Length, string EntryType, TapLoadContext Context, MethodInfo Start, MethodInfo Stop);

	/// <summary>
	/// The context a tap runs in. Collectible, so a rebuilt tap can replace it without restarting the app.
	/// <para>
	/// Everything the app already has -- the framework, the base library, anything the app ships -- comes
	/// from the app's own context, so the tap and the app share one copy of the framework. A tap built
	/// beside a copy of the framework would otherwise load that copy, and read a framework with no UI in
	/// it. Only what the app cannot supply is loaded from beside the tap, and never an assembly the app
	/// already has under the same name, even when the app's copy is too old for the tap: that tap is
	/// refused rather than handed a second framework.
	/// </para>
	/// </summary>
	private sealed class TapLoadContext : AssemblyLoadContext
	{
		private readonly string _directory;

		[RequiresUnreferencedCode(LoadsCode)]
		public TapLoadContext(string directory)
			: base("RoseMCP tap", isCollectible: true)
		{
			_directory = directory;
			Resolving += ResolveBeside;
		}

		/// <summary>Null, so the app's own context answers first for every name.</summary>
		protected override Assembly? Load(AssemblyName assemblyName) => null;

		/// <summary>Reads <paramref name="path"/> into memory and loads it, with its symbols where they sit beside it.</summary>
		[RequiresUnreferencedCode(LoadsCode)]
		public Assembly LoadFromBytes(string path)
		{
			using var image = new MemoryStream(File.ReadAllBytes(path));

			var symbolsPath = Path.ChangeExtension(path, ".pdb");
			if (!File.Exists(symbolsPath)) return LoadFromStream(image);

			using var symbols = new MemoryStream(File.ReadAllBytes(symbolsPath));
			return LoadFromStream(image, symbols);
		}

		[RequiresUnreferencedCode(LoadsCode)]
		private Assembly? ResolveBeside(AssemblyLoadContext context, AssemblyName name)
		{
			var appHasIt = Default.Assemblies.Any(loaded => string.Equals(loaded.GetName().Name, name.Name, StringComparison.Ordinal));
			if (appHasIt) return null;

			var candidate = Path.Combine(_directory, name.Name + ".dll");
			if (!File.Exists(candidate)) return null;

			using var image = new MemoryStream(File.ReadAllBytes(candidate));
			return LoadFromStream(image);
		}
	}
}
