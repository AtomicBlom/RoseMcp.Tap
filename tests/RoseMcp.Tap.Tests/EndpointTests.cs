using System.IO.Pipes;
using System.Reflection;

using RoseMcp.Tap.Endpoint;
using RoseMcp.Tap.Protocol;

namespace RoseMcp.Tap.Tests;

/// <summary>
/// The endpoint over a real pipe, loading a real assembly: the stub tap, which writes down every time it
/// is started or stopped and which context it ran in. Named pipes are Unix domain sockets off Windows, so
/// these run there too.
/// </summary>
public sealed class EndpointTests
{
	private static readonly string StubTap = typeof(EndpointTests).Assembly
		.GetCustomAttributes<AssemblyMetadataAttribute>()
		.Single(attribute => attribute.Key == "StubTapPath")
		.Value!;

	[Test]
	public async Task Greets_naming_the_framework_and_the_endpoint_version()
	{
		await using var session = await Session.OpenAsync();

		EndpointWire.TryReadGreeting(session.Greeting, out var version, out var framework, out var frameworkVersion).ShouldBeTrue();
		version.ShouldBe(EndpointWire.Version);
		framework.ShouldBe("Stub");
		frameworkVersion.ShouldBe("1.2.3");
	}

	/// <summary>The tap runs in a collectible context of its own, not the app's, which is what lets a rebuilt one replace it.</summary>
	[Test]
	public async Task Loads_a_tap_into_a_context_of_its_own_and_starts_it()
	{
		await using var session = await Session.OpenAsync();
		var log = TempFile();

		var reply = await session.AskAsync(EndpointWire.LoadRequest(StubTap, "StubTap.Entry", log));

		EndpointWire.ReadReply(reply).ShouldBe((true, "1.2.3"));
		File.ReadAllText(log).ShouldBe("start RoseMCP tap\n");
	}

	/// <summary>How a host reconnects: the tap stays loaded and is started again with the new startup data.</summary>
	[Test]
	public async Task The_same_file_again_starts_the_tap_again_without_stopping_it()
	{
		await using var session = await Session.OpenAsync();
		var log = TempFile();

		await session.AskAsync(EndpointWire.LoadRequest(StubTap, "StubTap.Entry", log));
		await session.AskAsync(EndpointWire.LoadRequest(StubTap, "StubTap.Entry", log));

		File.ReadAllText(log).ShouldBe("start RoseMCP tap\nstart RoseMCP tap\n");
	}

	[Test]
	public async Task A_different_file_stops_the_old_tap_before_starting_the_new_one()
	{
		await using var session = await Session.OpenAsync();
		var first = TempFile();
		var second = TempFile();

		await session.AskAsync(EndpointWire.LoadRequest(StubTap, "StubTap.Entry", first));
		var reply = await session.AskAsync(EndpointWire.LoadRequest(CopyOfStub(), "StubTap.Entry", second));

		EndpointWire.ReadReply(reply).Loaded.ShouldBeTrue();
		File.ReadAllText(first).ShouldBe("start RoseMCP tap\nstop\n");
		File.ReadAllText(second).ShouldBe("start RoseMCP tap\n");
	}

	/// <summary>A replacement that cannot start is refused before the working tap is touched, so the working one keeps working.</summary>
	[Test]
	public async Task A_refused_replacement_leaves_the_loaded_tap_running()
	{
		await using var session = await Session.OpenAsync();
		var log = TempFile();

		await session.AskAsync(EndpointWire.LoadRequest(StubTap, "StubTap.Entry", log));
		var refused = await session.AskAsync(EndpointWire.LoadRequest(CopyOfStub(), "StubTap.NoEntry", log));
		await session.AskAsync(EndpointWire.LoadRequest(StubTap, "StubTap.Entry", log));

		EndpointWire.ReadReply(refused).Loaded.ShouldBeFalse();
		File.ReadAllText(log).ShouldBe("start RoseMCP tap\nstart RoseMCP tap\n");
	}

	[Test]
	[Arguments("StubTap.Missing", "has no type StubTap.Missing")]
	[Arguments("StubTap.NoEntry", "needs a public static Start(string) and a public static Stop(), and has neither")]
	[Arguments("StubTap.Throwing", "StubTap.Throwing.Start threw InvalidOperationException")]
	public async Task A_tap_that_cannot_be_started_is_refused_saying_why(string entryType, string reason)
	{
		await using var session = await Session.OpenAsync();

		var reply = EndpointWire.ReadReply(await session.AskAsync(EndpointWire.LoadRequest(StubTap, entryType, TempFile())));

		reply.Loaded.ShouldBeFalse();
		reply.Detail.ShouldContain(reason);
	}

	[Test]
	public async Task A_file_that_is_not_there_is_refused_by_its_path()
	{
		await using var session = await Session.OpenAsync();
		var missing = Path.Combine(Path.GetTempPath(), $"no-such-tap-{Guid.NewGuid():N}.dll");

		var reply = EndpointWire.ReadReply(await session.AskAsync(EndpointWire.LoadRequest(missing, "StubTap.Entry", TempFile())));

		reply.Loaded.ShouldBeFalse();
		reply.Detail.ShouldContain(missing);
	}

	/// <summary>A request from a newer host is refused by name rather than ignored, so the host is never left waiting.</summary>
	[Test]
	public async Task A_request_the_endpoint_does_not_know_is_refused_by_name()
	{
		await using var session = await Session.OpenAsync();

		var reply = EndpointWire.ReadReply(await session.AskAsync(TapWire.Row("unload", "everything")));

		reply.Loaded.ShouldBeFalse();
		reply.Detail.ShouldContain("'unload'");
	}

	/// <summary>
	/// A load that fails in a way the loader did not foresee -- an empty type name, which reflection rejects
	/// by throwing -- is refused, and the endpoint goes on serving, rather than the exception ending the app.
	/// </summary>
	[Test]
	public async Task A_load_that_throws_unforeseen_is_refused_and_the_endpoint_keeps_serving()
	{
		await using var session = await Session.OpenAsync();

		var refused = EndpointWire.ReadReply(await session.AskAsync(EndpointWire.LoadRequest(StubTap, string.Empty, TempFile())));
		var loaded = EndpointWire.ReadReply(await session.AskAsync(EndpointWire.LoadRequest(StubTap, "StubTap.Entry", TempFile())));

		refused.Loaded.ShouldBeFalse();
		refused.Detail.ShouldContain("ArgumentException");
		loaded.Loaded.ShouldBeTrue();
	}

	[Test]
	public async Task With_tap_folders_a_tap_anywhere_else_is_refused()
	{
		var copy = CopyOfStub();
		await using var session = await Session.OpenAsync([Path.GetDirectoryName(copy)!]);

		var elsewhere = EndpointWire.ReadReply(await session.AskAsync(EndpointWire.LoadRequest(StubTap, "StubTap.Entry", TempFile())));
		var inside = EndpointWire.ReadReply(await session.AskAsync(EndpointWire.LoadRequest(copy, "StubTap.Entry", TempFile())));

		elsewhere.Loaded.ShouldBeFalse();
		elsewhere.Detail.ShouldContain("is not in a folder this endpoint loads taps from");
		inside.Loaded.ShouldBeTrue();
	}

	/// <summary>A folder's name is not a prefix of another's: <c>taps</c> lets nothing in from <c>taps-old</c>.</summary>
	[Test]
	public async Task A_tap_folder_covers_the_folders_beneath_it_and_no_sibling_sharing_its_name()
	{
		var copy = CopyOfStub();
		var folder = Path.GetDirectoryName(copy)!;
		var beneath = Directory.CreateDirectory(Path.Combine(folder, "nested")).FullName;
		File.Copy(copy, Path.Combine(beneath, Path.GetFileName(copy)));
		var sibling = Directory.CreateDirectory(folder + "-old").FullName;
		File.Copy(copy, Path.Combine(sibling, Path.GetFileName(copy)));
		await using var session = await Session.OpenAsync([folder + Path.DirectorySeparatorChar]);

		var nested = EndpointWire.ReadReply(await session.AskAsync(EndpointWire.LoadRequest(Path.Combine(beneath, Path.GetFileName(copy)), "StubTap.Entry", TempFile())));
		var next = EndpointWire.ReadReply(await session.AskAsync(EndpointWire.LoadRequest(Path.Combine(sibling, Path.GetFileName(copy)), "StubTap.Entry", TempFile())));

		nested.Loaded.ShouldBeTrue();
		next.Loaded.ShouldBeFalse();
	}

	[Test]
	public async Task Serves_one_host_after_another()
	{
		var name = Session.NewPipeName();
		using var endpoint = TapEndpoint.Start(name, "Stub", "1.2.3");

		for (var host = 0; host < 2; host++)
		{
			await using var client = await Session.ConnectAsync(name);
			var greeting = await TapWire.ReadMessageAsync(client, CancellationToken.None);

			greeting.ShouldNotBeNull();
			greeting.ShouldStartWith(EndpointWire.GreetingPrefix);
		}
	}

	/// <summary>
	/// A pipe something else created under the endpoint's name is the impostor a host checks for, so the
	/// endpoint refuses to join it and says so rather than listening behind it.
	/// </summary>
	[Test]
	public async Task Does_not_listen_behind_a_pipe_another_holder_created_first()
	{
		var name = Session.NewPipeName();
		await using var impostor = new NamedPipeServerStream(name, PipeDirection.InOut, 2, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
		var said = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var endpoint = TapEndpoint.Start(name, "Stub", "1.2.3", line => said.TrySetResult(line));

		(await said.Task.WaitAsync(TimeSpan.FromSeconds(5))).ShouldStartWith($"Could not listen on {name}");
	}

	private static string TempFile()
	{
		var path = Path.Combine(Path.GetTempPath(), $"rosemcp-tap-test-{Guid.NewGuid():N}.log");
		File.WriteAllText(path, string.Empty);
		return path;
	}

	/// <summary>The stub copied to a folder of its own: the same assembly, but a different file to the endpoint.</summary>
	private static string CopyOfStub()
	{
		var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"rosemcp-tap-test-{Guid.NewGuid():N}"));
		var copy = Path.Combine(folder.FullName, Path.GetFileName(StubTap));
		File.Copy(StubTap, copy);
		return copy;
	}

	/// <summary>An endpoint with one host connected to it and greeted.</summary>
	private sealed class Session : IAsyncDisposable
	{
		private readonly TapEndpoint _endpoint;
		private readonly NamedPipeClientStream _client;

		private Session(TapEndpoint endpoint, NamedPipeClientStream client, string greeting)
		{
			_endpoint = endpoint;
			_client = client;
			Greeting = greeting;
		}

		public string Greeting { get; }

		public static string NewPipeName() => $"rosemcp-tap-test-{Guid.NewGuid():N}";

		public static async Task<Session> OpenAsync(IReadOnlyCollection<string>? tapDirectories = null)
		{
			var name = NewPipeName();
			var endpoint = TapEndpoint.Start(name, "Stub", "1.2.3", tapDirectories: tapDirectories);
			var client = await ConnectAsync(name);
			var greeting = await TapWire.ReadMessageAsync(client, CancellationToken.None);

			return new Session(endpoint, client, greeting ?? string.Empty);
		}

		public static async Task<NamedPipeClientStream> ConnectAsync(string name)
		{
			var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
			await client.ConnectAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
			return client;
		}

		public async Task<string> AskAsync(string request)
		{
			await TapWire.WriteMessageAsync(_client, request, CancellationToken.None);
			return await TapWire.ReadMessageAsync(_client, CancellationToken.None) ?? string.Empty;
		}

		public async ValueTask DisposeAsync()
		{
			await _client.DisposeAsync();
			_endpoint.Dispose();
		}
	}
}
