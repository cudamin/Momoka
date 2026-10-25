local Element = require('elements/Element')

-- The host owns service availability, episode identity and HTTP. This element only edits text.
local DanmakuInput = class(Element)
local request_serial = 0
local function utf16_length(text)
	local count = 0
	for _, char in utf8_iter(text) do count = count + (#char == 4 and 2 or 1) end
	return count
end
local function parse(json)
	if type(json) ~= 'string' or #json > 4096 then return nil end
	local value = utils.parse_json(json)
	return type(value) == 'table' and value or nil
end

function DanmakuInput:new() return Class.new(self) end
function DanmakuInput:init()
	Element.init(self, 'danmaku_input', {render_order = 4.5, anchor_id = 'top_bar'})
	self.visible, self.can_send, self.busy, self.focused = false, false, false, false
	self.context, self.text, self.position, self.revision = '', '', 0, 0
	self.pending, self.message, self.geometry = nil, '', nil
	self.selected_all = false
	self.send_owner = {}
	self:observe_mp_property('focused', 'bool', function(_, value) if value == false then self:blur() end end)
	for _, event in ipairs({'start-file', 'end-file', 'shutdown'}) do
		self:register_mp_event(event, function() self:reset() end)
	end
	self:register_disposer(cursor:on('primary_down', function()
		if self.focused and (not self.geometry or not point_collides_with(cursor, self.geometry)) then self:blur() end
	end))
end

function DanmakuInput:set_state(json)
	local value = parse(json)
	if not value or type(value.context) ~= 'string' or #value.context > 256 then return end
	if value.context ~= self.context then
		self:reset()
		self.context = value.context
	end
	self.visible = value.visible == true and self.context ~= ''
	self.can_send, self.busy = value.canSend == true, value.busy == true
	if not self.visible then self.geometry = nil; self:blur() end
	request_render()
end

function DanmakuInput:reset()
	self:blur()
	self.visible, self.can_send, self.busy = false, false, false
	self.context, self.text, self.position, self.revision = '', '', 0, 0
	self.pending, self.message, self.geometry = nil, '', nil
	self.selected_all = false
	request_render()
end

function DanmakuInput:result(json)
	local value = parse(json)
	local pending = self.pending
	if not value or not pending or value.context ~= self.context or value.context ~= pending.context
		or value.requestId ~= pending.id or type(value.success) ~= 'boolean' then return end
	self.pending, self.busy = nil, false
	if value.success and self.revision == pending.revision and self.text == pending.text then
		self.text, self.position, self.selected_all = '', 0, false
		self.revision = self.revision + 1
	end
	self.message = type(value.message) == 'string' and #value.message <= 1000 and value.message or ''
	if self.message ~= '' and self.visible then mp.commandv('show-text', self.message, 3000) end
	request_render()
end

function DanmakuInput:submit()
	if not self.visible or not self.can_send or self.busy or self.pending or self.context == '' then return end
	if self.text:match('^%s*$') or utf16_length(self.text) > 100 then return end
	request_serial = request_serial + 1
	local id = 'uosc-' .. tostring(mp.get_time()) .. '-' .. request_serial
	self.pending = {context = self.context, id = id, text = self.text, revision = self.revision}
	self.message = ''
	momoka_notify('momoka-danmaku-send', utils.format_json({context = self.context, requestId = id, text = self.text}))
	request_render()
end

function DanmakuInput:insert(text)
	if not self.focused or type(text) ~= 'string' then return end
	text = text:gsub('[\r\n\t]', ' '):gsub('[%z\1-\31\127]', '')
	local head = self.selected_all and '' or self.text:sub(1, self.position)
	local tail = self.selected_all and '' or self.text:sub(self.position + 1)
	local remaining, parts = 100 - utf16_length(head .. tail), {}
	for _, char in utf8_iter(text) do
		local length = #char == 4 and 2 or 1
		if remaining < length then break end
		parts[#parts + 1], remaining = char, remaining - length
	end
	local inserted = table.concat(parts)
	if inserted == '' then return end
	self.text, self.position = head .. inserted .. tail, #head + #inserted
	self.selected_all, self.message, self.revision = false, '', self.revision + 1
	request_render()
end

function DanmakuInput:move(direction)
	if not self.focused then return end
	if direction == 'home' then self.position = 0
	elseif direction == 'end' then self.position = #self.text
	elseif direction == 'left' then self.position = self.selected_all and 0 or utf8_prev(self.text, self.position)
	elseif direction == 'right' then self.position = self.selected_all and #self.text or utf8_next(self.text, self.position) end
	self.selected_all = false
	request_render()
end

function DanmakuInput:erase(backwards)
	if not self.focused then return end
	if self.selected_all then self.text, self.position = '', 0
	elseif backwards then
		local previous = utf8_prev(self.text, self.position)
		self.text, self.position = self.text:sub(1, previous) .. self.text:sub(self.position + 1), previous
	else
		self.text = self.text:sub(1, self.position) .. self.text:sub(utf8_next(self.text, self.position) + 1)
	end
	self.selected_all, self.message, self.revision = false, '', self.revision + 1
	request_render()
end

function DanmakuInput:focus()
	if self.focused or not self.visible or Menu:is_open() then return end
	self.focused = true
	self.previous_ime = mp.get_property_bool('input-ime', false)
	mp.set_property_bool('input-ime', true)
	if momoka_refresh_shortcuts then momoka_refresh_shortcuts() end
	-- Block existing non-text player bindings (including modifiers/function keys). ANY_UNICODE
	-- captures printable characters before their individual default bindings in mpv.
	for _, entry in ipairs(mp.get_property_native('input-bindings') or {}) do
		local key = entry.key
		if type(key) == 'string' and key ~= '' and key ~= 'CLOSE_WIN'
			and key:upper() ~= 'ANY_UNICODE'
			and not key:find('MBTN', 1, true) and not key:find('MOUSE', 1, true)
			and not key:find('WHEEL', 1, true) and not key:find('TOUCH', 1, true) then
			self:add_key_binding(key, function() end, 'editing')
		end
	end
	local function bind(key, action, repeatable)
		self:add_key_binding(key, {function(info)
			if self.focused and info.event ~= 'up' and (repeatable or info.event ~= 'repeat') then action(info) end
		end, {complex = true, repeatable = repeatable == true}}, 'editing')
	end
	bind('any_unicode', function(info) self:insert(info.key_text) end, true)
	bind('SPACE', function() self:insert(' ') end, true)
	for i = 0, 9 do local digit = tostring(i); bind('KP' .. i, function() self:insert(digit) end, true) end
	bind('KP_DEC', function() self:insert('.') end, true)
	bind('ENTER', function() self:submit() end)
	bind('KP_ENTER', function() self:submit() end)
	bind('ESC', function() self:blur() end)
	bind('TAB', function() self:blur() end)
	bind('Shift+TAB', function() self:blur() end)
	bind('BS', function() self:erase(true) end, true)
	bind('DEL', function() self:erase(false) end, true)
	for _, key in ipairs({'LEFT', 'RIGHT', 'HOME', 'END'}) do
		local direction = key:lower(); bind(key, function() self:move(direction) end, true)
	end
	bind('Ctrl+a', function() self.selected_all = true; request_render() end)
	bind('Ctrl+v', function() self:insert(get_clipboard()) end)
	cursor:refresh_hold()
	request_render()
end

function DanmakuInput:blur()
	if not self.focused then return end
	self.focused, self.selected_all = false, false
	self:remove_key_bindings('editing')
	mp.set_property_bool('input-ime', self.previous_ime == true)
	self.previous_ime = nil
	if momoka_refresh_shortcuts then momoka_refresh_shortcuts() end
	cursor:refresh_hold()
	request_render()
end

function DanmakuInput:destroy()
	self:reset()
	Element.destroy(self)
end

function DanmakuInput:preferred_width(available)
	if not self.visible then return 0 end
	local scale = state.scale or 1
	return math.min(available, math.min(320 * scale, math.max(120 * scale, available * 0.4)))
end

function DanmakuInput:place(ax, ay, bx, by, font_size)
	self.ax, self.ay, self.bx, self.by = ax, ay, bx, by
	self.geometry = bx > ax and {ax = ax, ay = ay, bx = bx, by = by, input_owner = self} or nil
	self.font_size = font_size
end

-- Register behind the actual top-bar/control hitboxes so an outside click only dismisses
-- editing; it must not turn into the canvas' delayed play/pause click after the input blurs.
function DanmakuInput:outside_zone()
	if self.focused then cursor:zone('primary_down', display, function() self:blur() end) end
end

function DanmakuInput:render()
	local top = Elements.top_bar
	if not self.visible or not self.geometry or not top or not top.enabled then self:blur(); return end
	local visibility = self:get_visibility()
	if visibility <= 0 then return end
	local ass, rect = assdraw.ass_new(), self.geometry
	local scale, font = state.scale or 1, self.font_size
	local padding = math.min(math.max(1, (rect.bx - rect.ax) / 4), math.max(3, round(font / 2)))
	local busy = self.busy or self.pending ~= nil
	local show_send = self.can_send and rect.bx - rect.ax >= 76 * scale
	local send_width = show_send and math.min(rect.bx - rect.ax, (busy and 60 or 46) * scale) or 0
	local text_bx, cy = rect.bx - send_width - padding, (rect.ay + rect.by) / 2
	ass:rect(rect.ax, rect.ay, rect.bx, rect.by, {
		color = bg, opacity = visibility * config.opacity.title, radius = state.radius,
		border = self.focused and math.max(1, scale) or 0, border_color = fg,
	})
	local text_rect = {ax = rect.ax, ay = rect.ay, bx = rect.bx - send_width, by = rect.by, input_owner = self}
	cursor:zone('primary_click', text_rect, function() self:focus() end)
	local opts = {size = font, color = bgt, opacity = visibility, wrap = 2,
		clip = string.format('\\clip(%d,%d,%d,%d)', rect.ax + padding, rect.ay, text_bx, rect.by)}
	local text_ax = rect.ax + padding
	if self.text ~= '' then
		local head = self.text:sub(1, self.position)
		local before = text_width(ass_escape(head) .. '\239\187\191', opts)
		local offset = self.focused and math.max(0, before - math.max(0, text_bx - text_ax - padding)) or 0
		if self.selected_all then ass:rect(text_ax, rect.ay + 3 * scale, text_bx, rect.by - 3 * scale, {color = fg, opacity = visibility * 0.2}) end
		ass:txt(text_ax - offset, cy, 4, ass_escape(self.text) .. '\239\187\191', opts)
		if self.focused then
			local x = math.min(text_bx, text_ax + before - offset)
			ass:rect(x, cy - font * 0.45, x + math.max(1, scale), cy + font * 0.45, {color = fg, opacity = visibility})
		end
	else
		local placeholder = not self.can_send and '请先选择弹幕库' or self.message ~= '' and self.message or '发条弹幕吧'
		opts.opacity = visibility * 0.65
		ass:txt(text_ax, cy, 4, ass_escape(placeholder), opts)
		if self.focused then ass:rect(text_ax, cy - font * 0.45, text_ax + math.max(1, scale), cy + font * 0.45, {color = fg, opacity = visibility}) end
	end
	if show_send then
		local send = {ax = rect.bx - send_width, ay = rect.ay, bx = rect.bx, by = rect.by, input_owner = self.send_owner}
		local active = not busy and not self.text:match('^%s*$')
		cursor:zone('primary_click', send, function() if active then self:submit() end end)
		ass:txt((send.ax + send.bx) / 2, cy, 5, busy and '发送中' or '发送', {size = font * 0.85, color = bgt, opacity = visibility * (active and 1 or 0.5)})
	end
	return ass
end

return DanmakuInput
