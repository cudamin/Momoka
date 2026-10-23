"""Run the shipped danmaku Lua in bundled libmpv with deterministic clocks; no media, window, HTTP or user data."""
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
source = root / 'assets/mpv-ui/scripts/momoka_danmaku.lua'
tests = root / 'tests/Momoka.Tests/Lua/danmaku-tests.lua'
prelude = r'''
local fixture_mp = require('mp')
fixture_native_command = fixture_mp.commandv
fixture_native_overlay = fixture_mp.create_osd_overlay
fixture_events, fixture_messages, fixture_observers = {}, {}, {}
fixture_commands, fixture_properties, fixture_canvas = {}, {}, {1280, 720}
fixture_mp.register_event = function(name, callback)
    fixture_events[name] = fixture_events[name] or {}
    table.insert(fixture_events[name], callback)
end
fixture_mp.register_script_message = function(name, callback) fixture_messages[name] = callback end
fixture_mp.observe_property = function(name, _, callback) fixture_observers[name] = callback end
fixture_mp.commandv = function(...) fixture_commands[#fixture_commands + 1] = {...}; return true end
fixture_mp.get_property_native = function(name, fallback)
    if fixture_properties[name] ~= nil then return fixture_properties[name] end
    return fallback
end
fixture_mp.get_property_number = fixture_mp.get_property_native
fixture_mp.get_osd_size = function() return fixture_canvas[1], fixture_canvas[2] end
fixture_mp.create_osd_overlay = function()
    local overlay = {data = '', removed = true}
    function overlay:update() self.removed = false; return {} end
    function overlay:remove() self.removed = true end
    return overlay
end
fixture_mp.add_periodic_timer = function(interval, callback)
    local timer = {enabled = true, timeout = interval, callback = callback}
    function timer:kill() self.enabled = false end
    function timer:resume() self.enabled = true end
    function timer:is_enabled() return self.enabled end
    return timer
end
'''
script = output / 'momoka_danmaku.lua'
script.write_text(prelude + '\n' + source.read_text(encoding='utf-8') + '\n' + tests.read_text(encoding='utf-8'), encoding='utf-8', newline='\n')
manifest = {str(path.relative_to(root)).replace('\\', '/'): hashlib.sha256(path.read_bytes()).hexdigest() for path in (source, tests)}
(output / 'source-hashes.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
directories = [os.add_dll_directory(str(root)), os.add_dll_directory(str(root / 'assets/mpv-runtime'))]
mpv = ctypes.CDLL(str(root / 'libmpv-2.dll'))

class Event(ctypes.Structure):
    _fields_ = [('id', ctypes.c_int), ('error', ctypes.c_int), ('userdata', ctypes.c_uint64), ('data', ctypes.c_void_p)]

class Message(ctypes.Structure):
    _fields_ = [('count', ctypes.c_int), ('args', ctypes.POINTER(ctypes.c_char_p))]

class Log(ctypes.Structure):
    _fields_ = [('prefix', ctypes.c_char_p), ('level', ctypes.c_char_p), ('text', ctypes.c_char_p), ('log_level', ctypes.c_int)]

mpv.mpv_create.restype = ctypes.c_void_p
mpv.mpv_initialize.argtypes = [ctypes.c_void_p]
mpv.mpv_set_option_string.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p]
mpv.mpv_request_log_messages.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
mpv.mpv_wait_event.argtypes = [ctypes.c_void_p, ctypes.c_double]
mpv.mpv_wait_event.restype = ctypes.POINTER(Event)
mpv.mpv_terminate_destroy.argtypes = [ctypes.c_void_p]
handle = mpv.mpv_create()
checks, errors, finished = [], [], False
try:
    for name, value in {
        'config': 'no', 'load-scripts': 'no', 'osc': 'no', 'load-stats-overlay': 'no', 'terminal': 'no',
        'vo': 'null', 'ao': 'null', 'video': 'no', 'audio': 'no', 'idle': 'yes', 'access-references': 'no',
        'load-unsafe-playlists': 'no', 'input-default-bindings': 'no', 'osd-font': 'Microsoft YaHei', 'scripts': str(script),
    }.items():
        if mpv.mpv_set_option_string(handle, name.encode(), value.encode()) < 0:
            raise RuntimeError('Rejected option: ' + name)
    mpv.mpv_request_log_messages(handle, b'error')
    if mpv.mpv_initialize(handle) < 0:
        raise RuntimeError('mpv initialization failed')
    deadline = time.monotonic() + 25
    while time.monotonic() < deadline and not finished:
        event = mpv.mpv_wait_event(handle, 0.05).contents
        if event.id == 16:
            message = ctypes.cast(event.data, ctypes.POINTER(Message)).contents
            values = [message.args[index].decode('utf-8', 'replace') for index in range(message.count)]
            if values and values[0] == 'fixture-result':
                checks, finished = json.loads(values[1]), True
        elif event.id == 2:
            log = ctypes.cast(event.data, ctypes.POINTER(Log)).contents
            errors.append(log.text.decode('utf-8', 'replace').strip())
finally:
    mpv.mpv_terminate_destroy(handle)
    for directory in directories:
        directory.close()
report = {'completed': finished, 'checks': checks, 'errors': errors,
          'coverage': 'Bundled libmpv Lua and real libass command; fake clock/canvas/events; no media, window, HTTP or user settings.'}
(output / 'results.json').write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
for check in checks:
    print(('PASS ' if check['passed'] else 'FAIL ') + check['name'] + ': ' + check.get('detail', ''))
print(f'Completed={finished}; checks={len(checks)}; failed={sum(not check["passed"] for check in checks)}; errors={len(errors)}')
sys.exit(0 if finished and checks and all(check['passed'] for check in checks) and not errors else 1)
