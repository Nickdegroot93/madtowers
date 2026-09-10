"""Deterministic chroma-key extraction, alpha-aware resize and lossless PNG optimization."""
import json,pathlib,tempfile,os
import numpy as np
from PIL import Image,ImageDraw,ImageFilter
ROOT=pathlib.Path(__file__).resolve().parent
DEST=ROOT.parents[1]/'Assets/Resources/ChapterArt'

def extract(source):
 rgb=np.asarray(Image.open(source).convert('RGB'),dtype=np.float32)
 r,g,b=rgb[:,:,0],rgb[:,:,1],rgb[:,:,2]
 corners=np.concatenate([rgb[:20,:20].reshape(-1,3),rgb[-20:,-20:].reshape(-1,3)])
 if np.mean((corners[:,1]>210)&(corners[:,0]<45)&(corners[:,2]<45))<.95:
  raise ValueError(f'{source}: background is not clean chroma green; inspect and regenerate')
 # Only saturated green is the screen. Muted olive in the painted subjects is retained.
 excess=g-np.maximum(r,b)
 matte=1-np.clip((excess-45)/120,0,1)*np.clip((g-110)/70,0,1)
 # Remove chroma spill in the antialiased silhouette; fully opaque paint is untouched.
 edge=(matte>0)&(matte<1)
 rgb[:,:,1]=np.where(edge,np.minimum(g,np.maximum(r,b)+18),g)
 # Despill the four-pixel outer edge, including dark green antialias pixels.
 # Interior chapter foliage remains untouched.
 screen=Image.fromarray(((matte==0)*255).astype('uint8'))
 border=np.asarray(screen.filter(ImageFilter.MaxFilter(9)))>0
 rgb[:,:,1]=np.where(border,np.minimum(rgb[:,:,1],np.maximum(r,b)),rgb[:,:,1])
 out=np.dstack((rgb,matte*255)).astype('uint8')
 out[matte==0,:3]=0
 img=Image.fromarray(out)
 box=img.getbbox()
 if not box: raise ValueError('Empty cutout')
 img=img.crop(box); img.thumbnail((464,464),Image.Resampling.LANCZOS)
 final=Image.new('RGBA',(512,512));final.paste(img,((512-img.width)//2,(512-img.height)//2))
 return final

def corner_placement(sprite):
 # Half the painted alpha belongs inside the modal corner (mirrored on the right).
 alpha=np.asarray(sprite)[:,:,3].astype(float)
 n=alpha.shape[0]; lo=0.; hi=.5
 for _ in range(18):
  inset=(lo+hi)/2
  inside=alpha[:round(n*(.5+inset)),round(n*(.5-inset)):].sum()/alpha.sum()
  if inside < .5: lo=inset
  else: hi=inset
 return round((lo+hi)/2,5)


def process():
 sources=sorted((ROOT/'raw/background-led').glob('*.png'))
 if not sources:
  raise ValueError('No raw PNGs found; existing sprites and metadata were left unchanged')
 manifest=ROOT/'manifest.json'
 layout=DEST/'CornerLayout.json'
 records={r['asset']:r for r in json.loads(manifest.read_text())} if manifest.exists() else {}
 placements={r['key']:r for r in json.loads(layout.read_text())['entries']} if layout.exists() else {}
 # Prepare the entire batch and contact sheet before replacing any shipping output.
 # Partial source folders update only those assets; existing metadata is retained.
 with tempfile.TemporaryDirectory(prefix='.chapter-art-',dir=ROOT) as staging:
  stage=pathlib.Path(staging)
  outputs={}
  for source in sources:
   job=json.loads(source.with_suffix('.json').read_text())
   job_id,prompt=job['id'],job['params']['prompt']
   sprite=extract(source)
   dest=DEST/source.name
   pending=stage/source.name
   sprite.save(pending,optimize=True)
   asset=dest.relative_to(ROOT.parents[1]).as_posix()
   records[asset]=dict(asset=asset,job_id=job_id,model='gpt_image_2_5',resolution='1k',quality='high',prompt=prompt,bytes=pending.stat().st_size)
   outputs[dest]=pending
   if source.stem.startswith('ornament_'):
    placements[source.stem]=dict(key=source.stem,inset=corner_placement(sprite))
  ordered=[records[key] for key in sorted(records)]
  pending_layout=stage/'CornerLayout.json'
  pending_layout.write_text(json.dumps(dict(entries=[placements[key] for key in sorted(placements)]),indent=2))
  pending_manifest=stage/'manifest.json'
  pending_manifest.write_text(json.dumps(ordered,indent=2))
  cols=5;rows=(len(ordered)+cols-1)//cols
  contact=Image.new('RGB',(cols*210,rows*234),(29,33,40));draw=ImageDraw.Draw(contact)
  for i,record in enumerate(ordered):
   dest=ROOT.parents[1]/record['asset']
   with Image.open(outputs.get(dest,dest)) as original:
    im=original.convert('RGBA')
   im.thumbnail((200,200),Image.Resampling.LANCZOS)
   x=(i%cols)*210;y=(i//cols)*234
   contact.paste(im,(x,y),im);draw.text((x+8,y+205),pathlib.Path(record['asset']).stem,fill='white')
  pending_contact=stage/'contact.png'
  contact.save(pending_contact)
  outputs[layout]=pending_layout
  outputs[manifest]=pending_manifest
  outputs[ROOT/'review/contact.png']=pending_contact
  for dest,pending in outputs.items():
   dest.parent.mkdir(parents=True,exist_ok=True)
   os.replace(pending,dest)
 print(len(sources),'assets updated,',len(ordered),'assets total,',sum(r['bytes'] for r in ordered),'bytes')


if __name__=='__main__':
 process()
