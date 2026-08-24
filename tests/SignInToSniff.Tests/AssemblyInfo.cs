using Xunit.Sdk;
using Xunit.v3;

// Proxy integration tests share the application's fixed localhost:8000 endpoint.
[assembly: Parallelization(Mode = ParallelMode.None)]
