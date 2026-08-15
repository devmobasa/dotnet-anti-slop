# Bounded API slice

This sample demonstrates:

- route-level cancellation;
- no-tracking entity reads followed by DTO projection;
- deterministic keyset pagination and a server-side maximum page size;
- no request fire-and-forget task;
- a bounded channel and hosted worker for deferred work;
- a fresh asynchronous DI scope per queued command;
- structured error logging.

It is a source example rather than a standalone project; copy it into an
ASP.NET Core project and provide the normal EF Core/provider registrations.
