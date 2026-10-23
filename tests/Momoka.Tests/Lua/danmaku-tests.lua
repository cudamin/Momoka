local checks = {}
local function check(name, callback)
    local ok, error = pcall(callback)
    checks[#checks + 1] = {name = name, passed = ok, detail = ok and '' or tostring(error)}
end
local function require_true(value, message) if not value then error(message or 'assertion failed') end end
local function fire(name) for _, callback in ipairs(fixture_events[name] or {}) do callback({event = name}) end end
local function call(name, ...) fixture_messages[name](...) end
local function restart()
    fixture_properties = {pause = false, ['time-pos'] = 0}
    fixture_canvas = {1280, 720}
    fixture_commands = {}
    fire('start-file'); fire('file-loaded')
end
local function load(entries, preferences, id)
    local opts = preferences or {show = true, opacity = 70, fontSize = 36, speed = 1, area = 50, density = 40}
    local value = tostring(id or 1)
    call('momoka-danmaku-begin', tostring(serial), value, utils.format_json(opts))
    call('momoka-danmaku-chunk', tostring(serial), value, utils.format_json(entries))
    call('momoka-danmaku-commit', tostring(serial), value)
end
local function comment(time, text, mode, color) return {time = time, text = text, mode = mode or 1, color = color or 16777215} end

check('handshake includes file serial and commit includes transfer id and count', function()
    restart()
    require_true(fixture_commands[1][2] == 'momoka-danmaku-ready' and fixture_commands[1][3] == tostring(serial))
    load({comment(0, '测试')}, nil, 42)
    local command = fixture_commands[#fixture_commands]
    require_true(command[2] == 'momoka-danmaku-loaded' and command[3] == tostring(serial) .. ':42:1')
end)
check('scrolling follows video clock, not wall clock', function()
    restart(); load({comment(0, '滚动测试')})
    local first = overlay.data:match('pos%(([%d.%-]+),')
    fixture_properties['time-pos'] = 2; render()
    local second = overlay.data:match('pos%(([%d.%-]+),')
    require_true(tonumber(second) < tonumber(first), 'scroll did not advance')
    fixture_properties['time-pos'] = 20; render(); require_true(overlay.data == '', 'expired comment remains')
    fixture_properties['time-pos'] = 2; render(); require_true(overlay.data:find('滚动测试', 1, true), 'backward seek did not restore comment')
end)
check('pause stops periodic work and seek while paused renders current position', function()
    restart(); load({comment(0, '暂停测试')})
    require_true(timer.enabled, 'active playback timer is off')
    fixture_properties.pause = true; sync_timer(); require_true(not timer.enabled, 'paused timer still running')
    fixture_properties['time-pos'] = 3
    fixture_observers['time-pos'](); require_true(overlay.data:find('暂停测试', 1, true))
    fixture_properties.pause = false; sync_timer(); require_true(timer.enabled)
end)
check('hidden comments preserve transfer but do not run timer', function()
    restart(); load({comment(0, '隐藏测试')}, {show = false})
    require_true(not timer.enabled and overlay.removed)
    require_true(#comments == 1, 'visibility destroyed preference/data')
end)
check('stale file or transfer cannot replace current comments', function()
    restart(); local old = serial; load({comment(0, '旧片')})
    restart(); load({comment(0, '新片')}, nil, 2)
    call('momoka-danmaku-begin', tostring(old), '99', utils.format_json({show = true}))
    call('momoka-danmaku-chunk', tostring(old), '99', utils.format_json({comment(0, '迟到旧片')}))
    call('momoka-danmaku-commit', tostring(old), '99')
    call('momoka-danmaku-chunk', tostring(serial), '1', utils.format_json({comment(0, '迟到旧请求')}))
    require_true(#comments == 1 and comments[1].text == '新片')
    call('momoka-danmaku-clear', tostring(old), '100'); require_true(#comments == 1)
    call('momoka-danmaku-begin', tostring(serial), '1', utils.format_json({show = true}))
    require_true(#comments == 1 and comments[1].text == '新片', 'older begin replaced current same-file transfer')
    call('momoka-danmaku-clear', tostring(serial), '1')
    require_true(#comments == 1, 'older clear removed current same-file transfer')
end)
check('end-file clears data and overlay and cancels timer', function()
    restart(); load({comment(0, '收场')}); fire('end-file')
    require_true(#comments == 0 and #layout == 0 and not loaded and not timer.enabled and overlay.removed)
end)
check('all ordinary modes have separate lanes within selected area', function()
    restart(); load({comment(0, '滚动', 1), comment(0, '顶部', 5), comment(0, '底部', 4)})
    require_true(#layout == 3)
    local positions = {}
    for _, item in ipairs(layout) do
        require_true(item.y >= 0 and item.y + options.fontSize <= 360, 'half-screen area overflow')
        require_true(not positions[item.y], 'different modes overlap')
        positions[item.y] = true
    end
end)
check('font and region extremes keep every line inside region', function()
    for _, font in ipairs({20, 36, 64}) do
        for _, area in ipairs({25, 50, 75, 100}) do
            restart(); local entries = {}
            for index = 1, 80 do entries[#entries + 1] = comment(0, tostring(index), ({1, 4, 5})[(index % 3) + 1]) end
            load(entries, {show = true, fontSize = font, area = area, density = 100})
            for _, item in ipairs(layout) do require_true(item.y + font <= 720 * area / 100, 'region overflow') end
        end
    end
end)
check('density bounds concurrent visible comments', function()
    restart(); local entries = {}
    for index = 1, 200 do entries[#entries + 1] = comment(0, tostring(index)) end
    load(entries, {show = true, fontSize = 20, area = 100, density = 10})
    require_true(#layout <= 10 and #layout > 0)
end)
check('scroll lanes reject catch-up collision for faster long successor', function()
    restart(); width = 1280
    local first = {start = 0, width = 100, duration = 8}
    require_true(not eligible(first, 1, 600, 8), 'long faster comment catches the previous comment')
    require_true(eligible(first, 4, 600, 8), 'safe lane never reopens')
end)
check('speed and delay alter schedule and remain finite after invalid options', function()
    restart(); load({comment(2, '延迟')}, {show = true, speed = 2, delay = 1.5})
    require_true(layout[1].start == 3.5 and layout[1].duration == 4)
    load({comment(0, '非法值')}, {show = true, opacity = -1, fontSize = 1000, speed = -2, area = 999, density = 999}, 2)
    require_true(options.opacity == 10 and options.fontSize == 64 and options.speed == 0.5 and options.area == 100 and options.density == 100)
end)
check('UTF8 and ASS metacharacters remain literal and do not execute tags', function()
    restart(); load({comment(0, '{\\pos(0,0)}\\N你好😀', 5, 255)})
    require_true(overlay.data:find('\\{', 1, true), 'brace not escaped')
    require_true(not overlay.data:find('{\\pos(0,0)}', 1, true), 'untrusted position tag entered output')
    require_true(overlay.data:find('你好😀', 1, true), 'UTF8 lost')
    local real = fixture_native_overlay('ass-events')
    real.res_x, real.res_y, real.data, real.hidden, real.compute_bounds = 1280, 720, overlay.data, true, true
    local ok, result = pcall(function() return real:update() end)
    real:remove()
    require_true(ok and result ~= nil, 'real libass rejected output')
end)
check('wide and narrow windows rebuild virtual width without stretching font', function()
    restart(); load({comment(0, '比例测试')})
    fixture_canvas = {760, 900}; render()
    require_true(math.abs(overlay.res_x - 760 / 900 * 720) < 0.01 and overlay.res_y == 720)
    fixture_canvas = {1920, 1080}; render()
    require_true(math.abs(overlay.res_x - 1280) < 0.01)
end)
check('invalid mode, time, color and nontext comments are discarded', function()
    restart(); load({42, comment(-1, 'bad'), comment(0, 'bad', 7), comment(0, 'bad', 1, -1),
        {time = 'x', text = 'bad', mode = 1, color = 1}, {time = 0, text = 42, mode = 1, color = 1}, comment(0, 'good')})
    require_true(#comments == 1 and comments[1].text == 'good')
end)
check('empty libraries stop timer and still send successful zero receipt', function()
    restart(); load({})
    require_true(not timer.enabled and overlay.removed)
    require_true(fixture_commands[#fixture_commands][3] == tostring(serial) .. ':1:0')
end)
fixture_native_command('script-message', 'fixture-result', utils.format_json(checks))
