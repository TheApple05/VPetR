"""Render motion evidence from the exact validated pet atlas."""
from pathlib import Path
from PIL import Image,ImageDraw
import json
import subprocess
import sys
import imageio_ffmpeg

ROOT=Path(__file__).resolve().parents[1]
RUN=ROOT/'Library/Pets/Rimuru'
QA=RUN/'qa'
OUT=RUN/'previews';OUT.mkdir(exist_ok=True)
PET_CACHE=Path.home()/'.codex/plugins/cache/openai-curated-remote/work-pets'
SKILL=next((path for path in sorted(PET_CACHE.glob('*/skills/create-pet/scripts'),reverse=True)
            if (path/'render_animation_previews.py').is_file()),None)
if SKILL is None:
    raise FileNotFoundError(f'Pets authoring helper render_animation_previews.py was not found under {PET_CACHE}. Install the Work Pets plugin first.')
sys.path.insert(0,str(ROOT/'scripts'))
from export_vpet import STATES,DURATIONS

atlas=Image.open(RUN/'final/spritesheet-extended.png').convert('RGBA')
frames={}
for row,state in enumerate(STATES):
    target=QA/'final-frames'/state;target.mkdir(parents=True,exist_ok=True)
    frames[state]=[]
    for col in range(len(DURATIONS[row])):
        im=atlas.crop((col*192,row*208,(col+1)*192,(row+1)*208))
        im.save(target/f'{col:02}.png');frames[state].append(im)
subprocess.run([sys.executable,str(SKILL/'render_animation_previews.py'),
                '--frames-root',str(QA/'final-frames'),'--output-dir',str(OUT/'states')],check=True)

def panel(im,label):
    out=Image.new('RGB',(192,232),'#eaf0f7');out.paste(im,(0,24),im)
    ImageDraw.Draw(out).text((8,7),label,fill='#15243b')
    return out

def gif(images,durations,path):
    images[0].save(path,save_all=True,append_images=images[1:],duration=durations,loop=0,disposal=2)

display={'running-right':'Move right','running-left':'Move left','running':'Focused work',
         'idle':'Idle','waving':'Wave','jumping':'Jump','failed':'Oops','waiting':'Waiting','review':'Review'}
grid=[]
for t in range(0,4080,80):
    canvas=Image.new('RGB',(576,696),'#eaf0f7')
    for row,state in enumerate(STATES):
        durations=DURATIONS[row];position=t%sum(durations);index=0
        while position>=durations[index]:position-=durations[index];index+=1
        canvas.paste(panel(frames[state][index],display[state]),((row%3)*192,(row//3)*232))
    grid.append(canvas)
gif(grid,80,OUT/'all-states.gif')

transition=[*frames['idle'],*frames['jumping'],*frames['idle']]
tdurations=[*DURATIONS[0],*DURATIONS[4],*DURATIONS[0]]
gif([panel(im,'Idle / jump / idle') for im in transition],tdurations,OUT/'idle-jump-idle.gif')
look=[]
for i in range(16):
    r=9+i//8;c=i%8
    look.append(panel(atlas.crop((c*192,r*208,(c+1)*192,(r+1)*208)),f'Look {i*22.5:g} degrees'))
gif(look,150,OUT/'look-loop.gif')
stills=Image.new('RGB',(768,232),'#eaf0f7')
for n,(state,index,label) in enumerate([('idle',0,'Idle'),('waving',2,'Wave'),('jumping',2,'Jump peak'),('running',2,'Focused work')]):
    p=panel(frames[state][index],label);stills.paste(p,(n*192,0));p.save(OUT/f'still-{n+1:02}.png')
stills.save(OUT/'four-poses.png')

ffmpeg=imageio_ffmpeg.get_ffmpeg_exe()
result=subprocess.run([ffmpeg,'-y','-i',str(OUT/'all-states.gif'),'-an','-c:v','libx264',
                       '-pix_fmt','yuv420p','-movflags','+faststart',str(OUT/'all-states.mp4')],capture_output=True,text=True)
(QA/'preview-video-encoding.log').write_text(result.stderr)
result.check_returncode()
(OUT/'manifest.json').write_text(json.dumps({'source':'final/spritesheet-extended.png',
    'standard_states':9,'look_directions':16,'video':'all-states.mp4','gif':'all-states.gif',
    'transition':'idle-jump-idle.gif','four_stills':'four-poses.png'},indent=2))
print('Final previews saved:',OUT)
