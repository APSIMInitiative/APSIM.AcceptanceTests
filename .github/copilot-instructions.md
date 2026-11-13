# Copilot instructions

These instructions apply to C# changes throughout this repository. Follow the
existing project structure and behavior unless the task explicitly calls for a
change.

## Repository structure

- `AcceptanceTestsWebAPI\` is the ASP.NET Core minimal API. Keep HTTP endpoint
  handling under `Requests\`, application setup under `Configuration\`,
  persistence types and EF Core configuration under `Data\`, and background or
  integration work under `Services\`.
- `AcceptanceTestsWebApp\` is the interactive Blazor Server application. Keep
  UI behavior in its Razor components and related application code in this
  project.
- `Shared\` contains types shared between the applications, such as models and
  enums. Avoid introducing web- or persistence-specific dependencies here.
- `Tests\` is the xUnit test project. Organize test files in folders named
  after the production projects they exercise, for example
  `Tests\AcceptanceTestsWebAPI\`.

All current projects target .NET 10. Nullable reference types and implicit
usings are enabled; retain those settings and keep changes compatible with the
target framework.

## C# style

- Use four spaces for indentation, braces on their own lines, and file-scoped
  namespaces. Keep one primary type per file and name the file after that type,
  following the surrounding project structure.
- Namespaces must follow the solutions folder structure. 
- Use `PascalCase` for types, methods, properties, and constants;
  `camelCase` for parameters and local variables; and prefix interfaces with
  `I`.
- Prefer clear, descriptive names and small methods with one responsibility.
  Use an explicit type when it makes the code easier to understand.
- Follow established C# patterns in the surrounding code, including records
  for simple data contracts and primary constructors where they improve
  clarity. Do not refactor working code just to adopt newer syntax.
- Keep comments focused on intent, constraints, or non-obvious decisions.
  Document public APIs with XML comments when their purpose or contract is not
  self-evident.
- Do not add a formatter, analyzer, or new style configuration solely for a
  local change.
- Lines must not exceed 120 characters.

## Correctness and application behavior

- Treat nullable warnings as useful feedback. Model optional values as
  nullable, check values at boundaries, and avoid null-forgiving operators
  unless required by a framework pattern (for example, an EF Core navigation
  property populated by the ORM).
- Use `async`/`await` for asynchronous I/O. Pass a `CancellationToken` through
  request, database, and background operations when the API provides one;
  avoid blocking asynchronous work with `.Result` or `.Wait()`.
- Validate external input before using it in database operations, file paths,
  or cloud-storage requests. Return an appropriate HTTP result for invalid or
  missing input; do not turn errors into success-shaped responses.
- Use structured logging with useful context. Do not log secrets or include
  credentials in source, settings, tests, or documentation; use the existing
  configuration and environment-variable mechanisms.
- Keep persistence and application state changes explicit. For EF Core,
  express queries in the database, use no-tracking queries for read-only
  access where appropriate, and use transactions or conditional updates when
  multiple writes must be atomic or concurrent requests could conflict.
- Preserve the run and job lifecycle guarantees: duplicate job completion
  notifications must remain idempotent, stale runs must not update a newer
  run, and fetch work must only be claimed or completed by the worker holding
  a valid lease.
- Use UTC for new persisted timestamps and name UTC fields accordingly. When
  changing an existing timestamp contract, preserve compatibility or update
  all dependent code and tests deliberately.
- Keep EF Core migrations and the model snapshot in sync. Create migrations
  with the repository's EF Core tooling rather than hand-editing generated
  migration or snapshot code.

## Tests and verification

- Add or update focused xUnit tests for changed behavior, especially input
  validation, failure handling, concurrency, retries, and state transitions
  when relevant. Prefer assertions against observable results and persisted
  state over implementation details.
- Keep tests isolated and deterministic. Use temporary resources where needed
  and clean them up even when an assertion fails. Do not depend on live Azure
  services or credentials in unit tests; use test doubles for external
  services.
- From the repository root, build and test with:

  ```sh
  dotnet build AcceptanceTests.sln
  dotnet test AcceptanceTests.sln
  ```

- Run the narrowest relevant tests while iterating, then run the solution
  build and tests when practical. Report any validation that could not be run
  and why.
