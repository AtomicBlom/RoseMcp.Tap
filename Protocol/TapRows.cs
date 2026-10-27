using System.Globalization;

namespace RoseMcp.Tap.Protocol;

/// <summary>
/// The rows a tap answers with, one writer per row, in the field order the host reads them by. Every
/// field goes through <see cref="TapWire.Row"/>, so a value holding a tab or a newline cannot shift the
/// fields after it.
/// <para>
/// Handles are numbers a tap issues, stable for as long as the element lives, with zero meaning no
/// element. A line or column of zero means the position is not known; an empty file means the element
/// was not declared in markup the tap can name.
/// </para>
/// </summary>
#if ROSEMCP_TAP_PUBLIC
public
#else
internal
#endif
static class TapRows
{
	/// <summary>The first line of a <see cref="TapCommands.Properties"/> reply that found the element.</summary>
	public const string PropertiesOk = "ok";

	/// <summary>The whole <see cref="TapCommands.Properties"/> reply when the handle names no element the tap can read.</summary>
	public const string PropertiesError = "error";

	/// <summary>The writable field of a <see cref="Property"/> row for a property that can be changed live.</summary>
	public const string Writable = "1";

	/// <summary>
	/// One element of a <see cref="TapCommands.Tree"/> reply: its handle, its parent's (zero at a root),
	/// its index among the parent's children, its type, its name, where it was declared, and its address.
	/// <para>
	/// The address is <c>#name</c> for a named element, or <c>Type[index]</c> segments joined by
	/// <c>/</c> from the nearest named ancestor, each index counting earlier siblings of the same short
	/// type name. It has to count exactly as RoseMCP's markup diff counts, or an edit lands on the
	/// element next door.
	/// </para>
	/// </summary>
	public static string Tree(ulong handle, ulong parent, int childIndex, string type, string name, string file, int line, int column, string address) =>
		TapWire.Row(
			Number(handle),
			Number(parent),
			Number(childIndex),
			type,
			name,
			file,
			Number(line),
			Number(column),
			address);

	/// <summary>The element a <see cref="TapCommands.Properties"/> reply describes: its type and where it was declared.</summary>
	public static string Element(string type, string file, int line, int column) =>
		TapWire.Row("E", type, file, Number(line), Number(column));

	/// <summary>
	/// One property of a <see cref="TapCommands.Properties"/> reply.
	/// <para>
	/// <paramref name="provenance"/> is where the effective value came from, in the names the XAML taps
	/// use: <c>Local</c>, <c>Style</c>, <c>Animation</c>, <c>Inherited</c>, <c>Default</c>,
	/// <c>ParentTemplate</c> and the rest. <paramref name="file"/>, <paramref name="line"/> and
	/// <paramref name="column"/> are where that value was set, where the tap knows.
	/// </para>
	/// <para>
	/// <paramref name="writable"/> is <see cref="Writable"/>, or the reason the property cannot be changed
	/// live, from a tap that declares <see cref="TapCommands.PropertiesWritable"/>; a tap that does not
	/// passes null and the field is left off.
	/// </para>
	/// </summary>
	public static string Property(
		string name,
		string? value,
		string valueType,
		string declaringType,
		string provenance,
		string file,
		int line,
		int column,
		bool valueUnavailable,
		string? writable)
	{
		string[] fields =
		[
			"P",
			name,
			value ?? string.Empty,
			valueType,
			declaringType,
			provenance,
			file,
			Number(line),
			Number(column),
			value is null ? "1" : "0",
			valueUnavailable ? "1" : "0",
		];

		return writable is null ? TapWire.Row(fields) : TapWire.Row([.. fields, writable]);
	}

	/// <summary>
	/// One command of an <see cref="TapCommands.Apply"/> batch, read by position: op, target, property,
	/// value type, value, arg, index. Null for a line that is not a command.
	/// </summary>
	public static TapApplyCommand? ReadCommand(string line)
	{
		if (line.Length == 0) return null;

		var fields = TapWire.Fields(line);
		if (fields.Length < 2) return null;

		return new TapApplyCommand(
			FieldAt(fields, 0),
			FieldAt(fields, 1),
			FieldAt(fields, 2),
			FieldAt(fields, 3),
			FieldAt(fields, 4),
			FieldAt(fields, 5),
			int.TryParse(FieldAt(fields, 6), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var index) ? index : 0);
	}

	/// <summary>
	/// The result of one <see cref="TapCommands.Apply"/> command: the command's op, target, property and
	/// arg, which are what the host finds the result again by, then the status (<c>applied</c>, or a
	/// sentence saying why not), the handle it landed on and what the property said before.
	/// </summary>
	public static string ApplyResult(TapApplyCommand command, string status, ulong handle, string old) =>
		TapWire.Row(command.Op, command.Target, command.Property, status, command.Arg, Number(handle), old);

	/// <summary>The reply to <see cref="TapCommands.Select"/>: the overlay armed, at the extent it was arranged at.</summary>
	public static string Armed(int width, int height) => TapWire.Row("armed", Number(width), Number(height));

	/// <summary>
	/// The first row of a <see cref="TapCommands.Selection"/> reply: the overlay's mode (<c>idle</c>,
	/// <c>select</c> or <c>rulers</c>), whether it prefers the app's own markup, why the last pick went
	/// away where it did, and whether changes are marked.
	/// </summary>
	public static string SelectionHeader(string mode, bool justMyMarkup, string gone, bool changeMarks) =>
		TapWire.Row(mode, Flag(justMyMarkup), gone, Flag(changeMarks));

	/// <summary>One element under the pick, topmost first: handle, type, name, and whether its type is the framework's own.</summary>
	public static string Candidate(ulong handle, string type, string name, bool isFrameworkType) =>
		TapWire.Row(Number(handle), type, name, Flag(isFrameworkType));

	/// <summary>
	/// The reply to <see cref="TapCommands.Ask"/> when there is a press: its sequence number, counted
	/// across the app's life; its kind; a readable sentence; the element and second element it names;
	/// a colour; and its age in milliseconds, which is what tells a fresh press from one made while no
	/// host was connected.
	/// </summary>
	public static string Ask(long sequence, string kind, string text, ulong element, ulong secondElement, string colour, long ageMilliseconds) =>
		TapWire.Row(
			"ask",
			Number(sequence),
			kind,
			text,
			Number(element),
			Number(secondElement),
			colour,
			Number(ageMilliseconds));

	private static string FieldAt(string[] fields, int index) => index < fields.Length ? fields[index] : string.Empty;

	private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

	private static string Number(ulong value) => value.ToString(CultureInfo.InvariantCulture);

	private static string Flag(bool value) => value ? "1" : "0";
}

/// <summary>One command of an <see cref="TapCommands.Apply"/> batch, as <see cref="TapRows.ReadCommand"/> reads it.</summary>
/// <param name="Op">What to do: an edit kind, or <see cref="TapCommands.CreateInstance"/>.</param>
/// <param name="Target">The address of the element it acts on, or a slot name an earlier command in the batch created.</param>
/// <param name="Property">The property it sets or clears, or the type <see cref="TapCommands.CreateInstance"/> creates.</param>
/// <param name="ValueType">The type the value is written in, where the markup says.</param>
/// <param name="Value">The value, as markup text.</param>
/// <param name="Arg">The child an <see cref="TapCommands.AddChild"/> inserts, or the resource key a <see cref="TapCommands.SetResource"/> replaces.</param>
/// <param name="Index">Where among the target's children an <see cref="TapCommands.AddChild"/> inserts.</param>
#if ROSEMCP_TAP_PUBLIC
public
#else
internal
#endif
sealed record TapApplyCommand(string Op, string Target, string Property, string ValueType, string Value, string Arg, int Index);
