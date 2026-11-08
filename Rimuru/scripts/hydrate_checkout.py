"""Hydrate the pinned partial Git clone through verified raw GitHub blobs."""
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
import hashlib
import json
import subprocess
import time
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent

def git(*args, **kwargs):
    return subprocess.check_output(['git', '-C', str(REPO), *args], **kwargs)

commit = git('rev-parse', 'HEAD').decode().strip()
entries = []
for record in git('ls-tree', '-r', '-z', 'HEAD').split(b'\0'):
    if not record:
        continue
    metadata, path = record.split(b'\t', 1)
    mode, kind, sha = metadata.split()
    if kind == b'blob':
        entries.append({'type': 'blob', 'path': path.decode('utf-8'),
                        'sha': sha.decode()})

def wanted(path):
    return not (path.startswith('README.assets/') or
                path.startswith('VPet-Simulator.Windows/mod/0000_core/pet/vup/') or
                path.endswith('.zlps'))

rows = [x for x in entries if wanted(x['path'])]

def fetch(row):
    target = REPO / row['path']
    target.parent.mkdir(parents=True, exist_ok=True)
    for attempt in range(3):
        try:
            if target.is_file():
                data = target.read_bytes()
            else:
                url = 'https://raw.githubusercontent.com/TheApple05/VPetR/' + commit + '/' + urllib.parse.quote(row['path'])
                data = urllib.request.urlopen(url, timeout=45).read()
            digest = hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest()
            if digest != row['sha']:
                raise ValueError('blob hash mismatch: ' + row['path'])
            target.write_bytes(data)
            stored = git('hash-object', '-w', '--stdin', input=data).decode().strip()
            assert stored == row['sha']
            return len(data)
        except Exception:
            if attempt == 2:
                raise
            time.sleep(attempt + 1)

total = 0
with ThreadPoolExecutor(max_workers=16) as pool:
    jobs = [pool.submit(fetch, row) for row in rows]
    for n, job in enumerate(as_completed(jobs), 1):
        total += job.result()
        if n % 50 == 0 or n == len(rows):
            print(f'Verified {n}/{len(rows)} files, {total / 1e6:.1f} MB', flush=True)

report = {'commit': commit, 'files': len(rows), 'bytes': total,
          'verification': 'Every blob SHA-1 equals the pinned Git tree entry',
          'omitted': ['stock vup character frames', 'README media', 'optional .zlps gallery archives']}
(ROOT / 'checkout-verification.json').write_text(json.dumps(report, indent=2))
print(json.dumps(report, indent=2))
