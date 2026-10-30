"""Dedicated PERF-07/15 harness: shipped LuaJIT in idle libmpv, deterministic timers/canvas, no media or window."""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import sys
import time

parser = argparse.ArgumentParser()
parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[2])
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--baseline-source', type=Path)
parser.add_argument('--real-timers', action='store_true', help='Use the shipped timer event loop in a fresh idle context.')
parser.add_argument('--inject-failure', action='store_true', help='Exercise the harness failing-assertion exit path.')
args = parser.parse_args()
if args.real_timers and args.baseline_source:
    parser.error('--real-timers verifies repaired lifecycle only; do not combine with --baseline-source.')
root, output = args.root.resolve(), args.output.resolve()
output.mkdir(parents=True, exist_ok=False)
fixture = Path(__file__).resolve().parent

PRELUDE = r"""
local fixture_mp = require('mp')
fixture_native_command = fixture_mp.commandv
fixture_native_overlay = fixture_mp.create_osd_overlay
assert(type(fixture_mp.add_timeout) == 'function' and type(fixture_mp.get_time) == 'function', 'Shipped timer APIs missing')
fixture_native_add_timeout, fixture_native_get_time = fixture_mp.add_timeout, fixture_mp.get_time
fixture_native_add_periodic_timer = fixture_mp.add_periodic_timer
fixture_events, fixture_messages, fixture_observers = {}, {}, {}
fixture_commands, fixture_properties, fixture_canvas = {}, {}, {1280, 720}
fixture_clock, fixture_timers = 0, {}
fixture_counts = {reads = 0, json = 0, updates = 0, removes = 0, sets = 0, formats = 0}
fixture_mp.register_event = function(name, callback)
    fixture_events[name] = fixture_events[name] or {}
    table.insert(fixture_events[name], callback)
end
fixture_mp.register_script_message = function(name, callback) fixture_messages[name] = callback end
fixture_mp.observe_property = function(name, _, callback) fixture_observers[name] = callback end
fixture_mp.commandv = function(...) fixture_commands[#fixture_commands + 1] = {...}; return true end
fixture_mp.get_property_native = function(name, fallback)
    fixture_counts.reads = fixture_counts.reads + 1
    if fixture_properties[name] ~= nil then return fixture_properties[name] end
    return fallback
end
fixture_mp.get_property_number = fixture_mp.get_property_native
fixture_mp.get_property = fixture_mp.get_property_native
fixture_mp.set_property = function(name, value)
    fixture_counts.sets = fixture_counts.sets + 1
    fixture_properties[name] = value; return true
end
fixture_mp.set_property_native = fixture_mp.set_property
fixture_mp.set_property_number = fixture_mp.set_property
fixture_mp.get_osd_size = function() return fixture_canvas[1], fixture_canvas[2] end
fixture_mp.get_time = function() return fixture_clock end
fixture_mp.create_osd_overlay = function()
    local overlay = {data = '', removed = true}
    function overlay:update() fixture_counts.updates = fixture_counts.updates + 1; self.removed = false; return {} end
    function overlay:remove() fixture_counts.removes = fixture_counts.removes + 1; self.removed = true end
    return overlay
end
local function make_timer(interval, callback, periodic)
    local timer = {enabled = true, timeout = interval, callback = callback, periodic = periodic, due = fixture_clock + interval}
    function timer:kill() self.enabled = false end
    function timer:resume() if not self.enabled then self.enabled = true; self.due = fixture_clock + self.timeout end end
    function timer:is_enabled() return self.enabled end
    fixture_timers[#fixture_timers + 1] = timer
    return timer
end
fixture_mp.add_periodic_timer = function(interval, callback) return make_timer(interval, callback, true) end
fixture_mp.add_timeout = function(interval, callback) return make_timer(interval, callback, false) end
function fixture_advance(seconds)
    local goal = fixture_clock + seconds
    local steps = 0
    while true do
        local next_timer
        for _, candidate in ipairs(fixture_timers) do
            if candidate.enabled and candidate.due <= goal + 0.00000001
                and (not next_timer or candidate.due < next_timer.due) then next_timer = candidate end
        end
        if not next_timer then break end
        fixture_clock = next_timer.due
        if next_timer.periodic then next_timer.due = fixture_clock + next_timer.timeout else next_timer.enabled = false end
        next_timer.callback()
        steps = steps + 1
        if steps > 100000 then error('Unbounded timer loop') end
    end
    fixture_clock = goal
end
function fixture_fire(name) for _, callback in ipairs(fixture_events[name] or {}) do callback({event = name}) end end
function fixture_call(name, ...) fixture_messages[name](...) end
function fixture_reset_counts() for key in pairs(fixture_counts) do fixture_counts[key] = 0 end end
local fixture_utils = require('mp.utils')
local fixture_format_json = fixture_utils.format_json
fixture_utils.format_json = function(...)
    fixture_counts.json = fixture_counts.json + 1
    return fixture_format_json(...)
end
local fixture_string_format = string.format
string.format = function(...)
    fixture_counts.formats = fixture_counts.formats + 1
    return fixture_string_format(...)
end
"""

class Event(ctypes.Structure):
    _fields_ = [('id', ctypes.c_int), ('error', ctypes.c_int), ('userdata', ctypes.c_uint64), ('data', ctypes.c_void_p)]

class Message(ctypes.Structure):
    _fields_ = [('count', ctypes.c_int), ('args', ctypes.POINTER(ctypes.c_char_p))]

class Log(ctypes.Structure):
    _fields_ = [('prefix', ctypes.c_char_p), ('level', ctypes.c_char_p), ('text', ctypes.c_char_p), ('log_level', ctypes.c_int)]

manifest_path = root / 'assets/mpv-runtime/approved-payload.json'
manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
# This test checks the fixed native entry only; changed UI approval is deliberately left to parent review.
def find_mpv_entry(value):
    if isinstance(value, dict):
        if value.get('path') == 'libmpv-2.dll' and 'sha256' in value:
            return value
        for child in value.values():
            found = find_mpv_entry(child)
            if found:
                return found
    elif isinstance(value, list):
        for child in value:
            found = find_mpv_entry(child)
            if found:
                return found
    return None

entry = find_mpv_entry(manifest)
native_path = root / 'libmpv-2.dll'
native_hash = hashlib.sha256(native_path.read_bytes()).hexdigest()
if not entry or native_hash != entry['sha256'].lower():
    raise SystemExit('Bundled libmpv does not match the fixed approved native digest; do not refresh approval from test input.')
directories = [os.add_dll_directory(str(root)), os.add_dll_directory(str(root / 'assets/mpv-runtime'))]
mpv = ctypes.CDLL(str(native_path))
mpv.mpv_create.restype = ctypes.c_void_p
mpv.mpv_initialize.argtypes = [ctypes.c_void_p]
mpv.mpv_set_option_string.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p]
mpv.mpv_request_log_messages.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
mpv.mpv_wait_event.argtypes = [ctypes.c_void_p, ctypes.c_double]
mpv.mpv_wait_event.restype = ctypes.POINTER(Event)
mpv.mpv_terminate_destroy.argtypes = [ctypes.c_void_p]

reports = []
try:
    for mode in ('danmaku', 'translation'):
        name = 'momoka_danmaku.lua' if mode == 'danmaku' else 'momoka_subtitle_translation.lua'
        source = (args.baseline_source.resolve() / ('baseline-' + name)) if args.baseline_source else root / 'assets/mpv-ui/scripts' / name
        tests = fixture / 'Lua' / (mode + ('-real-timer-tests.lua' if args.real_timers else '-performance-tests.lua'))
        existing = root / 'tests/Momoka.Tests/Lua' / ('danmaku-tests.lua' if mode == 'danmaku' else 'subtitle-translation-tests.lua')
        legacy = existing.read_text(encoding='utf-8').replace(
            "fixture_native_command('script-message', 'fixture-result', utils.format_json(checks))",
            'fixture_legacy_checks = checks')
        # All tests, including legacy functional checks, use the actual script and real bundled parser.
        script = output / name
        flags = ('fixture_baseline = ' + ('true' if args.baseline_source else 'false')
                 + '\nfixture_inject_failure = ' + ('true' if args.inject_failure else 'false') + '\n')
        initial = ''
        snapshot = ''
        if mode == 'translation':
            initial = "fixture_properties = {path='synthetic-loaded-context',['sub-text']='Original line',sid=2,['sub-visibility']=true,['sub-start']=1,['sub-end']=4}\n"
            snapshot = "\nfixture_initial_state = {loaded=loaded,cue=fixture_properties['user-data/momoka-subtitle-translation/cue'],ready=fixture_properties['user-data/momoka-subtitle-translation/ready'],timer_enabled=fixture_timers[#fixture_timers].enabled}\n"
        if args.real_timers:
            # Preserve actual timer methods before loading production code. Only media/property boundaries are fake.
            timer_restore = "fixture_mp.add_timeout = fixture_native_add_timeout\nfixture_mp.get_time = fixture_native_get_time\nfixture_mp.add_periodic_timer = fixture_native_add_periodic_timer\n"
            script.write_text(PRELUDE + flags + timer_restore + source.read_text(encoding='utf-8') + '\n'
                              + tests.read_text(encoding='utf-8'), encoding='utf-8', newline='\n')
        else:
            script.write_text(PRELUDE + flags + initial + source.read_text(encoding='utf-8') + snapshot + '\n' + legacy
                              + '\n' + tests.read_text(encoding='utf-8'), encoding='utf-8', newline='\n')
        handle = mpv.mpv_create()
        checks, errors, payload, completed = [], [], None, False
        if not handle:
            raise RuntimeError('mpv_create failed')
        try:
            for key, value in {
                'config': 'no', 'load-scripts': 'no', 'osc': 'no', 'load-stats-overlay': 'no', 'terminal': 'no',
                'vo': 'null', 'ao': 'null', 'video': 'no', 'audio': 'no', 'idle': 'yes', 'access-references': 'no',
                'load-unsafe-playlists': 'no', 'input-default-bindings': 'no', 'scripts': str(script),
            }.items():
                if mpv.mpv_set_option_string(handle, key.encode(), value.encode()) < 0:
                    raise RuntimeError('Rejected isolated option: ' + key)
            mpv.mpv_request_log_messages(handle, b'error')
            if mpv.mpv_initialize(handle) < 0:
                raise RuntimeError('mpv_initialize failed')
            deadline = time.monotonic() + 90
            while time.monotonic() < deadline and not completed:
                event = mpv.mpv_wait_event(handle, 0.05).contents
                if event.id == 16:
                    message = ctypes.cast(event.data, ctypes.POINTER(Message)).contents
                    values = [message.args[index].decode('utf-8', 'replace') for index in range(message.count)]
                    if values and values[0] == 'perf-fixture-result':
                        payload = json.loads(values[1])
                        checks, completed = payload.get('checks', []), payload.get('completed', False)
                elif event.id == 2:
                    log = ctypes.cast(event.data, ctypes.POINTER(Log)).contents
                    errors.append(log.text.decode('utf-8', 'replace').strip())
            # Drain error events queued before completion; completion must not hide a Lua error.
            for _ in range(100):
                event = mpv.mpv_wait_event(handle, 0).contents
                if event.id == 0:
                    break
                if event.id == 2:
                    log = ctypes.cast(event.data, ctypes.POINTER(Log)).contents
                    errors.append(log.text.decode('utf-8', 'replace').strip())
        finally:
            mpv.mpv_terminate_destroy(handle)
        report = {'mode': mode, 'completed': completed, 'checks': checks, 'errors': errors, 'payload': payload,
                  'source': str(source), 'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                  'source_size': source.stat().st_size, 'fixture_sha256': hashlib.sha256(tests.read_bytes()).hexdigest(),
                  'legacy_fixture_sha256': hashlib.sha256(existing.read_bytes()).hexdigest(),
                  'native_sha256': native_hash, 'baseline': bool(args.baseline_source), 'real_timers': args.real_timers,
                  'coverage': ('Bundled LuaJIT idle context; actual native Lua timer event loop, fake canvas/properties/events/overlay; ' if args.real_timers else 'Bundled LuaJIT idle context; fake clock/canvas/events/overlay, real JSON and selected libass checks; ') + 'no media/window/network/user settings. UI approval not run or refreshed.'}
        (output / (mode + '-results.json')).write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
        for check in checks:
            print(('PASS ' if check['passed'] else 'FAIL ') + check['name'] + ': ' + check.get('detail', ''))
        print(f'{mode}: completed={completed}; checks={len(checks)}; failed={sum(not c["passed"] for c in checks)}; errors={len(errors)}')
        if errors:
            print('\n'.join(errors))
        reports.append(report)
finally:
    for directory in directories:
        directory.close()
sys.exit(0 if reports and all(r['completed'] and r['checks'] and all(c['passed'] for c in r['checks']) and not r['errors'] for r in reports) else 1)
