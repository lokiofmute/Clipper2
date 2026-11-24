# Running C# Tests

Requires the [.NET SDK](https://dotnet.microsoft.com/download) 10.0 (the library targets net8.0, the tests net10.0).

```bash
dotnet test CSharp/Clipper2.sln -c Release
```

| Project | What it covers |
|---------|----------------|
| `Clipper2Lib.Tests` | `Shared/*.cs` against the plain library |
| `Clipper2Lib.Tests.Z` | `Shared/*.cs` and `Shared/Z/*.cs` against the `USINGZ` library |

Test names equal the C++ test names in `CPP/Tests`, and the data in the repository's `Tests/*.txt` is shared with the C++ suite (`TestData.Path` finds it).
