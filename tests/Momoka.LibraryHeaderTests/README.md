# Library header contract

This fixture compiles the current `LibraryPage.ApplyHeaderLayout` and its search margin method verbatim at every build. Small value-only WinUI boundaries record property assignments; they do not simulate XAML layout. The tests cover resume pages without a parent library, each non-search header shape, folder breadcrumb ownership, search restoration, Jellyfin-only column routing, and initialization guards. A markup contract checks that the index's Auto column has no fixed-width floor and that the real control hides its internal rail.

From this worktree, after restore:

```powershell
& "$HOME/.dotnet/dotnet.exe" build tests/Momoka.LibraryHeaderTests/Momoka.LibraryHeaderTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project tests/Momoka.LibraryHeaderTests/Momoka.LibraryHeaderTests.csproj -c Release --no-build --no-restore
```

Actual gap measurements, wrapped rows, index non-overlap and height/count hiding and restoration are covered separately by `--probe-shell --probe-section library,search --screen 1`. This fixture starts no window, reads no user settings and performs no server requests or playback. Passing it does not establish rendered layout or actual Auto-column collapse.
