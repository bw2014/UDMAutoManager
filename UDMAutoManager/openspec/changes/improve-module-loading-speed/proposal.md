## Why

`UDMAutoManager.Program.main()` is registered with `[FISCA.MainMethod(StartupPriority.LastAsynchronized)]`, a priority whose name signals to the FISCA host that this module's work should be detached from the host's startup sequence. In practice, `main()` still runs a synchronous database round-trip (`QueryHelper.Select("select url from _udm")`) directly on the thread the module loader calls it on, *before* any of the per-URL update-check work is handed off. `ServerModule.AutoManaged(url)` is a small, fire-and-forget-shaped call (it schedules a continuation task), but the blocking query and the sequential `foreach` that invokes it for every row still execute inline. If the host's module loader invokes `LastAsynchronized` modules' `main()` methods one after another rather than in parallel, this inline DB call becomes pure serial overhead added to every application startup, and it grows with the number of rows in `_udm` and the number of other modules following the same pattern (25 sibling modules exist under `Desktop/Modules`). There is also no error isolation: an exception from the query or from any single `AutoManaged(url)` call is not caught locally, so one bad URL/row can abort processing of the rest.

## What Changes

- Move the `_udm` query and the per-URL `AutoManaged` dispatch off the module loader's calling thread (e.g. `Task.Run`) so `main()` returns to the loader immediately instead of blocking on a synchronous DB round-trip.
- Dispatch `AutoManaged(url)` for each row without waiting on prior rows to finish (e.g. `Parallel.ForEach` or a fire-and-forget loop), instead of the current sequential `foreach`.
- Add local exception handling around the query and around each per-URL call so a single failure (bad URL, DB hiccup) does not abort processing of the remaining rows and does not throw back into the host's module loader.
- No change to business behavior: which URLs get processed and what `AutoManaged` does per URL stays the same.

## Capabilities

### New Capabilities
- `module-startup-performance`: Defines how `UDMAutoManager`'s startup entry point (`main()`) must behave with respect to the host's module loading — non-blocking handoff, concurrent per-URL processing, and per-item error isolation — so it does not add serial latency to application startup.

### Modified Capabilities
(none — no existing specs in this repo)

## Impact

- Affected code: `Program.cs` (`Program.main()`), the sole source file in this module.
- No changes to `UDMAutoManager.csproj`, external dependencies, or the `FISCA`/`FISCA.Data` reference assemblies (out of scope — they are prebuilt host-framework binaries, not part of this repo).
- No database schema or `_udm` table changes.
- Behavior visible to operators: UDM auto-update checks for multiple URLs now run concurrently instead of one-at-a-time, and a failure on one URL/row no longer prevents the others from being checked.
