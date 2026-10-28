using System.Globalization;

namespace RoseMcp.Tap.Protocol;

/// <summary>
/// What a host and a framework's diagnostics endpoint say to each other: the endpoint greets, the host
/// asks it to load a tap, and the endpoint answers once. Messages are framed by
/// <see cref="TapWire.WriteMessage"/> and their fields made by <see cref="TapWire.Row"/>.
/// <list type="number">
/// <item><description>
/// The endpoint greets with <c>RoseTapEndpoint/{version}</c>, the framework's name and its version.
/// </description></item>
/// <item><description>
/// The host sends <c>load</c>, the tap's assembly path, its entry type and the startup data to call
/// <c>Start</c> with.
/// </description></item>
/// <item><description>
/// The endpoint answers <c>loaded</c> with the framework's name and version, or <c>refused</c> with a
/// reason.
/// </description></item>
/// </list>
/// <para>
/// Versioned apart from the tap protocol, because the two move at different speeds. The endpoint is
/// compiled into a framework or an app and changes only when they take a new copy; the tap protocol
/// moves with RoseMCP and the tap. Tied together, a protocol change would leave every app built before
/// it unable to attach until rebuilt. Changes here are additions only -- a new field on the end, a new
/// request -- so a host can always speak the newest version both sides know.
/// </para>
/// </summary>
#if ROSEMCP_TAP_PUBLIC
public
#else
internal
#endif
static class EndpointWire
{
	/// <summary>The version of the endpoint protocol this copy of the contract speaks.</summary>
	public const int Version = 1;

	/// <summary>How an endpoint's greeting starts.</summary>
	public const string GreetingPrefix = "RoseTapEndpoint/";

	/// <summary>The request that loads a tap and starts it.</summary>
	public const string Load = "load";

	/// <summary>The answer to <see cref="Load"/> when the tap's <c>Start</c> was called and returned.</summary>
	public const string Loaded = "loaded";

	/// <summary>The answer to <see cref="Load"/> when nothing was started, with the reason.</summary>
	public const string Refused = "refused";

	/// <summary>The pipe an endpoint listens on in process <paramref name="pid"/>, from a pattern such as <c>contoso-diag-{pid}</c>.</summary>
	public static string PipeName(string pattern, int pid) =>
		pattern.Replace("{pid}", pid.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

	/// <summary>The endpoint's greeting, naming the framework that hosts it.</summary>
	public static string Greeting(string framework, string frameworkVersion) =>
		TapWire.Row(GreetingPrefix + Version.ToString(CultureInfo.InvariantCulture), framework, frameworkVersion);

	/// <summary>The endpoint version, framework and framework version an endpoint greeted with, or false when the message is not a greeting.</summary>
	public static bool TryReadGreeting(string message, out int version, out string framework, out string frameworkVersion)
	{
		var fields = TapWire.Fields(message);
		version = 0;
		framework = fields.Length > 1 ? fields[1] : string.Empty;
		frameworkVersion = fields.Length > 2 ? fields[2] : string.Empty;

		return fields[0].StartsWith(GreetingPrefix, StringComparison.Ordinal)
			&& int.TryParse(fields[0].AsSpan(GreetingPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out version);
	}

	/// <summary>The request to load the tap at <paramref name="assemblyPath"/> and call <paramref name="entryType"/>'s <c>Start</c> with <paramref name="startupData"/>.</summary>
	public static string LoadRequest(string assemblyPath, string entryType, string startupData) =>
		TapWire.Row(Load, assemblyPath, entryType, startupData);

	/// <summary>What a <see cref="Load"/> request asks for, or false when the message is not one.</summary>
	public static bool TryReadLoadRequest(string message, out string assemblyPath, out string entryType, out string startupData)
	{
		var fields = TapWire.Fields(message);
		var isLoad = fields.Length >= 4 && fields[0] == Load;

		assemblyPath = isLoad ? fields[1] : string.Empty;
		entryType = isLoad ? fields[2] : string.Empty;
		startupData = isLoad ? fields[3] : string.Empty;

		return isLoad;
	}

	/// <summary>The answer to a load that started the tap.</summary>
	public static string LoadedReply(string framework, string frameworkVersion) => TapWire.Row(Loaded, framework, frameworkVersion);

	/// <summary>The answer to a load that started nothing, and why.</summary>
	public static string RefusedReply(string reason) => TapWire.Row(Refused, reason);

	/// <summary>
	/// Whether a reply says the tap was loaded, and the detail it carries: the framework's version for a
	/// load, the reason for a refusal, or the reply quoted where it is neither.
	/// </summary>
	public static (bool Loaded, string Detail) ReadReply(string reply)
	{
		var fields = TapWire.Fields(reply);
		return fields[0] switch
		{
			Loaded => (true, fields.Length > 2 ? fields[2] : string.Empty),
			Refused => (false, fields.Length > 1 ? fields[1] : string.Empty),
			_ => (false, $"The endpoint answered '{(reply.Length <= 60 ? reply : reply[..60] + "...")}', which is not a reply to a load."),
		};
	}
}
