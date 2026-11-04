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
local function visible_x()
    return tonumber(overlay.data:match('pos%(([%d.%-]+),'))
end
local function visual_font_pixels()
    return tonumber(overlay.data:match('\\fs([%d.]+)')) * fixture_canvas[2] / overlay.res_y
end
local function real_bounds(data)
    local actual = fixture_native_overlay('ass-events')
    actual.res_x, actual.res_y, actual.data = overlay.res_x, overlay.res_y, data or overlay.data
    actual.hidden, actual.compute_bounds = true, true
    local result = actual:update()
    actual:remove()
    require_true(type(result) == 'table' and type(result.x0) == 'number' and type(result.y1) == 'number', 'native ASS bounds unavailable')
    return result
end
local function full_library()
    local entries = {}
    for index = 1, 100 do entries[#entries + 1] = comment(0, 'row ' .. tostring(index), 5) end
    return entries
end
check('window scaling can be disabled without stretching fixed pixel text', function()
    restart(); load({comment(0, '缩放')}, {show = true, scaleWithWindow = false})
    require_true(math.abs(visual_font_pixels() - 36) < 0.01)
    fixture_canvas = {1920, 1080}; fire('seek')
    require_true(math.abs(visual_font_pixels() - 36) < 0.02, 'fixed pixel font grew with window')
    fixture_canvas = {640, 360}; fire('seek')
    require_true(math.abs(visual_font_pixels() - 36) < 0.02, 'fixed pixel font shrank with window')
    load({comment(0, '比例')}, {show = true, scaleWithWindow = true}, 2)
    require_true(math.abs(visual_font_pixels() - 18) < 0.01, 'proportional mode stopped scaling')
end)
check('subtitle avoidance reserves bottom fifth and restores it on main track visibility changes', function()
    restart()
    fixture_properties.sid, fixture_properties['sub-visibility'] = 2, true
    load(full_library(), {show = true, area = 100, density = 100, avoidSubtitles = true})
    local reserved_count = #layout
    for _, entry in ipairs(layout) do require_true(entry.y + 36 <= 576, 'selected visible subtitles have no safe region') end
    fixture_properties['sub-visibility'] = false
    require_true(type(fixture_observers['sub-visibility']) == 'function', 'subtitle visibility is not observed')
    fixture_observers['sub-visibility']()
    require_true(#layout > reserved_count, 'hiding subtitles did not restore full region')
    fixture_properties['sub-visibility'] = true; fixture_observers['sub-visibility']()
    require_true(#layout == reserved_count, 'showing subtitles did not restore protection')
    fixture_properties.sid = 'no'; fixture_observers.sid()
    require_true(#layout > reserved_count, 'unselected subtitles still reserve space')
end)
check('subtitle avoidance accepts secondary and bitmap tracks without depending on subtitle text', function()
    restart()
    fixture_properties.sid, fixture_properties['sub-visibility'] = -2, true
    fixture_properties['secondary-sid'], fixture_properties['secondary-sub-visibility'] = 7, true
    fixture_properties['sub-text'], fixture_properties['secondary-sub-text'] = '', ''
    load(full_library(), {show = true, area = 100, density = 100, avoidSubtitles = true})
    local protected = #layout
    for _, entry in ipairs(layout) do require_true(entry.y + 36 <= 576, 'secondary bitmap subtitle region was ignored') end
    fixture_properties['secondary-sub-visibility'] = false
    require_true(type(fixture_observers['secondary-sub-visibility']) == 'function')
    fixture_observers['secondary-sub-visibility']()
    require_true(#layout > protected)
    fixture_properties['secondary-sid'] = 'auto'; fixture_properties['secondary-sub-visibility'] = true
    fixture_observers['secondary-sid']()
    require_true(#layout > protected, 'auto without an actual selected track reserves space')
end)
check('subtitle avoidance leaves an already restricted half-screen region unchanged', function()
    restart(); load(full_library(), {show = true, area = 50, density = 100, avoidSubtitles = true})
    local count = #layout
    fixture_properties.sid, fixture_properties['sub-visibility'] = 3, true
    if fixture_observers.sid then fixture_observers.sid() end
    require_true(#layout == count, 'safe half-screen region was unnecessarily reduced')
end)
check('more density uses extra nonoverlapping rows while the legacy hard cap remains effective', function()
    restart(); load(full_library(), {show = true, area = 100, density = 100, densityMode = 0})
    local normal = #layout
    load(full_library(), {show = true, area = 100, density = 100, densityMode = 1}, 2)
    require_true(#layout > normal, 'more mode adds no rows')
    local positions = {}
    for _, entry in ipairs(layout) do require_true(not positions[entry.y], 'more mode overlaps fixed rows'); positions[entry.y] = true end
    load(full_library(), {show = true, area = 100, density = 10, densityMode = 1}, 3)
    require_true(#layout <= 10, 'more mode bypassed the hard cap')
end)
check('overlap density reuses full lanes but never exceeds the requested concurrent cap', function()
    restart(); load(full_library(), {show = true, area = 25, density = 40, densityMode = 2})
    require_true(#layout == 40, 'overlap mode still discards full lanes')
    local positions, overlapping = {}, false
    for _, entry in ipairs(layout) do overlapping = overlapping or positions[entry.y] == true; positions[entry.y] = true end
    require_true(overlapping, 'overlap mode did not reuse lanes')
    require_true(#layout <= options.density)
end)
check('disabling overlap prevention admits full lanes and re-enabling restores safe planning', function()
    restart(); load(full_library(), {show = true, area = 25, density = 40, preventOverlap = true})
    local safe = #layout
    load(full_library(), {show = true, area = 25, density = 40, preventOverlap = false}, 2)
    require_true(#layout == 40 and #layout > safe, 'overlap switch has no rendering effect')
    load(full_library(), {show = true, area = 25, density = 40, preventOverlap = true}, 3)
    require_true(#layout == safe, 'safe planning was not restored')
end)
check('font family and bold styles reach actual ASS without accepting font tag injection', function()
    restart(); load({comment(0, 'Font中文😀', 5)}, {show = true, fontFamily = 'Arial', bold = true})
    require_true(overlay.data:find('\\fnArial', 1, true) and overlay.data:find('\\b1', 1, true), 'requested font/bold was ignored')
    local first = real_bounds()
    require_true(first.x1 > first.x0 and first.y1 > first.y0)
    load({comment(0, 'plain', 5)}, {show = true, fontFamily = 'Microsoft YaHei', bold = false}, 2)
    require_true(overlay.data:find('\\fnMicrosoft YaHei', 1, true) and overlay.data:find('\\b0', 1, true), 'restore retained old bold/font')
    load({comment(0, 'safe', 5)}, {show = true, fontFamily = 'Arial}\\pos(0,0){', bold = true}, 3)
    require_true(not overlay.data:find('Arial}\\pos', 1, true), 'font name injected ASS position')
    real_bounds()
end)
check('outline, heavy outline and diagonal projection produce distinct real glyph footprints', function()
    restart(); load({comment(0, 'Style测试', 5)}, {show = true, borderStyle = 0})
    local normal = real_bounds()
    load({comment(0, 'Style测试', 5)}, {show = true, borderStyle = 1}, 2)
    local heavy = real_bounds()
    require_true(overlay.data:find('\\bord3', 1, true), 'heavy outline was not applied')
    require_true(heavy.x1 - heavy.x0 > normal.x1 - normal.x0, 'heavy outline does not expand native footprint')
    load({comment(0, 'Style测试', 5)}, {show = true, borderStyle = 2}, 3)
    require_true(overlay.data:find('\\bord0', 1, true) and overlay.data:find('\\shad3', 1, true), 'diagonal shadow was not applied')
    real_bounds()
end)
check('font, bold and border changes refresh width caches used to prevent collisions', function()
    restart(); load({comment(0, 'WW中文😀')}, {show = true})
    local plain = layout[1].width
    options.bold = true; rebuild()
    require_true(layout[1].width > plain, 'bold reused unstyled width')
    local bold_width = layout[1].width
    options.borderStyle = 1; rebuild()
    require_true(layout[1].width > bold_width, 'heavy outline reused old width')
    local old_key = comments[1].measured_key
    options.fontFamily = 'Arial'; rebuild()
    require_true(comments[1].measured_key ~= old_key, 'family change retained width cache key')
end)
check('real glyph bounds stay inside every chosen region for bold and border style extremes', function()
    for _, family in ipairs({'Microsoft YaHei', 'Arial'}) do
        for _, border in ipairs({0, 1, 2}) do
            restart()
            load(full_library(), {show = true, area = 25, density = 100, fontSize = 64, fontFamily = family, bold = true, borderStyle = border})
            local result = real_bounds()
            require_true(result.y0 >= 0 and result.y1 <= 180, 'native glyphs exceed selected quarter-screen region')
        end
    end
end)
check('speed synchronization uses video time once at double playback speed', function()
    restart(); fixture_properties.speed = 2
    load({comment(0, '同步')}, {show = true, speed = 1, speedSyncPlayback = true})
    fixture_properties['time-pos'] = 4; render()
    local synchronized = visible_x()
    require_true(synchronized ~= nil, 'double speed expired comments twice')
    load({comment(0, '同步')}, {show = true, speed = 1, speedSyncPlayback = true}, 2)
    fixture_properties.speed = 1; fixture_properties['time-pos'] = 4; render()
    require_true(math.abs(visible_x() - synchronized) < 0.01, 'synchronized mode multiplied playback speed twice')
end)
check('unsynchronized motion advances equally after one real second at one and two playback speed', function()
    restart(); fixture_properties.speed = 1
    load({comment(0, '相同速度')}, {show = true, speedSyncPlayback = false})
    fixture_properties['time-pos'] = 1; render(); local one = visible_x()
    restart(); fixture_properties.speed = 2
    load({comment(0, '相同速度')}, {show = true, speedSyncPlayback = false})
    fixture_properties['time-pos'] = 2; render()
    require_true(math.abs(visible_x() - one) < 0.01, 'unsynchronized speed still follows playback multiplier')
end)
check('unsynchronized rate changes preserve positions and immediately compensate later motion', function()
    restart(); fixture_properties.speed = 1
    load({comment(0, '变速')}, {show = true, speedSyncPlayback = false})
    fixture_properties['time-pos'] = 2; render(); local before = visible_x()
    fixture_properties.speed = 2
    require_true(type(fixture_observers.speed) == 'function', 'playback speed is not observed')
    fixture_observers.speed()
    require_true(math.abs(visible_x() - before) < 0.01, 'rate change jumped the active comment')
    fixture_properties['time-pos'] = 4; render(); local after = visible_x()
    fixture_properties.speed = 1; fixture_observers.speed()
    fixture_properties['time-pos'] = 5; render(); local later = visible_x()
    require_true(math.abs((before - after) - (after - later)) < 0.02, 'rate compensation is not continuous')
end)
check('unsynchronized comments appear at media timestamps and pause and seek retain ownership', function()
    restart(); fixture_properties.speed = 2
    load({comment(4, '时点')}, {show = true, speedSyncPlayback = false})
    fixture_properties['time-pos'] = 3; render(); require_true(not overlay.data:find('时点', 1, true))
    fixture_properties['time-pos'] = 4; render(); require_true(overlay.data:find('时点', 1, true))
    local paused = visible_x()
    fixture_properties.pause = true; sync_timer(); render(); render()
    require_true(not timer.enabled and math.abs(visible_x() - paused) < 0.01, 'paused animation moved or timer resumed')
    fixture_properties['time-pos'] = 12; fire('seek'); require_true(overlay.data:find('时点', 1, true), 'paused seek cannot reconstruct compensated comment')
    fixture_properties['time-pos'] = 1; fire('seek'); require_true(not overlay.data:find('时点', 1, true), 'backward seek kept a future comment')
    fire('end-file'); fixture_properties.speed = 1
    if fixture_observers.speed then fixture_observers.speed() end
    require_true(#comments == 0 and overlay.removed and not timer.enabled, 'late rate callback touched an ended file')
end)
check('unsynchronized slow comments survive beyond sixteen media seconds and expire at the actual end', function()
    restart(); fixture_properties.speed = 2
    load({comment(0, '长时长')}, {show = true, speed = 0.5, speedSyncPlayback = false})
    fixture_properties['time-pos'] = 20; render()
    require_true(overlay.data:find('长时长', 1, true), 'hardcoded sixteen-second lookup loses compensated comments')
    fixture_properties['time-pos'] = 32; render()
    require_true(not overlay.data:find('长时长', 1, true), 'expired compensated comment remains')
end)
check('invalid new preferences fall back without hiding valid default comments', function()
    restart()
    load({comment(0, '默认')}, {show = true, fontFamily = 42, borderStyle = -1, densityMode = 99, bold = 'true',
        scaleWithWindow = 'false', preventOverlap = 'false', speedSyncPlayback = 'false'})
    require_true(overlay.data:find('默认', 1, true))
    require_true(overlay.data:find('\\fnMicrosoft YaHei', 1, true) and overlay.data:find('\\b0', 1, true))
    require_true(options.borderStyle == 0 and options.densityMode == 0 and options.scaleWithWindow == true
        and options.preventOverlap == true and options.speedSyncPlayback == true, 'invalid new preferences changed defaults')
end)
check('fixed pixel text recovers after a tiny window has no room for one safe lane', function()
    restart(); fixture_canvas = {160,90}
    load({comment(0,'小窗恢复')}, {show=true,scaleWithWindow=false,fontSize=64,area=25})
    require_true(#layout == 0 and overlay.removed, 'tiny fixed pixel lane overflows the region')
    fixture_canvas = {1280,720}; fire('seek')
    require_true(#layout == 1 and not overlay.removed, 'enlarging the tiny window never recovers a lane')
end)
check('safe mode keeps real bold emoji glyphs from intersecting a neighboring fixed lane', function()
    restart()
    load({comment(0,'MW国😀',5),comment(0,'MW国😀',5)}, {show=true,bold=true,borderStyle=1,densityMode=1,fontSize=36,area=100})
    require_true(#layout == 2)
    local bounds = {}
    for index, entry in ipairs(layout) do
        local data = string.format('{\\an8\\pos(640,%.2f)\\fnMicrosoft YaHei\\fs36\\b1\\bord3\\shad0\\q2}%s',entry.y,entry.text)
        bounds[index] = real_bounds(data)
    end
    require_true(bounds[1].y1 <= bounds[2].y0, 'tight lanes overlap actual bold/fallback glyphs')
end)
check('compensated rate changes preserve safe visible survivors until their actual duration ends', function()
    restart(); fixture_properties.speed=1
    load({comment(0,'Keep',5),comment(7,'Later',5)}, {show=true,speedSyncPlayback=false,fontSize=64,area=25,density=40})
    fixture_properties['time-pos']=5; render()
    require_true(overlay.data:find('Keep',1,true))
    fixture_properties.speed=10; fixture_observers.speed()
    require_true(overlay.data:find('Keep',1,true), 'new schedule removed a surviving comment')
    fixture_properties['time-pos']=9; render()
    require_true(overlay.data:find('Keep',1,true), 'compensated survivor disappeared before six real seconds')
    require_true(not overlay.data:find('Later',1,true), 'later comment overlaps the occupied fixed lane')
    fixture_properties['time-pos']=15; render()
    require_true(not overlay.data:find('Keep',1,true), 'survivor remains at duration boundary')
end)
check('fixed pixel shrink retains the full real glyph before deferred planning including diagonal shadow', function()
    for _, style in ipairs({0,1,2}) do
        restart(); fixture_properties['time-pos'] = 1
        load({comment(0,'完整字形😀',5)}, {show=true,fontSize=36,area=100,scaleWithWindow=false,borderStyle=style})
        local original_rebuild, rebuilds = rebuild, 0
        rebuild = function(...) rebuilds = rebuilds + 1; return original_rebuild(...) end
        fixture_canvas = {640,360}; render()
        rebuild = original_rebuild
        require_true(rebuilds == 0 and pending_resize ~= nil, 'fixed font resize scans the full library before settle')
        require_true(math.abs(visual_font_pixels()-36)<0.01, 'shrink changed requested physical pixel size')
        local clipped = real_bounds()
        local unclipped = real_bounds((overlay.data:gsub('\\clip%([^)]*%)', '')))
        require_true(math.abs(clipped.y0-unclipped.y0)<=1 and math.abs(clipped.y1-unclipped.y1)<=1,
            'old lane clip cuts a resized glyph or diagonal shadow')
    end
end)
check('fixed pixel shrink immediately separates visible rows and hides lines outside the region', function()
    restart(); fixture_properties['time-pos']=1
    load({comment(0,'第一行😀',5),comment(0,'第二行😀',5),comment(0,'第三行😀',5)},
        {show=true,fontSize=36,area=25,scaleWithWindow=false,bold=true,borderStyle=2})
    fixture_canvas={640,360}; render()
    require_true(overlay.data:find('第一行',1,true), 'safe resized first row was lost')
    require_true(not overlay.data:find('第二行',1,true), 'out-of-area row remains partially clipped instead of hidden')
    local visible = real_bounds()
    require_true(visible.y0>=0 and visible.y1<=180, 'resized visible row escapes the selected region')
    load({comment(0,'第一行😀',5),comment(0,'第二行😀',5)},
        {show=true,fontSize=36,area=100,scaleWithWindow=false,bold=true,borderStyle=2}, 2)
    fixture_canvas={1280,720}; fire('seek')
    fixture_canvas={640,360}; render()
    local bounds={}
    for data in overlay.data:gmatch('[^\n]+') do
        local clipped=real_bounds(data)
        local full=real_bounds((data:gsub('\\clip%([^)]*%)','')))
        require_true(math.abs(clipped.y0-full.y0)<=1 and math.abs(clipped.y1-full.y1)<=1, 'one resized row is clipped')
        bounds[#bounds+1]=full
    end
    require_true(#bounds==2 and bounds[1].y1<=bounds[2].y0, 'resized rows overlap before deferred planning')
end)
check('same-file appearance retransmission preserves compensated positions across multiple playback rates', function()
    restart(); fixture_properties.speed=1
    load({comment(0,'透明度不移动')}, {show=true,speedSyncPlayback=false})
    fixture_properties['time-pos']=2; render()
    fixture_properties.speed=2; fixture_observers.speed()
    fixture_properties['time-pos']=8; render()
    local before=visible_x()
    load({comment(0,'透明度不移动')}, {show=true,speedSyncPlayback=false,opacity=35}, 2)
    require_true(math.abs(before-visible_x())<0.01, 'appearance reload resets the compensated clock')
    local first_alpha=overlay.data:match('\\alpha&H(%x+)&')
    require_true(first_alpha=='A6', 'new opacity was not applied')
    load({comment(0,'滚动连续')}, {show=true,speedSyncPlayback=false}, 3)
    local position=visible_x()
    load({comment(0,'滚动连续')}, {show=true,speedSyncPlayback=false,opacity=85}, 4)
    require_true(math.abs(visible_x()-position)<0.01, 'data/filter replacement rewinds active scrolling')
end)
check('same-file appearance retransmission never revives an expired compensated comment', function()
    restart(); fixture_properties.speed=1
    load({comment(0,'不能复活')}, {show=true,speedSyncPlayback=false})
    fixture_properties['time-pos']=8; render()
    require_true(not overlay.data:find('不能复活',1,true), 'comment has not reached its original expiry')
    fixture_properties.speed=2; fixture_observers.speed()
    fixture_properties['time-pos']=10; render()
    require_true(not overlay.data:find('不能复活',1,true))
    load({comment(0,'不能复活')}, {show=true,speedSyncPlayback=false,opacity=50}, 2)
    require_true(not overlay.data:find('不能复活',1,true), 'opacity reload revives an expired comment')
end)
check('fixed pixel resize checks only visible scroll collisions until the deferred full rebuild', function()
    restart(); fixture_properties['time-pos']=0
    load({comment(0,'AAAA'),comment(1,'BBBB')},
        {show=true,fontSize=64,area=25,scaleWithWindow=false,preventOverlap=true})
    fixture_properties['time-pos']=2; render()
    fixture_canvas={1280,1440}; render()
    local count=0
    for _ in overlay.data:gmatch('[^\n]+') do count=count+1 end
    require_true(count==1, 'resize leaves temporarily intersecting scroll comments visible')
    load({comment(0,'AAAA'),comment(1,'BBBB')},
        {show=true,fontSize=64,area=25,scaleWithWindow=false,preventOverlap=false}, 2)
    fixture_canvas={1280,720}; fire('seek')
    fixture_canvas={1280,1440}; render()
    count=0
    for _ in overlay.data:gmatch('[^\n]+') do count=count+1 end
    require_true(count==2, 'resize enforces overlap prevention when the user turned it off')
end)
check('sync mode and file changes deliberately reset compensated clocks without preserving old owners', function()
    restart(); fixture_properties.speed=1
    load({comment(0,'换模式')}, {show=true,speedSyncPlayback=false})
    fixture_properties['time-pos']=2; render()
    fixture_properties.speed=2; fixture_observers.speed()
    fixture_properties['time-pos']=10; render()
    require_true(overlay.data:find('换模式',1,true), 'mixed rate comment is not active')
    load({comment(0,'换模式')}, {show=true,speedSyncPlayback=true}, 2)
    require_true(not overlay.data:find('换模式',1,true), 'switch to synchronized motion keeps compensated time')
    load({comment(0,'换模式')}, {show=true,speedSyncPlayback=false}, 3)
    require_true(overlay.data:find('换模式',1,true), 'switch back did not start a new compensated owner')
    restart(); fixture_properties.speed=2; fixture_properties['time-pos']=10
    load({comment(0,'新文件')}, {show=true,speedSyncPlayback=false})
    require_true(overlay.data:find('新文件',1,true), 'previous file retained speed history')
end)
check('same-file clear and replacement preserve compensated time while old data and overlay are removed', function()
    restart(); fixture_properties.speed=1
    load({comment(0,'替换来源')}, {show=true,speedSyncPlayback=false})
    fixture_properties['time-pos']=2; render()
    fixture_properties.speed=2; fixture_observers.speed()
    fixture_properties['time-pos']=8; render(); local before=visible_x()
    call('momoka-danmaku-clear',tostring(serial),'2')
    require_true(#comments==0 and #layout==0 and overlay.removed and not timer.enabled, 'clear retains the old library')
    load({comment(0,'替换来源')}, {show=true,speedSyncPlayback=false}, 3)
    require_true(math.abs(visible_x()-before)<0.01, 'same-file clear rewinds playback-rate history')
end)
fixture_native_command('script-message', 'fixture-result', utils.format_json(checks))
