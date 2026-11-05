"""Execute production uosc input modules in a supplied Lua 5.1 interpreter, without mpv."""
import argparse
import ctypes
import sys
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--lua', type=Path, required=True, help='Path to a trusted Lua 5.1 shared library')
args = parser.parse_args()
sys.stdout.reconfigure(encoding='utf-8')
root = Path(__file__).resolve().parents[1]
lib = ctypes.CDLL(str(args.lua.resolve()))
lib.luaL_newstate.restype = ctypes.c_void_p
lib.luaL_openlibs.argtypes = [ctypes.c_void_p]
lib.luaL_loadstring.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
lib.lua_pcall.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int, ctypes.c_int]
lib.lua_getfield.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_char_p]
lib.lua_tolstring.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.POINTER(ctypes.c_size_t)]
lib.lua_tolstring.restype = ctypes.c_void_p
lib.lua_tointeger.argtypes = [ctypes.c_void_p, ctypes.c_int]
lib.lua_tointeger.restype = ctypes.c_longlong
lib.lua_close.argtypes = [ctypes.c_void_p]

def text(state, index):
    size = ctypes.c_size_t()
    value = lib.lua_tolstring(state, index, ctypes.byref(size))
    return ctypes.string_at(value, size.value).decode('utf-8', 'replace') if value else ''

source = "fixture_root = [=[" + root.as_posix() + "]=]\n"
source += (root / 'tests/Momoka.Tests/Lua/uosc-review-tests.lua').read_text(encoding='utf-8')
state = lib.luaL_newstate()
try:
    lib.luaL_openlibs(state)
    status = lib.luaL_loadstring(state, source.encode('utf-8'))
    if status == 0:
        status = lib.lua_pcall(state, 0, 0, 0)
    if status:
        print(text(state, -1))
    else:
        lib.lua_getfield(state, -10002, b'fixture_result')
        print(text(state, -1))
        lib.lua_getfield(state, -10002, b'fixture_failures')
        status = int(lib.lua_tointeger(state, -1))
finally:
    lib.lua_close(state)
raise SystemExit(1 if status else 0)
