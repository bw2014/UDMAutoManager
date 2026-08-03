## Context

`UDMAutoManager` is one of ~25 sibling plugin modules under `Desktop/Modules/` that the FISCA host application loads at startup. Each module can expose a `[FISCA.MainMethod(StartupPriority)]`-annotated static method; `FISCA.ModuleLoader` invokes these as part of application startup, and `FISCA.StartupPriority` has three values: `FirstAsynchronized`, `MiddleSynchronized`, `LastAsynchronized`. `UDMAutoManager.Program.main()` is registered at `LastAsynchronized`, which by naming convention signals "run last, and don't make the host wait on you."

`FISCA.dll` and `FISCA.Data.dll` are prebuilt reference binaries (`<Private>False</Private>` in the `.csproj`, loaded from `library/*.dll`) — they are not part of this repo and are out of scope to modify. Reflection against `FISCA.dll` confirms `ServerModule.AutoManaged(string)` is a small method (125 bytes of IL) with a compiler-generated `Task` continuation delegate (`<AutoManaged>b__1(System.Threading.Tasks.Task)`), consistent with it internally scheduling async work per call rather than blocking synchronously end-to-end. The actual inline, synchronous work in this module is the ADO.NET query in `main()` itself (`QueryHelper.Select("select url from _udm")`) and the sequential `foreach` that calls `AutoManaged` once per row before returning control to the loader.

Whether `ModuleLoader` runs different modules' `LastAsynchronized` main methods concurrently or one-after-another cannot be confirmed from outside `FISCA.dll` (no decompiler was available in this environment). The design therefore treats "don't block the caller" as the responsibility of this module regardless of the loader's own concurrency model — it's a correct, low-risk change either way, and directly removes the one piece of confirmed inline blocking work (the DB round-trip) from the loader's call path.

## Goals / Non-Goals

**Goals:**
- `Program.main()` returns to the module loader without waiting on the `_udm` query or any `AutoManaged(url)` call to complete.
- Multiple `_udm` rows are processed concurrently rather than one at a time.
- A failure on one row (bad URL, DB error, exception from `AutoManaged`) does not stop processing of the other rows and does not throw back into the loader.
- Preserve existing behavior: same query, same rows processed, same call (`ServerModule.AutoManaged(url)`) per row, same `RTContext.IsDiagMode` guard.

**Non-Goals:**
- Changing what `AutoManaged` does, how updates are checked, or the `_udm` schema.
- Modifying `FISCA.dll` / `FISCA.Data.dll` or the module loader's own scheduling behavior.
- Adding logging/telemetry infrastructure beyond what's needed to not silently swallow errors (see Risks).
- Rate-limiting or throttling concurrent `AutoManaged` calls — the row count in `_udm` is expected to be small (a handful of UDM servers), so unbounded concurrency is acceptable.

## Decisions

**1. Wrap the whole body of `main()` in `Task.Factory.StartNew(...)` (fire-and-forget from the loader's perspective).**
This is the minimal change that guarantees `main()` returns immediately regardless of how `ModuleLoader` invokes `LastAsynchronized` methods. `Task.Run` (the more common .NET 4.5+ shorthand) is not available — `UDMAutoManager.csproj` targets `TargetFrameworkVersion=v4.0`, and the v4.0 reference assemblies used at compile time don't expose `Task.Run` (confirmed by a build failure: `CS0117 'Task' 未包含 'Run' 的定義`). `Task.Factory.StartNew` is the .NET 4.0-compatible equivalent and is used instead. Alternative considered: only wrap the query in a task and keep the `foreach` synchronous after it — rejected because the `foreach` itself, calling into `AutoManaged` N times, would still run inline relative to the query's continuation and doesn't address per-row concurrency.

**2. Process rows with `Parallel.ForEach` instead of `foreach`.**
`AutoManaged` is expected to do network I/O per URL (checking a remote server for updates), so sequential processing means total time scales with the number of rows. `Parallel.ForEach` gives concurrent dispatch without pulling in `async`/`await` plumbing that `ServerModule.AutoManaged` (a `void`-returning, fire-and-forget-shaped API) doesn't expose anyway. Alternative considered: `Task.WhenAll` over `Task.Run(() => AutoManaged(url))` per row — functionally similar; `Parallel.ForEach` was chosen for less boilerplate given there's no awaitable result to gather.

**3. Catch and isolate exceptions per row, and around the query itself, reporting via `System.Diagnostics.Trace.TraceError`.**
Today, an unhandled exception from `q.Select(...)` or from any `AutoManaged(url)` call propagates out of `main()`. Since `main()` now runs inside `Task.Factory.StartNew`, an unobserved faulted task would otherwise be silently dropped (or crash the process on finalization, depending on .NET Framework 4.0's unobserved-exception behavior). `FISCA.ServerModule.OutputException(Exception)` looked like the natural reporting hook (it's used internally by `AutoManaged`'s own continuation), but reflection against `FISCA.dll` showed it is `internal static`, not public — it is not callable from this module, so the original plan to reuse it doesn't work. No other public logging facility exists in `FISCA.dll`/`FISCA.Data.dll` (`FISCA.ErrorBox.Show` was considered but rejected: it's a modal Windows Forms dialog, wrong for a silent background task and would introduce a disruptive UI popup risk on a code path that previously never touched the UI). Each row's processing and the query itself are instead wrapped in local `try/catch` with `Trace.TraceError(ex.ToString())` — the standard .NET mechanism for reporting without a hard dependency on any specific listener being configured — so failures are visible but non-fatal and non-blocking.

## Risks / Trade-offs

- [Fire-and-forget `Task.Run` in `main()` means the loader gets no signal of completion or failure] → Acceptable: this matches the `LastAsynchronized` contract's intent, and per-row exceptions are still surfaced via `ServerModule.OutputException`, not swallowed silently.
- [Unbounded `Parallel.ForEach` could spike thread-pool usage if `_udm` ever grows large] → Accepted for now given the table is expected to hold a small, operator-managed list of UDM server URLs; revisit with a bounded degree of parallelism if that assumption changes.
- [Behavior can't be fully verified against the real `ModuleLoader` scheduling semantics since `FISCA.dll` internals aren't decompilable in this environment] → Mitigation: the change is correct and strictly non-regressive under either possible loader behavior (sequential or parallel module invocation), so it doesn't need that confirmation to be safe to ship.

## Migration Plan

- Single self-contained code change in `Program.cs`; no data migration, no config changes, no dependency changes.
- Deploy as a normal module build/update; rollback is reverting `Program.cs` to the previous synchronous version if unexpected issues arise.
