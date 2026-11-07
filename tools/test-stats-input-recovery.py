"""Execute actual stats.lua with bundled LuaJIT, inert timing/pass input and no media or window."""
import argparse
import ctypes
import hashlib
import json
import os
import sys
import time
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
root, output = args.root.resolve(), args.output.resolve()
output.mkdir(parents=True, exist_ok=False)
source = root / 'assets/mpv-ui/scripts/stats.lua'
tests = root / 'tests/Momoka.Tests/Lua/stats-input-recovery-tests.lua'
prelude = r'''
local mp = require('mp')
fixture_native_command = mp.commandv
fixture_props, fixture_messages, fixture_events, fixture_bindings = {}, {}, {}, {}
fixture_canvas = ''
local passes = {}
for index = 1, 30 do passes[index] = {desc='PASS-'..index,last=1,avg=1,peak=1,samples={1}} end
fixture_props['vo-passes'] = {fresh=passes}
fixture_props['osd-height'] = 720
fixture_props['osd-scale-by-window'] = true
fixture_props['audio-delay'] = 0
fixture_props['track-list'] = {}
fixture_props['options/script-opts'] = {}
mp.get_property_native = function(name, fallback) if fixture_props[name] ~= nil then return fixture_props[name] end return fallback end
mp.get_property_number = mp.get_property_native
mp.get_property_bool = mp.get_property_native
mp.get_property = function(name, fallback) return fixture_props[name] or fallback or '' end
mp.get_property_osd = function() return '' end
mp.set_property_native = function(name, value) fixture_props[name] = value end
mp.set_property = mp.set_property_native
mp.observe_property = function(name, _, callback) callback(name, fixture_props[name]) end
mp.unobserve_property = function() end
mp.add_key_binding = function(_, name, callback) fixture_bindings[name] = callback end
mp.add_forced_key_binding = mp.add_key_binding
mp.remove_key_binding = function(name) fixture_bindings[name] = nil end
mp.register_script_message = function(name, callback) fixture_messages[name] = callback end
mp.register_event = function(name, callback) fixture_events[name] = fixture_events[name] or {}; table.insert(fixture_events[name], callback) end
mp.add_periodic_timer = function(interval, callback)
  local timer={enabled=false,timeout=interval,callback=callback}
  function timer:kill() self.enabled=false end
  function timer:stop() self.enabled=false end
  function timer:resume() self.enabled=true end
  function timer:is_enabled() return self.enabled end
  return timer
end
mp.set_osd_ass = function(_,_,text) fixture_canvas=text end
mp.osd_message = function(text) fixture_canvas=text end
mp.command_native = function(args) return args[2] or '' end
'''
script = output / 'stats.lua'
script.write_text(prelude + '\n' + source.read_text(encoding='utf-8') + '\n' + tests.read_text(encoding='utf-8'), encoding='utf-8', newline='\n')
(output / 'source-hashes.json').write_text(json.dumps({str(path.relative_to(root)):hashlib.sha256(path.read_bytes()).hexdigest() for path in (source,tests)}, indent=2), encoding='utf-8')
directories = [os.add_dll_directory(str(root)), os.add_dll_directory(str(root / 'assets/mpv-runtime'))]
mpv = ctypes.CDLL(str(root / 'libmpv-2.dll'))
class Event(ctypes.Structure):
    _fields_ = [('id',ctypes.c_int),('error',ctypes.c_int),('userdata',ctypes.c_uint64),('data',ctypes.c_void_p)]
class Message(ctypes.Structure):
    _fields_ = [('count',ctypes.c_int),('args',ctypes.POINTER(ctypes.c_char_p))]
class Log(ctypes.Structure):
    _fields_ = [('prefix',ctypes.c_char_p),('level',ctypes.c_char_p),('text',ctypes.c_char_p),('log_level',ctypes.c_int)]
mpv.mpv_create.restype = ctypes.c_void_p
mpv.mpv_initialize.argtypes = [ctypes.c_void_p]
mpv.mpv_set_option_string.argtypes = [ctypes.c_void_p,ctypes.c_char_p,ctypes.c_char_p]
mpv.mpv_request_log_messages.argtypes = [ctypes.c_void_p,ctypes.c_char_p]
mpv.mpv_wait_event.argtypes = [ctypes.c_void_p,ctypes.c_double]
mpv.mpv_wait_event.restype = ctypes.POINTER(Event)
mpv.mpv_terminate_destroy.argtypes = [ctypes.c_void_p]
handle = mpv.mpv_create()
checks, errors, completed = [], [], False
try:
    for key,value in {'config':'no','load-scripts':'no','osc':'no','load-stats-overlay':'no','terminal':'no','vo':'null','ao':'null','video':'no','audio':'no','idle':'yes','access-references':'no','load-unsafe-playlists':'no','input-default-bindings':'no','scripts':str(script)}.items():
        if mpv.mpv_set_option_string(handle,key.encode(),value.encode()) < 0: raise RuntimeError('Rejected '+key)
    mpv.mpv_request_log_messages(handle,b'error')
    if mpv.mpv_initialize(handle) < 0: raise RuntimeError('Initialization failed')
    deadline = time.monotonic()+15
    while time.monotonic() < deadline and not completed:
        event=mpv.mpv_wait_event(handle,0.05).contents
        if event.id == 16:
            msg=ctypes.cast(event.data,ctypes.POINTER(Message)).contents
            values=[msg.args[index].decode('utf-8','replace') for index in range(msg.count)]
            if values and values[0]=='fixture-result': checks,completed=json.loads(values[1]),True
        elif event.id==2:
            log=ctypes.cast(event.data,ctypes.POINTER(Log)).contents
            errors.append(log.text.decode('utf-8','replace'))
finally:
    mpv.mpv_terminate_destroy(handle)
    for item in directories: item.close()
report={'completed':completed,'checks':checks,'errors':errors,'coverage':'actual stats.lua and LuaJIT; inert passes/properties/timing; no media/window/network/config'}
(output/'results.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
for row in checks: print(('PASS ' if row['passed'] else 'FAIL ')+row['name'])
for error in errors: print(error)
sys.exit(0 if completed and checks and all(row['passed'] for row in checks) and not errors else 1)
