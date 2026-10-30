-- Actual 20Hz shipped timer. Only subtitle/canvas/overlay boundaries are synthetic.
local checks = {}
local started = fixture_native_get_time()
local function check(name, action)
    local ok, failure = pcall(action)
    checks[#checks + 1] = {name=name,passed=ok,detail=ok and '' or tostring(failure)}
end
local function require_true(value, message) if not value then error(message or 'assertion failed') end end
local function show(text, original, start)
    fixture_call('momoka-subtitle-translation-show',utils.format_json({
        text=text or '合成译文',original=original or 'Original line',sid='2',start=start or 1,['end']=4,fontSize=32,position=80}))
end
local function finish()
    if fixture_inject_failure then check('native-timer harness injected failure is not a pass', function() error('intentional failing assertion') end) end
    fixture_fire('end-file')
    fixture_native_command('script-message','perf-fixture-result',utils.format_json({
        completed=true,checks=checks,runtime=jit and jit.version or _VERSION,
        elapsed_seconds=fixture_native_get_time()-started,actual_timer_loop=true}))
end
fixture_properties={pause=true,['sub-text']='Original line',sid=2,['sub-visibility']=true,['sub-start']=1,['sub-end']=4}
fixture_fire('start-file');fixture_fire('file-loaded');show()
fixture_reset_counts()
fixture_native_add_timeout(0.30,function()
    check('PERF-15 actual Lua timer: loaded static cue polls but never rebuilds JSON/overlay',function()
        require_true(timer:is_enabled() and fixture_counts.reads > 0)
        require_true(fixture_counts.json == 0 and fixture_counts.updates == 0 and fixture_counts.formats == 0)
    end)
    fixture_fire('end-file');fixture_reset_counts()
    fixture_native_add_timeout(0.20,function()
        check('PERF-15 actual Lua timer: EOF kills polling and removes cue/translation',function()
            require_true(not timer:is_enabled() and fixture_counts.reads == 0 and overlay.removed)
            require_true(fixture_properties['user-data/momoka-subtitle-translation/cue'] == '')
        end)
        fixture_fire('start-file');fixture_fire('file-loaded');clear()
        fixture_properties['sub-text'],fixture_properties['sub-start']='Recovered line',10
        -- No observer call: native periodic fallback must notice the new cue.
        fixture_native_add_timeout(0.20,function()
            check('PERF-15 actual Lua timer: new file and clear restore fallback detection and show',function()
                local cue=fixture_properties['user-data/momoka-subtitle-translation/cue']
                require_true(timer:is_enabled() and type(cue)=='table' and cue.text=='Recovered line' and cue.start==10)
                show('恢复译文','Recovered line',10)
                require_true(not overlay.removed and overlay.data:find('恢复译文',1,true))
            end)
            finish()
        end)
    end)
end)
