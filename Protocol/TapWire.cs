using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace RoseMcp.Tap.Protocol;

/// <summary>
/// How a field, a row and a message are written between a RoseMCP live-app host and a tap, in both
/// directions: how a field is escaped, how fields make a row, how a reply is matched to the request it
/// answers, and how a message is framed on the pipe.
/// <para>
/// One contract rather than one per direction, because the protocol is delimited by tabs and newlines
/// and a property value can hold either. Escaped on the way out and unescaped on the way in by one side
/// only, a tab in a value shifts every field after it and a newline splits one command into two -- and
/// since a result is found again by the fields it was sent with, an edit that landed then reports that
/// it was never applied.
/// </para>
/// <para>
/// The native XAML taps implement the same contract in C++, in RoseMCP's <c>tap_channel.h</c>, and
/// RoseMCP's tests hold that file's escape table and framing to this one.
/// </para>
/// </summary>
#if ROSEMCP_TAP_PUBLIC
public
#else
internal
#endif
static class TapWire
{
	/// <summary>
	/// The largest message either side sends or accepts. A length past it is not a message: it is a
	/// stream that has lost its place, or something that is not a tap writing to the pipe.
	/// </summary>
	public const int MaxMessageBytes = 64 * 1024 * 1024;

	/// <summary>
	/// A field made safe to sit in a row: every tab, carriage return, newline and backslash written as a
	/// backslash and a letter, so the only raw tabs in a row separate its fields and the only raw newlines
	/// in a message separate its rows.
	/// </summary>
	public static string Escape(string field)
	{
		if (field.AsSpan().IndexOfAny("\t\r\n\\") < 0) return field;

		var builder = new StringBuilder(field.Length + 8);
		foreach (var character in field)
		{
			switch (character)
			{
				case '\t': builder.Append(@"\t"); break;
				case '\r': builder.Append(@"\r"); break;
				case '\n': builder.Append(@"\n"); break;
				case '\\': builder.Append(@"\\"); break;
				default: builder.Append(character); break;
			}
		}

		return builder.ToString();
	}

	/// <summary>
	/// A field as it was before <see cref="Escape"/>. A backslash before any other character stands for
	/// that character, and one at the very end is kept, so a field that was never escaped comes through
	/// unless it holds a backslash.
	/// </summary>
	public static string Unescape(string field)
	{
		if (field.IndexOf('\\', StringComparison.Ordinal) < 0) return field;

		var builder = new StringBuilder(field.Length);
		for (var i = 0; i < field.Length; i++)
		{
			if (field[i] == '\\' && i + 1 < field.Length)
			{
				var next = field[++i];
				builder.Append(next switch
				{
					't' => '\t',
					'r' => '\r',
					'n' => '\n',
					_ => next,
				});
			}
			else
			{
				builder.Append(field[i]);
			}
		}

		return builder.ToString();
	}

	/// <summary>Fields escaped and joined into one row, which is how every command and every result travels.</summary>
	public static string Row(params string[] fields)
	{
		var escaped = new string[fields.Length];
		for (var i = 0; i < fields.Length; i++) escaped[i] = Escape(fields[i]);

		return string.Join('\t', escaped);
	}

	/// <summary>A row taken apart into the fields it was made from.</summary>
	public static string[] Fields(string row)
	{
		var fields = row.Split('\t');
		for (var i = 0; i < fields.Length; i++) fields[i] = Unescape(fields[i]);

		return fields;
	}

	/// <summary>
	/// A message body carrying the id of the request it is, or the one it answers: the id, a newline, then
	/// the body. A tap serves on its app's UI thread, which nothing on the host's side can cancel, so a
	/// request the host has given up on is still answered -- later, and ahead of the reply to whatever the
	/// host asked next. The id is what says whose reply a message is.
	/// </summary>
	public static string Frame(uint id, string body) => id.ToString(CultureInfo.InvariantCulture) + "\n" + body;

	/// <summary>The id and body of a message made by <see cref="Frame"/>, or false when it was not made that way.</summary>
	public static bool TryReadFrame(string frame, out uint id, out string body)
	{
		var newline = frame.IndexOf('\n', StringComparison.Ordinal);
		if (newline > 0 && uint.TryParse(frame.AsSpan(0, newline), NumberStyles.None, CultureInfo.InvariantCulture, out id))
		{
			body = frame[(newline + 1)..];
			return true;
		}

		id = 0;
		body = string.Empty;
		return false;
	}

	/// <summary>
	/// Writes one message: its length as four little-endian bytes, then that many bytes of UTF-8. One
	/// framing for every message in both directions, so no message ever needs an encoding decision of its
	/// own.
	/// </summary>
	public static void WriteMessage(Stream stream, string message)
	{
		var payload = Encoding.UTF8.GetBytes(message);
		var header = new byte[4];
		BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);

		stream.Write(header, 0, header.Length);
		stream.Write(payload, 0, payload.Length);
		stream.Flush();
	}

	/// <summary>The asynchronous form of <see cref="WriteMessage"/>.</summary>
	public static async Task WriteMessageAsync(Stream stream, string message, CancellationToken cancellation)
	{
		var payload = Encoding.UTF8.GetBytes(message);
		var header = new byte[4];
		BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);

		await stream.WriteAsync(header, cancellation).ConfigureAwait(false);
		await stream.WriteAsync(payload, cancellation).ConfigureAwait(false);
		await stream.FlushAsync(cancellation).ConfigureAwait(false);
	}

	/// <summary>
	/// Reads one message, or returns null when the other end has gone or sent something that is not a
	/// message: a zero-length read is the far end closing, and a length outside the bound is a stream
	/// that has lost its place.
	/// </summary>
	public static string? ReadMessage(Stream stream)
	{
		var header = new byte[4];
		if (!ReadExactly(stream, header)) return null;

		var length = BinaryPrimitives.ReadInt32LittleEndian(header);
		if (length is < 0 or > MaxMessageBytes) return null;

		var payload = new byte[length];
		return ReadExactly(stream, payload) ? Encoding.UTF8.GetString(payload) : null;
	}

	/// <summary>The asynchronous form of <see cref="ReadMessage"/>.</summary>
	public static async Task<string?> ReadMessageAsync(Stream stream, CancellationToken cancellation)
	{
		var header = new byte[4];
		if (!await ReadExactlyAsync(stream, header, cancellation).ConfigureAwait(false)) return null;

		var length = BinaryPrimitives.ReadInt32LittleEndian(header);
		if (length is < 0 or > MaxMessageBytes) return null;

		var payload = new byte[length];
		return await ReadExactlyAsync(stream, payload, cancellation).ConfigureAwait(false) ? Encoding.UTF8.GetString(payload) : null;
	}

	private static bool ReadExactly(Stream stream, byte[] buffer)
	{
		var read = 0;
		while (read < buffer.Length)
		{
			int got;
			try
			{
				got = stream.Read(buffer, read, buffer.Length - read);
			}
			catch (Exception exception) when (exception is IOException or ObjectDisposedException)
			{
				return false;
			}

			if (got <= 0) return false;

			read += got;
		}

		return true;
	}

	private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellation)
	{
		var read = 0;
		while (read < buffer.Length)
		{
			int got;
			try
			{
				got = await stream.ReadAsync(buffer.AsMemory(read), cancellation).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
			{
				return false;
			}

			if (got <= 0) return false;

			read += got;
		}

		return true;
	}
}
