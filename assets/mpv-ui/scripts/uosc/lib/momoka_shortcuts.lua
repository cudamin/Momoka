-- MOMOKA[shortcuts]: the host owns action identities; modal menus temporarily own their keys.
local bindings = {}
local active = {}

-- Match the host's canonical key spelling. A punctuation key is one key, never command text.
local named_keys = {
	SPACE = true, BS = true, DEL = true, INS = true, HOME = true, END = true,
	ESC = true, ENTER = true, TAB = true, CAPS_LOCK = true, NUM_LOCK = true, SCROLL_LOCK = true,
	PRINT = true, PAUSE = true, MENU = true, SLEEP = true,
	MUTE = true, GO_BACK = true, GO_FORWARD = true, SEARCH = true, FAVORITES = true,
	HOMEPAGE = true, MAIL = true, PLAYONLY = true, PAUSEONLY = true,
	PGUP = true, PGDWN = true, LEFT = true, RIGHT = true, UP = true, DOWN = true,
	KP_DEC = true, KP_SEPARATOR = true, KP_DIVIDE = true, KP_MULTIPLY = true, KP_SUBTRACT = true, KP_ADD = true,
	KP_ENTER = true, KP_BEGIN = true, KP_PGUP = true, KP_PGDWN = true, KP_HOME = true, KP_END = true,
	KP_LEFT = true, KP_RIGHT = true, KP_UP = true, KP_DOWN = true, KP_INS = true, KP_DEL = true,
	VOLUME_MUTE = true, VOLUME_DOWN = true, VOLUME_UP = true, NEXT = true, PREV = true,
	STOP = true, PLAYPAUSE = true, RECORD = true, FORWARD = true, REWIND = true,
	CHANNEL_UP = true, CHANNEL_DOWN = true, ZOOMIN = true, XF86_BACK = true, XF86_FORWARD = true, XF86_REFRESH = true,
	XF86_STOP = true, XF86_SEARCH = true, XF86_FAVORITES = true, XF86_HOMEPAGE = true,
}
local punctuation_keys = {['['] = true, [']'] = true, ['-'] = true, ['='] = true,
	[','] = true, ['.'] = true, ['`'] = true}
local function valid_key(key)
	if #key >= 48 then return false end
	local bare = key
	for _, modifier in ipairs({'Ctrl+', 'Alt+', 'Shift+'}) do
		if bare:sub(1, #modifier) == modifier then bare = bare:sub(#modifier + 1) end
	end
	if named_keys[bare] or punctuation_keys[bare] or bare:match('^[a-z0-9]$') or bare:match('^[!-~]$')
		or bare:match('^KP[0-9]$') then return true end
	local number = bare:match('^F(%d+)$')
	return number ~= nil and tonumber(number) >= 1 and tonumber(number) <= 24
end

function momoka_clear_shortcuts()
	for _, name in ipairs(active) do mp.remove_key_binding(name) end
	active = {}
end

function momoka_refresh_shortcuts()
	momoka_clear_shortcuts()
	if Menu:is_open() or Elements:v('danmaku_input', 'focused', false) then return end
	local index = 0
	for key, action in pairs(bindings) do
		index = index + 1
		local name = 'momoka-action-' .. index
		active[#active + 1] = name
		mp.add_forced_key_binding(key, name, function()
			if action ~= '' then momoka_notify('momoka-shortcut', action) end
		end, {repeatable = action:find('seek-', 1, true) == 1 or action:find('volume-', 1, true) == 1
			or action:find('subtitle-seek-', 1, true) == 1 or action:find('subtitle-scale-', 1, true) == 1
			or action:find('subtitle-position-', 1, true) == 1
			or action == 'video-zoom-in' or action == 'video-zoom-out'
			or (action:find('video-pan-', 1, true) == 1 and action ~= 'video-pan-reset')})
	end
end

mp.register_script_message('momoka-shortcuts', function(json)
	local values = utils.parse_json(json)
	if type(values) ~= 'table' then return end
	local next_bindings = {}
	for key, action in pairs(values) do
		if type(key) == 'string' and type(action) == 'string'
			and valid_key(key)
			and (action == '' or action:match('^[a-z%-]+$')) then
			next_bindings[key] = action
		end
	end
	bindings = next_bindings
	momoka_refresh_shortcuts()
end)
