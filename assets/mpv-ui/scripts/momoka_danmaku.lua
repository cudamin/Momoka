-- Momoka's independent danmaku layer. No HTTP, media reads, credentials or subtitle tracks.
-- Every transfer is bound to this script's file serial and a host load id; stale chunks are discarded.
local mp = require('mp')
local utils = require('mp.utils')
local overlay = mp.create_osd_overlay('ass-events')
overlay.z = -10 -- uosc/normal OSD stays above comments.
local serial, loaded, transfer = 0, false, nil
local highest_transfer = 0
local comments, layout = {}, {}
local options = {show = false, opacity = 70, fontSize = 36, speed = 1, area = 50, density = 40, delay = 0}
local width, height = 1280, 720
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

local function text_width(text, font)
    -- Conservative Unicode widths. Overestimating is preferable to collisions or clipping through the next lane.
    local units = 0
    for character in text:gmatch('[%z\1-\127\194-\244][\128-\191]*') do
        units = units + (#character == 1 and (character == ' ' and 0.5 or 1) or 1.1)
    end
    return math.max(font, units * font + 8)
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
    local font = options.fontSize
    local line = font + 8
    local rows = math.max(1, math.floor((720 * options.area / 100 - 16) / line))
    local has_scroll, has_top, has_bottom = false, false, false
    for _, comment in ipairs(comments) do
        has_scroll = has_scroll or comment.mode == 1
        has_top = has_top or comment.mode == 5
        has_bottom = has_bottom or comment.mode == 4
    end
    local top_rows = has_top and math.max(1, math.floor(rows / 5)) or 0
    local bottom_rows = has_bottom and math.max(1, math.floor(rows / 5)) or 0
    if not has_scroll then
        top_rows = has_top and (has_bottom and math.ceil(rows / 2) or rows) or 0
        bottom_rows = has_bottom and rows - top_rows or 0
    end
    if top_rows + bottom_rows > rows then bottom_rows = math.max(0, rows - top_rows) end
    local scroll_rows = has_scroll and math.max(0, rows - top_rows - bottom_rows) or 0
    local lanes = {scroll = {}, top = {}, bottom = {}}
    local endings = {}
    for _, comment in ipairs(comments) do
        local start = comment.time + options.delay
        local duration = (comment.mode == 1 and 8 or 6) / options.speed
        -- Compact the bounded density queue in place instead of allocating one table per comment.
        local kept = 0
        for index = 1, #endings do
            if endings[index] > start then kept = kept + 1; endings[kept] = endings[index] end
        end
        for index = #endings, kept + 1, -1 do endings[index] = nil end
        if #endings < options.density then
            if comment.measured_font ~= font then
                comment.measured_width = text_width(comment.text, font)
                comment.measured_font = font
            end
            local comment_width = comment.measured_width
            local lane_set, count, offset
            if comment.mode == 1 then lane_set, count, offset = lanes.scroll, scroll_rows, top_rows
            elseif comment.mode == 5 then lane_set, count, offset = lanes.top, top_rows, 0
            else lane_set, count, offset = lanes.bottom, bottom_rows, rows - bottom_rows end
            for lane = 1, count do
                local previous = lane_set[lane]
                local free = comment.mode == 1 and eligible(previous, start, comment_width, duration)
                    or comment.mode ~= 1 and (not previous or previous.start + previous.duration <= start)
                if free then
                    comment.escaped_text = comment.escaped_text or escape(comment.text)
                    layout_count = layout_count + 1
                    local entry = layout[layout_count] or {}
                    entry.start, entry.duration, entry.width, entry.mode = start, duration, comment_width, comment.mode
                    entry.y, entry.text, entry.color = 8 + (offset + lane - 1) * line, comment.escaped_text, comment.color
                    lane_set[lane] = entry
                    layout[layout_count] = entry
                    endings[#endings + 1] = start + duration
                    break
                end
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
    return finite(value) and value > 0 and value or nil
end

local function settle_resize(request)
    -- A killed timeout may already have been dispatched. It cannot rebuild a successor file/transfer.
    if pending_resize ~= request or not loaded or request.file ~= serial or request.transfer ~= transfer then return end
    -- Sample once more at the boundary, including paused/temporarily unavailable canvases.
    local latest = virtual_canvas_width()
    if not latest or math.abs(width - latest) <= 1 then cancel_resize(); return end
    if math.abs(request.width - latest) > 1 then request.width, request.changed = latest, mp.get_time() end
    local due = math.min(request.changed + resize_quiet_seconds, request.started + resize_max_seconds)
    local remaining = due - mp.get_time()
    if remaining > 0 then
        resize_timer = mp.add_timeout(remaining, function() settle_resize(request) end)
        return
    end
    pending_resize, resize_timer = nil, nil
    width = request.width
    rebuild()
    render()
end

local function request_resize(value)
    if math.abs(width - value) <= 1 then cancel_resize(); return end
    local now = mp.get_time()
    if pending_resize then
        if math.abs(pending_resize.width - value) > 1 then
            pending_resize.width, pending_resize.changed = value, now
        end
        return
    end
    local request = {width = value, started = now, changed = now, file = serial, transfer = transfer}
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
    if not loaded or not options.show or not finite(position) or #layout == 0 then overlay:remove(); return end
    local virtual_width = virtual_canvas_width()
    if not virtual_width then return end
    if force_resize == true then
        cancel_resize()
        if math.abs(width - virtual_width) > 1 then width = virtual_width; rebuild() end
    else request_resize(virtual_width) end
    -- Follow the canvas immediately; keep lane planning until 150 ms quiet (at most 500 ms per batch).
    -- Seeking and new/configured transfers bypass that delay; no full scan is needed for an ordinary seek.
    height = 720
    local ass = {}
    local alpha = math.floor((100 - options.opacity) * 255 / 100 + 0.5)
    for index = lower_bound(position - 16), #layout do
        local entry = layout[index]
        if entry.start > position then break end
        local elapsed = position - entry.start
        if elapsed >= 0 and elapsed < entry.duration then
            local x = entry.mode == 1 and virtual_width - elapsed * (virtual_width + entry.width) / entry.duration or virtual_width / 2
            local alignment = entry.mode == 1 and 7 or 8
            local color = entry.color
            local red, green, blue = math.floor(color / 65536), math.floor(color / 256) % 256, color % 256
            ass[#ass + 1] = string.format('{\\an%d\\pos(%.2f,%.2f)\\fnMicrosoft YaHei\\fs%.2f\\bord1.5\\shad0\\q2\\alpha&H%02X&\\1c&H%02X%02X%02X&}%s',
                alignment, x, entry.y, options.fontSize, alpha, blue, green, red, entry.text)
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

local function reset()
    cancel_resize()
    comments, layout, transfer = {}, {}, nil
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
    reset()
    highest_transfer = number
    transfer = tostring(id)
    options = {show = value.show == true, opacity = clamp(value.opacity, 10, 100, 70),
        fontSize = clamp(value.fontSize, 20, 64, 36), speed = clamp(value.speed, 0.5, 2, 1),
        area = clamp(value.area, 25, 100, 50), density = math.floor(clamp(value.density, 10, 100, 40)),
        delay = clamp(value.delay, -300, 300, 0)}
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
            entry.measured_font, entry.measured_width, entry.escaped_text = nil, nil, nil
            comments[#comments + 1] = entry
        end
    end
end)

mp.register_script_message('momoka-danmaku-commit', function(file, id)
    if not current(file, id) then return end
    table.sort(comments, function(left, right) return left.time < right.time end)
    cancel_resize()
    width = virtual_canvas_width() or width
    rebuild()
    sync_timer()
    mp.commandv('script-message', 'momoka-danmaku-loaded', tostring(serial) .. ':' .. transfer .. ':' .. tostring(#comments))
end)

mp.register_script_message('momoka-danmaku-clear', function(file, id)
    local number = tonumber(id)
    if tonumber(file) == serial and finite(number) and number >= highest_transfer then
        highest_transfer = number
        reset()
    end
end)

timer = mp.add_periodic_timer(1 / 30, render)
timer:kill()
mp.observe_property('pause', 'bool', sync_timer)
mp.observe_property('time-pos', 'number', function() if mp.get_property_native('pause', false) then render() end end)
mp.observe_property('osd-dimensions', 'native', render)
mp.register_event('seek', function() render(true) end)
mp.register_event('start-file', function() serial = serial + 1; highest_transfer = 0; loaded = false; reset() end)
mp.register_event('file-loaded', function()
    loaded = true
    mp.commandv('script-message', 'momoka-danmaku-ready', tostring(serial))
end)
mp.register_event('end-file', function() loaded = false; reset() end)
mp.register_event('shutdown', reset)
