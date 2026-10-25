do
	local native_mp = {}
	for key, value in pairs(mp) do native_mp[key] = value end
	local results = {}
	local clock, timers, commands, properties, keys, observers
	local original_request_render, original_tween = request_render, tween
	local original_primary_up = {}
	for _, callback in ipairs(cursor.handlers.primary_up) do original_primary_up[#original_primary_up + 1] = callback end
	local function require_equal(actual, expected, message)
		if actual ~= expected then error((message or 'mismatch') .. ': ' .. tostring(actual) .. ' ~= ' .. tostring(expected)) end
	end
	local function emit(name)
		for _, callback in ipairs(fixture_events[name] or {}) do callback({event = name}) end
	end
	local function advance(seconds)
		clock = clock + seconds
		for _ = 1, 10 do
			local due = false
			for _, timer in ipairs(timers) do
				if timer.enabled and timer.at <= clock then
					timer.enabled, due = false, true
					timer.callback()
				end
			end
			if not due then return end
		end
	end
	local function count_command(first, second)
		local count = 0
		for _, command in ipairs(commands) do
			if command[1] == first and (second == nil or command[2] == second) then count = count + 1 end
		end
		return count
	end
	local function canvas()
		cursor:clear_zones()
		momoka_click_pause_zone()
		cursor.x, cursor.y, cursor.hidden, cursor.disabled = 400, 350, false, false
	end
	local function down() cursor:trigger('primary_down', create_shortcut('primary_down')) end
	local function up() cursor:trigger('primary_up', create_shortcut('primary_up')) end
	local function cancel_up() cursor:create_primary_handler()({event = 'up', canceled = true}) end
	local function menu(data)
		local value
		local item = Menu:open(data or {type = 'fixture', items = {{title = 'one', value = 1}, {title = 'two', value = 2}, {title = 'three', value = 3}}},
			function(event) if event.type == 'activate' then value = event.value end end)
		return item, function() return value end
	end
	local function reset()
		if Menu:is_open() then Menu:close(true) end
		if Elements.danmaku_input then Elements.danmaku_input:reset() end
		clock, timers, commands, properties, keys, observers = 100, {}, {}, {}, {}, {}
		display.width, display.height, display.bx, display.by, display.initialized = 1280, 720, 1280, 720, true
		state.scale, state.fullormaxed, state.pause, state.duration, state.time, state.is_video = 1, false, false, 1000, 100, true
		state.ime_active = false
		cursor.last_events = {}
		cursor.handlers.primary_up = {}
		for _, callback in ipairs(original_primary_up) do cursor.handlers.primary_up[#cursor.handlers.primary_up + 1] = callback end
		cursor.history:clear()
		cursor.first_real_mouse_move_received = true
		cursor.x, cursor.y, cursor.hidden, cursor.disabled = 400, 350, false, false
		Elements.curtain.dependents, Elements.curtain.opacity = {}, 0
		momoka_click_pause_pending, momoka_click_pause_press_last = nil, nil
		momoka_click_pause_second_half, momoka_click_pause_focus_at = false, nil
		if momoka_click_pause_reset then momoka_click_pause_reset() end
		canvas()
	end
	local function test(name, run)
		local ok, detail = pcall(function() reset(); run() end)
		results[#results + 1] = {name = name, passed = ok, detail = ok and '' or tostring(detail)}
	end
	native_mp.add_timeout(0.2, function()
		local ok, fatal = pcall(function()
			mp.get_time = function() return clock end
			mp.add_timeout = function(delay, callback)
				local timer = {timeout = delay, at = clock + delay, callback = callback, enabled = true}
				function timer:kill() self.enabled = false end
				function timer:resume() self.at, self.enabled = clock + self.timeout, true end
				function timer:is_enabled() return self.enabled end
				timers[#timers + 1] = timer
				return timer
			end
			mp.commandv = function(...) commands[#commands + 1] = {...}; return true end
			mp.command = function(value) commands[#commands + 1] = {'command', value}; return true end
			mp.set_property_native = function(name, value) properties[name] = value; return true end
			mp.set_property_bool = mp.set_property_native
			mp.set_property = mp.set_property_native
			mp.get_property_native = function(name, fallback)
				if properties[name] ~= nil then return properties[name] end
				if name == 'input-doubleclick-time' then return 300 end
				if name == 'input-bindings' then return {{key = 'SPACE'}, {key = 'F8'}, {key = 'Ctrl+i'}} end
				return native_mp.get_property_native(name, fallback)
			end
			mp.get_property_bool = function(name, fallback)
				if properties[name] ~= nil then return properties[name] end
				return fallback or false
			end
			mp.add_forced_key_binding = function(key, name, callback) keys[key] = {name = name, callback = callback} end
			mp.remove_key_binding = function(name)
				for key, binding in pairs(keys) do if binding.name == name then keys[key] = nil end end
			end
			mp.observe_property = function(name, _, callback) observers[name] = callback; callback(name, mp.get_property_native(name)) end
			mp.unobserve_property = function() end
			request_render = function() end
			tween = function(_, to, setter, duration_or_callback, callback)
				setter(type(to) == 'function' and to() or to)
				local done = type(duration_or_callback) == 'function' and duration_or_callback or callback
				if done then done() end
				return nil
			end
			clock, timers, commands, properties, keys = 100, {}, {}, {}, {}
			test('menu close does not click through to underlying button', function()
				local button = {ax = 350, ay = 300, bx = 450, by = 400}
				local clicked = 0
				cursor:zone('primary_click', button, function() clicked = clicked + 1 end)
				cursor:zone('primary_down', display, function() end)
				down()
				cursor:clear_zones()
				cursor:zone('primary_click', button, function() clicked = clicked + 1 end)
				advance(0.2); up()
				require_equal(clicked, 0)
			end)
			test('a release cannot activate a newly revealed button', function()
				local clicked = 0
				down()
				cursor:zone('primary_click', {ax = 350, ay = 300, bx = 450, by = 400}, function() clicked = clicked + 1 end)
				advance(0.05); up()
				require_equal(clicked, 0)
			end)
			test('secondary compound clicks keep the secondary event identity', function()
				local event
				cursor:zone('secondary_click', {ax = 350, ay = 300, bx = 450, by = 400}, function(shortcut) event = shortcut.key end)
				cursor:trigger('secondary_down', create_shortcut('secondary_down'))
				cursor:trigger('secondary_up', create_shortcut('secondary_up'))
				require_equal(event, 'secondary_click')
			end)
			test('button capture survives a render with a new rectangle', function()
				local owner, clicked = {}, 0
				cursor:zone('primary_click', {ax = 350, ay = 300, bx = 450, by = 400, input_owner = owner}, function() clicked = clicked + 1 end)
				down(); cursor:clear_zones()
				cursor:zone('primary_click', {ax = 350, ay = 300, bx = 450, by = 400, input_owner = owner}, function() clicked = clicked + 1 end)
				up(); require_equal(clicked, 1)
			end)
			test('normal compound button click fires exactly once', function()
				local clicked = 0
				cursor:zone('primary_click', {ax = 350, ay = 300, bx = 450, by = 400}, function() clicked = clicked + 1 end)
				down(); advance(0.05); up()
				require_equal(clicked, 1)
			end)
			test('centered to anchored menu replacement releases curtain', function()
				menu()
				require_equal(#Elements.curtain.dependents, 1)
				local replacement = menu({type = 'picture', momoka_anchor = true, items = {{title = 'one', value = 1}}})
				replacement:close(true)
				require_equal(#Elements.curtain.dependents, 0)
				require_equal(Elements.curtain.opacity, 0)
			end)
			test('centered menu replacement owns one curtain registration', function()
				menu(); menu(); menu()
				require_equal(#Elements.curtain.dependents, 1)
				Menu:close(true)
				require_equal(#Elements.curtain.dependents, 0)
			end)
			test('menu keypad enter activates selected item', function()
				local item, picked = menu()
				item:select_index(2)
				item:handle_shortcut(create_shortcut('kp_enter'), {event = 'press', is_mouse = false})
				require_equal(picked(), 2)
			end)
			-- 尺寸门槛跟着 2026-10-17 的 menu_scale 0.88 下调一档（原 hint 12、最小宽 200）：这两条守的是
			-- 「还看得清、还点得中」，不是某个绝对像素；缩放档本身由下面的全屏 1.3× 与 hidpi 两条用例钉住。
			test('menu rows keep text legible with space for a check and hints', function()
				local item = menu()
				assert(item.item_height >= item.font_size + 12, 'rows crowd the text')
				assert(item.font_size_hint >= 11 and item.font_size_hint < item.font_size, 'hint hierarchy lost')
				assert(item.root.width >= 190, 'short labels collapse the hit target')
				assert(item.item_indent > item.font_size + item.hover_inset, 'check overlaps the title')
				item:close(true)
			end)
			test('a separator after an action keeps its text and clickable row', function()
				local item, picked = menu({items = {
					{title = 'Playback information', value = 1, separator = true},
					{title = 'Settings', value = 2},
				}})
				assert(item:render().text:find('Playback information', 1, true), 'separator erased its action')
				local panel = item.layout[1]
				cursor.x, cursor.y = panel.x + 40, panel.y + item.padding + item.item_height / 2
				item:update_proximity()
				item:handle_cursor_down(); item:handle_cursor_up()
				require_equal(picked(), 1)
			end)
			test('a separator gap cannot activate its preceding action', function()
				local item, picked = menu({items = {
					{title = 'Action', value = 1, separator = true},
					{title = 'Next action', value = 2},
				}})
				item:select_index(1); item:render()
				local panel = item.layout[1]
				cursor.x, cursor.y = panel.x + 40, panel.y + item.padding + item.item_height + item.padding
				item:update_proximity()
				item:handle_cursor_down(); item:handle_cursor_up()
				require_equal(picked(), nil)
			end)
			test('a separated submenu remains visible and opens with the keyboard', function()
				local item = menu({items = {
					{title = 'Shaders', separator = true, items = {{title = 'Automatic', value = 1}}},
					{title = 'Settings', value = 2},
				}})
				assert(item:render().text:find('Shaders', 1, true), 'separator erased its submenu')
				item:select_index(1)
				item:handle_shortcut(create_shortcut('right'), {event = 'press', is_mouse = false})
				require_equal(item.current.title, 'Shaders')
			end)
			test('menu scales 1.3x live while fullscreen or maximized', function()
				local item = menu()
				state.fullscreen = true
				update_fullormaxed()
				require_equal(state.scale, 1.3)
				require_equal(item.font_size, 18) -- round(16 * 1.3 * menu_scale 0.88)
				require_equal(item.item_height, 36)
				state.fullscreen, state.maximized = false, true
				update_fullormaxed()
				require_equal(state.scale, 1.3)
				require_equal(item.font_size, 18)
				state.fullscreen, state.maximized = false, false
				update_fullormaxed()
				require_equal(state.scale, 1)
				require_equal(item.font_size, 14)
				item:close(true)
			end)
			test('menu font follows the hidpi scale like the integrated flyout', function()
				local item = menu()
				state.hidpi_scale = 1.5
				update_display_dimensions() -- state.scale 重算不依赖 osd 画布，harness 里 vo=null 也一样
				item:on_display()
				require_equal(item.font_size, 21)
				state.hidpi_scale = 1
				update_display_dimensions()
				item:on_display()
				require_equal(item.font_size, 14)
				item:close(true)
			end)
			test('menu pointer click supersedes keyboard selected row', function()
				local item, picked = menu()
				cursor.x, cursor.y = item.ax + item.padding + 10, item.current.top + item.scroll_step * 1.5
				item:update_proximity()
				item.mouse_nav = false
				item:select_index(3)
				item:handle_cursor_down()
				item:handle_cursor_up(create_shortcut('primary_up'))
				require_equal(picked(), 2)
			end)
			test('menu drag cancels on pointer leaving without infinite scroll', function()
				local rows = {}; for i = 1, 100 do rows[i] = {title = tostring(i), value = i} end
				local item = menu({type = 'fixture', items = rows})
				item:set_scroll_to(500)
				cursor.x, cursor.y = item.ax + 40, item.current.top + 40
				item:update_proximity(); item:handle_cursor_down()
				cursor:leave()
				require_equal(item.drag_last_y, nil)
				require_equal(item.is_dragging, false)
				require_equal(item.current.scroll_y, 500)
			end)
			test('timeline cancellation restores the original pause state', function()
				local item = Elements.timeline
				item:update_dimensions(); item:handle_cursor_down()
				require_equal(properties.pause, true)
				item:on_global_mouse_leave(); item:handle_cursor_up()
				require_equal(properties.pause, false)
			end)
			test('timeline release finishes fast dragging with an exact seek', function()
				local item = Elements.timeline
				item:update_dimensions(); item:handle_cursor_down(); item:set_from_cursor(true)
				cursor.x = 700
				item:handle_cursor_up()
				local last
				for _, command in ipairs(commands) do if command[1] == 'seek' then last = command end end
				require_equal(last[3], 'absolute+exact')
				require_equal(properties.pause, false)
			end)
			test('timeline paused drag stays paused after cancellation', function()
				state.pause = true
				local item = Elements.timeline
				item:update_dimensions(); item:handle_cursor_down(); item:on_global_mouse_leave()
				require_equal(properties.pause, true)
			end)
			test('single picture click followed by a button retains its pause', function()
				down(); advance(0.05); up(); advance(0.1)
				cursor:zone('primary_click', {ax = 350, ay = 300, bx = 450, by = 400}, function() end)
				down(); advance(0.25)
				require_equal(count_command('cycle', 'pause'), 1)
			end)
			test('double picture click never toggles pause', function()
				down(); advance(0.05); up(); advance(0.1); down(); advance(0.05); up(); advance(0.5)
				require_equal(count_command('cycle', 'pause'), 0)
			end)
			test('native window drag cancellation never pauses even within the click distance', function()
				down(); advance(0.05); cursor.x = cursor.x + 4; cancel_up(); advance(0.5); up(); advance(0.5)
				require_equal(count_command('cycle', 'pause'), 0)
				require_equal(cursor.last_events.primary_down, nil)
			end)
			test('click after canceled drag starts a new gesture', function()
				down(); advance(0.05); cancel_up(); advance(0.05); down(); advance(0.05); up(); advance(0.5)
				require_equal(count_command('cycle', 'pause'), 1)
			end)
			test('canceling a later control gesture retains the earlier picture click', function()
				down(); advance(0.05); up(); advance(0.05)
				cursor:zone('primary_click', {ax = 350, ay = 300, bx = 450, by = 400}, function() error('canceled click') end)
				down(); advance(0.05); cancel_up(); advance(0.5)
				require_equal(count_command('cycle', 'pause'), 1)
			end)
			test('canceled release skips menu activation but runs release cleanup', function()
				local clicked, cleaned = 0, false
				cursor:zone('primary_up', display, function() clicked = clicked + 1 end)
				cursor:once('primary_up', function() cleaned = true end)
				down(); cancel_up()
				require_equal(clicked, 0); require_equal(cleaned, true)
			end)
			test('complex left binding preserves normal down and up with modifiers', function()
				properties['mouse-pos'] = {x = 400, y = 350, hover = true}
				local handler = cursor:create_primary_handler('ctrl')
				handler({event = 'down'}); advance(0.05); handler({event = 'repeat'}); handler({event = 'up'}); advance(0.5)
				require_equal(count_command('cycle', 'pause'), 1)
			end)
			test('picture press cannot survive across a file boundary', function()
				down(); emit('start-file'); advance(0.05); canvas(); up(); advance(0.5)
				require_equal(count_command('cycle', 'pause'), 0)
			end)
			test('right edge submenu stays within the display', function()
				cursor.x, cursor.y = 1270, 300
				local item = menu({type = 'picture', momoka_anchor = true, items = {
					{title = 'Parent', items = {{title = 'Child', value = 1}}}
				}})
				item:select_index(1)
				local boxes = {}
				local original = assdraw.ass_new
				assdraw.ass_new = function()
					return setmetatable({}, {__index = function(_, name)
						if name == 'rect' then return function(_, ax, ay, bx, by, opts)
							if opts and opts.color == options.menu_background_color then boxes[#boxes + 1] = {ax = ax, bx = bx} end
						end end
						return function() end
					end})
				end
				local ok, err = pcall(function() item:render() end)
				assdraw.ass_new = original
				assert(ok, err)
				require_equal(#boxes, 2)
				for _, box in ipairs(boxes) do assert(box.ax >= 0 and box.bx <= display.width, 'submenu outside display') end
				item:close(true)
			end)
			test('nested submenus keep opening left at the right screen edge', function()
				display.width, display.bx = 640, 640
				cursor.x, cursor.y = 630, 100
				local item = menu({momoka_anchor = true, items = {
					{title = 'Parent', items = {{title = 'Child', items = {{title = 'Leaf', value = 1}}}}},
				}})
				item:select_index(1); item:activate_selected_item(); item:select_index(1)
				local panels = item:compute_layout()
				require_equal(#panels, 3)
				assert(panels[2].x < panels[1].x and panels[3].x < panels[2].x, 'submenu flipped over its ancestor')
				for _, panel in ipairs(panels) do assert(panel.x >= 0 and panel.x + panel.w <= display.width) end
			end)
			test('hovering an overlapping submenu does not change the covered parent row', function()
				display.width, display.bx = 640, 640
				cursor.x, cursor.y = 630, 100
				local item = menu({momoka_anchor = true, items = {
					{title = 'Parent', items = {
						{title = 'Child', items = {{title = 'First', value = 1}, {title = 'Second', value = 2}}},
						{title = 'Covered sibling', value = 3},
					}},
				}})
				item:select_index(1); item:activate_selected_item(); item:select_index(1)
				local panels = item:compute_layout()
				cursor.x = panels[3].x + panels[3].w - 10
				cursor.y = panels[3].y + item.padding + item.item_height * 1.5
				item.mouse_nav = true; cursor.history:clear()
				item:render()
				require_equal(item.current.selected_index, 1)
				require_equal(#item:compute_layout(), 3)
			end)
			test('the gap between cascading menus cannot activate an item', function()
				local item, picked = menu({items = {{title = 'Parent', items = {{title = 'Child', value = 1}}}}})
				item:select_index(1); item:render()
				local panels = item.layout
				cursor.x = (panels[1].x + panels[1].w + panels[2].x) / 2
				cursor.y = panels[1].y + item.padding + item.item_height / 2
				item:update_proximity()
				item:handle_cursor_down(); item:handle_cursor_up()
				require_equal(picked(), nil)
				require_equal(item.current, item.root)
				require_equal(#item:compute_layout(), 2)
			end)
			test('submenu preview guard survives mouse navigation toward it', function()
				-- 2026-10-07 复刻参考菜单实拍抓的崩：预览面板表是 x/y/w/h，去程守卫把它喂给
				-- direction_to_rectangle_distance（要 ax/ay/bx/by）＝算 nil，uosc 脚本整只死掉。
				-- 钉住：mouse_nav 下悬停子菜单父行再渲染，必须活着（选中行被悬停改写是另一回事）。
				cursor.x, cursor.y = 640, 360
				cursor.history:clear()
				local item = menu({type = 'fixture', items = {
					{title = 'Parent', items = {{title = 'Child', value = 1}}},
					{title = 'Leaf', value = 2},
				}})
				item.mouse_nav = true
				item:select_index(1)
				local ok, err = pcall(function() item:render() end)
				assert(ok, 'render crashed with a hovered submenu row: ' .. tostring(err))
				item:close(true)
			end)
			test('file boundary closes stale menus and releases the curtain', function()
				menu(); emit('start-file')
				require_equal(Menu:is_open(), nil)
				require_equal(#Elements.curtain.dependents, 0)
			end)
			test('external track without title uses a fallback label', function()
				properties['track-list'] = {{type = 'sub', id = 1, external = true}}
				local open = create_select_tracklist_type_menu_opener({type = 'sub', prop = 'sid', title = 'Subtitles'})
				open()
				require_equal(Menu:is_open().current.items[1].value, 1)
			end)
			test('host shortcuts rebind and stay suspended while a menu owns input', function()
				fixture_messages['momoka-shortcuts'](utils.format_json({['Ctrl+F9'] = 'toggle-pause', SPACE = ''}))
				assert(keys['Ctrl+F9'], 'new key not installed')
				keys.SPACE.callback()
				require_equal(count_command('script-message', 'momoka-shortcut'), 0)
				keys['Ctrl+F9'].callback()
				require_equal(count_command('script-message', 'momoka-shortcut'), 1)
				menu()
				require_equal(keys['Ctrl+F9'], nil)
				fixture_messages['momoka-shortcuts'](utils.format_json({['Ctrl+F8'] = 'toggle-pause', SPACE = ''}))
				require_equal(keys['Ctrl+F8'], nil)
				Menu:close(true)
				assert(keys['Ctrl+F8'], 'menu close did not restore latest bindings')
				require_equal(keys['Ctrl+F9'], nil)
				fixture_messages['momoka-shortcuts']('{}')
			end)
			test('closing a menu re-enables unchanged host bindings', function()
				fixture_messages['momoka-shortcuts'](utils.format_json({p = 'previous-episode'}))
				menu(); Menu:close(true)
				assert(keys.p, 'unchanged binding not restored')
				keys.p.callback()
				require_equal(count_command('script-message', 'momoka-shortcut'), 1)
				fixture_messages['momoka-shortcuts']('{}')
			end)
			test('danmaku control follows host capability and file boundaries', function()
				fixture_messages['momoka-danmaku-visibility']('yes')
				assert(state.has_danmaku, 'configured danmaku button is hidden')
				fixture_messages['momoka-danmaku-visibility']('no')
				assert(not state.has_danmaku, 'disabled danmaku button remains visible')
				fixture_messages['momoka-danmaku-visibility']('yes')
				emit('start-file')
				assert(not state.has_danmaku, 'file switch retained previous capability')
			end)
			local function input_state(values)
				local data = {visible = true, canSend = true, busy = false, context = 'fixture-episode-1'}
				for key, value in pairs(values or {}) do data[key] = value end
				fixture_messages['momoka-danmaku-input-state'](utils.format_json(data))
				return Elements.danmaku_input
			end
			local function draw_input(width, title, scale)
				display.width, display.bx = width or 1280, width or 1280
				state.scale, state.has_playlist = scale or 1, false
				options.top_bar_controls, options.top_bar_title = 'right', 'yes'
				local bar = Elements.top_bar
				bar.enabled, bar.forced_visibility = true, 1
				bar.render_titles.main, bar.render_titles.alt = title or '测试标题', nil
				bar:update_dimensions()
				canvas()
				bar:render()
				local input = Elements.danmaku_input
				local rendered = input:render()
				return input, rendered, bar
			end
			local function press(key, text, event)
				assert(keys[key], 'missing editing binding ' .. key)
				keys[key].callback({event = event or 'press', key_text = text, key_name = key, is_mouse = false})
			end
			local function answer(input, success, message, changes)
				local data = {context = input.context, requestId = input.pending.id, success = success, message = message or ''}
				for key, value in pairs(changes or {}) do data[key] = value end
				fixture_messages['momoka-danmaku-input-result'](utils.format_json(data))
			end
			test('danmaku input follows visibility and matching without exposing a send button', function()
				local input = input_state({canSend = false})
				local _, rendered = draw_input()
				assert(rendered.text:find('请先选择弹幕库', 1, true))
				for _, zone in ipairs(cursor.zones) do assert(zone.hitbox.input_owner ~= input.send_owner, 'send hitbox exists without match') end
				input:focus(); input:insert('保留草稿')
				input_state({visible = false})
				require_equal(input.focused, false)
				require_equal(input:render(), nil)
				require_equal(input.text, '保留草稿')
				input_state()
				draw_input()
				require_equal(input.text, '保留草稿')
				input_state({context = 'fixture-episode-2'})
				require_equal(input.text, '')
			end)
			test('danmaku Unicode editing owns shortcuts and restores the latest binding and IME', function()
				fixture_messages['momoka-shortcuts'](utils.format_json({['Ctrl+F9'] = 'toggle-pause'}))
				local input = input_state()
				draw_input()
				properties['input-ime'] = false
				input:focus()
				require_equal(properties['input-ime'], true)
				require_equal(keys['Ctrl+F9'], nil)
				require_equal(Elements.top_bar:get_visibility(), 1)
				press('any_unicode', '中文'); press('SPACE'); press('any_unicode', '🙂')
				require_equal(input.text, '中文 🙂')
				press('BS'); require_equal(input.text, '中文 ')
				press('LEFT'); press('DEL'); require_equal(input.text, '中文')
				press('HOME'); press('DEL'); require_equal(input.text, '文')
				press('END'); press('any_unicode', '字', 'up'); require_equal(input.text, '文')
				press('F8'); press('Ctrl+i')
				require_equal(count_command('cycle', 'pause'), 0)
				fixture_messages['momoka-shortcuts'](utils.format_json({['Ctrl+F8'] = 'toggle-pause'}))
				require_equal(keys['Ctrl+F8'], nil)
				input:blur()
				require_equal(properties['input-ime'], false)
				assert(keys['Ctrl+F8'], 'latest host binding was not restored')
				fixture_messages['momoka-shortcuts']('{}')
			end)
			test('danmaku paste and select all respect the 100 UTF16 unit bound', function()
				local input = input_state()
				draw_input(); input:focus()
				local original_clipboard = get_clipboard
				get_clipboard = function() return '第一行\n第二行\t🙂' end
				local ok, detail = pcall(function() press('Ctrl+v') end)
				get_clipboard = original_clipboard
				assert(ok, detail)
				require_equal(input.text, '第一行 第二行 🙂')
				press('Ctrl+a'); input:insert(string.rep('🙂', 51))
				require_equal(input.text, string.rep('🙂', 50))
				press('Ctrl+a'); input:insert(string.rep('字', 101))
				require_equal(input.text, string.rep('字', 100))
				press('Ctrl+a'); press('BS'); require_equal(input.text, '')
			end)
			test('danmaku submit is single flight and correlated acknowledgements preserve newer drafts', function()
				local input = input_state()
				draw_input(); input:focus(); input:insert('第一条')
				press('ENTER'); press('ENTER', nil, 'repeat'); input:submit()
				require_equal(count_command('script-message', 'momoka-danmaku-send'), 1)
				local payload
				for _, command in ipairs(commands) do
					if command[1] == 'script-message' and command[2] == 'momoka-danmaku-send' then payload = utils.parse_json(command[3]) end
				end
				require_equal(payload.text, '第一条'); require_equal(payload.context, input.context)
				assert(payload.requestId ~= '')
				answer(input, true, '', {requestId = 'unrelated'})
				assert(input.pending, 'unrelated acknowledgement cleared pending request')
				press('Ctrl+a'); input:insert('下一条')
				answer(input, true, '已发送')
				require_equal(input.text, '下一条')
				local previous_id = payload.requestId
				input:submit(); assert(input.pending.id ~= previous_id)
				answer(input, false, '发送失败，请重试')
				require_equal(input.text, '下一条')
				input:submit(); answer(input, true, '已发送')
				require_equal(input.text, '')
				input_state({busy = true}); input:insert('忙时不能发'); input:submit()
				require_equal(count_command('script-message', 'momoka-danmaku-send'), 3)
			end)
			test('danmaku context and file boundaries reject delayed acknowledgements and release input', function()
				local input = input_state()
				draw_input(); input:focus(); input:insert('旧集'); input:submit()
				local old = {context = input.context, requestId = input.pending.id, success = true, message = '旧回执'}
				input_state({context = 'fixture-episode-2'})
				draw_input(); input:focus(); input:insert('新集草稿')
				fixture_messages['momoka-danmaku-input-result'](utils.format_json(old))
				require_equal(input.text, '新集草稿')
				require_equal(input.message, '')
				emit('start-file')
				require_equal(input.focused, false); require_equal(input.visible, false); require_equal(input.text, '')
				require_equal(properties['input-ime'], false)
				input_state(); draw_input(); input:focus(); input:insert('结束清理')
				emit('end-file')
				require_equal(input.focused, false); require_equal(input.text, '')
			end)
			test('danmaku menu and native focus loss release editing without consuming a draft', function()
				-- Recreate under mocked observers so the focused callback is captured.
				local input = require('elements/DanmakuInput'):new()
				input_state(); draw_input()
				properties['input-ime'] = true
				input:focus(); input:insert('草稿')
				observers.focused('focused', false)
				require_equal(input.focused, false); require_equal(input.text, '草稿')
				require_equal(properties['input-ime'], true)
				input:focus(); menu({search_style = 'palette', items = {{title = 'one', value = 1}}})
				require_equal(input.focused, false)
				require_equal(input.text, '草稿')
				assert(Menu:is_open())
				Menu:close(true)
				properties['input-ime'] = false
			end)
			test('danmaku input and outside clicks never fall through to picture pause', function()
				local input = input_state()
				draw_input()
				cursor.x, cursor.y = input.ax + 10, (input.ay + input.by) / 2
				down(); up()
				require_equal(input.focused, true)
				draw_input()
				cursor.x, cursor.y = 500, 450
				down()
				draw_input()
				cursor.x, cursor.y = 500, 450
				up(); advance(1)
				require_equal(input.focused, false)
				require_equal(count_command('cycle', 'pause'), 0)
			end)
			test('danmaku title allocation follows short titles and stays inside narrow and scaled windows', function()
				input_state()
				local input, _, bar = draw_input(1280, '短片名')
				local short_x = input.ax
				draw_input(1280, string.rep('很长的影片标题', 20))
				assert(input.ax > short_x, 'input is not placed after the actual title')
				for _, width in ipairs({360, 760, 1280}) do
					for _, scale in ipairs({1, 1.3, 1.5}) do
						draw_input(width, string.rep('长标题', 20), scale)
						assert(input.geometry, 'input disappeared instead of shortening title')
						if input.geometry then
							assert(input.bx > input.ax)
							assert(input.ax > bar.ax)
							assert(input.bx <= bar.bx - bar.size * #bar.buttons, 'input overlaps window buttons')
							assert(input:render())
						end
					end
				end
				input_state({visible = false}); draw_input()
				require_equal(input.geometry, nil)
			end)
			test('external track reload advertises a registered F5 key', function()
				properties['track-list'] = {{type = 'sub', id = 1, external = true, title = 'fixture.srt'}}
				local open = create_select_tracklist_type_menu_opener({type = 'sub', prop = 'sid', title = 'Subtitles'})
				open()
				assert(keys.f5, 'F5 has no input binding')
				keys.f5.callback({event = 'press', is_mouse = false})
				require_equal(count_command('sub-reload'), 1)
			end)
		end)
		if not ok then results[#results + 1] = {name = 'fixture setup', passed = false, detail = tostring(fatal)} end
		for key, value in pairs(native_mp) do mp[key] = value end
		request_render, tween = original_request_render, original_tween
		native_mp.commandv('script-message', 'fixture-result', utils.format_json(results))
	end)
end
