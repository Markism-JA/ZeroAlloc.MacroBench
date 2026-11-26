## Summary

<!-- Describe what this PR changes and why. Link the related issue if applicable. -->

## Checklist

- [ ] Built cleanly with zero warnings (`dotnet build ZeroAlloc.MacroBench.slnx --no-incremental -c Release`, including `CS1591` XML docs)
- [ ] All tests pass in both `Release` and `Debug` (`dotnet test ZeroAlloc.MacroBench.slnx`)
- [ ] Verified hot-path recording/pacing changes introduce zero managed allocations
- [ ] Added/updated unit or concurrency tests for behavior, `unsafe`, or generator changes
