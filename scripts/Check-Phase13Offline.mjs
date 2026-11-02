// Exercise the self-contained evidence walkthrough. This does not test application UI.
// Requires Playwright and an installed Edge browser, or PLAYWRIGHT_EXECUTABLE_PATH.
import fs from 'node:fs/promises';
import path from 'node:path';
import {createRequire} from 'node:module';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';
import assert from 'node:assert/strict';
const require = process.env.RUNTIME_NODE_MODULES
 ? createRequire(path.join(process.env.RUNTIME_NODE_MODULES,'package.json')) : createRequire(import.meta.url);
const {chromium}=require('playwright');
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const input=path.join(root,'docs/release/offline-demo.html');
const screenshotDir=path.join(root,'artifacts/phase13/verification/offline-browser');
await fs.mkdir(screenshotDir,{recursive:true});
const browser=await chromium.launch(process.env.PLAYWRIGHT_EXECUTABLE_PATH
 ? {executablePath:process.env.PLAYWRIGHT_EXECUTABLE_PATH,headless:true} : {channel:'msedge',headless:true});
const errors=[],network=[];
try {
 const context=await browser.newContext({viewport:{width:1280,height:800},offline:true});
 const page=await context.newPage();
 page.on('pageerror',e=>errors.push(e.message));
 page.on('request',r=>{if(/^https?:/.test(r.url()))network.push(r.url())});
 await page.goto(pathToFileURL(input).href);
 const count=await page.locator('section').count();
 assert.equal(count,7);
 for(let i=0;i<count;i++){
  assert.equal(await page.locator('section.active').count(),1);
  assert.equal(await page.locator('#page').innerText(),`${i+1} / ${count}`);
  await page.screenshot({path:path.join(screenshotDir,`desktop-${i+1}.png`),fullPage:true});
  await page.keyboard.press('ArrowRight');
 }
 assert.equal(await page.locator('#page').innerText(),'7 / 7');
 await page.locator('#prev').click();
 assert.equal(await page.locator('#page').innerText(),'6 / 7');
 await page.locator('#next').click();
 assert.equal(await page.locator('#page').innerText(),'7 / 7');
 const imageState=await page.locator('img').evaluateAll(images=>images.map(i=>({complete:i.complete,width:i.naturalWidth})));
 assert(imageState.every(i=>i.complete&&i.width>0));
 await page.setViewportSize({width:390,height:844});
 for(let i=0;i<count;i++)await page.keyboard.press('ArrowLeft');
 assert.equal(await page.locator('#page').innerText(),'1 / 7');
 for(let i=0;i<count;i++){
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Horizontal overflow');
  await page.keyboard.press('ArrowRight');
 }
 await page.screenshot({path:path.join(screenshotDir,'mobile.png'),fullPage:true});
 assert.deepEqual(errors,[]);assert.deepEqual(network,[]);
 const report={checkedUtc:new Date().toISOString(),passed:true,browser:browser.version(),
  input:'docs/release/offline-demo.html',sha256:createHash('sha256').update(await fs.readFile(input)).digest('hex'),
  checks:['7 pages and boundaries','keyboard navigation','previous/next buttons','embedded images loaded',
   'no HTTP requests in offline context','no JavaScript errors','390px mobile width without horizontal overflow'],
  scope:'Evidence walkthrough only; not authenticated TixFlow UI or VPS acceptance.'};
 await fs.writeFile(path.join(root,'docs/release/offline-demo-check.json'),JSON.stringify(report,null,2)+'\n');
 console.log(JSON.stringify(report,null,2));
 await context.close();
}finally{await browser.close();}
