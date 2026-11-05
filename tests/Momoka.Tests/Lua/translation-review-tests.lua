local events, observers, messages, results = {}, {}, {}, {}
local properties = {path='local-fixture', ['sub-text']='same', sid=2, ['sub-visibility']=true, ['sub-start']=1, ['sub-end']=4}
local overlay = {data='', update=function(self) self.removed=false end, remove=function(self) self.removed=true end}
local payload
local mp = {
    create_osd_overlay=function() return overlay end,
    get_property=function(name, fallback) return properties[name] or fallback end,
    get_property_native=function(name, fallback) local value=properties[name]; if value == nil then return fallback end; return value end,
    get_property_number=function(name) return properties[name] end,
    set_property_number=function(name,value) properties[name]=value end,
    set_property_native=function(name,value) properties[name]=value end,
    get_osd_size=function() return 1280,720 end,
    register_event=function(name,callback) events[name]=callback end,
    register_script_message=function(name,callback) messages[name]=callback end,
    observe_property=function(name,kind,callback) observers[name]=callback end,
    add_periodic_timer=function() return {resume=function() end,kill=function() end} end,
}
package.loaded.mp = mp
package.loaded['mp.utils'] = {parse_json=function() local value={}; for key,item in pairs(payload) do value[key]=item end; return value end}
dofile(fixture_root .. '/assets/mpv-ui/scripts/momoka_subtitle_translation.lua')
local function show(revision, original)
    payload={original=original or 'same',text='translated',sid='2',start=1,['end']=4,revision=revision,fontSize=32,position=80}
    messages['momoka-subtitle-translation-show']('fixture')
end
fixture_failures=0
local function test(name,action)
    local ok,message=pcall(action)
    results[#results+1]=(ok and 'PASS ' or 'FAIL ')..name..(ok and '' or ': '..tostring(message))
    if not ok then fixture_failures=fixture_failures+1 end
end
test('R3 seek publishes new revision for unchanged cue and rejects old reply',function()
    local old=properties['user-data/momoka-subtitle-translation/cue']
    show(old.revision)
    assert(overlay.data~='' and not overlay.removed)
    events.seek()
    local current=properties['user-data/momoka-subtitle-translation/cue']
    assert(current.revision==old.revision+1 and current.text==old.text)
    assert(overlay.data=='' and overlay.removed)
    show(old.revision)
    assert(overlay.data=='', 'old seek reply accepted')
    show(current.revision)
    assert(overlay.data~='' and not overlay.removed, 'same cue cannot be restored')
end)
test('R3 cross-cue seek rejects stale cached original',function()
    properties['sub-text']='next'
    events.seek()
    show(properties['user-data/momoka-subtitle-translation/cue'].revision)
    assert(overlay.data=='', 'old original accepted after cross-cue seek')
end)
test('R19 style redraw does not erase valid translated text',function()
    show(properties['user-data/momoka-subtitle-translation/cue'].revision,'next')
    assert(overlay.data~='')
    payload.fontSize=40; payload.position=75
    messages['momoka-subtitle-translation-show']('fixture')
    assert(not overlay.removed and overlay.data:find('fs40',1,true) and overlay.data:find('540.0',1,true))
end)
fixture_result=table.concat(results,'\n')
