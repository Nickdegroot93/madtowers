"""Generate background-led chapter vignettes with the authenticated Higgsfield CLI.
Usage: python3 Tools/ChapterArt/generate.py submit Jungle Japan Neon (at most 8 pending jobs)
       python3 Tools/ChapterArt/generate.py fetch
Existing job IDs are reused; completed jobs are never regenerated automatically.
"""
import concurrent.futures,json,pathlib,subprocess,sys,urllib.request
ROOT=pathlib.Path(__file__).resolve().parent
RAW=ROOT/'raw/background-led';RAW.mkdir(parents=True,exist_ok=True)
CHAPTERS=json.load(open(ROOT/'chapters.json'))

def run(args):return json.loads(subprocess.check_output(['higgsfield',*args,'--json'],text=True))
def submit(key):
 p=RAW/f'{key}_job.json'
 if p.exists():return
 c=next(c for c in CHAPTERS if c['skin'].split('/')[-1]==key.removeprefix('ornament_'))
 prompt=PROMPTS[key]
 job=run(['generate','create','gpt_image_2_5','--resolution','1k','--quality','high','--background','opaque','--aspect-ratio','1:1','--variant','flare','--image-references',c['background'],'--prompt',prompt])
 p.write_text(json.dumps(job));print(key,job,flush=True)
def fetch(key):
 p=RAW/f'{key}_job.json'
 if not p.exists():return
 job=run(['generate','get',json.load(open(p))[0]])
 (RAW/f'{key}.json').write_text(json.dumps(job,indent=2))
 if job['status']=='completed' and not (RAW/f'{key}.png').exists():urllib.request.urlretrieve(job['result_url'],RAW/f'{key}.png')
 print(key,job['status'],flush=True)
PROMPTS = json.load(open(ROOT/'prompts.json'))
if __name__=='__main__':
 if len(sys.argv)<2 or sys.argv[1] not in ('submit','fetch'):
  raise SystemExit('Usage: generate.py submit KEY... | fetch [KEY...]')
 if sys.argv[1]=='submit' and (len(sys.argv)<3 or len(sys.argv[2:])>8):
  raise SystemExit('Name one to eight assets explicitly; each new job spends credits.')
 keys=sys.argv[2:] or list(PROMPTS)
 with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:list(pool.map(submit if sys.argv[1]=='submit' else fetch,keys))
