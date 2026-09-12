"""Check recorded readability evidence, never infer semantic quality from pixels."""
import hashlib
import json
from pathlib import Path

FIELDS=('nativeSize','semanticSilhouette','largeForms','featureThickness','pixelClusters','bodySeparation','directionSemantics','crossDirection','terrainIdentity','motion')

def issues(root, asset):
    if asset.get('artifactKind') or asset.get('role') not in ('actor','large_actor','huge_actor','prop','tile','pickup','actor_readability','architectural_prop'): return []
    q=asset.get('qa',{}); r=q.get('readability',{}); errors=[]
    if r.get('version')!=1 or r.get('assetSha256')!=asset.get('sha256'):
        return ['Missing/current-hash native readability record']
    if not r.get('reviewer'): errors.append('Readability reviewer missing')
    actor=asset.get('role') in ('actor','large_actor','huge_actor','actor_readability')
    required=set(FIELDS)-{'bodySeparation','directionSemantics','crossDirection','terrainIdentity','motion'}
    if actor: required.update(('bodySeparation','directionSemantics','crossDirection'))
    if asset.get('role')=='tile': required.add('terrainIdentity')
    if asset.get('frameCount',1)>1: required.add('motion')
    for key in FIELDS:
        v=r.get(key)
        if key in required and v!='pass': errors.append(key+' has not passed')
        elif key not in required and v not in ('pass','not_applicable'): errors.append(key+' needs pass or not_applicable')
        if v=='not_applicable' and not r.get('notApplicableReasons',{}).get(key): errors.append(key+' needs N/A reason')
    hashes=q.get('evidenceHashes',{})
    evidence=r.get('evidencePaths',[])
    if not evidence: errors.append('Native readability evidence missing')
    bundle_found=False
    for rel in evidence:
        p=(Path(root)/rel).resolve()
        if not p.is_relative_to(Path(root).resolve()) or not p.is_file(): errors.append('Missing/unsafe readability evidence');continue
        if hashes.get(rel)!=hashlib.sha256(p.read_bytes()).hexdigest():errors.append('Stale/unbound readability evidence')
        if p.suffix=='.json':
            try:
                report=json.loads(p.read_text(encoding='utf-8-sig'))
                if not any(f.get('assetId')==asset.get('assetId') and f.get('sha256')==asset.get('sha256') for f in report.get('frames',[])):continue
                required_previews={'native-1x.png','native-2x.png','grayscale-1x.png','silhouette-1x.png','dark-ground-1x.png','busy-ground-1x.png'}
                previews=report.get('previews',{})
                if not required_previews<=previews.keys():continue
                for name,sha in previews.items():
                    preview=(p.parent/name).resolve()
                    if not preview.is_relative_to(Path(root).resolve()) or not preview.is_file() or hashlib.sha256(preview.read_bytes()).hexdigest()!=sha:
                        errors.append('Missing/stale preview bundle image')
                bundle_found=True
            except (ValueError,TypeError,AttributeError):errors.append('Invalid preview evidence JSON')
    if not bundle_found:errors.append('Current asset requires native preview evidence bundle')
    if asset.get('frameCount',1)>1 and (q.get('frameIsolation') is not True or q.get('motion') is not True):
        errors.append('Animation isolation/motion QA pending')
    return errors
