namespace RoseMcp.Tap.Protocol;

/// <summary>
/// Marks an assembly as a RoseMCP tap and says how to reach it. RoseMCP reads this from the assembly's
/// metadata without loading it, finds it by its full name rather than by type identity, and never runs
/// the tap in its own processes: the tap only ever runs inside the app it serves.
/// <para>
/// <code>
/// [assembly: RoseMcpTap(Name = "Contoso", Module = "Contoso.dll", Endpoint = "contoso-diag-{pid}", EntryType = "Contoso.RoseTap.Tap", Protocol = 5)]
/// </code>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
#if ROSEMCP_TAP_PUBLIC
public
#else
internal
#endif
sealed class RoseMcpTapAttribute : Attribute
{
	/// <summary>The framework the tap serves, as it should read in an error: <c>Contoso</c>.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// The file name of the framework's own assembly, which a target serving it has loaded:
	/// <c>Contoso.dll</c>. What lets RoseMCP say a target runs the framework but has no endpoint
	/// listening, rather than that nothing serves it.
	/// </summary>
	public string Module { get; set; } = string.Empty;

	/// <summary>
	/// The pipe the framework's diagnostics endpoint listens on in a target, with <c>{pid}</c> standing
	/// for the target's process id: <c>contoso-diag-{pid}</c>.
	/// </summary>
	public string Endpoint { get; set; } = string.Empty;

	/// <summary>
	/// The type the endpoint starts the tap through, by full name. It has a public static
	/// <c>Start(string startupData)</c> and a public static <c>Stop()</c>, found by name because each copy
	/// of the endpoint compiles its own types and an interface could not be shared between them.
	/// </summary>
	public string EntryType { get; set; } = string.Empty;

	/// <summary>The protocol version the tap speaks, which is <see cref="TapGreeting.ProtocolVersion"/> of the contract it was built against.</summary>
	public int Protocol { get; set; }
}
