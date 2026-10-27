namespace RoseMcp.Tap.Protocol;

/// <summary>
/// The names of the commands a host sends a tap, which are also the names a tap declares in its
/// capabilities, and the items some of them take.
/// <para>
/// A command arrives as its name, then its arguments after a space on the same line
/// (<c>properties 12 all</c>, <c>select rulers nomyxaml</c>) or on the lines after it (<c>apply</c>).
/// A tap answers every command it serves with something, even <c>none</c>; an empty reply means the
/// command was not understood, and a host must be able to tell that from a lost message, or it resends
/// an edit.
/// </para>
/// </summary>
#if ROSEMCP_TAP_PUBLIC
public
#else
internal
#endif
static class TapCommands
{
	/// <summary>Every element, one <see cref="TapRows.Tree"/> row each.</summary>
	public const string Tree = "tree";

	/// <summary>
	/// One element's properties: <c>properties &lt;handle&gt;</c>, with <c> all</c> for defaults too. The reply is
	/// <see cref="TapRows.PropertiesOk"/> or <see cref="TapRows.PropertiesError"/>, then an <see cref="TapRows.Element"/>
	/// row and a <see cref="TapRows.Property"/> row per property.
	/// </summary>
	public const string Properties = "properties";

	/// <summary>
	/// A capability rather than a command: the tap fills the writable field of each property row, saying
	/// whether that property can be changed live, or why not.
	/// </summary>
	public const string PropertiesWritable = "properties.writable";

	/// <summary>
	/// A batch of edits, one <see cref="TapRows.ReadCommand"/> row per line after the command, answered
	/// with one <see cref="TapRows.ApplyResult"/> row per command. Declared with the edit kinds a tap
	/// applies: <c>apply:SetProperty,ClearProperty</c>.
	/// </summary>
	public const string Apply = "apply";

	/// <summary>
	/// Arms the overlay's pointer: <c>select</c> to pick, <c>select rulers</c> to measure, either with
	/// <c>all</c> to reach elements that are not hit-testable and <c>myxaml</c> or <c>nomyxaml</c> to
	/// prefer the app's own markup or not. Answered with <see cref="TapRows.Armed"/>. Declared with its
	/// modes: <c>select:pick,rulers</c>.
	/// </summary>
	public const string Select = "select";

	/// <summary>The mode of <see cref="Select"/> that picks an element.</summary>
	public const string SelectPick = "pick";

	/// <summary>The mode of <see cref="Select"/> that measures between elements.</summary>
	public const string SelectRulers = "rulers";

	/// <summary>Picks an element outright: <c>selecthandle &lt;handle&gt;</c>, answered <c>selected</c> or <c>none</c>.</summary>
	public const string SelectHandle = "selecthandle";

	/// <summary>Clears the pick, answered <c>cleared</c> or <c>nothing</c>.</summary>
	public const string Deselect = "deselect";

	/// <summary>Disarms the overlay's pointer, answered <c>idle</c>.</summary>
	public const string Idle = "idle";

	/// <summary>
	/// What the overlay holds: a <see cref="TapRows.SelectionHeader"/> row, then a
	/// <see cref="TapRows.Candidate"/> row per element under the pick, topmost first.
	/// </summary>
	public const string Selection = "selection";

	/// <summary>
	/// The last press of the toolbar's Ask button: <c>ask &lt;seen&gt;</c>, answered with an
	/// <see cref="TapRows.Ask"/> row or <c>none</c>. Answered off the UI thread, because a host asks on
	/// every refresh.
	/// </summary>
	public const string Ask = "ask";

	/// <summary>Shows or hides the marks on what live edits changed: <c>changes on</c> or <c>changes off</c>, answered <c>on</c> or <c>off</c>.</summary>
	public const string Changes = "changes";

	/// <summary>Ends the session in the app, answered <c>released</c> or <c>already released</c>.</summary>
	public const string Detach = "detach";

	/// <summary>An edit kind of <see cref="Apply"/>: sets a property's local value.</summary>
	public const string SetProperty = "SetProperty";

	/// <summary>An edit kind of <see cref="Apply"/>: clears a property's local value.</summary>
	public const string ClearProperty = "ClearProperty";

	/// <summary>An edit kind of <see cref="Apply"/>: inserts an element, built by <see cref="CreateInstance"/> commands.</summary>
	public const string AddChild = "AddChild";

	/// <summary>An edit kind of <see cref="Apply"/>: removes an element.</summary>
	public const string RemoveChild = "RemoveChild";

	/// <summary>An edit kind of <see cref="Apply"/>: replaces a resource in a dictionary.</summary>
	public const string SetResource = "SetResource";

	/// <summary>
	/// A command inside an <see cref="Apply"/> batch rather than an edit kind: creates the element an
	/// <see cref="AddChild"/> inserts. A tap that applies <see cref="AddChild"/> serves it.
	/// </summary>
	public const string CreateInstance = "CreateInstance";

	/// <summary>
	/// A command inside an <see cref="Apply"/> batch rather than an edit kind: puts what a slot built behind a key
	/// in the target's resource dictionary. A <see cref="SetResource"/> edit is sent as the
	/// <see cref="CreateInstance"/> commands that build the resource, then one of these.
	/// </summary>
	public const string ReplaceResource = "ReplaceResource";
}
