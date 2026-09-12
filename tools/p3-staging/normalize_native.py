"""Explicit, uniform source-to-runtime normalization. No inferred crops or artwork."""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image

def digest(p): return hashlib.sha256(p.read_bytes()).hexdigest()

def normalize(recipe_path):
    recipe_path=recipe_path.resolve(); root=recipe_path.parent
    r=json.loads(recipe_path.read_text(encoding='utf-8-sig'))
    def path(name):
        p=(root/name).resolve()
        if not p.is_relative_to(root): raise ValueError('Recipe paths must stay under recipe directory')
        return p
    source=path(r['source'])
    if digest(source)!=r['sourceSha256']: raise ValueError('Source hash mismatch')
    im=Image.open(source).convert('RGBA')
    if r.get('mask'):
        mask=path(r['mask'])
        if digest(mask)!=r['maskSha256']: raise ValueError('Mask hash mismatch')
        alpha=Image.open(mask).convert('L')
        if alpha.size!=im.size: raise ValueError('Mask size mismatch')
        im.putalpha(alpha)
    elif not r.get('opaqueTile') and im.getchannel('A').getextrema()[0]==255:
        raise ValueError('Opaque source needs a reviewed mask; no guessed background extraction')
    # Body profiles use binary alpha; remove low-alpha fringe before checking bounds.
    im.putalpha(im.getchannel('A').point(lambda a:255 if a>=128 else 0))
    crop=r['crop']; x,y,w,h=crop
    if min(x,y)<0 or min(w,h)<=0 or x+w>im.width or y+h>im.height: raise ValueError('Invalid reviewed crop')
    if not r.get('familyTransformId'): raise ValueError('Shared family transform ID required')
    scale=r['uniformScale']
    if not isinstance(scale,(float,int)) or not 0<scale<=16: raise ValueError('Invalid uniform scale')
    cropped=im.crop((x,y,x+w,y+h))
    scaled=cropped.resize((max(1,round(w*scale)),max(1,round(h*scale))),Image.Resampling.NEAREST)
    px,py=r['pivot']; fx,fy=r['sourceFoot']
    tx,ty=round(px-(fx-x)*scale),round(py-(fy-y)*scale)
    cw,ch=r['canvas']; b=scaled.getchannel('A').getbbox()
    gutter=0 if r.get('opaqueTile') else 2
    if not b or b[0]+tx<gutter or b[1]+ty<gutter or b[2]+tx>cw-gutter or b[3]+ty>ch-gutter:
        raise ValueError('Transformed artwork would clip or violate gutter; revise source/contract')
    out=Image.new('RGBA',(cw,ch));out.alpha_composite(scaled,(tx,ty))
    destination=path(r['output']);destination.parent.mkdir(parents=True,exist_ok=True)
    if destination.exists(): raise ValueError('Version outputs; refusing overwrite')
    out.save(destination.with_name(destination.stem+'.pre-palette.png'))
    palette=[tuple(bytes.fromhex(c.lstrip('#'))) for c in r['palette']]
    if not palette: raise ValueError('Explicit family palette required')
    data=[]
    for p in out.getdata():
        if p[3]<128: data.append((0,0,0,0));continue
        c=min(palette,key=lambda c:sum((c[i]-p[i])**2 for i in range(3)))
        data.append((*c,255))
    out.putdata(data);out.save(destination)
    report=dict(recipe=r,sourceSize=list(im.size),placement=[tx,ty],actualResize=list(scaled.size),
                opaqueBounds=out.getchannel('A').getbbox(),outputSha256=digest(destination),
                userReview='pending',note='Normalization facts only; inspect pre-palette and final native PNG')
    destination.with_suffix('.recipe.json').write_text(json.dumps(report,indent=2)+'\n')
    return report

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('recipe',type=Path)
    args=p.parse_args();print(json.dumps(normalize(args.recipe),indent=2))
