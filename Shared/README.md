# AcceptanceTestsShared

## Overview

AcceptanceTestsShared is a shared library for the APSIM AcceptanceTests System, providing common enums and models used across both the API and web application.

## Building

From the repository root:

```bash
dotnet build Shared/Shared.csproj
```

## Contributing

When modifying shared models or enums:

1. Ensure changes are backward compatible where possible
2. Update both AcceptanceTestsWebAPI and AcceptanceTestsWebApp to use new types
3. Run tests to verify no breaking changes

## Support

For issues or questions, contact the APSIM team.
