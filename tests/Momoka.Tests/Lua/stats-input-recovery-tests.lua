local checks = {}
local function check(name, passed) checks[#checks + 1] = {name = name, passed = passed, detail = ''} end
update_scale()
cycle_stats()
cycle_stats()
check('R11-004 stats reports scroll ownership', fixture_props['user-data/momoka-stats/scrollable'] == true)
check('stats first page truncates tail', not fixture_canvas:find('PASS%-30'))
local scroll = fixture_messages['momoka-scroll']
if scroll then for _ = 1, 30 do scroll('down') end end
check('R11-004 host scroll reaches final pass', fixture_canvas:find('PASS%-30') ~= nil)
stop_stats()
check('R11-004 close releases input ownership', fixture_props['user-data/momoka-stats/scrollable'] == false)
local old = fixture_canvas
if scroll then scroll('down') end
check('closed scroll does not redraw', fixture_canvas == old)
fixture_bindings['display-page-2-toggle']()
check('direct page opens scroll ownership', fixture_props['user-data/momoka-stats/scrollable'] == true)
for _, callback in ipairs(fixture_events['start-file'] or {}) do callback() end
check('file boundary closes and releases scroll', fixture_props['user-data/momoka-stats/scrollable'] == false)
fixture_native_command('script-message', 'fixture-result', require('mp.utils').format_json(checks))
