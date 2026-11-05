# Library header contract

This fixture extracts the current LibraryPage header methods at build time and runs them with value-only WinUI boundaries. It checks that only Emby/Jellyfin Resume keeps the compact title and 28 DIP gap; FnNas, Plex, ordinary libraries, favorites, upcoming, folders and search retain their earlier layout rules. It also covers page-kind restoration, initialization guards and the audio colon resource.

Build this project in Release, then run it with --no-build. Seven AgreementsTests overlap the main suite and must not be counted twice. Real layout and screenshots are covered by the offline inspect entry InspectHeaderPolish, with 1132/760/600 DIP widths and a deliberate 128 DIP gap that must fail the assertion. Source-linked tests do not prove rendered layout. No real accounts, server writes or playback are used.
