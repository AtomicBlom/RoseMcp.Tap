# RoseMcp.Tap

The contract between [RoseMCP](https://github.com/AtomicBlom/RoseMCP)'s live-app host and a **tap**: an assembly loaded into a running app that answers RoseMCP's UI tools (`rose_xaml_tree`, `rose_xaml_properties`, `rose_xaml_apply`, picking, rulers and the rest) about that app's element tree.

Both sides compile it in as source. It holds only what both compile, and nothing that knows RoseMCP's host or any framework's types.

| Folder | Holds | Compiled into |
|---|---|---|
| `Protocol/` | Escaping, framing, the greeting and capabilities, command names, reply-row writers, the endpoint's request format, and `RoseMcpTapAttribute` | RoseMCP's host, every tap, and every endpoint |
| `Endpoint/` | The pipe listener and assembly loader a framework hosts so a tap can be loaded into a running app | A framework or app that hosts the endpoint, alongside `Protocol/` |

## Taking it in

Add this repository as a git submodule and link the files into one of your own projects:

```xml
<ItemGroup>
  <Compile Include="external/RoseMcp.Tap/Protocol/*.cs" Link="RoseMcp.Tap/Protocol/%(Filename)%(Extension)" />
  <Compile Include="external/RoseMcp.Tap/Endpoint/*.cs" Link="RoseMcp.Tap/Endpoint/%(Filename)%(Extension)" />
</ItemGroup>
```

Every type is `internal`, so two assemblies that each compile a copy never clash. Define `ROSEMCP_TAP_PUBLIC` to make the `Protocol/` types public instead, for a project whose own dependents use them.

The endpoint loads code by name, so it is annotated `RequiresUnreferencedCode` and `RequiresDynamicCode`. A framework that stays NativeAOT-clean starts it behind a feature switch marked `[FeatureGuard]` for both, which keeps its analyzers quiet and lets the trimmer remove the endpoint wherever the switch is off. `tests/AotCheck` compiles the contract with the AOT analyzers on to keep those annotations complete.

## A tap

A tap is one managed assembly marked with the attribute RoseMCP reads from its metadata, without loading it:

```csharp
[assembly: RoseMcpTap(Name = "Contoso", Module = "Contoso.dll", Endpoint = "contoso-diag-{pid}", EntryType = "Contoso.RoseTap.Tap", Protocol = 5)]
```

The entry type has a public static `Start(string startupData)` and a public static `Stop()`. The startup data is `workDir|pipeName|key`. The tap connects to the host's pipe and greets it with `TapGreeting.Write(key, capabilities)`. It then answers each command in `TapCommands` with the rows in `TapRows`.

## Versions

- **The tap protocol** (`TapGreeting.ProtocolVersion`) moves only when a row an existing command answers with changes shape. A tap that can do something new declares a new capability name instead. A host treats a missing name as not served and ignores a name it does not know.
- **The endpoint protocol** (`EndpointWire.Version`) is separate. The endpoint is compiled into apps and changes only when they take a new copy, so its changes are additions only.

## Tests

```
dotnet run --project tests/RoseMcp.Tap.Tests
```

The endpoint tests load a real stub tap over a real pipe. Named pipes are Unix domain sockets off Windows, so the tests run on Linux too.

## Licence

MIT. See `LICENSE`.
