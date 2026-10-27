using System.Text;

namespace RoseMcp.Tap.Protocol;

/// <summary>
/// What a tap says it can do, as it says it in the line after its greeting: names separated by spaces,
/// each a command (<c>tree</c>), a command with the items it serves (<c>select:pick,rulers</c>,
/// <c>apply:SetProperty,ClearProperty</c>), or a finer capability of one (<c>properties.writable</c>).
/// <para>
/// Three levels, because read-only means something different at each: a command a tap does not serve,
/// a mode or edit kind of one it does, and a single property it reports but cannot change. The host
/// refuses the first two without sending anything, naming the tap, and passes the third through so an
/// agent knows before it edits.
/// </para>
/// <para>
/// Two rules hold it together. A name that is missing is not served, and a name the reader does not
/// know is ignored, so a new capability is a new name rather than a protocol version: a tap built
/// against an older contract simply does not name it, and keeps working. An item is served only where
/// it is listed, so a command named without a list serves none of its items.
/// </para>
/// </summary>
#if ROSEMCP_TAP_PUBLIC
public
#else
internal
#endif
sealed class TapCapabilities
{
	private readonly Dictionary<string, HashSet<string>> _declared;

	private TapCapabilities(Dictionary<string, HashSet<string>> declared) => _declared = declared;

	/// <summary>A tap that declares nothing, which is what a greeting without a capabilities line is read as.</summary>
	public static TapCapabilities None { get; } = new(new Dictionary<string, HashSet<string>>(StringComparer.Ordinal));

	/// <summary>
	/// The capabilities a line declares. Never fails: a token this reader cannot make sense of is a name
	/// it does not know, and those are ignored by rule rather than refused.
	/// </summary>
	public static TapCapabilities Parse(string line)
	{
		var declared = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

		foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var colon = token.IndexOf(':', StringComparison.Ordinal);
			var name = colon < 0 ? token : token[..colon];
			if (name.Length == 0) continue;

			if (!declared.TryGetValue(name, out var items))
			{
				items = new HashSet<string>(StringComparer.Ordinal);
				declared[name] = items;
			}

			if (colon < 0) continue;

			foreach (var item in token[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				items.Add(item);
			}
		}

		return new TapCapabilities(declared);
	}

	/// <summary>Capabilities built in code, each argument a token as it would appear in the line.</summary>
	public static TapCapabilities Of(params string[] tokens) => Parse(string.Join(' ', tokens));

	/// <summary>Whether the tap serves <paramref name="name"/>: a command, or a finer capability such as <c>properties.writable</c>.</summary>
	public bool Serves(string name) => _declared.ContainsKey(name);

	/// <summary>Whether the tap serves <paramref name="item"/> of <paramref name="command"/>: a mode of <c>select</c>, an edit kind of <c>apply</c>.</summary>
	public bool Serves(string command, string item) => _declared.TryGetValue(command, out var items) && items.Contains(item);

	/// <summary>The items a command was declared with, in no particular order; empty where it was declared without any, or not at all.</summary>
	public IReadOnlyCollection<string> ItemsOf(string command) =>
		_declared.TryGetValue(command, out var items) ? items : [];

	/// <summary>Every name declared, in no particular order.</summary>
	public IReadOnlyCollection<string> Names => _declared.Keys;

	/// <summary>The line a tap sends: names in ordinal order, each command's items in ordinal order after a colon.</summary>
	public override string ToString()
	{
		var builder = new StringBuilder();
		foreach (var name in _declared.Keys.Order(StringComparer.Ordinal))
		{
			if (builder.Length > 0) builder.Append(' ');
			builder.Append(name);

			var items = _declared[name];
			if (items.Count == 0) continue;

			builder.Append(':').Append(string.Join(',', items.Order(StringComparer.Ordinal)));
		}

		return builder.ToString();
	}
}
