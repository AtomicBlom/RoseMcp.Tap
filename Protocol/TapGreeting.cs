using System.Globalization;

namespace RoseMcp.Tap.Protocol;

/// <summary>
/// The first message a tap sends on the host's pipe: <c>RoseTap/{version}/{key}</c>, then a newline and
/// the line of <see cref="TapCapabilities"/> it serves.
/// <para>
/// The version says which shape every row has, so a host can refuse a tap whose rows it would misread
/// rather than report them as data. The key is the one the host handed the tap in its startup data, and
/// presenting it is what proves the tap on the pipe is the one this session loaded rather than whatever
/// reached the pipe first.
/// </para>
/// <para>
/// The version moves only when a row an existing command answers with changes shape. A tap that can do
/// something new says so with a new capability name, which an older host ignores and a newer one reads.
/// </para>
/// </summary>
#if ROSEMCP_TAP_PUBLIC
public
#else
internal
#endif
static class TapGreeting
{
	/// <summary>The version of the protocol this copy of the contract writes and reads.</summary>
	public const int ProtocolVersion = 5;

	/// <summary>How every greeting starts, so one that does not can be told from one with the wrong version.</summary>
	public const string Prefix = "RoseTap/";

	/// <summary>The greeting a tap sends, presenting <paramref name="key"/> and declaring <paramref name="capabilities"/>.</summary>
	public static string Write(string key, TapCapabilities capabilities) =>
		Prefix + ProtocolVersion.ToString(CultureInfo.InvariantCulture) + "/" + key + "\n" + capabilities;

	/// <summary>
	/// The version, key and capabilities a greeting carries, or false when its first line is not a
	/// greeting that names a version. A greeting with no capabilities line declares
	/// <see cref="TapCapabilities.None"/>.
	/// </summary>
	public static bool TryRead(string message, out int version, out string key, out TapCapabilities capabilities)
	{
		version = 0;
		key = string.Empty;
		capabilities = TapCapabilities.None;

		var lines = message.Split('\n');
		var first = lines[0].TrimEnd('\r');
		if (!first.StartsWith(Prefix, StringComparison.Ordinal)) return false;

		var rest = first[Prefix.Length..];
		var slash = rest.IndexOf('/', StringComparison.Ordinal);
		var versionText = slash < 0 ? rest : rest[..slash];
		if (!int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out version)) return false;

		key = slash < 0 ? string.Empty : rest[(slash + 1)..];
		if (lines.Length > 1) capabilities = TapCapabilities.Parse(lines[1].TrimEnd('\r'));

		return true;
	}
}
