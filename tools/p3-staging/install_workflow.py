"""Apply the explicitly requested P3.0 workflow refactor to the asset repository."""
from pathlib import Path
import json
import shutil

root=Path('C:/ws/asset-production-system').resolve()
skill=root/'solo-vs-mortal-art'; here=Path(__file__).resolve().parent
assert (root/'AGENTS.md').is_file() and (skill/'SKILL.md').is_file()
for name in ('native_preview.py','normalize_native.py','readability_gate.py'):
    shutil.copy2(here/name,skill/'scripts'/name)
shutil.copy2(here/'native-readability.md',skill/'references/native-readability.md')
entry=skill/'SKILL.md';s=entry.read_text(encoding='utf-8')
s=s.replace('## Đọc theo task','## P3.0 — Native readability is blocking\n\nRead [native-readability.md](references/native-readability.md) before every world raster task. Produce native 1x/2x evidence with `scripts/native_preview.py`; author large semantic forms at final gameplay pixels. Use explicit family recipes in `scripts/normalize_native.py`, not legacy bbox-fitting scripts. New integration requires current-hash readability gates; technical pass and old trial authorization are insufficient. The current user explicitly requests agent readability diagnosis and a small regenerated comparison batch; retain userReview=pending and do not claim gameplay acceptance. Do not continue P3.1+ or regenerate the full catalog.\n\n## Đọc theo task')
entry.write_text(s,encoding='utf-8')
for name in ('source-to-runtime.md','production-workflow.md','art-direction.md','approval-policy.md','godot-handoff.md'):
    p=skill/'references'/name;s=p.read_text(encoding='utf-8')
    notice='\n> P3.0 update (2026-09-12): [native-readability.md](native-readability.md) governs new world raster output. Final gameplay-size semantic evidence is required; old enlarged previews and trial authorization do not establish readiness. Current requested agent diagnosis is permitted; user approval remains separate.\n'
    first=s.find('\n');s=s[:first+1]+notice+s[first+1:]
    if name=='art-direction.md':
        s=s.replace('Player body cao khoảng 40–44px trên canvas 64×64','Player visible height lấy từ World Scale Policy của project tiêu thụ (P3.0 hiện tại 55px), canvas phải đủ gutter')
        s=s.replace('Body40–44px; đầu khoảng 1/4 tổng chiều cao; feet/contact ổn định','Body theo World Scale Policy; head/torso/limbs dùng chung family contract; feet/contact ổn định')
    p.write_text(s,encoding='utf-8')
p=skill/'references/style-lock.json';s=json.loads(p.read_text())
s['nativeReadability']={'version':1,'contract':'native-readability.md','requiredPreviewScales':[1,2],'minimumCriticalFeaturePixels':2,'minimumIdentityGapPixels':2,'physicalSizeAuthority':'consuming project world-scale-policy; preserve ratios','directionSemantics':{'s':'front','n':'true_back','w':'left','e':'right'},'integrationRequiresCurrentHashEvidence':True}
p.write_text(json.dumps(s,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
p=skill/'scripts/production.py';s=p.read_text(encoding='utf-8')
s=s.replace('from validate_assets import validate','from validate_assets import validate\nfrom readability_gate import issues as readability_issues')
s=s.replace("    return 'integration_ready' if integration_trial_authorized(root,r,state) else state", "    # P3.0: old snapshot remains historical, not a current readiness bypass.\n    if (state in ('integration_ready','release_ready') or integration_trial_authorized(root,r,state)) and readability_issues(root,r):\n        return 'needs_rework' if any(r.get('qa',{}).get('readability',{}).get(k)=='fail' for k in ('nativeSize','semanticSilhouette','directionSemantics')) else 'generated'\n    return 'integration_ready' if integration_trial_authorized(root,r,state) else state")
s=s.replace("        item.setdefault('qa',{})['evidencePaths']=evidence;item['qa']['technical']=True", "        item.setdefault('qa',{})['evidencePaths']=evidence;item['qa']['technical']=True\n        if item['qa'].get('readability'):\n            item['qa']['readability']['evidencePaths']=[safe(mp.parent,ep).relative_to(root).as_posix() for ep in item['qa']['readability'].get('evidencePaths',[])]")
p.write_text(s,encoding='utf-8')
# Default existing motion preview to native dimensions; keep zoom controls usable.
p=skill/'scripts/preview.template.html';s=p.read_text(encoding='utf-8')
s=s.replace('<option value="1">1× native</option>','<option value="1" selected>1× native</option>').replace('<option value="6" selected>6×</option>','<option value="6">6×</option>')
s=s.replace('.hero{width:384px;height:384px;', '.hero{width:64px;height:64px;').replace('.directions canvas{width:192px;height:192px;', '.directions canvas{width:64px;height:64px;').replace('.frames canvas{width:128px;height:128px;', '.frames canvas{width:64px;height:64px;').replace('.hero{width:320px;height:320px}', '.hero{width:64px;height:64px}')
p.write_text(s,encoding='utf-8')
print('P3.0 skill, source/runtime docs, style contract, native tools and production gate installed.')
