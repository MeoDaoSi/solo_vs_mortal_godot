"""P3.0 five-sample recipe/manifest assembly from reviewed source bounds."""
import hashlib
import json
import shutil
from pathlib import Path
from normalize_native import normalize

root=Path(__file__).resolve().parents[2]
batch=root/'docs/V2.5/readability/batch'
style=json.loads(Path('C:/ws/asset-production-system/solo-vs-mortal-art/references/style-lock.json').read_text())
specs={
 'skeleton':([156,188,908,955],[610,1143],55,[64,64],[32,60],['ash','bone','cloth']),
 'pillar':([138,121,681,1435],[478,1556],96,[64,112],[32,106],['ash','stone']),
 'chest':([196,238,884,850],[638,1088],30,[64,64],[32,56],['ash','stone','bone']),
 'terrain':([0,0,1254,1254],[0,0],32,[32,32],[0,0],['ash','stone','vegetation'])}
for name,(crop,foot,height,canvas,pivot,ramps) in specs.items():
    recipe=dict(source=f'raw/{name}.png',sourceSha256=hashlib.sha256((batch/f'raw/{name}.png').read_bytes()).hexdigest(),crop=crop,sourceFoot=foot,uniformScale=height/crop[3],familyTransformId=f'p3-{name}-v1-single-source',canvas=canvas,pivot=pivot,palette=[c for ramp in ramps for c in style['palette'][ramp]],output=f'runtime/{name}.png',opaqueTile=name=='terrain')
    path=batch/f'{name}.recipe.json';path.write_text(json.dumps(recipe,indent=2)+'\n');normalize(path)

assets=[]; samples=[]
generation=json.loads((batch/'generation.json').read_text())
for name,file,role,canvas,pivot,aid in [
 ('player','player-v2.png','actor_readability',[64,64],[32,60],'player.readability.sample.s'),
 ('skeleton','skeleton.png','actor_readability',[64,64],[32,60],'soul.skeleton.readability.sample.s'),
 ('pillar','pillar.png','architectural_prop',[64,112],[32,106],'world.ash_graves.readability.pillar'),
 ('chest','chest.png','prop',[64,64],[32,56],'world.ash_graves.readability.chest'),
 ('terrain','terrain.png','tile',[32,32],[0,0],'world.ash_graves.readability.ground')]:
    sha=hashlib.sha256((batch/'runtime'/file).read_bytes()).hexdigest()
    prompt=(batch/'player-prompt.txt').read_text() if name=='player' else generation[name]['prompt']
    assets.append(dict(assetId=aid,file='runtime/'+file,sha256=sha,role=role,representation='environment' if name in ('pillar','chest','terrain') else 'ui',direction='s' if name in ('player','skeleton') else 'none',clip='static',frameSize=canvas,frameCount=1,pivot=pivot,frameDurationMs=[1000],status='generated',source=dict(tool='built-in image_gen',model=None,localJobId='p3-readability-'+name,referenceHashes=['59bc6b957d801804510db87c3a71cce3bfb63efd62c412091a6b9cb2045b7aa6'],prompt=prompt),qa=dict(technical=False,visual=False,motion=False,inEngine=False,userReview='pending')))
    samples.append(dict(file=file,pivot=pivot,label=name,sha256=sha))
manifest=dict(schemaVersion=1,styleId=style['styleId'],styleVersion=style['styleVersion'],purpose='P3.0 static calibration only; not canonical catalog replacements or animation approval',assets=assets)
(batch/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
package=root/'assets/v2.5/readability-samples-v001';package.mkdir(parents=True,exist_ok=True)
for sample in samples:shutil.copy2(batch/'runtime'/sample['file'],package/sample['file'])
(package/'samples.json').write_text(json.dumps(dict(samples=samples,scope=manifest['purpose']),indent=2)+'\n')
print('Five static native raster samples prepared; all user review pending.')
