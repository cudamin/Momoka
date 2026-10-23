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
        SubtitleManagementTests.Register();
        MpvProcessTests.Register();
        SelfCheckTests.Register();
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
        MoviePilotVersionQueryTests.Register();
        MoviePilotEpisodePatternTests.Register();
        AgreementsTests.Register();

        return TestHarness.Run();
    }
}
