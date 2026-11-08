"""Preview the twelve new loops from the actual exported native sprites."""
from pathlib import Path
from PIL import Image,ImageDraw
import json,subprocess,imageio_ffmpeg

root=Path(__file__).resolve().parents[1]
run=root/'Library/Pets/Rimuru/native-expansion'
request=json.loads((run/'animations.json').read_text())
native=root.parent/'VPet-Simulator.Windows/mod/0100_rimuru/pet/rimuru/frames'
loops=[]
for job in request['jobs']:
    files=sorted((native/job['id']).glob('*.png'))
    assert len(files)==6
    images=[Image.open(p).convert('RGBA') for p in files]
    assert all(im.size==(256,256) for im in images)
    loops.append(images)
out=run/'previews';out.mkdir(exist_ok=True)
frames=[]
for n in range(6):
    grid=Image.new('RGB',(768,696),'#eaf0f7');draw=ImageDraw.Draw(grid)
    for i,(job,images) in enumerate(zip(request['jobs'],loops,strict=True)):
        x=i%4*192;y=i//4*232;im=images[n].resize((192,192),Image.Resampling.LANCZOS)
        grid.paste(im,(x,y+30),im);draw.text((x+8,y+8),job['label'],fill='#15243b')
    frames.append(grid)
frames[0].save(out/'new-animations.gif',save_all=True,append_images=frames[1:],duration=request['frame_ms'],loop=0,disposal=2)
frames[2].save(out/'new-animations.png')
result=subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(),'-y','-stream_loop','5','-i',str(out/'new-animations.gif'),'-an','-c:v','libx264','-pix_fmt','yuv420p','-movflags','+faststart',str(out/'new-animations.mp4')],capture_output=True,text=True)
result.check_returncode()
print(out)
