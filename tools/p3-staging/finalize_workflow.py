"""Complete P3.0 tool wiring and preserve the actual calibration batch."""
from pathlib import Path
import json
import shutil

root=Path('C:/ws/asset-production-system');skill=root/'solo-vs-mortal-art';here=Path(__file__).resolve().parent
for n in ('native_preview.py','normalize_native.py','readability_gate.py'):shutil.copy2(here/n,skill/'scripts'/n)
p=skill/'references/style-lock.json';s=json.loads(p.read_text())
# Explicit new calibration roles preserve all old role contracts and pivots.
s['roles']['actor_readability']={'canvas':[64,64],'defaultPivot':[32,60],'alpha':'binary','requiresTransparency':True,'maxColors':16}
s['roles']['architectural_prop']={'canvas':[64,112],'defaultPivot':[32,106],'alpha':'binary','requiresTransparency':True,'maxColors':16}
p.write_text(json.dumps(s,indent=2)+'\n')
p=skill/'scripts/validate_assets.py';s=p.read_text(encoding='utf-8')
s=s.replace('from PIL import Image','from PIL import Image\nfrom readability_gate import issues as readability_issues')
s=s.replace("if role in ('actor', 'large_actor', 'huge_actor')", "if role in ('actor', 'large_actor', 'huge_actor', 'actor_readability')")
s=s.replace("            if status == 'approved':", "            if status == 'approved':\n                errors.extend(f'{label}: {issue}' for issue in readability_issues(root,asset))")
p.write_text(s,encoding='utf-8')
# Keep original processing code callable for historical reproduction. New work
# follows the skill's explicit-recipe path; readiness is enforced at promotion.
for rel in ('art/production/normalize_player_slice01.py','art/production/normalize_monster_batch.py','art/production/prepare_souls_batch.py'):
    p=root/rel;s=p.read_text(encoding='utf-8');s='# P3.0: historical normalizer. New batches use scripts/normalize_native.py and native-readability.md.\n'+s;p.write_text(s,encoding='utf-8')
p=root/'art/production/work/world.slice_01/world_pipeline.py';s=p.read_text(encoding='utf-8');p.write_text('# P3.0: historical fit/quantize path; new batches require explicit final-size recipes and material ramps.\n'+s,encoding='utf-8')
dest=root/'art/production/work/readability-p3-v001'
shutil.copytree(here.parents[1]/'docs/V2.5/readability/batch',dest,dirs_exist_ok=True)
shutil.copy2(here.parents[1]/'docs/V2.5/readability/root-cause-audit.md',dest/'root-cause-audit.md')
print('Updated native gate/validator, documented historical normalizers, saved small calibration batch.')
