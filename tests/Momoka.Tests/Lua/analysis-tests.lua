local fixture_results={}
local function test(name, fn)
 local ok, detail=pcall(fn)
 fixture_results[#fixture_results+1]={name=name,passed=ok,detail=ok and '' or tostring(detail)}
end
local function eq(a,b) if a~=b then error(tostring(a)..' ~= '..tostring(b)) end end
local function generation()return fixture_props['user-data/momoka-analysis-generation'] end
local function command_count(kind)
 local n=0;for _,cmd in ipairs(fixture_commands)do if cmd[1]==kind then n=n+1 end end;return n
end
local crop_missing = fixture_without_crop
if crop_missing then
 test('missing cropdetect stays hidden and never sends a filter command',function()
  fixture_events['file-loaded']()
  eq(fixture_props['user-data/momoka-analysis-crop-available'],false)
  local count=command_count('vf');fixture_messages.crop(generation());eq(command_count('vf'),count)
 end)
end
if not crop_missing then
test('crop metadata commits only a stable bounded rectangle',function()
 fixture_messages.crop(generation());local t=fixture_timers[#fixture_timers]
 fixture_props['vf-metadata/momoka_cropdetect']={['lavfi.cropdetect.w']='1920',['lavfi.cropdetect.h']='800',['lavfi.cropdetect.x']='0',['lavfi.cropdetect.y']='140'}
 t.callback();fixture_time=fixture_time+1.1;t.callback()
 eq(fixture_props['video-crop'],'1920x800+0+140');eq(fixture_props['user-data/momoka-analysis-active'],true)
 fixture_messages.crop(generation());eq(fixture_props['video-crop'],'');eq(fixture_props['user-data/momoka-analysis-active'],false)
end)
test('crop rejects native hardware frames and re-enables copy decoding',function()
 fixture_props['hwdec-current']='d3d11va';fixture_events['video-reconfig']();eq(fixture_props['user-data/momoka-analysis-crop-available'],false)
 local count=command_count('vf');fixture_messages.crop(generation());eq(command_count('vf'),count)
 fixture_props['hwdec-current']='d3d11va-copy';fixture_events['video-reconfig']();eq(fixture_props['user-data/momoka-analysis-crop-available'],true)
end)
test('file boundary rejects late crop timers and old messages',function()
 fixture_messages.crop(generation());local t=fixture_timers[#fixture_timers];local old=generation()
 fixture_hooks.on_unload();fixture_events['start-file']();local count=command_count('vf')
 t.callback();fixture_messages.crop(old);eq(command_count('vf'),count);eq(fixture_props['video-crop'],'')
end)
end
test('silence detection restores original state and seeks once',function()
 fixture_props['time-pos']=10;fixture_props.speed=1;fixture_props.mute=false;fixture_props.pause=true
 fixture_events['file-loaded']();fixture_messages.silence(generation())
 eq(fixture_props.speed,8);eq(fixture_props.mute,true);eq(fixture_props.pause,false)
 fixture_observers['af-metadata/momoka_silence']('meta',{['lavfi.silence_start']='15'})
 eq(fixture_props.speed,1);eq(fixture_props.mute,false);eq(fixture_props.pause,true)
 eq(fixture_commands[#fixture_commands][1],'seek');eq(fixture_commands[#fixture_commands][2],'15')
end)
test('unload restores silence state without letting old callback seek successor',function()
 fixture_props['time-pos']=20;fixture_props.pause=false;fixture_messages.silence(generation())
 local callback=fixture_observers['af-metadata/momoka_silence'];fixture_hooks.on_unload();fixture_events['start-file']()
 local count=command_count('seek');callback('meta',{['lavfi.silence_start']='30'});eq(command_count('seek'),count);eq(fixture_props.speed,1)
end)
test('passthrough audio hides and rejects silence scanning',function()
 fixture_props['audio-out-params']={format='spdif-ac3'};fixture_events['audio-reconfig']()
 eq(fixture_props['user-data/momoka-analysis-silence-available'],false);local count=command_count('af');fixture_messages.silence(generation());eq(command_count('af'),count)
end)
test('chapter bridge validates data and rejects stale generations',function()
 fixture_messages.chapters('[{"time":1,"title":"Chapter"}]',generation());eq(fixture_props['chapter-list'][1].title,'Chapter')
 fixture_messages.chapters('[{"time":-1,"title":"Bad"}]',generation());eq(fixture_props['chapter-list'][1].title,'Chapter')
 fixture_messages.chapters('{}',generation());eq(fixture_props['chapter-list'][1].title,'Chapter')
 fixture_messages.chapters('[{"time":2,"title":"Old"}]','-1');eq(fixture_props['chapter-list'][1].title,'Chapter')
end)
for k,v in pairs(fixture_original)do fixture_native[k]=v end
fixture_original.commandv('script-message','fixture-result',fixture_utils.format_json(fixture_results))
