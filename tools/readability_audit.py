"""Offline PNG evidence, never an engine/gameplay test. Pillow only; no asset edits."""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageOps

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'docs/V2.5/readability/before'
OUT.mkdir(parents=True, exist_ok=True)
catalog = json.loads((ROOT/'data/v2.5/asset-catalog.v2.5.json').read_text())['assets']
metrics = {a['assetId']: a for a in json.loads((ROOT/'data/v2.5/presentation-visual-metrics.v2.5.json').read_text())['assets']}
policy = json.loads((ROOT/'data/v2.5/world-scale-policy.v2.5.json').read_text())
ids = [f'player.base.{clip}.{d}' for clip in ('idle','move') for d in 'snwe']
ids += ['soul.skeleton.rank01.enemy.south','soul.skeleton.rank01.enemy.idle.s','soul.skeleton.rank01.ally.south','soul.goblin.rank01.enemy.idle.s']
ids += ['world.ash_graves.'+x for x in ('pillar','chest','shrine','portal','ground.mask15','path.mask15','ruin.mask15')]
assets = [next(a for a in catalog if a['assetId']==i) for i in ids if any(a['assetId']==i for a in catalog)]
sheet = Image.new('RGBA',(760, len(assets)*152),(39,43,54,255))
draw = ImageDraw.Draw(sheet)
rows=[]
for row,a in enumerate(assets):
    aid=a['assetId']; x,y,w,h=a['frames'][0]['rect']
    im=Image.open(ROOT/a['relativeFile']).convert('RGBA').crop((x,y,x+w,y+h))
    boxes=[]
    full=Image.open(ROOT/a['relativeFile']).convert('RGBA')
    for f in a['frames']:
        fx,fy,fw,fh=f['rect']; boxes.append(full.crop((fx,fy,fx+fw,fy+fh)).getchannel('A').getbbox())
    metric=metrics.get(aid); scale=1.; target=None
    if metric:
        obj=next((o for o in policy['worldObjects'] if o['assetId']==aid),None)
        ratio=obj['visualHeightRatio'] if obj else 1. if aid.startswith(('player.','soul.skeleton.')) else None
        if ratio is not None: target=55*ratio; scale=target/metric['opaqueBounds'][3]
    elif aid.startswith('player.'): scale=1.35
    kind='integer' if scale==round(scale) else 'half-step' if scale*2==round(scale*2) else 'arbitrary fractional'
    # Nearest raster approximation. This is explicitly not a Godot capture.
    raster=im.resize((round(w*scale),round(h*scale)),Image.Resampling.NEAREST)
    py=row*152
    draw.text((4,py+2),aid,fill='white')
    sheet.alpha_composite(im,(4,py+20))
    sheet.alpha_composite(im.resize((w*2,h*2),Image.Resampling.NEAREST),(80,py+20))
    gray=ImageOps.grayscale(im).convert('RGBA');gray.putalpha(im.getchannel('A'))
    sheet.alpha_composite(gray,(220,py+20))
    sil=Image.new('RGBA',im.size,'#ffffff');sil.putalpha(im.getchannel('A'))
    sheet.alpha_composite(sil,(300,py+20))
    # Tall props placed in a separate sheet below to avoid clipping the audit rows.
    if raster.height<=128: sheet.alpha_composite(raster,(380,py+20))
    draw.text((520,py+25),f'scale {scale:.6f}\n{kind}\nbbox {im.getchannel("A").getbbox()}\n1x / 2x / gray / silhouette',fill='white')
    rows.append(dict(assetId=aid,frameSize=[w,h],firstFrameBounds=im.getchannel('A').getbbox(),allFrameBounds=boxes,metricOpaqueBounds=metric['opaqueBounds'] if metric else None,targetVisibleHeight=target,scale=scale,scaleClass=kind,idealVisibleHeight=(boxes[0][3]-boxes[0][1])*scale,scope='offline nearest approximation; engine measurement pending'))
    im.save(OUT/(aid+'.1x.png'))
sheet.save(OUT/'native-contact-sheet.png')
(OUT/'measurements.json').write_text(json.dumps(rows,indent=2)+'\n')
print(json.dumps(rows,indent=2))
