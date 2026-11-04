-- Momoka's independent danmaku layer. No HTTP, media reads, credentials or subtitle tracks.
-- Every transfer is bound to this script's file serial and a host load id; stale chunks are discarded.
local mp = require('mp')
local utils = require('mp.utils')
local overlay = mp.create_osd_overlay('ass-events')
overlay.z = -10 -- uosc/normal OSD stays above comments.
local serial, loaded, transfer = 0, false, nil
local highest_transfer = 0
local comments, layout = {}, {}
local options = {show = false, opacity = 70, fontSize = 36, speed = 1, area = 50, density = 40, delay = 0,
    scaleWithWindow = true, avoidSubtitles = false, preventOverlap = true, speedSyncPlayback = true,
    fontFamily = 'Microsoft YaHei', bold = false, borderStyle = 0, densityMode = 0}
local width, height = 1280, 720
local physical_height, planned_font, planned_area = 720, 36, 360
local speed_segments, playback_speed = {}, 1
local maximum_duration = 16
local timer, resize_timer, pending_resize
local resize_quiet_seconds, resize_max_seconds = 0.15, 0.5
local render

local function finite(value)
    return type(value) == 'number' and value == value and value ~= math.huge and value ~= -math.huge
end

local function clamp(value, low, high, fallback)
    return finite(value) and math.max(low, math.min(high, value)) or fallback
end

local function escape(text)
    -- Same libass-safe escaping as bundled uosc: literal backslashes cannot become ASS directives.
    return text:gsub('\\', '\\\239\187\191'):gsub('{', '\\{'):gsub('}', '\\}'):gsub('[\r\n]', ' ')
end

local function font_family(value)
    if type(value) ~= 'string' or #value > 256 or value:find('[\\{}%c]') then return 'Microsoft YaHei' end
    local trimmed = value:match('^%s*(.-)%s*$')
    return trimmed ~= '' and trimmed or 'Microsoft YaHei'
end

local function font_size(canvas_height)
    return options.scaleWithWindow and options.fontSize or options.fontSize * 720 / (canvas_height or physical_height)
end

local function font_extent(font)
    -- Emoji and fallback fonts can be taller than fs; leave room for their ink and bold strokes.
    -- Each lane is also clipped below so an exceptional font cannot draw into its neighbor.
    return font * 1.4 + (options.borderStyle == 1 and 6 or 3)
end

local function subtitle_visible()
    -- mpv 270367e8af command.c: sid GET returns the actual selected user_tid, or -2/no;
    -- options.c: visibility is independent. Do not use sub-text: bitmap tracks need this margin too.
    for _, prefix in ipairs({'', 'secondary-'}) do
        local id = tonumber(mp.get_property_native(prefix .. 'sid', 'no'))
        if id and id > 0 and mp.get_property_native(prefix .. 'sub-visibility', false) == true then return true end
    end
    return false
end

local function display_area()
    return 720 * math.min(options.area, options.avoidSubtitles and subtitle_visible() and 80 or 100) / 100
end

local function current_speed()
    return clamp(mp.get_property_number('speed', 1), 0.01, 100, 1)
end

local function motion_time(position)
    if options.speedSyncPlayback or #speed_segments == 0 then return position end
    local low, high = 1, #speed_segments
    while low < high do
        local middle = math.ceil((low + high) / 2)
        if speed_segments[middle].position <= position then low = middle else high = middle - 1 end
    end
    local segment = speed_segments[low]
    return segment.motion + (position - segment.position) / segment.speed
end

local function reset_speed_clock()
    playback_speed = current_speed()
    speed_segments = {{position = 0, motion = 0, speed = playback_speed}}
end

local function text_width(text, font)
    -- Conservative Unicode widths. Overestimating is preferable to collisions or clipping through the next lane.
    local units = 0
    for character in text:gmatch('[%z\1-\127\194-\244][\128-\191]*') do
        units = units + (#character == 1 and (character == ' ' and 0.5 or 1) or 1.1)
    end
    -- The estimate intentionally includes bold and outline/shadow extents. Font families are
    -- cache identities even when this conservative estimate happens to be identical.
    local stroke = options.borderStyle == 1 and 6 or 3
    return math.max(font, units * font * (options.bold and 1.1 or 1) + 8 + stroke)
end

local function eligible(previous, start, comment_width, duration)
    if not previous then return true end
    local old_speed = (width + previous.width) / previous.duration
    local new_speed = (width + comment_width) / duration
    local earliest = previous.start + (previous.width + 12) / old_speed
    if new_speed > old_speed then
        earliest = math.max(earliest, previous.start + previous.duration - (width - 12) / new_speed)
    end
    return start >= earliest
end

local function rebuild()
    local layout_count = 0
    local font = font_size()
    local glyph_height = font_extent(font)
    local line = glyph_height + (options.densityMode == 1 and 2 or 8)
    local area = display_area()
    planned_font, planned_area = font, area
    maximum_duration = 16
    local rows = math.max(0, math.floor((area - 16) / line))
    local has_scroll, has_top, has_bottom = false, false, false
    for _, comment in ipairs(comments) do
        has_scroll = has_scroll or comment.mode == 1
        has_top = has_top or comment.mode == 5
        has_bottom = has_bottom or comment.mode == 4
    end
    local top_rows = has_top and rows > 0 and math.max(1, math.floor(rows / 5)) or 0
    local bottom_rows = has_bottom and rows > 0 and math.max(1, math.floor(rows / 5)) or 0
    if not has_scroll then
        top_rows = has_top and (has_bottom and math.ceil(rows / 2) or rows) or 0
        bottom_rows = has_bottom and rows - top_rows or 0
    end
    if top_rows + bottom_rows > rows then bottom_rows = math.max(0, rows - top_rows) end
    local scroll_rows = has_scroll and math.max(0, rows - top_rows - bottom_rows) or 0
    local lanes = {scroll = {}, top = {}, bottom = {}}
    local reuse = {scroll = 0, top = 0, bottom = 0}
    local allow_overlap = not options.preventOverlap or options.densityMode == 2
    local measured_key = table.concat({tostring(font), options.fontFamily, tostring(options.bold), tostring(options.borderStyle)}, ':')
    local endings = {}
    for _, comment in ipairs(comments) do
        local media_start = comment.time + options.delay
        local start = motion_time(media_start)
        local duration = (comment.mode == 1 and 8 or 6) / options.speed
        -- Compact the bounded density queue in place instead of allocating one table per comment.
        local kept = 0
        for index = 1, #endings do
            if endings[index] > start then kept = kept + 1; endings[kept] = endings[index] end
        end
        for index = #endings, kept + 1, -1 do endings[index] = nil end
        if #endings < options.density then
            if comment.measured_key ~= measured_key then
                comment.measured_width = text_width(comment.text, font)
                comment.measured_font = font
                comment.measured_key = measured_key
            end
            local comment_width = comment.measured_width
            local lane_set, count, offset, kind
            if comment.mode == 1 then lane_set, count, offset, kind = lanes.scroll, scroll_rows, top_rows, 'scroll'
            elseif comment.mode == 5 then lane_set, count, offset, kind = lanes.top, top_rows, 0, 'top'
            else lane_set, count, offset, kind = lanes.bottom, bottom_rows, rows - bottom_rows, 'bottom' end
            local chosen
            for lane = 1, count do
                local previous = lane_set[lane]
                local free = comment.mode == 1 and eligible(previous, start, comment_width, duration)
                    or comment.mode ~= 1 and (not previous or previous.start + previous.duration <= start)
                if free then
                    chosen = lane
                    break
                end
            end
            if not chosen and allow_overlap and count > 0 then
                chosen = reuse[kind] % count + 1
                reuse[kind] = reuse[kind] + 1
            end
            if chosen then
                comment.escaped_text = comment.escaped_text or escape(comment.text)
                layout_count = layout_count + 1
                local entry = layout[layout_count] or {}
                entry.start, entry.duration, entry.width, entry.mode = start, duration, comment_width, comment.mode
                entry.media_start = media_start
                entry.row = offset + chosen
                entry.y, entry.text, entry.color = 8 + (offset + chosen - 1) * line, comment.escaped_text, comment.color
                entry.clip_top, entry.clip_bottom = math.max(0, entry.y - 4), math.min(area, entry.y + glyph_height)
                lane_set[chosen] = entry
                layout[layout_count] = entry
                endings[#endings + 1] = start + duration
                maximum_duration = math.max(maximum_duration, duration)
            end
        end
    end
    for index = #layout, layout_count + 1, -1 do layout[index] = nil end
end

local function cancel_resize()
    pending_resize = nil
    if resize_timer then resize_timer:kill(); resize_timer = nil end
end

local function virtual_canvas_width()
    local w, h = mp.get_osd_size()
    if not finite(w) or not finite(h) or w <= 0 or h <= 0 then return nil end
    local value = math.floor(w / h * 720 + 0.5)
    return finite(value) and value > 0 and value or nil, h
end

local function canvas_changed(value, canvas_height)
    return math.abs(width - value) > 1
        or not options.scaleWithWindow and math.abs(planned_font - font_size(canvas_height)) > 0.01
end

local function settle_resize(request)
    -- A killed timeout may already have been dispatched. It cannot rebuild a successor file/transfer.
    if pending_resize ~= request or not loaded or request.file ~= serial or request.transfer ~= transfer then return end
    -- Sample once more at the boundary, including paused/temporarily unavailable canvases.
    local latest, canvas_height = virtual_canvas_width()
    if not latest or not canvas_changed(latest, canvas_height) then cancel_resize(); return end
    if math.abs(request.width - latest) > 1 or not options.scaleWithWindow and request.height ~= canvas_height then
        request.width, request.height, request.changed = latest, canvas_height, mp.get_time()
    end
    local due = math.min(request.changed + resize_quiet_seconds, request.started + resize_max_seconds)
    local remaining = due - mp.get_time()
    if remaining > 0 then
        resize_timer = mp.add_timeout(remaining, function() settle_resize(request) end)
        return
    end
    pending_resize, resize_timer = nil, nil
    width, physical_height = request.width, request.height
    rebuild()
    render()
end

local function request_resize(value, canvas_height)
    if not canvas_changed(value, canvas_height) then cancel_resize(); return end
    local now = mp.get_time()
    if pending_resize then
        if math.abs(pending_resize.width - value) > 1 or not options.scaleWithWindow and pending_resize.height ~= canvas_height then
            pending_resize.width, pending_resize.height, pending_resize.changed = value, canvas_height, now
        end
        return
    end
    local request = {width = value, height = canvas_height, started = now, changed = now, file = serial, transfer = transfer}
    pending_resize = request
    resize_timer = mp.add_timeout(resize_quiet_seconds, function() settle_resize(request) end)
end

local function lower_bound(time)
    local low, high = 1, #layout + 1
    while low < high do
        local middle = math.floor((low + high) / 2)
        if layout[middle].start < time then low = middle + 1 else high = middle end
    end
    return low
end

render = function(force_resize)
    local position = mp.get_property_number('time-pos')
    if not loaded or not options.show or not finite(position) then overlay:remove(); return end
    local virtual_width, canvas_height = virtual_canvas_width()
    if not virtual_width then return end
    if force_resize == true then
        cancel_resize()
        if canvas_changed(virtual_width, canvas_height) then
            width, physical_height = virtual_width, canvas_height
            rebuild()
        end
    else request_resize(virtual_width, canvas_height) end
    if #layout == 0 then overlay:remove(); return end
    -- Follow the canvas immediately; keep lane planning until 150 ms quiet (at most 500 ms per batch).
    -- Seeking and new/configured transfers bypass that delay; no full scan is needed for an ordinary seek.
    height = 720
    local ass = {}
    local font = font_size(canvas_height)
    local glyph_height = font_extent(font)
    local line = glyph_height + (options.densityMode == 1 and 2 or 8)
    local rows = math.max(0, math.floor((planned_area - 16) / line))
    local transition = math.abs(font - planned_font) > 0.01 or math.abs(virtual_width - width) > 1
    local occupied = transition and options.preventOverlap and options.densityMode ~= 2 and {} or nil
    local motion = motion_time(position)
    local border = options.borderStyle == 1 and 3 or options.borderStyle == 2 and 0 or 1.5
    local shadow = options.borderStyle == 2 and 3 or 0
    local alpha = math.floor((100 - options.opacity) * 255 / 100 + 0.5)
    for index = lower_bound(motion - maximum_duration), #layout do
        local entry = layout[index]
        if entry.start > motion then break end
        local elapsed = motion - entry.start
        if elapsed >= 0 and elapsed < entry.duration and entry.row <= rows then
            -- The full schedule stays deferred, but visible lane geometry follows the new font now.
            -- Cached widths have a constant padding; scale only their text part until the rebuild.
            local padding = 8 + (options.borderStyle == 1 and 6 or 3)
            local entry_width = (entry.width - padding) * font / planned_font + padding
            local x = entry.mode == 1 and virtual_width - elapsed * (virtual_width + entry_width) / entry.duration or virtual_width / 2
            local left, right = entry.mode == 1 and x or x - entry_width / 2,
                entry.mode == 1 and x + entry_width or x + entry_width / 2
            local y = 8 + (entry.row - 1) * line
            -- A taller font or changed aspect can invalidate a previously safe scroll gap.
            -- Check only the bounded visible entries; the 40,000-comment schedule still rebuilds once.
            if not occupied or not occupied[entry.row] or left >= occupied[entry.row] + 12 then
                if occupied then occupied[entry.row] = right end
                local alignment = entry.mode == 1 and 7 or 8
                local color = entry.color
                local red, green, blue = math.floor(color / 65536), math.floor(color / 256) % 256, color % 256
                -- Clip to the selected area, including strokes/fallback glyphs with unusual metrics.
                ass[#ass + 1] = string.format('{\\an%d\\pos(%.2f,%.2f)\\fn%s\\fs%.2f\\b%d\\bord%.1f\\shad%d\\q2\\clip(0,%.2f,%.2f,%.2f)\\alpha&H%02X&\\1c&H%02X%02X%02X&}%s',
                    alignment, x, y, options.fontFamily, font, options.bold and 1 or 0, border, shadow,
                    math.max(0, y - 4), virtual_width, math.min(planned_area, y + glyph_height), alpha, blue, green, red, entry.text)
            end
        end
    end
    overlay.res_x, overlay.res_y, overlay.data = virtual_width, height, table.concat(ass, '\n')
    overlay:update()
end

local function sync_timer()
    if not timer then return end
    if loaded and options.show and #layout > 0 and not mp.get_property_native('pause', false) then timer:resume()
    else timer:kill() end
    render()
end

local function reset(preserve_clock)
    cancel_resize()
    comments, layout, transfer = {}, {}, nil
    if preserve_clock ~= true then speed_segments = {} end
    options.show = false
    overlay:remove()
    if timer then timer:kill() end
end

local function current(file, id)
    return loaded and tonumber(file) == serial and transfer and tostring(id) == transfer
end

mp.register_script_message('momoka-danmaku-ping', function()
    if loaded then mp.commandv('script-message', 'momoka-danmaku-ready', tostring(serial)) end
end)

mp.register_script_message('momoka-danmaku-begin', function(file, id, json)
    local number = tonumber(id)
    if not loaded or tonumber(file) ~= serial or not finite(number) or number ~= math.floor(number)
        or number <= highest_transfer or #json > 4096 then return end
    local value = utils.parse_json(json)
    if type(value) ~= 'table' then return end
    -- Same-file appearance/filter transfers replace data without rewinding ongoing motion.
    -- File boundaries already discarded this owner; changing sync mode resets it.
    local preserve_clock = #speed_segments > 0 and options.speedSyncPlayback == (value.speedSyncPlayback ~= false)
    reset(preserve_clock)
    highest_transfer = number
    transfer = tostring(id)
    options = {show = value.show == true, opacity = clamp(value.opacity, 10, 100, 70),
        fontSize = clamp(value.fontSize, 20, 64, 36), speed = clamp(value.speed, 0.5, 2, 1),
        area = clamp(value.area, 25, 100, 50), density = math.floor(clamp(value.density, 10, 100, 40)),
        delay = clamp(value.delay, -300, 300, 0), scaleWithWindow = value.scaleWithWindow ~= false,
        avoidSubtitles = value.avoidSubtitles == true, preventOverlap = value.preventOverlap ~= false,
        speedSyncPlayback = value.speedSyncPlayback ~= false, fontFamily = font_family(value.fontFamily),
        bold = value.bold == true, borderStyle = (value.borderStyle == 1 or value.borderStyle == 2) and value.borderStyle or 0,
        densityMode = (value.densityMode == 1 or value.densityMode == 2) and value.densityMode or 0}
    if not preserve_clock then reset_speed_clock() end
end)

mp.register_script_message('momoka-danmaku-chunk', function(file, id, json)
    if not current(file, id) or #json > 300000 then return end
    local entries = utils.parse_json(json)
    if type(entries) ~= 'table' then return end
    for _, entry in ipairs(entries) do
        if #comments >= 40000 then break end
        if type(entry) == 'table' and finite(entry.time) and entry.time >= 0 and entry.time <= 86400
            and (entry.mode == 1 or entry.mode == 4 or entry.mode == 5)
            and finite(entry.color) and entry.color >= 0 and entry.color <= 16777215 and entry.color == math.floor(entry.color)
            and type(entry.text) == 'string' and #entry.text > 0 and #entry.text <= 1200 then
            -- Cached derived values belong to this accepted text, never to fields supplied in JSON.
            entry.measured_font, entry.measured_width, entry.measured_key, entry.escaped_text = nil, nil, nil, nil
            comments[#comments + 1] = entry
        end
    end
end)

mp.register_script_message('momoka-danmaku-commit', function(file, id)
    if not current(file, id) then return end
    table.sort(comments, function(left, right) return left.time < right.time end)
    cancel_resize()
    local canvas_width, canvas_height = virtual_canvas_width()
    width, physical_height = canvas_width or width, canvas_height or physical_height
    rebuild()
    sync_timer()
    mp.commandv('script-message', 'momoka-danmaku-loaded', tostring(serial) .. ':' .. transfer .. ':' .. tostring(#comments))
end)

mp.register_script_message('momoka-danmaku-clear', function(file, id)
    local number = tonumber(id)
    if tonumber(file) == serial and finite(number) and number >= highest_transfer then
        highest_transfer = number
        reset(true)
    end
end)

timer = mp.add_periodic_timer(1 / 30, render)
timer:kill()
mp.observe_property('speed', 'number', function()
    local value = current_speed()
    if not loaded or options.speedSyncPlayback or value == playback_speed then playback_speed = value; return end
    local position = mp.get_property_number('time-pos')
    if not finite(position) then return end
    local motion = motion_time(position)
    while #speed_segments > 0 and speed_segments[#speed_segments].position >= position do
        speed_segments[#speed_segments] = nil
    end
    speed_segments[#speed_segments + 1] = {position = position, motion = motion, speed = value}
    playback_speed = value
    rebuild()
    sync_timer()
end)
local function subtitles_changed()
    if not loaded or not options.avoidSubtitles or display_area() == planned_area then return end
    rebuild()
    sync_timer()
end
for _, property in ipairs({'sid', 'sub-visibility', 'secondary-sid', 'secondary-sub-visibility'}) do
    mp.observe_property(property, 'native', subtitles_changed)
end
mp.observe_property('pause', 'bool', sync_timer)
mp.observe_property('time-pos', 'number', function() if mp.get_property_native('pause', false) then render() end end)
mp.observe_property('osd-dimensions', 'native', render)
mp.register_event('seek', function()
    if loaded and not options.speedSyncPlayback then reset_speed_clock(); rebuild() end
    render(true)
end)
mp.register_event('start-file', function() serial = serial + 1; highest_transfer = 0; loaded = false; reset() end)
mp.register_event('file-loaded', function()
    loaded = true
    mp.commandv('script-message', 'momoka-danmaku-ready', tostring(serial))
end)
mp.register_event('end-file', function() loaded = false; reset() end)
mp.register_event('shutdown', reset)
