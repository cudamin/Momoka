namespace Momoka.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (MpvProcessTests.IsChild(args)) return MpvProcessTests.RunChildAsync(args).GetAwaiter().GetResult();
        if (args is ["selfcheck-mutex-fixture", var source, var target])
            return SelfCheckTests.RunMutexChild(source, target);
        if (args is ["test-harness-fixture", var scenario])
            return TestHarnessTests.RunChild(scenario);
        if (args is ["activation-listener-fixture", var activationScenario])
            return ActivationListenerTests.RunChild(activationScenario);
        if (args is ["settings-page-state-fixture", var shellAssembly, var sourceRoot])
            return SettingsPageStateTests.Run(shellAssembly, sourceRoot);
        if (args is ["settings-applicability-fixture", var applicabilityShell, var applicabilityRoot])
            return SettingsApplicabilityTests.Run(applicabilityShell, applicabilityRoot);
        if (args is ["library-resume-paging-fixture", var resumeShell, var resumeRoot])
            return LibraryResumePagingTests.Run(resumeShell, resumeRoot);
        if (args is ["library-favorite-source-fixture", var favoriteShell, var favoriteRoot])
            return LibraryFavoriteSourceTests.Run(favoriteShell, favoriteRoot);
        if (args is ["settings-screenshot-restore-fixture", var screenshotShell, var screenshotRoot])
            return SettingsScreenshotRestoreTests.Run(screenshotShell, screenshotRoot);
        if (args is ["fnnas-library-editor-fixture", var editorShell, var editorRoot])
            return FnNasLibraryEditorTests.Run(editorShell, editorRoot);
        if (args is ["fnnas-user-state-fixture", var stateShell, var stateRoot])
            return FnNasUserStateWriteTests.Run(stateShell, stateRoot);
        if (args is ["subtitle-forced-only-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            SubtitleForcedOnlyTests.Register();
            return TestHarness.Run();
        }
        if (args is ["player-volume-gesture-fixture", var volumeShell, var volumeRoot])
            return PlayerVolumeGestureTests.Run(volumeShell, volumeRoot);
        if (args is ["audio-output-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            AudioOutputTests.Register();
            return TestHarness.Run();
        }
        if (args is ["selfcheck-output-isolation-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            SelfCheckTests.Register();
            SelfCheckOutputIsolationTests.Register();
            return TestHarness.Run();
        }
        if (args is ["http-download-error-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            HttpDownloadErrorTests.Register();
            HttpDeadlineTests.Register();
            HttpDiagnosticsTests.Register();
            return TestHarness.Run();
        }
        if (args is ["moviepilot-transfer-submission-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            MoviePilotTransferSubmissionTests.Register();
            return TestHarness.Run();
        }
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("Momoka Core 测试");
        Console.WriteLine();

        TestHarnessTests.Register();
        ActivationListenerTests.Register();
        PlaybackTests.Register();
        KeywordPriorityTests.Register();
        PlaybackLifecycleTests.Register();
        MediaVersionDiscoveryTests.Register();
        BackendLifecycleTests.Register();
        MpvCustomConfigTests.Register();
        AudioOutputTests.Register();
        VideoOutputTests.Register();
        ShaderTransactionTests.Register();
        PlaybackTrackStateTests.Register();
        SubtitleFileTests.Register();
        SubtitleBehaviorTests.Register();
        SubtitleForcedOnlyTests.Register();
        SubtitleManagementTests.Register();
        MpvProcessTests.Register();
        SelfCheckTests.Register();
        SelfCheckOutputIsolationTests.Register();
        SettingsTests.Register();
        StartupEntryTests.Register();
        NotificationTests.Register();
        ServerSettingsTests.Register();
        ServerAccessTests.Register();
        ShortcutTests.Register();
        SeekKeyTests.Register();
        ServiceTests.Register();
        FontTests.Register();
        HtmlColorTests.Register();
        SubtitlePreviewTests.Register();
        EmbyTests.Register();
        EmbyRealtimeTests.Register();
        FnNasProtocolTests.Register();
        FnNasLibraryInputTests.Register();
        FnNasUserStateWriteTests.Register();
        FnNasMediaTests.Register();
        PlexProtocolTests.Register();
        PlexMediaTests.Register();
        JellyfinWebConsoleTests.Register();
        SessionTests.Register();
        IdentityRegressionTests.Register();
        HttpRedirectTests.Register();
        HttpDiagnosticsTests.Register();
        FailurePathTests.Register();
        ImageCacheTests.Register();
        ImageIdentityTests.Register();
        BrowsingPreferencesTests.Register();
        LibrarySearchTests.Register();
        AggregateQueryTests.Register();
        LibraryBrowseTests.Register();
        LibraryRecommendedTests.Register();
        LibraryFavoritesTests.Register();
        LibraryUpcomingTests.Register();
        LibraryDisplayTests.Register();
        LibraryEpisodeFolderTests.Register();
        LibraryIndexTests.Register();
        MoviePilotSearchSitesTests.Register();
        MoviePilotResourceOptionsTests.Register();
        MoviePilotAvailabilityTests.Register();
        MoviePilotMediaDetailTests.Register();
        ItemDetailTests.Register();
        ItemArtworkTests.Register();
        ItemMenuTests.Register();
        ItemIdentifyTests.Register();
        ItemActionTests.Register();
        HomeCarouselTests.Register();
        HomeFoldTests.Register();
        BackdropBlurTests.Register();
        HomeLayoutTests.Register();
        HomeLatestTests.Register();
        ResumeRowTests.Register();
        UiLifecycleTests.Register();
        StartIntentTests.Register();
        DiagnosticsTests.Register();
        ScreenTests.Register();
        CardStripTests.Register();
        CardSizeTests.Register();
        WrapLayoutTests.Register();
        StartupArgsTests.Register();
        ThemeTests.Register();
        PlayerPaletteTests.Register();
        PlayerMotionTests.Register();
        PlayerInteractionTests.Register();
        CoverArtworkTests.Register();
        PictureRevealTests.Register();
        StatusCoalescerTests.Register();
        ResizeFreezeTests.Register();
        VolumeScaleTests.Register();
        TimelineScaleTests.Register();
        TimelineParityTests.Register();
        SpeedWheelTests.Register();
        MpvUiTests.Register();
        MediaVersionTests.Register();
        ReleaseGroupTests.Register();
        InlineSwitchTests.Register();
        WindowFormTests.Register();
        PinIndicatorTests.Register();
        AboutTests.Register();
        UpdateTests.Register();
        MoviePilotTests.Register();
        AniBtTests.Register();
        AniBtIntegrationTests.Register();
        MoviePilotSubscriptionTests.Register();
        MoviePilotAuditTests.Register();
        MoviePilotTransferTests.Register();
        MoviePilotTransferSubmissionTests.Register();
        MoviePilotVersionQueryTests.Register();
        MoviePilotEpisodePatternTests.Register();
        AgreementsTests.Register();

        return TestHarness.Run();
    }
}
