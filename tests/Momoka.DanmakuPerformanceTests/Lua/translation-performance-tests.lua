local perf_checks = fixture_legacy_checks or {}
local samples = {}
local function check(name, action)
    local ok, failure = pcall(action)
    perf_checks[#perf_checks + 1] = {name=name,passed=ok,detail=ok and '' or tostring(failure)}
end
local function require_true(value, message) if not value then error(message or 'assertion failed') end end
local function restart_cue()
    fixture_properties = {pause=true,['sub-text']='Original line',sid=2,['sub-visibility']=true,['sub-start']=1,['sub-end']=4}
    fixture_canvas = {1280,720}
    fixture_fire('start-file'); fixture_fire('file-loaded')
end
local function show_value(text, original, start, font)
    fixture_call('momoka-subtitle-translation-show', utils.format_json({
        original=original or 'Original line',text=text or '实时翻译字幕',sid='2',start=start or 1,['end']=4,fontSize=font or 32,position=80}))
end
local function sample(state)
    fixture_reset_counts(); fixture_advance(60)
    local value = {state=state,seconds=60,property_reads=fixture_counts.reads,json_serializations=fixture_counts.json,
        overlay_updates=fixture_counts.updates,cue_publishes=fixture_counts.sets,ass_string_formats=fixture_counts.formats}
    samples[#samples + 1] = value
    return value
end
check('PERF-15: late load-script initializes cue and polling without a replayed file-loaded event', function()
    local initial = fixture_initial_state
    require_true(initial and initial.loaded and initial.timer_enabled and initial.ready == 1)
    require_true(type(initial.cue) == 'table' and initial.cue.text == 'Original line' and initial.cue.sid == '2')
end)
check('PERF-15: 60s paused-static JSON / ASS / overlay operation counts', function()
    restart_cue(); show_value()
    local value = sample('paused static cue with active translation')
    if fixture_baseline then
        require_true(value.json_serializations == 1200 and value.overlay_updates == 1200, 'baseline changed')
    else
        require_true(value.json_serializations == 0 and value.overlay_updates == 0 and value.ass_string_formats == 0, 'static work repeated')
        require_true(value.property_reads > 0, 'loaded fallback was disabled; not a zero-cost design')
        require_true(value.cue_publishes == 0 and not overlay.removed)
    end
end)
check('PERF-15: 60s cleared-translation operation counts and cue detection', function()
    clear()
    local value = sample('translation cleared; same paused cue')
    if fixture_baseline then require_true(value.json_serializations == 1200, 'baseline changed')
    else require_true(value.json_serializations == 0 and value.overlay_updates == 0 and value.property_reads > 0) end
end)
check('PERF-15: EOF removes translation/cue: 60s idle operation counts', function()
    show_value(); fixture_fire('end-file')
    require_true(overlay.removed and overlay.data == '' and fixture_properties['user-data/momoka-subtitle-translation/cue'] == '')
    local value = sample('end-file; no active translation')
    if fixture_baseline then require_true(value.property_reads == 2400, 'baseline changed')
    else require_true(value.property_reads == 0 and value.json_serializations == 0 and value.overlay_updates == 0) end
end)
if not fixture_baseline then
    check('PERF-15: file-loaded resumes fallback; new subtitle works after EOF or clear without a show-trigger timer', function()
        fixture_properties['sub-text'] = 'Successor line'
        fixture_properties['sub-start'] = 10
        fixture_fire('start-file'); fixture_fire('file-loaded')
        require_true(timer.enabled, 'new file timer stayed killed')
        -- Deliberately omit property observers: 20Hz fallback remains available.
        fixture_properties['sub-text'], fixture_properties['sub-start'] = 'Silent observer line', 20
        fixture_advance(0.06)
        local cue = fixture_properties['user-data/momoka-subtitle-translation/cue']
        require_true(type(cue) == 'table' and cue.text == 'Silent observer line' and cue.start == 20, 'fallback lost next cue')
        show_value('新字幕译文', 'Silent observer line', 20)
        require_true(overlay.data:find('新字幕译文',1,true))
        clear(); fixture_properties['sub-text'], fixture_properties['sub-start'] = 'After clear', 21
        fixture_observers['sub-text']()
        show_value('清除后恢复', 'After clear', 21)
        require_true(overlay.data:find('清除后恢复',1,true) and timer.enabled)
    end)
    check('PERF-15: same text different timestamps/track/visibility promptly invalidates old reply', function()
        for _, field in ipairs({'sub-start','sub-end','sid','sub-visibility'}) do
            restart_cue(); show_value()
            if field == 'sub-start' then fixture_properties[field] = 10
            elseif field == 'sub-end' then fixture_properties[field] = 20
            elseif field == 'sid' then fixture_properties[field] = 3
            else fixture_properties[field] = false end
            fixture_observers[field]()
            require_true(overlay.removed and overlay.data == '', field .. ' left translation visible')
            show_value(); require_true(overlay.data == '', field .. ' accepted stale reply')
        end
    end)
    check('PERF-15: same reply/style/canvas does not resubmit; changed text/style/canvas updates exactly once', function()
        restart_cue(); show_value(); fixture_reset_counts()
        show_value(); render(); fixture_observers['osd-dimensions']()
        require_true(fixture_counts.updates == 0, 'identical reply resubmitted')
        show_value('changed'); require_true(fixture_counts.updates == 1)
        show_value('changed', nil, nil, 40); require_true(fixture_counts.updates == 2)
        fixture_canvas = {1920,800}; fixture_observers['osd-dimensions']()
        require_true(fixture_counts.updates == 3 and overlay.res_x == 1728)
        fixture_observers['osd-dimensions'](); fixture_advance(0.2)
        require_true(fixture_counts.updates == 3, 'stable new canvas repeated')
        clear(); show_value('changed', nil, nil, 40)
        require_true(fixture_counts.updates == 4 and not overlay.removed, 'dedupe blocked restore after remove')
    end)
    check('PERF-15: seek/start-file/shutdown clear ownership and stale timer cannot republish idle cue', function()
        restart_cue(); show_value(); fixture_fire('seek')
        require_true(overlay.removed and overlay.data == '' and timer.enabled, 'seek disabled next subtitle')
        show_value(); fixture_fire('start-file')
        require_true(not timer.enabled and overlay.removed)
        timer.callback()
        require_true(fixture_properties['user-data/momoka-subtitle-translation/cue'] == '', 'old timer republished old file')
        fixture_fire('file-loaded'); show_value(); fixture_fire('shutdown')
        require_true(not timer.enabled and overlay.removed and overlay.data == '')
    end)
end
if fixture_inject_failure then check('harness injected failure is not a pass', function() error('intentional failing assertion') end) end
fixture_native_command('script-message', 'perf-fixture-result', utils.format_json({
    completed=true,checks=perf_checks,samples=samples,runtime=jit and jit.version or _VERSION}))
