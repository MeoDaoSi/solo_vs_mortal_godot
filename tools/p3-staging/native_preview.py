"""Native raster review evidence from a PNG production manifest. No quality approval."""
import argparse
import hashlib
import html
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageOps

def build(manifest, output):
    manifest=manifest.resolve(); output.mkdir(parents=True,exist_ok=True)
    assets=json.loads(manifest.read_text(encoding='utf-8-sig'))['assets']
    assets=sorted(assets,key=lambda a:(a['assetId'].rsplit('.',1)[0], 'snwe'.find(a.get('direction',''))))
    frames=[]; facts=[]
    for a in assets:
        path=(manifest.parent/a['file']).resolve()
        if not path.is_relative_to(manifest.parent): raise ValueError('Asset outside manifest directory')
        sha=hashlib.sha256(path.read_bytes()).hexdigest()
        if sha!=a['sha256']: raise ValueError('Asset hash mismatch')
        im=Image.open(path).convert('RGBA'); w,h=a['frameSize']
        if im.size!=(w*a['frameCount'],h): raise ValueError('Invalid strip geometry')
        group=[]
        for i in range(a['frameCount']):
            frame=im.crop((i*w,0,(i+1)*w,h)); group.append(frame)
            facts.append(dict(assetId=a['assetId'],sha256=sha,frame=i,canvas=[w,h],bounds=frame.getchannel('A').getbbox(),pivot=a['pivot']))
        frames.append((a,group))
    # One asset per row, every frame at native dimensions. No fit-to-card scaling.
    width=max(400,max(sum(f.width+8 for f in fs) for _,fs in frames))
    height=sum(max(f.height for f in fs)+30 for _,fs in frames)
    names=[]
    for mode in ('native','grayscale','silhouette','dark-ground','busy-ground'):
        sheet=Image.new('RGBA',(width,height),'#272b36');d=ImageDraw.Draw(sheet);y=0
        for a,fs in frames:
            d.text((2,y+2),a['assetId'],fill='white');x=0
            for f in fs:
                bg=Image.new('RGBA',f.size,'#161820' if mode=='dark-ground' else '#272b36')
                if mode=='busy-ground':
                    bd=ImageDraw.Draw(bg)
                    for by in range(0,f.height,4):
                        for bx in range(0,f.width,4):
                            if (bx//4+by//4)%3==0: bd.rectangle((bx,by,bx+2,by+2),fill='#54564f')
                if mode=='grayscale':
                    src=ImageOps.grayscale(f).convert('RGBA');src.putalpha(f.getchannel('A'))
                elif mode=='silhouette':
                    src=Image.new('RGBA',f.size,'white');src.putalpha(f.getchannel('A'))
                else:src=f
                bg.alpha_composite(src);sheet.alpha_composite(bg,(x,y+20));x+=f.width+8
            y+=max(f.height for f in fs)+30
        filename=mode+'-1x.png';sheet.save(output/filename);names.append(filename)
        if mode=='native':
            sheet.resize((width*2,height*2),Image.Resampling.NEAREST).save(output/'native-2x.png');names.append('native-2x.png')
    # Props beside the first Player at the exact raster sizes in the manifest.
    player=next((fs[0] for a,fs in frames if a['assetId'].startswith('player.')),None)
    props=[(a,fs[0]) for a,fs in frames if a['role'] in ('prop','architectural_prop')]
    if player is not None and props:
        sh=Image.new('RGBA',(sum(f.width+player.width+20 for _,f in props),max(max(f.height,player.height) for _,f in props)+26),'#272b36');d=ImageDraw.Draw(sh);x=0
        for a,f in props:
            d.text((x,2),a['assetId'].split('.')[-1],fill='white')
            sh.alpha_composite(player,(x,sh.height-player.height));sh.alpha_composite(f,(x+player.width+8,sh.height-f.height));x+=f.width+player.width+20
        sh.save(output/'props-player-1x.png');names.append('props-player-1x.png')
    tiles=[(a,fs[0]) for a,fs in frames if a['role']=='tile']
    if tiles:
        sh=Image.new('RGBA',(len(tiles)*104,124),'#272b36');d=ImageDraw.Draw(sh)
        for i,(a,f) in enumerate(tiles):
            d.text((i*104,2),a['assetId'].split('.')[-2],fill='white')
            for y in range(3):
                for x in range(3):sh.alpha_composite(f,(i*104+x*32,24+y*32))
        sh.save(output/'terrain-repeat-1x.png');names.append('terrain-repeat-1x.png')
        # Comparison, not a certified Wang transition assembly.
        sh=Image.new('RGBA',(288,160),'#272b36')
        for y in range(5):
            for x in range(9):
                f=tiles[min(len(tiles)-1,x//3)][1];sh.alpha_composite(f,(x*32,y*32))
        sh.save(output/'terrain-family-patch-1x.png');names.append('terrain-family-patch-1x.png')
    evidence=dict(schemaVersion=1,manifestSha256=hashlib.sha256(manifest.read_bytes()).hexdigest(),frames=facts,previews={n:hashlib.sha256((output/n).read_bytes()).hexdigest() for n in names},userReview='pending',scope='Offline PNG review. Busy ground is a diagnostic pattern; terrain patch is family comparison, not transition certification.')
    (output/'evidence.json').write_text(json.dumps(evidence,indent=2)+'\n')
    (output/'index.html').write_text('<!doctype html><meta charset="utf-8"><title>Native readability review</title><style>body{background:#161820;color:white;font:14px sans-serif}img{image-rendering:pixelated;max-width:none}section{overflow:auto}p{max-width:800px}</style><h1>Native readability — user review pending</h1><p>Use browser zoom 100%. Images retain native dimensions; scroll instead of fit-to-window. These are offline evidence, not Arena captures or approvals.</p>'+''.join('<h2>'+html.escape(n)+'</h2><section><img src="'+n+'"></section>' for n in names),encoding='utf-8')
    return dict(assets=len(assets),frames=len(facts),output=str(output),userReview='pending')

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('manifest',type=Path);p.add_argument('output',type=Path)
    a=p.parse_args();print(json.dumps(build(a.manifest,a.output)))
