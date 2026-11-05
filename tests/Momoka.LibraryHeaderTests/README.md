# Library header contract

This fixture compiles the current `LibraryPage.ApplyHeaderLayout` and its search margin method verbatim at every build, and compile-links the entire production `AlphaBar.xaml.cs`. Small value-only WinUI boundaries record property assignments; they do not simulate XAML layout. The tests cover resume pages without a parent library, each non-search header shape, folder breadcrumb ownership, search restoration, Jellyfin-only column routing, initialization guards, and the actual AlphaBar height callback across full/compact/hidden/restored states with the selected letter retained. A markup contract checks that the index's Auto column has no fixed-width floor. Auto-column measured zero width and recovery remain real-window probe assertions.

From this worktree, after restore:

```powershell
& "$HOME/.dotnet/dotnet.exe" build tests/Momoka.LibraryHeaderTests/Momoka.LibraryHeaderTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project tests/Momoka.LibraryHeaderTests/Momoka.LibraryHeaderTests.csproj -c Release --no-build --no-restore
```

Actual gap measurements, wrapped rows, index non-overlap and height/count hiding and restoration are covered separately by `--probe-shell --probe-section library,search --screen 1`. This fixture starts no window, reads no user settings and performs no server requests or playback. Passing it does not establish rendered layout or actual Auto-column collapse.

The project also runs the existing seven `AgreementsTests` contracts without starting the full main runner. Removing the ineffective height VisualStateGroup intentionally retires only its four state/group names; the real `Rail`, `Root` and `Letters` elements remain. These seven cases overlap the main suite and must not be counted twice.
