using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;

using RoseMcp.Tap.Protocol;

namespace RoseMcp.Tap.Endpoint;

/// <summary>
/// The pipe a framework listens on inside an app so a RoseMCP host can load a tap into it: the managed
/// counterpart of XAML's <c>InitializeXamlDiagnosticsEx</c>. A framework starts one when its UI comes up,
/// if the app has opted into diagnostics, and a host connecting to it asks for a tap assembly to be
/// loaded and started.
/// <para>
/// It knows nothing about any framework and nothing about RoseMCP's host: it loads an assembly and calls
/// a static method on it, so compiling it in gives a framework no dependency on RoseMCP. The tap reaches
/// the framework through the framework's own public diagnostics API, not through anything passed here.
/// </para>
/// <para>
/// The pipe is created for the current user only. On Windows, anyone running as that user can already
/// debug the app or inject a library into it; elsewhere that is not always so (Linux's Yama
/// <c>ptrace_scope</c>, macOS's hardened runtime), and there the endpoint lets that user run code in the
/// app. A framework that would rather not starts it with the folders taps may be loaded from, and every
/// other path is refused. The host still checks that the process serving the pipe is its target before it
/// sends anything, because a process that created the name first would otherwise be handed the host's
/// pipe and key.
/// </para>
/// <para>
/// Nothing a request does can end the app: a load that fails in a way nothing here foresaw is refused,
/// and anything else that escapes is said and the host dropped.
/// </para>
/// <para>
/// One connection at a time, on a background thread of its own. An idle endpoint costs that one blocked
/// thread and nothing else.
/// </para>
/// </summary>
internal sealed class TapEndpoint : IDisposable
{
	private const string LoadsCode = "The endpoint loads a tap assembly and calls into it by name, which needs the JIT and the types the tap names.";

	private readonly string _framework;
	private readonly string _frameworkVersion;
	private readonly Action<string>? _log;
	private readonly CancellationTokenSource _stopping = new();
	private readonly TapLoader _loader;
	private readonly Thread _thread;

	[RequiresUnreferencedCode(LoadsCode)]
	[RequiresDynamicCode(LoadsCode)]
	private TapEndpoint(string pipeName, string framework, string frameworkVersion, Action<string>? log, IReadOnlyCollection<string>? tapDirectories)
	{
		PipeName = pipeName;
		_framework = framework;
		_frameworkVersion = frameworkVersion;
		_log = log;
		_loader = new TapLoader(tapDirectories);

		_thread = new Thread(Run)
		{
			IsBackground = true,
			Name = "rosemcp-tap-endpoint",
		};
	}

	/// <summary>The pipe this endpoint listens on, without the <c>\\.\pipe\</c> prefix.</summary>
	public string PipeName { get; }

	/// <summary>
	/// Starts listening on <paramref name="pipeName"/>, announcing <paramref name="framework"/> at
	/// <paramref name="frameworkVersion"/> to every host that connects. <paramref name="log"/> hears what
	/// the endpoint does and why it refused anything, on the endpoint's own thread. With
	/// <paramref name="tapDirectories"/>, a tap is loaded only from one of those folders or a folder beneath
	/// one; without, from any path the app can read.
	/// </summary>
	[RequiresUnreferencedCode(LoadsCode)]
	[RequiresDynamicCode(LoadsCode)]
	public static TapEndpoint Start(string pipeName, string framework, string frameworkVersion, Action<string>? log = null,
		IReadOnlyCollection<string>? tapDirectories = null)
	{
		var endpoint = new TapEndpoint(pipeName, framework, frameworkVersion, log, tapDirectories);
		endpoint._thread.Start();
		return endpoint;
	}

	[RequiresUnreferencedCode(LoadsCode)]
	[RequiresDynamicCode(LoadsCode)]
	private void Run()
	{
		try
		{
			RunAsync(_stopping.Token).GetAwaiter().GetResult();
		}
		catch (Exception exception)
		{
			// An exception leaving this thread would end the app, which is worse than an endpoint that stops.
			_log?.Invoke($"The endpoint on {PipeName} stopped: {exception.GetType().Name}: {exception.Message}");
		}
	}

	/// <summary>
	/// Accepts one host at a time, with the next instance of the pipe already listening while the current
	/// host is served.
	/// <para>
	/// Two instances rather than one, because off Windows a pipe is a Unix domain socket whose listener
	/// closes with the last instance of the name. A host that connects in the moment between one instance
	/// going and the next being made is accepted by the old listener and then dropped with it, and reads
	/// the endpoint's greeting as a closed pipe. Only the current instance is ever served, so requests are
	/// still answered one host at a time.
	/// </para>
	/// </summary>
	[RequiresUnreferencedCode(LoadsCode)]
	[RequiresDynamicCode(LoadsCode)]
	private async Task RunAsync(CancellationToken stopping)
	{
		var next = Listen(first: true);

		while (next is not null && !stopping.IsCancellationRequested)
		{
			var server = next;
			next = null;

			await using (server.ConfigureAwait(false))
			{
				try
				{
					await server.WaitForConnectionAsync(stopping).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					return;
				}
				catch (IOException exception)
				{
					_log?.Invoke($"A connection to {PipeName} failed before it was served: {exception.Message}");
					next = Listen(first: false);
					continue;
				}

				next = Listen(first: false);

				try
				{
					await ServeAsync(server, stopping).ConfigureAwait(false);
				}
				catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
				{
					_log?.Invoke($"The host on {PipeName} went before it was answered: {exception.Message}");
				}
				catch (Exception exception)
				{
					_log?.Invoke($"Serving the host on {PipeName} failed, so it was dropped: {exception.GetType().Name}: {exception.Message}");
				}
			}
		}

		if (next is not null) await next.DisposeAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// A new instance of the pipe, or null when one cannot be made.
	/// <para>
	/// The first is made as the first instance of the name, so a pipe some other process already created
	/// under it is refused rather than joined. Said and given up on rather than retried: a pipe named for
	/// this process's id that something else created is exactly the impostor the host checks for, and
	/// listening behind it would only hide that.
	/// </para>
	/// </summary>
	private NamedPipeServerStream? Listen(bool first)
	{
		var options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;
		if (first) options |= PipeOptions.FirstPipeInstance;

		try
		{
			return new NamedPipeServerStream(PipeName, PipeDirection.InOut, maxNumberOfServerInstances: 2, PipeTransmissionMode.Byte, options);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			_log?.Invoke($"Could not listen on {PipeName}: {exception.Message}");
			return null;
		}
	}

	/// <summary>Greets the host that connected and answers each request it sends until it hangs up.</summary>
	[RequiresUnreferencedCode(LoadsCode)]
	[RequiresDynamicCode(LoadsCode)]
	private async Task ServeAsync(Stream stream, CancellationToken stopping)
	{
		await TapWire.WriteMessageAsync(stream, EndpointWire.Greeting(_framework, _frameworkVersion), stopping).ConfigureAwait(false);

		while (true)
		{
			var request = await TapWire.ReadMessageAsync(stream, stopping).ConfigureAwait(false);
			if (request is null) return;

			string reply;
			if (EndpointWire.TryReadLoadRequest(request, out var assemblyPath, out var entryType, out var startupData))
			{
				string? refusal;
				try
				{
					refusal = _loader.Load(assemblyPath, entryType, startupData);
				}
				catch (Exception exception)
				{
					// A failure the loader did not foresee: the host is told, rather than the exception ending the app.
					refusal = $"Loading {entryType} from '{assemblyPath}' threw {exception.GetType().Name}: {exception.Message}";
				}

				reply = refusal is not null
					? EndpointWire.RefusedReply(refusal)
					: EndpointWire.LoadedReply(_framework, _frameworkVersion);
			}
			else
			{
				var verb = TapWire.Fields(request)[0];
				reply = EndpointWire.RefusedReply($"'{verb}' is not a request endpoint version {EndpointWire.Version} knows.");
			}

			_log?.Invoke($"{PipeName}: {request.Split('\t')[0]} answered {reply.Split('\t')[0]}");
			await TapWire.WriteMessageAsync(stream, reply, stopping).ConfigureAwait(false);
		}
	}

	/// <summary>Stops listening, then stops the tap it loaded, if any.</summary>
	public void Dispose()
	{
		_stopping.Cancel();

		// Bounded, because a host mid-request holds the thread in a read the cancellation ends on its own
		// schedule, and disposal is reached from an app shutting down.
		_thread.Join(TimeSpan.FromSeconds(2));

		_loader.Stop();
		_stopping.Dispose();
	}
}
