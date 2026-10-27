using RoseMcp.Tap.Protocol;

namespace RoseMcp.Tap.Tests;

/// <summary>
/// The reply rows a tap writes, held to the field positions RoseMCP's host reads them by. A field written
/// one place to the left is read as the next field's value, with no error at either end.
/// </summary>
public sealed class RowTests
{
	[Test]
	public void A_tree_row_puts_each_field_where_the_host_reads_it()
	{
		var fields = TapWire.Fields(TapRows.Tree(7, 3, 2, "SUIsei.Controls.Button", "Ok", "Views/Main.xaml", 12, 5, "#Panel/Button[1]"));

		fields.ShouldBe(["7", "3", "2", "SUIsei.Controls.Button", "Ok", "Views/Main.xaml", "12", "5", "#Panel/Button[1]"]);
	}

	[Test]
	public void A_property_row_flags_a_null_value_and_leaves_the_writable_field_off_unless_asked()
	{
		var fields = TapWire.Fields(TapRows.Property("Tag", null, "Object", "SUIsei.FrameworkElement", "Default", string.Empty, 0, 0, false, null));

		fields.Length.ShouldBe(11);
		fields[0].ShouldBe("P");
		fields[2].ShouldBe(string.Empty);
		fields[9].ShouldBe("1");
		fields[10].ShouldBe("0");
	}

	[Test]
	public void A_property_row_from_a_tap_that_declares_writable_carries_the_reason_last()
	{
		var fields = TapWire.Fields(TapRows.Property(
			"Text", "a\tb", "String", "SUIsei.Controls.TextBlock", "Local", "Main.xaml", 4, 9, false, "set by a compiled {x:Bind}"));

		fields.Length.ShouldBe(12);
		fields[2].ShouldBe("a\tb");
		fields[5].ShouldBe("Local");
		fields[11].ShouldBe("set by a compiled {x:Bind}");
	}

	[Test]
	public void An_apply_result_echoes_the_fields_its_command_is_found_again_by()
	{
		var command = TapRows.ReadCommand(TapWire.Row("AddChild", "#Panel", string.Empty, string.Empty, string.Empty, "slot:1", "2"))!;

		command.Op.ShouldBe(TapCommands.AddChild);
		command.Arg.ShouldBe("slot:1");
		command.Index.ShouldBe(2);

		TapWire.Fields(TapRows.ApplyResult(command, "applied", 19, string.Empty))
			.ShouldBe(["AddChild", "#Panel", string.Empty, "applied", "slot:1", "19", string.Empty]);
	}

	[Test]
	public void The_selection_and_ask_rows_put_each_field_where_the_host_reads_it()
	{
		TapWire.Fields(TapRows.SelectionHeader("rulers", true, string.Empty, false)).ShouldBe(["rulers", "1", string.Empty, "0"]);
		TapWire.Fields(TapRows.Candidate(5, "Border", "Frame", true)).ShouldBe(["5", "Border", "Frame", "1"]);
		TapWire.Fields(TapRows.Armed(1280, 720)).ShouldBe(["armed", "1280", "720"]);
		TapWire.Fields(TapRows.Ask(3, "element", "Button 'Ok'", 5, 0, "#ff0000", 120))
			.ShouldBe(["ask", "3", "element", "Button 'Ok'", "5", "0", "#ff0000", "120"]);
	}

	[Test]
	public void An_endpoint_exchange_reads_back_as_it_was_written()
	{
		EndpointWire.TryReadGreeting(EndpointWire.Greeting("SUIsei", "0.1.0"), out var version, out var framework, out var frameworkVersion).ShouldBeTrue();
		version.ShouldBe(EndpointWire.Version);
		framework.ShouldBe("SUIsei");
		frameworkVersion.ShouldBe("0.1.0");

		EndpointWire.TryReadLoadRequest(EndpointWire.LoadRequest(@"C:\taps\SUIsei.RoseTap.dll", "SUIsei.RoseTap.Tap", @"C:\work|pipe|key"), out var path, out var entry, out var data)
			.ShouldBeTrue();
		path.ShouldBe(@"C:\taps\SUIsei.RoseTap.dll");
		entry.ShouldBe("SUIsei.RoseTap.Tap");
		data.ShouldBe(@"C:\work|pipe|key");

		EndpointWire.ReadReply(EndpointWire.LoadedReply("SUIsei", "0.1.0")).ShouldBe((true, "0.1.0"));
		EndpointWire.ReadReply(EndpointWire.RefusedReply("no such type")).ShouldBe((false, "no such type"));
		EndpointWire.PipeName("suisei-diag-{pid}", 4120).ShouldBe("suisei-diag-4120");
	}
}
