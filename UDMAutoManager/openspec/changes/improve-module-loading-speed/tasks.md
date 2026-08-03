## 1. Implementation

- [x] 1.1 Wrap the body of `Program.main()` (the `_udm` query and row processing) in `Task.Factory.StartNew(...)` so the method returns to the module loader immediately, preserving the existing `RTContext.IsDiagMode` early-return check outside the task. (`Task.Run` is unavailable under this project's `TargetFrameworkVersion=v4.0`; `Task.Factory.StartNew` is the .NET 4.0-compatible equivalent.)
- [x] 1.2 Replace the sequential `foreach (DataRow row in ...)` loop with `Parallel.ForEach` so `ServerModule.AutoManaged(url)` is dispatched concurrently across rows.
- [x] 1.3 Wrap the `QueryHelper.Select("select url from _udm")` call in a local `try/catch` that reports the exception via `Trace.TraceError(ex.ToString())` and prevents the failure from escaping the module's startup path. (`ServerModule.OutputException` is `internal`, not callable from this module — confirmed via reflection.)
- [x] 1.4 Wrap each row's `AutoManaged(url)` call in a local `try/catch` (inside the `Parallel.ForEach` body) that reports the exception via `Trace.TraceError(ex.ToString())` without stopping processing of the remaining rows.

## 2. Verification

- [x] 2.1 Build the project (`msbuild UDMAutoManager.csproj` or via IDE) and confirm no compile errors from the `Task.Run` / `Parallel.ForEach` changes.
- [x] 2.2 Manually trace through the updated `main()` against each scenario in `specs/module-startup-performance/spec.md` (non-blocking handoff, diag-mode short-circuit, concurrent dispatch, per-row error isolation, query failure isolation, behavioral equivalence of URLs passed) and confirm the code satisfies each one.
- [x] 2.3 Confirm no change to the set of URLs passed to `AutoManaged` compared to the previous implementation (same query, same rows, one call per URL).
