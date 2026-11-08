"""Extract one generated native animation using the existing pet component fitter."""
from pathlib import Path
import argparse, hashlib, importlib.util, json
from PIL import Image, ImageDraw

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('state')
parser.add_argument('--height',type=int,default=174)
args=parser.parse_args()
assert 80 <= args.height <= 198
root=Path(__file__).resolve().parents[1]/'Library/Pets/Rimuru/native-expansion'
source=root/'strips'/f'{args.state}.png'
PET_CACHE=Path.home()/'.codex/plugins/cache/openai-curated-remote/work-pets'
SKILL=next((path for path in sorted(PET_CACHE.glob('*/skills/create-pet/scripts'),reverse=True)
            if (path/'extract_strip_frames.py').is_file()),None)
if SKILL is None:
    raise FileNotFoundError(f'Pets authoring helper extract_strip_frames.py was not found under {PET_CACHE}. Install the Work Pets plugin first.')
spec=importlib.util.spec_from_file_location('pet_extract',SKILL/'extract_strip_frames.py')
extract=importlib.util.module_from_spec(spec);spec.loader.exec_module(extract)
strip=Image.open(source).convert('RGBA')
components=extract.connected_components(strip)
largest=max(c['area'] for c in components)
assert len([c for c in components if c['area']>=max(120,largest*.2)])==6, 'Need exactly six separated complete poses'
frames=extract.extract_component_frames(strip,6)
assert frames is not None and len(frames)==6
bounds=[im.getchannel('A').point(lambda a:255 if a>16 else 0).getbbox() for im in frames]
assert all(bounds), 'Empty pose'
height=max(b[3]-b[1] for b in bounds)
scale=min(1,args.height/height)
ground=max(b[3] for b in bounds)
target=root/'frames'/args.state;target.mkdir(parents=True,exist_ok=True)
registered=[];records=[]
for n,im in enumerate(frames):
    resized=im.resize((round(192*scale),round(208*scale)),Image.Resampling.LANCZOS)
    cell=Image.new('RGBA',(192,208))
    cell.alpha_composite(resized,((192-resized.width)//2,202-round(ground*scale)))
    bbox=cell.getchannel('A').point(lambda a:255 if a>16 else 0).getbbox()
    assert bbox and bbox[0]>0 and bbox[1]>0 and bbox[2]<192 and bbox[3]<208, 'Clipped pose'
    cell.save(target/f'{n:02}.png');registered.append(cell)
    records.append({'frame':n,'bbox':bbox,'sha256':hashlib.sha256(cell.tobytes()).hexdigest()})
assert len({r['sha256'] for r in records})>=3, 'Animation needs distinct poses'
qa=root/'qa';qa.mkdir(exist_ok=True)
contact=Image.new('RGB',(1152,232),'#eaf0f7');draw=ImageDraw.Draw(contact)
for n,im in enumerate(registered):
    contact.paste(im,(n*192,24),im);draw.text((n*192+8,6),f'{args.state} {n+1}',fill='#15243b')
contact.save(qa/f'{args.state}-contact.png')
preview=[]
for im in registered:
    bg=Image.new('RGB',(192,208),'#eaf0f7');bg.paste(im,(0,0),im);preview.append(bg)
preview[0].save(qa/f'{args.state}.gif',save_all=True,append_images=preview[1:],duration=[180,160,180,180,180,300],loop=0)
report={'ok':True,'state':args.state,'frames':6,'shared_scale':scale,'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'poses':records}
(qa/f'{args.state}.json').write_text(json.dumps(report,indent=2))
print(json.dumps({'ok':True,'state':args.state,'frames':6,'contact':str(qa/f'{args.state}-contact.png')}))
