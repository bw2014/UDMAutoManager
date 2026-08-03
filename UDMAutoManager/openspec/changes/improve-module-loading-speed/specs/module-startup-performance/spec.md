## ADDED Requirements

### Requirement: Non-blocking handoff to the module loader
`UDMAutoManager.Program.main()` SHALL return control to the FISCA module loader without waiting on the `_udm` lookup query or on any `ServerModule.AutoManaged` call to complete.

#### Scenario: main() returns before update checks finish
- **WHEN** the FISCA host invokes `Program.main()` at application startup and the `_udm` table contains one or more rows
- **THEN** `main()` returns to the caller without blocking on the database query or on any in-flight `AutoManaged(url)` call

#### Scenario: Diagnostic mode still short-circuits synchronously
- **WHEN** `RTContext.IsDiagMode` is true
- **THEN** `main()` performs no query and no `AutoManaged` calls, and returns immediately, matching current behavior

### Requirement: Concurrent per-URL processing
The system SHALL dispatch `ServerModule.AutoManaged(url)` for each row returned by the `_udm` query concurrently rather than waiting for each prior call to finish before starting the next.

#### Scenario: Multiple UDM URLs are processed concurrently
- **WHEN** the `_udm` query returns more than one row
- **THEN** `AutoManaged` is invoked for all returned URLs without one URL's processing being required to complete before the next URL's processing starts

### Requirement: Per-row error isolation
A failure while querying `_udm` or while processing any single row's `AutoManaged(url)` call SHALL NOT prevent processing of the other rows, and SHALL NOT propagate as an unhandled exception out of the module's startup path.

#### Scenario: One row throws, others still process
- **WHEN** the `_udm` query returns multiple rows and `AutoManaged(url)` throws an exception for one of them
- **THEN** the remaining rows are still processed, and the exception is reported (not silently discarded and not thrown back into the loader)

#### Scenario: Query itself fails
- **WHEN** `QueryHelper.Select("select url from _udm")` throws an exception
- **THEN** the exception is caught and reported without crashing the module's startup invocation

### Requirement: Behavioral equivalence with prior implementation
For a given set of `_udm` rows, the set of URLs passed to `ServerModule.AutoManaged` SHALL be identical to what the previous synchronous, sequential implementation would have produced.

#### Scenario: Same URLs, same calls
- **WHEN** the `_udm` table contains a given set of URL rows
- **THEN** each URL is passed to `ServerModule.AutoManaged` exactly once, regardless of the concurrency model used to dispatch the calls
