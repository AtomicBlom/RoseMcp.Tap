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
/// The pipe is created for the current user only. Anyone running as that user can already debug the app
/// or inject a library into it, so the endpoint grants nothing new; the host still checks that the
/// process serving the pipe is its target before it sends anything, because a process that created the
/// name first would otherwise be handed the host's pipe and key.
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
	private readonly TapLoader _loader = new();
	private readonly Thread _thread;

	[RequiresUnreferencedCode(LoadsCode)]
	[RequiresDynamicCode(LoadsCode)]
	private TapEndpoint(string pipeName, string framework, string frameworkVersion, Action<string>? log)
	{
		PipeName = pipeName;
		_framework = framework;
		_frameworkVersion = frameworkVersion;
		_log = log;

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
	/// the endpoint does and why it refused anything, on the endpoint's own thread.
	/// </summary>
	[RequiresUnreferencedCode(LoadsCode)]
	[RequiresDynamicCode(LoadsCode)]
	public static TapEndpoint Start(string pipeName, string framework, string frameworkVersion, Action<string>? log = null)
	{
		var endpoint = new TapEndpoint(pipeName, framework, frameworkVersion, log);
		endpoint._thread.Start();
		return endpoint;
	}

	[RequiresUnreferencedCode(LoadsCode)]
	[RequiresDynamicCode(LoadsCode)]
	private void Run() => RunAsync(_stopping.Token).GetAwaiter().GetResult();

	[RequiresUnreferencedCode(LoadsCode)]
	[RequiresDynamicCode(LoadsCode)]
	private async Task RunAsync(CancellationToken stopping)
	{
		while (!stopping.IsCancellationRequested)
		{
			NamedPipeServerStream server;
			try
			{
				server = new NamedPipeServerStream(
					PipeName,
					PipeDirection.InOut,
					maxNumberOfServerInstances: 1,
					PipeTransmissionMode.Byte,
					PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				// Another process already holds the name. Said and given up on rather than retried: a pipe
				// named for this process's id that something else created is exactly the impostor the host
				// checks for, and listening behind it would only hide that.
				_log?.Invoke($"Could not listen on {PipeName}: {exception.Message}");
				return;
			}

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
					continue;
				}

				try
				{
					await ServeAsync(server, stopping).ConfigureAwait(false);
				}
				catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
				{
					_log?.Invoke($"The host on {PipeName} went before it was answered: {exception.Message}");
				}
			}
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
				reply = _loader.Load(assemblyPath, entryType, startupData) is { } refusal
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
