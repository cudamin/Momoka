-- Independent translated subtitle OSD. No HTTP, secrets, files, sid writes, or speech recognition.
-- Host replies are accepted only while their original cue is still selected and visible.
local mp = require('mp')
local utils = require('mp.utils')
local overlay = mp.create_osd_overlay('ass-events')
overlay.z = -5 -- Keep interactive uosc and normal OSD above the translation.
local active, loaded = nil, false
local published = ''

local function finite(value)
    return type(value) == 'number' and value == value and value ~= math.huge and value ~= -math.huge
end
local function clear()
    active = nil
    overlay.data = ''
    overlay:remove()
end
local function number_matches(property, expected)
    if expected == nil then return true end
    local current = mp.get_property_number(property)
    return finite(current) and math.abs(current - expected) < 0.001
end
local function current_cue(value)
    return loaded and mp.get_property_native('sub-visibility', false)
        and tostring(mp.get_property_native('sid', 'no')) == value.sid
        and mp.get_property('sub-text', '') == value.original
        and number_matches('sub-start', value.start) and number_matches('sub-end', value['end'])
end
local function publish_cue()
    local value, native_cue = '', ''
    local text = mp.get_property('sub-text', '')
    local sid = tostring(mp.get_property_native('sid', 'no'))
    if loaded and mp.get_property_native('sub-visibility', false) and text ~= ''
        and #text <= 20000 and sid ~= 'no' and sid ~= 'auto' and #sid <= 20 then
        local start, ending = mp.get_property_number('sub-start'), mp.get_property_number('sub-end')
        native_cue = {text = text, sid = sid, start = finite(start) and start or nil,
            ['end'] = finite(ending) and ending or nil}
        value = utils.format_json(native_cue)
    end
    if value ~= published then
        published = value
        mp.set_property_native('user-data/momoka-subtitle-translation/cue', native_cue)
    end
end
local function escape(text)
    -- Literal slashes cannot become ASS tags; braces are escaped as in shipped uosc.
    return text:gsub('\\', '\\\239\187\191'):gsub('{', '\\{'):gsub('}', '\\}')
        :gsub('\r\n', '\n'):gsub('\r', '\n'):gsub('[%z\1-\8\11\12\14-\31\127]', ''):gsub('\n', '\\N')
end
local function render()
    if not active then return end
    if not current_cue(active) then clear(); return end
    local width, height = mp.get_osd_size()
    if not finite(width) or not finite(height) or height <= 0 then return end
    local res_x = math.max(360, math.min(3840, 720 * width / height))
    overlay.res_x, overlay.res_y = res_x, 720
    overlay.data = string.format('{\\an2\\pos(%.1f,%.1f)\\fs%d\\b0\\1c&HFFFFFF&\\3c&H000000&\\bord1.5\\shad0\\q2}%s',
        res_x / 2, 720 * active.position / 100, active.fontSize, escape(active.text))
    overlay:update()
end
mp.register_script_message('momoka-subtitle-translation-show', function(payload)
    if type(payload) ~= 'string' or #payload > 100000 then return end
    local value = utils.parse_json(payload)
    if type(value) ~= 'table' or type(value.original) ~= 'string' or #value.original > 20000
        or type(value.text) ~= 'string' or #value.text == 0 or #value.text > 60000
        or type(value.sid) ~= 'string' or #value.sid > 20
        or (value.start ~= nil and not finite(value.start)) or (value['end'] ~= nil and not finite(value['end']))
        or not finite(value.fontSize) or not finite(value.position) then return end
    value.fontSize = math.floor(math.max(20, math.min(64, value.fontSize)))
    value.position = math.max(10, math.min(90, value.position))
    if not current_cue(value) then return end
    active = value
    render()
end)
mp.register_script_message('momoka-subtitle-translation-clear', clear)
mp.register_event('start-file', function() loaded = false; clear(); publish_cue() end)
mp.register_event('file-loaded', function() loaded = true; clear(); publish_cue() end)
mp.register_event('end-file', function() loaded = false; clear(); publish_cue() end)
mp.register_event('seek', function() clear(); publish_cue() end)
mp.observe_property('sub-text', 'string', function() publish_cue(); if active then render() end end)
mp.observe_property('sid', 'native', function() publish_cue(); if active then render() end end)
mp.observe_property('sub-visibility', 'bool', function() publish_cue(); if active then render() end end)
mp.observe_property('sub-start', 'number', publish_cue)
mp.observe_property('sub-end', 'number', publish_cue)
mp.observe_property('osd-dimensions', 'native', function() if active then render() end end)
mp.add_periodic_timer(0.05, function() publish_cue(); render() end)
-- load-script can arrive after file-loaded; initialise from current file without relying on a replayed event.
loaded = mp.get_property_native('path') ~= nil
mp.set_property_number('user-data/momoka-subtitle-translation/ready', 1)
publish_cue()
