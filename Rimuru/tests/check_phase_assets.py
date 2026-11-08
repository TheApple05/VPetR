"""Check that phase boundaries join on identical poses and new idle art is distinct."""
from pathlib import Path
import json

root = Path(__file__).resolve().parents[1]
frames = root.parent/'VPet-Simulator.Windows/mod/0100_rimuru/pet/rimuru/frames'
count = 0
for family in frames.iterdir():
    if not (family/'start').is_dir():
        continue
    start, loop, end = [sorted((family/phase).glob('*.png')) for phase in ('start', 'loop', 'end')]
    assert start and loop and end, family
    assert start[-1].read_bytes() == loop[0].read_bytes(), f'{family.name}: start/loop pop'
    assert loop[-1].read_bytes() == end[0].read_bytes(), f'{family.name}: loop/end pop'
    assert end[-1].read_bytes() == start[0].read_bytes(), f'{family.name}: exit pose differs from entry'
    assert len(loop) > len(start), f'{family.name}: start must not replay the full loop'
    count += 1
for name in ('lookaround', 'scarf', 'yawn', 'sway'):
    qa = json.loads((root/'Library/Pets/Rimuru/native-expansion/qa'/f'{name}.json').read_text())
    assert qa['ok'] and len({pose['sha256'] for pose in qa['poses']}) >= 3
assert count >= 15
print(f'Passed {count} phase families: identical boundary poses, distinct short phases, four new idle loops.')
