"""Export approved generated poses as a native VPet character mod."""
from pathlib import Path
from PIL import Image
import argparse
import hashlib
import json
import os

ROOT = Path(__file__).resolve().parents[1]
RUN = ROOT / 'Library/Pets/Rimuru'
STATES = ['idle', 'running-right', 'running-left', 'waving', 'jumping',
          'failed', 'waiting', 'running', 'review']
DURATIONS = [[280,110,110,140,140,320], [120]*7+[220], [120]*7+[220],
             [140]*3+[280], [140]*4+[280], [140]*7+[240],
             [150]*5+[260], [120]*5+[220], [150]*5+[280]]

def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding='utf-8')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--atlas', type=Path, default=RUN/'final/spritesheet-extended.png')
    parser.add_argument('--output', type=Path, default=ROOT.parent/'VPet-Simulator.Windows/mod/0100_rimuru')
    args = parser.parse_args()
    atlas = Image.open(args.atlas).convert('RGBA')
    assert atlas.size in [(1536,1872),(1536,2288)]
    mod = args.output
    pet = mod/'pet/rimuru'
    families={state:([atlas.crop((col*192,row*208,(col+1)*192,(row+1)*208))
                     for col in range(len(DURATIONS[row]))],DURATIONS[row])
              for row,state in enumerate(STATES)}
    expansion=RUN/'native-expansion'
    native=json.loads((expansion/'animations.json').read_text())
    for job in native['jobs']:
        state=job['id'];qa=json.loads((expansion/'qa'/f'{state}.json').read_text())
        assert qa['ok'] and qa['frames']==6
        images=[Image.open(expansion/'frames'/state/f'{n:02}.png').convert('RGBA') for n in range(6)]
        assert all(im.size==(192,208) and hashlib.sha256(im.tobytes()).hexdigest()==record['sha256']
                   for im,record in zip(images,qa['poses'],strict=True)), f'Unvalidated frames: {state}'
        families[state]=(images,native['frame_ms'])
    for state,(images,durations) in families.items():
        folder=pet/'frames'/state
        folder.mkdir(parents=True,exist_ok=True)
        for old in folder.glob('*.png'): old.unlink()
        for col,(frame,duration) in enumerate(zip(images,durations,strict=True)):
            canvas = Image.new('RGBA',(256,256))
            canvas.alpha_composite(frame.resize((236,256),Image.Resampling.LANCZOS),(10,0))
            path = pet/'frames'/state/f'{col:03}_{duration}.png'
            path.parent.mkdir(parents=True,exist_ok=True)
            canvas.save(path)
    write(mod/'info.lps',
          'vupmod#Rimuru Companion:|author#Personal Collection:|gamever#11000:|ver#200:|\n'
          'intro#A blue-haired golden-eyed companion inspired by your fan art. English edition.:|\n')
    geometry = '\n'.join([
        'pet#rimuru:|intor#A cheerful chibi companion inspired by your Rimuru art collection.:|path#rimuru:|petname#Rimuru:|',
        'tag#rimuru:|',
        'touchhead:|px#120:|py#80:|sw#260:|sh#230:|',
        'touchbody:|px#150:|py#300:|sw#200:|sh#150:|',
        'touchraised:|' + ''.join(f'{mode}_px#110:|{mode}_py#70:|{mode}_sw#280:|{mode}_sh#240:|' for mode in ['happy','nomal','poorcondition','ill']),
        'raisepoint:|' + ''.join(f'{mode}_x#250:|{mode}_y#160:|' for mode in ['happy','nomal','poorcondition','ill']),
        'side:|left#170:|right#330:|',
        'duration:|state#12:|resting#12:|sit#12:|stretch#10:|sleep#20:|',
        'work:|Type#Work:|Name#Focus session:|MoneyBase#8:|Graph#focus:|StrengthFood#2:|StrengthDrink#2:|Feeling#1:|Time#30:|FinishBonus#0.1:|Left#110:|Top#330:|Width#280:|',
        'work:|Type#Study:|Name#Study together:|MoneyBase#20:|Graph#study:|StrengthFood#2:|StrengthDrink#2:|Feeling#1:|Time#30:|FinishBonus#0.1:|Left#110:|Top#330:|Width#280:|',
        'work:|Type#Play:|Name#Take a break:|MoneyBase#8:|Graph#play:|StrengthFood#1:|StrengthDrink#1:|Feeling#-1:|Time#15:|FinishBonus#0.1:|Left#110:|Top#330:|Width#280:|',
        'move:|graph#walk.left:|TriggerLeft#200:|TriggerType#16:|CheckLeft#100:|CheckType#16:|SpeedX#-12:|Distance#6:|ModeType#14:|',
        'move:|graph#walk.right:|TriggerRight#200:|TriggerType#32:|CheckRight#100:|CheckType#32:|SpeedX#12:|Distance#6:|ModeType#14:|',
    ]) + '\n'
    write(mod/'pet/rimuru.lps',geometry)
    mappings = [
        ('default','Default','idle'), ('startup','StartUP','waving'),
        ('shutdown','Shutdown','waving'), ('raised','Raised_Dynamic','carry'),
        ('raised','Raised_Static','carry'), ('touch.head','Touch_Head','headpat'),
        ('touch.body','Touch_Body','bodypat'), ('idle','Idel','idle'),
        ('sit','Idel','sit'), ('stretch','Idel','stretch'), ('rest','Idel','rest'), ('inspect','Idel','review'),
        ('sleep','Sleep','sleep'), ('say','Say','talk'),
        ('resting','StateONE','sit'), ('resting','StateTWO','rest'),
        ('focus','Work','running'), ('study','Work','study'), ('play','Work','jumping'),
        ('walk.left','Move','running-left'), ('walk.right','Move','running-right'),
        ('switch.up','Switch_Up','cheer'), ('switch.down','Switch_Down','failed'),
        ('thirsty','Switch_Thirsty','waiting'), ('hungry','Switch_Hunger','waiting'),
        ('eat','Common','eat'), ('drink','Common','drink'), ('gift','Common','cheer'), ('levelup','Common','cheer'),
    ]
    lines=[]
    for name,kind,state in mappings:
        variants=(['A_Start','B_Loop','C_End'] if kind in
                  {'Raised_Static','Touch_Head','Touch_Body','Sleep','Say','StateONE','StateTWO','Work','Move'} else ['Single'])
        for mode in ['Nomal','Ill']:
            for variant in variants:
                used = 'rest' if mode=='Ill' and kind=='Default' else state
                lines.append(f'pnganimation#{name}:|path#frames\\{used}:|graph#{kind}:|animat#{variant}:|mode#{mode}:|')
    write(pet/'info.lps','\n'.join(lines)+'\n')
    dialogue = [
        'clicktext:|LikeMin#0:|State#Nomal:|Text#Hey! Ready to make something cool together?:|tag#rimuru:|',
        'clicktext:|LikeMin#0:|State#Nomal:|Text#You have got this. I will keep you company!:|tag#rimuru:|',
        'clicktext:|LikeMin#0:|State#Nomal:|Text#A little break sounds nice. Let us recharge.:|tag#rimuru:|',
        'clicktext:|LikeMin#0:|State#Nomal:|Text#One step at a time. We will figure it out together.:|tag#rimuru:|',
        'LowFoodText:|Mode#H:|Strength#L:|Like#N:|Text#Snack break? I could go for something tasty!:|tag#rimuru:|',
        'LowDrinkText:|Mode#H:|Strength#L:|Like#N:|Text#Time for a sip of water. You too!:|tag#rimuru:|',
    ]
    write(mod/'text/rimuru.lps','\n'.join(dialogue)+'\n')
    write(mod/'README-English.txt',
          'RIMURU COMPANION\n\nCopy this entire 0100_rimuru folder into a VPet mod folder.\n'
          'Enable it in Settings > Mods, select Rimuru, and select English. Restart VPet.\n\n'
          'Includes 21 pose families and 129 frames, including dedicated sleep, touch,\n'
          'eating, drinking, talking, reading, carry, rest, stretch and celebration loops.\n'
          'Random idle actions use sitting, stretching, resting and inspection.\n'
          'Eating and drinking show a bread roll and mug for all corresponding items.\n'
          'No code plugins, account credentials, or paid services are required.\n')
    report={'atlas':Path(os.path.relpath(args.atlas, ROOT.parent)).as_posix(),'atlas_sha256':hashlib.sha256(args.atlas.read_bytes()).hexdigest(),
            'png_frames':len(list(pet.rglob('*.png'))),'graph_definitions':len(lines),
            'pose_families':len(families),'sprite_size':256,
            'families':{state:len(images) for state,(images,_) in families.items()},
            'character':'rimuru','language':'en','output':Path(os.path.relpath(mod, ROOT.parent)).as_posix()}
    write(mod/'export-manifest.json',json.dumps(report,indent=2))
    print(json.dumps(report,indent=2))

if __name__=='__main__':main()
