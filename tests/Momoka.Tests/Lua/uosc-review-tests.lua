-- Executed only by lua51.dll. All mp values and events are deterministic local fixtures.
package.path = fixture_root .. '/assets/mpv-ui/scripts/uosc/?.lua;' .. package.path
os = nil
debug = nil
io = nil
local function noop() end
local events, observers, commands, results, timeouts = {}, {}, {}, {}, {}
local function timer(callback)
    return {timeout = 0, callback = callback, kill = noop, resume = noop, stop = noop, is_enabled = function() return false end}
end
mp = {
    get_time = function() return 10 end,
    get_property = function(_, fallback) return fallback or '1000' end,
    get_property_native = function(_, fallback) return fallback end,
    add_timeout = function(delay, callback) local value = timer(callback); value.delay = delay; timeouts[#timeouts + 1] = value; return value end,
    add_periodic_timer = function(_, callback) return timer(callback) end,
    observe_property = function(name, kind, callback) observers[name] = observers[name] or {}; table.insert(observers[name], callback) end,
    unobserve_property = noop,
    register_event = function(name, callback) events[name] = events[name] or {}; table.insert(events[name], callback) end,
    unregister_event = noop,
    register_script_message = noop,
    get_script_name = function() return 'uosc' end,
    set_key_bindings = noop, add_key_binding = noop, add_forced_key_binding = noop,
    remove_key_binding = noop, enable_key_bindings = noop, disable_key_bindings = noop,
    get_property_bool = function(_, fallback) return fallback or false end,
    set_property = noop, set_property_bool = noop,
    set_property_native = function(name, value) table.insert(commands, {name, value}); state[name] = value end,
    commandv = function(...) table.insert(commands, {...}) end,
    command = function(command) table.insert(commands, {'command', command}) end,
}
msg = {error = function(message) error(message) end, warn = noop}
utils = {}
assdraw = {ass_new = function() return setmetatable({text = '', scale = 1}, {__index = function() return noop end}) end}
QUARTER_PI_SIN = math.sin(math.pi / 4)
fg, bg, fgt, bgt = 'FFFFFF', '000000', '000000', 'FFFFFF'
options = {flash_duration = 1000, animation_duration = 100, proximity_in = 40, proximity_out = 120, autohide = false,
    volume = 'right', volume_size = 40, volume_border = 1, volume_step = 1, font_scale = 1,
    speed_step_is_factor = false, speed_step = 0.1, controls = '<video,audio>speed',
    controls_size = 32, controls_spacing = 2, controls_margin = 8}
state = {platform = 'fixture', render_delay = 1/60, render_last_time = 0, scale = 1, radius = 2,
    is_idle = false, is_video = true, has_audio = true, volume = 50, volume_max = 130, speed = 1}
config = {refine = {}, cursor_leave_fadeout_elements = {}, opacity = {controls = 0, slider = 1, slider_gauge = 1}}
display = {width = 1280, height = 720}
request_render = noop
require('lib/std')
Elements = require('elements/Elements')
cursor = require('lib/cursor')
require('lib/utils')
request_render = noop
Menu = require('elements/Menu')
require('elements/Curtain'):new()
momoka_click_pause_cancel = noop

local function test(name, action)
    local ok, message = pcall(action)
    results[#results + 1] = (ok and 'PASS ' or 'FAIL ') .. name .. (ok and '' or ': ' .. tostring(message))
    if not ok then fixture_failures = fixture_failures + 1 end
end
fixture_failures = 0
for _, text in ipairs({'中文', '😀文', 'a文'}) do
    test('R10 forward delete removes one complete UTF-8 character: ' .. text, function()
        local menu = setmetatable({current = {id='fixture', search_style='on_demand', items={}, search={query=text,cursor=0}},
            type_to_search=false, search_trigger=noop}, {__index=Menu})
        menu:handle_shortcut(create_shortcut('del'), {event='down',is_mouse=false})
        assert(menu.current.search.query == '文', 'invalid forward deletion')
    end)
end
local volume = require('elements/Volume'):new()
volume.slider.enabled = true
volume.slider:set_coordinates(1200, 100, 1240, 400)
volume.min_visibility = 1
cursor.first_real_mouse_move_received, cursor.hidden = true, false
cursor.x, cursor.y = 1220, 250
volume.slider:render()
test('R11 cursor.leave releases volume drag without any volume write', function()
    cursor:trigger('primary_down', create_shortcut('primary_down'))
    assert(volume.slider.pressed, 'real primary down did not begin drag')
    commands = {}
    cursor:leave()
    assert(not volume.slider.pressed, 'drag remains active')
    assert(#commands == 0, 'leave emitted volume write')
end)
for _, value in ipairs({math.huge, -math.huge, 0/0}) do
    test('R11 invalid cursor coordinate emits no volume command', function()
        volume.slider.pressed = true
        cursor.y = value
        commands = {}
        volume.slider:on_global_mouse_move()
        assert(not volume.slider.pressed and #commands == 0, 'invalid coordinate was applied')
    end)
end
test('R11 valid drag still applies volume', function()
    volume.slider.pressed = true
    cursor.y = 250
    commands = {}
    volume.slider:on_global_mouse_move()
    assert(#commands == 1 and commands[1][1] == 'no-osd' and commands[1][3] == 'volume')
end)
fixture_result = table.concat(results, '\n')
