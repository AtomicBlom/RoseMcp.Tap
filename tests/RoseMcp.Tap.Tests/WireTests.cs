using RoseMcp.Tap.Protocol;

namespace RoseMcp.Tap.Tests;

/// <summary>
/// The parts of the contract every message rides on: escaping, rows, request ids and framing. A host
/// and a tap built from different copies of this have to agree on every one of them, or a value holding
/// a tab shifts the fields after it and an edit that landed is reported as never applied.
/// </summary>
public sealed class WireTests
{
	[Test]
	[Arguments("")]
	[Arguments("plain")]
	[Arguments("one\ttwo")]
	[Arguments("one\r\ntwo")]
	[Arguments(@"C:\path\to\file.xaml")]
	[Arguments(@"ends in a backslash\")]
	[Arguments("all of them: \t \n \r \\ and after")]
	public void A_field_holds_no_delimiter_once_escaped_and_comes_back_as_it_was(string value)
	{
		var escaped = TapWire.Escape(value);

		escaped.ShouldNotContain('\t');
		escaped.ShouldNotContain('\n');
		escaped.ShouldNotContain('\r');
		TapWire.Unescape(escaped).ShouldBe(value);
	}

	[Test]
	public void A_row_comes_back_as_the_fields_it_was_made_from()
	{
		string[] fields = ["SetProperty", "#Slot/TextBlock[0]", "Text", "String", "one\ttwo\nthree\\four", string.Empty, "0"];

		TapWire.Fields(TapWire.Row(fields)).ShouldBe(fields);
	}

	[Test]
	public void A_frame_carries_its_id_and_its_body_whole()
	{
		TapWire.TryReadFrame(TapWire.Frame(42, "tree\nsecond line"), out var id, out var body).ShouldBeTrue();

		id.ShouldBe(42u);
		body.ShouldBe("tree\nsecond line");
	}

	[Test]
	public void Something_not_made_as_a_frame_is_not_read_as_one()
	{
		TapWire.TryReadFrame("RoseTap/5/key", out _, out _).ShouldBeFalse();
	}

	[Test]
	public async Task Messages_come_back_whole_and_in_order_across_a_stream()
	{
		using var stream = new MemoryStream();
		TapWire.WriteMessage(stream, "first");
		await TapWire.WriteMessageAsync(stream, "second — with UTF-8", CancellationToken.None);
		TapWire.WriteMessage(stream, string.Empty);

		stream.Position = 0;
		TapWire.ReadMessage(stream).ShouldBe("first");
		(await TapWire.ReadMessageAsync(stream, CancellationToken.None)).ShouldBe("second — with UTF-8");
		TapWire.ReadMessage(stream).ShouldBe(string.Empty);
		TapWire.ReadMessage(stream).ShouldBeNull();
	}

	/// <summary>A length past the bound is a stream that has lost its place, and is read as the end rather than allocated.</summary>
	[Test]
	public void A_length_past_the_bound_is_not_a_message()
	{
		using var stream = new MemoryStream([0xFF, 0xFF, 0xFF, 0x7F, 1, 2, 3]);

		TapWire.ReadMessage(stream).ShouldBeNull();
	}
}
