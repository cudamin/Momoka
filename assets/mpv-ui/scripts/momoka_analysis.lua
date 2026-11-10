-- Explicit local playback analysis. No network, filenames, credentials or user configuration.
-- Detection follows dyphire dynamic-crop/chapterskip filter semantics; state belongs to one file.
local mp = require('mp')
local utils = require('mp.utils')
local serial, crop, silence = 0, nil, nil
local crop_label, silence_label = 'momoka_cropdetect', 'momoka_silence'
-- Direct-filter creation is validated before media loads. Merely having lavfi does not mean
-- this LGPL FFmpeg build contains cropdetect; unsupported features stay hidden.
local crop_supported = mp.commandv('vf', 'add', '@momoka_crop_probe:lavfi-cropdetect=limit=24/255:round=2:reset=1')
if crop_supported then mp.commandv('vf', 'remove', '@momoka_crop_probe') end
local silence_supported = mp.commandv('af', 'add', '@momoka_silence_probe:lavfi-silencedetect=noise=-40dB:d=0.3')
if silence_supported then mp.commandv('af', 'remove', '@momoka_silence_probe') end

local function finite(value)
    return type(value) == 'number' and value == value and value ~= math.huge and value ~= -math.huge
end
local function available()
    local decoder = mp.get_property('hwdec-current') or 'no'
    local video = mp.get_property_native('video-params')
    local audio = mp.get_property_native('audio-out-params')
    local format = type(audio) == 'table' and audio.format or ''
    local pcm = silence_supported and format ~= '' and not tostring(format):lower():find('spdif', 1, true)
    local filters = mp.get_property_native('vf') or {}
    local svp = false
    for _, filter in ipairs(filters) do
        if filter.name == 'vapoursynth' or filter.label == 'svp' then svp = true end
    end
    local can_crop = crop_supported and type(video) == 'table' and finite(video.w) and finite(video.h)
        and (decoder == 'no' or decoder:find('copy', 1, true)) and not svp
    mp.set_property_native('user-data/momoka-analysis-ready', true)
    mp.set_property_native('user-data/momoka-analysis-generation', serial)
    mp.set_property_native('user-data/momoka-analysis-crop-available', not not can_crop)
    mp.set_property_native('user-data/momoka-analysis-silence-available', not not pcm)
    return can_crop, pcm
end
local function tell(text) mp.osd_message(text, 2.5) end
local function publish_active() mp.set_property_native('user-data/momoka-analysis-active', not not (crop or silence)) end
local function current(generation) return tostring(serial) == tostring(generation) end
local function remove_filter(kind, label)
    mp.commandv(kind, 'remove', '@' .. label)
end
local function stop_crop(restore)
    if not crop then return end
    local owned = crop
    crop = nil
    publish_active()
    if owned.timer then owned.timer:kill() end
    remove_filter('vf', crop_label)
    if restore and owned.applied then mp.set_property('video-crop', owned.original) end
end
local function restore_if_current(name, applied, original)
    -- Preserve a simultaneous user adjustment, and never borrow values from a successor file.
    if mp.get_property_native(name) == applied then mp.set_property_native(name, original) end
end
local function stop_silence(restore)
    if not silence then return end
    local owned = silence
    silence = nil
    publish_active()
    if owned.timer then owned.timer:kill() end
    mp.unobserve_property(owned.callback)
    remove_filter('af', silence_label)
    if restore and owned.serial == serial then
        restore_if_current('speed', owned.speed, owned.original_speed)
        restore_if_current('mute', true, owned.original_mute)
        restore_if_current('pause', false, owned.original_pause)
    end
end
local function crop_command()
    if crop then stop_crop(true); tell('去黑边已关闭，原裁切已恢复'); return end
    local can_crop = available()
    if not can_crop then tell('去黑边需要软解或 copy 硬解，且不能与 SVP 同时使用'); return end
    local original = mp.get_property('video-crop') or ''
    local ok = mp.commandv('vf', 'pre', '@' .. crop_label .. ':lavfi-cropdetect=limit=24/255:round=2:reset=1')
    if not ok then tell('无法启动黑边检测'); return end
    local owned = {serial = serial, original = original, started = mp.get_time()}
    crop = owned
    publish_active()
    owned.timer = mp.add_periodic_timer(0.1, function()
        if crop ~= owned or owned.serial ~= serial then return end
        local meta = mp.get_property_native('vf-metadata/' .. crop_label) or {}
        local function number(name) return tonumber(meta['lavfi.cropdetect.' .. name]) end
        local w, h, x, y = number('w'), number('h'), number('x'), number('y')
        local video = mp.get_property_native('video-params') or {}
        if finite(w) and finite(h) and finite(x) and finite(y) and w >= 16 and h >= 16 and x >= 0 and y >= 0
            and finite(video.w) and finite(video.h) and x + w <= video.w and y + h <= video.h then
            local value = string.format('%dx%d+%d+%d', w, h, x, y)
            if owned.candidate ~= value then owned.candidate, owned.since = value, mp.get_time() end
            -- Require one stable second; full-frame black flashes cannot commit a tiny crop.
            if mp.get_time() - owned.since >= 1 and (w ~= video.w or h ~= video.h) then
                local applied = mp.set_property('video-crop', value)
                if applied then owned.applied = true; tell('去黑边：' .. value) end
                if owned.timer then owned.timer:kill() end
                remove_filter('vf', crop_label)
                return
            end
        end
        if mp.get_time() - owned.started >= 8 then
            stop_crop(false)
            tell('未找到稳定黑边，保留原画面')
        end
    end)
    tell('正在检测黑边；再次执行可取消')
end
local function silence_command()
    if silence then stop_silence(true); tell('已取消静音点扫描'); return end
    local _, pcm = available()
    local position = mp.get_property_number('time-pos')
    local speed = mp.get_property_native('speed')
    local mute = mp.get_property_native('mute')
    local pause = mp.get_property_native('pause')
    if not pcm or not finite(position) or not finite(speed) or type(mute) ~= 'boolean' or type(pause) ~= 'boolean' then
        tell('静音点扫描需要可解码的音轨和已确认的播放状态'); return
    end
    if not mp.commandv('af', 'add', '@' .. silence_label .. ':lavfi-silencedetect=noise=-40dB:d=0.3') then
        tell('无法启动静音点扫描'); return
    end
    local owned = {serial = serial, start = position, started = mp.get_time(), speed = 8,
        original_speed = speed, original_mute = mute, original_pause = pause}
    silence = owned
    publish_active()
    owned.callback = function(_, metadata)
        if silence ~= owned or serial ~= owned.serial or type(metadata) ~= 'table' then return end
        local point = tonumber(metadata['lavfi.silence_start'])
        if not finite(point) or point < owned.start + 1 then return end
        stop_silence(true)
        if serial == owned.serial then
            local result = mp.commandv('seek', tostring(point), 'absolute+exact')
            tell(result and '已跳到下一个静音位置' or '静音位置跳转失败')
        end
    end
    mp.observe_property('af-metadata/' .. silence_label, 'native', owned.callback)
    local changed = mp.set_property_native('mute', true) and mp.set_property_native('speed', owned.speed)
        and mp.set_property_native('pause', false)
    if not changed then stop_silence(true); tell('扫描状态未能完整应用，已取消'); return end
    owned.timer = mp.add_periodic_timer(0.2, function()
        if silence ~= owned or serial ~= owned.serial then return end
        local time = mp.get_property_number('time-pos')
        if mp.get_time() - owned.started >= 15 or finite(time) and time - owned.start >= 120 then
            stop_silence(true); tell('扫描范围内未找到静音点，已恢复播放状态')
        end
    end)
    tell('正在扫描下一个静音点；再次执行可取消')
end
local function boundary()
    stop_crop(true)
    stop_silence(true)
    serial = serial + 1
    mp.set_property_native('user-data/momoka-analysis-generation', serial)
    mp.set_property_native('user-data/momoka-analysis-crop-available', false)
    mp.set_property_native('user-data/momoka-analysis-silence-available', false)
end
mp.register_script_message('crop', function(generation) if current(generation) then crop_command() end end)
mp.register_script_message('silence', function(generation) if current(generation) then silence_command() end end)
mp.register_script_message('chapters', function(json, generation)
    if not current(generation) or type(json) ~= 'string' or #json > 3000000 then return end
    local nodes = utils.parse_json(json)
    if type(nodes) ~= 'table' or #nodes > 10000 then tell('章节数据无效'); return end
    for _, node in ipairs(nodes) do
        if type(node) ~= 'table' or not finite(node.time) or node.time < 0
            or type(node.title) ~= 'string' or #node.title > 800
            or select(2, node.title:gsub('[^\128-\191]', '')) > 200 then tell('章节数据无效'); return end
    end
    local ok = mp.set_property_native('chapter-list', nodes)
    tell(ok and '章节已更新' or '章节更新失败')
end)
mp.register_script_message('reset', function(generation) if current(generation) then boundary() end end)
mp.add_hook('on_unload', 5, function() boundary() end)
mp.register_event('start-file', function()
    -- on_unload owns restoration while the old file is still current; start-file only invalidates work.
    stop_crop(false); stop_silence(false); serial = serial + 1
    mp.set_property_native('user-data/momoka-analysis-generation', serial)
end)
mp.register_event('file-loaded', available)
mp.register_event('video-reconfig', available)
mp.register_event('audio-reconfig', available)
mp.register_event('shutdown', boundary)
available()
