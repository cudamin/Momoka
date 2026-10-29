-- These checks run after the unchanged legacy functional tests, in the same actual script scope.
local perf_checks = fixture_legacy_checks or {}
local samples = {}
local function check(name, action)
    local ok, failure = pcall(action)
    perf_checks[#perf_checks + 1] = {name = name, passed = ok, detail = ok and '' or tostring(failure)}
end
local function require_true(value, message) if not value then error(message or 'assertion failed') end end
local actual_rebuild, rebuild_count, rebuild_times = rebuild, 0, {}
rebuild = function(...)
    rebuild_count = rebuild_count + 1
    local started = os.clock()
    local result = actual_rebuild(...)
    rebuild_times[#rebuild_times + 1] = (os.clock() - started) * 1000
    return result
end
local actual_text_width, width_reads = text_width, 0
text_width = function(...) width_reads = width_reads + 1; return actual_text_width(...) end
local actual_escape, escape_reads = escape, 0
escape = function(...) escape_reads = escape_reads + 1; return actual_escape(...) end
local function restart_at_canvas(w, h)
    fixture_properties = {pause = false, ['time-pos'] = 60}
    fixture_canvas = {w or 1280, h or 720}
    fixture_fire('start-file'); fixture_fire('file-loaded')
end
local function load_count(count, id, font, speed, delay)
    local file, load = tostring(serial), tostring(id or 1)
    fixture_call('momoka-danmaku-begin', file, load, utils.format_json({show=true,fontSize=font or 36,speed=speed or 1,delay=delay or 0,area=50,density=40,opacity=70}))
    for offset = 0, count - 1, 128 do
        local batch = {}
        for index = offset + 1, math.min(offset + 128, count) do
            batch[#batch + 1] = {time=(index-1)*1200/count,mode=1,color=16777215,text='合成弹幕' .. tostring(index)}
        end
        fixture_call('momoka-danmaku-chunk', file, load, utils.format_json(batch))
    end
    fixture_call('momoka-danmaku-commit', file, load)
end
local function reset_measurement() rebuild_count, rebuild_times, width_reads, escape_reads = 0, {}, 0, 0 end
local function sum(values) local result = 0; for _, value in ipairs(values) do result = result + value end; return result end
local function median(values)
    if #values == 0 then return 0 end
    local sorted = {}; for _, value in ipairs(values) do sorted[#sorted + 1] = value end
    table.sort(sorted); return sorted[math.ceil(#sorted / 2)]
end
local function signature()
    local values = {}
    for _, item in ipairs(layout) do
        values[#values + 1] = {item.start,item.duration,item.width,item.mode,item.y,item.text,item.color}
    end
    return utils.format_json(values)
end
check('PERF-07: 40000 comments / ten continuous resizes: immediate canvas and measured work counts', function()
    restart_at_canvas(); load_count(40000)
    reset_measurement()
    for index = 1, 10 do render() end
    local stable = rebuild_count
    reset_measurement()
    local callbacks = {}
    for index = 1, 10 do
        fixture_canvas[1] = 1280 + 4 * index
        local started = os.clock()
        fixture_observers['osd-dimensions']()
        callbacks[#callbacks + 1] = (os.clock() - started) * 1000
        require_true(math.abs(overlay.res_x - fixture_canvas[1]) < 0.01, 'canvas waited for lane rebuild')
        fixture_advance(0.02)
    end
    local before_settle = rebuild_count
    fixture_advance(0.2)
    local changed = rebuild_count
    samples[#samples + 1] = {scenario='40000 comments / 10 aspect changes at 20ms intervals',
        accepted_comments=#comments,layout_entries=#layout,aspect_changes=10,stable_rebuilds=stable,
        rebuilds_before_settle=before_settle,aspect_rebuilds=changed,
        observer_total_cpu_ms=sum(callbacks),observer_median_cpu_ms=median(callbacks),
        rebuild_total_cpu_ms=sum(rebuild_times),rebuild_median_cpu_ms=median(rebuild_times),
        measurement_scope='Synthetic CPU time, not native frame/GPU latency'}
    require_true(stable == 0, 'stable dimensions rebuilt')
    if fixture_baseline then require_true(changed == 10, 'baseline no longer reproduces immediate rebuilds')
    else require_true(before_settle == 0 and changed == 1, 'ten rapid resizes were not coalesced') end
end)
if not fixture_baseline then
    check('PERF-07: settled result equals direct rebuild; stable render and paused resize do not lose planning', function()
        local expected = signature()
        actual_rebuild()
        require_true(signature() == expected, 'reused working entries changed lane planning')
        reset_measurement()
        fixture_advance(0.5)
        require_true(rebuild_count == 0, 'stable timer rebuilt')
        fixture_properties.pause = true; sync_timer()
        require_true(not timer.enabled, 'pause resumed animation ticker')
        fixture_canvas[1] = 1700; fixture_observers['osd-dimensions']()
        require_true(rebuild_count == 0 and pending_resize ~= nil)
        fixture_advance(0.16)
        require_true(rebuild_count == 1 and width == 1700 and not timer.enabled, 'paused canvas never settled')
    end)
    check('PERF-07: sustained resize is bounded to one rebuild per 500ms batch, then final settle', function()
        restart_at_canvas(); load_count(10000)
        reset_measurement()
        for index = 1, 24 do
            fixture_canvas[1] = 1300 + index * 5; fixture_observers['osd-dimensions']()
            fixture_advance(0.05)
        end
        local before_final = rebuild_count
        fixture_advance(0.2)
        require_true(before_final >= 2 and before_final <= 3, 'maximum wait was unbounded or rebuilt every resize')
        require_true(rebuild_count <= 3 and width == 1420, 'final dimensions not retained')
    end)
    check('PERF-07: backward/forward seek renders immediately and flushes at most pending resize', function()
        restart_at_canvas(); load_count(1000)
        reset_measurement()
        fixture_properties.pause = true; sync_timer()
        fixture_properties['time-pos'] = 0; fixture_fire('seek')
        require_true(rebuild_count == 0 and overlay.data:find('合成弹幕1', 1, true), 'ordinary seek scanned whole film')
        fixture_properties['time-pos'] = 80; fixture_fire('seek')
        require_true(rebuild_count == 0 and not overlay.data:find('合成弹幕1}', 1, true))
        fixture_canvas[1] = 1400; fixture_observers['osd-dimensions']()
        local queued = resize_timer
        fixture_properties['time-pos'] = 0; fixture_fire('seek')
        require_true(rebuild_count == 1 and pending_resize == nil and width == 1400, 'seek did not flush pending dimensions')
        queued.callback(); fixture_advance(0.3)
        require_true(rebuild_count == 1, 'cancelled seek timer rebuilt a second time')
    end)
    check('PERF-07: returning to committed width cancels resize; physical same-aspect/invalid sizes do not rebuild', function()
        restart_at_canvas(); load_count(1000)
        reset_measurement()
        fixture_canvas[1] = 1500; fixture_observers['osd-dimensions']()
        local queued = resize_timer
        fixture_canvas = {1280,720}; fixture_observers['osd-dimensions']()
        require_true(pending_resize == nil, 'return-to-base left a rebuild pending')
        queued.callback(); fixture_advance(0.3)
        fixture_canvas = {1920,1080}; fixture_observers['osd-dimensions'](); fixture_advance(0.2)
        for _, canvas in ipairs({{0,720},{1280,0},{math.huge,720},{1280,0/0}}) do
            fixture_canvas = canvas; fixture_observers['osd-dimensions']()
        end
        require_true(rebuild_count == 0, 'same aspect or invalid canvas rebuilt')
    end)
    check('PERF-07: start-file, end-file, clear and new config discard already-dispatched resize ownership', function()
        for _, boundary in ipairs({'start-file','end-file','clear','new-config','successor-file'}) do
            restart_at_canvas(); load_count(100)
            reset_measurement()
            fixture_canvas[1] = 1500; fixture_observers['osd-dimensions']()
            local queued, old_file = resize_timer, serial
            if boundary == 'clear' then fixture_call('momoka-danmaku-clear', tostring(serial), '2')
            elseif boundary == 'new-config' then load_count(200, 2, 48, 2, 20)
            elseif boundary == 'successor-file' then fixture_fire('start-file'); fixture_fire('file-loaded'); load_count(200, 1, 48, 2, 20)
            else fixture_fire(boundary) end
            local after_boundary = rebuild_count
            queued.callback(); fixture_advance(0.3)
            require_true(rebuild_count == after_boundary, boundary .. ' accepted old resize callback')
            if boundary == 'new-config' or boundary == 'successor-file' then
                require_true(#comments == 200 and options.fontSize == 48 and width == 1500, 'config waited for resize')
                require_true(layout[1].start == 20 and layout[1].duration == 4, 'delay/speed kept old schedule')
            else require_true(#comments == 0 and #layout == 0 and overlay.removed, boundary .. ' left old data') end
            fixture_call('momoka-danmaku-chunk', tostring(old_file), '1', utils.format_json({{time=0,mode=1,color=1,text='old'}}))
            require_true(#comments == ((boundary == 'new-config' or boundary == 'successor-file') and 200 or 0), 'old transfer regained ownership')
        end
    end)
    check('PERF-07: settle resamples the latest paused canvas and cancels unavailable size without scanning', function()
        restart_at_canvas(); load_count(1000)
        fixture_properties.pause = true; sync_timer()
        reset_measurement()
        fixture_canvas[1] = 1400; fixture_observers['osd-dimensions']()
        -- No observer replay: the boundary must not commit a stale intermediate dimension.
        fixture_canvas[1] = 1600; fixture_advance(0.16)
        require_true(rebuild_count == 0 and pending_resize ~= nil, 'stale canvas was committed')
        fixture_advance(0.16)
        require_true(rebuild_count == 1 and width == 1600, 'latest canvas did not settle')
        fixture_canvas[1] = 1700; fixture_observers['osd-dimensions']()
        fixture_canvas[2] = 0; fixture_advance(0.16)
        require_true(rebuild_count == 1 and pending_resize == nil, 'unavailable canvas scanned whole film')
    end)
    check('PERF-07: text width/escape caches reuse accepted text, never untrusted derived payload fields', function()
        restart_at_canvas()
        fixture_call('momoka-danmaku-begin', tostring(serial), '1', utils.format_json({show=true}))
        fixture_call('momoka-danmaku-chunk', tostring(serial), '1', utils.format_json({
            {time=0,mode=1,color=16777215,text='{\\pos(0,0)}',measured_font=36,measured_width=-999,escaped_text='injected'}
        }))
        fixture_call('momoka-danmaku-commit', tostring(serial), '1')
        require_true(layout[1].width > 0 and not layout[1].text:find('injected', 1, true))
        require_true(layout[1].text:find('\\{', 1, true), 'escaped cache accepts raw directives')
        reset_measurement(); rebuild(); rebuild()
        require_true(width_reads == 0 and escape_reads == 0, 'same text/font recomputed derived values')
        options.fontSize = 48; rebuild()
        require_true(width_reads == 1 and escape_reads == 0 and comments[1].measured_font == 48, 'font change kept stale width')
    end)
end
if fixture_inject_failure then check('harness injected failure is not a pass', function() error('intentional failing assertion') end) end
fixture_native_command('script-message', 'perf-fixture-result', utils.format_json({
    completed=true,checks=perf_checks,samples=samples,runtime=jit and jit.version or _VERSION}))
