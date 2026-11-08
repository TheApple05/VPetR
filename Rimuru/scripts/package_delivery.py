"""Package the tested native build and installable character with fresh defaults."""
from pathlib import Path
from zipfile import ZipFile,ZIP_DEFLATED
import hashlib,json,shutil,tempfile

ROOT=Path(__file__).resolve().parents[1]
APP=ROOT/'dist/Rimuru-VPet'
shutil.copy2(ROOT/'README.md',APP/'README-English.md')
shutil.copy2(ROOT.parent/'README_en.md',APP/'UPSTREAM-README.md')
settings=('Setting#VPET:|\nlanguage#en:|\ngameconfig:|petgraph#rimuru:|\n'
          'SingleTips:|tutorial#2026/11/03:|helloworld#True:|\n')

def allowed(path):
    rel=path.relative_to(APP)
    if any(p.lower() in {'cache','saves','saves_bkp','backup'} for p in rel.parts):return False
    if len(rel.parts)==1 and path.name.lower().startswith('save') and path.suffix.lower()=='.lps':return False
    if path.suffix.lower() in {'.log','.bkp'}:return False
    if path.name=='disable-auto-updates.txt' or path.name.startswith('rimuru-update-'):return False
    if path.name.startswith(('rimuru-diagnostics','rimuru-live-control','Setting-')):return False
    return True

records=[]
portable=ROOT/'dist/Rimuru-VPet-Portable.zip'
with ZipFile(portable,'w',ZIP_DEFLATED,compresslevel=5) as z:
    for p in APP.rglob('*'):
        if not p.is_file() or not allowed(p):continue
        rel=p.relative_to(APP).as_posix()
        if rel=='Setting.lps':z.writestr('Rimuru-VPet/'+rel,settings)
        else:z.write(p,'Rimuru-VPet/'+rel)
with ZipFile(portable) as z:
    assert z.testzip() is None
    assert not any(part.lower() in {'saves','saves_bkp','backup'} for name in z.namelist() for part in Path(name).parts)
records.append({'file':portable.name,'bytes':portable.stat().st_size,'sha256':hashlib.sha256(portable.read_bytes()).hexdigest()})
mod=ROOT.parent/'VPet-Simulator.Windows/mod/0100_rimuru'
modzip=ROOT/'dist/Rimuru-Character-Mod.zip'
with ZipFile(modzip,'w',ZIP_DEFLATED,compresslevel=5) as z:
    for p in mod.rglob('*'):
        if p.is_file():z.write(p,'0100_rimuru/'+p.relative_to(mod).as_posix())
records.append({'file':modzip.name,'bytes':modzip.stat().st_size,'sha256':hashlib.sha256(modzip.read_bytes()).hexdigest()})
(ROOT/'dist/package-manifest.json').write_text(json.dumps(records,indent=2))
print(json.dumps(records,indent=2))
smoke=Path(tempfile.mkdtemp(prefix='portable-smoke-',dir=ROOT/'dist'))
with ZipFile(portable) as z:
    for member in z.infolist():
        destination=(smoke/member.filename).resolve()
        assert destination.is_relative_to(smoke.resolve())
    z.extractall(smoke)
(ROOT/'dist/portable-smoke-path.txt').write_text(str(smoke/'Rimuru-VPet'))
print('Exact portable ZIP extracted for smoke test:',smoke)
