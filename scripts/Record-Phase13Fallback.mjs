// Build a silent fallback video from the checked offline evidence pages.
// It is explicitly NOT a recording of authenticated application/VPS transactions.
import fs from 'node:fs/promises';
import path from 'node:path';
import {createRequire} from 'node:module';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
import assert from 'node:assert/strict';
const require=process.env.RUNTIME_NODE_MODULES?createRequire(path.join(process.env.RUNTIME_NODE_MODULES,'package.json')):createRequire(import.meta.url);
const {chromium}=require('playwright');
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const report=JSON.parse(await fs.readFile(path.join(root,'docs/release/offline-demo-check.json'),'utf8'));
const sha=body=>createHash('sha256').update(body).digest('hex');
assert.equal(report.passed,true);
assert.equal(sha(await fs.readFile(path.join(root,report.input))),report.sha256,'Run offline checks again after edits');
const frames=[];
for(let i=1;i<=7;i++){
 const name=`artifacts/phase13/verification/offline-browser/desktop-${i}.png`;
 const body=await fs.readFile(path.join(root,name));
 frames.push({path:name,sha256:sha(body),url:`data:image/png;base64,${body.toString('base64')}`});
}
const browser=await chromium.launch(process.env.PLAYWRIGHT_EXECUTABLE_PATH?{executablePath:process.env.PLAYWRIGHT_EXECUTABLE_PATH,headless:true}:{channel:'msedge',headless:true});
try{
 const context=await browser.newContext({offline:true});
 const page=await context.newPage();
 const result=await page.evaluate(async frames=>{
  const images=await Promise.all(frames.map(frame=>new Promise((resolve,reject)=>{const i=new Image();i.onload=()=>resolve(i);i.onerror=reject;i.src=frame.url;})));
  const canvas=document.createElement('canvas');canvas.width=1280;canvas.height=850;
  document.body.appendChild(canvas);const ctx=canvas.getContext('2d');let index=0;
  function draw(){ctx.fillStyle='#0f172a';ctx.fillRect(0,0,1280,850);ctx.fillStyle='#fbbf24';ctx.font='20px sans-serif';ctx.fillText('TixFlow · Walkthrough bằng chứng · Không phải video giao dịch ứng dụng/VPS',30,32);ctx.drawImage(images[index],0,50,1280,800);}
  draw();const stream=canvas.captureStream(15);
  const mimeType=['video/webm;codecs=vp9','video/webm;codecs=vp8','video/webm'].find(t=>MediaRecorder.isTypeSupported(t));
  if(!mimeType)throw new Error('No supported WebM encoder');
  const chunks=[],recorder=new MediaRecorder(stream,{mimeType,videoBitsPerSecond:1800000});
  const finished=new Promise((resolve,reject)=>{recorder.ondataavailable=e=>{if(e.data.size)chunks.push(e.data)};recorder.onerror=reject;recorder.onstop=resolve;});
  const interval=setInterval(draw,1000/15);const start=performance.now();recorder.start();
  for(index=0;index<images.length;index++){draw();await new Promise(resolve=>setTimeout(resolve,6000));}
  index=images.length-1;clearInterval(interval);recorder.stop();await finished;stream.getTracks().forEach(t=>t.stop());
  const blob=new Blob(chunks,{type:mimeType});const url=await new Promise(resolve=>{const r=new FileReader();r.onload=()=>resolve(r.result);r.readAsDataURL(blob);});
  return {url,mimeType,elapsedSeconds:(performance.now()-start)/1000};
 },frames);
 const video=Buffer.from(result.url.split(',')[1],'base64');
 const output='docs/release/TixFlow-evidence-fallback.webm';
 await fs.writeFile(path.join(root,output),video);
 // Decode a real frame to verify that the encoded file is playable.
 const playback=await page.evaluate(async url=>{
  const video=document.createElement('video');video.muted=true;video.src=url;
  await new Promise((resolve,reject)=>{video.onloadeddata=resolve;video.onerror=()=>reject(new Error('Video decode failed'))});
  await video.play();await new Promise(resolve=>setTimeout(resolve,500));video.pause();
  return {width:video.videoWidth,height:video.videoHeight,decoded:video.currentTime>0};
 },result.url);
 assert.equal(playback.decoded,true);assert.equal(playback.width,1280);assert.equal(playback.height,850);
 const metadata={createdUtc:new Date().toISOString(),output,sha256:sha(video),bytes:video.length,
  seconds:result.elapsedSeconds,mimeType:result.mimeType,playback,
  htmlSha256:report.sha256,frames:frames.map(({path,sha256})=>({path,sha256})),
  scope:'Silent 42-second evidence walkthrough from offline pages. Not authenticated application UI, not VPS acceptance, not the planned 8–12 minute live demo.'};
 await fs.writeFile(path.join(root,'docs/release/fallback-video.json'),JSON.stringify(metadata,null,2)+'\n');
 console.log(JSON.stringify({output,bytes:video.length,seconds:metadata.seconds,decoded:playback.decoded},null,2));
 await context.close();
}finally{await browser.close();}
