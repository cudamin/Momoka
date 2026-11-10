local fixture_native = require('mp')
local fixture_utils = require('mp.utils')
local fixture_original = {}
for k,v in pairs(fixture_native) do fixture_original[k]=v end
local fixture_props = {['video-params']={w=1920,h=1080}, ['audio-out-params']={format='float'},
 ['hwdec-current']='no', ['video-crop']='', ['time-pos']=10, ['speed']=1, ['mute']=false, ['pause']=false, ['vf']={}}
local fixture_commands, fixture_events, fixture_messages, fixture_hooks, fixture_observers, fixture_timers = {}, {}, {}, {}, {}, {}
local fixture_time = 100
fixture_native.get_time=function() return fixture_time end
fixture_native.get_property_native=function(name, fallback) if fixture_props[name]~=nil then return fixture_props[name] end return fallback end
fixture_native.get_property=function(name, fallback) return fixture_props[name] or fallback end
fixture_native.get_property_number=function(name, fallback) return tonumber(fixture_props[name]) or fallback end
fixture_native.set_property_native=function(name,value) fixture_props[name]=value;return true end
fixture_native.set_property=fixture_native.set_property_native
fixture_native.commandv=function(...)
 local args={...};fixture_commands[#fixture_commands+1]=args
 if args[3] and args[3]:find('@momoka_crop_probe:',1,true) and fixture_without_crop then return false end
 return true
end
fixture_native.osd_message=function() end
fixture_native.register_event=function(name, callback) fixture_events[name]=callback end
fixture_native.register_script_message=function(name, callback) fixture_messages[name]=callback end
fixture_native.add_hook=function(name, priority, callback) fixture_hooks[name]=callback end
fixture_native.observe_property=function(name, kind, callback) fixture_observers[name]=callback;callback(name,fixture_props[name]) end
fixture_native.unobserve_property=function(callback) for k,v in pairs(fixture_observers) do if v==callback then fixture_observers[k]=nil end end end
fixture_native.add_periodic_timer=function(timeout,callback)
 local t={timeout=timeout,callback=callback,enabled=true};function t:kill()self.enabled=false end
 fixture_timers[#fixture_timers+1]=t;return t
end
