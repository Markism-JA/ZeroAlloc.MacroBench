# Security Policy

## Reporting a Vulnerability

ZeroAlloc.MacroBench uses `unsafe` code, unmanaged memory allocations (`NativeMemory.AlignedAlloc`), and OS process/thread APIs. Memory-safety bugs or native handle leaks should not be reported in public GitHub issues.

Please report potential security or memory-safety vulnerabilities privately via GitHub Private Vulnerability Reporting:

- Go to the repository's Security tab -> Advisories -> Report a vulnerability.

When reporting, please include:
- Affected version (`ZeroAlloc.MacroBench` version / commit)
- OS and CPU architecture (e.g., Windows x64, Linux ARM64, macOS ARM64)
- A minimal reproduction or description of the memory-safety impact

You will receive an acknowledgment as soon as possible, and a fix will be coordinated before public disclosure.
