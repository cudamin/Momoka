-- Actual shipped mp.add_timeout / timer kill/resume; synthetic paused canvas, no native media.
local checks = {}
local started = fixture_native_get_time()
local rebuild_total, actual_rebuild = 0, rebuild
rebuild = function(...) rebuild_total = rebuild_total + 1; return actual_rebuild(...) end
local function check(name, action)
    local ok, failure = pcall(action)
    checks[#checks + 1] = {name=name,passed=ok,detail=ok and '' or tostring(failure)}
end
local function require_true(value, message) if not value then error(message or 'assertion failed') end end
local function load_synthetic()
    fixture_properties = {pause=true,['time-pos']=1}
    fixture_canvas = {1280,720}
    fixture_fire('start-file'); fixture_fire('file-loaded')
    fixture_call('momoka-danmaku-begin', tostring(serial),'1',utils.format_json({show=true}))
    fixture_call('momoka-danmaku-chunk', tostring(serial),'1',utils.format_json({{time=0,mode=1,color=16777215,text='合成弹幕'}}))
    fixture_call('momoka-danmaku-commit', tostring(serial),'1')
end
local function finish()
    if fixture_inject_failure then check('native-timer harness injected failure is not a pass', function() error('intentional failing assertion') end) end
    fixture_fire('end-file')
    fixture_native_command('script-message','perf-fixture-result',utils.format_json({
        completed=true,checks=checks,runtime=jit and jit.version or _VERSION,
        elapsed_seconds=fixture_native_get_time()-started,actual_timer_loop=true}))
end
check('PERF-07 actual Lua timer: paused resize queues exactly one one-shot, animation remains stopped', function()
    load_synthetic()
    require_true(not timer:is_enabled(), 'paused animation timer is active')
    rebuild_total = 0
    for index=1,10 do fixture_canvas[1]=1280+index*4; fixture_observers['osd-dimensions']() end
    require_true(rebuild_total == 0 and resize_timer:is_enabled(), 'resize was not deferred')
end)
fixture_native_add_timeout(0.30, function()
    check('PERF-07 actual Lua timer: settled resize runs once and follows final canvas while paused', function()
        require_true(rebuild_total == 1 and pending_resize == nil and width == 1320, 'real timeout failed to settle')
        require_true(not timer:is_enabled() and overlay.res_x == 1320)
    end)
    fixture_canvas[1]=1500; fixture_observers['osd-dimensions']()
    local old = resize_timer
    fixture_fire('start-file'); fixture_fire('file-loaded')
    local before = rebuild_total
    fixture_native_add_timeout(0.25, function()
        check('PERF-07 actual Lua timer: file boundary kills stale one-shot without touching successor', function()
            require_true(not old:is_enabled() and rebuild_total == before and #comments == 0 and overlay.removed)
        end)
        finish()
    end)
end)
