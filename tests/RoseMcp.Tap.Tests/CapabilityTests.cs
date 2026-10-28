using RoseMcp.Tap.Protocol;

namespace RoseMcp.Tap.Tests;

/// <summary>
/// The capabilities line and the greeting it follows. The two rules are what let a tap and a host built
/// from different copies of the contract keep working together: what is missing is not served, and what
/// is unknown is ignored.
/// </summary>
public sealed class CapabilityTests
{
	private const string ContosoLine =
		"tree properties properties.writable apply:SetProperty,ClearProperty,AddChild,RemoveChild select:pick,rulers selecthandle deselect selection ask detach";

	[Test]
	public void Commands_modes_and_finer_capabilities_are_read_from_one_line()
	{
		var capabilities = TapCapabilities.Parse(ContosoLine);

		capabilities.Serves(TapCommands.Tree).ShouldBeTrue();
		capabilities.Serves(TapCommands.PropertiesWritable).ShouldBeTrue();
		capabilities.Serves(TapCommands.Select, TapCommands.SelectRulers).ShouldBeTrue();
		capabilities.Serves(TapCommands.Apply, TapCommands.SetProperty).ShouldBeTrue();
	}

	[Test]
	public void What_is_missing_is_not_served()
	{
		var capabilities = TapCapabilities.Parse(ContosoLine);

		capabilities.Serves(TapCommands.Changes).ShouldBeFalse();
		capabilities.Serves(TapCommands.Apply, TapCommands.SetResource).ShouldBeFalse();
		capabilities.Serves(TapCommands.Idle, "anything").ShouldBeFalse();
	}

	[Test]
	public void A_command_named_without_a_list_serves_none_of_its_items()
	{
		var capabilities = TapCapabilities.Parse("apply select");

		capabilities.Serves(TapCommands.Apply).ShouldBeTrue();
		capabilities.Serves(TapCommands.Apply, TapCommands.SetProperty).ShouldBeFalse();
		capabilities.Serves(TapCommands.Select, TapCommands.SelectPick).ShouldBeFalse();
	}

	/// <summary>A name from a newer contract is read as one more name, and nothing about the rest of the line changes.</summary>
	[Test]
	public void An_unknown_name_is_carried_and_changes_nothing_else()
	{
		var capabilities = TapCapabilities.Parse("tree magnifier.frame:scene properties");

		capabilities.Serves(TapCommands.Tree).ShouldBeTrue();
		capabilities.Serves(TapCommands.Properties).ShouldBeTrue();
		capabilities.Serves("magnifier.frame", "scene").ShouldBeTrue();
	}

	[Test]
	public void The_line_written_is_the_line_read()
	{
		var written = TapCapabilities.Parse(ContosoLine).ToString();

		TapCapabilities.Parse(written).ToString().ShouldBe(written);
		TapCapabilities.Of("tree", "select:rulers,pick").ToString().ShouldBe("select:pick,rulers tree");
	}

	[Test]
	public void A_greeting_carries_its_version_key_and_capabilities()
	{
		var greeting = TapGreeting.Write("a1b2", TapCapabilities.Parse(ContosoLine));

		TapGreeting.TryRead(greeting, out var version, out var key, out var capabilities).ShouldBeTrue();
		version.ShouldBe(TapGreeting.ProtocolVersion);
		key.ShouldBe("a1b2");
		capabilities.Serves(TapCommands.Select, TapCommands.SelectPick).ShouldBeTrue();
	}

	[Test]
	public void A_greeting_without_a_capabilities_line_declares_nothing()
	{
		TapGreeting.TryRead("RoseTap/4/key", out var version, out _, out var capabilities).ShouldBeTrue();

		version.ShouldBe(4);
		capabilities.Names.ShouldBeEmpty();
	}

	[Test]
	[Arguments("hello from the provider")]
	[Arguments("RoseTap/four/key")]
	[Arguments("")]
	public void Something_that_is_not_a_greeting_is_not_read_as_one(string message)
	{
		TapGreeting.TryRead(message, out _, out _, out _).ShouldBeFalse();
	}
}
